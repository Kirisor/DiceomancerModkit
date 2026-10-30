// =============================================================================
//  ModKit.Core · 触发点「附加字段」名表与取值表（Tier 1，2026-10-14）
//
//  ★ 为什么要有这个文件：
//    `TriggerTypeNames.NeedsExtraFields` 只回答"需要附加字段"，而校验器要能
//    **点名作者该写哪个 JSON 字段**，装配层要知道该往 `TriggerTargetInfo` 的
//    哪个成员上写。三件事需要同一张表，所以集中在这里。
//
//  ★ 游戏侧字段 → 平台 JSON 字段的**改名**只有两处，必须写死在这里：
//      battlePhase          → battlePhase        （同名）
//      certainTurnStartInfo → certainTurn        （改名，见 Api RelicDef.CertainTurn）
//      relativeCharacterType→ relativeCharacter  （改名，见 Api RelicDef.RelativeCharacter）
//      extraString          → extraString        （同名）
//      colorE               → colorE             （同名）
//
//  ★ 「谁读哪个字段」的判据是**游戏反编译原文**
//    `Trigger/TriggerManager.cs:70-185` 的 `CheckTriggerFitTargetTrigger`
//    大 switch，逐条抄录如下（只列与这 5 个字段相关的）：
//      · ChangeBattlePhase   : relativeCharacterType（:92-96）+ battlePhase（:97-100）
//      · CharacterRollDice   : relativeCharacterType（:110-120）
//      · WholeNewTurnStart   : certainTurnStartInfo（:121-132，class 为 null 会崩）
//      · EnemyHealthBarBreak : relativeCharacterType（:146-156）
//      · ManaConsumed        : colorE（:157-158）
//      · CharacterCheck      : relativeCharacterType（:159-167）+ extraString（:172）
//      · CardKeywordTrigger / AbilityStandardTrigger / TakeRest : extraString（:178-181）
//    写了触发点**不读**的字段不会崩，但会掩盖作者笔误 ⇒ 校验器警告、装配层不写。
//
//  ★ 本文件不引用任何游戏类型（ModKit.Core 的既有纪律）：枚举取值以**字符串**表达，
//    由 ModKit.Game 侧在装配时转成真正的游戏枚举。
// =============================================================================

using System;
using System.Collections.Generic;

namespace ModKit.Core
{
    /// <summary>遗物触发点附加字段的 JSON 名（对应 Api <c>RelicDef</c> 的 5 个属性）。</summary>
    public static class TriggerFieldNames
    {
        public const string BattlePhase = "battlePhase";
        public const string CertainTurn = "certainTurn";
        public const string RelativeCharacter = "relativeCharacter";
        public const string ExtraString = "extraString";
        public const string ColorE = "colorE";

        /// <summary>触发点的卡牌过滤（2026-10-30 暴露；游戏字段 <c>cardCoreInfoCheck</c>）。</summary>
        public const string CardRequirement = "cardRequirement";

        private static readonly string[] All = new string[]
        {
            "battlePhase", "certainTurn", "relativeCharacter", "extraString", "colorE", "cardRequirement"
        };

        /// <summary>平台已暴露的全部附加字段 JSON 名（顺序固定，供诊断稳定输出）。</summary>
        public static IReadOnlyList<string> AllNames { get { return new List<string>(All); } }

