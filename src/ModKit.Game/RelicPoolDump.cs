// =============================================================================
//  ModKit.Game · 遗物池落盘（诊断导出）
//
//  为什么要它：
//    一件遗物"能不能被刷出来"完全由游戏侧的 19 个 `RelicDataSo` 集合决定
//    （`Resources.LoadAll<RelicDataSo>("ScriptableObjects/Relics/")`，见
//     `GameData/RelicDataManager.LoadAllRelic` + `SetCampaignRelicPool`）：
//      · `Standard` 类型 —— 任何职业都进池（**共用**）；
//      · `Colored`  类型 —— 只有 `colorEs ⊆ 本局三色` 才进池（按色彩组合分区）；
//      · `Utils`    类型 —— **永远不进战役池**（RelicDataManager 只处理 Standard/Colored）；
//      · `requireUnlock=true` 的集合还要 `CareerManager.GetUpgradeInfo(unlockUpgradeName).upgraded`。
//    这套归属关系**只存在于资产里**，任何 JSON 都没有；此前只能 grep 日志里的
//    `[relic] … pool=Colored(UnlockRelics_Green)` 行，日志一滚就没了。
//    所以这里在**加载 mod 之前**（纯原版状态）把它落盘成 `relics.json`。
//
//  ★ 只读导出：不写游戏状态、不改存档、不改玩法。
//  ★ 与 capabilities.json 同目录（BepInEx\ModKitDump）、同样的原子写。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using ModKit.Api;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Card;
using Relic;

namespace ModKit.Game
{
    /// <summary>
    /// 把原版**遗物池归属**写成 JSON（诊断导出，只读）。
    /// </summary>
    /// <remarks>
    /// 结构：
    /// <code>
    /// {
    ///   "$schema": "modkit-relic-pools/1",
    ///   "_counts": { "collections": 19, "relics": 205 },
    ///   "collections": [
    ///     { "asset": "RelicDataSo_Standard_Common", "type": "Standard",
    ///       "requireUnlock": false, "unlockUpgradeName": null, "colors": [],
    ///       "rarity": "Normal", "count": 87,
    ///       "relics": [ { "name": "Abacus", "zh": "算盘", ... } ] }
    ///   ],
    ///   "poolByRelic": { "Abacus": ["RelicDataSo_Standard_Common"] }
    /// }
    /// </code>
    /// <c>poolByRelic</c> 是反查表：一件遗物可能同时属于多个集合（游戏侧 `TryAdd` 只留第一件，
    /// 但那不影响"它能从哪些集合进来"这个事实）。
    /// </remarks>
    internal static class RelicPoolDump
    {
        /// <summary>文件自述的 schema 标识。</summary>
        internal const string SchemaId = "modkit-relic-pools/1";

        /// <summary>文件名（固定）。</summary>
        internal const string FileName = "relics.json";

        /// <summary>游戏侧加载遗物资产的 Resources 目录（与 <c>RelicDataManager.LoadAllRelic</c> 逐字一致）。</summary>
        internal const string ResourcesFolder = "ScriptableObjects/Relics/";

        /// <summary>
        /// 导出到 <c>BepInEx\ModKitDump\relics.json</c>。失败只记日志，绝不向上抛。
        /// </summary>
        /// <returns>写出的路径；未导出时 <c>null</c>。</returns>
        internal static string TryWrite(IGameLog log)
        {
            try
            {
                if (!CapabilitiesDump.Enabled) { return null; }

                string dir = CapabilitiesDump.ResolveDirectory();
                if (string.IsNullOrEmpty(dir)) { return null; }

                string path = System.IO.Path.Combine(dir, FileName);
                var root = Build(out int collectionCount, out int relicCount, out string error);
                if (root == null)
                {
                    log.Warn("遗物池导出跳过：" + error);
                    return null;
                }

                CapabilitiesDump.AtomicWrite(path, root.ToString(Newtonsoft.Json.Formatting.Indented));
                log.Info("遗物池导出：" + collectionCount + " 个集合 / " + relicCount + " 件遗物 → " + path);
                return path;
            }
            catch (Exception ex)
            {
                log.Error("遗物池导出失败（不影响内容）：" + ex.GetType().Name + ": " + ex.Message, ex);
                return null;
            }
        }

