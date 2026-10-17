// =============================================================================
//  ModKit.Game · 平台自建的两个「施加能力」特殊效果
//  （内置语义 apply_prim_ability / apply_util_ability 的落地）
//
//  ★ 为什么平台要自己写这两个（2026-10-13 用户实机崩溃后）：
//    游戏的 AbilityUtil.GenerateAbility 第 30-37 行做数字替换：
//        RNumberInfo[] array = abilitySo.abilityInfo.effectInfo.effectNumberInfos.ToArray();
//        if (rNumbers != null)
//            for (int i = 0; i < rNumbers.Length; i++)
//                array[i] = rNumbers[i].GetRNumberInfo();   // ← array 比 rNumbers 短就下标越界
//    也就是说：**卡牌效果带的数字个数，绝不能超过目标能力自己的参数槽数**。
//    而原版的 EffectApplyPrimAbility / EffectApplyUtilAbility 会把卡上的 rNumbers
//    原样传进去，**一个长度检查都没有**。
//
//    实机症状（用户截图，2026-10-13）：
//        System.IndexOutOfRangeException
//          at Ability.AbilityUtil.GenerateAbility(...)
//          at Combat.Character.GainAbility(...)
//          at Combat.CharacterPlayer.GainAbility(...)
//          at Command.SubCommandApplyAbility.ResolveSubCommand()
//      触发的那张卡：{ "builtin": "apply_util_ability", "name": "DoubleDamageThisTurn", "amounts": [1] }
//      —— DoubleDamageThisTurn 是"本回合伤害翻倍"的固定效果、**0 个参数槽**，
//         卡上却带了 1 个数字 ⇒ array[0] 越界。
//
//  ★ 为什么在**运行期**夹紧，而不是装配期：
//    装配顺序是 卡 → 遗物 → 关键词 → **能力**（见 PlatformRuntime.AssembleAll），
//    卡片装配时**本 mod 的能力还没注册**，取不到它的槽数（原版能力能取到，mod 能力不能）。
//    运行期 `abilitySo` 已经按名解析完毕 ⇒ 槽数一定拿得到，对原版与 mod 能力一视同仁。
//
//  ★ 语义（与游戏一致，只去掉越界那一种情况）：
//    · 卡带 M 个数字、能力有 N 个槽 ⇒ 只替换前 min(M, N) 个（N = 0 时一个都不传）；
//    · M < N 时其余槽保留能力自己的默认值（游戏本来就允许，行为不变）；
//    · 能力名解析不到（拼错/没注册）⇒ 记 Error 并跳过，**不再让游戏抛 NRE**；
//    · 每次"数字过多被裁"只报一次日志（按能力名去重），避免刷屏。
// =============================================================================

using System;
using System.Collections.Generic;
using Combat;
using Command;
using Cysharp.Threading.Tasks;
using RollNumber;

