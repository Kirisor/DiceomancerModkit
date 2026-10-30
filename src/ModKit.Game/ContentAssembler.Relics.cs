// =============================================================================
//  ModKit.Game · 遗物注册与读回（Relic）—— ContentAssembler 的 partial 分片
//
//  ★ 来历：本文件是 2026-10-13 第二批按子域拆分 ContentAssembler.cs 时切出的，
//    全部内容**逐字节照搬**原文（含 XML 注释原文），只新增本文件头与
//    using / namespace / class 外壳，未改任何逻辑。
//
//  ★ 负责子域：遗物进容器、进战役池、读回断言
//    VerifyRelicsReadBack / RegisterRelicSo / RegisterRelicCollection /
//    RefreshCampaignRelicPool。
//
//  ★ 不在本文件：公共入口 AssembleRelic 按拆分约定留在主文件 ContentAssembler.cs。
// =============================================================================

using System;
using System.Collections.Generic;
using Card;
using Effects;
using GameData;
using DiceUtilities;
using Combat;
using Keyword;
using Trigger;
using Relic;
using RollNumber;
using Loot;
using ModKit.Core;
using UnityEngine;
using HarmonyLib;

// 别名：ModKit.Api 的枚举与游戏同名（Rarity / ResolveTiming 等），
// 同时 using 两个命名空间会 CS0104 二义。用别名保持可读性又不冲突。
using ApiRarity = ModKit.Api.Rarity;
using ApiResolveTiming = ModKit.Api.ResolveTiming;
using ApiBuiltinEffect = ModKit.Api.BuiltinEffect;
using ApiCombatantSelector = ModKit.Api.CombatantSelector;
using ApiManaColor = ModKit.Api.ManaColor;
using ApiCardDef = ModKit.Api.CardDef;
using ApiRelicDef = ModKit.Api.RelicDef;
using ApiEffectSpec = ModKit.Api.EffectSpec;

