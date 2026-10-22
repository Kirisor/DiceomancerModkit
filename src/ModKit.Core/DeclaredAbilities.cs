// =============================================================================
//  ModKit.Core · 已声明能力名的解析表
//
//  为什么要它（2026-10-22 实机 NRE）：
//    卡片的 gain_ability / apply_prim_ability / apply_util_ability 里，作者写的是
//    **能力 id**（"milkfrog_choice"），而平台给 mod 能力注册的全名是
//    "<modId>.<id>"（"manual_demo.milkfrog_choice"）。
//    装配器把 abilityName 原样写进 EffectInfo ⇒ 游戏
//    `AbilityDataManager.GetAbilitySo("milkfrog_choice")` 拿不到 ⇒ `abilitySo` 为 null
//    ⇒ EffectApplyAbility.GetEffectDesKey() 直接 NullReferenceException
//    （症状：图鉴里卡片只剩缩略图、开局/奖励界面点卡就弹错）。
//
//  做法：ModLoader 读到能力的**那一刻**就把 `leaf → 全名` 记下来，
//  装配器写 abilityName 时用 Resolve 换成全名。原版能力不在表里 ⇒ 原样返回。
// =============================================================================

using System;
using System.Collections.Generic;

namespace ModKit.Core
{
    /// <summary>mod 声明过的能力名（短名 → 带 mod 前缀的全名）。</summary>
    public static class DeclaredAbilities
    {
        private static readonly Dictionary<string, string> ByLeaf =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>登记一个 mod 声明的能力。<paramref name="id"/> 可以是短名或已带前缀的全名。</summary>
        public static void Register(string modId, string id)
        {
            if (string.IsNullOrEmpty(id)) { return; }

            string full = id;
            if (full.IndexOf('.') < 0 && !string.IsNullOrEmpty(modId))
            {
                full = modId + "." + id;
            }

            string leaf = full;
            int dot = full.LastIndexOf('.');
            if (dot >= 0 && dot + 1 < full.Length) { leaf = full.Substring(dot + 1); }

            ByLeaf[leaf] = full;
            ByLeaf[full] = full;
        }

        /// <summary>把作者写的能力名换成游戏里注册的全名；不是 mod 能力就原样返回。</summary>
        public static string Resolve(string name)
        {
            if (string.IsNullOrEmpty(name)) { return name; }
            string full;
            return ByLeaf.TryGetValue(name, out full) ? full : name;
        }

        /// <summary>清空（每次重新加载 mod 时调用，避免旧条目残留）。</summary>
        public static void Clear() { ByLeaf.Clear(); }

        /// <summary>当前登记条数（诊断用）。</summary>
        public static int Count { get { return ByLeaf.Count; } }
    }
}