namespace Effects.Special.CardEffect
{
    /// <summary>施加能力时的数字槽夹紧（原版两个 `EffectApply*Ability` 都缺这一步）。</summary>
    internal static class AbilityApplyNumbers
    {
        /// <summary>已经就"数字过多/能力缺失"报过警的能力名，避免每次施加都刷日志。</summary>
        private static readonly HashSet<string> Warned = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>目标能力自己的参数槽数；解析不到时返回 0。</summary>
        internal static int SlotCount(EffectBase effect)
        {
            try
            {
                var so = effect != null ? effect.abilitySo : null;
                var info = so != null ? so.abilityInfo : null;
                var ei = info != null ? info.effectInfo : null;
                var list = ei != null ? ei.effectNumberInfos : null;
                return list != null ? list.Count : 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>能力名（仅用于日志；解析不到时返回 null）。</summary>
        internal static string AbilityName(EffectBase effect)
        {
            try
            {
                var so = effect != null ? effect.abilitySo : null;
                var info = so != null ? so.abilityInfo : null;
                return info != null ? info.abilityName : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>能力没解析出来时的处理：记 Error 并跳过（原版会在这里 NRE）。</summary>
        internal static bool EnsureResolved(EffectBase effect)
        {
            if (effect != null && effect.abilitySo != null && effect.abilitySo.abilityInfo != null)
            {
                return true;
            }
            var name = AbilityName(effect);
            UnityEngine.Debug.LogError("[ModKit] 施加能力失败：找不到能力 '" + (name ?? "(空)") +
                "'（AbilityDataManager.GetAbilitySo 返回 null）—— 该效果已跳过，未执行。");
            return false;
        }

        /// <summary>
        /// 把卡上的 rNumbers 裁到目标能力能接受的个数。
        /// 返回 null / 空数组都表示"不替换任何槽"（游戏侧 `if (rNumbers != null)` 之后循环零次）。
        /// </summary>
        internal static RNumber[] Clamp(EffectBase effect)
        {
            var mine = effect != null ? effect.rNumbers : null;
            if (mine == null || mine.Length == 0) { return null; }

            int slots = SlotCount(effect);
            if (mine.Length <= slots) { return mine; }

            var name = AbilityName(effect);

            if (slots == 0)
            {
                WarnOnce(name, "该能力没有参数槽，卡上写的 " + mine.Length +
                    " 个数字已忽略（原样传进去会让游戏在下标越界处崩溃）。");
                return null;
            }

            var take = new RNumber[slots];
            Array.Copy(mine, take, slots);
            WarnOnce(name, "该能力只有 " + slots + " 个参数槽，卡上写了 " + mine.Length +
                " 个 —— 只取前 " + slots + " 个。");
            return take;
        }

        private static void WarnOnce(string abilityName, string message)
        {
            var key = abilityName ?? "(空)";
            if (!Warned.Add(key)) { return; }
            UnityEngine.Debug.LogWarning("[ModKit] 施加能力 · " + key + "：" + message);
        }

        /// <summary>卡面文案键 —— 与原版 `EffectApplyPrimAbility` / `EffectApplyUtilAbility` 完全一致。</summary>
        internal static string DescribeKey(EffectTargetEnum target)
        {
            switch (target)
            {
                case EffectTargetEnum.Self: return "Key_ApplyPrimAbilitySelf";
                case EffectTargetEnum.TargetOpponent: return "Key_ApplyPrimAbilityTargetOpponent";
                case EffectTargetEnum.AllOpponents: return "Key_ApplyPrimAbilityOpponents";
                case EffectTargetEnum.All: return "Key_ApplyPrimAbilityAll";
                case EffectTargetEnum.RandomOpponent: return "Key_ApplyPrimAbilityRandomOpponent";
                default: return "";
            }
        }
    }

    /// <summary>平台自带「基础能力」效果（内置语义 <c>apply_prim_ability</c>）。</summary>
    /// <remarks>与原版 `EffectApplyPrimAbility` 等价，只多一步数字槽夹紧与能力缺失时的日志。</remarks>
    public sealed class EffectSpEffectApplyPrimAbility : EffectBase
    {
        protected override UniTask ApplyEffectForTarget(Character targetCharacter)
        {
            if (!AbilityApplyNumbers.EnsureResolved(this)) { return UniTask.CompletedTask; }

            targetCharacter.GainAbility(abilitySo.abilityInfo, 1, AbilityApplyNumbers.Clamp(this));
            return UniTask.CompletedTask;
        }

        public override string GetEffectDesKey()
        {
            return AbilityApplyNumbers.DescribeKey(effectTargetEnum);
        }
    }

    /// <summary>平台自带「功能能力」效果（内置语义 <c>apply_util_ability</c>）。</summary>
    /// <remarks>
    /// 与原版 `EffectApplyUtilAbility` 等价（含 `GetCauseBusy() => true`），
    /// 只多一步数字槽夹紧与能力缺失时的日志。
    /// </remarks>
    public sealed class EffectSpEffectApplyUtilAbility : EffectBase
    {
        protected override UniTask ApplyEffectForTarget(Character targetCharacter)
        {
            if (!AbilityApplyNumbers.EnsureResolved(this)) { return UniTask.CompletedTask; }

            new SubCommandApplyAbility(targetCharacter, abilitySo.abilityInfo, 1,
                isInsert: true, addToQueue: true, AbilityApplyNumbers.Clamp(this));
            return UniTask.CompletedTask;
        }

        public override string GetEffectDesKey()
        {
            return AbilityApplyNumbers.DescribeKey(effectTargetEnum);
        }

        public override bool GetCauseBusy()
        {
            // 与原版 EffectApplyUtilAbility 一致（它内部入队子命令，需要占住命令队列）。
            return true;
        }
    }
}
