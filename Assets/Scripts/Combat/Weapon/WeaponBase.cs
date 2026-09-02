using System.Collections.Generic;
using UnityEngine;

namespace Fit.Combat.Weapon
{
    /// <summary>
    /// 武器运行时逻辑。挂在玩家（或敌人）身上，持有一把 WeaponData。
    ///
    /// 【为什么 Hitscan 和 Projectile 写在同一个类里】
    /// 它们是 WeaponData.Mode 的两个分支，共用弹匣、射速、后坐力、行为钩子。
    /// 分成两个类会导致这些逻辑重复两遍，加功能要改两处。
    /// 真出现第三种模式（比如持续光束）再考虑拆。
    ///
    /// 【后坐力为什么只做客户端视觉 —— 重申】
    /// §7 已记录决策：后坐力绝不同步。
    /// 如果后坐力走网络，玩家会感觉到"我明明压住了枪，准星却自己飘了"，
    /// 因为服务器回滚会覆盖本地状态。这里累积的 Recoil 值只喂给本地相机。
    ///
    /// 【为什么投射物要用对象池】
    /// 弹幕场景下每秒可能生成上百个投射物，Instantiate/Destroy 会造成
    /// GC 尖峰导致掉帧 —— 在需要精确操作的弹幕游戏里，掉帧 = 死亡。
    /// </summary>
    public sealed class WeaponBase : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private Transform _muzzle;
        [SerializeField] private Camera _aimCamera;
        [SerializeField] private LayerMask _hitscanMask = ~0;

        [Header("辅助瞄准（PVE 可大方做）")]
        [SerializeField] private bool _assistEnabled = true;
        [Tooltip("准星吸附角度（度）。PVE 没有公平性问题，给宽松点。")]
        [SerializeField, Range(0f, 10f)] private float _assistAngleDegrees = 3f;
        [SerializeField] private float _assistRange = 60f;

        public WeaponData Data { get; private set; }
        public bool HasWeapon => Data != null;

        public int AmmoInMagazine { get; private set; }
        public bool IsReloading { get; private set; }
        public float ReloadProgress { get; private set; }

        /// <summary>本地累积的后坐力（仅视觉）。由相机读取。</summary>
        public Vector2 Recoil { get; private set; }

        /// <summary>开火事件。参数：后坐力强度 / 屏幕震动强度。UI 与相机监听这个。</summary>
        public event System.Action<float, float> OnFired;
        public event System.Action<int, int> OnAmmoChanged;
        public event System.Action<WeaponData> OnWeaponChanged;

        private float _nextShotTime;
        private float _reloadEndTime;
        private int _burstRemaining;
        private readonly Queue<Projectile> _pool = new();

        public void Equip(WeaponData data)
        {
            if (data == null) { Unequip(); return; }

            // 换武器时旧弹丸类型不适用，池子必须清掉再建
            if (Data != null && Data != data)
                ClearPool();

            Data = data;
            AmmoInMagazine = data.MagazineSize;
            IsReloading = false;
            _burstRemaining = 0;

            OnWeaponChanged?.Invoke(data);
            OnAmmoChanged?.Invoke(AmmoInMagazine, Data.MagazineSize);
        }

        public void Unequip()
        {
            Data = null;
            AmmoInMagazine = 0;
            OnWeaponChanged?.Invoke(null);
        }

        private void Update()
        {
            TickRecoilRecovery();
            TickReload();
        }

        /// <summary>
        /// 尝试开火。参数由输入层传入（按住/点按）。
        /// </summary>
        public void TryFire(bool triggerHeld, bool triggerPressed)
        {
            if (!HasWeapon || IsReloading) return;

            bool wants = Data.Trigger switch
            {
                TriggerType.Auto => triggerHeld,
                TriggerType.Semi => triggerPressed,
                TriggerType.Burst => triggerPressed,
                _ => triggerHeld
            };

            if (!wants) return;
            if (Time.time < _nextShotTime) return;

            if (AmmoInMagazine <= 0)
            {
                BeginReload();
                return;
            }

            if (Data.Trigger == TriggerType.Burst)
            {
                if (_burstRemaining <= 0) _burstRemaining = Data.BurstCount;
                _burstRemaining--;
            }

            Fire();
            _nextShotTime = Time.time + Data.SecondsBetweenShots;

            AmmoInMagazine--;
            OnAmmoChanged?.Invoke(AmmoInMagazine, Data.MagazineSize);
            if (AmmoInMagazine <= 0) BeginReload();
        }

        private void Fire()
        {
            Vector3 origin = _muzzle != null ? _muzzle.position : transform.position;
            Vector3 direction = GetAimDirection(origin);

            var ctx = new WeaponFireContext
            {
                Data = Data,
                Owner = gameObject,
                Muzzle = _muzzle,
                Origin = origin,
                Direction = direction,
                ShotIndexInBurst = Data.BurstCount - _burstRemaining
            };

            for (int i = 0; i < Mathf.Max(1, Data.PelletsPerShot); i++)
            {
                Vector3 dir = ApplySpread(direction, Data.SpreadDegrees);

                if (Data.Mode == FireMode.Hitscan) FireHitscan(origin, dir, ctx);
                else FireProjectile(origin, dir, ctx);
            }

            foreach (var b in Data.Behaviours)
                b?.OnFire(ctx);

            // 后坐力只加到本地，绝不同步
            Recoil += new Vector2(
                Random.Range(-Data.RecoilKick * 0.35f, Data.RecoilKick * 0.35f),
                Data.RecoilKick);

            OnFired?.Invoke(Data.RecoilKick, Data.ScreenShake);

            if (Data.FireSound != null && _muzzle != null)
                AudioSource.PlayClipAtPoint(Data.FireSound, _muzzle.position);
        }

