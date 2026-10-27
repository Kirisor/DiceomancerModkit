// =============================================================================
//  ModKit.Game · 「全职业进阶 N」成就的补发（跟 mod 有关，只是症状看着像玄学）
//
//  ★ 症状（2026-10-28 反馈）：6 个原版职业都通关过进阶 20 了，「全职业进阶 20」没解锁。
//
//  ★ 原版判定（AchievementManager.RefreshByCampaignVictory）：
//        int num = 20;
//        foreach (var info in careerStatisticsPackage.classInfos)
//            num = Mathf.Min(info.highestAscensionLevel, num);   // ← 取**所有**职业的最小值
//        if (num >= 10) GainAchievement("ACH_CLEAR_ALL_CLASS_ASC_10");
//        if (num >= 20) GainAchievement("ACH_CLEAR_ALL_CLASS_ASC_20");
//    classInfos 是按「所有 Class 卡包」逐条补出来的（CareerStatisticsPackage.Validate），
//    **mod 职业也在表里**，而它的 highestAscensionLevel 从 0 开始。
//    于是装了任何带职业的 mod 之后，num 被那个 mod 职业压在低位 —— 两个成就都永远不会触发。
//    实测存档（2026-10-28）：
//        Standard_Class_PBV/PGB/PGV/PRB/PRG/PRV  →  highestAscensionLevel = 20
//        naifrog.milk_frog                       →  9      ⇒ num = 9
//
//  ★ 做法：**不改原版算法，只在它跑完之后补一次**。
//    自己按「只看原版职业」再算一遍最小值（口径 = Standard_Class_ 前缀），满足就补发。
//    为什么不改 classInfos：判定期间 `GainAchievement` 会顺手 `SaveCareerStatistics`，
//    那时表里若少了 mod 职业，存档会把它的统计写丢 —— 所以一行都不动，只做补发。
//    原版能正确解锁的场景（没有 mod 职业 / 全职业都到 20）我们什么都不做（标记已是 1）。
//
//  ★ 另补「读档即结算」：这个条件是**持久状态**（各职业历史上通关过的最高进阶），
//    原版只在一局胜利那一刻检查；玩家早就满足却因为上面的 bug 没拿到时，不该非要再赢一局。
// =============================================================================

using System;
using Career;
using HarmonyLib;
using UnityEngine;

namespace ModKit.Game
{
    /// <summary>按「原版职业」口径补发「全职业进阶 10 / 20」。</summary>
    internal static class AchievementCompat
    {
        private const string AchievementAsc10 = "ACH_CLEAR_ALL_CLASS_ASC_10";
        private const string AchievementAsc20 = "ACH_CLEAR_ALL_CLASS_ASC_20";

        private static bool _logged;

        /// <summary>原版职业的类名前缀（mod 职业用 "&lt;modId&gt;.&lt;id&gt;"，不会撞上）。</summary>
        private static bool IsVanillaClassName(string className)
        {
            return !string.IsNullOrEmpty(className) &&
                   className.StartsWith("Standard_Class_", StringComparison.Ordinal);
        }

        /// <summary>按原版职业各自通关过的最高进阶，补发两个全职业成就。</summary>
        internal static void GrantIfEarned(string why)
        {
            try
            {
                var manager = CareerManager.Instance;
                if (manager == null || manager.achievementManager == null) { return; }

                var pkg = manager.careerStatisticsPackage;
                if (pkg == null || pkg.classInfos == null || pkg.classInfos.Count == 0) { return; }

                int min = 20;
                int counted = 0;
                int modded = 0;
                for (int i = 0; i < pkg.classInfos.Count; i++)
                {
                    var info = pkg.classInfos[i];
                    if (info == null) { continue; }
                    if (!IsVanillaClassName(info.className)) { modded++; continue; }
                    min = Mathf.Min(min, info.highestAscensionLevel);
                    counted++;
                }
                if (counted == 0) { return; }

                bool got10 = min >= 10;
                bool got20 = min >= 20;
                if (!got10) { return; }

                if (got10) { Grant(manager, AchievementAsc10, 10, min, counted, modded, why); }
                if (got20) { Grant(manager, AchievementAsc20, 20, min, counted, modded, why); }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModKit] 成就补发失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void Grant(CareerManager manager, string key, int need,
            int min, int counted, int modded, string why)
        {
            int already = 0;
            try { already = manager.careerStatisticsPackage.achievementInfo.GetStoryMarkerProgress(key); }
            catch { }

            // 成就表里有没有这个 key —— 没有的话 GainAchievement 会**静默什么都不做**
            // （它内部先 allAchievementInfo.Any(key) 再写），这是"日志里连一句都没有"的常见原因。
            bool inTable = false;
            try
            {
                var all = manager.achievementManager.allAchievementInfo;
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i].achievementKey == key) { inTable = true; break; }
                    }
                }
            }
            catch { }

            if (!_logged)
            {
                _logged = true;
                Debug.Log("[ModKit] 成就检定：" + key + "（" + why + "｜原版职业 " + counted +
                    " 个，最低进阶 " + min + " ≥ " + need +
                    (modded > 0 ? "；" + modded + " 个 mod 职业不计入" : "") +
                    "｜成就表内=" + (inTable ? "有" : "**没有**") +
                    "｜本地标记(前)=" + already + "）。");
            }

            if (already >= 1) { return; }
            if (!inTable) { return; }

            manager.achievementManager.GainAchievement(key);   // 写存档标记 + 解 Steam 成就

            int after = 0;
            try { after = manager.careerStatisticsPackage.achievementInfo.GetStoryMarkerProgress(key); }
            catch { }
            Debug.Log("[ModKit] 成就补发：" + key + "｜本地标记(后)=" + after +
                (after >= 1 ? "（已写入存档）" : "（**没写进去**，检查 SaveCareerStatistics 是否抛错）"));
        }
    }

    /// <summary>一局胜利结算之后补一次（原版判定刚跑完，此时职业统计已更新）。</summary>
    [HarmonyPatch(typeof(AchievementManager), "RefreshByCampaignVictory")]
    internal static class AllClassAchievementOnVictory
    {
        [HarmonyPostfix]
        private static void Postfix() { AchievementCompat.GrantIfEarned("胜利结算"); }
    }

    /// <summary>读档后补一次：这批职业统计是持久状态，不该非要"再赢一局"才结算。</summary>
    [HarmonyPatch(typeof(CareerManager), "DeserializeCareerStatisticsPackage")]
    internal static class AllClassAchievementOnLoad
    {
        [HarmonyPostfix]
        private static void Postfix() { AchievementCompat.GrantIfEarned("读档"); }
    }
}
