// =============================================================================
//  Run History（战绩）· 存储层
//
//  目录结构（默认 <游戏根>\BepInEx\RunHistory）：
//    index.json          列表页用的轻量索引（面板只读它）
//    runs/<runId>.json   一局一个文件（约 8~30 KB，含逐节点快照）
//
//  ★ 三条硬规矩：
//    ① **原子写**：先写 `.tmp` 再替换 —— 崩溃/断电不会留下半截 JSON；
//    ② **索引可重建**：index.json 丢了或坏了，扫 runs/ 就能整个重建；
//    ③ **单条坏文件不阻塞**：读取时逐条 try/catch，坏的那条跳过并记警告。
//
//  ★ 不做"参与云同步"的事：数据目录在 BepInEx 下，不进游戏存档目录。
//    （《杀戮尖塔》的逐局文件撑爆 Steam 云同步是社区里的常见抱怨。）
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using Newtonsoft.Json;

namespace RunHistory
{
    internal static class RunHistoryStore
    {
        /// <summary>数据根目录（绝对路径）。</summary>
        internal static string DataDir { get; private set; }

        /// <summary>局文件目录。</summary>
        internal static string RunsDir { get { return Path.Combine(DataDir, "runs"); } }

        private const string IndexName = "index.json";

        /// <summary>写盘用的序列化设置：可读、不写 null、时间字符串原样保留。</summary>
        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
        };

        private static readonly object Gate = new object();
        private static RunIndex _index;

        /// <summary>初始化目录。必须在任何采集之前调用。</summary>
        internal static void Configure(string dataDirOverride)
        {
            DataDir = string.IsNullOrEmpty(dataDirOverride)
                ? Path.Combine(Paths.BepInExRootPath, "RunHistory")
                : dataDirOverride;

            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(RunsDir);
        }

        // ---------------- 索引 ----------------

        internal static RunIndex Index
        {
            get
            {
                lock (Gate)
                {
                    if (_index == null) { _index = LoadOrRebuildIndex(); }
                    return _index;
                }
            }
        }

        /// <summary>丢掉内存里的索引，下次访问时重新读盘（面板打开时用，能立刻看到刚打完的局）。</summary>
        internal static void ReloadIndex()
        {
            lock (Gate) { _index = null; }
        }

        private static RunIndex LoadOrRebuildIndex()
        {
            string path = Path.Combine(DataDir, IndexName);
            if (File.Exists(path))
            {
                try
                {
                    var idx = JsonConvert.DeserializeObject<RunIndex>(File.ReadAllText(path, Encoding.UTF8), Json);
                    if (idx != null && idx.entries != null) { return idx; }
                }
                catch (Exception ex)
                {
                    Warn("索引读取失败，将扫描 runs/ 重建：" + ex.GetType().Name + " - " + ex.Message);
                }
            }
            return RebuildIndex();
        }

        /// <summary>扫描 runs/ 重建索引（索引丢失/损坏时的兜底）。</summary>
        internal static RunIndex RebuildIndex()
        {
            var idx = new RunIndex();
            try
            {
                foreach (string file in Directory.GetFiles(RunsDir, "*.json"))
                {
                    try
                    {
                        var rec = JsonConvert.DeserializeObject<RunRecord>(File.ReadAllText(file, Encoding.UTF8), Json);
                        if (rec == null || string.IsNullOrEmpty(rec.runId)) { continue; }
                        idx.entries.Add(ToEntry(rec, file));
                    }
                    catch (Exception ex)
                    {
                        Warn("跳过损坏的战绩文件 " + Path.GetFileName(file) + "：" + ex.GetType().Name);
                    }
                }
                idx.entries.Sort(delegate (RunIndexEntry a, RunIndexEntry b)
                {
                    return string.CompareOrdinal(b.startedAt ?? "", a.startedAt ?? "");   // 新的在前
                });
                SaveIndex(idx);
                RunHistoryPlugin.Log.LogInfo("战绩：索引已重建（" + idx.entries.Count + " 局）。");
            }
            catch (Exception ex)
            {
                Error("重建索引失败：" + ex);
            }
            return idx;
        }

        internal static void SaveIndex(RunIndex idx)
        {
            try
            {
                AtomicWrite(Path.Combine(DataDir, IndexName), JsonConvert.SerializeObject(idx, Json));
                _index = idx;
            }
            catch (Exception ex)
            {
                Error("索引写盘失败：" + ex.GetType().Name + " - " + ex.Message);
            }
        }

