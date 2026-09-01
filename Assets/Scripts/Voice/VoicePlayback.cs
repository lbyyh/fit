using System.Collections.Generic;
using UnityEngine;

namespace Fit.Voice
{
    /// <summary>
    /// 远端语音播放。
    ///
    /// 关键设计：抖动缓冲（jitter buffer）。
    /// 网络到达是不均匀的，直接来一帧播一帧会断断续续。做法是：
    ///   1. 先积累 _warmupFrames 帧再开始播放，用延迟换流畅；
    ///   2. 播放速率根据缓冲水位做微调（0.98x ~ 1.02x），避免缓冲耗尽或无限膨胀；
    ///   3. 缓冲溢出时丢最旧的帧，宁可断一下也不要越播越延迟。
    ///
    /// 这套逻辑没做好，语音就会出现"越聊延迟越大"的经典问题。
    /// </summary>
    public sealed class VoicePlayback : MonoBehaviour
    {
        private readonly Queue<float[]> _jitterBuffer = new();

        private AudioSource _source;
        private AudioClip _clip;
        private VoiceSettings _settings;

        private int _warmupFrames = 2;
        private int _maxBufferedFrames;
        private int _writePosition;
        private float _playbackRate = 1f;

        public void Configure(VoiceSettings settings, int maxBufferedFrames)
        {
            _settings = settings;
            _maxBufferedFrames = maxBufferedFrames;

            _source = gameObject.AddComponent<AudioSource>();
            _source.spatialBlend = 1f;            // 3D 音效靠空间衰减
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = settings.MinDistance;
            _source.maxDistance = settings.MaxDistance;
            _source.dopplerLevel = 0f;            // 语音不要多普勒，听着很怪
            _source.loop = true;

            // 环形缓冲：2 秒容量足够吸收抖动
            _clip = AudioClip.Create($"VoiceClip_{GetInstanceID()}",
                                    settings.SampleRate * 2, 1, settings.SampleRate, false);
            _source.clip = _clip;
            _source.Play();
        }

        public void Enqueue(byte[] encoded)
        {
            var pcm = OpusCodec.Decode(encoded, _settings.SamplesPerFrame);
            if (pcm == null) return;

            _jitterBuffer.Enqueue(pcm);

            if (_jitterBuffer.Count > _maxBufferedFrames)
                _jitterBuffer.Dequeue(); // 丢旧帧，防止延迟累积
        }

        private void Update()
        {
            if (_clip == null)
                return;

            AdjustPlaybackRate();

            int writable = SamplesWritable();
            int frameSize = _settings.SamplesPerFrame;
            int framesToWrite = writable / frameSize;

            for (int f = 0; f < framesToWrite; f++)
            {
                if (_jitterBuffer.Count < _warmupFrames)
                    break; // 缓冲不足，写静音，避免 AudioSource 停掉

                _clip.SetData(_jitterBuffer.Dequeue(), _writePosition);
                _writePosition = (_writePosition + frameSize) % _clip.samples;
            }
        }

        /// <summary>
        /// 自适应播放速率：缓冲多就稍微快放，缓冲少就稍微慢放。
        /// 微调幅度控制在 ±2% 以内，超出这个范围人耳能听出音调变化。
        /// </summary>
        private void AdjustPlaybackRate()
        {
            float fill = (float)_jitterBuffer.Count / _maxBufferedFrames;

            float target = fill switch
            {
                > 0.6f => 1.02f,
                < 0.25f => 0.98f,
                _ => 1f
            };

            _playbackRate = Mathf.Lerp(_playbackRate, target, Time.deltaTime * 2f);
            _source.pitch = _playbackRate;
        }

        private int SamplesWritable()
        {
            int read = _source.timeSamples;
            int write = _writePosition;
            int length = _clip.samples;

            int distance = read - write;
            if (distance <= 0)
                distance += length;

            // 保留一帧余量，防止读写指针相撞
            return Mathf.Max(0, distance - _settings.SamplesPerFrame);
        }

        private void OnDestroy()
        {
            if (_clip != null)
                Destroy(_clip);
        }
    }
}
