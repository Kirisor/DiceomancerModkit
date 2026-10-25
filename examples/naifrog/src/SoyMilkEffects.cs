// =============================================================================
//  豆奶系列 + 奶蛙回旋：需要读敌方减益层数的效果
//    · mf_soy_love  —— 豆奶之恋：目标每有 1 层流血，施加 N 层燃烧
//    · mf_soy_ruin  —— 豆奶之殇：移除所有敌人的减益，每移除 1 层所有敌人失去 N 点生命（一次结算）
//    · mf_soy_vow   —— 豆奶之誓：移除所有敌人的减益，每移除 1 层获得 N 层随机增益
//    · mf_mire_whirl—— 奶蛙回旋：造成「目标减益层数 × N」的伤害
//
//  ★ 移除减益的写法照原版 EffectSpEffectRemoveAllDeBuff：
//    取 abilities 里 abilityType == DeBuff 的项，逐条 SubCommandChangeAbilityLayer(..., RemoveAbility)。
//  ★ 随机增益的池子照原版「多就是好 大就是棒」：DataManager.Instance.GetAbilityDataManager().GetAllPlayerBuff()。
//    随机结果按能力聚合后每种只发一次命令（层数很多时少发几十条命令）。
// =============================================================================

using System;
using System.Collections.Generic;
using Ability;
using Card;
using Combat;
using Combat.Enemy;
using Command;
using GameData;
using ModKit.Api;
using UnityEngine;

namespace ManualDemo
{
    /// <summary>敌方减益的读取与移除。</summary>
    internal static class SoyMilk
    {
        /// <summary>取当前效果的目标敌人；找不到时退回场上第一个敌人。</summary>
        internal static Character PickEnemy(EffectContext ctx)
        {
            try
            {
                EnemyManager manager = EnemyManager.Instance;
                List<CharacterEnemy> all = manager == null ? null : manager.GetAllEnemy();
                if (all == null || all.Count == 0) { return null; }

                string want = null;
                try { want = ctx.Opponent == null ? null : ctx.Opponent.Name; } catch { }

                CharacterEnemy first = null;
                foreach (CharacterEnemy enemy in all)
                {
                    if (enemy == null) { continue; }
                    if (first == null) { first = enemy; }
                    if (!string.IsNullOrEmpty(want) &&
                        string.Equals(enemy.characterName, want, StringComparison.Ordinal))
                    {
                        return enemy;
                    }
                }
                return first;
            }
            catch { return null; }
        }

        /// <summary>某个敌人的减益总层数。</summary>
        internal static int DebuffLayers(Character target)
        {
            if (target == null || target.abilities == null) { return 0; }
            int total = 0;
            foreach (AbilityBase ability in target.abilities)
            {
                if (ability == null || ability.abilityType != AbilityType.DeBuff) { continue; }
                total += Math.Max(0, ability.GetLayerNumber());
            }
            return total;
        }

        /// <summary>某个敌人身上指定能力的层数（按叶子名匹配）。</summary>
        internal static int LayersOf(Character target, string leaf)
        {
            if (target == null || target.abilities == null) { return 0; }
            int total = 0;
            foreach (AbilityBase ability in target.abilities)
            {
                if (ability == null || string.IsNullOrEmpty(ability.abilityName)) { continue; }
                if (string.Equals(CardKeywordMilkFrog.LeafOf(ability.abilityName), leaf,
                        StringComparison.OrdinalIgnoreCase))
                {
                    total += Math.Max(0, ability.GetLayerNumber());
                }
            }
            return total;
        }

        /// <summary>移除一个敌人的全部减益，返回移除的总层数。</summary>
        internal static int RemoveAllDebuffs(Character target)
        {
            if (target == null || target.abilities == null) { return 0; }

            List<AbilityBase> list = new List<AbilityBase>();
            int total = 0;
            foreach (AbilityBase ability in target.abilities)
            {
                if (ability == null || ability.abilityType != AbilityType.DeBuff) { continue; }
                total += Math.Max(0, ability.GetLayerNumber());
                list.Add(ability);
            }
            foreach (AbilityBase ability in list)
            {
                new SubCommandChangeAbilityLayer(ability,
                    SubCommandChangeAbilityLayer.ChangeAbilityLayerType.RemoveAbility);
            }
            return total;
        }

        /// <summary>移除所有敌人的减益，返回移除的总层数。</summary>
        internal static int RemoveAllEnemiesDebuffs()
        {
            int total = 0;
            try
            {
                EnemyManager manager = EnemyManager.Instance;
                List<CharacterEnemy> all = manager == null ? null : manager.GetAllEnemy();
                if (all == null) { return 0; }
                foreach (CharacterEnemy enemy in all)
                {
                    if (enemy == null) { continue; }
                    total += RemoveAllDebuffs(enemy);
                }
            }
            catch { }
            return total;
        }
    }

