// =============================================================================
//  ModKit.Game · 渲染探针
//
//  ★ 为什么需要这个东西：
//    平台其它断言（卡读回、遗物池成员、能力按名可取）只证明**数据层持有**。
//    "游戏里真的把这张卡显示出来、且文字是对的"只能靠游戏自己渲染来证明。
//
//  ★ 挂钩点：CardBase.SetCardNameTMP(string cardName) —— 卡面名字的最终落点
//
//        private void SetCardNameTMP(string cardName)
//        {
//            string text = LocalizationSettings.StringDatabase
//                .GetLocalizedString("CardName", cardName, null, FallbackBehavior.UseProjectSettings);
//            ...
//            cardNameTextMeshPro.text = text;      // ← 真正上屏的值
//        }
//
//  ★★★ 本文件最重要的一条教训（2026-10-11，实机证伪）：
//    第一版 Postfix **自己又调了一次本地化查询**（`GetLocalizedString("CardName", key)`，2 参重载），
//    读到"木盾"就打印"✓ 上屏文本已本地化"。而用户实机看到卡面写着
//    `No translation found for 'sample_starter.wooden_shield' in CardName`。
//
//    → **探针断言的是它自己算出来的值，不是游戏真正放到屏幕上的值。假阳性。**
//    → 现在改为读 `__instance.cardNameTextMeshPro.text` —— **游戏亲自写进去的那个字符串**。
//
//    一般化：量具本身也可能骗人。**要断言"A 对 B 做了什么"，就去读 B，不要重算一遍 A。**
//
//  ★ 另：跨场景的观测不能依赖会随场景消亡的 MonoBehaviour。
//    早期版本把报告排成定时任务、挂在 Bootstrap.Update 上，结果报告**从不触发**
//    （那个 MonoBehaviour 在场景切换后就死了）。现在**首次命中即打印**，没有定时器。
// =============================================================================

using System;
using System.Collections.Generic;
using HarmonyLib;

namespace ModKit.Game
{
    /// <summary>渲染探针：记录游戏实际上屏过的卡名与文本。</summary>
    public static class RenderProbe
    {
        private static readonly object Gate = new object();

        /// <summary>平台装配过的卡名（用于判定"这是我们的卡"）。</summary>
        private static readonly HashSet<string> ModCardNames = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>观测到的上屏记录：卡名 → **游戏实际写进 TMP 的文本**。</summary>
        private static readonly Dictionary<string, string> Rendered = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>非 mod 卡的渲染次数（用于证明探针本身在工作）。</summary>
        private static int _vanillaRenders;

        /// <summary>是否已挂钩（幂等）。</summary>
        public static bool Installed { get; private set; }

        private static IGameLog _log;

        /// <summary>登记一批"我们的卡"，之后它们一被渲染就会被记录。</summary>
        public static void RegisterModCards(IEnumerable<string> cardNames, IGameLog log)
        {
            _log = log;
            if (cardNames == null) { return; }
            lock (Gate)
            {
                foreach (var n in cardNames)
                {
                    if (!string.IsNullOrEmpty(n)) { ModCardNames.Add(n); }
                }
            }
        }

        /// <summary>装挂钩。返回是否成功。</summary>
        public static bool Install(IGameLog log)
        {
            _log = log;
            if (Installed) { return true; }

            try
            {
                var target = AccessTools.Method(typeof(Card.CardBase), "SetCardNameTMP", new[] { typeof(string) });
                if (target == null)
                {
                    log.Warn("渲染探针：找不到 CardBase.SetCardNameTMP(string) —— 跳过" +
                        "（这意味着卡在游戏里真的显示出来这一层本轮无法自动断言）。");
                    return false;
                }

                var harmony = new Harmony("modkit.renderprobe");
                harmony.Patch(target, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(RenderProbe), nameof(Postfix))));

