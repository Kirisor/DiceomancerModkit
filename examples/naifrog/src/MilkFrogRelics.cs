// =============================================================================
//  奶蛙 · 绿黑遗物组（GreenBlack）的运行时
//
//  三件遗物（relics/mf_relic_*.json）：
//    · 奶■      —— 敌人血条被击破时结算一张奶蛙牌（每回合 1 次）
//    · 微型奶蛙  —— 玩家打出奶蛙牌、结算完成时抽 1 张（每场 3 次）
//    · 奶妈      —— 打出奶蛙牌时检定 10，成功则该牌全部数字本场 +1
//
//  ★ 为什么遗物要写 C#：遗物 JSON 只能表达「触发点 + 内置效果」，
//    「结算一张奶蛙牌」「检定成功后再改牌」都不在内置语义里 ⇒ 走 custom 效果。
//
//  ★ 触发上下文（MilkFrogTriggerContext）：游戏把「哪个事件触发了遗物」放在
//    `TriggerBase` 里（CommandCastCard 会把 Card 一起塞进去），但遗物效果拿到的
//    EffectBase 上没有这个引用 ⇒ 这里用一处 Harmony prefix 把当前 TriggerBase 记下来，
//    效果体再按需取 `Card`。只读、不改变游戏行为。
// =============================================================================

using System;
using System.Collections.Generic;
using Card;
using Combat;
using Command;
using DiceUtilities;
using Effects;
using Keyword;
using ModKit.Api;
using RollNumber;
using Trigger;
using HarmonyLib;
using UnityEngine;

namespace ManualDemo
{
    /// <summary>把「当前正在结算的触发」记下来，供遗物效果读取触发它的那张牌。</summary>
    /// <remarks>
    /// 只读、幂等、无副作用：<see cref="EffectBase.TriggeredApplyEffect"/> 是配置型触发
    /// （遗物/能力的触发效果）的唯一入口，前缀里读一下参数不会影响任何原有流程。
    /// </remarks>
    [HarmonyPatch(typeof(EffectBase), "TriggeredApplyEffect")]
    internal static class MilkFrogTriggerContextPatch
    {
        private static void Prefix(TriggerBase trigger)
        {
            MilkFrogTriggerContext.Current = trigger;
        }
    }

    internal static class MilkFrogTriggerContext
    {
        internal static TriggerBase Current;

        /// <summary>触发这次效果的牌（只有 CastCard 这类带牌的触发点才有；其余为 null）。</summary>
        internal static CardBase Card
        {
            get { return Current == null ? null : Current.Card; }
        }
    }

    /// <summary>奶蛙牌判定（容忍 <c>naifrog.milk_frog</c> 这种带前缀的写法）。</summary>
    internal static class MilkFrogRelicUtil
    {
        internal static bool IsMilkFrog(CardBase card)
        {
            if (card == null) { return false; }
            List<CardKeyword> keywords = card.CardKeywords;
            if (keywords == null) { return false; }
            foreach (CardKeyword keyword in keywords)
            {
                if (keyword != null && CardKeywordMilkFrog.IsMilkFrogName(keyword.KeywordName)) { return true; }
            }
            return false;
        }

        internal static CardBase FindMilkFrog(IEnumerable<CardBase> cards)
        {
            if (cards == null) { return null; }
            foreach (CardBase card in cards)
            {
                if (IsMilkFrog(card)) { return card; }
            }
            return null;
        }
    }

    /// <summary>奶■：敌人血条被击破时，结算一张奶蛙牌（手牌优先，其次抽牌堆）。</summary>
    /// <remarks>
    /// 结算走 <see cref="MilkFrogCascade.ResolveAsCascade"/> —— 与「奶蛙」关键词同一条通路
    /// （不花能量、自动挑目标、期间打开「被奶蛙结算时」窗口）。
    /// 次数上限（每回合 1 次）由遗物数据里的 triggerLimit 控制，不在这里记账。
    /// </remarks>
    [ModEffect("mf_relic_settle_frog")]
    public sealed class MfRelicSettleFrog : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            try
            {
                CardManager manager = CardManager.Instance;
                if (manager == null)
                {
                    ctx.Log("奶■：不在战斗场景。");
                    return;
                }

