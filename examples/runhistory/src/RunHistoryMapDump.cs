// =============================================================================
//  Run History（战绩）· 地图转储（判定「同种子是否同图」的硬证据）
//
//  ★ 为什么要它：
//    只靠肉眼看两张地图，说不清「哪一部分不一样」（段落组成？节点类型？连边？）。
//    v0.2.0 就是在这种模糊反馈里绕了一圈。所以每次生成地图都把它**原样落盘**：
//      maps/<时间>-<seed>-<章节>.json    段落 / 节点 / 连边 / 坐标
//    并在日志里打一行**指纹**（结构哈希，不含坐标抖动）：
//      战绩：地图已生成｜ChapterPlain｜段 11 / 节点 41｜指纹 3f2a1c9d｜明细 maps\...
//    同一个种子跑两次 → 两行指纹一对比，一样就是一样，不一样就看 json 差在哪。
//
//  ★ 坐标不进指纹：`MapSectionPlainNormalRoad` 用**未播种的** UnityEngine.Random 给节点
//    加 ±0.05 的视觉抖动，它不影响任何玩法，却会让坐标每次都差一点点。
//    结构（段落类型 / 节点类型 / 连边）才是要固定的东西。
// =============================================================================

using System;
using System.Globalization;
using System.IO;
using System.Text;
using World.ChapterMapGenerator;
using World.ChapterMapGenerator.MapSectionGenerator;
using World.NewMap;

namespace RunHistory
{
    internal static class RunHistoryMapDump
    {
        private const int KeepFiles = 80;

