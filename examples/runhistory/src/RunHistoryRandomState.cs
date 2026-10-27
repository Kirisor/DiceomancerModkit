// =============================================================================
//  Run History（战绩）· 把游戏里那个「没播种的随机源」按种子按住
//
//  ★ 两次踩坑记录（别再退回这两条路）：
//    v0.2.0：`FieldInfo.SetValue` 给 `ShufflingExtension.rng`（`static readonly`）换新实例。
//    v0.2.1：换实例后**回读校验**，日志写「校验=一致」，以为成功 —— 地图照样每次不同。
//    结论：换字段对游戏无效。`ShufflingExtension.Random<T>` 与 `rng` 同在一个程序集里，
//    Mono 会把 `static readonly` 的引用内联进调用点，游戏代码用的一直是**最初那个对象**；
//    而反射读字段拿到的是新对象，于是校验「一致」却完全没按住真正的随机流。
//
//  ★ 正确做法：**不换实例，只改对象**。
//    把 `new System.Random(seed)` 的内部状态（SeedArray / inext / inextp …）原样拷进
//    那个一直在被使用的实例上。内联的老引用和字段读出的引用是同一个对象，两条路一起成立。
//    字段名在 Mono 与 .NET Framework 上不同，所以全部走反射枚举，不写死名字。
//
//  ★ 每次回读校验（比的是**被播种的那个对象**，不是字段里的新对象）。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;

namespace RunHistory
{
    internal static class RunHistoryRandomState
    {
        private static bool _resolved;
        private static FieldInfo _rngField;
        private static FieldInfo[] _fields;

        /// <summary>最近一次重置的报告（给地图转储用）。</summary>
        internal static string LastReport { get; private set; }

        /// <summary>最近一次重置是否通过校验。</summary>
        internal static bool LastOk { get; private set; }

        private static void Resolve()
        {
            if (_resolved) { return; }
            _resolved = true;
            try
            {
                _rngField = typeof(Utilities.ShufflingExtension).GetField(
                    "rng", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (_rngField == null)
                {
                    RunHistoryPlugin.Log.LogWarning(
                        "战绩：找不到 Utilities.ShufflingExtension.rng —— 段落组合无法按种子固定。");
                    return;
                }
                _fields = InstanceFields(_rngField.FieldType);
            }
            catch (Exception ex)
            {
                RunHistoryPlugin.Log.LogWarning("战绩：解析洗牌随机源失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// 把游戏的全局洗牌随机源重置到 `new Random(seed)` 的状态。
        /// 返回是否校验通过。
        /// </summary>
        internal static bool Reseed(int seed, string reason)
        {
            Resolve();
            LastReport = null;
            LastOk = false;

            if (_rngField == null)
            {
                LastReport = "找不到 ShufflingExtension.rng";
                return false;
            }

            System.Random rng = null;
            try { rng = _rngField.GetValue(null) as System.Random; }
            catch { }
            if (rng == null)
            {
                LastReport = "ShufflingExtension.rng 尚未初始化";
                LastOk = false;
                return false;
            }

            // ★ 只**就地复制状态**，绝不换实例。
            //   v0.2.0 / v0.2.1 都是靠 `SetValue` 换实例 —— 字段读回来确实指向新实例、
            //   「校验=一致」，但地图照样每次都不同。说明游戏代码里真正被调用的还是**原来那个对象**：
            //   Mono 会把同程序集内 `static readonly` 的引用内联进调用点，换字段对它无效。
            //   改对象内容则两条路都覆盖 —— 内联的老引用与字段读出来的引用是同一个对象。
            bool copied;
            try { copied = CopyState(rng, seed); }
            catch (Exception ex)
            {
                RunHistoryPlugin.Log.LogWarning("战绩：复制随机源状态失败 —— " + ex.GetType().Name + ": " + ex.Message);
                copied = false;
            }

            // 若字段被旧版本换成了别的实例，指回刚播种的这个对象（保持单一真源）
            try
            {
                object current = _rngField.GetValue(null);
                if (!ReferenceEquals(current, rng)) { _rngField.SetValue(null, rng); }
            }
            catch { }

            // Unity 的全局随机源也对齐（森林/城市的 RandomByWeight 走它）
            ReseedUnity(seed);

            // 回读校验：现在这个对象是 new Random(seed) 的状态吗？
            bool ok = false;
            try { ok = StateEquals(rng, new System.Random(seed)); } catch { }
            LastOk = ok;
            LastReport = "就地重置=" + (copied ? "成功" : "失败") + "｜校验=" + (ok ? "一致" : "不一致");

            string line = "战绩：地图随机源按 seed " + seed + " 重置（" + reason + "）｜" + LastReport;
            if (ok) { RunHistoryPlugin.Log.LogInfo(line); }
            else { RunHistoryPlugin.Log.LogWarning(line + " —— 本局地图可能仍不按种子生成。"); }
            return ok;
        }

        /// <summary>
        /// 只对齐 Unity 的全局随机源（`UnityEngine.Random`）。
        /// 森林/城市的 `PoolElementWeight.RandomByWeight()` 走它；它是引擎级全局状态，
        /// 会被粒子/飘字一类的表现层顺手消耗，所以在每个段落生成前都重新对齐一次，
        /// 免得"排在前面的段落播了几次特效"这种不相干的事影响后面的节点类型。
        /// </summary>
        internal static void ReseedUnity(int seed)
        {
            try { UnityEngine.Random.InitState(seed); } catch { }
        }

        // ---------------- 内部：反射状态搬运 ----------------
        private static FieldInfo[] InstanceFields(Type type)
        {
            var list = new List<FieldInfo>();
            for (Type cur = type; cur != null && cur != typeof(object); cur = cur.BaseType)
            {
                foreach (FieldInfo f in cur.GetFields(BindingFlags.Instance | BindingFlags.NonPublic |
                                                       BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    if (f.IsStatic) { continue; }
                    list.Add(f);
                }
            }
            return list.ToArray();
        }

        private static bool CopyState(System.Random dest, int seed)
        {
            if (_fields == null || _fields.Length == 0) { return false; }
            var src = new System.Random(seed);
            foreach (FieldInfo f in _fields)
            {
                object value = f.GetValue(src);
                object current = f.GetValue(dest);
                Array srcArray = value as Array;
                if (srcArray != null)
                {
                    Array dstArray = current as Array;
                    if (dstArray != null && dstArray.Length == srcArray.Length)
                    {
                        Array.Copy(srcArray, dstArray, srcArray.Length);   // 就地覆盖：不碰 readonly 字段
                    }
                    else
                    {
                        f.SetValue(dest, srcArray.Clone());
                    }
                }
                else if (!Equals(current, value))
                {
                    f.SetValue(dest, value);
                }
            }
            return StateEquals(dest, src);
        }

        private static bool StateEquals(System.Random a, System.Random b)
        {
            if (_fields == null || _fields.Length == 0 || a == null || b == null) { return false; }
            foreach (FieldInfo f in _fields)
            {
                object va = f.GetValue(a);
                object vb = f.GetValue(b);
                Array aa = va as Array;
                Array ab = vb as Array;
                if (aa != null || ab != null)
                {
                    if (aa == null || ab == null || aa.Length != ab.Length) { return false; }
                    for (int i = 0; i < aa.Length; i++)
                    {
                        if (!Equals(aa.GetValue(i), ab.GetValue(i))) { return false; }
                    }
                }
                else if (!Equals(va, vb)) { return false; }
            }
            return true;
        }
    }
}
