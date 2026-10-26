// =============================================================================
//  Run History（战绩）· 把游戏里那个「没播种的随机源」按种子按住
//
//  ★ 为什么需要这个文件（v0.2.0 失败的教训）：
//    v0.2.0 用 `FieldInfo.SetValue` 直接给 `ShufflingExtension.rng`（`static readonly`）
//    换一个新实例 —— 这在 Unity 的 Mono 上**不保证生效**（readonly 静态字段可能被
//    运行时挡住，或者写进去也不影响已缓存的引用）。日志里看不到报错，但地图照样每次不同。
//
//  ★ 所以这里改走「状态复制」：不换实例，把 `new System.Random(seed)` 的**内部状态**
//    （_seedArray / _inext / _inextp …）原样拷贝进游戏正在用的那个实例上。
//    这条路不依赖 readonly 能不能写 —— 它改的是对象内容，不是字段。
//    字段名在 Mono 与 .NET Framework 上不同，所以全部走反射枚举，不写死名字。
//
//  ★ 每次都回读校验：把当前状态和 `new Random(seed)` 逐字段比对，结果写进日志。
//    「校验=一致」是硬证据；不一致就必须报告，而不是假装成功。
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

            // ① 先试换实例（能成最好：状态天然正确）
            var fresh = new System.Random(seed);
            string replaceState;
            try
            {
                _rngField.SetValue(null, fresh);
                bool took = ReferenceEquals(_rngField.GetValue(null), fresh);
                replaceState = took ? "成功" : "未生效";
                if (took) { rng = fresh; }
            }
            catch (Exception ex)
            {
                replaceState = "被拒(" + ex.GetType().Name + ")";
            }

            // ② 无论如何都做状态复制（换实例没成的话，这就是唯一的路）
            string copyState = "未尝试";
            if (!ReferenceEquals(rng, fresh))
            {
                try { copyState = CopyState(rng, seed) ? "成功" : "失败"; }
                catch (Exception ex) { copyState = "异常(" + ex.GetType().Name + ")"; }
            }

            // ③ Unity 的全局随机源也对齐（森林/城市的 RandomByWeight 走它）
            try { UnityEngine.Random.InitState(seed); } catch { }

            // ④ 回读校验：现在是 new Random(seed) 的状态吗？
            bool ok = false;
            try { ok = StateEquals(rng, new System.Random(seed)); } catch { }
            LastOk = ok;
            LastReport = "字段替换=" + replaceState + "｜状态复制=" + copyState + "｜校验=" + (ok ? "一致" : "不一致");

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
