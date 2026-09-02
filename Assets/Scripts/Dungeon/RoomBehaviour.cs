using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fit.Dungeon
{
    public enum RoomType
    {
        Start,
        Combat,
        Treasure,   // 宝箱房
        Shop,       // 商店房
        Elite,      // 精英房
        Lottery,    // 抽奖房
        Boss,       // Boss 房
        Corridor,
    }

    public enum RoomState
    {
        Dormant,    // 未进入
        Active,     // 玩家在内，进行中
        Cleared,    // 已完成
    }

    /// <summary>
    /// 房间基类。
    ///
    /// 【房间是内容的基本单元】
    /// ID-005 的价值在于：房间是可以无限堆的内容容器。
    /// 新武器、新敌人、新机制都能以"新房间模板"的形式加进来，
    /// 不用碰核心代码。这是这个设计最大的资产，所以基类要保持足够薄。
    ///
    /// 【门位锚点为什么用显式 Transform】
    /// 房间拼接最容易出的问题是门对不齐。
    /// 用显式锚点 + 运行时校验（见 ValidateAnchors），
    /// 能在编辑器阶段就发现"这个房间拼上去会歪"。
    /// </summary>
    public abstract class RoomBehaviour : MonoBehaviour
    {
        [Header("身份")]
        [SerializeField] protected RoomType _type = RoomType.Combat;

        [Header("门位锚点")]
        [SerializeField] protected Transform[] _doorAnchors = Array.Empty<Transform>();

        [Header("刷怪点（战斗类房间）")]
        [SerializeField] protected Transform[] _spawnPoints = Array.Empty<Transform>();

        public RoomType Type => _type;
        public RoomState State { get; protected set; } = RoomState.Dormant;
        public IReadOnlyList<Transform> DoorAnchors => _doorAnchors;
        public IReadOnlyList<Transform> SpawnPoints => _spawnPoints;

        /// <summary>房间状态变化。生成器监听以控制相邻房间的加载。</summary>
        public event Action<RoomBehaviour, RoomState> OnStateChanged;

        public virtual void EnterRoom()
        {
            State = RoomState.Active;
            OnStateChanged?.Invoke(this, State);
            OnEntered();
        }

        public virtual void ClearRoom()
        {
            if (State == RoomState.Cleared) return;

            State = RoomState.Cleared;
            OnStateChanged?.Invoke(this, State);
            OnCleared();
        }

        protected abstract void OnEntered();
        protected abstract void OnCleared();

        /// <summary>
        /// 编辑器校验：门位与刷怪点是否配好。
        /// 门位没配会导致房间生成器连不上，这类错误拖到运行时很难查。
        /// </summary>
        public virtual bool ValidateAnchors(out string error)
        {
            if (_doorAnchors == null || _doorAnchors.Length == 0)
            {
                error = $"{name}: 房间没有任何门位锚点，无法与相邻房间连接";
                return false;
            }

            for (int i = 0; i < _doorAnchors.Length; i++)
            {
                if (_doorAnchors[i] == null)
                {
                    error = $"{name}: 门位锚点 [{i}] 为空";
                    return false;
                }
            }

            error = null;
            return true;
        }
    }
}
