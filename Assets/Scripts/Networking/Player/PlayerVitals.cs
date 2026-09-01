using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Fit.Networking.Sync;
using UnityEngine;

namespace Fit.Networking.Player
{
    /// <summary>
    /// 玩家生命与状态。
    ///
    /// How to Fish 把 PlayerVitals / PlayerDying / DeadPlayer / PlayerEating 拆成了
    /// 四个独立网络对象，并且有 FireTick / PoisonTick / LowerFullnessTick 三个 Tick 计数。
    ///
    /// 这里合并成一处，但保留了同样的 Tick 驱动思路：
    /// 持续伤害（灼烧/中毒）不用每帧扣血，而是按 Tick 结算 ——
    /// 既省带宽（血量同步频率可以降低），也让"燃烧剩余时间"这类状态天然可序列化。
    /// </summary>
    public sealed class PlayerVitals : NetworkEntity
    {
        [Header("配置")]
        [SerializeField] private float _maxHealth = 100f;
        [SerializeField] private float _maxFullness = 100f;
        [SerializeField] private float _tickInterval = 1f;
        [SerializeField] private float _healthRegenPerTick = 0.5f;
        [SerializeField] private float _fullnessDrainPerTick = 0.15f;

        /// <summary>血量。SyncVar 带阈值，避免每帧微小变化刷屏。</summary>
        [SyncVar(OnChange = nameof(OnHealthChanged))]
        private float _health;

        /// <summary>饱食度。</summary>
        [SyncVar]
        private float _fullness;

        /// <summary>灼烧剩余 Tick 数 —— 比"剩余秒数"更容易跨客户端对齐。</summary>
        [SyncVar(OnChange = nameof(OnFireTicksChanged))]
        private int _fireTicks;

        /// <summary>中毒剩余 Tick 数。</summary>
        [SyncVar]
        private int _poisonTicks;

        [SyncVar(OnChange = nameof(OnAliveChanged))]
        private bool _alive = true;

        public event Action<float> HealthChanged;
        public event Action<bool> AliveChanged;

        public float Health => _health;
        public float Fullness => _fullness;
        public bool IsAlive => _alive;
        public bool IsBurning => _fireTicks > 0;
        public bool IsPoisoned => _poisonTicks > 0;

        private float _nextTickTime;

        public override void OnStartServer()
        {
            base.OnStartServer();
            _health = _maxHealth;
            _fullness = _maxFullness;
            _alive = true;
            _nextTickTime = Time.time + _tickInterval;
        }

        private void Update()
        {
            if (!base.IsServerStarted || !_alive)
                return;

            if (Time.time < _nextTickTime)
                return;

            _nextTickTime = Time.time + _tickInterval;
            Tick();
        }

        /// <summary>一次状态结算。所有持续效果在这里统一处理。</summary>
        [Server]
        private void Tick()
        {
            float delta = 0f;

            if (_fireTicks > 0)
            {
                _fireTicks--;
                delta -= FireDamagePerTick;
            }

            if (_poisonTicks > 0)
            {
                _poisonTicks--;
                delta -= PoisonDamagePerTick;
            }

            // 饥饿掉血 / 饱腹回血
            _fullness = Mathf.Clamp(_fullness - _fullnessDrainPerTick, 0f, _maxFullness);
            delta += _fullness > 0f ? _healthRegenPerTick : -_healthRegenPerTick;

            ApplyHealthDelta(delta);
        }

        [Server]
        public void ApplyHealing(float amount) => ApplyHealthDelta(Mathf.Abs(amount));

        [ServerRpc(RequireOwnership = false)]
        public void ApplyDamage(float amount)
        {
            if (!_alive || amount <= 0f) return;
            ApplyHealthDelta(-amount);
        }

        [ServerRpc(RequireOwnership = false)]
        public void ApplyFire(int ticks)
        {
            // 取较大值而不是累加，避免多人同时点火导致瞬间秒杀
            _fireTicks = Mathf.Max(_fireTicks, ticks);
        }

        [ServerRpc(RequireOwnership = false)]
        public void ApplyPoison(int ticks)
        {
            _poisonTicks = Mathf.Max(_poisonTicks, ticks);
        }

        [Server]
        public void Revive(float healthRatio = 1f)
        {
            _health = _maxHealth * Mathf.Clamp01(healthRatio);
            _fullness = _maxFullness * 0.5f;
            _fireTicks = 0;
            _poisonTicks = 0;
            _alive = true;
        }

        [Server]
        private void ApplyHealthDelta(float delta)
        {
            float next = Mathf.Clamp(_health + delta, 0f, _maxHealth);
            if (Mathf.Approximately(next, _health)) return;

            _health = next;
            if (_health <= 0f)
                _alive = false;
        }

        private const float FireDamagePerTick = 4f;
        private const float PoisonDamagePerTick = 2f;

        private void OnHealthChanged(float prev, float next, bool asServer)
        {
            HealthChanged?.Invoke(next);
        }

        private void OnAliveChanged(bool prev, bool next, bool asServer)
        {
            if (!next)
            {
                _fireTicks = 0;
                _poisonTicks = 0;
            }
            AliveChanged?.Invoke(next);
        }

        private void OnFireTicksChanged(int prev, int next, bool asServer)
        {
            // 在这里挂 VFX：点燃/熄灭。How to Fish 的 VFX Graph 里有 Fire 相关的图
        }
    }
}
