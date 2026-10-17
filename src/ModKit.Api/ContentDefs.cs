// =============================================================================
//  ModKit.Api · 内容定义（JSON 反序列化目标）
//
//  这些类型**只用于承载数据**，不含任何逻辑。mod 作者永远看不到它们——
//  它们由平台在加载期从 JSON 反序列化出来，走 §3.4 的 10 条加载期校验，
//  然后翻译成游戏内容。
//
//  为什么放在 Api 而不是 Runtime：作者写 C# 效果时可能需要读"这张卡的定义"
//  （例如实现一张按自身费用缩放的卡）。把它放 Api 让两侧共用同一份契约，
//  避免"Runtime 的定义"与"作者以为的定义"漂移。
//
//  字段名与 JSON 的对应关系由平台的序列化设置决定（见 Runtime），
//  此处一律用 PascalCase，平台按 snake_case 解析。
// =============================================================================

using System.Collections.Generic;

namespace ModKit.Api
{
    /// <summary>数字槽：可以是固定整数，也可以是骰子表达式。</summary>
    public sealed class NumberSpec
    {
        /// <summary>固定值。当 <see cref="Dice"/> 为 null 时生效。</summary>
        public int? Value { get; set; }

        /// <summary>骰子表达式。非 null 时 <see cref="Value"/> 被忽略。</summary>
        public DiceSpec Dice { get; set; }
    }

    /// <summary>骰子表达式（映射游戏的 <c>RNumberInfo(isDice: true)</c>）。</summary>
    /// <remarks>
    /// <para>
    /// 注意：游戏 <c>RNumberInfo</c> 的 <c>state</c> 构造参数被完全丢弃、全库无消费点，
    /// 因此平台**不暴露 state**——暴露它只会把作者引向一个无效字段。
    /// </para>
    /// </remarks>
    public sealed class DiceSpec
    {
        public int Min { get; set; } = 1;
        public int Max { get; set; } = 6;
    }

    /// <summary>一条效果声明：要么是内置语义，要么指向自定义效果类。</summary>
    public sealed class EffectSpec
    {
        /// <summary>内置语义名（snake_case，如 <c>"deal_damage"</c>）。与 <see cref="Custom"/> 二选一。</summary>
        public string Builtin { get; set; }

        /// <summary>自定义效果 id（对应 <see cref="ModEffectAttribute.Id"/>）。与 <see cref="Builtin"/> 二选一。</summary>
        public string Custom { get; set; }

        /// <summary>数字槽。可以是一个数字，也可以是数组（对应游戏的多槽效果）。</summary>
        public List<NumberSpec> Amounts { get; set; }

        /// <summary>目标选择器名（<c>Self</c>/<c>Opponent</c>/<c>AllOpponents</c>/<c>All</c>/<c>RandomOpponent</c>）。</summary>
        public string Target { get; set; }

        /// <summary>伤害模式名（<c>Direct</c>/<c>Indirect</c>/<c>LoseLife</c>），仅伤害类语义需要。</summary>
        public string Mode { get; set; }

        /// <summary>法力颜色名，仅法力类语义需要。</summary>
        public string Color { get; set; }

        /// <summary>状态名 / 能力名 / 卡牌 id，视语义而定。</summary>
        public string Name { get; set; }

        /// <summary>
        /// 数字改造类效果（<c>set_number</c> / <c>number_plus_sub</c>）的配置对象。
        /// </summary>
        /// <remarks>
        /// ★ 为什么这两个语义**必须**有它：游戏的 <c>EffectSetNumber.GetEffectDesKey()</c> 与
        /// <c>EffectNumberPlusSub.GetEffectDesKey()</c> 会**直接解引用** <c>SetterRNumberConfig</c>
        /// （读 <c>canBeDiceNumber</c> / <c>isChangePermanent</c>）——
        /// 平台不写它，**卡面描述那一行就 NullReferenceException**（还没打出就崩；2026-10-13 读源码确认）。
        /// <para>
        /// 省略本字段没问题：平台会用**游戏自身的默认值**（见 <see cref="NumberSetterDef"/> 的说明），
        /// 所以只写 <c>amounts</c> 也能用；要控"永久/临时""能不能改造骰子"再写它。
        /// </para>
        /// </remarks>
        public NumberSetterDef Setter { get; set; }

        /// <summary>
        /// 检索类效果（<c>seek_card</c>）的**筛选条件**。
        /// </summary>
        /// <remarks>
        /// ★ 为什么必须显式给：游戏侧 <c>CardCoreInfoCheck</c> 是 struct，**默认值 = 不加任何限制**。
        /// 漏写不会报错，而是让这张卡静默变成"检索任意牌"——玩法完全不同。校验器因此强制必填。
        /// <para>
        /// 原版实测（<c>DysonSphereProgram</c>）：一张卡挂两个 <c>SeekCard</c>，条件分别是
        /// <c>{describeKey=BlueCard, 必含 Blue}</c> 与 <c>{describeKey=GreenCard, 必含 Green}</c>。
        /// </para>
        /// </remarks>
        public CardRequireDef Require { get; set; }

        /// <summary>
        /// 随机类效果（<c>add_random_card</c> / <c>improvise_card</c>）的**卡池**声明。
        /// </summary>
        /// <remarks>
        /// ★ 必须给：游戏侧 <c>EffectAddRandomCard</c> 读
        /// <c>GenRandomCardInfoPack.genRandomCardInfoWithProbability</c>，
        /// 而 <c>GetCardInfoPreview</c> 也解引用同一个对象 ⇒ 不给它会在**描述/预览**阶段就 NRE。
        /// <para>
        /// ★ 两个语义**共用这一个字段**（装配层共用同一个卡池构造器），差别只在"谁来挑"：
        /// <list type="bullet">
        /// <item><c>add_random_card</c>：<c>amounts[0]</c> = <b>加几张</b>，抽到的卡直接进手牌；</item>
        /// <item><c>improvise_card</c>（探寻）：<c>amounts[0]</c> = <b>能挑几张</b>；
        ///   **候选张数由游戏规则值决定**（<c>RuleManager.GetCurrentValueOfRule("ImproviseCard")</c>，
        ///   默认 <b>3</b>）—— 卡上不写，也写不出来。候选自动带 <c>Temporary</c>（临时）关键词、
        ///   自动排除本卡自己，玩家从候选里选中若干张进手牌。</item>
        /// </list>
        /// </para>
        /// </remarks>
        public CardPoolDef Pool { get; set; }
    }

