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
}
