// =============================================================================
//  ModKit.Game · 原版卡面导出（诊断导出）
//
//  回答用户的两个问题：
//    ① "游戏内到底有哪些卡？" —— 一份机器导出的 cards.json（不是手工快照）。
//    ② "把卡图也加进去。"     —— 每张卡的卡面图导出成 PNG（默认只出缩略图）。
//
//  ★ 只读：不改游戏状态/存档/玩法。唯一的写目标是
//      <游戏根>\BepInEx\ModKitDump\cards.json
//      <游戏根>\BepInEx\ModKitDump\art\<internalName>.png
//    （刻意不写 Diceomancer_Data —— 那是受保护的 A 级资产。）
//
//  ★ "名字 → 图"的对应关系**由游戏自己给**：直接用卡 SO 自己的
//    <c>CardSo.cardArt</c>（Sprite），**不按名字猜资源路径**。
//    猜路径这件事一旦错，导出的就是"看起来对但其实是别的卡"的图，比导不出来更坏。
//
//  ★ 尺寸：默认最长边 256px 缩略图（文档用不上原图）；原图导出是配置开关，默认关。
//
//  ★ 去重：多张卡共用同一张图（同纹理 **且** 同图集矩形）时只写一个 PNG，
//    清单里复用同一个文件名 —— 避免 500+ 张重复 PNG。
//
//  ★ 安全阀：总量上限（MaxCards）、单卡失败只记 error 并跳过、
//    整个导出包在 try/catch 里 —— 绝不让导出把游戏启动/平台流水线炸掉。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Card;
using GameData;
using ModKit.Api;
using ModKit.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace ModKit.Game
{
    /// <summary>一次卡面导出的结果（供哨兵日志核对）。</summary>
    public sealed class CardArtExportResult
    {
        public int CardCount;
        public int UniqueArtCount;
        public int ArtWritten;
        public int ArtReused;
        public int ArtFailed;
        public int CardErrors;
        public bool Truncated;
        public double ElapsedMs;
        public string ArtDirectory;
        public string CardsJsonPath;

        public override string ToString()
        {
            return string.Format(
                "卡 {0} 张 / 去重后图 {1} 张（新写 {2}，复用 {3}，失败 {4}）/ 清单内 error {5}{6}｜耗时 {7:F0} ms",
                CardCount, UniqueArtCount, ArtWritten, ArtReused, ArtFailed, CardErrors,
                Truncated ? "（已按上限截断）" : "", ElapsedMs);
        }
    }

    /// <summary>
    /// 枚举游戏内的卡 SO，导出卡面图（PNG）与卡清单（JSON）。
    /// </summary>
    public static class CardArtExporter
    {
        /// <summary>总开关。由插件从配置写入。</summary>
        public static bool Enabled = true;

        /// <summary>除缩略图外，再导出一份原图（默认关）。</summary>
        public static bool ExportFullArt = false;

        /// <summary>缩略图最长边（像素）。</summary>
        public static int ThumbnailMaxEdge = 256;

        /// <summary>总量上限（安全阀）。超过时按内部名 Ordinal 排序取前 N 张并如实标注。</summary>
        public static int MaxCards = 2000;

        /// <summary>
        /// 已存在的 PNG 是否跳过重写。默认 <c>true</c>：
        /// 第二次开游戏时不必再为 500+ 张图做 GPU 取回 + PNG 编码（那是启动期的真实卡顿来源）。
        /// </summary>
        public static bool ReuseExistingArt = true;

        /// <summary>
        /// 取回像素时是否上下翻转。默认 <c>false</c>（RenderTexture → ReadPixels 的常规结果）。
        /// 留这个开关是因为 <c>Graphics.Blit</c> 的朝向依赖图形后端，实机若发现图是倒的就打开它。
        /// </summary>
        public static bool FlipVertically = false;

        /// <summary>导出目录覆盖。<c>null</c>/空 = 用 <see cref="CapabilitiesDump.DefaultDirectory"/>。</summary>
        public static string DirectoryOverride;

        public const string CardsFileName = "cards.json";
        public const string ArtSubDirectory = "art";
        public const string FullArtSubDirectory = "full";

        public static string ResolveDirectory()
        {
            return string.IsNullOrEmpty(DirectoryOverride) ? CapabilitiesDump.DefaultDirectory : DirectoryOverride;
        }

        /// <summary>
        /// 导出。**任何异常都只影响这一项** —— 记 Error 后返回 null，绝不带崩平台。
        /// </summary>
        public static CardArtExportResult TryExport(IGameLog log)
        {
            if (!Enabled) { return null; }

            try
            {
                return Export(log);
            }
            catch (Exception ex)
            {
                if (log != null)
                {
                    log.Error("[cards-dump] 卡面导出失败（平台其余功能不受影响）：" + ex.Message, ex);
                }
                return null;
            }
        }

        /// <summary>执行导出（异常向上抛，由 <see cref="TryExport"/> 降级）。</summary>
        public static CardArtExportResult Export(IGameLog log)
        {
            var sw = Stopwatch.StartNew();
            var result = new CardArtExportResult();

            var mgr = CardDataManager.Instance;
            if (mgr == null)
            {
                if (log != null) { log.Warn("[cards-dump] CardDataManager.Instance 为 null（数据尚未加载），本次跳过。"); }
                return null;
            }

            CardSo[] all = mgr.GetAllCardsCardDateCollectionsInGame();
            if (all == null || all.Length == 0)
            {
                if (log != null) { log.Warn("[cards-dump] GetAllCardsCardDateCollectionsInGame() 为空，本次跳过。"); }
                return null;
            }

            string dir = ResolveDirectory();
            string artDir = Path.Combine(dir, ArtSubDirectory);
            string fullDir = Path.Combine(artDir, FullArtSubDirectory);

            if (DiagnosticSwitches.ExportCardsArtPng) { Directory.CreateDirectory(artDir); }
            if (DiagnosticSwitches.ExportCardsArtPng && ExportFullArt) { Directory.CreateDirectory(fullDir); }

            // ---- 战役卡池名单（"这张卡会不会在战役里出现"）----
            var pool = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var pooled = mgr.GetAllCardsCardDateCollectionsInRandomPool();
                if (pooled != null)
                {
                    foreach (var so in pooled)
                    {
                        if (so != null && !string.IsNullOrEmpty(so.cardName)) { pool.Add(so.cardName); }
                    }
                }
            }
            catch (Exception ex)
            {
                if (log != null) { log.Warn("[cards-dump] 取战役卡池失败（inCampaignPool 将全为 false）：" + ex.GetType().Name); }
            }

            // ---- 语言清单（尽量多取；取不到就只写当前语言）----
            var locales = new List<Locale>();
            try
            {
                var provider = LocalizationSettings.AvailableLocales;
                if (provider != null && provider.Locales != null) { locales.AddRange(provider.Locales); }
            }
            catch (Exception ex)
            {
                if (log != null) { log.Warn("[cards-dump] 取可用语言列表失败（显示名可能不全）：" + ex.GetType().Name); }
            }

            // 确定性：按内部名 Ordinal 排序后再截断/导出（Dictionary 的枚举顺序没有保证）
            var cards = new List<CardSo>(all.Length);
            foreach (var so in all) { if (so != null && !string.IsNullOrEmpty(so.cardName)) { cards.Add(so); } }
            cards.Sort(delegate (CardSo a, CardSo b) { return string.CompareOrdinal(a.cardName, b.cardName); });

            // 去重（内部名相同只留一份）
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            var unique = new List<CardSo>(cards.Count);
            foreach (var so in cards) { if (seenNames.Add(so.cardName)) { unique.Add(so); } }

            if (MaxCards > 0 && unique.Count > MaxCards)
            {
                result.Truncated = true;
                unique.RemoveRange(MaxCards, unique.Count - MaxCards);
            }

            result.CardCount = unique.Count;

            if (log != null)
            {
                log.Info("[cards-dump] 开始导出卡面：候选卡 " + all.Length + " 张（去重后 " + result.CardCount +
                    " 张）｜缩略图最长边 " + ThumbnailMaxEdge + "px｜原图 " + (ExportFullArt ? "导出" : "不导出") +
                    "｜目录 " + dir);
            }

            // ---- 图去重：key = 纹理实例ID + 图集矩形 ----
            //   ★ 只按纹理 ID 去重是**错的**：同一张图集里的两张卡纹理相同、矩形不同，
            //     那样会把 A 卡的图当成 B 卡的图。所以矩形必须参与 key。
            var artByKey = new Dictionary<string, ArtFile>(StringComparer.Ordinal);
            var usedFileNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var cardNodes = new JArray();

            for (int i = 0; i < unique.Count; i++)
            {
                var so = unique[i];
                JObject node;
                try
                {
                    node = DescribeCard(so, pool, locales);
                }
                catch (Exception ex)
                {
                    // 单卡失败不影响别的卡：仍然输出一条（用户要的是"游戏里有哪些卡"，不是"哪些卡没报错"）
                    node = new JObject();
                    node["internalName"] = so.cardName;
                    node["error"] = "读取卡数据失败：" + ex.GetType().Name + "：" + ex.Message;
                    result.CardErrors++;
                }

                // ---- 卡图 ----
                try
                {
                    string artError;
                    string rel = ExportOneArt(so, artDir, fullDir, artByKey, usedFileNames, out artError,
                        ref result, log);
                    if (rel != null) { node["image"] = rel; }
                    else
                    {
                        node["image"] = null;
                        node["error"] = Merge(node["error"] != null ? node["error"].ToString() : null, artError);
                        result.ArtFailed++;
                        result.CardErrors++;
                    }
                }
                catch (Exception ex)
                {
                    node["image"] = null;
                    node["error"] = Merge(node["error"] != null ? node["error"].ToString() : null,
                        "卡图导出异常：" + ex.GetType().Name + "：" + ex.Message);
                    result.ArtFailed++;
                    result.CardErrors++;
                }

                cardNodes.Add(node);

                if (log != null && (i + 1) % 100 == 0)
                {
                    log.Info("[cards-dump] 进度 " + (i + 1) + " / " + unique.Count + "（已写图 " +
                        result.ArtWritten + "，复用 " + result.ArtReused + "，失败 " + result.ArtFailed + "）");
                }
            }

            result.UniqueArtCount = artByKey.Count;
            result.ArtDirectory = artDir;

            // ---- 清单 JSON ----
            var root = new JObject();
            root["$schema"] = "modkit-cards-dump/1";
            root["_generatedAtUtc"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'",
                System.Globalization.CultureInfo.InvariantCulture);
            root["_gameRoot"] = BepInEx.Paths.GameRootPath;
            root["_artDirectory"] = artDir;
            root["_thumbMaxEdge"] = ThumbnailMaxEdge;
            root["_fullArtExported"] = ExportFullArt;
            root["_note"] =
                "由 ModKit.Game.CardArtExporter 在游戏运行期导出（只读）。" +
                "internalName 是卡 SO 的 cardName（也就是卡 artTemplate 的合法取值）；" +
                "displayNames 取自游戏本地化表 CardName（按可用语言逐个查，查不到的语言不出现）；" +
                "image 是**相对本 JSON 所在目录**的路径（形如 art/<内部名>.png），不是相对 _artDirectory；" +
                "多张卡共用同一张图时复用同一个文件名（导出侧按「纹理实例ID + 图集矩形」去重）。" +
                "artTemplate 对原版卡而言就是它自己的内部名。";

            var counts = new JObject();
            counts["cards"] = result.CardCount;
            counts["uniqueArt"] = result.UniqueArtCount;
            counts["artWritten"] = result.ArtWritten;
            counts["artReused"] = result.ArtReused;
            counts["artFailed"] = result.ArtFailed;
            counts["cardErrors"] = result.CardErrors;
            counts["truncated"] = result.Truncated;
            root["_counts"] = counts;

            var localeCodes = new JArray();
            foreach (var l in locales)
            {
                try { if (l != null && l.Identifier.Code != null) { localeCodes.Add(l.Identifier.Code); } }
                catch { }
            }
            root["_locales"] = localeCodes;

            root["cards"] = cardNodes;

            string path = Path.Combine(dir, CardsFileName);
            string text = JsonConvert.SerializeObject(root, Formatting.Indented).Replace("\r\n", "\n");
            CapabilitiesDump.AtomicWrite(path, text);

            result.CardsJsonPath = path;

            sw.Stop();
            result.ElapsedMs = sw.Elapsed.TotalMilliseconds;

            if (log != null)
            {
                log.Info("[cards-dump] 卡清单已写入：" + path + "（" + new FileInfo(path).Length + " 字节）");
                log.Info("[cards-dump] 卡图目录：" + artDir);
                log.Info("[cards-dump] 导出完成：" + result);
            }

            return result;
        }

        // ---------------------------------------------------------------------
        // 单张卡的描述
        // ---------------------------------------------------------------------

        private static JObject DescribeCard(CardSo so, HashSet<string> pool, List<Locale> locales)
        {
            var node = new JObject();
            node["internalName"] = so.cardName;

            // 显示名：按语言逐个查游戏自己的本地化表（能取多少取多少）
            var names = new JObject();
            try
            {
                var db = LocalizationSettings.StringDatabase;
                if (db != null && locales.Count > 0)
                {
                    foreach (var loc in locales)
                    {
                        if (loc == null) { continue; }
                        string code = null;
                        try { code = loc.Identifier.Code; } catch { }
                        if (string.IsNullOrEmpty(code)) { continue; }

                        try
                        {
                            string v = db.GetLocalizedString("CardName", so.cardName, loc,
                                FallbackBehavior.UseProjectSettings);
                            if (!string.IsNullOrEmpty(v) &&
                                v.IndexOf("No translation found", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                names[code] = v;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            node["displayNames"] = names;

            CardInfo info = so.CardInfo;

            node["rarity"] = info.rarity.ToString();

            var colors = new JArray();
            if (info.cardColorEs != null)
            {
                foreach (var c in info.cardColorEs) { colors.Add(c.ToString()); }
            }
            node["colors"] = colors;

            var cost = new JArray();
            if (info.cardManaCostColorEArrays != null)
            {
                foreach (var slot in info.cardManaCostColorEArrays)
                {
                    var one = new JArray();
                    if (slot.colorEs != null)
                    {
                        foreach (var c in slot.colorEs) { one.Add(c.ToString()); }
                    }
                    cost.Add(one);
                }
            }
            node["cost"] = cost;

            var resolve = new JArray();
            if (info.cardEffectInfos != null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var eff in info.cardEffectInfos)
                {
                    string r = eff.cardEffectResolveType.ToString();
                    if (seen.Add(r)) { resolve.Add(r); }
                }
            }
            node["resolve"] = resolve;

            var keywords = new JArray();
            if (info.cardKeyWordInfos != null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var kw in info.cardKeyWordInfos)
                {
                    if (!string.IsNullOrEmpty(kw.keywordName) && seen.Add(kw.keywordName))
                    {
                        keywords.Add(kw.keywordName);
                    }
                }
            }
            node["keywords"] = keywords;

            // ★ 对原版卡而言，作者要写的 artTemplate 就是它自己的内部名 —— 没有别的映射层。
            node["artTemplate"] = so.cardName;

            node["inCampaignPool"] = pool.Contains(so.cardName);

            return node;
        }

        // ---------------------------------------------------------------------
        // 单张卡的图
        // ---------------------------------------------------------------------

        private sealed class ArtFile
        {
            public string RelativePath;   // 相对 _artDirectory，如 "art/Block.png"
        }

        /// <summary>
        /// 导出（或复用）一张卡的图，返回相对路径（相对导出目录），如 <c>art/Block.png</c>。
        /// 失败时返回 <c>null</c> 并通过 <paramref name="error"/> 给出原因。
        /// </summary>
        private static string ExportOneArt(
            CardSo so, string artDir, string fullDir,
            Dictionary<string, ArtFile> artByKey, Dictionary<string, string> usedFileNames,
            out string error, ref CardArtExportResult result, IGameLog log)
        {
            error = null;

            // 卡面是游戏版权素材：默认不导出 PNG（只写 cards.json 的名字清单）
            if (!DiagnosticSwitches.ExportCardsArtPng) { return null; }

            Sprite sprite = so.cardArt;
            if (sprite == null)
            {
                error = "卡没有任何卡面图（CardSo.cardArt 为 null）";
                return null;
            }

            Texture2D texture = sprite.texture;
            if (texture == null)
            {
                error = "卡面 Sprite 没有关联纹理（sprite.texture 为 null）";
                return null;
            }

            // 图集里被"紧打包 + 旋转"的子图不能用矩形裁剪取回 —— 如实跳过，不装作拿到了。
            try
            {
                if (sprite.packed && sprite.packingRotation != SpritePackingRotation.None)
                {
                    error = "卡面在打包图集里被旋转，无法用矩形裁剪（packingRotation=" +
                        sprite.packingRotation + "）";
                    return null;
                }
            }
            catch { }

            Rect tr;
            try { tr = sprite.textureRect; }
            catch (Exception ex)
            {
                error = "取 sprite.textureRect 失败：" + ex.GetType().Name;
                return null;
            }

            if (tr.width <= 0.5f || tr.height <= 0.5f)
            {
                error = "卡面的图集矩形为空（" + tr.width + "x" + tr.height + "）";
                return null;
            }

            string key = texture.GetInstanceID().ToString() + "|" +
                ((int)tr.x) + "," + ((int)tr.y) + "," + ((int)tr.width) + "," + ((int)tr.height);

            ArtFile known;
            if (artByKey.TryGetValue(key, out known))
            {
                result.ArtReused++;
                return known.RelativePath;
            }

            // ---- 文件名（按第一张使用它的卡命名；冲突则加序号）----
            string fileName = MakeUniqueFileName(so.cardName, usedFileNames, key);
            string pngPath = Path.Combine(artDir, fileName);

            bool existed = File.Exists(pngPath) && new FileInfo(pngPath).Length > 0;
            if (existed && ReuseExistingArt)
            {
                // 图已经在磁盘上且允许复用 —— 跳过 GPU 取回与 PNG 编码（启动期的主要开销）
            }
            else
            {
                Texture2D thumb = null;
                try
                {
                    thumb = RenderSpriteToTexture(sprite, texture, tr, ThumbnailMaxEdge, false);
                    byte[] png = thumb.EncodeToPNG();                    if (png == null || png.Length == 0) { throw new InvalidOperationException("EncodeToPNG 返回空"); }
                    AtomicWriteBytes(pngPath, png);
                    result.ArtWritten++;
                }
                finally
                {
                    if (thumb != null) { UnityEngine.Object.Destroy(thumb); }
                }
            }

            // ---- 可选：原图 ----
            if (ExportFullArt)
            {
                try
                {
                    string fullPath = Path.Combine(fullDir, fileName);
                    if (!(ReuseExistingArt && File.Exists(fullPath) && new FileInfo(fullPath).Length > 0))
                    {
                        Texture2D full = null;
                        try
                        {
                            full = RenderSpriteToTexture(sprite, texture, tr, 0, true);
                            byte[] png = full.EncodeToPNG();
                            if (png != null && png.Length > 0)
                            {
                                AtomicWriteBytes(fullPath, png);
                            }
                        }
                        finally
                        {
                            if (full != null) { UnityEngine.Object.Destroy(full); }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 原图失败不影响缩略图
                    if (log != null) { log.Warn("[cards-dump] 原图导出失败（" + so.cardName + "）：" + ex.GetType().Name); }
                }
            }

            var file = new ArtFile();
            file.RelativePath = ArtSubDirectory + "/" + fileName;   // 正斜杠：markdown 用
            artByKey[key] = file;
            return file.RelativePath;
        }

        private static string Merge(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) { return b; }
            if (string.IsNullOrEmpty(b)) { return a; }
            return a + "；" + b;
        }

        /// <summary>图片的原子写（与能力表同一套：先写 .tmp，再顶替）。</summary>
        internal static void AtomicWriteBytes(string path, byte[] bytes)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) { Directory.CreateDirectory(dir); }

            string tmp = path + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, bytes);
                if (File.Exists(path))
                {
                    try { File.Replace(tmp, path, null, true); return; }
                    catch (Exception) { File.Delete(path); }
                }
                File.Move(tmp, path);
            }
            finally
            {
                try { if (File.Exists(tmp)) { File.Delete(tmp); } } catch { }
            }
        }

        private static string MakeUniqueFileName(string internalName, Dictionary<string, string> used, string key)
        {
            string baseName = Sanitize(internalName);
            if (string.IsNullOrEmpty(baseName)) { baseName = "card"; }

            string candidate = baseName;
            string owner;
            if (!used.TryGetValue(candidate, out owner)) { used[candidate] = key; return candidate + ".png"; }
            if (string.Equals(owner, key, StringComparison.Ordinal)) { return candidate + ".png"; }

            for (int i = 2; i < 10000; i++)
            {
                string next = baseName + "_" + i;
                if (!used.TryGetValue(next, out owner)) { used[next] = key; return next + ".png"; }
                if (string.Equals(owner, key, StringComparison.Ordinal)) { return next + ".png"; }
            }

            // 极端兜底：用纹理 key 的散列，绝不可能重名
            string fallback = baseName + "_" + Math.Abs(key.GetHashCode()).ToString();
            used[fallback] = key;
            return fallback + ".png";
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) { return null; }
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char ch in name)
            {
                bool bad = false;
                for (int i = 0; i < invalid.Length; i++) { if (invalid[i] == ch) { bad = true; break; } }
                sb.Append(bad ? '_' : ch);
            }
            return sb.ToString().Trim();
        }

        // ---------------------------------------------------------------------
        // Sprite → Texture2D
        // ---------------------------------------------------------------------

        /// <summary>
        /// 把 <paramref name="sprite"/> 的画面渲染成一张新的 <see cref="Texture2D"/>。
        /// </summary>
        /// <remarks>
        /// ★ 为什么走 RenderTexture（而不是 <c>texture.GetPixels</c>）：
        ///   游戏里的卡图是打包进图集的，纹理**默认不可读**（<c>isReadable = false</c>），
        ///   直接 <c>GetPixels</c> 会抛 <c>UnityException: Texture ... is not readable</c>。
        ///   RenderTexture + <c>Graphics.Blit(scale, offset)</c> 既能取回不可读纹理，
        ///   又能**只裁 sprite 在图集里的那一块**（不裁就会导出别人的图）。
        /// <para>
        /// ★ <paramref name="texture"/> 不可读时无法走 CPU 兜底，所以 CPU 路径只是"渲染路径失败后的备选"。
        /// </para>
        /// </remarks>
        private static Texture2D RenderSpriteToTexture(
            Sprite sprite, Texture2D texture, Rect tr, int maxEdge, bool keepNative)
        {
            int sw = Mathf.Max(1, Mathf.RoundToInt(tr.width));
            int sh = Mathf.Max(1, Mathf.RoundToInt(tr.height));

            int tw = sw, th = sh;
            if (!keepNative && maxEdge > 0)
            {
                float k = Mathf.Min(1f, maxEdge / (float)Mathf.Max(sw, sh));
                tw = Mathf.Max(1, Mathf.RoundToInt(sw * k));
                th = Mathf.Max(1, Mathf.RoundToInt(sh * k));
            }

            Exception renderError = null;
            RenderTexture rt = null;
            RenderTexture previous = RenderTexture.active;

            try
            {
                rt = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

                // 整张纹理就是这张卡的画面时**不做裁剪** —— 此时 scale/offset 就是 1/0，
                // 用最朴素的 blit 重载，连"uv * scale + offset"那套语义都用不上（少一层不确定性）。
                bool wholeTexture =
                    Mathf.Approximately(tr.x, 0f) && Mathf.Approximately(tr.y, 0f) &&
                    Mathf.Approximately(tr.width, texture.width) &&
                    Mathf.Approximately(tr.height, texture.height);

                if (wholeTexture)
                {
                    Graphics.Blit(texture, rt);
                }
                else
                {
                    // 图集子图：scale/offset = 取纹理里的
                    // [tr.x, tr.x+tr.width] × [tr.y, tr.y+tr.height] 这一块
                    Vector2 scale = new Vector2(tr.width / texture.width, tr.height / texture.height);
                    Vector2 offset = new Vector2(tr.x / texture.width, tr.y / texture.height);
                    Graphics.Blit(texture, rt, scale, offset);
                }

                RenderTexture.active = rt;
                var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false, false);
                tex.ReadPixels(new Rect(0, 0, tw, th), 0, 0, false);
                tex.Apply(false, false);
                if (FlipVertically) { FlipInPlace(tex); }
                return tex;
            }
            catch (Exception ex)
            {
                renderError = ex;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null) { RenderTexture.ReleaseTemporary(rt); }
            }

            // ---- CPU 兜底（只在纹理可读时可用）----
            if (texture.isReadable)
            {
                try
                {
                    Color[] px = texture.GetPixels((int)tr.x, (int)tr.y, sw, sh);
                    Color[] scaled = DownscaleBox(px, sw, sh, tw, th);
                    var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false, false);
                    tex.SetPixels(scaled);
                    tex.Apply(false, false);
                    if (FlipVertically) { FlipInPlace(tex); }
                    return tex;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "渲染取回与 CPU 取回都失败（渲染：" + renderError.GetType().Name +
                        "，CPU：" + ex.GetType().Name + "）", ex);
                }
            }

            throw new InvalidOperationException(
                "渲染取回失败且纹理不可读（isReadable=false），无法兜底：" + renderError.GetType().Name +
                "：" + renderError.Message, renderError);
        }

        /// <summary>上下翻转（给"图是倒的"那种后端差异留的手动开关）。</summary>
        private static void FlipInPlace(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            Color[] px = tex.GetPixels();
            var flipped = new Color[px.Length];
            for (int y = 0; y < h; y++)
            {
                int src = y * w;
                int dst = (h - 1 - y) * w;
                for (int x = 0; x < w; x++) { flipped[dst + x] = px[src + x]; }
            }
            tex.SetPixels(flipped);
            tex.Apply(false, false);
        }

        /// <summary>矩形均值缩小（CPU 兜底路径用；GPU 路径本身带双线性过滤）。</summary>
        private static Color[] DownscaleBox(Color[] src, int sw, int sh, int tw, int th)
        {
            if (sw == tw && sh == th) { return src; }

            var dst = new Color[tw * th];
            for (int y = 0; y < th; y++)
            {
                int y0 = (int)((long)y * sh / th);
                int y1 = (int)((long)(y + 1) * sh / th);
                if (y1 <= y0) { y1 = y0 + 1; }

                for (int x = 0; x < tw; x++)
                {
                    int x0 = (int)((long)x * sw / tw);
                    int x1 = (int)((long)(x + 1) * sw / tw);
                    if (x1 <= x0) { x1 = x0 + 1; }

                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    int n = 0;
                    for (int sy = y0; sy < y1 && sy < sh; sy++)
                    {
                        int row = sy * sw;
                        for (int sx = x0; sx < x1 && sx < sw; sx++)
                        {
                            Color c = src[row + sx];
                            r += c.r; g += c.g; b += c.b; a += c.a; n++;
                        }
                    }
                    if (n == 0) { dst[y * tw + x] = new Color(0f, 0f, 0f, 0f); }
                    else
                    {
                        float inv = 1f / n;
                        dst[y * tw + x] = new Color(r * inv, g * inv, b * inv, a * inv);
                    }
                }
            }
            return dst;
        }
    }
}