        // ---------------- 局文件 ----------------

        /// <summary>把一局写盘，并同步索引条目。</summary>
        internal static void SaveRun(RunRecord rec)
        {
            if (rec == null || string.IsNullOrEmpty(rec.runId)) { return; }

            lock (Gate)
            {
                try
                {
                    rec.updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string path = PathOf(rec.runId);
                    AtomicWrite(path, JsonConvert.SerializeObject(rec, Json));

                    var idx = Index;
                    string rel = "runs/" + Path.GetFileName(path);
                    var entry = ToEntry(rec, rel);

                    int at = idx.entries.FindIndex(delegate (RunIndexEntry e) { return e.runId == rec.runId; });
                    if (at >= 0) { idx.entries[at] = entry; } else { idx.entries.Add(entry); }
                    idx.entries.Sort(delegate (RunIndexEntry a, RunIndexEntry b)
                    {
                        return string.CompareOrdinal(b.startedAt ?? "", a.startedAt ?? "");
                    });
                    SaveIndex(idx);

                    if (RunHistoryPlugin.CfgVerbose != null && RunHistoryPlugin.CfgVerbose.Value)
                    {
                        RunHistoryPlugin.Log.LogInfo("战绩：已写入 " + rel + "（节点 " + rec.nodes.Count +
                            "，结果 " + Results.Label(rec.result) + "）。");
                    }
                }
                catch (Exception ex)
                {
                    Error("战绩写盘失败（本局数据可能不完整）：" + ex.GetType().Name + " - " + ex.Message);
                }
            }
        }

        /// <summary>按 seed 找未结束/最近的一局（续玩时归并）。</summary>
        internal static RunRecord FindBySeed(int seed)
        {
            try
            {
                var idx = Index;
                RunIndexEntry best = null;
                for (int i = 0; i < idx.entries.Count; i++)
                {
                    var e = idx.entries[i];
                    if (e.seed != seed) { continue; }
                    // 优先未结束的那条；否则取最新（entries 已按时间倒序）
                    if (e.result == Results.InProgress) { best = e; break; }
                    if (best == null) { best = e; }
                }
                if (best == null) { return null; }
                return LoadRun(best.file);
            }
            catch (Exception ex)
            {
                Warn("按 seed 找战绩失败：" + ex.GetType().Name + " - " + ex.Message);
                return null;
            }
        }

        internal static RunRecord LoadRun(string relativeFile)
        {
            try
            {
                if (string.IsNullOrEmpty(relativeFile)) { return null; }
                string path = Path.Combine(DataDir, relativeFile.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) { return null; }
                return JsonConvert.DeserializeObject<RunRecord>(File.ReadAllText(path, Encoding.UTF8), Json);
            }
            catch (Exception ex)
            {
                Warn("读取战绩文件失败：" + relativeFile + " —— " + ex.GetType().Name);
                return null;
            }
        }

        /// <summary>面板用：按索引读出一局（找不到或损坏返回 null）。</summary>
        internal static RunRecord LoadEntry(RunIndexEntry entry)
        {
            return entry == null ? null : LoadRun(entry.file);
        }

        internal static string PathOf(string runId)
        {
            return Path.Combine(RunsDir, Sanitize(runId) + ".json");
        }

        /// <summary>把 runId 变成安全文件名（只保留字母数字与 -_）。</summary>
        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) { return "run"; }
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            }
            return sb.ToString();
        }

        private static RunIndexEntry ToEntry(RunRecord rec, string file)
        {
            return new RunIndexEntry
            {
                runId = rec.runId,
                startedAt = rec.startedAt,
                className = rec.className,
                result = rec.result,
                durationSeconds = rec.durationSeconds,
                seed = rec.seed,
                nodes = rec.nodes == null ? 0 : rec.nodes.Count,
                ascensionLevel = rec.ascensionLevel,
                upgradeCoins = rec.final != null && rec.final.stats != null
                    ? rec.final.upgradeCoins : 0,
                file = file.Replace('\\', '/'),
            };
        }

        /// <summary>原子写：先写 .tmp，再替换目标文件。</summary>
        internal static void AtomicWrite(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, null); }
                catch
                {
                    File.Copy(tmp, path, true);
                    try { File.Delete(tmp); } catch { }
                }
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        private static void Warn(string message) { RunHistoryPlugin.Log.LogWarning(message); }
        private static void Error(string message) { RunHistoryPlugin.Log.LogError(message); }
    }
}
