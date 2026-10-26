# 战绩（Run History）

给 DICEOMANCER 记录**每一局**的流水账，并用一个游戏内面板回看 —— 类似《杀戮尖塔》的 Run History。

> 状态：**数据层 + 面板均已完成**。
> 数据层：开局/续玩/逐节点/结算/放弃全部采集落盘；
> 面板：桌面界面右上角「战绩」入口 → 列表页（分页）→ 详情页（摘要 / 路线 / 卡组 / 遗物 / 统计 / 悬停高亮）。

## 用法

进游戏到**桌面界面**（点"开始游戏"之后那个房间），右上角「模组图鉴」牌子下方就是「战 绩」。
点开是一局一行的列表（日期 / 职业 / 结果 / 时长 / 节点 / 收益），点任意一行进详情：

- **摘要**：职业 · 结果 · 日期 · 种子 · 时长 · 进阶 · 续玩次数 · 收益（升级币）
- **路线**：按顺序的节点色块（战斗/精英/首领/休息/商店/宝箱/未知/删卡…），腐化节点标红
- **卡组 / 遗物**：卡用游戏自己的卡面图，遗物用图标；已升级的卡左上角有 ↑ 角标
- **悬停高亮**：鼠标放到某张卡或某件遗物上，路线条里会高亮"它是在第几个节点拿到的"
- **统计**：击杀（普通/精英/首领）、新节点、骰子（掷出 1 / 掷出最大 / 掷出最小）、检定成败、钓鱼、紫雾、终局血量金币、升级币

## 为什么是独立插件

- 这个 mod 不含卡/遗物/关键词，钩的是**游戏内部流程**，ModKit 的内容契约用不上；
- `ModKit.Api.IMod` 这个入口契约虽然写在 API 里，但平台侧从未调用过（全仓无 `IMod` 引用），照它写等于写个不响的铃；
- 独立插件不依赖平台：平台被停用、或没装 ModKit，战绩照样记录。

## 安装

把 `RunHistory.dll` 放进 `<游戏根>/BepInEx/plugins/`，重启游戏即可。

第一次运行会在 `<游戏根>/BepInEx/RunHistory/` 下创建：

```
RunHistory/
  index.json          列表页用的轻量索引（可删，会自动重建）
  runs/<runId>.json   一局一个文件
```

**不写游戏存档目录**（存档格式随版本变，而且那一层会被 Steam 云同步）。

## 配置（`BepInEx/config/diceomancer.runhistory.cfg`）

| 键 | 默认 | 说明 |
|---|---|---|
| `General.Enabled` | true | 关掉后不装任何钩子 |
| `General.DataDir` | 空 | 留空 = `BepInEx/RunHistory` |
| `General.SnapshotNodes` | true | 逐节点快照（路线 / 逐层获取 / 每层耗时都靠它） |
| `Diagnostics.Verbose` | false | 每次落盘与差分的细节 |

## 数据长什么样

```jsonc
{
  "schema": 1,
  "runId": "20261026-203412-1442534115",
  "startedAt": "2026-10-26 20:34:12",
  "seed": 1442534115,
  "className": "naifrog.milk_frog",
  "ascensionLevel": 0,
  "result": "lose",                 // in_progress / win / lose / abandon / interrupted
  "durationSeconds": 1830.5,        // 游戏自己的 playedTimeInSecond（暂停不计）
  "resumedCount": 1,
  "startingDeck": ["naifrog.milkfrogblock", "…"],
  "nodes": [
    { "index": 1, "at": "2026-10-26 20:35:02", "sincePrevSeconds": 50.1,
      "nodeType": "Enemy", "nodeId": "0-3", "chapter": "ChapterPlain", "depth": 3,
      "corrupted": false, "hp": 68, "maxHp": 80, "gold": 42,
      "deckSize": 10, "relicCount": 2,
      "newCards": [], "newRelics": [] }
  ],
  "final": {
    "deck": ["naifrog.milkfrogstrike+", "Dice"],
    "relics": ["naifrog.milk_frog_talisman"],
    "hp": 0, "maxHp": 80, "gold": 210,
    "stats": { "normalEnemySlainCount": 23, "rollTheOneDiceCount": 3, "…": "…" },
    "upgradeCoins": 57              // 游戏自己的升级币公式
  }
}
```

名字一律存**内部名**（`naifrog.milkfrogstrike`），显示时再查本地化；mod 被卸载后旧战绩也不会变空白。

## 采集点

| 钩子 | 时机 | 用途 |
|---|---|---|
| `CampaignManager.NewGameLaunchInitialize` | 开新局 | 建记录（seed / 起始牌组） |
| `CampaignManager.DeserializeCampaignInfo` | 继续游戏 | 按 seed 归并，`resumedCount++` |
| `WorldManager.MapEngage(MapNode)` | 进入节点 | 节点事件 + 快照 + 逐层获取（与上一节点做差集） |
| `CampaignManager.CampaignEnd` | 结算 | 胜负 / 终局卡组 / 遗物 / 统计 / 收益 |
| `UI.PauseMenu.OnAbandonRunClick` | 放弃 | 标记「放弃」（放弃也走 `CampaignDefeated`，不标记就与阵亡同形） |

「中断」判定：新局开始时，索引里所有仍为 `in_progress` 的旧局标成 `interrupted` —— 新局开始意味着上一局不可能再续上。

## 不变量

- 只读游戏状态，**不写游戏存档**；
- 所有钩子 `try/catch`，异常只记日志，绝不影响游戏；
- 原子写（`.tmp` + 替换），崩溃不会留下半截 JSON；
- 索引损坏可重建；单条坏文件只跳过并警告。
