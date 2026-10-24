// =============================================================================
//  ModKit.Game · 卡的注册与读回（CardRegistration）—— ContentAssembler 的 partial 分片
//
//  ★ 来历：本文件是 2026-10-13 第二批按子域拆分 ContentAssembler.cs 时切出的，
//    全部内容**逐字节照搬**原文（含 XML 注释原文），只新增本文件头与
//    using / namespace / class 外壳，未改任何逻辑。
//
//  ★ 负责子域：卡进容器、进战役池、读回断言
//    RegisterCardSo / RegisterCardCollection / RefreshCampaignCardPool /
//    VerifyCardsReadBack / AddToCampaignPool。
//
//  ★ 不在本文件：单卡字段构造（.Cards.cs）、卡池构造（.CardPools.cs）。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Card;
using Effects;
using GameData;
using DiceUtilities;
using Combat;
using Keyword;
using Trigger;
using Relic;
using RollNumber;
using Loot;
using ModKit.Core;
using UnityEngine;
using HarmonyLib;

// 别名：ModKit.Api 的枚举与游戏同名（Rarity / ResolveTiming 等），
// 同时 using 两个命名空间会 CS0104 二义。用别名保持可读性又不冲突。
using ApiRarity = ModKit.Api.Rarity;
using ApiResolveTiming = ModKit.Api.ResolveTiming;
using ApiBuiltinEffect = ModKit.Api.BuiltinEffect;
using ApiCombatantSelector = ModKit.Api.CombatantSelector;
using ApiManaColor = ModKit.Api.ManaColor;
using ApiCardDef = ModKit.Api.CardDef;
using ApiRelicDef = ModKit.Api.RelicDef;
using ApiEffectSpec = ModKit.Api.EffectSpec;

namespace ModKit.Game
{
    public sealed partial class ContentAssembler
    {
        // ---------------------------------------------------------------------
        // 注册
        // ---------------------------------------------------------------------

        private void RegisterCardSo(CardSo so, string cardName, AssemblyResult result)
        {
            try
            {
                // _allInGameCardSoDictionary 是直接决定 GetCardSoByName 的唯一入口。
                // 不写它 = 卡一放出去就退化成 EmptyCard（且递归回退可能爆栈）。
                var dict = Reflect.Get<Dictionary<string, CardSo>>(
                    CardDataManager.Instance, "_allInGameCardSoDictionary");

                if (dict == null)
                {
                    result.Errors.Add(cardName + "：_allInGameCardSoDictionary 取不到，卡未注册。");
                    return;
                }

                dict[cardName] = so;
            }
            catch (Exception ex)
            {
                result.Errors.Add(cardName + "：注册卡失败 —— " + ex.Message);
            }
        }

        /// <summary>所有 mod 的衍生卡全名（被别的卡 toCard / keywordCardSoName 指向的卡）。</summary>
        /// <remarks>装配期由 <see cref="CollectDerivedCards"/> 填；图鉴与卡池共用这一份。</remarks>
        internal static readonly HashSet<string> DerivedCardNames = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 扫出这批卡里的衍生卡：任何卡的**关键词**里 <c>keywordCardSoName</c>（= JSON 的 toCard）
        /// 指向的卡名，都算衍生卡。
        /// </summary>
        /// <remarks>
        /// 只能识别"由关键词声明"的那一类（精粹/装填弹/建造完成卡都是这种写法）。
        /// 效果里 <c>add_card</c> 之类动态生成的卡不在此列（游戏侧也没有静态清单）。
        /// </remarks>
        private static HashSet<string> CollectDerivedCards(List<CardSo> cards)
        {
            var derived = new HashSet<string>(StringComparer.Ordinal);
            if (cards == null) { return derived; }

            foreach (var so in cards)
            {
                if (so == null) { continue; }
                CardInfo info;
                try { info = so.CardInfo; } catch { continue; }
                if (info.cardKeyWordInfos == null) { continue; }

                foreach (var kw in info.cardKeyWordInfos)
                {
                    if (!string.IsNullOrEmpty(kw.keywordCardSoName)) { derived.Add(kw.keywordCardSoName); }
                }
            }
            return derived;
        }

