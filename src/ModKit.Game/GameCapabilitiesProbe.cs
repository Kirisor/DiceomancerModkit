// =============================================================================
//  ModKit.Game · 启动探测（平台能力的唯一来源）
//
//  这个类回答一个问题：**"本 build 里，作者的哪些写法真的能用？"**
//
//  它是"作者不用懂游戏"的兑现处：
//    · 不硬编码"6 个颜色都能用" —— 而是问游戏 <c>ManaIconDic</c> 到底注册了哪几个键；
//    · 不硬编码原版卡名 —— 而是从 <c>CardDataManager</c> 现取；
//    · 触发点表用**实际发出的名字**，而不是游戏的 <c>AllTriggerType</c> 枚举
//      （探针实测两者不一致：枚举里有的从不发出，发出的有的不在枚举里）。
//
//  ★ 本文件**故意大量使用 try/catch**：
//    这是"接缝边界"——平台的代码，允许降级；游戏原方法的失败语义不归它管。
//    与第 9 步 §7.2「对齐 A」一致：接缝边界要求 try/catch + FailOpen 降级。
//    探测失败 → 该项能力为空 → 校验器会把作者的内容判为不可用，
//    而不是让游戏在运行几十分钟后以"玩法异常"的形式暴露。
//
//  证据来源：探针报告（Character/CardManager/ManaManager/RelicBase 逐行核实）
// =============================================================================

using System;
using System.Collections.Generic;
using Card;
using Combat;
using Effects;
using GameData;
using Keyword;
using ModKit.Api;
using ModKit.Core;

namespace ModKit.Game
{
    /// <summary>
    /// 探测结果：不可变的能力快照，实现 <see cref="IContentCapabilities"/> 供校验器消费。
    /// </summary>
    public sealed class GameCapabilities : CapabilitiesBase
    {
        private readonly List<ManaColor> _colors;
        private readonly List<List<ManaColor>> _costCombos;
        private readonly List<string> _cards;
        private readonly List<string> _relics;
        private readonly List<string> _keywords;
        private readonly List<string> _triggers;
        private readonly List<string> _customEffects;

        public GameCapabilities(
            List<ManaColor> colors,
            List<List<ManaColor>> costCombos,
            List<string> cards,
            List<string> relics,
            List<string> keywords,
            List<string> triggers)
        {
            _colors = colors ?? new List<ManaColor>();
            _costCombos = costCombos ?? new List<List<ManaColor>>();
            _cards = cards ?? new List<string>();
            _relics = relics ?? new List<string>();
            _keywords = keywords ?? new List<string>();
            _triggers = triggers ?? new List<string>();

            // 这一项由 Runtime 在扫描 mod 程序集后回填（[ModEffect]）。
            _customEffects = new List<string>();
        }

        public override IReadOnlyList<ManaColor> AvailableCostColors { get { return _colors; } }

        /// <summary>
        /// 本 build 注册过图标的**费用槽组合**（含单色与多色混合）。
        /// </summary>
        /// <remarks>
        /// 由 <see cref="ProbeCostCombos"/> 枚举 <c>ManaManager.ManaIconDic</c> 的键得到。
        /// 多色混合槽（如"虚色或红色"）的图标是**预制资产**，只有这里列出的组合能用。
        /// </remarks>
        public IReadOnlyList<IReadOnlyList<ManaColor>> AvailableCostCombos
        {
            get
            {
                var result = new List<IReadOnlyList<ManaColor>>();
                foreach (var c in _costCombos) { result.Add(c); }
                return result;
            }
        }

        /// <summary>
        /// 这个费用槽组合注册过图标吗？ —— **有序**匹配。
        /// </summary>
        /// <remarks>
        /// 为什么不复用"每个颜色都可用"来判断：多色混合槽的图标是**预制的一整张图**，
        /// 游戏按完整有序序列查表。所以 <c>[Void, Red]</c> 与 <c>[Red, Void]</c> 是两个不同的键，
        /// 且即使两个单色都可用，它们的组合也**未必**注册过。
        /// </remarks>
        public override bool IsKnownCostCombo(IReadOnlyList<ManaColor> combo)
        {
            if (combo == null || combo.Count == 0) { return false; }

            for (int i = 0; i < _costCombos.Count; i++)
            {
                var known = _costCombos[i];
                if (known.Count != combo.Count) { continue; }

                bool same = true;
                for (int j = 0; j < known.Count; j++)
                {
                    if (known[j] != combo[j]) { same = false; break; }
                }
                if (same) { return true; }
            }
            return false;
        }
        public override IReadOnlyList<string> VanillaCardNames { get { return _cards; } }
        public override IReadOnlyList<string> VanillaRelicNames { get { return _relics; } }
        public override IReadOnlyList<string> KnownKeywords { get { return _keywords; } }
        public override IReadOnlyList<string> KnownTriggerNames { get { return _triggers; } }

        public override IReadOnlyList<string> KnownCustomEffectIds { get { return _customEffects; } }

        /// <summary>供 Runtime 回填扫描到的自定义效果 id（内部使用）。</summary>
        internal void AddCustomEffectId(string id)
        {
            if (!string.IsNullOrEmpty(id) && !_customEffects.Contains(id)) { _customEffects.Add(id); }
        }
    }

    /// <summary>
    /// 探测器的探测结果统计 —— 写进日志的"哨兵"，让"探测到底成功了没有"可核对。
    /// </summary>
    public sealed class ProbeSummary
    {
        public int ColorCount;
        public int CardCount;
        public int RelicCount;
        public int KeywordCount;
        public int TriggerCount;

        /// <summary>失败的探测项（名字 + 原因），非空表示能力表不完整。</summary>
        public readonly List<string> Failures = new List<string>();

        public bool HasFailures { get { return Failures.Count > 0; } }

        public override string ToString()
        {
            return string.Format(
                "费用色 {0} / 原版卡 {1} / 原版遗物 {2} / 关键词 {3} / 触发点 {4}{5}",
                ColorCount, CardCount, RelicCount, KeywordCount, TriggerCount,
                HasFailures ? "（失败项 " + Failures.Count + "： " + string.Join("；", Failures.ToArray()) + "）" : "");
        }
    }

    /// <summary>启动探测。所有方法都只读游戏状态，绝不写入。</summary>
    public static class GameCapabilitiesProbe
    {
        /// <summary>
        /// 触发点名表 —— **取自"游戏实际发出的名字"**（探针报告 §B-3 全库扫描结果）。
        /// </summary>
        /// <remarks>
        /// 刻意**不**引用游戏的 <c>TriggerTargetInfo.AllTriggerType</c>：
        /// 探针实测两者不一致——
        ///   · 在枚举里但从不发出：<c>ResolveCard</c> / <c>ResolveCardFinished</c> /
        ///     <c>DiscardCard</c> / <c>Passive</c> / <c>OwnerCardRelated</c> / <c>Special</c>；
        ///   · 被发出但不在枚举里：<c>HandCardCountChange</c> / <c>AcquireRelic</c> /
        ///     <c>RelicActive</c> / <c>RelicCorrupted</c> / <c>CardEffectNoCast</c> / <c>SummonResolve</c>。
        /// 用枚举做校验会让作者"写了个永远不触发的钩子"却毫无提示。
        /// </remarks>
        // 单一来源：实际发出名单归 ModKit.Core.GameFacts（Tools 层也要用，
        // 放这里会逼 lint 工具拖入 Unity 依赖）。此处只做转发，避免两处漂移。
        public static string[] ActualTriggerNames
        {
            get { return GameFacts.ActualTriggerNames; }
        }

        private static readonly ManaColor[] AllColors =
        {
            ManaColor.Red, ManaColor.Green, ManaColor.Blue,
            ManaColor.Purple, ManaColor.Void, ManaColor.Black
        };

        /// <summary>执行全部探测，返回能力表与统计。</summary>
        public static GameCapabilities Probe(IGameLog log, out ProbeSummary summary)
        {
            var summaryLocal = new ProbeSummary();
            summary = summaryLocal;

            var colors = ProbeColors(log, summaryLocal);
            var costCombos = ProbeCostCombos(log, summaryLocal);
            var cards = ProbeCardNames(log, summaryLocal);
            var relics = ProbeRelicNames(log, summaryLocal);
            var keywords = ProbeKeywordNames(log, summaryLocal);
            var triggers = new List<string>(ActualTriggerNames);

            summaryLocal.ColorCount = colors.Count;
            summaryLocal.CardCount = cards.Count;
            summaryLocal.RelicCount = relics.Count;
            summaryLocal.KeywordCount = keywords.Count;
            summaryLocal.TriggerCount = triggers.Count;

            // 只保留一条**常驻**统计：原版内容里每个关键词被用了多少次。
            // 它是《ModKit-关键词参考.md》那张表的可复现来源（日志里搜 [kwuse]），
            // 只读、开销可忽略，所以留在默认路径上。
            DumpKeywordUsage(log, summaryLocal);
            DumpEffectUsage(log, summary);
            DumpAbilityUsage(log);

            // ★ 下面两个是**按需诊断工具**，默认不调用（它们是为 2026-10-12 那轮
            //   关键词攻坚临时加的，当时会把 7 张原版卡的结构逐条打出来，日志很吵）：
            //     DumpCardStructure("<原版卡名>", log)  —— 打印某张卡的完整声明结构
            //     DumpDamageTargets(log, summary)       —— 统计全部伤害效果的目标分布
            //   需要时在下面手动加一行，跑一次即可；不要常开。

            return new GameCapabilities(colors, costCombos, cards, relics, keywords, triggers);
        }

