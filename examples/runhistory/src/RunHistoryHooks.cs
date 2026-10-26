// =============================================================================
//  Run History（战绩）· 采集钩子
//
//  五个补丁，全部是 Postfix/Prefix（不改游戏方法体，也不接管任何返回值）：
//    · CampaignManager.NewGameLaunchInitialize   开新局（游戏自己在这里定 seed 与 playedDate）
//    · CampaignManager.DeserializeCampaignInfo   继续游戏（唯一入口；InGameTransit 不走它）
//    · WorldManager.MapEngage(MapNode)           进入一个地图节点
//    · CampaignManager.CampaignEnd（private）    结算（胜利/阵亡/放弃都会走到这里）
//    · UI.PauseMenu.OnAbandonRunClick            放弃本局 —— 必须单独标记，否则与阵亡同形
//
//  ★ 参数用 __0/__1 绑定而不是名字：游戏被混淆/改名时按位置更稳（参数名在 Release 里也可能被抹掉）。
// =============================================================================

using HarmonyLib;

namespace RunHistory
{
    [HarmonyPatch(typeof(Combat.CampaignManager), "NewGameLaunchInitialize")]
    internal static class HookNewRun
    {
        [HarmonyPostfix]
        private static void Postfix() { RunHistoryCapture.OnNewRun(); }
    }

    [HarmonyPatch(typeof(Combat.CampaignManager), "DeserializeCampaignInfo")]
    internal static class HookContinue
    {
        /// <summary>
        /// 只把"正在进行的战役"交给采集：已经打完的战役点继续也会走这里，
        /// 那不该算作新的一局（否则每个通关/阵亡的档都会多一条垃圾记录）。
        /// </summary>
        [HarmonyPostfix]
        private static void Postfix(Combat.CampaignInformation __0) { RunHistoryCapture.OnContinue(__0); }
    }

    [HarmonyPatch(typeof(World.WorldManager), "MapEngage")]
    internal static class HookNodeEnter
    {
        [HarmonyPostfix]
        private static void Postfix(World.NewMap.MapNode __0) { RunHistoryCapture.OnNode(__0); }
    }

    [HarmonyPatch(typeof(Combat.CampaignManager), "CampaignEnd")]
    internal static class HookRunEnd
    {
        [HarmonyPostfix]
        private static void Postfix() { RunHistoryCapture.OnRunEnd(); }
    }

    [HarmonyPatch(typeof(UI.PauseMenu), "OnAbandonRunClick")]
    internal static class HookAbandon
    {
        [HarmonyPrefix]
        private static void Prefix() { RunHistoryCapture.MarkAbandon(); }
    }

    /// <summary>
    /// 种子覆盖：`GenerateNewSeed()` 是开局时给 `_campaignRandomSeed` 赋值的地方
    /// （在 `NewGameLaunchInitialize` 的第一行），此后地图才按它生成。
    /// </summary>
    [HarmonyPatch(typeof(Combat.CampaignManager), "GenerateNewSeed")]
    internal static class HookSeedGenerated
    {
        [HarmonyPostfix]
        private static void Postfix() { RunHistorySeed.AfterSeedGenerated(); }
    }

    /// <summary>
    /// 世界生成前：把 `ShufflingExtension` 那个**未播种**的静态 System.Random 也按同一个种子重置。
    /// （这一步是热身；真正决定性的一步在下面的 <see cref="HookMapGenerate"/>。）
    /// </summary>
    [HarmonyPatch(typeof(World.WorldManager), "GenerateNewWorld")]
    internal static class HookWorldGeneration
    {
        [HarmonyPrefix]
        private static void Prefix() { RunHistorySeed.BeforeWorldGeneration(); }

        [HarmonyPostfix]
        private static void Postfix(ref World.WorldInformation __result)
        {
            RunHistoryMapDump.Write(__result, RunHistorySeed.CurrentSeed(), "新世界");
        }
    }

    /// <summary>
    /// 地图生成前：**紧挨着真正取随机数的地方**再重置一次。
    ///
    /// 为什么非得在这里：`GenerateNewWorld` 之前还有一段路（实例化章节预制体、它的 Awake/OnEnable），
    /// 中间任何一次 `.Random()` / `.Shuffle()` 都会把随机流往前推；只在 `GenerateNewWorld` 前重置，
    /// 结果就依赖那段路里到底消耗了几次 —— 那正是 v0.2.0 地图仍然每次不同的原因之一。
    /// `ChapterMapGeneratorBase.GenerateMap()` 的第一句就是 `GenerateChildrenSections()`（挑段落），
    /// 所以这里是「重置 → 取数」之间没有任何空隙的位置。
    /// </summary>
    [HarmonyPatch(typeof(World.ChapterMapGenerator.ChapterMapGeneratorBase), "GenerateMap")]
    internal static class HookMapGenerate
    {
        [HarmonyPrefix]
        private static void Prefix() { RunHistorySeed.ReseedForMap("地图生成前"); }
    }

    /// <summary>
    /// 每个段落生成前：单独对齐 `UnityEngine.Random`。
    /// 段落里的节点类型（森林/城市的 `RandomByWeight`）与坐标抖动都取它；
    /// 它是引擎级全局状态，容易被别的系统顺手消耗，所以按段落重新播种，
    /// 让「第几段」而不是「之前发生过什么」决定它的值。
    /// </summary>
    [HarmonyPatch(typeof(World.ChapterMapGenerator.MapSectionGenerator.MapSectionBase), "GenerateSection")]
    internal static class HookSectionGenerate
    {
        [HarmonyPrefix]
        private static void Prefix(World.ChapterMapGenerator.MapSectionGenerator.MapSectionBase __instance)
        {
            int seed = RunHistorySeed.CurrentSeed();
            RunHistoryRandomState.ReseedUnity(seed * 31 + (__instance == null ? 0 : __instance.sectionID));
        }
    }
}
