using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;

namespace ModKit.Studio
{
    /// <summary>
    /// 一键部署：把平台四件套（以及可选的 BepInEx）装进游戏目录，并可把当前 mod 同步到 mods 根目录。
    /// 原则：不做不可逆操作 —— 覆盖前一律备份到 .modkit-bak；游戏在运行时直接拒绝。
    /// </summary>
    public partial class MainWindow
    {
        private const string BakFolder = ".modkit-bak";

        private static readonly string[] PlatformDlls =
        {
            "ModKit.Api.dll",
            "ModKit.Core.dll",
            "ModKit.Game.dll",
            "ModKit.Plugin.dll",
        };

        private static string PayloadDir
        {
            get { return Path.Combine(AppContext.BaseDirectory, "payload"); }
        }

        private sealed class DeployStep
        {
            public string Name;
            public string Status;
            public string Detail;
        }

        private void OnDeployClick(object sender, RoutedEventArgs e)
        {
            var cfg = StudioConfig.Current;
            var steps = new List<DeployStep>();
            try
            {
                if (!cfg.HasGame)
                {
                    Status("先选游戏根目录（含 Diceomancer_Data 的那层）");
                    return;
                }
                if (GameIsRunning())
                {
                    Status("游戏正在运行 —— 先关闭游戏再部署（dll 被占用会写坏）");
                    return;
                }

                var payload = PayloadDir;
                if (!Directory.Exists(payload))
                {
                    Status("这个版本不含部署文件（缺 payload 目录）—— 请下载完整的 Studio 包");
                    return;
                }

                // ① BepInEx
                var bep = cfg.BepInExPath;
                if (!File.Exists(Path.Combine(bep, "core", "BepInEx.dll")))
                {
                    var payloadBep = Path.Combine(payload, "bepinex");
                    if (!Directory.Exists(payloadBep))
                    {
                        Status("检测到未安装 BepInEx，且当前 Studio 包未携带它 —— 请先安装 BepInEx 5.4.23.2 x64");
                        return;
                    }
                    CopyTree(payloadBep, cfg.GameRoot, steps, "BepInEx");
                }
                else
                {
                    steps.Add(new DeployStep { Name = "BepInEx", Status = "跳过", Detail = "已安装" });
                }

                // ② 平台四件套
                var plugins = cfg.PluginsPath;
                Directory.CreateDirectory(plugins);
                var payloadPlugins = Path.Combine(payload, "plugins");
                foreach (var dll in PlatformDlls)
                {
                    var src = Path.Combine(payloadPlugins, dll);
                    var dst = Path.Combine(plugins, dll);
                    if (!File.Exists(src))
                    {
                        steps.Add(new DeployStep { Name = dll, Status = "失败", Detail = "payload 里缺这个文件" });
                        continue;
                    }
                    if (File.Exists(dst) && SameFile(src, dst))
                    {
                        steps.Add(new DeployStep { Name = dll, Status = "跳过", Detail = "已是最新" });
                        continue;
                    }
                    var existed = File.Exists(dst);
                    if (existed) { Backup(dst, Path.Combine(plugins, BakFolder)); }
                    File.Copy(src, dst, true);
                    steps.Add(new DeployStep
                    {
                        Name = dll,
                        Status = existed ? "更新" : "安装",
                        Detail = existed ? "旧文件已备份" : new FileInfo(dst).Length + " B",
                    });
                }

                // ③ 配置（只在缺失时写，绝不覆盖用户设置）
                var cfgDst = Path.Combine(bep, "config", "diceomancer.modkit.cfg");
                var cfgSrc = Path.Combine(payload, "config", "diceomancer.modkit.cfg");
                if (!File.Exists(cfgDst) && File.Exists(cfgSrc))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cfgDst));
                    File.Copy(cfgSrc, cfgDst);
                    steps.Add(new DeployStep { Name = "diceomancer.modkit.cfg", Status = "安装", Detail = "默认配置" });
                }
                else
                {
                    steps.Add(new DeployStep { Name = "diceomancer.modkit.cfg", Status = "跳过", Detail = "保留现有配置" });
                }

