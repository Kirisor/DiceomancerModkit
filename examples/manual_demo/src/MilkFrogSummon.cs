using System;
using System.Collections.Generic;
using Card;
using Combat;
using Command;
using Effects;
using GameData;
using ModKit.Api;
using Organism;
using RollNumber;
using UnityEngine;

namespace ManualDemo
{
    /// <summary>
    /// 召唤一个「泥偶蛙」：生命 = Amount，行动 = 加 1 点黑能量，死亡后给「泥偶蛙精粹」。
    /// </summary>
    /// <remarks>
    /// ★ 为什么不用原版 <c>Summon</c> 关键词：它的描述模板要 <c>{Number}</c> 与 <c>{RuleNumber}</c>，
    ///   在 mod 卡的道具提示路径上取不到值 → 实机直接弹
    ///   `OperationException: Could not evaluate the selector 'Number'`（2026-10-21 复现）。
    ///   这里直接按 <c>CardKeywordSummon.GetSummonInfo()</c> 的写法构造 OrganismInfo，
    ///   绕开关键词与它的文案模板。
    /// </remarks>
    [ModEffect("mf_summon_mudpuppet")]
    public sealed class MfSummonMudPuppet : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            SummonHelper.Spawn(ctx, "泥偶蛙", ctx.Amount, "mf_essence_mud", new EffectInfo
            {
                effectName = "AddMana",
                manaString = "K",                       // K = Black（原版 EffectAddMana 的写法）
                effectNumberInfos = new List<RNumberInfo>(),
            });
        }
    }

    /// <summary>
    /// 召唤一个「血蛙卵」：生命 = Amount，行动 = 对随机敌人施加 5 层流血，死亡后给「血蛙卵精粹」。
    /// </summary>
    [ModEffect("mf_summon_bloodspawn")]
    public sealed class MfSummonBloodSpawn : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            SummonHelper.Spawn(ctx, "血蛙卵", ctx.Amount, "mf_essence_blood", new EffectInfo
            {
                effectName = "ApplyAbility",
                abilityName = "Bleed",
                effectTargetEnum = EffectTargetEnum.RandomOpponent,
                effectNumberInfos = new List<RNumberInfo> { new RNumberInfo(5) },
            });
        }
    }

    /// <summary>召唤的公共部分（照抄 <c>CardKeywordSummon.GetSummonInfo()</c> 的字段填法）。</summary>
    internal static class SummonHelper
    {
        internal static void Spawn(EffectContext ctx, string summonName, int life, string deathCardId, EffectInfo action)
        {
            try
            {
                CardBase owner = null;
                try { owner = CardHelper.CurrentCastingCard; } catch { }

                var info = new OrganismInfo
                {
                    SummonName = summonName,
                    LifeCounter = Math.Max(1, life),
                    ColorEs = owner == null ? new[] { ColorE.Green } : owner.GetCardColoEs(),
                    CardIcon = owner == null ? null : owner.GetCardArtSprite(),
                    EffectInfos = new[] { action },
                    deathCardInfo = FindCardInfo(deathCardId),
                };

                new SubCommandOrganismSpawn(info);
                ctx.Log("召唤「" + summonName + "」（生命 " + Math.Max(1, life) + "）。");
            }
            catch (Exception e)
            {
                Debug.LogError("[ModKit] 召唤失败：" + e);
            }
        }

        /// <summary>按 id 找卡 SO 的 CardInfo（mod 卡在游戏里带前缀，用后缀匹配）。</summary>
        internal static CardInfo FindCardInfo(string cardId)
        {
            try
            {
                var cdm = CardDataManager.Instance;
                if (cdm == null) { return default(CardInfo); }

                var so = cdm.GetCardSoByName(cardId);
                if (so != null) { return so.CardInfo.CreateDeepCopy(); }

                var f = cdm.GetType().GetField("_allInGameCardSoDictionary",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                var dict = f == null ? null : f.GetValue(cdm) as System.Collections.IDictionary;
                if (dict == null) { return default(CardInfo); }

                string suffix = "." + cardId;
                foreach (System.Collections.DictionaryEntry e in dict)
                {
                    var key = e.Key as string;
                    var card = e.Value as CardSo;
                    if (card == null || key == null) { continue; }
                    if (key == cardId || key.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        return card.CardInfo.CreateDeepCopy();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[ModKit] 找召唤死亡卡失败：" + e.Message);
            }
            return default(CardInfo);
        }
    }

    /// <summary>取"当前正在结算的卡"（召唤需要卡名/立绘/颜色）。</summary>
    internal static class CardHelper
    {
        internal static CardBase CurrentCastingCard
        {
            get
            {
                try
                {
                    var cm = CardManager.Instance;
                    if (cm == null) { return null; }
                    var stack = cm.GetCardStack();
                    if (stack != null && stack.Count > 0) { return stack[stack.Count - 1]; }
                }
                catch { }
                return null;
            }
        }
    }
}