    /// <summary>
    /// 卡池（映射游戏 <c>Loot.GenRandomCardInfoPack</c>）。
    /// </summary>
    /// <remarks>
    /// 游戏侧取卡有**三条路径**（<c>Loot/LootManager.cs:486-517</c> 的 <c>GetCardPoolByInfo</c>）：
    /// <c>SpecificPool</c>（写死的 CardSo 表）/ <c>SpecialPool</c>（按名字取游戏内容里的集合）/
    /// <c>CareerPool</c>（全部标准卡）。取完再统一按条目条件
    /// （<c>CardRequirementCheckInfo</c>，见 <see cref="PoolRequireDef"/>）过滤。
    /// <para>
    /// 两种形态，**互斥**：
    /// <list type="number">
    /// <item><b>单条目</b>（本类上的 <see cref="Specific"/> / <see cref="Special"/> / <see cref="Career"/>）：
    ///   平台构造**单条目、权重 1**（与原版 <c>ABitofEverything</c> 的实测形状一致：
    ///   <c>pack{ name=Junk | p=1 pool=SpecialPool specialPool=Junk specific=[] }</c>）。</item>
    /// <item><b>多条目加权（混池）</b>（<see cref="Entries"/>，2026-10-14 新增）：每条一个权重 + 一条取卡路径，
    ///   对应原版 <c>UnstablePortal</c> 那样的 <c>p=65 / p=30 / p=5</c> 三桶。</item>
    /// </list>
    /// </para>
    /// </remarks>
    public sealed class CardPoolDef
    {
        /// <summary>写死的卡名表（→ <c>CardPool.SpecificPool</c>）。**三选一**。</summary>
        public List<string> Specific { get; set; }

        /// <summary>原版特殊池名（→ <c>CardPool.SpecialPool</c> + <c>specialPoolName</c>），如 <c>"Junk"</c>。**三选一**。</summary>
        public string Special { get; set; }

        /// <summary>全部标准卡池（→ <c>CardPool.CareerPool</c>）。**三选一**。</summary>
        public bool? Career { get; set; }

        /// <summary>池条目自己的筛选条件（→ <c>CardRequirementCheckInfo</c>）。单条目形态用。</summary>
        public PoolRequireDef Require { get; set; }

        /// <summary>
        /// **卡池说明文本的本地化键** —— 它就是游戏里的 <c>packName</c>。
        /// </summary>
        /// <remarks>
        /// ★ 实测硬契约（2026-10-13 实机踩中）：游戏在
        /// <c>Effects/EffectBase.cs:356</c> 用
        /// <c>new LocalizedString("CardRequirementDes", GenRandomCardInfoPack.packName)</c>
        /// 取**玩家在卡面看到的那句"加 N 张 &lt;这里&gt; 到你的手中"**。
        /// 也就是说 <c>packName</c> 不是内部名，而是 <c>CardRequirementDes</c> 表里的 key；
        /// 给一个表里没有的值 ⇒ 卡面直接显示 <c>No translation found for …</c>。
        /// <para>
        /// ★ 它是**整包一份**的（<c>GenRandomCardInfoPack.packName</c> 是 pack 级字段，
        /// 条目 <c>GenRandomCardInfo</c> **没有**自己的名字）—— 所以多条目时也只有一个说明文本。
        /// </para>
        /// <para>
        /// 默认值策略：走 <see cref="Special"/> 时自动用**原版池名**（如 <c>"Junk"</c> —— 原版就有文案，
        /// 这也是原版「什么都来一点」的写法）；多条目时**所有条目都是同一个 <c>special</c> 池名**
        /// 才能这样默认（原版 <c>UnstablePortal</c> 正是这种：三条 <c>CardsFromOtherRealm</c> 只差稀有度），
        /// 否则走 <see cref="Specific"/> / <see cref="Career"/> / 混合池时
        /// **必须在 mod 的 i18n 里自己加一条 <c>CardRequirementDes</c>**，校验器会对缺失报错。
        /// </para>
        /// </remarks>
        public string DescribeKey { get; set; }

        /// <summary>
        /// **多条目加权（混池）**：每条一个权重 + 一条取卡路径 + 可选筛选条件。
        /// </summary>
        /// <remarks>
        /// ★ 与单条目字段（<see cref="Specific"/> / <see cref="Special"/> / <see cref="Career"/>）
        /// **互斥** —— 两者都写时校验器报错（避免"以为混了、实际只走了一条"）。
        /// <para>
        /// 游戏侧语义（<c>Loot/ElementPoolWithProbability.DistributeProbabilityAmongInfos</c>：
        /// <c>max = Σp</c>，每次抽卡 <c>RandomRange(0, max, seed + i*10000)</c> 落桶，
        /// 再 <c>GetCardPoolByInfo</c> 取该桶的池、洗牌后取 <c>countForActuallyGenerate</c> 张）：
        /// 权重是**整数、按比例**，绝对值不重要（65/30/5 与 13/6/1 等价）。
        /// <b>权重全为 0 ⇒ 一张都抽不出来</b>（<c>max=0</c>，没有桶能落进去），校验器会拦。
        /// </para>
        /// <para>
        /// 池子不够时游戏用 <c>404Card</c> 补位（<c>LootManager.cs:475-482</c>）——
        /// 所以把某条权重调得远大于它那桶的卡数，会看到"404 卡"。
        /// </para>
        /// </remarks>
        public List<PoolEntryDef> Entries { get; set; }
    }

    /// <summary>
    /// 多条目加权卡池里的一条（映射游戏 <c>Loot.ElementWithProbability&lt;GenRandomCardInfo&gt;</c>）。
    /// </summary>
    /// <remarks>
    /// 取卡路径与 <see cref="CardPoolDef"/> 的单条目形态**同一套三选一**
    /// （<c>specific</c> / <c>special</c> / <c>career</c>），筛选条件也用同一个
    /// <see cref="PoolRequireDef"/> —— 装配层复用同一个构造器，不另写一份。
    /// <para>
    /// ★ 这里**故意没有** <c>describeKey</c>：游戏只有 pack 级的 <c>packName</c>，
    /// 条目自己的说明键不存在，暴露它只会让作者以为写了有用（要改说明请用
    /// <see cref="CardPoolDef.DescribeKey"/>）。
    /// </para>
    /// </remarks>
    public sealed class PoolEntryDef
    {
        /// <summary>
        /// 权重（→ <c>ElementWithProbability.probability</c>，游戏侧是 <c>int</c>）。
        /// </summary>
        /// <remarks>
        /// ★ **必填、不给默认值**：权重是"相对比例"，给个默认 1 会让作者以为在等分，
        /// 而实际比例取决于其他条目写了几 —— 这类"以为写对了"的错误平台一律转成加载期报错。
        /// </remarks>
        public int? P { get; set; }

