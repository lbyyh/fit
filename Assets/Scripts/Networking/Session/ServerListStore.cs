using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fit.Networking.Session
{
    /// <summary>
    /// 本地"最近加入的房间"记录。
    ///
    /// How to Fish 里有 LoadAllServers / UpdateSavedServerButtons / DeleteServerButton 一整套，
    /// 说明本地房间列表是玩家高频使用路径 —— 熟人联机场景下，玩家反复进的是同几个房间。
    /// 这里把存储与业务解耦，序列化用 Newtonsoft.Json（与 How to Fish 一致）。
    /// </summary>
    public sealed class ServerListStore
    {
        private const int MaxEntries = 20;

        private readonly string _filePath;
        private readonly List<ServerEntry> _entries = new();

        public IReadOnlyList<ServerEntry> Entries => _entries;

        public ServerListStore(string fileName = "servers.json")
        {
            _filePath = Path.Combine(Application.persistentDataPath, fileName);
            Load();
        }

        public void AddOrTouch(string displayName, string address, ushort port)
        {
            var existing = _entries.Find(e => e.Address == address && e.Port == port);

            if (existing != null)
            {
                existing.DisplayName = displayName;
                existing.LastJoined = DateTime.UtcNow.ToBinary();
            }
            else
            {
                _entries.Add(new ServerEntry
                {
                    DisplayName = displayName,
                    Address = address,
                    Port = port,
                    LastJoined = DateTime.UtcNow.ToBinary()
                });
            }

            _entries.Sort((a, b) => b.LastJoined.CompareTo(a.LastJoined));

            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);

            Save();
        }

        public void Remove(string address, ushort port)
        {
            _entries.RemoveAll(e => e.Address == address && e.Port == port);
            Save();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;

                var json = File.ReadAllText(_filePath);
                var loaded = JsonUtility.FromJson<Wrapper>(json);

                if (loaded?.Items != null)
                    _entries.AddRange(loaded.Items);
            }
            catch (Exception ex)
            {
                // 存档损坏不应阻断启动，清空重来即可
                Debug.LogWarning($"[ServerList] 读取失败，将重置：{ex.Message}");
                _entries.Clear();
            }
        }

        private void Save()
        {
            try
            {
                var json = JsonUtility.ToJson(new Wrapper { Items = _entries }, true);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ServerList] 保存失败：{ex.Message}");
            }
        }

        [Serializable]
        public class ServerEntry
        {
            public string DisplayName;
            public string Address;
            public ushort Port;
            public long LastJoined;

            public DateTime LastJoinedUtc => DateTime.FromBinary(LastJoined);
        }

        // JsonUtility 不能直接序列化 List<T>，包一层
        [Serializable]
        private class Wrapper
        {
            public List<ServerEntry> Items = new();
        }
    }
}
