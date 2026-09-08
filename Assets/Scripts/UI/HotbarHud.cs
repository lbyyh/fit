using Fit.Gameplay.Tools;
using UnityEngine;
using UnityEngine.UI;

namespace Fit.UI
{
    /// <summary>
    /// 底部工具栏 HUD（3 格）。
    ///
    /// 【为什么动态建而不要求 prefab】
    /// 工具栏是固定结构（N 个格子 + 图标 + 快捷键数字），没有美术排版需求，
    /// 做成 prefab 反而多一个要维护、容易漏绑引用的资产。
    /// 和 ThreatIndicator 一个思路：挂上组件就能用，编辑器生成器只需 AddComponent。
    ///
    /// 【为什么 HUD 不跟着低分辨率渲染缩小】
    /// Q1 的 640×360 只作用于 3D 渲染（PixelRenderScaler）。
    /// HUD 是 ScreenSpaceOverlay 的 Canvas，不受渲染缩放影响 —— 这也正是想要的：
    /// 世界糊成像素块，但工具栏文字保持清晰可读。
    /// </summary>
    public sealed class HotbarHud : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField, Tooltip("留空则场景里自动找。")]
        private Hotbar _hotbar;

        [Header("布局")]
        [SerializeField] private int _slotSize = 64;
        [SerializeField] private int _spacing = 10;
        [SerializeField] private int _bottomMargin = 24;

        [Header("配色")]
        [SerializeField] private Color _normalBg = new(0f, 0f, 0f, 0.45f);
        [SerializeField] private Color _selectedBg = new(1f, 1f, 1f, 0.92f);
        [SerializeField] private Color _normalText = new(1f, 1f, 1f, 0.75f);
        [SerializeField] private Color _selectedText = new(0.08f, 0.08f, 0.08f, 1f);

        [Header("选中表现")]
        [SerializeField, Tooltip("选中格放大的倍率。轻微放大即可，太夸张会挡视线。")]
        private float _selectedScale = 1.14f;

        private sealed class SlotView
        {
            public RectTransform Root;
            public Image Background;
            public Image Icon;
            public Text KeyLabel;
            public Text NameLabel;
        }

        private SlotView[] _views;
        private Canvas _canvas;

        private void Awake()
        {
            if (_hotbar == null)
                _hotbar = GetComponentInParent<Hotbar>() ?? FindObjectOfType<Hotbar>();

            BuildUi();
        }

        private void OnEnable()
        {
            if (_hotbar == null) return;
            _hotbar.OnSelectionChanged += HandleSelectionChanged;
            _hotbar.OnSlotChanged += HandleSlotChanged;
        }

        private void OnDisable()
        {
            if (_hotbar == null) return;
            _hotbar.OnSelectionChanged -= HandleSelectionChanged;
            _hotbar.OnSlotChanged -= HandleSlotChanged;
        }

        private void Start() => RefreshAll();

        // ---------------------------------------------------------------- 构建

        private void BuildUi()
        {
            int count = _hotbar != null ? _hotbar.SlotCount : 3;

            _canvas = GetComponent<Canvas>();
            if (_canvas == null)
            {
                _canvas = gameObject.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 10;
                gameObject.AddComponent<CanvasScaler>();
            }

            float totalWidth = count * _slotSize + (count - 1) * _spacing;

            var container = new GameObject("HotbarSlots");
            var rt = container.AddComponent<RectTransform>();
            rt.SetParent(transform, false);
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, _bottomMargin);
            rt.sizeDelta = new Vector2(totalWidth, _slotSize);

            var layout = container.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = _spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            _views = new SlotView[count];
            for (int i = 0; i < count; i++)
                _views[i] = BuildSlot(container.transform, i, font);
        }

        private SlotView BuildSlot(Transform parent, int index, Font font)
        {
            var go = new GameObject($"Slot_{index + 1}");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(_slotSize, _slotSize);

            var bg = go.AddComponent<Image>();
            bg.color = _normalBg;

            // 图标：撑满格子的大部分，留出一圈内边距
            var iconGo = new GameObject("Icon");
            var iconRt = iconGo.AddComponent<RectTransform>();
            iconRt.SetParent(rt, false);
            iconRt.anchorMin = new Vector2(0.18f, 0.24f);
            iconRt.anchorMax = new Vector2(0.82f, 0.82f);
            iconRt.offsetMin = Vector2.zero;
            iconRt.offsetMax = Vector2.zero;
            var icon = iconGo.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.color = new Color(1f, 1f, 1f, 0f);   // 没图标时透明，靠名字兜底

            // 快捷键数字（左上角）
            var keyGo = new GameObject("Key");
            var keyRt = keyGo.AddComponent<RectTransform>();
            keyRt.SetParent(rt, false);
            keyRt.anchorMin = new Vector2(0f, 1f);
            keyRt.anchorMax = new Vector2(0f, 1f);
            keyRt.pivot = new Vector2(0f, 1f);
            keyRt.anchoredPosition = new Vector2(4f, -2f);
            keyRt.sizeDelta = new Vector2(20f, 18f);
            var key = keyGo.AddComponent<Text>();
            key.font = font;
            key.fontSize = 13;
            key.color = _normalText;
            key.text = (index + 1).ToString();

            // 物品名（底部）。图标缺失时它就是主要信息来源。
            var nameGo = new GameObject("Name");
            var nameRt = nameGo.AddComponent<RectTransform>();
            nameRt.SetParent(rt, false);
            nameRt.anchorMin = new Vector2(0f, 0f);
            nameRt.anchorMax = new Vector2(1f, 0.28f);
            nameRt.offsetMin = new Vector2(2f, 2f);
            nameRt.offsetMax = new Vector2(-2f, 0f);
            var name = nameGo.AddComponent<Text>();
            name.font = font;
            name.fontSize = 12;
            name.alignment = TextAnchor.MiddleCenter;
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            name.color = _normalText;
            name.text = string.Empty;

            return new SlotView { Root = rt, Background = bg, Icon = icon, KeyLabel = key, NameLabel = name };
        }

        // ---------------------------------------------------------------- 刷新

        private void RefreshAll()
        {
            if (_views == null || _hotbar == null) return;

            for (int i = 0; i < _views.Length; i++)
                RefreshSlot(i);

            HandleSelectionChanged(_hotbar.SelectedIndex);
        }

        private void RefreshSlot(int index)
        {
            if (_views == null || index < 0 || index >= _views.Length) return;

            var view = _views[index];
            var item = _hotbar != null ? _hotbar.GetSlot(index) : null;

            if (item == null)
            {
                view.Icon.sprite = null;
                view.Icon.color = new Color(1f, 1f, 1f, 0f);
                view.NameLabel.text = string.Empty;
                return;
            }

            view.Icon.sprite = item.Icon;
            view.Icon.color = item.Icon != null
                ? Color.white
                : new Color(1f, 1f, 1f, 0f);

            // 图标缺失时显示名字，避免格子一片空白
            string name = item.DisplayName ?? string.Empty;
            view.NameLabel.text = item.Icon == null && name.Length > 4
                ? name.Substring(0, 4)
                : name;
        }

        private void HandleSelectionChanged(int index)
        {
            if (_views == null) return;

            for (int i = 0; i < _views.Length; i++)
            {
                bool on = i == index;
                var v = _views[i];

                v.Background.color = on ? _selectedBg : _normalBg;
                v.KeyLabel.color = on ? _selectedText : _normalText;
                v.NameLabel.color = on ? _selectedText : _normalText;
                v.Root.localScale = on ? Vector3.one * _selectedScale : Vector3.one;
            }
        }

        private void HandleSlotChanged(int index) => RefreshSlot(index);
    }
}
