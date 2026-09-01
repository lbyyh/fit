using System;
using FishNet;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace Fit.Networking
{
    /// <summary>
    /// 会话生命周期管理：开房间 / 加入 / 断开 / 重连。
    ///
    /// 拓扑沿用 How to Fish 的 Listen Server（房主即服务器）：
    ///   - 零服务器运维成本，适合小团队；
    ///   - 代价是房主掉线整个房间结束，所以重连逻辑必须做扎实；
    ///   - 另一个代价是房主拥有权威，作弊门槛低 —— 需要在反作弊上另想办法。
    /// </summary>
    public sealed class FitNetworkManager : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private TransportSwitcher _transportSwitcher;

        [Header("重连")]
        [SerializeField] private int _maxReconnectAttempts = 5;
        [SerializeField] private float _reconnectDelaySeconds = 2f;

        private int _reconnectAttempts;
        private string _lastAddress = string.Empty;
        private ushort _lastPort;

        public event Action OnSessionStarted;
        public event Action<string> OnSessionFailed;
        public event Action OnSessionEnded;

        public bool IsHosting => _networkManager != null && _networkManager.IsServerStarted;
        public bool IsConnected => _networkManager != null && _networkManager.IsClientStarted;

        private void Awake()
        {
            if (_networkManager == null)
                _networkManager = InstanceFinder.NetworkManager;
        }

        private void OnEnable()
        {
            _networkManager.ServerManager.OnServerConnectionState += HandleServerState;
            _networkManager.ClientManager.OnClientConnectionState += HandleClientState;
        }

        private void OnDisable()
        {
            _networkManager.ServerManager.OnServerConnectionState -= HandleServerState;
            _networkManager.ClientManager.OnClientConnectionState -= HandleClientState;
        }

        /// <summary>作为房主开房间（同时启动服务器与本地客户端）。</summary>
        public void StartHost(ushort port = 7770)
        {
            _transportSwitcher.Resolve();
            _lastPort = port;

            var t = _networkManager.TransportManager.Transport;
            if (t is PortableTransport portable)
                portable.SetPort(port);

            _networkManager.ServerManager.StartConnection();
            _networkManager.ClientManager.StartConnection();
        }

        /// <summary>作为客户端加入。</summary>
        public void StartClient(string address, ushort port = 7770)
        {
            _transportSwitcher.Resolve();
            _lastAddress = address;
            _lastPort = port;

            if (_networkManager.TransportManager.Transport is PortableTransport portable)
                portable.SetClientAddress(address);

            _networkManager.ClientManager.StartConnection(address, port);
        }

        public void StopSession()
        {
            _reconnectAttempts = 0;
            if (_networkManager.IsClientStarted)
                _networkManager.ClientManager.StopConnection();
            if (_networkManager.IsServerStarted)
                _networkManager.ServerManager.StopConnection();
        }

        private void HandleServerState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
                OnSessionStarted?.Invoke();
        }

        private void HandleClientState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                _reconnectAttempts = 0; // 连接成功，重置退避计数
                OnSessionStarted?.Invoke();
                return;
            }

            if (args.ConnectionState != LocalConnectionState.Stopped)
                return;

            OnSessionEnded?.Invoke();

            // 非主动断开才尝试重连，避免退出房间时被重连逻辑拽回来
            if (_reconnectAttempts < _maxReconnectAttempts && !string.IsNullOrEmpty(_lastAddress))
                StartCoroutine(ReconnectRoutine());
        }

        private System.Collections.IEnumerator ReconnectRoutine()
        {
            _reconnectAttempts++;

            // 指数退避，避免房主重启期间客户端疯狂重试
            float delay = _reconnectDelaySeconds * Mathf.Pow(1.5f, _reconnectAttempts - 1);
            Debug.Log($"[Session] 第 {_reconnectAttempts} 次重连，{delay:F1}s 后尝试…");
            yield return new WaitForSeconds(delay);

            if (!IsConnected)
                StartClient(_lastAddress, _lastPort);
            else
                OnSessionFailed?.Invoke("重连时已连接");

            if (_reconnectAttempts >= _maxReconnectAttempts && !IsConnected)
                OnSessionFailed?.Invoke($"重连失败（已尝试 {_maxReconnectAttempts} 次）");
        }
    }
}