        /// <summary>写死的卡名表。**三选一**（与单条目形态一致，**已停用**：会写坏存档）。</summary>
        public List<string> Specific { get; set; }

        /// <summary>原版特殊池名。**三选一**。</summary>
        public string Special { get; set; }

        /// <summary>全部标准卡池。**三选一**。</summary>
        public bool? Career { get; set; }

        /// <summary>本条的筛选条件（→ <c>CardRequirementCheckInfo</c>）。</summary>
        public PoolRequireDef Require { get; set; }
    }

    /// <summary>
    /// 卡池条目的筛选条件（映射游戏 <c>Card.CardRequirementCheckInfo</c>，12 字段）。
    /// </summary>
    /// <remarks>
    /// 与检索用的 <see cref="CardRequireDef"/> **不是同一个结构**：
    /// 那个映射 <c>CardCoreInfoCheck</c>（玩家手里的牌要满足什么），
    /// 这个映射 <c>CardRequirementCheckInfo</c>（**从卡池里挑哪些卡**）。
    /// 两者都留空时的语义都是"不限制"。
    /// <para>
    /// 未暴露的两个字段（<c>specialRequirement</c> / <c>requirementDescribeKey</c>）只影响
    /// **预览里的条件文案**，不影响取卡结果 —— 已知限制，等实机需要时再补。
    /// </para>
    /// </remarks>
    public sealed class PoolRequireDef
    {
        /// <summary>稀有度白名单（→ <c>acceptedCardRarity</c>）。</summary>
        public List<string> Rarities { get; set; }

        /// <summary>卡必须**含**这些色（→ <c>includeColor</c>）。</summary>
        public List<string> Colors { get; set; }

        /// <summary>只允许 <see cref="Colors"/> 里的颜色（→ <c>isOnlyHaveIncludedColor</c>）。</summary>
        public bool? OnlyColors { get; set; }

        /// <summary>必须**含全** <see cref="Colors"/>（→ <c>isIncludeAllIncludedColor</c>）。</summary>
        public bool? AllColors { get; set; }

        /// <summary>卡必须**不含**这些色（→ <c>excludeColor</c>）。</summary>
        public List<string> ExcludeColors { get; set; }

        /// <summary>只要单色卡（→ <c>isSingleColor</c>）。</summary>
        public bool? SingleColor { get; set; }

        /// <summary>只要多色卡（→ <c>isMultiColor</c>）。</summary>
        public bool? MultiColor { get; set; }

        /// <summary>卡必须带这个关键词（→ <c>requireHasCardKeyword</c> + <c>requiredKeyword</c>）。</summary>
        public string Keyword { get; set; }

        /// <summary>排除某张卡（→ <c>CantBeCertainCardName</c>）。</summary>
        public string CannotBe { get; set; }
    }

    /// <summary>
    /// 检索条件（映射游戏 <c>Card.CardCoreInfoCheck</c>）。
    /// </summary>
    /// <remarks>
    /// 只暴露原版**实际用到**的那几个字段；游戏 struct 的其余字段保持默认（= 不限制），
    /// 其中 <c>selectedTargetCheckType</c> 原版也是默认值（dump 显示 <c>NoRequire</c>），平台不碰。
    /// </remarks>
    public sealed class CardRequireDef
    {
        /// <summary>牌必须**含**这些流派色（→ <c>requireHasCardColorE</c> + <c>requiredColorE</c>）。</summary>
        public List<string> Colors { get; set; }

        /// <summary>牌必须**不含**这些流派色（→ <c>requireNoCardColorE</c> + <c>requiredNoColorE</c>）。</summary>
        public List<string> ExcludeColors { get; set; }

        /// <summary>稀有度白名单（→ <c>requireRarity</c> + <c>requiredRarities</c>）。</summary>
        public List<string> Rarities { get; set; }

        /// <summary>牌必须带这个关键词（→ <c>requireHasCardKeyword</c> + <c>requiredKeyword</c>）。</summary>
        public string Keyword { get; set; }

        /// <summary>
        /// 条件文本的本地化键 —— **必填**。
        /// </summary>
        /// <remarks>
        /// ★ 它**不是文本本身**，而是 <c>CardRequirementDes</c> 表里的 key：
        /// 游戏在 <c>EffectBase.cs:301</c> 用 <c>new LocalizedString("CardRequirementDes", requireDescribeKey)</c>
        /// 取玩家看到的条件说明。填原版 key（如 <c>"BlueCard"</c>）可直接复用原版文案；
        /// 填自己的 key 就要在 mod 的 i18n 里加一张 <c>CardRequirementDes</c> 表。
        /// 留空会让 tooltip 显示 <c>No translation found for ''</c> ⇒ 校验器报错。
        /// </remarks>
        public string DescribeKey { get; set; }
    }

    /// <summary>
    /// 数字改造配置（映射游戏 <c>RollNumber.SetterRNumberConfig</c>，共 5 个字段）。
    /// </summary>
    /// <remarks>
    /// 每个字段都可省略（<c>null</c> = 用游戏默认值）。游戏侧字段默认值：
    /// <c>canBeFromCard = false</c>、<c>canBeDiceNumber = true</c>、
    /// <c>isChangePermanent = false</c>、<c>chooseNumberCount = 1</c>、<c>extraInfo = ""</c>。
    /// </remarks>
    public sealed class NumberSetterDef
    {
        /// <summary>改的是**永久值**（true）还是本回合临时值（false）。省略 = false（临时）。</summary>
        public bool? Permanent { get; set; }

        /// <summary>允许把**骰子**当改造对象。省略 = true（游戏默认）。</summary>
        public bool? AllowDice { get; set; }

        /// <summary>允许改造**来自卡牌**的数字。省略 = false（游戏默认）。</summary>
        public bool? AllowFromCard { get; set; }

        /// <summary>选择几个数字来改造（游戏 <c>chooseNumberCount</c>）。省略 = 1。</summary>
        public int? ChooseCount { get; set; }
    }

