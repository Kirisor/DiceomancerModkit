// =============================================================================
//  ModKit.Core · 内容校验器
//
//  这是平台最核心的价值所在：**把游戏的"静默失败"变成加载期报错**。
//
//  为什么需要它（全部有实测依据）：
//    · 游戏的四条内容挂钩点是"命名即契约"，类名/命名空间写错时
//      **不抛异常、只是静默不生效**；
//    · 费用色组合不合法时抛 KeyNotFoundException，而索引器不带消息，
//      报错只显示 Card.ColorEArray，完全无法定位；
//    · 本地化条目不设 IsSmart 时 {数字} 会抛 FormatException，
//      且表现为游戏内弹窗、**日志零 Error**。
//
//  校验器的职责就是在这三类失败发生**之前**，用带字段路径的错误阻止内容加载。
//  一条内容的校验失败不会影响同 mod 的其他内容，也不会影响其他 mod。
//
//  依赖：Newtonsoft.Json（本机游戏 Managed 内自带 691,712 B；生产环境 BepInEx
//  已加载它，因此不作为新增分发物）。纯逻辑，不引用游戏类型。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ModKit.Api;
using Newtonsoft.Json.Linq;

namespace ModKit.Core
{
    /// <summary>内容校验器。所有方法都是纯函数，不触碰游戏状态。</summary>
    public static partial class ContentValidator
    {
        /// <summary>id 格式：与 schema 的 <c>^[a-z][a-z0-9_]{2,40}$</c> 一致。</summary>
        private static readonly Regex IdPattern =
            new Regex("^[a-z][a-z0-9_]{2,40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public const string CardSchema = "modkit/card@1";
        public const string RelicSchema = "modkit/relic@1";
        public const string KeywordSchema = "modkit/keyword@1";
        public const string I18nSchema = "modkit/i18n@1";
        public const string ManifestSchema = "modkit/mod@1";

        // ---------------------------------------------------------------------
        // 卡牌
        // ---------------------------------------------------------------------

        /// <summary>
        /// 校验一张卡牌的声明。返回的诊断里所有 <c>Path</c> 都是 JSON 字段路径。
        /// </summary>
        /// <param name="json">内容文件的原始文本（不是 JObject，便于报告"解析失败在第几行"）。</param>
        /// <param name="caps">平台能力表（测试时可注入假表）。</param>
        public static ValidationReport ValidateCard(string json, IContentCapabilities caps)
        {
            var report = new ValidationReport();
            JObject root;
            if (!TryParse(json, report, caps, out root)) { return report; }

            string id = ReadString(root, "id");
            if (!ReportId(id, CardSchema, report, IdPattern)) { /* 继续校验其余字段，给作者完整清单 */ }

            CheckSchema(root, CardSchema, report, id);
            CheckLocalizedText(root, "name", true, report, id);

            // 费用：每项必须是单色，且必须是**该 build 真实注册**的颜色
            CheckCost(root, caps, report, id);

            // 稀有度 / 结算时机
            CheckEnum(root, "rarity", true,
                delegate (string s) { return RarityNames.Resolve(s).Mapped; },
                string.Join(" / ", ToArray(RarityNames.AllNames)), report, id);
            CheckEnum(root, "resolve", true,
                delegate (string s) { return ResolveTimingNames.Resolve(s).Mapped; },
                string.Join(" / ", ToArray(ResolveTimingNames.AllNames)), report, id);

            // 卡面流派色（可选）
            CheckOptionalColorArray(root, "colors", caps, report, id);

            // 颜色组合是否合法（最多两种；费用色与卡牌色是独立系统）
            CheckCardColorCombination(root, report, id);

            // artTemplate：Q2 决策是"只借原版资产"，所以必须能对上原版卡名
            CheckArtTemplate(root, "artTemplate", caps.VanillaCardNames, "原版卡名", report, id);

            // art：mod 自带的卡面图（可选）。这里只校验路径形状，文件存在性由 Loader 查。
            CheckCardArt(root, "art", report, id);

            // 效果列表
            CheckEffects(root, caps, report, id, false);
            // 效果与关键词至少要有一项 —— 原版「十字弩-装填」(CrossBowReload)
            // 就是 0 效果 + 纯关键词卡，所以"必须有效果"被放宽成"两者至少有一项"。
            if ((root["effects"] as JArray) == null && (root["keywords"] as JArray) == null)
            {
                report.Error(id, "effects", "缺少 effects，且 keywords 也为空 —— 至少要有一项。");
            }

            // 关键词（可选）
            CheckKeywords(root, caps, report, id);
            CheckUpgrade(root, caps, report, id);

            return report;
        }

        // ---------------------------------------------------------------------
        // 遗物
        // ---------------------------------------------------------------------

        public static ValidationReport ValidateRelic(string json, IContentCapabilities caps)
        {
            var report = new ValidationReport();
            JObject root;
            if (!TryParse(json, report, caps, out root)) { return report; }

            string id = ReadString(root, "id");
            ReportId(id, RelicSchema, report, IdPattern);
            CheckSchema(root, RelicSchema, report, id);
            CheckLocalizedText(root, "name", true, report, id);
            CheckArtTemplate(root, "artTemplate", caps.VanillaRelicNames, "原版遗物名", report, id);

            string trigger = ReadString(root, "trigger");
            bool hasTrigger = !string.IsNullOrEmpty(trigger);

            // ★ 2026-10-14：原"特殊遗物"通路（遗物 JSON 的 C# 钩子字段）已**删除** ——
            //   它在运行期没有任何消费者，声明了它的遗物会被静默装配成 artTemplate 的克隆。
            //   遗物只剩"触发型"一种形态 ⇒ 没填 trigger 是硬错误，不再有"二选一"。
            if (!hasTrigger)
            {
                report.Error(id, "trigger",
                    "遗物必须填 trigger + effects（原\"特殊遗物\"通路已删除，不再支持 C# 钩子字段）。");
            }

            if (hasTrigger)
            {
                // ★ 校验的是**写入 triggerTargetInfo.triggerType 的那张表**
                //   （反编译 TriggerTargetInfo.AllTriggerType，33 个）——
                //   因为游戏 GetTriggerLocalizedString / 触发匹配都是拿这个字符串去 switch 的。
                //   注意它与 caps.KnownTriggerNames（探针取自"游戏实际发出的触发点"）**不是同一张表**：
                //   后者用于描述"游戏会发出哪些事件"，前者才是我们写进数据的取值域。
                var triggerLookup = TriggerTypeNames.Resolve(trigger);
                if (!triggerLookup.Mapped)
                {
                    report.Error(id, "trigger",
                        "触发类型 '" + trigger + "' 不在游戏的 TriggerTargetInfo.AllTriggerType 表里。" +
                        (string.IsNullOrEmpty(triggerLookup.Hint) ? "" : "是否想写 '" + triggerLookup.Hint + "'？") +
                        "可选：" + string.Join(" / ", (System.Collections.Generic.List<string>)TriggerTypeNames.AllNames));
                }
                else if (TriggerTypeNames.NeedsExtraField(triggerLookup.Value))
                {
                    // ★ 2026-10-14：诊断必须**点名缺哪个字段**（ExtraFieldName），
                    //   否则作者只知道"需要附加字段"，仍旧不知道该写什么。
                    // ★ 2026-10-14 第二轮（Tier 1）：其中 5 个字段平台**已经能表达**了
                    //   （battlePhase / certainTurn / relativeCharacter / extraString / colorE）。
                    //   这类触发点**不在本分支告警** —— 由 CheckRelicTriggerFields 统一评审：
                    //   作者提供了字段 ⇒ 静默通过（本任务的核心行为变化）；
                    //   没提供 ⇒ 在那里点名该写哪个 JSON 字段。
                    //   只有平台仍**无法表达**的（cardCoreInfoCheck / SpecialExtraCondition）
                    //   才继续在这里警告。
                    var needField = TriggerTypeNames.ExtraFieldName(triggerLookup.Value);
                    if (TriggerFieldNames.JsonNameOf(needField) == null)
                    {
                        string needFieldText = string.IsNullOrEmpty(needField) ? "(该字段)" : needField;
                        report.Warn(id, "trigger",
                            "触发类型 '" + triggerLookup.Value + "' 需要附加字段 " + needFieldText +
                            " 才能生效，但**平台尚未暴露它**（JSON 表达不了）—— " +
                            "装配层只会写入 triggerType，" + needFieldText + " 保持 null/默认值 ⇒ " +
                            "这个遗物**不会按你的预期触发**。" +
                            "只写 triggerType 就能工作的触发类型：" +
                            string.Join(" / ", (System.Collections.Generic.List<string>)TriggerTypeNames.SimpleNames));
                    }
                }
                else if (!TriggerTypeNames.IsAlwaysOn(triggerLookup.Value) && !Contains(caps.KnownTriggerNames, triggerLookup.Value))
                {
                    // 取值合法，但**游戏原版代码里没有地方发出这个触发点** —— 用了就永远不会触发。
                    // 这条曾经"填对了但不响"，所以给警告而不是放过。
                    report.Warn(id, "trigger",
                        "触发类型 '" + triggerLookup.Value + "' 在游戏的 AllTriggerType 表里，" +
                        "但**原版代码里没有任何地方实际发出它** —— 你的遗物很可能永远不会触发。" +
                        "建议改用平台已探测到确实会发出的触发点。");
                }

                // ★ 触发点附加字段（Tier 1，2026-10-14）放在最后统一评审，
                //   判据四条：取值非法 ⇒ Error / 需要却没给 ⇒ **崩类 Error、静默类 Warning**（2026-10-14 第二轮）/
                //   给了用不到的 ⇒ Warning / 齐备 ⇒ 静默。
                CheckRelicTriggerFields(root, triggerLookup.Mapped ? triggerLookup.Value : null, report, id);

                CheckEffects(root, caps, report, id, true);
            }

            return report;
        }

        // ---------------------------------------------------------------------
        // 遗物 · 触发点附加字段（Tier 1，2026-10-14）
        //
        //  ★ 这一节解决的是"**填了也不会工作**"的那批触发点：平台过去只写
        //    triggerTargetInfo.triggerType，而游戏 TriggerManager.CheckTriggerFitTargetTrigger
        //    还会读 battlePhase / certainTurnStartInfo / relativeCharacterType /
        //    extraString / colorE（反编译 Trigger/TriggerManager.cs:70-185）。
        //    原版 205 件遗物里有 88 次真实使用落在这些触发点上
        //    （dump: state/archive/relic-dump-20261014/relic-dump.log）。
        //
        //  四条判据（对应验收）：
        //    ① 触发点需要这个字段、作者也给了 ⇒ **不警告**（本任务的核心行为变化）
        //    ② 触发点需要、作者没给     ⇒ 看 MissingCrashes 分流（2026-10-14 第二轮 / 第三轮校准）：
        //         · battlePhase / certainTurnStartInfo 在游戏侧是**裸解引用的 class 字段**，
        //           平台现已补**中性默认值**（ContentAssembler.cs:392-429 + 装配期自检）⇒ **不再崩**；
        //           但默认值会把触发条件**固定成那个默认值**、多半与作者预期不符 ⇒ 仍报 **Error**
        //           （lint 退出码非 0、该文件不会被加载 —— 拦住"带着与预期不符的配置进游戏"）
        //         · relativeCharacter / extraString / colorE 只是"静默死" ⇒ 保持 Warning
        //       两种都点名 JSON 字段，并说清是"条件被固定成默认值"还是永不匹配。
        //    ③ 取值非法（枚举名写错）   ⇒ Error，列出合法值
        //    ④ 给了该触发点不读的字段   ⇒ Warning，点名被忽略的字段
        // ---------------------------------------------------------------------

        /// <summary>
        /// 校验遗物的触发点附加字段。
        /// </summary>
        /// <param name="mappedTriggerType">
        /// 归一化后的触发点名。触发点本身没解析出来时传 <c>null</c> ——
        /// 此时只做取值校验，不做"用不到"扫描（否则会在触发点已报错之上再堆一串噪声）。
        /// </param>
        private static void CheckRelicTriggerFields(JObject root, string mappedTriggerType, ValidationReport report, string id)
        {
            // ---- ③ 取值合法性：无论该触发点吃不吃这个字段，写错就是错 ----
            CheckBattlePhaseField(root, report, id);
            CheckCertainTurnField(root, report, id);
            CheckEnumField(root, report, id, TriggerFieldNames.RelativeCharacter,
                TriggerFieldValues.RelativeCharacterNames, TriggerFieldValues.ResolveRelativeCharacter);
            CheckEnumField(root, report, id, TriggerFieldNames.ColorE,
                TriggerFieldValues.ManaColorNames, TriggerFieldValues.ResolveManaColor);
            CheckStringField(root, report, id, TriggerFieldNames.ExtraString);

            if (string.IsNullOrEmpty(mappedTriggerType)) { return; }

            // ---- ② 触发点需要它、作者却没给 ⇒ 点名该写哪个 JSON 字段 ----
            //   ★ 2026-10-14 第二轮：严重级别按"不给会怎样"分流。
            //   ★ 2026-10-14 第三轮（平台兜底后校准口径）：这两个 class 字段已由装配层补
            //     **中性默认值**（ContentAssembler.cs:407-418）+ 装配期自检 ⇒ 缺字段**不再崩游戏**。
            //     Error 之所以保留，是因为"带着中性默认值进游戏"= 触发条件与作者预期多半不符 ——
            //     仍然必须拦在加载之前，但理由是**条件被固定成默认值**，不是崩溃。
            //     静默死的保持警告（别把作者吓到、也别让好文件被拒）。文案两种都已说清后果。
            string missingGameField = TriggerTypeNames.ExtraFieldName(mappedTriggerType);
            string missingJsonField = TriggerFieldNames.JsonNameOf(missingGameField);
            if (missingJsonField != null && !HasField(root, missingJsonField))
            {
                string message = MissingFieldMessage(mappedTriggerType, missingGameField, missingJsonField);
                if (TriggerFieldNames.MissingCrashes(missingJsonField))
                {
                    report.Error(id, "trigger", message);
                }
                else
                {
                    report.Warn(id, "trigger", message);
                }
            }

            // ---- ④ 给了、但这个触发点不读它 ⇒ 点名被忽略的字段 ----
            var allFields = TriggerFieldNames.AllNames;
            for (int i = 0; i < allFields.Count; i++)
            {
                string jsonField = allFields[i];
                if (!HasField(root, jsonField)) { continue; }
                if (TriggerFieldNames.Consumes(mappedTriggerType, jsonField)) { continue; }

                var consumed = TriggerFieldNames.FieldsConsumedBy(mappedTriggerType);
                report.Warn(id, jsonField,
                    "触发点 '" + mappedTriggerType + "' 不读 " + jsonField +
                    "（游戏字段 " + TriggerFieldNames.GameNameOf(jsonField) + "）—— " +
                    "这个字段会被**忽略**：装配层不会把它写进 triggerTargetInfo，" +
                    "游戏侧也就无从比较（不会有报错，纯静默）。" +
                    (consumed.Count == 0
                        ? "该触发点在游戏里没有任何附加字段可填，请删掉它以免误导。"
                        : "该触发点读的是：" + string.Join(" / ", (List<string>)consumed) + "。"));
            }
        }

        /// <summary>"需要附加字段却没给"的诊断文案。**平台兜底成中性默认值** / 静默死要分开说，处置完全不同。</summary>
        private static string MissingFieldMessage(string triggerType, string gameField, string jsonField)
        {
            string head = "触发类型 '" + triggerType + "' 需要附加字段 " + gameField +
                          "（在遗物 JSON 上写成 \"" + jsonField + "\"）才能生效 —— 你没给 ⇒ ";

            if (TriggerFieldNames.MissingCrashes(jsonField))
            {
                return head +
                       "平台会给它补一个**中性默认值**（" + NeutralDefaultFor(jsonField) + "），" +
                       "因此**不会崩**；但这会把触发条件**固定成那个默认值**（" +
                       NeutralBehaviorFor(jsonField) + "），多半不是你想要的 ⇒ 请显式写出你要的取值。" +
                       "照原版真实取值补上，例如 " + ExampleFor(jsonField) + "。";
            }

            return head +
                   "游戏侧保持默认值，于是**永远不匹配**（静默死：不崩、不报错、什么都没有）。" +
                   "请照原版真实取值补上，例如 " + ExampleFor(jsonField) + "。";
        }

        /// <summary>给诊断配一个"原版真实取值"样例（抄自 205 件原版遗物 dump）。</summary>
        private static string ExampleFor(string jsonField)
        {
            switch (jsonField)
            {
                case TriggerFieldNames.BattlePhase:
                    return "\"battlePhase\": { \"phase\": \"Upkeep\", \"isStart\": true }";
                case TriggerFieldNames.CertainTurn:
                    return "\"certainTurn\": { \"require\": false, \"turnCount\": 0, \"relation\": \"Before\" }";
                case TriggerFieldNames.RelativeCharacter:
                    return "\"relativeCharacter\": \"Opponents\"";
                case TriggerFieldNames.ExtraString:
                    return "\"extraString\": \"LongRest\"";
                case TriggerFieldNames.ColorE:
                    return "\"colorE\": \"Red\"";
                default:
                    return "\"" + jsonField + "\": ...";
            }
        }
        /// <summary>
        /// 装配层给该字段补的**中性默认值**（真源 <c>ModKit.Game/ContentAssembler.cs:407-418</c>）。
        /// </summary>
        /// <remarks>
        /// ★ 为什么校验器文案必须把它写出来：2026-10-14 实机 NRE 修复后，缺字段**不再崩游戏**，
        ///   真实后果变成"条件被固定成这个默认值"。不写出来，作者会以为缺字段无害。
        /// </remarks>
        private static string NeutralDefaultFor(string jsonField)
        {
            switch (jsonField)
            {
                case TriggerFieldNames.BattlePhase:
                    return "battlePhase = (BattlePhase.Main, isStart=false)";
                case TriggerFieldNames.CertainTurn:
                    return "certainTurn = { require=false, turnCount=0, relation=Before }";
                default:
                    return "该字段的中性默认值";
            }
        }

        /// <summary>该中性默认值在游戏里的**实际触发行为**（"条件被固定成什么"）。</summary>
        private static string NeutralBehaviorFor(string jsonField)
        {
            switch (jsonField)
            {
                case TriggerFieldNames.BattlePhase:
                    return "只在「进入 Main 阶段且 isStart=false」时匹配";
                case TriggerFieldNames.CertainTurn:
                    return "require=false ⇒ 每回合都触发（无法表达「第 3 回合才触发」这类条件）";
                default:
                    return "条件被固定成默认值";
            }
        }

        /// <summary>该 JSON 字段在遗物对象上是否**提供了**（null / 缺失都算没提供）。</summary>
        private static bool HasField(JObject root, string field)
        {
            var t = root[field];
            return t != null && t.Type != JTokenType.Null;
        }

        /// <summary>字段必须是字符串（结构错就是 Error，别留给游戏去猜）。</summary>
        private static void CheckStringField(JObject root, ValidationReport report, string id, string field)
        {
            var t = root[field];
            if (t == null || t.Type == JTokenType.Null) { return; }
            if (t.Type != JTokenType.String)
            {
                report.Error(id, field, field + " 必须是字符串，当前是 " + t.Type + "。");
            }
        }

        /// <summary>枚举型附加字段：必须是字符串且落在合法值里（大小写不敏感，拼错给相近建议）。</summary>
        private static void CheckEnumField(JObject root, ValidationReport report, string id, string field,
            IReadOnlyList<string> legal, Func<string, LookupResult<string>> resolve)
        {
            var t = root[field];
            if (t == null || t.Type == JTokenType.Null) { return; }

            string legalText = TriggerFieldValues.Describe(legal);

            if (t.Type != JTokenType.String)
            {
                report.Error(id, field, field + " 必须是字符串（合法值：" + legalText + "），当前是 " + t.Type + "。");
                return;
            }

            string raw = (string)t;
            if (string.IsNullOrEmpty(raw))
            {
                report.Error(id, field, field + " 不能是空字符串。合法值：" + legalText + "（大小写不敏感）。");
                return;
            }

            var lookup = resolve(raw);
            if (!lookup.Mapped)
            {
                report.Error(id, field,
                    field + " 取值 '" + raw + "' 非法。合法值：" + legalText + "（大小写不敏感）。" +
                    (string.IsNullOrEmpty(lookup.Hint) ? "" : "是否想写 '" + lookup.Hint + "'？"));
            }
        }

        /// <summary>校验 <c>battlePhase</c> 的形状与 <c>phase</c>/<c>isStart</c> 取值。</summary>
        private static void CheckBattlePhaseField(JObject root, ValidationReport report, string id)
        {
            var t = root[TriggerFieldNames.BattlePhase];
            if (t == null || t.Type == JTokenType.Null) { return; }

            var obj = t as JObject;
            if (obj == null)
            {
                report.Error(id, TriggerFieldNames.BattlePhase,
                    "battlePhase 必须是对象 { \"phase\": ..., \"isStart\": ... }，当前是 " + t.Type + "。");
                return;
            }

            string legalText = TriggerFieldValues.Describe(TriggerFieldValues.BattlePhaseNames);
            var phase = obj["phase"];
            if (phase == null || phase.Type == JTokenType.Null)
            {
                report.Error(id, "battlePhase.phase",
                    "battlePhase 缺少 phase —— 游戏侧它决定「哪个阶段」，缺了只能填默认值。" +
                    "合法值：" + legalText + "（大小写不敏感）。");
            }
            else if (phase.Type != JTokenType.String)
            {
                report.Error(id, "battlePhase.phase",
                    "battlePhase.phase 必须是字符串（合法值：" + legalText + "），当前是 " + phase.Type + "。");
            }
            else
            {
                var lookup = TriggerFieldValues.ResolveBattlePhase((string)phase);
                if (!lookup.Mapped)
                {
                    report.Error(id, "battlePhase.phase",
                        "battlePhase.phase 取值 '" + (string)phase + "' 非法。合法值：" + legalText +
                        "（大小写不敏感）。" +
                        (string.IsNullOrEmpty(lookup.Hint) ? "" : "是否想写 '" + lookup.Hint + "'？"));
                }
            }

            var isStart = obj["isStart"];
            if (isStart != null && isStart.Type != JTokenType.Null && isStart.Type != JTokenType.Boolean)
            {
                report.Error(id, "battlePhase.isStart",
                    "battlePhase.isStart 必须是 true/false，当前是 " + isStart.Type + "。");
            }
        }

        /// <summary>校验 <c>certainTurn</c> 的形状与 <c>require</c>/<c>turnCount</c>/<c>relation</c> 取值。</summary>
        private static void CheckCertainTurnField(JObject root, ValidationReport report, string id)
        {
            var t = root[TriggerFieldNames.CertainTurn];
            if (t == null || t.Type == JTokenType.Null) { return; }

            var obj = t as JObject;
            if (obj == null)
            {
                report.Error(id, TriggerFieldNames.CertainTurn,
                    "certainTurn 必须是对象 { \"require\": ..., \"turnCount\": ..., \"relation\": ... }，" +
                    "当前是 " + t.Type + "。");
                return;
            }

            bool require = false;
            var requireToken = obj["require"];
            if (requireToken != null && requireToken.Type != JTokenType.Null)
            {
                if (requireToken.Type != JTokenType.Boolean)
                {
                    report.Error(id, "certainTurn.require",
                        "certainTurn.require 必须是 true/false，当前是 " + requireToken.Type + "。");
                }
                else
                {
                    require = requireToken.Value<bool>();
                }
            }

            var turnCount = obj["turnCount"];
            if (turnCount != null && turnCount.Type != JTokenType.Null && turnCount.Type != JTokenType.Integer)
            {
                report.Error(id, "certainTurn.turnCount",
                    "certainTurn.turnCount 必须是整数，当前是 " + turnCount.Type + "。");
            }

            string legalText = TriggerFieldValues.Describe(TriggerFieldValues.CertainTurnRelationNames);
            var relation = obj["relation"];
            if (relation == null || relation.Type == JTokenType.Null)
            {
                // ★ 只在"真的要用到回合数"时才追着要 relation：游戏里
                //   `if (!requireCertainTurn) return true;`（TriggerManager.cs:122-125）
                //   ⇒ require=false 时 relation 根本不参与比较，报错就是假阳性。
                if (require)
                {
                    report.Warn(id, "certainTurn.relation",
                        "certainTurn.require 为 true 但没给 relation —— 游戏侧会落到枚举默认值 " +
                        "Before（" + legalText + "）。建议显式写出来。");
                }
            }
            else if (relation.Type != JTokenType.String)
            {
                report.Error(id, "certainTurn.relation",
                    "certainTurn.relation 必须是字符串（合法值：" + legalText + "），当前是 " + relation.Type + "。");
            }
            else
            {
                var lookup = TriggerFieldValues.ResolveCertainTurnRelation((string)relation);
                if (!lookup.Mapped)
                {
                    report.Error(id, "certainTurn.relation",
                        "certainTurn.relation 取值 '" + (string)relation + "' 非法。合法值：" + legalText +
                        "（大小写不敏感）。" +
                        (string.IsNullOrEmpty(lookup.Hint) ? "" : "是否想写 '" + lookup.Hint + "'？"));
                }
            }
        }

        // ---------------------------------------------------------------------
        // 关键词
        // ---------------------------------------------------------------------

        /// <summary>
        /// 校验一个关键词的声明。
        /// </summary>
        /// <remarks>
        /// 关键词路径与卡/遗物**共用同一个游戏工厂**（<c>CardKeyWordUtil.GenerateKeywordFromKeywordInfo</c> 用
        /// <c>Type.GetType("Keyword.CardKeyword" + name)</c>），所以平台的 Transpiler 桥顺带把关键词也接管了。
        /// 这意味着"数据化关键词"**不需要额外补丁** —— 但也意味着一件事：
        /// <b>关键词的"行为"必须由 C# 类提供</b>（游戏没有"纯数据关键词"这种形态，
        /// <c>CardKeyWordInfo.effectInfos</c> 不驱动 <c>CardKeyword</c> 的 virtual 钩子）。
        /// 所以本 schema 检查的是"命名与元数据"，真正的行为在 C# 类里。
        /// </remarks>
        public static ValidationReport ValidateKeyword(string json, IContentCapabilities caps)
        {
            var report = new ValidationReport();
            JObject root;
            if (!TryParse(json, report, caps, out root)) { return report; }

            string id = ReadString(root, "id");
            ReportKeywordId(id, report);
            CheckSchema(root, KeywordSchema, report, id);
            CheckLocalizedText(root, "name", true, report, id);

            // 关键词名必须能拼出 Keyword.CardKeyword<名> —— 这是游戏的硬约定。
            // 给出**精确的类名要求**，因为"类名写错"在游戏里是静默退化成基类（不报错）。
            if (!string.IsNullOrEmpty(id) && IdPattern.IsMatch(id))
            {
                string className = "CardKeyword" + Capitalize(id);
                report.Info(id, "id",
                    "请提供类 Keyword." + className + "（继承 CardKeyword，需 public 无参构造）。" +
                    "类名不存在时游戏会**静默退化成基类** —— 不报错，但关键词没有任何行为。");
            }

            // 数字型关键词：游戏用 CardKeyWordUtil.GetHasNumberFromName 判定
            var hasNumber = root["hasNumber"];
            if (hasNumber != null && hasNumber.Type != JTokenType.Boolean)
            {
                report.Error(id, "hasNumber", "hasNumber 必须是 true/false。");
            }

            // 可选的 effects（若给了就校验，作为作者意图的记录）
            CheckEffects(root, caps, report, id, false);

            return report;
        }

        // ---------------------------------------------------------------------
        // 本地化
        // ---------------------------------------------------------------------

        /// <summary>
        /// 校验本地化文件。
        /// </summary>
        /// <remarks>
        /// 这里的关键不是格式，而是提醒作者：平台会**自动**把条目设为 IsSmart。
        /// 游戏里忘设 IsSmart 的后果是 {数字} 抛 FormatException，且只表现为游戏内弹窗。
        /// </remarks>
        public static ValidationReport ValidateI18n(string json, IContentCapabilities caps)
        {
            var report = new ValidationReport();
            JObject root;
            if (!TryParse(json, report, caps, out root)) { return report; }

            CheckSchema(root, I18nSchema, report, null);

            string language = ReadString(root, "language");
            if (string.IsNullOrEmpty(language))
            {
                report.Error(null, "language", "缺少 language（如 zh-Hans / en）。");
            }
            else if (language.Length < 2)
            {
                report.Error(null, "language", "language 至少 2 个字符，当前 '" + language + "'。");
            }

            var tables = root["tables"] as JObject;
            if (tables == null || tables.Count == 0)
            {
                report.Error(null, "tables", "tables 不能为空（形如 { \"CardName\": { \"my_card\": \"我的卡\" } }）。");
                return report;
            }

            int entryCount = 0;
            foreach (var table in tables.Properties())
              {
                  // ★ 表名校验（纯字符串、没有编译期检查，作者最容易在这里栽）：
                  //   写了不存在的表名时，游戏只会"取表失败 → 跳过"，表现是**文案不生效**。
                  //   实测容易想当然写出的 "RelicDescription" 就不存在 ——
                  //   遗物描述是 RelicBase.GetEffectDescription() 程序化生成的，不走本地化表。
                  if (!ModKit.Core.GameFacts.IsKnownTableName(table.Name))
                  {
                      report.Error(null, "tables." + table.Name,
                          "游戏里没有名为 \"" + table.Name + "\" 的本地化表 —— 这个表的内容不会被注入" +
                          "（表现为文案不生效）。已核实的表名共 " + ModKit.Core.GameFacts.LocalizationTableNames.Length +
                          " 个：CardName / FlavorText / EffectDescription / KeyWord / Relic / SpecialState / Story / UI 等。");
                  }

                var entries = table.Value as JObject;
                if (entries == null || entries.Count == 0)
                {
                    report.Warn(null, "tables." + table.Name, "空表，将被忽略。");
                    continue;
                }

                foreach (var entry in entries.Properties())
                {
                    entryCount++;
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        report.Error(null, "tables." + table.Name, "存在空键。");
                    }
                    if (entry.Value.Type != JTokenType.String)
                    {
                        report.Error(null, "tables." + table.Name + "." + entry.Name,
                            "值必须是字符串（当前是 " + entry.Value.Type + "）。");
                    }
                    else if (IsFormatTemplateTable(table.Name))
                    {
                        // ★ P3：这张表的值会被游戏当**格式化模板**渲染 ⇒ 花括号会在战斗中弹异常框。
                        //   P4（2026-10-21）曾放开白名单占位符，实机证明自定义效果的数字槽与模板
                        //   占位符对不上（OperationException: Could not evaluate the selector 'Number'），
                        //   因此恢复**严格禁止**：作者要引用数值就用文字描述。
                        CheckNoFormatBraces((string)entry.Value,
                            "tables." + table.Name + "." + entry.Name, report, null);
                    }
                }
            }

            if (entryCount == 0)
            {
                report.Error(null, "tables", "没有任何有效条目。");
            }

            return report;
        }

