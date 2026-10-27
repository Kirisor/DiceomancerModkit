// =============================================================================
//  ModKit.Game · 把 mod 职业从原版「职业统计」里摘出去
//
//  ★ 为什么要做：
//    原版 `CareerStatisticsPackage.Validate()` 会按「所有 Class 卡包」补条目，
//    mod 职业也在其中。而原版成就「全职业进阶 10 / 20」是取
//    `Min(所有职业的 highestAscensionLevel)` 判定的 —— 只要有一个 mod 职业没打到 20，
//    两个成就就永远不亮（实测：6 个原版职业都是 20，naifrog.milk_frog 是 9）。
//
//  ★ 为什么不能只在成就判定里绕开：
//    玩家要求"把奶蛙那条记录移出去"，也就是让原版数据本身就是干净的 6 职业。
//    存档里的那条已经手工移出（备份在 save-backup 目录），但如果只是删存档，
//    下一次 `LoadCareerStatistics()` 里的 `Validate()` 又会以 0 统计把它加回来
//    （0 比 9 更糟）。所以这里补两道：
//      ① 读档后：就地摘掉所有非 `Standard_Class_` 条目 —— 之后整局的内存里只有原版职业，
//         `AddCampaignInfo` 自然不会给 mod 职业记账，结算存档也是干净的；
//      ② 写档前：临时摘掉、写完立刻还原 —— 兜住"某条路径（如 `SaveAbandonCampaignStatistics`
//         会先从磁盘读一份再存）在写盘瞬间表里带着 mod 职业"的情况。
//
//  ★ 不会踩到 `NewGameLaunchInitialize` 的空引用：
//    那一行 `classInfos.FirstOrDefault(...).isUnlocked = true` 紧跟在 `Validate()` 之后，
//    Validate 一定会先把所有职业条目补齐 —— 我们只在**读档/写档**这两个时刻动手，不碰 Validate。
//
//  ★ 代价（已与玩家确认过方向）：玩 mod 职业的局不再累计职业统计；
//    原有记录已导出为 JSON，随时可以还原。
// =============================================================================

using System;
using System.Collections.Generic;
using Career;
using HarmonyLib;
using UnityEngine;
using Utilities;

namespace ModKit.Game
{
    internal static class CareerStatsCompat
    {
        private static bool _loggedLoad;
        private static bool _loggedSave;

        /// <summary>写档期间被临时摘下的原表（Postfix 里原样放回）。</summary>
        private static List<CareerStatisticsInfoForClass> _restore;

        /// <summary>原版职业的类名前缀（mod 职业用 "&lt;modId&gt;.&lt;id&gt;"）。</summary>
        internal static bool IsVanillaClassName(string className)
        {
            return !string.IsNullOrEmpty(className) &&
                   className.StartsWith("Standard_Class_", StringComparison.Ordinal);
        }

        /// <summary>就地把非原版职业条目摘掉，返回摘掉的数量。</summary>
        internal static int StripInPlace(CareerStatisticsPackage package, string reason)
        {
            if (package == null || package.classInfos == null) { return 0; }

            int removed = 0;
            var names = new List<string>();
            for (int i = package.classInfos.Count - 1; i >= 0; i--)
            {
                var info = package.classInfos[i];
                if (info == null) { package.classInfos.RemoveAt(i); continue; }
                if (IsVanillaClassName(info.className)) { continue; }
                names.Add(info.className);
                package.classInfos.RemoveAt(i);
                removed++;
            }

            if (removed > 0)
            {
                bool first = "读档".Equals(reason) ? !_loggedLoad : !_loggedSave;
                if ("读档".Equals(reason)) { _loggedLoad = true; } else { _loggedSave = true; }
                if (first)
                {
                    Debug.Log("[ModKit] 职业统计（" + reason + "）：已摘出 " + removed +
                        " 个 mod 职业条目（" + string.Join(", ", names.ToArray()) +
                        "）—— 原版「全职业进阶」成就只认 Standard_Class_*。");
                }
            }
            return removed;
        }

        /// <summary>写档前：备份原表 → 摘出 mod 职业（Postfix 负责还原）。</summary>
        internal static void BeginSave(CareerStatisticsPackage package)
        {
            if (_restore != null) { return; }   // 重入保护：已经在写档流程里了
            if (package == null || package.classInfos == null || package.classInfos.Count == 0) { return; }

            bool hasMod = false;
            for (int i = 0; i < package.classInfos.Count; i++)
            {
                var info = package.classInfos[i];
                if (info != null && !IsVanillaClassName(info.className)) { hasMod = true; break; }
            }
            if (!hasMod) { return; }

            _restore = new List<CareerStatisticsInfoForClass>(package.classInfos);
            StripInPlace(package, "写档");
        }

        /// <summary>写档后：把原表放回去，游戏内逻辑看到的仍然是完整表。</summary>
        internal static void EndSave(CareerStatisticsPackage package)
        {
            if (_restore == null) { return; }
            try
            {
                if (package != null) { package.classInfos = _restore; }
            }
            finally
            {
                _restore = null;
            }
        }
    }

    /// <summary>读档后：内存里只留原版职业（整局的记账与结算存档都因此干净）。</summary>
    [HarmonyPatch(typeof(Career.CareerManager), "DeserializeCareerStatisticsPackage")]
    internal static class CareerStatsStripOnLoad
    {
        [HarmonyPostfix]
        private static void Postfix(Utilities.CareerStatisticsPackage statisticsPackage)
        {
            try { CareerStatsCompat.StripInPlace(statisticsPackage, "读档"); }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModKit] 职业统计（读档）摘除失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }

    /// <summary>写档时：文件里不落 mod 职业条目（内存表原样还原）。</summary>
    [HarmonyPatch(typeof(SaveLoad.SaveLoadManager), "SaveCareerStatistics")]
    internal static class CareerStatsStripOnSave
    {
        [HarmonyPrefix]
        private static void Prefix(Utilities.CareerStatisticsPackage statisticsPackage)
        {
            try { CareerStatsCompat.BeginSave(statisticsPackage); }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModKit] 职业统计（写档）摘除失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        [HarmonyPostfix]
        private static void Postfix(Utilities.CareerStatisticsPackage statisticsPackage)
        {
            try { CareerStatsCompat.EndSave(statisticsPackage); }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModKit] 职业统计（写档）还原失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
