using System;
using Fit.Feedback;
using UnityEngine;

namespace Fit.Combat
{
    using Fit.Combat.Weapon;

    /// <summary>
    /// 飞行中的投射物。
    ///
    /// 【为什么用 SphereCast 而不是等 OnTriggerEnter】
    /// 弹幕速度很快时，一帧能移动好几米。用触发器的物理回调会直接穿过薄墙和
    /// 玩家模型（tunneling）—— 表现为"子弹从身上穿过去了但没打中"，
    /// 在弹幕游戏里这是灾难级 bug。每帧手动 SphereCast 上一帧位置到当前位置，
    /// 就能保证不漏判。
    ///
    /// 【服务器权威边界】
    /// 命中判定与伤害结算必须在权威端。
    /// 阶段 1 离线运行时，本机即权威；阶段 3 接入时把 Hit 判定挪到服务器即可，
    /// 客户端只保留飞行表演（视觉插值）。
    ///
    /// 【可读性（ID-007）】
    /// 弹体必须自发光 + 拖尾。低分辨率 3D 下（Q1）像素很粗，
    /// 不发光的深色子弹在暗地牢里几乎看不见 —— 这直接决定弹幕玩法成不成立。
    /// </summary>
    [RequireComponent(typeof(ProjectileVisual))]
    public sealed class Projectile : MonoBehaviour
    {
        [Header("碰撞")]
        [SerializeField] private LayerMask _hitMask = ~0;
        [SerializeField] private float _radius = 0.18f;
        [SerializeField] private bool _destroyOnHit = true;

        [Header("穿透")]
        [SerializeField] private int _maxPierceCount;

        private float _speed;
        private float _lifetime;
        private float _damage;
        private Vector3 _direction;
        private float _traveled;
        private int _pierced;
        private GameObject _owner;
        private WeaponData _sourceWeapon;
        private bool _initialized;
        private Vector3 _previousPosition;

        /// <summary>
        /// 回收事件。WeaponBase 监听它把弹丸放回对象池。
        /// 不走事件的话，Retire 只能自己 SetActive(false)，池子永远收不到货。
        /// </summary>
        public event Action<Projectile> OnRetired;

        public Vector3 Direction => _direction;
        public float Speed => _speed;
        public GameObject Owner => _owner;

        private void OnEnable() => _previousPosition = transform.position;

        public void Initialize(
            Vector3 origin,
            Vector3 direction,
            float speed,
            float damage,
            float lifetime,
            GameObject owner,
            WeaponData sourceWeapon = null)
        {
            transform.position = origin;
            transform.forward = direction.normalized;

            _direction = direction.normalized;
            _speed = speed;
            _damage = damage;
            _lifetime = lifetime;
            _owner = owner;
            _sourceWeapon = sourceWeapon;

            _traveled = 0f;
            _pierced = 0;
            _initialized = true;
            _previousPosition = origin;
        }

        private void Update()
        {
            if (!_initialized) return;

            float step = _speed * Time.deltaTime;
            MoveAndCollide(step);

            _traveled += step;
            if (_traveled >= _lifetime)
                Retire();
        }

        private void MoveAndCollide(float step)
        {
            _previousPosition = transform.position;
            Vector3 next = _previousPosition + _direction * step;

            // 用球体扫描覆盖整段位移，彻底避免高速穿透
            if (Physics.SphereCast(_previousPosition, _radius, _direction, out RaycastHit hit, step, _hitMask, QueryTriggerInteraction.Ignore))
            {
                transform.position = hit.point;
                ResolveHit(hit);
                return;
            }

            transform.position = next;
        }

        private void ResolveHit(RaycastHit hit)
        {
            // 打到发射者自己：忽略（散弹近距离自伤会很烦人）
            if (hit.collider.gameObject == _owner) return;

            var info = DamageInfo.Create(_damage, _owner, hit.point, _direction);
            info.Flags |= DamageFlags.NoDown;   // 默认不因被弹幕击中直接倒地，交由 DownedState 判定

            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null)
                damageable.ApplyDamage(info);

            // 通知武器行为（爆炸、吸血、连锁等挂在这里）
            if (_sourceWeapon != null)
            {
                var ctx = new WeaponHitContext
                {
                    Data = _sourceWeapon,
                    Owner = _owner,
                    HitPoint = hit.point,
                    HitNormal = hit.normal,
                    Target = hit.collider.gameObject,
                    DamageDealt = _damage
                };
                foreach (var b in _sourceWeapon.Behaviours)
                    b?.OnHit(ctx);
            }

            bool canPierce = _pierced < _maxPierceCount;
            _pierced++;

            if (_destroyOnHit && !canPierce)
                Retire();
        }

        private void Retire()
        {
            _initialized = false;
            OnRetired?.Invoke(this);

            // 没有监听者（例如敌人子弹，不走武器池）时自己兜底关掉
            if (OnRetired == null)
                gameObject.SetActive(false);
        }
    }
}