        private static JObject Build(out int collectionCount, out int relicCount, out string error)
        {
            collectionCount = 0;
            relicCount = 0;
            error = null;

            RelicDataSo[] assets;
            try { assets = Resources.LoadAll<RelicDataSo>(ResourcesFolder); }
            catch (Exception ex)
            {
                error = "Resources.LoadAll 失败 —— " + ex.GetType().Name + ": " + ex.Message;
                return null;
            }

            if (assets == null || assets.Length == 0)
            {
                error = "Resources 下没有 RelicDataSo（目录 '" + ResourcesFolder + "'）—— 可能资源尚未初始化";
                return null;
            }

            var list = new List<RelicDataSo>(assets.Length);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] != null && assets[i].relicDataCollection != null) { list.Add(assets[i]); }
            }
            // 同一份数据两次导出字节一致：按资产名排序（Resources.LoadAll 的顺序没有保证）
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            var root = new JObject();
            root["$schema"] = SchemaId;
            root["_generatedAtUtc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            root["_folder"] = ResourcesFolder;
            root["_note"] =
                "原版遗物池归属（只读导出）。type=Standard 任何职业都进池（共用）；" +
                "type=Colored 仅当 colorEs ⊆ 本局三色时进池；type=Utils 永远不进战役池；" +
                "requireUnlock=true 的集合还要该职业的升级项已购买。";

            var collections = new JArray();
            var poolByRelic = new JObject();
            var allNames = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < list.Count; i++)
            {
                var asset = list[i];
                var col = asset.relicDataCollection;

                var node = new JObject();
                node["asset"] = asset.name;
                node["type"] = col.relicDataCollectionType.ToString();
                node["requireUnlock"] = col.requireUnlock;
                node["unlockUpgradeName"] = string.IsNullOrEmpty(col.unlockUpgradeName) ? (JToken)JValue.CreateNull() : col.unlockUpgradeName;
                node["colors"] = ColorNames(col.colorEs);
                node["rarity"] = col.rarity.ToString();

                var relics = new JArray();
                var sos = col.relicSos;
                if (sos != null)
                {
                    var items = new List<RelicSo>(sos.Length);
                    for (int j = 0; j < sos.Length; j++)
                    {
                        if (sos[j] != null && sos[j].relicInfo != null) { items.Add(sos[j]); }
                    }
                    items.Sort((a, b) => string.CompareOrdinal(RelicName(a), RelicName(b)));

                    for (int j = 0; j < items.Count; j++)
                    {
                        var r = items[j];
                        string name = RelicName(r);
                        if (string.IsNullOrEmpty(name)) { continue; }

                        var info = r.relicInfo;
                        var rn = new JObject();
                        rn["name"] = name;
                        rn["zh"] = LocalizedRelicName(name);
                        rn["rarity"] = info.relicRarity.ToString();
                        rn["colors"] = ColorNames(info.relicColors);
                        rn["mainType"] = info.relicMainType.ToString();
                        rn["special"] = info.isSpecialRelic;
                        relics.Add(rn);

                        if (allNames.Add(name)) { relicCount++; }
                        if (!(poolByRelic[name] is JArray pools))
                        {
                            pools = new JArray();
                            poolByRelic[name] = pools;
                        }
                        pools.Add(asset.name);
                    }
                }

                node["count"] = relics.Count;
                node["relics"] = relics;
                collections.Add(node);
            }

            collectionCount = collections.Count;
            root["_counts"] = new JObject
            {
                ["collections"] = collectionCount,
                ["relics"] = relicCount
            };
            root["collections"] = collections;
            root["poolByRelic"] = poolByRelic;
            return root;
        }

        private static string RelicName(RelicSo so)
        {
            try { return so == null || so.relicInfo == null ? null : so.relicInfo.relicName; }
            catch { return null; }
        }

        private static JArray ColorNames(ColorE[] colors)
        {
            var arr = new JArray();
            if (colors != null)
            {
                for (int i = 0; i < colors.Length; i++) { arr.Add(colors[i].ToString()); }
            }
            return arr;
        }

        private static string LocalizedRelicName(string relicName)
        {
            try { return new UnityEngine.Localization.LocalizedString("Relic", relicName).GetLocalizedString(); }
            catch { return null; }
        }
    }
}
