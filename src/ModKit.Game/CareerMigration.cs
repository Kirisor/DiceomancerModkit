using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Career;
using HarmonyLib;
using SaveLoad;
using ModKit.Core;
using Utilities;

namespace ModKit.Game
{
    /// <summary>
    /// 打开「生涯数据」前，把平台登记的文案补写回 Encyclopedia 表。
    /// </summary>
    /// <remarks>
    /// 实机症状（2026-10-23）：启动时注入成功，但打开「生涯数据」时职业名显示
    /// <c>No translation found for 'manual_demo.milk_frog' in Encyclopedia</c>
    /// —— Unity Localization 的表是按需加载、用完可释放的，我们启动时写的那份会被换掉。
    /// <para>
    /// ★ 这里**不**挂 <c>LocalizedStringDatabase.GetTable(string)</c>：
    ///   该方法不在 <c>LocalizedStringDatabase</c> 上声明（是基类/接口转发），
    ///   Harmony 的 <c>DeclaredMethod</c> 解析不到 → 整个 <c>PatchAll</c> 抛异常 →
    ///   平台被判定"桥安装失败"而整体停用（2026-10-23 实机：游戏按原版运行）。
    ///   改挂游戏自己的界面方法：生涯数据组装列表之前补写一次，时机确定、签名稳定。
    /// </para>
    /// </remarks>
    [HarmonyPatch]
    internal static class StatisticsLocalizationGuard
    {
        private static bool _logged;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Collection.SubCollectionCareers), "SetStatisticsEntries");
        }

        private static void Prefix()
        {
            try
            {
                var pump = PlatformRuntime.Localization;
                if (pump == null) { return; }
                pump.Reinforce("Encyclopedia");
                if (!_logged)
                {
                    _logged = true;
                    ModKitLog.Info("生涯数据：打开面板前补写 Encyclopedia 文案（表被游戏重载后也能显示职业名）。");
                }
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
        /// <summary>旧 classKey → 规范 classKey（规范键 = 当前 mod id 拼出的 classKey）。</summary>
        /// <remarks>
        /// 只收录"平台自己改过 id"造成的旧桶。新增一条即可，键值方向必须是
        /// **旧 → 新**：迁移后所有数据都落在当前职业的桶里，文案才查得到。
        /// </remarks>
        private static readonly Dictionary<string, string> Renames =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "manual_demo.milk_frog", "naifrog.milk_frog" },    // mod 改名 manual_demo → naifrog
                { "manual_demo.trial_trio", "naifrog.milk_frog" },   // 更早的试水职业，并入奶蛙
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
