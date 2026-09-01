using FishNet;
using FishNet.Managing;
using Fit.Save;
using Fit.Voice;
using Fit.World;
using UnityEngine;

namespace Fit.Core
{
    /// <summary>
    /// 游戏唯一入口。负责按依赖顺序拉起各子系统，并在退出时反序释放。
    ///
    /// 设计来源：How to Fish 的 RuntimeInitializeOnLoads 里有 70+ 个自动初始化入口，
    /// 好处是接入快，坏处是初始化顺序不可控、依赖关系隐藏。
    /// 这里改成显式编排，顺序一目了然，也便于排查启动期问题。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Header("子系统")]
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private IslandStreamer _islandStreamer;
        [SerializeField] private VoiceSystem _voiceSystem;
        [SerializeField] private AutoSaver _autoSaver;

        private void Awake()
        {
            Application.runInBackground = true; // 房主切窗口时世界必须继续跑
            Application.targetFrameRate = 0;    // 交给玩家在设置里限帧

            ServiceLocator.Register(_networkManager);
            ServiceLocator.Register(_islandStreamer);
            ServiceLocator.Register(_voiceSystem);
            ServiceLocator.Register(_autoSaver);
        }

        private void Start()
        {
            _islandStreamer.Initialize();
            _voiceSystem.Initialize();
            _autoSaver.Initialize();
        }

        private void OnApplicationQuit()
        {
            _autoSaver.Flush();
            _voiceSystem.Shutdown();
            ServiceLocator.Clear();
        }
    }

    /// <summary>
    /// 极简服务定位器。只用于跨系统引用，不承担依赖注入职责。
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly System.Collections.Generic.Dictionary<System.Type, Object> _services = new();

        public static void Register<T>(T instance) where T : Object
        {
            _services[typeof(T)] = instance;
        }

        public static T Get<T>() where T : Object
        {
            return _services.TryGetValue(typeof(T), out var found) ? (T)found : null;
        }

        public static bool TryGet<T>(out T instance) where T : Object
        {
            instance = Get<T>();
            return instance != null;
        }

        public static void Clear() => _services.Clear();
    }
}
