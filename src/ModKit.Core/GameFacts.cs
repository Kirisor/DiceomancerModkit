// =============================================================================
//  ModKit.Core · 游戏事实表（Game-derived facts）
//
//  这里放"从游戏反编译产物里核实出来、且 Api/Core/Game/Tools 都需要"的事实。
//
//  ★ 为什么要单独一个文件（而不是写在 Game 层）：
//    Tools（lint）必须能在**没有游戏、没有 Unity** 的机器上跑，
//    所以它不能引用 Game 层。而触发点名表是校验器要用的事实 ——
//    放 Game 层会逼 Tools 拖入 Unity 依赖；两处各存一份又会漂移。
//    归到 Core 是唯一没有副作用的做法。
//
//  ★ 数据来源：全库扫描 `new TriggerBase("…")` 的实测去重结果，
//    **不是**游戏的 TriggerTargetInfo.AllTriggerType 枚举。
//    两者实测不一致（枚举里有的从不发出，发出的有的不在枚举里）——
//    用枚举做校验会让作者"写了个永远不触发的钩子"却毫无提示。
// =============================================================================

using System;
using System.Collections.Generic;

namespace ModKit.Core
{
    /// <summary>已核实的游戏事实。</summary>
    public static class GameFacts
    {
        /// <summary>
        /// 游戏**实际发出**的触发点名（32 项，全库扫描 `new TriggerBase` 去重）。
        /// </summary>
        /// <remarks>
        /// 与 <c>TriggerTargetInfo.AllTriggerType</c> 枚举的差异（实测）：
        /// <list type="bullet">
        /// <item>在枚举里但从不发出：<c>ResolveCard</c> / <c>ResolveCardFinished</c> /
        /// <c>DiscardCard</c> / <c>Passive</c> / <c>OwnerCardRelated</c> / <c>Special</c>；</item>
        /// <item>被发出但不在枚举里：<c>HandCardCountChange</c> / <c>AcquireRelic</c> /
        /// <c>RelicActive</c> / <c>RelicCorrupted</c> / <c>CardEffectNoCast</c> / <c>SummonResolve</c>。</item>
        /// </list>
        /// </remarks>
        public static readonly string[] ActualTriggerNames =
        {
            "AbilityStandardTrigger", "AcquireRelic", "BattleStart", "BeenAttacked", "BuyInStore",
            "CardEffectNoCast", "CardKeywordTrigger", "CastCard", "CastCardFinished", "ChangeBattlePhase",
            "ChangeWorldPhase", "CharacterCheck", "CharacterLoseLife", "CharacterRollDice", "DamageBeenDealt",
            "DrawCard", "EnemyDieStart", "EnemyHealthBarBreak", "EnterUnvisitedNode", "ExertCard",
            "HandCardCountChange", "ManaConsumed", "ManaOverflow", "OrganismPerish", "RelicActive",
            "RelicCorrupted", "ShuffleGraveToLibrary", "SummonResolve", "TakeRest", "TimePass",
            "UseTheOneDice", "WholeNewTurnStart"
        };

        /// <summary>以 IReadOnlyList 形式返回触发点名（方便消费方统一处理）。</summary>
        public static IReadOnlyList<string> TriggerNameList
        {
            get { return new List<string>(ActualTriggerNames); }
        }

        /// <summary>
        /// 游戏里**真实存在**的本地化表名（27 个，全库扫描
        /// <c>GetLocalizedString(…)</c> / <c>new LocalizedString(…)</c> 去重）。
        /// </summary>
        /// <remarks>
        /// 为什么需要这份清单：作者写了不存在的表名时，游戏只会"取表失败 → 跳过"，
        /// 表现是**文案不生效**（卡还能用，只是显示 key 名）。而表名是纯字符串，
        /// 没有任何编译期检查 —— 所以平台要替他查。
        /// <para>
        /// 这份清单是**实测得来**的：例如很容易想当然写出的
        /// <c>RelicDescription</c> **并不存在** —— 遗物描述是
        /// <c>RelicBase.GetEffectDescription()</c> 程序化生成的，不走本地化表。
        /// </para>
        /// </remarks>
        public static readonly string[] LocalizationTableNames =
        {
            "Ability", "AbilityDescription", "Ascension", "CardName", "CardRequirementDes",
            "CareerUpgrade", "Command", "Dialogue", "DiceRelated", "EffectDescription",
            "EffectDescriptionAbbreviation", "Encyclopedia", "EnemyInfo", "EnemyIntention",
            "FlavorText", "Helper", "KeyWord", "PickCardConfigure", "Relic", "SpecialState",
            "Story", "StoryMarker", "TachiE Dialogue", "Tooltips", "Trigger", "Tutorials", "UI"
        };

        /// <summary>以 IReadOnlyList 形式返回已核实的本地化表名。</summary>
        public static IReadOnlyList<string> TableNameList
        {
            get { return new List<string>(LocalizationTableNames); }
        }

        /// <summary>表名是否在已核实清单里（大小写不敏感 —— 游戏按名精确取，但作者写错大小写也该被指出来）。</summary>
        public static bool IsKnownTableName(string name)
        {
            if (string.IsNullOrEmpty(name)) { return false; }
            for (int i = 0; i < LocalizationTableNames.Length; i++)
            {
                if (string.Equals(LocalizationTableNames[i], name, StringComparison.Ordinal)) { return true; }
            }
            return false;
        }

        /// <summary>
        /// 平台调用游戏内容工厂时，必须被 Transpiler 桥接管的 4 个方法。
        /// </summary>
        /// <remarks>
        /// 这份清单是"平台与游戏契约"的一部分：
        /// <c>Type.GetType(string)</c> 只搜调用方程序集，所以这 4 处必须替换，
        /// 否则 mod 的内容类找不到（静默失效或 NRE）。
        /// </remarks>
        public static readonly string[] RequiredBridgeTargets =
        {
            "Utilities.EffectUtils.GenerateEffectFromEffectInfo",
            "Keyword.CardKeyWordUtil.GenerateKeywordFromKeywordInfo",
            "Relic.RelicManager.GenerateRelicEffectFromRelicInfo",
            "Combat.Character.GenSpecialState"
        };
    }
}
