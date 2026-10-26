// =============================================================================
//  Run History（战绩）· 采集逻辑
//
//  一局的生命周期与采集点（全部是"只读游戏状态 + 落盘"，不改任何游戏数据）：
//    开局  CampaignManager.NewGameLaunchInitialize   → 建记录（seed / 起始牌组）
//    续玩  CampaignManager.DeserializeCampaignInfo   → 按 seed 归并到已有记录
//    进点  WorldManager.MapEngage(MapNode)           → 节点事件 + 快照 + 逐层获取（差分）
//    结算  CampaignManager.CampaignEnd               → 胜负/终局卡组/遗物/统计/收益
//    放弃  UI.PauseMenu.OnAbandonRunClick            → 标记"放弃"（否则与阵亡同形）
//
//  ★ 为什么"逐层获取"不用额外数据源：每进一个节点把"当前拥有的卡名/遗物名集合"
//    抄一份，与上一个节点做差集，新出现的那些就是这一层拿到的。
//    与《杀戮尖塔 2》那个"悬停卡片高亮它在第几层获得"是同一套办法。
//
//  ★ 所有入口都包 try/catch：战绩是旁观者，任何异常都不允许影响游戏本身。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using Card;
using Combat;
using Relic;
using World;
using World.NewMap;

namespace RunHistory
{
    internal static class RunHistoryCapture
    {
        /// <summary>当前正在记录的局（null = 还没开局/已结算）。</summary>
        private static RunRecord _current;

        /// <summary>玩家点了"放弃本局"（放弃也走 CampaignDefeated，不打这个标记就与阵亡同形）。</summary>
        private static bool _abandoned;

        private static readonly HashSet<string> PrevCards = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> PrevRelics = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>上一个节点时的"游戏累计游玩秒数"，用来算每层耗时（暂停不计）。</summary>
        private static double _lastPlayedSeconds;

        // ---------------- 生命周期 ----------------

        /// <summary>开新局。由 <c>CampaignManager.NewGameLaunchInitialize</c> 之后调用。</summary>
        internal static void OnNewRun()
        {
            try
            {
                var campaign = CampaignManager.Instance;
                if (campaign == null) { return; }

                int seed = campaign.GetCampaignSeed();
                string runId = NewRunId(seed);

                // 上一局还挂着"进行中" → 它不可能再被续上了（新局已经开始），标中断
                CloseOlderInProgress(runId);

                var record = new RunRecord
                {
                    runId = runId,
                    startedAt = Stamp(),
                    seed = seed,
                    className = TryClassName(),
                    startingDeck = DeckNames(),
                };
                RefreshBaseline(record);
                _abandoned = false;
                _current = record;

                Save(record);
                RunHistoryPlugin.Log.LogInfo("战绩：新局开始记录（" + record.className +
                    "｜seed " + seed + "｜起始 " + record.startingDeck.Count + " 张）。");
            }
            catch (Exception ex) { Fail("开局采集", ex); }
        }

        /// <summary>继续游戏。由 <c>CampaignManager.DeserializeCampaignInfo</c> 之后调用。</summary>
        /// <remarks>
        /// ★ 两道闸，都是为了不产出"垃圾局"（2026-10-26 首次实机验证后补）：
        ///   ① 只认 <c>CampaignMode.Gaming</c> —— 已经打完的战役（Defeated/Victory）在主菜单
        ///      点"继续"时也会走一次反序列化，那时不该新建一局；
        ///   ② seed 为 0 直接跳过 —— 没有战役时 <c>GetCampaignSeed()</c> 返回 0，
        ///      实测会凭空生成一条 "seed 0 / 空牌组" 的假记录。
        /// </remarks>
        internal static void OnContinue(Combat.CampaignInformation campaignInfo = null)
        {
            try
            {
                var campaign = CampaignManager.Instance;
                if (campaign == null) { return; }

                if (campaignInfo != null && campaignInfo.campaignMode != Combat.CampaignMode.Gaming)
                {
                    RunHistoryPlugin.Log.LogDebug("战绩：本次反序列化的战役已结束（" +
                        campaignInfo.campaignMode + "），不记战绩。");
                    return;
                }

                int seed = campaign.GetCampaignSeed();
                if (seed == 0) { return; }
                var record = RunHistoryStore.FindBySeed(seed);
                bool recovered = false;

                if (record == null || Results.IsFinal(record.result))
                {
                    // 没记过（旧版本/记录被删）→ 从存档里认领一局；同 seed 但已结算 → 视为新的一局
                    record = new RunRecord
                    {
                        runId = NewRunId(seed),
                        startedAt = Stamp(),
                        seed = seed,
                        className = TryClassName(),
                        recoveredFromSave = true,
                        startingDeck = DeckNames(),
                    };
                    recovered = true;
                }
                else
                {
                    record.resumedCount++;
                }

                record.result = Results.InProgress;
                RefreshBaseline(record);
                _abandoned = false;
                _current = record;

                Save(record);
                RunHistoryPlugin.Log.LogInfo("战绩：继续本局（seed " + seed + "｜" +
                    (recovered ? "从存档认领" : "续玩第 " + record.resumedCount + " 次") + "）。");
            }
            catch (Exception ex) { Fail("继续游戏采集", ex); }
        }

