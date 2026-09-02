using UnityEngine;

namespace Fit.Combat
{
    /// <summary>
    /// 一次伤害的完整描述。
    ///
    /// 用结构体而不是散落的参数，是为了让"脑洞武器"能往里面塞额外信息 ——
    /// 比如连锁闪电需要知道上一跳是谁、爆炸需要知道爆心、击退需要方向。
    /// 新增武器效果时改这一个结构即可，不用改所有调用点。
    /// </summary>
    public struct DamageInfo
    {
        public float Amount;
        public GameObject Source;        // 谁开的枪（玩家或敌人）
        public GameObject Instigator;    // 最终责任人，用于归属击杀
        public Vector3 HitPoint;
        public Vector3 Direction;        // 伤害来向，用于击退与屏幕边缘提示
        public DamageFlags Flags;

        public bool IsCrit => (Flags & DamageFlags.Critical) != 0;
        public bool CanDown => (Flags & DamageFlags.NoDown) == 0;

        public static DamageInfo Create(float amount, GameObject source, Vector3 hitPoint, Vector3 direction)
            => new()
            {
                Amount = amount,
                Source = source,
                Instigator = source,
                HitPoint = hitPoint,
                Direction = direction,
                Flags = DamageFlags.None
            };
    }

    [System.Flags]
    public enum DamageFlags
    {
        None = 0,
        Critical = 1 << 0,
        /// <summary>不会导致倒地（例如环境轻微伤害、队友误伤）。</summary>
        NoDown = 1 << 1,
        /// <summary>无视无敌帧。仅用于必死场景，别乱用。</summary>
        IgnoreInvulnerable = 1 << 2,
    }

    /// <summary>
    /// 可被伤害的对象。玩家、敌人、可破坏物件统一实现这个接口，
    /// 这样武器不需要知道自己打的是什么 —— 这是"脑洞武器"能自由组合的前提。
    /// </summary>
    public interface IDamageable
    {
        bool IsAlive { get; }
        bool IsInvulnerable { get; }
        /// <returns>实际造成的伤害量（可能被护甲/无敌帧削减，返回 0 表示完全免疫）。</returns>
        float ApplyDamage(DamageInfo info);
    }
}
