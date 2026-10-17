// =============================================================================
//  ModKit.Core · 名字映射权威表
//
//  JSON 里的字符串名 ↔ Api 枚举值，**只有这一张表**。
//  校验器与运行时都从这里取值——避免"校验通过但运行时不认识"这类漂移。
//
//  为什么要有这张表：游戏的四条内容挂钩点都是"命名即契约"
//  （写错不抛异常、只是静默不生效）。平台把这层映射显式化，
//  并在加载期用 mapped=None 判定为错误，从源头消除静默失败。
// =============================================================================

using System;
using System.Collections.Generic;
using ModKit.Api;

namespace ModKit.Core
{
    /// <summary>映射结果。<see cref="Mapped"/> 为 false 表示这个名字平台不认识。</summary>
    public struct LookupResult<T>
    {
        public LookupResult(bool mapped, T value, string canonicalName, string hint)
        {
            Mapped = mapped;
            Value = value;
            CanonicalName = canonicalName;
            Hint = hint;
        }

        public bool Mapped { get; private set; }
        public T Value { get; private set; }

        /// <summary>规范化写法（用于日志与文档提示）。</summary>
        public string CanonicalName { get; private set; }

        /// <summary>未命中时的近似候选提示，帮作者排查拼写。</summary>
        public string Hint { get; private set; }
    }

    /// <summary>
    /// 内置效果语义的权威表：snake_case 名 ↔ <see cref="BuiltinEffect"/>。
    /// </summary>
    /// <remarks>
    /// 表里的每一项都能在 `docs/ModKit-P0-内容契约与API规格.md` §4.6/§6.4 找到对应证据。
    /// 实证档位存在 <see cref="BuiltinEffectCatalog"/>，不在这里重复。
    /// </remarks>
    public static class BuiltinEffectNames
    {
        private static readonly Dictionary<string, BuiltinEffect> ByName =
            new Dictionary<string, BuiltinEffect>(StringComparer.OrdinalIgnoreCase)
        {
            { "deal_damage",        BuiltinEffect.DealDamage },
            { "heal",               BuiltinEffect.Heal },
            { "block",              BuiltinEffect.Block },
            { "lose_life",          BuiltinEffect.LoseLife },
            { "draw",               BuiltinEffect.Draw },
            { "discard_random",     BuiltinEffect.DiscardRandom },
            { "gain_mana",          BuiltinEffect.GainMana },
            { "set_max_mana",       BuiltinEffect.SetMaxMana },
            { "clean_mana",         BuiltinEffect.CleanMana },
            { "apply_state",        BuiltinEffect.ApplyState },
            { "gain_ability",       BuiltinEffect.GainAbility },
            { "add_card",           BuiltinEffect.AddCard },
            { "add_temporary_card", BuiltinEffect.AddTemporaryCard },
            { "upgrade_card",       BuiltinEffect.UpgradeCard },
            { "end_turn",           BuiltinEffect.EndTurn },
            { "add_random_card",       BuiltinEffect.AddRandomCard },
            { "apply_prim_ability",    BuiltinEffect.ApplyPrimAbility },
            { "apply_util_ability",    BuiltinEffect.ApplyUtilAbility },
            { "change_max_health",     BuiltinEffect.ChangeMaxHealth },
            { "discard_hand",          BuiltinEffect.DiscardHand },
            { "discard_one_random",    BuiltinEffect.DiscardOneRandomCard },
            { "number_plus_sub",       BuiltinEffect.NumberPlusSub },
            { "recover",               BuiltinEffect.Recover },
            { "set_number",            BuiltinEffect.SetNumber },
            { "seek_card",             BuiltinEffect.SeekCard },
            { "improvise_card",        BuiltinEffect.ImproviseCard }
        };

        /// <summary>全部合法名（供文档生成与错误提示用）。</summary>
        public static IReadOnlyList<string> AllNames
        {
            get
            {
                var list = new List<string>(ByName.Keys);
                list.Sort(StringComparer.Ordinal);
                return list;
            }
        }

