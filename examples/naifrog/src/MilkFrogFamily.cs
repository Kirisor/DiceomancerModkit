// =============================================================================
//  奶蛙职业 · 「奶家人」召唤族（2026-10-24）
//
//  ★ 原版机制参考（全部照抄自反编译的 Assembly-CSharp）：
//    · CardKeywordSummon.AfterCardResolvePhase → new SubCommandOrganismSpawn(GetSummonInfo())
//      OrganismInfo{ SummonName, CardIcon, ColorEs, EffectInfos, LifeCounter, deathCardInfo }
//    · SummonBase：GetLife / AddLife / RemoveLife；回合末失 1 生命并触发一次行动，
//      死亡时把 deathCardInfo 那张卡加入手中（=「精粹」）。
//    · EffectSpEffectCancer：`for (i < summon.GetLife()) new CommandOrganismResolve(summon, isInsert: true)`
//      ——「连续触发直到死亡」就是它：按目标当前生命排队这么多次触发。
//    · EffectSpEffectAddLifeToAllSummon：遍历 SummonManager.GetOrganisms() 逐个 AddLife。
//    · EffectSpEffectGainAllDeBuff：遍历 AbilityDataManager.GetAllEnemyDeBuff()，逐个施加同层数。
//    · EffectSpEffectChangeMaxHealth：new SubCommandChangeMaxHealth(isAdd, amount)。
//    · EffectSpEffectTempReduceMaxHp：「最大生命值减少 N，直到战斗结束」—— 改的是同一个
//      最大生命值 RNumber，但走 SubCommandChangeRNumber(ChangeType.ChaosTemp)，
//      BattleManager.Win() 里 RollNumberManager.RestoreAllRNumberTempoState() 会还原。
//
//  ★★ 重要：**自定义效果不要写数字槽（amounts）**（2026-10-24 实机教训）
//    游戏的卡面文案渲染会把每个数字槽做成一个独立的 TMP 数字对象，再塞进描述文本里预留的
//    {Number} 占位符。而自定义效果（[ModEffect]）的描述走平台兜底，**拿不到格式化参数**，
//    `{Number}` 既不会被替换、数字对象也没有落点 ⇒ 数字会**浮在卡图上**、文本里空一格。
//    所以本文件的数值全部**写死在代码里**、文案里写死数字，效果**不带 amounts**。
//    确有需要「数字随卡面变化」的卡，改用原版 builtin（它才吃得到占位符）。
//
//  ★ 召唤物伤害走 IndirectDamage（与原版召唤物一致，不吃力量/易伤/暴击）。
//    要让召唤伤害吃乘区，把 DamageMode.Indirect 改成 Direct（一处）。
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
using RollNumber;
using ModKit.Api;

namespace ManualDemo
{
    /// <summary>「奶家人」族：六张召唤卡 + 共用的召唤/查询工具。</summary>
    internal static class MilkFrogFamily
    {
        /// <summary>奶蛋？—— 既是召唤物也是「奶之刻」的判定目标。</summary>
        internal const string Question = "mf_naidan_question";

        /// <summary>族的成员（短 id）。17「奶家人」自己不算成员。</summary>
        internal static readonly string[] Members =
        {
            "mf_mudpuppet",      // 奶蛋（原奶狗）
            "mf_bloodspawn",     // 奶鼠（原奶蛋）
            "mf_milk_dog",       // 奶狗
            "mf_milk_rabbit",    // 奶兔
            "mf_milk_king",      // 奶king
            Question             // 奶蛋？
        };

        /// <summary>
        /// 短 id → 借用的原版召唤物名（决定立绘与显示名）。
        /// </summary>
        /// <remarks>
        /// 游戏的 <c>SummonBase.Initialize</c> 用 <c>OrganismInfo.SummonName</c> 去查
        /// <c>AssetRefSo.SpriteDict</c> 与 <c>CardName</c> 两张表；模组卡名带前缀，两张表都查不到
        /// （日志里那行 <c>Sprite not found: naifrog.xxx</c> 就是这么来的）。
        /// 所以这里直接借原版基础元素的名字与立绘。
        /// </remarks>
        internal static readonly Dictionary<string, string> Visual =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "mf_mudpuppet", "EarthElement" },
                { "mf_bloodspawn", "FireElement" },
                { "mf_milk_dog", "FireElement" },
                { "mf_milk_rabbit", "WaterElement" },
                { "mf_milk_king", "EarthElement" },
                { Question, "WaterElement" },
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

