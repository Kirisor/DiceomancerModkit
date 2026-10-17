using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ModKit.Studio
{
    /// <summary>
    /// Studio 的运行配置：游戏根目录与 mods 目录。
    /// 存到 %APPDATA%\ModKitStudio\config.json；缺失时自动探测（Steam 库 → 常见盘符 → 相对路径）。
    /// 所有其它模块一律从这里取路径，不再出现本机绝对路径。
    /// </summary>
    internal sealed class StudioConfig
    {
        private static StudioConfig _current;

        public string GameRoot { get; set; }
        public string ModsRoot { get; set; }

        public static StudioConfig Current
        {
            get { return _current ?? (_current = Load()); }
        }

        public static string ConfigFile
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ModKitStudio");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "config.json");
            }
        }

        // ---------------- 派生路径 ----------------

        public string BepInExPath { get { return Path.Combine(GameRoot ?? "", "BepInEx"); } }
        public string PluginsPath { get { return Path.Combine(BepInExPath, "plugins"); } }
        public string ManagedPath { get { return Path.Combine(GameRoot ?? "", "Diceomancer_Data", "Managed"); } }
        public string ModKitDumpPath { get { return Path.Combine(BepInExPath, "ModKitDump"); } }
        public string DocsPath { get { return Path.Combine(BepInExPath, "docs"); } }

        public string DumpFile(string name) { return Path.Combine(ModKitDumpPath, name); }
        public string DocsFile(string name) { return Path.Combine(DocsPath, name); }

        public bool HasGame
        {
            get { return !string.IsNullOrEmpty(GameRoot) && Directory.Exists(ManagedPath); }
        }

        // ---------------- 读写 ----------------

        public static StudioConfig Load()
        {
            var cfg = new StudioConfig();
            try
            {
                var file = ConfigFile;
                if (File.Exists(file))
                {
                    var root = JsonNode.Parse(File.ReadAllText(file, Encoding.UTF8)) as JsonObject;
                    if (root != null)
                    {
                        cfg.GameRoot = (string)root["gameRoot"];
                        cfg.ModsRoot = (string)root["modsRoot"];
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(cfg.GameRoot) || !Directory.Exists(cfg.ManagedPath))
            {
                cfg.GameRoot = DetectGameRoot();
            }
            if (string.IsNullOrEmpty(cfg.ModsRoot))
            {
                cfg.ModsRoot = string.IsNullOrEmpty(cfg.GameRoot)
                    ? ""
                    : Path.Combine(cfg.GameRoot, "BepInEx", "DiceomancerMods");
            }

            // 首次运行或探测到新路径时落盘，之后启动直接读
            try
            {
                if (!File.Exists(ConfigFile) && !string.IsNullOrEmpty(cfg.GameRoot)) { cfg.Save(); }
            }
            catch { }
            return cfg;
        }

        public void Save()
        {
            try
            {
                var node = new JsonObject
                {
                    ["gameRoot"] = GameRoot ?? "",
                    ["modsRoot"] = ModsRoot ?? "",
                };
                File.WriteAllText(ConfigFile, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                    new UTF8Encoding(false));
            }
            catch { }
        }

        // ---------------- 探测 ----------------

        /// <summary>探测游戏根目录：Steam 库清单 → 常见安装位置 → 从 Studio 所在目录上找。</summary>
        private static string DetectGameRoot()
        {
            foreach (var steam in SteamRoots())
            {
                var libraries = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                foreach (var lib in ReadLibraryPaths(libraries))
                {
                    var candidates = new[]
                    {
                        Path.Combine(lib, "steamapps", "common", "DICEOMANCER"),
                        Path.Combine(lib, "SteamApps", "common", "DICEOMANCER"),
                    };
                    foreach (var c in candidates)
                    {
                        if (LooksLikeGame(c)) { return c; }
                    }
                }
                var direct = Path.Combine(steam, "steamapps", "common", "DICEOMANCER");
                if (LooksLikeGame(direct)) { return direct; }
            }

            foreach (var drive in new[] { "C", "D", "E", "F", "G", "H" })
            {
                foreach (var rel in new[] { "SteamLibrary", "Steam", "Games", "deepseek space", "Program Files (x86)\\Steam" })
                {
                    var c = Path.Combine(drive + ":\\", rel, "steamapps", "common", "DICEOMANCER");
                    if (LooksLikeGame(c)) { return c; }
                    c = Path.Combine(drive + ":\\", rel, "DICEOMANCER");
                    if (LooksLikeGame(c)) { return c; }
                }
            }

            return "";
        }

        private static IEnumerable<string> SteamRoots()
        {
            var list = new List<string>();
            foreach (var drive in new[] { "C", "D", "E", "F", "G", "H" })
            {
                list.Add(Path.Combine(drive + ":\\", "Program Files (x86)", "Steam"));
                list.Add(Path.Combine(drive + ":\\", "Steam"));
                list.Add(Path.Combine(drive + ":\\", "SteamLibrary"));
            }
            return list;
        }

        /// <summary>从 libraryfolders.vdf 里捞 "path" 行（不引入 vdf 解析库）。</summary>
        private static IEnumerable<string> ReadLibraryPaths(string vdf)
        {
            var list = new List<string>();
            try
            {
                if (!File.Exists(vdf)) { return list; }
                foreach (var raw in File.ReadAllLines(vdf, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (!line.StartsWith("\"path\"")) { continue; }
                    var parts = line.Split('"');
                    if (parts.Length >= 4)
                    {
                        var p = parts[3].Replace("\\\\", "\\");
                        if (Directory.Exists(p)) { list.Add(p); }
                    }
                }
            }
            catch { }
            return list;
        }

        private static bool LooksLikeGame(string root)
        {
            try
            {
                return !string.IsNullOrEmpty(root) &&
                       Directory.Exists(Path.Combine(root, "Diceomancer_Data", "Managed"));
            }
            catch { return false; }
        }
    }
}
