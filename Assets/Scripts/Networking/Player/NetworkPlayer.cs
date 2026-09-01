using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Fit.Networking.Player
{
    /// <summary>
    /// 玩家根对象。只负责"谁是谁"，具体能力拆到兄弟组件：
    ///   PlayerVitals（生命/状态）、PlayerInventory（物品）、PlayerMovement（移动）等。
    ///
    /// 这个拆分方式直接来自 How to Fish 的 55 个 NetworkBehaviour 清单。
    /// </summary>
    public sealed class NetworkPlayer : NetworkEntity
    {
        /// <summary>同步显示名。OnChange 回调让 UI 无需轮询。</summary>
        [SyncVar(OnChange = nameof(OnDisplayNameChanged))]
        public string DisplayName = string.Empty;

        /// <summary>同步外观（对应 How to Fish 里的 PlayerSkin）。</summary>
        [SyncVar(OnChange = nameof(OnSkinIdChanged))]
        public int SkinId;

        /// <summary>房主用于标记该玩家是否已完成新手引导。</summary>
        [SyncVar]
        public bool HasFinishedTutorial;

        public static event Action<NetworkPlayer, string> AnyDisplayNameChanged;
        public static event Action<NetworkPlayer, int> AnySkinChanged;

        public bool IsReady { get; private set; }

        public override void OnStartClient()
        {
            base.OnStartClient();

            if (base.IsOwner)
            {
                // 本地玩家把名字推给服务器，由服务器写入 SyncVar 广播
                SubmitDisplayName(SteamSession.IsSteamAvailable
                    ? LocalSteamName()
                    : $"Player_{UnityEngine.Random.Range(1000, 9999)}");
            }

            gameObject.tag = base.IsOwner ? "LocalPlayer" : "RemotePlayer";
        }

        [ServerRpc]
        private void SubmitDisplayName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                name = "Unnamed";

            DisplayName = name.Length > 24 ? name.Substring(0, 24) : name; // 防止超长名字撑爆 UI
        }

        [ServerRpc]
        public void SetReady(bool ready)
        {
            IsReady = ready;
            Debug.Log($"[Room] {DisplayName} ready={ready}");
        }

        [ServerRpc]
        public void RequestSkin(int skinId) => SkinId = skinId;

        private void OnDisplayNameChanged(string prev, string next, bool asServer)
        {
            AnyDisplayNameChanged?.Invoke(this, next);
        }

        private void OnSkinIdChanged(int prev, int next, bool asServer)
        {
            AnySkinChanged?.Invoke(this, next);
        }

        private static string LocalSteamName()
        {
#if !DISABLESTEAMWORKS
            try
            {
                if (Steamworks.SteamClient.IsValid)
                    return Steamworks.SteamClient.Name;
            }
            catch
            {
                // 忽略，走随机名兜底
            }
#endif
            return $"Player_{UnityEngine.Random.Range(1000, 9999)}";
        }
    }
}
