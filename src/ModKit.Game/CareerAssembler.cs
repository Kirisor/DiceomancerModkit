// =============================================================================
//  ModKit.Game · 职业装配（新职业 = 一颗新球 + 一个 Class 牌组集合 + 一份初始牌组）
//
//  游戏侧的三件事（2026-10-15 勘察，全部来自反编译源码）：
//    ① 职业名：`CardDataManager.GetClassNameByColorE(ColorE[])`
//       要求 **恰好 3 个颜色**，在 `_colorCardDateCollections` 里找
//       `cardCollectionType == Class` 且 `colorEs` 为其子集的集合，返回其 `cardCollectionName`。
//       ⇒ 加一个 Class 集合 = 多一个职业；而且 `GetStandardCardDataCollectionByColorE`
//         是按颜色子集收人，所以这个集合的卡会**自动进该职业的战役卡池**。
//    ② 开局牌组 / 起始遗物：`CareerInformationForGameInitialize.allStartDeck`（List<StartColorESDecks>），
//       按 `colorEs` 与玩家选中的 3 色比对（`DitwUtil.CheckTwoArraySame`）。
//    ③ 选球界面介绍卡 `ClassIntroCard.SetByKey`：
//       文案取本地化表 **`Encyclopedia`** 的 `<classKey>` 与 `<classKey>_Intro`；
//       并且**硬取 `relicSos[1]`** 做护符预览 ⇒ 起始遗物少于 2 件会直接抛异常。
//
//  ⇒ 本类只做"把这三件事写进游戏"，球的出现与状态由 CareerPatch 负责。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Card;
using Career;
using Combat;
using GameData;
using HarmonyLib;
using ModKit.Api;
using Relic;

namespace ModKit.Game
{
    /// <summary>把 mod 声明的职业装配进游戏的职业/色球系统。</summary>
    internal static class CareerAssembler
    {
        /// <summary>已成功装配的 mod 职业（供自建职业选择界面枚举）。</summary>
        internal static readonly System.Collections.Generic.List<ModKit.Api.ClassSpec> DeclaredSpecs =
            new System.Collections.Generic.List<ModKit.Api.ClassSpec>();

        /// <summary>各职业的球贴图绝对路径（供职业选择界面显示图标）。</summary>
        internal static readonly System.Collections.Generic.Dictionary<ModKit.Api.ClassSpec, string> SpecOrbSprites =
            new System.Collections.Generic.Dictionary<ModKit.Api.ClassSpec, string>();

        /// <summary>各职业的 classKey（= 本地化前缀 / Class 集合名 / StartColorESDecks.classKey）。</summary>
        private static readonly System.Collections.Generic.Dictionary<ModKit.Api.ClassSpec, string> _specKeys =
            new System.Collections.Generic.Dictionary<ModKit.Api.ClassSpec, string>();

/// <summary>本地化表名 —— 职业名与介绍都在这里（ClassIntroCard 写死的）。</summary>
        private const string EncyclopediaTable = "Encyclopedia";

        /// <summary>原版已经做成球的 5 色（Black 不在其中 ⇒ 新职业至少要用 Black 才不撞车）。</summary>
        private static readonly ColorE[] VanillaOrbColors = new ColorE[]
        {
            ColorE.Red, ColorE.Green, ColorE.Blue, ColorE.Purple, ColorE.Void
        };

