using System;
using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Managing.Scened;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Fit.World
{
    /// <summary>
    /// 岛屿流式加载器。
    ///
    /// 直接对应 How to Fish 的 OnlineIslandManager：
    /// 主场景常驻，5 个岛屿作为 Additive 场景按需装卸，玩家在岛之间切换。
    /// 代码里能看到 LoadIsland / UnloadAllIslandsRoutine / OnIslandChange /
    /// IslandSpawner / IslandBuildOffset / TimeWhenSwappingIsland 一整套生命周期。
    ///
    /// 为什么要流式而不是一张大地图：
    ///   - 内存：一张覆盖全部岛屿的地形 + 植被，显存很容易破 4GB；
    ///   - 加载时间：分包后可以边玩边下；
    ///   - 联机同步：只同步玩家所在区域的对象，带宽与 CPU 都省。
    ///
    /// 两个必须处理好的细节：
    ///   1. 切换期间的输入锁 —— How to Fish 有 TimeWhenSwappingIsland，
    ///      就是为了防止玩家在卸载过程中做出跨岛操作导致状态错乱；
    ///   2. 卸载时机 —— 不能玩家一离开边界就卸载，要留缓冲，
    ///      否则在边界来回走会触发反复加载（thrashing）。
    /// </summary>
    public sealed class IslandStreamer : MonoBehaviour
    {
        [Serializable]
        public class IslandDefinition
        {
            public string Id;
            public string AddressableKey;
            public Vector3 WorldOffset;
            public float UnloadRadius = 260f;
        }

        [Header("配置")]
        [SerializeField] private List<IslandDefinition> _islands = new();
        [SerializeField] private float _loadRadius = 180f;
        [SerializeField] private float _unloadHysteresis = 60f;   // 迟滞，防止边界抖动
        [SerializeField] private float _minSwitchLockSeconds = 0.35f;

        [Header("联机")]
        [SerializeField] private bool _networked = true;

        private readonly Dictionary<string, IslandRuntime> _runtimes = new();
        private readonly HashSet<string> _pending = new();

        private Transform _observer;
        private float _switchLockUntil;

        public event Action<string> OnIslandLoaded;
        public event Action<string> OnIslandUnloaded;
        public event Action<bool> OnSwitchingChanged;

        public bool IsSwitching => _pending.Count > 0;
        public string CurrentIslandId { get; private set; } = string.Empty;

        public void Initialize()
        {
            foreach (var def in _islands)
                _runtimes[def.Id] = new IslandRuntime(def);
        }

        private void LateUpdate()
        {
            if (_observer == null) return;

            foreach (var runtime in _runtimes.Values)
                Evaluate(runtime, _observer.position);
        }

        public void SetObserver(Transform observer) => _observer = observer;

        private void Evaluate(IslandRuntime runtime, Vector3 observerPos)
        {
            var def = runtime.Definition;
            Vector3 islandCenter = def.WorldOffset;
            float distance = Vector3.Distance(observerPos, islandCenter);

            bool shouldBeLoaded = runtime.IsLoaded
                ? distance <= def.UnloadRadius + _unloadHysteresis
                : distance <= _loadRadius;

            if (shouldBeLoaded && !runtime.IsLoaded && !_pending.Contains(def.Id))
                Load(runtime);

            else if (!shouldBeLoaded && runtime.IsLoaded && !_pending.Contains(def.Id))
                StartCoroutine(DelayedUnload(runtime));
        }

        private void Load(IslandRuntime runtime)
        {
            var id = runtime.Definition.Id;
            _pending.Add(id);
            _switchLockUntil = Time.time + _minSwitchLockSeconds;
            OnSwitchingChanged?.Invoke(true);

            StartCoroutine(LoadRoutine(runtime));
        }

        private IEnumerator LoadRoutine(IslandRuntime runtime)
        {
            var def = runtime.Definition;

            if (_networked && InstanceFinder.NetworkManager != null && InstanceFinder.NetworkManager.IsServerStarted)
            {
                // 联机模式：走 FishNet 的 SceneManager，保证所有客户端同步加载
                var sld = new SceneLoadData(def.AddressableKey)
                {
                    Options = new LoadOptions { AllowStacking = false },
                    PreferredActiveScene = new PreferredScene(SceneLookupOption.Shift, 0)
                };

                InstanceFinder.SceneManager.LoadConnectionScenes(sld);
                // 实际项目中应监听 SceneManager.OnLoadEnd 后再置 IsLoaded
                yield return new WaitForSeconds(0.1f);
            }
            else
            {
                // 单机模式：直接走 Addressables
                var handle = Addressables.LoadSceneAsync(def.AddressableKey,
                                                         LoadSceneMode.Additive,
                                                         activateOnLoad: true);

                yield return handle;

                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    runtime.Handle = handle;
                    runtime.IsLoaded = true;
                }
                else
                {
                    Debug.LogError($"[IslandStreamer] 加载岛屿 {def.Id} 失败");
                }
            }

            if (runtime.IsLoaded)
            {
                CurrentIslandId = def.Id;
                OnIslandLoaded?.Invoke(def.Id);
            }

            _pending.Remove(def.Id);
            if (_pending.Count == 0)
                OnSwitchingChanged?.Invoke(false);
        }

        /// <summary>
        /// 延迟卸载 + 二次确认。
        /// 直接卸载的话，玩家在边界来回横跳会疯狂加载卸载，这里用协程等一段时间再确认。
        /// </summary>
        private IEnumerator DelayedUnload(IslandRuntime runtime)
        {
            var id = runtime.Definition.Id;
            _pending.Add(id);

            yield return new WaitForSeconds(2f);

            // 等一会儿再确认一次，玩家可能又走回来了
            if (_observer != null)
            {
                float d = Vector3.Distance(_observer.position, runtime.Definition.WorldOffset);
                if (d <= runtime.Definition.UnloadRadius + _unloadHysteresis)
                {
                    _pending.Remove(id);
                    yield break;
                }
            }

            if (runtime.Handle.HasValue && runtime.Handle.Value.IsValid())
                Addressables.UnloadSceneAsync(runtime.Handle.Value);

            runtime.Handle = null;
            runtime.IsLoaded = false;

            if (CurrentIslandId == id)
                CurrentIslandId = string.Empty;

            OnIslandUnloaded?.Invoke(id);
            _pending.Remove(id);

            if (_pending.Count == 0)
                OnSwitchingChanged?.Invoke(false);
        }

        /// <summary>切换期间锁输入，避免在半加载状态下操作。</summary>
        public bool InputLocked => Time.time < _switchLockUntil || IsSwitching;

        public IEnumerator UnloadAll()
        {
            foreach (var runtime in _runtimes.Values)
            {
                if (!runtime.IsLoaded) continue;

                if (runtime.Handle.HasValue && runtime.Handle.Value.IsValid())
                {
                    var op = Addressables.UnloadSceneAsync(runtime.Handle.Value);
                    yield return op;
                }

                runtime.Handle = null;
                runtime.IsLoaded = false;
            }

            CurrentIslandId = string.Empty;
        }

        private sealed class IslandRuntime
        {
            public readonly IslandDefinition Definition;
            public bool IsLoaded;
            public AsyncOperationHandle<SceneInstance>? Handle;

            public IslandRuntime(IslandDefinition definition) => Definition = definition;
        }
    }
}
