namespace Fit.Gameplay.Achievements
{
    /// <summary>
    /// 成就定义。
    ///
    /// 直接对标 How to Fish 的 A01~A28。用 int ID 而不是字符串：
    ///   - 存档里存 int 列表，体积小；
    ///   - Steam SetAchievement 需要的是字符串 API 名，所以这里保留映射表；
    ///   - 加新成就只改这一个文件 + Steamworks 后台，不用动存档结构。
    /// </summary>
    public static class AchievementIds
    {
        public const int FirstCatch = 1;
        public const int FirstBoss = 2;
        public const int AllCreatures = 3;
        public const int MasterAngler = 4;
        public const int FullyUpgraded = 5;
        public const int Millionaire = 6;
        public const int DeepDiver = 7;
        public const int Speedrun = 8;
        public const int Pacifist = 9;
        public const int Completionist = 10;

        /// <summary>映射到 Steamworks 后台配置的 API 名称。</summary>
        public static string ToSteamApiName(int id) => id switch
        {
            FirstCatch => "ACH_FIRST_CATCH",
            FirstBoss => "ACH_FIRST_BOSS",
            AllCreatures => "ACH_ALL_CREATURES",
            MasterAngler => "ACH_MASTER_ANGLER",
            FullyUpgraded => "ACH_FULLY_UPGRADED",
            Millionaire => "ACH_MILLIONAIRE",
            DeepDiver => "ACH_DEEP_DIVER",
            Speedrun => "ACH_SPEEDRUN",
            Pacifist => "ACH_PACIFIST",
            Completionist => "ACH_COMPLETIONIST",
            _ => string.Empty
        };
    }
}