    /// <summary>
    /// 一个费用槽：付这一格时可以用的颜色集合。
    /// </summary>
    /// <remarks>
    /// <b>1 个颜色</b> = 普通单色槽；<b>多个颜色</b> = 混合槽，语义是"其中任意一个都能付"。
    /// <para>
    /// 游戏侧的对应物是 <c>Card.ColorEArray</c>（本身就是 <c>ColorE[]</c>）；
    /// 卡面图标按**完整的有序颜色序列**查 <c>ManaManager.ManaIconDic</c>，
    /// 所以混合槽的合法组合是**预制在资产里的有限集合**，不是任意排列都行。
    /// 平台启动时枚举该字典的全部键，得到本 build 真实可用的组合。
    /// </para>
    /// </remarks>
    public sealed class CostSlotDef
    {
        /// <summary>本槽接受的颜色名。**有序** —— 图标按有序序列查表。</summary>
        public List<string> Colors { get; set; }
    }

    /// <summary>
    /// 卡牌上挂的一个关键词。
    /// </summary>
    /// <remarks>
    /// JSON 里两种写法，**可以混用**：
    /// <list type="bullet">
    /// <item><c>"Exert"</c> —— 只要关键词本身（关键词自己会做事）；</item>
    /// <item><c>{ "name": "Miracle", "effects": [ … ] }</c> —— <b>关键词自带效果</b>。</item>
    /// </list>
    /// <para>
    /// ★ 为什么有的关键词**必须**带效果：关键词自己决定那份效果**什么时候结算**。
    /// 最典型的是「奇迹」（<c>Miracle</c>）—— 它的语义是
    /// "如果这张牌不是在<b>回合开始时</b>抽到的，则打出时触发<b>它的效果</b>"，
    /// 那个"它的效果"指的就是**挂在关键词上的这一份**
    /// （实现见 <c>Keyword.CardKeywordMiracle.CardResolvePhaseAfterEffect</c>：
    /// 它遍历 <c>EffectBases</c> 逐个结算 —— 那是关键词自己的效果数组，不是卡牌的）。
    /// </para>
    /// <para>
    /// 只写关键词名而不给效果，奇迹就会表现得像"没生效"
    /// （卡面 tooltip 会显示"[奇迹]当前未激活"，因为可结算的效果是空的）。
    /// </para>
    /// </remarks>
    public sealed class KeywordRefDef
    {
        /// <summary>关键词名（合法值见游戏的 <c>CardKeyWordUtil.AllKeywordStrings</c>，共 36 个）。</summary>
        public string Name { get; set; }

        /// <summary>挂在关键词上的效果（可空；不是所有关键词都需要）。</summary>
        public List<EffectSpec> Effects { get; set; }

        /// <summary>
        /// 关键词自带效果的**结算时机**（<c>OnCast</c> / <c>OnDraw</c> / <c>OnDiscard</c> / <c>EndInHand</c>）。
        /// </summary>
        /// <remarks>
        /// ★ 缺省是 <c>OnCast</c>（**打出时**）—— 这才是关键词效果的正常语义：
        /// 关键词挂在卡上，由关键词自己在卡牌结算阶段决定何时触发它那份效果
        /// （见 <c>CardKeywordMiracle.CardResolvePhaseAfterEffect</c>）。
        /// <para>
        /// ★ <b>绝不要让它继承"卡牌自己"的 <c>resolve</c>。</b>
        /// 典型例子：「稳手」整张卡是"回合结束时若留在手中"（<c>EndInHand</c>），
        /// 但它的奇迹效果是"**打出时**触发"—— 两者时机本来就不一样，
        /// 混成一个就会让奇迹不按预期触发。
        /// </para>
        /// </remarks>
        public string Resolve { get; set; }

        /// <summary>
        /// <summary>
        /// **变身 / 恢复的目标卡**（本 mod 内的另一张卡短 id）。
        /// </summary>
        /// <remarks>
        /// ★ 对应游戏的 <c>CardKeyWordInfo.keywordCardSoName</c>。实测原版：
        /// <c>CrossBow</c> 的 <c>Loaded</c> → <c>CrossBowReload</c>；
        /// <c>CrossBowReload</c> 的 <c>Reload</c> → <c>CrossBow</c>；
        /// <c>MetalParts</c> 的 <c>Construct</c> → <c>GatlingGun</c>；
        /// <c>GatlingGun</c> 的 <c>Constructed</c> → <c>MetalParts</c>。
        /// <para>
        /// ★★ <b>平台只负责填这个目标，绝不实现"什么时候切换"。</b>
        /// 切换时机（打出即变 / 攒够需求才变 / 战斗结束后恢复）全在游戏的
        /// <c>CardKeyword</c> 子类里 —— 平台多写一层就会与原版行为冲突。
        /// </para>
        /// <para>写 mod 内的短 id 即可，平台自动补 mod 前缀。</para>
        /// </remarks>
        public string ToCard { get; set; }

        /// <summary>
        /// **费用颜色串**：缩写字母连写，如 <c>"RR"</c>（红红）、<c>"RB"</c>（红蓝）、<c>"V"</c>（虚）。
        /// </summary>
        /// <remarks>
        /// ★ 对应游戏的 <c>CardKeyWordInfo.manaString</c>。实测原版：
        /// <c>StupidFist</c>（没脑子拳）的 <c>Storm</c> 是 <c>"RR"</c>
        /// —— 每 2 点红法力触发一次重复结算；<c>DualSMG</c> 的 <c>Storm</c> 是 <c>"RB"</c>。
        /// 游戏用 <c>DitwUtil.GetManaColorEFromAbbrString</c> 解析。
        /// <para>字母：R=红 / G=绿 / B=蓝 / P=紫 / V=虚 / K=黑（后两个待实测确认）。</para>
        /// </remarks>
        public string Mana { get; set; }

