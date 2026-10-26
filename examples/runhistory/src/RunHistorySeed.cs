// =============================================================================
//  Run History（战绩）· 用种子重开一局
//
//  ★ 游戏里"地图为什么长得那样"由两个随机源共同决定（反编译确认）：
//    ① **战役种子**：`CampaignManager._campaignRandomSeed`
//       → `WorldRandomSeedUtil.GetChapterSeed()` = 战役种子 + 章节偏移（Plain+33333 / Forest+66666 / City+99999）
//       → 节点类型洗牌、精英分布、首领挑选、以及每个节点的遭遇（GetMapNodeSeed = 章节种子 + 节点坐标）
//    ② **一个没有设置种子的静态 System.Random**：`Utilities.ShufflingExtension.rng`
//       → `list.Random()` / `list.Shuffle()`：主要用在 `ChapterMapGeneratorPlain` 挑段落预制体（6 处）
//    所以只覆盖①的话，同一张图的**段落组合**仍然每次不同 —— 必须把②也按同一个种子重置。
//
//  ★ 生效时机（关键）：
//    `_campaignRandomSeed` 由 `CampaignManager.NewGameLaunchInitialize()` 里的
//    `GenerateNewSeed()` 赋值（那一刻还没有生成任何地图）；之后才进 Main 场景
//    → `WorldManager.GenerateNewWorld()` → `GenerateMap()`。
//    所以：在 `GenerateNewSeed` 之后覆盖种子、在 `GenerateNewWorld` 之前重置洗牌器。
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

        private static readonly FieldInfo ShufflerRngField = SafeField(
            typeof(Utilities.ShufflingExtension), "rng");

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

        /// <summary>`WorldManager.GenerateNewWorld()` 之前：把未播种的全局洗牌随机源也按同一个种子重置。</summary>
        internal static void BeforeWorldGeneration()
        {
            try
            {
                var campaign = Combat.CampaignManager.Instance;
                if (campaign == null) { return; }
                int seed = campaign.GetCampaignSeed();

                if (ShufflerRngField == null)
                {
                    RunHistoryPlugin.Log.LogWarning("战绩：找不到 ShufflingExtension.rng，段落组合可能每次不同。");
                    return;
                }

                // readonly static 字段：Mono 下反射可以写；失败就只记日志（地图仍按战役种子生成，只是段落组合不定）
                ShufflerRngField.SetValue(null, new System.Random(seed));

                // Unity 的随机源也顺手对齐，避免有别的路径混用
                try { UnityEngine.Random.InitState(seed); } catch { }

                if (RunHistoryPlugin.CfgVerbose != null && RunHistoryPlugin.CfgVerbose.Value)
                {
                    RunHistoryPlugin.Log.LogInfo("战绩：世界生成前已把全局洗牌随机源重置为 seed=" + seed + "。");
                }
            }
            catch (Exception ex)
            {
                RunHistoryPlugin.Log.LogWarning("战绩：重置洗牌随机源失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
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
