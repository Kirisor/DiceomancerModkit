using System;
using Card;
using GameData;
using HarmonyLib;
using Relic;
using Trigger;
using UnityEngine;

namespace ManualDemo
{
    /// <summary>
    /// 「奶蛙之护符」的两处运行时修正。
    /// </summary>
    /// <remarks>
    /// 本 build 的 <c>ModKit.Api.dll</c> 无法从源码重建（缺 ClassSpec），
    /// 所以遗物定义里没法直接写 <c>colors</c> / <c>triggerLimit</c>。
    /// 这里在装配完成后就地补上：
    /// <list type="bullet">
    /// <item>三颗流派球：紫 / 绿 / <b>黑</b>（把德鲁伊护符的虚色球换成黑球）；</item>
    /// <item>触发上限：每回合 2 次（<c>hasTriggerLimit</c> + <c>triggerLimit</c> + <c>EachTurn</c>）。</item>
    /// </list>
    /// </remarks>
    [HarmonyPatch(typeof(RelicDataManager), "GetRelicSo")]
    internal static class MilkFrogTalismanPatch
    {
        internal const string RelicSuffix = ".milk_frog_talisman";
        private static bool _applied;

        private static void Postfix(string relicName, RelicSo __result)
        {
            if (_applied || __result == null || __result.relicInfo == null) { return; }
            if (string.IsNullOrEmpty(relicName) || !relicName.EndsWith(RelicSuffix, StringComparison.Ordinal)) { return; }

            try
            {
                __result.relicInfo.relicColors = new[] { ColorE.Purple, ColorE.Green, ColorE.Black };
                __result.relicInfo.hasTriggerLimit = true;
                __result.relicInfo.triggerLimitType = RelicTriggerLimitType.EachTurn;
                __result.relicInfo.triggerLimit = 2;
                _applied = true;
                Debug.Log("[ModKit] 奶蛙之护符：已设为 紫/绿/黑 三球，每回合至多触发 2 次。");
            }
            catch (Exception e)
            {
                Debug.LogError("[ModKit] 奶蛙之护符：修正失败 —— " + e.Message);
            }
        }
    }
}