        /// <summary>
        /// **召唤物的名字**（只有 <c>Summon</c> 关键词用得上）：写游戏**原版**召唤物的名字，
        /// 如 <c>FireElement</c>（火元素）、<c>WaterElement</c>（水元素）。
        /// </summary>
        /// <remarks>
        /// ★ <b>为什么必须要有这个字段</b>：游戏的 <c>CardKeywordSummon.GetSummonInfo()</c> 写的是
        /// <c>SummonName = OwnerCard.CardName</c> —— 召唤物的名字**硬绑在"打出这张牌"的卡名上**，
        /// 而立绘（<c>AssetRefSo.SpriteDict</c>）与显示名（<c>CardName</c> 本地化表）**又都拿这个名字去查**。
        /// <para>
        /// 原版之所以天衣无缝，是因为那 9 张 <c>Summon</c> 卡的**卡名本身就是召唤物名**；
        /// 而 mod 卡的卡名带 mod 前缀，两张表都查不到 —— 症状是游戏打一行
        /// <c>Sprite not found: &lt;前缀&gt;.&lt;卡名&gt;</c>（**不致命**，召唤物照样出现，只是没有立绘）。
        /// </para>
        /// <para><b>可用值</b>（= 那 9 张原版 Summon 卡的卡名）：
        /// <c>ChaosElement / DarkElement / EarthElement / FireElement / FusedElement /
        /// PureElement / SpaceFireElement / SteamTank / WaterElement</c>。</para>
        /// <para>★ <b>留空 = 保持原版行为</b>（召唤物名字就是本卡卡名，即"打出这张牌变成召唤物"）。</para>
        /// </remarks>
        public string Summon { get; set; }

        /// 关键词自带的**数值**（只有一部分关键词用得上）。
        /// </summary>
        /// <remarks>
        /// 需要与否看游戏 <c>CardKeyWordUtil.GetHasNumberFromName</c>（private，实测名单）：
        /// <c>Fracture / Loaded / Reload / Check / Item / Vengeance / Scorching / Improvise /
        /// Kick / Cycle / Charge / Growth / Decay / Recover / Rainbow / Surge / GatherQi /
        /// Summon / Evolve / Coordinated / Construct</c>。
        /// 这类关键词在卡面上会渲染成「[关键词 N]」，那个 N 就来自这里；
        /// 其余关键词写了也会被游戏忽略（它只为需要的那些建 <c>RNumber</c>）。
        /// </remarks>
        public int? Number { get; set; }
    }

    /// <summary>
    /// 卡牌升级（"+"）的声明。对应游戏的 <c>Card.CardUpgradeInfo</c>。
    /// </summary>
    /// <remarks>
    /// ★ 游戏判定「这张卡能不能升级」的方式是：<b>这 8 类增量里至少有一类被开启</b>
    /// （<c>CardUpgradeInfo.GetHasCardUpgradeInfo()</c>）。
    /// 全部留空 = 这张卡不可升级 —— 那是平台的默认状态。
    /// <para>
    /// 升级**不需要**另外写一张卡：游戏会把这里的增量**应用到原卡的深拷贝**上。
    /// 这也是为什么它和"特殊升级"（指向另一张卡）是两套不同机制。
    /// </para>
    /// </remarks>
    public sealed class CardUpgradeDef
    {
        /// <summary>改**效果数值**：按效果下标指定升级后的数字槽。最常用。</summary>
        public List<EffectNumberChangeDef> EffectNumbers { get; set; }

        /// <summary>改**费用**（整份替换，格式同卡牌的 <c>cost</c>）。</summary>
        public List<CostSlotDef> Cost { get; set; }

        /// <summary>改**卡面流派色**（整份替换）。</summary>
        public List<string> Colors { get; set; }

        /// <summary>
        /// 升级后**新增**的关键词。写法与卡牌的 <c>keywords</c> 完全一致
        /// （字符串，或 <c>{ name, resolve, effects }</c> 对象）。
        /// </summary>
        /// <remarks>
        /// ★★ <b>这是"升级关键词效果"的唯一正路</b>（原版就这么干）。
        /// 游戏 <c>CardUpgradeInfo</c> 里能碰到关键词的只有两个开关：
        /// <c>removeCardKeyword</c> 与 <c>addCardKeyword</c>。
        /// 效果类增量（<c>changeEffectNumber</c> / <c>changeEffect</c> / <c>insertEffect</c>）
        /// <b>全部只作用于卡牌自己的 effects</b>；而那条"关键词卡"路径
        /// （<c>UpgradeKeywordInfo</c>）要求 <c>GetHasCardSoFromName</c> 为真 ——
        /// 该名单只有 <c>Loaded</c>/<c>Reload</c>/<c>Summon</c>/<c>Construct</c>/<c>Constructed</c>，
        /// <b>不含 Miracle</b>（实测 dump 里 <c>keywordCardSoName</c> 也是空的）。
        /// <para>
        /// 原版<b>缓慢剑</b>的实测数据（平台探针 dump）正是：
        /// <c>addKw=True rmKw=True changeEffectNumber=True</c> ——
        /// <b>删掉旧奇迹、换上数值更大的新奇迹</b>，卡牌本体则走 <c>effectNumbers</c>。
        /// </para>
        /// <para>
        /// 游戏执行顺序是**先 remove 后 add**（<c>GetUpgradeCardInfo</c> L896 → L910），
        /// 所以"同名替换"是安全的：写 <c>removeKeywords:["Miracle"]</c> +
        /// <c>addKeywords:[{name:"Miracle", …}]</c> 即可。
        /// </para>
        /// </remarks>
        public List<KeywordRefDef> AddKeywords { get; set; }

        /// <summary>升级后**移除**的关键词名。</summary>
        public List<string> RemoveKeywords { get; set; }

        /// <summary>**替换**某个效果（按下标）。</summary>
        public List<EffectReplaceDef> ReplaceEffects { get; set; }

        /// <summary>**插入**一个效果（到指定下标位置）。</summary>
        public List<EffectReplaceDef> InsertEffects { get; set; }

        /// <summary>改写某个关键词的**数值**。</summary>
        public List<KeywordNumberDef> KeywordNumbers { get; set; }
    }

    /// <summary>升级时改某个效果的数值。</summary>
    public sealed class EffectNumberChangeDef
    {
        /// <summary>效果下标（从 0 开始，对应卡牌 <c>effects</c> 数组的顺序）。</summary>
        public int? Index { get; set; }

        /// <summary>升级后的数字槽（格式同效果的 <c>amounts</c>）。</summary>
        public List<NumberSpec> Amounts { get; set; }
    }

    /// <summary>升级时替换或插入一个效果。</summary>
    public sealed class EffectReplaceDef
    {
        /// <summary>效果下标。</summary>
        public int? Index { get; set; }

        /// <summary>要放入的效果（替换/插入时都取第一项）。</summary>
        public List<EffectSpec> Effects { get; set; }
    }

    /// <summary>升级时改写某个关键词的数值。</summary>
    public sealed class KeywordNumberDef
    {
        /// <summary>关键词名。</summary>
        public string Keyword { get; set; }

