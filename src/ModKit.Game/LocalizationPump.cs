// =============================================================================
//  ModKit.Game · 文案注入泵
//
//  把 mod 的多语言文案写进游戏的 Unity Localization StringTable。
//
//  ★ 这一块几乎照搬现役已实证做法（mod/DiceomancerMod/Localization/ModLocalization.cs），
//    因为写错的代价很高：
//
//    **运行时 AddEntry 返回的条目默认不是 Smart，而我们的文案里有 {Number} 这类
//      命名占位符。Unity Localization 会按 String.Format 处理，遇到 {Number}
//      直接抛 FormatException —— 表现为游戏里弹出「哦不！有错误！」，
//      而日志里**零 Error**。**
//
//    所以本平台把 IsSmart = true 写死在泵里，**不提供关闭选项** ——
//    这是"把静默失败变成不可能"，不是可配置项。
//
//  另外两条来自现役实现的经验：
//    · 注入按语言码**惰性执行**：语言一变，下次查询重新写一遍。
//      既避免同语言重复 AddEntry，也不需要监听事件（在事件回调里做同步加载有死锁风险）。
//    · 失败不致命：文案没写上，卡仍可用（只是显示 key 名）。所以整段包 try/catch，
//      与"接缝边界允许降级"一致。
// =============================================================================