        /// <summary>进入一个地图节点。由 <c>WorldManager.MapEngage(MapNode)</c> 之后调用。</summary>
        internal static void OnNode(MapNode mapNode)
        {
            try
            {
                if (mapNode == null) { return; }
                if (RunHistoryPlugin.CfgSnapshotNodes != null && !RunHistoryPlugin.CfgSnapshotNodes.Value) { return; }

                if (_current == null) { OnContinue(); }
                if (_current == null) { return; }
                if (Results.IsFinal(_current.result)) { return; }

                var node = new RunNode
                {
                    index = _current.nodes.Count + 1,
                    at = Stamp(),
                    chapter = TryChapterName(),
                    corrupted = IsCorrupted(mapNode),
                    hp = Hp(),
                    maxHp = MaxHp(),
                    gold = Gold(),
                    deckSize = PrevCards.Count,
                    relicCount = PrevRelics.Count,
                };

                var info = mapNode.node;
                if (info != null)
                {
                    node.nodeType = info.nodeType.ToString();
                    node.nodeId = NodeIdText(info.nodeID);
                }

                node.sincePrevSeconds = SincePrevSeconds();
                DiffInto(node);

                _current.nodes.Add(node);
                _current.durationSeconds = PlayedSeconds();
                Save(_current);
            }
            catch (Exception ex) { Fail("节点采集", ex); }
        }

        /// <summary>本局结算。由 <c>CampaignManager.CampaignEnd</c> 之后调用。</summary>
        internal static void OnRunEnd()
        {
            try
            {
                var campaign = CampaignManager.Instance;
                if (campaign == null) { return; }

                var statsInfo = campaign.GetCampaignStatusInfo();
                if (_current == null) { OnContinue(); }
                if (_current == null) { return; }

                var record = _current;
                var stats = CopyStats(statsInfo);

                record.result = stats.isWin
                    ? Results.Win
                    : (_abandoned ? Results.Abandon : Results.Lose);
                record.ascensionLevel = stats.ascensionLevel;
                record.durationSeconds = stats.playedTimeInSecond;
                if (string.IsNullOrEmpty(record.className)) { record.className = stats.className; }

                record.final = new RunFinal
                {
                    deck = DeckNames(),
                    relics = RelicNames(),
                    hp = Hp(),
                    maxHp = MaxHp(),
                    gold = Gold(),
                    stats = stats,
                    upgradeCoins = UpgradeCoins(statsInfo),
                };

                Save(record);

                RunHistoryPlugin.Log.LogInfo("战绩：本局已结算 —— " + Results.Label(record.result) +
                    "｜" + record.className + "｜seed " + record.seed +
                    "｜节点 " + record.nodes.Count +
                    "｜时长 " + TimeSpan.FromSeconds(record.durationSeconds).ToString(@"hh\:mm\:ss") +
                    "｜收益 " + record.final.upgradeCoins + "（" + record.runId + "）");

                _current = null;
                _abandoned = false;
                PrevCards.Clear();
                PrevRelics.Clear();
            }
            catch (Exception ex) { Fail("结算采集", ex); }
        }

