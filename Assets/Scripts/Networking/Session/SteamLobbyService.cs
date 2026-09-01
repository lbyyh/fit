using System;
using FishNet;
using FishNet.Transporting;
using UnityEngine;

#if !DISABLESTEAMWORKS
using Steamworks;
using Steamworks.Data;
#endif

namespace Fit.Networking.Session
{
    /// <summary>
    /// Steam 大厅封装。
    ///
    /// 沿用 How to Fish 的做法：不做专用服务器与匹配池，用 Steam 大厅 + 好友邀请。
    /// 好处是零后端成本、Steam 生态内体验完整（Steam 好友列表直接加入、邀请通知、富文本状态）；
    /// 代价是跨平台联机能力受限，这也是为什么传输层要保留 UTP 作为备选。
    ///
    /// 关键实现点：大厅创建完成后必须调用 SetLobbyData 写入连接标识，
    /// 客户端通过 GetLobbyData 读出来再建立连接 —— Steam 大厅本身不转发任何游戏数据。
    /// </summary>
    public sealed class SteamLobbyService : MonoBehaviour
    {
        public const string KeyHostAddress = "fit_host";
        public const string KeyBuildId = "fit_build";

        [SerializeField] private int _maxMembers = 8;

        public event Action<string> OnLobbyCreated;
        public event Action<string> OnLobbyJoined;
        public event Action<string> OnLobbyFailed;

        public string CurrentLobbyId { get; private set; } = string.Empty;
        public bool InLobby => !string.IsNullOrEmpty(CurrentLobbyId);

#if !DISABLESTEAMWORKS
        private Lobby _currentLobby;
        private Callback<LobbyCreated_t> _lobbyCreated;
        private Callback<LobbyEnter_t> _lobbyEntered;
        private Callback<LobbyChatUpdate_t> _lobbyChatUpdate;
        private Callback<GameLobbyJoinRequested_t> _lobbyJoinRequested;
#endif

        private void OnEnable()
        {
#if !DISABLESTEAMWORKS
            if (!SteamSession.IsSteamAvailable)
            {
                Debug.LogWarning("[SteamLobby] Steam 不可用，大厅功能已禁用。");
                return;
            }

            _lobbyCreated = Callback<LobbyCreated_t>.Create(HandleLobbyCreated);
            _lobbyEntered = Callback<LobbyEnter_t>.Create(HandleLobbyEntered);
            _lobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(HandleChatUpdate);
            _lobbyJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(HandleJoinRequested);
#endif
        }

        public void CreateLobby(bool friendsOnly = false)
        {
#if !DISABLESTEAMWORKS
            if (!SteamSession.IsSteamAvailable)
            {
                OnLobbyFailed?.Invoke("Steam 不可用");
                return;
            }

            var type = friendsOnly
                ? LobbyType.FriendsOnly
                : LobbyType.Public;

            SteamMatchmaking.CreateLobbyAsync(_maxMembers, type);
#else
            OnLobbyFailed?.Invoke("构建中已禁用 Steamworks");
#endif
        }

        public void JoinLobby(string lobbyId)
        {
#if !DISABLESTEAMWORKS
            if (!ulong.TryParse(lobbyId, out var id))
            {
                OnLobbyFailed?.Invoke($"无效的大厅 ID：{lobbyId}");
                return;
            }
            SteamMatchmaking.JoinLobbyAsync(new SteamId { Value = id });
#endif
        }

        public void LeaveLobby()
        {
#if !DISABLESTEAMWORKS
            if (InLobby)
            {
                _currentLobby.Leave();
                CurrentLobbyId = string.Empty;
            }
#endif
        }

        /// <summary>
        /// 房主把自己的连接地址写进大厅元数据。
        /// Steam P2P 场景下通常写 SteamID（FishySteamworks 用它建立 P2P 连接）；
        /// UTP 降级场景下写 IP:Port。
        /// </summary>
        public void PublishHostAddress(string address, string buildId)
        {
#if !DISABLESTEAMWORKS
            if (!InLobby) return;
            _currentLobby.SetData(KeyHostAddress, address);
            _currentLobby.SetData(KeyBuildId, buildId);
#endif
        }

        public string GetHostAddress()
        {
#if !DISABLESTEAMWORKS
            return InLobby ? _currentLobby.GetData(KeyHostAddress) : string.Empty;
#else
            return string.Empty;
#endif
        }

        /// <summary>检查大厅内玩家版本是否一致，不一致直接拒绝，避免莫名其妙的同步错位。</summary>
        public bool IsBuildCompatible(string localBuildId)
        {
#if !DISABLESTEAMWORKS
            var remote = InLobby ? _currentLobby.GetData(KeyBuildId) : localBuildId;
            return string.IsNullOrEmpty(remote) || remote == localBuildId;
#else
            return true;
#endif
        }

#if !DISABLESTEAMWORKS
        private void HandleLobbyCreated(LobbyCreated_t result)
        {
            if (result.Result != Result.OK)
            {
                OnLobbyFailed?.Invoke($"创建大厅失败：{result.Result}");
                return;
            }

            _currentLobby = new Lobby { Id = result.ID };
            CurrentLobbyId = result.ID.ToString();
            OnLobbyCreated?.Invoke(CurrentLobbyId);
        }

        private void HandleLobbyEntered(LobbyEnter_t result)
        {
            _currentLobby = new Lobby { Id = result.LobbyID };
            CurrentLobbyId = result.LobbyID.ToString();
            OnLobbyJoined?.Invoke(CurrentLobbyId);
        }

        private void HandleChatUpdate(LobbyChatUpdate_t result)
        {
            // 房主离开时 result.StateChange 会带上 Removed 标记。
            // 客户端在这里触发"房主已断开"提示并自动退出。
            Debug.Log($"[SteamLobby] 成员变动：{result.StateChange}");
        }

        private void HandleJoinRequested(GameLobbyJoinRequested_t result)
        {
            // 玩家从 Steam 好友列表点"加入游戏"时走这里
            JoinLobby(result.Lobby.Id.ToString());
        }
#endif
    }
}
