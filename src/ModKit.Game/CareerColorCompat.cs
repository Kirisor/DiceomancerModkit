// =============================================================================
//  ModKit.Game · 主菜单三色圆点的取色兜底
//
//  修的是什么（原版缺陷，不是我们引入的）：
//    `MainMenuCircles.GetColorByColorE(ColorE)` 是一个**穷举 switch**，
//    只认 Red/Green/Blue/Purple/Void，其余一律
//      `_ => throw new ArgumentOutOfRangeException("colorE", colorE, null)`。
//    注意：**它连 Black 都没覆盖**（Black 是原版枚举里存在、只是没做成球的颜色）。
//
//  什么时候会炸（实机链路）：
//    1) 玩家用 mod 职业（奶蛙 = Black/Purple/Void）；
//    2) 确认后 `SmallGameMainMenu.SetColorsOnMainMenu()` →
//       `MainMenuCircles.SetAllCirclesByPickedColors(三色)` → `GetColorByColorE(Black)`；
//    3) `MainMenuCircles.SetPickedCircles` 按 `centerCircles.Length` 逐个取色 ——
//       走到 Black 那一步直接抛。
//
//  为什么是"静默"的（所以一直没人发现）：
//    `CareerOrbTicker.ForceSwitchBack` 里是用 `TryInvoke(...)` 反射调 `SetColorsOnMainMenu`，
//    外层 try/catch 把异常吞了 ⇒ 主菜单三色圆点只是**不更新**，日志里什么都没有。
//    但 `SmallGameMainMenu.BackFromSubSmallGame` 的 DOTween OnComplete 里**没有** try/catch，
//    同一个异常会打断回调的剩余部分（`RefreshPlayButton` / `ResetAllButtons` /
//    `subSmallGameBase.SetActive(false)`）⇒ 表现为"回房间后开始按钮不亮、选球界面还留着"。
//
//  修法（最小侵入）：
//    在 `GetColorByColorE` 前面加一个 Harmony prefix：
//      · 原版 5 色 ⇒ 直接放行，原方法照常执行（零行为变化）；
//      · Black    ⇒ 用游戏自己的美术色（`WorldManager.GetColorFromEnumColor(Black)`
//                    对 Black 有专门分支走 `colorEAssetRefSo`），失败则平台兜底色；
//      · 其它值   ⇒ 确定性兜底色（给将来 `(ColorE)6+` 留口，且**不**调
//                    `GetColorFromEnumColor`，因为它对未定义值会 Debug.LogError 并返白）。
//
//  与"第 6 颗球"的边界：
//    本补丁**不依赖** `EnableCustomOrb` / 职业声明，装了平台就生效；
//    它只影响"把颜色画成圆点"这一步，不改任何职业判定。
// =============================================================================

using System;
using System.Collections.Generic;
using Card;
using HarmonyLib;
using UnityEngine;

namespace ModKit.Game
{
    /// <summary>主菜单三色圆点：让原版 5 色之外的取值不再抛异常。</summary>
    internal static class MainMenuCircleColorCompat
    {
        private static IGameLog _log;

        /// <summary>Black 取不到游戏美术色时的兜底色（深灰紫，与奶蛙配色接近）。</summary>
        private static readonly Color BlackFallback = new Color(0.16f, 0.15f, 0.20f, 1f);

        /// <summary>任何解析异常时的中性色（宁可难看，也不要抛异常打断回调）。</summary>
        private static readonly Color NeutralFallback = new Color(0.50f, 0.50f, 0.55f, 1f);

        /// <summary>每个颜色值只提示一次，避免每帧刷屏。</summary>
        private static readonly HashSet<int> _warned = new HashSet<int>();

        /// <summary>每个颜色值只记一条"成功取到色"，作为实机验收到这条路径的凭据。</summary>
        private static readonly HashSet<int> _noted = new HashSet<int>();

        /// <summary>装挂钩。可重复调用，只会生效一次（Harmony 自身按方法去重）。</summary>
        internal static bool Install(Harmony harmony, IGameLog log)
        {
            _log = log;
            if (harmony == null) { return false; }

            try
            {
                var target = AccessTools.Method(
                    typeof(SmallGames.WorldBetween.MainMenuCircles), "GetColorByColorE");
                if (target == null)
                {
                    if (log != null)
                    {
                        log.Warn("主菜单圆点：找不到 MainMenuCircles.GetColorByColorE —— " +
                            "非原版颜色仍会在 SetColorsOnMainMenu 抛异常（退化为原版行为，不新增风险）。");
                    }
                    return false;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(MainMenuCircleColorCompat), nameof(BeforeGetColorByColorE))));

