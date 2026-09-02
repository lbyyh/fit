using FishNet.Object;
using FishNet.Serializing;
using FishNet.Transporting;
using UnityEngine;

namespace Fit.Networking.Sync
{
    /// <summary>
    /// 物理对象的网络同步。
    ///
    /// How to Fish 有一个独立的 RigidbodySync NetworkBehaviour，说明它把物理同步
    /// 从"每帧发 Transform"升级成了"状态 + 收敛"的做法。
    ///
    /// 这里实现的是工业界标准套路：
    ///   - 低频（可配置）发送权威状态（位置/旋转/速度/角速度）；
    ///   - 接收端维护一个目标态，用指数平滑 + 阈值判定做插值；
    ///   - 偏差超过阈值时直接瞬移（teleport threshold），避免"橡皮筋"长时间不收敛；
    ///   - 只有 authoritative 一端写入，远端只读取，杜绝双向打架。
    ///
    /// 对于船、可投掷物、被击飞的生物这类物体，这套方案比逐帧同步省 5~10 倍带宽。
    /// </summary>
    public sealed class NetworkRigidbodySync : NetworkEntity
    {
        [Header("目标")]
        [SerializeField] private Rigidbody _rigidbody;

        [Header("同步参数")]
        [SerializeField] private float _sendInterval = 0.05f;      // 20Hz
        [SerializeField] private float _interpolationSpeed = 12f;  // 平滑系数
        [SerializeField] private float _teleportThreshold = 8f;    // 超过此距离直接吸附
        [SerializeField] private bool _syncVelocity = true;

        private readonly struct State
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 Velocity;
            public readonly Vector3 AngularVelocity;

            public State(Vector3 p, Quaternion r, Vector3 v, Vector3 av)
            {
                Position = p; Rotation = r; Velocity = v; AngularVelocity = av;
            }
        }

        private State _target;
        private float _nextSendTime;
        private bool _hasTarget;

        public override void OnStartServer()
        {
            base.OnStartServer();
            _rigidbody = _rigidbody ? _rigidbody : GetComponent<Rigidbody>();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            _rigidbody = _rigidbody ? _rigidbody : GetComponent<Rigidbody>();

            if (!base.IsOwner)
            {
                // 远端：物理交给网络状态驱动，禁用本地模拟避免抖动
                _rigidbody.isKinematic = true;
                _rigidbody.interpolation = RigidbodyInterpolation.None;
            }
        }

        private void FixedUpdate()
        {
            if (base.IsServerStarted && Time.unscaledTime >= _nextSendTime)
            {
                _nextSendTime = Time.unscaledTime + _sendInterval;
                BroadcastState(_rigidbody.position, _rigidbody.rotation,
                               _rigidbody.velocity, _rigidbody.angularVelocity);
            }
        }

        private void Update()
        {
            if (base.IsOwner || base.IsServerStarted || !_hasTarget)
                return;

            // 远端插值收敛
            float t = 1f - Mathf.Exp(-_interpolationSpeed * Time.deltaTime);

            Vector3 pos = _rigidbody.position;
            float distance = Vector3.Distance(pos, _target.Position);

            if (distance > _teleportThreshold)
            {
                pos = _target.Position;                       // 偏差过大，直接吸附
                _rigidbody.rotation = _target.Rotation;
            }
            else
            {
                pos = Vector3.Lerp(pos, _target.Position, t);
                _rigidbody.rotation = Quaternion.Slerp(_rigidbody.rotation, _target.Rotation, t);
            }

            _rigidbody.position = pos;
        }

        [ObserversRpc(BufferLast = true, ExcludeServer = true)]
        private void BroadcastState(Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity)
        {
            _target = new State(position, rotation, velocity, angularVelocity);
            _hasTarget = true;

            if (_syncVelocity && _rigidbody != null && !_rigidbody.isKinematic)
            {
                _rigidbody.velocity = velocity;
                _rigidbody.angularVelocity = angularVelocity;
            }
        }

        /// <summary>
        /// 房主强制重置物体（例如把船传送回码头）时调用，
        /// 避免客户端残留旧目标态导致物体自己"飞回来"。
        /// </summary>
        public void ForceResync()
        {
            _hasTarget = false;
            if (base.IsServerStarted)
                BroadcastState(_rigidbody.position, _rigidbody.rotation,
                               _rigidbody.velocity, _rigidbody.angularVelocity);
        }
    }
}
