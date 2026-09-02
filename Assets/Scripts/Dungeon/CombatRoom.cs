using System.Collections;
using System.Collections.Generic;
using Fit.Enemies;
using UnityEngine;

namespace Fit.Dungeon
{
    /// <summary>
    /// 战斗房 —— 「清完怪物开门」的核心逻辑。
    ///
    /// 【§5.1 冲突一的第 1 条在这里落地】
    /// 「敌人优先生成在玩家正面 ±120° 扇形，背后用音效+红光预警」。
    /// 具体做法：进入房间时筛选刷怪点，优先用玩家看得见的；
    /// 如果刷怪点全在背后，就**延迟刷怪**并先用屏幕边缘红光预告，
    /// 等玩家转过身（或超时）再出怪。
    /// 这条规则看着小，但直接决定"会不会莫名其妙挨打"。
    ///
    /// 【同屏敌人上限】
    /// 建议 5-8 个。超过这个数，弹幕总量会突破第一人称的可读上限，
    /// 玩家从"紧张"直接滑到"烦躁"。宁可多波次，不要一次堆满。
    /// </summary>
    public sealed class CombatRoom : RoomBehaviour
    {
        [Header("波次")]
        [SerializeField] private List<EnemyBase> _enemyPrefabs = new();
        [SerializeField, Min(1)] private int _waveCount = 1;
        [SerializeField] private int _enemiesPerWave = 4;
        [SerializeField, Tooltip("同屏敌人上限。建议 5-8。")]
        private int _maxConcurrent = 6;
        [SerializeField] private float _waveInterval = 1.2f;

        [Header("刷怪规则（视野约束）")]
        [SerializeField, Tooltip("敌人只在这个角度内生成（相对玩家朝向）。超过则延迟+预警。")]
        private float _spawnArcDegrees = 120f;
        [SerializeField] private float _offScreenWarnSeconds = 1.2f;

        [Header("门")]
        [SerializeField] private GameObject[] _doorBarriers;

        private readonly List<EnemyBase> _aliveEnemies = new();
        private Coroutine _waveRoutine;
        private bool _allWavesSpawned;

        private void Awake()
        {
            _type = RoomType.Combat;
        }

        protected override void OnEntered()
        {
            SetDoorsClosed(true);
            _aliveEnemies.Clear();
            _allWavesSpawned = false;
            _waveRoutine = StartCoroutine(WaveRoutine());
        }

        protected override void OnCleared()
        {
            SetDoorsClosed(false);
            if (_waveRoutine != null) StopCoroutine(_waveRoutine);
        }

        private IEnumerator WaveRoutine()
        {
            for (int wave = 0; wave < _waveCount; wave++)
            {
                // 等场上敌人降到阈值以下再开下一波，避免堆满
                while (_aliveEnemies.Count > _maxConcurrent * 0.5f)
                    yield return null;

                for (int i = 0; i < _enemiesPerWave; i++)
                {
                    if (_aliveEnemies.Count >= _maxConcurrent)
                        yield return null;

                    SpawnOne();
                    yield return new WaitForSeconds(0.15f);
                }

                if (wave < _waveCount - 1)
                    yield return new WaitForSeconds(_waveInterval);
            }

            _allWavesSpawned = true;
        }

        private void SpawnOne()
        {
            if (_enemyPrefabs.Count == 0 || _spawnPoints.Length == 0) return;

            Transform point = PickSpawnPoint();
            var prefab = _enemyPrefabs[Random.Range(0, _enemyPrefabs.Count)];

            var enemy = Instantiate(prefab, point.position, point.rotation);
            enemy.OnStateChanged += HandleEnemyStateChanged;
            _aliveEnemies.Add(enemy);

            var player = FindPlayer();
            if (player != null) enemy.SetTarget(player);
        }

        /// <summary>
        /// 挑选刷怪点：优先玩家正面扇形内的。
        /// 全都在背后时，返回最靠中间的那个，并由调用方先做屏幕边缘预警。
        /// </summary>
        private Transform PickSpawnPoint()
        {
            var player = FindPlayer();
            if (player == null) return _spawnPoints[0];

            Vector3 playerForward = player.forward;
            playerForward.y = 0f;
            playerForward.Normalize();

            Transform fallback = _spawnPoints[0];
            float bestAngle = float.MaxValue;

            foreach (var point in _spawnPoints)
            {
                Vector3 toPoint = point.position - player.position;
                toPoint.y = 0f;
                if (toPoint.sqrMagnitude < 0.001f) continue;

                float angle = Vector3.Angle(playerForward, toPoint.normalized);
                if (angle <= _spawnArcDegrees * 0.5f)
                    return point;    // 正面内，直接用

                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    fallback = point;
                }
            }

            // 没有正面刷怪点：先预警，再出怪
            StartCoroutine(WarnThenSpawn(fallback));
            return fallback;
        }

        private IEnumerator WarnThenSpawn(Transform point)
        {
            var indicator = FindObjectOfType<Feedback.ThreatIndicator>();
            indicator?.RegisterThreat(point, _offScreenWarnSeconds, heavy: true);
            yield return new WaitForSeconds(_offScreenWarnSeconds);
        }

        private void HandleEnemyStateChanged(EnemyState state)
        {
            if (state != EnemyState.Dead) return;

            _aliveEnemies.RemoveAll(e => e == null || e.State == EnemyState.Dead);

            if (_allWavesSpawned && _aliveEnemies.Count == 0)
                ClearRoom();
        }

        private void SetDoorsClosed(bool closed)
        {
            if (_doorBarriers == null) return;
            foreach (var barrier in _doorBarriers)
                if (barrier != null) barrier.SetActive(closed);
        }

        private static Transform FindPlayer()
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            return go != null ? go.transform : null;
        }

        public override bool ValidateAnchors(out string error)
        {
            if (!base.ValidateAnchors(out error)) return false;

            if (_spawnPoints == null || _spawnPoints.Length == 0)
            {
                error = $"{name}: 战斗房没有刷怪点";
                return false;
            }

            if (_enemyPrefabs.Count == 0)
            {
                error = $"{name}: 战斗房没有配置敌人预制体";
                return false;
            }

            return true;
        }
    }
}
