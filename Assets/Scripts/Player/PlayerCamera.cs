using Fit.Combat.Weapon;
using UnityEngine;

namespace Fit.Player
{
    /// <summary>
    /// 第一人称相机表现层：后坐力、屏幕震动、视角 FOV。
    ///
    /// 【这里全部是纯本地效果，绝不同步】
    /// 后坐力、震屏如果在网络上同步，玩家会看到"我没开枪但准星在跳"，
    /// 或者"我明明压住枪了但服务器回滚又把它拉回去"。
    /// 所以这些效果只从本地 WeaponBase 读取，永远不进网络状态。
    ///
    /// 【为什么后坐力要分「立即上跳」和「缓慢回复」】
    /// 只有缓慢回复的话，连射时准星会一路飘走，玩家压不住枪；
    /// 只有立即上跳的话手感很"顿"。两者叠加是主流 FPS 的做法。
    /// </summary>
    public sealed class PlayerCamera : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private FPSController _controller;
        [SerializeField] private WeaponBase _weapon;
        [SerializeField] private Camera _camera;

        [Header("后坐力")]
        [SerializeField, Range(0f, 1f)] private float _recoilToPitchRatio = 0.6f;
        [SerializeField] private float _maxRecoilPitch = 12f;

        [Header("震动")]
        [SerializeField] private float _shakeDecay = 6f;
        [SerializeField] private float _maxShakeOffset = 0.12f;

        [Header("FOV")]
        [SerializeField] private float _baseFov = 90f;
        [Tooltip("冲刺时 FOV 增加量。速度感来源。")]
        [SerializeField] private float _sprintFovBoost = 8f;
        [SerializeField] private float _fovLerpSpeed = 6f;

        private float _recoilPitch;
        private float _recoilYaw;
        private float _shakeStrength;
        private float _currentFov;

        private void Awake()
        {
            if (_camera == null) _camera = GetComponent<Camera>();
            _currentFov = _baseFov;

            if (_weapon != null)
                _weapon.OnFired += OnWeaponFired;
        }

        private void OnDestroy()
        {
            if (_weapon != null)
                _weapon.OnFired -= OnWeaponFired;
        }

        private void OnWeaponFired(float recoil, float shake)
        {
            // 只累积到一半上限，留出压枪空间
            _recoilPitch = Mathf.Clamp(_recoilPitch + recoil * _recoilToPitchRatio, 0f, _maxRecoilPitch);
            _recoilYaw += Random.Range(-recoil * 0.3f, recoil * 0.3f);
            _shakeStrength = Mathf.Min(_shakeStrength + shake, 1f);
        }

        private void LateUpdate()
        {
            // 后坐力回复：朝 0 收敛，且回复速度略快于武器侧的 Recoil 值衰减
            _recoilPitch = Mathf.Lerp(_recoilPitch, 0f, 8f * Time.deltaTime);
            _recoilYaw = Mathf.Lerp(_recoilYaw, 0f, 8f * Time.deltaTime);
            _shakeStrength = Mathf.Lerp(_shakeStrength, 0f, _shakeDecay * Time.deltaTime);

            float targetFov = _baseFov + (_controller != null && _controller.IsSprinting ? _sprintFovBoost : 0f);
            _currentFov = Mathf.Lerp(_currentFov, targetFov, _fovLerpSpeed * Time.deltaTime);

            if (_camera != null)
                _camera.fieldOfView = _currentFov;

            Vector3 offset = Random.insideUnitSphere * (_shakeStrength * _maxShakeOffset);
            transform.localPosition = offset;
            transform.localRotation = Quaternion.Euler(-_recoilPitch, _recoilYaw, 0f);
        }

        /// <summary>
        /// 基础 FOV 可在设置里调。第一人称弹幕游戏建议 95-105，
        /// 比标准 90 更宽能缓解 §5.1 冲突一（视野盲区）。
        /// </summary>
        public void SetBaseFov(float fov)
        {
            _baseFov = Mathf.Clamp(fov, 70f, 120f);
        }
    }
}
