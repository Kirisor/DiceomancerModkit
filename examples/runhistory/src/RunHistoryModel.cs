// =============================================================================
//  Run History（战绩）· 数据模型
//
//  设计原则：
//    ① **只放纯数据**（字符串/数字/数组），不引用任何游戏类型 ——
//       这样游戏更新、或这个 mod 以后的版本变了，旧战绩仍然读得出来；
//    ② 每个文件带 `schema`，索引损坏可以整个重建；
//    ③ 名字一律存**内部名**（例如 `naifrog.milkfrogstrike`），显示时再查本地化，
//       查不到就直接显示内部名（mod 被卸载后旧战绩不会变成空白）。
// =============================================================================

using System;
using System.Collections.Generic;

namespace RunHistory
{
    /// <summary>一局战绩。</summary>
    [Serializable]
    internal sealed class RunRecord
    {
        /// <summary>数据结构版本。改字段时 +1，读取方据此降级。</summary>
        public int schema = 1;

        /// <summary>局 id：<c>开局时间-seed</c>（同一局续玩不会产生第二条）。</summary>
        public string runId;

        /// <summary>开局时间（游戏自己记的 `playedDate`，格式 yyyy-MM-dd HH:mm:ss）。</summary>
        public string startedAt;

        /// <summary>本局种子（<c>CampaignManager.GetCampaignSeed()</c>）。</summary>
        public int seed;

        /// <summary>职业（内部名，如 <c>naifrog.milk_frog</c>；结算时可从统计里补齐）。</summary>
        public string className;

        /// <summary>进阶等级（结局时从统计里取）。</summary>
        public int ascensionLevel;

        /// <summary>结局：in_progress / win / lose / abandon / interrupted。</summary>
        public string result = Results.InProgress;

        /// <summary>游戏自己累计的游玩秒数（暂停不计）。</summary>
        public double durationSeconds;

        /// <summary>续玩次数（同一局被继续过几次）。</summary>
        public int resumedCount;

        /// <summary>这条记录是从存档里“捡回来”的（开局钩子没跑到，只能靠 seed 认领）。</summary>
        public bool recoveredFromSave;

        /// <summary>开局牌组（做差分的基线，也方便面板展示）。</summary>
        public List<string> startingDeck = new List<string>();

        /// <summary>逐节点轨迹（SnapshotNodes=false 时为空）。</summary>
        public List<RunNode> nodes = new List<RunNode>();

        /// <summary>结算信息（in_progress 时为 null）。</summary>
        public RunFinal final;

        /// <summary>本局最后更新到磁盘的时间（排查断档用）。</summary>
        public string updatedAt;
    }

    /// <summary>一个地图节点的到达记录 + 当时的快照。</summary>
    [Serializable]
    internal sealed class RunNode
    {
        /// <summary>本局第几个节点（从 1 开始）。</summary>
        public int index;

        /// <summary>到达时间（本地时间）。</summary>
        public string at;

        /// <summary>距上一个节点经过的游戏秒数（用统计里的 playedTime 差值，暂停不计）。</summary>
        public double sincePrevSeconds;

        /// <summary>节点类型（<c>World.NodeType</c> 的字符串形式）。</summary>
        public string nodeType;

        /// <summary>节点 id（<c>MapNodeInfo.nodeID</c>）。</summary>
        public string nodeId;

        /// <summary>章节（<c>ChapterMapName</c> 的字符串形式）。</summary>
        public string chapter;

        /// <summary>节点在地图上的列深度（<c>Node.depth</c>）。</summary>
        public int depth;

        /// <summary>该节点是否被紫雾腐化。</summary>
        public bool corrupted;

        public int hp;
        public int maxHp;
        public int gold;

        /// <summary>玩家当前拥有的卡牌总数。</summary>
        public int deckSize;

        /// <summary>玩家当前拥有的遗物数。</summary>
        public int relicCount;

        /// <summary>相比上一个节点**新获得**的卡（带 <c>+</c> 表示已升级）。</summary>
        public List<string> newCards = new List<string>();

        /// <summary>相比上一个节点新获得的遗物。</summary>
        public List<string> newRelics = new List<string>();
    }

    /// <summary>结算信息。</summary>
    [Serializable]
    internal sealed class RunFinal
    {
        /// <summary>终局卡组（卡名，已升级的带 <c>+</c> 后缀）。</summary>
        public List<string> deck = new List<string>();

        /// <summary>终局遗物名。</summary>
        public List<string> relics = new List<string>();

        public int hp;
        public int maxHp;
        public int gold;

        /// <summary>游戏自己的逐局统计（原样抄过来）。</summary>
        public RunStats stats;

        /// <summary>本局“价值”= 游戏自己的升级币公式。</summary>
        public int upgradeCoins;
    }

    /// <summary>
    /// 逐局统计。<c>Career.CampaignStatisticsInfo</c> 的镜像 ——
    /// 游戏只在结算时它有值，随后就被累加进生涯总表并丢弃，这里抄一份留下来。
    /// </summary>
    [Serializable]
    internal sealed class RunStats
    {
        public bool isWin;
        public string className;
        public string playedDate;
        public int seed;
        public int ascensionLevel;
        public int normalEnemySlainCount;
        public int eliteSlainCount;
        public int bossSlainCount;
        public int travelNewNodeCount;
        public int rollTheOneDiceCount;
        public int rollMaxNumberCount;
        public int rollMinNumberCount;
        public int checkSuccessCount;
        public int checkFailCount;
        public int fishCount;
        public int catchByPurpleMistCount;
        public double playedTimeInSecond;
    }

    /// <summary>列表页用的轻量索引（面板只读它，避免解析所有局文件）。</summary>
    [Serializable]
    internal sealed class RunIndex
    {
        public int schema = 1;
        public List<RunIndexEntry> entries = new List<RunIndexEntry>();
    }

    /// <summary>索引条目。</summary>
    [Serializable]
    internal sealed class RunIndexEntry
    {
        public string runId;
        public string startedAt;
        public string className;
        public string result;
        public double durationSeconds;
        public int seed;
        public int nodes;
        public int ascensionLevel;
        public int upgradeCoins;

        /// <summary>相对数据目录的文件路径（<c>runs/xxx.json</c>）。</summary>
        public string file;
    }

    /// <summary>结局取值（字符串便于阅读与向后兼容）。</summary>
    internal static class Results
    {
        internal const string InProgress = "in_progress";
        internal const string Win = "win";
        internal const string Lose = "lose";
        internal const string Abandon = "abandon";
        internal const string Interrupted = "interrupted";

        /// <summary>是不是已经结束（结束的局不再接受节点追加）。</summary>
        internal static bool IsFinal(string result)
        {
            return result == Win || result == Lose || result == Abandon || result == Interrupted;
        }

        /// <summary>中文标签（日志与面板共用）。</summary>
        internal static string Label(string result)
        {
            switch (result)
            {
                case Win: return "通关";
                case Lose: return "阵亡";
                case Abandon: return "放弃";
                case Interrupted: return "中断";
                default: return "进行中";
            }
        }
    }
}