        internal static void Assemble(
            string modId,
            string modRoot,
            ClassSpec spec,
            List<CardSo> modCards,
            List<RelicSo> modRelics,
            LocalizationPump pump,
            AssemblyResult result,
            IGameLog log)
        {
            if (spec == null) { return; }
            if (!DeclaredSpecs.Contains(spec)) { DeclaredSpecs.Add(spec); }

            try
            {
                // ---- 0. 校验（宁可在加载期报清楚，也不要开局界面才炸）----
                if (spec.Colors == null || spec.Colors.Count != 3)
                {
                    result.Errors.Add(modId + "：职业 " + spec.Id + " 的 colors 必须恰好 3 个（游戏按 3 色组合认职业）。");
                    return;
                }

                // 这个职业要占用的"新球"（由 mod 声明；平台只校验它确实是原版没做成球的颜色）
                ColorE orbColor = GameCapabilitiesProbe.ToGameColor(spec.OrbColor);
                if (VanillaOrbColors.Contains(orbColor))
                {
                    result.Errors.Add(modId + "：职业 " + spec.Id + " 声明的 orbColor = " + orbColor +
                        " 已经是原版做成球的颜色，会和原版职业撞车。" +
                        "必须挑原版没做成球的（目前只有 Black）。");
                    return;
                }

                var colorEs = new ColorE[3];
                for (int i = 0; i < 3; i++)
                {
                    colorEs[i] = GameCapabilitiesProbe.ToGameColor(spec.Colors[i]);
                }

                if (!colorEs.Contains(orbColor))
                {
                    result.Errors.Add(modId + "：职业 " + spec.Id + " 声明的 orbColor = " + orbColor +
                        " 不在它的 colors 三色里 —— 玩家凑不出这个组合，职业永远选不到。");
                    return;
                }

                if (spec.StartRelics == null || spec.StartRelics.Count < 2)
                {
                    result.Errors.Add(modId + "：职业 " + spec.Id + " 的 startRelics 至少 2 件 —— " +
                        "游戏的职业介绍卡硬取 relicSos[1] 做护符预览，少于 2 件会在开局界面抛异常。");
                    return;
                }

                // ---- 0.5 平台侧启用（只有全部校验通过才会走到这里）----
                //   ★ 这里是"接口 → 平台"的唯一接口点：平台把 mod 声明的球颜色 / 贴图 / 大小落下来，
                //     并**到这一刻才**建每帧宿主。没有职业声明 ⇒ 平台在游戏里零足迹（可随时拆装）。
                CareerPatch.ClassDeclared = true;
                var orbSpritePath = ModArt.Resolve(modRoot, spec.OrbSprite);
                SpecOrbSprites[spec] = orbSpritePath;
                CareerPatch.ConfigureClass(orbColor, orbSpritePath, spec.BallScale, log);
                CareerPatch.EnsureHost(log);

                // ---- 1. 按 id 取出平台的卡 / 遗物（卡名是 mod 前缀归一化后的完整名）----
                var deckCards = new List<CardSo>();
                var deckAmounts = new List<CardSoAndAmount>();
                if (spec.StartDeck != null)
                {
                    foreach (var rawId in spec.StartDeck)
                    {
                        CardSo so = ResolveDeckCard(modId, rawId, modCards);
                        if (so == null)
                        {
                            result.Errors.Add(modId + "：职业 " + spec.Id + " 的初始牌组引用了不存在的卡 '" + rawId + "'。");
                            continue;
                        }
                        deckCards.Add(so);
                        deckAmounts.Add(new CardSoAndAmount { cardSo = so, amount = 1 });
                    }
                }
                if (deckAmounts.Count == 0)
                {
                    result.Errors.Add(modId + "：职业 " + spec.Id + " 的初始牌组是空的。");
                    return;
                }

                var relicSos = new List<RelicSo>();
                foreach (var rawId in spec.StartRelics)
                {
                    RelicSo so = ResolveStartRelic(modId, rawId, modRelics);
                    if (so == null)
                    {
                        result.Errors.Add(modId + "：职业 " + spec.Id + " 的起始遗物引用了不存在的遗物 '" + rawId + "'。");
                        continue;
                    }
                    relicSos.Add(so);
                }
                if (relicSos.Count < 2)
                {
                    result.Errors.Add(modId + "：职业 " + spec.Id + " 解析后只剩 " + relicSos.Count +
                        " 件起始遗物（需要 ≥2）。");
                    return;
                }

                // 职业 key：同时当本地化 key 前缀、牌组集合名、StartColorESDecks.classKey
                string classKey = modId + "." + (string.IsNullOrEmpty(spec.Id) ? "class" : spec.Id);

                // ---- 2. 注册 Class 牌组集合（职业名就是它的 cardCollectionName）----
                var cdm = CardDataManager.Instance;
                if (cdm == null)
                {
                    result.Warnings.Add(modId + "：CardDataManager.Instance 为 null，职业 " + spec.Id + " 未装配（下次启动会重试）。");
                    return;
                }

                var collections = Reflect.Get<List<CardDateCollection>>(cdm, "_colorCardDateCollections");
                if (collections == null)
                {
                    result.Errors.Add(modId + "：_colorCardDateCollections 取不到，职业未装配。");
                    return;
                }

                bool exists = collections.Any(c => c != null && c.cardCollectionName == classKey);
                if (!exists)
                {
                    // 卡池：优先用 spec.PoolCards（职业自己的卡池，决定整局能否变强）；
                    // 没声明就退化成"初始牌组里的去重卡"。
                    var poolCards = new List<CardSo>();
                    int vanillaInPool = 0;
                    if (spec.PoolCards != null && spec.PoolCards.Count > 0)
                    {
                        foreach (var rawId in spec.PoolCards)
                        {
                            // ★ 解析顺序与初始牌组一致（2026-10-30）：先按 "<modId>.<id>" 找本 mod 的卡，
                            //   找不到再当**原版卡名**查游戏数据 —— 职业池里点名原版通用卡是正常需求。
                            //   之前这里只查 mod 自己的卡，于是作者写原版卡名一律报
                            //   「卡池引用了不存在的卡」并被静默跳过（池子永远凑不到目标张数）。
                            CardSo so = ResolveDeckCard(modId, rawId, modCards);
                            if (so == null)
                            {
                                result.Errors.Add(modId + "：职业 " + spec.Id + " 的卡池引用了不存在的卡 '" + rawId + "'。");
                                continue;
                            }
                            if (!poolCards.Contains(so))
                            {
                                poolCards.Add(so);
                                // 计数只看卡名是否带本 mod 前缀（原版卡名没有前缀）
                                if (!so.cardName.StartsWith(modId + ".", StringComparison.Ordinal)) { vanillaInPool++; }
                            }
                        }
                    }
                    // ★ 衍生卡（精粹等）不进职业卡池 —— 它们只能由效果生成（2026-10-24 实机反馈）。
                    poolCards.RemoveAll(c => c == null || ContentAssembler.DerivedCardNames.Contains(c.cardName));

                    if (poolCards.Count == 0)
                    {
                        foreach (var c in deckCards) { if (!poolCards.Contains(c)) { poolCards.Add(c); } }
                        result.Warnings.Add(modId + "：职业 " + spec.Id + " 没声明 poolCards，卡池退化为主牌组去重（"
                            + poolCards.Count + " 张）。");
                    }

                    collections.Add(new CardDateCollection
                    {
                        cardCollectionType = CardCollectionType.Class,
                        colorEs = colorEs,
                        isInRandomPool = true,          // 该职业的卡按颜色子集进入本局卡池
                        cardCollectionName = classKey,
                        commonCards = poolCards.ToArray(),
                        unCommonCards = new CardSo[0],
                        rareCards = new CardSo[0],
                        startDeck = deckAmounts.ToArray(),
                        startRelic = relicSos.ToArray()
                    });
                    log.Info("已注册职业牌组集合：" + classKey + "（卡池 " + poolCards.Count + " 张" +
                        (vanillaInPool > 0 ? "，其中原版卡 " + vanillaInPool + " 张" : "") +
                        " / 初始牌组 " + deckAmounts.Count + " 张 / 色组 " + string.Join("+", colorEs.Select(c => c.ToString())) + "）");
                }

                // ---- 2.2 随机池隔离：把该 mod 的集合/卡登记下来 ----
                //   掉落里的 `CareerPool` 取的是「所有集合的卡」，不区分职业，只靠掉落包自己的
                //   颜色条件过滤 ⇒ 模组卡会漏进原版职业的奖励池。这里登记后由
                //   `ModPoolIsolation` 在「当前职业不是本 mod 的职业」时把它们摘掉。
                try
                {
                    ModPoolIsolation.Register(modId, classKey, colorEs, modCards);
                }
                catch (Exception ex)
                {
                    log.Warn("随机池隔离：登记失败 —— " + ex.Message);
                }

                // ---- 2.5 职业隔离：把该 mod 的「通用卡集合」的 colorEs 收紧成职业三色 ----
                //   原版图鉴（SubCollectionCards.GetCardsByClass）按「集合的 colorEs ⊆ 该职业三色」筛集合，
                //   而**空数组是任意集合的子集** ⇒ 六个职业页会把 mod 卡全部收录。
                //   收紧成职业三色（含 Black，不属于任何原版三色）后就不再出现在任何原版职业页，
                //   只在我们自己的「模组图鉴」里露面。
                try
                {
                    var allCollections = Reflect.Get<List<CardDateCollection>>(cdm, "_colorCardDateCollections");
                    if (allCollections != null)
                    {
                        string standardCollection = "ModKit_" + modId;
                        foreach (var col in allCollections)
                        {
                            if (col == null || col.cardCollectionName != standardCollection) { continue; }
                            if (col.colorEs != null && col.colorEs.SequenceEqual(colorEs)) { continue; }
                            col.colorEs = colorEs.ToArray();
                            log.Info("职业隔离：集合 " + standardCollection + " 的 colorEs 已收紧为 " +
                                string.Join("+", colorEs.Select(c => c.ToString())) + "（原版职业图鉴页不再收录）。");
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Warn("职业隔离：收紧集合 colorEs 失败 —— " + ex.Message);
                }

                // ---- 2.7 基础牌身份：把本职业初始牌组里的 mod 卡并入游戏的 `BasicCard` 集合 ----
                //   ★ 游戏给"初始牌组"一种额外身份：只有 `BasicCard` 集合里的卡才算**基础牌**。
                //     吃这个身份的效果（反编译核对，2026-10-29）：
                //       · `EffectSpEffectUpgradeRandomBasicCard`（小树枝等）—— "升级随机 N 张基础牌"
                //       · `SpecialStoryUpgradeBasicCards` —— 事件"升级 N 张基础牌"
                //       · `CardUtils.CheckCardCoreInfoSpecialRequirement("BasicCard")` —— 卡/效果按身份筛
                //     原版六职业的初始牌组卡天然躺在资产的这个集合里；mod 的卡是运行期新建的
                //     `CardSo`，资产里不可能有 ⇒ 不补这一步，mod 的打击/格挡永远吃不到这些收益。
                try
                {
                    AttachBasicCards(cdm, modId, deckCards, log);
                }
                catch (Exception ex)
                {
                    log.Warn("基础牌：并入 BasicCard 集合失败 —— " + ex.GetType().Name + ": " + ex.Message);
                }

                // ---- 3. 开局牌组 / 起始遗物 + 把新球放进序列化色表 ----
                var init = CareerManager.Instance == null ? null : CareerManager.Instance.GetCareerInformationForInitialize();
                if (init == null)
                {
                    result.Warnings.Add(modId + "：CareerInformationForGameInitialize 取不到，开局牌组未写入（职业仍在，牌组会是空）。");
                }
                else
                {
                    if (init.allStartDeck == null) { init.allStartDeck = new List<StartColorESDecks>(); }
                    _specKeys[spec] = classKey;
                    if (!init.allStartDeck.Any(d => d != null && d.classKey == classKey))
                    {
                        init.allStartDeck.Add(new StartColorESDecks
                        {
                            colorEs = colorEs,
                            deck = deckAmounts.ToArray(),
                            relicSos = relicSos.ToArray(),
                            classKey = classKey
                        });
                        log.Info("已登记开局牌组：" + classKey + "（" + deckAmounts.Count + " 张牌、" +
                            relicSos.Count + " 件起始遗物）");
                    }

                    // ★ 序列化的色表也要补一条：RefreshCareerOrbState 是按颜色 Find 的，
                    //   而选球界面读的就是这份表（CareerPatch 另外给计算结果也补了一条，双保险）。
                    if (init.colorEAndStates == null) { init.colorEAndStates = new List<ColorEAndState>(); }
                    if (!init.colorEAndStates.Any(s => s != null && s.colorE == CareerPatch.NewOrbColor))
                    {
                        init.colorEAndStates.Add(new ColorEAndState(CareerPatch.NewOrbColor, ColorEState.Free));
                    }
                }

                // ---- 4. 文案：Encyclopedia.<classKey> = 职业名，<classKey>_Intro = 介绍 ----
                if (pump != null)
                {
                    if (!string.IsNullOrEmpty(spec.NameZh))
                    {
                        pump.Add("zh-Hans", EncyclopediaTable, classKey, spec.NameZh);
                    }
                    if (!string.IsNullOrEmpty(spec.IntroZh))
                    {
                        pump.Add("zh-Hans", EncyclopediaTable, classKey + "_Intro", spec.IntroZh);
                    }
                }

                // ---- 5. 放开新球：从"看得见点不动"变成可选 ----
                CareerPatch.OrbState = ColorEState.Free;

                log.Info("职业装配完成：" + classKey + "（名「" + spec.NameZh + "」/ 球 " + CareerPatch.NewOrbColor +
                    "=" + CareerPatch.OrbState + " / 牌组 " + deckAmounts.Count + " 张）");
            }
            catch (Exception ex)
            {
                result.Errors.Add(modId + "：职业 " + (spec.Id ?? "?") + " 装配异常 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        //  基础牌身份（BasicCard 集合）
        //
        //  游戏把「初始牌组」当成一种**身份**，而不是给卡加字段：`CardDataManager` 里有一个
        //  具名集合 `BasicCard`，判定基础牌的唯一依据就是"卡在不在这个集合里"
        //  （`CardUtils.CheckCardCoreInfoSpecialRequirement` 的 `"BasicCard"` 分支逐字如此）。
        //  原版六职业的初始牌组卡是资产里就写好的，mod 的卡是运行期 new 出来的 `CardSo`，
        //  资产里不可能有 ⇒ 平台必须替它补上身份，否则「升级基础牌」类效果永远跳过它。
        // ------------------------------------------------------------------

        /// <summary>
        /// 把「本 mod 自己的」初始牌组卡并入游戏的 <c>BasicCard</c> 集合（幂等）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>为什么是"初始牌组里的 mod 卡"</b>：原版这个集合装的就是各职业的初始牌组
        /// （打击 / 格挡 / 职业标志牌），不是"所有低费卡"——反编译 <c>EffectSpEffectUltimateStrikeBlock</c>
        /// 还要在集合里再筛一次 <c>cardName.Contains("Strike") || Contains("Block")</c>，
        /// 说明集合里确实不止打击/格挡。mod 职业的初始牌组与它一一对应，按牌组走即与原版同义。
        /// </para>
        /// <para>
        /// <b>为什么只挑本 mod 的卡</b>：初始牌组可以点名原版卡（奶蛙带「至尊骰」Dice），
        /// 原版卡的身份由原版数据决定，平台不去改它。
        /// </para>
        /// <para>
        /// <b>为什么要判重</b>：整条流水线在场景切换 / 重建守卫里会重跑，重复 Add 会让集合里
        /// 出现同一张卡多次（`GetAllCards()` 不去重 ⇒ "升级随机基础牌"的抽签概率被放大）。
        /// </para>
        /// </remarks>
        /// <returns>本次真正加进去的张数（去重后去重前都没有时为 0）。</returns>
        private static int AttachBasicCards(CardDataManager cdm, string modId, List<CardSo> deckCards, IGameLog log)
        {
            if (cdm == null || deckCards == null || deckCards.Count == 0) { return 0; }

            // 只挑本 mod 自己的卡（cardName 已归一化成 "<modId>.<id>"）
            string prefix = modId + ".";
            var own = new List<CardSo>();
            foreach (var c in deckCards)
            {
                if (c == null || string.IsNullOrEmpty(c.cardName)) { continue; }
                if (!c.cardName.StartsWith(prefix, StringComparison.Ordinal)) { continue; }
                if (own.Contains(c)) { continue; }      // 初始牌组里同一种卡出现多次（4 张打击）
                own.Add(c);
            }
            if (own.Count == 0) { return 0; }

            CardDateCollection basic;
            try
            {
                // ⚠️ 这个方法内部是 Dictionary 索引器：集合名写错会抛 KeyNotFoundException，
                //    而且消息里只有一个空字符串 —— 这里自己兜住并说清楚。
                basic = cdm.GetSpecialCardDataCollectionByName("BasicCard");
            }
            catch (Exception ex)
            {
                log?.Warn("基础牌：取不到游戏的 BasicCard 集合，mod 的初始牌这次没有基础牌身份 —— " +
                    ex.GetType().Name + ": " + ex.Message);
                return 0;
            }
            if (basic == null)
            {
                log?.Warn("基础牌：游戏的 BasicCard 集合是 null，mod 的初始牌这次没有基础牌身份。");
                return 0;
            }

            // 判重（集合里三桶都要看：GetAllCards() = common + uncommon + rare）
            var have = new HashSet<string>(StringComparer.Ordinal);
            AddNames(have, basic.commonCards);
            AddNames(have, basic.unCommonCards);
            AddNames(have, basic.rareCards);

            var add = new List<CardSo>();
            foreach (var c in own)
            {
                if (have.Contains(c.cardName)) { continue; }
                add.Add(c);
            }
            if (add.Count == 0) { return 0; }

            // 放到 commonCards 桶：`GetAllCards()` 三桶都会拼上，放哪桶都进池；
            // 稀有度本来就写在卡自己的 CardInfo 里，这里不影响掉落权重。
            var merged = new List<CardSo>(basic.commonCards ?? new CardSo[0]);
            merged.AddRange(add);
            basic.commonCards = merged.ToArray();

            // 读回：游戏判"基础牌"是 `GetAllCards()`（三桶拼接）⇒ 必须从那里再看一眼，
            // 才能证明我们没把卡塞进一个它读不到的桶里。
            string backText = "";
            try
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                AddNames(seen, basic.GetAllCards());
                int back = 0;
                foreach (var c in add) { if (seen.Contains(c.cardName)) { back++; } }
                backText = "，读回 " + back + "/" + add.Count + "，集合共 " + seen.Count + " 张";
                if (back != add.Count)
                {
                    log?.Warn("基础牌：读回只有 " + back + "/" + add.Count +
                        " 张出现在 BasicCard 集合里 —— 「升级基础牌」可能仍会跳过被漏掉的那几张。");
                }
            }
            catch (Exception ex)
            {
                log?.Warn("基础牌：读回 BasicCard 失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }

            var names = new List<string>();
            foreach (var c in add) { names.Add(c.cardName); }
            log?.Info("基础牌：已把 " + modId + " 的 " + add.Count + " 张初始牌并入游戏的 BasicCard 集合（" +
                string.Join(" / ", names) + backText + "）—— 「升级基础牌」类效果从现在起会认它们。");
            return add.Count;
        }

        private static void AddNames(HashSet<string> target, CardSo[] cards)
        {
            if (cards == null) { return; }
            for (int i = 0; i < cards.Length; i++)
            {
                if (cards[i] != null && !string.IsNullOrEmpty(cards[i].cardName)) { target.Add(cards[i].cardName); }
            }
        }

        // ------------------------------------------------------------------
        //  开局牌组：缓存 / 重写回 / 直接生成 / 发牌前兜底
        //
        //  背景（实机 2026-10-20）：自建面板选完职业后进战斗一张手牌都没有。
        //  ① 原版只在 `LeaveSubSmallGame` 里生成牌组（SetStartDeckAndRelics），自建面板绕开了它；
        //  ② 补调 `SetStartDeckAndRelics(true)` 抛 TargetInvocationException——最可能是
        //     `GetStartDeckByColorE` 在 `allStartDeck` 里匹配不到（这份表随场景重载被换掉），
        //     于是 `startDeck` 为 null，后续 `startDeck.deck` 空引用；
        //  ③ 也不能等战斗里再补：`GameManager` 的顺序是
        //     `GameStartPlayerCardInitial()`（发初始手牌）**先于** `ManaManagerInitialize()`，
        //     所以补生成必须挂在**发牌之前**，即 `CardManager.GameStartPlayerCardInitial` 的前缀。
        // ------------------------------------------------------------------

        /// <summary>取该职业的 classKey（装配时记录）。</summary>
        internal static string GetClassKey(ModKit.Api.ClassSpec spec)
        {
            string key;
            return (spec != null && _specKeys.TryGetValue(spec, out key)) ? key : null;
        }

        /// <summary>
        /// 确保存档统计里有这个职业并已解锁。
        /// 原版 `CareerStatisticsPackage.Validate()` 会扫所有 `Class` 集合补条目，
        /// 但那是"进游戏时"才跑；主界面的升魔盒/进阶在此之前就要用这条数据。
        /// 缺条目时 `CampaignManager.NewGameLaunchInitialize` 里的
        /// `classInfos.FirstOrDefault(...).isUnlocked = true` 会直接空引用。
        /// </summary>
        internal static void EnsureClassUnlocked(string classKey, IGameLog log)
        {
            if (string.IsNullOrEmpty(classKey)) { return; }
            try
            {
                var pkg = CareerManager.Instance == null ? null : CareerManager.Instance.careerStatisticsPackage;
                if (pkg == null) { return; }
                if (pkg.classInfos == null) { pkg.classInfos = new List<CareerStatisticsInfoForClass>(); }

                var info = pkg.classInfos.FirstOrDefault(c => c != null && c.className == classKey);
                if (info == null)
                {
                    info = new CareerStatisticsInfoForClass { className = classKey };
                    pkg.classInfos.Add(info);
                    if (log != null) { log.Info("职业统计：已补 " + classKey + " 的 classInfos 条目（否则新档开局会空引用）。"); }
                }
                if (!info.isUnlocked)
                {
                    info.isUnlocked = true;
                    if (log != null) { log.Info("职业统计：已把 " + classKey + " 置为已解锁。"); }
                }
            }
            catch (Exception ex)
            {
                if (log != null) { log.Warn("职业统计：处理 " + classKey + " 失败 —— " + ex.GetType().Name + ": " + ex.Message); }
            }
        }
        /// <summary>
        /// 解析初始牌组的一张卡：先按 "&lt;modId&gt;.&lt;id&gt;" 在本 mod 的卡里找，
        /// 找不到再当作**原版卡名**去游戏数据里找 —— 这样职业可以点名带原版卡入组
        /// （例：奶蛙职业自带「至尊骰」Dice）。
        /// </summary>
        /// <remarks>
        /// ★ 不能用 <c>null</c> 判断"没找到"：游戏 <c>CardDataManager.GetCardSoByName</c>
        ///   查不到时**返回 EmptyCard（"空白卡牌"）而不是 null**。判 null 会让"按原版卡名兜底"
        ///   那一步永远不执行，牌组里就多出一张空白卡（2026-10-22 实机：初始牌组点名原版卡
        ///   "Dice"，进战斗变成空白卡；Player.log 记录 "manual_demo.Dice is not found in
        ///   CardDataManager"，而 GetCardSoByName 已经把 EmptyCard 当成"找到了"）。
        /// </remarks>
        private static CardSo ResolveDeckCard(string modId, string rawId, List<CardSo> modCards)
        {
            if (string.IsNullOrEmpty(rawId)) { return null; }
            string qualified = Qualify(modId, rawId);

            var so = modCards == null ? null : modCards.FirstOrDefault(c => c != null && c.cardName == qualified);
            if (so != null) { return so; }

            try
            {
                var cdm = CardDataManager.Instance;
                if (cdm == null) { return null; }

                so = cdm.GetCardSoByName(qualified);
                if (!IsExactCard(so, qualified)) { so = cdm.GetCardSoByName(rawId); }
                return IsExactCard(so, rawId) ? so : null;
            }
            catch { return null; }
        }

        /// <summary>按名查到的卡是否**真的是这张**（空白卡的 cardName 也非空，所以必须比名字）。</summary>
        private static bool IsExactCard(CardSo so, string expectedName)
        {
            return so != null
                && !string.IsNullOrEmpty(so.cardName)
                && so.cardName != "EmptyCard"
                && string.Equals(so.cardName, expectedName, StringComparison.Ordinal);
        }

        /// <summary>解析起始遗物：先本 mod，再原版遗物（例：TheOneDice 至尊骰）。</summary>
        private static RelicSo ResolveStartRelic(string modId, string rawId, List<RelicSo> modRelics)
        {
            if (string.IsNullOrEmpty(rawId)) { return null; }
            string qualified = Qualify(modId, rawId);

            var so = modRelics == null ? null : modRelics.FirstOrDefault(r => r != null && r.relicInfo.relicName == qualified);
            if (so != null) { return so; }

            try
            {
                var rdm = DataManager.Instance == null ? null : DataManager.Instance.GetRelicDataManager();
                if (rdm != null)
                {
                    so = rdm.GetRelicSo(qualified);
                    if (so == null) { so = rdm.GetRelicSo(rawId); }
                }
            }
            catch { }
            return so;
        }

        /// <summary>把 mod 内的短 id 归一化成平台用的完整名（已经是完整名则原样返回）。</summary>
        private static string Qualify(string modId, string id)        {
            if (string.IsNullOrEmpty(id)) { return id; }
            return id.Contains(".") ? id : modId + "." + id;
        }

        /// <summary>
        /// 从 mod 的入口程序集里扫描 <see cref="IModClassProvider"/> 并装配（没有就安静跳过）。
        /// </summary>
        /// <remarks>
        /// 复用平台既有的"扫 mod 程序集"套路（与 <c>[ModEffect]</c> 的发现方式同源），
        /// 好处是**不需要给 mod.json 新增字段**，职业声明跟着 mod 的 C# 走：
        /// 卡 / 遗物仍然写在 JSON 里，职业只是按 id 引用它们。
        /// </remarks>
        internal static void AssembleFromModAssembly(
            string modId,
            string entryAssemblyPath,
            List<CardSo> modCards,
            List<RelicSo> modRelics,
            IGameLog log,
            AssemblyResult result)
        {
            if (string.IsNullOrEmpty(entryAssemblyPath)) { return; }   // 纯数据 mod：没有职业声明
            try
            {
                System.Reflection.Assembly asm = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (!a.IsDynamic && string.Equals(a.Location, entryAssemblyPath, StringComparison.OrdinalIgnoreCase))
                        {
                            asm = a;
                            break;
                        }
                    }
                    catch { /* 动态程序集没有 Location，忽略 */ }
                }
                if (asm == null)
                {
                    result.Warnings.Add(modId + "：入口程序集尚未加载（" + entryAssemblyPath + "），职业声明被跳过。");
                    return;
                }

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException ex)
                {
                    // 引用游戏程序集时必然发生：能拿到多少算多少
                    types = ex.Types.Where(t => t != null).ToArray();
                }

                var providerType = typeof(IModClassProvider);
                int found = 0;
                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || t.IsInterface) { continue; }
                    if (!providerType.IsAssignableFrom(t)) { continue; }
                    found++;

                    ClassSpec spec;
                    try
                    {
                        spec = ((IModClassProvider)Activator.CreateInstance(t)).ProvideClass();
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(modId + "：职业声明 " + t.Name + " 读取失败 —— " + ex.GetType().Name + ": " + ex.Message);
                        continue;
                    }
                    if (spec == null) { continue; }

                    var pump = new LocalizationPump(log);
                    Assemble(modId, System.IO.Path.GetDirectoryName(entryAssemblyPath), spec,
                        modCards, modRelics, pump, result, log);
                    var pumpResult = pump.Flush();
                    if (pumpResult != null && log != null) { log.Info("职业文案写入：" + pumpResult); }
                }

                if (found == 0 && log != null)
                {
                    log.Info("职业声明：本 mod 未实现 IModClassProvider，跳过。");
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add(modId + "：扫描职业声明失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
