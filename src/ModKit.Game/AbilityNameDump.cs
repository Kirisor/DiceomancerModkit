using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ability;
using ModKit.Api;
using UnityEngine;
using UnityEngine.Localization;
using Newtonsoft.Json;

namespace ModKit.Game
{
    /// <summary>
    /// 导出原版能力的**中文名**：能力名只存在于本地化表 `Ability`（键 = abilityInfo.abilityName），
    /// 离线拿不到，所以只能在游戏运行期读一次并写成 JSON，供 ModKit Studio 等外部工具使用。
    /// </summary>
    internal static class AbilityNameDump
    {
        internal const string FileName = "abilities-zh.json";

        internal static void Run(IGameLog log)
        {
            try
            {
                var sos = Resources.LoadAll<AbilitySo>("ScriptableObjects/Abilities/StandardAbilities/");
                var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var so in sos)
                {
                    if (so == null || so.abilityInfo == null) { continue; }
                    var name = so.abilityInfo.abilityName;
                    if (string.IsNullOrEmpty(name) || map.ContainsKey(name)) { continue; }
                    string zh = null;
                    try { zh = new LocalizedString("Ability", name).GetLocalizedString(); } catch { }
                    if (!string.IsNullOrEmpty(zh) && zh != name) { map[name] = zh; }
                }

                var dir = DumpDirectory();
                if (dir == null) { return; }
                Directory.CreateDirectory(dir);

                var payload = new
                {
                    generatedAtUtc = DateTime.UtcNow.ToString("o"),
                    source = "LocalizedString(\"Ability\", abilityName) @ Resources.LoadAll<AbilitySo>",
                    count = map.Count,
                    abilities = map,
                };
                File.WriteAllText(
                    Path.Combine(dir, FileName),
                    JsonConvert.SerializeObject(payload, Formatting.Indented),
                    new UTF8Encoding(false));

                if (log != null) { log.Info("能力中文名已导出：" + map.Count + " 条 → ModKitDump/" + FileName); }
            }
            catch (Exception ex)
            {
                if (log != null) { log.Warn("能力中文名导出失败 —— " + ex.Message); }
            }
        }

        private static string DumpDirectory()
        {
            try
            {
                // 本程序集位于 BepInEx\plugins，Dump 目录固定在 BepInEx\ModKitDump
                var plugins = Path.GetDirectoryName(typeof(AbilityNameDump).Assembly.Location);
                var bep = Path.GetDirectoryName(plugins ?? "");
                return string.IsNullOrEmpty(bep) ? null : Path.Combine(bep, "ModKitDump");
            }
            catch { return null; }
        }
    }
}
