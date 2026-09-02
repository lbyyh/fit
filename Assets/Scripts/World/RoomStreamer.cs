using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Fit.World
{
    /// <summary>
    /// 房间流式加载器。
    ///
    /// 【这是 IslandStreamer 的重构，不是重写】
    /// 原版用于海洋开放世界的岛屿装卸（见 Legacy/World/IslandStreamer）。
    /// 核心逻辑——「按距离调度加载、迟滞防抖、二次确认卸载、切换期间锁输入」——
    /// 在地牢里完全通用，只是粒度从「岛」换成「房间」，距离判据从世界坐标
    /// 换成房间图的跳数（hop count）。
    /// 保留这些设计的原因是它们解决的是同一类问题：边界抖动导致的反复加载。
    ///
    /// 【为什么用跳数而不是欧氏距离】
    /// 房间是图结构，两个房间可能物理上很近但图上不连通（隔着墙）。
    /// 用跳数才能正确表达"玩家下一步可能去哪"。
    ///
    /// 【加载策略】
    /// 保持「当前房间 + 相邻一层」常驻。这样开门瞬间相邻房间已经就绪，
    /// 不会有加载卡顿打断战斗节奏。
    /// </summary>
    public sealed class RoomStreamer : MonoBehaviour
    {
        [Serializable]
        public class RoomDefinition
        {
            public string Id;
            public string AddressableKey;
            /// <summary>房间图上的坐标。用于计算跳数。</summary>
            public Vector2Int GridPosition;
        }

        [Header("配置")]
        [SerializeField] private List<RoomDefinition> _rooms = new();
        [Tooltip("预加载的跳数。1 = 只预加载直接相邻的房间。")]
        [SerializeField] private int _preloadHops = 1;
        [Tooltip("卸载的跳数阈值。要比预加载大，形成迟滞。")]
        [SerializeField] private int _unloadHops = 3;
        [SerializeField] private float _unloadDelaySeconds = 2f;
        [SerializeField] private float _minSwitchLockSeconds = 0.25f;

        private readonly Dictionary<string, RoomRuntime> _runtimes = new();
        private readonly HashSet<string> _pending = new();

        private Vector2Int _currentCell;
        private bool _hasCurrentCell;
        private float _switchLockUntil;

        public event Action<string> OnRoomLoaded;
        public event Action<string> OnRoomUnloaded;
        public event Action<bool> OnSwitchingChanged;

        public bool IsSwitching => _pending.Count > 0;

        /// <summary>切换期间锁输入，防止在半加载状态下开门/穿门。</summary>
        public bool InputLocked => Time.time < _switchLockUntil || IsSwitching;

        public void Initialize()
        {
            foreach (var def in _rooms)
                _runtimes[def.Id] = new RoomRuntime(def);
        }

        /// <summary>玩家进入某个房间格子。由房间触发器调用。</summary>
        public void SetCurrentCell(Vector2Int cell)
        {
            _currentCell = cell;
            _hasCurrentCell = true;
            EvaluateAll();
        }

        public void RegisterRoom(string id, Vector2Int cell)
        {
            _runtimes[id] = new RoomRuntime(new RoomDefinition { Id = id, GridPosition = cell });
        }

        private void EvaluateAll()
        {
            if (!_hasCurrentCell) return;

            foreach (var runtime in _runtimes.Values)
                Evaluate(runtime);
        }

        private void Evaluate(RoomRuntime runtime)
        {
            // 曼哈顿距离作为跳数：房间图是四连通的网格
            int hops = Mathf.Abs(runtime.Definition.GridPosition.x - _currentCell.x)
                     + Mathf.Abs(runtime.Definition.GridPosition.y - _currentCell.y);

            if (runtime.IsLoaded)
            {
                if (hops > _unloadHops && !_pending.Contains(runtime.Definition.Id))
                    StartCoroutine(DelayedUnload(runtime));
            }
            else
            {
                if (hops <= _preloadHops && !_pending.Contains(runtime.Definition.Id))
                    StartCoroutine(LoadRoutine(runtime));
            }
        }

        private IEnumerator LoadRoutine(RoomRuntime runtime)
        {
            var id = runtime.Definition.Id;
            _pending.Add(id);
            _switchLockUntil = Time.time + _minSwitchLockSeconds;
            OnSwitchingChanged?.Invoke(true);

            var handle = Addressables.LoadSceneAsync(
                runtime.Definition.AddressableKey,
                LoadSceneMode.Additive,
                activateOnLoad: true);

            yield return handle;

            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                runtime.Handle = handle;
                runtime.IsLoaded = true;
                OnRoomLoaded?.Invoke(id);
            }
            else
            {
                Debug.LogError($"[RoomStreamer] 加载房间 {id} 失败");
            }

            _pending.Remove(id);
            if (_pending.Count == 0) OnSwitchingChanged?.Invoke(false);
        }

        /// <summary>
        /// 延迟卸载 + 二次确认。
        /// 继承自 IslandStreamer 的关键设计：玩家在门边来回走时会疯狂装卸，
        /// 必须等一段时间再确认一次，否则会 thrashing。
        /// </summary>
        private IEnumerator DelayedUnload(RoomRuntime runtime)
        {
            var id = runtime.Definition.Id;
            _pending.Add(id);

            yield return new WaitForSeconds(_unloadDelaySeconds);

            // 二次确认：玩家可能又走回来了
            if (_hasCurrentCell)
            {
                int hops = Mathf.Abs(runtime.Definition.GridPosition.x - _currentCell.x)
                         + Mathf.Abs(runtime.Definition.GridPosition.y - _currentCell.y);

                if (hops <= _unloadHops)
                {
                    _pending.Remove(id);
                    yield break;
                }
            }

            if (runtime.Handle.HasValue && runtime.Handle.Value.IsValid())
            {
                var op = Addressables.UnloadSceneAsync(runtime.Handle.Value);
                yield return op;
            }

            runtime.Handle = null;
            runtime.IsLoaded = false;
            OnRoomUnloaded?.Invoke(id);

            _pending.Remove(id);
            if (_pending.Count == 0) OnSwitchingChanged?.Invoke(false);
        }

        public IEnumerator UnloadAll()
        {
            foreach (var runtime in _runtimes.Values)
            {
                if (!runtime.IsLoaded) continue;

                if (runtime.Handle.HasValue && runtime.Handle.Value.IsValid())
                    yield return Addressables.UnloadSceneAsync(runtime.Handle.Value);

                runtime.Handle = null;
                runtime.IsLoaded = false;
            }

            _hasCurrentCell = false;
        }

        private sealed class RoomRuntime
        {
            public readonly RoomDefinition Definition;
            public bool IsLoaded;
            public AsyncOperationHandle<SceneInstance>? Handle;

            public RoomRuntime(RoomDefinition definition) => Definition = definition;
        }
    }
}
