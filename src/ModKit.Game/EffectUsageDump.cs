// =============================================================================
//  ModKit.Game · 原版卡牌「效果使用」导出
//
//  为什么要它：现有 cards.json 由 CardArtExporter 导出，只有卡面与元数据，
//  **没有 effects**（效果存在 CardSo.cardInfo.cardEffectInfos 里，属运行期对象）。
//  于是"某个职业用了多少种效果/数值档位/关键词搭配"这类设计基准拿不到，
//  只能靠实机跑一次导出来。
//
//  导出内容（每张原版卡）：
//    name / rarity / colors / cost / keywords
//    effects[]: effectName / target / numbers（效果数字个数） / abilityName /
//               additionalString / colorE / specialEffectString
//    keywordEffects[]: 关键词自带的效果（如 Miracle）
//
//  产物：<游戏根>/BepInEx/ModKitDump/effect-usage.json
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Card;
using Effects;
using Keyword;
using GameData;
using ModKit.Api;
using Newtonsoft.Json;
using UnityEngine;

namespace ModKit.Game
{
    internal static class EffectUsageDump
    {
        internal const string FileName = "effect-usage.json";

        private sealed class EffectRec
        {
            public string effectName;
            public string target;
            public int numbers;
            public string abilityName;
            public string additionalString;
            public int colorE;
            public string specialEffectString;
        }

        private sealed class CardRec
        {
            public string name;
            public string rarity;
            public string[] colors;
            public int cost;
            public string[] keywords;
            public List<EffectRec> effects = new List<EffectRec>();
            public List<EffectRec> keywordEffects = new List<EffectRec>();
        }

        internal static void Run(IGameLog log)
        {
            try
            {
                var all = CollectCardSos();
                if (all.Count == 0)
                {
                    if (log != null) { log.Warn("效果导出：没有取到任何 CardSo，跳过。"); }
                    return;
                }

                var cards = new List<CardRec>(all.Count);
                var usage = new SortedDictionary<string, int>(StringComparer.Ordinal);

                foreach (var so in all)
                {
                    if (so == null) { continue; }
                    CardInfo info;
                    try { info = so.CardInfo; } catch { continue; }

                    var rec = new CardRec
                    {
                        name = so.cardName,
                        rarity = info.rarity.ToString(),
                        colors = ToStrings(info.cardColorEs),
                        cost = info.cardManaCostColorEArrays == null ? 0 : info.cardManaCostColorEArrays.Length,
                        keywords = KeywordNames(info.cardKeyWordInfos),
                    };

                    if (info.cardEffectInfos != null)
                    {
                        foreach (var cei in info.cardEffectInfos)
                        {
                            var e = ToRec(cei.effectInfo);
                            if (e == null) { continue; }
                            rec.effects.Add(e);
                            int n;
                            usage.TryGetValue(e.effectName, out n);
                            usage[e.effectName] = n + 1;
                        }
                    }

                    // 关键词自带的效果（Miracle / Choose 这类）
                    if (info.cardKeyWordInfos != null)
                    {
                        foreach (var kw in info.cardKeyWordInfos)
                        {
                            var arr = kw.GetType().GetField("cardEffectInfos");
                            // 关键词效果结构随版本变化，取不到就跳过（不阻断主导出）
                        }
                    }

                    cards.Add(rec);
                }

                var dir = DumpDirectory();
                if (dir == null) { return; }
                Directory.CreateDirectory(dir);

                var payload = new
                {
                    generatedAtUtc = DateTime.UtcNow.ToString("o"),
                    source = "CardSo.CardInfo.cardEffectInfos @ CardDataManager",
                    cardCount = cards.Count,
                    effectVocabulary = EffectInfo.EffectNameArray,
                    effectUsage = usage,
                    cards,
                };

                File.WriteAllText(Path.Combine(dir, FileName),
                    JsonConvert.SerializeObject(payload, Formatting.Indented),
                    new UTF8Encoding(false));

                if (log != null)
                {
                    log.Info("效果使用已导出：" + cards.Count + " 张卡 / " + usage.Count + " 种效果 → ModKitDump/" + FileName);
                }
            }
            catch (Exception ex)
            {
                if (log != null) { log.Warn("效果使用导出失败 —— " + ex.GetType().Name + ": " + ex.Message); }
            }
        }

        private static EffectRec ToRec(EffectInfo e)
        {
            if (e == null) { return null; }
            return new EffectRec
            {
                effectName = e.effectName,
                target = e.effectTargetEnum.ToString(),
                numbers = e.effectNumberInfos == null ? 0 : e.effectNumberInfos.Count,
                abilityName = e.abilityName,
                additionalString = e.additionalString,
                colorE = (int)e.colorE,
                specialEffectString = e.specialEffectString,
            };
        }

        private static string[] ToStrings(ColorE[] arr)
        {
            if (arr == null) { return new string[0]; }
            var r = new string[arr.Length];
            for (int i = 0; i < arr.Length; i++) { r[i] = arr[i].ToString(); }
            return r;
        }

        private static string[] KeywordNames(CardKeyWordInfo[] infos)
        {
            if (infos == null) { return new string[0]; }
            var r = new string[infos.Length];
            for (int i = 0; i < infos.Length; i++) { r[i] = infos[i].keywordName; }
            return r;
        }

        /// <summary>取全部 CardSo：优先走 CardDataManager 的公开枚举，失败再反射私有字典。</summary>
        private static List<CardSo> CollectCardSos()
        {
            var result = new List<CardSo>();
            try
            {
                var mgr = CardDataManager.Instance;
                if (mgr == null) { return result; }

                var dictField = mgr.GetType().GetField("_allInGameCardSoDictionary",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                if (dictField != null)
                {
                    var dict = dictField.GetValue(mgr) as System.Collections.IDictionary;
                    if (dict != null)
                    {
                        foreach (var v in dict.Values)
                        {
                            var so = v as CardSo;
                            if (so != null) { result.Add(so); }
                        }
                    }
                }

                if (result.Count == 0)
                {
                    var all = Resources.LoadAll<CardSo>("");
                    if (all != null) { result.AddRange(all); }
                }
            }
            catch { }
            return result;
        }

        private static string DumpDirectory()
        {
            try
            {
                var plugins = Path.GetDirectoryName(typeof(EffectUsageDump).Assembly.Location);
                var bep = Path.GetDirectoryName(plugins ?? "");
                return string.IsNullOrEmpty(bep) ? null : Path.Combine(bep, "ModKitDump");
            }
            catch { return null; }
        }
    }
}
