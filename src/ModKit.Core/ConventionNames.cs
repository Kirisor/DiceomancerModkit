using System;
using System.Text;

namespace ModKit.Core
{
    /// <summary>
    /// 游戏的「按名字拼类」约定 —— 把作者写的**原始内容 id** 归一到平台约定类名。
    /// </summary>
    /// <remarks>
    /// 游戏侧有几处是拿字符串拼类型名去问 <c>Type.GetType</c> 的：
    /// <list type="bullet">
    /// <item><c>Keyword/CardKeyWordUtil.cs:80</c>：<c>"Keyword.CardKeyword" + keywordName</c>；</item>
    /// <item>遗物：<c>"Relic.RelicEffects.Relic" + name</c>；</item>
    /// <item>作者自写效果 / 能力：<c>"Effects.Special.CardEffect.EffectSpEffect" + name</c> 等。</item>
    /// </list>
    /// 这些串里的 <c>&lt;名&gt;</c> 是**作者的原始 id**（下划线小写，甚至可能带 mod 前缀，
    /// 如 <c>manual_demo.milk_frog</c>），而平台约定类的类名是 PascalCase
    /// （<c>CardKeywordMilkFrog</c>）。两边必须在这里对齐 —— 否则游戏侧
    /// <c>Type.GetType</c> 落空后**静默退化成基类**：不报错、不崩，但关键词一点行为都没有。
    /// <para>
    /// 2026-10-15 实机定位：「奶蛙」（<c>manual_demo.milk_frog</c>）的类
    /// <c>CardKeywordMilkFrog</c> **从未被实例化** —— 静态构造没跑、Harmony 补丁没装、
    /// 卡面没有数字、连锁钩子不触发。根因就是这里缺了归一化
    /// （装配期的核验用的是 PascalCase 类名，所以流水线当时报的是"类存在"，掩盖了它）。
    /// </para>
    /// </remarks>
    public static class ConventionNames
    {
        /// <summary>下划线小写名 → PascalCase（<c>milk_frog</c> → <c>MilkFrog</c>）。</summary>
        public static string ToPascal(string id)
        {
            if (string.IsNullOrEmpty(id)) { return id; }

            var parts = id.Split('_');
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.Length == 0) { continue; }
                sb.Append(char.ToUpperInvariant(p[0]));
                if (p.Length > 1) { sb.Append(p.Substring(1)); }
            }
            return sb.ToString();
        }

        /// <summary>取最后一个点之后的一段（容忍 <c>manual_demo.milk_frog</c> 这类带 mod 前缀的写法）。</summary>
        public static string Leaf(string name)
        {
            if (string.IsNullOrEmpty(name)) { return name; }
            int dot = name.LastIndexOf('.');
            return dot >= 0 && dot < name.Length - 1 ? name.Substring(dot + 1) : name;
        }

        /// <summary>
        /// 游戏侧的「拼类」前缀。长前缀在前：<c>Keyword.CardKeyword</c> 必须先于
        /// <c>CardKeyword</c> 匹配，否则会多出一截当 id。
        /// </summary>
        private static readonly string[] Prefixes =
        {
            "Keyword.CardKeyword",
            "Relic.RelicEffects.Relic",
            "Effects.Special.CardEffect.EffectSpEffect",
            "Effects.Special.AbilityEffect.EffectSpAbility",
            "CardKeyword",
            "Relic",
            "EffectSpEffect",
            "EffectSpAbility",
        };

        /// <summary>
        /// 试着把游戏的「拼类」查找串归一成平台约定类名；不属于任何一个约定族时返回 null。
        /// </summary>
        /// <example>
        /// <c>Keyword.CardKeywordmilk_frog</c> → <c>CardKeywordMilkFrog</c>；
        /// <c>Keyword.CardKeywordmanual_demo.milk_frog</c> → <c>CardKeywordMilkFrog</c>。
        /// </example>
        public static string TryConventionClassName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) { return null; }

            // 程序集限定名（"Foo.Bar, Assembly-CSharp"）不在这里归一：
            // 把 ", Assembly" 拆开去拼类名只会制造错误的候选。
            if (typeName.IndexOf(',') >= 0) { return null; }

            for (int i = 0; i < Prefixes.Length; i++)
            {
                string prefix = Prefixes[i];
                if (typeName.Length <= prefix.Length) { continue; }
                if (!typeName.StartsWith(prefix, StringComparison.Ordinal)) { continue; }

                string rest = typeName.Substring(prefix.Length);

                // 只用前缀里最后那一段作类名前缀：Keyword.CardKeyword → CardKeyword
                int dot = prefix.LastIndexOf('.');
                string classPrefix = dot >= 0 ? prefix.Substring(dot + 1) : prefix;

                string candidate = classPrefix + ToPascal(Leaf(rest));
                if (candidate.Length > classPrefix.Length) { return candidate; }
            }

            return null;
        }
    }
}
