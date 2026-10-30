// =============================================================================
//  ModKit.Game · 能力表落盘（诊断导出）
//
//  为什么需要它（2026-10-13 记录的两个"只有数量、没有名字"的缺口）：
//    ① 原版遗物名：GameCapabilitiesProbe.ProbeRelicNames 早就取到了**完整**名单
//       （实机 ~205 件），但只打了一行「原版遗物名探测：205 件」——
//       名字没有任何去处。仓库里那两份 capabilities.json 是**手工快照**，
//       vanillaRelicNames 因此只有 1 项，遗物的 artTemplate 校验形同虚设。
//    ② 费用槽组合：ProbeCostCombos 枚举了 ManaIconDic 的全部键（含顺序），
//       同样只打日志（[cost-combos] 行），没有机器可读的落盘形态。
//
//  ★ 本文件是**只读导出的落盘端**：它不改游戏状态、不改存档、不改玩法，
//    只把已经在内存里的探测结果写成 JSON 文件。
//
//  ★ 输出路径确定且**打印在日志里**：
//      <游戏根>\BepInEx\ModKitDump\capabilities.json
//    （<游戏根> = BepInEx.Paths.GameRootPath；不写进 Diceomancer_Data，避免碰 A 级资产。）
//
//  ★ 写入是**原子**的：先写 <目标>.tmp，再用 File.Replace / File.Move 顶替。
//    中途失败不会留下半截 JSON（半截 JSON 会让消费方解析失败且很难发现）。
//
//  ★ 失败**明确报错**，不静默：写盘异常一律 log.Error（带异常），并把路径失败如实抛出。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ModKit.Api;
using ModKit.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ModKit.Game
{
    /// <summary>
    /// 把启动探测到的**完整能力表**写成 JSON 文件（诊断导出，只读）。
    /// </summary>
    /// <remarks>
    /// 产出的键与 <c>modkit\mods\capabilities.json</c> 同构（同名键同语义），
    /// 额外的键一律以 <c>_</c> 开头（<c>_counts</c> / <c>_sources</c> / …），
    /// 所以即使有人把它直接拿去当 lint 的能力表用，也不会污染既有解析。
    /// </remarks>
    public static class CapabilitiesDump
    {
        /// <summary>文件自述的 schema 标识（消费方用它区分"手工快照"与"机器导出"）。</summary>
        public const string SchemaId = "modkit-capabilities-dump/1";

        /// <summary>文件名（固定）。</summary>
        public const string FileName = "capabilities.json";

        /// <summary>导出目录名（挂在 <c>&lt;游戏根&gt;\BepInEx\</c> 下）。</summary>
        public const string DirectoryName = "ModKitDump";

        /// <summary>开关。由插件从配置写入（默认开）。</summary>
        public static bool Enabled = true;

        /// <summary>导出目录覆盖。<c>null</c>/空 = 用 <see cref="DefaultDirectory"/>。</summary>
        public static string DirectoryOverride;

        /// <summary><c>&lt;游戏根&gt;\BepInEx\ModKitDump</c>。</summary>
        public static string DefaultDirectory
        {
            get
            {
                return Path.Combine(BepInEx.Paths.GameRootPath, "BepInEx", DirectoryName);
            }
        }

        /// <summary>解析实际导出目录（配置覆盖优先）。</summary>
        public static string ResolveDirectory()
        {
            return string.IsNullOrEmpty(DirectoryOverride) ? DefaultDirectory : DirectoryOverride;
        }

        /// <summary>
        /// 导出并返回文件路径。**任何异常都只影响这一项**（记 Error，返回 null），
        /// 绝不让诊断导出把平台流水线带崩。
        /// </summary>
        public static string TryWrite(IGameLog log, GameCapabilities caps, ProbeSummary probe)
        {
            if (!Enabled) { return null; }

            try
            {
                return Write(log, caps, probe);
            }
            catch (Exception ex)
            {
                if (log != null)
                {
                    log.Error("[capabilities-dump] 能力表落盘失败（其余功能不受影响）：" + ex.Message, ex);
                }
                return null;
            }
        }

        /// <summary>导出（异常向上抛，调用方决定怎么降级）。</summary>
        public static string Write(IGameLog log, GameCapabilities caps, ProbeSummary probe)
        {
            if (caps == null) { throw new ArgumentNullException("caps"); }

            string dir = ResolveDirectory();
            string path = Path.Combine(dir, FileName);

            JObject root = Build(caps, probe, dir);
            string text = JsonConvert.SerializeObject(root, Formatting.Indented).Replace("\r\n", "\n");

            AtomicWrite(path, text);

            long size = new FileInfo(path).Length;

            // ★ 这一行是给用户的"去哪找"路标，必须打出来（验收要求：输出路径确定且打印在日志里）。
            if (log != null)
            {
                log.Info("[capabilities-dump] 完整能力表已写入：" + path + "（" + size + " 字节）");
                log.Info("[capabilities-dump] 条数：" + Describe(root));
            }

            return path;
        }

        /// <summary>日志里那行条数摘要（与 JSON 里的 <c>_counts</c> 同源）。</summary>
        private static string Describe(JObject root)
        {
            var counts = root["_counts"] as JObject;
            if (counts == null) { return "(无)"; }

            var parts = new List<string>();
            foreach (var p in counts.Properties())
            {
                parts.Add(p.Name + " " + p.Value);
            }
            return string.Join(" / ", parts.ToArray());
        }

        /// <summary>
        /// 构造 JSON 对象（显式建 <see cref="JObject"/> 而不是序列化匿名对象：
        /// 键的**插入顺序**因此是确定的，人读与 diff 都稳定）。
        /// </summary>
        private static JObject Build(GameCapabilities caps, ProbeSummary probe, string dir)
        {
            var colors = new List<string>();
            foreach (var c in caps.AvailableCostColors) { colors.Add(c.ToString()); }

            // ★ 费用槽组合必须**排序**后才落盘：它来自 Dictionary 的键枚举，
            //   枚举顺序在 .NET 里没有保证 ⇒ 不排序的话同一份游戏数据两次导出可能字节不同。
            //   注意只排序"组合之间"，每个组合**内部的颜色顺序原样保留**（顺序参与匹配）。
            var combos = new List<string[]>();
            foreach (var combo in caps.AvailableCostCombos)
            {
                var arr = new string[combo.Count];
                for (int i = 0; i < combo.Count; i++) { arr[i] = combo[i].ToString(); }
                combos.Add(arr);
            }
            combos.Sort(delegate (string[] a, string[] b)
            {
                return string.CompareOrdinal(string.Join("+", a), string.Join("+", b));
            });

            var root = new JObject();
            root["$schema"] = SchemaId;
            root["_generatedAtUtc"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            root["_gameRoot"] = BepInEx.Paths.GameRootPath;
            root["_dumpDirectory"] = dir;
            root["_note"] =
                "由 ModKit.Game.CapabilitiesDump 在游戏运行期导出（只读探测，不改游戏状态/存档/玩法）。" +
                "键名与 modkit\\mods\\capabilities.json 同构；以 _ 开头的键是本文件的元信息。" +
                "knownCostCombos 是本导出**新增**的键（手工快照里没有），顺序参与匹配。";

            // ---- 探测来源与条数（每一条清单都注明"从哪来""几个"）----
            var sources = new JObject();
            AddSource(sources, "vanillaCardNames",
                "CardDataManager.Instance.GetAllCardsCardDateCollectionsInGame()[].cardName（去重 + Ordinal 排序）",
                caps.VanillaCardNames.Count);
            AddSource(sources, "vanillaRelicNames",
                "DataManager.Instance.GetRelicDataManager().GetAllInGameRelicSo()[].relicInfo.relicName（去重 + Ordinal 排序）",
                caps.VanillaRelicNames.Count);
            AddSource(sources, "availableCostColors",
                "ManaManager.Instance.ManaIconDic 逐单色 ContainsKey(new ColorEArray(c))（ModKit.Game.GameCapabilitiesProbe.ProbeColors）",
                colors.Count);
            AddSource(sources, "knownCostCombos",
                "ManaManager.Instance.ManaIconDic **全部键**（含顺序；已按组合字符串 Ordinal 排序后落盘）（ProbeCostCombos）",
                combos.Count);
            AddSource(sources, "knownKeywords",
                "CardKeyWordUtil.AllKeywordStrings（游戏自己的权威注册名名单）",
                caps.KnownKeywords.Count);
            AddSource(sources, "knownTriggerNames",
                "ModKit.Core.GameFacts.ActualTriggerNames —— 游戏**实际发出**的触发点名，不是 AllTriggerType 枚举",
                caps.KnownTriggerNames.Count);
            AddSource(sources, "knownCustomEffectIds",
                "ModKit.Core.ContentRegistry 扫描 AppDomain 得到（[ModEffect] / 平台内置）",
                caps.KnownCustomEffectIds.Count);
            root["_sources"] = sources;

            int failures = probe != null ? probe.Failures.Count : 0;
            var counts = new JObject();
            counts["vanillaCardNames"] = caps.VanillaCardNames.Count;
            counts["vanillaRelicNames"] = caps.VanillaRelicNames.Count;
            counts["availableCostColors"] = colors.Count;
            counts["knownCostCombos"] = combos.Count;
            counts["knownKeywords"] = caps.KnownKeywords.Count;
            counts["knownTriggerNames"] = caps.KnownTriggerNames.Count;
            counts["knownCustomEffectIds"] = caps.KnownCustomEffectIds.Count;
            counts["probeFailures"] = failures;
            root["_counts"] = counts;

            // ---- 清单本体 ----
            root["vanillaCardNames"] = ToArray(caps.VanillaCardNames);
            root["vanillaRelicNames"] = ToArray(caps.VanillaRelicNames);
            root["availableCostColors"] = ToArray(colors);
            root["knownKeywords"] = ToArray(caps.KnownKeywords);
            root["knownTriggerNames"] = ToArray(caps.KnownTriggerNames);
            root["knownCustomEffectIds"] = ToArray(caps.KnownCustomEffectIds);

            var comboArray = new JArray();
            foreach (var c in combos)
            {
                var one = new JArray();
                foreach (var s in c) { one.Add(s); }
                comboArray.Add(one);
            }
            root["knownCostCombos"] = comboArray;

            // ---- 探测失败项（有值就说明能力表不完整，消费方必须看得见）----
            var failArray = new JArray();
            if (probe != null)
            {
                foreach (var f in probe.Failures) { failArray.Add(f); }
            }
            root["probeFailures"] = failArray;

            // 兼容：哨兵汇总里那一行文本也留一份，便于和日志对照
            root["_probeSummary"] = probe != null ? probe.ToString() : "(无)";

            // ---- 特殊卡牌集合（BasicCard = 游戏里的「基础牌」身份）----
            //  为什么落盘：游戏判定「是不是基础牌」**不看卡的任何字段**，只看卡在不在具名集合里
            //  （`CardUtils.CheckCardCoreInfoSpecialRequirement` 的 `"BasicCard"` 分支逐字如此）。
            //  mod 职业的初始牌要靠平台补进这个集合，所以这份原版清单就是那条逻辑的**证据**：
            //  只有它写清"原版基础牌到底是哪些"，才能确认"按初始牌组补身份"是对的。
            //  ★ 落盘时机在**加载 mod 之前**（见 PlatformRuntime），所以这里看到的是纯原版内容。
            root["specialCardCollections"] = DumpSpecialCardCollections();

            return root;
        }

        /// <summary>
        /// 特殊卡牌集合：每个集合只写张数，<c>BasicCard</c> 额外写全名单（它是「基础牌」身份）。
        /// </summary>
        private static JObject DumpSpecialCardCollections()
        {
            var result = new JObject();
            try
            {
                var cdm = GameData.CardDataManager.Instance;
                if (cdm == null)
                {
                    result["_error"] = "CardDataManager.Instance 为 null（本次没导出集合）";
                    return result;
                }

                var map = Reflect.Get<Dictionary<string, Card.CardDateCollection>>(cdm, "_cardDateCollectionDictionary");
                if (map == null)
                {
                    result["_error"] = "_cardDateCollectionDictionary 取不到（本次没导出集合）";
                    return result;
                }

                // Dictionary 的枚举顺序在 .NET 里没保证 ⇒ 排序，保证同一份游戏数据两次导出字节一致
                var keys = new List<string>(map.Keys);
                keys.Sort(StringComparer.Ordinal);

                var counts = new JObject();
                var basic = new List<string>();
                var cols = new JArray();
                foreach (var key in keys)
                {
                    var col = map[key];
                    if (col == null) { counts[key] = 0; continue; }

                    var names = new List<string>();
                    try
                    {
                        foreach (var c in col.GetAllCards())
                        {
                            if (c != null && !string.IsNullOrEmpty(c.cardName)) { names.Add(c.cardName); }
                        }
                    }
                    catch { /* 集合本身坏了不该拖垮整份导出 */ }

                    names.Sort(StringComparer.Ordinal);   // 枚举顺序无保证 ⇒ 排序，保证字节稳定
                    counts[key] = names.Count;
                    if (key == "BasicCard") { basic = names; }

                    // ---- 新增：集合元信息 + 名单 ----
                    // 为什么需要它：职业卡池是「本局三色 ⊇ 集合 colorEs」筛出来的，
                    // 而 colorEs / 类型只有运行期才知道（资产里看不出来）。没有这份元信息，
                    // 就无法回答「某个职业到底自动吃进了哪些原版卡」。
                    // ★ 只对 Class / Standard 两类写名单（它们才是 `_colorCardDateCollections`
                    //   的成员、才会进战役池）；其余特殊集合只给张数，避免把导出撑大。
                    var one = new JObject();
                    one["name"] = key;
                    var typeText = col.cardCollectionType.ToString();
                    one["type"] = typeText;
                    one["inRandomPool"] = col.isInRandomPool;
                    var colorsArr = new JArray();
                    if (col.colorEs != null)
                    {
                        foreach (var c in col.colorEs) { colorsArr.Add(c.ToString()); }
                    }
                    one["colors"] = colorsArr;
                    one["count"] = names.Count;
                    if (typeText == "Class" || typeText == "Standard")
                    {
                        one["cards"] = ToArray(names);
                    }
                    cols.Add(one);
                }

                result["_counts"] = counts;
                result["basicCards"] = ToArray(basic);
                result["collections"] = cols;
                result["_note"] =
                    "BasicCard 就是游戏口径的「基础牌」：效果里的「升级随机 N 张基础牌」" +
                    "（EffectSpEffectUpgradeRandomBasicCard / SpecialStoryUpgradeBasicCards）与 " +
                    "CardUtils 的 \"BasicCard\" 特判都只认这个集合。" +
                    " collections 里 type=Class/Standard 的集合才会进战役池，筛选条件是 " +
                    "collection.colorEs ⊆ 本局三色；它们的 cards 就是全部成员名。";
            }
            catch (Exception ex)
            {
                result["_error"] = ex.GetType().Name + ": " + ex.Message;
            }
            return result;
        }

        private static void AddSource(JObject into, string key, string origin, int count)
        {
            var o = new JObject();
            o["count"] = count;
            o["origin"] = origin;
            into[key] = o;
        }

        private static JArray ToArray(IReadOnlyList<string> items)
        {
            var arr = new JArray();
            if (items == null) { return arr; }
            for (int i = 0; i < items.Count; i++) { arr.Add(items[i]); }
            return arr;
        }

        /// <summary>
        /// 原子写：先写 <c>&lt;目标&gt;.tmp</c>，成功后再顶替目标文件。
        /// </summary>
        /// <remarks>
        /// 为什么不用 <c>File.WriteAllText(目标)</c>：写到一半被杀/抛异常会留下**半截 JSON**，
        /// 而半截 JSON 在消费方看来只是"解析失败"，很难回溯是谁写的。
        /// 这里保证：目标路径上要么是旧的完整内容，要么是新的完整内容。
        /// </remarks>
        internal static void AtomicWrite(string path, string text)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tmp = path + ".tmp";
            var utf8NoBom = new UTF8Encoding(false);

            try
            {
                File.WriteAllText(tmp, text, utf8NoBom);

                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tmp, path, null, true);
                    }
                    catch (Exception)
                    {
                        // 某些文件系统/占用场景不支持 Replace（或目标被别的进程打开）：
                        // 退化成"删旧 + 改名"。仍然不会留下半截文件。
                        File.Delete(path);
                        File.Move(tmp, path);
                    }
                }
                else
                {
                    File.Move(tmp, path);
                }
            }
            finally
            {
                try { if (File.Exists(tmp)) { File.Delete(tmp); } } catch { }
            }
        }
    }
}
