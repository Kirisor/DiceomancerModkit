// =============================================================================
//  ModKit.Game · 效果装配（EffectInfo）—— ContentAssembler 的 partial 分片
//
//  ★ 来历：本文件是 2026-10-13 第二批按子域拆分 ContentAssembler.cs 时切出的，
//    全部内容**逐字节照搬**原文（含 XML 注释原文），只新增本文件头与
//    using / namespace / class 外壳，未改任何逻辑。
//
//  ★ 负责子域：BuildEffectInfo —— 把 ApiEffectSpec 逐个语义构造成游戏 EffectInfo。
//
//  ★ 不在本文件：枚举映射（SelectorToGame / ColorToManaAbbr 等）跨子域共用，留在主文件。
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
        // ---------------------------------------------------------------------
        // EffectInfo —— 逐个语义构造
        // ---------------------------------------------------------------------

        private EffectInfo BuildEffectInfo(ApiEffectSpec spec, CardEffectResolveType resolveType,
            string ownerId, AssemblyResult result)
        {
            string builtinName = spec.Builtin;
            string customId = spec.Custom;

            var info = new EffectInfo
            {
                effectTargetEnum = SelectorToGame(spec.Target, EffectTargetEnum.Self),
                isPositive = true,          // 默认正向；负面效果后续通过语义名细化
                extraDelay = 0,
                cardEffectResolveType = resolveType,
                effectNumberInfos = BuildNumberInfos(spec)
            };

            if (!string.IsNullOrEmpty(customId))
            {
                // 自定义效果走 SpecialEffect 家族：
                //   类名必须是 EffectSpEffect<customId>，命名空间 Effects.Special.CardEffect
                //   （由 Transpiler 桥把 Type.GetType 换成平台解析器，使其能被找到）
                info.effectName = "SpecialEffect";
                info.specialEffectString = customId;
                return info;
            }

            var lookup = ModKit.Core.BuiltinEffectNames.Resolve(builtinName);
            if (!lookup.Mapped)
            {
                result.Errors.Add(ownerId + "：未知内置语义 " + builtinName + "（校验器应已拦截）。");
                return null;
            }

            switch (lookup.Value)
            {
                case ApiBuiltinEffect.DealDamage:
                    // 直接伤害 vs 间接伤害跳过命中判定与调整器 —— 这是玩法强度差异，
                    // 所以契约要求作者必须显式写 mode（平台不给默认值）。
                    info.effectName = "DealDamage";
                    info.isPositive = false;
                    break;

                case ApiBuiltinEffect.LoseLife:
                    info.effectName = "LoseLife";
                    info.isPositive = false;
                    break;

                case ApiBuiltinEffect.Heal:
                    info.effectName = "Heal";
                    break;

                case ApiBuiltinEffect.Block:
                    info.effectName = "Block";
                    break;

                case ApiBuiltinEffect.Draw:
                    // ★ 必须保留 rNumber（2026-10-11 实机证伪）：
                    //   游戏 EffectUtils.GetEffectRNumberCountByName 的 switch 里，
                    //   返回 0 的是 DiscardHand / AddMana / ApplyPrimAbility / DoNothing /
                    //   DiscardOneRandomCard / **DrawOneCard**；**"Draw" 落到 default → 需要 1 个**。
                    //   而 EffectDraw.ApplyEffectForTarget 里就一行
                    //       int n = rNumbers[0].GetCollapsedNumber();
                    //   给 0 个 → IndexOutOfRangeException；描述模板 抽{Number}张牌。 也解析不了。
                    //   之前误按 "Draw 需要 0 个" 写，两处报错由此而来。
                    info.effectName = "Draw";
                    break;

                case ApiBuiltinEffect.DrawOne:
                    // DrawOneCard 确实需要 0 个（EffectDrawOneCard 内部写死 DrawCards(1)）
                    info.effectName = "DrawOneCard";
                    info.effectNumberInfos = new List<RNumberInfo>();
                    break;

                case ApiBuiltinEffect.DiscardRandom:
                    info.effectName = "DiscardRandomCards";
                    break;

                case ApiBuiltinEffect.GainMana:
                    // 单字符编码：P=Purple R=Red G=Green B=Blue V=Void K=Black
                    info.effectName = "AddMana";
                    info.manaString = ColorToManaAbbr(spec.Color);
                    info.effectNumberInfos = new List<RNumberInfo>();
                    break;

                case ApiBuiltinEffect.CleanMana:
                    // ★★ 2026-10-13 **二次**实机证伪后的最终口径 —— 请勿改回 effectName。
                    //   第一阶段：switch 里没有 case ⇒ 落 default ⇒ 装配静默失败 ⇒ 牌库空白卡。
                    //   第二阶段（补了 case 之后）：写成 effectName = "CleanMana" 仍然崩 ——
                    //     游戏 27 个普通效果类里**没有 EffectCleanMana**（全库 grep "CleanMana"
                    //     零命中，只有 ManaManager.CleanAllMana() 这个方法，且它只被
                    //     战斗结束的 BattleManager.cs:245 调用）。于是 EffectUtils 拼出
                    //     "Effects.EffectCleanMana" ⇒ Type.GetType 返回 null ⇒
                    //     Activator.CreateInstance(null) 抛
                    //     `ArgumentNullException: Value cannot be null. Parameter name: type`
                    //     （实机：fx_clean_mana 发卡失败，实发 8/10）。
                    //   ⇒ 正确做法：走**特殊效果**通道，由平台自建的
                    //     Effects.Special.CardEffect.EffectSpEffectCleanMana 落地
                    //     （内部 new SubCommandRemoveMana(ManaManager.GetManaArray())，
                    //      与原版 EffectSpEffectRemoveAllManaAndDamage 同款）。
                    //     该类在 ModKit.Game.dll 里，靠 Transpiler 桥跨程序集解析。
                    //   ⇒ 卡面文案键 Key_CleanMana 由 PlatformRuntime.AddBuiltinEffectDescriptions
                    //     补进 EffectDescription 表（游戏自带表里没有这个键）。
                    //   ⇒ 一般化教训：**effectName 必须是游戏侧真实存在的类名**，
                    //     "四处登记齐全"不等于"目标类存在"——加内置效果时要同时核实类。
                    info.effectName = "SpecialEffect";
                    info.specialEffectString = "CleanMana";
                    info.effectNumberInfos = new List<RNumberInfo>();
                    break;

                case ApiBuiltinEffect.EndTurn:
                    // ★★ 2026-10-13 二次实机证伪后的最终口径 —— 同样不能写 effectName。
                    //   游戏里**没有 Effects.EffectEndTurn**：EndTurn 在原版**只以特殊效果存在**
                    //   （Effects.Special.CardEffect.EffectSpEffectEndTurn，原版卡 `Dead` 在用，
                    //    见 [fxuse][特殊] EndTurn x4）。它的实现就是
                    //     `new CommandEndTurn(fromPlayerStandardEndTurn: false)`。
                    //   ⇒ 必须写 SpecialEffect + specialEffectString="EndTurn"。
                    //   ⇒ 这条不需要平台补文案：描述键 Key_EndTurn 是游戏自带的（Dead 在用）。
                    //   ⇒ 行为细节（源码实证）：`fromPlayerStandardEndTurn:false` 能**越过 busy 判定**，
                    //     但命令队列里已有 CommandEndTurn 时，新命令会被**静默丢弃**
                    //     （Command/CommandEndTurn.cs:19-25）—— 所以"卡牌强制结束回合"与玩家
                    //     点空格在同一 tick 竞争时可能没有效果，这是游戏行为，平台不额外承诺。
                    info.effectName = "SpecialEffect";
                    info.specialEffectString = "EndTurn";
                    info.effectNumberInfos = new List<RNumberInfo>();
                    break;

                case ApiBuiltinEffect.SetMaxMana:
                    info.effectName = "ChangeMaxManaCount";
                    break;

                case ApiBuiltinEffect.ApplyState:
                    info.effectName = "AlterSpecialState";
                    info.additionalString = spec.Name;   // 状态名
                    break;

                case ApiBuiltinEffect.GainAbility:
                    info.effectName = "ApplyAbility";
                    info.abilityName = ModKit.Core.DeclaredAbilities.Resolve(spec.Name);   // 短名 → mod 全名
                    break;

                case ApiBuiltinEffect.AddRandomCard:
                    // 随机加卡：读取 rNumbers[0]（加几张）+ genRandomCardInfoPack（卡池）。
                    // ★ 卡池必须显式构造：游戏读 pack.genRandomCardInfoWithProbability，
                    //   且 GetCardInfoPreview 也解引用它 ⇒ 空对象会在描述/预览阶段 NRE。
                    info.effectName = "AddRandomCard";
                    info.genRandomCardInfoPack = BuildCardPool(spec.Pool, ownerId, result, "add_random_card");
                    break;

                case ApiBuiltinEffect.ApplyPrimAbility:
                    // ★ 与 gain_ability 一样必须写 abilityName —— 游戏侧读的是
                    //   `abilitySo.abilityInfo`，而 abilitySo 靠这个名字解析（不写 ⇒ null ⇒ NRE）。
                    // ★★ 2026-10-13 用户实机崩溃后改走**平台自建特殊效果**：
                    //   原版 `EffectApplyPrimAbility` 把卡上的 rNumbers 原样传给
                    //   `AbilityUtil.GenerateAbility`，而后者按
                    //     `array = abilitySo.abilityInfo.effectInfo.effectNumberInfos`（能力自己的参数槽）
                    //     `for (i < rNumbers.Length) array[i] = ...`
                    //   做替换 —— **卡上的数字个数超过能力槽数就 IndexOutOfRangeException**
                    //   （实机栈：AbilityUtil.GenerateAbility ← Character.GainAbility ←
                    //     SubCommandApplyAbility.ResolveSubCommand）。
                    //   平台自建的 `EffectSpEffectApplyPrimAbility` 在运行期按目标的真实槽数夹紧，
                    //   并把"能力找不到"从 NRE 降级为一条 Error 日志。
                    info.effectName = "SpecialEffect";
                    info.specialEffectString = "ApplyPrimAbility";
                    info.abilityName = ModKit.Core.DeclaredAbilities.Resolve(spec.Name);   // 短名 → mod 全名
                    break;

                case ApiBuiltinEffect.ApplyUtilAbility:
                    // 同上：原版 `EffectApplyUtilAbility` 也读 `abilitySo.abilityInfo` 且不做长度检查
                    //   ⇒ 换成平台自建的 `EffectSpEffectApplyUtilAbility`（行为等价 + 数字夹紧）。
                    info.effectName = "SpecialEffect";
                    info.specialEffectString = "ApplyUtilAbility";
                    info.abilityName = ModKit.Core.DeclaredAbilities.Resolve(spec.Name);   // 短名 → mod 全名
                    break;

                case ApiBuiltinEffect.ChangeMaxHealth:
                    info.effectName = "ChangeMaxHealth";
                    break;

                case ApiBuiltinEffect.DiscardHand:
                    // 该效果不读 rNumbers（EffectDiscardHand 内部自己遍历手牌）
                    info.effectName = "DiscardHand";
                    info.effectNumberInfos = new List<RNumberInfo>();
                    break;

                case ApiBuiltinEffect.DiscardOneRandomCard:
                    // 同上，也不读 rNumbers
                    info.effectName = "DiscardOneRandomCard";
                    info.effectNumberInfos = new List<RNumberInfo>();
                    break;

                case ApiBuiltinEffect.ImproviseCard:
                    // 探寻（原版卡「不稳定传送门 UnstablePortal」用的就是它）。
                    // ★ 数字语义与别处不同：rNumbers[0] = **能挑几张**；
                    //   **候选张数由游戏规则值决定**（RuleManager.GetCurrentValueOfRule("ImproviseCard")，
                    //   默认 3，+ ExtraCardsWhenImprovise 策略）—— 卡上不写。
                    // ★ 卡池必须显式构造：游戏读 GenRandomCardInfoPack.genRandomCardInfoWithProbability
                    //   抽候选，且卡面描述（EffectBase.AddRandomCardInfoDesStringToLocalizedString
                    //   读 packName）与 GetCardInfoPreview 都解引用它 ⇒ 空对象在描述/预览阶段就 NRE。
                    // ★ 复用 add_random_card 的同一个构造器（BuildCardPool），不另写一份。
                    // ⚠️ 与下面 UpgradeCard 的 `effectName = "ImproviseCard"` 无关：那是既有的
                    //   （可疑的）映射，本轮**不动**它，只在这里给出正确的探寻语义。
                    info.effectName = "ImproviseCard";
                    info.genRandomCardInfoPack = BuildCardPool(spec.Pool, ownerId, result, "improvise_card");
                    break;

                case ApiBuiltinEffect.SeekCard:
                    // 检索：读取 rNumbers[0] 当"检索几张"，筛选条件全在 cardCoreInfoCheck。
                    // ★ 条件对象必须显式构造：CardCoreInfoCheck 是 struct，默认值 = 不加任何限制
                    //   ⇒ 只写 effectName 会静默变成"检索任意牌"（校验器已强制 require 必填）。
                    info.effectName = "SeekCard";
                    info.cardCoreInfoCheck = BuildCardCoreInfoCheck(spec.Require);
                    break;

                case ApiBuiltinEffect.NumberPlusSub:
                    // 用 isPositive 决定加还是减 —— 作者要写对 mode（见手册「直接/间接」那套约定）
                    info.effectName = "NumberPlusSub";
                    // ★ 必须给数字改造配置：EffectNumberPlusSub.GetEffectDesKey() 直接解引用它
                    //   （读 isChangePermanent / canBeDiceNumber）⇒ 为 null 时**卡面描述那一行 NRE**。
                    info.setterRNumberConfig = BuildNumberSetter(spec.Setter);
                    break;

                case ApiBuiltinEffect.Recover:
                    info.effectName = "Recover";
                    break;

                case ApiBuiltinEffect.SetNumber:
                    info.effectName = "SetNumber";
                    // ★ 同上：EffectSetNumber.GetEffectDesKey() 也直接解引用 SetterRNumberConfig。
                    info.setterRNumberConfig = BuildNumberSetter(spec.Setter);
                    break;

                case ApiBuiltinEffect.AddCard:
                    info.effectName = "AddCard";
                    // ★ 目标卡名必须补 mod 前缀，否则游戏按全名查不到 ⇒ 加进来的是「空白卡牌」。
                    info.cardName = QualifyCardName(spec.Name, ownerId);
                    break;

                case ApiBuiltinEffect.AddTemporaryCard:
                    info.effectName = "AddCard";
                    info.cardName = QualifyCardName(spec.Name, ownerId);
                    // 游戏读 additionalString 给这张卡挂上对应关键词（"Temporary" = 临时）
                    info.additionalString = "Temporary";
                    break;

                case ApiBuiltinEffect.UpgradeCard:
                    info.effectName = "ImproviseCard";
                    break;

                default:
                    result.Errors.Add(ownerId + "：内置语义 " + builtinName + " 尚无装配实现（校验器档位与实际实现不一致）。");
                    return null;
            }

            return info;
        }

    }
}
