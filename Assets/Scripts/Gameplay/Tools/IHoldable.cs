using UnityEngine;

namespace Fit.Gameplay.Tools
{
    /// <summary>
    /// 可手持物 —— 武器与农具的统一抽象。
    ///
    /// 【为什么需要它，而不是把锄头塞进 WeaponData】
    /// 底部工具栏要同时装得下枪和锄头，但两者差别很大：
    /// 枪有弹匣 / 射速 / 后坐力 / 散布，锄头这些**一个都没有**。
    /// 硬塞进 WeaponData 会让它背上一堆不适用字段（弹匣容量填几？散布角填几？），
    /// 而且 Validate() 会开始出现"锄头不需要但必须填"的字段。
    ///
    /// 所以工具走独立的数据类（ToolData），靠这个接口让工具栏只认
    /// "能拿在手里、能按左键用"的东西，不关心它到底是枪还是锄头。
    ///
    /// 以后加水壶、鱼竿、相机，都是新建一个 ToolData 资产，Hotbar 一行不用改。
    /// 这是 ID-005（内容量是主要工作量）下的正确取舍。
    /// </summary>
    public interface IHoldable
    {
        /// <summary>显示名。HUD 与提示用。</summary>
        string DisplayName { get; }

        /// <summary>图标。低分辨率风格下用小尺寸色块即可。</summary>
        Sprite Icon { get; }

        /// <summary>被选中（切到这一格）。用来挂手持模型、播收放动画。</summary>
        void OnSelect();

        /// <summary>被取消选中。</summary>
        void OnDeselect();

        /// <summary>
        /// 主使用（左键）。
        /// </summary>
        /// <param name="held">按住</param>
        /// <param name="pressed">本帧刚按下</param>
        void Use(bool held, bool pressed);

        /// <summary>
        /// 次使用（右键）。武器是瞄准，工具预留给"收回 / 切换模式"。
        /// 阶段 1 可以先空实现。
        /// </summary>
        void UseSecondary(bool held, bool pressed);
    }
}