    /// <summary>豆奶之恋：目标每有 1 层流血，施加 Amount 层燃烧。</summary>
    [ModEffect("mf_soy_love")]
    public sealed class MfSoyLove : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            Character target = SoyMilk.PickEnemy(ctx);
            if (target == null) { ctx.Log("豆奶之恋：没有目标。"); return; }

            int bleed = SoyMilk.LayersOf(target, "Bleed");
            int perLayer = Math.Max(1, ctx.AmountAt(0));
            int burn = bleed * perLayer;
            if (burn <= 0) { ctx.Log("豆奶之恋：目标没有流血。"); return; }

            ctx.GiveAbility("Burn", burn, CombatantSelector.Opponent);
            ctx.Log("豆奶之恋：" + bleed + " 层流血 → " + burn + " 层燃烧。");
        }
    }

    /// <summary>豆奶之殇：移除所有敌人的减益，每移除 1 层所有敌人失去 Amount 点生命（一次结算）。</summary>
    [ModEffect("mf_soy_ruin")]
    public sealed class MfSoyRuin : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            int perLayer = Math.Max(1, ctx.AmountAt(0));
            int removed = SoyMilk.RemoveAllEnemiesDebuffs();
            if (removed <= 0) { ctx.Log("豆奶之殇：没有减益可移除。"); return; }

            int damage = removed * perLayer;
            ctx.LoseLife(damage, CombatantSelector.AllOpponents);
            ctx.Log("豆奶之殇：移除 " + removed + " 层 → 所有敌人失去 " + damage + " 点生命。");
        }
    }

    /// <summary>豆奶之誓：移除所有敌人的减益，每移除 1 层获得 Amount 层随机增益。</summary>
    [ModEffect("mf_soy_vow")]
    public sealed class MfSoyVow : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            int perLayer = Math.Max(1, ctx.AmountAt(0));
            int removed = SoyMilk.RemoveAllEnemiesDebuffs();
            if (removed <= 0) { ctx.Log("豆奶之誓：没有减益可移除。"); return; }

            int rolls = removed * perLayer;
            AbilityDataManager adm = DataManager.Instance == null ? null : DataManager.Instance.GetAbilityDataManager();
            AbilitySo[] pool = adm == null ? null : adm.GetAllPlayerBuff();
            Character player = CharacterPlayer.Instance;
            if (pool == null || pool.Length == 0 || player == null)
            {
                ctx.Log("豆奶之誓：拿不到增益池。");
                return;
            }

            // 先按能力聚合，再每种发一次命令（24 层就是 24 次随机，但最多 14 条命令）。
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, AbilitySo> byName = new Dictionary<string, AbilitySo>(StringComparer.Ordinal);
            for (int i = 0; i < rolls; i++)
            {
                AbilitySo so = pool[ctx.Random.Next(pool.Length)];
                if (so == null || so.abilityInfo == null) { continue; }
                string key = so.abilityInfo.abilityName;
                if (string.IsNullOrEmpty(key)) { continue; }
                byName[key] = so;
                counts[key] = counts.TryGetValue(key, out int current) ? current + 1 : 1;
            }

            string summary = "";
            foreach (KeyValuePair<string, int> pair in counts)
            {
                new SubCommandApplyAbility(player, byName[pair.Key].abilityInfo, pair.Value);
                summary += (summary.Length > 0 ? " / " : "") + pair.Key + "×" + pair.Value;
            }
            ctx.Log("豆奶之誓：移除 " + removed + " 层 → 随机增益 " + rolls + " 层（" + summary + "）。");
        }
    }

    /// <summary>奶蛙回旋：造成「目标减益层数 × Amount」的伤害。</summary>
    [ModEffect("mf_mire_whirl")]
    public sealed class MfMireWhirl : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            Character target = SoyMilk.PickEnemy(ctx);
            if (target == null) { ctx.Log("奶蛙回旋：没有目标。"); return; }

            int layers = SoyMilk.DebuffLayers(target);
            int multiplier = Math.Max(1, ctx.AmountAt(0));
            int damage = layers * multiplier;
            if (damage <= 0) { ctx.Log("奶蛙回旋：目标没有减益。"); return; }

            ctx.DealDamage(damage, DamageMode.Direct, CombatantSelector.Opponent);
            ctx.Log("奶蛙回旋：" + layers + " 层减益 ×" + multiplier + " → " + damage + " 点伤害。");
        }
    }
}
