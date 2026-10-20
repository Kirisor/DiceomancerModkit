// =============================================================================
//  ModKit.Game · 自定义颜色声明（colors.json）
//
//  为什么在 Game 层：颜色是**内容**，属于 mod 而不是平台。平台只提供注册表与
//  六处注入接缝，mod 在自己的目录里声明"我要一颗什么颜色的球"。
//
//  读取位置：<modsRoot>/<modId>/colors.json（modsRoot 由 DiskFileSource 约定，
//  即 <游戏根>\BepInEx\DiceomancerMods）。
//
//  为什么直接在这里读、而不是走 Core 的 mod.json 解析：
//  ① mod.json 由 Core 解析，加字段要重建 Core.dll，而仓库缺 ClassApi.cs 目前
//     编不出来（实施报告 B-01），会卡住整条链路；
//  ② Game 层本来就引用了 Newtonsoft.Json，独立文件读取零成本、可独立演进。
//
//  格式（顶层可以是 { "colors": [...] } 或直接是数组）：
//  {
//    "colors": [
//      {
//        "value": 6,                       // 必填：>5 且 ≤15，与游戏 ColorE 的数值一致
//        "name": "黄色能量球",              // 球名（Encyclopedia/Orb_<n>）
//        "desc": "秩序与平衡之力！",         // 描述（Orb_<n>_Des），可省
//        "color": "#A39212",               // 必填：'#RRGGBB' / [r,g,b] / "r,g,b"
//        "icon": "art/color6.png",         // 法力球图标（相对 mod 根），可省→现画圆
//        "pip":  "art/pip6.png"            // 卡面流派色小点（相对 mod 根），可省
//      }
//    ]
//  }
//
//  冲突规则：同一个 value 先到先得（按目录名的序数排序遍历），后者报 ERROR 并跳过。
//  停用规则：目录名以 _ 或 . 开头即跳过，与本平台其它约定一致。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ModKit.Game
{
    internal static class CustomColorDecl
    {
        internal const string FileName = "colors.json";

        /// <summary>一张卡的"重着色"声明：把 JSON 里的占位色改名成自定义色。</summary>
        internal sealed class CardRecolor
        {
            public int[] Colors;        // 卡的身份色（画框/流派色）
            public int[][] Cost;        // 费用槽（每槽一组颜色）
            public string OwnerMod;
        }

        /// <summary>cardId（可能带 mod 前缀）→ 重着色规则。先到先得。</summary>
        internal static readonly System.Collections.Generic.Dictionary<string, CardRecolor> Recolors =
            new System.Collections.Generic.Dictionary<string, CardRecolor>(StringComparer.OrdinalIgnoreCase);

        /// <summary>按卡 id 找重着色规则（支持 "tune_scale" 与 "manual_demo.tune_scale" 两种写法）。</summary>
        internal static CardRecolor FindRecolor(string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) { return null; }
            CardRecolor r;
            if (Recolors.TryGetValue(cardId, out r)) { return r; }
            int dot = cardId.IndexOf('.');
            if (dot >= 0 && Recolors.TryGetValue(cardId.Substring(dot + 1), out r)) { return r; }
            foreach (var kv in Recolors)
            {
                if (cardId.EndsWith("." + kv.Key, StringComparison.OrdinalIgnoreCase)) { return kv.Value; }
            }
            return null;
        }

        /// <summary>扫描所有 mod 目录并注册其颜色声明；返回成功注册数。</summary>
        internal static int LoadAll(string modsRoot, IGameLog log)
        {
            int count = 0;
            try
            {
                if (string.IsNullOrEmpty(modsRoot) || !Directory.Exists(modsRoot)) { return 0; }

                var dirs = new List<string>(Directory.GetDirectories(modsRoot));
                dirs.Sort(StringComparer.Ordinal);      // 先到先得要有确定顺序

                foreach (var dir in dirs)
                {
                    var modId = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(modId)) { continue; }
                    if (modId.StartsWith("_", StringComparison.Ordinal) ||
                        modId.StartsWith(".", StringComparison.Ordinal)) { continue; }

                    count += LoadOne(dir, modId, log);
                }
            }
            catch (Exception ex)
            {
                if (log != null) { log.Warn("自定义颜色声明：扫描失败 —— " + ex.GetType().Name + ": " + ex.Message); }
            }
            return count;
        }

        private static int LoadOne(string modRoot, string modId, IGameLog log)
        {
            var file = Path.Combine(modRoot, FileName);
            if (!File.Exists(file)) { return 0; }

            int count = 0;
            try
            {
                var root = JToken.Parse(File.ReadAllText(file));
                var arr = root as JArray ?? root["colors"] as JArray;
                if (arr == null)
                {
                    if (log != null) { log.Warn("自定义颜色声明：" + modId + "/colors.json 里没有 colors 数组，跳过。"); }
                    return 0;
                }

                foreach (var item in arr)
                {
                    try
                    {
                        var valueToken = item["value"];
                        if (valueToken == null) { log?.Warn("自定义颜色声明：" + modId + " 有 entry 缺 value，跳过。"); continue; }

                        int value = (int)valueToken;
                        string name = (string)item["name"];
                        string desc = (string)item["desc"];

                        Color color;
                        if (!TryParseColor(item["color"], out color))
                        {
                            log?.Error("自定义颜色声明：" + modId + " 的颜色 " + value + " 的 color 字段无效（要 '#RRGGBB' / [r,g,b] / 'r,g,b'），跳过。");
                            continue;
                        }

                        Sprite icon = LoadSprite(modRoot, (string)item["icon"], modId, value, "icon", log);
                        Sprite pip = LoadSprite(modRoot, (string)item["pip"], modId, value, "pip", log);

                        // 没给图标就现画一个圆：让"只声明颜色"也能用
                        if (icon == null) { icon = CustomColorCompat.MakeCircleSpriteFor(color); }

                        if (CustomColorCompat.Register(value, color, name, icon, desc, pip, modId))
                        {
                            count++;
                            log?.Info("自定义颜色声明：" + modId + " 注册 " + (string.IsNullOrEmpty(name) ? ("颜色" + value) : name) +
                                      "（ColorE=" + value + (pip != null ? "，含卡面小点图" : "") + "）。");
                        }
                        else
                        {
                            log?.Error("自定义颜色声明：" + modId + " 想用颜色 " + value + "，但该值已被占用，已跳过（先到先得）。");
                        }
                    }
                    catch (Exception ex)
                    {
                        log?.Warn("自定义颜色声明：" + modId + " 的一条 entry 解析失败：" + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                log?.Error("自定义颜色声明：" + modId + "/colors.json 解析失败：" + ex.GetType().Name + ": " + ex.Message);
            }

            // ---- cardRecolor：卡牌重着色（把 JSON 里的占位色改成自定义色）----
            try
            {
                ParseCardRecolor(modRoot, modId, log);
            }
            catch (Exception ex)
            {
                log?.Warn("自定义颜色声明：" + modId + " 的 cardRecolor 解析失败：" + ex.GetType().Name + ": " + ex.Message);
            }

            return count;
        }

        private static Sprite LoadSprite(string modRoot, string relPath, string modId, int value, string field, IGameLog log)
        {
            if (string.IsNullOrEmpty(relPath)) { return null; }
            try
            {
                var abs = ModArt.Resolve(modRoot, relPath);
                string err;
                var sp = ModArt.Load(abs, out err);
                if (sp == null)
                {
                    log?.Error("自定义颜色声明：" + modId + " 的颜色 " + value + " 的 " + field + " 加载失败（" + relPath + "）—— " + err);
                }
                return sp;
            }
            catch (Exception ex)
            {
                log?.Warn("自定义颜色声明：" + modId + " 的颜色 " + value + " 的 " + field + " 读取异常：" + ex.Message);
                return null;
            }
        }

        /// <summary>解析 cardRecolor 段。格式：[{ "card": "tune_weight", "colors": [6], "cost": [[6]] }]</summary>
        private static void ParseCardRecolor(string modRoot, string modId, IGameLog log)
        {
            var file = Path.Combine(modRoot, FileName);
            if (!File.Exists(file)) { return; }

            var root = JToken.Parse(File.ReadAllText(file));
            var arr = root["cardRecolor"] as JArray;
            if (arr == null) { return; }

            foreach (var item in arr)
            {
                try
                {
                    var cardId = (string)item["card"];
                    if (string.IsNullOrEmpty(cardId)) { continue; }

                    var rec = new CardRecolor { OwnerMod = modId };

                    var colors = item["colors"] as JArray;
                    if (colors != null)
                    {
                        var list = new List<int>();
                        foreach (var v in colors) { list.Add((int)v); }
                        rec.Colors = list.ToArray();
                    }

                    var cost = item["cost"] as JArray;
                    if (cost != null)
                    {
                        var slots = new List<int[]>();
                        foreach (var slot in cost)
                        {
                            if (slot is JArray s2)
                            {
                                var l2 = new List<int>();
                                foreach (var v in s2) { l2.Add((int)v); }
                                slots.Add(l2.ToArray());
                            }
                        }
                        rec.Cost = slots.ToArray();
                    }

                    if (Recolors.ContainsKey(cardId))
                    {
                        log?.Error("自定义颜色声明：" + modId + " 的 cardRecolor 里 " + cardId + " 已被占用，已跳过（先到先得）。");
                        continue;
                    }
                    Recolors[cardId] = rec;
                    log?.Info("自定义颜色声明：" + modId + " 重着色 " + cardId +
                              "（身份色 " + (rec.Colors == null ? "-" : string.Join("/", rec.Colors)) +
                              (rec.Cost == null ? "" : "，费用 " + CostText(rec.Cost)) + "）。");
                }
                catch (Exception ex)
                {
                    log?.Warn("自定义颜色声明：" + modId + " 的一条 cardRecolor 解析失败：" + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private static string CostText(int[][] cost)
        {
            var parts = new List<string>();
            foreach (var slot in cost) { parts.Add("[" + string.Join("/", slot) + "]"); }
            return string.Join("", parts);
        }

        /// <summary>颜色解析：'#RRGGBB' / '#RGB' / [r,g,b] / "r,g,b"；0-1 或 0-255 都能吃。</summary>
        private static bool TryParseColor(JToken token, out Color color)
        {
            color = Color.white;
            if (token == null) { return false; }

            try
            {
                if (token.Type == JTokenType.String)
                {
                    var s = ((string)token).Trim();
                    if (s.StartsWith("#", StringComparison.Ordinal))
                    {
                        var hex = s.Substring(1);
                        if (hex.Length == 3)
                        {
                            hex = "" + hex[0] + hex[0] + hex[1] + hex[1] + hex[2] + hex[2];
                        }
                        if (hex.Length != 6) { return false; }
                        int r = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        int g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        int b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        color = new Color(r / 255f, g / 255f, b / 255f, 1f);
                        return true;
                    }

                    var parts = s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3)
                    {
                        float r = float.Parse(parts[0], CultureInfo.InvariantCulture);
                        float g = float.Parse(parts[1], CultureInfo.InvariantCulture);
                        float b = float.Parse(parts[2], CultureInfo.InvariantCulture);
                        if (r > 1f || g > 1f || b > 1f) { r /= 255f; g /= 255f; b /= 255f; }
                        color = new Color(r, g, b, 1f);
                        return true;
                    }
                    return false;
                }

                if (token.Type == JTokenType.Array && ((JArray)token).Count >= 3)
                {
                    var a = (JArray)token;
                    float r = (float)a[0], g = (float)a[1], b = (float)a[2];
                    if (r > 1f || g > 1f || b > 1f) { r /= 255f; g /= 255f; b /= 255f; }
                    color = new Color(r, g, b, 1f);
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
