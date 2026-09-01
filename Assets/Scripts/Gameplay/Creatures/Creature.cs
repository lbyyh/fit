using FishNet.Object;
using Fit.Networking;
using UnityEngine;
using UnityEngine.AI;

namespace Fit.Gameplay.Creatures
{
    /// <summary>
    /// 生物实体基类。
    ///
    /// How to Fish 的生物体系是一棵继承树：
    ///   Creature（基类）→ Fish / AttackingFish / RunningFish
    ///                   → Bird / Albatross
    ///                   → Crab / CrabArms / Spidercrab
    ///                   → Piranha / Pufferfish / BowheadWhale
    /// 再配 CreatureManager 统管生成与回收，BossManager 单独管 Boss。
    ///
    /// 这里只给出基类骨架 + 一个 Fish 示例，其余按同样的模式扩展。
    ///
    /// 关键设计决策：AI 只在服务器跑。
    /// NavMesh 寻路的结果通过位置同步下发，客户端不跑 AI。
    /// 这样既省客户端 CPU，也避免客户端与服务器的 AI 判定打架。
    /// </summary>
    public abstract class Creature : NetworkEntity
    {
        [Header("配置")]
        [SerializeField] protected float MaxHealth = 50f;
        [SerializeField] protected float MoveSpeed = 2.5f;
        [SerializeField] protected float FleeSpeedMultiplier = 1.8f;
        [SerializeField] protected float DetectRadius = 15f;
        [SerializeField] protected bool Hostile;

        [Header("组件")]
        [SerializeField] protected NavMeshAgent Agent;

        [SyncVar]
        protected float Health;

        [SyncVar]
        protected CreatureState State;

        protected enum CreatureState
        {
            Idle = 0,
            Roaming = 1,
            Fleeing = 2,
            Chasing = 3,
            Attacking = 4,
            Dead = 5
        }

        public bool IsDead => State == CreatureState.Dead;
        public bool IsHostile => Hostile;

        public override void OnStartServer()
        {
            base.OnStartServer();
            Health = MaxHealth;
            State = CreatureState.Idle;
        }

        private void Update()
        {
            if (!base.IsServerStarted || IsDead) return;
            TickAi();
        }

        /// <summary>子类实现具体行为。只在服务器调用。</summary>
        protected abstract void TickAi();

        [Server]
        public virtual void ApplyDamage(float amount)
        {
            if (IsDead || amount <= 0f) return;

            Health -= amount;

            if (Health <= 0f)
                Die();
            else
                OnDamaged();
        }

        [Server]
        protected virtual void Die()
        {
            State = CreatureState.Dead;
            if (Agent != null)
                Agent.isStopped = true;

            // 掉落物生成交给 ItemManager，避免生物类依赖物品系统
            OnDeath();

            // 延迟回收：给死亡动画/特效留出时间
            StartCoroutine(DespawnAfterDelay(3f));
        }

        protected virtual void OnDamaged() { }
        protected virtual void OnDeath() { }

        private System.Collections.IEnumerator DespawnAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (IsServerStarted)
                base.Despawn();
        }

        [Server]
        protected void MoveTo(Vector3 destination, bool fleeing = false)
        {
            if (Agent == null || !Agent.isOnNavMesh) return;

            Agent.speed = MoveSpeed * (fleeing ? FleeSpeedMultiplier : 1f);
            Agent.SetDestination(destination);
        }
    }

    /// <summary>
    /// 普通鱼：被靠近就逃跑，被抓住就掉血。
    /// 这是 How to Fish 里 RunningFish / AttackingFish 的简化对照。
    /// </summary>
    public sealed class Fish : Creature
    {
        [SerializeField] private float _roamRadius = 20f;

        private Vector3 _homePosition;
        private float _nextRoamTime;

        private const float RoamInterval = 4f;

        public override void OnStartServer()
        {
            base.OnStartServer();
            _homePosition = transform.position;
            _nextRoamTime = Time.time + Random.Range(0f, RoamInterval);
        }

        protected override void TickAi()
        {
            var threat = FindNearestPlayer();

            if (threat != null)
            {
                // 朝反方向逃
                Vector3 away = transform.position + (transform.position - threat.position).normalized * _roamRadius;
                MoveTo(away, fleeing: true);

                if (State != CreatureState.Fleeing)
                    State = CreatureState.Fleeing;
                return;
            }

            if (Time.time >= _nextRoamTime)
            {
                _nextRoamTime = Time.time + RoamInterval + Random.Range(-1f, 1f);

                var offset = Random.insideUnitSphere * _roamRadius;
                offset.y = 0f;
                MoveTo(_homePosition + offset);

                State = CreatureState.Roaming;
            }
        }

        private Transform FindNearestPlayer()
        {
            // 实际项目应维护一个服务器端的玩家列表，避免每帧 FindObjectsOfType
            var players = Fit.Gameplay.CreatureRegistry.ActivePlayers;
            Transform nearest = null;
            float nearestDistance = DetectRadius;

            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null) continue;

                float d = Vector3.Distance(transform.position, p.position);
                if (d < nearestDistance)
                {
                    nearestDistance = d;
                    nearest = p;
                }
            }

            return nearest;
        }
    }
}
