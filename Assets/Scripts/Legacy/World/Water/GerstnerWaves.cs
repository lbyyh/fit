using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Fit.World.Water
{
    /// <summary>
    /// Gerstner 波水面高度场。
    ///
    /// How to Fish 的水面用的是 Gerstner 波 + 分块瓦片：
    /// 代码里有 GerstnerWave、GetWaterHeight、GetWaterInfo、SnapAllWaterTiles。
    ///
    /// 为什么选 Gerstner 而不是简单的正弦叠加：
    ///   - 正弦波只有垂直位移，波峰是圆的，看着像果冻；
    ///   - Gerstner 波额外做水平位移，波峰会变尖，接近真实海浪；
    ///   - 代价是水平位移会让顶点在波峰处交叉（"打结"），需要控制陡度参数 Q。
    ///
    /// 为什么用 Burst + Jobs：
    ///   水面高度是 CPU 端每帧要查询几百次的函数（浮力、船体、玩家游泳判定、
    ///   粒子落点…）。纯 C# 循环在 8 波叠加时会吃掉 2~3ms，
    ///   Burst 向量化后能压到 0.1ms 量级。
    /// </summary>
    public sealed class GerstnerWaves : MonoBehaviour
    {
        [System.Serializable]
        public struct Wave
        {
            public float2 Direction;   // 归一化
            public float Amplitude;
            public float Wavelength;
            public float Speed;
            public float Steepness;    // Q，0~1，超过 1/waveCount 会打结
        }

        [Header("波参数")]
        [SerializeField] private Wave[] _waves =
        {
            new Wave { Direction = new float2(1, 0),   Amplitude = 0.35f, Wavelength = 32f, Speed = 1.1f, Steepness = 0.35f },
            new Wave { Direction = new float2(0.7f, 0.7f), Amplitude = 0.22f, Wavelength = 19f, Speed = 1.4f, Steepness = 0.30f },
            new Wave { Direction = new float2(-0.4f, 0.9f), Amplitude = 0.14f, Wavelength = 11f, Speed = 1.8f, Steepness = 0.25f },
            new Wave { Direction = new float2(0.9f, -0.3f), Amplitude = 0.07f, Wavelength = 6f,  Speed = 2.3f, Steepness = 0.20f }
        };

        [Header("性能")]
        [SerializeField] private int _batchQueryCapacity = 256;

        private NativeArray<WaveData> _waveData;
        private bool _allocated;

        public float BaseLevel => transform.position.y;

        private void OnEnable() => Allocate();
        private void OnDisable() => Dispose();

        private void Allocate()
        {
            if (_allocated) return;

            _waveData = new NativeArray<WaveData>(_waves.Length, Allocator.Persistent);
            for (int i = 0; i < _waves.Length; i++)
                _waveData[i] = WaveData.From(_waves[i]);

            _allocated = true;
        }

        private void Dispose()
        {
            if (!_allocated) return;
            _waveData.Dispose();
            _allocated = false;
        }

        /// <summary>
        /// 单点查询高度。用于玩家游泳判定、单个浮标等低频调用。
        /// 高频批量查询请用 <see cref="SampleBatch"/>。
        /// </summary>
        public float SampleHeight(float x, float z, float time)
        {
            float height = 0f;
            float2 pos = new float2(x, z);

            for (int i = 0; i < _waveData.Length; i++)
            {
                var w = _waveData[i];
                float k = 2f * math.PI / w.Wavelength;
                float c = math.sqrt(9.81f / k);
                float f = k * (math.dot(w.Direction, pos) - c * time * w.Speed);

                height += w.Amplitude * math.sin(f);
            }

            return BaseLevel + height;
        }

        /// <summary>
        /// 批量查询。船体浮力、渔网、大面积粒子落点都走这里。
        /// 返回 NativeArray 由调用方负责 Dispose。
        /// </summary>
        public NativeArray<float3> SampleBatch(NativeArray<float2> positions, float time)
        {
            var result = new NativeArray<float3>(positions.Length, Allocator.TempJob);

            var job = new GerstnerJob
            {
                Waves = _waveData,
                Positions = positions,
                Time = time,
                BaseLevel = BaseLevel,
                Results = result
            };

            job.Schedule(positions.Length, _batchQueryCapacity).Complete();
            return result;
        }

        [BurstCompile]
        private struct GerstnerJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<WaveData> Waves;
            [ReadOnly] public NativeArray<float2> Positions;
            [ReadOnly] public float Time;
            [ReadOnly] public float BaseLevel;

            [WriteOnly] public NativeArray<float3> Results;

            public void Execute(int index)
            {
                float2 pos = Positions[index];
                float height = 0f;
                float2 displacement = float2.zero;

                for (int i = 0; i < Waves.Length; i++)
                {
                    var w = Waves[i];

                    float k = 2f * math.PI / w.Wavelength;
                    float c = math.sqrt(9.81f / k);
                    float f = k * (math.dot(w.Direction, pos) - c * Time * w.Speed);
                    float a = w.Steepness / k;

                    height += w.Amplitude * math.sin(f);

                    // Gerstner 的水平位移：让波峰变尖
                    displacement += w.Direction * (a * w.Amplitude * math.cos(f));
                }

                Results[index] = new float3(pos.x + displacement.x,
                                            BaseLevel + height,
                                            pos.y + displacement.y);
            }
        }

        public struct WaveData
        {
            public float2 Direction;
            public float Amplitude;
            public float Wavelength;
            public float Speed;
            public float Steepness;

            public static WaveData From(Wave w)
            {
                return new WaveData
                {
                    Direction = math.normalizesafe(w.Direction, new float2(1, 0)),
                    Amplitude = w.Amplitude,
                    Wavelength = math.max(0.01f, w.Wavelength),
                    Speed = w.Speed,
                    Steepness = w.Steepness
                };
            }
        }
    }
}