                Installed = true;
                log.Info("渲染探针已挂钩：CardBase.SetCardNameTMP(string)。" +
                    "mod 卡一旦被游戏渲染，会立即在日志里出现一行。");
                log.Info("渲染探针判定依据：**游戏实际写进 cardNameTextMeshPro.text 的值**" +
                    "（不是探针自己再查一次本地化表 —— 那样会给假阳性）。");
                return true;
            }
            catch (Exception ex)
            {
                log.Warn("渲染探针挂钩失败：" + ex.GetType().Name + " - " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Postfix：<c>__instance</c> 是被渲染的卡，<c>cardName</c> 是游戏请求的键。
        /// </summary>
        /// <remarks>
        /// ★ 判定读 <c>__instance.cardNameTextMeshPro.text</c> —— 游戏亲手写进去的值。
        /// 绝不在此重新调用本地化查询（那正是上一版假阳性的来源）。
        /// </remarks>
        private static void Postfix(Card.CardBase __instance, string cardName)
        {
            try
            {
                if (string.IsNullOrEmpty(cardName)) { return; }

                bool isOurs;
                lock (Gate) { isOurs = ModCardNames.Contains(cardName); }

                if (!isOurs)
                {
                    lock (Gate) { _vanillaRenders++; }
                    return;
                }

                // ★ 读游戏实际写上屏的文本
                //   用**反射**读 `.text`，而不是直接写 `__instance.cardNameTextMeshPro.text`。
                //   原因：TMP_Text 的继承链经过 UnityEngine.UI 的 MaskableGraphic，
                //   直接访问会要求平台同时引用 Unity.TextMeshPro 与 UnityEngine.UI 两个程序集
                //   （少一个就是 CS0012 连锁报错）。探针只读一个字符串值，
                //   不值得为此把这两条依赖压到平台上。
                string onScreen = null;
                try
                {
                    var field = AccessTools.Field(typeof(Card.CardBase), "cardNameTextMeshPro");
                    var tmp = field != null && __instance != null ? field.GetValue(__instance) : null;
                    if (tmp != null)
                    {
                        var textProp = AccessTools.Property(tmp.GetType(), "text");
                        if (textProp != null) { onScreen = textProp.GetValue(tmp, null) as string; }
                        else
                        {
                            var textField = AccessTools.Field(tmp.GetType(), "m_text");
                            if (textField != null) { onScreen = textField.GetValue(tmp) as string; }
                        }
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(onScreen)) { onScreen = "(卡面文本为空或取不到)"; }

                int count;
                lock (Gate)
                {
                    if (Rendered.ContainsKey(cardName)) { count = 0; }   // 已报告过
                    else { Rendered[cardName] = onScreen; count = Rendered.Count; }
                }

                if (count > 0 && _log != null)
                {
                    // 判定：游戏本地化失败时会在卡面写 "No translation found for … in <表>"
                    bool missing = onScreen.IndexOf("No translation found", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isKey = onScreen.IndexOf(cardName, StringComparison.Ordinal) >= 0;

                    string verdict;
                    if (missing) { verdict = "✗ 游戏报**找不到翻译**（表里有键缺失）—— 文案注入失败"; }
                    else if (isKey) { verdict = "✗ 卡面显示的是 key 名 —— 文案注入失败"; }
                    else { verdict = "✓ 卡面文本既非缺失提示、也非 key 名 —— 文案注入生效"; }

                    _log.Info("★★ 渲染探针命中 [第 " + count + " 张] " + cardName +
                        " —— 卡面实际文本 \u201c" + onScreen + "\u201d —— " + verdict);
                }
            }
            catch
            {
                // 探针自己绝不能影响游戏
            }
        }

        /// <summary>把观测结果写成一段汇总日志（可选调用）。</summary>
        public static void Report(IGameLog log)
        {
            if (log == null) { log = _log; }
            if (log == null) { return; }

            if (!Installed)
            {
                log.Info("渲染探针：未安装（无法断言游戏内表现）。");
                return;
            }

            List<KeyValuePair<string, string>> hits;
            int vanilla;
            lock (Gate)
            {
                hits = new List<KeyValuePair<string, string>>(Rendered);
                vanilla = _vanillaRenders;
            }

            if (hits.Count == 0)
            {
                log.Info("渲染探针汇总：截止目前，游戏还没有渲染过任何 mod 卡" +
                    "（已观测到 " + vanilla + " 次原版卡渲染，说明探针本身在工作）。");
                return;
            }

            log.Info("=== 渲染探针汇总（卡面实际文本）===");
            for (int i = 0; i < hits.Count; i++)
            {
                log.Info("  " + hits[i].Key + " → \u201c" + hits[i].Value + "\u201d");
            }
            log.Info("（另有 " + vanilla + " 次原版卡渲染）");
            log.Info("=== 渲染探针汇总结束 ===");
        }
    }
}
