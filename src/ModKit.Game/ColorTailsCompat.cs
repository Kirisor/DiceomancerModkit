// =============================================================================
//  ModKit.Game · 自定义颜色的两处"尾巴"补丁（实机 2026-10-21）
//
//  ① DitwUtil.GetColorTextFromColorE —— 把颜色翻译成 TMP 图标标签
//     `colorE switch { Red…Black, _ => throw }`：6 直接抛
//     ArgumentOutOfRangeException。触发路径：
//       CardBase.CheckCanBePlayed → ManaManager.CheckHasManaForManaCost
//       → HelperManager.GetHelperTextAndShowHelper → GetColorTextFromColorE
//     ⇒ 打出"黄费用"卡时崩在提示文本上。
//     自定义色没有对应的 TMP sprite，返回空串（提示里少一个图标，不影响结算）。
//
//  ② ManaConverter.GetPossibleColorEs —— "卡牌转色"可选的色集合
//     原版写死了 Red/Green/Blue/Purple/Black（连 Void 都不在），
//     所以身份色是 6 的卡**根本不允许被转换**。
//     ⇒ postfix 把该卡颜色里属于已注册自定义色的部分补回去。
// =============================================================================

using System.Collections.Generic;
using Card;
using ColorE = Card.ColorE;
using HarmonyLib;
using Utilities;

namespace ModKit.Game
{
    /// <summary>把颜色翻成 TMP 图标；自定义色返回空串，不抛异常。</summary>
    [HarmonyPatch(typeof(DitwUtil), "GetColorTextFromColorE")]
    internal static class ColorTextCompat
    {
        private static bool _warned;

        private static bool Prefix(ColorE colorE, ref string __result)
        {
            if ((int)colorE <= (int)ColorE.Black) { return true; }

            __result = string.Empty;   // 没有 Mana<自定义> 的 TMP sprite
            if (!_warned)
            {
                _warned = true;
                UnityEngine.Debug.Log("[ModKit] 颜色文案：GetColorTextFromColorE(" + (int)colorE +
                    ") 无内置图标，已返回空串（避免 ArgumentOutOfRangeException）。");
            }
            return false;
        }
    }

    /// <summary>让"卡牌转色"接受自定义色。</summary>
    [HarmonyPatch(typeof(Combat.ManaConverter), "GetPossibleColorEs")]
    internal static class ConverterColorCompat
    {
        private static void Postfix(CardBase card, ref ColorE[] __result)
        {
            try
            {
                if (card == null) { return; }

                var own = card.GetCardColoEs();
                if (own == null || own.Length == 0) { return; }

                List<ColorE> extra = null;
                for (int i = 0; i < own.Length; i++)
                {
                    var c = own[i];
                    if ((int)c <= (int)ColorE.Black) { continue; }

                    CustomColorCompat.CustomColor reg;
                    if (!CustomColorCompat.TryGet(c, out reg)) { continue; }

                    if (__result != null)
                    {
                        bool already = false;
                        for (int j = 0; j < __result.Length; j++) { if (__result[j] == c) { already = true; break; } }
                        if (already) { continue; }
                    }

                    if (extra == null) { extra = new List<ColorE>(__result ?? new ColorE[0]); }
                    extra.Add(c);
                }

                if (extra != null) { __result = extra.ToArray(); }
            }
            catch { }
        }
    }
}