        /// <summary>升级后的数值。</summary>
        public int? Number { get; set; }
    }

    /// <summary>
    /// 一张卡牌的声明（对应 <c>modkit/card@1</c>）。
    /// </summary>
    public sealed class CardDef
    {
        /// <summary>schema 版本串，必须等于 <c>modkit/card@1</c>。</summary>
        public string Schema { get; set; }

        /// <summary>mod 内唯一标识，<c>^[a-z][a-z0-9_]{2,40}$</c>。平台会加 mod 前缀防跨 mod 冲突。</summary>
        public string Id { get; set; }

        /// <summary>显示名，<c>{语言标签: 文本}</c>。</summary>
        public Dictionary<string, string> Name { get; set; }


        /// <summary>
        /// 费用槽列表。**每个槽 = 卡面上的一枚费用图标。**
        /// </summary>
        /// <remarks>
        /// JSON 里两种写法：
        /// <list type="bullet">
        /// <item>字符串 —— 单色槽，如 <c>"Void"</c>；</item>
        /// <item>字符串数组 —— **多色槽（任选其一）**，如 <c>["Void", "Red"]</c>，
        /// 表示"虚色或红色都能付这一格"。</item>
        /// </list>
        /// <para>
        /// ★ 混合槽不是平台发明的能力：游戏的 <c>ColorEArray</c> 本身就是颜色数组，
        /// 卡面图标按**完整的有序颜色序列**在 <c>ManaManager.ManaIconDic</c> 里查表。
        /// 平台启动时枚举该字典的全部键拿到真实可用组合；未注册的组合在**加载期**就报错——
        /// 否则游戏会抛 <c>KeyNotFoundException</c>，而且索引器不带消息、
        /// 只显示 <c>Card.ColorEArray</c>，完全无法定位。
        /// </para>
        /// </remarks>
        public List<CostSlotDef> Cost { get; set; }

        /// <summary>稀有度名（<c>Common</c>/<c>Uncommon</c>/<c>Rare</c>）。</summary>
        public string Rarity { get; set; }

        /// <summary>卡面流派色。</summary>
        public List<string> Colors { get; set; }

        /// <summary>结算时机名（<c>OnCast</c>/<c>OnDraw</c>/<c>OnDiscard</c>/<c>EndInHand</c>）。</summary>
        public string Resolve { get; set; }

        /// <summary>
        /// 借用的原版卡名（提供卡面 / 动画 / Tooltip）。
        /// Q2 决策：只借原版资产，平台在加载期校验该名字确实存在。
        /// </summary>
        public string ArtTemplate { get; set; }

        /// <summary>
        /// mod **自带**的卡面图：相对 mod 目录的路径（如 <c>"art/my_card.png"</c>）。
        /// 留空 = 沿用 <see cref="ArtTemplate"/> 那张原版卡面。
        /// </summary>
        /// <remarks>
        /// ★ 为什么只收"相对路径"：mod 是要打包分发给别人的，绝对路径在别人机器上必然失效。
        /// 平台在**加载期**就拒绝绝对路径、盘符与 <c>..</c> 上跳，并把文件是否存在一起查掉
        /// （见 ModKit.Core 的 ModLoader / ContentValidator）——不留给运行期静默失败。
        /// <para>
        /// 支持 <c>.png</c> / <c>.jpg</c> / <c>.jpeg</c>。建议**正方形**、边长 256 或 512 px：
        /// 游戏卡面不是"SpriteRenderer 画一张 Sprite"，而是把
        /// <c>cardArt.texture</c> 塞进卡面材质的 <c>_CardArtTex</c> 槽按 UV 采样
        /// （反编译 <c>Card.CardBase</c> 第 284 行），所以：
        /// ① 非正方形的图会被拉伸；② Sprite 的 pivot / pixelsPerUnit 对卡面**没有影响**。
        /// </para>
        /// <para>
        /// 这张图**只换卡面**：动画、Tooltip、费用图标仍然来自 <see cref="ArtTemplate"/>，
        /// 所以 <c>artTemplate</c> 依然是必填字段。
        /// </para>
        /// </remarks>
        public string Art { get; set; }

        /// <summary>效果列表，至少 1 项。</summary>
        public List<EffectSpec> Effects { get; set; }

        /// <summary>
        /// 挂在这张卡上的关键词。
        /// </summary>
        /// <remarks>
        /// 每项可以是**字符串**（只要关键词本身），也可以是**对象**（关键词 + 自带效果）。
        /// 有些关键词必须自带效果才会生效 —— 详见 <see cref="KeywordRefDef"/>。
        /// </remarks>
        public List<KeywordRefDef> Keywords { get; set; }

        /// <summary>
        /// 升级（"+"）声明。**留空 = 这张卡不可升级**（平台默认状态）。
        /// </summary>
        /// <remarks>
        /// 游戏判断「能不能升级」看的就是这里有没有内容 ——
        /// 详见 <see cref="CardUpgradeDef"/> 的说明。
        /// </remarks>
        public CardUpgradeDef Upgrade { get; set; }
    }

    /// <summary>
    /// 一件遗物的声明（对应 <c>modkit/relic@1</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 遗物只有一种受支持形态：触发型（<see cref="Trigger"/> + <see cref="Effects"/>）。
    /// （原"特殊遗物"通路 —— 遗物 JSON 的那个可选 C# 钩子字段 —— 已于 2026-10-14 **删除**，
    /// 见 docs\ModKit-能力缺口与下一步.md：它在运行期没有任何消费者，只会把遗物静默装配成"像 artTemplate 那件原版遗物"的克隆。）
    /// </para>
    /// </remarks>
    public sealed class RelicDef
    {
        public string Schema { get; set; }
        public string Id { get; set; }
        public Dictionary<string, string> Name { get; set; }

        /// <summary>借用的原版遗物名（提供图标与模板）。</summary>
        public string ArtTemplate { get; set; }

        /// <summary>触发点名（取自平台内置的**实际发出**触发点表）。</summary>
        public string Trigger { get; set; }

        /// <summary>触发型遗物的效果列表。</summary>
        public List<EffectSpec> Effects { get; set; }

        /// <summary>是否进入遗物池。</summary>
        public bool AddToPool { get; set; } = true;

