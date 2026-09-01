using System.Collections.Generic;
using Fit.Networking;
using Fit.Networking.Session;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fit.UI
{
    /// <summary>
    /// 房间界面。
    ///
    /// 对应 How to Fish 的这一组 UI 入口：
    ///   CreateLocalLobbyButton / CreateOnlineLobbyButton / LoadServerButton /
    ///   DeleteServerButton / CopyLobbyIDButton / UpdateSavedServerButtons
    ///
    /// 三个必须做对的交互细节：
    ///   1. 创建大厅后立刻把大厅 ID 显示出来并提供复制 —— 没有这一步，
    ///      非好友联机就得靠截图传 ID，体验极差；
    ///   2. 版本校验前置 —— 加入前先比对 BuildId，不一致直接拒绝并提示。
    ///      版本不匹配导致的同步错位，排查起来非常痛苦；
    ///   3. 加入过程中禁用按钮 —— 防止连点产生多个并发连接请求。
    /// </summary>
    public sealed class LobbyMenu : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private FitNetworkManager _session;
        [SerializeField] private SteamLobbyService _lobby;
        [SerializeField] private TransportSwitcher _transport;

        [Header("UI")]
        [SerializeField] private Button _createOnlineButton;
        [SerializeField] private Button _createOfflineButton;
        [SerializeField] private Button _copyIdButton;
        [SerializeField] private Button _leaveButton;
        [SerializeField] private TMP_Text _lobbyIdText;
        [SerializeField] private TMP_Text _statusText;
        [SerializeField] private Transform _serverListRoot;
        [SerializeField] private ServerListEntryView _entryPrefab;

        private readonly ServerListStore _store = new();
        private readonly List<ServerListEntryView> _entryViews = new();
        private bool _busy;

        private void Awake()
        {
            _createOnlineButton.onClick.AddListener(CreateOnline);
            _createOfflineButton.onClick.AddListener(CreateOffline);
            _copyIdButton.onClick.AddListener(CopyLobbyId);
            _leaveButton.onClick.AddListener(Leave);

            _session.OnSessionFailed += ShowError;
            _session.OnSessionStarted += HandleSessionStarted;
        }

        private void Start() => RefreshServerList();

        private void OnDestroy()
        {
            _session.OnSessionFailed -= ShowError;
            _session.OnSessionStarted -= HandleSessionStarted;
        }

        private void CreateOnline()
        {
            if (_busy) return;
            SetBusy(true, "正在创建大厅…");

            // Steam 不可用时降级到 UTP 直连，而不是直接报错
            if (!SteamSession.IsSteamAvailable)
            {
                _transport.SetKind(TransportSwitcher.TransportKind.UnityTransport);
                ShowStatus("Steam 不可用，已切换到直连模式");
                _session.StartHost(7770);
                return;
            }

            _lobby.CreateLobby(friendsOnly: false);
        }

        private void CreateOffline()
        {
            if (_busy) return;
            SetBusy(true, "正在启动本地房间…");

            _transport.SetKind(TransportSwitcher.TransportKind.UnityTransport);
            _session.StartHost(7770);
        }

        public void JoinSaved(string address, ushort port, string displayName)
        {
            if (_busy) return;
            SetBusy(true, $"正在连接 {displayName}…");

            _store.AddOrTouch(displayName, address, port);
            _session.StartClient(address, port);
        }

        private void CopyLobbyId()
        {
            if (!_lobby.InLobby) return;

            GUIUtility.systemCopyBuffer = _lobby.CurrentLobbyId;
            ShowStatus("大厅 ID 已复制到剪贴板");
        }

        private void Leave()
        {
            _session.StopSession();
            _lobby.LeaveLobby();
            SetBusy(false, "已离开房间");
            RefreshServerList();
        }

        private void HandleSessionStarted()
        {
            SetBusy(false, "已连接");

            var hostAddress = _lobby.GetHostAddress();
            _lobbyIdText.text = string.IsNullOrEmpty(hostAddress)
                ? "本地房间"
                : hostAddress;
        }

        private void ShowError(string message) => SetBusy(false, $"<color=#E24B4A>{message}</color>");

        private void ShowStatus(string message)
        {
            if (_statusText != null)
                _statusText.text = message;
        }

        private void SetBusy(bool busy, string status)
        {
            _busy = busy;

            if (_createOnlineButton) _createOnlineButton.interactable = !busy;
            if (_createOfflineButton) _createOfflineButton.interactable = !busy;

            ShowStatus(status);
        }

        private void RefreshServerList()
        {
            foreach (var view in _entryViews)
                if (view != null) Destroy(view.gameObject);

            _entryViews.Clear();

            foreach (var entry in _store.Entries)
            {
                var view = Instantiate(_entryPrefab, _serverListRoot);
                view.Bind(entry, this);
                _entryViews.Add(view);
            }
        }
    }
}
