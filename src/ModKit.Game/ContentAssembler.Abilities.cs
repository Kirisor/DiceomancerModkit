// =============================================================================
//  ModKit.Game · 能力（Ability）装配
//
//  ★ 为什么单独一个 partial 文件：
//    用脚本往 ContentAssembler.cs 里插代码会把 C# 结构切坏
//    （插到属性 getter 与 `}` 之间 → CS1513/CS1519）。
//    新功能另开文件，既避免插行，也让 diff 可读。
//
//  ★ 装配路径（与卡片同构，探针已核实）：
//    ① 克隆一个原版 AbilitySo 作模板（取 abilitySprite / abilityAnimationInfo / resolveAudioClip）
//    ② 写 AbilityInfo（abilityName / abilityType / isUnique / effectInfo / abilityColorEs）
//    ③ 注册进 _allAbilitySos —— **唯一查询入口** GetAbilitySo
//    ④ isInRandomPool 为真时按 type × owner 写对分类桶
//    ⑤ 读回断言
//
//  ★ 为什么"必须注册进 _allAbilitySos"是硬要求（反编译 `AbilityUtil.GenerateAbility` 原文）：
//      AbilitySo abilitySo = DataManager.Instance.GetAbilityDataManager().GetAbilitySo(abilityInfo.abilityName);
//      if (abilitySo == null) { Debug.Log("Ability So is null" + abilityInfo.abilityName); }   // ← 只打日志
//      RNumberInfo[] array = abilitySo.abilityInfo.effectInfo.effectNumberInfos.ToArray();     // ← 紧接着 NRE
//    也就是说那个 null 检查是**假的**：取不到就直接崩。平台必须保证能力在字典里。
// =============================================================================

using System;
using System.Collections.Generic;
using Ability;
using Card;
using Effects;
using GameData;
using ModKit.Core;
using UnityEngine;

