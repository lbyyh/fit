using Fit.Gameplay.Farming;
using UnityEngine;

namespace Fit.Gameplay.Tools
{
    /// <summary>
    /// 农具运行时逻辑。挂在玩家身上，持有一把 ToolData。
    ///
    /// 【与 WeaponBase 的分工】
    /// 两者都实现 IHoldable，由 Hotbar 统一调度，但内部完全不同：
    ///   - 武器关心伤害 / 弹匣 / 后坐力，朝敌人开火
    ///   - 工具关心"准星指着哪块地"，朝地面作业
    /// 分工明确，互不污染。
    ///
    /// 【为什么只响应点按，不响应按住】
    /// 锄地 / 播种是一次性动作，按住连发会瞬间锄完一整畦，
    /// 既没有节奏感也会让玩家觉得"这农具是电动的"。
    /// 冷却（UseCooldown）兜住连点。
    /// </summary>
    public sealed class ToolBase : MonoBehaviour, IHoldable
    {
        [Header("配置")]
        [SerializeField] private ToolData _tool;

        [Header("引用")]
        [SerializeField] private Camera _aimCamera;
        [Tooltip("射线起点。一般直接留空用相机即可。")]
        [SerializeField] private Transform _useOrigin;
        [Tooltip("手持模型挂点。留空则挂在相机下。")]
        [SerializeField] private Transform _viewModelAnchor;

        [Header("挥动表现")]
        [SerializeField, Tooltip("一次挥动动作的时长（秒），纯视觉。")]
        private float _swingSeconds = 0.28f;
        [SerializeField, Tooltip("挥动最大角度（度）。")]
        private float _swingDegrees = 55f;

        private float _nextUseTime;
        private float _swingTimeLeft;
        private GameObject _viewModel;

        public ToolData Data => _tool;
        public string DisplayName => _tool != null ? _tool.DisplayName : string.Empty;
        public Sprite Icon => _tool != null ? _tool.Icon : null;

        public void SetTool(ToolData data)
        {
            _tool = data;
            RebuildViewModel();
        }

        // ---------------------------------------------------------------- IHoldable

        public void OnSelect()
        {
            RebuildViewModel();
            if (_viewModel != null) _viewModel.SetActive(true);
        }

        public void OnDeselect()
        {
            if (_viewModel != null) _viewModel.SetActive(false);
        }

        public void Use(bool held, bool pressed)
        {
            if (_tool == null || !pressed) return;
            if (Time.time < _nextUseTime) return;

            if (!TryGetTarget(out var land, out var hit)) return;

            bool applied = _tool.Kind switch
            {
                ToolKind.Hoe => land.Till(hit.point),
                ToolKind.Seed => land.Plant(hit.point, _tool.Crop),
                ToolKind.WateringCan => land.Water(hit.point),
                _ => false
            };

            if (!applied) return;   // 没生效就不进冷却，避免"对着石头锄了一下然后干等"

            _nextUseTime = Time.time + _tool.UseCooldown;
            _swingTimeLeft = _swingSeconds;

            if (_tool.UseSound != null)
                AudioSource.PlayClipAtPoint(_tool.UseSound, hit.point);
        }

        public void UseSecondary(bool held, bool pressed)
        {
            // 预留：右键收回工具 / 切换种子种类。阶段 1 暂无行为。
        }

        // ---------------------------------------------------------------- 内部

        /// <summary>
        /// 找出准星指向的耕地。
        ///
        /// 【为什么用 RaycastAll 而不是单个 Raycast】
        /// 玩家低头锄地时，射线第一下很可能先打在自己身上（CharacterController 的胶囊），
        /// 单发 Raycast 会直接判定"没找到"，表现为"明明对着地按了却没反应"。
        /// 这里取所有命中里最近的一块 Farmland，并跳过自己。
        /// 工具使用频率很低（有使用冷却），多一次数组分配完全可以接受。
        /// </summary>
        private bool TryGetTarget(out Farmland land, out RaycastHit hit)
        {
            land = null;
            hit = default;

            var cam = _aimCamera != null ? _aimCamera : Camera.main;
            if (cam == null) return false;

            Vector3 origin = _useOrigin != null ? _useOrigin.position : cam.transform.position;
            Vector3 dir = cam.transform.forward;

            var hits = Physics.RaycastAll(origin, dir, _tool.Range, _tool.TargetMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;   // 跳过自己

                var found = h.collider.GetComponentInParent<Farmland>();
                if (found == null) return false;   // 被蔬菜/建筑挡住了，锄不到后面的地

                land = found;
                hit = h;
                return true;
            }

            return false;
        }

        private void Update() => TickSwing();

        /// <summary>
        /// 挥动动画。纯客户端表现，绝不同步 —— 和武器后坐力同一个道理（§7）。
        ///
        /// 【为什么用 1-cos 曲线】
        /// 线性来回会让动作显得机械；用 (1-cos)/2 做一次"挥出去再收回"，
        /// 起步和收尾都慢、中间快，接近真实挥动，且一定回到初始角度不会累积漂移。
        /// </summary>
        private void TickSwing()
        {
            if (_viewModel == null || _swingTimeLeft <= 0f) return;

            _swingTimeLeft -= Time.deltaTime;

            float t = 1f - Mathf.Clamp01(_swingTimeLeft / _swingSeconds);
            float curve = (1f - Mathf.Cos(t * Mathf.PI * 2f)) * 0.5f;

            _viewModel.transform.localRotation =
                Quaternion.Euler(-_swingDegrees * curve, 0f, 0f);
        }

        private void RebuildViewModel()
        {
            if (_viewModel != null)
                Destroy(_viewModel);

            if (_tool == null || _tool.ViewModelPrefab == null) return;

            var anchor = _viewModelAnchor != null ? _viewModelAnchor : transform;
            _viewModel = Instantiate(_tool.ViewModelPrefab, anchor);
            _viewModel.transform.localPosition = Vector3.zero;
            _viewModel.transform.localRotation = Quaternion.identity;
        }

        private void OnDestroy()
        {
            if (_viewModel != null) Destroy(_viewModel);
        }
    }
}
