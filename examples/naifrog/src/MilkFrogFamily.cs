// =============================================================================
//  奶蛙职业 · 「奶家人」召唤族（2026-10-24）
//
//  ★ 原版机制参考（全部照抄自反编译的 Assembly-CSharp）：
//    · CardKeywordSummon.AfterCardResolvePhase → new SubCommandOrganismSpawn(GetSummonInfo())
//      OrganismInfo{ SummonName, CardIcon, ColorEs, EffectInfos, LifeCounter, deathCardInfo }
//    · SummonBase：GetLife / AddLife / RemoveLife；回合末失 1 生命并触发一次行动，
//      死亡时把 deathCardInfo 那张卡加入手中（=「精粹」）。
//    · EffectSpEffectCancer：`for (i < summon.GetLife()) new CommandOrganismResolve(summon, isInsert: true)`
//      —— 「连续触发直到死亡」就是它：按目标当前生命排队这么多次触发。
//    · EffectSpEffectAddLifeToAllSummon：遍历 SummonManager.GetOrganisms() 逐个 AddLife。
//    · EffectSpEffectGainAllDeBuff：遍历 AbilityDataManager.GetAllEnemyDeBuff()，逐个施加同层数。
//    · EffectSpEffectSummonRandomBasicElement：从卡上取 Summon 关键词的 effectInfos/number/toCard 组 OrganismInfo。
//
//  ★ 召唤物造成的伤害走 IndirectDamage（与**原版召唤物一致**，不吃力量/易伤/暴击）。
//    若要让它吃乘区，把下面 DamageMode.Indirect 改成 Direct 即可（一处）。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Ability;
using Card;
using Combat;
using Command;
using GameData;
using Keyword;
using Organism;
using ModKit.Api;

namespace ManualDemo
{
    /// <summary>「奶家人」族：六张召唤卡 + 共用的召唤/查询工具。</summary>
    internal static class MilkFrogFamily
    {
        /// <summary>奶蛋？—— 既是召唤物也是「奶之刻」的判定目标。</summary>
        internal const string Question = "mf_naidan_question";

        /// <summary>族的成员（短 id，运行时补 mod 前缀）。17「奶家人」自己不算成员。</summary>
        internal static readonly string[] Members =
        {
            "mf_mudpuppet",      // 奶蛋（原奶狗）
            "mf_bloodspawn",     // 奶鼠（原奶蛋）
            "mf_milk_dog",       // 奶狗
            "mf_milk_rabbit",    // 奶兔
            "mf_milk_king",      // 奶king
            Question             // 奶蛋？
        };

        private static readonly Random Rng = new Random();

        /// <summary>短 id → 全名（<c>naifrog.xxx</c>）。</summary>
        internal static string Qualify(string shortId)
        {
            string modId = typeof(MilkFrogFamily).Assembly.GetName().Name;
            return modId + "." + shortId;
        }

        /// <summary>按短 id 取卡；取不到返回 null（游戏取不到会返回 EmptyCard，必须自己判掉）。</summary>
        internal static CardSo GetCard(string shortId)
        {
            try
            {
                CardSo so = CardDataManager.Instance.GetCardSoByName(Qualify(shortId));
                if (so == null || string.IsNullOrEmpty(so.cardName) || so.cardName == "EmptyCard") { return null; }
                return so;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>把一张「带 Summon 关键词的卡」召唤出来（照原版 CardKeywordSummon 的取数方式）。</summary>
        internal static void Spawn(CardSo so)
        {
            if (so == null || so.CardInfo.cardKeyWordInfos == null) { return; }

            CardKeyWordInfo kw = so.CardInfo.cardKeyWordInfos
                .FirstOrDefault(k => string.Equals(k.keywordName, "Summon", StringComparison.OrdinalIgnoreCase));
            if (kw.keywordName == null) { return; }

            CardSo deathCard = null;
            if (!string.IsNullOrEmpty(kw.keywordCardSoName))
            {
                deathCard = CardDataManager.Instance.GetCardSoByName(kw.keywordCardSoName);
            }

            var info = new OrganismInfo
            {
                SummonName = so.cardName,
                CardIcon = so.cardArt,
                ColorEs = so.CardInfo.cardColorEs,
                EffectInfos = kw.effectInfos,
                LifeCounter = kw.number,
                deathCardInfo = deathCard == null ? default(CardInfo) : deathCard.CardInfo
            };
            new SubCommandOrganismSpawn(info);
        }

        /// <summary>当前存活召唤物（生命 &gt; 0）。</summary>
        internal static SummonBase[] Living()
        {
            try
            {
                var manager = SummonManager.Instance;
                if (manager == null) { return new SummonBase[0]; }
                var all = manager.GetOrganisms();
                if (all == null) { return new SummonBase[0]; }
                return all.Where(o => o != null && o.GetLife() > 0).ToArray();
            }
            catch
            {
                return new SummonBase[0];
            }
        }

        /// <summary>场上是否有某个短 id 对应的召唤物（按召唤物名 = 卡名匹配）。</summary>
        internal static bool HasSummon(string shortId)
        {
            string full = Qualify(shortId);
            return Living().Any(o => string.Equals(o.GetSummonName(), full, StringComparison.Ordinal));
        }

        internal static CardSo RandomMember()
        {
            CardSo[] cards = Members.Select(GetCard).Where(c => c != null).ToArray();
            if (cards.Length == 0) { return null; }
            return cards[Rng.Next(cards.Length)];
        }
    }

    /// <summary>造成 Amount 点伤害，重复 Amount1 次（每一段都是独立判定/独立乘区）。</summary>
    /// <remarks>召唤物用它 = 原版召唤物那种间接伤害（不吃乘区）；改成 <see cref="DamageMode.Direct"/> 即吃乘区。</remarks>
    [ModEffect("mf_repeat_damage")]
    public sealed class MfRepeatDamage : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            int damage = Math.Max(0, ctx.Amount);
            int times = Math.Max(1, ctx.AmountAt(1));
            for (int i = 0; i < times; i++)
            {
                ctx.DealDamage(damage, DamageMode.Indirect, CombatantSelector.Opponent);
            }
        }
    }

