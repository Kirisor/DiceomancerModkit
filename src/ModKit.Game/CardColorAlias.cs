// =============================================================================
//  ModKit.Game · 把 mod 声明的颜色别名注入 Core（替代此前的"改 JSON 文本"方案）
//
//  为什么下沉到 Core：颜色名的解析点是 ModKit.Core 的 ManaColorNames.Resolve，
//  它同时被这些地方使用——
//    · 校验器：费用槽 / 遗物随机色 / 效果参数里的 color
//    · 装配器：卡身份色、费用槽、按颜色检索的卡池
//  只在 Game 层做文本替换的话，校验器不认自定义色，
//  「按颜色检索的卡池」「随机颜色遗物」都会漏掉它。
//
//  别名来源：各 mod 的 colors.json
//    { "colors": [ { "value": 6, "alias": "Yellow", ... } ],
//      "aliases": { "Gold": 6 } }
//  不区分大小写；重名先到先得（按 mod 目录序数）；不覆盖原版色名。
//
//  时机：必须在 ModLoader 解析任何 mod 内容**之前**完成注入
//  （见 PlatformRuntime.RunOnce 开头的第一步）。
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ModKit.Game
{
    internal static class CardColorAlias
    {
        private static bool _loaded;
        private static int _registered;

        internal static int RegisteredCount { get { return _registered; } }

        /// <summary>扫描所有 mod 的 colors.json，把别名注册进 Core 的颜色名表。</summary>
        internal static void EnsureLoaded(string modsRoot, IGameLog log = null)
        {
            if (_loaded) { return; }
            _loaded = true;

            try
            {
                if (string.IsNullOrEmpty(modsRoot) || !Directory.Exists(modsRoot)) { return; }

                var dirs = new List<string>(Directory.GetDirectories(modsRoot));
                dirs.Sort(StringComparer.Ordinal);

                var registered = new List<string>();

                foreach (var dir in dirs)
                {
                    var modId = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(modId)) { continue; }
                    if (modId.StartsWith("_", StringComparison.Ordinal) ||
                        modId.StartsWith(".", StringComparison.Ordinal)) { continue; }

                    var file = Path.Combine(dir, CustomColorDecl.FileName);
                    if (!File.Exists(file)) { continue; }

                    JToken root;
                    try { root = JToken.Parse(File.ReadAllText(file)); }
                    catch (Exception ex)
                    {
                        log?.Warn("颜色别名：" + modId + "/colors.json 解析失败 —— " + ex.Message);
                        continue;
                    }

                    // 写法一：colors[].alias
                    var colors = root["colors"] as JArray;
                    if (colors != null)
                    {
                        foreach (var c in colors)
                        {
                            var alias = (string)c["alias"];
                            var valueToken = c["value"];
                            if (string.IsNullOrEmpty(alias) || valueToken == null) { continue; }
                            if (ModKit.Core.ManaColorNames.RegisterAlias(alias, (int)valueToken))
                            {
                                _registered++;
                                registered.Add(alias + "=" + (int)valueToken);
                            }
                        }
                    }

                    // 写法二：顶层 aliases: { "Gold": 6 }
                    var aliases = root["aliases"] as JObject;
                    if (aliases != null)
                    {
                        foreach (var kv in aliases)
                        {
                            int v;
                            try { v = (int)kv.Value; } catch { continue; }
                            if (ModKit.Core.ManaColorNames.RegisterAlias(kv.Key, v))
                            {
                                _registered++;
                                registered.Add(kv.Key + "=" + v);
                            }
                        }
                    }
                }

                if (_registered > 0)
                {
                    var msg = "颜色别名：已注入 Core " + _registered + " 个（" + string.Join(", ", registered) +
                              "）——卡牌/遗物/卡池的 colors 与 cost 都可直接写别名。";
                    if (log != null) { log.Info(msg); } else { Debug.Log("[ModKit] " + msg); }
                }
            }
            catch (Exception ex)
            {
                var msg = "颜色别名注入失败：" + ex.GetType().Name + ": " + ex.Message;
                if (log != null) { log.Warn(msg); } else { Debug.LogWarning("[ModKit] " + msg); }
            }
        }
    }
}
