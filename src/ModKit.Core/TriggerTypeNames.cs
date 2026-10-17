// =============================================================================
//  ModKit.Core · 触发器名表（TriggerTargetInfo.AllTriggerType）
//
//  ★ 探针结论（2026-10-11，见 docs/ModKit-P2-三类探针报告.md）：
//    反编译 `Trigger/TriggerTargetInfo.cs` 给了权威清单：
//
//      public static string[] AllTriggerType = new string[33] { ... };
//      public string triggerType;          // ← 唯一必填的主字段
//      // 其余字段按触发类型选用：worldPhaseInfo / battlePhase /
//      // cardCoreInfoCheck / relativeCharacterType / certainTurnStartInfo /
//      // colorE / extraString / requireSuccess
//
//    **平台目前只暴露 triggerType**（其余字段留空 = 用游戏默认）。
//    这覆盖了"战斗开始时/回合结束时/出牌时/被攻击时"等最常用的一批，
//    但不覆盖需要附加字段的组合（例如 `ChangeBattlePhase` 需要 `battlePhase` 指定
//    是"回合开始"还是"行动结束"）。这一条已在手册里如实标注。
//
//  ★ 为什么要把这张表抄进平台：
//    作者的 JSON 里写 `"trigger": "BattleStart"`，平台要能在**lint 阶段**就告诉他
//    拼错了（而不是等游戏里安静地不触发）。这与 `type`/`owner` 的做法一致。
// =============================================================================

using System;
using System.Collections.Generic;

namespace ModKit.Core
{
    /// <summary>触发器类型名（游戏 <c>TriggerTargetInfo.AllTriggerType</c>）。</summary>
    public static class TriggerTypeNames
    {
        /// <summary>
        /// 游戏支持的全部触发类型。顺序与反编译原文一致。
        /// </summary>
        private static readonly string[] All = new string[]
        {
            "DrawCard", "CastCard", "CastCardFinished", "ResolveCard", "ResolveCardFinished",
            "DiscardCard", "ExertCard", "ShuffleGraveToLibrary",
            "BattleStart", "BattleEnd",
            "WholeNewTurnStart", "ChangeBattlePhase", "TimePass", "EnterUnvisitedNode",
            "EnemyHealthBarBreak", "EnemyDieStart", "ChangeWorldPhase", "CharacterRollDice",
            "Passive", "CharacterLoseLife", "DamageBeenDealt",
            "BeenAttacked", "OwnerCardRelated", "OrganismPerish", "ManaOverflow", "ManaConsumed",
            "CharacterCheck", "BuyInStore", "UseTheOneDice", "CardKeywordTrigger",
            "AbilityStandardTrigger", "TakeRest", "Special"
        };

        public static IReadOnlyList<string> AllNames { get { return new List<string>(All); } }

        /// <summary>解析触发类型名（大小写不敏感）。返回规范化后的拼法。</summary>
        public static LookupResult<string> Resolve(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                for (int i = 0; i < All.Length; i++)
                {
                    if (string.Equals(All[i], name, StringComparison.OrdinalIgnoreCase))
                    {
                        return new LookupResult<string>(true, All[i], name, null);
                    }
                }
            }

            string hint = BuiltinEffectNames.Closest(name, AllNames);
            return new LookupResult<string>(false, null, null, hint);
        }

