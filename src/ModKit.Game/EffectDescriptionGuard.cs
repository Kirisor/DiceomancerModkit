// =============================================================================
//  ModKit.Game · 效果描述渲染的兜底与探针
//
//  背景（2026-10-21 实机）：
//    自定义效果（[ModEffect]）的描述模板 Key_mf_* 一旦出现 {Number}，
//    渲染时会抛
//      OperationException: Error parsing format string: Could not evaluate the selector 'Number'
//    但原版同类卡（速子长矛 TachyonLance）同样是 SpecialEffect，且**带 2 个数字**，
//    走同一套模板机制却正常 —— 说明变量在"数字槽 / 参数"上，而不是模板写法。
//
//  本补丁做两件事：
//    1. Prefix 打印：descriptionKey / 类名 / rNumbers 数量 —— 拿到真实数据；
//    2. Finalizer：若渲染仍抛异常，**吞掉并返回兜底文案**，避免玩家看到错误框。
//       兜底文案取 mod 注入的同名模板里"去掉花括号"的纯文本（取不到就用占位）。
// =============================================================================

using System;
using Effects;
using HarmonyLib;
using UnityEngine;

namespace ModKit.Game
{
    [HarmonyPatch(typeof(EffectBase), "GetEffectLocalizeDescription")]
    internal static class EffectDescriptionGuard
    {
        private static readonly System.Collections.Generic.HashSet<string> Logged =
            new System.Collections.Generic.HashSet<string>();

        private static void Prefix(EffectBase __instance)
        {
            try
            {
                if (__instance == null) { return; }
                var key = __instance.descriptionKey;
                if (string.IsNullOrEmpty(key) || !key.StartsWith("Key_mf_")) { return; }
                if (!Logged.Add(key)) { return; }

                int n = __instance.rNumbers == null ? 0 : __instance.rNumbers.Length;
                var eff = __instance.GetEffectName();
                Debug.Log("[ModKit] 描述探针：" + key +
                          " 类=" + __instance.GetType().Name +
                          " isSpecial=" + eff.isSpecial +
                          " effectName=" + eff.effectName +
                          " rNumbers=" + n);
            }
            catch { }
        }

        private static Exception Finalizer(Exception __exception, ref string __result, EffectBase __instance)
        {
            if (__exception == null) { return null; }

            try
            {
                string key = __instance == null ? "?" : __instance.descriptionKey;
                int n = __instance == null || __instance.rNumbers == null ? 0 : __instance.rNumbers.Length;

                // 兜底：把模板里的 {xxx} 占位符去掉，至少给出可读文本
                string fallback = null;
                try
                {
                    fallback = UnityEngine.Localization.Settings.LocalizationSettings.StringDatabase
                        .GetLocalizedString("EffectDescription", key, null,
                            UnityEngine.Localization.Settings.FallbackBehavior.UseProjectSettings);
                }
                catch { }

                if (string.IsNullOrEmpty(fallback))
                {
                    fallback = "（该效果描述渲染失败：" + key + "）";
                }
                else
                {
                    fallback = System.Text.RegularExpressions.Regex.Replace(fallback, "\\{[^}]*\\}", "N");
                }

                __result = fallback;
                Debug.LogError("[ModKit] 效果描述渲染异常已兜底：" + key + " rNumbers=" + n +
                               " 异常=" + __exception.GetType().Name + ": " + __exception.Message +
                               " → 显示为：" + fallback);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ModKit] 兜底处理本身失败：" + ex.Message);
                __result = "（描述渲染失败）";
            }
            return null;   // 返回 null = 异常已被处理
        }
    }
}
