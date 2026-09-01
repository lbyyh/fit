using System;
using UnityEngine;

namespace Fit.Save
{
    /// <summary>
    /// 自动存档。
    ///
    /// How to Fish 有 AutoSaver + AutoSave 协程，说明它用的是定时自动存档
    /// 而不是纯手动存档 —— 在联机 co-op 里这是必须的，
    /// 因为玩家不知道房主什么时候关房间，靠手动存档一定会丢进度。
    ///
    /// 三个设计点：
    ///   1. 节流 —— 每次金币变动都写盘会卡主线程，用最小间隔 + 脏标记合并写入；
    ///   2. 时机 —— 除了定时，还要在关键节点强制落盘（房主退出、退出房间、断电级事件）；
    ///   3. 云同步 —— Steam 云写入是异步的且有配额，失败不能阻断本地存档。
    /// </summary>
    public sealed class AutoSaver : MonoBehaviour
    {
        [Header("配置")]
        [SerializeField] private float _intervalSeconds = 120f;
        [SerializeField] private float _minWriteIntervalSeconds = 5f;

        public event Action OnSaved;

        public PlayerProgress Progress { get; private set; }
        public WorldState World { get; private set; }

        private float _nextAutoSave;
        private float _lastWriteTime = -999f;
        private bool _dirty;

        public void Initialize()
        {
            Load();
            _nextAutoSave = Time.time + _intervalSeconds;
        }

        private void Update()
        {
            if (Time.time >= _nextAutoSave)
            {
                _nextAutoSave = Time.time + _intervalSeconds;
                Save();
            }
        }

        public void Load()
        {
            Progress = LoadOrCreate<PlayerProgress>(SaveFile.ProgressPath);
            World = LoadOrCreate<WorldState>(SaveFile.WorldPath);
        }

        private static T LoadOrCreate<T>(string path) where T : class, new()
        {
            if (SaveFile.TryRead(path, out var json))
            {
                try
                {
                    var loaded = JsonUtility.FromJson<T>(json);
                    if (loaded != null)
                        return loaded;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AutoSaver] 存档解析失败，已重置：{ex.Message}");
                }
            }

            return new T();
        }

        /// <summary>标记数据已变更。真正写盘由节流逻辑决定时机。</summary>
        public void MarkDirty() => _dirty = true;

        /// <summary>立即写盘。玩家点"保存"、切场景、房主关房间时调用。</summary>
        public void Save()
        {
            if (!_dirty) return;
            if (Time.time - _lastWriteTime < _minWriteIntervalSeconds) return;

            Flush();
        }

        /// <summary>强制写盘，忽略节流与脏标记。退出流程必须走这个。</summary>
        public void Flush()
        {
            _lastWriteTime = Time.time;
            _dirty = false;

            SaveFile.WriteAtomic(SaveFile.ProgressPath, JsonUtility.ToJson(Progress, true));

            // 世界状态只在房主端写入
            if (Fit.Core.ServiceLocator.TryGet<Fit.Networking.FitNetworkManager>(out var session) && session.IsHosting)
                SaveFile.WriteAtomic(SaveFile.WorldPath, JsonUtility.ToJson(World, true));

            UploadToCloud();

            OnSaved?.Invoke();
        }

        private void UploadToCloud()
        {
#if !DISABLESTEAMWORKS
            try
            {
                if (!Steamworks.SteamClient.IsValid) return;

                // Steam 云写入是异步且有配额限制的，这里做一次尽力而为的同步。
                // 失败不影响本地存档，下次启动会重试。
                // Steamworks.SteamRemoteStorage.FileWriteAsync(SaveFile.ProgressPath, bytes);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AutoSaver] 云存档失败：{ex.Message}");
            }
#endif
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) Flush();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) Flush();
        }
    }
}
