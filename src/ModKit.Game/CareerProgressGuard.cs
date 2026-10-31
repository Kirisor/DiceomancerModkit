// =============================================================================
//  ModKit.Game · mod 职业的**生涯进度**与**跨职业成就**为什么能共存
//
//  ★ 数据结构（反编译 Utilities/CareerStatisticsPackage.cs）：
//       overallInfo : 总览（胜场/负场/最高进阶…，由各职业桶累加/取最大得来）
//       classInfos  : List<CareerStatisticsInfoForClass>，**按 className 分桶**
//                     桶里是 playedTime / winCount / loseCount / highestAscensionLevel / winStreak…
//       achievementInfo : 成就标记（StoryMarkerDict）
//
//  ★ 进度怎么记（原版口径，一行都不能改）：
//       · 一局结束时 `CampaignEnd` 先算 `campaignStatisticsInfo.className =
//         CardDataManager.GetClassNameByColorE(campaignUsedColorE)`，
//         再由 `CareerStatisticsPackage.AddCampaignInfo` **写进同名桶**；
//       · 桶里 `highestAscensionLevel` 只在该局**胜利**时取 max（见
//         `CareerStatisticsInfoBase.AddCampaignStatisticsInfo`）；
//       · 主界面的升魔盒按 `classInfo.highestAscensionLevel + 1` 算"这个职业最多能选到进阶几"
//         （`AscensionBox.GetClassMaxAscensionLevel`），胜场为 0 时一律只给进一。
//     ⇒ 只要桶存在且 key 对得上，mod 职业的进阶进度**天然会保存**，不需要自己维护存档。
//
//  ★ 成就怎么被"跨职业聚合"影响（原版唯一一处）：
//       `AchievementManager.RefreshByCampaignVictory` 里
//           num = min(所有 classInfo.highestAscensionLevel)
//           if (num >= 10) 解锁 ACH_CLEAR_ALL_CLASS_ASC_10
//           if (num >= 20) 解锁 ACH_CLEAR_ALL_CLASS_ASC_20
//       mod 职业的桶从 0 开始 ⇒ 装了带职业的 mod 后这两个成就**永远不解锁**。
//
//  ★ 本文件的两条互补做法（缺一不可）：
//       ① **保住 mod 桶**（本文件）—— 结算前若桶缺失就补建，让进度照常写入；
//          绝不为了成就去删 mod 桶：删了进度就没了，而且 `GainAchievement` 会顺手
//          `SaveCareerStatistics`，删桶那一刻存档就把数据写丢。
//       ② **只在聚合处过滤**（`AchievementCompat.cs`）—— 不改原版算法、不动 classInfos，
//          等它跑完再用"只看 Standard_Class_ 前缀"的口径补发这两个成就。
//
//  ★ 另外两条设计约束（写 mod 时别踩）：
//       · 职业颜色**不能等于某个原版职业的三色**：`GetClassNameByColorE` 取的是
//         "colorEs ⊆ 本局三色"的第一个 Class 集合，原版集合先注册 ⇒ 撞色时这一局会被记到
//         **原版职业**的桶里（进度写进别人家 + 误发"用某职业通关"成就）。
//         奶蛙用 紫+绿+黑（原版六职业没有一个含黑）⇒ 天然安全。
//       · 改 modId / 改职业 id ⇒ classKey 变了 ⇒ 旧桶成孤儿（进度看着像丢了）。
//         用 `CareerMigration.Renames` 把旧 key 合并进新 key。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Career;
using HarmonyLib;
using Utilities;

namespace ModKit.Game
{
    /// <summary>
    /// 结算前守住 mod 职业的生涯桶：缺了就补，避免这一局的胜负"写进空气"。
    /// </summary>
    /// <remarks>
    /// 什么时候会缺桶：存档被手工清过、mod 在**已开局**的存档中途才装上、
    /// 或存档来自"桶字段还没建"的旧版本。原版 <c>AddCampaignInfo</c> 对找不到桶的
    /// className 是**静默跳过**（`classInfos.Where(...)` 空集合，循环一次都不跑），
    /// 于是总览更新了、职业桶没更新 —— 症状是"这一局赢了但升魔盒还是只给进阶 1"。
    /// </remarks>
    [HarmonyPatch(typeof(CareerStatisticsPackage), nameof(CareerStatisticsPackage.AddCampaignInfo))]
    internal static class ModClassStatsBucketGuard
    {
        private static bool _logged;

        private static void Prefix(CampaignStatisticsInfo campaignStatisticsInfo)
        {
            try
            {
                if (campaignStatisticsInfo == null) { return; }
                string key = campaignStatisticsInfo.className;
                if (!IsModClassKey(key)) { return; }

                var manager = CareerManager.Instance;
                var package = manager == null ? null : manager.careerStatisticsPackage;
                if (package == null) { return; }
                if (package.classInfos == null) { package.classInfos = new List<CareerStatisticsInfoForClass>(); }
                if (package.classInfos.Any(c => c != null && c.className == key)) { return; }

                package.classInfos.Add(new CareerStatisticsInfoForClass { className = key, isUnlocked = true });
                if (!_logged)
                {
                    _logged = true;
                    ModKitLog.Info("生涯统计：结算前补建 mod 职业桶 '" + key +
                        "'（原版对缺失的桶是静默跳过 —— 不补的话这一局的胜负不会记进职业进度）。");
                }
            }
            catch (Exception ex)
            {
                ModKitLog.Warn("生涯统计：补建 mod 职业桶失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// mod 职业 key 形如 <c>&lt;modId&gt;.&lt;classId&gt;</c>（平台装配时用的就是它）；
        /// 原版六个职业是 <c>Standard_Class_*</c>，不含点。
        /// </summary>
        private static bool IsModClassKey(string className)
        {
            return !string.IsNullOrEmpty(className) && className.IndexOf('.') > 0;
        }
    }
}