namespace ModKit.Game
{
    public sealed partial class ContentAssembler
    {
        /// <summary>
        /// 遗物的读回断言：确认"游戏真的能取到、且在战役池里"。
        /// </summary>
        /// <remarks>
        /// 与卡的读回断言同一目的：拦住两种**静默**失败 ——
        /// ① 写进了错的容器（按名查不到）；② 池子被 <c>SetCampaignRelicPool</c> 重建冲掉（永远不出现在奖励里）。
        /// </remarks>
        internal void VerifyRelicsReadBack(string modId, List<RelicSo> relics, AssemblyResult result)
        {
            if (relics == null || relics.Count == 0) { return; }

            try
            {
                var dm = GameData.DataManager.Instance;
                if (dm == null)
                {
                    result.Errors.Add(modId + "：遗物读回断言无法执行（DataManager.Instance 为 null）。");
                    return;
                }

                var manager = dm.GetRelicDataManager();
                if (manager == null)
                {
                    result.Errors.Add(modId + "：遗物读回断言无法执行（RelicDataManager 为 null）。");
                    return;
                }

                // ① 按名查得到吗（走游戏自己的入口 GetRelicSo）
                int missing = 0;
                foreach (var so in relics)
                {
                    if (so == null || so.relicInfo == null) { continue; }
                    var found = manager.GetRelicSo(so.relicInfo.relicName);
                    if (found == null)
                    {
                        missing++;
                        result.Errors.Add(so.relicInfo.relicName +
                            "：遗物读回失败 —— GetRelicSo 取不到（注册进了错的容器？）。");
                    }
                }

                // ② 在战役池里吗（否则遗物存在但永远不出现在奖励里）
                int inPool = 0;
                try
                {
                    var pool = Reflect.Get<List<RelicSo>>(manager, "_campaignRelicPool");
                    if (pool != null)
                    {
                        foreach (var so in relics) { if (so != null && pool.Contains(so)) inPool++; }
                    }
                }
                catch { }

                string line = string.Format("{0}(遗物)：按名可取 {1}/{2}，在战役池 {3}/{2}",
                    modId, relics.Count - missing, relics.Count, inPool);
                result.ReadBackSummary = (result.ReadBackSummary == "(未执行)" || string.IsNullOrEmpty(result.ReadBackSummary))
                    ? line
                    : result.ReadBackSummary + "；" + line;

                if (missing == 0) { _log.Info("遗物读回断言通过：" + line); }
                else { _log.Error("遗物读回断言失败：" + line); }
            }
            catch (Exception ex)
            {
                result.Errors.Add(modId + "：遗物读回断言本身抛异常 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void RegisterRelicSo(RelicDataManager manager, string fieldName,
            RelicSo so, string relicName, AssemblyResult result)
        {
            try
            {
                var dict = Reflect.Get<Dictionary<string, RelicSo>>(manager, fieldName);
                if (dict == null)
                {
                    result.Errors.Add(relicName + "：" + fieldName + " 取不到，遗物未完全注册。");
                    return;
                }
                dict[relicName] = so;
            }
            catch (Exception ex)
            {
                result.Errors.Add(relicName + "：写入 " + fieldName + " 失败 —— " + ex.Message);
            }
        }

        /// <summary>
        /// 给新遗物建一个自建 collection 并追加进池。
        /// </summary>
        /// <remarks>
        /// 进战役池的 4 个必要条件（依据 <c>SetCampaignRelicPool</c> 原文）：
        /// ① collection 在 <c>_allRelicDataCollections</c> 里；② <c>requireUnlock=false</c>；
        /// ③ 类型 Standard，或 Colored 且 <c>colorEs ⊆ usedColorE</c>；④ <b>Utils 永远进不了</b> —— 故这里用 Standard。
        /// </remarks>
        /// <summary>
        /// 把本 mod 的遗物注册成一个 <c>RelicDataCollection</c>，并重建战役遗物池。
        /// </summary>
        /// <remarks>
        /// ★ 与卡池**完全同构的缺陷**（本轮核对确认）：
        /// <code>
        /// public void SetCampaignRelicPool(ColorE[] usedColorE)
        /// {
        ///     SetUnlockedRelicDataCollections();   // 从 _allRelicDataCollections 重建 _unlockRelicDataCollections
        ///     _campaignRelicPool.Clear();          // ← 整体清空
        ///     foreach (col in _unlockRelicDataCollections)   // ← 只从 *_unlock* 列表灌
        ///         switch (col.relicDataCollectionType) { case Standard: ... ; case Colored: ... }
        /// }
        /// </code>
        /// 所以"把遗物塞进 <c>_allRelicDataCollections</c>"**还不够** —— 必须再触发一次池子重建，
        /// 否则遗物按名查得到、却永远不出现在奖励里（静默）。
        /// <para>
        /// 参数为什么这样选（都能过游戏的过滤）：
        /// <list type="bullet">
        /// <item><c>requireUnlock = false</c> → 过 <c>!requireUnlock || unlocked</c>；</item>
        /// <item><c>relicDataCollectionType = Standard</c> → 过 switch（且 <b>Utils 永不进池</b>）；</item>
        /// <item><c>colorEs = 空数组</c> → Standard 分支本来不看它，留着便于改成 Colored 时也天然通过。</item>
        /// </list>
        /// </para>
        /// </remarks>
        internal void RegisterRelicCollection(string modId, List<RelicSo> relics, AssemblyResult result)
        {
            if (relics == null || relics.Count == 0) { return; }

            try
            {
                var dm = GameData.DataManager.Instance;
                if (dm == null)
                {
                    result.Warnings.Add(modId + "：DataManager.Instance 为 null，遗物未进集合（可稍后重试）。");
                    return;
                }

                var manager = dm.GetRelicDataManager();
                if (manager == null)
                {
                    result.Warnings.Add(modId + "：GetRelicDataManager() 为 null，遗物未进集合。");
                    return;
                }

                var list = Reflect.Get<List<RelicDataCollection>>(manager, "_allRelicDataCollections");
                if (list == null)
                {
                    result.Warnings.Add(modId + "：_allRelicDataCollections 取不到，遗物未进集合。");
                    return;
                }

                // ---- 2026-10-30：按「池归属」把遗物拆成两类集合 ----
                //   · 没写 poolColors 的 ⇒ 共用的 Standard 集合（原来的行为，所有职业都能刷）
                //   · 写了 poolColors 的 ⇒ 按色组分别进 Colored 集合
                //     游戏 `RelicDataManager.SetCampaignRelicPool` 只收 `colorEs ⊆ 本局三色` 的
                //     Colored 集合 ⇒ 写 ["Purple","Green","Black"] 就天然只有奶蛙能刷到。
                var shared = new List<RelicSo>();
                var groups = new List<RelicColorGroup>();
                for (int i = 0; i < relics.Count; i++)
                {
                    var r = relics[i];
                    if (r == null || r.relicInfo == null) { continue; }
                    ColorE[] colors;
                    if (RelicPoolColors.TryGetValue(r.relicInfo.relicName, out colors) && colors != null && colors.Length > 0)
                    {
                        RelicColorGroup group = null;
                        for (int g = 0; g < groups.Count; g++)
                        {
                            if (SameColors(groups[g].Colors, colors)) { group = groups[g]; break; }
                        }
                        if (group == null) { group = new RelicColorGroup(colors); groups.Add(group); }
                        group.Relics.Add(r);
                    }
                    else
                    {
                        shared.Add(r);
                    }
                }

                // ---- ① 共用集合（Standard）----
                if (shared.Count > 0)
                {
                    string collectionName = "ModKit_" + modId;
                    if (FindCollection(list, collectionName) == null)
                    {
                        list.Add(new RelicDataCollection
                        {
                            relicDataCollectionType = RelicDataCollectionType.Standard,
                            requireUnlock = false,
                            unlockUpgradeName = collectionName,
                            colorEs = new ColorE[0],
                            relicSos = shared.ToArray()
                        });
                        _log.Info("已注册遗物集合：" + collectionName + "（" + shared.Count + " 件，所有职业共用）");
                    }
                    else
                    {
                        _log.Info("遗物集合 " + collectionName + " 已存在，跳过重复注册。");
                    }
                }

                // ---- ② 专属集合（Colored，按色组分组）----
                for (int g = 0; g < groups.Count; g++)
                {
                    var group = groups[g];
                    string groupName = "ModKit_" + modId + "_c" + g;
                    if (FindCollection(list, groupName) != null) { continue; }
                    list.Add(new RelicDataCollection
                    {
                        relicDataCollectionType = RelicDataCollectionType.Colored,
                        requireUnlock = false,
                        unlockUpgradeName = groupName,
                        colorEs = group.Colors,
                        relicSos = group.Relics.ToArray()
                    });
                    _log.Info("已注册遗物集合：" + groupName + "（" + group.Relics.Count + " 件，专属色组 " +
                        string.Join("+", group.Colors) + "）");
                }

                RefreshCampaignRelicPool(manager, result, modId);
            }
            catch (Exception ex)
            {
                result.Warnings.Add(modId + "：注册遗物集合失败（遗物仍可按名查询）—— " + ex.Message);
            }
        }

        /// <summary>一个「同色组」的专属遗物集合（注册期的临时分组结构）。</summary>
        private sealed class RelicColorGroup
        {
            internal RelicColorGroup(ColorE[] colors)
            {
                Colors = colors;
                Relics = new List<RelicSo>();
            }

            internal ColorE[] Colors { get; private set; }

            internal List<RelicSo> Relics { get; private set; }
        }

        private static bool SameColors(ColorE[] a, ColorE[] b)
        {
            if (a == null || b == null || a.Length != b.Length) { return false; }
            for (int i = 0; i < a.Length; i++)
            {
                bool found = false;
                for (int j = 0; j < b.Length; j++) { if (a[i] == b[j]) { found = true; break; } }
                if (!found) { return false; }
            }
            return true;
        }

        private static RelicDataCollection FindCollection(List<RelicDataCollection> list, string name)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].unlockUpgradeName == name) { return list[i]; }
            }
            return null;
        }

        /// <summary>重建战役遗物池，让刚注册的集合生效。</summary>
        private void RefreshCampaignRelicPool(RelicDataManager manager, AssemblyResult result, string modId)
        {
            try
            {
                var setPool = AccessTools.Method(typeof(RelicDataManager), "SetCampaignRelicPool");
                if (setPool == null)
                {
                    result.Warnings.Add(modId + "：找不到 SetCampaignRelicPool（遗物已进集合，下一局会包含）。");
                    return;
                }

                // 用当前的"战役用色"作参数。取不到就给空数组：Standard 分支不看它，
                // 只有 Colored 分支才用（而平台建的集合是 Standard）。
                object colors = null;
                try
                {
                    var cm = CampaignManager.Instance;
                    if (cm != null) { colors = Reflect.Get(cm, "campaignUsedColorE"); }
                }
                catch { }
                if (colors == null) { colors = new ColorE[0]; }

                setPool.Invoke(manager, new[] { colors });
                _log.Info("已重建战役遗物池（SetCampaignRelicPool）。");
            }
            catch (Exception ex)
            {
                result.Warnings.Add(modId + "：重建战役遗物池失败（遗物已进集合，下一局会包含）—— " + ex.Message);
            }
        }

        // ---------------------------------------------------------------------
        //  遗物 · 触发点附加字段的装配（Tier 1，2026-10-14）
        //
        //  ★ 解决的问题：`AssembleRelic` 过去只写 `new TriggerTargetInfo { triggerType = ... }`，
        //    而游戏 `TriggerManager.CheckTriggerFitTargetTrigger`（反编译 :88-181）还会读
        //    `battlePhase` / `certainTurnStartInfo` / `relativeCharacterType` / `extraString` /
        //    `colorE`。其中 `battlePhase`、`certainTurnStartInfo` 是 **class 字段**，
        //    为 null 时游戏直接解引用 ⇒ NullReferenceException（这就是旧
        //    `fx_relic_new_turn.RISKY.json.disabled` 被标成"会崩"的机制）。
        //
        //  ★ 写入判据与校验器**完全同源**：`TriggerFieldNames.Consumes(trigger, field)`
        //    逐条抄自反编译的那个大 switch。触发点不读的字段**不写**（写了游戏也不看，
        //    校验器已给「字段被忽略」警告）。
        //
        //  ★ `ownerCharacter` **不写** —— `RelicEffect.RegisterTrigger()` 会自动补
        //    `CharacterPlayer.Instance`（反编译 Relic.RelicEffects/RelicEffect.cs:59-63），
        //    这里再写一次就会把"相对谁"的判定基准搞乱。
        // ---------------------------------------------------------------------

        /// <summary>
        /// 把遗物 JSON 上平铺的 5 个触发点附加字段按**游戏语义**写进
        /// <paramref name="info"/>。<paramref name="triggerType"/> 必须是归一化后的触发点名。
        /// </summary>
        /// <remarks>
        /// 只写"该触发点真的会读"的字段；取值非法的（校验器本该已拦下）记进
        /// <c>result.Errors</c> 并**不写入**该字段。字符串→游戏枚举的转换在这里做，
        /// 因为 ModKit.Core 的纪律是"不引用任何游戏类型"。
        /// <para>
        /// ★★ 2026-10-14 修正 —— **本方法必须收 <c>ref</c>**。<see cref="TriggerTargetInfo"/> 是 **struct**，
        /// 本方法要写它的 <c>battlePhase</c> / <c>certainTurnStartInfo</c> / <c>relativeCharacterType</c> /
        /// <c>extraString</c> / <c>colorE</c>。按值收参时这些写入**全部落在副本上**、调用方的结构体一个字节都没变，
        /// 运行期只剩下平台补的中性默认值（<c>(Main,isStart=false)</c> / <c>require:false</c>）—— 实机现象就是
        /// 「作者声明 <c>(Upkeep,isStart=true)</c>，实际在 <c>(Main,false)</c> 触发」，而 lint 与单测**看不见**，
        /// 因为它们只走 JSON→defs→校验器，从不读装配出来的游戏对象。
        /// ⇒ 除了这里的 <c>ref</c>，调用点还必须用 <see cref="VerifyRelicTriggerFields"/> **从最终对象回读**把关。
        /// </para>
        /// </remarks>
        internal void ApplyRelicTriggerFields(ref TriggerTargetInfo info, ApiRelicDef def,
            string triggerType, AssemblyResult result)
        {
            if (def == null) { return; }

            // ---- ① battlePhase（ChangeBattlePhase 专用）----
            // 游戏 :93-99：先判 relativeCharacterType，再比 battlePhase.battlePhase / .isStart。
            if (TriggerFieldNames.Consumes(triggerType, TriggerFieldNames.BattlePhase))
            {
                if (def.BattlePhase == null || string.IsNullOrEmpty(def.BattlePhase.Phase))
                {
                    // 不替作者猜：缺了就保持 null（= 老行为），但要在日志里点名，
                    // 否则实机上只会看到一串 NullReferenceException 而没有上下文。
                    _log.Warn("遗物 " + def.Id + " 的触发点 " + triggerType +
                        " 缺少 battlePhase —— 游戏侧触发时会 NullReferenceException（崩）。" +
                        "请在遗物 JSON 里补 \"battlePhase\": { \"phase\": \"Upkeep\", \"isStart\": true }。");
                }
                else
                {
                    var phase = TriggerFieldValues.ResolveBattlePhase(def.BattlePhase.Phase);
                    if (!phase.Mapped)
                    {
                        result.Errors.Add(def.Id + "：battlePhase.phase 取值 '" + def.BattlePhase.Phase +
                            "' 非法（校验器应已拦截）。");
                    }
                    else
                    {
                        // 唯一构造函数是 (BattlePhase, bool)，没有无参构造 —— 两个值必须一起给。
                        info.battlePhase = new Combat.BattleWorld.BattlePhaseRequirement(
                            ParseGameEnum<Combat.BattleWorld.BattlePhase>(phase.Value),
                            def.BattlePhase.IsStart);
                    }
                }
            }

            // ---- ② certainTurn（WholeNewTurnStart 专用）----
            // 游戏 :121-131：`if (!requireCertainTurn) return true;` 之后才按 relation 比回合数。
            if (TriggerFieldNames.Consumes(triggerType, TriggerFieldNames.CertainTurn))
            {
                if (def.CertainTurn == null)
                {
                    _log.Warn("遗物 " + def.Id + " 的触发点 " + triggerType +
                        " 缺少 certainTurn —— 游戏侧触发时会 NullReferenceException（崩）。" +
                        "请在遗物 JSON 里补 \"certainTurn\": { \"require\": false, \"turnCount\": 0, \"relation\": \"Before\" }。");
                }
                else
                {
                    var relation = TriggerFieldValues.ResolveCertainTurnRelation(def.CertainTurn.Relation);
                    // relation 只在 require=true 时才参与比较（游戏 :122-125 先短路），
                    // 所以 require=false 且没写 relation 属于正常写法，不算错 ——
                    // 与校验器的口径保持一致。
                    if (!relation.Mapped && def.CertainTurn.Require)
                    {
                        result.Errors.Add(def.Id + "：certainTurn.relation 取值 '" +
                            (def.CertainTurn.Relation ?? "(空)") + "' 非法（校验器应已拦截）。");
                    }
                    else
                    {
                        info.certainTurnStartInfo = new Combat.BattleWorld.CertainTurnStartInfo
                        {
                            requireCertainTurn = def.CertainTurn.Require,
                            turnCount = def.CertainTurn.TurnCount,
                            certainTurnRelationInfo = relation.Mapped
                                ? ParseGameEnum<Combat.BattleWorld.CertainTurnRelationInfo>(relation.Value)
                                : default(Combat.BattleWorld.CertainTurnRelationInfo)
                        };
                    }
                }
            }

            // ---- ③ relativeCharacter（ChangeBattlePhase / CharacterRollDice / EnemyHealthBarBreak / CharacterCheck）----
            if (TriggerFieldNames.Consumes(triggerType, TriggerFieldNames.RelativeCharacter))
            {
                if (!string.IsNullOrEmpty(def.RelativeCharacter))
                {
                    var rel = TriggerFieldValues.ResolveRelativeCharacter(def.RelativeCharacter);
                    if (!rel.Mapped)
                    {
                        result.Errors.Add(def.Id + "：relativeCharacter 取值 '" + def.RelativeCharacter +
                            "' 非法（校验器应已拦截）。");
                    }
                    else
                    {
                        info.relativeCharacterType =
                            ParseGameEnum<Combat.BattleWorld.RelativeCharacterType>(rel.Value);
                    }
                }
                // 没给 ⇒ 保持枚举默认值 Owner（游戏侧就是这个默认值，不额外写零值）。
            }

            // ---- ④ extraString（TakeRest / CardKeywordTrigger / AbilityStandardTrigger / CharacterCheck）----
            if (TriggerFieldNames.Consumes(triggerType, TriggerFieldNames.ExtraString) &&
                !string.IsNullOrEmpty(def.ExtraString))
            {
                info.extraString = def.ExtraString;
            }

            // ---- ⑤ colorE（ManaConsumed 专用）----
            if (TriggerFieldNames.Consumes(triggerType, TriggerFieldNames.ColorE) &&
                !string.IsNullOrEmpty(def.ColorE))
            {
                var color = TriggerFieldValues.ResolveManaColor(def.ColorE);
                if (!color.Mapped)
                {
                    result.Errors.Add(def.Id + "：colorE 取值 '" + def.ColorE + "' 非法（校验器应已拦截）。");
                }
                else
                {
                    info.colorE = ParseGameEnum<ColorE>(color.Value);
                }
            }

            // ---- ⑥ 作者给了、但这个触发点不读 ⇒ 只记一条日志，明确"没写进去" ----
            //     （校验器已经给过 Warning；这里再留一条运行期痕迹，便于实机排查。）
            var fields = TriggerFieldNames.AllNames;
            for (int i = 0; i < fields.Count; i++)
            {
                string field = fields[i];
                if (!HasApiField(def, field)) { continue; }
                if (TriggerFieldNames.Consumes(triggerType, field)) { continue; }
                _log.Warn("遗物 " + def.Id + " 给了 " + field + "，但触发点 " + triggerType +
                    " 不读它 —— 已忽略（不会写进 triggerTargetInfo）。");
            }
        }

        /// <summary>
        /// 装配后回读校验：拿**最终对象里**的 <c>triggerTargetInfo</c> 与作者 JSON 的声明逐字段比对，返回不一致清单。
        /// </summary>
        /// <remarks>
        /// ★ 2026-10-14 实测事故的护栏。<see cref="TriggerTargetInfo"/> 是 struct，写入方法一旦漏了 <c>ref</c>，
        /// 作者字段就只写进副本（实机现象：<c>battlePhase{Upkeep,isStart:true}</c> 变成 <c>(Main,false)</c>，
        /// 「回合开始抽牌」变成「玩家结束回合抽牌」，牌当场被 End 阶段弃掉）。
        /// 本方法的输入**必须取自最终要交给游戏的 RelicEffectInfo**，而不是某个中间局部变量 —— 只有这样，
        /// 「写丢了」「写错对象」这两类缺陷才能在装配期被抓住。返回空列表 = 声明与运行期一致。
        /// </remarks>
        internal List<string> VerifyRelicTriggerFields(TriggerTargetInfo info, ApiRelicDef def, string triggerType)
        {
            var mismatches = new List<string>();
            if (def == null) { return mismatches; }

            // ① battlePhase
            if (TriggerFieldNames.Consumes(triggerType, TriggerFieldNames.BattlePhase) &&
                def.BattlePhase != null && !string.IsNullOrEmpty(def.BattlePhase.Phase))
            {
                var phase = TriggerFieldValues.ResolveBattlePhase(def.BattlePhase.Phase);
                if (phase.Mapped)
                {
                    var expected = ParseGameEnum<Combat.BattleWorld.BattlePhase>(phase.Value);
                    if (info.battlePhase == null ||
                        info.battlePhase.battlePhase != expected ||
                        info.battlePhase.isStart != def.BattlePhase.IsStart)
                    {
                        mismatches.Add("battlePhase 声明 " + phase.Value + "/isStart=" + def.BattlePhase.IsStart +
                            "，运行期 " + (info.battlePhase == null
                                ? "null"
                                : info.battlePhase.battlePhase + "/isStart=" + info.battlePhase.isStart));
                    }
                }
            }

            // ② certainTurn
            if (TriggerFieldNames.Consumes(triggerType, TriggerFieldNames.CertainTurn) && def.CertainTurn != null)
            {
                var relation = TriggerFieldValues.ResolveCertainTurnRelation(def.CertainTurn.Relation);
                bool ok = info.certainTurnStartInfo != null &&
                          info.certainTurnStartInfo.requireCertainTurn == def.CertainTurn.Require &&
                          info.certainTurnStartInfo.turnCount == def.CertainTurn.TurnCount;
                if (ok && relation.Mapped)
                {
                    ok = info.certainTurnStartInfo.certainTurnRelationInfo ==
                         ParseGameEnum<Combat.BattleWorld.CertainTurnRelationInfo>(relation.Value);
                }
                if (!ok)
                {
                    mismatches.Add("certainTurn 声明 require=" + def.CertainTurn.Require +
                        "/turn=" + def.CertainTurn.TurnCount + "/" + (def.CertainTurn.Relation ?? "(空)") +
                        "，运行期 " + (info.certainTurnStartInfo == null
                            ? "null"
                            : "require=" + info.certainTurnStartInfo.requireCertainTurn +
                              "/turn=" + info.certainTurnStartInfo.turnCount +
                              "/" + info.certainTurnStartInfo.certainTurnRelationInfo));
                }
            }

            // ③ relativeCharacter
            if (TriggerFieldNames.Consumes(triggerType, TriggerFieldNames.RelativeCharacter) &&
                !string.IsNullOrEmpty(def.RelativeCharacter))
            {
                var rel = TriggerFieldValues.ResolveRelativeCharacter(def.RelativeCharacter);
                if (rel.Mapped)
                {
                    var expected = ParseGameEnum<Combat.BattleWorld.RelativeCharacterType>(rel.Value);
                    if (info.relativeCharacterType != expected)
                    {
                        mismatches.Add("relativeCharacter 声明 " + rel.Value +
                            "，运行期 " + info.relativeCharacterType);
                    }
                }
            }

            // ④ extraString
            if (TriggerFieldNames.Consumes(triggerType, TriggerFieldNames.ExtraString) &&
                !string.IsNullOrEmpty(def.ExtraString) && info.extraString != def.ExtraString)
            {
                mismatches.Add("extraString 声明 '" + def.ExtraString + "'，运行期 '" +
                    (info.extraString ?? "(空)") + "'");
            }

            // ⑤ colorE
            if (TriggerFieldNames.Consumes(triggerType, TriggerFieldNames.ColorE) &&
                !string.IsNullOrEmpty(def.ColorE))
            {
                var color = TriggerFieldValues.ResolveManaColor(def.ColorE);
                if (color.Mapped)
                {
                    var expected = ParseGameEnum<ColorE>(color.Value);
                    if (info.colorE != expected)
                    {
                        mismatches.Add("colorE 声明 " + color.Value + "，运行期 " + info.colorE);
                    }
                }
            }

            return mismatches;
        }

        /// <summary>诊断用：把这件遗物**实际写进去**的附加字段列成一行。</summary>
        internal string DescribeTriggerFields(TriggerTargetInfo info, string triggerType)
        {
            var parts = new List<string>();
            if (info.battlePhase != null) { parts.Add("battlePhase=" + info.battlePhase.battlePhase + "/isStart=" + info.battlePhase.isStart); }
            if (info.certainTurnStartInfo != null)
            {
                parts.Add("certainTurn=require:" + info.certainTurnStartInfo.requireCertainTurn +
                          "/turn:" + info.certainTurnStartInfo.turnCount +
                          "/" + info.certainTurnStartInfo.certainTurnRelationInfo);
            }
            if (info.relativeCharacterType != Combat.BattleWorld.RelativeCharacterType.Owner)
            {
                parts.Add("relativeCharacter=" + info.relativeCharacterType);
            }
            if (!string.IsNullOrEmpty(info.extraString)) { parts.Add("extraString=" + info.extraString); }
            if (info.colorE != ColorE.Red || TriggerFieldNames.Consumes(triggerType, TriggerFieldNames.ColorE))
            {
                parts.Add("colorE=" + info.colorE);
            }

            return parts.Count == 0 ? "(无)" : string.Join("，", parts.ToArray());
        }

        /// <summary>Api 侧是否给了这个 JSON 字段（null 与空串都算没给）。</summary>
        private static bool HasApiField(ApiRelicDef def, string jsonField)
        {
            switch (jsonField)
            {
                case TriggerFieldNames.BattlePhase: return def.BattlePhase != null;
                case TriggerFieldNames.CertainTurn: return def.CertainTurn != null;
                case TriggerFieldNames.RelativeCharacter: return !string.IsNullOrEmpty(def.RelativeCharacter);
                case TriggerFieldNames.ExtraString: return !string.IsNullOrEmpty(def.ExtraString);
                case TriggerFieldNames.ColorE: return !string.IsNullOrEmpty(def.ColorE);
                default: return false;
            }
        }

        /// <summary>
        /// 把**校验器已归一化**的枚举名转成真正的游戏枚举值。
        /// </summary>
        /// <remarks>
        /// 归一化保证大小写与拼写都对，所以这里用 <see cref="Enum.Parse(Type, string)"/> 是安全的
        /// （不走 <c>Enum.TryParse</c> 的"数字也能过"分支 —— 那种宽松正是校验器要拦的）。
        /// </remarks>
        private static T ParseGameEnum<T>(string canonicalName)
        {
            return (T)Enum.Parse(typeof(T), canonicalName);
        }

    }
}
