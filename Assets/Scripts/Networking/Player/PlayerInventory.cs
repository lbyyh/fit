using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Serializing;

namespace Fit.Networking.Player
{
    /// <summary>
    /// 玩家背包。
    ///
    /// 沿用 How to Fish 的槽位模型（InventorySlot / BaitSlot / ItemManager），
    /// 但同步策略做了优化：
    ///   - 槽位内容用 SyncList（增量同步，只发变化的槽）；
    ///   - 数值型总量（金币）单独放 MoneyManager 而不是塞进背包；
    ///   - 结构体的网络序列化手写，避免 FishNet 自动生成器处理不了嵌套集合。
    ///
    /// 注意 FishNet 的 SyncList 需要在 Awake 里就创建好，不能延迟到 OnStartNetwork。
    /// </summary>
    public sealed class PlayerInventory : NetworkEntity
    {
        public readonly SyncList<ItemStack> Slots = new();

        [SerializeField] private int _capacity = 24;

        public event Action<int> SlotChanged;

        public int Capacity => _capacity;

        private void Awake()
        {
            Slots.OnChange += HandleSlotsChanged;
        }

        private void OnDestroy()
        {
            Slots.OnChange -= HandleSlotsChanged;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            for (int i = 0; i < _capacity; i++)
                Slots.Add(ItemStack.Empty);
        }

        /// <summary>客户端请求拾取，由服务器做权威判定（防止重复拾取）。</summary>
        [ServerRpc]
        public void RequestPickup(int itemId, int count)
        {
            if (count <= 0) return;

            // 先堆叠到已有槽位
            for (int i = 0; i < Slots.Count && count > 0; i++)
            {
                var stack = Slots[i];
                if (stack.IsEmpty || stack.ItemId != itemId) continue;

                int space = stack.MaxStack - stack.Count;
                if (space <= 0) continue;

                int add = System.Math.Min(space, count);
                Slots[i] = new ItemStack(itemId, stack.Count + add, stack.MaxStack);
                count -= add;
            }

            // 再放进空槽
            for (int i = 0; i < Slots.Count && count > 0; i++)
            {
                if (!Slots[i].IsEmpty) continue;

                int add = System.Math.Min(ItemStack.DefaultMaxStack, count);
                Slots[i] = new ItemStack(itemId, add, ItemStack.DefaultMaxStack);
                count -= add;
            }

            if (count > 0)
                Debug.Log($"[Inventory] {OwnerIdText()} 背包已满，丢弃 {count} 个 itemId={itemId}");
        }

        [ServerRpc]
        public void RequestRemoveAt(int slotIndex, int count)
        {
            if (slotIndex < 0 || slotIndex >= Slots.Count) return;

            var stack = Slots[slotIndex];
            if (stack.IsEmpty) return;

            int next = stack.Count - count;
            Slots[slotIndex] = next > 0
                ? new ItemStack(stack.ItemId, next, stack.MaxStack)
                : ItemStack.Empty;
        }

        [ServerRpc]
        public void RequestSwap(int from, int to)
        {
            if (from == to) return;
            if (from < 0 || from >= Slots.Count) return;
            if (to < 0 || to >= Slots.Count) return;

            var a = Slots[from];
            var b = Slots[to];

            // 同类可合并
            if (!a.IsEmpty && !b.IsEmpty && a.ItemId == b.ItemId && b.Count < b.MaxStack)
            {
                int move = System.Math.Min(a.Count, b.MaxStack - b.Count);
                Slots[to] = new ItemStack(b.ItemId, b.Count + move, b.MaxStack);
                int remain = a.Count - move;
                Slots[from] = remain > 0 ? new ItemStack(a.ItemId, remain, a.MaxStack) : ItemStack.Empty;
                return;
            }

            Slots[from] = b;
            Slots[to] = a;
        }

        public bool Contains(int itemId, int count = 1)
        {
            int total = 0;
            for (int i = 0; i < Slots.Count; i++)
                if (Slots[i].ItemId == itemId)
                    total += Slots[i].Count;

            return total >= count;
        }

        private void HandleSlotsChanged(SyncListOperation op, int index, ItemStack oldItem, ItemStack newItem, bool asServer)
        {
            SlotChanged?.Invoke(index);
        }

        private string OwnerIdText() => base.Owner.IsValid ? base.Owner.ClientId.ToString() : "?";
    }

    /// <summary>背包格子。纯值类型，便于 SyncList 增量同步。</summary>
    public struct ItemStack
    {
        public const int DefaultMaxStack = 64;

        public int ItemId;
        public int Count;
        public int MaxStack;

        public ItemStack(int itemId, int count, int maxStack = DefaultMaxStack)
        {
            ItemId = itemId;
            Count = count;
            MaxStack = maxStack <= 0 ? DefaultMaxStack : maxStack;
        }

        public bool IsEmpty => ItemId <= 0 || Count <= 0;

        public static ItemStack Empty => new(0, 0, DefaultMaxStack);
    }

    /// <summary>
    /// 手写序列化器。FishNet 的源码生成器能覆盖大部分情况，
    /// 但值类型放在 SyncList 里时手写更可控，也省去一次代码生成。
    /// </summary>
    public static class ItemStackSerializers
    {
        public static void WriteItemStack(this Writer w, ItemStack value)
        {
            w.WriteInt32(value.ItemId);
            w.WriteInt32(value.Count);
            w.WriteInt32(value.MaxStack);
        }

        public static ItemStack ReadItemStack(this Reader r)
        {
            return new ItemStack(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        }
    }
}