        /// <summary>
        /// 只暴露 triggerType 也能用的那一批（不需要附加字段的）。
        /// </summary>
        /// <remarks>
        /// ★ 为什么要有这个白名单：有些触发类型**必须**配合附加字段才有意义
        /// （例如 `ChangeBattlePhase` 需要 `battlePhase` 说明是回合开始/行动开始/行动结束/回合结束；
        /// `ManaConsumed` 需要 `colorE`；`DrawCard`/`CastCard` 需要 `cardCoreInfoCheck`）。
        /// 平台目前只写 `triggerType`，所以这些类型即使填对了也**不会按作者预期工作** ——
        /// 校验器会对它们给出**警告**，让作者知道"这不是平台的能力范围"，而不是静默失效。
        /// <para>
        /// ★★ 2026-10-14 补入 `WholeNewTurnStart`（缺口处置，证据
        /// <c>docs/ModKit-能力缺口与下一步.md</c> §P0-1）：它依赖 `TriggerTargetInfo` 的
        /// <b>class 字段</b> `certainTurnStartInfo`，为 null 时
        /// <c>TriggerManager.CheckTriggerFitTargetTrigger</c> 会直接
        /// <c>NullReferenceException</c> —— <b>崩游戏</b>，而不是"不触发"。
        /// 此前它被当成"只写 triggerType 就能工作"的简单触发点放行 ⇒
        /// <b>作者照 <see cref="SimpleNames"/> 推荐名单写反而会崩</b>，这是最坏的一种错。
        /// 同类还有 `ChangeBattlePhase`（`battlePhase` 也是 class 字段）。
        /// 判据是"字段是否为 class"：`worldPhaseInfo` / `cardCoreInfoCheck` 是 struct，
        /// 留空只是语义变宽，不会崩。
        /// </para>
        /// <para>
        /// ★★ 2026-10-14 第二轮补入 `EnemyHealthBarBreak` / `AbilityStandardTrigger`
        /// （全是**静默死**、不崩）：
        /// <list type="bullet">
        /// <item><c>EnemyHealthBarBreak</c> 比较 <c>relativeCharacterType</c>；
        /// 平台不写 ⇒ 默认 <c>Owner</c>，而"敌人血条被击破"事件里的 owner 是敌人 ⇒
        /// <b>对敌人血条永不匹配</b>（主角自己血条才匹配，显然不是作者想要的）。</item>
        /// <item><c>AbilityStandardTrigger</c> 比较 <c>extraString</c>（能力名）；
        /// 平台为 null ⇒ 只在"能力名也是 null"时才匹配 ⇒ <b>永不匹配</b>。</item>
        /// </list>
        /// 这两条是上一轮的同类族发现，此前只在日志里记着、没有落到名单上。
        /// </para>
        /// <para>
        /// ★ 名单的**值是"缺哪个字段"** —— 校验器的诊断要能点名，否则作者无从下手。
        /// 新增触发点时**必须同时给字段名**，否则 <see cref="ExtraFieldName"/> 返回 null，
        /// 诊断只说"需要附加字段"就等于没说。
        /// </para>
        /// </remarks>
        private static readonly Dictionary<string, string> NeedsExtraFields =
            new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "ChangeBattlePhase",      "battlePhase" },            // class 字段（为 null 会崩）
            { "WholeNewTurnStart",      "certainTurnStartInfo" },   // class 字段（为 null 会崩）
            { "ManaConsumed",           "colorE" },
            { "DrawCard",               "cardCoreInfoCheck" },
            { "CastCard",               "cardCoreInfoCheck" },
            { "DiscardCard",            "cardCoreInfoCheck" },
            { "CastCardFinished",       "cardCoreInfoCheck" },
            { "CardKeywordTrigger",     "extraString" },            // 关键词名
            { "CharacterCheck",         "cardCoreInfoCheck" },
            { "TakeRest",               "extraString" },            // LongRest / ShortRest
            { "EnemyHealthBarBreak",    "relativeCharacterType" },  // 静默死：默认 Owner ⇒ 对敌人血条永不匹配
            { "AbilityStandardTrigger", "extraString" },            // 静默死：平台为 null ⇒ 永不匹配
            { "Special",                "SpecialExtraCondition" }   // 运行时委托，JSON 表达不了
        };

        /// <summary>该触发类型是否需要平台尚未暴露的附加字段。</summary>
        public static bool NeedsExtraField(string triggerType)
        {
            return !string.IsNullOrEmpty(triggerType) && NeedsExtraFields.ContainsKey(triggerType);
        }

        /// <summary>
        /// 该触发类型**缺哪个**附加字段（让诊断能点名）。
        /// 不需要附加字段、或名单里没登记字段名时返回 null。
        /// </summary>
        public static string ExtraFieldName(string triggerType)
        {
            string field;
            if (!string.IsNullOrEmpty(triggerType) && NeedsExtraFields.TryGetValue(triggerType, out field))
            {
                return field;
            }
            return null;
        }

        /// <summary>
        /// 该触发类型**不是"事件"**，而是"常驻模式" —— 不该被"原版从不发出"那条检查打扰。
        /// </summary>
        /// <remarks>
        /// ★ 来历（2026-10-11 lint 实测）：`Passive` 的含义是"永远开着"
        /// （`TriggerTargetInfo.GetTriggerLocalizedString` 对 `"Passive"` 直接返回空串），
        /// 它当然不会出现在"发出触发点"的代码里。最初的检查把"原版没发出"一律当可疑，
        /// 于是给 `Passive` 报了假警告。
        /// </remarks>
        private static readonly HashSet<string> AlwaysOn = new HashSet<string>(StringComparer.Ordinal)
        {
            "Passive"
        };

        /// <summary>该触发类型是否为"常驻模式"（非事件）。</summary>
        public static bool IsAlwaysOn(string triggerType)
        {
            return !string.IsNullOrEmpty(triggerType) && AlwaysOn.Contains(triggerType);
        }

        /// <summary>只写 triggerType 就能正常工作的触发类型（推荐作者使用）。</summary>
        public static IReadOnlyList<string> SimpleNames
        {
            get
            {
                var list = new List<string>();
                for (int i = 0; i < All.Length; i++)
                {
                    if (!NeedsExtraFields.ContainsKey(All[i])) { list.Add(All[i]); }
                }
                return list;
            }
        }
    }
}
