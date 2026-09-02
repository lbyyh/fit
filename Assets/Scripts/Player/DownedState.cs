using System;
using Fit.Combat;
using UnityEngine;

namespace Fit.Player
{
    /// <summary>
    /// 倒地与救援 —— Q4 决策的实现。
    ///
    /// 【为什么选"倒地可救"】
    /// 纯 roguelike 的"死了从头来"与"轻松欢乐"基调冲突；
    /// 但完全没有惩罚又会让弹幕失去紧张感。
    /// 倒地可救是两者之间最好的平衡：
    ///   - 个人失误不致命（不会因为一次走位失误毁掉整局）
    ///   - 全队倒地才重开（保住 roguelike 的压迫感）
    ///   - **倒地待救本身就是整活素材**：队友为了救你反而团灭，是最经典的欢乐时刻
    ///
    /// 【服务器权威边界】
    /// 倒地状态、救援进度必须权威端判定。
    /// 客户端改内存把自己改成"未倒地"是没用的 —— 血量在服务器，
    /// 血量为 0 时服务器会强制进入倒地。
    ///
    /// 【呼救与语音联动（ID-004 整活向）】
    /// 倒地时可以"喊"。因为语音链路是自研的，能拿到麦克风能量值，
    /// 可以直接把音量映射成呼救提示的强度 —— 喊得越响，队友屏幕上的指示越晃。
    /// 这是商业语音方案（Vivox 等）很难做到的独门机制。
    /// </summary>
    public sealed class DownedState : MonoBehaviour
    {
        [Header("配置")]
        [Tooltip("倒地后可支撑的时间。超时真正死亡。")]
        [SerializeField] private float _downedDuration = 25f;
        [Tooltip("救援读条时长")]
        [SerializeField] private float _reviveSeconds = 3f;
        [Tooltip("救援半径")]
        [SerializeField] private float _reviveRadius = 2.5f;
        [Tooltip("扶起后恢复的血量比例")]
        [SerializeField, Range(0.1f, 1f)] private float _reviveHealthRatio = 0.5f;
        [Tooltip("倒地移动速度（很慢，只能爬）")]
        [SerializeField] private float _crawlSpeed = 1.4f;

        [Header("呼救")]
        [SerializeField] private float _callForHelpInterval = 2.5f;

        public bool IsDowned { get; private set; }
        public float RemainingSeconds { get; private set; }
        public float ReviveProgress { get; private set; }
        public bool BeingRevived => ReviveProgress > 0f;

        /// <summary>倒地 / 被救起 / 真正死亡。参数为倒地者。</summary>
        public event Action<DownedState> OnDowned;
        public event Action<DownedState> OnRevived;
        public event Action<DownedState> OnBleedOut;
        /// <summary>呼救。参数为倒地者与世界坐标 —— UI 与语音系统监听。</summary>
        public event Action<DownedState, Vector3> OnCalledForHelp;

        private Health _health;
        private FPSController _controller;
        private float _bleedOutTime;
        private float _nextCallTime;
        private float _reviveAccumulator;

        private void Awake()
        {
            _health = GetComponent<Health>();
            _controller = GetComponent<FPSController>();
        }

        private void OnEnable()
        {
            if (_health != null)
                _health.OnDepleted += HandleDepleted;
        }

        private void OnDisable()
        {
            if (_health != null)
                _health.OnDepleted -= HandleDepleted;
        }

        private void HandleDepleted(DamageInfo info)
        {
            // 带 NoDown 标记的伤害不会导致倒地（弹幕默认带此标记，见 Projectile）
            if (!info.CanDown) return;
            if (IsDowned) return;

            EnterDowned();
        }

        public void EnterDowned()
        {
            if (IsDowned) return;

            IsDowned = true;
            RemainingSeconds = _downedDuration;
            _bleedOutTime = Time.time + _downedDuration;
            _reviveAccumulator = 0f;
            ReviveProgress = 0f;

            if (_controller != null) _controller.enabled = false;
            OnDowned?.Invoke(this);
        }

        private void Update()
        {
            if (!IsDowned) return;

            RemainingSeconds = Mathf.Max(0f, _bleedOutTime - Time.time);

            TickCrawl();
            TickCallForHelp();

            if (RemainingSeconds <= 0f)
                BleedOut();
        }

        /// <summary>倒地后还能缓慢爬行，保留一点自救空间与喜剧效果。</summary>
        private void TickCrawl()
        {
            var cc = GetComponent<CharacterController>();
            if (cc == null) return;

            float h = UnityEngine.Input.GetAxisRaw("Horizontal");
            float v = UnityEngine.Input.GetAxisRaw("Vertical");
            Vector3 wish = (transform.right * h + transform.forward * v).normalized;
            if (wish.sqrMagnitude > 0.01f)
                cc.Move(wish * (_crawlSpeed * Time.deltaTime));
        }

        private void TickCallForHelp()
        {
            if (Time.time < _nextCallTime) return;
            _nextCallTime = Time.time + _callForHelpInterval;
            OnCalledForHelp?.Invoke(this, transform.position);
        }

        /// <summary>
        /// 由救援者每帧调用。返回是否已完成救援。
        /// 设计成"调用方驱动"而不是自己找队友，是为了让救援规则
        /// （谁有资格救、要不要读条）由上层玩法系统决定。
        /// </summary>
        public bool TickRevive(float deltaTime, bool rescuerAlive)
        {
            if (!IsDowned) return false;

            if (!rescuerAlive)
            {
                _reviveAccumulator = 0f;
                ReviveProgress = 0f;
                return false;
            }

            _reviveAccumulator += deltaTime;
            ReviveProgress = Mathf.Clamp01(_reviveAccumulator / _reviveSeconds);

            if (ReviveProgress < 1f) return false;

            Resurrect();
            return true;
        }

        /// <summary>救援中断（施救者受伤/走开）。</summary>
        public void InterruptRevive()
        {
            _reviveAccumulator = 0f;
            ReviveProgress = 0f;
        }

        private void Resurrect()
        {
            IsDowned = false;
            ReviveProgress = 0f;
            _reviveAccumulator = 0f;

            // 扶起来给短暂无敌，避免刚起身又被同一波弹幕打死 —— 这是救援机制的关键细节，
            // 没有它的话"救起来立刻又倒"会让救援失去意义
            _health?.Revive(_reviveHealthRatio);

            if (_controller != null) _controller.enabled = true;

            OnRevived?.Invoke(this);
        }

        private void BleedOut()
        {
            IsDowned = false;
            OnBleedOut?.Invoke(this);
        }

        /// <summary>救援者是否在有效距离内。</summary>
        public bool IsInReviveRange(Vector3 rescuerPosition)
            => Vector3.Distance(transform.position, rescuerPosition) <= _reviveRadius;
    }
}
