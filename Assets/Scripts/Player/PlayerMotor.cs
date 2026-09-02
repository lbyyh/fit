using Fit.Combat.Weapon;
using UnityEngine;

namespace Fit.Player
{
    /// <summary>
    /// 玩家输入驱动 —— 把原始输入喂给 FPSController，并驱动武器与交互。
    ///
    /// 【为什么单独拆一层】
    /// FPSController.Simulate() 接收的是一个 InputFrame 结构体，不直接读输入。
    /// 这样阶段 3 接入联机时，网络层可以直接构造 InputFrame 发给服务器，
    /// 控制器本身完全不用改 —— 这正是客户端预测需要的结构。
    ///
    /// 【关于输入系统】
    /// 这里用传统 Input（Input.GetAxis / GetMouseButton）。
    /// 如果工程启用了新 Input System，需要在
    /// Project Settings > Player > Active Input Handling 设为 "Both"，
    /// 或者把这里改成 InputAction。阶段 1 先跑通，输入方案后面统一。
    ///
    /// 【救援交互放这里】
    /// 因为"谁能救谁"是玩家之间的交互，属于输入层职责。
    /// 判定逻辑本身在 DownedState 里。
    /// </summary>
    public sealed class PlayerMotor : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private FPSController _controller;
        [SerializeField] private WeaponBase _weapon;
        [SerializeField] private DownedState _downed;

        [Header("输入")]
        [SerializeField] private KeyCode _jumpKey = KeyCode.Space;
        [SerializeField] private KeyCode _sprintKey = KeyCode.LeftShift;
        [SerializeField] private KeyCode _dodgeKey = KeyCode.LeftControl;
        [SerializeField] private KeyCode _reloadKey = KeyCode.R;
        [SerializeField] private KeyCode _interactKey = KeyCode.E;
        [SerializeField] private string _horizontalAxis = "Horizontal";
        [SerializeField] private string _verticalAxis = "Vertical";
        [SerializeField] private string _mouseXAxis = "Mouse X";
        [SerializeField] private string _mouseYAxis = "Mouse Y";

        [Header("鼠标")]
        [SerializeField] private bool _lockCursor = true;
        [SerializeField] private KeyCode _unlockKey = KeyCode.Escape;

        [Header("救援")]
        [SerializeField] private float _interactRange = 3f;

        private bool _cursorLocked;

        private void Awake()
        {
            if (_controller == null) _controller = GetComponent<FPSController>();
            if (_weapon == null) _weapon = GetComponentInChildren<WeaponBase>();
            if (_downed == null) _downed = GetComponent<DownedState>();

            SetCursorLocked(_lockCursor);
        }

        private void Update()
        {
            if (Input.GetKeyDown(_unlockKey))
                SetCursorLocked(false);

            if (Input.GetMouseButtonDown(0) && !_cursorLocked && _lockCursor)
                SetCursorLocked(true);

            // 倒地时不接受移动与开火输入，但保留爬行（在 DownedState 内处理）
            if (_downed != null && _downed.IsDowned)
            {
                TickReviveInteraction();
                return;
            }

            GatherAndSimulate();
            TickWeapon();
            TickReviveInteraction();
        }

        private void GatherAndSimulate()
        {
            var frame = new FPSController.InputFrame
            {
                Move = new Vector2(
                    Input.GetAxisRaw(_horizontalAxis),
                    Input.GetAxisRaw(_verticalAxis)),
                LookX = _cursorLocked ? Input.GetAxis(_mouseXAxis) : 0f,
                LookY = _cursorLocked ? Input.GetAxis(_mouseYAxis) : 0f,
                Jump = Input.GetKeyDown(_jumpKey),
                Sprint = Input.GetKey(_sprintKey),
                Dodge = Input.GetKeyDown(_dodgeKey)
            };

            _controller?.Simulate(frame, Time.deltaTime);
        }

        private void TickWeapon()
        {
            if (_weapon == null) return;

            if (Input.GetKeyDown(_reloadKey))
                _weapon.BeginReload();

            _weapon.TryFire(
                triggerHeld: Input.GetMouseButton(0),
                triggerPressed: Input.GetMouseButtonDown(0));
        }

        /// <summary>
        /// 救援交互：按住 E 扶起范围内的倒地队友，松开或走开则中断。
        /// </summary>
        private void TickReviveInteraction()
        {
            if (_downed != null && _downed.IsDowned) return;   // 自己倒地时救不了人

            var target = FindRevivableTarget();
            if (target == null) return;

            if (!Input.GetKey(_interactKey) || !target.IsInReviveRange(transform.position))
            {
                target.InterruptRevive();
                return;
            }

            target.TickRevive(Time.deltaTime, rescuerAlive: true);
        }

        private DownedState FindRevivableTarget()
        {
            // 原型阶段直接全局查找。正式版应由房间/队伍管理器维护玩家列表，
            // 每帧 FindObjectsOfType 在人多时会造成 GC 压力。
            var all = FindObjectsOfType<DownedState>();
            DownedState best = null;
            float bestDistance = _interactRange;

            foreach (var candidate in all)
            {
                if (candidate == _downed || !candidate.IsDowned) continue;

                float d = Vector3.Distance(transform.position, candidate.transform.position);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = candidate;
                }
            }

            return best;
        }

        private void SetCursorLocked(bool locked)
        {
            _cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
