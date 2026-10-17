// =============================================================================
//  ModKit.Game · 卡牌字段构造（Card）—— ContentAssembler 的 partial 分片
//
//  ★ 来历：本文件是 2026-10-13 第二批按子域拆分 ContentAssembler.cs 时切出的，
//    全部内容**逐字节照搬**原文（含 XML 注释原文），只新增本文件头与
//    using / namespace / class 外壳，未改任何逻辑。
//
//  ★ 负责子域：卡牌定义 → 游戏结构的私有构造
//    BuildCardInfo / BuildUpgradeInfo / BuildEffectWithIndex / BuildKeywordInfo /
//    BuildManaCost / BuildCardColors / QualifyCardName。
//
//  ★ 不在本文件：公共入口 AssembleCard（留在主文件）、卡池构造（.CardPools.cs）、
//    卡的注册与读回（.CardRegistration.cs）。
// =============================================================================

using System;
using System.Collections.Generic;
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
        /// <summary>
        /// 构造 <see cref="CardInfo"/>。
        /// </summary>
        /// <remarks>
        /// 必填项依据实测的运行时消费路径（侦察报告 A3）：
        /// <list type="bullet">
        /// <item><c>cardName</c> 必填 —— 空名会让 GetCardSoByName 递归回退到 "EmptyCard"，
        /// 连它都不在字典里时**无限递归爆栈**；</item>
        /// <item><c>cardColorEs</c> 必填 —— null 数组在 CardBase 里抛 ArgumentNullException；</item>
        /// <item><c>cardManaCostColorEArrays</c> 每项必须是 ManaIconDic 里注册过的单色数组；</item>
        /// <item><c>cardKeyWordInfos</c> / <c>cardEffectInfos</c> 显式给空数组而非 null
        /// （走属性会经 CreateDeepCopy 自动补空，但直接反射写绕过属性时不会补）。</item>
        /// </list>
        /// </remarks>
        private CardInfo? BuildCardInfo(ApiCardDef def, AssemblyResult result)
        {
            var resolveType = ResolveTimingToGame(def.Resolve);

            // --- 效果列表 ---
            var effectInfos = new List<CardEffectInfo>();
            if (def.Effects != null)
            {
                foreach (var spec in def.Effects)
                {
                    var effectInfo = BuildEffectInfo(spec, resolveType, def.Id, result);
                    if (effectInfo == null) { continue; }

                    effectInfos.Add(new CardEffectInfo
                    {
                        // 注意：这个值会**覆盖** EffectBase.CardEffectResolveType，是最终生效值。
                        // 与 effectInfo 里那份保持一致即可。
                        cardEffectResolveType = resolveType,
                        effectInfo = effectInfo
                    });
                }
            }

            if (effectInfos.Count == 0 && (def.Keywords == null || def.Keywords.Count == 0))
            {
                result.Errors.Add(def.Id + "：没有任何成功装配的效果，放弃这张卡。");
                return null;
            }

            // --- 关键词 ---
            var keywordInfos = new CardKeyWordInfo[0];
            if (def.Keywords != null && def.Keywords.Count > 0)
            {
                var list = new List<CardKeyWordInfo>();
                foreach (var k in def.Keywords)
                {
                    var kwi = BuildKeywordInfo(k, def.Id, result);
                    if (kwi.HasValue) { list.Add(kwi.Value); }
                }
                keywordInfos = list.ToArray();
            }

            // --- 费用：单色，且必须是本 build 注册过的 ---
            var costs = BuildManaCost(def, result);

            var info = new CardInfo
            {
                cardName = def.Id,
                cardArtAlterName = null,
                rarity = RarityToGame(def.Rarity),
                cardColorEs = BuildCardColors(def),
                cardManaCostColorEArrays = costs,
                cardKeyWordInfos = keywordInfos,
                cardEffectInfos = effectInfos.ToArray(),
                // 升级（"+"）：作者没写 upgrade 时留默认值 = 不可升级
                cardUpgradeInfo = BuildUpgradeInfo(def, resolveType, result)
            };
            return info;
        }

        /// <summary>
        /// 把 JSON 的 <c>upgrade</c> 块装成游戏的 <see cref="CardUpgradeInfo"/>。
        /// </summary>
        /// <remarks>
        /// ★ 游戏判定「这张卡能不能升级」看的就是这个结构里**有没有任何一类增量被开启**
        /// （<c>CardUpgradeInfo.GetHasCardUpgradeInfo()</c>）。
        /// 所以作者**不写** <c>upgrade</c> 时平台留结构体默认值 →
        /// 这张卡就是不可升级的，**这是有意的默认**。
        /// <para>
        /// ★ 增量全部作用在原卡的**深拷贝**上（<c>CardManager.GetUpgradeCardInfo</c>），
        /// 所以不需要另写一张"升级后的卡"。
        /// </para>
        /// </remarks>
        private CardUpgradeInfo BuildUpgradeInfo(ApiCardDef def, CardEffectResolveType resolveType,
            AssemblyResult result)
        {
            var info = default(CardUpgradeInfo);
            var up = def.Upgrade;
            if (up == null) { return info; }   // 没写 upgrade → 不可升级

            // ---- ① 改效果数值（最常用）----
            if (up.EffectNumbers != null && up.EffectNumbers.Count > 0)
            {
                var list = new List<EffectNumberChangeInfo>();
                foreach (var n in up.EffectNumbers)
                {
                    if (!n.Index.HasValue)
                    {
                        result.Errors.Add(def.Id + "：upgrade.effectNumbers 缺少 index。");
                        continue;
                    }
                    list.Add(new EffectNumberChangeInfo
                    {
                        effectIndex = n.Index.Value,
                        rNumberInfos = BuildNumberInfos(n.Amounts).ToArray()
                    });
                }
                if (list.Count > 0)
                {
                    info.changeEffectNumber = true;
                    info.effectNumberChangeInfo = list.ToArray();
                }
            }

            // ---- ② 改费用（整份替换；复用卡牌那套槽装配）----
            //   ★ 解析层用 null 表示"没写 cost"、空列表表示"显式写了空数组"，
            //     所以这里只判 null —— 写 "cost": [] 就是"升级后变成无费用卡"。
            //     （旧版用 Count > 0 判定，会把"升级后免费"静默忽略。）
            if (up.Cost != null)
            {
                var probe = new ApiCardDef { Id = def.Id, Cost = up.Cost };
                info.cardManaCostColorEArrays = BuildManaCost(probe, result);
                info.changeCardCostMana = true;
            }

            // ---- ③ 改卡面流派色（整份替换）----
            if (up.Colors != null && up.Colors.Count > 0)
            {
                var probe = new ApiCardDef { Id = def.Id, Colors = up.Colors };
                info.cardColorEs = BuildCardColors(probe);
                info.changeCardColor = true;
            }

            // ---- ④ 加关键词 ----
            if (up.AddKeywords != null && up.AddKeywords.Count > 0)
            {
                var list = new List<CardKeyWordInfo>();
                foreach (var k in up.AddKeywords)
                {
                    var kwi = BuildKeywordInfo(k, def.Id, result);
                    if (kwi.HasValue) { list.Add(kwi.Value); }
                }
                if (list.Count > 0)
                {
                    info.cardKeyWordInfosToAdd = list.ToArray();
                    info.addCardKeyword = true;
                }
            }

            // ---- ⑤ 删关键词 ----
            if (up.RemoveKeywords != null && up.RemoveKeywords.Count > 0)
            {
                var list = new List<string>();
                foreach (var k in up.RemoveKeywords)
                {
                    if (!string.IsNullOrEmpty(k)) { list.Add(k); }
                }
                if (list.Count > 0)
                {
                    info.cardKeyWordInfosToRemove = list.ToArray();
                    info.removeCardKeyword = true;
                }
            }

            // ---- ⑥ 替换 / 插入效果 ----
            bool hasReplace, hasInsert;
            info.effectReplacers = BuildEffectWithIndex(up.ReplaceEffects, resolveType, def.Id, result, out hasReplace);
            info.effectInserter = BuildEffectWithIndex(up.InsertEffects, resolveType, def.Id, result, out hasInsert);
            if (hasReplace) { info.changeEffect = true; }
            if (hasInsert) { info.insertEffect = true; }

            // ---- ⑦ 改关键词数值 ----
            if (up.KeywordNumbers != null && up.KeywordNumbers.Count > 0)
            {
                var first = up.KeywordNumbers[0];
                if (!string.IsNullOrEmpty(first.Keyword))
                {
                    info.keyWordNumberChangeInfo = new KeyWordNumberChangeInfo
                    {
                        keywordName = first.Keyword,
                        keywordNumber = first.Number ?? 0
                    };
                    info.changeKeywordNumber = true;
                }
            }

            return info;
        }

        private EffectWithIndex[] BuildEffectWithIndex(List<ModKit.Api.EffectReplaceDef> specs,
            CardEffectResolveType resolveType, string ownerId, AssemblyResult result, out bool any)
        {
            any = false;
            if (specs == null || specs.Count == 0) { return new EffectWithIndex[0]; }

            var list = new List<EffectWithIndex>();
            foreach (var s in specs)
            {
                if (!s.Index.HasValue)
                {
                    result.Errors.Add(ownerId + "：upgrade 的效果替换/插入缺少 index。");
                    continue;
                }
                if (s.Effects == null || s.Effects.Count == 0) { continue; }

                var effectInfo = BuildEffectInfo(s.Effects[0], resolveType, ownerId, result);
                if (effectInfo == null) { continue; }

                list.Add(new EffectWithIndex
                {
                    effectIndex = s.Index.Value,
                    cardEffectInfo = new CardEffectInfo
                    {
                        cardEffectResolveType = resolveType,
                        effectInfo = effectInfo
                    }
                });
            }

            if (list.Count == 0) { return new EffectWithIndex[0]; }
            any = true;
            return list.ToArray();
        }

        /// <summary>
        /// 把一个平台关键词声明装成游戏的 <see cref="CardKeyWordInfo"/>；写得不完整时返回 null。
        /// </summary>
        /// <remarks>
        /// ★ 卡牌的 <c>keywords</c> 与升级的 <c>addKeywords</c> **共用这一个方法**。
        /// 两者形态完全一致（字符串，或含 <c>name</c>/<c>resolve</c>/<c>effects</c> 的对象）；
        /// 而原版升级关键词效果的做法正是"**删掉旧的、加一个数值更大的新的**"
        /// （实测原版缓慢剑：<c>addKw=True rmKw=True</c>）。
        /// 两处若各写一份，升级版迟早会与基础版行为不一致。
        /// <para>
        /// ★ 关键词效果有**自己的结算时机**，缺省"打出时"（<c>OnCast</c>）。
        /// 绝不沿用卡牌的 <c>resolve</c> —— 例如「稳手」整张卡是 <c>EndInHand</c>
        /// （回合结束时若留手），但它的奇迹效果是**打出时**触发；
        /// 把两者混成一个，奇迹就不会按预期触发。
        /// </para>
        /// </remarks>
        private CardKeyWordInfo? BuildKeywordInfo(ModKit.Api.KeywordRefDef k,
            string ownerId, AssemblyResult result)
        {
            if (k == null || string.IsNullOrEmpty(k.Name)) { return null; }

            // CardKeyWordInfo 是 struct，主构造接受 (keywordName, number=0)
            var kwi = new CardKeyWordInfo(k.Name);
            // 关键词自带的数值（只有部分关键词用得上，见 KeywordRefDef.Number）。
            if (k.Number.HasValue) { kwi.number = k.Number.Value; }
            // ★ 变身/恢复目标卡（keywordCardSoName）。
            //   平台只填目标 —— 切换时机由游戏的 CardKeyword 子类决定，不要在这里多做。
            if (!string.IsNullOrEmpty(k.ToCard))
            {
                int modDot = ownerId == null ? -1 : ownerId.IndexOf('.');
                kwi.keywordCardSoName = modDot > 0
                    ? ownerId.Substring(0, modDot) + "." + k.ToCard
                    : k.ToCard;
            }

            // ★ 费用颜色串（manaString），例如 Storm 的 "RR"。
            if (!string.IsNullOrEmpty(k.Mana)) { kwi.manaString = k.Mana; }

            // ★ 召唤物的名字（只有 Summon 用得上）。
            //   游戏把 SummonName 写死成"打出这张牌"的卡名，而立绘与显示名都拿它去查表 ——
            //   mod 卡名带前缀，所以两张表都查不到（召唤物照常出现，只是没立绘）。
            //   作者写了自己的召唤物名，就登记给 SummonHook，由它挂钩替换。
            if (!string.IsNullOrEmpty(k.Summon) && !string.IsNullOrEmpty(ownerId))
            {
                SummonHook.Register(ownerId, k.Summon);
            }

            // ★ 关键词**自带的效果**：有些关键词（最典型是「奇迹」Miracle）
            //   唯一的实质内容就是这份效果，由关键词自己决定结算时机。
            if (k.Effects != null && k.Effects.Count > 0)
            {
                var keywordResolve = string.IsNullOrEmpty(k.Resolve)
                    ? ResolveTimingToGame("OnCast")
                    : ResolveTimingToGame(k.Resolve);

                var innerEffects = new List<EffectInfo>();
                foreach (var spec in k.Effects)
                {
                    var innerEffect = BuildEffectInfo(spec, keywordResolve, ownerId, result);
                    if (innerEffect != null) { innerEffects.Add(innerEffect); }
                }
                kwi.effectInfos = innerEffects.ToArray();
            }

            return kwi;
        }

        private ColorEArray[] BuildManaCost(ApiCardDef def, AssemblyResult result)
        {
            var list = new List<ColorEArray>();
            if (def.Cost != null)
            {
                foreach (var slot in def.Cost)
                {
                    if (slot == null || slot.Colors == null || slot.Colors.Count == 0)
                    {
                        // 解析层把非法形态留成 null/空 → 这里如实报告，不擅自解释
                        result.Errors.Add(def.Id + "：存在空的费用槽（写法非法，解析层留空）。");
                        continue;
                    }

                    // ★ 一个槽 = 一个 ColorEArray。
                    //   槽里有几个颜色就装几个：多个颜色的语义是"其中任意一个都能付这一格"
                    //   （这正是原版卡上"虚色或红色"那种图标的实现方式）。
                    var combo = new List<ColorE>();
                    bool allMapped = true;
                    foreach (var name in slot.Colors)
                    {
                        var lookup = ModKit.Core.ManaColorNames.Resolve(name);
                        if (!lookup.Mapped) { allMapped = false; continue; }
                        combo.Add(GameCapabilitiesProbe.ToGameColor(lookup.Value));
                    }
                    if (!allMapped || combo.Count == 0) { continue; }

                    // 组合必须是游戏**注册过图标**的 —— 图标是预制资产，而且顺序参与匹配。
                    if (!_caps.IsKnownCostCombo(combo))
                    {
                        // 校验器本应已拦下；走到这里说明它漏了，如实报告而不是静默放行。
                        result.Errors.Add(def.Id + "：费用槽 [" + string.Join(", ", slot.Colors) +
                            "] 未在本 build 注册图标（校验器应已拦截，请报告此不一致）。");
                        continue;
                    }

                    list.Add(new ColorEArray(combo.ToArray()));
                }
            }

            // ★ 空列表 = **无费用卡**，必须原样返回空数组。
            //   对齐原版「喷烟器 Smoker」：它的 CardInfo.cardManaCostColorEArrays 就是空数组，
            //   卡面一个费用图标都不画。游戏侧空费用是安全的：
            //     · CardBase.SetByCardInfo → 先 CleanCard() 清空，再看 `Length != 0` 决定是否复制，
            //       最后 SetManaCost() 按列表实例化图标 ⇒ 空列表 = 零图标；
            //     · CardKeywordPhantom 用 `if (num > 0)` 保护过。
            //   ★ 旧版这里回退成单色 Void（"费用为空，回退到单色 Void"）—— 那会让作者写的
            //     `"cost": []` 变成"花 1 个虚色"，卡面上多出一个灰色菱形（2026-10-15 实机发现）。
            return list.ToArray();
        }

        private ColorE[] BuildCardColors(ApiCardDef def)
        {
            var list = new List<ColorE>();
            if (def.Colors != null)
            {
                foreach (var name in def.Colors)
                {
                    var lookup = ModKit.Core.ManaColorNames.Resolve(name);
                    if (lookup.Mapped) { list.Add(GameCapabilitiesProbe.ToGameColor(lookup.Value)); }
                }
            }
            if (list.Count == 0)
            {
                // cardColorEs 是必填（null 会抛）→ 三色通吃作为默认
                list.Add(ColorE.Red);
                list.Add(ColorE.Green);
                list.Add(ColorE.Blue);
            }
            return list.ToArray();
        }

        /// <summary>
        /// 构造 <see cref="EffectInfo"/>。
        /// </summary>
        /// <remarks>
        /// 必填 7 项（依据 EffectUtils.GenerateEffectFromEffectInfo 的实际消费）：
        /// <c>effectName</c> / <c>specialEffectString</c>（SpecialEffect 时）/ <c>effectTargetEnum</c> /
        /// <c>isPositive</c> / <c>cardEffectResolveType</c> / <c>effectNumberInfos</c>（Count 决定 rNumbers 长度）。
        /// 其余留默认值安全。
        /// </remarks>
        /// <summary>
        /// 把作者写的「本 mod 内的短 id」补成游戏认的全名（带 mod 前缀）。
        /// </summary>
        /// <remarks>
        /// ★ 2026-10-13 实机证伪：`add_card` / `add_temporary_card` 原先直接写
        /// <c>info.cardName = spec.Name</c>，**没补前缀** ⇒ 游戏按全名查不到
        /// ⇒ 加进来的是一张「**空白卡牌**」（卡面写着"当你看到这个就说明出错了"）。
        /// 这与 <c>toCard</c> 的处理不一致 —— 后者一开始就补了前缀。
        /// <para>已经带点（<c>.</c>）的名字视为作者自己写全了，原样放行。</para>
        /// </remarks>
        private static string QualifyCardName(string name, string ownerId)
        {
            if (string.IsNullOrEmpty(name)) { return name; }
            if (name.IndexOf('.') >= 0) { return name; }
            int modDot = ownerId == null ? -1 : ownerId.IndexOf('.');
            return modDot > 0 ? ownerId.Substring(0, modDot) + "." + name : name;
        }

    }
}
