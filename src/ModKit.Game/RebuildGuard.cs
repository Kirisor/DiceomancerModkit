// =============================================================================
//  ModKit.Game · 重建守卫
//
//  ★ 为什么需要它（2026-10-11 实机证伪，本类缺陷第三次出现）：
//    游戏的数据管理器在**初始化时会把派生结构整个重建**，直接从 Resources 重灌，
//    于是平台运行期注册进去的东西被无声冲掉：
//
//      // RelicDataManager.LoadAllRelic()
//      _allStandPoolRelicSo.Clear();
//      _allRelicDataCollections.Clear();                 // ← 我们加的集合没了
//      var array = Resources.LoadAll<RelicDataSo>("ScriptableObjects/Relics/");
//      ... _allInGameRelicSo.TryAdd(relicSo.relicInfo.relicName, relicSo);   // ← 只填原版
//
//      // AbilityDataManager.LoadAllAbility()
//      _allAbilitySos.Clear();                           // ← 我们的能力没了
//      var array = Resources.LoadAll<AbilitySo>("ScriptableObjects/Abilities/StandardAbilities/");
//      ... _allAbilitySos.TryAdd(...);
//
//    实机症状：**读回断言通过**（那一刻确实在），但**第一场战斗发牌之后**
//    `GetRelicSo("sample_starter.stone_bracelet")` 与 `GetAbilitySo(...)` 都返回 null
//    → 遗物图鉴里看不见、能力也不出现。
//
//  ★ 本节的三次同类缺陷（都写在这里提醒未来）：
//    1. 战役卡池：`SetCampaignCardPool()` 会 Clear 再只从 card collections 重灌；
//    2. 战役遗物池：`SetCampaignRelicPool()` 同构；
//    3. **数据字典本身**：`LoadAllRelic()` / `LoadAllAbility()` 把字典也整个重建。
//    → 规律：**任何"游戏会重建的容器"，平台写进去都不算数，必须在重建之后重新写。**
//
//  ★ 做法：不跟游戏的重建对抗，而是**挂在重建之后重新注册**（Harmony postfix）。
// =============================================================================

using System;
using System.Collections.Generic;
using HarmonyLib;
using GameData;

namespace ModKit.Game
{
    /// <summary>当游戏重建数据容器时，把平台的内容重新注册回去。</summary>
    public static class RebuildGuard
    {
        private static readonly object Gate = new object();

        /// <summary>重注册动作。每个动作都必须**可重复执行**（幂等）。</summary>
        private static readonly List<Action> ReapplyActions = new List<Action>();

        private static IGameLog _log;
        private static bool _installed;

        public static bool Installed { get { return _installed; } }

        /// <summary>登记一个"重建后要重做"的动作。</summary>
        public static void AddReapply(Action action, IGameLog log)
        {
            _log = log;
            if (action == null) { return; }
            lock (Gate) { ReapplyActions.Add(action); }
        }

        /// <summary>是否已经登记过内容（没登记就不用挂钩）。</summary>
        public static bool HasWork { get { lock (Gate) { return ReapplyActions.Count > 0; } } }

        /// <summary>装挂钩。</summary>
        public static bool Install(IGameLog log)
        {
            _log = log;
            if (_installed) { return true; }
            if (!HasWork) { return false; }

            try
            {
                var harmony = new Harmony("modkit.rebuildguard");
                int ok = 0;

                // 遗物：LoadAllRelic 重建 _allInGameRelicSo / _allRelicDataCollections
                var relicReload = AccessTools.Method(typeof(RelicDataManager), "LoadAllRelic");
                if (relicReload != null)
                {
                    harmony.Patch(relicReload, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(RebuildGuard), nameof(AfterRelicReload))));
                    ok++;
                }
                else { log.Warn("重建守卫：找不到 RelicDataManager.LoadAllRelic，遗物不会自动重注册。"); }

                // 能力：LoadAllAbility 重建 _allAbilitySos 与各分类桶
                var abilityReload = AccessTools.Method(typeof(AbilityDataManager), "LoadAllAbility");
                if (abilityReload != null)
                {
                    harmony.Patch(abilityReload, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(RebuildGuard), nameof(AfterAbilityReload))));
                    ok++;
                }
                else { log.Warn("重建守卫：找不到 AbilityDataManager.LoadAllAbility，能力不会自动重注册。"); }

                _installed = ok > 0;
                if (_installed)
                {
                    log.Info("重建守卫已挂钩 " + ok + " 个重建点（RelicDataManager.LoadAllRelic / " +
                        "AbilityDataManager.LoadAllAbility）—— 游戏重建数据容器后会自动重注册平台内容。");
                }
                return _installed;
            }
            catch (Exception ex)
            {
                log.Warn("重建守卫挂钩失败：" + ex.GetType().Name + " - " + ex.Message);
                return false;
            }
        }

        private static void AfterRelicReload()
        {
            RunAll("遗物数据容器");
        }

        private static void AfterAbilityReload()
        {
            RunAll("能力数据容器");
        }

        private static void RunAll(string what)
        {
            List<Action> snapshot;
            lock (Gate)
            {
                if (ReapplyActions.Count == 0) { return; }
                snapshot = new List<Action>(ReapplyActions);
            }

            int ok = 0, failed = 0;
            foreach (var a in snapshot)
            {
                try { a(); ok++; }
                catch (Exception ex)
                {
                    failed++;
                    if (_log != null)
                    {
                        _log.Warn("重建守卫：重注册动作失败 —— " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }

            if (_log != null)
            {
                _log.Info("重建守卫：游戏重建了" + what + "，已重执行 " + ok +
                    " 个注册动作" + (failed > 0 ? "（失败 " + failed + " 个）" : "") + "。");
            }
        }
    }
}
