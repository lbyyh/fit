using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

namespace Fit.Voice
{
    /// <summary>
    /// 语音的网络传输层。
    ///
    /// 沿用 How to Fish 的 MetaVoiceChat.NetProviders.FishNet：
    ///   本地采集 → 编码 → ServerRpc 发给房主 → 房主 ObserversRpc 广播给其余客户端。
    ///
    /// 这是"星型中继"而非 P2P 网状：
    ///   - 优点：实现简单，N 个玩家只需 N 条上行 + N 条下行（房主承担 N²的下行总量）；
    ///   - 缺点：房主带宽成为瓶颈。8 人房间 × 16kbps ≈ 房主需要 900kbps 上行，
    ///     这是选人上限时的重要约束。
    ///
    /// 如果后续要扩到 16 人以上，必须改成 P2P 网状或引入语音服务器（Vivox / Steam Voice）。
    /// </summary>
    public sealed class FishNetVoiceProvider : NetworkBehaviour
    {
        [SerializeField] private VoiceSettings _settings;
        [SerializeField] private int _maxBufferedFrames = 6;

        private MicrophoneInput _mic;
        private readonly Dictionary<int, VoicePlayback> _remoteVoices = new();

        private float _frameAccumulator;

        public bool IsMuted
        {
            get => _mic != null && _mic.Muted;
            set { if (_mic != null) _mic.Muted = value; }
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            if (_settings == null)
                _settings = Resources.Load<VoiceSettings>("VoiceSettings");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            if (!base.IsOwner) return;

            OpusCodec.Initialize(_settings.SampleRate, 1, _settings.FrameMilliseconds,
                                 _settings.Bitrate, _settings.UseForwardErrorCorrection);
            RnnoiseDenoiser.Initialize();

            _mic = new MicrophoneInput(_settings);
            _mic.OnFrameEncoded += HandleLocalFrame;
            _mic.Start();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();

            if (_mic != null)
            {
                _mic.OnFrameEncoded -= HandleLocalFrame;
                _mic.Dispose();
                _mic = null;
            }

            foreach (var kv in _remoteVoices)
                Destroy(kv.Value.gameObject);

            _remoteVoices.Clear();
        }

        private void Update()
        {
            if (base.IsOwner)
                _mic?.Update();

            // 限制发送频率，防止高帧率下把房主上行打满
            _frameAccumulator += Time.deltaTime;
        }

        private void HandleLocalFrame(byte[] encoded, int frameSize)
        {
            if (_frameAccumulator < 1f / _settings.MaxFramesPerSecond)
                return;

            _frameAccumulator = 0f;
            UploadFrame(encoded);
        }

        /// <summary>本地帧上行到房主。</summary>
        [ServerRpc(RunLocally = false)]
        private void UploadFrame(byte[] encoded)
        {
            RelayFrame(base.Owner.ClientId, encoded);
        }

        /// <summary>房主广播给除发送者外的所有人。</summary>
        [ObserversRpc(ExcludeOwner = true, ExcludeServer = false)]
        private void RelayFrame(int senderId, byte[] encoded)
        {
            if (senderId == base.LocalConnection.ClientId)
                return; // 自己不需要听自己的回声

            GetOrCreatePlayback(senderId).Enqueue(encoded);
        }

        private VoicePlayback GetOrCreatePlayback(int senderId)
        {
            if (_remoteVoices.TryGetValue(senderId, out var existing) && existing != null)
                return existing;

            var go = new GameObject($"Voice_{senderId}");
            go.transform.SetParent(transform, false);

            var playback = go.AddComponent<VoicePlayback>();
            playback.Configure(_settings, _maxBufferedFrames);
            _remoteVoices[senderId] = playback;

            return playback;
        }
    }
}