        /// <summary>
        /// 把本 mod 的卡注册成一个 <c>CardDateCollection</c>，并重建战役池。
        /// </summary>
        /// <remarks>
        /// ★ 为什么必须这样做（而不是直接往 <c>campaignCardPools</c> 里 Add）——
        /// 这是本轮实测查明的**真实缺陷**：
        /// <code>
        /// private void SetCampaignCardPool()
        /// {
        ///     campaignCardPools = new List&lt;CardSo&gt;();                      // ← 整个重建
        ///     foreach (col in GetStandardCardDataCollectionByColorE(used))
        ///         campaignCardPools.AddRange(col.GetAllCards());            // ← 只从集合灌
        /// }
        /// </code>
        /// 它**只从"卡数据集合"重建**池子，所以直接 Add 进池的卡会在
        /// <c>SetCampaignUsedColorE</c> / 开新局时被执行这个方法**冲掉** ——
        /// 卡还留在 <c>_allInGameCardSoDictionary</c> 里（能按名查到），但**抽不到**。
        /// 症状是"卡存在却永远不出现在牌库里"，且**没有任何报错**。
        /// <para>
        /// 正确做法是让卡成为**池子的来源**：注册一个 collection 进
        /// <c>_colorCardDateCollections</c>，然后再触发一次池子重建。
        /// </para>
        /// <para>
        /// <b>为什么 collection 的 <c>colorEs</c> 给空数组</b>：过滤条件是
        /// <c>collection.colorEs.All(usedColorE.Contains)</c> —— 空集是任意集合的子集，
        /// 所以空数组恒为真，不必知道本局的用色（那是个私有字段）。
        /// </para>
        /// </remarks>
        internal void RegisterCardCollection(string modId, List<CardSo> cards, AssemblyResult result)
        {
            if (cards == null || cards.Count == 0) { return; }

            try
            {
                var cdm = CardDataManager.Instance;
                if (cdm == null)
                {
                    result.Warnings.Add(modId + "：CardDataManager.Instance 为 null，卡未进集合（可稍后重试）。");
                    return;
                }

                var collections = Reflect.Get<List<CardDateCollection>>(cdm, "_colorCardDateCollections");
                if (collections == null)
                {
                    result.Warnings.Add(modId + "：_colorCardDateCollections 取不到，卡未进集合。");
                    return;
                }

                // 已在集合里就别重复注册（幂等：重跑流水线/多场景时不重复堆积）
                string collectionName = "ModKit_" + modId;
                for (int i = 0; i < collections.Count; i++)
                {
                    if (collections[i] != null && collections[i].cardCollectionName == collectionName)
                    {
                        _log.Info("集合 " + collectionName + " 已存在，跳过重复注册。");
                        RefreshCampaignCardPool(result, modId);
                        return;
                    }
                }

                // ★ 衍生卡（被别人的 Summon/toCard 指向的卡：精粹、装填弹、建造完成卡…）
                //   不进**卡池集合** —— 它们只能由效果生成。此前只挡了"开局发放"，
                //   集合里仍然带着它们，于是战役池/奖励里会冒出精粹卡（2026-10-24 实机反馈）。
                var derived = CollectDerivedCards(cards);
                foreach (var name in derived) { DerivedCardNames.Add(name); }

                var all = cards.Where(c => c != null && !derived.Contains(c.cardName)).ToArray();
                if (all.Length != cards.Count)
                {
                    _log.Info("卡池集合：已排除 " + (cards.Count - all.Length) + " 张衍生卡（只由效果生成）。");
                }
                var collection = new CardDateCollection
                {
                    cardCollectionType = CardCollectionType.Standard,
                    // 空数组 = 恒满足 colorEs ⊆ usedColorE 的过滤条件（空集是任意集合的子集）
                    colorEs = new ColorE[0],
                    isInRandomPool = true,
                    cardCollectionName = collectionName,
                    // 全放 commonCards：GetAllCards() = common.Concat(unCommon).Concat(rare)，
                    // 放哪个桶都会进池；稀有度已由 CardInfo 自己带，这里只影响"进哪条查询路径"。
                    commonCards = all,
                    unCommonCards = new CardSo[0],
                    rareCards = new CardSo[0],
                    startDeck = new CardSoAndAmount[0],
                    startRelic = new RelicSo[0]
                };

                collections.Add(collection);
                _log.Info("已注册卡集合：" + collectionName + "（" + all.Length + " 张）");

                RefreshCampaignCardPool(result, modId);
            }
            catch (Exception ex)
            {
                result.Warnings.Add(modId + "：注册卡集合失败（卡仍可按名查询）—— " + ex.Message);
            }
        }

        /// <summary>
        /// 重建战役卡池，让刚注册的集合生效。
        /// </summary>
        /// <remarks>
        /// <c>SetCampaignCardPool</c> 是 private → 必须反射调用。
        /// 它内部会 new 一个新 List，所以调用后 <c>campaignCardPools</c> 指向新对象 ——
        /// 这一点很重要：调用方**不能缓存**旧引用。
        /// </remarks>
        private void RefreshCampaignCardPool(AssemblyResult result, string modId)
        {
            try
            {
                var cm = CampaignManager.Instance;
                if (cm == null)
                {
                    result.Warnings.Add(modId + "：CampaignManager.Instance 为 null，池子未重建（下一局会自然包含）。");
                    return;
                }

                var setPool = AccessTools.Method(typeof(CampaignManager), "SetCampaignCardPool");
                if (setPool == null)
                {
                    result.Warnings.Add(modId + "：找不到 SetCampaignCardPool（卡已进集合，下一局会包含）。");
                    return;
                }
                setPool.Invoke(cm, null);
                _log.Info("已重建战役卡池（SetCampaignCardPool）。");
            }
            catch (Exception ex)
            {
                // 池子没重建不致命：卡已进集合，下一局建池时会自然带上
                result.Warnings.Add(modId + "：重建战役卡池失败（卡已进集合，下一局会包含）—— " + ex.Message);
            }
        }

