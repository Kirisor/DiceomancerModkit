using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ModKit.Api;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using Newtonsoft.Json;

namespace ModKit.Game
{
    /// <summary>
    /// 把游戏本地化表（中文）整表导出成 JSON，供 ModKit Studio 等外部工具直接使用游戏原文。
    /// 只做只读读取，不修改任何游戏数据。表名与键名都是游戏自己的（例如 Trigger / Rarity）。
    /// </summary>
    internal static class LocalizedTextDump
    {
        internal const string FileName = "localization-zh.json";

        /// <summary>候选表名：存在就整个导出。未知表名会被跳过，不会报错。</summary>
        private static readonly string[] CandidateTables =
        {
            "Trigger", "Rarity", "ResolveTiming", "Ability", "AbilityDescription",
            "KeyWord", "CardName", "CardRequirementDes", "FlavorText", "Relic", "EffectDescription",
            "Encyclopedia", "Command", "EnemyIntention", "Card", "UI",
        };

        internal static void Run(IGameLog log)
        {
            try
            {
                // 优先拿简体中文的 Locale 对象；拿不到就用当前语言
                var locale = LocalizationSettings.AvailableLocales.GetLocale(new LocaleIdentifier("zh-Hans"));
                if (locale == null) { locale = LocalizationSettings.SelectedLocale; }
                var db = LocalizationSettings.StringDatabase;
                var payload = new Dictionary<string, object>(StringComparer.Ordinal);
                int tableCount = 0;
                int entryCount = 0;

                foreach (var tableName in CandidateTables)
                {
                    try
                    {
                        var table = db.GetTable(tableName, locale);
                        if (table == null) { continue; }
                        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
                        foreach (var pair in table.Values)
                        {
                            var key = pair.Key;
                            var value = pair.Value;
                            if (key != null && !string.IsNullOrEmpty(value)) { entries[key] = value; }
                        }
                        if (entries.Count == 0) { continue; }
                        payload[tableName] = entries;
                        tableCount++;
                        entryCount += entries.Count;
                    }
                    catch { }
                }

                var dir = DumpDirectory();
                if (dir == null) { return; }
                Directory.CreateDirectory(dir);
                File.WriteAllText(
                    Path.Combine(dir, FileName),
                    JsonConvert.SerializeObject(new
                    {
                        generatedAtUtc = DateTime.UtcNow.ToString("o"),
                        locale = "zh-Hans",
                        source = "LocalizationSettings.StringDatabase.GetTable(table, zh-Hans)",
                        tables = tableCount,
                        entries = entryCount,
                        data = payload,
                    }, Formatting.Indented),
                    new UTF8Encoding(false));

                if (log != null) { log.Info("本地化文本已导出：" + tableCount + " 张表 / " + entryCount + " 条 → ModKitDump/" + FileName); }
            }
            catch (Exception ex)
            {
                if (log != null) { log.Warn("本地化文本导出失败 —— " + ex.Message); }
            }
        }

        private static string DumpDirectory()
        {
            try
            {
                var plugins = Path.GetDirectoryName(typeof(LocalizedTextDump).Assembly.Location);
                var bep = Path.GetDirectoryName(plugins ?? "");
                return string.IsNullOrEmpty(bep) ? null : Path.Combine(bep, "ModKitDump");
            }
            catch { return null; }
        }
    }
}
