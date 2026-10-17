// =============================================================================
//  ModKit.Game · 重建后重注册（装配层侧）
//
//  配合 RebuildGuard：游戏重建数据容器后，把平台内容重新写回去。
//  单独一个 partial 文件，避免往 ContentAssembler.cs 里插代码（插行切坏过结构）。
// =============================================================================

using System;
using System.Collections.Generic;
using Ability;
using Card;
using GameData;
using Relic;

namespace ModKit.Game
{
    public sealed partial class ContentAssembler
    {
        /// <summary>已装配的能力对象（重建守卫要重注册时用）。</summary>
        private static readonly List<AbilitySo> AssembledAbilitySos = new List<AbilitySo>();

        /// <summary>记下一个刚装配好的能力，供重建后重注册。</summary>
        internal static void RememberAbility(AbilitySo so)
        {
            if (so == null) { return; }
            AssembledAbilitySos.Add(so);
        }

        /// <summary>
        /// 把已装配的能力**重新注册**回游戏的数据容器。
        /// </summary>
        /// <remarks>
        /// 游戏 <c>AbilityDataManager.LoadAllAbility()</c> 会 <c>_allAbilitySos.Clear()</c>
        /// 后只从 Resources 重灌，所以平台注册的能力会被冲掉。
        /// 这里把当初装配好的 <c>AbilitySo</c> 对象**原样**写回去（不重新克隆，避免产生副本）。
        /// </remarks>
        internal static void ReRegisterAbilities(AssemblyResult result)
        {
            if (AssembledAbilitySos.Count == 0) { return; }

            var dm = DataManager.Instance;
            if (dm == null) { return; }
            var adm = dm.GetAbilityDataManager();
            if (adm == null) { return; }

            foreach (var so in AssembledAbilitySos)
            {
                if (so == null || so.abilityInfo == null) { continue; }
                string name = so.abilityInfo.abilityName;

                try
                {
                    var dict = Reflect.Get<Dictionary<string, AbilitySo>>(adm, "_allAbilitySos");
                    if (dict != null) { dict[name] = so; }

                    // 分类桶（按 type × owner）——与装配时同一套规则
                    if (!so.isInRandomPool) { continue; }
                    bool player = so.abilityOwnerShip == AbilityOwnerShip.Player ||
                                  so.abilityOwnerShip == AbilityOwnerShip.All;
                    bool enemy = so.abilityOwnerShip == AbilityOwnerShip.Enemy ||
                                 so.abilityOwnerShip == AbilityOwnerShip.All;

                    switch (so.abilityInfo.abilityType)
                    {
                        case AbilityType.Buff:
                            if (player) { PutAbilityRaw(adm, "_allPlayerBuff", so, name); }
                            if (enemy) { PutAbilityRaw(adm, "_allEnemyBuff", so, name); }
                            break;
                        case AbilityType.DeBuff:
                            if (player) { PutAbilityRaw(adm, "_allPlayerDeBuff", so, name); }
                            if (enemy) { PutAbilityRaw(adm, "_allEnemyDeBuff", so, name); }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    if (result != null)
                    {
                        result.Warnings.Add(name + "：重建后重注册能力失败 —— " + ex.Message);
                    }
                }
            }
        }

        private static void PutAbilityRaw(AbilityDataManager adm, string field, AbilitySo so, string name)
        {
            var dict = Reflect.Get<Dictionary<string, AbilitySo>>(adm, field);
            if (dict != null) { dict[name] = so; }
        }

        /// <summary>
        /// 把已装配的遗物**重新注册**回游戏的数据容器。
        /// </summary>
        /// <remarks>
        /// 遗物这一侧游戏会重建两样东西：
        /// ① <c>_allRelicDataCollections</c> / <c>_allInGameRelicSo</c>（<c>LoadAllRelic</c> 里 Clear + 从 Resources 重灌）
        /// ② 战役遗物池（<c>SetCampaignRelicPool</c>）。
        /// <c>RegisterRelicCollection</c> 本身已幂等（按 <c>ModKit_&lt;modId&gt;</c> 查重），
        /// 所以直接重调它 + 重建池子即可。
        /// </remarks>
        internal void ReRegisterRelics(string modId, List<RelicSo> relics, AssemblyResult result)
        {
            var dm = DataManager.Instance;
            if (dm == null) { return; }
            var manager = dm.GetRelicDataManager();
            if (manager == null) { return; }

            // ★ 关键：**必须重写这两个字典**（2026-10-11 第二次实机才补上）。
            //   GetRelicSo(name) 就是 `_allInGameRelicSo.GetValueOrDefault(name)`；
            //   _allStandPoolRelicSo 是"标准池"来源。
            //   而 RegisterRelicCollection / RefreshCampaignRelicPool **都不碰这两个字典** ——
            //   所以只重调它们的话，重建之后 GetRelicSo 仍然返回 null
            //   （实机症状：图鉴里看不到、验收辅助"取不到遗物"）。
            if (relics != null)
            {
                foreach (var so in relics)
                {
                    if (so == null || so.relicInfo == null) { continue; }
                    RegisterRelicSo(manager, "_allInGameRelicSo", so, so.relicInfo.relicName, result);
                    RegisterRelicSo(manager, "_allStandPoolRelicSo", so, so.relicInfo.relicName, result);
                }
            }

            // 再补集合与战役池
            RegisterRelicCollection(modId, relics, result);
            RefreshCampaignRelicPool(manager, result, modId);
        }
    }
}