        // ---------------------------------------------------------------------
        // 可用费用色 —— 平台最有价值的一项探测
        // ---------------------------------------------------------------------

        /// <summary>
        /// 探测**本 build 真实注册了图标键**的费用色。
        /// </summary>
        /// <remarks>
        /// 为什么这比硬编码重要：<c>ManaIconDic</c> 的键比较器是
        /// <c>ColorEArrayEqualityComparer</c>，相等判定用**有序 <c>SequenceEqual</c>**。
        /// 字典内容来自场景/prefab 的序列化字段，源码里根本读不到。
        /// 所以唯一可靠的办法就是问运行中的游戏：<c>ContainsKey(new ColorEArray(c))</c>。
        /// 用同一比较器探测，零副作用。
        /// </remarks>
        /// <summary>
        /// 平台颜色 → 游戏 <see cref="ColorE"/> 的**显式映射**。
        /// </summary>
        /// <remarks>
        /// 为什么不用强制转换：<c>ModKit.Api.ManaColor</c> 是平台自有类型，
        /// 它的数值顺序不应成为与游戏耦合的隐式契约 —— 游戏哪天调整
        /// <c>ColorE</c> 的成员顺序，强转会静默给出错误的颜色。
        /// 显式 switch 让"顺序变了"变成编译错误（找不到成员），而不是行为漂移。
        /// <para>
        /// 探针实测的游戏取值：<c>Red=0, Green=1, Blue=2, Purple=3, Void=4, Black=5</c>。
        /// </para>
        /// </remarks>
        internal static ColorE ToGameColor(ManaColor color)
        {
            switch (color)
            {
                case ManaColor.Red: return ColorE.Red;
                case ManaColor.Green: return ColorE.Green;
                case ManaColor.Blue: return ColorE.Blue;
                case ManaColor.Purple: return ColorE.Purple;
                case ManaColor.Void: return ColorE.Void;
                case ManaColor.Black: return ColorE.Black;
                default:
                    throw new ArgumentOutOfRangeException("color", color,
                        "未知的平台颜色枚举值，映射表需要更新。");
            }
        }

        private static List<ManaColor> ProbeColors(IGameLog log, ProbeSummary summary)
        {
            var result = new List<ManaColor>();

            Dictionary<ColorEArray, UnityEngine.Sprite> dict;
            try
            {
                var mgr = ManaManager.Instance;
                if (mgr == null)
                {
                    summary.Failures.Add("费用色：ManaManager.Instance 为 null（通常因为当前不在战斗世界内）");
                    return result;
                }
                dict = mgr.ManaIconDic;
            }
            catch (Exception ex)
            {
                summary.Failures.Add("费用色：取 ManaManager 失败 —— " + ex.GetType().Name);
                return result;
            }

            if (dict == null)
            {
                summary.Failures.Add("费用色：ManaIconDic 为 null");
                return result;
            }

            foreach (var c in AllColors)
            {
                try
                {
                    // 单色键：与游戏代码内部查表用的构造方式完全一致
                    // （Mana.cs:51 / ManaCost.cs:24 都用 new ColorEArray(单色)）
                    var gameColor = ToGameColor(c);
                    if (dict.ContainsKey(new ColorEArray(gameColor)))
                    {
                        result.Add(c);
                    }
                }
                catch (Exception ex)
                {
                    summary.Failures.Add("费用色 " + c + " 探测异常：" + ex.GetType().Name);
                }
            }

            if (log != null)
            {
                log.Info("费用色探测：字典共 " + dict.Count + " 个键，可用单色 " + result.Count + " / 6（" +
                    Join(result) + "）");
            }

            return result;
        }

        /// <summary>
        /// 枚举 <c>ManaIconDic</c> 的**全部键**，得到本 build 真实注册的**费用槽组合**。
        /// </summary>
        /// <remarks>
        /// ★ 这才是"多色任选"的真相：<c>Card.ColorEArray</c> 本身就是颜色数组，
        /// 一个槽可以装多个颜色（语义 = 其中任意一个都能付这一格），
        /// 而卡面图标是按**完整有序序列**查表的一张预制图。
        /// <para>
        /// ★ 为什么早先误判成"多色没有对应表达"：上一版探针只用
        /// <c>ContainsKey(new ColorEArray(单色))</c> **逐个试单色**，
        /// 看到字典有 21 个键、单色只命中 6 个，就把其余当成了别的用途。
        /// 而字典是 <c>public readonly</c> 的 —— **直接枚举键**即可，根本不用猜。
        /// </para>
        /// </remarks>
        private static List<List<ManaColor>> ProbeCostCombos(IGameLog log, ProbeSummary summary)
        {
            var result = new List<List<ManaColor>>();

            Dictionary<ColorEArray, UnityEngine.Sprite> dict;
            try
            {
                var mgr = ManaManager.Instance;
                if (mgr == null)
                {
                    summary.Failures.Add("费用槽组合：ManaManager.Instance 为 null");
                    return result;
                }
                dict = mgr.ManaIconDic;
            }
            catch (Exception ex)
            {
                summary.Failures.Add("费用槽组合：取 ManaManager 失败 —— " + ex.GetType().Name);
                return result;
            }

            if (dict == null)
            {
                summary.Failures.Add("费用槽组合：ManaIconDic 为 null");
                return result;
            }

            int skipped = 0;
            foreach (var key in dict.Keys)
            {
                try
                {
                    if (key.colorEs == null || key.colorEs.Length == 0) { skipped++; continue; }

                    var combo = new List<ManaColor>();
                    bool ok = true;
                    foreach (var c in key.colorEs)
                    {
                        ManaColor mapped;
                        if (!TryToPlatformColor(c, out mapped)) { ok = false; break; }
                        combo.Add(mapped);
                    }
                    // 出现了平台枚举里没有的颜色 → 跳过，**不装作认识**
                    if (!ok) { skipped++; continue; }

                    result.Add(combo);
                }
                catch { skipped++; }
            }

            if (log != null)
            {
                int singles = 0, mixed = 0;
                var singlesText = new List<string>();
                var mixedText = new List<string>();
                foreach (var c in result)
                {
                    var text = string.Join("+", c.ToArray());
                    if (c.Count == 1) { singles++; singlesText.Add(text); }
                    else { mixed++; mixedText.Add(text); }
                }
                log.Info("[cost-combos] 费用槽组合探测：字典 " + dict.Count + " 个键，识别出 " + result.Count +
                    " 个（跳过 " + skipped + "）｜单色 " + singles + " 个、多色混合 " + mixed + " 个");
                log.Info("[cost-combos] single: " + string.Join(" / ", singlesText.ToArray()));
                log.Info("[cost-combos] mixed : " + (mixedText.Count == 0 ? "（无）" : string.Join(" / ", mixedText.ToArray())));
                log.Info("[cost-combos] 多色槽按上面 mixed 行给出的**顺序**写，例如 mixed 里是 Red+Void 就写 [[\"Red\",\"Void\"]]（顺序参与匹配）");
            }

            return result;
        }

        /// <summary>游戏 <see cref="ColorE"/> → 平台 <see cref="ManaColor"/>；不认识返回 false（不瞎猜）。</summary>
        private static bool TryToPlatformColor(ColorE game, out ManaColor color)
        {
            switch (game)
            {
                case ColorE.Red: color = ManaColor.Red; return true;
                case ColorE.Green: color = ManaColor.Green; return true;
                case ColorE.Blue: color = ManaColor.Blue; return true;
                case ColorE.Purple: color = ManaColor.Purple; return true;
                case ColorE.Void: color = ManaColor.Void; return true;
                case ColorE.Black: color = ManaColor.Black; return true;
                default: color = ManaColor.Void; return false;
            }
        }

        // ---------------------------------------------------------------------
        // 原版卡名 / 遗物名
        // ---------------------------------------------------------------------

