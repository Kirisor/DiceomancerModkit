// =============================================================================
//  ModKit.Api · 效果契约
//
//  作者唯一需要实现的东西就是这里。注意 ApplyAsync 返回 BCL 的 Task，
//  而不是游戏的 UniTask（Cysharp 第三方类型）—— Api 引用即违反规则 1。
//  平台在适配层完成 Task ↔ UniTask 的转换。
//
//  为什么是 Task 而不是 ValueTask：net472 的 BCL 里没有 ValueTask
//  （它属于 System.Threading.Tasks.Extensions 包）。让 mod 作者多引一个包
//  与"零门槛"目标冲突，代价也只是每个异步效果一次 Task 分配。
//  同步效果用 SyncCardEffect 基类，它复用同一个已完成的 Task，无额外分配。
//
//  绝大多数效果是同步的（游戏自己的 191 个 Special.CardEffect 实现里 98 个是同步体），
//  所以提供 SyncCardEffect 基类来消掉 async 样板。
// =============================================================================

using System;
using System.Threading.Tasks;

namespace ModKit.Api
{
    /// <summary>
    /// 官方（内置）效果语义的标识。作者在 JSON 里写 <c>{ "builtin": "deal_damage", ... }</c>
    /// 会映射到这里的一项。
    /// </summary>
    /// <remarks>
    /// </remarks>
    public enum BuiltinEffect
    {
        None = 0,

        DealDamage,
        Heal,
        Block,
        LoseLife,

        Draw,

        /// <summary>立即抽一张（游戏 <c>DrawOneCard</c>，对应 <c>CardManager.DrawOneCard()</c>）。</summary>
        /// <remarks>
        /// 与 <see cref="Draw"/> 的区别是**时机**：<c>Draw</c> 对应 <c>DrawCards(n)</c>，
        /// 它只是把 n 个抽牌子命令入队（返回 ≠ 牌已到手）；
        /// <c>DrawOne</c> 是真·立即抽。平台暴露两者是为了让作者按需选择，
        /// 但两者都标"未实机验证"。
        /// </remarks>
        DrawOne,

        DiscardRandom,

        GainMana,
        SetMaxMana,
        CleanMana,

        ApplyState,
        GainAbility,

        AddCard,
        AddTemporaryCard,
        UpgradeCard,

        AddRandomCard,
        ApplyPrimAbility,
        ApplyUtilAbility,
        ChangeMaxHealth,
        DiscardHand,
        DiscardOneRandomCard,
        NumberPlusSub,
        Recover,
        SetNumber,
        SeekCard,
        EndTurn,

