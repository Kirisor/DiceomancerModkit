// =============================================================================
//  ModKit.Game · 战役存档的"自定义颜色"编解码层（全图版）
//
//  为什么需要：
//    战役存档走 DataContractSerializer（枚举按**名字**写 XML），遇到枚举里没有的
//    取值会直接抛 SerializationException —— 自定义色 (ColorE)6 就是这种情况。
//    CareerSave / WorldSave 走 Odin（枚举写数字），不受影响。
//
//  覆盖范围（实机两次踩坑的合集）：
//    ① CampaignInformation.campaignUsedColorE        —— 职业三色
//    ② cardInfo.cardColorEs 之类的卡级数组            —— 卡随战役存档时序列化
//    ③ 卡的费用数组 cardManaCostColorEArrays[].colorEs
//
//  机制：
//    写盘前 —— 深度遍历对象图，把每一处自定义 ColorE 换成 ColorE.Void，
//              并把「路径 → 原值」写进同目录的 CampaignSave.colors.json；
//    写盘后 —— 用记录的数组引用就地还原（内存里始终是 6，运行期不受影响）；
//    读盘前 —— 按 sidecar 的路径把值还原。
//
//  路径语法：root.Field[3].Field[0]。
//  降级：路径解析失败只记 WARNING，游戏照常继续（对应位置退化成 Void）。
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Card;
using ColorE = Card.ColorE;
using HarmonyLib;
using UnityEngine;

namespace ModKit.Game
{
    internal static class CampaignColorCodec
    {
        internal const string SidecarName = "CampaignSave.colors.json";
        private const int MaxDepth = 8;
        private const int MaxItems = 512;

        private sealed class Hit
        {
            public string Path;
            public ColorE[] Array;
            public int Index;
            public ColorE Original;
        }

        private static readonly List<Hit> _pending = new List<Hit>();

        internal static bool ReplaceCustomValues(object root)
        {
            _pending.Clear();
            if (root == null) { return false; }
            try
            {
                Walk(root, "root", 0, new HashSet<object>(ReferenceComparer), _pending);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModKit] 战役存档颜色扫描异常：" + ex.Message);
            }
            return _pending.Count > 0;
        }