                if (log != null)
                {
                    log.Info("主菜单圆点：已接管非原版颜色的取色（Black/未定义值不再抛异常；原版 5 色行为不变）。");
                }
                return true;
            }
            catch (Exception ex)
            {
                if (log != null) { log.Warn("主菜单圆点：挂钩失败 —— " + ex.GetType().Name + ": " + ex.Message); }
                return false;
            }
        }

        /// <summary>
        /// 原版 5 色放行；其余值由平台给出颜色并跳过原方法。
        /// 签名必须与目标方法一致：`private Color GetColorByColorE(ColorE colorE)`。
        /// </summary>
        private static bool BeforeGetColorByColorE(ColorE colorE, ref Color __result)
        {
            if (IsVanillaOrbColor(colorE)) { return true; }

            try
            {
                __result = Resolve(colorE);
            }
            catch (Exception ex)
            {
                __result = NeutralFallback;
                WarnOnce(-1, "主菜单圆点：解析颜色 " + (int)colorE + " 失败，用中性兜底色 —— " +
                    ex.GetType().Name + ": " + ex.Message);
            }
            return false;
        }

        /// <summary>原版做成球的 5 色：这些必须交还给原方法，保证零行为变化。</summary>
        private static bool IsVanillaOrbColor(ColorE colorE)
        {
            return colorE == ColorE.Red || colorE == ColorE.Green || colorE == ColorE.Blue ||
                   colorE == ColorE.Purple || colorE == ColorE.Void;
        }

        /// <summary>给一个非原版颜色算出圆点色。</summary>
        private static Color Resolve(ColorE colorE)
        {
            // 先查自定义颜色注册表（(ColorE)6 及以后）：注册过就用作者/平台声明的色。
            CustomColorCompat.CustomColor custom;
            if (CustomColorCompat.TryGet(colorE, out custom)) { return custom.Base; }

            if (colorE == ColorE.Black)
            {
                // 原版给 Black 留了真实美术色：WorldManager.GetColorFromEnumColor 对 Black
                // 有专门分支（走 colorEAssetRefSo），这是最贴近游戏观感的一手来源。
                try
                {
                    var wm = World.WorldManager.Instance;
                    if (wm != null)
                    {
                        Color c = wm.GetColorFromEnumColor(ColorE.Black);
                        // 取不到时该 API 会返白（并打一条 Debug.LogError），白圆点在深色桌面上等于"没画"。
                        if (c.a > 0.01f && !IsNearWhite(c))
                        {
                            NoteOnce((int)ColorE.Black, "主菜单圆点：Black 已用游戏美术色兜底（r=" +
                                c.r.ToString("0.00") + " g=" + c.g.ToString("0.00") + " b=" + c.b.ToString("0.00") + "）。");
                            return c;
                        }
                    }
                }
                catch (Exception ex)
                {
                    WarnOnce((int)ColorE.Black, "主菜单圆点：取 Black 美术色失败，改用平台兜底色 —— " + ex.Message);
                }
                return BlackFallback;
            }

            // 尚未进入枚举的色彩（将来的 (ColorE)6+）：给确定性兜底色。
            // **不**调 WorldManager.GetColorFromEnumColor —— 它对未定义值 Debug.LogError 后返白。
            WarnOnce((int)colorE, "主菜单圆点：颜色 " + (int)colorE +
                " 没有原版配色，使用平台兜底色（如需精确颜色，见《ModKit-职业球扩展方案》§8 的颜色注册表）。");
            return PaletteFor((int)colorE);
        }

        /// <summary>按整数序号生成稳定、互不相同的颜色（黄金角散布色相）。</summary>
        private static Color PaletteFor(int value)
        {
            float hue = Mathf.Repeat(0.137f * (value - (int)ColorE.Black), 1f);
            return Color.HSVToRGB(hue, 0.55f, 0.92f);
        }

        private static bool IsNearWhite(Color c)
        {
            return c.r > 0.97f && c.g > 0.97f && c.b > 0.97f;
        }

        private static void WarnOnce(int key, string message)
        {
            if (!_warned.Add(key)) { return; }
            if (_log != null) { _log.Warn(message); }
            else { Debug.LogWarning("[ModKit] " + message); }
        }

        private static void NoteOnce(int key, string message)
        {
            if (!_noted.Add(key)) { return; }
            if (_log != null) { _log.Info(message); }
            else { Debug.Log("[ModKit] " + message); }
        }
    }
}