                CardBase picked = MilkFrogRelicUtil.FindMilkFrog(manager.cardHand)
                                  ?? MilkFrogRelicUtil.FindMilkFrog(manager.GetCardLibrary());
                if (picked == null)
                {
                    ctx.Log("奶■：手牌与抽牌堆里都没有奶蛙牌。");
                    return;
                }

                MilkFrogCascade.ResolveAsCascade(picked);
                ctx.Log("奶■：结算「" + picked.CardName + "」。");
            }
            catch (Exception e)
            {
                ctx.Log("奶■：结算失败 —— " + e.GetType().Name + ": " + e.Message);
            }
        }
    }

    /// <summary>奶妈：打出奶蛙牌时检定 10，成功则该牌全部数字在本场战斗内 +1。</summary>
    /// <remarks>
    /// 检定口径与游戏原生一致（<c>DiceManager.DiceCheck</c> 掷 D20 + 修正，
    /// <c>GetResult() &gt;= 需求</c> 即成功；<c>CheckDiceRollSuccess</c> 只负责播检定表现，
    /// 原版 <c>EffectSpAbilityWithCheck</c> 也是"不 await、自己算结果"这一写法）。
    /// <para>
    /// 数字 +1 照原版 <c>EffectSpEffectTempAddAllCardNumber</c> 的形状：
    /// 逐个数 <c>SubCommandChangeRNumber</c> 入队。差别只有两点：
    /// <c>ChaosPermanent</c>（本场战斗持续）且只作用于**触发它的这张牌**。
    /// 骰子数字（<c>IsDiceRNumber</c>）没有"恒定值 +1"的语义，按原版同样跳过。
    /// </para>
    /// </remarks>
    [ModEffect("mf_relic_check_grow")]
    public sealed class MfRelicCheckGrow : SyncCardEffect
    {
        private const int Requirement = 10;
        private const string CheckKey = "MilkFrogGrowth";

        public override void Apply(EffectContext ctx)
        {
            try
            {
                CardBase card = MilkFrogTriggerContext.Card;
                if (card == null)
                {
                    ctx.Log("奶妈：拿不到触发它的那张牌（触发点没带牌）。");
                    return;
                }

                DiceManager dice = DiceManager.Instance;
                if (dice == null)
                {
                    ctx.Log("奶妈：拿不到骰子管理器。");
                    return;
                }

                RollDicesResult result = dice.DiceCheck(card, CheckKey, 0);

                bool success = result.GetResult() >= Requirement;
                try
                {
                    dice.CheckDiceRollSuccess(card, Requirement, result, isQuickCheck: true, card.transform.position);
                }
                catch (Exception e)
                {
                    ctx.Log("奶妈：检定表现播放失败（不影响结果）—— " + e.GetType().Name);
                }

                if (!success)
                {
                    ctx.Log("奶妈：检定失败（" + result.GetResult() + " < " + Requirement + "）。");
                    return;
                }

                int changed = 0;
                foreach (RNumber number in card.rollableNumbers)
                {
                    if (number == null || number.IsDiceRNumber()) { continue; }
                    new SubCommandChangeRNumber(
                        number,
                        new RNumberInfo(number.GetCollapsedNumber() + 1),
                        SubCommandChangeRNumber.ChangeType.ChaosPermanent);
                    changed++;
                }

                ctx.Log("奶妈：检定成功（" + result.GetResult() + "），「" + card.CardName + "」" +
                    changed + " 个数字本场 +1。");
            }
            catch (Exception e)
            {
                ctx.Log("奶妈：结算失败 —— " + e.GetType().Name + ": " + e.Message);
            }
        }
    }
}