        /// <summary>玩家点了"放弃本局"。</summary>
        internal static void MarkAbandon()
        {
            try { _abandoned = true; } catch { }
        }

        // ---------------- 面板/调试用 ----------------

        /// <summary>把索引里所有"进行中"的旧局标成中断（新局开始时调用）。</summary>
        private static void CloseOlderInProgress(string currentRunId)
        {
            try
            {
                var index = RunHistoryStore.Index;
                for (int i = 0; i < index.entries.Count; i++)
                {
                    var entry = index.entries[i];
                    if (entry == null || entry.runId == currentRunId) { continue; }
                    if (entry.result != Results.InProgress) { continue; }

                    var old = RunHistoryStore.LoadEntry(entry);
                    if (old == null) { continue; }
                    old.result = Results.Interrupted;
                    RunHistoryStore.SaveRun(old);
                    RunHistoryPlugin.Log.LogInfo("战绩：上一局未能收尾，已标记为中断（" + old.runId + "）。");
                }
            }
            catch (Exception ex) { Fail("中断判定", ex); }
        }

        private static void RefreshBaseline(RunRecord record)
        {
            PrevCards.Clear();
            foreach (var name in DeckNames()) { PrevCards.Add(name); }
            PrevRelics.Clear();
            foreach (var name in RelicNames()) { PrevRelics.Add(name); }
            _lastPlayedSeconds = PlayedSeconds();
        }

        /// <summary>算这一层新获得的卡/遗物（与上一节点做差集）。</summary>
        private static void DiffInto(RunNode node)
        {
            var cards = DeckNames();
            foreach (var name in cards)
            {
                if (PrevCards.Add(name)) { node.newCards.Add(name); }
            }
            var relics = RelicNames();
            foreach (var name in relics)
            {
                if (PrevRelics.Add(name)) { node.newRelics.Add(name); }
            }
            node.deckSize = cards.Count;
            node.relicCount = relics.Count;
        }

        // ---------------- 读游戏状态（全部带兜底） ----------------

        private static string NewRunId(int seed)
        {
            return DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + seed;
        }

