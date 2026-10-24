// =============================================================================
//  ModKit.Game · 召唤物名字挂钩（SummonHook）
//
//  解决什么问题
//  ------------
//  游戏的 `CardKeywordSummon.GetSummonInfo()` 里写死了：
//
//      SummonName = OwnerCard.CardName
//
//  而 `SummonName` 又被拿去查两张表：
//    · 立绘  —— `AssetRefSo.SpriteDict`（`SummonBase.cs:65`，查不到只打一行
//               `Sprite not found: …`，**不致命**）
//    · 显示名 —— `CardName` 本地化表（`SummonBase.cs:255`）
//
//  原版那 9 张 `Summon` 卡之所以天衣无缝，是因为**卡名本身就是召唤物名**
//  （`FireElement` / `WaterElement` / …）。而 mod 卡的卡名带 mod 前缀
//  （`sample_starter.xxx`），两张表都查不到 —— 召唤物会出现、会死、会给奖励卡，
//  但**没有立绘**，日志里多一行 `Sprite not found`。
//
//  怎么修
//  ------
//  作者在 JSON 里写 `"summon": "FireElement"`；平台把
//  `卡名 → 召唤物名` 记进本类的表，再用 Harmony postfix 把
//  `GetSummonInfo()` 返回的 `OrganismInfo.SummonName` 换掉。
//
//  ★ 为什么只改 `SummonName` 一处就够：它同时决定立绘、显示名、
//    以及 `SummonManager` 对 `"FireElement"` 的特判（`SummonManager.cs:337`）。
//
//  ★ 安全原则（与原版共存）：任何一步失败都只记日志、**绝不改变原版行为**。
//    挂钩装不上、卡名查不到、或作者没写 `summon` —— 一律原样放行。
// =============================================================================

using System;
using System.Collections.Generic;
using Card;        // CardBase
using HarmonyLib;  // Harmony, AccessTools, HarmonyMethod
using Keyword;     // CardKeywordSummon
using Organism;    // OrganismInfo
using UnityEngine; // Debug

namespace ModKit.Game
{
    /// <summary>把作者声明的召唤物名字，换成游戏真正会去查表的那个名字。</summary>
    public static class SummonHook
    {
        /// <summary>`卡名（含 mod 前缀） → 召唤物名`。卡名在全局唯一，用它当键最稳。</summary>
        private static readonly Dictionary<string, string> Names =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static bool _installed;
        private static bool _failed;

        /// <summary>
        /// 登记一个「这张卡的召唤物叫什么」。由 <c>ContentAssembler.BuildKeywordInfo</c> 调用。
        /// 首次调用时顺带把挂钩装上（装配发生在开局，早于任何战斗，时序安全）。
        /// </summary>
        public static void Register(string cardName, string summonName)
        {
            if (string.IsNullOrEmpty(cardName) || string.IsNullOrEmpty(summonName)) { return; }

            Names[cardName] = summonName;
            int dot = cardName.IndexOf('.');
            if (dot >= 0) { Names[cardName.Substring(dot + 1)] = summonName; }
            EnsureInstalled();
        }

        private static void EnsureInstalled()
        {
            if (_installed || _failed) { return; }

            try
            {
                // GetSummonInfo 是 private，AccessTools 按名字+参数表取，不受访问级别限制。
                var target = AccessTools.Method(typeof(CardKeywordSummon), "GetSummonInfo", Type.EmptyTypes);
                if (target == null)
                {
                    _failed = true;
                    ModKitLog.Warn("召唤挂钩：找不到 CardKeywordSummon.GetSummonInfo() —— 跳过；" +
                        "关键词的 summon 字段不会生效（召唤物仍会出现，但没有立绘）。");
                    return;
                }

                var harmony = new Harmony("modkit.summonhook");
                harmony.Patch(target, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(SummonHook), nameof(Postfix))));

                _installed = true;
                ModKitLog.Info("召唤挂钩已安装：CardKeywordSummon.GetSummonInfo()，已登记召唤物名 "
                    + Names.Count + " 个。");
            }
            catch (Exception ex)
            {
                _failed = true;
                ModKitLog.Warn("召唤挂钩安装失败：" + ex.GetType().Name + " - " + ex.Message + "（原版行为不受影响）");
            }
        }

        /// <summary>
        /// `GetSummonInfo()` 返回之后调用：若这张卡声明过召唤物名，就把它换掉。
        /// <c>OrganismInfo</c> 是值类型，用 <c>ref __result</c> 改完直接生效。
        /// </summary>
        private static void Postfix(CardKeywordSummon __instance, ref OrganismInfo __result)
        {
            try
            {
                if (__instance == null) { return; }

                CardBase owner = __instance.OwnerCard;
                if (owner == null || string.IsNullOrEmpty(owner.CardName)) { return; }

                string summonName;
                if (!Names.TryGetValue(owner.CardName, out summonName))
                {
                    // 兜底：卡名是 "<modId>.<短id>" 时，也用短 id 查一次（注册侧可能只拿到短 id）。
                    int dot = owner.CardName.IndexOf('.');
                    if (dot < 0 || !Names.TryGetValue(owner.CardName.Substring(dot + 1), out summonName))
                    {
                        ModKitLog.Warn("召唤挂钩：卡 " + owner.CardName + " 没有登记召唤物名（表里 "
                            + Names.Count + " 条）。");
                        return;
                    }
                }
                if (string.IsNullOrEmpty(summonName)) { return; }

                __result.SummonName = summonName;
            }
            catch (Exception ex)
            {
                // 绝不让挂钩影响原版：出错就原样放行。
                Debug.LogWarning("[ModKit] 召唤挂钩在执行期出错（已忽略）：" + ex.Message);
            }
        }
    }
}