        /// <summary>
        /// 取原版卡名清单。
        /// </summary>
        /// <remarks>
        /// 用 <c>CardDataManager.GetAllCardsCardDateCollectionsInGame()</c> —— 这是**public**
        /// 方法，比反射私有字典 <c>_allInGameCardSoDictionary</c> 更稳（后者只作兜底）。
        /// </remarks>
        private static List<string> ProbeCardNames(IGameLog log, ProbeSummary summary)
        {
            var result = new List<string>();
            try
            {
                var mgr = CardDataManager.Instance;
                if (mgr == null)
                {
                    summary.Failures.Add("原版卡名：CardDataManager.Instance 为 null（数据尚未加载）");
                    return result;
                }

                var arr = mgr.GetAllCardsCardDateCollectionsInGame();
                if (arr == null)
                {
                    summary.Failures.Add("原版卡名：GetAllCardsCardDateCollectionsInGame() 返回 null");
                    return result;
                }

                foreach (var so in arr)
                {
                    if (so == null) { continue; }
                    if (!string.IsNullOrEmpty(so.cardName) && !result.Contains(so.cardName))
                    {
                        result.Add(so.cardName);
                    }
                }
                result.Sort(StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                summary.Failures.Add("原版卡名：探测失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }

            if (log != null) { log.Info("原版卡名探测：" + result.Count + " 张"); }
            return result;
        }

        /// <summary>
        /// 取原版遗物名清单。
        /// </summary>
        /// <remarks>
        /// 路径是 <c>DataManager.Instance.GetRelicDataManager().GetAllInGameRelicSo()</c>。
        /// 注意 <c>RelicDataManager</c> 由 <c>DataManager.LoadAllGameData()</c> 才 new 出来，
        /// 所以这个方法**必须在数据加载之后再调**（平台挂在 LoadAllGameData 的 Postfix 之后）。
        /// </remarks>
        private static List<string> ProbeRelicNames(IGameLog log, ProbeSummary summary)
        {
            var result = new List<string>();
            try
            {
                var dm = DataManager.Instance;
                if (dm == null)
                {
                    summary.Failures.Add("原版遗物名：DataManager.Instance 为 null");
                    return result;
                }

                var rdm = dm.GetRelicDataManager();
                if (rdm == null)
                {
                    summary.Failures.Add("原版遗物名：GetRelicDataManager() 为 null（LoadAllGameData 尚未执行）");
                    return result;
                }

                var arr = rdm.GetAllInGameRelicSo();
                if (arr == null)
                {
                    summary.Failures.Add("原版遗物名：GetAllInGameRelicSo() 返回 null");
                    return result;
                }

                foreach (var so in arr)
                {
                    if (so == null) { continue; }
                    var name = so.relicInfo.relicName;
                    if (!string.IsNullOrEmpty(name) && !result.Contains(name))
                    {
                        result.Add(name);
                    }
                }
                result.Sort(StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                summary.Failures.Add("原版遗物名：探测失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }

            if (log != null) { log.Info("原版遗物名探测：" + result.Count + " 件"); }
            return result;
        }

        // ---------------------------------------------------------------------
        // 关键词
        // ---------------------------------------------------------------------

        /// <summary>
        /// 取关键词名清单。
        /// </summary>
        /// <remarks>
        /// 用 <c>CardKeyWordUtil.AllKeywordStrings</c>（public static，36 项）——
        /// 这是游戏自己的权威名单，比"扫程序集里的 CardKeyword* 类名"更准
        /// （类名是文件名，而名单是注册名；两者可能不一致）。
        /// </remarks>
        /// <summary>
        /// 把某张原版卡（默认 <c>SlowSword</c>）的**完整声明结构**打进日志。
        /// </summary>
        /// <remarks>
        /// ★ 为什么需要它：想搞清楚"原版的卡牌升级到底改了哪些字段"，
        /// 靠读反编译代码已经走到死路 ——
        /// <c>UpgradeKeywordInfo</c> 只改 <c>_cardInfo</c>、不碰 <c>effectInfos</c>，
        /// 而描述渲染用的恰恰是 <c>EffectBases</c>（来自 <c>effectInfos</c>）。
        /// 那就**直接问游戏**：把那张卡装配前的原始数据原样打印出来。
        /// <para>只读，不写游戏状态。</para>
        /// </remarks>
        /// <summary>要按名字 dump 的卡（内部名精确匹配，或中文卡名包含匹配）。由插件从配置写入。</summary>
        /// <remarks>
        /// ★ 为什么需要它：卡的**中文名与内部名是两套东西** —— 中文名在本地化 CardName 表里，
        ///   而作者写 <c>artTemplate</c>、我们排查"某张卡是怎么拼出来的"时，手里往往只有中文名。
        ///   本通道把匹配到的卡按 <see cref="DumpCardStructure"/> 的格式打进日志。
        ///   配置项：<c>Diagnostics/DumpCards</c>（逗号/分号分隔，留空 = 不 dump）。
        /// </remarks>
        public static string DumpCardsSpec;

        /// <summary>按配置里的名字 dump 卡的完整结构（内部名精确匹配 或 中文名包含匹配）。</summary>
        public static void DumpCardsByName(IGameLog log)
        {
            var spec = DumpCardsSpec;
            if (log == null || string.IsNullOrEmpty(spec)) { return; }

            var wanted = new List<string>();
            foreach (var s in spec.Split(new[] { ',', ';', '，', '；' }))
            {
                var t = s.Trim();
                if (t.Length > 0) { wanted.Add(t); }
            }
            if (wanted.Count == 0) { return; }

            try
            {
                var mgr = CardDataManager.Instance;
                if (mgr == null) { log.Info("[dump] CardDataManager.Instance 为 null，跳过按名 dump"); return; }

                var all = mgr.GetAllCardsCardDateCollectionsInGame();
                log.Info("[dump] ===== 按名 dump 卡：请求 " + string.Join(",", wanted.ToArray())
                    + "｜本 build 卡 " + (all == null ? 0 : all.Length) + " 张 =====");

                int matched = 0;
                var hit = new bool[wanted.Count];
                for (int i = 0; all != null && i < all.Length; i++)
                {
                    var so = all[i];
                    if (so == null || string.IsNullOrEmpty(so.cardName)) { continue; }

                    string localized = null;
                    try
                    {
                        localized = new UnityEngine.Localization.LocalizedString("CardName", so.cardName).GetLocalizedString();
                    }
                    catch { }

                    for (int w = 0; w < wanted.Count; w++)
                    {
                        bool isHit = string.Equals(so.cardName, wanted[w], StringComparison.Ordinal)
                                  || (!string.IsNullOrEmpty(localized) && localized.IndexOf(wanted[w], StringComparison.Ordinal) >= 0);
                        if (!isHit) { continue; }

                        hit[w] = true;
                        matched++;
                        log.Info("[dump] 匹配「" + wanted[w] + "」：内部名=" + so.cardName + "｜中文名=" + D(localized));
                        DumpCardStructure(so.cardName, log);
                    }
                }

                for (int w = 0; w < wanted.Count; w++)
                {
                    if (!hit[w]) { log.Info("[dump] 未匹配到「" + wanted[w] + "」——内部名与中文名都不是它"); }
                }
                log.Info("[dump] ===== 按名 dump 结束（命中 " + matched + " 张）=====");
            }
            catch (Exception ex)
            {
                log.Info("[dump] 按名 dump 失败：" + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static void DumpCardStructure(string cardName, IGameLog log)
        {
            if (log == null) { return; }

            try
            {
                var mgr = CardDataManager.Instance;
                if (mgr == null) { log.Info("[dump] CardDataManager.Instance 为 null"); return; }

                var so = mgr.GetCardSoByName(cardName);
                if (so == null) { log.Info("[dump] 找不到卡：" + cardName); return; }

                var info = so.CardInfo;

                log.Info("[dump] ===== " + cardName + " 的声明结构 =====");
                log.Info("[dump] cardName=" + info.cardName
                    + " rarity=" + info.rarity
                    + " colors=" + Len(info.cardColorEs)
                    + " costSlots=" + Len(info.cardManaCostColorEArrays));

                // ---- 卡牌自己的效果 ----
                var effects = info.cardEffectInfos;
                log.Info("[dump] cardEffectInfos: " + Len(effects));
                if (effects != null)
                {
                    for (int i = 0; i < effects.Length; i++)
                    {
                        var ce = effects[i];
                        var ei = ce.effectInfo;
                        log.Info("[dump]   [" + i + "] resolve=" + ce.cardEffectResolveType
                            + " effectName=" + D(ei == null ? null : ei.effectName)
                            + " ability=" + D(ei == null ? null : ei.abilityName)
                            + " target=" + (ei == null ? "-" : ei.effectTargetEnum.ToString()) + " numbers=" + Numbers(ei)
                            + DescribeConfigs(ei));
                    }
                }

                // ---- 关键词（重点）----
                var kws = info.cardKeyWordInfos;
                log.Info("[dump] cardKeyWordInfos: " + Len(kws));
                if (kws != null)
                {
                    for (int i = 0; i < kws.Length; i++)
                    {
                        var kw = kws[i];
                        var kwCard = kw.GetCardInfo();
                        string kwLocalized = null;
                        try { kwLocalized = Keyword.CardKeyWordUtil.GetLocalizedKeywordName(kw); } catch { }
                        log.Info("[dump]   [" + i + "] name=" + D(kw.keywordName)
                            + " 中文名=" + D(kwLocalized)
                            + " number=" + kw.number
                            + " keywordCardSoName=" + D(kw.keywordCardSoName)
                            + " innerCardName=" + D(kwCard.cardName) + " mana=" + D(kw.manaString) + " colorE=" + kw.colorE.ToString()
                            + " effectInfos=" + Len(kw.effectInfos));

                        if (kw.effectInfos != null)
                        {
                            for (int j = 0; j < kw.effectInfos.Length; j++)
                            {
                                var e = kw.effectInfos[j];
                                log.Info("[dump]       (" + j + ") effectName=" + D(e == null ? null : e.effectName)
                                    + " resolve=" + (e == null ? "-" : e.cardEffectResolveType.ToString())
                                    + " ability=" + D(e == null ? null : e.abilityName)
                                    + " target=" + (e == null ? "-" : e.effectTargetEnum.ToString()) + " numbers=" + Numbers(e)
                                    + DescribeConfigs(e));
                            }
                        }
                    }
                }

                // ---- 升级数据（最想知道的部分）----
                var up = info.cardUpgradeInfo;
                log.Info("[dump] cardUpgradeInfo: color=" + up.changeCardColor
                    + " cost=" + up.changeCardCostMana
                    + " addKw=" + up.addCardKeyword
                    + " rmKw=" + up.removeCardKeyword
                    + " changeEffect=" + up.changeEffect
                    + " insertEffect=" + up.insertEffect
                    + " changeEffectNumber=" + up.changeEffectNumber
                    + " changeKwNumber=" + up.changeKeywordNumber
                    + " isUpgraded=" + up.isUpgraded);
                if (up.changeEffectNumber && up.effectNumberChangeInfo != null)
                {
                    for (int i = 0; i < up.effectNumberChangeInfo.Length; i++)
                    {
                        var c = up.effectNumberChangeInfo[i];
                        log.Info("[dump]   effectNumberChange[" + i + "] effectIndex=" + c.effectIndex
                            + " numbers=" + Len(c.rNumberInfos));
                    }
                }
                if (up.changeKeywordNumber)
                {
                    log.Info("[dump]   keywordNumber: name=" + D(up.keyWordNumberChangeInfo.keywordName)
                        + " number=" + up.keyWordNumberChangeInfo.keywordNumber);
                }
                log.Info("[dump] ===== 结束 =====");
            }
            catch (Exception ex)
            {
                log.Info("[dump] 失败：" + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // =====================================================================
        //  遗物结构按名 dump（Diagnostics/DumpRelics）
        //
        //  ★ 为什么需要它：卡有 DumpCardStructure 这条通道，**遗物此前一条都没有**
        //    （全仓 DumpRelic / RelicStructure 0 命中）⇒ 原版遗物的真实参数
        //    （relicMainType / requiredChargeCount / 触发字段 / 效果数字）只能靠读
        //    反编译代码猜 —— 而它们是**资产里的序列化值**，源码里根本读不到。
        //
        //  ★ 与 DumpCards 完全同构：同样的匹配口径、同样的"整体 try/catch + 单件失败只记一行"。
        //    只读：不写任何游戏数据、不碰存档。
        // =====================================================================

        /// <summary>
        /// 要按名字 dump 的遗物（内部名精确匹配 或 中文名包含匹配；<c>*</c> = dump 全部原版遗物）。
        /// 由插件从配置 <c>Diagnostics/DumpRelics</c> 写入；留空 = 不 dump。
        /// </summary>
        /// <remarks>
        /// ★ 为什么中文名匹配也要支持：遗物的**内部名与中文名是两套东西** ——
        ///   中文名在本地化表 <c>Relic</c> 里（<c>RelicBase.GetOwnerLocalizedName</c>：
        ///   <c>new LocalizedString("Relic", RelicName)</c>）。调试时手里往往只有中文名。
        /// </remarks>
        public static string DumpRelicsSpec;

        /// <summary>
        /// 按配置里的名字 dump 遗物的完整声明结构。
        /// </summary>
        /// <param name="log">日志口。</param>
        /// <param name="phase">
        /// 可选阶段标签（"装配前" / "装配后"）。同一份日志里会有两次 dump，标签用来区分
        /// 「原版遗物的真实结构」与「平台装配后的读回」。
        /// </param>
        public static void DumpRelicsByName(IGameLog log, string phase = null)
        {
            var spec = DumpRelicsSpec;
            if (log == null || string.IsNullOrEmpty(spec)) { return; }

            bool wantAll = false;
            var wanted = new List<string>();
            foreach (var s in spec.Split(new[] { ',', ';', '，', '；' }))
            {
                var t = s.Trim();
                if (t.Length == 0) { continue; }
                if (t == "*") { wantAll = true; continue; }
                wanted.Add(t);
            }
            if (!wantAll && wanted.Count == 0) { return; }

            string tag = string.IsNullOrEmpty(phase) ? "" : ("｜阶段=" + phase);

            try
            {
                var dm = DataManager.Instance;
                if (dm == null) { log.Info("[relic] DataManager.Instance 为 null，跳过按名 dump" + tag); return; }

                var rdm = dm.GetRelicDataManager();
                if (rdm == null)
                {
                    log.Info("[relic] GetRelicDataManager() 为 null（LoadAllGameData 尚未执行），跳过按名 dump" + tag);
                    return;
                }

                // ---- 枚举来源①（权威）：游戏自己的入口 ----
                // RelicDataManager.LoadAllRelic() 把**每个 RelicDataCollection.relicSos** 收进
                // _allInGameRelicSo（TryAdd，重名只留第一件），GetAllInGameRelicSo() 就是它的 Values。
                // 所以这是"游戏认得的全部遗物"，不需要我们去猜容器。
                Relic.RelicSo[] all = null;
                string source = "GetAllInGameRelicSo()";
                try
                {
                    all = rdm.GetAllInGameRelicSo();
                }
                catch (Exception ex)
                {
                    log.Info("[relic] GetAllInGameRelicSo() 失败：" + ex.GetType().Name + ": " + ex.Message);
                }

                // ---- 池归属来源：RelicDataManager._allRelicDataCollections（private，只读反射）----
                // 该字段没有 public 取值口，但它是"这件遗物属于哪个池/哪个稀有度"的唯一来源。
                // 反射失败只记一行，不影响 dump 主体（池归属打成"(未知)"）。
                List<Relic.RelicDataCollection> collections = null;
                try
                {
                    collections = Reflect.Get<List<Relic.RelicDataCollection>>(rdm, "_allRelicDataCollections");
                }
                catch (Exception ex)
                {
                    log.Info("[relic] _allRelicDataCollections 取不到（池归属未知）：" +
                        ex.GetType().Name + ": " + ex.Message);
                }

                // ---- 枚举来源②（退化）：拿不到全集时，用「战役池 + 各 collection.relicSos」取并集 ----
                // 触发条件：GetAllInGameRelicSo() 返回 null / 空（例如 RelicDataManager 只被部分初始化）。
                if (all == null || all.Length == 0)
                {
                    source = "退化并集（战役池 + 各 collection.relicSos）";
                    var byName = new Dictionary<string, Relic.RelicSo>(StringComparer.Ordinal);
                    AddRelics(byName, SafeCampaignRelicPool(rdm));
                    if (collections != null)
                    {
                        for (int i = 0; i < collections.Count; i++)
                        {
                            var c = collections[i];
                            if (c == null) { continue; }
                            AddRelics(byName, c.relicSos);
                        }
                    }
                    all = new List<Relic.RelicSo>(byName.Values).ToArray();
                }

                log.Info("[relic] ===== 按名 dump 遗物：请求 " +
                    (wantAll ? "*" : string.Join(",", wanted.ToArray())) +
                    "｜枚举来源 " + source +
                    "｜本 build 遗物 " + (all == null ? 0 : all.Length) + " 件" + tag + " =====");

                int matched = 0;
                var hit = new bool[wanted.Count];
                for (int i = 0; all != null && i < all.Length; i++)
                {
                    // ★ 单件失败只记一行并继续 —— 废弃/半初始化的 SO 不能让整条 dump 断掉。
                    try
                    {
                        var so = all[i];
                        if (so == null) { continue; }

                        var info = so.relicInfo;
                        if (info == null || string.IsNullOrEmpty(info.relicName)) { continue; }

                        int which = -1;
                        if (!wantAll)
                        {
                            string localized = LocalizedRelicName(info.relicName);
                            for (int w = 0; w < wanted.Count; w++)
                            {
                                bool isHit = string.Equals(info.relicName, wanted[w], StringComparison.Ordinal)
                                          || (!string.IsNullOrEmpty(localized) &&
                                              localized.IndexOf(wanted[w], StringComparison.Ordinal) >= 0);
                                if (isHit) { which = w; break; }
                            }
                            if (which < 0) { continue; }
                            hit[which] = true;
                        }

                        matched++;
                        if (which >= 0)
                        {
                            log.Info("[relic] 匹配「" + wanted[which] + "」：内部名=" + info.relicName +
                                "｜中文名=" + D(LocalizedRelicName(info.relicName)));
                        }
                        DumpRelicStructure(so, collections, log);
                    }
                    catch (Exception ex)
                    {
                        log.Info("[relic] 第 " + i + " 件遗物处理失败（已跳过）：" +
                            ex.GetType().Name + ": " + ex.Message);
                    }
                }

                for (int w = 0; w < wanted.Count; w++)
                {
                    if (!hit[w]) { log.Info("[relic] 未匹配到「" + wanted[w] + "」——内部名与中文名都不是它"); }
                }
                log.Info("[relic] ===== 按名 dump 结束（命中 " + matched + " 件）" + tag + " =====");
            }
            catch (Exception ex)
            {
                log.Info("[relic] 按名 dump 失败（不影响内容）：" + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// dump 一件遗物的两段：**一行摘要**（便于一眼看分布）+ **详细块**。
        /// </summary>
        /// <remarks>
        /// 整体 try/catch：任何反射/字段取值失败都只变成一行日志，绝不向上抛（更不崩游戏）。
        /// </remarks>
        private static void DumpRelicStructure(Relic.RelicSo so,
            List<Relic.RelicDataCollection> collections, IGameLog log)
        {
            if (so == null || log == null) { return; }

            string name = "(未命名)";
            try { if (so.relicInfo != null) { name = so.relicInfo.relicName; } }
            catch { }

            try
            {
                var info = so.relicInfo;
                if (info == null) { log.Info("[relic] " + name + "：relicInfo 为 null，跳过"); return; }

                var main = info.mainEffectInfo;
                bool typeExists = RelicEffectClassExists(info.relicName);

                // ---------------- 第 1 段：一行摘要 ----------------
                log.Info("[relic] " + D(info.relicName)
                    + " | special=" + info.isSpecialRelic
                    + " | mainType=" + info.relicMainType
                    + " | rarity=" + info.relicRarity
                    + " | colors=" + Len(info.relicColors) + "[" + NamesOf(info.relicColors) + "]"
                    + " | charge=" + info.requiredChargeCount + "/" + info.chargeCount
                    + " | trigLimit=" + (info.hasTriggerLimit
                        ? (info.triggerLimitType + ":" + info.triggerLimit) : "none")
                    + " | effects=" + Len(main == null ? null : main.effectInfo)
                        + "+" + Len(info.extraRelicEffectInfos)
                    + " | pool=" + PoolLabel(info.relicName, collections));

                // ---------------- 第 2 段：详细块 ----------------
                log.Info("[relic] ===== " + D(info.relicName) + " 的声明结构 =====");
                log.Info("[relic]   [RelicSo] relicIcon=" +
                    (so.relicIcon == null ? "null" : so.relicIcon.name) +
                    "｜animationInfo(struct) isTargeted=" + so.animationInfo.isTargeted +
                    " cardAnimationType=" + so.animationInfo.cardAnimationType +
                    " standardAnimationPaths=" + (so.animationInfo.standardAnimationPaths == null
                        ? -1 : so.animationInfo.standardAnimationPaths.Count) +
                    " extraAnimationPaths=" + (so.animationInfo.extraAnimationPaths == null
                        ? -1 : so.animationInfo.extraAnimationPaths.Count));

                log.Info("[relic]   [RelicInfo] 全字段（布尔/枚举/null 都显式打出来）：");
                log.Info("[relic]     relicName=" + D(info.relicName));
                log.Info("[relic]     isSpecialRelic=" + info.isSpecialRelic);
                log.Info("[relic]     useSpecialDesKey=" + info.useSpecialDesKey);
                log.Info("[relic]     relicColors=" + Len(info.relicColors) + "[" + NamesOf(info.relicColors) + "]");
                log.Info("[relic]     relicRarity=" + info.relicRarity);
                log.Info("[relic]     relicMainType=" + info.relicMainType);
                log.Info("[relic]     requiredChargeCount=" + info.requiredChargeCount);
                log.Info("[relic]     canBeActiveNotInBattle=" + info.canBeActiveNotInBattle);
                log.Info("[relic]     mainEffectInfo=" + (main == null ? "null" : "非 null"));
                log.Info("[relic]     acquired=" + info.acquired);
                log.Info("[relic]     extraRelicEffectInfos=" + Len(info.extraRelicEffectInfos));
                log.Info("[relic]     hasTriggerLimit=" + info.hasTriggerLimit);
                log.Info("[relic]     noTriggerAfterFullCharge=" + info.noTriggerAfterFullCharge);
                log.Info("[relic]     triggerLimitType=" + info.triggerLimitType);
                log.Info("[relic]     triggerLimit=" + info.triggerLimit);
                log.Info("[relic]     chargeCount=" + info.chargeCount);
                log.Info("[relic]     canChargeBeyondRequirement=" + info.canChargeBeyondRequirement);
                log.Info("[relic]     extraTooltip: encyclopediaKeys=" + Len(info.extraTooltip.encyclopediaKeys) +
                    "[" + NamesOf(info.extraTooltip.encyclopediaKeys) + "]" +
                    " abilityKeys=" + Len(info.extraTooltip.abilityKeys) +
                    "[" + NamesOf(info.extraTooltip.abilityKeys) + "]" +
                    " hasFlavorText=" + info.extraTooltip.hasFlavorText +
                    " extraCardsForPreview=" + Len(info.extraTooltip.extraCardsForPreview));

                if (main != null)
                {
                    DumpTriggerTargetInfo(main.triggerTargetInfo, "mainEffectInfo", log);
                    DumpEffectInfos(main.effectInfo, "mainEffectInfo.effectInfo", log);
                }

                for (int i = 0; info.extraRelicEffectInfos != null && i < info.extraRelicEffectInfos.Length; i++)
                {
                    var extra = info.extraRelicEffectInfos[i];
                    string label = "extraRelicEffectInfos[" + i + "]";
                    if (extra == null) { log.Info("[relic]   " + label + "=null"); continue; }
                    DumpTriggerTargetInfo(extra.triggerTargetInfo, label, log);
                    DumpEffectInfos(extra.effectInfo, label + ".effectInfo", log);
                }

                // -------- 额外判定 ①：特殊遗物行为类是否存在 --------
                // 游戏的两条路径（RelicManager.cs:231 / RelicBase.cs:574）：
                //   isSpecialRelic=false ⇒ Type.GetType("Relic.RelicEffects.RelicBase")（一定存在）
                //   isSpecialRelic=true  ⇒ Type.GetType("Relic.RelicEffects.Relic" + relicName)
                //   后者返回 null 时 Activator.CreateInstance 抛 ArgumentNullException（实机=点开就崩）。
                string runtime;
                if (info.isSpecialRelic)
                {
                    runtime = typeExists
                        ? "Relic<名字>（特殊遗物，走该类）"
                        : "Type.GetType 返回 null ⇒ Activator.CreateInstance 抛 ArgumentNullException";
                }
                else
                {
                    runtime = "RelicBase（普通/数据驱动，行为由 mainEffectInfo 决定）";
                }
                log.Info("[relic]   判定①行为类：Relic.RelicEffects.Relic" + D(info.relicName) +
                    " 存在=" + typeExists +
                    "｜isSpecialRelic=" + info.isSpecialRelic +
                    "｜运行期实际使用=" + runtime);
                if (info.isSpecialRelic != typeExists)
                {
                    log.Info("[relic]   ⚠ 判定①不一致：isSpecialRelic=" + info.isSpecialRelic +
                        " 而类型存在=" + typeExists +
                        "（isSpecialRelic=true 且类型不存在 = 运行时必崩；反之为死类，无影响）");
                }

                // -------- 额外判定 ②：属于哪些 RelicDataCollection --------
                DumpRelicCollections(info.relicName, collections, log);
            }
            catch (Exception ex)
            {
                log.Info("[relic] " + name + " 的 dump 失败（已跳过）：" + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// 把 <c>TriggerTargetInfo</c> 的 **11 个字段全部**打出来。
        /// </summary>
        /// <remarks>
        /// ★ 重点是能一眼看出 <c>battlePhase</c> / <c>certainTurnStartInfo</c> 这两个
        ///   **class 字段是否非空** —— 它们为 null 时
        ///   <c>TriggerManager.CheckTriggerFitTargetTrigger</c> 直接 <c>NullReferenceException</c>（崩游戏）。
        ///   其余字段（struct / enum / string）留空只是"语义变宽"，不崩。
        /// </remarks>
        private static void DumpTriggerTargetInfo(Trigger.TriggerTargetInfo t, string label, IGameLog log)
        {
            log.Info("[relic]   " + label + ".triggerTargetInfo（11 字段全打）：");
            log.Info("[relic]     [1/11] triggerType=" + D(t.triggerType));
            log.Info("[relic]     [2/11] ownerCharacter=" +
                (t.ownerCharacter == null ? "null" : t.ownerCharacter.name));
            log.Info("[relic]     [3/11] worldPhaseInfo(struct) worldPhase=" + t.worldPhaseInfo.worldPhase +
                " phaseLogic=" + t.worldPhaseInfo.phaseLogic);
            log.Info("[relic]     [4/11] battlePhase(class)=" + DescribeBattlePhase(t.battlePhase));
            log.Info("[relic]     [5/11] cardCoreInfoCheck(struct)=" + DescribeCardCoreInfoCheck(t.cardCoreInfoCheck));
            log.Info("[relic]     [6/11] relativeCharacterType=" + t.relativeCharacterType);
            log.Info("[relic]     [7/11] certainTurnStartInfo(class)=" +
                DescribeCertainTurnStartInfo(t.certainTurnStartInfo));
            log.Info("[relic]     [8/11] colorE=" + t.colorE);
            log.Info("[relic]     [9/11] extraString=" + D(t.extraString));
            log.Info("[relic]     [10/11] requireSuccess=" + t.requireSuccess);
            log.Info("[relic]     [11/11] SpecialExtraCondition(delegate)=" +
                (t.SpecialExtraCondition == null ? "null" : "非 null（运行时委托）"));
            log.Info("[relic]     class 字段判定（为 null 会 NRE 崩游戏）：battlePhase=" +
                (t.battlePhase == null ? "null ⇒ 崩" : "非 null") +
                "｜certainTurnStartInfo=" + (t.certainTurnStartInfo == null ? "null ⇒ 崩" : "非 null"));
        }

        private static string DescribeBattlePhase(Combat.BattleWorld.BattlePhaseRequirement r)
        {
            if (r == null) { return "null"; }
            return "battlePhase=" + r.battlePhase + " isStart=" + r.isStart;
        }

        private static string DescribeCertainTurnStartInfo(Combat.BattleWorld.CertainTurnStartInfo r)
        {
            if (r == null) { return "null"; }
            return "requireCertainTurn=" + r.requireCertainTurn + " turnCount=" + r.turnCount +
                " certainTurnRelationInfo=" + r.certainTurnRelationInfo;
        }

        private static string DescribeCardCoreInfoCheck(Card.CardCoreInfoCheck c)
        {
            return "describe=" + D(c.requireDescribeKey) +
                " hasColor=" + c.requireHasCardColorE + "[" + NamesOf(c.requiredColorE) + "]" +
                " noColor=" + c.requireNoCardColorE + "[" + NamesOf(c.requiredNoColorE) + "]" +
                " hasKeyword=" + c.requireHasCardKeyword + " kw=" + D(c.requiredKeyword) +
                " rarity=" + c.requireRarity + "[" + NamesOf(c.requiredRarities) + "]" +
                " special=" + D(c.specialRequirement) +
                " targetCheck=" + c.selectedTargetCheckType;
        }

        /// <summary>打一个效果数组的每条（effectName / abilityName / target / numbers / additionalString）。</summary>
        private static void DumpEffectInfos(EffectInfo[] arr, string label, IGameLog log)
        {
            log.Info("[relic]   " + label + ": " + Len(arr));
            if (arr == null) { return; }
            for (int i = 0; i < arr.Length; i++)
            {
                var e = arr[i];
                if (e == null) { log.Info("[relic]     [" + i + "]=null"); continue; }
                log.Info("[relic]     [" + i + "] effectName=" + D(e.effectName) +
                    " abilityName=" + D(e.abilityName) +
                    " effectTargetEnum=" + e.effectTargetEnum +
                    " numbers=" + Numbers(e) +
                    " additionalString=" + D(e.additionalString) +
                    DescribeConfigs(e));
            }
        }

        /// <summary>该遗物落在哪些 <c>RelicDataCollection</c> 里（标签 = 类别 + 解锁名）。</summary>
        private static string PoolLabel(string relicName, List<Relic.RelicDataCollection> collections)
        {
            if (collections == null) { return "(未知：collections 取不到)"; }

            var parts = new List<string>();
            for (int i = 0; i < collections.Count; i++)
            {
                var c = collections[i];
                if (c == null || c.relicSos == null) { continue; }
                for (int j = 0; j < c.relicSos.Length; j++)
                {
                    var r = c.relicSos[j];
                    if (r == null || r.relicInfo == null) { continue; }
                    if (!string.Equals(r.relicInfo.relicName, relicName, StringComparison.Ordinal)) { continue; }
                    string label = c.relicDataCollectionType.ToString() +
                        (string.IsNullOrEmpty(c.unlockUpgradeName) ? "" : "(" + c.unlockUpgradeName + ")");
                    if (!parts.Contains(label)) { parts.Add(label); }
                    break;
                }
            }
            return parts.Count == 0 ? "(不在任何 collection 里)" : string.Join("+", parts.ToArray());
        }

        /// <summary>打印该遗物所属的每个 collection 的全字段（含 requireUnlock / rarity）。</summary>
        private static void DumpRelicCollections(string relicName,
            List<Relic.RelicDataCollection> collections, IGameLog log)
        {
            if (collections == null)
            {
                log.Info("[relic]   判定②所属 collection：_allRelicDataCollections 取不到，无法判定");
                return;
            }

            int found = 0;
            for (int i = 0; i < collections.Count; i++)
            {
                var c = collections[i];
                if (c == null || c.relicSos == null) { continue; }

                bool contains = false;
                for (int j = 0; j < c.relicSos.Length; j++)
                {
                    var r = c.relicSos[j];
                    if (r != null && r.relicInfo != null &&
                        string.Equals(r.relicInfo.relicName, relicName, StringComparison.Ordinal))
                    {
                        contains = true;
                        break;
                    }
                }
                if (!contains) { continue; }

                found++;
                log.Info("[relic]   判定②所属 collection[" + i + "]：type=" + c.relicDataCollectionType +
                    " requireUnlock=" + c.requireUnlock +
                    " unlockUpgradeName=" + D(c.unlockUpgradeName) +
                    " rarity=" + c.rarity +
                    " colorEs=" + Len(c.colorEs) + "[" + NamesOf(c.colorEs) + "]" +
                    " relicSos=" + Len(c.relicSos));
            }

            if (found == 0)
            {
                log.Info("[relic]   判定②所属 collection：不在任何 RelicDataCollection 的 relicSos 里");
            }
        }

        /// <summary>
        /// 判断 <c>Relic.RelicEffects.Relic&lt;遗物名&gt;</c> 这个类型是否真的存在。
        /// </summary>
        /// <remarks>
        /// ★ **不能用 <c>Type.GetType(名字)</c>**：它**不跨程序集** —— 本文件在 ModKit.Game 里，
        ///   目标类型在 Assembly-CSharp 里，直接调必然返回 null（假阴性，会误导判断）。
        ///   游戏自己能在 <c>RelicManager.cs:231</c> / <c>RelicBase.cs:574</c> 用 <c>Type.GetType</c>，
        ///   是因为那两处**就在 Assembly-CSharp 内部**。这里显式去 Assembly-CSharp 里查，语义等价。
        /// </remarks>
        private static bool RelicEffectClassExists(string relicName)
        {
            try
            {
                if (string.IsNullOrEmpty(relicName)) { return false; }
                return typeof(Relic.RelicEffects.RelicBase).Assembly.GetType(
                    "Relic.RelicEffects.Relic" + relicName, false) != null;
            }
            catch { return false; }
        }

        /// <summary>遗物显示名（本地化表 <c>Relic</c>，键 = 内部名）。失败返回 null，绝不抛。</summary>
        private static string LocalizedRelicName(string relicName)
        {
            try
            {
                return new UnityEngine.Localization.LocalizedString("Relic", relicName).GetLocalizedString();
            }
            catch { return null; }
        }

        /// <summary>把一组遗物按内部名并进字典（退化枚举路径用；重名只留第一件，与游戏 TryAdd 同口径）。</summary>
        private static void AddRelics(Dictionary<string, Relic.RelicSo> byName, Relic.RelicSo[] arr)
        {
            if (arr == null) { return; }
            for (int i = 0; i < arr.Length; i++)
            {
                var so = arr[i];
                if (so == null || so.relicInfo == null || string.IsNullOrEmpty(so.relicInfo.relicName)) { continue; }
                if (!byName.ContainsKey(so.relicInfo.relicName)) { byName[so.relicInfo.relicName] = so; }
            }
        }

        /// <summary>取战役遗物池；失败返回 null（它只在战役里被 <c>SetCampaignRelicPool</c> 填充）。</summary>
        private static Relic.RelicSo[] SafeCampaignRelicPool(RelicDataManager rdm)
        {
            try { return rdm.GetCampaignRelicPool(); }
            catch { return null; }
        }

        private static string D(string s) { return string.IsNullOrEmpty(s) ? "(空)" : s; }

        /// <summary>
        /// 打印效果上的"配置对象与附加字段"。
        /// </summary>
        /// <remarks>
        /// ★ 为什么必须单独打：有些效果的**行为全在配置对象里** ——
        /// <c>SeekCard</c> 全看 <c>cardCoreInfoCheck</c>（11 个字段的卡牌条件），
        /// <c>AddRandomCard</c> 全看 <c>genRandomCardInfoPack</c>（带概率的卡池）。
        /// 不打它们，dump 只剩一句 <c>effectName=SeekCard</c>，等于没看到实现。
        /// <para>
        /// 消费点（读源码确认）：
        /// <c>SubCommandSeekCard.ResolveSubCommand</c> 用 <c>CardUtils.CheckCardCoreInfoFitCheck</c>
        /// 过滤**牌库**，按 <c>WorldRandomSeedUtil.GetMapNodeUniqueSeed()</c> 洗牌后抽 N 张；
        /// <c>LootManager.GenRandomCardByProbabilityPool</c> 按
        /// <c>ElementPoolWithProbability&lt;GenRandomCardInfo&gt;</c> 的 <c>probability</c> 加权抽样。
        /// </para>
        /// </remarks>
        private static string DescribeConfigs(EffectInfo ei)
        {
            if (ei == null) { return ""; }
            string s = "";

            // ---- SeekCard 的条件对象（struct：全默认 = 任意卡都匹配）----
            var chk = ei.cardCoreInfoCheck;
            bool chkIsDefault = string.IsNullOrEmpty(chk.requireDescribeKey)
                && !chk.requireHasCardColorE && (chk.requiredColorE == null || chk.requiredColorE.Length == 0)
                && !chk.requireNoCardColorE && (chk.requiredNoColorE == null || chk.requiredNoColorE.Length == 0)
                && !chk.requireHasCardKeyword && string.IsNullOrEmpty(chk.requiredKeyword)
                && string.IsNullOrEmpty(chk.specialRequirement)
                && !chk.requireRarity && (chk.requiredRarities == null || chk.requiredRarities.Length == 0);
            if (!chkIsDefault)
            {
                s += " check{ describe=" + D(chk.requireDescribeKey)
                    + " hasColor=" + chk.requireHasCardColorE + "[" + NamesOf(chk.requiredColorE) + "]"
                    + " noColor=" + chk.requireNoCardColorE + "[" + NamesOf(chk.requiredNoColorE) + "]"
                    + " hasKeyword=" + chk.requireHasCardKeyword + " kw=" + D(chk.requiredKeyword)
                    + " special=" + D(chk.specialRequirement)
                    + " rarity=" + chk.requireRarity + "[" + NamesOf(chk.requiredRarities) + "]"
                    + " targetCheck=" + chk.selectedTargetCheckType + " }";
            }

            // ---- AddRandomCard 的卡池对象（struct）----
            var pack = ei.genRandomCardInfoPack;
            var pool = pack.genRandomCardInfoWithProbability;
            bool hasPool = pool != null && pool.elementWithProbability != null && pool.elementWithProbability.Length > 0;
            if (!string.IsNullOrEmpty(pack.packName) || hasPool)
            {
                s += " pack{ name=" + D(pack.packName);
                if (hasPool)
                {
                    for (int i = 0; i < pool.elementWithProbability.Length; i++)
                    {
                        var ew = pool.elementWithProbability[i];
                        var info = ew == null ? null : ew.element;
                        s += " | p=" + (ew == null ? -1 : ew.probability)
                            + " pool=" + (info == null ? "-" : info.cardPool.ToString())
                            + " specialPool=" + D(info == null ? null : info.specialPoolName)
                            + " specific=[" + (info == null ? "" : CardNames(info.specificPool)) + "]";
                        if (info != null) { s += DescribeRequirement(info.cardRequirementCheckInfo); }
                    }
                }
                s += " }";
            }

            // ---- 其余附加字段（有的语义全靠它们，空值不打以保持紧凑）----
            if (!string.IsNullOrEmpty(ei.cardName)) { s += " cardName=" + ei.cardName; }
            if (!string.IsNullOrEmpty(ei.specialEffectString)) { s += " spStr=" + ei.specialEffectString; }
            if (!string.IsNullOrEmpty(ei.manaString)) { s += " mana=" + ei.manaString; }
            if (!string.IsNullOrEmpty(ei.additionalString)) { s += " addStr=" + ei.additionalString; }
            if (!string.IsNullOrEmpty(ei.cardSmallGameName)) { s += " smallGame=" + ei.cardSmallGameName; }
            if (!ei.isPositive) { s += " isPositive=false"; }
            if (ei.extraDelay != 0) { s += " extraDelay=" + ei.extraDelay; }
            return s;
        }

        /// <summary>卡池条目自己的条件（<c>GenRandomCardInfo.cardRequirementCheckInfo</c>），只打非默认项。</summary>
        private static string DescribeRequirement(Card.CardRequirementCheckInfo r)
        {
            string s = "";
            if (r.acceptedCardRarity != null && r.acceptedCardRarity.Length > 0) { s += " rar=[" + NamesOf(r.acceptedCardRarity) + "]"; }
            if (r.includeColor != null && r.includeColor.Length > 0)
            {
                s += " inc=[" + NamesOf(r.includeColor) + "] only=" + r.isOnlyHaveIncludedColor + " all=" + r.isIncludeAllIncludedColor;
            }
            if (r.excludeColor != null && r.excludeColor.Length > 0) { s += " exc=[" + NamesOf(r.excludeColor) + "]"; }
            if (r.isSingleColor) { s += " single"; }
            if (r.isMultiColor) { s += " multi"; }
            if (!string.IsNullOrEmpty(r.specialRequirement)) { s += " special=" + r.specialRequirement; }
            if (!string.IsNullOrEmpty(r.requirementDescribeKey)) { s += " desKey=" + r.requirementDescribeKey; }
            if (r.requireHasCardKeyword) { s += " kw=" + r.requiredKeyword; }
            if (!string.IsNullOrEmpty(r.CantBeCertainCardName)) { s += " cantBe=" + r.CantBeCertainCardName; }
            return s.Length == 0 ? "" : "{" + s + " }";
        }

        private static string NamesOf<T>(T[] a)
        {
            if (a == null) { return ""; }
            var parts = new string[a.Length];
            for (int i = 0; i < a.Length; i++) { parts[i] = a[i] == null ? "(null)" : a[i].ToString(); }
            return string.Join(",", parts);
        }

        private static string CardNames(Card.CardSo[] a)
        {
            if (a == null) { return ""; }
            var parts = new string[a.Length];
            for (int i = 0; i < a.Length; i++) { parts[i] = a[i] == null ? "(null)" : a[i].cardName; }
            return string.Join(",", parts);
        }
        private static int Len<T>(T[] a) { return a == null ? -1 : a.Length; }

        private static string Numbers(EffectInfo e)
        {
            if (e == null || e.effectNumberInfos == null) { return "-"; }
            var parts = new List<string>();
            foreach (var n in e.effectNumberInfos)
            {
                // ★ 不要用 n.ToString()：RNumberInfo 没重写 ToString，打出来是类型名
                //   （2026-10-13 实测踩到：dump 里显示 "numbers=Card.RNumberInfo"，看不到数字）。
                parts.Add(n.isDiceNumber
                    ? "dice" + (n.diceInfos == null ? "[]" : "x" + n.diceInfos.Length)
                    : n.oriNumber.ToString());
            }
            return parts.Count == 0 ? "(无)" : string.Join(",", parts.ToArray());
        }

        /// <summary>
        /// 统计**全部原版卡**里伤害效果的目标分布。
        /// </summary>
        /// <remarks>
        /// ★ 用来回答"原版到底有没有显式写 target"。
        /// 资源是二进制，读不到文本；但可以统计：
        /// 如果原版的伤害效果里几乎没有 <c>Self</c>（剩下的才是明确的自伤卡），
        /// 就说明设计者是**逐条设过**的，而不是依赖字段默认值 ——
        /// 那平台强制显式 target 就是对的做法。
        /// <para>只读，不写游戏状态。</para>
        /// </remarks>
        private static void DumpDamageTargets(IGameLog log, ProbeSummary summary)
        {
            if (log == null) { return; }
            try
            {
                var mgr = CardDataManager.Instance;
                if (mgr == null) { return; }

                int total = 0, self = 0, opponent = 0, allOpp = 0, all = 0, randomOpp = 0, other = 0;
                int cardsWithDamage = 0;

                foreach (var name in ProbeCardNames(null, summary))
                {
                    var so = mgr.GetCardSoByName(name);
                    if (so == null) { continue; }

                    var effects = so.CardInfo.cardEffectInfos;
                    if (effects == null) { continue; }

                    bool hasDamage = false;
                    foreach (var ce in effects)
                    {
                        var ei = ce.effectInfo;
                        if (ei == null || ei.effectName != "DealDamage") { continue; }

                        hasDamage = true;
                        total++;
                        switch (ei.effectTargetEnum)
                        {
                            case EffectTargetEnum.Self: self++; break;
                            case EffectTargetEnum.TargetOpponent: opponent++; break;
                            case EffectTargetEnum.AllOpponents: allOpp++; break;
                            case EffectTargetEnum.All: all++; break;
                            case EffectTargetEnum.RandomOpponent: randomOpp++; break;
                            default: other++; break;
                        }
                    }
                    if (hasDamage) { cardsWithDamage++; }
                }

                log.Info("[dump] 全原版卡伤害目标分布：伤害效果 " + total + " 个 / 涉及 " +
                    cardsWithDamage + " 张卡");
                log.Info("[dump]   Self(打自己)=" + self + "｜TargetOpponent=" + opponent +
                    "｜AllOpponents=" + allOpp + "｜All=" + all +
                    "｜RandomOpponent=" + randomOpp + "｜其它=" + other);
            }
            catch (Exception ex)
            {
                log.Info("[dump] 伤害目标统计失败：" + ex.GetType().Name + ": " + ex.Message);
            }
        }
        /// <summary>
        /// 统计每个关键词**在原版内容里实际被用了多少次**，并给出例子卡名。
        /// </summary>
        /// <remarks>
        /// ★ 存在的理由：游戏代码里"认得"某个关键词（private 判定函数有分支）
        /// **不等于**原版内容里用过它。实测差别很大 ——
        /// 有些关键词在 <c>KeyWord</c> 本地化表里根本没有条目，
        /// 硬给 mod 卡挂上会让 tooltip 渲染抛 <c>NullReferenceException</c>
        /// （<c>DitwUtil.ContainsLink</c>，2026-10-12 实机踩中）。
        /// <para>
        /// 所以"能不能用"的判据应当是**内容里是否出现过**，而不是"代码里是否有分支"。
        /// 本探针给出这个事实：每个关键词被多少张原版卡使用、以及前 3 个例子。
        /// </para>
        /// <para>只读，不写游戏状态。</para>
        /// </remarks>
        /// <summary>
        /// 统计**原版卡实际用到的效果名**（照 DumpKeywordUsage 的思路）。
        /// </summary>
        /// <remarks>
        /// 为什么必须有这个：代码里有 Effect* 类，**不等于**内容里在用 ——
        /// 关键词那轮就是靠同款探针，从「官方 36 + 我补的 19」里筛出「实际只有 32 个」的。
        /// 效果同理：EffectSpEffect* 有 212 个类，但可能有一批是死代码。
        /// <para>输出前缀 [fxuse]，便于 看日志.cmd 过滤。本版只统计**卡牌**的效果。</para>
        /// </remarks>
        private static void DumpEffectUsage(IGameLog log, ProbeSummary summary)
        {
            if (log == null) { return; }
            try
            {
                var mgr = CardDataManager.Instance;
                if (mgr == null) { log.Info("[fxuse] CardDataManager.Instance 为 null，跳过"); return; }

                var plain = new Dictionary<string, int>();
                var special = new Dictionary<string, int>();
                var samples = new Dictionary<string, string>();
                int cards = 0, slots = 0;

                foreach (var cardName in ProbeCardNames(null, summary))
                {
                    var so = mgr.GetCardSoByName(cardName);
                    if (so == null) { continue; }   // CardInfo 是 struct，不能判空
                    cards++;

                    var ces = so.CardInfo.cardEffectInfos;
                    if (ces == null) { continue; }

                    foreach (var ce in ces)
                    {
                        var ei = ce.effectInfo;
                        if (ei == null) { continue; }
                        slots++;

                        if (ei.effectName == "SpecialEffect")
                        {
                            var s = ei.specialEffectString;
                            if (string.IsNullOrEmpty(s)) { continue; }
                            if (!special.ContainsKey(s)) { special[s] = 0; samples["sp:" + s] = cardName; }
                            special[s]++;
                        }
                        else
                        {
                            var n = ei.effectName;
                            if (string.IsNullOrEmpty(n)) { continue; }
                            if (!plain.ContainsKey(n)) { plain[n] = 0; samples["pf:" + n] = cardName; }
                            plain[n]++;
                        }
                    }
                }

                log.Info("[fxuse] ===== 原版卡实际用到的效果 =====");
                log.Info("[fxuse] 扫描卡 " + cards + " 张，效果槽 " + slots + " 个");
                log.Info("[fxuse] 普通效果 " + plain.Count + " 种；特殊效果 " + special.Count + " 种");

                var ks = new List<string>(plain.Keys);
                ks.Sort(StringComparer.Ordinal);
                foreach (var k in ks) { log.Info("[fxuse][普通] " + k + " x" + plain[k] + "  例:" + samples["pf:" + k]); }

                var ss = new List<string>(special.Keys);
                ss.Sort(StringComparer.Ordinal);
                foreach (var k in ss) { log.Info("[fxuse][特殊] " + k + " x" + special[k] + "  例:" + samples["sp:" + k]); }
            }
            catch (Exception ex)
            {
                log.Warn("[fxuse] 统计失败（不影响游戏）：" + ex.Message);
            }
        }
        /// <summary>
        /// 统计**原版全部能力**及其 <c>AbilityType</c>（照 DumpKeywordUsage 的思路）。
        /// </summary>
        /// <remarks>
        /// 为什么必须有这个：`apply_prim_ability` / `apply_util_ability` 要求作者填**能力名**，
        /// 而且这两个效果只有施加**对应类别**（`PrimAbility` / `Util`）的能力才合语义 ——
        /// 但平台此前**从没探测过能力名，更没探测过每个能力的类别**，作者只能靠猜。
        /// 顺带补上「命名冲突」那笔欠账：作者起名时终于有原版能力名可对照。
        /// <para>输出前缀 <c>[abuse]</c>。</para>
        /// </remarks>
        private static void DumpAbilityUsage(IGameLog log)
        {
            if (log == null) { return; }
            try
            {
                var dm = DataManager.Instance;
                if (dm == null) { log.Info("[abuse] DataManager.Instance 为 null，跳过"); return; }
                var adm = dm.GetAbilityDataManager();
                if (adm == null) { log.Info("[abuse] GetAbilityDataManager() 为 null（LoadAllGameData 尚未执行）"); return; }

                var all = adm.GetAllAbilitySos();
                if (all == null) { log.Info("[abuse] GetAllAbilitySos() 返回 null"); return; }

                var byType = new Dictionary<string, int>();
                int ok = 0;
                log.Info("[abuse] ===== 原版能力清单（类别 | 名字）=====");
                foreach (var so in all)
                {
                    if (so == null) { continue; }
                    var info = so.abilityInfo;
                    if (info == null || string.IsNullOrEmpty(info.abilityName)) { continue; }
                    ok++;
                    string t = info.abilityType.ToString();
                    if (!byType.ContainsKey(t)) { byType[t] = 0; }
                    byType[t]++;
                    log.Info("[abuse] " + t + " | " + info.abilityName
                        + " | unique=" + info.isUnique
                        + " | effect=" + (info.effectInfo == null ? "-" : info.effectInfo.effectName)
                        + " | trigger=" + (info.triggerTargetInfo.triggerType ?? "-"));
                }
                log.Info("[abuse] 共 " + ok + " 个能力");
                var keys = new List<string>(byType.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (var k in keys) { log.Info("[abuse] 类别 " + k + " : " + byType[k] + " 个"); }
            }
            catch (Exception ex)
            {
                log.Warn("[abuse] 统计失败（不影响游戏）：" + ex.Message);
            }
        }

        private static void DumpKeywordUsage(IGameLog log, ProbeSummary summary)
        {
            if (log == null) { return; }
            try
            {
                var mgr = CardDataManager.Instance;
                if (mgr == null) { return; }

                var counts = new Dictionary<string, int>();
                var samples = new Dictionary<string, List<string>>();

                foreach (var cardName in ProbeCardNames(null, summary))
                {
                    var so = mgr.GetCardSoByName(cardName);
                    if (so == null) { continue; }

                    var kws = so.CardInfo.cardKeyWordInfos;
                    if (kws == null) { continue; }

                    foreach (var kw in kws)
                    {
                        if (string.IsNullOrEmpty(kw.keywordName)) { continue; }
                        if (!counts.ContainsKey(kw.keywordName))
                        {
                            counts[kw.keywordName] = 0;
                            samples[kw.keywordName] = new List<string>();
                        }
                        counts[kw.keywordName]++;
                        if (samples[kw.keywordName].Count < 30)
                        {
                            samples[kw.keywordName].Add(cardName);
                        }
                    }
                }

                log.Info("[kwuse] ===== 关键词在原版内容里的实际使用情况 =====");
                log.Info("[kwuse] 原版卡共用到 " + counts.Count + " 个不同关键词");

                var sorted = new List<string>(counts.Keys);
                sorted.Sort(StringComparer.Ordinal);
                foreach (var k in sorted)
                {
                    log.Info("[kwuse]   " + k + " x" + counts[k] + "   例: " +
                        string.Join(", ", samples[k].ToArray()));
                }

                // 官方名单里、但原版内容从未用过的
                var unusedOfficial = new List<string>();
                foreach (var k in CardKeyWordUtil.AllKeywordStrings)
                {
                    if (!string.IsNullOrEmpty(k) && !counts.ContainsKey(k)) { unusedOfficial.Add(k); }
                }
                log.Info("[kwuse] 官方 AllKeywordStrings 里**从未**被原版卡使用: " +
                    (unusedOfficial.Count == 0 ? "(无)" : string.Join(", ", unusedOfficial.ToArray())));

                // 内容里用到、但不在我们已知集合里的（漏网之鱼）
                var known = new HashSet<string>(CardKeyWordUtil.AllKeywordStrings);

                var unknown = new List<string>();
                foreach (var k in counts.Keys) { if (!known.Contains(k)) { unknown.Add(k); } }
                log.Info("[kwuse] 内容里用到、但不在平台已知集合里的: " +
                    (unknown.Count == 0 ? "(无)" : string.Join(", ", unknown.ToArray())));
                log.Info("[kwuse] ===== 结束 =====");
            }
            catch (Exception ex)
            {
                log.Info("[kwuse] 统计失败：" + ex.GetType().Name + ": " + ex.Message);
            }
        }
        private static List<string> ProbeKeywordNames(IGameLog log, ProbeSummary summary)
        {
            var result = new List<string>();
            try
            {
                var arr = CardKeyWordUtil.AllKeywordStrings;
                if (arr == null || arr.Length == 0)
                {
                    summary.Failures.Add("关键词：CardKeyWordUtil.AllKeywordStrings 为空");
                    return result;
                }

                foreach (var k in arr)
                {
                    if (!string.IsNullOrEmpty(k) && !result.Contains(k)) { result.Add(k); }
                }
                result.Sort(StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                summary.Failures.Add("关键词：探测失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }

            if (log != null) { log.Info("关键词探测：" + result.Count + " 个"); }
            return result;
        }

        // ---------------------------------------------------------------------

        private static string Join(IList<ManaColor> colors)
        {
            if (colors == null || colors.Count == 0) { return "无"; }
            var parts = new string[colors.Count];
            for (int i = 0; i < colors.Count; i++) { parts[i] = colors[i].ToString(); }
            return string.Join(", ", parts);
        }
    }
}
