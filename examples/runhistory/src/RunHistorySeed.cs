// =============================================================================
//  Run History（战绩）· 用种子重开一局
//
//  ★ 游戏里"地图为什么长得那样"由两个随机源共同决定（反编译确认）：
//    ① **战役种子**：`CampaignManager._campaignRandomSeed`
//       → `WorldRandomSeedUtil.GetChapterSeed()` = 战役种子 + 章节偏移（Plain+33333 / Forest+66666 / City+99999）
//       → 节点类型洗牌、精英分布、首领挑选、以及每个节点的遭遇（GetMapNodeSeed = 章节种子 + 节点坐标）
//    ② **一个没有设置种子的静态 System.Random**：`Utilities.ShufflingExtension.rng`
//       → `list.Random()` / `list.Shuffle()`：段落预制体的挑选、节点之间的连边、
//         普通路段/城市的节点类型、`PoolElement.PopRandomElement()`
//    所以只覆盖①的话，同一张图的**段落组合/连边/节点类型**仍然每次不同。
//
//  ★ ②必须按「状态复制」重置，不能只换字段（v0.2.0 就栽在这里）：
//    `rng` 是 `static readonly`，`FieldInfo.SetValue` 在 Mono 上可能不生效，
//    而且即使换了实例，也不保证别处拿的仍是旧引用。
//    见 `RunHistoryRandomState`：直接把 `new Random(seed)` 的内部状态拷进原实例，并回读校验。
//
//  ★ 生效时机（关键）：
//    `_campaignRandomSeed` 由 `CampaignManager.NewGameLaunchInitialize()` 里的
//    `GenerateNewSeed()` 赋值（那一刻还没有生成任何地图）；之后才进 Main 场景
//    → `WorldManager.GenerateNewWorld()` → `ChapterMapGeneratorBase.GenerateMap()`。
//    重置做两次：`GenerateNewWorld` 之前（热身）、`GenerateMap` 之前（决定性，紧挨着真正取随机数的地方）。
//
//  ★ 一次性语义：待用种子只在**下一次开局**生效，用掉即清除（写盘，重启也保留）。
// =============================================================================

using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;

namespace RunHistory
{
    /// <summary>待用种子：下一次开局时替换掉游戏自己生成的种子。</summary>
    internal static class RunHistorySeed
    {
        private const string FileName = "pending-seed.json";

        private static readonly FieldInfo CampaignSeedField = SafeField(
            typeof(Combat.CampaignManager), "_campaignRandomSeed");

        private static int? _pending;

        /// <summary>当前待用种子（null = 没有）。</summary>
        internal static int? Pending { get { return _pending; } }

        private static string FilePath
        {
            get { return Path.Combine(RunHistoryStore.DataDir, FileName); }
        }

        // ---------------- 存取 ----------------

        /// <summary>启动时把上次没用的待用种子读回来。</summary>
        internal static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) { return; }
                var payload = JsonConvert.DeserializeObject<Payload>(File.ReadAllText(FilePath));
                if (payload == null) { return; }
                _pending = payload.seed;
                RunHistoryPlugin.Log.LogInfo("战绩：找回待用种子 " + payload.seed +
                    (string.IsNullOrEmpty(payload.note) ? "" : "（" + payload.note + "）") + "，下次开局生效。");
            }
            catch (Exception ex)
            {
                RunHistoryPlugin.Log.LogWarning("战绩：待用种子读取失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>设置待用种子（下一次开局生效）。</summary>
        internal static void SetPending(int seed, string note)
        {
            try
            {
                _pending = seed;
                AtomicWrite(new Payload { seed = seed, note = note, setAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }.ToJson());
                RunHistoryPlugin.Log.LogInfo("战绩：已设置待用种子 " + seed +
                    (string.IsNullOrEmpty(note) ? "" : "（" + note + "）") + "，下次开局生效。");
            }
            catch (Exception ex)
            {
                RunHistoryPlugin.Log.LogWarning("战绩：待用种子写入失败（仅本进程有效）—— " + ex.Message);
            }
        }

        internal static void ClearPending()
        {
            _pending = null;
            try { if (File.Exists(FilePath)) { File.Delete(FilePath); } } catch { }
        }

        // ---------------- 两个钩子 ----------------

        /// <summary>`CampaignManager.GenerateNewSeed()` 之后：用待用种子替换刚生成的种子。</summary>
        internal static void AfterSeedGenerated()
        {
            if (_pending == null) { return; }
            int seed = _pending.Value;

            try
            {
                var campaign = Combat.CampaignManager.Instance;
                if (campaign == null) { return; }
                if (CampaignSeedField == null)
                {
                    RunHistoryPlugin.Log.LogError("战绩：找不到 CampaignManager._campaignRandomSeed，种子未替换。");
                    return;
                }

                CampaignSeedField.SetValue(campaign, seed);
                RunHistoryPlugin.Log.LogInfo("战绩：本次开局使用指定种子 " + seed +
                    "（原种子已被替换；章节地图将由此种子生成）。");
                ClearPending();   // 一次性：用完就清
            }
            catch (Exception ex)
            {
                RunHistoryPlugin.Log.LogWarning("战绩：替换种子失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// `WorldManager.GenerateNewWorld()` 之前：把未播种的全局洗牌随机源按同一个种子重置。
        /// （只是热身；真正决定性的一次在 `ChapterMapGeneratorBase.GenerateMap()` 之前。）
        /// </summary>
        internal static void BeforeWorldGeneration()
        {
            ReseedForMap("世界生成前");
        }

        /// <summary>
        /// 把地图生成用到的所有随机源对齐到当前战役种子。
        /// `reason` 只进日志/转储，方便区分是哪一钩子起的效。
        /// </summary>
        internal static void ReseedForMap(string reason)
        {
            try
            {
                var campaign = Combat.CampaignManager.Instance;
                if (campaign == null)
                {
                    RunHistoryPlugin.Log.LogWarning("战绩：重置随机源时 CampaignManager 尚未就绪（原因：" + reason + "）。");
                    return;
                }
                RunHistoryRandomState.Reseed(campaign.GetCampaignSeed(), reason);
            }
            catch (Exception ex)
            {
                RunHistoryPlugin.Log.LogWarning("战绩：重置随机源失败（" + reason + "）—— " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>当前战役种子（拿不到返回 0）。</summary>
        internal static int CurrentSeed()
        {
            try
            {
                var campaign = Combat.CampaignManager.Instance;
                return campaign == null ? 0 : campaign.GetCampaignSeed();
            }
            catch { return 0; }
        }

        // ---------------- 内部 ----------------

        private static FieldInfo SafeField(Type type, string name)
        {
            try
            {
                return type == null ? null : type.GetField(name,
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            }
            catch { return null; }
        }

        private static void AtomicWrite(string text)
        {
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(FilePath))
            {
                try { File.Replace(tmp, FilePath, null); }
                catch { File.Copy(tmp, FilePath, true); try { File.Delete(tmp); } catch { } }
            }
            else { File.Move(tmp, FilePath); }
        }

        [Serializable]
        private sealed class Payload
        {
            public int seed;
            public string note;
            public string setAt;

            internal string ToJson()
            {
                return JsonConvert.SerializeObject(this, Formatting.Indented);
            }
        }
    }
}
