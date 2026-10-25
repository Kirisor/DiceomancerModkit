// =============================================================================
//  奶蛙 · 「获得：…」型卡 + 防御/过牌件
//    · mf_grant_dynasty  —— 奶蛙王朝：获得能力 mf_dynasty_aura（卡面显示「获得：…」文案）
//    · mf_grant_exist    —— 奶蛙存在吗？：获得能力 mf_exist_watch
//    · mf_peach_cascade  —— 黄桃罐头：被奶蛙结算时获得加固
//    · mf_call_family    —— 呼叫奶家人：抽 3 张，每张绿色牌回 1 点绿色法力
//
//  ★ 「获得：…」的卡面文案约定（用户 2026-10-26 定）：
//    这类效果的卡面文字直接就是「获得：<效果描述>」，不走 gain_ability 的
//    「获得 N 层 <能力名>」。做法是用一个自定义效果占位（Key_ 文案里写「获得：…」），
//    实际的能力授予在 C# 里完成 —— 这样描述与行为各自可控。
// =============================================================================

using System;
using System.Collections.Generic;
using Ability;
using Card;
using Combat;
using Command;
using DiceUtilities;
using GameData;
using ModKit.Api;
using UnityEngine;

namespace ManualDemo
{
    /// <summary>能力授予与「获得：…」卡的小工具。</summary>
    internal static class MilkFrogGrants
    {
        /// <summary>按叶子名授予一个 mod 能力（平台注册时带 mod 前缀，两个名字都试一遍）。</summary>
        internal static void Grant(string leaf, int layers)
        {
            try
            {
                AbilityDataManager adm = DataManager.Instance == null ? null : DataManager.Instance.GetAbilityDataManager();
                if (adm == null) { return; }

                AbilitySo so = adm.GetAbilitySo("naifrog." + leaf);
                if (so == null) { so = adm.GetAbilitySo(leaf); }
                if (so == null)
                {
                    Debug.LogWarning("[ModKit] 授予能力失败：找不到 " + leaf);
                    return;
                }

                Character player = CharacterPlayer.Instance;
                if (player == null) { return; }
                new SubCommandApplyAbility(player, so.abilityInfo, Math.Max(1, layers));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ModKit] 授予能力 " + leaf + " 失败 —— " + e.Message);
            }
        }

        /// <summary>玩家身上是否有「奶蛙存在吗？」的观察者能力。</summary>
        internal static bool HasExistWatch()
        {
            try
            {
                Character player = CharacterPlayer.Instance;
                if (player == null || player.abilities == null) { return false; }
                foreach (AbilityBase ability in player.abilities)
                {
                    if (ability == null || string.IsNullOrEmpty(ability.abilityName)) { continue; }
                    if (string.Equals(CardKeywordMilkFrog.LeafOf(ability.abilityName), "mf_exist_watch",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>奶蛙关键词触发时的观察者收益：抽 1 张，然后随机弃 1 张。</summary>
        internal static async Cysharp.Threading.Tasks.UniTask ExistWatchAsync()
        {
            try
            {
                CardManager manager = CardManager.Instance;
                if (manager == null) { return; }

                await manager.DrawCards(1);

                List<CardBase> hand = manager.cardHand;
                if (hand == null || hand.Count == 0) { return; }
                CardBase discard = hand[UnityEngine.Random.Range(0, hand.Count)];
                if (discard != null)
                {
                    manager.CardDiscardLogic(discard, isActivelyDiscard: true);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ModKit] 奶蛙存在吗：抽弃失败 —— " + e.Message);
            }
        }
    }

    /// <summary>奶蛙王朝：获得能力（卡面文案是「获得：新召唤的奶家人每个数字 +3」）。</summary>
    [ModEffect("mf_grant_dynasty")]
    public sealed class MfGrantDynasty : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            MilkFrogGrants.Grant(MilkFrogDynasty.AbilityLeaf, 1);
        }
    }

    /// <summary>奶蛙存在吗？：获得观察者能力。</summary>
    [ModEffect("mf_grant_exist")]
    public sealed class MfGrantExist : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            MilkFrogGrants.Grant("mf_exist_watch", 1);
        }
    }

    /// <summary>黄桃罐头：被奶蛙结算时，获得 Amount 层加固。</summary>
    [ModEffect("mf_peach_cascade")]
    public sealed class MfPeachCascade : CascadeTriggeredEffect
    {
        public override void ApplyWhenCascading(EffectContext ctx)
        {
            int amount = ctx.AmountAt(0);
            if (amount <= 0) { amount = 6; }
            ctx.GiveAbility("Fortified", amount, CombatantSelector.Self);
        }
    }

    /// <summary>呼叫奶家人：抽 3 张，每抽到 1 张绿色牌获得 1 点绿色法力。</summary>
    [ModEffect("mf_call_family")]
    public sealed class MfCallFamily : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            int green = 0;
            try
            {
                CardManager manager = CardManager.Instance;
                CardBase[] library = manager == null ? null : manager.GetCardLibrary();
                if (library != null)
                {
                    int look = Math.Min(3, library.Length);
                    for (int i = 0; i < look; i++)
                    {
                        CardBase card = library[i];
                        if (card == null) { continue; }
                        CardInfo info = card.GetCardDeepCopyInfo();
                        if (info.cardColorEs != null && Array.IndexOf(info.cardColorEs, ColorE.Green) >= 0)
                        {
                            green++;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ctx.Log("呼叫奶家人：数绿色牌失败 —— " + e.Message);
            }

            ctx.Draw(3);
            if (green > 0) { ctx.GainMana(ManaColor.Green, green); }
            ctx.Log("呼叫奶家人：抽 3 张，绿色牌 " + green + " 张。");
        }
    }
}
