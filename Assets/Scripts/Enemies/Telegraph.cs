using System;
using UnityEngine;

namespace Fit.Enemies
{
    /// <summary>
    /// 攻击前摇（Telegraph）—— ID-008 的实现，也是 ID-007 预警光效的驱动源。
    ///
    /// 【为什么前摇是这类游戏的生命线】
    /// §5.1 冲突一：第一人称 FOV 只有 90-110°，背后 270° 看不见。
    /// 玩家能躲开弹幕的唯一依据是"提前知道要来了"。
    /// 没有前摇，弹幕就退化成"随机挨打"，无论数值怎么调都不好玩。
    ///
    /// 【0.3-0.5 秒是经验值】
    /// 短于 0.25s：人眼来不及反应，等于没有前摇。
    /// 长于 0.6s：节奏拖沓，且玩家会养成"看到光才开始动"的坏习惯，
    ///           导致移动节奏被打断，手感变粘。
    /// Boss 的大招可以放宽到 0.8-1.2s，因为那本来就该有仪式感。
    ///
    /// 【这个组件不负责发射】
    /// 它只负责"预告"，结束时回调，由 EnemyBase 决定发什么。
    /// 这样同一个前摇能挂在不同攻击上，且动画师可以独立调时长。
    /// </summary>
    public sealed class Telegraph : MonoBehaviour
    {
        [Header("配置")]
        [SerializeField, Tooltip("前摇时长。建议 0.3-0.5 秒，Boss 大招可到 1.2 秒。")]
        private float _duration = 0.4f;

        [Header("充能光效")]
        [SerializeField] private Renderer _chargeRenderer;
        [SerializeField] private Color _chargeColor = new(1f, 0.35f, 0.1f, 1f);
        [SerializeField, Range(1f, 10f)] private float _chargeIntensity = 4f;
        [SerializeField] private Light _chargeLight;
        [SerializeField] private float _lightMaxIntensity = 2.5f;
        [SerializeField] private AnimationCurve _ramp = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("缩放提示")]
        [SerializeField] private bool _pulseScale = true;
        [SerializeField, Range(1f, 1.6f)] private float _maxScale = 1.15f;

        /// <summary>前摇开始。参数：本次时长、是否为大招（影响警示强度）。</summary>
        public event Action<float, bool> OnTelegraphStarted;
        /// <summary>前摇结束，可以发射了。</summary>
        public event Action OnTelegraphCompleted;

        public bool IsActive { get; private set; }
        public float Progress { get; private set; }
        /// <summary>本次是否为重攻击 —— 影响屏幕边缘提示的强度。</summary>
        public bool IsHeavy { get; private set; }

        private float _endTime;
        private float _startTime;
        private float _activeDuration;
        private Vector3 _baseScale;
        private MaterialPropertyBlock _block;

        private void Awake()
        {
            _baseScale = transform.localScale;
            _block = new MaterialPropertyBlock();
        }

        /// <summary>
        /// 开始一次前摇。
        /// </summary>
        /// <param name="duration">覆盖默认时长，传 0 用默认值</param>
        /// <param name="heavy">重攻击，提示更强</param>
        public void Begin(float duration = 0f, bool heavy = false)
        {
            _activeDuration = duration > 0f ? duration : _duration;
            _startTime = Time.time;
            _endTime = Time.time + _activeDuration;
            IsActive = true;
            IsHeavy = heavy;
            Progress = 0f;

            OnTelegraphStarted?.Invoke(_activeDuration, heavy);
        }

        public void Cancel()
        {
            IsActive = false;
            Progress = 0f;
            ApplyVisuals(0f);
        }

        private void Update()
        {
            if (!IsActive) return;

            Progress = Mathf.Clamp01((Time.time - _startTime) / Mathf.Max(0.01f, _activeDuration));
            ApplyVisuals(Progress);

            if (Time.time < _endTime) return;

            IsActive = false;
            ApplyVisuals(0f);
            OnTelegraphCompleted?.Invoke();
        }

        /// <summary>
        /// 把充能进度表现到渲染器与灯光上。
        /// 用 MaterialPropertyBlock 避免材质实例化，同屏几十个敌人也不会炸内存。
        /// </summary>
        private void ApplyVisuals(float t)
        {
            float curved = _ramp.Evaluate(t);

            if (_chargeRenderer != null)
            {
                _chargeRenderer.GetPropertyBlock(_block);
                _block.SetColor(EmissionId, _chargeColor * (_chargeIntensity * curved));
                _chargeRenderer.SetPropertyBlock(_block);
            }

            if (_chargeLight != null)
                _chargeLight.intensity = _lightMaxIntensity * curved;

            if (_pulseScale)
                transform.localScale = _baseScale * Mathf.Lerp(1f, _maxScale, curved);
        }

        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    }
}
