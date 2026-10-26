// =============================================================================
//  ModKit.Game · 把内容装配提前到 `InitializeGame` 内部（2026-10-26）
//
//  为什么必须在这里（而不是靠就绪轮询器）：
//    原版「继续游戏」是**一个同步方法**里跑完的（`GameManager.Start → InitializeGame`）：
//      ① GameStartLoadResource() → DataManager.LoadAllGameData()   ← 原版内容从资产加载
//      ② 生涯存档 / ③ 生涯统计
//      ④ 世界/地图/角色/金钱/剧情/骰子/牌堆初始化
//      ⑤ ManaManager.ManaManagerInitialize()          ← 战斗场地就位
//      ⑥ CampaignManager.DeserializeCampaignInfo(...) ← 从这里开始要 SO 与效果类
//    整个过程在一帧内完成，`Update`（轮询器）**没有机会插进去**。
//    实测：反序列化在帧内第 ⑥ 步抛 NRE（`RelicSo` 还没注册），而轮询器的
//    「运行时事实已就绪」日志出现在异常**之后** —— 这条竞态靠轮询永远赢不了。
//
//  为什么选 `ManaManagerInitialize` 的 Postfix：
//    那一刻 ⑤ 刚结束：DataManager / RelicDataManager / AbilityDataManager /
//    CardDataManager / CardManager / CampaignManager / BattleMultipleWorldsManager /
//    BattleField / ManaManager 全部就位（正好就是 `ModKitBootstrap.IsReady()` 判定的五项），
//    而 `ManaIconDic` 也刚被填满（费用色/费用槽组合探测要的正是它）。
//    下一行就是 ⑥ —— 中间只隔一个 if/else。
//
//  ★ 只对「继续游戏」生效：
//    新游戏的内容创建发生在反序列化**之后**，现有轮询路径一直是好的，不去动它。
//    `SystemManager.initializeGameMode` 在 `InitializeGame` 返回后才会被改写成
//    `InGameTransit`（见 GameManager.Start），所以这里读到的仍是原值。
// =============================================================================

using Combat;
using HarmonyLib;

namespace ModKit.Game
{
    /// <summary>
    /// `ManaManagerInitialize` 之后同步跑一次平台流水线 —— 存档反序列化之前的最后一个安全接缝。
    /// </summary>
    /// <remarks>
    /// 失败绝不冒泡：整段在 <see cref="PlatformRuntime.TryAssembleBeforeContinueDeserialize"/>
    /// 里 try/catch，并且失败时会把幂等闸拆掉，让就绪轮询器按老路再试一次。
    /// </remarks>
    [HarmonyPatch(typeof(ManaManager), "ManaManagerInitialize")]
    internal static class EarlyContentAssembly
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            PlatformRuntime.TryAssembleBeforeContinueDeserialize();
        }
    }
}
