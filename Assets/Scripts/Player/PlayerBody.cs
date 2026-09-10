using UnityEngine;

namespace Fit.Player
{
    /// <summary>
    /// 玩家的全身模型。
    ///
    /// 【为什么第一人称还需要全身模型】
    /// 本地玩家看不见自己，但队友看得见。这是 1-5 人合作游戏，身体是队友判断
    /// 「谁在哪、朝哪个方向」的唯一依据 —— 没有它，多人游戏里你就是个隐形人。
    ///
    /// 【为什么本地玩家要隐藏身体】
    /// 相机在 1.6m 高，身体模型也在同一位置。不隐藏的话会看到模型内部
    /// （背面剔除后的空洞、穿插的手臂），第一人称体验直接崩掉。
    /// 这是所有 FPS 的标准做法：本地只渲染手臂(viewmodel)，全身留给别人看。
    ///
    /// 【无骨骼、无动画的补偿】
    /// 当前模型是 AI 生成的静态网格，没有任何骨骼绑定，做不了走路动画。
    /// 这里用程序化的上下浮动 + 左右倾斜模拟步频，至少让它不像"滑行的雕像"。
    /// 等以后有带骨骼的模型，删掉 Update 里的摇摆即可，其余逻辑不用动。
    /// </summary>
    public sealed class PlayerBody : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField, Tooltip("模型根节点。留空则自动取第一个子物体。")]
        private Transform _modelRoot;
        [SerializeField, Tooltip("用于读取移动速度。留空则自动查找。")]
        private FPSController _controller;

        [Header("显隐")]
        [SerializeField, Tooltip("本地玩家是否隐藏身体。第一人称必须为 true。\n" +
                                 "主地图的皮肤展示台会单独实例化一个模型，不受这个开关影响。")]
        private bool _hideForOwner = true;

        [Header("摇摆（无骨骼动画的权宜方案）")]
        [SerializeField, Tooltip("步频，单位是「周期/米」。0.35 约等于正常步幅。")]
        private float _bobFrequency = 0.35f;
        [SerializeField] private float _bobAmplitude = 0.05f;
        [SerializeField, Tooltip("左右倾斜角度（度）。")]
        private float _leanAngle = 3.5f;
        [SerializeField, Tooltip("摆动强度的插值速度。太小显得拖沓，太大会抖。")]
        private float _smoothing = 8f;

        private Renderer[] _renderers;
        private Vector3 _baseLocalPos;
        private Quaternion _baseLocalRot;
        private float _phase;
        private float _strength;
        private bool _isLocal;

        /// <summary>当前是否被隐藏（本地玩家或手动调用）。</summary>
        public bool IsHidden { get; private set; }

        private void Awake()
        {
            if (_modelRoot == null && transform.childCount > 0)
                _modelRoot = transform.GetChild(0);

            if (_controller == null)
                _controller = GetComponentInParent<FPSController>();

            _isLocal = IsLocalPlayer();

            if (_modelRoot != null)
            {
                _baseLocalPos = _modelRoot.localPosition;
                _baseLocalRot = _modelRoot.localRotation;
                _renderers = _modelRoot.GetComponentsInChildren<Renderer>(true);
            }

            ApplyVisibility();
        }

        /// <summary>
        /// 判断是不是本地玩家。
        /// 没有 NetworkObject 时（灰盒 / 单机测试场景）一律按本地处理 ——
        /// 那种场景下本来也没有队友看你。
        /// </summary>
        private bool IsLocalPlayer()
        {
            var netObj = GetComponentInParent<FishNet.Object.NetworkObject>();
            return netObj == null || netObj.IsOwner;
        }

        /// <summary>外部强制显隐（例如主地图展示台、观战视角）。</summary>
        public void SetVisible(bool visible)
        {
            IsHidden = !visible;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            bool visible = !IsHidden && !(_hideForOwner && _isLocal);

            // 只关 Renderer 而不是 SetActive(false)：
            // 关掉整个物体会让摇摆逻辑、未来的皮肤切换回调一起停掉。
            if (_renderers != null)
                foreach (var r in _renderers)
                    if (r != null) r.enabled = visible;
        }

        private void Update()
        {
            if (_modelRoot == null) return;

            float speed = _controller != null ? _controller.HorizontalVelocity.magnitude : 0f;

            // 慢走时摆动小，站定时完全消失 —— 静止还晃动会很出戏
            float target = Mathf.Clamp01(speed / 6f);
            _strength = Mathf.Lerp(_strength, target, Time.deltaTime * _smoothing);

            if (_strength < 0.001f)
            {
                // 已经静止，把姿态平滑归位后就不必再算了
                _modelRoot.localPosition = Vector3.Lerp(
                    _modelRoot.localPosition, _baseLocalPos, Time.deltaTime * _smoothing);
                _modelRoot.localRotation = Quaternion.Lerp(
                    _modelRoot.localRotation, _baseLocalRot, Time.deltaTime * _smoothing);
                return;
            }

            _phase += speed * _bobFrequency * Time.deltaTime;

            float t = _phase * Mathf.PI * 2f;
            float bobY = Mathf.Sin(t) * _bobAmplitude * _strength;
            float leanZ = Mathf.Cos(t) * _leanAngle * _strength;

            // 叠加在初始偏移之上，而不是覆盖 —— 否则编辑器里摆好的位置会被吃掉
            _modelRoot.localPosition = _baseLocalPos + new Vector3(0f, bobY, 0f);
            _modelRoot.localRotation = _baseLocalRot * Quaternion.Euler(0f, 0f, leanZ);
        }
    }
}
