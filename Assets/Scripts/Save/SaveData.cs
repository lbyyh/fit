using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fit.Save
{
    /// <summary>
    /// 存档数据结构。
    ///
    /// 设计约束（从 How to Fish 的存档实现反推）：
    ///   - 玩家数据与世界数据分开存。How to Fish 有 GetSavedPlayer / GetSavedCreature /
    ///     CurServerSave / ServerHasFinishedTutorial 四套，说明玩家进度（跨房间跟随）
    ///     与房间世界状态（跟房主走）是解耦的。
    ///   - 版本号必带。联机游戏改数据结构是常态，没有版本号就只能做废档处理。
    ///   - Steam 云存档走 ISteamRemoteStorage，本地也要有备份，云失败不能阻断游戏。
    /// </summary>
    [Serializable]
    public sealed class PlayerProgress
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public string PlayerName = "Unnamed";
        public int SkinId;
        public long Money;
        public bool HasFinishedTutorial;
        public double TotalPlayTimeSeconds;
        public List<int> UnlockedAchievements = new();
        public List<int> OwnedItems = new();
        public List<IslandProgressEntry> Islands = new();
    }

    [Serializable]
    public sealed class IslandProgressEntry
    {
        public string IslandId;
        public bool Discovered;
        public int CreaturesCaught;
        public int BossesDefeated;
    }

    /// <summary>
    /// 房间世界状态。只在房主机器上持久化，客户端不写。
    /// 房主退出时保存，重开时可以选择继续。
    /// </summary>
    [Serializable]
    public sealed class WorldState
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public int Day;
        public float TimeOfDay;
        public long ServerMoney;
        public List<CreatureStateEntry> Creatures = new();
    }

    [Serializable]
    public sealed class CreatureStateEntry
    {
        public int TypeId;
        public float PositionX;
        public float PositionY;
        public float PositionZ;
        public float RotationY;
        public float Health;
    }

    /// <summary>
    /// 存档读写。
    ///
    /// 两个必须坚持的原则：
    ///   1. 原子写 —— 先写临时文件再替换。写到一半断电会留下半个 JSON，
    ///      下次启动解析失败，玩家进度全丢，这是最容易挨骂的 bug。
    ///   2. 损坏可恢复 —— 解析失败时保留 .bak，静默重置而不是弹窗卡住启动流程。
    /// </summary>
    public static class SaveFile
    {
        public static string ProgressPath =>
            Path.Combine(Application.persistentDataPath, "progress.json");

        public static string WorldPath =>
            Path.Combine(Application.persistentDataPath, "world.json");

        public static void WriteAtomic(string path, string json)
        {
            string temp = path + ".tmp";
            string backup = path + ".bak";

            try
            {
                if (File.Exists(path))
                    File.Copy(path, backup, true);

                File.WriteAllText(temp, json);
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Save] 写入失败 {path}：{ex.Message}");
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }

        public static bool TryRead(string path, out string json)
        {
            json = null;

            try
            {
                if (!File.Exists(path)) return false;

                json = File.ReadAllText(path);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Save] 读取失败 {path}：{ex.Message}");
                return false;
            }
        }
    }
}
