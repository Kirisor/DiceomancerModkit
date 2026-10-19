// =============================================================================
//  ModKit.Game · 图鉴跳转（L2）
//
//  原版在「非战斗」状态右键卡牌会走 CardBase.TryCheckThisCardInBook()，跳原版图鉴。
//  原版图鉴里只有 534 张原版卡，mod 卡进去找不到。
//  这里给 TryCheckThisCardInBook 打 Prefix：
//    · 卡名属于我们收录的 mod 卡 ⇒ 打开「模组图鉴」并定位到那一页，吞掉原版流程；
//    · 其它卡 ⇒ 返回 true，走原版。
// =============================================================================

using System;
using HarmonyLib;
using ModKit.Api;

namespace ModKit.Game
{
    internal static partial class ModCompendium
    {
        /// <summary>跳转提示文本（标题下方那一行）。</summary>
        internal static TMPro.TextMeshProUGUI _jumpText;

        /// <summary>
        /// 打开图鉴并定位到指定卡；卡名不是我们收录的 mod 卡时返回 false（交给原版处理）。
        /// </summary>
        internal static bool TryOpenToCard(string cardName)
        {
            try
            {
                if (string.IsNullOrEmpty(cardName)) { return false; }

                int index = -1;
                for (int i = 0; i < _cards.Count; i++)
                {
                    var c = _cards[i];
                    if (c != null && string.Equals(c.cardName, cardName, StringComparison.Ordinal))
                    {
                        index = i;
                        break;
                    }
                }
                if (index < 0) { return false; }

                if (_panelGo == null) { BuildPanel(); }
                if (_panelGo == null) { return false; }

                if (!_panelGo.activeSelf)
                {
                    _panelGo.SetActive(true);
                    BlockOtherUI(true);
                }

                int perPage = Math.Max(1, PerPage());
                _tab = 0;
                _page = index / perPage;
                Refresh();

                if (_jumpText != null) { _jumpText.text = "来自右键跳转：" + cardName; }
                return true;
            }
            catch (Exception ex)
            {
                Warn("跳转", "打开模组图鉴定位失败 —— " + Describe(ex));
                return false;
            }
        }
    }

    /// <summary>把「右键卡牌 → 看它在书里」的入口接到模组图鉴。</summary>
    [HarmonyPatch(typeof(Card.CardBase), "TryCheckThisCardInBook")]
    internal static class CompendiumJumpPatch
    {
        private static bool _installed;

        internal static void Install(IGameLog log)
        {
            if (_installed) { return; }
            try
            {
                var target = AccessTools.Method(typeof(Card.CardBase), "TryCheckThisCardInBook");
                if (target == null)
                {
                    log.Warn("图鉴跳转：找不到 CardBase.TryCheckThisCardInBook —— 右键跳转不生效");
                    return;
                }
                var harmony = new Harmony("modkit.compendiumjump");
                harmony.Patch(target, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(CompendiumJumpPatch), nameof(Prefix))));
                _installed = true;
                log.Info("图鉴跳转已挂钩：右键 mod 卡会打开模组图鉴并定位到该卡。");
            }
            catch (Exception ex)
            {
                log.Warn("图鉴跳转安装失败：" + ex.Message);
            }
        }

        private static bool Prefix(Card.CardBase __instance)
        {
            try
            {
                if (__instance == null) { return true; }
                var name = __instance.CardName;
                if (string.IsNullOrEmpty(name)) { return true; }
                if (ModCompendium.TryOpenToCard(name)) { return false; }   // 已接管，跳过原版
            }
            catch { }
            return true;
        }
    }
}