        /// <summary>
        /// 装配后的**读回断言**：确认"平台以为注册成功"与"游戏真的能取到"一致。
        /// </summary>
        /// <remarks>
        /// 这是本轮加的核心防护。平台此前只验证到"我写进去了"，
        /// 而真正决定作者体验的是"游戏能不能取到"。两者不一致的形态有两种，
        /// 且都是**静默**的：
        /// <list type="number">
        /// <item>写进了错的容器 → <c>GetCardSoByName</c> 取不到；</item>
        /// <item>取得到但不在池子里 → 永远抽不到（本轮实测到的那个缺陷）。</item>
        /// </list>
        /// 所以这里**按游戏自己的查询入口**读回来验证，而不是断言"我写成功了"。
        /// </remarks>
        internal void VerifyCardsReadBack(string modId, List<CardSo> cards, AssemblyResult result)
        {
            if (cards == null || cards.Count == 0) { return; }

            try
            {
                var cdm = CardDataManager.Instance;
                if (cdm == null)
                {
                    result.Errors.Add(modId + "：读回断言无法执行（CardDataManager.Instance 为 null）。");
                    return;
                }

                // ① 按名查得到吗（走游戏自己的入口 GetCardSoByName）
                int missing = 0;
                foreach (var so in cards)
                {
                    if (so == null) { continue; }

                    // cardInfo 是 private 字段（CardInfo 属性会返回深拷贝，改名了就对不上，
                    // 所以这里读字段本身）
                    CardInfo info;
                    try { info = (CardInfo)Reflect.Get(so, "cardInfo"); }
                    catch { continue; }

                    if (string.IsNullOrEmpty(info.cardName)) { continue; }

                    var found = cdm.GetCardSoByName(info.cardName);
                    if (found == null)
                    {
                        missing++;
                        result.Errors.Add(info.cardName +
                            "：读回失败 —— GetCardSoByName 取不到（注册进了错的容器？）。");
                    }
                }

                // ② 在战役池里吗（否则卡存在但抽不到）
                var cm = CampaignManager.Instance;
                if (cm != null && cm.campaignCardPools != null)
                {
                    foreach (var so in cards)
                    {
                        if (so == null) { continue; }
                        if (!cm.campaignCardPools.Contains(so))
                        {
                            result.Warnings.Add(so.name +
                                "：不在战役卡池里 —— 本局抽不到（通常是 SetCampaignCardPool 之后才装配，下一局会包含）。");
                        }
                    }
                }

                int inPool = 0;
                if (cm != null && cm.campaignCardPools != null)
                {
                    foreach (var so in cards) { if (so != null && cm.campaignCardPools.Contains(so)) inPool++; }
                }

                result.ReadBackSummary = string.Format(
                    "{0}：按名可取 {1}/{2}，在战役池 {3}/{2}",
                    modId, cards.Count - missing, cards.Count, inPool);

                if (missing == 0)
                {
                    _log.Info("读回断言通过：" + result.ReadBackSummary);
                }
                else
                {
                    _log.Error("读回断言失败：" + result.ReadBackSummary);
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(modId + "：读回断言本身抛异常 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// 把新卡加入战役随机池。
        /// </summary>
        /// <remarks>
        /// 三条真实入口里最直接的是 <c>CampaignManager.campaignCardPools</c>（public List）。
        /// <b>时序很重要</b>：<c>SetCampaignCardPool()</c> 会**整体重建**这个列表，
        /// 所以追加必须发生在那之后，否则会被清掉。
        /// 另一条路是改 <c>_colorCardDateCollections</c> 里的 collection，但那是私有容器。
        /// </remarks>
        private void AddToCampaignPool(CardSo so, AssemblyResult result, string cardName)
        {
            try
            {
                var cm = CampaignManager.Instance;
                if (cm == null)
                {
                    result.Warnings.Add(cardName + "：CampaignManager.Instance 为 null，暂未进池（可稍后重试）。");
                    return;
                }

                var pool = cm.campaignCardPools;
                if (pool == null)
                {
                    result.Warnings.Add(cardName + "：campaignCardPools 为 null，暂未进池。");
                    return;
                }

                if (!pool.Contains(so))
                {
                    pool.Add(so);
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add(cardName + "：进战役池失败（卡仍可按名查询）—— " + ex.Message);
            }
        }

    }
}
