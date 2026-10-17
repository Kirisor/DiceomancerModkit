using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace ModKit.Studio
{
    /// <summary>
    /// 在编辑器里直接编译当前 mod 的 C# 部分：
    /// <c>&lt;mod&gt;/src/*.cs</c> → <c>&lt;mod&gt;/&lt;modId&gt;.dll</c>。
    /// 引用集与 build-mod.sh 一致（游戏程序集 + ModKit.Api + BepInEx/Harmony）。
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>找 dotnet 可执行文件：PATH → 常见安装位置。</summary>
        private static string FindDotnet()
        {
            var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathVar.Split(Path.PathSeparator))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(dir)) { continue; }
                    var exe = Path.Combine(dir.Trim('"'), "dotnet.exe");
                    if (File.Exists(exe)) { return exe; }
                }
                catch { }
            }
            foreach (var drive in new[] { "C", "D", "E", "F", "G" })
            {
                foreach (var rel in new[] { "Program Files\\dotnet\\dotnet.exe", "dotnet\\dotnet.exe" })
                {
                    var exe = Path.Combine(drive + ":\\", rel);
                    if (File.Exists(exe)) { return exe; }
                }
            }
            return null;
        }

        /// <summary>在 dotnet 安装目录下找最新的 csc.dll。</summary>
        private static string FindCsc(string dotnet)
        {
            try
            {
                if (string.IsNullOrEmpty(dotnet)) { return null; }
                var root = Path.Combine(Path.GetDirectoryName(dotnet) ?? "", "sdk");
                if (!Directory.Exists(root)) { return null; }
                return Directory.GetDirectories(root)
                    .OrderByDescending(x => x, StringComparer.Ordinal)
                    .Select(d => Path.Combine(d, "Roslyn", "bincore", "csc.dll"))
                    .FirstOrDefault(File.Exists);
            }
            catch { return null; }
        }

        private void OnCompileModClick(object sender, RoutedEventArgs e)
        {
            if (_mod == null) { Status("先选一个模组"); return; }
            var modDir = _mod.Dir;
            var srcDir = System.IO.Path.Combine(modDir, "src");
            if (!Directory.Exists(srcDir)) { Status("该 mod 没有 src 目录"); return; }

            var sources = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories);
            if (sources.Length == 0) { Status("src 下没有 .cs 文件"); return; }

            var managed = StudioConfig.Current.ManagedPath;
            var plugins = StudioConfig.Current.PluginsPath;
            var core = System.IO.Path.Combine(StudioConfig.Current.BepInExPath, "core");

            var refNames = new[]
            {
                "mscorlib.dll", "System.dll", "System.Core.dll", "System.Xml.dll", "netstandard.dll",
                "Assembly-CSharp.dll", "UnityEngine.dll", "UnityEngine.CoreModule.dll", "UnityEngine.UI.dll",
                "UnityEngine.UIModule.dll", "UnityEngine.TextRenderingModule.dll", "Unity.TextMeshPro.dll",
                "UnityEngine.LocalizationModule.dll", "Unity.Localization.dll", "Unity.ResourceManager.dll",
                "UnityEngine.ImageConversionModule.dll", "UnityEngine.AudioModule.dll", "FMODUnity.dll",
                "UnityEngine.Physics2DModule.dll", "UnityEngine.PhysicsModule.dll",
                "UnityEngine.InputLegacyModule.dll", "Newtonsoft.Json.dll", "UniTask.dll", "UniTask.Linq.dll",
            };

            var dotnet = FindDotnet();
            var csc = FindCsc(dotnet);
            if (dotnet == null || csc == null)
            {
                Status("找不到 .NET SDK（dotnet/csc）。纯数据 mod 不需要编译；要写 C# 请安装 .NET SDK 后重试");
                return;
            }

            var args = new List<string>
            {
                "exec", csc, "-nologo", "-target:library", "-nostdlib+", "-langversion:9", "-optimize+",
            };

            var output = System.IO.Path.Combine(modDir, _mod.Id + ".dll");
            args.Add("-out:" + output + ".tmp");

            foreach (var n in refNames)
            {
                var p = System.IO.Path.Combine(managed, n);
                if (File.Exists(p)) { args.Add("-r:" + p); }
            }
            foreach (var n in new[] { "ModKit.Api.dll", "ModKit.Core.dll" })
            {
                var p = System.IO.Path.Combine(plugins, n);
                if (File.Exists(p)) { args.Add("-r:" + p); }
            }
            foreach (var n in new[] { "BepInEx.dll", "0Harmony.dll" })
            {
                var p = System.IO.Path.Combine(core, n);
                if (File.Exists(p)) { args.Add("-r:" + p); }
            }
            args.AddRange(sources);

            try
            {
                var psi = new ProcessStartInfo(dotnet)
                {
                    WorkingDirectory = modDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                foreach (var a in args) { psi.ArgumentList.Add(a); }

                var sb = new StringBuilder();
                using (var proc = Process.Start(psi))
                {
                    sb.Append(proc.StandardOutput.ReadToEnd());
                    sb.Append(proc.StandardError.ReadToEnd());
                    proc.WaitForExit();
                    if (proc.ExitCode != 0)
                    {
                        CompileReport.Text = FirstLines(sb.ToString(), 6);
                        Status("编译失败（见下方输出）");
                        return;
                    }
                }

                if (File.Exists(output))
                {
                    File.Copy(output, output + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".bak", true);
                }
                File.Copy(output + ".tmp", output, true);
                File.Delete(output + ".tmp");

                CompileReport.Text = "✓ 编译成功：" + _mod.Id + ".dll（" + new FileInfo(output).Length + " B）";
                Status("已编译 " + _mod.Id + ".dll —— 重启游戏后生效");
            }
            catch (Exception ex)
            {
                CompileReport.Text = "编译异常：" + ex.Message;
                Status("编译异常：" + ex.Message);
            }
        }

        private static string FirstLines(string text, int count)
        {
            if (string.IsNullOrEmpty(text)) { return "（无输出）"; }
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join("\n", lines.Take(count));
        }
    }
}