        // ---------------------------------------------------------------------
        // 内部：结构校验
        // ---------------------------------------------------------------------

        private static bool TryParse(string json, ValidationReport report, IContentCapabilities caps, out JObject root)
        {
            root = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                report.Error(null, "(文件)", "文件为空。");
                return false;
            }

            try
            {
                root = JObject.Parse(json);
                return true;
            }
            catch (Exception ex)
            {
                report.Error(null, "(文件)", "JSON 解析失败：" + ex.Message);
                return false;
            }
        }

        private static void CheckSchema(JObject root, string expected, ValidationReport report, string id)
        {
            string schema = ReadString(root, "$schema");
            if (string.IsNullOrEmpty(schema))
            {
                report.Error(id, "$schema",
                    "缺少 $schema。必须显式声明契约版本（应为 \"" + expected + "\"）——" +
                    "平台不认识的版本会被拒绝，而不是猜。");
                return;
            }

            if (!string.Equals(schema, expected, StringComparison.Ordinal))
            {
                report.Error(id, "$schema",
                    "契约版本不匹配：文件声明 \"" + schema + "\"，平台要求 \"" + expected + "\"。" +
                    "请按新版 schema 改写（旧版会归档，不会静默兼容）。");
            }
        }

        private static bool ReportId(string id, string schema, ValidationReport report, Regex pattern)
        {
            if (string.IsNullOrEmpty(id))
            {
                report.Error(null, "id", "缺少 id。");
                return false;
            }

            if (!pattern.IsMatch(id))
            {
                report.Error(id, "id",
                    "id 不符合 ^[a-z][a-z0-9_]{2,40}$（小写字母开头，只含小写字母/数字/下划线，长度 3-41）。");
                return false;
            }

            return true;
        }

