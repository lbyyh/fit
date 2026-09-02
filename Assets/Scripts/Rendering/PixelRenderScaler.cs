using UnityEngine;
using UnityEngine.UI;

namespace Fit.Rendering
{
    /// <summary>
    /// 低分辨率渲染缩放器 —— Q1「低分辨率 3D」的实现。
    ///
    /// 【原理】
    /// 世界相机渲染到一张低分辨率 RenderTexture，再用**点采样**（Point）放大铺满屏幕。
    /// 点采样是关键 —— 用双线性过滤会糊成一团，就没有像素味了。
    ///
    /// 【为什么 HUD 不走低分辨率】
    /// 血条、弹药、威胁指示器如果也降到 640×360，文字会糊到看不清。
    /// 所以只有**世界渲染**降分辨率，UI 保持原生分辨率叠在上层。
    /// 这也是为什么用 RawImage 而不是全局 RenderScale —— 前者能精确控制分层。
    ///
    /// 【与弹幕可读性的冲突（重要）】
    /// 分辨率越低，远处弹体占的像素越少，越难看清 —— 这和 ID-007 是对着干的。
    /// 建议默认用 640×360 而不是 320×180。
    /// 如果一定要用 320，必须同时把弹体半径调大（Projectile._radius），
    /// 否则远距离弹幕会完全不可见。
    ///
    /// 【档位设计】
    /// 三档直接给玩家在设置里选，因为"像素味"是主观偏好，
    /// 有人就是喜欢糊，有人看得难受。
    /// </summary>
    [ExecuteAlways]
    public sealed class PixelRenderScaler : MonoBehaviour
    {
        public enum ResolutionTier
        {
            /// <summary>320×180 —— 重像素味，可读性最差</summary>
            Retro320 = 180,
            /// <summary>640×360 —— 推荐默认，像素味与可读性的平衡点</summary>
            Standard640 = 360,
            /// <summary>960×540 —— 轻度像素化</summary>
            Light960 = 540,
            /// <summary>原生分辨率，关闭像素化</summary>
            Native = 0,
        }

        [Header("引用")]
        [SerializeField] private Camera _worldCamera;
        [SerializeField] private RawImage _outputImage;
        [SerializeField] private Canvas _outputCanvas;

        [Header("配置")]
        [SerializeField] private ResolutionTier _tier = ResolutionTier.Standard640;
        [Tooltip("按高度计算，宽度跟随屏幕宽高比")]
        [SerializeField] private bool _matchScreenAspect = true;
        [SerializeField] private FilterMode _filterMode = FilterMode.Point;

        [Header("后处理")]
        [Tooltip("轻微色阶量化，增强复古感。0 = 关闭。")]
        [SerializeField, Range(0f, 1f)] private float _colorQuantize;

        public ResolutionTier Tier => _tier;

        private RenderTexture _target;
        private int _lastWidth;
        private int _lastHeight;

        private void OnEnable() => Apply();

        private void OnDisable() => Release();

        private void Update()
        {
            // 窗口尺寸变化（拖动、切换全屏）时重建 RT
            if (_lastWidth == Screen.width && _lastHeight == Screen.height) return;
            Apply();
        }

        public void SetTier(ResolutionTier tier)
        {
            _tier = tier;
            Apply();
        }

        [ContextMenu("Apply")]
        public void Apply()
        {
            if (_worldCamera == null) _worldCamera = GetComponent<Camera>();
            if (_worldCamera == null) return;

            Release();

            int targetHeight = (int)_tier;

            // Native 档：不接管渲染，直接输出到屏幕
            if (targetHeight <= 0)
            {
                _worldCamera.targetTexture = null;
                if (_outputImage != null) _outputImage.gameObject.SetActive(false);
                CacheScreenSize();
                return;
            }

            int height = Mathf.Max(60, targetHeight);
            int width = _matchScreenAspect
                ? Mathf.RoundToInt(height * ((float)Screen.width / Mathf.Max(1, Screen.height)))
                : Mathf.RoundToInt(height * (16f / 9f));

            _target = new RenderTexture(width, height, 24, RenderTextureFormat.Default)
            {
                filterMode = _filterMode,
                antiAliasing = 1,
                wrapMode = TextureWrapMode.Clamp
            };
            _target.Create();

            _worldCamera.targetTexture = _target;

            if (_outputImage != null)
            {
                _outputImage.gameObject.SetActive(true);
                _outputImage.texture = _target;
                // RawImage 自身也要点采样，否则 UI 层的双线性会抵消掉 RT 的硬边
                if (_outputImage.texture != null)
                    _outputImage.texture.filterMode = _filterMode;
            }

            if (_outputCanvas != null)
                _outputCanvas.sortingOrder = -100;   // 保证 HUD 画在它上面

            CacheScreenSize();
        }

        private void CacheScreenSize()
        {
            _lastWidth = Screen.width;
            _lastHeight = Screen.height;
        }

        private void Release()
        {
            if (_worldCamera != null) _worldCamera.targetTexture = null;

            if (_target != null)
            {
                if (Application.isPlaying) Destroy(_target);
                else DestroyImmediate(_target);
                _target = null;
            }
        }

        private void OnValidate()
        {
            if (!Application.isPlaying) return;
            Apply();
        }
    }
}
