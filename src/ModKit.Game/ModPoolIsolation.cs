// =============================================================================
//  ModPoolIsolation —— 把「别的职业的模组卡」挡在当前职业的奖励池之外。
//
//  ★ 问题（2026-10-24 实机反馈）：
//    玩原版职业时，奖励里会冒出奶蛙职业的卡。
//
//  ★ 原因（反编译确认，`Assembly-CSharp` 的方法坐标是反编译行号）：
//    `LootManager.GetCardPoolByInfo` 的 `CareerPool` 分支（44403）取的是
//    `CardDataManager.GetAllStandardDataCollections()` ——
//    **所有集合、不区分职业、也不看集合的 colorEs**，拼完再按每个掉落包的
//    `CardRequirementCheckInfo` 过滤。也就是说：
//      · `CampaignPool`（战役池）是干净的：`SetCampaignCardPool` 用
//        `GetStandardCardDataCollectionByColorE(used)` 按 colorEs ⊆ 本局三色 筛过，
//        模组集合的 colorEs 是「职业三色」（CareerAssembler 收紧过）⇒ 原版职业筛不进来；
//      · 但凡掉落包走 `CareerPool`，模组卡就在候选里，能不能掉全看那个包的颜色条件。
//        模组卡大多是单色或绿+黑，碰上用了这些颜色的原版职业就直接进来了。
//
//  ★ 做法：挂 `LootManager.GetCardPoolByInfo`（掉落取池的唯一出口）与
//    `CardDataManager.GetAllCardsCardDateCollectionsInRandomPool`（"全都混一起"规则用的池），
//    在**当前职业不是本 mod 的职业**时，把该 mod 的卡从返回的候选池里摘掉。
//
//    · 只处理「声明了职业的 mod」（有 classKey）：纯加卡 mod（没有职业）保持原样，
//      那种 mod 本来就是想让卡混进原版职业。
//    · 不在战役里（主菜单/图鉴）不筛，那些界面要看到全部内容。
//    · 特意不挂 `GetAllStandardDataCollections`：它同时被「生涯统计职业列表」调用
//      （`Assembly-CSharp:23082`），在那儿筛会把模组职业从统计页摘掉。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Card;
using Combat;
using GameData;
using HarmonyLib;

namespace ModKit.Game
{
    /// <summary>模组卡池隔离：当前职业不是某个 mod 的职业时，把该 mod 的集合/卡片从随机池里摘掉。</summary>
    /// <remarks>
    /// 只在「正在战役里」时生效（`CampaignManager.GetCampaignUsedColorE()` 有 3 个颜色且能解析出职业名）。
    /// 主菜单 / 图鉴这类不跑战役的场景**不筛** —— 那些界面要看到全部内容。
    /// </remarks>
    internal static class ModPoolIsolation
    {
        private sealed class ModEntry
        {
            public string ModId;
            public string ClassKey;
            public ColorE[] ClassColors;
            public readonly HashSet<string> CardNames = new HashSet<string>(StringComparer.Ordinal);
        }

        private static readonly List<ModEntry> _mods = new List<ModEntry>();
        private static readonly Dictionary<string, ModEntry> _byCard =
            new Dictionary<string, ModEntry>(StringComparer.Ordinal);

        private static string _lastLogSignature;

        /// <summary>登记一个「带职业的 mod」：它的职业 key、职业三色与它的卡表。</summary>
        /// <remarks>纯加卡 mod（没有职业）不登记 —— 那种 mod 本来就想让卡混进原版职业。</remarks>
        internal static void Register(string modId, string classKey, ColorE[] classColors, IEnumerable<CardSo> cards)
        {
            if (string.IsNullOrEmpty(modId) || string.IsNullOrEmpty(classKey)) { return; }

            ModEntry entry = _mods.FirstOrDefault(m => m != null && m.ClassKey == classKey);
            if (entry == null)
            {
                entry = new ModEntry { ModId = modId, ClassKey = classKey };
                _mods.Add(entry);
            }
            if (classColors != null && classColors.Length == 3) { entry.ClassColors = classColors; }

            if (cards != null)
            {
                foreach (var so in cards)
                {
                    if (so == null || string.IsNullOrEmpty(so.cardName)) { continue; }
                    if (entry.CardNames.Add(so.cardName)) { _byCard[so.cardName] = entry; }
                }
            }
        }

