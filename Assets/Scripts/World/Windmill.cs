using UnityEngine;

namespace Fit.World
{
    /// <summary>
    /// 风车叶片旋转。
    ///
    /// 【为什么值得为它单独写个脚本】
    /// 主地图的价值是"停留舒适"（SCENE_FARM.md §2）。一个静止的风车会让整个院子显得像张贴图，
    /// 而转动的叶片是低成本、高回报的环境动效 —— 三行代码的成本，但远看就知道场景是活的。
    ///
    /// 只做纯客户端表现：它不影响任何判定，联机时各客户端各转各的，
    /// 即使转速略有偏差也没人会在意（真要同步反而浪费带宽）。
    /// </summary>
    public sealed class Windmill : MonoBehaviour
    {
        [Header("旋转")]
        [SerializeField, Tooltip("转速（转/分钟）")] private float _rpm = 9f;
        [SerializeField, Tooltip("旋转轴（局部空间）")] private Vector3 _axis = Vector3.forward;

        [Header("随机化")]
        [SerializeField, Tooltip("初始角度随机，避免多个风车整齐划一")]
        private bool _randomizePhase = true;

        private void Awake()
        {
            if (_randomizePhase)
                transform.Rotate(_axis, Random.Range(0f, 360f), Space.Self);
        }

        /// <summary>设置转速（转/分钟）。供编辑器生成器链式调用。</summary>
        public Windmill SetRpm(float rpm)
        {
            _rpm = rpm;
            return this;
        }

        private void Update()
        {
            // rpm → 度/秒：转/分钟 × 360° ÷ 60 秒 = rpm × 6
            transform.Rotate(_axis, _rpm * 6f * Time.deltaTime, Space.Self);
        }
    }
}
