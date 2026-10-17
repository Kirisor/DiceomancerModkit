// =============================================================================
//  ModKit.Game · 自定义能力效果：「获得格挡时额外 +X」
//
//  ★ 这是平台第一条真正走通「数据化 + C# 兜底」的效果。
//
//  ★ 为什么必须自己写 C#（探针结论，2026-10-11）：
//    游戏的 28 个内置效果里**没有**"格挡加值"这一类；33 个触发类型里**也没有**"获得格挡时"。
//    游戏只为**伤害**提供了调整接口
//        public interface IAdjustDamage { int AdjustDamage(int); ... }
//    （伤害被动 AbilityPassiveAttacker/DefenderDamageAddSub 实现它），
//    **没有任何对等的格挡接口** → 只能在 Character.ApplyBlock 上挂钩。
//
//  ★ 类名与命名空间是**机械拼出来的**（EffectUtils.GenerateEffectFromEffectInfo）：
//        abilityInfo.effectInfo.effectName = "SpecialEffect"
//        abilityInfo.effectInfo.specialEffectString = "<X>"
//        isAbilityEffect: true
//      → Type.GetType("Effects.Special.AbilityEffect.EffectSpAbility" + "<X>")
//    所以本类必须叫 EffectSpAbilityBlockGainAddSub，且位于 Effects.Special.AbilityEffect。
//    平台侧只要在能力 JSON 里写 { "custom": "AbilityBlockGainAddSub" } 即可（BuildEffectInfo
//    的 custom 分支会产出 SpecialEffect + specialEffectString）。
//
//  ★ 为什么 Type.GetType 能找得到我们（在插件程序集里）：
//    Type.GetType 原本只搜「调用方程序集 + 核心库」，但平台装桥时把它换成了
//    ModKitBridge.Resolve —— 后者搜索**全部已加载程序集**。
//
//  ★ X 从哪来：`AbilityUtil.cs:65`
//        abilityBase.rNumbersForEffect = new List<RNumber> { abilityBase.GetLayerRNumber() };
//    → 能力效果的 rNumbers[0] **就是能力层数**。所以"护盾提升 N"里的 N 自然等于层数，
//      多次施加会叠加层数，加值随之增长 —— 正是作者期望的语义。
//
//  ★ 生命周期（用户提供的关键机制）：原版游戏里增益能力的来源只有卡牌与遗物，
//    且**战斗结束直接清除**。所以这里**不需要装卸补丁**：补丁常驻，每次格挡时
//    遍历角色当前持有的能力求和即可 —— 能力一清，加值自然归零，不会无限叠加。
// =============================================================================

using System;
using System.Collections.Generic;
using Ability;
using Combat;
using Effects;
using HarmonyLib;
using Trigger;
using UnityEngine;

namespace Effects.Special.AbilityEffect
{
    /// <summary>
    /// 「获得格挡时额外 +X」——X 等于本能力的层数。
    /// </summary>
    // 注意：**不需要 [ModEffect] 属性** —— ContentRegistry.IndexByConvention 会按
    // 「namespace + EffectSpAbility 前缀」自动注册，id = 类名去掉 "EffectSpAbility" 前缀
    // = "BlockGainAddSub"。JSON 里就写这个 id（**不要**把 Ability 再加一遍，那是错的前缀）。
    public class EffectSpAbilityBlockGainAddSub : EffectBase
    {
        /// <summary>本类提供的加值是否已用在该角色上（避免补丁自重入）。</summary>
        [ThreadStatic] private static bool _inBonus;

        private AbilityBase _abilityBase;

        /// <summary>登记过的"提供格挡加值"的能力**实例**。</summary>
        /// <remarks>
        /// ★ 为什么记实例而不是能力名（2026-10-11 实机证伪后改正）：
        ///   反编译 `AbilityUtil.GenerateAbility` 的赋值顺序是
        ///       L70: abilityBase.abilityEffect = EffectUtils.GenerateEffectFromEffectInfo(...)  // ← 本类 EffectInitialize 在此被调
        ///       L71: abilityBase.abilityName  = abilityInfo.abilityName;                          // ← 名字**之后**才设
        ///   即 EffectInitialize 执行时 `_abilityBase.abilityName` **还是空的** ——
        ///   按名字登记会**静默失败**（实机症状：打出卡拿到"1层护盾提升"，但格挡没有加值）。
        ///   改记**实例引用**，绕开这个顺序依赖。
        /// </remarks>
        private static readonly HashSet<AbilityBase> BonusAbilities =
            new HashSet<AbilityBase>();