        // ---------------------------------------------------------------------
        //  触发点附加字段（Tier 1，2026-10-14）
        //
        //  ★ 为什么平铺在遗物上，而不是塞进 Effects：
        //    这些字段属于**触发点**（游戏 `TriggerTargetInfo`），不属于效果。
        //    游戏 `TriggerManager.CheckTriggerFitTargetTrigger` 按触发点名读取它们；
        //    不提供时行为分两类（见每个属性的「不给会怎样」）。
        //  ★ 只对下表里的触发点生效；给了别的触发点不读的字段会被**忽略**
        //    （校验器给「字段被忽略」警告，装配层也不写入）。
        // ---------------------------------------------------------------------

        /// <summary>
        /// <c>ChangeBattlePhase</c> 专用：指定"哪个阶段、是开始还是结束"。
        /// </summary>
        /// <remarks>
        /// JSON 名 <c>battlePhase</c>。原版真实取值示例（实机 dump 205 件遗物）：
        /// <c>{"phase":"Main","isStart":false}</c>、<c>{"phase":"Upkeep",...}</c>。
        /// <para>
        /// ★ **不给会崩**：游戏里它是 <b>class</b> 字段，为 null 时
        /// <c>TriggerManager.CheckTriggerFitTargetTrigger</c> 直接
        /// <c>NullReferenceException</c> —— 不是"不触发"，是**崩游戏**。
        /// </para>
        /// </remarks>
        public BattlePhaseSpec BattlePhase { get; set; }

        /// <summary>
        /// <c>WholeNewTurnStart</c> 专用："第 N 回合 / 在第 N 回合之前 / 之后"。
        /// </summary>
        /// <remarks>
        /// JSON 名 <c>certainTurn</c>（对应游戏字段名 <c>certainTurnStartInfo</c>）。
        /// 原版真实取值示例：<c>{"require":false,"turnCount":0,"relation":"Before"}</c>。
        /// <para>
        /// ★ **不给会崩**：同 <see cref="BattlePhase"/> —— 它也是 class 字段，
        /// 为 null 时 <c>CheckTriggerFitTargetTrigger</c> 抛 <c>NullReferenceException</c>。
        /// 想让遗物"每回合都触发"就照原版写 <c>require:false, turnCount:0, relation:"Before"</c>。
        /// </para>
        /// </remarks>
        public CertainTurnSpec CertainTurn { get; set; }

        /// <summary>
        /// "相对谁"：<c>Owner</c>（自己）/ <c>Opponents</c>（对手，<b>注意是复数</b>）/ <c>All</c>。
        /// </summary>
        /// <remarks>
        /// JSON 名 <c>relativeCharacter</c>（对应游戏字段 <c>relativeCharacterType</c>）。
        /// 被 <c>ChangeBattlePhase</c> / <c>CharacterRollDice</c> /
        /// <c>EnemyHealthBarBreak</c> / <c>CharacterCheck</c> 读取。
        /// <para>
        /// ★ **不给会怎样**：默认 <c>Owner</c>。对 <c>EnemyHealthBarBreak</c> 来说默认值
        /// 意味着"只在**自己**血条被击破时触发" ⇒ 作者想要的"敌人血条被击破"**永不匹配**（静默死）。
        /// </para>
        /// </remarks>
        public string RelativeCharacter { get; set; }

        /// <summary>
        /// 触发点的字符串参数。按触发点不同含义不同：<c>TakeRest</c> 取
        /// <c>LongRest</c> / <c>ShortRest</c>；<c>CardKeywordTrigger</c> 取关键词名；
        /// <c>AbilityStandardTrigger</c> 取能力名；<c>CharacterCheck</c> 取判定 id。
        /// </summary>
        /// <remarks>
        /// JSON 名 <c>extraString</c>（游戏字段同名）。原版 dump 里多数为**空**，
        /// 只有 <c>TakeRest</c> 等用非空值。
        /// <para>
        /// ★ **不给会怎样**：平台不写 ⇒ 游戏侧为 <c>null</c> ⇒ 只在"事件侧也是 null"
        /// 时才匹配。对 <c>AbilityStandardTrigger</c> 这类比较它的触发点就是**永不匹配**（静默死）。
        /// </para>
        /// </remarks>
        public string ExtraString { get; set; }

        /// <summary>
        /// <c>ManaConsumed</c> 专用：消耗的是哪种颜色的法力。
        /// 合法值 <c>Red</c> / <c>Green</c> / <c>Blue</c> / <c>Purple</c> / <c>Void</c> / <c>Black</c>。
        /// </summary>
        /// <remarks>
        /// JSON 名 <c>colorE</c>（游戏字段同名，类型是枚举 <c>ColorE</c>）。
        /// <para>
        /// ★ **不给会怎样**：默认枚举 0 = <c>Red</c> ⇒ 只在消耗**红色**法力时触发，
        /// 作者若想要别的颜色就是**静默错配**（不崩，但永远不是他想要的）。
        /// </para>
        /// </remarks>
        public string ColorE { get; set; }
    }

    /// <summary>
    /// 战斗阶段描述（遗物 JSON 的 <c>battlePhase</c>，游戏 <c>CharacterAndBattlePhase</c>）。
    /// </summary>
    /// <remarks>
    /// 合法 <see cref="Phase"/>：<c>Upkeep</c> / <c>Main</c> / <c>End</c> / <c>CleanUp</c>
    /// （游戏 <c>Combat.BattleWorld.BattlePhase</c>，源 <c>Combat.BattleWorld/BattlePhase.cs:3-9</c>）。
    /// </remarks>
    public sealed class BattlePhaseSpec
    {
        /// <summary>阶段名（<c>Upkeep</c> / <c>Main</c> / <c>End</c> / <c>CleanUp</c>）。</summary>
        public string Phase { get; set; }

        /// <summary>
        /// 是"进入该阶段"还是"离开该阶段"。原版 <c>CommandStartBattlePhase.SendTrigger()</c>
        /// 一律用 <c>isStart: true</c>；"行动结束"类遗物用 <c>false</c>。
        /// </summary>
        public bool IsStart { get; set; }
    }

    /// <summary>
    /// "第几个回合"条件（遗物 JSON 的 <c>certainTurn</c>，游戏 <c>CertainTurnStartInfo</c>）。
    /// </summary>
    /// <remarks>
    /// 合法 <see cref="Relation"/>：<c>Before</c> / <c>Exactly</c> / <c>After</c>
    /// （游戏 <c>Combat.BattleWorld.CertainTurnRelationInfo</c>，
    /// 源 <c>Combat.BattleWorld/CertainTurnRelationInfo.cs:3-8</c>）。
    /// </remarks>
    public sealed class CertainTurnSpec
    {
        /// <summary>
        /// 是否真的启用回合数条件。<c>false</c> = 条件不强加（<b>每回合都触发</b>）。
        /// </summary>
        public bool Require { get; set; }

