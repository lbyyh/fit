using Fit.Combat;
using UnityEngine;

namespace Fit.Enemies
{
    public enum PatternShape
    {
        /// <summary>单发直射</summary>
        Single,
        /// <summary>扇形散射</summary>
        Spread,
        /// <summary>环形（全向）</summary>
        Ring,
        /// <summary>连发（有间隔，会连续前摇）</summary>
        Burst,
    }

    /// <summary>
    /// 弹幕发射模式（ScriptableObject）。
    ///
    /// 【§5.1 冲突一的直接影响】
    /// 这里刻意**没有**提供"满屏弹幕雨"这类高密度模式。
    /// 第一人称下 FOV 只有 90-110°，满屏弹幕 = 莫名其妙地死。
    /// 所以模式设计的核心是"可预判"：
    ///   - 数量克制（Spread 默认 3-5 发，Ring 默认 8 发）
    ///   - 形状规则（扇形/环形/螺旋，玩家能读出规律）
    ///   - 弹速偏慢（默认 18，见下方速度说明）
    ///
    /// 【弹速为什么默认这么慢】
    /// 延迟 100ms 时，弹速 40 意味着玩家看到的位置与实际位置差 4 米 ——
    /// 这会让"我明明躲开了却被判中"频繁发生。
    /// 弹速降到 18，100ms 误差只有 1.8 米，配合碰撞宽容度就基本无感了。
    /// 想做快速弹，必须同时加大弹体半径，否则可读性崩坏。
    ///
    /// 【做成 ScriptableObject 的理由】
    /// 和武器一样：新增敌人攻击方式 = 新建资产，不改代码。
    /// 内容量是这个游戏的主要工作量（§3.2），能省一处是一处。
    /// </summary>
    [CreateAssetMenu(menuName = "Fit/Bullet Pattern", fileName = "Pattern_New")]
    public sealed class BulletPattern : ScriptableObject
    {
        [Header("形状")]
        public PatternShape Shape = PatternShape.Spread;
        [Tooltip("弹丸数量。克制使用 —— 超过 8 发在第一人称下基本读不出来。")]
        [Range(1, 16)] public int Count = 3;
        [Tooltip("扇形总张角（度）")]
        [Range(0f, 360f)] public float ArcDegrees = 45f;

        [Header("弹丸参数")]
        public Projectile ProjectilePrefab;
        [Tooltip("弹速。建议 14-24。见类说明里的延迟/可读性权衡。")]
        public float Speed = 18f;
        public float Damage = 12f;
        public float Lifetime = 5f;

        [Header("连发（Shape = Burst）")]
        [Tooltip("连发次数")]
        [Range(1, 10)] public int BurstCount = 3;
        [Tooltip("连发间隔")]
        public float BurstInterval = 0.18f;

        [Header("瞄准")]
        [Tooltip("是否朝目标瞄准。关闭则为固定方向（用于陷阱/固定炮台）。")]
        public bool AimAtTarget = true;
        [Tooltip("瞄准时的随机偏差（度）。给一点偏差，避免绝对精准导致的挫败。")]
        [Range(0f, 15f)] public float AimJitterDegrees = 2f;

        /// <summary>
        /// 发射一轮。由 EnemyBase 在前摇结束后调用。
        /// </summary>
        /// <param name="origin">发射点</param>
        /// <param name="baseDirection">基础朝向（通常指向玩家）</param>
        /// <param name="owner">发射者</param>
        /// <param name="target">瞄准目标，null 则用 baseDirection</param>
        public void Fire(Vector3 origin, Vector3 baseDirection, GameObject owner, Transform target = null)
        {
            if (ProjectilePrefab == null)
            {
                Debug.LogWarning($"[BulletPattern] {name} 未配置 ProjectilePrefab", this);
                return;
            }

            Vector3 forward = baseDirection.normalized;

            if (AimAtTarget && target != null)
                forward = (target.position - origin).normalized;

            if (AimJitterDegrees > 0f)
                forward = Jitter(forward, AimJitterDegrees);

            switch (Shape)
            {
                case PatternShape.Single:
                    Spawn(origin, forward, owner);
                    break;

                case PatternShape.Spread:
                    FireSpread(origin, forward, owner);
                    break;

                case PatternShape.Ring:
                    FireRing(origin, forward, owner);
                    break;

                case PatternShape.Burst:
                    // 连发交给调用方用协程驱动，这里只发一发
                    Spawn(origin, forward, owner);
                    break;
            }
        }

        private void FireSpread(Vector3 origin, Vector3 forward, GameObject owner)
        {
            if (Count <= 1) { Spawn(origin, forward, owner); return; }

            float half = ArcDegrees * 0.5f;
            float step = ArcDegrees / (Count - 1);

            for (int i = 0; i < Count; i++)
            {
                float angle = -half + step * i;
                Spawn(origin, RotateAroundUp(forward, angle), owner);
            }
        }

        private void FireRing(Vector3 origin, Vector3 forward, GameObject owner)
        {
            float step = 360f / Mathf.Max(1, Count);
            for (int i = 0; i < Count; i++)
                Spawn(origin, RotateAroundUp(forward, step * i), owner);
        }

        private void Spawn(Vector3 origin, Vector3 direction, GameObject owner)
        {
            Projectile p = Object.Instantiate(ProjectilePrefab, origin, Quaternion.LookRotation(direction));
            p.Initialize(origin, direction, Speed, Damage, Lifetime, owner);
        }

        private static Vector3 RotateAroundUp(Vector3 dir, float degrees)
            => Quaternion.AngleAxis(degrees, Vector3.up) * dir;

        private static Vector3 Jitter(Vector3 dir, float degrees)
            => Quaternion.AngleAxis(Random.Range(-degrees, degrees), Vector3.up) *
               Quaternion.AngleAxis(Random.Range(-degrees * 0.4f, degrees * 0.4f), Vector3.right) * dir;

        public int ShotCountForBurst => Shape == PatternShape.Burst ? BurstCount : 1;

        /// <summary>估算同屏弹幕密度，用于难度配平时的自检。</summary>
        public float DensityScore => Shape switch
        {
            PatternShape.Single => 1f,
            PatternShape.Spread => Count * 0.6f,
            PatternShape.Ring => Count * 0.8f,
            PatternShape.Burst => BurstCount * 0.7f,
            _ => 1f
        };
    }
}