        private static void CheckLocalizedText(JObject root, string field, bool required,
            ValidationReport report, string id)
        {
            var token = root[field];

            if (token == null || token.Type == JTokenType.Null)
            {
                if (required)
                {
                    report.Error(id, field, "缺少 " + field + "（至少一种语言，如 { \"zh-Hans\": \"…\" }）。");
                }
                else
                {
                    report.Warn(id, field, "缺少 " + field + "，平台会生成兜底文本。");
                }
                return;
            }

            var obj = token as JObject;
            if (obj == null)
            {
                report.Error(id, field, field + " 必须是对象 { 语言标签: 文本 }。");
                return;
            }

            if (obj.Count == 0)
            {
                report.Error(id, field, field + " 至少要有一种语言。");
                return;
            }

            foreach (var p in obj.Properties())
            {
                if (p.Value.Type != JTokenType.String)
                {
                    report.Error(id, field + "." + p.Name, "值必须是字符串。");
                }
                else if (string.IsNullOrWhiteSpace((string)p.Value))
                {
                    report.Warn(id, field + "." + p.Name, "空文本。");
                }
                else
                {
                    // ★ P3：作者写的 name（各语言）也不能出现花括号 ——
                    //   平台把注入的每个条目都设成 IsSmart=true，花括号会被当格式化指令求值。
                    CheckNoFormatBraces((string)p.Value, field + "." + p.Name, report, id);
                }
            }
        }