        /// <summary>按短 id 取「借用立绘」的原版元素卡（拿名字去查不到时会退回默认）。</summary>
        private static CardSo GetVisualCard(string shortId)
        {
            try
            {
                string name;
                if (!Visual.TryGetValue(shortId, out name) || string.IsNullOrEmpty(name)) { return null; }
                CardSo so = CardDataManager.Instance.GetCardSoByName(name);
                if (so == null || string.IsNullOrEmpty(so.cardName) || so.cardName == "EmptyCard") { return null; }
                return so;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>把一张「带 Summon 关键词的卡」召唤出来（照原版 CardKeywordSummon 的取数方式）。</summary>
        /// <param name="shortId">卡的短 id（用于查借用立绘）。</param>
        internal static void Spawn(string shortId)
        {
            CardSo so = GetCard(shortId);
            if (so == null) { return; }
            if (so.CardInfo.cardKeyWordInfos == null) { return; }

            CardKeyWordInfo kw = so.CardInfo.cardKeyWordInfos
                .FirstOrDefault(k => string.Equals(k.keywordName, "Summon", StringComparison.OrdinalIgnoreCase));
            if (kw.keywordName == null) { return; }

            CardSo deathCard = null;
            if (!string.IsNullOrEmpty(kw.keywordCardSoName))
            {
                deathCard = CardDataManager.Instance.GetCardSoByName(kw.keywordCardSoName);
            }

            // 借用原版元素名/立绘：这样 SummonName 能在 SpriteDict 与 CardName 两张表里查到。
            CardSo visual = GetVisualCard(shortId);
            string visualName = visual != null ? visual.cardName : so.cardName;

            var info = new OrganismInfo
            {
                SummonName = visualName,
                CardIcon = visual != null ? visual.cardArt : so.cardArt,
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

        /// <summary>场上是否有某个短 id 对应的召唤物。</summary>
        /// <remarks>
        /// 召唤物名现在是借用来的原版名（FireElement 等），所以按「立绘名」比较；
        /// 同时兼容直接以卡名召唤的情况（旧存档里的召唤物）。
        /// </remarks>
        internal static bool HasSummon(string shortId)
        {
            string visualName;
            if (!Visual.TryGetValue(shortId, out visualName)) { visualName = Qualify(shortId); }
            string cardName = Qualify(shortId);

            return Living().Any(o =>
            {
                string n = o.GetSummonName();
                return string.Equals(n, visualName, StringComparison.Ordinal)
                    || string.Equals(n, cardName, StringComparison.Ordinal);
            });
        }

        internal static string RandomMember()
        {
            string[] ids = Members.Where(id => GetCard(id) != null).ToArray();
            if (ids.Length == 0) { return null; }
            return ids[Rng.Next(ids.Length)];
        }
    }

    /// <summary>奶狗：造成 3 点伤害 ×2（召唤物行动）。</summary>
    [ModEffect("mf_dog_bite")]
    public sealed class MfDogBite : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            Repeat(ctx, 3, 2);
        }

        internal static void Repeat(EffectContext ctx, int damage, int times)
        {
            for (int i = 0; i < times; i++)
            {
                ctx.DealDamage(damage, DamageMode.Indirect, CombatantSelector.Opponent);
            }
        }
    }

    /// <summary>奶狗精粹：造成 3 点伤害 ×4。</summary>
    [ModEffect("mf_dog_bite_essence")]
    public sealed class MfDogBiteEssence : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            MfDogBite.Repeat(ctx, 3, 4);
        }
    }

    /// <summary>召唤一个随机的「奶家人」。</summary>
    [ModEffect("mf_summon_family_random")]
    public sealed class MfSummonFamilyRandom : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            string id = MilkFrogFamily.RandomMember();
            if (id == null) { ctx.Log("奶家人：找不到可召唤的卡。"); return; }
            MilkFrogFamily.Spawn(id);
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
                MilkFrogFamily.Spawn(id);
            }
        }
    }

    /// <summary>召唤「奶蛋？」（奶之刻的 else 分支）。</summary>
    [ModEffect("mf_summon_naidan_question")]
    public sealed class MfSummonQuestion : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            MilkFrogFamily.Spawn(MilkFrogFamily.Question);
        }
    }

    /// <summary>所有召唤物生命 +3（原版 Key_AddLifeToAllSummon 同款，数值写死在描述里）。</summary>
    [ModEffect("mf_add_all_summon_life")]
    public sealed class MfAddAllSummonLife : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            SummonBase[] living = MilkFrogFamily.Living();
            if (living.Length == 0) { ctx.Log("奶家人：没有召唤物。"); return; }
            foreach (SummonBase summon in living) { summon.AddLife(3); }
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
                .Where(o => !string.Equals(o.GetSummonName(), selfName, StringComparison.Ordinal)
                         && !string.Equals(o.GetSummonName(), "WaterElement", StringComparison.Ordinal))
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
    /// 奶之刻：有「奶蛋？」→ 对所有敌人施加 1d8 层所有减益 + 1d8 伤害；否则召唤「奶蛋？」。
    /// </summary>
    /// <remarks>「所有减益」= 原版 EffectSpEffectGainAllDeBuff 的取法：GetAllEnemyDeBuff()。</remarks>
    [ModEffect("mf_milk_clock")]
    public sealed class MfMilkClock : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            Apply(ctx, MilkClockLayers(ctx), MilkClockDamage(ctx), "1d8");
        }

        internal static int MilkClockLayers(EffectContext ctx)
        {
            return 1 + ctx.Random.Next(8);   // 1d8
        }

        internal static int MilkClockDamage(EffectContext ctx)
        {
            return 1 + ctx.Random.Next(8);   // 1d8
        }

        internal static void Apply(EffectContext ctx, int layers, int damage, string diceText)
        {
            if (!MilkFrogFamily.HasSummon(MilkFrogFamily.Question))
            {
                MilkFrogFamily.Spawn(MilkFrogFamily.Question);
                ctx.Log("奶之刻：没有「奶蛋？」，改为召唤它。");
                return;
            }

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
            ctx.Log("奶之刻：" + diceText + " → " + layers + " 层减益 / " + damage + " 伤害。");
        }
    }

    /// <summary>奶之刻（升级）：3d4 层所有减益 + 3d4 伤害。</summary>
    [ModEffect("mf_milk_clock_up")]
    public sealed class MfMilkClockUp : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            int layers = Roll3d4(ctx);
            int damage = Roll3d4(ctx);
            MfMilkClock.Apply(ctx, layers, damage, "3d4");
        }

        private static int Roll3d4(EffectContext ctx)
        {
            int sum = 0;
            for (int i = 0; i < 3; i++) { sum += 1 + ctx.Random.Next(4); }
            return sum;
        }
    }

    /// <summary>奶蛋？精粹：最大生命值减少 8（**直到战斗结束**），然后召唤「奶蛋？」。</summary>
    /// <remarks>
    /// 走的是原版「最大生命值减少{Number}，直到战斗结束」那条效果（<c>Key_TempReduceMaxHp</c>）
    /// 的实现 —— <c>Effects.Special.CardEffect.EffectSpEffectTempReduceMaxHp</c>：
    /// <code>
    /// RNumber rn = CharacterPlayer.Instance.GetCurrentPlayerMaxHpRNumber();
    /// RNumberInfo info = rn.GetRNumberInfo();
    /// info.oriNumber -= N;
    /// new SubCommandChangeRNumber(rn, info, SubCommandChangeRNumber.ChangeType.ChaosTemp);
    /// </code>
    /// <c>ChaosTemp</c> 会在 RNumber 里记下改动前的数值（<c>isInTempoState</c>），
    /// 战斗内多次施加都叠在同一个"临时窗口"里；<c>BattleManager.Win()</c> 调
    /// <c>RollNumberManager.RestoreAllRNumberTempoState()</c> 时一次性还原。
    /// 对比：直接 <c>SubCommandChangeMaxHealth</c> 是**永久**扣（会写进战役存档的
    /// <c>CampaignInformation.playerMaxHealth</c>，下个战斗开始也回不来）。
    /// </remarks>
    [ModEffect("mf_essence_question")]
    public sealed class MfEssenceQuestion : SyncCardEffect
    {
        /// <summary>每次扣掉的最大生命值（写死：自定义效果的文案拿不到数字槽）。</summary>
        internal const int MaxHealthLoss = 8;

        public override void Apply(EffectContext ctx)
        {
            try
            {
                RNumber maxHp = CharacterPlayer.Instance.GetCurrentPlayerMaxHpRNumber();
                RNumberInfo info = maxHp.GetRNumberInfo();
                info.oriNumber -= MaxHealthLoss;
                new SubCommandChangeRNumber(maxHp, info, SubCommandChangeRNumber.ChangeType.ChaosTemp);
            }
            catch (Exception e)
            {
                ctx.Log("奶蛋？精粹：临时扣最大生命值失败 —— " + e.GetType().Name + ": " + e.Message);
            }
            MilkFrogFamily.Spawn(MilkFrogFamily.Question);
        }
    }
}
