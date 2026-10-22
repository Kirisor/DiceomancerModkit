// =============================================================================
//  ModKit.Plugin · 就绪轮询器（时序修复）
//
//  ★ 为什么需要这个东西（两次实机失败换来的结论）：
//
//  平台流水线必须先探测游戏能力，再据此校验并装配 mod 内容。而探测需要两类事实：
//    ① **结构类**（原版卡名 / 关键词 / 触发点）—— 数据加载完就有；
//    ② **运行时类**（可用费用色 / 原版遗物名）—— 只在**战斗场地存在时**才有。
//
//  实测证据（反编译 `ManaManager.cs:88`）：
//      public static ManaManager Instance
//          => BattleMultipleWorldsManager.Instance.GetCurrentBattleField().manaManager;
//  所以任何"数据加载完成"钩子里问它都必然得到 null。
//
//  两次失败尝试：
//    · 挂 `DataManager.LoadAllGameData` 的 Postfix → **从未触发**；
//    · 挂 `CardDataManager.InitializeData` 的 Postfix → 触发了，但太早 ——
//      费用色取到空集，遗物名探测失败，导致作者的内容被**合理但错误地**拒绝
//      （校验器按"能力表为空 → 不可用"处理，逻辑没错，错的是提问时机）；
//    · 想在 `InitializeData` 里装"帧事件重试器" → 那时 `GameManager.Instance` 还是 null，
//      重试器**根本没装上**（日志里"已安装就绪重试器"从未出现）。
//
//  ★ 结论：不要依赖任何**钩子的时机**，而是**从第一帧起持续轮询直到就绪**。
//    这个 MonoBehaviour 由插件自己 AddComponent 创建，所以它不随任何场景销毁；
//    就绪并跑完之后自行 Destroy，不留常驻开销。
// =============================================================================

using System;
using System.Collections;
using Combat;
using Combat.BattleWorld;
using ModKit.Game;
using UnityEngine;

namespace ModKit.Plugin
{
    /// <summary>
    /// 轮询"游戏运行时事实是否可探测"，就绪后跑一次平台流水线，然后自毁。
    /// </summary>
    /// <remarks>
    /// 用协程而不是 <c>Update</c>：协程可以被显式停止，而且不需要每帧都跑判断逻辑。
    /// 轮询间隔 0.5 秒 —— 对"等一次战斗开始"这个量级足够快，也对 CPU 足够轻。
    /// </remarks>
    internal sealed class ModKitBootstrap : MonoBehaviour
    {
        /// <summary>轮询间隔（秒）。</summary>
        private const float PollInterval = 0.5f;

        /// <summary>最长等待时间（秒）。超时后如实报告并把平台停用，而不是无限等待。</summary>
        private const float TimeoutSeconds = 300f;

        private static ModKitBootstrap _instance;
        private bool _finished;
        private int _attempts;
        private float _nextCheckAt;
        private bool _startLogged;

        /// <summary>创建（幂等）。</summary>
        internal static void EnsureCreated()
        {
            if (_instance != null) { return; }

            // sceneLoaded 每个场景都会触发（地图 → 战斗 → …）。
            // 流水线已经跑过就不要再造轮询器 —— 否则会多跑一遍装配（实测发生过）。
            if (PlatformRuntime.HasRun) { return; }

            var go = new GameObject("ModKitBootstrap");

            // ★ 必须挂 DontDestroyOnLoad（实机第三次修正）：
            //   不挂时对象属于"当前场景"，而插件创建它时游戏还没进入正式场景 ——
            //   随后的一次场景加载会把它销毁，于是 **Start()/Update() 从未执行**。
            //   （实测症状：打了"已创建"日志，但心跳一条都没有，既不成功也不超时。）
            //   这正是现役 mod 注释里记录过的坑（"此时还没有活动场景，Unity 会拒绝
            //   DontDestroyOnLoad，对象会被随后的场景加载销毁"）—— 当年它被拒，
            //   而本插件运行在 BepInEx manager 就绪之后，所以现在可以正常挂上。
            try
            {
                UnityEngine.Object.DontDestroyOnLoad(go);
            }
            catch (Exception ex)
            {
                ModKitPlugin.HookLog.Warn("DontDestroyOnLoad 被拒（对象可能随场景销毁）：" + ex.Message);
            }

            _instance = go.AddComponent<ModKitBootstrap>();

            ModKitPlugin.HookLog.Info("已创建就绪轮询器（每 " + PollInterval +
                " 秒检查一次，最多等 " + TimeoutSeconds + " 秒）。");
        }