        /// <summary>
        /// 当前战役的职业 key（`&lt;modId&gt;.&lt;classId&gt;` 或原版职业名）；不在战役里返回 null。
        /// </summary>
        /// <remarks>
        /// ★ 先按**颜色集合**比对模组职业，再退回游戏自己的 `GetClassNameByColorE`。
        /// 原因：`GetClassNameByColorE` 返回的是「第一个 colorEs ⊆ 本局三色 的 Class 集合」，
        /// 万一某天出现一个单色/双色的 Class 集合排在前面，它会把模组职业误判成原版职业，
        /// 结果模组职业自己反而抽不到卡。颜色集合比对是精确的，优先用它。
        /// </remarks>
        private static string CurrentClassKey()
        {
            if (_mods.Count == 0) { return null; }
            try
            {
                var campaign = CampaignManager.Instance;
                if (campaign == null) { return null; }

                ColorE[] colors = campaign.GetCampaignUsedColorE();
                if (colors == null || colors.Length != 3) { return null; }

                foreach (var mod in _mods)
                {
                    if (mod != null && SameColorSet(mod.ClassColors, colors)) { return mod.ClassKey; }
                }

                var cdm = CardDataManager.Instance;
                if (cdm == null) { return null; }

                string name = cdm.GetClassNameByColorE(colors);
                if (string.IsNullOrEmpty(name) || name == "InvalidClass") { return null; }
                return name;
            }
            catch
            {
                return null;
            }
        }

        private static bool SameColorSet(ColorE[] a, ColorE[] b)
        {
            if (a == null || b == null || a.Length != 3 || b.Length != 3) { return false; }
            for (int i = 0; i < 3; i++)
            {
                if (!b.Contains(a[i])) { return false; }
            }
            return true;
        }

        /// <summary>卡片数组过滤：非本职业时摘掉该 mod 的卡。</summary>
        internal static CardSo[] FilterCards(CardSo[] all)
        {
            if (all == null || all.Length == 0 || _byCard.Count == 0) { return all; }

            string current = CurrentClassKey();
            if (current == null) { return all; }

            List<CardSo> kept = null;
            for (int i = 0; i < all.Length; i++)
            {
                var so = all[i];
                ModEntry owner;
                bool drop = so != null && !string.IsNullOrEmpty(so.cardName)
                    && _byCard.TryGetValue(so.cardName, out owner)
                    && owner != null && owner.ClassKey != current;

                if (drop)
                {
                    if (kept == null) { kept = new List<CardSo>(all.Length); kept.AddRange(all.Take(i)); }
                    continue;
                }
                if (kept != null) { kept.Add(so); }
            }

            if (kept == null) { return all; }

            LogOnce(current, all.Length, kept.Count);
            return kept.ToArray();
        }

        private static void LogOnce(string current, int before, int after)
        {
            string signature = current + "/" + before + "/" + after;
            if (signature == _lastLogSignature) { return; }
            _lastLogSignature = signature;
            try
            {
                ModKitLog.Info("职业隔离：当前职业 " + current + " —— 已把其它 mod 的卡从随机池里摘掉（"
                    + before + " → " + after + "）。");
            }
            catch { }
        }

