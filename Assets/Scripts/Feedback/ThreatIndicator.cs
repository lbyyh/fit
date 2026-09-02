using System.Collections.Generic;
using UnityEngine;

namespace Fit.Feedback
{
    /// <summary>
    /// 屏幕边缘威胁指示器 —— ID-009 的实现。
    ///
    /// 【这个组件为什么是玩法必需品而非 UI 装饰】
    /// §5.1 冲突一：第一人称 FOV 只有 90-110°，背后 270° 是盲区。
    /// 弹幕从背后飞来时玩家完全看不到，结果就是"莫名其妙地死"。
    /// 屏幕边缘红光把不可见的威胁转成可见的方向提示，
    /// 是让"第一人称 + 弹幕"这个组合成立的**核心补偿机制**。
    ///
    /// 【设计要点】
    ///   1. 指示的是"方向"而不是"精确位置" —— 玩家只需要知道往哪躲
    ///   2. 背后的威胁用更明显的脉动（背后完全看不见，需要更强的提示）
    ///   3. 威胁消失立刻回收，避免残留误导
    ///   4. 用对象池，弹幕场景下威胁源会频繁进出
    ///
    /// 【与 Telegraph 的配合】
    /// 敌人在**前摇开始**时就注册威胁（见 EnemyBase.BeginAttack），
    /// 而不是等子弹飞出来。这样提示出现在"可以开始躲"的时刻，而不是"已经来不及"的时刻。
    /// </summary>
    public sealed class ThreatIndicator : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private RectTransform _indicatorPrefab;
        [SerializeField] private Camera _camera;

        [Header("配置")]
        [Tooltip("指示器距屏幕中心的距离（屏幕短边的比例）")]
        [SerializeField, Range(0.3f, 0.5f)] private float _edgeRadiusRatio = 0.42f;
        [SerializeField] private float _behindPulseSpeed = 6f;
        [SerializeField] private float _behindExtraScale = 1.35f;
        [SerializeField] private int _poolSize = 12;

        [Header("可见性")]
        [Tooltip("正前方多少度内不显示提示（视野内已经能看到了，再提示是噪音）")]
        [SerializeField, Range(20f, 90f)] private float _frontalCullAngle = 45f;
        [SerializeField] private float _maxDisplayDistance = 40f;

        private readonly List<Threat> _threats = new();
        private readonly List<RectTransform> _pool = new();
        private int _activeCount;

        private struct Threat
        {
            public Transform Source;
            public float ExpireTime;
            public bool Heavy;
        }

        private void Awake()
        {
            if (_camera == null) _camera = Camera.main;
            BuildPool();
        }

        private void BuildPool()
        {
            if (_indicatorPrefab == null) return;

            for (int i = 0; i < _poolSize; i++)
            {
                var rt = Instantiate(_indicatorPrefab, _canvas != null ? _canvas.transform : transform);
                rt.gameObject.SetActive(false);
                _pool.Add(rt);
            }
        }

        /// <summary>
        /// 注册一个威胁。重复注册同一 Source 会刷新过期时间。
        /// </summary>
        public void RegisterThreat(Transform source, float duration = 2f, bool heavy = false)
        {
            if (source == null) return;

            for (int i = 0; i < _threats.Count; i++)
            {
                if (_threats[i].Source != source) continue;

                var refreshed = _threats[i];
                refreshed.ExpireTime = Time.time + duration;
                refreshed.Heavy = heavy;
                _threats[i] = refreshed;
                return;
            }

            _threats.Add(new Threat
            {
                Source = source,
                ExpireTime = Time.time + duration,
                Heavy = heavy
            });
        }

        public void Clear() => _threats.Clear();

        private void LateUpdate()
        {
            _threats.RemoveAll(t => t.Source == null || Time.time > t.ExpireTime);

            _activeCount = 0;
            foreach (var threat in _threats)
            {
                if (_activeCount >= _pool.Count) break;
                if (TryPlace(threat)) _activeCount++;
            }

            for (int i = _activeCount; i < _pool.Count; i++)
                _pool[i].gameObject.SetActive(false);
        }

        private bool TryPlace(in Threat threat)
        {
            if (_camera == null) return false;

            Vector3 toThreat = threat.Source.position - _camera.transform.position;
            float distance = toThreat.magnitude;
            if (distance > _maxDisplayDistance) return false;

            Vector3 local = _camera.transform.InverseTransformDirection(toThreat);
            bool behind = local.z <= 0f;

            // 视野正前方的威胁不需要提示 —— 玩家已经看见了
            float angle = Vector3.Angle(_camera.transform.forward, toThreat);
            if (!behind && angle < _frontalCullAngle) return false;

            // 把三维方向压成屏幕平面上的二维角度
            // 背后的威胁 x 取反，让它出现在"身后对应的那一侧"
            float screenX = behind ? -local.x : local.x;
            float screenAngle = Mathf.Atan2(screenX, Mathf.Abs(local.z) + 0.001f);

            // 正后方时 z 接近 0，角度趋近 ±90°，强制推到屏幕下方更符合直觉
            if (behind && Mathf.Abs(local.z) < 0.5f)
                screenAngle = screenX >= 0f ? Mathf.PI * 0.5f : -Mathf.PI * 0.5f;

            float radius = Mathf.Min(Screen.width, Screen.height) * _edgeRadiusRatio;
            float x = Mathf.Sin(screenAngle) * radius;
            float y = Mathf.Cos(screenAngle) * radius * 0.6f - radius * 0.15f;

            var rt = _pool[_activeCount];
            rt.gameObject.SetActive(true);
            rt.localPosition = new Vector3(x, y, 0f);

            // 指示器的朝向指向威胁，背后的做脉动放大
            float rotationZ = -screenAngle * Mathf.Rad2Deg;
            float scale = threat.Heavy ? 1.25f : 1f;
            if (behind)
                scale *= _behindExtraScale * (1f + Mathf.Sin(Time.time * _behindPulseSpeed) * 0.15f);

            rt.localRotation = Quaternion.Euler(0f, 0f, rotationZ);
            rt.localScale = Vector3.one * scale;

            return true;
        }
    }
}
