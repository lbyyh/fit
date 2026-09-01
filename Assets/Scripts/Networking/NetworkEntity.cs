using FishNet.Object;
using UnityEngine;

namespace Fit.Networking
{
    /// <summary>
    /// 所有联网实体的基类。
    ///
    /// 设计来源：How to Fish 把 55 个类拆成了独立的 NetworkBehaviour ——
    /// Player / PlayerMovement / PlayerVitals / PlayerInventory / PlayerEating /
    /// PlayerDying / CrabArms 各自同步自己的状态。
    ///
    /// 这样做的好处：
    ///   - 带宽可控：只有真正变化的部件才发数据，而不是整个玩家对象每帧打包；
    ///   - 职责清晰：加一个"进食"功能不用去动 PlayerMovement；
    ///   - 可组合：AI 生物和玩家能复用同一套 Vitals / Inventory 组件。
    /// 代价是 Draw Call 与对象数量上升，以及需要小心处理部件之间的初始化顺序。
    ///
    /// 本基类统一了"同一 NetworkObject 上的兄弟组件引用"这个问题。
    /// </summary>
    public abstract class NetworkEntity : NetworkBehaviour
    {
        /// <summary>本地玩家拥有（或作为房主时拥有权威）。</summary>
        protected bool HasAuthority => IsOwner || (IsServerStarted && Owner.IsValid);

        /// <summary>缓存同一 NetworkObject 上的兄弟组件，避免每帧 GetComponent。</summary>
        private readonly System.Collections.Generic.Dictionary<System.Type, Component> _siblingCache = new();

        /// <summary>
        /// 获取挂在同一个 NetworkObject 下的兄弟组件。
        /// FishNet 下 NetworkObject 是根，各部件是它的子对象或同级组件。
        /// </summary>
        protected T Sibling<T>() where T : Component
        {
            var type = typeof(T);

            if (_siblingCache.TryGetValue(type, out var cached) && cached != null)
                return (T)cached;

            // 优先同级，其次子级（部件常作为子节点挂载）
            var found = GetComponent<T>() ?? GetComponentInChildren<T>(true) ?? GetComponentInParent<T>();
            if (found != null)
                _siblingCache[type] = found;
            else
                Debug.LogWarning($"[{GetType().Name}] 未找到兄弟组件 {type.Name}", this);

            return found;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _siblingCache.Clear();
        }

        /// <summary>只在拥有权威的一端执行（服务器权威 + 本地预测）。</summary>
        protected void IfAuthoritative(System.Action action)
        {
            if (HasAuthority)
                action?.Invoke();
        }
    }
}
