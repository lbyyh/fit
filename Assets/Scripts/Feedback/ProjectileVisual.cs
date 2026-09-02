using UnityEngine;

namespace Fit.Feedback
{
    /// <summary>
    /// 投射物视觉 —— ID-007「明显发光轮廓」的实现。
    ///
    /// 【为什么这个组件是必选的（RequireComponent）】
    /// §5.1 冲突一：第一人称 FOV 只有 90-110°，背后 270° 看不见。
    /// 弹幕能不能玩，几乎完全取决于"能不能在余光里察觉到子弹来了"。
    /// 这不是锦上添花，是玩法成立的前提，所以强制每个 Projectile 都带上。
    ///
    /// 【低分辨率 3D 下的特殊问题（Q1）】
    /// 渲染分辨率降到 640×360 后，一个 0.2 米的弹体在远处只占几个像素。
    /// 深色材质 + 暗地牢 = 完全看不见。所以：
    ///   1. 材质必须自发光（不受场景光照影响）
    ///   2. 加一圈放大的背面外壳做描边（inverted hull），保证在亮背景上也能分辨
    ///   3. 拖尾用不受光的材质，长度足够长，形成"轨迹可读性"
    ///
    /// 描边与拖尾的视觉规格应由美术统一配，这里只提供挂载点与运行时控制。
    /// </summary>
    [ExecuteAlways]
    public sealed class ProjectileVisual : MonoBehaviour
    {
        [Header("自发光")]
        [SerializeField] private Color _emissiveColor = new(1f, 0.45f, 0.15f, 1f);
        [SerializeField, Range(0.5f, 8f)] private float _emissiveIntensity = 3f;

        [Header("描边（保证亮背景可辨）")]
        [SerializeField] private bool _useOutline = true;
        [SerializeField] private Color _outlineColor = Color.white;
        [SerializeField, Range(1.02f, 1.6f)] private float _outlineScale = 1.18f;

        [Header("拖尾")]
        [SerializeField] private TrailRenderer _trail;
        [SerializeField] private float _trailSeconds = 0.25f;

        [Header("预警")]
        [Tooltip("距离玩家多近时触发提示音（0 = 关闭）。配合 ID-009 屏幕边缘提示。")]
        [SerializeField] private float _proximityWarnDistance = 6f;
        [SerializeField] private AudioClip _proximityWarnSound;

        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private GameObject _outline;
        private bool _warned;

        private void Awake()
        {
            Cache();
            ApplyEmissive();
            BuildOutline();
            ConfigureTrail();
        }

        private void OnEnable()
        {
            _warned = false;
            if (_trail != null) _trail.Clear();
        }

        private void Cache()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _block = new MaterialPropertyBlock();
        }

        /// <summary>
        /// 把自发光写进 MaterialPropertyBlock。
        /// 用 MPB 而不是直接改 material，是为了让同预制体的不同实例能有不同的
        /// 发光强度（比如精英怪的弹更亮），且不会造成材质实例化爆炸。
        /// </summary>
        private void ApplyEmissive()
        {
            if (_renderers == null) return;

            foreach (var r in _renderers)
            {
                r.GetPropertyBlock(_block);
                _block.SetColor(EmissiveColorId, _emissiveColor * _emissiveIntensity);
                r.SetPropertyBlock(_block);
            }
        }

        /// <summary>
        /// 生成放大版背面外壳做描边。
        /// 需要在 URP 里用一个 Backface 渲染的材质（Cull Front），由美术提供。
        /// 拿不到材质时静默跳过，不影响运行。
        /// </summary>
        private void BuildOutline()
        {
            if (!_useOutline || _outline != null) return;

            var source = GetComponentInChildren<MeshFilter>();
            if (source == null || source.sharedMesh == null) return;

            _outline = new GameObject("Outline");
            _outline.transform.SetParent(transform, false);
            _outline.transform.localScale = Vector3.one * _outlineScale;

            var mf = _outline.AddComponent<MeshFilter>();
            mf.sharedMesh = source.sharedMesh;

            var mr = _outline.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // 优先用配置好的描边材质；没有就用默认材质兜底，保证不报错
            mr.sharedMaterial = OutlineMaterial != null
                ? OutlineMaterial
                : new Material(Shader.Find("Universal Render Pipeline/Unlit"))
                {
                    color = _outlineColor
                };
        }

        private void ConfigureTrail()
        {
            if (_trail == null) return;
            _trail.time = _trailSeconds;
            _trail.emitting = true;
        }

        private void Update()
        {
            if (_proximityWarnDistance <= 0f || _warned) return;
            if (Camera.main == null) return;

            float d = Vector3.Distance(transform.position, Camera.main.transform.position);
            if (d > _proximityWarnDistance) return;

            _warned = true;
            if (_proximityWarnSound != null)
                AudioSource.PlayClipAtPoint(_proximityWarnSound, transform.position);
        }

        private void OnValidate()
        {
            if (!Application.isPlaying) return;
            Cache();
            ApplyEmissive();
            ConfigureTrail();
        }

        /// <summary>描边材质（Cull Front 的 Unlit）。由美术在 ProjectSettings 里统一指定。</summary>
        public static Material OutlineMaterial { get; set; }

        private static readonly int EmissiveColorId = Shader.PropertyToID("_EmissionColor");
    }
}