        private static void CheckEnum(JObject root, string field, bool required,
            Func<string, bool> isKnown, string allowedList, ValidationReport report, string id)
        {
            string value = ReadString(root, field);
            if (string.IsNullOrEmpty(value))
            {
                if (required)
                {
                    report.Error(id, field, "缺少 " + field + "。合法值：" + allowedList + "。");
                }
                return;
            }

            if (!isKnown(value))
            {
                report.Error(id, field, "未知取值 '" + value + "'。合法值：" + allowedList + "。");
            }
        }

        private static void CheckArtTemplate(JObject root, string field,
            IReadOnlyList<string> pool, string poolName, ValidationReport report, string id)
        {
            string value = ReadString(root, field);
            if (string.IsNullOrEmpty(value))
            {
                report.Error(id, field,
                    "缺少 " + field + "。平台只借原版资产（Q2 决策），必须指定一张原版的" + poolName + "作为模板。");
                return;
            }

            if (pool == null || pool.Count == 0)
            {
                report.Warn(id, field, "平台未能取得" + poolName + "清单，无法校验 artTemplate。");
                return;
            }

            if (!Contains(pool, value))
            {
                report.Error(id, field,
                    "模板 '" + value + "' 不在本 build 的" + poolName + "清单里（共 " + pool.Count + " 项）。" +
                    Near(value, pool));
            }
        }

