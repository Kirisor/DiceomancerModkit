// =============================================================================
//  ModKit.Game · 卡池与需求构造（CardPool）—— ContentAssembler 的 partial 分片
//
//  ★ 来历：本文件是 2026-10-13 第二批按子域拆分 ContentAssembler.cs 时切出的，
//    全部内容**逐字节照搬**原文（含 XML 注释原文），只新增本文件头与
//    using / namespace / class 外壳，未改任何逻辑。
//
//  ★ 负责子域：随机卡池、池需求、数值 setter 构造
//    BuildCardPool / PoolDescribeKey / IsEmptyCard / SingleEntry / BuildPoolRequire /
//    BuildCardCoreInfoCheck / BuildRequireColors / BuildRequireRarities /
//    BuildNumberSetter / BuildNumberInfos。
//
//  ★ 不在本文件：枚举映射（RarityToGame 等）跨子域共用，留在主文件。
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
        /// 构造卡池（游戏 <c>Loot.GenRandomCardInfoPack</c>）—— <c>add_random_card</c> 与
        /// <c>improvise_card</c> **共用这一个构造器**。
        /// </summary>
        /// <remarks>
        /// 两种形态（校验器已保证互斥）：
        /// <list type="number">
        /// <item><b>单条目</b>：形状照原版实测（<c>ABitofEverything</c>）——
        ///   <c>pack{ name=Junk | p=1 pool=SpecialPool specialPool=Junk specific=[] }</c>，权重恒为 1。</item>
        /// <item><b>多条目加权（混池）</b>（2026-10-14 新增，<c>entries</c>）：每条一个整数权重，
        ///   对应原版 <c>UnstablePortal</c> 的 <c>p=65 / p=30 / p=5</c>。权重按比例落桶
        ///   （<c>ElementPoolWithProbability.DistributeProbabilityAmongInfos</c>：<c>max = Σp</c>，
        ///   <c>RandomRange(0, max, seed + i*10000)</c> 累计落桶）。</item>
        /// </list>
        /// <para>
        /// 三条取卡路径（<c>LootManager.GetCardPoolByInfo:486-517</c>）：
        /// <c>SpecificPool</c>（写死 CardSo 表）/ <c>SpecialPool</c>（按名取内容集合）/
        /// <c>CareerPool</c>（全部标准卡）。取完再按条目条件过滤。
        /// </para>
        /// <para>
        /// ★ <c>specific</c> 里的卡名解析不到时**记 Error 而不是塞 null**：
        /// 池数组里的 null 会一路活到抽卡逻辑（<c>AddRange</c> 不做判空），
        /// 变成难以归因的 NRE；这里是唯一能说清"哪张卡名写错了"的地方。
        /// </para>
        /// </remarks>
        /// <param name="semantic">
        /// 语义名（<c>add_random_card</c> / <c>improvise_card</c>），只用于错误文案 ——
        /// 让作者知道是哪条效果写错了。
        /// </param>
        private static GenRandomCardInfoPack BuildCardPool(ModKit.Api.CardPoolDef def, string ownerId,
            AssemblyResult result, string semantic)
        {
            var pack = new GenRandomCardInfoPack();

            if (def == null)
            {
                // 校验器已拦下；这里仍构造一个**结构合法**的对象（数组非 null），避免下游 NRE。
                pack.genRandomCardInfoWithProbability = SingleEntry(BuildPoolEntry(null, false, null));
                return pack;
            }

            // ---- 多条目加权（混池）----
            if (def.Entries != null && def.Entries.Count > 0)
            {
                var list = new List<ElementWithProbability<GenRandomCardInfo>>();
                foreach (var e in def.Entries)
                {
                    if (e == null) { continue; }

                    var entry = BuildPoolEntry(e.Special, e.Career == true, e.Require);
                    if (HasSpecific(e.Specific))
                    {
                        ReportSpecificUnsupported(entry, ownerId, result, semantic);
                    }

                    list.Add(new ElementWithProbability<GenRandomCardInfo>
                    {
                        // 校验器已保证 p 是 ≥0 的整数；这里 ?? 1 只是"漏网也不做出空池"的兜底。
                        probability = e.P ?? 1,
                        element = entry
                    });
                }

                pack.packName = PoolDescribeKey(def, ownerId);
                pack.genRandomCardInfoWithProbability = new ElementPoolWithProbability<GenRandomCardInfo>
                {
                    elementWithProbability = list.ToArray()
                };
                return pack;
            }

            // ---- 单条目 ----
            var single = BuildPoolEntry(def.Special, def.Career == true, def.Require);
            if (HasSpecific(def.Specific))
            {
                ReportSpecificUnsupported(single, ownerId, result, semantic);
            }

            pack.packName = PoolDescribeKey(def, ownerId);
            pack.genRandomCardInfoWithProbability = SingleEntry(single);
            return pack;
        }

        private static bool HasSpecific(List<string> specific)
        {
            return specific != null && specific.Count > 0;
        }

        /// <summary>
        /// 构造一个池条目（游戏 <c>Loot.GenRandomCardInfo</c>）：三条取卡路径三选一。
        /// </summary>
        /// <remarks>
        /// 单条目形态与多条目形态**共用这一个**，所以"路径怎么映射到游戏字段"只有一份实现。
        /// </remarks>
        private static GenRandomCardInfo BuildPoolEntry(string special, bool career, ModKit.Api.PoolRequireDef require)
        {
            var entry = new GenRandomCardInfo
            {
                cardPool = CardPool.CareerPool,
                cardRequirementCheckInfo = BuildPoolRequire(require)
            };

            if (!string.IsNullOrEmpty(special))
            {
                entry.cardPool = CardPool.SpecialPool;
                entry.specialPoolName = special;
            }
            else if (career)
            {
                // 显式 career: true ⇒ 默认值就是 CareerPool，这里只是把意图写明。
                entry.cardPool = CardPool.CareerPool;
            }
            // 既没给 special 也没给 career：保持初始化的 CareerPool（校验器会报"三选一"）。

            return entry;
        }

        /// <summary>
        /// <c>specific</c> 路径的处置：**报错并留空池**（不解析卡名）。
        /// </summary>
        /// <remarks>
        /// ★★ 该路径**已停用**（2026-10-13 实机崩在存档）：
        /// 它会把 CardSo **对象引用**放进 <c>EffectInfo.genRandomCardInfoPack.specificPool</c>，
        /// 而牌组的 CardInfo 与卡资产**共享同一个 EffectInfo 实例**（<c>CardInfo.CreateDeepCopy</c> 只浅拷数组），
        /// 于是本局结束存档时（<c>CampaignDefeated → SaveLoadManager.SaveCampaign →
        /// DataContractSerializer.WriteObject</c>）序列化器会去为 <c>Card.CardSo</c> 建 DataContract 并抛
        /// <c>InvalidDataContractException</c> —— 而 <c>SaveCampaign</c> 是 FileMode.Create 先截断再写，
        /// ⇒ **可能损坏战役存档**（主菜单反序列化失败还会删档）。
        /// 原版 <c>ABitofEverything</c> 之所以没事：它走 <c>specialPoolName="Junk"</c>，<c>specificPool</c> 是**空数组**
        /// （没有元素 ⇒ 序列化器不会去建 CardSo 的契约）。
        /// ⇒ 这里**故意不解析卡名**（解析就等于把 CardSo 塞回去），只报错并留空池：
        /// 空池抽不到卡时游戏用 404Card 兜底 —— 玩家看得见、日志有错、且**不碰存档图**。
        /// </remarks>
        private static void ReportSpecificUnsupported(GenRandomCardInfo entry, string ownerId,
            AssemblyResult result, string semantic)
        {
            result.Errors.Add(ownerId + "：" + semantic + " 的 pool.specific 暂不支持 —— 它会把 CardSo 引用"
                + "带进战役存档，导致本局结束时存档崩溃（InvalidDataContractException）。"
                + "请改用 pool.special（原版池名）或 pool.career（全部标准卡）+ pool.require 过滤。");
            entry.cardPool = CardPool.SpecificPool;
            entry.specificPool = new CardSo[0];
        }

        /// <summary>
        /// 卡池说明的本地化键（游戏侧就是 <c>packName</c>）。
        /// </summary>
        /// <remarks>
        /// ★ 实测硬契约（2026-10-13 实机踩中）：<c>Effects/EffectBase.cs:356</c> 用
        /// <c>new LocalizedString("CardRequirementDes", GenRandomCardInfoPack.packName)</c>
        /// 取卡面上「加 N 张 … 到你的手中」那句说明 —— <c>packName</c> **不是内部名，是本地化键**。
        /// 给一个表里没有的值 ⇒ 卡面直接显示 <c>No translation found for …</c>
        /// （实机症状：`No translation found for sample_starter.fx_add_random_specific.pool in CardRequirementDes`）。
        /// <para>
        /// ⇒ 取值顺序（多条目也只是"整包一份"，见 <c>GenRandomCardInfoPack.packName</c> 是 pack 级字段）：
        /// ① 作者写的 <c>describeKey</c>；
        /// ② 单条目 <c>special</c> ⇒ 用**原版池名**（原版「什么都来一点」正是 <c>packName = "Junk"</c>，
        ///    原版自带该文案）；
        /// ③ 多条目且**所有条目都是同一个 special 池名** ⇒ 同样复用该池名
        ///    （原版 <c>UnstablePortal</c> 就是这种：三条 <c>CardsFromOtherRealm</c> 只差稀有度）；
        /// ④ 其余（career / 混合池 / specific）⇒ 回退 <c>&lt;卡 id&gt;.pool</c>，
        ///    并由校验器在加载期拦下漏写 <c>describeKey</c> 的写法。
        /// </para>
        /// </remarks>
        private static string PoolDescribeKey(ModKit.Api.CardPoolDef def, string ownerId)
        {
            if (def != null && !string.IsNullOrEmpty(def.DescribeKey)) { return def.DescribeKey; }
            if (def != null && !string.IsNullOrEmpty(def.Special)) { return def.Special; }

            string uniform = UniformEntrySpecial(def);
            if (uniform != null) { return uniform; }

            return ownerId + ".pool";
        }

        /// <summary>
        /// 多条目池：若**所有条目都是同一个 special 池名**，返回那个名字；否则 null。
        /// </summary>
        /// <remarks>
        /// 这一条是原版 <c>UnstablePortal</c> 的形状：
        /// <c>pack{ name=CardsFromOtherRealm | p=65 … | p=30 … | p=5 … }</c>
        /// —— 三条路径同池、只差 <c>require.rarities</c>，所以 <c>packName</c> 用池名即可命中原版文案。
        /// </remarks>
        private static string UniformEntrySpecial(ModKit.Api.CardPoolDef def)
        {
            if (def == null || def.Entries == null || def.Entries.Count == 0) { return null; }

            string common = null;
            foreach (var e in def.Entries)
            {
                if (e == null || string.IsNullOrEmpty(e.Special)) { return null; }
                if (common == null) { common = e.Special; }
                else if (common != e.Special) { return null; }
            }
            return common;
        }

        /// <summary>
        /// 判断"没查到卡"—— 游戏 <c>GetCardSoByName</c> **查不到时返回 EmptyCard 而不是 null**。
        /// </summary>
        /// <remarks>
        /// <c>GameData/CardDataManager.cs:133-145</c>：字典里没有就
        /// <c>Debug.Log(cardName + " is not found in CardDataManager")</c> 然后返回 EmptyCard。
        /// 所以只判 <c>null</c> 会**静默把空白卡塞进卡池**（2026-10-13 实机症状：
        /// 打出「随机加卡(卡表)」得到一张「空白卡牌」；装配后 dump 打出
        /// <c>specific=[EmptyCard,EmptyCard]</c>）。
        /// <para>
        /// 用 <c>cardName == "EmptyCard"</c> 判定 —— 该对象由游戏按字典键 <c>"EmptyCard"</c> 注册。
        /// </para>
        /// </remarks>
        private static bool IsEmptyCard(CardSo so)
        {
            return so == null || string.IsNullOrEmpty(so.cardName) || so.cardName == "EmptyCard";
        }

        private static ElementPoolWithProbability<GenRandomCardInfo> SingleEntry(GenRandomCardInfo entry)
        {
            return new ElementPoolWithProbability<GenRandomCardInfo>
            {
                elementWithProbability = new[]
                {
                    new ElementWithProbability<GenRandomCardInfo> { probability = 1, element = entry }
                }
            };
        }

        /// <summary>
        /// 构造卡池条目的条件（游戏 <c>Card.CardRequirementCheckInfo</c>，struct）。
        /// </summary>
        /// <remarks>
        /// 与检索用的 <c>CardCoreInfoCheck</c> **不是同一个结构**：那个管"玩家手里的牌要满足什么"，
        /// 这个管"**从卡池里挑哪些卡**"。两者留空时的语义都是"不限制"。
        /// 未暴露 <c>specialRequirement</c> / <c>requirementDescribeKey</c>（只影响预览文案，不影响取卡）。
        /// </remarks>
        private static CardRequirementCheckInfo BuildPoolRequire(ModKit.Api.PoolRequireDef def)
        {
            // 构造函数唯一参数就是 acceptedCardRarity；其余字段在 struct 内自带初始化器（空数组/空串）
            var chk = new CardRequirementCheckInfo(new Rarity[0]);
            if (def == null) { return chk; }

            var rarities = BuildRequireRarities(def.Rarities);
            if (rarities.Length > 0) { chk.acceptedCardRarity = rarities; }

            var include = BuildRequireColors(def.Colors);
            if (include.Length > 0) { chk.includeColor = include; }
            chk.isOnlyHaveIncludedColor = def.OnlyColors == true;
            chk.isIncludeAllIncludedColor = def.AllColors == true;

            var exclude = BuildRequireColors(def.ExcludeColors);
            if (exclude.Length > 0) { chk.excludeColor = exclude; }

            chk.isSingleColor = def.SingleColor == true;
            chk.isMultiColor = def.MultiColor == true;

            if (!string.IsNullOrEmpty(def.Keyword))
            {
                chk.requireHasCardKeyword = true;
                chk.requiredKeyword = def.Keyword;
            }
            if (!string.IsNullOrEmpty(def.CannotBe)) { chk.CantBeCertainCardName = def.CannotBe; }
            return chk;
        }

        /// <summary>
        /// 构造检索条件（游戏 <c>Card.CardCoreInfoCheck</c>，struct）。
        /// </summary>
        /// <remarks>
        /// ★ 三条依据（原版 <c>DysonSphereProgram</c> 的实机 dump，2026-10-13）：
        ///   ① 原版只填了 <c>requireDescribeKey</c> + <c>requireHasCardColorE</c> + <c>requiredColorE</c>，
        ///      其余 8 个字段全默认；
        ///   ② struct 的默认值语义是「**不加任何限制**」⇒ 少填一个字段是放宽条件，不会报错；
        ///   ③ <c>requireDescribeKey</c> 是 <c>CardRequirementDes</c> 表的 key
        ///      （消费点 <c>Effects/EffectBase.cs:301</c> 的 tooltip），空串会显示
        ///      <c>No translation found for ''</c> ⇒ 由校验器强制必填。
        /// <para>
        /// <c>selectedTargetCheckType</c> 原版也是默认值（dump 显示 <c>NoRequire</c>），平台不碰。
        /// </para>
        /// </remarks>
        private static CardCoreInfoCheck BuildCardCoreInfoCheck(ModKit.Api.CardRequireDef def)
        {
            var chk = new CardCoreInfoCheck();   // struct：默认 = 不限制
            if (def == null) { return chk; }

            var required = BuildRequireColors(def.Colors);
            if (required.Length > 0)
            {
                chk.requireHasCardColorE = true;
                chk.requiredColorE = required;
            }

            var excluded = BuildRequireColors(def.ExcludeColors);
            if (excluded.Length > 0)
            {
                chk.requireNoCardColorE = true;
                chk.requiredNoColorE = excluded;
            }

            if (!string.IsNullOrEmpty(def.Keyword))
            {
                chk.requireHasCardKeyword = true;
                chk.requiredKeyword = def.Keyword;
            }

            var rarities = BuildRequireRarities(def.Rarities);
            if (rarities.Length > 0)
            {
                chk.requireRarity = true;
                chk.requiredRarities = rarities;
            }

            chk.requireDescribeKey = def.DescribeKey ?? "";
            return chk;
        }

        /// <summary>把作者写的颜色名（与 card 的 <c>colors</c> 同一套名字）转成游戏 <c>ColorE</c>。</summary>
        private static ColorE[] BuildRequireColors(List<string> names)
        {
            var list = new List<ColorE>();
            if (names != null)
            {
                foreach (var n in names)
                {
                    var lookup = ModKit.Core.ManaColorNames.Resolve(n);
                    if (lookup.Mapped) { list.Add(GameCapabilitiesProbe.ToGameColor(lookup.Value)); }
                }
            }
            return list.ToArray();
        }

        private static Rarity[] BuildRequireRarities(List<string> names)
        {
            var list = new List<Rarity>();
            if (names != null)
            {
                foreach (var n in names)
                {
                    if (string.IsNullOrEmpty(n)) { continue; }
                    list.Add(RarityToGame(n));
                }
            }
            return list.ToArray();
        }

        /// <summary>
        /// 构造数字改造配置（游戏 <c>RollNumber.SetterRNumberConfig</c>）。
        /// </summary>
        /// <remarks>
        /// ★ 为什么连"作者没写"也要构造一个：<c>EffectSetNumber</c> / <c>EffectNumberPlusSub</c>
        /// 的 <c>GetEffectDesKey()</c> **直接解引用** <c>SetterRNumberConfig</c>，
        /// 为 null 时卡面描述那一行就 NullReferenceException（2026-10-13 读源码确认）。
        /// <para>
        /// 作者省略 <c>setter</c> 时用**游戏自身的默认值**（照抄
        /// <c>RollNumber/SetterRNumberConfig.cs</c> 的字段默认）：
        /// <c>canBeFromCard=false</c>、<c>canBeDiceNumber=true</c>、
        /// <c>isChangePermanent=false</c>、<c>chooseNumberCount=1</c>、<c>extraInfo=""</c>。
        /// 所以"只写 amounts"的卡也能正常用；想控永久/临时、能否改造骰子再写 <c>setter</c>。
        /// </para>
        /// </remarks>
        private static SetterRNumberConfig BuildNumberSetter(ModKit.Api.NumberSetterDef def)
        {
            // 构造函数的唯一参数就是 canBeFromCard（游戏侧默认 false）
            var cfg = new SetterRNumberConfig(def != null && def.AllowFromCard == true);
            cfg.canBeDiceNumber = (def == null || def.AllowDice == null) ? true : def.AllowDice.Value;
            cfg.isChangePermanent = def != null && def.Permanent == true;
            cfg.chooseNumberCount = (def != null && def.ChooseCount.HasValue) ? def.ChooseCount.Value : 1;
            return cfg;
        }

        /// <summary>
        /// 构造数字槽。
        /// </summary>
        /// <remarks>
        /// <b>长度即 rNumbers 的长度</b>：<c>GenerateEffectFromEffectInfo</c> 只取
        /// <c>effectNumberInfos.Count</c> 当长度，真正的 RNumber 对象由调用方
        /// （<c>CardBase.AddEffectToCard</c>）按同一份列表预先生成。
        /// <para>
        /// 骰子数的硬约束（实测）：<c>isDice=true</c> 时 <c>diceInfos</c> 必填且长度 ≥1，
        /// 否则 <c>RNumber.SetRNumberByRNumberInfo</c> 直接 foreach null 抛 NRE。
        /// </para>
        /// </remarks>
        private List<RNumberInfo> BuildNumberInfos(ApiEffectSpec spec)
        {
            return BuildNumberInfos(spec == null ? null : spec.Amounts);
        }

        /// <summary>
        /// 把平台的数字槽列表装成游戏的 <see cref="RNumberInfo"/> 列表。
        /// </summary>
        /// <remarks>
        /// 拆出这个重载是为了让**升级**也能复用同一套数字映射 ——
        /// 升级里的 <c>effectNumbers[].amounts</c> 与效果里的 <c>amounts</c> 是同一种东西，
        /// 两处各写一份必然漂移。
        /// </remarks>
        private List<RNumberInfo> BuildNumberInfos(List<ModKit.Api.NumberSpec> amounts)
        {
            var list = new List<RNumberInfo>();
            if (amounts == null) { return list; }

            foreach (var amount in amounts)
            {
                if (amount == null) { continue; }

                if (amount.Dice != null)
                {
                    // 两种写法：
                    //   ① sides/count（2026-10-24 新增）→ 真骰子：1d8 / 3d4，直接映射 DiceType；
                    //   ② min/max → 只描述区间，游戏没有"区间骰"，于是退化成固定值（min==max）或单颗 D6。
                    var dice = amount.Dice;
                    int count = dice.Count.HasValue && dice.Count.Value > 0 ? dice.Count.Value : 1;
                    DiceType? type = dice.Sides.HasValue ? DiceTypeOf(dice.Sides.Value) : (DiceType?)null;

                    if (type.HasValue)
                    {
                        var infos = new DiceInfo[count];
                        for (int i = 0; i < count; i++) { infos[i] = new DiceInfo(type.Value); }
                        list.Add(new RNumberInfo(0, true, "Standard", infos));
                    }
                    else if (dice.Min == dice.Max)
                    {
                        list.Add(new RNumberInfo(dice.Min));
                    }
                    else
                    {
                        var infos = new DiceInfo[count];
                        for (int i = 0; i < count; i++) { infos[i] = new DiceInfo(DiceType.D6); }
                        list.Add(new RNumberInfo(0, true, "Standard", infos));
                    }
                }
                else
                {
                    list.Add(new RNumberInfo(amount.Value ?? 0));
                }
            }

            return list;
        }

        /// <summary>面数 → 游戏 <c>DiceType</c>（只认原版那 5 种）。</summary>
        private static DiceType? DiceTypeOf(int sides)
        {
            switch (sides)
            {
                case 4: return DiceType.D4;
                case 6: return DiceType.D6;
                case 8: return DiceType.D8;
                case 12: return DiceType.D12;
                case 20: return DiceType.D20;
                default: return null;
            }
        }

    }
}
