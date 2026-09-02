using Fit.Combat;
using UnityEngine;

namespace Fit.Player
{
    /// <summary>
    /// 第一人称角色控制器。
    ///
    /// 【为什么阶段 1 不直接写成 NetworkBehaviour】
    /// §8 关键路径建议：先定手感，再接联机。
    /// 所以这里把「移动模拟」写成纯 MonoBehaviour —— 输入进去，位移出来，
    /// 没有任何网络依赖，可以立刻在编辑器里跑起来调手感。
    ///
    /// 但这不是返工。客户端预测（阶段 3 必做）本来就要求
    /// 「模拟逻辑」与「复制逻辑」分离：客户端和服务器要跑同一份模拟代码，
    /// 才能做回滚比对。所以这个类的结构正是预测需要的形态 ——
    /// 阶段 3 只需加一个 NetworkBehaviour 外壳来驱动 Simulate() 并回传状态，
    /// 本文件一行都不用改。
    ///
    /// 【手感相关的几个刻意选择】
    ///   - 用加速度而非直接设速度：直接设速度的移动"太干"，没有重量感
    ///   - 空中控制力降到地面的 30%：保留惯性感，但还能微调落点
    ///   - Coyote Time（离地后仍可跳 0.1 秒）：FPS 必备，否则会频繁"按了没跳"
    ///   - 跳跃缓冲（落地前按跳会记住）：同上，减少"我按了啊"的挫败
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FPSController : MonoBehaviour
    {
        [Header("移动")]
        [SerializeField] private float _walkSpeed = 6.5f;
        [SerializeField] private float _sprintSpeed = 9.5f;
        [SerializeField] private float _acceleration = 60f;
        [SerializeField] private float _deceleration = 50f;
        [Tooltip("空中控制力相对地面的比例。0.3 = 空中只能微弱调整。")]
        [SerializeField, Range(0f, 1f)] private float _airControlRatio = 0.3f;

        [Header("跳跃")]
        [SerializeField] private float _jumpHeight = 1.6f;
        [SerializeField] private float _gravity = -26f;
        [Tooltip("离地后仍允许起跳的时间。FPS 必备手感补偿。")]
        [SerializeField] private float _coyoteTime = 0.1f;
        [Tooltip("落地前提前按跳会被记住多久。")]
        [SerializeField] private float _jumpBufferTime = 0.12f;

        [Header("翻滚闪避（ID-010）")]
        [SerializeField] private float _dodgeSpeed = 14f;
        [SerializeField] private float _dodgeDuration = 0.32f;
        [SerializeField] private float _dodgeCooldown = 0.6f;
        [Tooltip("无敌帧时长。这是核心生存手段，必须够长到能穿过一层弹幕。")]
        [SerializeField] private float _dodgeInvulnerable = 0.3f;

        [Header("视角")]
        [SerializeField] private float _mouseSensitivity = 2.2f;
        [SerializeField] private float _minPitch = -88f;
        [SerializeField] private float _maxPitch = 88f;

        public bool IsGrounded { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsDodging => _dodgeEndTime > Time.time;
        public float DodgeCooldownRatio => Mathf.Clamp01((_dodgeEndTime + _dodgeCooldown - Time.time) / _dodgeCooldown);
        public Vector3 HorizontalVelocity { get; private set; }

        /// <summary>翻滚触发事件。UI 与音效监听。</summary>
        public event System.Action OnDodged;

        private CharacterController _controller;
        private Health _health;
        private Camera _camera;

        private Vector3 _velocity;
        private float _pitch;
        private float _yaw;
        private float _lastGroundedTime;
        private float _jumpPressedTime;
        private float _dodgeEndTime;
        private float _nextDodgeTime;
        private Vector3 _dodgeDirection;

        /// <summary>
        /// 输入结构。抽出来是为了让阶段 3 的网络层能直接序列化这一份数据
        /// 发送给服务器做预测/回滚，不用重新组织字段。
        /// </summary>
        public struct InputFrame
        {
            public Vector2 Move;      // x = 左右，y = 前后
            public float LookX;
            public float LookY;
            public bool Jump;
            public bool Sprint;
            public bool Dodge;
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _health = GetComponent<Health>();
            _camera = GetComponentInChildren<Camera>();

            Vector3 euler = transform.eulerAngles;
            _yaw = euler.y;
        }

        /// <summary>
        /// 每帧驱动。输入由调用方采集（本地或网络层）。
        /// </summary>
        public void Simulate(in InputFrame input, float deltaTime)
        {
            ApplyLook(input);
            TickDodge(input, deltaTime);
            TickMovement(input, deltaTime);
        }

        private void ApplyLook(in InputFrame input)
        {
            _yaw += input.LookX * _mouseSensitivity;
            _pitch -= input.LookY * _mouseSensitivity;
            _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);

            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

            if (_camera != null)
                _camera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void TickDodge(in InputFrame input, float deltaTime)
        {
            if (input.Dodge && Time.time >= _nextDodgeTime && !IsDodging)
            {
                // 没有输入方向时朝正前方翻滚，比原地不动更符合直觉
                _dodgeDirection = input.Move.sqrMagnitude > 0.01f
                    ? (transform.right * input.Move.x + transform.forward * input.Move.y).normalized
                    : transform.forward;

                _dodgeEndTime = Time.time + _dodgeDuration;
                _nextDodgeTime = _dodgeEndTime + _dodgeCooldown;

                // 无敌帧：写在 Health 上，这样任何伤害来源都不需要知道"玩家在翻滚"
                _health?.SetInvulnerable(_dodgeInvulnerable);
                OnDodged?.Invoke();
            }

            if (!IsDodging) return;

            // 翻滚期间强制位移，忽略常规加速逻辑
            _controller.Move(_dodgeDirection * (_dodgeSpeed * deltaTime));
        }

        private void TickMovement(in InputFrame input, float deltaTime)
        {
            if (IsDodging) return;

            IsGrounded = _controller.isGrounded;
            if (IsGrounded) _lastGroundedTime = Time.time;
            if (input.Jump) _jumpPressedTime = Time.time;

            bool canCoyote = Time.time - _lastGroundedTime <= _coyoteTime;
            bool hasBufferedJump = Time.time - _jumpPressedTime <= _jumpBufferTime;

            IsSprinting = input.Sprint && input.Move.y > 0.1f;
            float targetSpeed = IsSprinting ? _sprintSpeed : _walkSpeed;

            Vector3 wish = (transform.right * input.Move.x + transform.forward * input.Move.y).normalized;
            Vector3 target = wish * targetSpeed;

            float control = IsGrounded ? 1f : _airControlRatio;
            float rate = wish.sqrMagnitude > 0.01f
                ? _acceleration * control
                : _deceleration * control;

            Vector3 horizontal = new(_velocity.x, 0f, _velocity.z);
            horizontal = Vector3.MoveTowards(horizontal, target, rate * deltaTime);

            _velocity.x = horizontal.x;
            _velocity.z = horizontal.z;

            if (canCoyote && hasBufferedJump)
            {
                _velocity.y = Mathf.Sqrt(-2f * _gravity * _jumpHeight);
                _lastGroundedTime = -999f;   // 消耗掉 coyote，避免二段跳
                _jumpPressedTime = -999f;
            }

            _velocity.y += _gravity * deltaTime;
            if (IsGrounded && _velocity.y < 0f)
                _velocity.y = -2f;   // 贴地，避免下坡时弹跳

            _controller.Move(_velocity * deltaTime);
            HorizontalVelocity = new Vector3(_velocity.x, 0f, _velocity.z);
        }

        /// <summary>
        /// 击退。脑洞武器的击飞效果、Boss 的冲击波都走这里。
        /// 直接改速度而不是加力，保证击退量可预期。
        /// </summary>
        public void ApplyKnockback(Vector3 impulse)
        {
            _velocity += impulse;
        }
    }
}