        /// <summary>
        /// 探寻：从卡池抽若干**候选**卡给玩家挑，选中的进手牌
        /// （游戏 <c>Effects.EffectImproviseCard</c>，原版卡「不稳定传送门 / UnstablePortal」用的就是它）。
        /// </summary>
        /// <remarks>
        /// <para>
        /// ★ <b>数字 = 能挑几张</b>（游戏读 <c>rNumbers[0]</c>）。**候选张数不写在卡上**——
        /// 它由游戏规则值决定：<c>RuleManager.GetCurrentValueOfRule("ImproviseCard")</c>（默认 <b>3</b>）
        /// ＋策略 <c>ExtraCardsWhenImprovise</c> 的加成；可挑张数还会再加
        /// <c>ExtraPickCardsWhenImprovise</c> 策略的加成。所以 tooltip 里的「从 3 张卡牌中」
        /// 是游戏自己算的，作者写不出来、也不需要写。
        /// </para>
        /// <para>
        /// ★ <b>下列行为全部由游戏负责，平台不重复实现</b>（依据 2026-10-14 实机 dump ＋
        /// <c>Effects/EffectImproviseCard.cs</c> 源码）：
        /// <list type="number">
        /// <item>把本卡卡名写进候选池条件的 <c>CantBeCertainCardName</c> —— <b>自动排除自己</b>；</item>
        /// <item><c>LootManager.GenRandomCardByProbabilityPool(pack, 候选数, 世界种子)</c> 抽候选
        ///   （种子 = <c>WorldRandomSeedUtil.GetMapNodeUniqueSeed()</c>，同一地图节点可复现）；</item>
        /// <item>每张候选<b>自动加关键词 <c>Temporary</c></b>（临时）；</item>
        /// <item>弹选择界面（<c>SubCommandPickCardAndAction</c> ＋ <c>PickCardManager.PickCardConfigure</c>，
        ///   标题 <c>Title_Improvise</c>、按钮 <c>Key_Improvise</c>），<b>选中的卡进手牌</b>
        ///   （<c>CardManager.AddCardToPlayerCards(card, CardPlace.Hand)</c>）；</item>
        /// <item>卡面文案键 = <c>Key_ImproviseCard</c>（<b>游戏自带，作者不用写</b>）；
        ///   额外 tooltip 走 <c>Encyclopedia/ImproviseCard(+Description)</c>；</item>
        /// <item><c>GetCauseBusy() =&gt; true</c>（结算期间视为占用）。</item>
        /// </list>
        /// </para>
        /// <para>
        /// ★ <b>必须给 <see cref="EffectSpec.Pool"/></b>（与 <see cref="AddRandomCard"/> 同一个字段，
        /// 装配层复用同一个卡池构造器）：<c>GetCardInfoPreview()</c> 解引用
        /// <c>GenRandomCardInfoPack</c>，且 <c>EffectBase.AddRandomCardInfoDesStringToLocalizedString</c>
        /// （<c>EffectBase.cs:352-361</c>，判断里就有 <c>typeof(EffectImproviseCard)</c>）会读
        /// <c>GenRandomCardInfoPack.packName</c> ⇒ 不给会在**描述/预览**阶段就 NRE。
        /// </para>
        /// <para>
        /// ★ <c>ImproviseCard</c> **不在**游戏 <c>EffectUtils.EffectStrings</c>（22 项）里，
        /// 但那个数组不是"名称→类"的解析依据（真解析是 <c>Effect</c> ＋ 名字拼类名，
        /// <c>Effects.EffectImproviseCard</c> 确实存在）⇒ 平台直接写 <c>effectName="ImproviseCard"</c> 即可。
        /// </para>
        /// <para>
        /// 原版形状（<c>UnstablePortal</c> 实机 dump）：<c>costSlots=1</c>、两色 <c>Purple/Blue</c>、
        /// <c>rarity=Normal</c>、关键词 <c>Exert</c>，卡池为
        /// <c>pack{ name=CardsFromOtherRealm｜p=65 specialPool=CardsFromOtherRealm rar=[Normal]
        /// ｜p=30 rar=[Uncommon]｜p=5 rar=[Rare] }</c>（多条目加权，阶段 2 才由平台表达）。
        /// </para>
        /// </remarks>
        ImproviseCard
    }

