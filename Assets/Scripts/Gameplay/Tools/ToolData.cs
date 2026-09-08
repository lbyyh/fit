using System;
using UnityEngine;

namespace Fit.Gameplay.Tools
{
    public enum ToolKind
    {
        /// <summary>锄头：把生地锄成耕地。</summary>
        Hoe,
        /// <summary>种子：在耕地上播种。</summary>
        Seed,
        /// <summary>水壶：给作物浇水加速成长。</summary>
        WateringCan,
    }

    /// <summary>
    /// 工具定义（ScriptableObject）。
    ///
    /// 【为什么不做成 WeaponData 的一个 FireMode】
    /// 见 IHoldable 的说明：武器与工具的行为模型差别太大，
    /// 塞一起会让两边都别扭。这里只描述工具关心的字段 ——
    /// 没有伤害、没有弹匣、没有后坐力。
    ///
    /// 【为什么 Kind 用枚举而不是继承】
    /// 和 WeaponBehaviour 一样的理由：组合优于继承。
    /// 工具的行为差异只有"作用到地面时干什么"这一处，
    /// 在 ToolBase 里一个 switch 就够了，不需要每种工具一个类。
    /// </summary>
    [CreateAssetMenu(menuName = "Fit/Tool", fileName = "Tool_New")]
    public sealed class ToolData : ScriptableObject
    {
        [Header("身份")]
        public string Id;
        public string DisplayName;
        public ToolKind Kind = ToolKind.Hoe;
        public Sprite Icon;

        [Header("使用")]
        [Tooltip("使用间隔（秒）。防止连点瞬间锄完一整畦。\n" +
                 "0.35 是手感参考：比挥锄头的动作略长，和扳机节奏接近。")]
        public float UseCooldown = 0.35f;

        [Tooltip("有效距离。农具都是贴地作业，4 米左右够用。")]
        public float Range = 4f;

        [Tooltip("能作用的层。一般只用地面层，避免隔着蔬菜锄到远处的地。")]
        public LayerMask TargetMask = ~0;

        [Header("种子专用（Kind = Seed）")]
        [Tooltip("种下去长什么。留空则这袋种子什么也种不出来。")]
        public CropDef Crop;

        [Header("表现")]
        [Tooltip("手持模型（第一人称手臂+农具）。Q2：皮肤价值主要由手持物承载。")]
        public GameObject ViewModelPrefab;
        public AudioClip UseSound;

        public bool Validate(out string error)
        {
            if (string.IsNullOrEmpty(Id)) { error = "缺少 Id"; return false; }
            if (Kind == ToolKind.Seed && Crop == null)
            { error = $"{Id}: 种子但没配 Crop"; return false; }
            error = null;
            return true;
        }
    }

    /// <summary>
    /// 作物定义。种子指向它，决定种下去会长成什么。
    ///
    /// 【⚠️ 为什么成熟后是长出小怪，而不是收获资源】
    /// H2 已拍板「主地图蔬菜不掉落资源」，SCENE_FARM §4 也警告过：
    /// 主地图一旦能稳定产出资源，玩家就会赖在这里种田而不去闯关，
    /// 核心循环（§2）直接被架空。
    ///
    /// 所以这里刻意**不产出任何资源**。成熟后按 MatureEnemyPrefab 长出一只
    /// 蔬菜小怪 —— 玩家自己种、自己打，形成一个纯整活的闭环：
    ///   锄地 → 播种 → 长出怪 → 打掉 → 再锄
    /// 既让等队友时有事干（ID-004），又不产出任何可刷的东西。
    ///
    /// 将来真要做农场经营向的产出，那是另一个玩法循环，
    /// 必须重新过一遍 §4 的收益评估，不能在这里顺手加。
    /// </summary>
    [Serializable]
    public sealed class CropDef
    {
        [Header("成长")]
        [Tooltip("各阶段的模型，索引 0 是幼苗。数量决定阶段数。")]
        public GameObject[] StagePrefabs = Array.Empty<GameObject>();

        [Tooltip("每个阶段持续多久（秒）。长度应与 StagePrefabs 一致，短了就按最后一项补。")]
        public float[] StageSeconds = { 12f, 12f, 12f };

        [Header("成熟")]
        [Tooltip("成熟后生成的敌人。留空 = 成熟后只是个摆设（纯装饰）。")]
        public GameObject MatureEnemyPrefab;

        [Tooltip("浇水后成长速度倍率。水壶的价值就在这。")]
        [Range(1f, 4f)] public float WateredGrowthMultiplier = 2f;

        [Tooltip("浇水效果持续多久（秒）。")]
        public float WateredDuration = 30f;

        public int StageCount => StagePrefabs != null ? StagePrefabs.Length : 0;

        public float SecondsForStage(int stage)
        {
            if (StageSeconds == null || StageSeconds.Length == 0) return 10f;
            return StageSeconds[Mathf.Clamp(stage, 0, StageSeconds.Length - 1)];
        }

        public bool SpawnsEnemyOnMature => MatureEnemyPrefab != null;
    }
}
