using UnityEngine;

namespace Fit.Voice
{
    /// <summary>
    /// Opus / RNNoise 的托管封装。
    ///
    /// 两个库都不是 UPM 包，需要这样接入：
    ///   1. Concentus      —— NuGet 取 Concentus.dll，放入 Assets/Plugins/
    ///   2. RNNoise4Unity  —— GitHub 取 rnnoise.dll（原生）+ RNNoise4Unity.Runtime.dll（托管）
    ///
    /// 之所以把这两个库包一层而不是直接用：
    ///   - 集中处理初始化失败（原生库缺失时游戏不能崩，静音继续玩）；
    ///   - 便于后续换实现（比如 iOS 上改用原生 AudioToolbox 的 Opus）；
    ///   - 隔离不安全代码，业务层不直接接触 IntPtr。
    ///
    /// 接入前把下面的调用替换成真实 API 即可，调用点已经全部收敛在这一个文件里。
    /// </summary>
    public static class OpusCodec
    {
        private static bool _initialized;
        private static bool _available;

        public static bool IsAvailable => _available;

        public static void Initialize(int sampleRate, int channels, int frameMilliseconds, int bitrate, bool useFec)
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                // Concentus.OpusCodecFactory.CreateEncoder(sampleRate, channels, OpusApplication.Voip)
                // Concentus.OpusCodecFactory.CreateDecoder(sampleRate, channels)
                _available = true;
                Debug.Log($"[Voice] Opus 就绪：{sampleRate}Hz / {channels}ch / {frameMilliseconds}ms / {bitrate}bps");
            }
            catch (System.Exception ex)
            {
                _available = false;
                Debug.LogError($"[Voice] Opus 初始化失败，语音功能将禁用：{ex.Message}");
            }
        }

        /// <summary>编码一帧。返回 null 表示编码失败，调用方应静默丢弃。</summary>
        public static byte[] Encode(float[] pcm)
        {
            if (!_available) return null;

            try
            {
                // 真实实现：
                //   var buffer = new byte[MaxPacketSize];
                //   int len = _encoder.Encode(pcm, 0, pcm.Length, buffer, 0, buffer.Length);
                //   return buffer.AsSpan(0, len).ToArray();
                return new byte[0];
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Voice] 编码失败：{ex.Message}");
                return null;
            }
        }

        public static float[] Decode(byte[] packet, int frameSize)
        {
            if (!_available || packet == null || packet.Length == 0)
                return null;

            try
            {
                // 真实实现：
                //   var pcm = new float[frameSize];
                //   _decoder.Decode(packet, 0, packet.Length, pcm, 0, frameSize, useFec: true);
                //   return pcm;
                return new float[frameSize];
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Voice] 解码失败：{ex.Message}");
                return null;
            }
        }

        public static void Shutdown()
        {
            _initialized = false;
            _available = false;
        }
    }

    /// <summary>RNNoise 降噪封装。RNNoise 要求 48kHz 单声道输入。</summary>
    public static class RnnoiseDenoiser
    {
        private const int RequiredSampleRate = 48000;

        private static bool _initialized;
        private static bool _available;

        public static bool IsAvailable => _available;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                // 真实实现：Adrenak.RNNoise4Unity.RnnoiseDenoiser 实例化
                _available = true;
            }
            catch (System.Exception ex)
            {
                _available = false;
                Debug.LogWarning($"[Voice] RNNoise 不可用，将跳过降噪：{ex.Message}");
            }
        }

        /// <summary>原地降噪。约定输入为 480 采样点（48kHz 下的 10ms 帧）。</summary>
        public static void Process(float[] samples)
        {
            if (!_available) return;

            try
            {
                // 真实实现：_denoiser.Denoise(samples)
            }
            catch
            {
                // 降噪失败不应影响通话，静默跳过
            }
        }

        /// <summary>
        /// RNNoise 固定吃 48kHz，如果采集用 24kHz 需要先上采样。
        /// 线性插值对 2 倍上采样足够，听感上无差别。
        /// </summary>
        public static float[] Resample(float[] input, int fromRate, int toRate)
        {
            if (fromRate == toRate) return input;

            double ratio = (double)toRate / fromRate;
            int length = (int)(input.Length * ratio);
            var output = new float[length];

            for (int i = 0; i < length; i++)
            {
                double pos = i / ratio;
                int left = (int)pos;
                int right = System.Math.Min(left + 1, input.Length - 1);
                float frac = (float)(pos - left);
                output[i] = Mathf.Lerp(input[left], input[right], frac);
            }

            return output;
        }

        public static void Shutdown()
        {
            _initialized = false;
            _available = false;
        }
    }
}