    /// <summary>
    /// 内置语义的**实证档位表**。
    /// </summary>
    /// <remarks>
    /// 这张表是平台的"诚实清单"：只有 <see cref="EvidenceStatus.Verified"/> 的语义
    /// 才可以在作者手册里写成"可用"。
    /// <para>
    /// 依据：探针报告（`docs/ModKit-P0-内容契约与API规格.md` §4.6 与 §6.4）。
    /// </para>
    /// </remarks>
    public static class BuiltinEffectCatalog
    {
        /// <summary>某个内置语义当前处于哪个实证档位。</summary>
        public static EvidenceStatus StatusOf(BuiltinEffect effect)
        {
            switch (effect)
            {
                case BuiltinEffect.Block:
                case BuiltinEffect.Draw:
                    // 实机跑通：格挡（「木盾」5 格挡）与抽牌（「稳手」抽 1 张）
                    return EvidenceStatus.Verified;

                case BuiltinEffect.GainAbility:
                    // 实机跑通（2026-10-11）：「护盾强化」打出后游戏日志出现
                    // 「你获得了1层护盾提升」，且能力悬浮提示显示正确描述。
                    // 这是平台第一条端到端验证的"卡牌 → 施加能力"链路。
                    return EvidenceStatus.Verified;

                case BuiltinEffect.DealDamage:
                case BuiltinEffect.Heal:
                case BuiltinEffect.LoseLife:
                case BuiltinEffect.DrawOne:
                case BuiltinEffect.DiscardRandom:
                case BuiltinEffect.ApplyState:
                case BuiltinEffect.AddCard:
                case BuiltinEffect.AddTemporaryCard:
                case BuiltinEffect.UpgradeCard:
                case BuiltinEffect.EndTurn:
                case BuiltinEffect.ApplyPrimAbility:
                case BuiltinEffect.ApplyUtilAbility:
                case BuiltinEffect.ChangeMaxHealth:
                case BuiltinEffect.DiscardHand:
                case BuiltinEffect.DiscardOneRandomCard:
                case BuiltinEffect.Recover:
                    // 游戏代码可见、平台可封装，但尚未实机验证。
                    // ★ `Recover` 2026-10-13 实机确认渲染正确（「恢复3点负伤」），
                    //   行为待战斗内验证后再升 Verified。
                    return EvidenceStatus.ImplementedNotVerified;

                // ★★★ 以下三个**平台目前无法正确表达，不得对外承诺**（2026-10-13 实机证伪）：
                //
                //  AddRandomCard —— `EffectAddRandomCard` 依赖一个 `GenRandomCardInfoPack`
                //    （随机卡池资产）。平台没有这个字段 ⇒ 打出前 tooltip 渲染就崩：
                //      NullReferenceException in GenRandomCardInfoPack.IsSpecificCardPoolForPoolPreview
                //      ← CardUtils.GetCardInfoPreviewFromGenInfoPack ← EffectAddRandomCard.GetCardInfoPreview
                //    即使不崩，运行时 `genRandomCardInfoWithProbability` 也是 null。
                //    ⇒ 要支持它，得先有"卡池"字段；在那之前只能不承诺。
                //
                //  NumberPlusSub / SetNumber —— 两者都要 `SetterRNumberConfig`
                //    （isChangePermanent / canBeDiceNumber / canBeFromCard）。
                //    平台没有这个字段 ⇒ 全默认 false ⇒ 描述键退化成
                //    `Key_NumberPlus_Temp_NoDiceNoCard`（实机出现 "No translation found"），
                //    而且"选择改变哪个数字"的过滤条件会过严。
                //    ⇒ 要支持它们，得先有 rNumberConfig 字段。
                case BuiltinEffect.AddRandomCard:
                    // ★ 2026-10-13：平台已补 `pool`（卡池：specific 卡名表 / special 原版池名 / career 全池）
                    //   → 装配层构造 `GenRandomCardInfoPack`（单条目、权重 1，与原版实测形状一致）。
                    //   ✅ 2026-10-13 实机验证：`special`（原版 Junk 池，与原版「什么都来一点」同池）
                    //      与 `career`（全标准池 + `require.rarities` 过滤）两条路径都正常抽到卡并进手牌。
                    //   🚫 `specific`（写死卡表）**已主动停用**：它把 CardSo 对象引用放进 EffectInfo，
                    //      经"牌组与卡资产共享 EffectInfo"进战役存档 ⇒ InvalidDataContractException +
                    //      存档被 FileMode.Create 截断（2026-10-13 事故）。恢复它需先实现"运行时命名池"。
                    return EvidenceStatus.Verified;

                case BuiltinEffect.NumberPlusSub:
                case BuiltinEffect.SetNumber:
                    // ★ 2026-10-13：平台**已补** `setter` 字段
                    //   （`ModKit.Api.NumberSetterDef` → 游戏 `RollNumber.SetterRNumberConfig`，
                    //    5 个字段，省略时用游戏默认值：canBeFromCard=false / canBeDiceNumber=true /
                    //    isChangePermanent=false / chooseNumberCount=1）。
                    //   装配层现在**总是**构造它 ⇒ 描述键不会再退化成 `_NoDiceNoCard`，
                    //   也不会出现"GetEffectDesKey 解引用 null 而 NRE"。
                    return EvidenceStatus.ImplementedNotVerified;

                case BuiltinEffect.GainMana:
                    // 单色 Void 已实证；其余颜色待探测结果，故整体按"未验证"处理
                    return EvidenceStatus.ImplementedNotVerified;

                case BuiltinEffect.SetMaxMana:
                case BuiltinEffect.CleanMana:
                    // ★ CleanMana 2026-10-13 二次实机证伪后改走**特殊效果**通道：
                    //   游戏没有 Effects.EffectCleanMana，改由平台自建的
                    //   Effects.Special.CardEffect.EffectSpEffectCleanMana 落地 ——
                    //   ContentAssembler 写 effectName="SpecialEffect" + specialEffectString="CleanMana"。
                    //   档位仍是"已实现未验证"：新通道尚未实机跑过（C9）。
                    return EvidenceStatus.ImplementedNotVerified;

                case BuiltinEffect.SeekCard:
                    // ★ 2026-10-13：平台已补 `require`（筛选条件）+ `require.describeKey`（CardRequirementDes 表的 key）。
                    //   原版实测（DysonSphereProgram）：只用 requireDescribeKey + requireHasCardColorE + requiredColorE。
                    //   ⚠️ 游戏侧 struct 的默认值语义是"**不加任何限制**" ⇒ 漏写条件 = 静默变成"检索任意牌"，
                    //      所以校验器强制 require 与 describeKey 必填。
                    //   ✅ 2026-10-13 实机验证：样例卡 fx_seek_blue（colors:[Blue] + 自定义 describeKey，
                    //      文案走 mod i18n 的 CardRequirementDes）与 fx_seek_green_vanilla（复用原版 key
                    //      GreenCard）**均已生效**；装配后读回自检 dump 打出平台实际写入的
                    //      `check{ describe=… hasColor=True[Blue|Green] … targetCheck=NoRequire }`，与意图逐字段一致。
                    return EvidenceStatus.Verified;

                case BuiltinEffect.ImproviseCard:
                    // ★ 2026-10-14 新增：`Effects.EffectImproviseCard` 是游戏里**真实存在**的类
                    //   （原版卡 UnstablePortal 在用），平台直接写 effectName="ImproviseCard"，
                    //   卡池复用 add_random_card 的构造器（spec.Pool → GenRandomCardInfoPack）。
                    //   ⇒ 不需要像 clean_mana / end_turn 那样走"平台自建特殊效果"通道。
                    //   ⚠️ 它**不在** EffectUtils.EffectStrings（22 项）里 —— 但那不是
                    //   "名称→类"的解析依据，别被它误导（§1.2.1）。
                    return EvidenceStatus.ImplementedNotVerified;

                default:
                    return EvidenceStatus.Unsupported;
            }
        }
    }