        private static string Stamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static string TryClassName()
        {
            try
            {
                var cdm = GameData.CardDataManager.Instance;
                var campaign = CampaignManager.Instance;
                if (cdm == null || campaign == null) { return ""; }

                // campaignUsedColorE 是私有字段，游戏只在结算时把它转成 className
                var field = typeof(CampaignManager).GetField("campaignUsedColorE",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var colors = field == null ? null : field.GetValue(campaign) as ColorE[];
                if (colors == null || colors.Length == 0) { return ""; }
                return cdm.GetClassNameByColorE(colors) ?? "";
            }
            catch { return ""; }
        }

        private static string TryChapterName()
        {
            try
            {
                var wm = WorldManager.Instance;
                var chapter = wm == null ? null : wm.currentGeneratedChapterMap;
                return chapter == null ? "" : chapter.chapterMapName.ToString();
            }
            catch { return ""; }
        }

        private static bool IsCorrupted(MapNode node)
        {
            try { return node.nodeCorruptionStatus == NodeCorruptionStatus.Corrupted; }
            catch { return false; }
        }

        private static string NodeIdText(NodeID id)
        {
            try { return id.sectionIndex + "-" + id.nodeIndex; }
            catch { return ""; }
        }

        /// <summary>玩家当前拥有的全部卡名；已升级的加 <c>+</c> 后缀。</summary>
        private static List<string> DeckNames()
        {
            var list = new List<string>();
            try
            {
                var cm = CardManager.Instance;
                if (cm == null) { return list; }
                var cards = cm.GetAllPlayerCards();
                if (cards == null) { return list; }
                foreach (var card in cards)
                {
                    if (card == null) { continue; }
                    string name;
                    try { name = card.CardName; } catch { continue; }
                    if (string.IsNullOrEmpty(name)) { continue; }
                    bool upgraded = false;
                    try { upgraded = card.cardUpgradeInfo.isUpgraded; } catch { }
                    list.Add(upgraded ? name + "+" : name);
                }
                list.Sort(StringComparer.Ordinal);
            }
            catch (Exception ex) { Fail("读取卡组", ex); }
            return list;
        }

        /// <summary>玩家当前拥有的遗物名。</summary>
        private static List<string> RelicNames()
        {
            var list = new List<string>();
            try
            {
                var rm = RelicManager.Instance;
                if (rm == null) { return list; }
                var relics = rm.GetAllPlayerRelics();
                if (relics == null) { return list; }
                foreach (var holder in relics)
                {
                    if (holder == null || holder.Relic == null) { continue; }
                    string name = holder.Relic.RelicName;
                    if (!string.IsNullOrEmpty(name)) { list.Add(name); }
                }
                list.Sort(StringComparer.Ordinal);
            }
            catch (Exception ex) { Fail("读取遗物", ex); }
            return list;
        }

        private static int Hp()
        {
            try
            {
                var player = CharacterPlayer.Instance;
                return player == null ? 0 : player.GetHealthPoint();
            }
            catch { return 0; }
        }

        private static int MaxHp()
        {
            try
            {
                var player = CharacterPlayer.Instance;
                if (player == null) { return 0; }
                // MaxHealthPoint 是 protected 属性 —— 游戏没有公开 getter
                var prop = typeof(Character).GetProperty("MaxHealthPoint",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (prop == null) { return 0; }
                return (int)prop.GetValue(player, null);
            }
            catch { return 0; }
        }

        private static int Gold()
        {
            try
            {
                var money = World.MoneyManager.Instance;
                return money == null ? 0 : money.GetMoney();
            }
            catch { return 0; }
        }

        private static double PlayedSeconds()
        {
            try
            {
                var campaign = CampaignManager.Instance;
                if (campaign == null) { return 0; }
                return campaign.GetCampaignStatusInfo().playedTimeInSecond;
            }
            catch { return 0; }
        }

        private static double SincePrevSeconds()
        {
            double now = PlayedSeconds();
            double delta = now - _lastPlayedSeconds;
            _lastPlayedSeconds = now;
            return delta < 0 ? 0 : Math.Round(delta, 2);
        }

        private static int UpgradeCoins(Career.CampaignStatisticsInfo info)
        {
            try
            {
                return info == null ? 0
                    : Career.CampaignStatisticsInfo.GetCareerUpgradeCoinCountByStatistics(info);
            }
            catch { return 0; }
        }

        /// <summary>把游戏自己的逐局统计抄成纯数据（它随后就会被累加进生涯总表并丢弃）。</summary>
        private static RunStats CopyStats(Career.CampaignStatisticsInfo info)
        {
            var stats = new RunStats();
            if (info == null) { return stats; }
            try
            {
                stats.isWin = info.isWin;
                stats.className = info.className;
                stats.playedDate = info.playedDate;
                stats.seed = info.seed;
                stats.ascensionLevel = info.ascensionLevel;
                stats.normalEnemySlainCount = info.normalEnemySlainCount;
                stats.eliteSlainCount = info.eliteSlainCount;
                stats.bossSlainCount = info.bossSlainCount;
                stats.travelNewNodeCount = info.travelNewNodeCount;
                stats.rollTheOneDiceCount = info.rollTheOneDiceCount;
                stats.rollMaxNumberCount = info.rollMaxNumberCount;
                stats.rollMinNumberCount = info.rollMinNumberCount;
                stats.checkSuccessCount = info.checkSuccessCount;
                stats.checkFailCount = info.checkFailCount;
                stats.fishCount = info.fishCount;
                stats.catchByPurpleMistCount = info.catchByPurpleMistCount;
                stats.playedTimeInSecond = info.playedTimeInSecond;
            }
            catch (Exception ex) { Fail("抄写统计", ex); }
            return stats;
        }

        private static void Save(RunRecord record)
        {
            RunHistoryStore.SaveRun(record);
        }

        private static void Fail(string stage, Exception ex)
        {
            try { RunHistoryPlugin.Log.LogWarning("战绩：" + stage + "失败（本局记录可能不完整）—— " + ex.GetType().Name + ": " + ex.Message); }
            catch { }
        }
    }
}