        public static LookupResult<BuiltinEffect> Resolve(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return new LookupResult<BuiltinEffect>(false, BuiltinEffect.None, null, null);
            }

            BuiltinEffect value;
            if (ByName.TryGetValue(name, out value))
            {
                return new LookupResult<BuiltinEffect>(true, value, name, null);
            }

            string hint = Closest(name, AllNames);
            return new LookupResult<BuiltinEffect>(false, BuiltinEffect.None, null,
                hint == null ? null : "是否想写 " + hint + "？");
        }

        /// <summary>
        /// 极简的近似匹配：只做"包含关系"与"编辑距离 ≤2"，够用于拼写提示。
        /// </summary>
        internal static string Closest(string input, IReadOnlyList<string> candidates)
        {
            if (string.IsNullOrEmpty(input)) { return null; }

            string lower = input.ToLowerInvariant();
            string best = null;
            int bestScore = int.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                string c = candidates[i];
                string cl = c.ToLowerInvariant();

                if (cl.Contains(lower) || lower.Contains(cl))
                {
                    return c;
                }

                int d = Levenshtein(lower, cl);
                if (d < bestScore)
                {
                    bestScore = d;
                    best = c;
                }
            }

            return bestScore <= 2 ? best : null;
        }

        internal static int Levenshtein(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) { return b == null ? 0 : b.Length; }
            if (string.IsNullOrEmpty(b)) { return a.Length; }

            int[] prev = new int[b.Length + 1];
            int[] cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) { prev[j] = j; }

            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    int del = prev[j] + 1;
                    int ins = cur[j - 1] + 1;
                    int sub = prev[j - 1] + cost;
                    cur[j] = Math.Min(Math.Min(del, ins), sub);
                }
                int[] swap = prev; prev = cur; cur = swap;
            }

            return prev[b.Length];
        }
    }

    /// <summary>稀有度名映射。</summary>
    public static class RarityNames
    {
        private static readonly Dictionary<string, Rarity> ByName =
            new Dictionary<string, Rarity>(StringComparer.OrdinalIgnoreCase)
        {
            { "Common",   Rarity.Common },
            { "Uncommon", Rarity.Uncommon },
            { "Rare",     Rarity.Rare }
        };

        public static IReadOnlyList<string> AllNames
        {
            get { return new List<string>(ByName.Keys); }
        }

        public static LookupResult<Rarity> Resolve(string name)
        {
            Rarity v;
            if (!string.IsNullOrEmpty(name) && ByName.TryGetValue(name, out v))
            {
                return new LookupResult<Rarity>(true, v, name, null);
            }

            string hint = BuiltinEffectNames.Closest(name, AllNames);
            return new LookupResult<Rarity>(false, Rarity.Common, null,
                hint == null ? null : "是否想写 " + hint + "？");
        }
    }

    /// <summary>结算时机名映射。</summary>
    public static class ResolveTimingNames
    {
        private static readonly Dictionary<string, ResolveTiming> ByName =
            new Dictionary<string, ResolveTiming>(StringComparer.OrdinalIgnoreCase)
        {
            { "OnCast",    ResolveTiming.OnCast },
            { "OnDraw",    ResolveTiming.OnDraw },
            { "OnDiscard", ResolveTiming.OnDiscard },
            { "EndInHand", ResolveTiming.EndInHand }
        };

        public static IReadOnlyList<string> AllNames
        {
            get { return new List<string>(ByName.Keys); }
        }

        public static LookupResult<ResolveTiming> Resolve(string name)
        {
            ResolveTiming v;
            if (!string.IsNullOrEmpty(name) && ByName.TryGetValue(name, out v))
            {
                return new LookupResult<ResolveTiming>(true, v, name, null);
            }

            string hint = BuiltinEffectNames.Closest(name, AllNames);
            return new LookupResult<ResolveTiming>(false, ResolveTiming.OnCast, null,
                hint == null ? null : "是否想写 " + hint + "？");
        }
    }

    /// <summary>颜色名映射。</summary>
    public static class ManaColorNames
    {
        private static readonly Dictionary<string, ManaColor> ByName =
            new Dictionary<string, ManaColor>(StringComparer.OrdinalIgnoreCase)
        {
            { "Red",    ManaColor.Red },
            { "Green",  ManaColor.Green },
            { "Blue",   ManaColor.Blue },
            { "Purple", ManaColor.Purple },
            { "Void",   ManaColor.Void },
            { "Black",  ManaColor.Black }
        };

        public static IReadOnlyList<string> AllNames
        {
            get { return new List<string>(ByName.Keys); }
        }

        public static LookupResult<ManaColor> Resolve(string name)
        {
            ManaColor v;
            if (!string.IsNullOrEmpty(name) && ByName.TryGetValue(name, out v))
            {
                return new LookupResult<ManaColor>(true, v, name, null);
            }

            string hint = BuiltinEffectNames.Closest(name, AllNames);
            return new LookupResult<ManaColor>(false, ManaColor.Void, null,
                hint == null ? null : "是否想写 " + hint + "？");
        }
    }

    /// <summary>
    /// 目标选择器名映射。
    /// </summary>
    /// <remarks>
    /// <b>只有 5 个合法值</b>。游戏还有一个 <c>AllAllies</c>/<c>OtherAllies</c>，
    /// 但探针实测它们会落到目标解析 switch 之外、**静默不执行**，
    /// 因此平台把它们列为"不支持"并给出明确解释——而不是放行后让作者困惑于"为什么没效果"。
    /// </remarks>
    public static class CombatantSelectorNames
    {
        private static readonly Dictionary<string, CombatantSelector> ByName =
            new Dictionary<string, CombatantSelector>(StringComparer.OrdinalIgnoreCase)
        {
            { "Self",          CombatantSelector.Self },
            { "Opponent",      CombatantSelector.Opponent },
            { "AllOpponents",  CombatantSelector.AllOpponents },
            { "All",           CombatantSelector.All },
            { "RandomOpponent", CombatantSelector.RandomOpponent }
        };

        /// <summary>游戏里有、但平台明确不放行的取值（写出来会得到解释性错误）。</summary>
        private static readonly Dictionary<string, string> KnownUnsupported =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "AllAllies",  "游戏的目标解析路径只处理 5 个取值，AllAllies 会静默不执行" },
            { "OtherAllies", "游戏的目标解析路径只处理 5 个取值，OtherAllies 会静默不执行" }
        };

        public static IReadOnlyList<string> AllNames
        {
            get { return new List<string>(ByName.Keys); }
        }

        public static LookupResult<CombatantSelector> Resolve(string name)
        {
            CombatantSelector v;
            if (!string.IsNullOrEmpty(name) && ByName.TryGetValue(name, out v))
            {
                return new LookupResult<CombatantSelector>(true, v, name, null);
            }

            string why;
            if (!string.IsNullOrEmpty(name) && KnownUnsupported.TryGetValue(name, out why))
            {
                return new LookupResult<CombatantSelector>(false, CombatantSelector.Self, null, why);
            }

            string hint = BuiltinEffectNames.Closest(name, AllNames);
            return new LookupResult<CombatantSelector>(false, CombatantSelector.Self, null,
                hint == null ? null : "是否想写 " + hint + "？");
        }
    }

    /// <summary>
    /// 伤害模式名映射。
    /// </summary>
    /// <remarks>
    /// 平台**不提供默认值**：探针实测 <c>Direct</c> 走命中判定与伤害调整器、
    /// <c>Indirect</c> 跳过。给默认值等于让作者在不知情下改变玩法强度。
    /// </remarks>
    public static class DamageModeNames
    {
        private static readonly Dictionary<string, DamageMode> ByName =
            new Dictionary<string, DamageMode>(StringComparer.OrdinalIgnoreCase)
        {
            { "Direct",   DamageMode.Direct },
            { "Indirect", DamageMode.Indirect },
            { "LoseLife", DamageMode.LoseLife }
        };

        public static IReadOnlyList<string> AllNames
        {
            get { return new List<string>(ByName.Keys); }
        }

        public static LookupResult<DamageMode> Resolve(string name)
        {
            DamageMode v;
            if (!string.IsNullOrEmpty(name) && ByName.TryGetValue(name, out v))
            {
                return new LookupResult<DamageMode>(true, v, name, null);
            }

            string hint = BuiltinEffectNames.Closest(name, AllNames);
            return new LookupResult<DamageMode>(false, DamageMode.Direct, null,
                hint == null ? null : "是否想写 " + hint + "？");
        }
    }

    /// <summary>卡牌去向名映射。</summary>
    public static class CardDestinationNames
    {
        private static readonly Dictionary<string, CardDestination> ByName =
            new Dictionary<string, CardDestination>(StringComparer.OrdinalIgnoreCase)
        {
            { "Hand",    CardDestination.Hand },
            { "Library", CardDestination.Library },
            { "Grave",   CardDestination.Grave }
        };

        public static IReadOnlyList<string> AllNames
        {
            get { return new List<string>(ByName.Keys); }
        }

        public static LookupResult<CardDestination> Resolve(string name)
        {
            CardDestination v;
            if (!string.IsNullOrEmpty(name) && ByName.TryGetValue(name, out v))
            {
                return new LookupResult<CardDestination>(true, v, name, null);
            }

            string hint = BuiltinEffectNames.Closest(name, AllNames);
            return new LookupResult<CardDestination>(false, CardDestination.Hand, null,
                hint == null ? null : "是否想写 " + hint + "？");
        }
    }

    /// <summary>能力类型（游戏 <c>AbilityType</c>）。</summary>
    /// <remarks>
    /// 取值来源于游戏程序集，按结构核对；未逐项运行验证。
    /// <summary>能力类型（游戏 <c>AbilityType</c>）。</summary>
    /// <remarks>
    /// 但"写这个值能否进正确的桶"只做了逻辑核对。
    /// </remarks>
    public static class AbilityTypeNames
    {
        private static readonly string[] All = { "Buff", "DeBuff", "Util", "Other", "PrimAbility" };

        public static IReadOnlyList<string> AllNames { get { return new List<string>(All); } }

        /// <summary>解析能力类型名（大小写不敏感）。返回规范化后的拼法。</summary>
        public static LookupResult<string> Resolve(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                for (int i = 0; i < All.Length; i++)
                {
                    if (string.Equals(All[i], name, StringComparison.OrdinalIgnoreCase))
                    {
                        return new LookupResult<string>(true, All[i], name, null);
                    }
                }
            }

            string hint = BuiltinEffectNames.Closest(name, AllNames);
            return new LookupResult<string>(false, null, null, hint);
        }
    }

    /// <summary>能力归属（游戏 <c>AbilityOwnerShip</c>）：决定进谁的池。</summary>
    public static class AbilityOwnerNames
    {
        private static readonly string[] All = { "Player", "Enemy", "All" };

        public static IReadOnlyList<string> AllNames { get { return new List<string>(All); } }

        public static LookupResult<string> Resolve(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                for (int i = 0; i < All.Length; i++)
                {
                    if (string.Equals(All[i], name, StringComparison.OrdinalIgnoreCase))
                    {
                        return new LookupResult<string>(true, All[i], name, null);
                    }
                }
            }

            string hint = BuiltinEffectNames.Closest(name, AllNames);
            return new LookupResult<string>(false, null, null, hint);
        }
    }
}