        internal static void Write(World.WorldInformation info, int seed, string reason)
        {
            try
            {
                ChapterMapInfo chapterInfo = info.ChapterMapInfo;
                MapSectionInfo[] sections = chapterInfo.mapSectionInfos;
                if (sections == null) { sections = new MapSectionInfo[0]; }

                uint hash = 2166136261u;
                int nodeCount = 0;
                var sb = new StringBuilder(4096);
                sb.Append("{\n  \"seed\": ").Append(seed);
                sb.Append(",\n  \"at\": \"").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append('"');
                sb.Append(",\n  \"reason\": \"").Append(Escape(reason)).Append('"');
                sb.Append(",\n  \"chapter\": \"").Append(chapterInfo.chapterName).Append('"');
                sb.Append(",\n  \"randomState\": \"").Append(Escape(RunHistoryRandomState.LastReport ?? "")).Append('"');
                sb.Append(",\n  \"randomStateOk\": ").Append(RunHistoryRandomState.LastOk ? "true" : "false");
                sb.Append(",\n  \"sections\": [");

                for (int s = 0; s < sections.Length; s++)
                {
                    MapSectionInfo sec = sections[s];
                    MapNodeInfo[] nodes = sec.mapNodeInfos;
                    if (nodes == null) { nodes = new MapNodeInfo[0]; }
                    nodeCount += nodes.Length;

                    hash = Mix(hash, sec.sectionType);
                    hash = Mix(hash, sec.mapInfoSectionID);
                    hash = Mix(hash, sec.depth);
                    hash = Mix(hash, sec.backGroundName);
                    hash = Mix(hash, nodes.Length);

                    sb.Append(s == 0 ? "\n    {" : ",\n    {");
                    sb.Append("\n      \"id\": ").Append(sec.mapInfoSectionID);
                    sb.Append(",\n      \"type\": \"").Append(Escape(sec.sectionType)).Append('"');
                    sb.Append(",\n      \"bg\": \"").Append(Escape(sec.backGroundName)).Append('"');
                    sb.Append(",\n      \"depth\": ").Append(sec.depth);
                    sb.Append(",\n      \"pos\": [").Append(F(sec.sectionLocalPos.x)).Append(", ")
                      .Append(F(sec.sectionLocalPos.y)).Append(']');
                    sb.Append(",\n      \"nodes\": [");

                    for (int n = 0; n < nodes.Length; n++)
                    {
                        MapNodeInfo node = nodes[n];
                        hash = Mix(hash, node.nodeID.sectionIndex);
                        hash = Mix(hash, node.nodeID.nodeIndex);
                        hash = Mix(hash, (int)node.nodeType);
                        hash = Mix(hash, node.isChapterEnd ? 1 : 0);

                        sb.Append(n == 0 ? "\n        {" : ",\n        {");
                        sb.Append("\"node\": \"").Append(node.nodeID.sectionIndex).Append('-')
                          .Append(node.nodeID.nodeIndex).Append('"');
                        sb.Append(", \"type\": \"").Append(node.nodeType).Append('"');
                        sb.Append(", \"depth\": ").Append(node.nodeDepth);
                        sb.Append(", \"pos\": [").Append(F(node.localPosition.x)).Append(", ")
                          .Append(F(node.localPosition.y)).Append(']');

                        NodeID[] next = node.nextNodeIDs;
                        sb.Append(", \"next\": [");
                        if (next != null)
                        {
                            for (int k = 0; k < next.Length; k++)
                            {
                                if (k > 0) { sb.Append(", "); }
                                sb.Append('"').Append(next[k].sectionIndex).Append('-').Append(next[k].nodeIndex).Append('"');
                                hash = Mix(hash, next[k].sectionIndex);
                                hash = Mix(hash, next[k].nodeIndex);
                            }
                        }
                        sb.Append(']');

                        if (!string.IsNullOrEmpty(node.smallGameName))
                        {
                            sb.Append(", \"smallGame\": \"").Append(Escape(node.smallGameName)).Append('"');
                        }
                        if (!string.IsNullOrEmpty(node.specialIconName))
                        {
                            sb.Append(", \"icon\": \"").Append(Escape(node.specialIconName)).Append('"');
                        }
                        sb.Append('}');
                    }
                    sb.Append("\n      ]");
                    sb.Append("\n    }");
                }
                sb.Append("\n  ]\n}\n");

                string dir = Path.Combine(RunHistoryStore.DataDir, "maps");
                Directory.CreateDirectory(dir);
                string name = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + seed + "-" + chapterInfo.chapterName + ".json";
                string path = Path.Combine(dir, name);
                RunHistoryStore.AtomicWrite(path, sb.ToString());
                Prune(dir);

                RunHistoryPlugin.Log.LogInfo("战绩：地图已生成（" + reason + "）｜" + chapterInfo.chapterName +
                    "｜段 " + sections.Length + " / 节点 " + nodeCount +
                    "｜指纹 " + hash.ToString("x8") + "｜明细 " + Relative(path));
            }
            catch (Exception ex)
            {
                RunHistoryPlugin.Log.LogWarning("战绩：地图转储失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // ---------------- 内部 ----------------

        private static string Relative(string path)
        {
            try
            {
                string root = RunHistoryStore.DataDir;
                if (!string.IsNullOrEmpty(root) && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return path.Substring(root.Length).TrimStart('\\', '/');
                }
            }
            catch { }
            return path;
        }

        private static void Prune(string dir)
        {
            try
            {
                string[] files = Directory.GetFiles(dir, "*.json");
                if (files.Length <= KeepFiles) { return; }
                Array.Sort(files, StringComparer.Ordinal);
                for (int i = 0; i < files.Length - KeepFiles; i++)
                {
                    try { File.Delete(files[i]); } catch { }
                }
            }
            catch { }
        }

        private static uint Mix(uint hash, string text)
        {
            if (string.IsNullOrEmpty(text)) { return Mix(hash, 0); }
            hash ^= (uint)text.Length; hash *= 16777619u;
            for (int i = 0; i < text.Length; i++) { hash ^= text[i]; hash *= 16777619u; }
            return hash;
        }

        private static uint Mix(uint hash, int value)
        {
            hash ^= (uint)value;
            hash *= 16777619u;
            return hash;
        }

        private static string F(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) { return ""; }
            return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
