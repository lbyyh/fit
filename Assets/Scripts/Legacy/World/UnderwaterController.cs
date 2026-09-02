using UnityEngine;
using UnityEngine.Rendering;

namespace Fit.World
{
    /// <summary>
    /// 水下状态切换。
    ///
    /// How to Fish 对水下的处理相当完整：
    ///   UnderWaterCheck / SetUnderWater / ToggleUnderwaterImage / SetFog /
    ///   FogState / SetWaterAudio / SetWaterAudioSource / IncreaseUnderwaterTime /
    ///   TimeUnderWater / SetCustomWaterTarget / MoveWater
    ///
    /// 归纳成"进出水面时统一切换 4 件事"：
    ///   1. 雾效（Fog）—— 水下能见度低、偏色，是最强的沉浸感来源；
    ///   2. 音频（Audio）—— 低通滤波 + 切换到水下环境音，听觉反馈比视觉更即时；
    ///   3. 后处理（Post）—— 折射/色差/暗角；
    ///   4. 玩法（Gameplay）—— 氧气计时、移动阻尼、道具掉落（AddUnderwaterItem）。
    ///
    /// 这里把 1~3 收敛到一个地方，玩法侧通过事件订阅，避免各处各自判断 IsUnderWater。
    /// </summary>
    public sealed class UnderwaterController : MonoBehaviour
    {
        [Header("雾效")]
        [SerializeField] private Color _aboveWaterFog = new Color(0.72f, 0.82f, 0.88f, 1f);
        [SerializeField] private float _aboveWaterFogDensity = 0.004f;
        [SerializeField] private Color _underWaterFog = new Color(0.05f, 0.18f, 0.28f, 1f);
        [SerializeField] private float _underWaterFogDensity = 0.09f;

        [Header("音频")]
        [SerializeField] private AudioLowPassFilter _lowPass;
        [SerializeField] private float _aboveWaterCutoff = 22000f;
        [SerializeField] private float _underWaterCutoff = 700f;
        [SerializeField] private float _transitionSpeed = 3f;

        [Header("水面")]
        [SerializeField] private World.Water.GerstnerWaves _waves;
        [SerializeField] private Camera _camera;

        public event Action<bool> UnderwaterStateChanged;

        public bool IsUnderwater { get; private set; }
        public float TimeUnderwater { get; private set; }

        private float _currentFogDensity;
        private Color _currentFogColor;

        private void Awake()
        {
            if (_camera == null)
                _camera = Camera.main;

            _currentFogDensity = _aboveWaterFogDensity;
            _currentFogColor = _aboveWaterFog;
        }

        private void Update()
        {
            if (_camera == null || _waves == null) return;

            bool underwater = _camera.transform.position.y <
                              _waves.SampleHeight(_camera.transform.position.x,
                                                  _camera.transform.position.z,
                                                  Time.time);

            if (underwater != IsUnderwater)
                SetUnderwater(underwater);

            TimeUnderwater = underwater ? TimeUnderwater + Time.deltaTime : 0f;

            ApplyTransition();
        }

        public void SetUnderwater(bool underwater)
        {
            if (IsUnderwater == underwater) return;

            IsUnderwater = underwater;
            RenderSettings.fog = true;

            if (_lowPass != null)
                _lowPass.cutoffFrequency = underwater ? _underWaterCutoff : _aboveWaterCutoff;

            UnderwaterStateChanged?.Invoke(underwater);
        }

        /// <summary>
        /// 雾效插值过渡。直接切换会有明显突兀感，
        /// 用指数平滑让入水/出水有个 0.3s 左右的渐变。
        /// </summary>
        private void ApplyTransition()
        {
            float t = 1f - Mathf.Exp(-_transitionSpeed * Time.deltaTime);

            float targetDensity = IsUnderwater ? _underWaterFogDensity : _aboveWaterFogDensity;
            Color targetColor = IsUnderwater ? _underWaterFog : _aboveWaterFog;

            _currentFogDensity = Mathf.Lerp(_currentFogDensity, targetDensity, t);
            _currentFogColor = Color.Lerp(_currentFogColor, targetColor, t);

            RenderSettings.fogDensity = _currentFogDensity;
            RenderSettings.fogColor = _currentFogColor;
        }
    }
}