        private static bool _patched;

        public override void EffectInitialize()
        {
            try
            {
                _abilityBase = EffectOwner as AbilityBase;
                if (_abilityBase != null)
                {
                    // 只记实例 —— 此时 abilityName 尚未赋值（见上面注释），不能按名字登记
                    BonusAbilities.Add(_abilityBase);
                    Debug.Log("[ModKit] 格挡加值能力已就绪（实例已登记，X = 层数）");
                }

                EnsurePatched();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModKit] EffectSpAbilityBlockGainAddSub.EffectInitialize 失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 说明：**不重写 `TriggeredApplyEffect`**。
        /// </summary>
        /// <remarks>
        /// 本效果是"常驻加值"——加值发生在 Character.ApplyBlock 里，与触发时机无关。
        /// 不重写它还有一个实际好处：`TriggeredApplyEffect` 的返回类型是 `UniTask`，
        /// 重写就得给 ModKit.Game 引入 UniTask 程序集依赖；本效果不需要，就不引入。
        /// <para>
        /// 2026-10-13 补注：该依赖**已经被引入**了 —— 平台自建效果
        /// <see cref="Effects.Special.CardEffect.EffectSpEffectCleanMana"/> 必须重写
        /// <c>ApplyEffectForTarget</c>（同样是 UniTask 返回类型），
        /// 故 build-game.ps1 现在带 <c>-IncludeUniTask</c>。
        /// 本类仍然保持"不重写"的写法（它本来也不需要），只是上面这条"为了省依赖"的
        /// 理由不再成立 —— 保留原文以免后人误以为可以据此删掉 UniTask 引用。
        /// </para>
        /// </remarks>
        private static void EnsurePatched()
        {
            if (_patched) { return; }
            try
            {
                var target = AccessTools.Method(typeof(Character), "ApplyBlock", new[] { typeof(int) });
                if (target == null)
                {
                    Debug.LogWarning("[ModKit] 找不到 Character.ApplyBlock(int)，格挡加值无法生效。");
                    return;
                }

                var harmony = new Harmony("modkit.blockgainaddsub");
                harmony.Patch(target, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(EffectSpAbilityBlockGainAddSub), nameof(Postfix))));

                _patched = true;
                Debug.Log("[ModKit] 格挡加值补丁已挂钩：Character.ApplyBlock(int)。");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModKit] 格挡加值补丁挂钩失败：" + ex.GetType().Name + " - " + ex.Message);
            }
        }

        /// <summary>
        /// 后置补丁：额外补一次格挡。
        /// </summary>
        /// <remarks>
        /// ★ 为什么用后置 + 重入保护，而不是改参数的前置补丁：
        ///   前置要改原参数就得按**参数名**用 ref 绑定，名字对不上就静默失效；
        ///   后置只需 __instance，再用一个 [ThreadStatic] 标志防自重入即可 —— 更稳。
        /// ★ 为什么不用缓存：角色身上的能力列表会被战斗结束清空，
        ///   每次都现算才能保证"能力没了加值就没了"，不会留下幽灵加值。
        /// </remarks>
        private static void Postfix(Character __instance)
        {
            if (_inBonus) { return; }          // 防自重入：我们自己的 ApplyBlock 会再触发本补丁
            if (__instance == null) { return; }
            if (BonusAbilities.Count == 0) { return; }

            int extra = ComputeBonus(__instance);
            if (extra <= 0) { return; }

            _inBonus = true;
            try { __instance.ApplyBlock(extra); }
            catch (Exception ex)
            {
                Debug.LogWarning("[ModKit] 格挡加值追加失败：" + ex.Message);
            }
            finally { _inBonus = false; }
        }

        /// <summary>把该角色身上所有"提供格挡加值"的能力层数加起来。</summary>
        private static int ComputeBonus(Character character)
        {
            List<AbilityBase> list = character.abilities;
            if (list == null || list.Count == 0) { return 0; }

            int total = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var ab = list[i];
                if (ab == null) { continue; }
                // ★ 按**实例**判定（不能按 abilityName —— 见 BonusAbilities 的注释：
                //   EffectInitialize 执行时 abilityName 尚未赋值，按名字登记会静默失败）
                if (!BonusAbilities.Contains(ab)) { continue; }

                try
                {
                    var rn = ab.GetLayerRNumber();
                    if (rn != null) { total += rn.GetCollapsedNumber(); }
                }
                catch
                {
                    // 单个能力取层数失败不影响其它能力
                }
            }
            return total;
        }
    }
}