        /// <summary>
        /// 用 Update + 墙钟时间轮询，**不用协程**。
        /// </summary>
        /// <remarks>
        /// 为什么不用协程（实机第四次修正）：
        /// 协程的 yield 语义与 <c>Time.timeScale</c>、对象生命周期纠缠在一起 ——
        /// 实测出现"既不成功也不超时、心跳一条都没有"的静默状态，无从判断是
        /// "在等"还是"协程没跑起来"。<c>Update</c> 是 Unity 最基础的循环，
        /// 只要组件活着就一定会被调用；配合 <c>Time.realtimeSinceStartup</c>
        /// （不受 timeScale 影响）就能得到可观测、可推理的轮询。
        /// </remarks>
        private void Update()
        {

            if (_finished) { return; }

            // 启动探针：证明 Update 真的在跑（排查"组件是否存活"的关键证据）
            if (!_startLogged)
            {
                _startLogged = true;
                ModKitPlugin.HookLog.Info("轮询器已进入 Update 循环（timeScale=" +
                    Time.timeScale.ToString("F2") + "，realtime=" +
                    Time.realtimeSinceStartup.ToString("F1") + "s）。");
            }

            float now = Time.realtimeSinceStartup;
            if (now < _nextCheckAt) { return; }
            _nextCheckAt = now + PollInterval;
            _attempts++;

            string reason;
            if (IsReady(out reason))
            {
                ModKitPlugin.HookLog.Info(string.Format(
                    "运行时事实已就绪（{0} 次检查 / realtime {1:F1}s）→ 开始跑流水线。",
                    _attempts, now));
                Run();
                return;
            }

            // 心跳：每 10 次（约 5 真实秒）记一次，用来确认轮询在跑且给出未就绪原因
            if (_attempts % 10 == 0)
            {
                ModKitPlugin.HookLog.Info(string.Format(
                    "轮询心跳 #{0}（realtime {1:F0}s）：{2}", _attempts, now, reason));
            }

            if (now >= TimeoutSeconds)
            {
                // 超时：如实报告，不假装成功。
                // 平台**不装内容**是安全的（游戏按原版跑）；装作就绪去装内容才会造出坏卡。
                ModKitPlugin.HookLog.Error(string.Format(
                    "等待运行时事实超时（{0} 次检查）。最后一次的未就绪原因：{1}\n" +
                    "平台本次不装配任何内容（游戏按原版运行）。" +
                    "若确认已进入过战斗，请把本段日志留存以便排查。",
                    _attempts, reason));
                Finish();
            }
        }

        private void OnDestroy()
        {
            // 对象被销毁时别把 _instance 留成悬空引用
            if (_instance == this) { _instance = null; }
        }

        /// <summary>
        /// 判断运行时事实是否可探测。不可用时给出**具体原因**（而不是一个布尔值）。
        /// </summary>
        /// <remarks>
        /// 三项都必须成立 —— 任何一项拿不到，探测出来的能力表就是**不完整**的，
        /// 据此校验作者的内容会产生假报错（实测已发生过一次）。
        /// </remarks>
        private static bool IsReady(out string reason)
        {
            try
            {
                var bmwm = BattleMultipleWorldsManager.Instance;
                if (bmwm == null) { reason = "BattleMultipleWorldsManager.Instance 为 null（尚未进入任何战斗世界）"; return false; }

                var field = bmwm.GetCurrentBattleField();
                if (field == null) { reason = "GetCurrentBattleField() 为 null（战斗场地尚未创建）"; return false; }

                var mana = ManaManager.Instance;
                if (mana == null) { reason = "ManaManager.Instance 为 null（战斗场地的法力管理器尚未就绪）"; return false; }

                var dm = GameData.DataManager.Instance;
                if (dm == null) { reason = "DataManager.Instance 为 null（游戏数据尚未加载）"; return false; }

                var relics = dm.GetRelicDataManager();
                if (relics == null) { reason = "GetRelicDataManager() 为 null（遗物数据尚未构建）"; return false; }

                reason = "(已就绪)";
                return true;
            }
            catch (Exception ex)
            {
                reason = "检查就绪状态时异常：" + ex.GetType().Name + " - " + ex.Message;
                return false;
            }
        }

        private void Run()
        {
            try
            {
                PlatformRuntime.RunOnce(
                    ModKitPlugin.CfgModsRoot.Value,
                    ModKitPlugin.CfgStrictMode.Value,
                    ModKitPlugin.CfgDumpCapabilities.Value);
            }
            catch (Exception ex)
            {
                ModKitPlugin.HookLog.Error("流水线执行失败：" + ex.Message, ex);
            }
            finally
            {
                Finish();
            }
        }

        private void Finish()
        {
            if (_finished) { return; }
            _finished = true;
            _instance = null;
            try { Destroy(gameObject); } catch { }
        }
    }
}