        private static readonly Dictionary<string, string> GameToJson =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "battlePhase",           BattlePhase },
            { "certainTurnStartInfo",  CertainTurn },
            { "relativeCharacterType", RelativeCharacter },
            { "extraString",           ExtraString },
            { "colorE",                ColorE },
            { "cardCoreInfoCheck",     CardRequirement }
        };

        private static readonly Dictionary<string, string> JsonToGame =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { BattlePhase,           "battlePhase" },
            { CertainTurn,           "certainTurnStartInfo" },
            { RelativeCharacter,     "relativeCharacterType" },
            { ExtraString,           "extraString" },
            { ColorE,                "colorE" },
            { CardRequirement,       "cardCoreInfoCheck" }
        };

        /// <summary>
        /// 游戏 <c>TriggerTargetInfo</c> 上的字段名 → 平台 JSON 字段名。
        /// 平台仍未暴露的（<c>SpecialExtraCondition</c>）返回 <c>null</c>。
        /// </summary>
        public static string JsonNameOf(string gameFieldName)
        {
            string json;
            if (!string.IsNullOrEmpty(gameFieldName) && GameToJson.TryGetValue(gameFieldName, out json))
            {
                return json;
            }
            return null;
        }

        /// <summary>平台 JSON 字段名 → 游戏字段名（诊断里要两个都点出来）。</summary>
        public static string GameNameOf(string jsonFieldName)
        {
            string game;
            if (!string.IsNullOrEmpty(jsonFieldName) && JsonToGame.TryGetValue(jsonFieldName, out game))
            {
                return game;
            }
            return null;
        }

        /// <summary>
        /// 该字段是否属于"游戏侧**裸解引用**的两个 class 字段"（方法名沿用历史叫法：
        /// 2026-10-14 平台兜底后**缺字段已不再崩游戏**，见 remarks）。
        /// 其余是"不给会**静默失效**"。
        /// </summary>
        /// <remarks>
        /// ★ 校验器据此分流严重级别（2026-10-14 第二轮）：返回 <c>true</c> 的字段缺失时报
        /// <b>Error</b>（lint 拒绝、该文件在游戏里不会被加载），其余缺失时仍报 <b>Warn</b>。
        /// 判据出处：<c>Trigger/TriggerManager.cs:97-100</c>（battlePhase）与
        /// <c>:121-132</c>（certainTurnStartInfo）是这条 switch 里仅有的两处**裸解引用**
        /// —— 这是**游戏侧**的硬性不变量（原版 288/288 个 target 两者全非 null）。
        /// ★ 2026-10-14 第三轮（口径校准）：装配层现在会给这两个字段补中性默认值
        /// （<c>ModKit.Game/ContentAssembler.cs:407-418</c>）+ 装配期自检 ⇒ 缺字段**不会崩**；
        /// Error 保留是为了**不让作者带着"条件被固定成默认值"的配置进游戏**。
        /// </remarks>
        public static bool MissingCrashes(string jsonFieldName)
        {
            return jsonFieldName == BattlePhase || jsonFieldName == CertainTurn;
        }

        private static readonly Dictionary<string, string[]> ConsumedByTrigger =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "ChangeBattlePhase",   new string[] { BattlePhase, RelativeCharacter } },
            { "WholeNewTurnStart",   new string[] { CertainTurn } },
            { "CharacterRollDice",   new string[] { RelativeCharacter } },
            { "EnemyHealthBarBreak", new string[] { RelativeCharacter } },
            { "CharacterCheck",      new string[] { RelativeCharacter, ExtraString } },
            { "CardKeywordTrigger",  new string[] { ExtraString } },
            { "AbilityStandardTrigger", new string[] { ExtraString } },
            { "TakeRest",            new string[] { ExtraString } },
            { "ManaConsumed",        new string[] { ColorE } },
            // ★ 2026-10-30：这五个触发点按**卡牌条件**过滤（TriggerManager.cs:116-121 →
            //   CardUtils.CheckCardCoreInfoFitCheck），读的就是 cardCoreInfoCheck。
            { "CastCard",            new string[] { CardRequirement } },
            { "CastCardFinished",    new string[] { CardRequirement } },
            { "DrawCard",            new string[] { CardRequirement } },
            { "DiscardCard",         new string[] { CardRequirement } },
            { "ExertCard",           new string[] { CardRequirement } }
        };

        /// <summary>
        /// 该触发点**是否读取**这个 JSON 字段（判据 = 游戏 <c>CheckTriggerFitTargetTrigger</c>）。
        /// 返回 <c>false</c> 的字段即使写了也会被游戏忽略。
        /// </summary>
        public static bool Consumes(string triggerType, string jsonFieldName)
        {
            string[] fields;
            if (string.IsNullOrEmpty(triggerType) || string.IsNullOrEmpty(jsonFieldName)) { return false; }
            if (!ConsumedByTrigger.TryGetValue(triggerType, out fields)) { return false; }
            for (int i = 0; i < fields.Length; i++)
            {
                if (string.Equals(fields[i], jsonFieldName, StringComparison.Ordinal)) { return true; }
            }
            return false;
        }

        /// <summary>该触发点读取的全部 JSON 字段名（供"字段被忽略"诊断反查）。</summary>
        public static IReadOnlyList<string> FieldsConsumedBy(string triggerType)
        {
            string[] fields;
            if (!string.IsNullOrEmpty(triggerType) && ConsumedByTrigger.TryGetValue(triggerType, out fields))
            {
                return new List<string>(fields);
            }
            return new List<string>();
        }
    }

    /// <summary>
    /// 触发点附加字段用到的 4 张游戏枚举名表。
    /// </summary>
    /// <remarks>
    /// ★ 取值全部**逐条抄自游戏反编译源码**，不是猜的，也不是照抄任务书示例：
    /// <list type="bullet">
    /// <item><c>BattlePhase</c> —— <c>Combat.BattleWorld/BattlePhase.cs:3-9</c>：
    /// <c>Upkeep, Main, End, CleanUp</c>。</item>
    /// <item><c>CertainTurnRelationInfo</c> —— <c>Combat.BattleWorld/CertainTurnRelationInfo.cs:3-8</c>：
    /// <c>Before, Exactly, After</c>。</item>
    /// <item><c>RelativeCharacterType</c> —— <c>Combat.BattleWorld/RelativeCharacterType.cs:6-11</c>：
    /// <c>Owner, Opponents, All</c>（<b>Opponents 是复数</b>）。</item>
    /// <item><c>ColorE</c> —— <c>Card/ColorE.cs:3-11</c>：
    /// <c>Red, Green, Blue, Purple, Void, Black</c>。</item>
    /// </list>
    /// 这几张表与 <see cref="TriggerTypeNames"/> 用同一套解析口径（大小写不敏感 +
    /// 拼错给相近建议），诊断风格保持一致。
    /// </remarks>
    public static class TriggerFieldValues
    {
        private static readonly string[] BattlePhaseAll = new string[]
        {
            "Upkeep", "Main", "End", "CleanUp"
        };

        private static readonly string[] CertainTurnRelationAll = new string[]
        {
            "Before", "Exactly", "After"
        };

        private static readonly string[] RelativeCharacterAll = new string[]
        {
            "Owner", "Opponents", "All"
        };

        private static readonly string[] ManaColorAll = new string[]
        {
            "Red", "Green", "Blue", "Purple", "Void", "Black"
        };

        /// <summary>游戏 <c>BattlePhase</c> 的合法值。</summary>
        public static IReadOnlyList<string> BattlePhaseNames { get { return new List<string>(BattlePhaseAll); } }

        /// <summary>游戏 <c>CertainTurnRelationInfo</c> 的合法值。</summary>
        public static IReadOnlyList<string> CertainTurnRelationNames { get { return new List<string>(CertainTurnRelationAll); } }

        /// <summary>游戏 <c>RelativeCharacterType</c> 的合法值。</summary>
        public static IReadOnlyList<string> RelativeCharacterNames { get { return new List<string>(RelativeCharacterAll); } }

        /// <summary>游戏 <c>ColorE</c> 的合法值。</summary>
        public static IReadOnlyList<string> ManaColorNames { get { return new List<string>(ManaColorAll); } }

        /// <summary>解析 <c>battlePhase.phase</c>。</summary>
        public static LookupResult<string> ResolveBattlePhase(string name) { return Resolve(name, BattlePhaseAll); }

        /// <summary>解析 <c>certainTurn.relation</c>。</summary>
        public static LookupResult<string> ResolveCertainTurnRelation(string name) { return Resolve(name, CertainTurnRelationAll); }

        /// <summary>解析 <c>relativeCharacter</c>。</summary>
        public static LookupResult<string> ResolveRelativeCharacter(string name) { return Resolve(name, RelativeCharacterAll); }

        /// <summary>解析 <c>colorE</c>。</summary>
        public static LookupResult<string> ResolveManaColor(string name) { return Resolve(name, ManaColorAll); }

        /// <summary>大小写不敏感解析；拼错时用与触发器名表同一套"相近建议"。</summary>
        private static LookupResult<string> Resolve(string name, string[] all)
        {
            if (!string.IsNullOrEmpty(name))
            {
                for (int i = 0; i < all.Length; i++)
                {
                    if (string.Equals(all[i], name, StringComparison.OrdinalIgnoreCase))
                    {
                        return new LookupResult<string>(true, all[i], name, null);
                    }
                }
            }

            var candidates = new List<string>(all);
            string hint = BuiltinEffectNames.Closest(name, candidates);
            return new LookupResult<string>(false, null, null, hint);
        }

        /// <summary>把合法值拼成诊断文案里的一段（"合法值：A / B / C"）。</summary>
        public static string Describe(IReadOnlyList<string> values)
        {
            return string.Join(" / ", new List<string>(values).ToArray());
        }
    }
}
