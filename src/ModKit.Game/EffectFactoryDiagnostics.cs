// =============================================================================
//  ModKit.Game · 内容工厂的"最后一道保险"（2026-10-26）
//
//  背景（一次实机崩溃）：
//    继续游戏时 `GameManager.Start → InitializeGame → DeserializeCampaignInfo`
//    会用存档里的 CardInfo 重建卡牌，卡上的关键词/效果要按类名解析成游戏类型。
//    平台的内容注册表与效果适配层此时都应已就绪（见 ModAssemblyPreload 与
//    PlatformRuntime.InstallEarlyTypeFallbacks），但只要还有任何一种名字
//    解析不出来，游戏那行 `Activator.CreateInstance(Type.GetType(...))` 就会
//    抛 ArgumentNullException，把玩家挡在"继续游戏"之外。
//
//  本文件的定位：**不是修内容，而是保证"存档损坏/名字漂移"不会变成崩溃**。
//    ① 效果工厂：解析落空时用一个空壳效果顶上（占位空转），并打出**完整名字**；
//    ② 关键词工厂：失败时打出关键词名 + 卡名（便于一次定位是哪张卡）；
//    ③ 游戏开始：打出时序证据（流水线是否已跑、CardDataManager 是否就绪）。
//
//  ★ 为什么只吞 ArgumentNullException(type)：
//    那是"类型解析落空"的唯一签名。其它异常（空引用、逻辑错误）保持原样抛出 ——
//    平台不替游戏掩盖真实缺陷（与"接缝边界 FailOpen、核心逻辑 FailFast"一致）。
// =============================================================================

using System;
using Card;
using Effects;
using HarmonyLib;
using Keyword;
using RollNumber;
using Utilities;

namespace ModKit.Game
{
    /// <summary>
    /// 效果工厂兜底：类型解析落空时给一个空壳 <see cref="EffectBase"/>，并把名字记进日志。
    /// </summary>
    [HarmonyPatch(typeof(EffectUtils), nameof(EffectUtils.GenerateEffectFromEffectInfo))]
    internal static class EffectFactoryDiagnostics
    {
        [HarmonyFinalizer]
        private static Exception Finalizer(
            EffectInfo effectInfo,
            IHaveEffect effectOwner,
            RNumber[] numbers,
            ref EffectBase __result,
            Exception __exception)
        {
            if (__exception == null) { return null; }

            // 只接管"类型解析落空"这一种；其余异常按原样抛（平台的边界纪律）
            var ane = __exception as ArgumentNullException;
            if (ane == null || !string.Equals(ane.ParamName, "type", StringComparison.Ordinal))
            {
                return __exception;
            }

            string wanted = DescribeTypeName(effectInfo);
            try
            {
                ModKitLog.Error("效果类解析落空：游戏要的类型不存在 —— " + wanted +
                    "（effectName='" + (effectInfo == null ? "?" : effectInfo.effectName) +
                    "'，specialEffectString='" + (effectInfo == null || effectInfo.specialEffectString == null
                        ? "?" : effectInfo.specialEffectString) + "'）。" +
                    "已用一个空壳效果顶上，避免继续游戏直接崩溃 —— 这张卡该条效果本次不生效。");
            }
            catch { }

            if (__result != null) { return null; }

            var placeholderType = EffectBaseAdapterEmitter.PlaceholderType;
            if (placeholderType == null) { return __exception; }

            try
            {
                var placeholder = Activator.CreateInstance(placeholderType) as EffectBase;
                if (placeholder == null) { return __exception; }

                // 尽量把游戏随后会用到的东西填上，避免"用空壳顶住崩溃"之后
                // 在卡面描述/数字排版处立刻又炸一次。
                try { placeholder.EffectOwner = effectOwner; } catch { }
                try
                {
                    int want = effectInfo == null || effectInfo.effectNumberInfos == null
                        ? 0 : effectInfo.effectNumberInfos.Count;
                    if (numbers != null && numbers.Length == want) { placeholder.rNumbers = numbers; }
                    else { placeholder.rNumbers = new RNumber[want]; }
                }
                catch { }

                __result = placeholder;
                return null;   // 吞掉异常：这一条效果空转，其余内容照常
            }
            catch
            {
                return __exception;
            }
        }

        /// <summary>把 EffectInfo 还原成游戏真正去查的那个类型名（日志用）。</summary>
        private static string DescribeTypeName(EffectInfo info)
        {
            try
            {
                if (info == null) { return "(effectInfo=null)"; }
                if (string.Equals(info.effectName, "SpecialEffect", StringComparison.Ordinal))
                {
                    return "Effects.Special.CardEffect.EffectSpEffect" + (info.specialEffectString ?? "null");
                }
                return "Effects.Effect" + (info.effectName ?? "null");
            }
            catch { return "(?)"; }
        }
    }

    /// <summary>关键词工厂诊断：失败时点名关键词与卡，便于一次定位。</summary>
    [HarmonyPatch(typeof(CardKeyWordUtil), nameof(CardKeyWordUtil.GenerateKeywordFromKeywordInfo))]
    internal static class KeywordFactoryDiagnostics
    {
        [HarmonyFinalizer]
        private static Exception Finalizer(CardKeyWordInfo cardKeyWordInfo, CardBase card, Exception __exception)
        {
            if (__exception == null) { return null; }

            try
            {
                ModKitLog.Error("关键词生成失败：keyword='" +
                    (string.IsNullOrEmpty(cardKeyWordInfo.keywordName) ? "?" : cardKeyWordInfo.keywordName) +
                    "'，卡='" + (card == null ? "?" : SafeCardName(card)) + "' —— " +
                    __exception.GetType().Name + ": " + __exception.Message);
            }
            catch { }

            return __exception;   // 关键词这一层不吞：半成品关键词会让整张卡处于不一致状态
        }

        private static string SafeCardName(CardBase card)
        {
            try { return card.CardName ?? "?"; } catch { return "?"; }
        }
    }

    /// <summary>
    /// 游戏开始初始化时的时序证据（诊断用，只读）。
    /// </summary>
    /// <remarks>
    /// 继续游戏时，存档反序列化发生在 <c>GameManager.Start → InitializeGame</c> 内部；
    /// 平台的流水线要等到**战斗场地存在**才会跑。这条日志把"谁先谁后"钉在日志里，
    /// 避免以后再靠猜。
    /// </remarks>
    [HarmonyPatch(typeof(GameManager), "Start")]
    internal static class GameStartDiagnostics
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            try
            {
                var cdm = GameData.CardDataManager.Instance;
                ModKitLog.Info("游戏开始初始化：平台流水线" +
                    (PlatformRuntime.HasRun ? "已完成" : "尚未运行（继续游戏时这是正常现象）") +
                    "｜CardDataManager=" + (cdm == null ? "null" : "就绪") +
                    "｜随即会反序列化存档并重建卡牌。");
            }
            catch { }
        }
    }
}