        internal static void RestoreInMemory()
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                var h = _pending[i];
                if (h.Array == null || h.Index < 0 || h.Index >= h.Array.Length) { continue; }
                h.Array[h.Index] = h.Original;
            }
            _pending.Clear();
        }

        internal static List<string> AuditPaths(object root)
        {
            var hits = new List<Hit>();
            try { Walk(root, "root", 0, new HashSet<object>(ReferenceComparer), hits); }
            catch { }
            var paths = new List<string>(hits.Count);
            for (int i = 0; i < hits.Count; i++) { paths.Add(hits[i].Path); }
            for (int i = 0; i < hits.Count; i++)
            {
                var h = hits[i];
                if (h.Array != null && h.Index >= 0 && h.Index < h.Array.Length) { h.Array[h.Index] = h.Original; }
            }
            return paths;
        }

        private static void Walk(object node, string path, int depth, HashSet<object> seen, List<Hit> hits)
        {
            if (node == null || depth > MaxDepth) { return; }
            var t = node.GetType();
            if (t.IsPrimitive || t == typeof(string) || t.IsEnum) { return; }
            if (!t.IsValueType && !seen.Add(node)) { return; }

            if (node is Array arr)
            {
                var et = t.GetElementType();
                if (et == typeof(ColorE))
                {
                    var ca = (ColorE[])arr;
                    for (int i = 0; i < ca.Length; i++)
                    {
                        if (!IsCustom(ca[i])) { continue; }
                        hits.Add(new Hit { Path = path + "[" + i + "]", Array = ca, Index = i, Original = ca[i] });
                        ca[i] = ColorE.Void;
                    }
                    return;
                }

                int n = 0;
                foreach (var v in arr)
                {
                    Walk(v, path + "[" + n + "]", depth + 1, seen, hits);
                    n++;
                    if (n > MaxItems) { break; }
                }
                return;
            }

            var fields = t.GetFields(System.Reflection.BindingFlags.Instance |
                                     System.Reflection.BindingFlags.Public |
                                     System.Reflection.BindingFlags.NonPublic);
            foreach (var f in fields)
            {
                if (f.IsStatic) { continue; }
                object v;
                try { v = f.GetValue(node); } catch { continue; }
                if (v == null) { continue; }
                Walk(v, path + "." + f.Name, depth + 1, seen, hits);
            }
        }

        [Serializable]
        private sealed class Entry
        {
            public string path;
            public int value;
        }

        [Serializable]
        private sealed class Sidecar
        {
            public List<Entry> entries = new List<Entry>();
        }

        internal static string FolderOf(SaveLoad.SaveLoadManager mgr)
        {
            try
            {
                var f = mgr.GetType().GetField("_savFolder",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                var folder = (f == null) ? null : (f.GetValue(mgr) as string);
                if (!string.IsNullOrEmpty(folder)) { return folder; }
            }
            catch { }
            return Path.Combine(Application.persistentDataPath, "Save") + Path.DirectorySeparatorChar;
        }

        internal static void WriteSidecar(string folder)
        {
            if (string.IsNullOrEmpty(folder)) { return; }
            try
            {
                if (_pending.Count == 0) { DeleteSidecar(folder); return; }

                var data = new Sidecar();
                for (int i = 0; i < _pending.Count; i++)
                {
                    data.entries.Add(new Entry { path = _pending[i].Path, value = (int)_pending[i].Original });
                }

                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, SidecarName),
                    Newtonsoft.Json.JsonConvert.SerializeObject(data, Newtonsoft.Json.Formatting.Indented),
                    new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModKit] 战役颜色 sidecar 写入失败：" + ex.Message);
            }
        }

        internal static void DeleteSidecar(string folder)
        {
            if (string.IsNullOrEmpty(folder)) { return; }
            try
            {
                var p = Path.Combine(folder, SidecarName);
                if (File.Exists(p)) { File.Delete(p); }
            }
            catch { }
        }

        internal static int ApplySidecar(string folder, object root)
        {
            if (string.IsNullOrEmpty(folder) || root == null) { return 0; }
            int applied = 0;
            try
            {
                var p = Path.Combine(folder, SidecarName);
                if (!File.Exists(p)) { return 0; }

                var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Sidecar>(File.ReadAllText(p));
                if (data == null || data.entries == null) { return 0; }

                foreach (var e in data.entries)
                {
                    var arr = ResolveColorArray(root, e.path);
                    int idx = LastIndex(e.path);
                    if (arr == null || idx < 0 || idx >= arr.Length) { continue; }
                    arr[idx] = (ColorE)e.value;
                    applied++;
                }
                Debug.Log("[ModKit] 战役颜色：已按 sidecar 还原 " + applied + " 处自定义色。");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModKit] 战役颜色 sidecar 还原失败：" + ex.Message);
            }
            return applied;
        }

        /// <summary>
        /// 把 "root.playerDeck[0].cardColorEs[0]" 解析到**包含最后一个下标的那个 ColorE[]**。
        /// 注意：返回的是数组本身（调用方自己按 LastIndex 写值），不是下标取出来的枚举值
        /// —— 之前写成"取下标后的值"导致还原 0 处（实机 2026-10-21 复现）。
        /// </summary>
        private static ColorE[] ResolveColorArray(object root, string path)
        {
            if (string.IsNullOrEmpty(path)) { return null; }
            var parts = Split(path);
            object cur = root;
            ColorE[] host = null;

            for (int i = 0; i < parts.Count; i++)
            {
                if (cur == null) { return null; }
                var seg = parts[i];

                if (seg.IsIndex)
                {
                    bool last = (i == parts.Count - 1);

                    // 情况 A：当前就在一个 ColorE[] 里（路径以它的下标结尾）
                    if (host != null)
                    {
                        if (last) { return host; }
                        if (seg.Index < 0 || seg.Index >= host.Length) { return null; }
                        cur = host[seg.Index];
                        host = null;
                        continue;
                    }

                    // 情况 B：容器是普通数组 / 列表
                    if (cur is Array arr)
                    {
                        if (seg.Index < 0 || seg.Index >= arr.Length) { return null; }
                        cur = arr.GetValue(seg.Index);
                        host = cur as ColorE[];
                        continue;
                    }

                    var list = cur as System.Collections.IList;
                    if (list != null)
                    {
                        if (seg.Index < 0 || seg.Index >= list.Count) { return null; }
                        cur = list[seg.Index];
                        host = cur as ColorE[];
                        continue;
                    }

                    return null;
                }

                // 字段段
                if (string.Equals(seg.Name, "root", StringComparison.Ordinal) && ReferenceEquals(cur, root)) { continue; }

                var f = cur.GetType().GetField(seg.Name,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                if (f == null) { return null; }
                try { cur = f.GetValue(cur); } catch { return null; }

                host = cur as ColorE[];
                if (host != null && i + 1 < parts.Count && parts[i + 1].IsIndex) { return host; }
            }

            return host;
        }

        private sealed class Seg
        {
            public string Name;
            public int Index;
            public bool IsIndex;
        }

        private static List<Seg> Split(string path)
        {
            var list = new List<Seg>();
            var buf = new StringBuilder();
            foreach (var ch in path)
            {
                if (ch == '.' || ch == '[')
                {
                    if (buf.Length > 0) { list.Add(new Seg { Name = buf.ToString() }); buf.Length = 0; }
                }
                else if (ch == ']')
                {
                    int v;
                    if (int.TryParse(buf.ToString(), out v)) { list.Add(new Seg { Index = v, IsIndex = true }); }
                    buf.Length = 0;
                }
                else { buf.Append(ch); }
            }
            if (buf.Length > 0) { list.Add(new Seg { Name = buf.ToString() }); }
            return list;
        }

        private static int LastIndex(string path)
        {
            int close = path.LastIndexOf(']');
            if (close <= 0) { return -1; }
            int open = path.LastIndexOf('[', close);
            if (open < 0) { return -1; }
            int v;
            return int.TryParse(path.Substring(open + 1, close - open - 1), out v) ? v : -1;
        }

        internal static bool IsCustom(ColorE c) { return (int)c > (int)ColorE.Black; }

        private sealed class RefEq : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
            public int GetHashCode(object o) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o); }
        }
        private static readonly IEqualityComparer<object> ReferenceComparer = new RefEq();
    }

    [HarmonyPatch(typeof(SaveLoad.SaveLoadManager), "SaveCampaign")]
    internal static class SaveCampaignColorShim
    {
        private static void Prefix(SaveLoad.SaveLoadManager __instance, Combat.CampaignInformation campaignInformation)
        {
            try
            {
                var paths = CampaignColorCodec.AuditPaths(campaignInformation);
                if (paths.Count > 0)
                {
                    Debug.Log("[ModKit] 战役存档：含自定义颜色的字段 " + paths.Count + " 处 → " + string.Join(" | ", paths));
                }

                if (CampaignColorCodec.ReplaceCustomValues(campaignInformation))
                {
                    CampaignColorCodec.WriteSidecar(CampaignColorCodec.FolderOf(__instance));
                }
                else
                {
                    CampaignColorCodec.DeleteSidecar(CampaignColorCodec.FolderOf(__instance));
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModKit] 战役存档颜色替换失败：" + ex.Message);
            }
        }

        private static void Postfix()
        {
            try { CampaignColorCodec.RestoreInMemory(); }
            catch (Exception ex) { Debug.LogWarning("[ModKit] 战役存档颜色还原失败：" + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(Combat.CampaignManager), "DeserializeCampaignInfo")]
    internal static class LoadCampaignColorShim
    {
        private static void Prefix(Combat.CampaignInformation campaignInfo)
        {
            try
            {
                var mgr = SaveLoad.SaveLoadManager.Instance;
                if (mgr == null || campaignInfo == null) { return; }
                CampaignColorCodec.ApplySidecar(CampaignColorCodec.FolderOf(mgr), campaignInfo);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModKit] 战役读档颜色还原失败：" + ex.Message);
            }
        }
    }
}