    /// <summary>召唤一个随机的「奶家人」。</summary>
    [ModEffect("mf_summon_family_random")]
    public sealed class MfSummonFamilyRandom : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            CardSo so = MilkFrogFamily.RandomMember();
            if (so == null) { ctx.Log("奶家人：找不到可召唤的卡。"); return; }
            MilkFrogFamily.Spawn(so);
            ctx.Log("奶家人：召唤了「" + so.cardName + "」。");
        }
    }

    /// <summary>把每种「奶家人」各召唤一个（奶king 的精粹）。</summary>
    [ModEffect("mf_summon_family_all")]
    public sealed class MfSummonFamilyAll : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            foreach (string id in MilkFrogFamily.Members)
            {
                CardSo so = MilkFrogFamily.GetCard(id);
                if (so == null) { continue; }
                MilkFrogFamily.Spawn(so);
            }
        }
    }

    /// <summary>按短 id 召唤指定召唤物（奶之刻的 else 分支、奶蛋？精粹）。</summary>
    [ModEffect("mf_summon_naidan_question")]
    public sealed class MfSummonQuestion : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            CardSo so = MilkFrogFamily.GetCard(MilkFrogFamily.Question);
            if (so == null) { ctx.Log("奶蛋？：找不到这张卡。"); return; }
            MilkFrogFamily.Spawn(so);
        }
    }

    /// <summary>所有召唤物生命 +Amount（原版 Key_AddLifeToAllSummon 同款）。</summary>
    [ModEffect("mf_add_all_summon_life")]
    public sealed class MfAddAllSummonLife : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            SummonBase[] living = MilkFrogFamily.Living();
            if (living.Length == 0) { ctx.Log("奶家人：没有召唤物。"); return; }
            int amount = Math.Max(0, ctx.Amount);
            foreach (SummonBase summon in living) { summon.AddLife(amount); }
        }
    }

    /// <summary>
    /// 随机选一个存活召唤物（不含「奶蛋？」自己），连续触发它直到它死亡
    /// （照原版 EffectSpEffectCancer：按目标当前生命排队这么多次触发）。
    /// </summary>
    [ModEffect("mf_resolve_random_until_death")]
    public sealed class MfResolveRandomUntilDeath : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            string selfName = MilkFrogFamily.Qualify(MilkFrogFamily.Question);
            SummonBase[] candidates = MilkFrogFamily.Living()
                .Where(o => !string.Equals(o.GetSummonName(), selfName, StringComparison.Ordinal))
                .ToArray();
            if (candidates.Length == 0) { ctx.Log("奶蛋？：没有别的召唤物可以触发。"); return; }

            SummonBase target = candidates[ctx.Random.Next(candidates.Length)];
            int life = Math.Max(1, target.GetLife());
            ctx.Log("奶蛋？：连续触发「" + target.GetSummonName() + "」" + life + " 次。");
            for (int i = 0; i < life; i++)
            {
                new CommandOrganismResolve(target, isInsert: true);
            }
        }
    }

    /// <summary>
    /// 奶之刻：有「奶蛋？」→ 对所有敌人施加 Amount 层**所有减益** + 造成 Amount1 点伤害；
    /// 否则召唤「奶蛋？」。
    /// </summary>
    /// <remarks>「所有减益」= 原版 EffectSpEffectGainAllDeBuff 的取法：AbilityDataManager.GetAllEnemyDeBuff()。</remarks>
    [ModEffect("mf_milk_clock")]
    public sealed class MfMilkClock : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            if (!MilkFrogFamily.HasSummon(MilkFrogFamily.Question))
            {
                CardSo so = MilkFrogFamily.GetCard(MilkFrogFamily.Question);
                if (so != null) { MilkFrogFamily.Spawn(so); }
                return;
            }

            int layers = Math.Max(0, ctx.Amount);
            int damage = Math.Max(0, ctx.AmountAt(1));

            try
            {
                var abilityManager = DataManager.Instance == null ? null : DataManager.Instance.GetAbilityDataManager();
                if (abilityManager != null && layers > 0)
                {
                    AbilitySo[] debuffs = abilityManager.GetAllEnemyDeBuff();
                    if (debuffs != null)
                    {
                        foreach (AbilitySo so in debuffs)
                        {
                            if (so == null || so.abilityInfo == null) { continue; }
                            ctx.GiveAbility(so.abilityInfo.abilityName, layers, CombatantSelector.AllOpponents);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ctx.Log("奶之刻：施加减益时出错 —— " + e.Message);
            }

            if (damage > 0)
            {
                ctx.DealDamage(damage, DamageMode.Indirect, CombatantSelector.AllOpponents);
            }
        }
    }
}
