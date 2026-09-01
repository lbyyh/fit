using UnityEngine;

namespace Fit.Voice
{
    /// <summary>
    /// 语音配置。
    ///
    /// 参考 How to Fish 的 MetaVoiceChat 参数：
    ///   - VoiceInputType：自由麦 vs 按键说话。社交向 co-op 建议默认自由麦 + 噪声门，
    ///     因为钓鱼/开船这种慢节奏玩法里，按键说话会显著降低交流意愿。
    ///   - NoiseGate：配合 RNNoise 使用。RNNoise 擅长稳态噪声（风扇、机械键盘），
    ///     但对突发噪声（敲桌子）效果一般，所以前面还要加一道能量门。
    /// </summary>
    [CreateAssetMenu(menuName = "Fit/Voice Settings", fileName = "VoiceSettings")]
    public sealed class VoiceSettings : ScriptableObject
    {
        [Header("采集")]
        [Range(8000, 48000)]
        [Tooltip("Opus 原生支持 8/12/16/24/48 kHz。语音用 24k 足够，48k 意义不大还吃带宽。")]
        public int SampleRate = 24000;

        [Range(10, 60)]
        [Tooltip("单帧毫秒数。20ms 是延迟与包开销的最佳平衡点。")]
        public int FrameMilliseconds = 20;

        [Range(0.1f, 5f)]
        public float MicrophoneGain = 1.4f;

        [Header("编码")]
        [Range(6000, 64000)]
        [Tooltip("Opus 目标码率。16kbps 在无背景音乐时接近透明音质。")]
        public int Bitrate = 16000;

        public bool UseForwardErrorCorrection = true;

        [Header("噪声抑制")]
        public bool EnableRnnoise = true;

        [Range(0f, 0.2f)]
        [Tooltip("能量噪声门。低于此 RMS 的帧直接丢弃，连编码都省了。")]
        public float NoiseGateThreshold = 0.012f;

        [Header("传输")]
        [Range(1, 10)]
        [Tooltip("每秒向服务器发送的语音帧数上限。中继架构下这是房主带宽的主要开销来源。")]
        public int MaxFramesPerSecond = 50;

        [Header("3D 音效")]
        public float MinDistance = 2f;
        public float MaxDistance = 35f;

        public int FrameSize => SampleRate * FrameMilliseconds / 1000;

        /// <summary>单帧样本数（单声道）。</summary>
        public int SamplesPerFrame => FrameSize;
    }
}
