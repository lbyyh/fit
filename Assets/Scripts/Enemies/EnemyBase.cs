using System.Collections;
using Fit.Combat;
using Fit.Feedback;
using UnityEngine;
using UnityEngine.AI;

namespace Fit.Enemies
{
    public enum EnemyState
    {
        Idle,
        Chase,
        Telegraph,
        Attack,
        Recover,
        Dead,
    }

    /// <summary>
    /// 敌人基础 AI。
    ///
    /// 【服务器权威】
    /// 状态机、伤害、发射全部跑在权威端。
    /// 客户端只接收状态用于表现，不做任何判定 —— 否则改内存就能让敌人不动或秒死。
    ///
    /// 【为什么攻击流程强制走「前摇 → 发射」】
    /// ID-008。没有前摇的攻击在弹幕游戏里等于偷袭，玩家无法学习，
    /// 进而无法产生"我变强了"的感觉。所有攻击必须经 Telegraph，
    /// 所以这里把 Telegraph 做成必选组件。
    ///
    /// 【关于"敌人只生成在玩家正面扇形"】
    /// 那不是敌人的职责，是房间生成器（§3.2 DungeonGenerator）的职责：
    /// 刷怪点要选在玩家视野内或用音效预告。见 §5.1 冲突一第 1 条。
    /// 这里只负责"被唤醒后才行动"，避免玩家还没看到就被打。
    /// </summary>
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(Telegraph))]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class EnemyBase : MonoBehaviour
    {
        [Header("感知")]
        [SerializeField] private float _aggroRange = 25f;
        [SerializeField] private float _attackRange = 16f;
        [SerializeField] private LayerMask _sightMask = ~0;

        [Header("移动")]
        [SerializeField] private float _moveSpeed = 3.4f;
        [Tooltip("保持距离：太近会贴脸，太远不出手。")]
        [SerializeField] private float _preferredDistance = 10f;

        [Header("攻击")]
        [SerializeField] private BulletPattern _pattern;
        [SerializeField] private Transform _muzzle;
        [SerializeField] private float _attackCooldown = 2.2f;
        [SerializeField] private float _recoverSeconds = 0.35f;
        [Tooltip("前摇时长。建议 0.3-0.5，Boss 可到 1.2。")]
        [SerializeField] private float _telegraphSeconds = 0.4f;
        [Tooltip("重攻击：前摇更长、提示更强（屏幕边缘红光更亮）")]
        [SerializeField] private bool _isHeavyAttacker;

        [Header("出生保护")]
        [Tooltip("出生后多久才开始行动。给玩家反应时间，避免"刚进门就挨打"。")]
        [SerializeField] private float _spawnGraceSeconds = 0.6f;

        public EnemyState State { get; private set; } = EnemyState.Idle;
        public Transform Target { get; private set; }

        /// <summary>状态变化。客户端用于播放动画。</summary>
        public event System.Action<EnemyState> OnStateChanged;

        private Health _health;
        private Telegraph _telegraph;
        private NavMeshAgent _agent;
        private ThreatIndicator _threatIndicator;
        private float _nextAttackTime;
        private float _spawnTime;
        private bool _dead;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _telegraph = GetComponent<Telegraph>();
            _agent = GetComponent<NavMeshAgent>();

            _agent.speed = _moveSpeed;
            _spawnTime = Time.time;
        }

        private void OnEnable()
        {
            _health.OnDepleted += HandleDeath;
            _telegraph.OnTelegraphCompleted += HandleTelegraphCompleted;
        }

        private void OnDisable()
        {
            _health.OnDepleted -= HandleDeath;
            _telegraph.OnTelegraphCompleted -= HandleTelegraphCompleted;
        }

        public void SetTarget(Transform target) => Target = target;

        private void Update()
        {
            if (_dead) return;
            if (Time.time - _spawnTime < _spawnGraceSeconds) return;

            AcquireTarget();

            switch (State)
            {
                case EnemyState.Idle:
                    if (Target != null && InAggroRange()) SetState(EnemyState.Chase);
                    break;

                case EnemyState.Chase:
                    TickChase();
                    break;

                case EnemyState.Telegraph:
                    // 前摇期间停止移动，让玩家可以读招
                    _agent.isStopped = true;
                    FaceTarget();
                    break;

                case EnemyState.Recover:
                    _agent.isStopped = false;
                    if (Time.time >= _nextAttackTime - _recoverSeconds * 0.5f)
                        SetState(Target != null ? EnemyState.Chase : EnemyState.Idle);
                    break;
            }
        }

        private void AcquireTarget()
        {
            if (Target != null) return;

            // 简化：找最近的存活玩家。正式版应走房间/战斗管理器维护的目标列表
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null && player.GetComponent<IDamageable>()?.IsAlive == true)
                Target = player.transform;
        }

        private bool InAggroRange()
            => Target != null && Vector3.Distance(transform.position, Target.position) <= _aggroRange;

        private bool HasLineOfSight()
        {
            if (Target == null) return false;

            Vector3 from = _muzzle != null ? _muzzle.position : transform.position;
            Vector3 dir = (Target.position - from).normalized;

            return !Physics.Raycast(from, dir, out RaycastHit hit, _attackRange, _sightMask, QueryTriggerInteraction.Ignore)
                   || hit.transform.IsChildOf(Target);
        }

        private void TickChase()
        {
            if (Target == null) { SetState(EnemyState.Idle); return; }

            float distance = Vector3.Distance(transform.position, Target.position);

            bool canAttack = distance <= _attackRange
                             && Time.time >= _nextAttackTime
                             && HasLineOfSight();

            if (canAttack)
            {
                BeginAttack();
                return;
            }

            _agent.isStopped = false;

            // 保持在偏好距离：太近后退，太远前进
            Vector3 toTarget = (Target.position - transform.position).normalized;
            Vector3 destination = distance > _preferredDistance
                ? Target.position - toTarget * _preferredDistance
                : Target.position + toTarget * (_preferredDistance * 0.5f);

            _agent.SetDestination(destination);
            FaceTarget();
        }

        private void BeginAttack()
        {
            SetState(EnemyState.Telegraph);

            float duration = _isHeavyAttacker ? Mathf.Max(_telegraphSeconds, 0.8f) : _telegraphSeconds;
            _telegraph.Begin(duration, _isHeavyAttacker);

            // 前摇一开始就通知屏幕边缘提示系统（ID-009），
            // 让玩家在"还没看到弹幕"时就知道有威胁 —— 这是应对视野盲区的关键
            _threatIndicator?.RegisterThreat(transform, duration + 1.5f, _isHeavyAttacker);
        }

        private void HandleTelegraphCompleted()
        {
            if (_dead) return;

            SetState(EnemyState.Attack);

            if (_pattern != null && Target != null)
            {
                Vector3 origin = _muzzle != null ? _muzzle.position : transform.position;
                Vector3 dir = (Target.position - origin).normalized;

                if (_pattern.Shape == PatternShape.Burst)
                    StartCoroutine(BurstRoutine(origin, dir));
                else
                    _pattern.Fire(origin, dir, gameObject, Target);
            }

            _nextAttackTime = Time.time + _attackCooldown;
            SetState(EnemyState.Recover);
        }

        private IEnumerator BurstRoutine(Vector3 origin, Vector3 dir)
        {
            for (int i = 0; i < _pattern.ShotCountForBurst; i++)
            {
                if (_dead) yield break;

                if (Target != null)
                    dir = ((Target.position - origin).normalized);

                _pattern.Fire(origin, dir, gameObject, Target);
                yield return new WaitForSeconds(_pattern.BurstInterval);
            }
        }

        private void FaceTarget()
        {
            if (Target == null) return;

            Vector3 flat = Target.position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.001f) return;

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(flat),
                8f * Time.deltaTime);
        }

        private void HandleDeath(DamageInfo info)
        {
            if (_dead) return;
            _dead = true;

            SetState(EnemyState.Dead);
            _agent.isStopped = true;
            _telegraph.Cancel();

            // 死亡表现（炸成卡通碎片）由外部特效系统监听 OnStateChanged 处理
        }

        private void SetState(EnemyState next)
        {
            if (State == next) return;
            State = next;
            OnStateChanged?.Invoke(next);
        }

        public void BindThreatIndicator(ThreatIndicator indicator) => _threatIndicator = indicator;
    }
}
