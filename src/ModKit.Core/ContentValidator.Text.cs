// =============================================================================
//  ModKit.Core · 内容校验器（分片）—— 作者文案里的花括号（= 格式化指令）
//
//  ★ P3（2026-10-13 实机踩到的静默失败，实际是"战斗中弹异常框"）：
//    作者在描述文案里写 `{Number}` 这类花括号时，游戏并不把它当**文本**，
//    而是交给 Unity Localization 的 SmartFormat 当**格式化指令**求值。
//    求值失败的现场是：
//        System.OperationException: Error parsing format string:
//          Could not evaluate the selector "Number"   （战斗中弹窗）
//    异常源（反编译实证，2026-10-13）：
//        UnityEngine.Localization.SmartFormat/SmartFormatter.cs:304
//          throw formattingInfo.FormattingException("Could not evaluate the selector \"" + …)
//        UnityEngine.Localization.SmartFormat.Core.Formatting/FormattingException.cs:16
//          Message => "Error parsing format string: {0} at {1} …"
//    平台侧之所以**必然**走到 SmartFormat：文案注入泵对每一条目都写死
//    `IsSmart = true`（ModKit.Game/LocalizationPump.cs:252），不提供关闭选项。
//
//    所以本分片的规则是：**作者文案里出现 `{` 或 `}` 一律报错**（Error，不是警告）——
//    因为它的后果不是"少显示几个字"，而是**整张卡的描述渲染时抛异常**。
//
//  ★ 平台当前**不支持占位符**（也不做转义）：要引用数值就用文字描述，
//    例如平台自己的 Key_CleanMana = "清空所有法力。"、样板卡 =
//    "获得等同于本效果数值的格挡。" —— 一个括号都没有。
//
//  ★ 只查**作者提供**的字符串，不碰游戏原版表：
//    · 内容声明里的 name（卡 / 遗物 / 关键词 / 能力，各语言）；
//    · i18n 里会被游戏当**模板**渲染的那几张表（见 IsFormatTemplateTable）。
// =============================================================================

using System;

namespace ModKit.Core
{
    public static partial class ContentValidator
    {
        /// <summary>
        /// 查一段**作者写的文案**里有没有花括号；有就报 Error（带字段路径与出错片段）。
        /// </summary>
        /// <remarks>
        /// 只报第一处（一条诊断足以让作者去改），但消息里带上片段便于定位。
        /// </remarks>
        /// <summary>
        /// 允许在**模板表**里出现的占位符（必须是游戏能解析的键）。
        /// 依据 EffectBase.AddNumbersToLocalizedString：rNumbers 依次填
        /// <c>Number</c> / <c>Number1</c> / <c>Number2</c>；
        /// ManaString / State / CardName / AbilityName / CardRequirement 由同类方法填充。
        /// </summary>
        private static readonly string[] AllowedPlaceholders =
        {
            "Number", "Number1", "Number2",
            "Mana", "State", "CardName", "AbilityName", "CardRequirement",
        };

        /// <summary>
        /// 模板表专用：允许**白名单占位符**，其余花括号仍判错。
        /// </summary>
        /// <remarks>
        /// 为什么不能一刀切禁止：<c>EffectDescription</c> 的键（如 <c>Key_mf_feast</c>）
        /// 正是给游戏当模板渲染的，写了 <c>{Number}</c> 才会显示具体数值；
        /// 全禁会退化成「造成点伤害」这种没有数字的文案（实机 2026-10-21 复现）。
        /// 但仍要拦住"写错的占位符"——那会在战斗中抛 SmartFormat 解析异常。
        /// </remarks>
        private static void CheckPlaceholders(string text, string path, ValidationReport report, string id)
        {
            if (string.IsNullOrEmpty(text)) { return; }

            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('{', i);
                int close = open < 0 ? -1 : text.IndexOf('}', open + 1);
                if (open < 0) { break; }

                if (close < 0)
                {
                    report.Error(id, path, "文案里有未闭合的 '{'（片段：…" + Slice(text, open) + "…）。");
                    return;
                }

                string token = text.Substring(open + 1, close - open - 1).Trim();
                bool allowed = false;
                for (int k = 0; k < AllowedPlaceholders.Length; k++)
                {
                    if (string.Equals(token, AllowedPlaceholders[k], StringComparison.Ordinal)) { allowed = true; break; }
                }
                if (!allowed)
                {
                    report.Error(id, path,
                        "未知占位符 '{" + token + "}'。模板表只允许：" +
                        string.Join(" / ", AllowedPlaceholders) +
                        "。写错的占位符会在战斗中抛 SmartFormat 解析异常。");
                    return;
                }
                i = close + 1;
            }