        private void FireHitscan(Vector3 origin, Vector3 direction, WeaponFireContext ctx)
        {
            if (!Physics.Raycast(origin, direction, out RaycastHit hit, Data.Range, _hitscanMask, QueryTriggerInteraction.Ignore))
                return;

            if (hit.collider.gameObject == gameObject) return;

            float dealt = 0f;
            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                var info = DamageInfo.Create(Data.Damage, gameObject, hit.point, direction);
                dealt = damageable.ApplyDamage(info);
            }

            var hitCtx = new WeaponHitContext
            {
                Data = Data,
                Owner = gameObject,
                HitPoint = hit.point,
                HitNormal = hit.normal,
                Target = hit.collider.gameObject,
                DamageDealt = dealt
            };

            foreach (var b in Data.Behaviours)
                b?.OnHit(hitCtx);
        }

        private void FireProjectile(Vector3 origin, Vector3 direction, WeaponFireContext ctx)
        {
            Projectile p = Rent();
            p.Initialize(
                origin, direction,
                Data.ProjectileSpeed,
                Data.Damage,
                Data.ProjectileLifetime,
                gameObject,
                Data);

            foreach (var b in Data.Behaviours)
                b?.OnProjectileSpawned(p, ctx);
        }

        /// <summary>
        /// 辅助瞄准。PVE 没有公平性问题，玩家爽就是对的。
        /// 在设置里可关（_assistEnabled），并且只吸附"敌人层"的目标。
        /// </summary>
        private Vector3 GetAimDirection(Vector3 origin)
        {
            var cam = _aimCamera != null ? _aimCamera : Camera.main;
            if (cam == null) return transform.forward;

            Vector3 dir = cam.transform.forward;
            if (!_assistEnabled || _assistAngleDegrees <= 0f) return dir;

            // 在准星附近找最贴近准星的敌人，轻微修正朝向
            float bestAngle = _assistAngleDegrees;
            Vector3 best = dir;

            foreach (var hit in Physics.SphereCastAll(cam.transform.position, 0.6f, dir, _assistRange, _hitscanMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<IDamageable>() == null) continue;
                if (hit.collider.transform.IsChildOf(transform)) continue;

                Vector3 toTarget = (hit.point - origin).normalized;
                float angle = Vector3.Angle(dir, toTarget);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = toTarget;
                }
            }

            return best;
        }

        private static Vector3 ApplySpread(Vector3 direction, float degrees)
        {
            if (degrees <= 0f) return direction;
            return Quaternion.AngleAxis(Random.Range(-degrees, degrees), Vector3.up) *
                   Quaternion.AngleAxis(Random.Range(-degrees, degrees), Vector3.right) * direction;
        }

        private void TickRecoilRecovery()
        {
            if (Data == null) return;
            Recoil = Vector2.Lerp(Recoil, Vector2.zero, Data.RecoilRecoverSpeed * Time.deltaTime);
        }

        public void BeginReload()
        {
            if (!HasWeapon || IsReloading) return;
            if (AmmoInMagazine >= Data.MagazineSize) return;

            IsReloading = true;
            _reloadEndTime = Time.time + Data.ReloadSeconds;
        }

        private void TickReload()
        {
            if (!IsReloading) return;

            ReloadProgress = 1f - (_reloadEndTime - Time.time) / Mathf.Max(0.01f, Data.ReloadSeconds);

            if (Time.time < _reloadEndTime) return;

            AmmoInMagazine = Data.MagazineSize;
            IsReloading = false;
            ReloadProgress = 0f;
            _burstRemaining = 0;
            OnAmmoChanged?.Invoke(AmmoInMagazine, Data.MagazineSize);
        }

        private Projectile Rent()
        {
            Projectile result = null;

            while (_pool.Count > 0)
            {
                var pooled = _pool.Dequeue();
                if (pooled != null) { result = pooled; break; }
            }

            if (result == null)
                result = Instantiate(Data.ProjectilePrefab, transform);

            // 先减后加，保证池中复用时不会重复订阅
            result.OnRetired -= HandleProjectileRetired;
            result.OnRetired += HandleProjectileRetired;

            result.gameObject.SetActive(true);
            return result;
        }

        private void HandleProjectileRetired(Projectile projectile)
        {
            if (projectile == null) return;

            projectile.gameObject.SetActive(false);
            _pool.Enqueue(projectile);
        }

        private void ClearPool()
        {
            foreach (var p in _pool)
                if (p != null) Destroy(p.gameObject);
            _pool.Clear();
        }

        private void OnDestroy() => ClearPool();
    }
}
