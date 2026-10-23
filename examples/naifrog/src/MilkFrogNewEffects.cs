// =============================================================================
//  奶蛙 · 11~14 号卡的自定义效果
//    · mf_swarm_surge_cascade —— 被奶蛙结算时，对所有敌人造成伤害
//    · mf_choose_discard      —— 让玩家挑手牌放进弃牌堆（游戏没有这种内置效果类）
//    · mf_choir_tenacity      —— 开启"本回合每结算 1 张奶蛙卡 +N 坚韧"
// =============================================================================

using System;
using Card;
using Combat;
using Command;
using Cysharp.Threading.Tasks;
using ModKit.Api;

namespace ManualDemo
{
    /// <summary>蛙群怒涛（连锁部分）：被奶蛙结算时，对所有敌人造成 Amount 点伤害。</summary>
    [ModEffect("mf_swarm_surge_cascade")]
    public sealed class MfSwarmSurgeCascade : CascadeTriggeredEffect
    {
        public override void ApplyWhenCascading(EffectContext ctx)
        {
            ctx.DealDamage(Math.Max(1, ctx.Amount), DamageMode.Direct, CombatantSelector.AllOpponents);
        }
    }

    /// <summary>
    /// 蛙鸣换气（弃牌部分）：玩家从手牌里挑至多 Amount 张，放进弃牌堆。
    /// </summary>
    /// <remarks>
    /// 游戏只有"随机弃 / 弃全部"的效果类，没有"由玩家挑一张弃"；这条路是照
    /// <c>Keyword.CardKeywordKick</c> 的写法走的：PickCardManager（Hand 池）选牌，
    /// 提交回调里对每张调 <c>CardManager.CardDiscardLogic(card, isActivelyDiscard: true)</c>。
    /// </remarks>
    [ModEffect("mf_choose_discard")]
    public sealed class MfChooseDiscard : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            try
            {
                var cm = CardManager.Instance;
                if (cm == null) { return; }

                int want = Math.Max(1, ctx.Amount);
                int have = cm.cardHand == null ? 0 : cm.cardHand.Count;
                if (have <= 0)
                {
                    ctx.Log("蛙鸣换气：没有手牌可弃。");
                    return;
                }

                var cfg = new PickCardManager.PickCardConfigure(
                    PickCardManager.PickCardPoolType.Hand,
                    null,
                    PickCardManager.PickCardCountMode.Upto,
                    Math.Min(want, have),
                    null);
                cfg.titleKeyword = "Title_MilkfrogDiscard";
                cfg.actionNameKeyword = "Key_MilkfrogDiscard";
                cfg.PickCardsArraySubmitAction = cards =>
                {
                    if (cards != null)
                    {
                        foreach (var c in cards)
                        {
                            if (c != null) { CardManager.Instance.CardDiscardLogic(c, isActivelyDiscard: true); }
                        }
                    }
                    return UniTask.CompletedTask;
                };
                new SubCommandPickCardAndAction(cfg);
            }
            catch (Exception e)
            {
                ctx.Log("蛙鸣换气：选牌弃置失败 —— " + e.GetType().Name + ": " + e.Message);
            }
        }
    }

    /// <summary>蛙群壁垒（追加部分）：开启本回合的"每结算 1 张奶蛙卡 +Amount 坚韧"。</summary>
    [ModEffect("mf_choir_tenacity")]
    public sealed class MfChoirTenacity : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            int amount = Math.Max(1, ctx.Amount);
            MilkFrogTenacity.Enable(amount);
            ctx.Log("蛙群壁垒：本回合每结算 1 张奶蛙卡，额外获得 " + amount + " 点坚韧。");
        }
    }
}
