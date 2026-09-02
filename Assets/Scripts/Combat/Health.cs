using System;
using UnityEngine;

namespace Fit.Combat
{
    /// <summary>
    /// 血量。玩家、敌人、可破坏物共用。
    ///
    /// 【服务器权威边界】
    /// ApplyDamage 是唯一的扣血入口，且**只能在权威端调用**。
    /// 联机阶段（阶段 3）会改成：客户端只发"我打中了谁"，由服务器调用本方法。
    /// 现在离线跑时，调用方自己就是权威端，逻辑一致，接入时不需要重写。
    ///
    /// 【无敌帧为什么放在这里】
    /// 翻滚闪避（ID-010）的无敌帧如果放在 PlayerController 里，
    /// 敌人就必须知道玩家有没有翻滚 —— 耦合就错了。
    /// 放在这里，任何伤害来源都不需要关心目标的状态细节，只管问 ApplyDamage。
    ///
    /// 【没有做护甲/减伤系统】
    /// 阶段 1 不需要。真要加，在 ApplyDamage 里插一个 IDamageModifier 列表即可，
    /// 不改接口。
    /// </summary>
    public sealed class Health : MonoBehaviour, IDamageable
    {
        [Header("配置")]
        [SerializeField] private float _maxHealth = 100f;
        [SerializeField] private bool _invulnerableByDefault;

        [Header("受击后无敌")]
        [SerializeField] private float _hitInvulnerableSeconds = 0.25f;

        public float MaxHealth => _maxHealth;
        public float CurrentHealth { get; private set; }
        public float Ratio => _maxHealth > 0f ? CurrentHealth / _maxHealth : 0f;

        public bool IsAlive => CurrentHealth > 0f;
        public bool IsInvulnerable => _invulnerableByDefault || Time.time < _invulnerableUntil;

        /// <summary>血量变化。参数：当前值 / 变化量（负数为扣血）/ 伤害来源。</summary>
        public event Action<float, float, GameObject> OnHealthChanged;
        public event Action<DamageInfo> OnDamaged;
        public event Action<DamageInfo> OnDepleted;

        private float _invulnerableUntil;

        private void Awake() => CurrentHealth = _maxHealth;

        public void SetMaxHealth(float value, bool refill = false)
        {
            _maxHealth = Mathf.Max(1f, value);
            if (refill) CurrentHealth = _maxHealth;
            else CurrentHealth = Mathf.Min(CurrentHealth, _maxHealth);
        }

        public float ApplyDamage(DamageInfo info)
        {
            if (!IsAlive) return 0f;

            // 无敌帧判定：IgnoreInvulnerable 用于必死场景（掉出地图、剧情杀）
            if (IsInvulnerable && (info.Flags & DamageFlags.IgnoreInvulnerable) == 0)
                return 0f;

            float applied = Mathf.Min(CurrentHealth, Mathf.Max(0f, info.Amount));
            CurrentHealth -= applied;

            // 受击后短暂无敌：防止密集弹幕在一帧内多次判定导致瞬间秒杀。
            // 这是弹幕游戏的刚需 —— 没有它，站进弹幕里会一秒暴毙，体感极差。
            if (_hitInvulnerableSeconds > 0f)
                _invulnerableUntil = Mathf.Max(_invulnerableUntil, Time.time + _hitInvulnerableSeconds);

            OnHealthChanged?.Invoke(CurrentHealth, -applied, info.Instigator);
            OnDamaged?.Invoke(info);

            if (CurrentHealth <= 0f)
                OnDepleted?.Invoke(info);

            return applied;
        }

        /// <summary>
        /// 进入无敌状态。翻滚闪避（ID-010）调用这里。
        /// 联机阶段必须改成服务器权威，否则客户端改内存就能永久无敌。
        /// </summary>
        public void SetInvulnerable(float seconds)
            => _invulnerableUntil = Mathf.Max(_invulnerableUntil, Time.time + seconds);

        public void Heal(float amount, GameObject source = null)
        {
            if (!IsAlive || amount <= 0f) return;

            float healed = Mathf.Min(amount, _maxHealth - CurrentHealth);
            if (healed <= 0f) return;

            CurrentHealth += healed;
            OnHealthChanged?.Invoke(CurrentHealth, healed, source);
        }

        /// <summary>复活并回满。用于被队友扶起（Q4 倒地可救）。</summary>
        public void Revive(float ratio = 0.5f)
        {
            CurrentHealth = Mathf.Max(1f, _maxHealth * Mathf.Clamp01(ratio));
            SetInvulnerable(2f);   // 扶起来给 2 秒无敌，避免刚起身又被同一波弹幕打死
            OnHealthChanged?.Invoke(CurrentHealth, CurrentHealth, null);
        }
    }
}