    /// <summary>实证档位。判定口径 = 认知纪律 C9：未做 ≠ 已验证。</summary>
    public enum EvidenceStatus
    {
        /// <summary>🚫 探针未通过 / 机制不存在 —— 不得对外承诺。</summary>
        Unsupported = 0,

        /// <summary>⚠️ 平台已实现、未实机验证 —— 可发布，但文档必须标注。</summary>
        ImplementedNotVerified = 1,

        /// <summary>✅ 本工作区实机验证通过 —— 可对外承诺。</summary>
        Verified = 2
    }

    /// <summary>
    /// 效果执行契约。这是 mod 作者唯一需要实现的东西。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 两条硬约束（来自探针，违反会静默失效或崩溃）：
    /// <list type="number">
    /// <item>实现类必须有 <b>public 无参构造函数</b>——游戏用 <c>Activator.CreateInstance</c> 实例化；</item>
    /// <item>不要在其中 <c>await</c> 游戏帧预算相关的东西；平台已在适配层处理时序。</item>
    /// </list>
    /// </para>
    /// </remarks>
    public interface ICardEffect
    {
        Task ApplyAsync(EffectContext ctx);
    }

    /// <summary>
    /// 同步效果的便捷基类——消掉 async 样板。
    /// </summary>
    public abstract class SyncCardEffect : ICardEffect
    {
        /// <summary>实现效果体。这里可以自由抛异常，平台会隔离并记 Error。</summary>
        public abstract void Apply(EffectContext ctx);

        /// <inheritdoc />
        public Task ApplyAsync(EffectContext ctx)
        {
            Apply(ctx);
            return CompletedTask;
        }

        /// <summary>
        /// 复用的已完成 Task。
        /// </summary>
        /// <remarks>
        /// net472 的 BCL **没有** <c>Task.CompletedTask</c>（它是 .NET 4.6+；
        /// 而 <c>ValueTask</c> 更不在 BCL 里，属 System.Threading.Tasks.Extensions 包）。
        /// 这里缓存一个单例，避免每个同步效果都分配一个 Task。
        /// </remarks>
        private static readonly Task CompletedTask = Task.FromResult(true);
    }

    /// <summary>
    /// 把效果类登记到平台。作者用 <c>[ModEffect("my_sacrifice")]</c> 标注实现类，
    /// JSON 里写 <c>{ "custom": "my_sacrifice" }</c> 引用它。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 平台在加载 mod 程序集时按此特性建索引，并在加载期校验 JSON 里的引用是否存在——
    /// 把游戏那种"命名写错静默不生效"变成加载期报错。
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ModEffectAttribute : Attribute
    {
        public ModEffectAttribute(string id)
        {
            Id = id;
        }

        /// <summary>效果标识，必须匹配 <c>^[a-z][a-z0-9_]{2,40}$</c>。</summary>
        public string Id { get; private set; }
    }
}
