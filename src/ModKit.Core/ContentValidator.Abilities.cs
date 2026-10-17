// =============================================================================
//  ModKit.Core · 能力（Ability）校验
//
//  单独一个 partial 文件，避免把 ContentValidator 撑得过大，
//  也避免用脚本往大文件里插代码。
// =============================================================================

using System;
using Newtonsoft.Json.Linq;

namespace ModKit.Core
{
    public static partial class ContentValidator
    {
        /// <summary>能力声明的 schema 标识。</summary>
        public const string AbilitySchema = "modkit/ability@1";

        /// <summary>
        /// 校验一个能力的声明。
        /// </summary>
        /// <remarks>
        /// 探针结论（见 <c>docs/ModKit-P2-三类探针报告.md</c>）：能力的**效果侧**
        /// 走 <c>EffectUtils.GenerateEffectFromEffectInfo</c>（已被桥接管），
        /// 所以**不需要新补丁**；但装配侧要写对 <c>AbilityDataManager</c> 的 5 个分类桶。
        /// <para>
        /// <b>与关键词相反的失败形态</b>：能力侧失败是**响亮的** ——
        /// 游戏用 <c>AddComponent(Type.GetType("Ability.AbilityTriggerBase"))</c>，
        /// 取不到类型时 <c>AddComponent(null)</c> 会直接抛异常。
        /// 好的一面是暴露快；坏的一面是**会崩**，所以平台校验必须更严。
        /// </para>
        /// </remarks>
        public static ValidationReport ValidateAbility(string json, IContentCapabilities caps)
        {
            var report = new ValidationReport();
            JObject root;
            if (!TryParse(json, report, caps, out root)) { return report; }

            string id = ReadString(root, "id");
            ReportId(id, AbilitySchema, report, IdPattern);
            CheckSchema(root, AbilitySchema, report, id);
            CheckLocalizedText(root, "name", true, report, id);

            // type / owner 是"进哪个池"的开关 ——
            // 拼错会让能力静默不在玩家池子里（game 只在 isInRandomPool 为真时按这两个值分桶）
            string type = ReadString(root, "type");
            if (string.IsNullOrEmpty(type))
            {
                report.Error(id, "type",
                    "缺少 type，必须显式指定（Buff / DeBuff / Util / Other / PrimAbility）—— " +
                    "它决定能力进哪个分类池，平台不给默认值。");
            }
            else
            {
                var r = AbilityTypeNames.Resolve(type);
                if (!r.Mapped)
                {
                    report.Error(id, "type", "未知的能力类型 " + Quote(type) + "。" +
                        HintText(r.Hint) + "可选：" + Join(AbilityTypeNames.AllNames));
                }
            }

            string owner = ReadString(root, "owner");
            if (string.IsNullOrEmpty(owner))
            {
                report.Error(id, "owner",
                    "缺少 owner，必须显式指定（Player / Enemy / All）—— " +
                    "它决定能力进谁的池子，平台不给默认值。");
            }
            else
            {
                var r = AbilityOwnerNames.Resolve(owner);
                if (!r.Mapped)
                {
                    report.Error(id, "owner", "未知的归属 " + Quote(owner) + "。" +
                        HintText(r.Hint) + "可选：" + Join(AbilityOwnerNames.AllNames));
                }
            }

            // 触发时机：不写就继承 artTemplate —— 那正是"描述与效果不符"的来源，所以要提示
            string trigger = ReadString(root, "trigger");
            if (!string.IsNullOrEmpty(trigger))
            {
                var t = TriggerTypeNames.Resolve(trigger);
                if (!t.Mapped)
                {
                    report.Error(id, "trigger",
                        "触发类型 " + Quote(trigger) + " 不在游戏的 TriggerTargetInfo.AllTriggerType 表里。" +
                        HintText(t.Hint) + "可选：" + Join(TriggerTypeNames.AllNames));
                }
                else if (TriggerTypeNames.NeedsExtraField(t.Value))
                {
                    // ★ 2026-10-14：与遗物侧同口径 —— 点名缺哪个字段（ExtraFieldName）。
                    var needField = TriggerTypeNames.ExtraFieldName(t.Value);
                    report.Warn(id, "trigger",
                        "触发类型 " + Quote(t.Value) + " 需要平台尚未暴露的附加字段 " +
                        (string.IsNullOrEmpty(needField) ? "(该字段)" : needField) +
                        " 才能生效，当前只会写入 triggerType —— 因此它不会按你的预期触发。" +
                        "（battlePhase / certainTurnStartInfo 两个 class 字段为空时会**崩游戏**；" +
                        "relativeCharacterType / extraString 为空时是**静默永不匹配**。）" +
                        "只写 triggerType 就能工作的触发类型：" + Join(TriggerTypeNames.SimpleNames));
                }
                else if (!TriggerTypeNames.IsAlwaysOn(t.Value) && !Contains(caps.KnownTriggerNames, t.Value))
                {
                    report.Warn(id, "trigger",
                        "触发类型 " + Quote(t.Value) + " 取值合法，但原版代码里没有任何地方实际发出它 —— " +
                        "能力很可能永远不触发。");
                }
            }
            else
            {
                report.Info(id, "trigger",
                    "未声明 trigger —— 触发时机将继承 artTemplate 指向的那个原版能力。" +
                    "如果只想要它的图标，请显式写 trigger（如 BattleStart），否则会出现「描述与效果不符」。");
            }

            // 效果是能力的"行为"来源 —— 必填
            CheckEffects(root, caps, report, id, true);

            // 探针给出的实现状态提示（C9：已实现但未实机验证的东西要说出来）
            report.Info(id, "type",
                "能力装配已实现：会克隆原版能力作模板、写进 AbilityDataManager 的分类桶。");

            // ★★ 重要限制（2026-10-11 实机证伪后补上）：
            //    平台**只能控制能力"做什么"（effects），控制不了"什么时候做"**。
            //    触发时机（triggerTargetInfo）、保留规则（abilityRetain）、各类延迟
            //    （resolveDelay / finishDelay）都**继承自 artTemplate 指向的那个原版能力**。
            //    实机症状：作者写"获得 3 点格挡"，实际却按模板能力的触发规则生效
            //    （例如每个单位回合结束时各触发一次）—— **描述与效果不符**。
            report.Info(id, "artTemplate",
                "能力这一步的 artTemplate **不只是外观**：你没显式声明 trigger 时，" +
                "触发时机、保留规则与各类延迟都继承自它。**写了 trigger 就能覆盖触发时机**；" +
                "保留规则与延迟目前仍继承自模板。");

            return report;
        }

        private static string Quote(string s)
        {
            return "\u201c" + s + "\u201d";
        }

        private static string HintText(string hint)
        {
            return string.IsNullOrEmpty(hint) ? "" : ("是否想写 " + Quote(hint) + "？");
        }

        private static string Join(System.Collections.Generic.IReadOnlyList<string> parts)
        {
            return string.Join(" / ", parts);
        }
    }
}
