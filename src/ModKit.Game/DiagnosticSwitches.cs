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

        /// <summary>
        /// 自定义颜色自检（配置项 <c>[Diagnostics] CustomColorSmokeTest</c>，缺省 false）：
        /// 打开后注册一个"黄"（(ColorE)6）并在第一场战斗投递 1 颗黄色法力，
        /// 用来端到端验证图标/音效/文案/随机改色几处是否都接住了。
        /// **会改变这一局的法力池**，只在排查时开。
        /// </summary>
        internal static bool CustomColorSmokeTest
        {
            get { return ReadBool("CustomColorSmokeTest"); }
        }

        /// <summary>
        /// 自定义颜色「费用链路」自检（配置项 <c>[Diagnostics] CustomColorCostCard</c>，缺省空）：
        /// 填一张卡的 id（或 "<modId>.<id>"，也支持中文卡名后缀），装配时把它的费用整份替换成
        /// **1 槽自定义色**（默认黄 = ColorE 6），用来验证：
        /// 卡面费用图标、黄法力能否支付、打出后能否结算、卡框主色（节点⑤）。
        /// 只在排查时开；会真实改变那张卡的费用。
        /// </summary>
        internal static string CustomColorCostCard
        {
            get { return ReadValue("CustomColorCostCard"); }
        }

        /// <summary>
        /// **法力球**图标文件（配置项 <c>[Diagnostics] CustomColorSprite</c>，缺省空）：
        /// 填 PNG 的绝对路径或相对游戏根目录的路径。留空时用平台现画的圆。
        /// 换图后**重启游戏**生效（法力球与费用图标都读它）。
        /// </summary>
        internal static string CustomColorSprite
        {
            get { return ReadValue("CustomColorSprite"); }
        }

        /// <summary>
        /// 卡面「流派色」自定义小图标的贴图文件（配置项 <c>[Diagnostics] CustomColorPipSprite</c>，缺省空）：
        /// 填一张 PNG 的**绝对路径**或相对游戏根目录的路径；留空则沿用游戏自己的彩色流派色槽贴图。
        /// 改完文件**重启游戏**即可生效，不需要重编译平台。
        /// </summary>
        internal static string CustomColorPipSprite
        {
            get { return ReadValue("CustomColorPipSprite"); }
        }

        /// <summary>
        /// 卡面自定义流派色小图标的缩放（配置项 <c>[Diagnostics] CustomColorPipScale</c>，缺省 1.0）：
        /// 1.0 = 与游戏紫色流派色点同尺寸；0.6 就小四成。只影响卡面小点，不影响法力球。
        /// </summary>
        internal static float CustomColorPipScale
        {
            get
            {
                var v = ReadValue("CustomColorPipScale");
                float f;
                if (!string.IsNullOrEmpty(v) && float.TryParse(v, out f) && f > 0f) { return f; }
                return 1f;
            }
        }

        /// <summary>
        /// **法力/费用图标**相对原版法力图标的世界尺寸倍率（配置项 <c>[Diagnostics] CustomColorIconScale</c>，缺省 1.0）：
        /// 1.0 = 与原版费用图标一样大；&gt;1 更大，&lt;1 更小。改完重启生效。
        /// </summary>
        internal static float CustomColorIconScale
        {
            get
            {
                var v = ReadValue("CustomColorIconScale");
                float f;
                if (!string.IsNullOrEmpty(v) && float.TryParse(v, out f) && f > 0f) { return f; }
                return 1f;
            }
        }

        private static bool ReadBool(string key)
        {
            var v = ReadValue(key);
            if (v == null) { return false; }
            return v.Equals("true", StringComparison.OrdinalIgnoreCase)
                || v.Equals("1", StringComparison.OrdinalIgnoreCase)
                || v.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>读 [Diagnostics] 段里某个键的原始文本；没有就返回 null。</summary>
        private static string ReadValue(string key)
        {
            try
            {
                var plugins = Path.GetDirectoryName(typeof(DiagnosticSwitches).Assembly.Location);
                var bep = Path.GetDirectoryName(plugins ?? "");
                if (string.IsNullOrEmpty(bep)) { return null; }

                var cfg = Path.Combine(bep, "config", "diceomancer.modkit.cfg");
                if (!File.Exists(cfg)) { return null; }

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

                    return line.Substring(eq + 1).Trim();
                }
            }
            catch { }
            return null;
        }
    }
}