            // 多余的 '}' 也拦一下
            if (text.IndexOf('}') >= 0 && text.IndexOf('{') < 0)
            {
                report.Error(id, path, "文案里有孤立 '}'（片段：…" + Slice(text, text.IndexOf('}')) + "…）。");
            }
        }

        private static string Slice(string text, int at)
        {
            int from = Math.Max(0, at - 10);
            int len = Math.Min(text.Length - from, 21);
            return text.Substring(from, len);
        }

        private static void CheckNoFormatBraces(string text, string path, ValidationReport report, string id)
        {
            if (string.IsNullOrEmpty(text)) { return; }

            int open = text.IndexOf('{');
            int close = text.IndexOf('}');
            if (open < 0 && close < 0) { return; }

            // 取先出现的那一个（作者最可能先看到的位置）
            char bad = (open >= 0 && (close < 0 || open < close)) ? '{' : '}';
            int at = (bad == '{') ? open : close;

            int from = Math.Max(0, at - 10);
            int len = Math.Min(text.Length - from, 21);
            string snippet = text.Substring(from, len);

            report.Error(id, path,
                "文案里出现了 '" + bad + "'（片段：…" + snippet + "…）。" +
                "游戏不会把它当文本，而是当**格式化指令**交给 SmartFormat 求值 —— " +
                "写错会在战斗中直接弹异常框" +
                "（`OperationException: Error parsing format string: Could not evaluate the selector …`），" +
                "而日志里零 Error。平台目前**不支持占位符**，也不做转义：请只写纯文本，" +
                "要引用数值就用文字描述（例如「获得等同于本效果数值的格挡。」）。");
        }

        /// <summary>
        /// 这张本地化表的值是否会被游戏当**格式化模板**渲染（= 花括号会炸）。
        /// </summary>
        /// <remarks>
        /// 依据（2026-10-13 反编译核对，全部是"取值 → 塞进 Arguments → GetLocalizedString"）：
        /// <list type="bullet">
        /// <item><c>EffectDescription</c> —— <c>EffectBase.GetEffectLocalizeDescription</c>
        /// （Effects/EffectBase.cs:290-306）与 <c>numberReplaceString</c> 取值（:404）；</item>
        /// <item><c>CardRequirementDes</c> —— <c>EffectBase.cs:301</c> / <c>:356</c>、
        /// <c>Trigger/TriggerTargetInfo.cs:86</c>（都是 new LocalizedString + Arguments）；</item>
        /// <item><c>KeyWord</c> —— <c>RuleManager.GetLocalizedDescriptionForKeyWord</c>
        /// （Collection.Rules/RuleManager.cs:125-154）与 <c>CardKeyWordUtil.cs:137</c> / <c>:161</c>；</item>
        /// <item>名字里含 "Description" 的其它表（<c>AbilityDescription</c> /
        /// <c>EffectDescriptionAbbreviation</c>）同属"描述模板"，按同一规则处理。</item>
        /// </list>
        /// <c>CardName</c> / <c>FlavorText</c> 等**不在此列**：它们的值是被塞进别的模板当
        /// **参数值**的（如 <c>argDic.Add("CardName", …)</c>，EffectBase.cs:340-344），
        /// 游戏不会把参数值再解析一次。
        /// </remarks>
        private static bool IsFormatTemplateTable(string tableName)
        {
            if (string.IsNullOrEmpty(tableName)) { return false; }

            if (string.Equals(tableName, "CardRequirementDes", StringComparison.Ordinal)) { return true; }
            if (string.Equals(tableName, "KeyWord", StringComparison.Ordinal)) { return true; }

            return tableName.IndexOf("Description", StringComparison.Ordinal) >= 0;
        }
    }
}
