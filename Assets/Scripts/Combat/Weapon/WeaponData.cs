using System;
using UnityEngine;

namespace Fit.Combat.Weapon
{
    public enum FireMode
    {
        /// <summary>即时命中（枪械）。射线判定，无飞行时间。</summary>
        Hitscan,
        /// <summary>投射物（弹幕、榴弹、魔法弹）。有飞行时间，可躲。</summary>
        Projectile,
    }

    public enum TriggerType
    {
        Auto,
        Semi,
        Burst,
    }

    /// <summary>
    /// 武器定义（ScriptableObject）。
    ///
    /// 【为什么武器要数据驱动】
    /// ID-005 的房间图里，宝箱房/商店房都要发武器，抽奖房要随机武器。
    /// 如果武器是硬编码的类，每加一把枪都要改代码 + 改 UI + 改掉落表。
    /// 数据驱动后，加武器 = 新建一个资产文件，其他全自动。
    ///
    /// 【为什么 Behaviours 是数组】
    /// 见 WeaponBehaviour 的说明 —— 组合优于继承，是"脑洞武器"能持续产出的关键。
    /// 一把枪可以同时有「爆炸 + 吸血 + 弹跳」，不用写新类。
    /// </summary>
    [CreateAssetMenu(menuName = "Fit/Weapon", fileName = "Weapon_New")]
    public sealed class WeaponData : ScriptableObject
    {
        [Header("身份")]
        public string Id;
        public string DisplayName;

        [Header("射击模式")]
        public FireMode Mode = FireMode.Hitscan;
        public TriggerType Trigger = TriggerType.Auto;
        [Tooltip("连发模式的每次扣扳机发射数")]
        public int BurstCount = 3;

        [Header("伤害")]
        public float Damage = 25f;
        [Tooltip("基础扩散角（度）。第一人称下放宽一点更舒服。")]
        public float SpreadDegrees = 1.5f;
        [Tooltip("Hitscan 最大射程")]
        public float Range = 120f;
        [Tooltip("一次射击发射几个弹丸（散弹）")]
        public int PelletsPerShot = 1;

        [Header("节奏")]
        [Tooltip("每秒发射数。0.5 = 两秒一发，10 = 一秒十发。")]
        public float FireRate = 6f;
        public int MagazineSize = 30;
        public float ReloadSeconds = 1.6f;

        [Header("后坐力（仅客户端视觉）")]
        [Tooltip("【重要】后坐力绝不同步到网络。同步会让手感立刻崩溃（见 §7 决策日志）。\n这里的值只用于本地准星上跳与屏幕震动。")]
        public float RecoilKick = 1.2f;
        public float RecoilRecoverSpeed = 8f;
        [Tooltip("屏幕震动强度")]
        public float ScreenShake = 0.15f;

        [Header("投射物（Mode = Projectile 时生效）")]
        public Projectile ProjectilePrefab;
        public float ProjectileSpeed = 26f;
        [Tooltip("投射物存在时间，超时自动回收，防止无限累积")]
        public float ProjectileLifetime = 5f;

        [Header("行为组合")]
        public WeaponBehaviour[] Behaviours = Array.Empty<WeaponBehaviour>();

        [Header("表现")]
        public AudioClip FireSound;
        public GameObject MuzzleFlashPrefab;
        [Tooltip("手持模型（第一人称手臂+枪）。Q2 决定：皮肤价值主要由它承载。")]
        public GameObject ViewModelPrefab;

        public float SecondsBetweenShots => FireRate > 0f ? 1f / FireRate : float.MaxValue;

        /// <summary>
        /// 轻校验。配表阶段的低级错误在这里拦掉，免得运行时才发现。
        /// </summary>
        public bool Validate(out string error)
        {
            if (string.IsNullOrEmpty(Id)) { error = "缺少 Id"; return false; }
            if (Mode == FireMode.Projectile && ProjectilePrefab == null)
            { error = $"{Id}: 投射物模式但没配 ProjectilePrefab"; return false; }
            if (MagazineSize <= 0) { error = $"{Id}: 弹匣容量必须大于 0"; return false; }
            error = null;
            return true;
        }
    }
}