namespace ModKit.Game
{
    public sealed partial class ContentAssembler
    {
        /// <summary>
        /// 装配一个能力并注册进 <c>AbilityDataManager</c>。
        /// </summary>
        /// <remarks>
        /// 但"能力真的出现在游戏里、效果真的结算"还没跑过。
        /// </remarks>
        public AbilitySo AssembleAbility(ModKit.Api.AbilityDef def, AssemblyResult result)
        {
            if (def == null) { throw new ArgumentNullException("def"); }
            if (result == null) { result = new AssemblyResult(); }

            try
            {
                var dm = DataManager.Instance;
                if (dm == null)
                {
                    result.Errors.Add(def.Id + "：DataManager.Instance 为 null（数据尚未加载）。");
                    return null;
                }

                var adm = dm.GetAbilityDataManager();
                if (adm == null)
                {
                    result.Errors.Add(def.Id + "：GetAbilityDataManager() 为 null。");
                    return null;
                }

                // --- 1. 取模板（借原版能力的图标与动画）---
                var all = adm.GetAllAbilitySos();
                if (all == null || all.Length == 0)
                {
                    result.Errors.Add(def.Id + "：游戏里没有任何能力可作模板。");
                    return null;
                }

                AbilitySo template = null;
                if (!string.IsNullOrEmpty(def.ArtTemplate))
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] != null && all[i].abilityInfo != null &&
                            all[i].abilityInfo.abilityName == def.ArtTemplate)
                        {
                            template = all[i];
                            break;
                        }
                    }
                    if (template == null)
                    {
                        result.Warnings.Add(def.Id + "：artTemplate " + def.ArtTemplate +
                            " 不存在，改用第一个可用原版能力作模板。");
                    }
                }
                if (template == null) { template = all[0]; }

                // --- 2. 克隆并改数据 ---
                var so = UnityEngine.Object.Instantiate(template);
                so.name = "ModKit_" + def.Id;
                if (so.abilityInfo == null)
                {
                    result.Errors.Add(def.Id + "：模板能力的 abilityInfo 为 null，无法装配。");
                    return null;
                }

                var typeResult = AbilityTypeNames.Resolve(def.Type);
                if (!typeResult.Mapped)
                {
                    result.Errors.Add(def.Id + "：未知的能力类型 " + def.Type + "（校验器应已拦截）。");
                    return null;
                }
                AbilityType gameType = ToGameAbilityType(typeResult.Value);

                var ownerResult = AbilityOwnerNames.Resolve(def.Owner);
                if (!ownerResult.Mapped)
                {
                    result.Errors.Add(def.Id + "：未知的归属 " + def.Owner + "（校验器应已拦截）。");
                    return null;
                }
                AbilityOwnerShip gameOwner = ToGameAbilityOwner(ownerResult.Value);

                // ★ 先保留模板的 effectInfo（下面要用它的 resolveDelay / triggerTargetInfo 作基底），
                //   再逐项覆盖。不要整体替换 —— 那样会丢掉模板里游戏需要的既有字段。
                var modInfo = so.abilityInfo;
                modInfo.abilityName = def.Id;
                modInfo.abilityType = gameType;
                modInfo.isUnique = def.IsUnique;

                // 效果：能力的行为来源。与卡片共用同一个构建器
                // （effectInfo 走 EffectUtils.GenerateEffectFromEffectInfo，那个工厂已被桥接管）。
                if (def.Effects != null && def.Effects.Count > 0)
                {
                    var first = BuildEffectInfo(def.Effects[0], modInfo.effectInfo.cardEffectResolveType, def.Id, result);
                    if (result.HasErrors) { return null; }
                    modInfo.effectInfo = first;
                }

                // ★ 把作者声明的触发时机真正写进去（2026-10-11 补上）
                //   不写 = 继承模板的 triggerTargetInfo ——
                //   实机症状：作者写"获得 3 点格挡"，实际按模板 Nani 的 BeenAttacked 触发
                //   （"被敌人攻击时获得护盾"），描述与效果不符。
                if (!string.IsNullOrEmpty(def.Trigger))
                {
                    var tl = ModKit.Core.TriggerTypeNames.Resolve(def.Trigger);
                    if (!tl.Mapped)
                    {
                        result.Errors.Add(def.Id + "：未知触发类型 " + def.Trigger + "（校验器应已拦截）。");
                        return null;
                    }
                    // ★ 只覆盖 triggerType，**必须保留模板 triggerTargetInfo 里的其它字段**。
                    //   这与上面 L117 那条原则是同一条 —— 整体替换会让游戏在
                    //   Trigger.TriggerManager.CheckTriggerFitTargetTrigger 里解引用到 null，
                    //   抛 NullReferenceException（2026-10-12 实机踩中：
                    //   能力用 WholeNewTurnStart 时，一进入新回合就崩）。
                    //   原版那些"打出卡牌时触发"的能力（如活力加费用、加速抽卡）
                    //   之所以正常，正是因为它们的 triggerTargetInfo 带着完整字段。
                    // ★ 只覆盖 triggerType，**必须保留模板里的其它字段**。
                    //
                    //   TriggerTargetInfo 是 struct（Trigger/TriggerTargetInfo.cs L15），
                    //   所以不用判空 —— 直接改字段，模板的其他字段原样留着。
                    //
                    //   整体替换掉整个 struct 会让下面这些字段变成默认(null)：
                    //     worldPhaseInfo / battlePhase / cardCoreInfoCheck /
                    //     relativeCharacterType / certainTurnStartInfo / ownerCharacter …
                    //   游戏在 TriggerManager.CheckTriggerFitTargetTrigger 里解引用它们就会
                    //   抛 NullReferenceException（2026-10-12 实机踩中：
                    //   能力用 WholeNewTurnStart，一进入新回合就崩）。
                    //
                    //   原版那些"打出卡牌时触发"的能力（活力加费用、加速抽卡）
                    //   之所以正常，正是因为它们带着完整字段 —— 借模板就是借这些。
                    modInfo.triggerTargetInfo.triggerType = tl.Value;
                    _log.Info("已写入能力触发时机：" + def.Id + "（" + tl.Value + "，保留模板的其它触发字段）");
                }

                so.isInRandomPool = def.AddToPool;
                so.abilityOwnerShip = gameOwner;

                // --- 3. 注册进唯一查询入口 ---
                RegisterAbilitySo(adm, so, def.Id, result);

                // --- 4. 按 type × owner 分桶（只在 addToPool 时）---
                if (def.AddToPool)
                {
                    AddToAbilityBuckets(adm, so, def.Id, gameType, gameOwner, result);
                }

                result.RegisteredAbilities.Add(def.Id);
                // 记下来，供"游戏重建数据容器后重注册"使用（见 RebuildGuard）
                RememberAbility(so);
                _log.Info("已装配能力：" + def.Id + "（模板 " +
                    (template.abilityInfo != null ? template.abilityInfo.abilityName : "?") +
                    "，type=" + typeResult.Value + "，owner=" + ownerResult.Value + "）");
                return so;
            }
            catch (Exception ex)
            {
                result.Errors.Add(def.Id + "：装配异常 —— " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private void RegisterAbilitySo(AbilityDataManager adm, AbilitySo so, string abilityName, AssemblyResult result)
        {
            try
            {
                // _allAbilitySos 是 GetAbilitySo 的唯一入口。
                // 不写它 = 游戏取不到 → AbilityUtil.GenerateAbility 里那个假 null 检查之后直接 NRE。
                var dict = Reflect.Get<Dictionary<string, AbilitySo>>(adm, "_allAbilitySos");
                if (dict == null)
                {
                    result.Errors.Add(abilityName + "：_allAbilitySos 取不到，能力未注册（游戏取到时**会崩**）。");
                    return;
                }
                dict[abilityName] = so;

                // ★ 同时登记**短名**（去掉 mod 前缀）。
                //   卡片的 gain_ability / apply_prim_ability / apply_util_ability 里，
                //   作者写的是能力 id（如 "milkfrog_choice"），而平台注册时带 mod 前缀
                //   （"manual_demo.milkfrog_choice"）—— 游戏按作者写的名字查不到 ⇒ abilitySo 为 null
                //   ⇒ 实例化卡牌时 NRE（实机症状：图鉴里这张卡渲染失败、只剩一张缩略图）。
                //   短名只在**没有同名原版能力**时登记，避免覆盖原版。
                if (!string.IsNullOrEmpty(abilityName))
                {
                    int dot = abilityName.LastIndexOf('.');
                    if (dot >= 0 && dot + 1 < abilityName.Length)
                    {
                        string leaf = abilityName.Substring(dot + 1);
                        if (!dict.ContainsKey(leaf)) { dict[leaf] = so; }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(abilityName + "：注册能力失败 —— " + ex.Message);
            }
        }

        /// <summary>
        /// 按 <c>type</c> × <c>owner</c> 把能力写进对应的分类桶。
        /// </summary>
        /// <remarks>
        /// 对照反编译 <c>AbilityDataManager.LoadAllAbility</c> 的分桶逻辑：
        /// <code>
        /// if (!abilitySo.isInRandomPool) continue;
        /// switch (abilitySo.abilityInfo.abilityType)
        /// {
        /// case Buff:   owner==Player||All → _allPlayerBuff;  owner==Enemy||All → _allEnemyBuff;  break;
        /// case DeBuff: owner==Player||All → _allPlayerDeBuff; owner==Enemy||All → _allEnemyDeBuff; break;
        /// }
        /// </code>
        /// <b>注意</b>：<c>Util</c> / <c>Other</c> / <c>PrimAbility</c> **不在这个 switch 里** ——
        /// 它们只进 <c>_allAbilitySos</c>，不进任何分类桶（游戏本来就这样，不是平台遗漏）。
        /// </remarks>
        private void AddToAbilityBuckets(AbilityDataManager adm, AbilitySo so, string abilityName,
            AbilityType type, AbilityOwnerShip owner, AssemblyResult result)
        {
            bool player = owner == AbilityOwnerShip.Player || owner == AbilityOwnerShip.All;
            bool enemy = owner == AbilityOwnerShip.Enemy || owner == AbilityOwnerShip.All;

            switch (type)
            {
                case AbilityType.Buff:
                    if (player) { PutAbility(adm, "_allPlayerBuff", so, abilityName, result); }
                    if (enemy) { PutAbility(adm, "_allEnemyBuff", so, abilityName, result); }
                    break;

                case AbilityType.DeBuff:
                    if (player) { PutAbility(adm, "_allPlayerDeBuff", so, abilityName, result); }
                    if (enemy) { PutAbility(adm, "_allEnemyDeBuff", so, abilityName, result); }
                    break;

                default:
                    // Util / Other / PrimAbility：游戏的分桶 switch 不处理它们 → 只进全局字典
                    _log.Info("能力 " + abilityName + " 的类型 " + type +
                        " 不在游戏的分桶逻辑里，只注册进 _allAbilitySos（与游戏行为一致）。");
                    break;
            }
        }

        private void PutAbility(AbilityDataManager adm, string fieldName, AbilitySo so,
            string abilityName, AssemblyResult result)
        {
            try
            {
                var dict = Reflect.Get<Dictionary<string, AbilitySo>>(adm, fieldName);
                if (dict == null)
                {
                    result.Warnings.Add(abilityName + "：" + fieldName + " 取不到，未进该池。");
                    return;
                }
                dict[abilityName] = so;
            }
            catch (Exception ex)
            {
                result.Warnings.Add(abilityName + "：写入 " + fieldName + " 失败 —— " + ex.Message);
            }
        }

        /// <summary>
        /// 能力的读回断言：确认"游戏真的能取到、且在预期的池里"。
        /// </summary>
        /// <remarks>
        /// 与卡/遗物同一目的：拦住静默失败。
        /// 能力这一侧尤其重要 —— 取不到时游戏**会崩**（见文件头引的原文），
        /// 所以"注册成功"这件事必须被证明，而不能只靠"我写进去了"。
        /// </remarks>
        internal void VerifyAbilitiesReadBack(string modId, List<AbilitySo> abilities, AssemblyResult result)
        {
            if (abilities == null || abilities.Count == 0) { return; }

            try
            {
                var dm = DataManager.Instance;
                if (dm == null)
                {
                    result.Errors.Add(modId + "：能力读回断言无法执行（DataManager.Instance 为 null）。");
                    return;
                }

                var adm = dm.GetAbilityDataManager();
                if (adm == null)
                {
                    result.Errors.Add(modId + "：能力读回断言无法执行（AbilityDataManager 为 null）。");
                    return;
                }

                int missing = 0;
                foreach (var so in abilities)
                {
                    if (so == null || so.abilityInfo == null) { continue; }

                    var found = adm.GetAbilitySo(so.abilityInfo.abilityName);
                    if (found == null)
                    {
                        missing++;
                        result.Errors.Add(so.abilityInfo.abilityName +
                            "：能力读回失败 —— GetAbilitySo 取不到。游戏取不到时**会崩**（AbilityUtil 的 null 检查是假的）。");
                    }
                }

                string line = string.Format("{0}(能力)：按名可取 {1}/{2}",
                    modId, abilities.Count - missing, abilities.Count);
                result.ReadBackSummary = (result.ReadBackSummary == "(未执行)" || string.IsNullOrEmpty(result.ReadBackSummary))
                    ? line
                    : result.ReadBackSummary + "；" + line;

                if (missing == 0) { _log.Info("能力读回断言通过：" + line); }
                else { _log.Error("能力读回断言失败：" + line); }
            }
            catch (Exception ex)
            {
                result.Errors.Add(modId + "：能力读回断言本身抛异常 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // ---------------------------------------------------------------------
        // 枚举映射（显式 switch，不用强制转换 —— 与卡/遗物同一纪律）
        // ---------------------------------------------------------------------

        private static AbilityType ToGameAbilityType(string name)
        {
            switch (name)
            {
                case "Buff": return AbilityType.Buff;
                case "DeBuff": return AbilityType.DeBuff;
                case "Util": return AbilityType.Util;
                case "Other": return AbilityType.Other;
                case "PrimAbility": return AbilityType.PrimAbility;
                default: return AbilityType.Other;
            }
        }

        private static AbilityOwnerShip ToGameAbilityOwner(string name)
        {
            switch (name)
            {
                case "Player": return AbilityOwnerShip.Player;
                case "Enemy": return AbilityOwnerShip.Enemy;
                case "All": return AbilityOwnerShip.All;
                default: return AbilityOwnerShip.Player;
            }
        }
    }
}
