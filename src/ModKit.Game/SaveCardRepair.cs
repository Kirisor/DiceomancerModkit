// =============================================================================
//  ModKit.Game · 继续游戏后的"卡图补挂"（2026-10-26）
//
//  背景（同一次实机定位）：
//    继续游戏时，`GameManager.Start → InitializeGame → DeserializeCampaignInfo`
//    会用存档里的 CardInfo 重建每一张卡。而 mod 的 CardSo 只有在平台流水线里
//    才会注册进 CardDataManager —— 那一刻流水线还没跑（它要等战斗场地出现）。
//    于是 `GetCardSoByName("naifrog.xxx")` 落空、退回 EmptyCard：
//      · 名字/描述/颜色/费用/效果都来自存档里的 CardInfo ⇒ 玩法是对的；
//      · 但卡图来自 CardSo.cardArt ⇒ **卡面是空白的**。
//
//  本文件的做法：流水线把卡 SO 注册好之后，回头把"当时没认出来"的那些玩家卡
//  重新挂到真正的 CardSo 上，并刷新卡图/配色。
//
//  ★ 只碰"确实没认出来"的卡：
//    判据是它当前挂的 SO 名为 EmptyCard（或干脆没有）。正常新建的卡、
//    原版卡、已经挂对的卡一律跳过 —— 这是个**幂等的补丁动作**，
//    新游戏里它什么都不会做。
//
//  ★ 只用两个入口：写一个私有字段 `_linkedCardSo`（必须，游戏没有公开 setter），
//    然后调用**公开**的 `ChangeCardArtAlterName` 让游戏自己把卡图刷到材质上
//    （它内部就是 `SetArtRelatedByCardSo()` + `SetCardVisualColorByColorE()`）。
//    不重放 `SetByCardInfo` —— 那会重建关键词/效果/数字，风险远大于收益。
// =============================================================================

using System;
using System.Collections.Generic;
using Card;
using Combat;

namespace ModKit.Game
{
    /// <summary>把继续游戏时"没认出来"的 mod 卡，重新挂回真正的 CardSo。</summary>
    internal static class SaveCardRepair
    {
        /// <summary>空卡 SO 的名字（<c>CardDataManager.GetCardSoByName</c> 的兜底返回值）。</summary>
        private const string EmptyCardName = "EmptyCard";

        /// <summary>
        /// 用刚注册好的一批 mod 卡 SO，修补玩家手里的同名卡。
        /// </summary>
        /// <param name="modCards">本 mod 本次装配出来的卡 SO（可按名查）。</param>
        internal static void RelinkContinuedCards(List<CardSo> modCards)
        {
            try
            {
                if (modCards == null || modCards.Count == 0) { return; }

                var cm = CardManager.Instance;
                if (cm == null) { return; }

                var byName = new Dictionary<string, CardSo>(StringComparer.Ordinal);
                foreach (var so in modCards)
                {
                    if (so == null || string.IsNullOrEmpty(so.cardName)) { continue; }
                    if (!byName.ContainsKey(so.cardName)) { byName[so.cardName] = so; }
                }
                if (byName.Count == 0) { return; }

                CardBase[] cards;
                try { cards = cm.GetAllPlayerCards(); }
                catch { return; }
                if (cards == null || cards.Length == 0) { return; }

                int examined = 0, repaired = 0;
                foreach (var card in cards)
                {
                    if (card == null) { continue; }
                    examined++;

                    try
                    {
                        if (!TryRelink(card, byName)) { continue; }
                        repaired++;
                    }
                    catch (Exception ex)
                    {
                        ModKitLog.Warn("继续游戏修复：单张卡补挂失败（已跳过）—— " +
                            ex.GetType().Name + ": " + ex.Message);
                    }
                }

                if (repaired > 0)
                {
                    ModKitLog.Info("继续游戏修复：已把 " + repaired + " 张卡的卡图 SO 重新挂上" +
                        "（共检查 " + examined + " 张玩家卡）。");
                }
            }
            catch (Exception ex)
            {
                ModKitLog.Warn("继续游戏修复失败（不影响其它功能）：" +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool TryRelink(CardBase card, Dictionary<string, CardSo> byName)
        {
            string name;
            try { name = card.CardName; } catch { return false; }
            if (string.IsNullOrEmpty(name)) { return false; }

            CardSo target;
            if (!byName.TryGetValue(name, out target) || target == null) { return false; }

            CardSo current;
            try { current = Reflect.Get<CardSo>(card, "_linkedCardSo"); } catch { return false; }

            // 已经挂对了 / 挂的是真 SO（原版卡或新游戏建的卡）→ 不动
            if (current == target) { return false; }
            if (current != null && current.cardName != EmptyCardName) { return false; }

            // 存档里的卡可能没有颜色（异常数据）→ 此时不刷，避免 IndexOutOfRange
            CardInfo info;
            try { info = card.GetCardDeepCopyInfo(); } catch { return false; }
            if (info.cardColorEs == null || info.cardColorEs.Length == 0) { return false; }

            Reflect.Set(card, "_linkedCardSo", target);

            // 公开入口：内部会重取 _linkedCardSo.cardArt 并刷到卡面材质上
            card.ChangeCardArtAlterName(info.cardArtAlterName);
            return true;
        }
    }
}
