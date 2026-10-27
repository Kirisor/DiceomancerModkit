// =============================================================================
//  Run History（战绩）· 段落挑选探针（判定"游戏的随机流到底有没有被播种"）
//
//  为什么需要它：`ShufflingExtension.rng` 是 `static readonly`，而它的读取方
//  `ShufflingExtension.Random<T>` 与它同在一个程序集 —— 换字段（SetValue）不生效，
//  反射回读却"看起来成功"。所以要拿**实际挑选结果**做判据，而不是拿字段状态。
//
//  每次 Plain 章生成地图，打一行：
//    战绩：Plain 段落挑选｜候选 road=[MapSectionPlainNormalRoad_1, ...] mid=[...]
//           ｜选中 NE=... SE=... SM=... NM=... NF=... SF=...
//           ｜按种子应取 road#2,road#0,mid#1,mid#0,road#3,road#1
//
//  同一颗种子跑两次：把「选中」两行一比就知道段落是否按种子；把「选中」与「按种子应取」
//  一比就知道游戏的随机流到底有没有被我们按住。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using World.ChapterMapGenerator;
using World.ChapterMapGenerator.MapSectionGenerator;

namespace RunHistory
{
    internal static class RunHistoryMapProbe
    {
        private static readonly string[] SlotFields =
        {
            "northEntre", "southEntre", "southMid", "northMid", "northFinal", "southFinal",
        };

        internal static void LogPlainPicks(ChapterMapGeneratorBase generator)
        {
            try
            {
                if (!(generator is ChapterMapGeneratorPlain)) { return; }

                Type type = typeof(ChapterMapGeneratorPlain);
                MapSectionBase[] roads = Field<MapSectionBase[]>(type, generator, "possiblePlainRoadSection");
                MapSectionBase[] mids = Field<MapSectionBase[]>(type, generator, "possibleMidSections");

                var sb = new StringBuilder("战绩：Plain 段落挑选｜");
                sb.Append("候选 road=").Append(Names(roads));
                sb.Append(" mid=").Append(Names(mids));

                sb.Append("｜选中");
                string[] tags = { "NE", "SE", "SM", "NM", "NF", "SF" };
                for (int i = 0; i < SlotFields.Length; i++)
                {
                    var section = Field<MapSectionBase>(type, generator, SlotFields[i]);
                    sb.Append(' ').Append(tags[i]).Append('=')
                      .Append(section == null ? "<null>" : CleanName(section.name));
                }

                // 若游戏的随机流确实被播种，这 6 次取值应该是 new Random(seed) 的前 6 个 Next
                int seed = RunHistorySeed.CurrentSeed();
                if (roads != null && roads.Length > 0 && mids != null && mids.Length > 0)
                {
                    var expect = new System.Random(seed);
                    sb.Append("｜按种子应取 road#").Append(expect.Next(roads.Length));
                    sb.Append(",road#").Append(expect.Next(roads.Length));
                    sb.Append(",mid#").Append(expect.Next(mids.Length));
                    sb.Append(",mid#").Append(expect.Next(mids.Length));
                    sb.Append(",road#").Append(expect.Next(roads.Length));
                    sb.Append(",road#").Append(expect.Next(roads.Length));
                    sb.Append("（seed ").Append(seed).Append('）');
                }

                RunHistoryPlugin.Log.LogInfo(sb.ToString());
            }
            catch (Exception ex)
            {
                RunHistoryPlugin.Log.LogWarning("战绩：段落探针失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static T Field<T>(Type type, object instance, string name) where T : class
        {
            try
            {
                FieldInfo f = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f == null) { return null; }
                return f.GetValue(instance) as T;
            }
            catch { return null; }
        }

        private static string Names(MapSectionBase[] sections)
        {
            if (sections == null || sections.Length == 0) { return "[]"; }
            var sb = new StringBuilder("[");
            for (int i = 0; i < sections.Length; i++)
            {
                if (i > 0) { sb.Append(", "); }
                sb.Append(i).Append(':').Append(sections[i] == null ? "<null>" : CleanName(sections[i].name));
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) { return "<unnamed>"; }
            const string Clone = "(Clone)";
            return name.EndsWith(Clone, StringComparison.Ordinal)
                ? name.Substring(0, name.Length - Clone.Length).Trim()
                : name;
        }
    }
}
