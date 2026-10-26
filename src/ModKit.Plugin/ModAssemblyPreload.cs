// =============================================================================
//  ModKit.Plugin · mod 程序集预加载（时序修复，2026-10-26）
//
//  ★ 症状（用户实机）：奶蛙职业在局内退出游戏、重开继续时，启动即崩：
//      ArgumentNullException: Value cannot be null. Parameter name: type
//      Activator.CreateInstance(Type type)
//      ... EffectUtils.GenerateEffectFromEffectInfo
//      ... CardKeyWordUtil.GenerateKeywordFromKeywordInfo
//      ... CardBase.SetByCardInfo ← CampaignManager.DeserializeCampaignInfo
//      ... GameManager.InitializeGame ← GameManager.Start
//
//  ★ 根因：继续游戏时，`GameManager.Start → InitializeGame` 会**立刻**反序列化存档，
//    用存档里的 CardInfo 重建卡牌对象；卡上的关键词效果要按类名解析成游戏类型。
//    而平台的流水线是**随后**由就绪轮询器在战斗场地出现时才跑的，此时注册表里
//    还没有 mod 的程序集 ⇒ `Type.GetType("...EffectSpEffectmf_xxx")` 落空
//    ⇒ Activator.CreateInstance(null) 抛异常。
//    新游戏不走这条路（卡是平台装配后才创建的），所以只有「继续游戏」会崩。
//
//  ★ 修法：在**建内容注册表之前**把各 mod 的 entryAssembly 预加载进来。
//    这里只做文件读取 + Assembly.LoadFrom + 一次反射扫描，不碰任何游戏数据，
//    因此可以安全地在插件 Awake 里执行；之后注册表就能索引到 mod 的关键词类与
//    [ModEffect] 效果类，反序列化时按名字查得到类型。
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using ModKit.Game;

namespace ModKit.Plugin
{
    /// <summary>把各 mod 的入口程序集提前加载进 AppDomain。</summary>
    internal static class ModAssemblyPreload
    {
        private static readonly Regex EntryAssemblyRegex =
            new Regex("\"entryAssembly\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        /// <summary>扫描 mods 根目录，加载每个 mod 声明的入口程序集；返回加载到的程序集。</summary>
        internal static List<Assembly> Load(string configuredRoot, IGameLog log)
        {
            var loaded = new List<Assembly>();
            try
            {
                string root = ResolveModsRoot(configuredRoot);
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                {
                    if (log != null) { log.Warn("预加载 mod 程序集：目录不存在 —— " + root); }
                    return loaded;
                }

                foreach (string dir in Directory.GetDirectories(root))
                {
                    string dirName = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(dirName)) { continue; }
                    if (dirName.StartsWith("_") || dirName.StartsWith(".")) { continue; }

                    string manifest = Path.Combine(dir, "mod.json");
                    if (!File.Exists(manifest)) { continue; }

                    string entry = ReadEntryAssembly(manifest);
                    if (string.IsNullOrEmpty(entry)) { continue; }

                    string dllPath = Path.Combine(dir, entry);
                    if (!File.Exists(dllPath))
                    {
                        if (log != null)
                        {
                            log.Warn("预加载 mod 程序集：" + dirName + " 声明了 entryAssembly=" +
                                           entry + "，但文件不存在（跳过）。");
                        }
                        continue;
                    }

                    try
                    {
                        Assembly assembly = Assembly.LoadFrom(dllPath);
                        loaded.Add(assembly);
                        if (log != null)
                        {
                            log.Info("预加载 mod 程序集：" + dirName + " → " + assembly.GetName().Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (log != null)
                        {
                            log.Error("预加载 mod 程序集失败：" + dirName + " —— " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (log != null) { log.Error("预加载 mod 程序集失败 —— " + ex); }
            }
            return loaded;
        }

        private static string ResolveModsRoot(string configuredRoot)
        {
            if (string.IsNullOrEmpty(configuredRoot))
            {
                return Path.Combine(Paths.BepInExRootPath, "DiceomancerMods");
            }
            return Path.IsPathRooted(configuredRoot)
                ? configuredRoot
                : Path.Combine(Paths.BepInExRootPath, configuredRoot);
        }

        /// <summary>只取 entryAssembly 一个字段，避免为此引入 JSON 依赖。</summary>
        private static string ReadEntryAssembly(string manifestPath)
        {
            try
            {
                string text = File.ReadAllText(manifestPath, Encoding.UTF8);
                Match match = EntryAssemblyRegex.Match(text);
                return match.Success ? match.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
