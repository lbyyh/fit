using System;
using UnityEngine;

namespace Fit.Gameplay.Tools
{
    /// <summary>
    /// 底部快捷工具栏（默认 3 格）。
    ///
    /// 【为什么是 3 格】
    /// 主地图上玩家同时需要的事就三件：打菜（武器）、锄地（锄头）、播种（种子）。
    /// 再多的格子在第一人称 HUD 上会挤占屏幕，而这个游戏的核心在闯关，
    /// 主地图的农具体系不该长成另一个完整的经营游戏 —— 3 格刚好够用且够克制。
    /// 需求变了改 _slotCount 即可，逻辑与格子数无关。
    ///
    /// 【为什么 Hotbar 只认 IHoldable】
    /// 它不需要知道第 0 格是枪、第 1 格是锄头。
    /// 都当成"能选中、能按左键用"的东西，切换和快捷键逻辑就一份。
    /// 闯关时把第 1、2 格换成手雷 / 道具，这里一行都不用改。
    ///
    /// 【⚠️ 关于左键的归属】
    /// 左键不再由 PlayerMotor 直接喂给 WeaponBase —— 否则拿着锄头还会同时开枪。
    /// 现在统一由本类的 UseCurrent() 分发给当前格。
    /// PlayerMotor 检测到身上有 Hotbar 时会把左键转交过来（见 PlayerMotor.TickWeapon）。
    /// </summary>
    public sealed class Hotbar : MonoBehaviour
    {
        [Header("槽位")]
        [SerializeField, Tooltip("格子数。默认 3。")]
        private int _slotCount = 3;

        [SerializeField, Tooltip("初始槽位内容（拖武器 / 工具组件进来）。\n" +
                                 "运行时也能用 SetSlot() 改，比如捡到新武器。")]
        private MonoBehaviour[] _initialSlots;

        [Header("输入")]
        [SerializeField] private KeyCode[] _slotKeys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3 };
        [SerializeField] private bool _enableScrollWheel = true;
        [SerializeField] private string _scrollAxis = "Mouse ScrollWheel";
        [Tooltip("滚轮灵敏度阈值。太小会因为高精度鼠标一格滚过好几格。")]
        [SerializeField] private float _scrollThreshold = 0.05f;

        private IHoldable[] _slots;
        private int _selected;

        /// <summary>选中的格子变了。参数：新索引。</summary>
        public event Action<int> OnSelectionChanged;
        /// <summary>格子内容变了（换装/清空）。参数：索引。</summary>
        public event Action<int> OnSlotChanged;

        public int SlotCount => _slotCount;
        public int SelectedIndex => _selected;

        public IHoldable Current =>
            _slots != null && _selected >= 0 && _selected < _slots.Length ? _slots[_selected] : null;

        public IHoldable GetSlot(int index)
            => _slots != null && index >= 0 && index < _slots.Length ? _slots[index] : null;

        private void Awake()
        {
            _slots = new IHoldable[Mathf.Max(1, _slotCount)];

            // 场景里预配的槽位（编辑器拖拽 / 生成器用 SerializedObject 赋值）
            if (_initialSlots == null) return;

            for (int i = 0; i < _initialSlots.Length && i < _slots.Length; i++)
                _slots[i] = _initialSlots[i] as IHoldable;
        }

        private void Start()
        {
            // 强制走一次完整选中流程。
            // _selected 初值就是 0，直接 Select(0) 会被"已经是当前格"的判断挡掉，
            // 导致开局时当前格收不到 OnSelect（手持模型就不会挂上来）。
            int first = _selected;
            _selected = -1;
            Select(first);
        }

        private void Update() => TickSwitchInput();

        // ---------------------------------------------------------------- 装配

        /// <summary>把一件可手持物放进指定格子。传 null 清空该格。</summary>
        public void SetSlot(int index, IHoldable item)
        {
            if (_slots == null) _slots = new IHoldable[Mathf.Max(1, _slotCount)];
            if (index < 0 || index >= _slots.Length) return;

            if (_slots[index] == item) return;

            // 正在手里的被换掉，要先走一次收起流程
            if (_selected == index && _slots[index] != null)
                _slots[index].OnDeselect();

            _slots[index] = item;
            OnSlotChanged?.Invoke(index);

            if (_selected == index)
                item?.OnSelect();
        }

        /// <summary>选中某一格。越界的索引会被忽略。</summary>
        public void Select(int index)
        {
            if (_slots == null || index < 0 || index >= _slots.Length) return;
            if (index == _selected && _selected >= 0) return;

            _slots[_selected]?.OnDeselect();
            _selected = index;
            _slots[_selected]?.OnSelect();

            OnSelectionChanged?.Invoke(_selected);
        }

        /// <summary>往后/往前翻一格（滚轮用）。</summary>
        public void Cycle(int direction)
        {
            if (_slots == null || _slots.Length == 0) return;

            int next = (_selected + direction) % _slots.Length;
            if (next < 0) next += _slots.Length;
            Select(next);
        }

        // ---------------------------------------------------------------- 使用

        /// <summary>左键。转发给当前格。</summary>
        public void UseCurrent(bool held, bool pressed)
            => Current?.Use(held, pressed);

        /// <summary>右键。转发给当前格。</summary>
        public void UseCurrentSecondary(bool held, bool pressed)
            => Current?.UseSecondary(held, pressed);

        // ---------------------------------------------------------------- 输入

        private void TickSwitchInput()
        {
            if (_slotKeys != null)
            {
                for (int i = 0; i < _slotKeys.Length && i < _slots.Length; i++)
                {
                    if (Input.GetKeyDown(_slotKeys[i]))
                    {
                        Select(i);
                        return;
                    }
                }
            }

            if (!_enableScrollWheel) return;

            float scroll = Input.GetAxis(_scrollAxis);
            if (Mathf.Abs(scroll) < _scrollThreshold) return;

            // 向下滚（scroll < 0）是"下一格"，与大多数游戏一致
            Cycle(scroll > 0f ? -1 : 1);
        }
    }
}
