using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Career;
using HarmonyLib;
using SaveLoad;
using ModKit.Core;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using Utilities;

namespace ModKit.Game
{
    /// <summary>
    /// 本地化表被游戏重新加载后，把平台登记的文案补写回去。
    /// </summary>
    /// <remarks>
    /// 实机症状（2026-10-23）：启动时注入成功，但打开「生涯数据」时
    /// 职业名显示 <c>No translation found for 'manual_demo.milk_frog' in Encyclopedia</c>
    /// —— Unity Localization 的表是按需加载的，我们启动时写的那份被换掉了。
    /// </remarks>
    [HarmonyPatch(typeof(LocalizedStringDatabase), nameof(LocalizedStringDatabase.GetTable), new Type[] { typeof(string) })]
    internal static class LocalizationTableGuard
    {
        private static void Postfix(string tableName)
        {
            try
            {
                var pump = PlatformRuntime.Localization;
                if (pump == null || string.IsNullOrEmpty(tableName)) { return; }
                pump.Reinforce(tableName);
            }
            catch { }
        }
    }

    /// <summary>
    /// 生涯统计的**职业 key 迁移**。
    /// </summary>
    /// <remarks>
    /// 成因：生涯数据按 <see cref="CareerStatisticsInfoForClass.className"/> 分桶存储
    /// （`FULLVERSAVE/CareerStatisticsSave.xml`）。职业的 classKey 一旦变化
    /// （换 mod id、改职业 id、删掉试水职业），旧桶就会以"另一个职业"的身份留在存档里 ——
    /// 实机表现就是「生涯数据」里一个 mod 职业裂成三行：
    /// <c>manual_demo.milk_frog</c> / <c>manual_demo.trial_trio</c> / <c>naifrog.milk_frog</c>。
    /// <para>
    /// 这里在读档后把旧键**合并**进规范键（数值相加、最高值取 max），并立即回写。
    /// 迁移表写死得很小，因为只有"平台自己改过 id"这一种来源。
    /// </para>
    /// </remarks>
    [HarmonyPatch(typeof(SaveLoadManager), nameof(SaveLoadManager.LoadCareerStatistics))]
    internal static class CareerStatisticsMigration
    {
        /// <summary>旧 classKey → 规范 classKey。</summary>
        private static readonly Dictionary<string, string> Renames =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "naifrog.milk_frog", "manual_demo.milk_frog" },
                { "manual_demo.trial_trio", "manual_demo.milk_frog" },
            };

        private static readonly string[] SumFields =
        {
            "playedTimeInSecond", "winCount", "loseCount", "bossSlainCount",
            "eliteSlainCount", "enemySlainCount", "catchByPurpleMistCount",
        };

        private static readonly string[] MaxFields =
        {
            "highestAscensionLevel", "winStreak", "highestWinStreak",
        };

        private static void Postfix(SaveLoadManager __instance, CareerStatisticsPackage __result)
        {
            try
            {
                if (__result == null || __result.classInfos == null) { return; }

                bool changed = false;

                foreach (var kv in Renames)
                {
                    var oldInfo = __result.classInfos.FirstOrDefault(c => c != null && c.className == kv.Key);
                    if (oldInfo == null) { continue; }

                    var target = __result.classInfos.FirstOrDefault(c => c != null && c.className == kv.Value);
                    if (target == null)
                    {
                        // 没有规范桶 → 直接把旧桶改名（保住数据）
                        oldInfo.className = kv.Value;
                        ModKitLog.Info("生涯统计迁移：'" + kv.Key + "' 直接改名为 '" + kv.Value + "'。");
                    }
                    else
                    {
                        MergeInto(target, oldInfo);
                        __result.classInfos.Remove(oldInfo);
                        ModKitLog.Info("生涯统计迁移：'" + kv.Key + "' 已合并进 '" + kv.Value + "'。");
                    }
                    changed = true;
                }

                if (!changed) { return; }
                if (__instance == null)
                {
                    ModKitLog.Warn("生涯统计迁移：SaveLoadManager 为 null，改动只在内存里（下次启动会再迁一次）。");
                    return;
                }

                __instance.SaveCareerStatistics(__result);
                ModKitLog.Info("生涯统计迁移：已回写 CareerStatisticsSave.xml。");
            }
            catch (Exception ex)
            {
                ModKitLog.Error("生涯统计迁移失败：" + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>把 <paramref name="src"/> 的统计累加进 <paramref name="dst"/>。</summary>
        private static void MergeInto(CareerStatisticsInfoForClass dst, CareerStatisticsInfoForClass src)
        {
            foreach (string f in SumFields)
            {
                AddField(dst, src, f);
            }
            foreach (string f in MaxFields)
            {
                MaxField(dst, src, f);
            }

            // fastestWinTimeInSecond：取"最小的非零值"（0 = 没赢过）
            float a = GetFloat(dst, "fastestWinTimeInSecond");
            float b = GetFloat(src, "fastestWinTimeInSecond");
            float fastest = 0f;
            if (a > 0f && b > 0f) { fastest = Math.Min(a, b); }
            else if (a > 0f) { fastest = a; }
            else if (b > 0f) { fastest = b; }
            SetFloat(dst, "fastestWinTimeInSecond", fastest);

            dst.isUnlocked = dst.isUnlocked || src.isUnlocked;
        }

        private static void AddField(object dst, object src, string name)
        {
            try
            {
                var f = dst.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
                if (f == null) { return; }
                if (f.FieldType == typeof(int))
                {
                    f.SetValue(dst, (int)f.GetValue(dst) + (int)f.GetValue(src));
                }
                else if (f.FieldType == typeof(float))
                {
                    f.SetValue(dst, (float)f.GetValue(dst) + (float)f.GetValue(src));
                }
                else if (f.FieldType == typeof(double))
                {
                    f.SetValue(dst, (double)f.GetValue(dst) + (double)f.GetValue(src));
                }
            }
            catch { }
        }

        private static void MaxField(object dst, object src, string name)
        {
            try
            {
                var f = dst.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
                if (f == null || f.FieldType != typeof(int)) { return; }
                int a = (int)f.GetValue(dst);
                int b = (int)f.GetValue(src);
                if (b > a) { f.SetValue(dst, b); }
            }
            catch { }
        }

        private static float GetFloat(object o, string name)
        {
            try
            {
                var f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
                return f == null ? 0f : Convert.ToSingle(f.GetValue(o));
            }
            catch { return 0f; }
        }

        private static void SetFloat(object o, string name, float v)
        {
            try
            {
                var f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public);
                if (f != null) { f.SetValue(o, v); }
            }
            catch { }
        }
    }
}
