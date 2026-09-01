using System.Collections.Generic;
using UnityEngine;

namespace Fit.Voice
{
    /// <summary>
    /// 语音系统门面。串起采集、编码、传输、播放、设备管理。
    ///
    /// 对应的就是 How to Fish 的 MetaVoiceChat 顶层结构：
    ///   Input(Mic) / Opus / Rnnoise / NetProviders(FishNet) / Output(Multicast, AudioSource)
    /// 再加上 ChangeMicrophone、MicrophoneDevicesListener 这类设备管理。
    /// </summary>
    public sealed class VoiceSystem : MonoBehaviour
    {
        [Header("配置")]
        [SerializeField] private VoiceSettings _settings;

        [Header("组件")]
        [SerializeField] private FishNetVoiceProvider _provider;

        private MicrophoneInput _input;
        private readonly List<string> _devices = new();

        public IReadOnlyList<string> Devices => _devices;
        public bool IsMuted
        {
            get => _provider != null && _provider.IsMuted;
            set { if (_provider != null) _provider.IsMuted = value; }
        }

        public void Initialize()
        {
            if (_settings == null)
                _settings = Resources.Load<VoiceSettings>("VoiceSettings");

            if (_settings == null)
            {
                Debug.LogWarning("[VoiceSystem] 未找到 VoiceSettings，语音功能禁用。");
                return;
            }

            RefreshDevices();
        }

        public void Shutdown()
        {
            OpusCodec.Shutdown();
            RnnoiseDenoiser.Shutdown();
        }

        public void RefreshDevices()
        {
            _devices.Clear();
            _devices.AddRange(Microphone.devices);
        }

        public void SwitchMicrophone(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return;
            _input?.SwitchDevice(deviceName);
        }
    }
}