        /// <summary>
        /// 校验 mod **自带**的卡面路径 <c>art</c>（可选字段）。
        /// </summary>
        /// <remarks>
        /// ★ 这里只做**与文件系统无关**的形状校验（相对路径 / 不上跳 / 扩展名白名单）；
        ///   "文件到底在不在"由 Loader 拿到 mod 目录后再查（ModLoader 的卡加载路径）。
        ///   这么分是为了让校验器仍然能在**没有磁盘**的测试里跑。
        /// </remarks>
        private static void CheckCardArt(JObject root, string field, ValidationReport report, string id)
        {
            var token = root[field];
            if (token == null || token.Type == JTokenType.Null) { return; }   // 可选字段

            if (token.Type != JTokenType.String)
            {
                report.Error(id, field,
                    field + " 必须是字符串（相对 mod 目录的图片路径，如 art/my_card.png）。");
                return;
            }

            string value = (string)token;
            if (string.IsNullOrWhiteSpace(value))
            {
                report.Error(id, field,
                    field + " 不能是空字符串 —— 不想要自带卡面就整个字段别写，平台会沿用 artTemplate 的原版卡面。");
                return;
            }

            if (value.Length > 200)
            {
                report.Error(id, field, field + " 太长（" + value.Length + " 字符，上限 200）。");
                return;
            }

            string normalized = value.Replace('\\', '/');

            // ① 必须是**相对**路径 —— mod 要打包分发给别人，绝对路径在别人机器上必然失效
            if (normalized.StartsWith("/", StringComparison.Ordinal) || value.IndexOf(':') >= 0)
            {
                report.Error(id, field,
                    "必须是**相对 mod 目录**的路径，不能是绝对路径（'" + value + "'）。例：art/my_card.png");
                return;
            }

            // ② 不许上跳出 mod 目录
            string[] segments = normalized.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i] == "..")
                {
                    report.Error(id, field,
                        "路径里不允许出现 ..（'" + value + "'）—— mod 只能引用自己目录里的文件。");
                    return;
                }
            }

            // ③ 扩展名白名单（Unity 的 LoadImage 能解的更多，但平台只放行这三种，
            //    避免"看起来能用、换台机器就静默失败"）
            string ext = System.IO.Path.GetExtension(normalized);
            ext = ext == null ? string.Empty : ext.ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg")
            {
                report.Error(id, field,
                    "只支持 .png / .jpg / .jpeg（'" + value + "'）。" +
                    "其它扩展名平台不做静默降级。");
            }
        }

        // ---------------------------------------------------------------------
        // 内部：小工具
        // ---------------------------------------------------------------------

        internal static string ReadString(JObject obj, string field)
        {
            var t = obj[field];
            if (t == null || t.Type == JTokenType.Null) { return null; }
            return t.Type == JTokenType.String ? (string)t : null;
        }

        private static bool TryReadInt(JObject obj, string field, out int value)
        {
            value = 0;
            var t = obj[field];
            if (t == null || t.Type != JTokenType.Integer) { return false; }
            value = (int)t;
            return true;
        }

        /// <summary>把字面量字符串列表转数组，便于 <c>string.Join</c>。</summary>
        private static string[] ToArray(IReadOnlyList<string> names)
        {
            var arr = new string[names.Count];
            for (int i = 0; i < names.Count; i++) { arr[i] = names[i]; }
            return arr;
        }

        private static bool Contains<T>(IReadOnlyList<T> list, T value)
        {
            if (list == null) { return false; }
            var cmp = EqualityComparer<T>.Default;
            for (int i = 0; i < list.Count; i++)
            {
                if (cmp.Equals(list[i], value)) { return true; }
            }
            return false;
        }

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            if (list == null || value == null) { return false; }
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.Ordinal)) { return true; }
            }
            return false;
        }

        private static string JoinColors(IReadOnlyList<ManaColor> colors)
        {
            if (colors == null || colors.Count == 0) { return "(空 —— 探测可能失败)"; }
            var parts = new List<string>();
            for (int i = 0; i < colors.Count; i++) { parts.Add(colors[i].ToString()); }
            return string.Join(" / ", parts.ToArray());
        }

        private static string Near(string value, IReadOnlyList<string> candidates)
        {
            string hint = BuiltinEffectNames.Closest(value, candidates);
            return hint == null ? "（清单里没有相近的名字）" : "是否想写 '" + hint + "'？";
        }
    }
}
