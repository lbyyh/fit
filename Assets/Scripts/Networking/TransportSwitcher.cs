using FishNet.Managing;
using FishNet.Managing.Timing;
using FishNet.Transporting;
using FishNet.Transporting.Multipass;
using UnityEngine;

namespace Fit.Networking
{
    /// <summary>
    /// 传输层切换器 —— 复用 How to Fish 的 Multipass 双栈设计。
    ///
    /// 为什么要两条链路：
    ///   1. FishySteamworks 走 Steam Networking Sockets，天然穿透 NAT，房主无需端口转发，
    ///      是 Steam 版本的主路径。
    ///   2. FishyUnityTransport 走 UTP/UDP 直连，用于非 Steam 环境（其他商店、局域网、
    ///      开发期多开），以及 Steam 中继不可用时的降级。
    ///
    /// 运行时可根据 Steam 初始化结果自动选择，也可由玩家在设置里手动指定。
    /// </summary>
    public sealed class TransportSwitcher : MonoBehaviour
    {
        public enum TransportKind
        {
            Auto = 0,
            Steam = 1,
            UnityTransport = 2
        }

        [Header("引用")]
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private Multipass _multipass;

        [Header("设置")]
        [SerializeField] private TransportKind _kind = TransportKind.Auto;

        public TransportKind Kind => _kind;
        public Transport ActiveTransport { get; private set; }

        private void Awake()
        {
            if (_networkManager == null)
                _networkManager = InstanceFinder.NetworkManager;
            if (_multipass == null && _networkManager != null)
                _multipass = _networkManager.TransportManager.GetComponent<Multipass>();
        }

        /// <summary>
        /// 在启动连接前调用，决定本次会话使用哪条链路。
        /// Steam 未初始化（非 Steam 环境 / 未登录 / Steam 未运行）时自动退回 UTP。
        /// </summary>
        public Transport Resolve()
        {
            var transportManager = _networkManager.TransportManager;

            Transport chosen = _kind switch
            {
                TransportKind.Steam => FindTransportByTypeName("FishySteamworks"),
                TransportKind.UnityTransport => FindTransportByTypeName("FishyUnityTransport"),
                _ => SteamSession.IsSteamAvailable
                    ? FindTransportByTypeName("FishySteamworks")
                    : FindTransportByTypeName("FishyUnityTransport")
            };

            if (chosen == null)
            {
                Debug.LogError("[TransportSwitcher] 未找到可用传输组件，回退到 Multipass 默认。");
                return transportManager.Transport;
            }

            transportManager.SetClientTransport(chosen);
            transportManager.SetServerTransport(chosen);
            ActiveTransport = chosen;

            Debug.Log($"[TransportSwitcher] 使用传输：{chosen.GetType().Name}");
            return chosen;
        }

        private Transport FindTransportByTypeName(string typeName)
        {
            if (_multipass == null)
                return null;

            foreach (var t in _multipass.Transports)
            {
                if (t == null) continue;
                if (t.GetType().Name.Contains(typeName))
                    return t;
            }
            return null;
        }

        public void SetKind(TransportKind kind) => _kind = kind;
    }

    /// <summary>
    /// Steam 可用性探测。集中在一处，避免各处散落 SteamAPI.IsSteamRunning() 调用。
    /// </summary>
    public static class SteamSession
    {
        public static bool IsSteamAvailable
        {
            get
            {
#if !DISABLESTEAMWORKS
                try
                {
                    return Steamworks.SteamAPI.IsSteamRunning() && Steamworks.SteamClient.IsValid;
                }
                catch
                {
                    return false;
                }
#else
                return false;
#endif
            }
        }
    }
}
