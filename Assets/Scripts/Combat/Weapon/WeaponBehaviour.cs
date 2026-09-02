using UnityEngine;

namespace Fit.Combat.Weapon
{
    /// <summary>
    /// 开火时的上下文。传给各个 WeaponBehaviour 钩子。
    /// </summary>
    public struct WeaponFireContext
    {
        public WeaponData Data;
        public GameObject Owner;
        public Transform Muzzle;
        public Vector3 Origin;
        public Vector3 Direction;
        public int ShotIndexInBurst;
    }

    /// <summary>
    /// 命中时的上下文。
    /// </summary>
    public struct WeaponHitContext
    {
        public WeaponData Data;
        public GameObject Owner;
        public Vector3 HitPoint;
        public Vector3 HitNormal;
        public GameObject Target;
        public float DamageDealt;
    }

    /// <summary>
    /// 武器行为模块 —— 「脑洞武器系统」的扩展点。
    ///
    /// 设计意图：
    /// 如果把每种武器写成一个类，加一把"打中会分裂成三发追踪弹的散弹枪"
    /// 就要新写一个类，武器一多就爆炸。
    /// 改成行为组合后，新增武器 = 新建一个 WeaponData 资产，拖几个 Behaviour 上去，
    /// **不写一行代码**。这是支撑"脑洞"能持续产出的基础设施。
    ///
    /// 举例：
    ///   - 弹跳弹     → OnProjectileSpawned 挂 BounceOnImpact
    ///   - 爆炸弹     → OnHit 生成爆炸范围伤害
    ///   - 连锁闪电   → OnHit 寻找附近敌人继续跳
    ///   - 吸血       → OnHit 给 Owner 回血
    /// </summary>
    public abstract class WeaponBehaviour : ScriptableObject
    {
        public virtual void OnFire(in WeaponFireContext ctx) { }
        public virtual void OnHit(in WeaponHitContext ctx) { }
        public virtual void OnProjectileSpawned(Projectile projectile, in WeaponFireContext ctx) { }
    }
}