        /// <summary>战役池（`CampaignManager.campaignCardPools`）过滤 + 诊断。</summary>
        /// <remarks>
        /// 战役池本来应该被「集合 colorEs ⊆ 本局三色」挡住，但那条链依赖两个我们控制不了的量：
        /// ① 本局三色到底是多少（读档 / 选球 / 序章各写一次）；
        /// ② 集合的 colorEs 有没有在某次数据重建里被改回空数组（空数组对所有职业都成立）。
        /// 所以这里不再信任上游：直接把「当前职业不是本 mod 职业」的卡从池里摘掉，
        /// 并把**实际三色 + 解析出的职业 + 池内模组卡张数 + 摘掉张数**打进日志 ——
        /// 复现时一眼就能看出是哪一环错了。
        /// <para>
        /// 这条也覆盖 `EffectSpEffectJoker` 这类**直接读 `campaignCardPools`** 的效果
        /// （不走 `GetCardPoolByInfo`，所以取池出口的钩子管不到它）。
        /// </para>
        /// </remarks>
        internal static void FilterCampaignPool(List<CardSo> pool)
        {
            if (pool == null || pool.Count == 0 || _byCard.Count == 0) { return; }

            string current = CurrentClassKey();
            int modCards = 0;
            int removed = 0;
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                var so = pool[i];
                ModEntry owner;
                if (so == null || string.IsNullOrEmpty(so.cardName)) { continue; }
                if (!_byCard.TryGetValue(so.cardName, out owner) || owner == null) { continue; }

                modCards++;
                if (string.Equals(owner.ClassKey, current, StringComparison.Ordinal)) { continue; }

                pool.RemoveAt(i);
                removed++;
            }

            LogCampaignPool(current, pool.Count, modCards, removed);
        }

        private static void LogCampaignPool(string current, int remaining, int modCards, int removed)
        {
            string colors = CurrentColorsText();
            string signature = "campaign/" + current + "/" + colors + "/" + remaining + "/" + modCards + "/" + removed;
            if (signature == _lastLogSignature) { return; }
            _lastLogSignature = signature;
            try
            {
                ModKitLog.Info("职业隔离：战役池重建 —— 本局三色 " + colors + "，解析职业 " + (current ?? "<未知>")
                    + "，池内模组卡 " + modCards + " 张，摘除 " + removed + " 张，剩余 " + remaining + " 张。");
            }
            catch { }
        }

        private static string CurrentColorsText()
        {
            try
            {
                var campaign = CampaignManager.Instance;
                if (campaign == null) { return "<无战役>"; }

                ColorE[] colors = campaign.GetCampaignUsedColorE();
                if (colors == null) { return "<null>"; }
                if (colors.Length == 0) { return "<空>"; }

                return string.Join("+", colors.Select(c => c.ToString()));
            }
            catch
            {
                return "<读取失败>";
            }
        }

        // ---------------------------------------------------------------------
        // 挂钩子：只改「随机取卡」这两条入口，不动其它查询
        // ---------------------------------------------------------------------

        /// <summary>战役池重建（`SetCampaignCardPool` 是 private，按名挂）。</summary>
        /// <remarks>每次开局 / 读档 / 平台手动重建都会走到这里，正好是「池子被重新算一遍」的时机。</remarks>
        [HarmonyPatch(typeof(CampaignManager), "SetCampaignCardPool")]
        internal static class PatchSetCampaignCardPool
        {
            [HarmonyPostfix]
            internal static void Postfix(CampaignManager __instance)
            {
                if (__instance == null) { return; }
                FilterCampaignPool(__instance.campaignCardPools);
            }
        }

        /// <summary>
        /// `LootManager.GetCardPoolByInfo` —— 掉落取池的唯一出口（CareerPool / CampaignPool /
        /// SpecialPool / SpecificPool 都从这里返回）。
        /// </summary>
        /// <remarks>
        /// ★ 故意**不**挂 `CardDataManager.GetAllStandardDataCollections`：那个方法同时被
        /// 「生涯统计的职业列表」调用（`Assembly-CSharp:23082`），在那里筛会把模组职业
        /// 从统计页里一起摘掉。挂在取池出口上，影响面只有奖励/预览。
        /// </remarks>
        [HarmonyPatch(typeof(Loot.LootManager), "GetCardPoolByInfo")]
        internal static class PatchCardPoolByInfo
        {
            [HarmonyPostfix]
            internal static void Postfix(ref CardSo[] __result)
            {
                __result = FilterCards(__result);
            }
        }

        /// <summary>规则「CardGenPoolAllMixedUp」走的全随机池。</summary>
        [HarmonyPatch(typeof(CardDataManager), "GetAllCardsCardDateCollectionsInRandomPool")]
        internal static class PatchRandomPoolCards
        {
            [HarmonyPostfix]
            internal static void Postfix(ref CardSo[] __result)
            {
                __result = FilterCards(__result);
            }
        }
    }
}
