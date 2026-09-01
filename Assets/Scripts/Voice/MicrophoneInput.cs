using System;
using UnityEngine;

namespace Fit.Voice
{
    /// <summary>
    /// 麦克风采集 + 降噪 + 编码。
    ///
    /// 处理链：Microphone → 重采样 → 能量门 → RNNoise 降噪 → Opus 编码 → 输出字节
    ///
    /// 这是 How to Fish 的 MetaVoiceChat.Input.Mic 那一条链。
    /// 两个关键第三方依赖（都不在 UPM 上，需要手动放入 Assets/Plugins）：
    ///   - Concentus：纯托管 Opus 实现，无原生库，跨平台省心
    ///   - RNNoise4Unity：RNNoise 的 Unity 封装，底层是 GRU 神经网络降噪
    ///
    /// 为什么不用 Unity 自带的 Microphone 直接推流：
    ///   1. Unity Microphone 只能按设备原生采样率采集，需要重采样到 Opus 支持的档位；
    ///   2. 不做降噪的话，机械键盘和风扇噪音在 Opus 低码率下会被放大成"水下音"；
    ///   3. 不做能量门的话，静默时仍在持续发包，白白吃房主上行带宽。
    /// </summary>
    public sealed class MicrophoneInput : IDisposable
    {
        public event Action<byte[], int> OnFrameEncoded;

        private readonly VoiceSettings _settings;
        private AudioClip _clip;
        private string _device;
        private int _lastSamplePosition;
        private float[] _sampleBuffer;
        private bool _muted;

        public bool IsRecording => _clip != null && Microphone.IsRecording(_device);
        public bool Muted
        {
            get => _muted;
            set => _muted = value;
        }

        public MicrophoneInput(VoiceSettings settings)
        {
            _settings = settings;
            _sampleBuffer = new float[settings.SamplesPerFrame];
        }

        public void Start(string deviceName = null)
        {
            if (IsRecording) return;

            _device = string.IsNullOrEmpty(deviceName)
                ? (Microphone.devices.Length > 0 ? Microphone.devices[0] : null)
                : deviceName;

            if (string.IsNullOrEmpty(_device))
            {
                Debug.LogWarning("[Voice] 未检测到麦克风设备。");
                return;
            }

            // 循环录制 1 秒的缓冲，靠读取位置差来取新数据
            _clip = Microphone.Start(_device, true, 1, _settings.SampleRate);
            _lastSamplePosition = 0;
        }

        public void Stop()
        {
            if (!string.IsNullOrEmpty(_device) && Microphone.IsRecording(_device))
                Microphone.End(_device);

            _clip = null;
        }

        /// <summary>每帧调用，把新采集到的样本切成帧并编码。</summary>
        public void Update()
        {
            if (!IsRecording || _muted)
                return;

            int position = Microphone.GetPosition(_device);
            if (position <= 0 || position == _lastSamplePosition)
                return;

            int available = position - _lastSamplePosition;
            if (available < 0)
                available += _clip.samples; // 环形缓冲绕回

            int frameSize = _settings.SamplesPerFrame;

            while (available >= frameSize)
            {
                _clip.GetData(_sampleBuffer, _lastSamplePosition);

                _lastSamplePosition = (_lastSamplePosition + frameSize) % _clip.samples;
                available -= frameSize;

                ProcessFrame(_sampleBuffer);
            }
        }

        private void ProcessFrame(float[] samples)
        {
            // 1) 能量门：静默帧直接丢弃
            if (Rms(samples) < _settings.NoiseGateThreshold)
                return;

            // 2) 增益
            if (!Mathf.Approximately(_settings.MicrophoneGain, 1f))
            {
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = Mathf.Clamp(samples[i] * _settings.MicrophoneGain, -1f, 1f);
            }

            // 3) RNNoise 降噪（要求 48kHz 输入，内部会重采样）
            if (_settings.EnableRnnoise)
                RnnoiseDenoiser.Process(samples);

            // 4) Opus 编码
            var encoded = OpusCodec.Encode(samples);
            if (encoded == null || encoded.Length == 0)
                return;

            OnFrameEncoded?.Invoke(encoded, _settings.SamplesPerFrame);
        }

        private static float Rms(float[] samples)
        {
            double sum = 0d;
            for (int i = 0; i < samples.Length; i++)
                sum += samples[i] * samples[i];

            return Mathf.Sqrt((float)(sum / samples.Length));
        }

        public void SwitchDevice(string deviceName)
        {
            Stop();
            Start(deviceName);
        }

        public void Dispose() => Stop();
    }
}
