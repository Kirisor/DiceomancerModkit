using System;
using System.IO;

namespace ModKit.Game
{
    /// <summary>
    /// 诊断开关：读取 <c>BepInEx\config\diceomancer.modkit.cfg</c> 里 <c>[Diagnostics]</c> 段的布尔项。
    /// 发布版默认全部关闭 —— 例如渲染探针会在每张 mod 卡上屏时写一行日志，只在排查时临时打开。
    /// </summary>
    internal static class DiagnosticSwitches
    {
        /// <summary>渲染探针是否开启（配置项 <c>[Diagnostics] RenderProbe</c>，缺省 false）。</summary>
        internal static bool RenderProbe
        {
            get { return ReadBool("RenderProbe"); }
        }

        /// <summary>
        /// 是否把卡面 PNG 导出到 ModKitDump\art（配置项 <c>[Diagnostics] ExportCardsArtPng</c>，缺省 false）。
        /// 卡面是游戏版权素材，默认不落地；cards.json 里的名字清单不受影响。
        /// </summary>
        internal static bool ExportCardsArtPng
        {
            get { return ReadBool("ExportCardsArtPng"); }
        }

        private static bool ReadBool(string key)
        {
            try
            {
                var plugins = Path.GetDirectoryName(typeof(DiagnosticSwitches).Assembly.Location);
                var bep = Path.GetDirectoryName(plugins ?? "");
                if (string.IsNullOrEmpty(bep)) { return false; }

                var cfg = Path.Combine(bep, "config", "diceomancer.modkit.cfg");
                if (!File.Exists(cfg)) { return false; }

                bool inSection = false;
                foreach (var raw in File.ReadAllLines(cfg))
                {
                    var line = raw.Trim();
                    if (line.Length == 0) { continue; }

                    if (line.StartsWith("["))
                    {
                        inSection = line.Equals("[Diagnostics]", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    if (!inSection) { continue; }

                    var eq = line.IndexOf('=');
                    if (eq <= 0) { continue; }
                    if (!line.Substring(0, eq).Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) { continue; }

                    var v = line.Substring(eq + 1).Trim();
                    return v.Equals("true", StringComparison.OrdinalIgnoreCase)
                        || v.Equals("1", StringComparison.OrdinalIgnoreCase)
                        || v.Equals("yes", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }
            return false;
        }
    }
}
