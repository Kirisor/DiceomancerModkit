// =============================================================================
//  奶蛙王朝 · 「新召唤的奶家人，每个数字都 +3」（含次数）
//
//  为什么这么做（照原版「多就是好，大就是棒」的同一套数据）：
//    · 原版 EffectSpEffectMultiAddRandomSummonNumber 改的是召唤物自己的 RNumber
//      （RollNumberManager.allRNumbers 里 Owner 为 SummonBase 的那些）；
//    · 召唤物的数字来自 OrganismInfo.EffectInfos[].effectNumberInfos，
//      SummonBase.Initialize 时一次性生成 RNumber 并放进召唤物文本；
//    · 所以在构造 OrganismInfo 时把 LifeCounter 与所有常量数字 +bonus，
//      **文本显示与实际结算会同时生效**，不需要生成后再补命令。
//
//  ★ EffectInfos 必须深拷贝（EffectInfo.CreateDeepCopy）：它们与 CardSo 共享，
//    直接改会把 +3 永久写进卡的原始数据，之后每次召唤都会再 +3。
//
//  ★ 骰子数字（isDiceNumber）不加：+3 层骰子没有可解释的语义。
// =============================================================================

using System;
using Ability;
using Combat;
using Effects;
using Organism;

namespace ManualDemo
{
    /// <summary>「奶蛙王朝」的加成读取与召唤物数值改写。</summary>
    internal static class MilkFrogDynasty
    {
        /// <summary>能力叶子名（平台会加 mod 前缀，比较时只看最后一段）。</summary>
        internal const string AbilityLeaf = "mf_dynasty_aura";

        /// <summary>每层王朝给每个数字的加成。</summary>
        internal const int PerStack = 3;

        /// <summary>当前加成总量：能力层数 × 3；两层王朝就是 +6。</summary>
        internal static int Bonus
        {
            get
            {
                try
                {
                    Character player = CharacterPlayer.Instance;
                    if (player == null || player.abilities == null) { return 0; }

                    int layers = 0;
                    foreach (AbilityBase ability in player.abilities)
                    {
                        if (ability == null || string.IsNullOrEmpty(ability.abilityName)) { continue; }
                        if (!string.Equals(CardKeywordMilkFrog.LeafOf(ability.abilityName), AbilityLeaf,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        layers += Math.Max(1, ability.GetLayerNumber());
                    }
                    return layers * PerStack;
                }
                catch
                {
                    return 0;
                }
            }
        }

        /// <summary>把加成写进召唤信息：生命与每个常量数字。</summary>
        internal static void Boost(OrganismInfo info)
        {
            if (info == null) { return; }
            int bonus = Bonus;
            if (bonus <= 0) { return; }

            info.LifeCounter += bonus;

            EffectInfo[] source = info.EffectInfos;
            if (source == null || source.Length == 0) { return; }

            EffectInfo[] copy = new EffectInfo[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                EffectInfo effect = source[i];
                if (effect == null) { continue; }

                EffectInfo cloned = effect.CreateDeepCopy();
                if (cloned.effectNumberInfos != null)
                {
                    for (int j = 0; j < cloned.effectNumberInfos.Count; j++)
                    {
                        Card.RNumberInfo number = cloned.effectNumberInfos[j];
                        if (number.isDiceNumber) { continue; }
                        number.oriNumber += bonus;
                        cloned.effectNumberInfos[j] = number;
                    }
                }
                copy[i] = cloned;
            }
            info.EffectInfos = copy;
        }
    }
}
