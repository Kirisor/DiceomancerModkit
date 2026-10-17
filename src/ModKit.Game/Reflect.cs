// =============================================================================
//  ModKit.Game · 反射助手
//
//  为什么需要反射：游戏的字段名是**资产的一部分**（第 1 步不变量 I2/I5），
//  平台不许改它们，只能按名读。而其中几个恰好是 private。
//
//  ★ 设计纪律：所有反射都必须**集中在这里**，并且失败时给出可诊断的信息
//    （类型名 + 字段名 + 一句"可能是游戏更新导致契约漂移"）。
//    散落各处的反射会让"游戏更新后平台静默失效"变成必然。
//
//  实测确认的私有成员（探针报告 §A-3/§B-1）：
//    · CardSo.cardInfo                 —— private SerializeField
//    · CardDataManager._allInGameCardSoDictionary
//    · RelicDataManager._allInGameRelicSo / _allStandPoolRelicSo
// =============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;

namespace ModKit.Game
{
    /// <summary>集中式反射访问器。所有失败都抛 <see cref="ModKitReflectionException"/>。</summary>
    public static class Reflect
    {
        private const BindingFlags InstancePrivate =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly Dictionary<string, FieldInfo> FieldCache =
            new Dictionary<string, FieldInfo>(StringComparer.Ordinal);

        /// <summary>取实例字段（含 private）。找不到即抛 —— 不返回 null 让调用方去猜。</summary>
        public static FieldInfo Field(Type type, string name)
        {
            string key = type.FullName + "." + name;

            FieldInfo cached;
            if (FieldCache.TryGetValue(key, out cached))
            {
                if (cached == null)
                {
                    throw new ModKitReflectionException(
                        "字段 " + key + " 不存在（已缓存失败结果）。可能是游戏更新导致契约漂移。");
                }
                return cached;
            }

            var f = type.GetField(name, InstancePrivate);
            FieldCache[key] = f;

            if (f == null)
            {
                throw new ModKitReflectionException(
                    "字段 " + key + " 不存在。可能是游戏更新导致契约漂移 —— " +
                    "请检查「不要碰的标识符」清单（反射类型名 / 序列化字段 / 资源路径 / 组件类名）。");
            }

            return f;
        }

        /// <summary>读实例字段值。</summary>
        public static object Get(object instance, string fieldName)
        {
            if (instance == null) { throw new ModKitReflectionException("读字段 " + fieldName + " 时实例为 null。"); }
            var f = Field(instance.GetType(), fieldName);
            return f.GetValue(instance);
        }

        /// <summary>读实例字段值并转成目标类型；类型不符即抛。</summary>
        public static T Get<T>(object instance, string fieldName) where T : class
        {
            object raw = Get(instance, fieldName);
            var typed = raw as T;
            if (typed == null && raw != null)
            {
                throw new ModKitReflectionException(
                    "字段 " + instance.GetType().FullName + "." + fieldName +
                    " 实际类型是 " + raw.GetType().FullName + "，期望 " + typeof(T).FullName + "。");
            }
            return typed;
        }

        /// <summary>写实例字段值（用于把装配好的 CardInfo 塞回 CardSo 的私有字段）。</summary>
        public static void Set(object instance, string fieldName, object value)
        {
            if (instance == null) { throw new ModKitReflectionException("写字段 " + fieldName + " 时实例为 null。"); }
            var f = Field(instance.GetType(), fieldName);
            f.SetValue(instance, value);
        }

        /// <summary>
        /// 尝试读一个 public static 属性（用于 <c>Instance</c> 这类单例口）。
        /// </summary>
        /// <remarks>
        /// 用属性而不是字段，是因为游戏里单例多为
        /// <c>public static X Instance =&gt; ...</c> 这种带逻辑的形式。
        /// </remarks>
        public static object StaticProperty(Type type, string name)
        {
            var p = type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null)
            {
                throw new ModKitReflectionException(
                    "静态属性 " + type.FullName + "." + name + " 不存在（可能已改名或改结构）。");
            }
            return p.GetValue(null, null);
        }
    }

    /// <summary>
    /// 反射失败专用异常。
    /// </summary>
    /// <remarks>
    /// 单独一个类型是为了让上层能精确区分"游戏契约漂移"与"mod 内容写错"——
    /// 前者要提示用户更新平台，后者要提示作者改 JSON。
    /// </remarks>
    public sealed class ModKitReflectionException : Exception
    {
        public ModKitReflectionException(string message) : base(message) { }
        public ModKitReflectionException(string message, Exception inner) : base(message, inner) { }
    }
}
