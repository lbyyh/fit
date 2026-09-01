using System.Collections.Generic;
using FishNet.Object;
using Fit.Networking;
using UnityEngine;

namespace Fit.Gameplay.Creatures
{
    /// <summary>
    /// 全局生物注册表。
    /// 让 AI 查询玩家/生物时不必每次 FindObjectsOfType —— 后者在几百个对象的场景里
    /// 每帧调用会导致明显的 GC 与 CPU 尖峰。
    /// </summary>
    public static class CreatureRegistry
    {
        public static readonly List<Transform> ActivePlayers = new();
        private static readonly List<Creature> _creatures = new();

        public static IReadOnlyList<Creature> Creatures => _creatures;

        public static void RegisterPlayer(Transform t)
        {
            if (!ActivePlayers.Contains(t))
                ActivePlayers.Add(t);
        }

        public static void UnregisterPlayer(Transform t) => ActivePlayers.Remove(t);

        public static void RegisterCreature(Creature c)
        {
            if (!_creatures.Contains(c))
                _creatures.Add(c);
        }

        public static void UnregisterCreature(Creature c) => _creatures.Remove(c);
    }

    /// <summary>
    /// 生物生成与密度管理。
    ///
    /// 对应 How to Fish 的 CreatureManager / BirdManager / IslandSpawner 这一层。
    ///
    /// 联机游戏里生成逻辑必须全部在服务器：
    ///   - 客户端各自生成会导致实体重复、ID 冲突；
    ///   - 生成位置要用服务器权威的随机（或固定种子），否则客户端预测会错位。
    ///
    /// 另外要注意"生成上限"：没有上限的话玩家挂机几小时会把实体数堆到几千，
    /// 服务器 tick 直接崩。How to Fish 的 CreatureManager 就有 AddAliveCreature 计数。
    /// </summary>
    public sealed class CreatureManager : NetworkEntity
    {
        [System.Serializable]
        public class SpawnRule
        {
            public Creature Prefab;
            public int MaxAlive = 20;
            public float IntervalSeconds = 3f;
            public float MinDistanceFromPlayer = 30f;
            public float MaxDistanceFromPlayer = 120f;
        }

        [Header("配置")]
        [SerializeField] private List<SpawnRule> _rules = new();
        [SerializeField] private int _globalCap = 80;
        [SerializeField] private float _spawnRadius = 150f;

        private readonly Dictionary<int, int> _aliveCounts = new();
        private readonly float[] _nextSpawnTimes = System.Array.Empty<float>();

        private float _tickTimer;

        public override void OnStartServer()
        {
            base.OnStartServer();

            foreach (var rule in _rules)
                _aliveCounts[rule.Prefab.GetInstanceID()] = 0;
        }

        private void Update()
        {
            if (!base.IsServerStarted) return;

            // 生成检查不需要每帧跑，0.5s 一次足够
            _tickTimer += Time.deltaTime;
            if (_tickTimer < 0.5f) return;
            _tickTimer = 0f;

            for (int i = 0; i < _rules.Count; i++)
                TrySpawn(_rules[i], i);
        }

        private void TrySpawn(SpawnRule rule, int index)
        {
            int key = rule.Prefab.GetInstanceID();

            if (_aliveCounts.TryGetValue(key, out int alive) && alive >= rule.MaxAlive)
                return;

            if (CreatureRegistry.Creatures.Count >= _globalCap)
                return;

            if (index < _nextSpawnTimes.Length && Time.time < _nextSpawnTimes[index])
                return;

            var center = GetSpawnCenter();
            if (center == null) return; // 没有玩家就不生成

            var position = FindValidPosition(center.position, rule);
            if (position == null) return;

            var instance = Instantiate(rule.Prefab, position.Value, Quaternion.identity);
            base.Spawn(instance.gameObject);

            _aliveCounts[key] = alive + 1;
        }

        private Transform GetSpawnCenter()
        {
            var players = CreatureRegistry.ActivePlayers;
            if (players.Count == 0) return null;
            return players[Random.Range(0, players.Count)];
        }

        /// <summary>
        /// 在环形区域内找一个合法点。
        /// 环形（而不是圆形）是为了避免生物直接刷在玩家脸上。
        /// </summary>
        private Vector3? FindValidPosition(Vector3 center, SpawnRule rule)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var offset = Random.insideUnitSphere * _spawnRadius;
                offset.y = 0f;

                float distance = offset.magnitude;
                if (distance < rule.MinDistanceFromPlayer)
                    continue;

                var candidate = center + offset;

                if (Physics.Raycast(candidate + Vector3.up * 50f, Vector3.down, out var hit, 100f))
                    return hit.point;
            }

            return null;
        }
    }
}
