// =============================================================================
//  Run History（战绩）· BepInEx 插件入口
//
//  ★ 为什么是独立 BepInEx 插件，而不是 ModKit 内容 mod：
//    ① 这个 mod 不含任何卡/遗物/关键词 —— 它钩的是**游戏内部流程**
//       （开局、进节点、结算、放弃），ModKit 的内容契约在这里用不上；
//    ② ModKit.Api 里虽然定义了 `IMod` 入口契约，但平台侧**从未调用过它**
//       （全仓 src/ModKit.Game 没有任何 IMod 引用），照它写等于写个不响的铃；
//    ③ 独立插件不依赖平台，平台被停用/没装时战绩照样记录。
//
//  ★ 数据落在 <游戏根>\BepInEx\RunHistory\，**不碰游戏存档目录**
//    （存档格式随版本变，而且那一层会被 Steam 云同步）。
// =============================================================================

using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace RunHistory
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class RunHistoryPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "diceomancer.runhistory";
        public const string PluginName = "Diceomancer 战绩（运行历史）";
        public const string PluginVersion = "0.2.2";

        internal static ConfigEntry<bool> CfgEnabled;

        /// <summary>数据目录。留空 = <c>&lt;BepInEx&gt;\RunHistory</c>。</summary>
        internal static ConfigEntry<string> CfgDataDir;

        /// <summary>是否逐节点记录快照（路线、逐层获取、每层耗时都依赖它）。</summary>
        internal static ConfigEntry<bool> CfgSnapshotNodes;

        /// <summary>诊断：把每次落盘/差分细节打进日志。</summary>
        internal static ConfigEntry<bool> CfgVerbose;

        internal static BepInEx.Logging.ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;

            CfgEnabled = Config.Bind("General", "Enabled", true,
                "是否记录战绩。关掉后不装任何钩子，游戏完全不受影响。");
            CfgDataDir = Config.Bind("General", "DataDir", "",
                "战绩数据目录。留空 = <游戏根>\\BepInEx\\RunHistory");
            CfgSnapshotNodes = Config.Bind("General", "SnapshotNodes", true,
                "逐节点记录快照（血量/金币/牌堆数/遗物数 + 新获得的卡与遗物）。" +
                "关掉后只记录开局与结算，路线与逐层获取会缺失。");
            CfgVerbose = Config.Bind("Diagnostics", "Verbose", false,
                "把每次落盘与差分的细节打进日志（排查用）。");

            if (!CfgEnabled.Value)
            {
                Logger.LogInfo(PluginName + "：已在配置中停用，不记录任何数据。");
                return;
            }

            try
            {
                RunHistoryStore.Configure(CfgDataDir.Value);
                RunHistorySeed.Load();
                new Harmony("diceomancer.runhistory").PatchAll(typeof(RunHistoryPlugin).Assembly);
                Logger.LogInfo(PluginName + " v" + PluginVersion + " 已就绪｜数据目录：" +
                    RunHistoryStore.DataDir + "｜待用种子：" +
                    (RunHistorySeed.Pending.HasValue ? RunHistorySeed.Pending.Value.ToString() : "无"));

                // 面板宿主：**不能**在 Awake 里 new GameObject —— 插件 Awake 时还没有活动场景，
                // Unity 会拒绝 DontDestroyOnLoad，对象随第一个场景加载被销毁（平台侧踩过同一个坑）。
                // 正确做法：订阅 sceneLoaded，等场景就绪后再建。
                UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
                Logger.LogInfo("战绩：已订阅 sceneLoaded（面板宿主将在场景就绪后创建）。");
            }
            catch (Exception ex)
            {
                // FailOpen：装不上钩子就整个停用，绝不影响游戏启动
                Logger.LogError("战绩：安装采集钩子失败，本 mod 已停用 —— " + ex);
            }
        }

        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            try
            {
                if (RunHistoryUiHost.Instance != null) { return; }
                var go = new GameObject("RunHistory.Host");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<RunHistoryUiHost>();
            }
            catch (Exception ex)
            {
                Log.LogWarning("战绩：创建面板宿主失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
