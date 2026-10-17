// =============================================================================
//  ModKit.Game · 内容装配器（ApiCardDef/ApiRelicDef → 游戏对象）
//
//  这是平台的"最后一公里"：把作者写的 JSON 变成游戏里真的能打出的一张卡。
//
//  ★ 本文件的所有字段名与语义，均已由独立只读侦察核实（非我凭记忆写），
//    核实中纠正了 4 处常见误解，逐条记在下面 —— 它们都很容易写错且**不报错**：
//
//    ① CardInfo 是 **struct**（不是 class），CardSo.cardInfo 是**值类型私有字段**，
//       反射 SetValue 可行（装箱）。
//    ② EffectInfo 有 **27 个**实例字段；"28" 是静态数组 EffectNameArray 的长度，
//       与字段数无关。
//    ③ 8 个 specialEffectHasXxx 布尔在**运行时是死字段** —— 全量反编译源码里
//       除声明与 CreateDeepCopy 复制外零读取点。留 false 完全安全
//       （它们是 Editor 期自定义 Inspector 的开关，Editor 程序集不在反编译范围内）。
//    ④ RNumberInfo 的 state 参数被**静默丢弃**（struct 里没有承载字段）；
//       RNumberStates 同样零读取。
//
//  ★ 一个必须写清的**架构级前置依赖**（否则装配出的卡必炸）：
//    EffectUtils 用 Type.GetType("Effects.Special.CardEffect.EffectSpEffect" + id)
//    解析效果类，而 **Type.GetType(string) 只搜索调用方程序集 + 核心库**。
//    调用方是 Assembly-CSharp —— 所以 mod 的效果类**必须**让这个查找能找到它们，
//    否则第 41 行 effectBase.EffectOwner 直接 NullReferenceException。
//    平台的对策是 Transpiler 桥（把该调用换成平台解析器，覆盖全 AppDomain），
//    见 ModKit.Game/ApiSurface.md 的"必需补丁"一节。
//    → **本装配器不做这个替换；它由平台引导流程在装配任何卡之前完成。**
//      未装桥就装配 = 静默造出必崩的卡，这是被明确禁止的组合。
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
    /// <summary>装配结果。</summary>
    public sealed class AssemblyResult
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        /// <summary>成功装配并注册的卡名。</summary>
        public readonly List<string> RegisteredCards = new List<string>();

        /// <summary>成功装配并注册的遗物名。</summary>
        public readonly List<string> RegisteredRelics = new List<string>();

        /// <summary>已核验类存在的关键词名。</summary>
        public readonly List<string> VerifiedKeywords = new List<string>();

        /// <summary>成功装配并注册的能力名。</summary>
        public readonly List<string> RegisteredAbilities = new List<string>();

        /// <summary>读回断言的人话小结（"(未执行)" = 没跑或无卡）。</summary>
        public string ReadBackSummary = "(未执行)";

        public bool HasErrors { get { return Errors.Count > 0; } }

        public override string ToString()
        {
            return string.Format("卡 {0} / 遗物 {1}｜错误 {2}，警告 {3}",
                RegisteredCards.Count, RegisteredRelics.Count, Errors.Count, Warnings.Count);
        }
    }

    /// <summary>
    /// 把内容定义装配成游戏对象并注册。
    /// </summary>
    /// <remarks>
    /// 每个内容独立 try/catch —— 与 Loader 的失败隔离口径一致：
    /// 一张卡装配失败不影响其他卡。
    /// </remarks>
    public sealed partial class ContentAssembler
    {
        private readonly IGameLog _log;
        private readonly ContentCapabilities _caps;

        public ContentAssembler(IGameLog log, ContentCapabilities caps)
        {
            _log = log;
            _caps = caps;
        }

        /// <summary>能力表（用于把平台枚举映射到游戏枚举、并校验费用槽）。</summary>
        public sealed class ContentCapabilities
        {
            /// <summary>探测得到的可用费用色（游戏枚举，单色）。</summary>
            public readonly List<ColorE> AvailableCostColors = new List<ColorE>();

            /// <summary>
            /// 探测得到的**费用槽组合**（游戏枚举，含单色与多色混合）。
            /// </summary>
            /// <remarks>
            /// 由 <c>GameCapabilitiesProbe.ProbeCostCombos</c> 枚举 <c>ManaIconDic</c> 的键得到。
            /// 多色混合槽的图标是**预制资产**，所以只有这里列出的组合能用，且**顺序参与匹配**。
            /// </remarks>
            public readonly List<List<ColorE>> AvailableCostCombos = new List<List<ColorE>>();

            /// <summary>这个费用槽组合注册过图标吗？—— **有序**匹配。</summary>
            public bool IsKnownCostCombo(List<ColorE> combo)
            {
                if (combo == null || combo.Count == 0) { return false; }

                for (int i = 0; i < AvailableCostCombos.Count; i++)
                {
                    var known = AvailableCostCombos[i];
                    if (known.Count != combo.Count) { continue; }

                    bool same = true;
                    for (int j = 0; j < known.Count; j++)
                    {
                        if (known[j] != combo[j]) { same = false; break; }
                    }
                    if (same) { return true; }
                }
                return false;
            }
        }

        // ---------------------------------------------------------------------
        // 卡牌
        // ---------------------------------------------------------------------

        /// <summary>
        /// 装配一张卡并注册进卡池。
        /// </summary>
        /// <remarks>
        /// 前置假设：调用方已跑过校验（<c>ContentValidator</c>），
        /// 因此 <c>artTemplate</c> 必然存在于原版卡名清单、费用色必然已探测可用。
        /// 这里再兜一层判空，是为了"校验器有 bug 时也不会崩游戏"。
        /// </remarks>
        /// <param name="hasFlavorText">
        /// 作者是否**确实**为这张卡提供了风味文本（i18n 的 <c>FlavorText</c> 表里有对应键）。
        /// 为 <c>true</c> 时平台会在克隆体上打开游戏的"显示风味文本"开关 —— 详见 2b 步骤的注释。
        /// </param>
        /// <param name="modRoot">
        /// 这个 mod 的目录绝对路径（<c>ModLoadResult.Path</c>）。仅当卡片声明了 <c>art</c>
        /// （自带卡面图）时用得上；传 <c>null</c> 时跳过自带图、仍用模板卡面。
        /// </param>
        public CardSo AssembleCard(ApiCardDef def, AssemblyResult result, bool hasFlavorText = false,
                                   string modRoot = null)
        {
            if (def == null) { throw new ArgumentNullException("def"); }
            if (result == null) { result = new AssemblyResult(); }

            try
            {
                // --- 1. 取模板（Q2 决策：只借原版资产）---
                var mgr = CardDataManager.Instance;
                if (mgr == null)
                {
                    result.Errors.Add(def.Id + "：CardDataManager.Instance 为 null（数据尚未加载）。");
                    return null;
                }

                var template = mgr.GetCardSoByName(def.ArtTemplate);
                if (template == null)
                {
                    result.Errors.Add(def.Id + "：找不到模板卡 '" + def.ArtTemplate + "'。");
                    return null;
                }

                // --- 2. 克隆模板 ---
                // 卡面(cardArt)/动画(cardAnimationInfo)/extraTooltip 全部来自模板 CardSo。
                // 为什么必须克隆：Resources.LoadAll 的内容烘焙在 resources.assets 里，
                // 运行期无法追加资源（第 1 步不变量 I3）。
                var so = UnityEngine.Object.Instantiate(template);
                so.name = "ModKit_" + def.Id;
                so.cardName = def.Id;
                so.specialUpgradedCardName = string.Empty;

                // --- 2a. 自带卡面图（作者画的图覆盖模板卡面）---
                // ★ 只换 so.cardArt 这一个引用就够：卡面渲染读的是 cardArt.texture
                //   （CardBase.cs:284 把 texture 塞进材质的 _CardArtTex 槽、按 UV 采样），
                //   所以 Sprite 的 pivot / pixelsPerUnit 对卡面没有影响；
                //   但卡面是按**正方形拉伸**显示的 ⇒ 非正方形会被拉扁。
                // ★ 改的是克隆体；原版模板的 cardArt 一个字节都没动。
                // ★ 加载失败只记 warning、退回模板卡面 —— 与"一张坏图不该让整个 mod 全灭"同一口径。
                if (!string.IsNullOrEmpty(def.Art))
                {
                    if (string.IsNullOrEmpty(modRoot))
                    {
                        result.Warnings.Add(def.Id + "：声明了 art 但装配时没拿到 mod 目录，"
                            + "自带卡面被跳过（仍用模板卡面）。");
                    }
                    else
                    {
                        string artError;
                        var art = ModArt.Load(ModArt.Resolve(modRoot, def.Art), out artError);
                        if (art == null)
                        {
                            result.Warnings.Add(def.Id + "：自带卡面加载失败，已退回模板卡面 —— " + artError);
                        }
                        else
                        {
                            so.cardArt = art;
                            if (art.texture.width != art.texture.height)
                            {
                                result.Warnings.Add(def.Id + "：自带卡面不是正方形（"
                                    + art.texture.width + "×" + art.texture.height
                                    + "），卡面按正方形拉伸显示，会被拉扁。");
                            }
                        }
                    }
                }

                // --- 2b. 风味文本开关（作者写了却不显示的那个 bug 就在这里）---
                // ★ 游戏取风味文本前会**先查一个开关**：
                //     CardBase.GetLocalizedFlavorTextByCardName() 只在 extraTooltip.hasFlavorText 为真时才被调用
                //     而这个值来自**模板 CardSo**（extraTooltip = _linkedCardSo.extraTooltip）
                //   原版模板通常是**关着**的 —— 于是作者在 i18n 的 FlavorText 表里写了文案，
                //   游戏**永远不会去查**，症状就是"风味描述不生效"（卡牌效果不受影响）。
                // ★ 只在作者**确实提供了**风味文本时才打开：否则游戏会去查一个不存在的键，
                //   把 "No translation found ..." 这种提示当成风味文本显示出来，更难看。
                // ★ ExtraTooltip 是 **struct 字段**，改的是克隆体，不会污染原版模板。
                if (hasFlavorText)
                {
                    so.extraTooltip.hasFlavorText = true;
                }

                // --- 3. 装配 CardInfo ---
                // CardInfo 是 **struct**（值类型），不能返回 null —— 用 Nullable 表达"装配失败"。
                CardInfo? maybeInfo = BuildCardInfo(def, result);
                if (!maybeInfo.HasValue) { return null; }
                CardInfo info = maybeInfo.Value;

                // --- 4. 反射写回私有字段 ---
                // CardSo.cardInfo 是 private 字段、类型是 struct（值类型）→ 装箱后 SetValue。
                Reflect.Set(so, "cardInfo", info);

                // --- 5. 注册进 GetCardSoByName 的唯一入口 ---
                RegisterCardSo(so, def.Id, result);

                // --- 6. 进战役随机池（否则卡存在但抽不到）---
                if (def.AddToPoolOrDefault())
                {
                    AddToCampaignPool(so, result, def.Id);
                }

                result.RegisteredCards.Add(def.Id);
                _log.Info("已装配卡：" + def.Id + "（模板 " + def.ArtTemplate + "）");
                return so;
            }
            catch (Exception ex)
            {
                result.Errors.Add(def.Id + "：装配异常 —— " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        // ---------------------------------------------------------------------
        // 遗物
        // ---------------------------------------------------------------------

        /// <summary>
        /// 装配一件遗物并注册进池。
        /// </summary>
        /// <remarks>
        /// 遗物比卡多一层数据（与现役实现、侦察报告一致）：
        /// <list type="number">
        /// <item>效果类：<c>RelicManager</c> 用 <c>Type.GetType("Relic.RelicEffects.Relic" + name)</c> 取
        /// → 依赖 Transpiler 桥（与卡同一机制）；</item>
        /// <item>资产：<c>RelicSo</c> 由 <c>Resources.LoadAll</c> 烘焙读入，运行期加不了文件
        /// → 同样"克隆一张原版遗物 + 改名字"；</item>
        /// <item>掉落池：靠 <c>RelicDataCollection</c> 列表，追加一个自建 collection 即可进池。</item>
        /// </list>
        /// <para>
        /// <b>时序注意</b>：<c>DataManager.LoadAllGameData()</c> 会重建 <c>RelicDataManager</c>
        /// 并 Clear 掉 <c>_allRelicDataCollections</c> / <c>_allStandPoolRelicSo</c>
        /// （唯独 <c>_allInGameRelicSo</c> 用 TryAdd 能存活）→ 本方法必须在它之后调用。
        /// </para>
        /// </remarks>
        public RelicSo AssembleRelic(ApiRelicDef def, AssemblyResult result)
        {
            if (def == null) { throw new ArgumentNullException("def"); }
            if (result == null) { result = new AssemblyResult(); }

            try
            {
                var dm = DataManager.Instance;
                if (dm == null)
                {
                    result.Errors.Add(def.Id + "：DataManager.Instance 为 null（数据尚未加载）。");
                    return null;
                }

                var relicDataManager = dm.GetRelicDataManager();
                if (relicDataManager == null)
                {
                    result.Errors.Add(def.Id + "：GetRelicDataManager() 为 null（LoadAllGameData 尚未执行）。");
                    return null;
                }

                // --- 1. 取模板 ---
                var all = relicDataManager.GetAllInGameRelicSo();
                if (all == null || all.Length == 0)
                {
                    result.Errors.Add(def.Id + "：游戏里没有任何遗物可作模板。");
                    return null;
                }

                RelicSo template = null;
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].relicInfo != null &&
                        all[i].relicInfo.relicName == def.ArtTemplate)
                    {
                        template = all[i];
                        break;
                    }
                }
                if (template == null) { template = all[0]; }   // 校验器应已保证存在，兜底取第一件

                // --- 2. 克隆并改数据 ---
                var so = UnityEngine.Object.Instantiate(template);
                so.name = "ModKit_" + def.Id;
                if (so.relicInfo == null)
                {
                    result.Errors.Add(def.Id + "：模板遗物的 relicInfo 为 null，无法装配。");
                    return null;
                }
                so.relicInfo.relicName = def.Id;
                // ★ isSpecialRelic = false（2026-10-11 实机证伪后改正）
                //   反编译 RelicManager.GenerateRelicEffectFromRelicInfo：
                //     Activator.CreateInstance(relicInfo.isSpecialRelic
                //         ? Type.GetType("Relic.RelicEffects.Relic" + relicInfo.relicName)
                //         : Type.GetType("Relic.RelicEffects.RelicBase"))
                //   isSpecialRelic 为 true 时会去找名字叫 "Relic" + relicName 的 C# 类。
                //   我们的 relicName 带 mod 前缀（如 sample_starter.stone_bracelet），
                //   拼出来是 "Relic.RelicEffects.Relicsample_starter.stone_bracelet" ——
                //   **永远不存在** → Type.GetType 返回 null → Activator.CreateInstance 抛
                //   ArgumentNullException（实机在遗物图鉴里点开就崩）。
                //   数据化遗物应当走 RelicBase（通用遗物行为），行为由 mainEffectInfo 驱动。
                //   注意：Transpiler 桥**不覆盖**这个调用点（它只补 4 个效果工厂），
                //   所以这里不能指望桥兜底。
                so.relicInfo.isSpecialRelic = false;
                // ★ 描述来源必须与模板解耦（2026-10-11 实机证伪后改正）
                //   反编译 RelicBase.ExtraStringForSpecialRelic：
                //     if (UseSpecialDesKey) { key = RelicName + "_SpDes"; 用它在 Relic 表查描述并**覆盖**自动生成的 }
                //     else                  { 用游戏**自动生成**的描述（由 mainEffectInfo 推导） }
                //   模板 Abacus 的 useSpecialDesKey 为 true，被我们原样继承 →
                //   游戏去找 "sample_starter.stone_bracelet_SpDes"，我们没提供
                //   → 界面显示 `No translation found for '…_SpDes' in Relic`（实机截图）。
                //   关掉它 → 描述由效果**自动生成**，与卡牌同一机制，且会如实描述真实行为。
                //   （遗物描述同样由游戏程序化生成，不走本地化表 —— 所以内容 JSON 里没有 description 字段。）
                so.relicInfo.useSpecialDesKey = false;

                // ★★ 遗物主类型必须**显式**声明，否则运行期会把作者写的 trigger 吃掉
                //   （2026-10-13 反编译源码证实，此前平台从不写这个字段，全靠 artTemplate 运气）。
                //   证据链（Relic/RelicBase.cs）：
                //     private void GenerateMainRelicEffect(RelicInfo relicInfo) {
                //         var relicEffect = GenerateRelicEffectFromRelicEffectInfo(relicInfo.mainEffectInfo);
                //         if ((uint)relicMainType <= 1u)                    // Active(0) / ChargedTrigger(1)
                //             relicEffect.TriggerTargetInfo = new TriggerTargetInfo { triggerType = "Passive" };
                //         MainRelicEffect = relicEffect;                    // ← 之后才 RegisterTrigger()
                //     }
                //   而 RegisterTrigger() 在 RelicBase.cs:332/335 才被调用，且
                //   TriggerManager.RegisterTriggerTarget 明确**丢弃 "Passive"**
                //   （Trigger/TriggerManager.cs:29）⇒ 主类型若为 Active/ChargedTrigger，
                //   **作者声明的 trigger 永远不会注册**，遗物静默失效（不报任何错）。
                //   ⇒ 置为 NoChargeTrigger（= 纯触发、不充能），与平台目前暴露的能力一致
                //     （平台只支持「trigger + effects」这一形态，没有充能/主动使用的数据字段）。
                //   ⇒ 同时按游戏自己的校验口径（Relic/RelicInfo.cs:68-84 ValidateSelf，
                //     那是 Editor 专用；这里在运行期补做等价动作）把充能相关字段归零。
                //   ⇒ 不要删这一段：删掉后遗物是否可用取决于作者借到哪件原版遗物，无法预测。
                so.relicInfo.relicMainType = RelicMainType.NoChargeTrigger;
                so.relicInfo.hasTriggerLimit = false;
                so.relicInfo.noTriggerAfterFullCharge = false;
                so.relicInfo.chargeCount = -1;
                so.relicInfo.canChargeBeyondRequirement = false;

                // ★★ 把作者声明的「触发 + 效果」真正写进去（2026-10-11 补上，这是遗物一直"没作用"的真因）
                //   此前 AssembleRelic 只改了 relicName / isSpecialRelic / useSpecialDesKey，
                //   **从未写过 `mainEffectInfo`** —— 于是遗物在功能上就是模板 Abacus，
                //   作者在 JSON 里写的 trigger / effects 被**静默忽略**。
                //   数据结构（反编译 RelicEffectInfo）就是为此设计的：
                //     public class RelicEffectInfo {
                //         public TriggerTargetInfo triggerTargetInfo;   // 何时触发
                //         public EffectInfo[] effectInfo;               // 做什么
                //     }
                //   触发名取自 TriggerTargetInfo.AllTriggerType（33 个，见 Core/TriggerTypeNames.cs）。
                if (!string.IsNullOrEmpty(def.Trigger))
                {
                    var triggerLookup = ModKit.Core.TriggerTypeNames.Resolve(def.Trigger);
                    if (!triggerLookup.Mapped)
                    {
                        result.Errors.Add(def.Id + "：未知触发类型 '" + def.Trigger + "'（校验器应已拦截）。");
                        return null;
                    }

                    var effectList = new List<EffectInfo>();
                    if (def.Effects != null)
                    {
                        foreach (var spec in def.Effects)
                        {
                            // 遗物效果没有"结算时机"的概念（那由 trigger 决定），
                            // 所以给一个中性值 OnCast —— 卡牌侧才用它区分 抽到/弃掉/回合结束。
                            var ei = BuildEffectInfo(spec, CardEffectResolveType.Cast, def.Id, result);
                            if (ei != null) { effectList.Add(ei); }
                        }
                    }

                    if (effectList.Count == 0)
                    {
                        result.Errors.Add(def.Id + "：声明了 trigger 但没有任何成功装配的效果。");
                        return null;
                    }

                    // ★★★ 硬性不变量（2026-10-14 实机 NullReferenceException 崩溃后补；有 dump 实证）★★★
                    //   游戏 TriggerManager.CallTriggerEvent 会对**每一个已注册 target**逐个调用
                    //   CheckTriggerFitTargetTrigger(Trigger/TriggerManager.cs:70-185)，其中
                    //     · ChangeBattlePhase 分支解引用 **target.battlePhase**（:97）
                    //     · WholeNewTurnStart 分支解引用 **target.certainTurnStartInfo**（:122）
                    //   两者都是 **class ⇒ null 即 NullReferenceException**（崩在 CommandEndBattlePhase 等
                    //   任何发出该触发事件的地方）。原版 dump 实证：**288/288 个目标两者全部非 null** ——
                    //   这是游戏的硬性不变量，任何注册进 TriggerManager 的 target 都必须补全这两个字段。
                    //   旧实现 `new TriggerTargetInfo { triggerType = … }` 让其余字段全默认（= null）⇒
                    //   破坏该不变量 ⇒ 游戏一发 ChangeBattlePhase 就崩（2026-10-14 用户实机撞到）。
                    //   这里补**与原版最常见取值一致的中性默认值**：
                    //     · battlePhase = (Main,false)   —— 只在 ChangeBattlePhase 分支被读，作者给了就覆盖；
                    //     · certainTurnStartInfo = {requireCertainTurn=false, turnCount=0, Before}
                    //       —— 只在 WholeNewTurnStart 分支被读，require=false 时游戏 :122 直接 return true。
                    //   ⚠️ TriggerTargetInfo 是 struct：字段改动必须在**整体赋给 RelicEffectInfo 之前**做完。
                    var triggerTargetInfo = new TriggerTargetInfo
                    {
                        triggerType = triggerLookup.Value,
                        battlePhase = new Combat.BattleWorld.BattlePhaseRequirement(
                            Combat.BattleWorld.BattlePhase.Main, false),
                        certainTurnStartInfo = new Combat.BattleWorld.CertainTurnStartInfo
                        {
                            requireCertainTurn = false,
                            turnCount = 0,
                            certainTurnRelationInfo = Combat.BattleWorld.CertainTurnRelationInfo.Before
                        }
                    };
                    // ★★★ 必须传 `ref`（2026-10-14 实机事故后补）★★★
                    //   TriggerTargetInfo 是 **struct**：按值传参时 ApplyRelicTriggerFields 里的所有字段写入
                    //   都落在**副本**上，调用方这里一个字节都没变 ⇒ 作者声明的 battlePhase/certainTurn/
                    //   relativeCharacter/extraString/colorE 全部静默丢失，运行期只剩上面补的中性默认值。
                    //   实测后果：JSON 写 (Upkeep,isStart=true)（回合开始抽牌），实际在 (Main,isStart=false)
                    //   （玩家按下结束回合那一刻）触发 ⇒ 抽到的牌随即被 End 阶段的弃牌清掉，净效果为零。
                    //   lint 与单测都看不见它（只走 JSON→defs→校验器，从不读装配出来的游戏对象），
                    //   所以下面还必须**从最终对象回读**再校验一次。
                    ApplyRelicTriggerFields(ref triggerTargetInfo, def, triggerLookup.Value, result);

                    // 兜底自检：不变量一旦被破坏，**在装配期报错**而不是让它进游戏崩（装配期报错 = 该遗物不注册）。
                    if (triggerTargetInfo.battlePhase == null || triggerTargetInfo.certainTurnStartInfo == null)
                    {
                        result.Errors.Add(def.Id + "：内部不变量被破坏 —— triggerTargetInfo 的 class 字段为 null" +
                            "（battlePhase=" + (triggerTargetInfo.battlePhase == null ? "null" : "ok") +
                            "，certainTurnStartInfo=" + (triggerTargetInfo.certainTurnStartInfo == null ? "null" : "ok") +
                            "）。这两个字段为 null 会让游戏在触发时 NullReferenceException 崩溃，已拒绝装配该遗物。");
                        return null;
                    }

                    so.relicInfo.mainEffectInfo = new RelicEffectInfo
                    {
                        triggerTargetInfo = triggerTargetInfo,
                        effectInfo = effectList.ToArray()
                    };

                    // ★★★ 装配后回读校验（2026-10-14 实机事故后补的护栏）★★★
                    //   取**最终对象里**的字段（而不是上面的局部变量）与作者 JSON 声明逐字段比对。
                    //   目的：让「写入丢失 / 写错对象 / 之后又被覆盖」这类缺陷在装配期暴露 ——
                    //   报错并拒绝注册，而不是让作者拿着"写对了但不生效"的 JSON 上实机。
                    //   对照的字段集合与门控条件由 ModKit.Core/TriggerFieldNames 决定（与校验器同一真源）。
                    var fieldMismatches = VerifyRelicTriggerFields(
                        so.relicInfo.mainEffectInfo.triggerTargetInfo, def, triggerLookup.Value);
                    if (fieldMismatches.Count > 0)
                    {
                        result.Errors.Add(def.Id + "：触发点附加字段**写入后回读不一致** —— " +
                            string.Join("；", fieldMismatches.ToArray()) +
                            "。该遗物在游戏里的实际触发行为会与 JSON 声明不符，已拒绝装配。");
                        return null;
                    }

                    _log.Info("已写入遗物效果（附加字段已从最终对象回读校验通过）：" + def.Id + "（触发 " + triggerLookup.Value +
                        "，附加字段 " + DescribeTriggerFields(so.relicInfo.mainEffectInfo.triggerTargetInfo, triggerLookup.Value) +
                        "，效果 " + effectList.Count + " 个）");
                }

                so.relicInfo.acquired = false;

                // --- 3. 注册进私有字典 ---
                RegisterRelicSo(relicDataManager, "_allInGameRelicSo", so, def.Id, result);
                if (def.AddToPool)
                {
                    RegisterRelicSo(relicDataManager, "_allStandPoolRelicSo", so, def.Id, result);
                    // ★ 集合注册**不在这里做** —— 与卡池同构：必须"建集合 + 重建池子"成对执行，
                    //   否则会被 SetCampaignRelicPool 整体冲掉。这一步统一由 PlatformRuntime
                    //   在本 mod 全部遗物装配完之后调用 RegisterRelicCollection 完成
                    //   （每件遗物单独建集合会造出 N 个集合，且都要各自触发一次重建）。
                }

                result.RegisteredRelics.Add(def.Id);
                _log.Info("已装配遗物：" + def.Id + "（模板 " +
                    (template.relicInfo != null ? template.relicInfo.relicName : "?") + "）");
                return so;
            }
            catch (Exception ex)
            {
                result.Errors.Add(def.Id + "：装配异常 —— " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        // ---------------------------------------------------------------------
        // 关键词
        // ---------------------------------------------------------------------

        /// <summary>
        /// 核验一个关键词的类是否存在。
        /// </summary>
        /// <remarks>
        /// 关键词与卡/遗物**共用同一个游戏工厂**；但游戏的查找带兜底：
        /// <code>
        /// Activator.CreateInstance(
        ///     Type.GetType("Keyword.CardKeyword" + name) ?? Type.GetType("Keyword.CardKeyword"))
        /// </code>
        /// —— 找不到就**静默退化成基类**（不报错、行为丢失）。现役 mod 的注释里明确记着这个坑。
        /// <para>
        /// 平台的对策：在装配期主动问注册表"这个关键词的类存不存在"，
        /// 不存在就报错 —— 而不是让作者进游戏才发现"关键词没效果"。
        /// </para>
        /// <para>
        /// 注意：这里**不构造实例**。构造需要 <c>CardBase</c>
        /// （游戏的 <c>GenerateKeywordFromKeywordInfo(info, card)</c> 签名要求），
        /// 装配期拿不到卡实例。核验类存在性已足以拦住"静默退化"这一最危险的失败模式。
        /// </para>
        /// </remarks>
        public bool AssembleKeyword(ModKit.Api.KeywordDef def, ContentRegistry registry, AssemblyResult result)
        {
            if (def == null) { throw new ArgumentNullException("def"); }
            if (result == null) { result = new AssemblyResult(); }

            try
            {
                if (string.IsNullOrEmpty(def.Id))
                {
                    result.Errors.Add("(关键词)：缺少 id。");
                    return false;
                }

                if (registry == null)
                {
                    // 没有注册表 = 桥没装好。这是"内容一定会静默失效"的状态，必须报错。
                    result.Errors.Add(def.Id + "：内容注册表不可用（Transpiler 桥未安装），关键词无法核验。");
                    return false;
                }

                // 注册表按类名索引：CardKeyword<名>。名字可能带 mod 前缀
                // （"kwmod.tenacious"），所以取最后一段并转 PascalCase：
                //   tenacious → CardKeywordTenacious
                // 这条派生规则与 ContentValidator 给出的提示一致（同一约定，两处都有测试覆盖）。
                string leaf = def.Id;
                int dot = def.Id.LastIndexOf('.');
                if (dot >= 0 && dot < def.Id.Length - 1) { leaf = def.Id.Substring(dot + 1); }

                string pascal = ToPascal(leaf);
                string byClassName = "CardKeyword" + pascal;

                if (registry.Resolve(byClassName) == null &&
                    registry.Resolve("CardKeyword" + def.Id) == null)
                {
                    result.Errors.Add(def.Id +
                        "：找不到实现类。游戏按 Keyword." + byClassName + " 查找（平台跨程序集解析），" +
                        "找不到时会**静默退化成基类**（不报错，但关键词没有任何行为）。" +
                        "请提供一个名为 " + byClassName + " 的类（继承 CardKeyword，需 public 无参构造）。");
                    return false;
                }

                result.VerifiedKeywords.Add(def.Id);
                _log.Info("已核验关键词：" + def.Id + "（类 " + byClassName + " 存在）");
                return true;
            }
            catch (Exception ex)
            {
                result.Errors.Add(def.Id + "：核验异常 —— " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 下划线小写名 → PascalCase（与 ContentValidator 的提示、注册表的归一化同一条规则）。
        /// 实现在 <see cref="ModKit.Core.ConventionNames"/> —— 三处共用一份，规则分叉过一次就够了。
        /// </summary>
        private static string ToPascal(string id)
        {
            return ModKit.Core.ConventionNames.ToPascal(id);
        }

        // ---------------------------------------------------------------------
        // 枚举映射（显式 switch，不用强制转换）
        // ---------------------------------------------------------------------

        private static Rarity RarityToGame(string name)
        {
            var r = ModKit.Core.RarityNames.Resolve(name);
            if (!r.Mapped) { return Rarity.Normal; }
            switch (r.Value)
            {
                case ApiRarity.Common: return Rarity.Normal;
                case ApiRarity.Uncommon: return Rarity.Uncommon;
                case ApiRarity.Rare: return Rarity.Rare;
                default: return Rarity.Normal;
            }
        }

        private static CardEffectResolveType ResolveTimingToGame(string name)
        {
            var t = ModKit.Core.ResolveTimingNames.Resolve(name);
            if (!t.Mapped) { return CardEffectResolveType.Cast; }
            switch (t.Value)
            {
                case ApiResolveTiming.OnDraw: return CardEffectResolveType.UponDraw;
                case ApiResolveTiming.OnDiscard: return CardEffectResolveType.UponDiscard;
                case ApiResolveTiming.EndInHand: return CardEffectResolveType.EndInHand;
                default: return CardEffectResolveType.Cast;
            }
        }

        private static EffectTargetEnum SelectorToGame(string name, EffectTargetEnum fallback)
        {
            if (string.IsNullOrEmpty(name)) { return fallback; }
            var s = ModKit.Core.CombatantSelectorNames.Resolve(name);
            if (!s.Mapped) { return fallback; }
            switch (s.Value)
            {
                case ApiCombatantSelector.Opponent: return EffectTargetEnum.TargetOpponent;
                case ApiCombatantSelector.AllOpponents: return EffectTargetEnum.AllOpponents;
                case ApiCombatantSelector.All: return EffectTargetEnum.All;
                case ApiCombatantSelector.RandomOpponent: return EffectTargetEnum.RandomOpponent;
                default: return EffectTargetEnum.Self;
            }
        }

        /// <summary>颜色 → 游戏的单字法力求缩写。</summary>
        private static string ColorToManaAbbr(string colorName)
        {
            var c = ModKit.Core.ManaColorNames.Resolve(colorName);
            if (!c.Mapped) { return "V"; }
            switch (c.Value)
            {
                case ApiManaColor.Purple: return "P";
                case ApiManaColor.Red: return "R";
                case ApiManaColor.Green: return "G";
                case ApiManaColor.Blue: return "B";
                case ApiManaColor.Black: return "K";
                default: return "V";
            }
        }
    }

    /// <summary>给 ApiCardDef 的小工具（ModKit.Api 的类型不应承载平台策略，故放这里）。</summary>
    internal static class CardDefExtensions
    {
        /// <summary>未显式声明时默认进池。</summary>
        internal static bool AddToPoolOrDefault(this ApiCardDef def)
        {
            return def != null;
        }
    }
}
