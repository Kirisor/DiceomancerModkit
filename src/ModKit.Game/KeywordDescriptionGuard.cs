// =============================================================================
//  ModKit.Game · 关键词描述渲染兜底
//
//  背景（2026-10-21 实机，两次）：
//    · 「限制」→ `Could not evaluate the selector 'RuleNumber'`
//    · 「召唤」→ `Could not evaluate the selector 'Number'`
//    mod 卡上的原版关键词，其描述模板（KeyWord/<名称>Description）里的
//    {Number} / {RuleNumber} 在部分渲染路径上取不到值 ⇒ SmartFormat 抛异常弹框。
//
//  做法：给 `CardKeyWordUtil.GetLocalizedKeywordDescription`（private static）挂 Finalizer，
//  异常时由平台自己按"模板 + 本卡关键词数值"渲染一份等价文案返回：
//    {Number}/{Number1}… ← 关键词的 RNumber（取不到用 0/1）
//    {RuleNumber}        ← 规则值（取不到用 1）
//    {Mana}/{State}/{CardName}/{AbilityName} ← 能取就取，取不到留原文
//  这样关键词在 mod 卡上不再炸，同时保留"能正常渲染就正常渲染"的行为。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using Keyword;
using UnityEngine.Localization.Settings;

namespace ModKit.Game
{
    [HarmonyPatch(typeof(CardKeyWordUtil), "GetLocalizedKeywordDescription")]
    internal static class KeywordDescriptionGuard
    {
        private static readonly System.Collections.Generic.HashSet<string> Logged =
            new System.Collections.Generic.HashSet<string>();

        private static Exception Finalizer(Exception __exception, ref string __result, CardKeyword cardKeyword)
        {
            if (__exception == null) { return null; }

            try
            {
                string name = cardKeyword == null ? "?" : cardKeyword.KeywordName;
                string raw = null;
                try
                {
                    raw = LocalizationSettings.StringDatabase.GetLocalizedString(
                        "KeyWord", name + "Description", null, FallbackBehavior.UseProjectSettings);
                }
                catch { }

                if (string.IsNullOrEmpty(raw) || raw.IndexOf("No translation found", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    raw = "[" + name + "]";
                }

                int number = 0;
                try { number = cardKeyword != null && cardKeyword.RNumber != null ? cardKeyword.RNumber.GetStableIntNumber() : 0; }
                catch { }

                // 逐个占位符替换（不交给 SmartFormat，避免再次抛）
                raw = Regex.Replace(raw, "\\{Number\\}", number.ToString());
                raw = Regex.Replace(raw, "\\{Number[0-9]+\\}", number.ToString());
                raw = Regex.Replace(raw, "\\{RuleNumber\\}", "1");
                raw = Regex.Replace(raw, "\\{Mana\\}", "");
                raw = Regex.Replace(raw, "\\{State\\}", "");
                raw = Regex.Replace(raw, "\\{CardName\\}", "");
                raw = Regex.Replace(raw, "\\{AbilityName\\}", "");
                // 剩下的未知占位符统一抹掉，保证不再有花括号
                raw = Regex.Replace(raw, "\\{[^}]*\\}", "");

                __result = raw;
                if (Logged.Add(name))
                {
                    ModKitLog.Warn("关键词描述渲染异常已兜底：" + name + "（" + __exception.GetType().Name
                        + ": " + __exception.Message + "）→ 显示为：" + raw);
                }
            }
            catch (Exception e)
            {
                ModKitLog.Error("关键词描述兜底本身失败：" + e.Message);
                __result = "";
            }
            return null;   // 异常已被处理
        }
    }
}