                // ④ 当前 mod
                if (_mod != null)
                {
                    var modsRoot = ResolveModsRoot(cfg);
                    var target = Path.Combine(modsRoot, _mod.Id);
                    if (Directory.Exists(target))
                    {
                        var bak = Path.Combine(modsRoot, BakFolder, _mod.Id + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                        CopyTree(target, bak, new List<DeployStep>(), null, true);
                        Directory.Delete(target, true);
                        steps.Add(new DeployStep { Name = "mod: " + _mod.Id, Status = "更新", Detail = "旧版本备份到 " + BakFolder });
                    }
                    else
                    {
                        steps.Add(new DeployStep { Name = "mod: " + _mod.Id, Status = "安装", Detail = target });
                    }
                    Directory.CreateDirectory(target);
                    CopyTree(_mod.Dir, target, new List<DeployStep>(), null, true);
                }

                // ⑤ 报告
                var sb = new StringBuilder();
                foreach (var s in steps)
                {
                    var mark = s.Status == "失败" ? "✗" : (s.Status == "跳过" ? "–" : "✓");
                    sb.AppendLine(mark + " " + s.Name + "  [" + s.Status + "]  " + (s.Detail ?? ""));
                }
                sb.AppendLine();
                sb.AppendLine("部署完成 —— 重启游戏后生效。备份在 " + BakFolder + " 目录。");
                CompileReport.Text = sb.ToString();
                Status("部署完成，重启游戏生效");
            }
            catch (UnauthorizedAccessException)
            {
                Finish(steps, "没有写权限：游戏装在 Program Files 时请以管理员身份运行 Studio");
            }
            catch (Exception ex)
            {
                Finish(steps, "部署出错：" + ex.GetType().Name + " " + ex.Message);
            }
        }

        private void Finish(List<DeployStep> steps, string error)
        {
            var sb = new StringBuilder();
            foreach (var s in steps)
            {
                var mark = s.Status == "失败" ? "✗" : (s.Status == "跳过" ? "–" : "✓");
                sb.AppendLine(mark + " " + s.Name + "  [" + s.Status + "]  " + (s.Detail ?? ""));
            }
            sb.AppendLine();
            sb.AppendLine("中止：" + error);
            CompileReport.Text = sb.ToString();
            Status(error);
        }

        private static bool GameIsRunning()
        {
            try
            {
                return Process.GetProcessesByName("Diceomancer").Length > 0;
            }
            catch { return false; }
        }

        /// <summary>游戏侧生效的 mods 根目录：cfg 的 ModsRoot 为空时用 BepInEx\DiceomancerMods。</summary>
        private static string ResolveModsRoot(StudioConfig cfg)
        {
            try
            {
                var cfgFile = Path.Combine(cfg.BepInExPath, "config", "diceomancer.modkit.cfg");
                if (File.Exists(cfgFile))
                {
                    foreach (var raw in File.ReadAllLines(cfgFile))
                    {
                        var line = raw.Trim();
                        if (!line.StartsWith("ModsRoot")) { continue; }
                        var eq = line.IndexOf('=');
                        if (eq < 0) { continue; }
                        var v = line.Substring(eq + 1).Trim();
                        if (v.Length > 2) { return v; }
                    }
                }
            }
            catch { }
            return Path.Combine(cfg.BepInExPath, "DiceomancerMods");
        }

        private static bool SameFile(string a, string b)
        {
            try
            {
                return Sha256(a) == Sha256(b);
            }
            catch { return false; }
        }

        private static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                return Convert.ToHexString(sha.ComputeHash(fs));
            }
        }

        private static void Backup(string file, string bakDir)
        {
            Directory.CreateDirectory(bakDir);
            var name = Path.GetFileName(file) + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak";
            File.Copy(file, Path.Combine(bakDir, name), true);
        }

        /// <summary>递归复制；skipJunk 时跳过 bin/obj 与 .bak。</summary>
        private static void CopyTree(string src, string dst, List<DeployStep> steps, string label, bool skipJunk = false)
        {
            Directory.CreateDirectory(dst);
            foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, dir);
                if (skipJunk && (rel.EndsWith("bin") || rel.EndsWith("obj") || rel.Contains(Path.DirectorySeparatorChar + BakFolder)))
                {
                    continue;
                }
                Directory.CreateDirectory(Path.Combine(dst, rel));
            }
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, file);
                if (skipJunk && (rel.EndsWith(".bak") || rel.Contains(Path.DirectorySeparatorChar + BakFolder)))
                {
                    continue;
                }
                var target = Path.Combine(dst, rel);
                var parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(parent)) { Directory.CreateDirectory(parent); }
                File.Copy(file, target, true);
            }
            if (label != null)
            {
                steps.Add(new DeployStep { Name = label, Status = "安装", Detail = dst });
            }
        }
    }
}
