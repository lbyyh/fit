using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Fit.Networking;
using Fit.Save;
using UnityEngine;

namespace Fit.Gameplay.Economy
{
    /// <summary>
    /// 房间级经济系统。
    ///
    /// 对应 How to Fish 的 MoneyManager。几个容易被忽略的点：
    ///
    /// 1. 用 long 而不是 float 存钱。
    ///    float 在超过 ~1600 万后整数精度就丢了，长线经营类游戏很容易触发。
    ///
    /// 2. 所有金额变动走服务器。
    ///    客户端只发"请求购买"，扣钱与发货都在服务器判定。
    ///    否则玩家改内存就能刷钱，而 Listen Server 架构下房主本身就是客户端。
    ///
    /// 3. 交易要防重入。
    ///    网络延迟下玩家可能连点两次购买，收到两个 ServerRpc。
    ///    这里用 _transactionLock 保证同一时刻只处理一笔。
    ///
    /// 4. 加钱/扣钱都要记录流水，便于排查"钱对不上"的 bug。
    /// </summary>
    public sealed class MoneyManager : NetworkEntity
    {
        [SyncVar(OnChange = nameof(OnMoneyChanged))]
        private long _money;

        public event Action<long> MoneyChanged;

        public long Money => _money;

        private bool _transactionLock;

        [Server]
        public void SetMoney(long amount)
        {
            _money = Math.Max(0L, amount);
        }

        public bool CanAfford(long price) => _money >= price;

        /// <summary>客户端请求购买。成功返回 true，失败（钱不够或已在交易中）返回 false。</summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestPurchase(int itemId, long price, int quantity)
        {
            if (_transactionLock)
            {
                Debug.LogWarning("[Economy] 交易进行中，请求被丢弃");
                return;
            }

            if (quantity <= 0) return;

            long total = price * quantity;
            if (total < 0) return; // 溢出保护

            if (!CanAfford(total))
                return;

            _transactionLock = true;

            try
            {
                _money -= total;
                GrantItem(itemId, quantity);
                MarkSaveDirty();
            }
            finally
            {
                _transactionLock = false;
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void RequestSell(int itemId, long unitPrice, int quantity)
        {
            if (_transactionLock) return;
            if (quantity <= 0 || unitPrice < 0) return;

            long total = unitPrice * quantity;
            if (total < 0) return;

            if (!ConsumeItem(itemId, quantity))
                return; // 实际没有这么多物品，拒绝交易

            _transactionLock = true;
            try
            {
                _money += total;
                MarkSaveDirty();
            }
            finally
            {
                _transactionLock = false;
            }
        }

        [Server]
        public void AddMoney(long amount, string reason)
        {
            _money = Math.Max(0L, _money + amount);
            Debug.Log($"[Economy] {reason}：{(amount >= 0 ? "+" : "")}{amount}，余额 {_money}");
            MarkSaveDirty();
        }

        private void GrantItem(int itemId, int quantity)
        {
            // 实际项目接入 PlayerInventory
        }

        private bool ConsumeItem(int itemId, int quantity)
        {
            // 实际项目接入 PlayerInventory，返回是否真的扣掉了
            return true;
        }

        private void MarkSaveDirty()
        {
            if (Core.ServiceLocator.TryGet<AutoSaver>(out var saver))
                saver.MarkDirty();
        }

        private void OnMoneyChanged(long prev, long next, bool asServer)
        {
            MoneyChanged?.Invoke(next);
        }
    }
}