using System;
using System.Collections.Generic;
using ModKit.Api;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace ModKit.Game
{
    /// <summary>一次注入的结果，供哨兵日志核对。</summary>
    public sealed class LocalizationPumpResult
    {
        public string LocaleCode;
        public int Written;
        public int SkippedMissingTable;
        public readonly List<string> MissingTables = new List<string>();

        /// <summary>读回自检：用游戏自己的查询重载读回来，成功的条数。</summary>
        public int ReadBackOk;

        /// <summary>读回自检：读不回来（或返回"找不到翻译"）的条数。</summary>
        public int ReadBackFailed;

        /// <summary>失败的样例（最多 5 个），便于定位是哪张表哪个键。</summary>
        public readonly List<string> FailedSamples = new List<string>();

        public override string ToString()
        {
            var s = string.Format("语言 {0}：写入 {1} 条", LocaleCode, Written);
            if (MissingTables.Count > 0)
            {
                s += "，缺表 " + MissingTables.Count + " 个（" + string.Join(", ", MissingTables.ToArray()) + "）";
            }

            // ★ 读回自检结果必须出现在摘要里 —— 否则"写入 9 条"会再次骗过我们自己。
            s += string.Format("｜读回自检 通过 {0} / 失败 {1}", ReadBackOk, ReadBackFailed);
            if (ReadBackFailed > 0)
            {
                s += "（失败样例：" + string.Join(", ", FailedSamples.ToArray()) + "）";
            }
            return s;
        }
    }

    /// <summary>
    /// 文案注入泵。
    /// </summary>
    /// <remarks>
    /// 用法：
    /// <code>
    /// var pump = new LocalizationPump(log);
    /// pump.Add("zh-Hans", "CardName", "my_card", "我的卡");
    /// pump.Add("zh-Hans", "FlavorText", "my_card", "……");
    /// var result = pump.Flush();   // 只在语言与上次不同时真正写盘
    /// </code>
    /// </remarks>
    public sealed class LocalizationPump
    {
        private readonly IGameLog _log;

        /// <summary>语言码 → (表名 → (键 → 文本))。作者可能为多语言各写一份。</summary>
        private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _pending =
            new Dictionary<string, Dictionary<string, Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>上一次真正执行注入的语言码。语言不限时跳过，避免重复 AddEntry。</summary>
        private string _lastFlushedLocale;

        public LocalizationPump(IGameLog log)
        {
            _log = log;
        }

        /// <summary>登记一条文案。同一 (语言, 表, 键) 重复登记时后者覆盖。</summary>
        /// <summary>游戏要求的表键约定：<c>AbilityDescription</c> 的键必须是 <c>&lt;abilityName&gt;_Des</c>。</summary>
        /// <remarks>
        /// ★ 来历（2026-10-11 实机）：能力描述在界面上显示为
        /// <c>No translation found for 'sample_starter.shield_up_Des' in AbilityDescription</c>
        /// —— 游戏自己会在能力名后面接 <c>_Des</c> 再去查表。
        /// 作者不该被迫知道这个约定，所以平台在这里**自动补后缀**。
        /// </remarks>
        private const string AbilityDesSuffix = "_Des";

        /// <summary>规范化键名（按游戏的表约定）。返回规范化后的键。</summary>
        private string NormalizeKey(string table, string key)
        {
            if (table == "AbilityDescription"
                && !key.EndsWith(AbilityDesSuffix, StringComparison.Ordinal))
            {
                string fixedKey = key + AbilityDesSuffix;
                if (_log != null)
                {
                    _log.Info("文案注入：AbilityDescription 的键 " + key +
                        " 按游戏约定补成 " + fixedKey + "（游戏用 <能力名>_Des 查表）。");
                }
                return fixedKey;
            }
            return key;
        }

        public void Add(string localeCode, string table, string key, string text)
        {
            if (string.IsNullOrEmpty(localeCode) || string.IsNullOrEmpty(table) || string.IsNullOrEmpty(key))
            {
                return;
            }

            key = NormalizeKey(table, key);

            Dictionary<string, Dictionary<string, string>> tables;
            if (!_pending.TryGetValue(localeCode, out tables))
            {
                tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
                _pending[localeCode] = tables;
            }

            Dictionary<string, string> entries;
            if (!tables.TryGetValue(table, out entries))
            {
                entries = new Dictionary<string, string>(StringComparer.Ordinal);
                tables[table] = entries;
            }

            entries[key] = text;
        }

        /// <summary>批量登记一个 <see cref="I18nDef"/>。</summary>
        public void AddI18n(I18nDef def)
        {
            if (def == null || def.Tables == null || string.IsNullOrEmpty(def.Language)) { return; }

            foreach (var table in def.Tables)
            {
                foreach (var entry in table.Value)
                {
                    Add(def.Language, table.Key, entry.Key, entry.Value);
                }
            }
        }

        /// <summary>
        /// 把当前语言的文案写进游戏表。
        /// </summary>
        /// <returns>本次（或上次，若语言未变）的注入结果；无可写内容时返回 null。</returns>
        public LocalizationPumpResult Flush()
        {
            try
            {
                var locale = LocalizationSettings.SelectedLocale;
                string code = locale != null ? locale.Identifier.Code : null;

                if (string.IsNullOrEmpty(code))
                {
                    _log.Warn("文案注入：拿不到当前语言码，本次跳过。");
                    return null;
                }

                // 语言没变 → 已经写过，不重复 AddEntry
                if (string.Equals(_lastFlushedLocale, code, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return FlushFor(code);
            }
            catch (Exception ex)
            {
                // 失败不致命：卡仍可用，只是显示 key 名
                _log.Error("文案注入失败（卡牌将显示 key 名）", ex);
                return null;
            }
        }

        private LocalizationPumpResult FlushFor(string code)
        {
            var result = new LocalizationPumpResult { LocaleCode = code };

            var database = LocalizationSettings.StringDatabase;
            if (database == null)
            {
                _log.Warn("文案注入：StringDatabase 为 null（本地化系统尚未就绪），本次跳过。");
                return null;
            }

            Dictionary<string, Dictionary<string, string>> tables;
            if (!_pending.TryGetValue(code, out tables))
            {
                // 这个语言没有登记任何文案 —— 记为已处理，避免每帧重试
                _lastFlushedLocale = code;
                _log.Info("文案注入：语言 " + code + " 没有登记文案，跳过。");
                return result;
            }

            foreach (var table in tables)
            {
                StringTable stringTable;
                try
                {
                    stringTable = database.GetTable(table.Key);
                }
                catch (Exception ex)
                {
                    result.SkippedMissingTable++;
                    result.MissingTables.Add(table.Key);
                    _log.Warn("文案注入：取表 " + table.Key + " 失败 —— " + ex.GetType().Name);
                    continue;
                }

                if (stringTable == null)
                {
                    result.SkippedMissingTable++;
                    result.MissingTables.Add(table.Key);
                    continue;
                }

                foreach (var entry in table.Value)
                {
                    try
                    {
                        // AddEntry 的 string 重载**可能返回 null**（AddEntry.cs:74-98：
                        // FindKeyId 失败时直接 return null）。不判空会 NRE。
                        var added = stringTable.AddEntry(entry.Key, entry.Value);
                        if (added == null)
                        {
                            _log.Warn("文案注入：AddEntry 返回 null，跳过 " + table.Key + "." + entry.Key);
                            continue;
                        }

                        // ★ 不可绕过的关键一步 —— 见文件头注释。
                        // IsSmart 的确切类型是 bool，位于 StringTableEntry（不是 TableEntry）。
                        added.IsSmart = true;

                        result.Written++;

                        // ★★ 读回自检（2026-10-11 新增，最重要的一步）：
                        // 用**游戏自己的那个重载**（4 参 + FallbackBehavior.UseProjectSettings）
                        // 读回来确认。理由：实机出现过"日志说写入 9 条、卡面却显示
                        // `No translation found for … in CardName`"的情况 ——
                        // 只统计"我调了几次 AddEntry"不算证据，**必须用游戏的查询入口读回**。
                        try
                        {
                            string back = database.GetLocalizedString(table.Key, entry.Key, null,
                                FallbackBehavior.UseProjectSettings);
                            if (string.IsNullOrEmpty(back) ||
                                back.IndexOf("No translation found", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                result.ReadBackFailed++;
                                if (result.FailedSamples.Count < 5)
                                {
                                    result.FailedSamples.Add(table.Key + "." + entry.Key);
                                }
                            }
                            else
                            {
                                result.ReadBackOk++;
                            }
                        }
                        catch (Exception vex)
                        {
                            result.ReadBackFailed++;
                            if (result.FailedSamples.Count < 5)
                            {
                                result.FailedSamples.Add(table.Key + "." + entry.Key +
                                    "(读回异常:" + vex.GetType().Name + ")");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _log.Warn("文案注入失败：" + table.Key + "." + entry.Key + " —— " + ex.GetType().Name);
                    }
                }
            }

            _lastFlushedLocale = code;
            _log.Info("文案注入完成：" + result);
            return result;
        }
    }
}