        /// <summary>目标回合数（<see cref="Require"/> 为 <c>false</c> 时游戏不读它，原版写 0）。</summary>
        public int TurnCount { get; set; }

        /// <summary>与 <see cref="TurnCount"/> 的关系（<c>Before</c> / <c>Exactly</c> / <c>After</c>）。</summary>
        public string Relation { get; set; }
    }

    /// <summary>
    /// 一个关键词的声明（对应 <c>modkit/keyword@1</c>，schema 仍为**草案**）。
    /// </summary>
    /// <remarks>
    /// P2 复核实证通过后才冻结此 schema。
    /// </remarks>
    public sealed class KeywordDef
    {
        public string Schema { get; set; }
        public string Id { get; set; }
        public Dictionary<string, string> Name { get; set; }

        /// <summary>借用的原版关键词名（提供图标与模板）。</summary>
        public string ArtTemplate { get; set; }

        /// <summary>是否带数字（映射游戏 <c>CardKeyWordUtil.GetHasNumberFromName</c>）。</summary>
        public bool HasNumber { get; set; }

        public List<EffectSpec> Effects { get; set; }
    }

    /// <summary>
    /// 一个能力的声明（对应 <c>modkit/ability@1</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么不需要新补丁</b>：能力的"行为"走 <c>EffectUtils.GenerateEffectFromEffectInfo</c>，
    /// 而那个工厂**已被 Transpiler 桥接管**。游戏侧取触发组件用的是常量名
    /// <c>"Ability.AbilityTriggerBase"</c>（不是按内容名拼接），所以没有逐内容的多态分派要桥接。
    /// </para>
    /// <para>
    /// <b>⚠️ 一个与关键词相反的差异</b>：这里失败是**响亮的** ——
    /// <c>AddComponent(null)</c> 会直接抛异常，而不是静默退化。好的一面是问题暴露得快；
    /// 坏的一面是它**会崩**，所以平台必须先确认 <c>Ability.AbilityTriggerBase</c> 存在。
    /// </para>
    /// </remarks>
    public sealed class AbilityDef
    {
        public string Schema { get; set; }
        public string Id { get; set; }
        public Dictionary<string, string> Name { get; set; }

        /// <summary>Buff / DeBuff / Util / Other（对应游戏 <c>AbilityType</c>）。</summary>
        public string Type { get; set; }

        /// <summary>Player / Enemy / All（对应游戏 <c>AbilityOwnerShip</c>）。</summary>
        public string Owner { get; set; }

        /// <summary>是否进随机池（对应 <c>AbilitySo.isInRandomPool</c>）。</summary>
        public bool AddToPool { get; set; }

        /// <summary>是否唯一（对应 <c>AbilityInfo.isUnique</c>）。</summary>
        public bool IsUnique { get; set; }

        /// <summary>借用的原版能力名（提供图标，**并决定未被 trigger 覆盖的行为**）。空则用第一个可用原版能力。</summary>
        public string ArtTemplate { get; set; }

        /// <summary>
        /// 触发时机（游戏的 <c>TriggerTargetInfo.AllTriggerType</c> 之一）。
        /// </summary>
        /// <remarks>
        /// ★ 为什么需要它（2026-10-11 实机证伪后补上）：
        /// 此前平台**不写** <c>AbilityInfo.triggerTargetInfo</c>，于是触发时机**继承自 <c>ArtTemplate</c>**——
        /// 作者只以为在借图标，实际连"什么时候触发"也一起借走了。
        /// 实机症状：作者写"获得 3 点格挡"，实际按模板的 <c>BeenAttacked</c> 触发
        /// （"被敌人攻击时获得护盾"），描述与效果不符。
        /// 现在作者可以显式声明触发时机（如 <c>BattleStart</c>）。
        /// </remarks>
        public string Trigger { get; set; }

        /// <summary>效果列表（至少 1 项）—— 这是能力的"行为"来源。</summary>
        public List<EffectSpec> Effects { get; set; }
    }

    /// <summary>
    /// 本地化文件（对应 <c>modkit/i18n@1</c>）。
    /// </summary>
    public sealed class I18nDef
    {
        public string Schema { get; set; }

        /// <summary>语言标签，如 <c>zh-Hans</c>。</summary>
        public string Language { get; set; }

        /// <summary>
        /// 表名 → (键 → 值)。
        /// 平台会自动把条目设为 IsSmart（否则 <c>{数字}</c> 占位符会抛 FormatException）。
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> Tables { get; set; }
    }

    /// <summary>
    /// mod 清单（<c>mod.json</c>）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 纯数据 mod 只需要 <see cref="Id"/> / <see cref="Name"/> / <see cref="Version"/>
    /// 加内容列表；带 C# 效果的 mod 额外填 <see cref="EntryAssembly"/>。
    /// </para>
    /// </remarks>
    public sealed class ModManifest
    {
        public string Schema { get; set; }

        /// <summary>mod 唯一标识，<c>^[a-z][a-z0-9_]{2,40}$</c>。</summary>
        public string Id { get; set; }

        public string Name { get; set; }
        public string Version { get; set; }

        /// <summary>作者（可选，仅用于日志与冲突提示）。</summary>
        public string Author { get; set; }

        /// <summary>
        /// 入口程序集文件名（相对 mod 目录），如 <c>MyMod.dll</c>。
        /// 纯数据 mod 留空——此时平台不加载任何代码，是最安全的形态。
        /// </summary>
        public string EntryAssembly { get; set; }

        /// <summary>卡牌 JSON 文件（相对 mod 目录）。</summary>
        public List<string> Cards { get; set; }

        /// <summary>遗物 JSON 文件。</summary>
        public List<string> Relics { get; set; }

        /// <summary>关键词 JSON 文件。</summary>
        public List<string> Keywords { get; set; }
        /// <summary>能力声明文件列表（schema <c>modkit/ability@1</c>）。</summary>
        public List<string> Abilities { get; set; }

        /// <summary>本地化 JSON 文件。</summary>
        public List<string> I18n { get; set; }

        /// <summary>目标游戏版本（可选）。与当前版本不符时平台给 Warn 并按 FailOpen 决定是否加载。</summary>
        public string GameVersion { get; set; }
    }
}
