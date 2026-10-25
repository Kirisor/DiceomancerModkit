// =============================================================================
//  奶蛙 · 召唤/连锁终端的三张卡的运行时代码
//    · mf_all_summons  —— 应奶尽奶：触发所有召唤物的效果，直到它们全部死亡
//    · mf_settle_all   —— 笑到最后：结算抽牌堆里所有带「奶蛙」的牌
//
//  ★ 应奶尽奶：照原版 EffectSpEffectCancer 的形状 —— 按目标当前生命排队这么多次结算
//    （`for (i < summon.GetLife()) new CommandOrganismResolve(summon, isInsert: true)`）。
//    节奏由游戏动画串行控制，平台不额外限流。
//
//  ★ 笑到最后：效果阶段的结算走 MilkFrogCascade.ResolveAsCascade ——
//    「被奶蛙结算时」窗口是打开的，且窗口内 CardKeywordMilkFrog 不再向外扩散，
//    所以这一阶段不会递归；第二波只来自这张卡自己的「奶蛙 N」。
//    **取牌用效果开始时的快照**：快照之后的结算不再回读抽牌堆。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Card;
using Combat;
using Command;
using Cysharp.Threading.Tasks;
using Keyword;
using ModKit.Api;
using Organism;

namespace ManualDemo
{
    /// <summary>应奶尽奶：把所有召唤物各自结算「当前生命」次，直到它们全部死亡。</summary>
    [ModEffect("mf_all_summons")]
    public sealed class MfAllSummons : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            try
            {
                SummonManager manager = SummonManager.Instance;
                if (manager == null)
                {
                    ctx.Log("应奶尽奶：不在战斗场景。");
                    return;
                }

                SummonBase[] summons = manager.GetOrganisms();
                if (summons == null || summons.Length == 0)
                {
                    ctx.Log("应奶尽奶：场上没有召唤物。");
                    return;
                }

                int total = 0;
                foreach (SummonBase summon in summons)
                {
                    if (summon == null) { continue; }
                    int life = summon.GetLife();
                    if (life <= 0) { continue; }
                    for (int i = 0; i < life; i++)
                    {
                        new CommandOrganismResolve(summon, isInsert: true);
                        total++;
                    }
                }
                ctx.Log("应奶尽奶：排队触发 " + total + " 次召唤物结算。");
            }
            catch (Exception e)
            {
                ctx.Log("应奶尽奶失败 —— " + e.GetType().Name + ": " + e.Message);
            }
        }
    }

    /// <summary>笑到最后：结算抽牌堆里的全部「奶蛙」卡（视为奶蛙结算）。</summary>
    [ModEffect("mf_settle_all")]
    public sealed class MfSettleAll : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            try
            {
                CardManager manager = CardManager.Instance;
                if (manager == null)
                {
                    ctx.Log("笑到最后：拿不到卡牌管理器。");
                    return;
                }

                CardBase[] library = manager.GetCardLibrary();
                if (library == null || library.Length == 0)
                {
                    ctx.Log("笑到最后：抽牌堆是空的。");
                    return;
                }

                // 快照：之后无论结算怎么改变抽牌堆，都只处理这一批。
                List<CardBase> snapshot = new List<CardBase>(library);
                List<CardBase> targets = new List<CardBase>();
                foreach (CardBase card in snapshot)
                {
                    if (card == null) { continue; }
                    if (card.CardKeywords == null) { continue; }
                    if (!card.CardKeywords.Any(k => k is CardKeywordMilkFrog)) { continue; }
                    targets.Add(card);
                }

                foreach (CardBase card in targets)
                {
                    MilkFrogCascade.ResolveAsCascade(card);
                }
                ctx.Log("笑到最后：结算抽牌堆中的 " + targets.Count + " 张奶蛙卡。");
            }
            catch (Exception e)
            {
                ctx.Log("笑到最后失败 —— " + e.GetType().Name + ": " + e.Message);
            }
        }
    }
}
