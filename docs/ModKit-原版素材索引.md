# ModKit 原版素材索引

> **⚠️ 本文件由脚本生成，请勿手改。**
>
> **生成器**：`tools/gen-vanilla-assets.py`
> **重新生成**：
>
> ```
> python tools\gen-vanilla-assets.py
> ```
>
> 手改会在下次重新生成时被**整个覆盖**。要改内容，请改生成器，或改数据源。

回答一个问题：**写卡/写遗物时，我能从原版借哪些素材？**

> **版权提示**：本文件里的卡图 = **DICEOMANCER 原版游戏素材**（从游戏运行期导出，见 §8），
> 版权属游戏开发者（UltraPiggyStudio）。随仓库公开只为**查阅 / 写卡时对照**；
> 仓库的 MIT 许可证**不覆盖**这些图，请不要单独再分发或商用。详见同级 `art/README.md`。

## 历史变更记录

（本节是生成器里的**脚本常量** `HISTORY`，重新生成不会丢；只追加，不改旧条目。）

| 日期 | 变更 |
|---|---|
| 2026-10-14 | 首次导入实机导出：能力表快照 532 → 实机 534，补入 One Time Crossbow、Sufficient Supplies；无任何卡需要移除。原版遗物名清单 1 → 205（此前写任何真遗物名都会被误判 Error）。新增已知费用槽组合 21 组（单色 6 + 多色 15）。 |
| 2026-10-14 | 卡图入库：实机导出的 492 张卡面 PNG 同步到 `docs/art/`，索引卡图列改为内嵌缩略图并可点开大图（文档自足，不再指向游戏目录）。新增 `docs/art/README.md`（版权提示）与`--art-max-edge` / `--art-jpeg` 体积开关（默认仍为原样拷贝）。 |
| 2026-10-14 | 卡图缩到 64 px：492 张副本从「原样拷贝 256 px / 35.0 MB」改为「重编码，最长边 64 px / 2.7 MB」，索引卡图列显示宽度 96 → 64 px；生成器的 `--art-max-edge` 默认值随之改为 64，重编码档的幂等判据改为「目标像素尺寸 == 源按 max-edge 缩放后的尺寸」。 |

---

## 数据来源与快照

| 项 | 值 |
|---|---|
| 数据源文件（相对仓库根） | `modkit\mods\capabilities.json` |
| 数据源 SHA256 | `921c4779905415e79c5ae03018fcc0f51a8fbe87bf7398935a6ea8e2fbecbf8c` |
| 数据源快照时间 | 2026-10-14 00:53:13 |
| 能力表 `_generatedAt` | 2026-10-14 |
| 能力表 `$schema` | `modkit/capabilities@1` |
| 本文件生成时间 | 2026-10-14 00:53:13（= 数据源快照时间；**刻意不写运行时刻**，见文末「重新生成」） |

**为什么是这一份**：`ModKit.Tools\ModLinter.cs::LoadCapabilities` 的查找顺序是
`--capabilities 显式路径` → `<目标目录>\capabilities.json` → `<目标目录的父目录>\capabilities.json`
→ 内置宽松表。本项目 lint 的目标目录是 `modkit\mods`，**第一顺位就命中**上面这份。

能力表自述（原文照抄 `_source` / `_note`）：

> `_source`：ModKit 探测层事实 + 游戏实机导出（2026-10-14 从实机导出同步；导出文件：G:\deepseek space\DICEOMANCER\BepInEx\ModKitDump\capabilities.json，由 ModKit.Game.CapabilitiesDump 在游戏运行期 2026-10-13T16:45:19Z 写盘，只读探测，不改游戏状态/存档/玩法）
>
> `_note`：2026-10-14 由实机导出整体同步，两个老缺口已补齐：vanillaCardNames 534（手工快照只有 532，缺 One Time Crossbow 与 Sufficient Supplies 两个名字）、vanillaRelicNames 205（此前只登记 Abacus 一项，导致遗物 artTemplate 的非 Abacus 取值几乎全被误判为 Error）、availableCostColors 6、knownKeywords 36（此前自称已核实子集）、knownTriggerNames 32，并新增 knownCostCombos 21 组（游戏内 ManaManager.ManaIconDic 的全部键，顺序参与匹配）。本文件与父目录副本 modkit\capabilities.json 内容字节一致 —— ModLinter.LoadCapabilities 在目标目录找不到能力表时会回落到父目录，两份不一致会造成静默命中旧表而误报未注册。导出侧另有 knownCustomEffectIds 315 / probeFailures 0，本次刻意未写入：ModKit.Tools 的 FileCapabilities 只解析 availableCostColors / vanillaCardNames / vanillaRelicNames / knownKeywords / knownTriggerNames 这五个清单键，写入它们此刻不改变任何判定；knownCostCombos 也尚未被 FileCapabilities 读取（IsKnownCostCombo 目前只逐色检查颜色合法性），多色槽是否预制过仍然校验不了。

### 实机导出（游戏内实测）导入状态

| 项 | 值 |
|---|---|
| 卡清单 | ✅ **已导入** `..\DICEOMANCER\BepInEx\ModKitDump\cards.json` |
| 卡清单 SHA256 | `f0ab4a1c4a8c577980a0af4f559a45dd56e1e110fd6f73785d5a637839fc8401` |
| 卡清单导出时间（游戏侧 UTC） | 2026-10-13T16:45:22Z |
| 卡清单里卡数 | **534** |
| 实机导出能力表 | ✅ 已导入 `..\DICEOMANCER\BepInEx\ModKitDump\capabilities.json`（SHA256 `02fd4ca60ad0d9539d8b7ff5b41ef16bf10e3956d767ed19d856e27772e21439`） |
| 卡图列 | ✅ **已内嵌并入库** `docs/art/`（前缀 `art`） |
| 卡图源目录（游戏导出） | `..\DICEOMANCER\BepInEx\ModKitDump\art` |
| 卡图副本目录（仓库） | `docs\art` |
| 卡图同步 | 源 **492** 张 → 新拷贝 **0** / 重编码 **492** / 跳过 **0**（幂等判据：目标像素尺寸 == 源按 max-edge 缩放后的尺寸） |
| 卡图体积档 | **重编码：最长边 64 px**（PNG；源图未动，副本 2.7 MB） |
| 卡图副本总体积 | **2.7 MB**（493 个文件，都在 `docs/art` 下） |
| 卡图引用自足性 | 引用 **533** 条 / 唯一文件 **492** 个 / **缺失 0** 个 |

**这张表的意思是**：`vanillaCardNames` 那一列的**真源已经换成游戏自己的枚举结果**，
不再是能力表里那份人手抄的快照。因此下面 §2 里**不会出现游戏内不存在的卡**。

**卡图为什么能脱离游戏目录看**：索引的卡图列是**内嵌缩略图**（HTML `<img width=64>`），
缩略图又套了一层链接指向 `art/.png` 里的**原图**；那些文件是导出的卡面副本，
已**拷进仓库** `docs/art/`（493 个 / 2.7 MB）—— 游戏目录删掉也不影响阅读。
**这张表的意思是**：`vanillaCardNames` 那一列的**真源已经换成游戏自己的枚举结果**，
不再是能力表里那份人手抄的快照。因此下面 §2 里**不会出现游戏内不存在的卡**。

### 各清单实际条数

下面是**数据源实际长度**（不是文档里抄的常量）：

| 清单键 | 条数 | 谁在用 |
|---|---|---|
| `availableCostColors` | **6** | 卡 `cost[]` / `colors[]` 的颜色合法性 |
| `vanillaCardNames` | **534** | 卡 `artTemplate` |
| `vanillaRelicNames` | **205** | 遗物 `artTemplate`（✅ 名单完整，硬校验已生效；出处见 §3） |
| `knownKeywords` | **36** | 卡 `keywords[].name` |
| `knownTriggerNames` | **32** | 遗物 `trigger` / 能力 `triggerType` 的第二道闸（见 §1、§5） |

---

## §1 字段 → 可借用清单 对照表

读法：**「lint 行为」那列决定你踩错的代价**——是当场报错，还是只在游戏里才炸。

| 你写的字段 | 载体 | 名字从哪张清单挑 | 条数 | lint 行为（有本文件时） |
|---|---|---|---|---|
| `artTemplate` | 卡 | `vanillaCardNames` | 534 | ✅ **硬校验**：不在清单里 ⇒ `[Error]`，并给相近候选 |
| `artTemplate` | 遗物 | `vanillaRelicNames` | 205 | ✅ **硬校验**（名单已完整，见 §3） |
| `keywords[].name`（含字符串形态 `"Keep"`） | 卡 | `knownKeywords` | 36 | ✅ **硬校验**：未注册 ⇒ `[Error]`，并给相近候选 |
| `trigger`（遗物）/ `triggerType`（能力） | 遗物·能力 | **不是这张清单**：取值域是反编译的 `TriggerTargetInfo.AllTriggerType`（33 个，见 `ModKit.Core\TriggerTypeNames.cs`） | 本文件另有 32 项 = 游戏**实际会发出**的触发点 | ❌ 不在 33 ⇒ `[Error]`；✅ 在 33 **但不在**本文件的 32 项里 ⇒ `[Warn]`（「原版代码里没有任何地方实际发出它」） |
| `cost[]` 里每个颜色名 | 卡 | `availableCostColors` | 6 | ✅ 未知色名 ⇒ `[Error]`；颜色合法但**该槽的组合**未注册 ⇒ `[Error]`（见 §6） |
| `colors[]`（卡面流派色） | 卡 | `availableCostColors` | 6 | ✅ 同上（另有一条独立限制：最多两种颜色） |

### 本文件**不**覆盖的校验

`capabilities.json` 只有 5 个清单键，其余校验来自**代码内置表**或 **DLL 代码扫描**，
不要在这份索引里找它们的名字：

| 字段 | 名字从哪来 |
|---|---|
| `$schema` / `id` / `rarity` / `resolve` / `effects` 结构 | 代码内置表（`ModKit.Core\Names.cs`、`ContentValidator.*.cs`） |
| `effects[].builtin` | `BuiltinEffectNames`（26 个，见《效果字典》§1.2） |
| 自写效果的 id | lint 的 **DLL 代码扫描**（扫 `mod.json` 指向的 dll）。⚠️ 遗物 `behaviour` 已于 **2026-10-14 删除**，不再有任何扫描或声明途径 |
| i18n 表名 | `GameFacts.TableNameList`（代码常量） |

---

## §2 原版卡名全表（534 条）—— 数据源：**游戏实机导出**

这就是卡牌 `artTemplate` 的**全部合法取值**：
**游戏内实测存在的卡**（`cards.json` 的 `cards[].internalName`）。

> ✅ 能力表快照里的每一个卡名，游戏里都**确实存在** —— 本次没有需要移除的项。

> **内部名 vs 中文名**：下表同时给出两者。中文显示名取自游戏本地化表 `CardName`，
> 挑不到中文的语言留空（显示 `—`）。挑 `artTemplate` 仍然要用**内部名**那一列。

### 卡片明细（534 张）

**卡图列说明**：列里是**内嵌缩略图**（HTML `<img width=64>`，显示宽 64 px），
图片文件是**本文件的同级目录** `art/`（已从游戏导出的卡面**拷进仓库**）——
所以**删掉游戏目录也能照常看**，作者可以直接对照卡面挑素材。
当前体积档：**重编码，最长边 64 px**（PNG；源图未动）——
要原始尺寸的副本用 `--art-max-edge 0`，要更小用 `--art-jpeg`。

> **怎么用（很重要）**
> - `artTemplate` 要填的是 **原版卡的内部名**（就是每格最下面那行等宽字，例如 `Block`、`Strike`）——
>   游戏会去借**那张卡的美术/卡框/动画**。
> - **mod 不需要、也不能引用任何图片文件**；本目录 `art/` 里的图**只是给你看清楚自己在借哪张画**。
> - 名字请**逐字符照抄**（大小写敏感）。含空格的名字（如 `Sufficient Supplies`、`One Time Crossbow`）也要原样写。
> - 平台 lint 会核对 `artTemplate` 是否在**实机导出的原版名单**里；写错会在**加载期**报错、卡不会被加载。
>


- **看大图**：每张缩略图都套了一层链接指向同一个文件（`art/<名字>.png`）——
  点缩略图即可打开原图。
- **文件名里的空格**：`Sufficient Supplies.png` 是全表唯一一个带空格的名字，
  链接里写成 `%20`（HTML 的 `src` 没有 Markdown 那种 `<...>` 包裹语法）。
- 原始导出图的**最长边**是 256 px（见 `cards.json` 的 `_thumbMaxEdge`）；仓库里的副本已缩到最长边 64 px，列里按 64 px 宽显示。
- 多张卡共用同一张图时，它们会**指向同一个文件**（导出侧已按"纹理 + 图集矩形"去重）。
- 不想要这一列就加 `--no-art`；不想动 `docs/art/` 就加 `--no-art-copy`。

**自足性核对（生成时现算）**：索引里的卡图引用共 **533** 条，去重后 **492** 个文件，
**全部 492 个都能在 `docs/art/` 里找到** ⇒ **缺失 0**。也就是说把游戏目录整个删掉，本文件的卡图照样显示。
（本目录实际有 493 个文件，其中 492 个被本索引引用。）

## A

<a id="group-a"></a>

<table><tr>
<td align="center"><a href="art/ABitofEverything.png"><img src="art/ABitofEverything.png" width="64" alt="ABitofEverything"></a><br><b>什么都来一点</b><br><code>ABitofEverything</code></td>
<td align="center"><a href="art/Abyss.png"><img src="art/Abyss.png" width="64" alt="Abyss"></a><br><b>深渊</b><br><code>Abyss</code></td>
<td align="center"><a href="art/Acupoint.png"><img src="art/Acupoint.png" width="64" alt="Acupoint"></a><br><b>点穴手</b><br><code>Acupoint</code></td>
<td align="center"><a href="art/AdvancedSummoning.png"><img src="art/AdvancedSummoning.png" width="64" alt="AdvancedSummoning"></a><br><b>高级召唤术</b><br><code>AdvancedSummoning</code></td>
<td align="center"><a href="art/Agile.png"><img src="art/Agile.png" width="64" alt="Agile"></a><br><b>迅捷</b><br><code>Agile</code></td>
<td align="center"><a href="art/AngelInvestment.png"><img src="art/AngelInvestment.png" width="64" alt="AngelInvestment"></a><br><b>天使投资</b><br><code>AngelInvestment</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Apathy.png"><img src="art/Apathy.png" width="64" alt="Apathy"></a><br><b>冷漠</b><br><code>Apathy</code></td>
<td align="center"><a href="art/ApexPredator.png"><img src="art/ApexPredator.png" width="64" alt="ApexPredator"></a><br><b>终极掠食者</b><br><code>ApexPredator</code></td>
<td align="center"><a href="art/Apocalypse.png"><img src="art/Apocalypse.png" width="64" alt="Apocalypse"></a><br><b>末日</b><br><code>Apocalypse</code></td>
<td align="center"><a href="art/ArtificialIntelligence.png"><img src="art/ArtificialIntelligence.png" width="64" alt="ArtificialIntelligence"></a><br><b>人工智能</b><br><code>ArtificialIntelligence</code></td>
<td align="center"><a href="art/Ascend.png"><img src="art/Ascend.png" width="64" alt="Ascend"></a><br><b>飞升</b><br><code>Ascend</code></td>
<td align="center"><a href="art/AscensionCurse.png"><img src="art/AscensionCurse.png" width="64" alt="AscensionCurse"></a><br><b>负担</b><br><code>AscensionCurse</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/AutoBallista.png"><img src="art/AutoBallista.png" width="64" alt="AutoBallista"></a><br><b>自动弩机</b><br><code>AutoBallista</code></td>
</tr></table>


## B

<a id="group-b"></a>

<table><tr>
<td align="center"><a href="art/Backdraft.png"><img src="art/Backdraft.png" width="64" alt="Backdraft"></a><br><b>复燃</b><br><code>Backdraft</code></td>
<td align="center"><a href="art/BadApple.png"><img src="art/BadApple.png" width="64" alt="BadApple"></a><br><b>烂苹果</b><br><code>BadApple</code></td>
<td align="center"><a href="art/BadDay.png"><img src="art/BadDay.png" width="64" alt="BadDay"></a><br><b>倒霉术</b><br><code>BadDay</code></td>
<td align="center"><a href="art/BalancedDriedFish.png"><img src="art/BalancedDriedFish.png" width="64" alt="BalancedDriedFish"></a><br><b>平衡的咸鱼干</b><br><code>BalancedDriedFish</code></td>
<td align="center"><a href="art/BalanceTweak.png"><img src="art/BalanceTweak.png" width="64" alt="BalanceTweak"></a><br><b>平衡调整</b><br><code>BalanceTweak</code></td>
<td align="center"><a href="art/BananaPeel.png"><img src="art/BananaPeel.png" width="64" alt="BananaPeel"></a><br><b>香蕉皮</b><br><code>BananaPeel</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Bandage.png"><img src="art/Bandage.png" width="64" alt="Bandage"></a><br><b>绷带</b><br><code>Bandage</code></td>
<td align="center"><a href="art/BarelyAScratch.png"><img src="art/BarelyAScratch.png" width="64" alt="BarelyAScratch"></a><br><b>略受擦伤</b><br><code>BarelyAScratch</code></td>
<td align="center"><a href="art/BatchSummon.png"><img src="art/BatchSummon.png" width="64" alt="BatchSummon"></a><br><b>批量召唤</b><br><code>BatchSummon</code></td>
<td align="center"><a href="art/BattleInstinct.png"><img src="art/BattleInstinct.png" width="64" alt="BattleInstinct"></a><br><b>战斗直觉</b><br><code>BattleInstinct</code></td>
<td align="center"><a href="art/BattleOfWits.png"><img src="art/BattleOfWits.png" width="64" alt="BattleOfWits"></a><br><b>智斗</b><br><code>BattleOfWits</code></td>
<td align="center"><a href="art/Beacon.png"><img src="art/Beacon.png" width="64" alt="Beacon"></a><br><b>信标</b><br><code>Beacon</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/BalancedDriedFish.png"><img src="art/BalancedDriedFish.png" width="64" alt="BentDriedFish"></a><br><b>弯曲的咸鱼干</b><br><code>BentDriedFish</code></td>
<td align="center"><a href="art/BiggerIsBetter.png"><img src="art/BiggerIsBetter.png" width="64" alt="BiggerIsBetter"></a><br><b>多就是好 大就是棒</b><br><code>BiggerIsBetter</code></td>
<td align="center"><a href="art/BigMeat.png"><img src="art/BigMeat.png" width="64" alt="BigMeat"></a><br><b>大排骨</b><br><code>BigMeat</code></td>
<td align="center"><a href="art/BigRedLaser.png"><img src="art/BigRedLaser.png" width="64" alt="BigRedLaser"></a><br><b>大红激光束</b><br><code>BigRedLaser</code></td>
<td align="center"><a href="art/BinarySword.png"><img src="art/BinarySword.png" width="64" alt="BinarySword"></a><br><b>二进制剑</b><br><code>BinarySword</code></td>
<td align="center"><a href="art/BlackFishCurse.png"><img src="art/BlackFishCurse.png" width="64" alt="BlackFishCurse"></a><br><b>黑鱼诅咒</b><br><code>BlackFishCurse</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/BlackFlask.png"><img src="art/BlackFlask.png" width="64" alt="BlackFlask"></a><br><b>黑元素瓶</b><br><code>BlackFlask</code></td>
<td align="center"><a href="art/BlackHole.png"><img src="art/BlackHole.png" width="64" alt="BlackHole"></a><br><b>黑洞</b><br><code>BlackHole</code></td>
<td align="center"><a href="art/BlackLotus.png"><img src="art/BlackLotus.png" width="64" alt="BlackLotus"></a><br><b>黑莲花</b><br><code>BlackLotus</code></td>
<td align="center"><a href="art/BlackTentacle.png"><img src="art/BlackTentacle.png" width="64" alt="BlackTentacle"></a><br><b>乌黑触手</b><br><code>BlackTentacle</code></td>
<td align="center"><a href="art/Blank.png"><img src="art/Blank.png" width="64" alt="Blank"></a><br><b>『 』</b><br><code>Blank</code></td>
<td align="center"><a href="art/Block.png"><img src="art/Block.png" width="64" alt="Block"></a><br><b>格挡</b><br><code>Block</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/BlockWithFace.png"><img src="art/BlockWithFace.png" width="64" alt="BlockWithFace"></a><br><b>脸接大招</b><br><code>BlockWithFace</code></td>
<td align="center"><a href="art/BloodOfMyBlood.png"><img src="art/BloodOfMyBlood.png" width="64" alt="BloodOfMyBlood"></a><br><b>吾血之血</b><br><code>BloodOfMyBlood</code></td>
<td align="center"><a href="art/BloodRain.png"><img src="art/BloodRain.png" width="64" alt="BloodRain"></a><br><b>血雨</b><br><code>BloodRain</code></td>
<td align="center"><a href="art/Bloodthirsty.png"><img src="art/Bloodthirsty.png" width="64" alt="Bloodthirsty"></a><br><b>嗜血</b><br><code>Bloodthirsty</code></td>
<td align="center"><a href="art/BloodToKnife.png"><img src="art/BloodToKnife.png" width="64" alt="BloodToKnife"></a><br><b>血刃</b><br><code>BloodToKnife</code></td>
<td align="center"><a href="art/BlueFlask.png"><img src="art/BlueFlask.png" width="64" alt="BlueFlask"></a><br><b>蓝元素瓶</b><br><code>BlueFlask</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Blunderbuss.png"><img src="art/Blunderbuss.png" width="64" alt="Blunderbuss"></a><br><b>大铁棒火铳</b><br><code>Blunderbuss</code></td>
<td align="center"><a href="art/BlunderbussReload.png"><img src="art/BlunderbussReload.png" width="64" alt="BlunderbussReload"></a><br><b>大铁棒火铳-装填</b><br><code>BlunderbussReload</code></td>
<td align="center"><a href="art/Blur.png"><img src="art/Blur.png" width="64" alt="Blur"></a><br><b>朦胧术</b><br><code>Blur</code></td>
<td align="center"><a href="art/BodyBurning.png"><img src="art/BodyBurning.png" width="64" alt="BodyBurning"></a><br><b>燃烧身躯</b><br><code>BodyBurning</code></td>
<td align="center"><a href="art/BorrowedCard.png"><img src="art/BorrowedCard.png" width="64" alt="BorrowedCard"></a><br><b>借用之牌</b><br><code>BorrowedCard</code></td>
<td align="center"><a href="art/BorrowedTime.png"><img src="art/BorrowedTime.png" width="64" alt="BorrowedTime"></a><br><b>回光返照</b><br><code>BorrowedTime</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/BottledPixie.png"><img src="art/BottledPixie.png" width="64" alt="BottledPixie"></a><br><b>瓶中精灵</b><br><code>BottledPixie</code></td>
<td align="center"><a href="art/BrainStorm.png"><img src="art/BrainStorm.png" width="64" alt="BrainStorm"></a><br><b>头脑风暴</b><br><code>BrainStorm</code></td>
<td align="center"><a href="art/BrainSurge.png"><img src="art/BrainSurge.png" width="64" alt="BrainSurge"></a><br><b>脑力激荡</b><br><code>BrainSurge</code></td>
<td align="center"><a href="art/Brainwave.png"><img src="art/Brainwave.png" width="64" alt="Brainwave"></a><br><b>灵光乍现</b><br><code>Brainwave</code></td>
<td align="center"><a href="art/Brawl.png"><img src="art/Brawl.png" width="64" alt="Brawl"></a><br><b>绝命乱斗</b><br><code>Brawl</code></td>
<td align="center"><a href="art/BreachingHammer.png"><img src="art/BreachingHammer.png" width="64" alt="BreachingHammer"></a><br><b>破墙大锤</b><br><code>BreachingHammer</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/BreakCocoon.png"><img src="art/BreakCocoon.png" width="64" alt="BreakCocoon"></a><br><b>破茧</b><br><code>BreakCocoon</code></td>
<td align="center"><a href="art/Breath.png"><img src="art/Breath.png" width="64" alt="Breath"></a><br><b>呼吸</b><br><code>Breath</code></td>
<td align="center"><a href="art/BrenGun.png"><img src="art/BrenGun.png" width="64" alt="BrenGun"></a><br><b>布伦轻机枪</b><br><code>BrenGun</code></td>
<td align="center"><a href="art/BrickThrow.png"><img src="art/BrickThrow.png" width="64" alt="BrickThrow"></a><br><b>飞砖</b><br><code>BrickThrow</code></td>
<td align="center"><a href="art/BrokenCard.png"><img src="art/BrokenCard.png" width="64" alt="BrokenCard"></a><br><b>损毁的卡牌</b><br><code>BrokenCard</code></td>
<td align="center"><a href="art/BucketOfLava.png"><img src="art/BucketOfLava.png" width="64" alt="BucketOfLava"></a><br><b>岩浆桶</b><br><code>BucketOfLava</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Buff.png"><img src="art/Buff.png" width="64" alt="Buff"></a><br><b>加强</b><br><code>Buff</code></td>
<td align="center"><a href="art/Build.png"><img src="art/Build.png" width="64" alt="Build"></a><br><b>开工</b><br><code>Build</code></td>
<td align="center"><a href="art/BurnedMatch.png"><img src="art/BurnedMatch.png" width="64" alt="BurnedMatch"></a><br><b>燃尽火柴</b><br><code>BurnedMatch</code></td>
<td align="center"><a href="art/Burnout.png"><img src="art/Burnout.png" width="64" alt="Burnout"></a><br><b>燃尽</b><br><code>Burnout</code></td>
<td align="center"><a href="art/Burst.png"><img src="art/Burst.png" width="64" alt="Burst"></a><br><b>爆发</b><br><code>Burst</code></td>
</tr></table>


## C

<a id="group-c"></a>

<table><tr>
<td align="center"><a href="art/Calamity.png"><img src="art/Calamity.png" width="64" alt="Calamity"></a><br><b>灾厄</b><br><code>Calamity</code></td>
<td align="center"><a href="art/Cancer.png"><img src="art/Cancer.png" width="64" alt="Cancer"></a><br><b>癌变</b><br><code>Cancer</code></td>
<td align="center"><a href="art/Cannon.png"><img src="art/Cannon.png" width="64" alt="Cannon"></a><br><b>大炮</b><br><code>Cannon</code></td>
<td align="center"><a href="art/Cardboard.png"><img src="art/Cardboard.png" width="64" alt="Cardboard"></a><br><b>硬纸板</b><br><code>Cardboard</code></td>
<td align="center"><a href="art/CardOfManyThings.png"><img src="art/CardOfManyThings.png" width="64" alt="CardOfManyThings"></a><br><b>万象无常牌</b><br><code>CardOfManyThings</code></td>
<td align="center"><a href="art/Catalyst.png"><img src="art/Catalyst.png" width="64" alt="Catalyst"></a><br><b>催化剂</b><br><code>Catalyst</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/ChainSword.png"><img src="art/ChainSword.png" width="64" alt="ChainSword"></a><br><b>电锯剑</b><br><code>ChainSword</code></td>
<td align="center"><a href="art/ChaosElement.png"><img src="art/ChaosElement.png" width="64" alt="ChaosElement"></a><br><b>扭曲元素</b><br><code>ChaosElement</code></td>
<td align="center"><a href="art/ChaosStrike.png"><img src="art/ChaosStrike.png" width="64" alt="ChaosStrike"></a><br><b>混乱打击</b><br><code>ChaosStrike</code></td>
<td align="center"><a href="art/ChaoticTransform.png"><img src="art/ChaoticTransform.png" width="64" alt="ChaoticTransform"></a><br><b>混乱转变</b><br><code>ChaoticTransform</code></td>
<td align="center"><a href="art/Cheese.png"><img src="art/Cheese.png" width="64" alt="Cheese"></a><br><b>奶酪</b><br><code>Cheese</code></td>
<td align="center"><a href="art/Chill.png"><img src="art/Chill.png" width="64" alt="Chill"></a><br><b>淡定</b><br><code>Chill</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/ChipStorm.png"><img src="art/ChipStorm.png" width="64" alt="ChipStorm"></a><br><b>筹码雨</b><br><code>ChipStorm</code></td>
<td align="center"><a href="art/ChuckleHead.png"><img src="art/ChuckleHead.png" width="64" alt="ChuckleHead"></a><br><b>榆木脑袋</b><br><code>ChuckleHead</code></td>
<td align="center"><a href="art/ClassicCoke.png"><img src="art/ClassicCoke.png" width="64" alt="ClassicCoke"></a><br><b>可乐</b><br><code>ClassicCoke</code></td>
<td align="center"><a href="art/CleanSweep.png"><img src="art/CleanSweep.png" width="64" alt="CleanSweep"></a><br><b>大扫除</b><br><code>CleanSweep</code></td>
<td align="center"><a href="art/Collapse.png"><img src="art/Collapse.png" width="64" alt="Collapse"></a><br><b>坍塌</b><br><code>Collapse</code></td>
<td align="center"><a href="art/Colossus.png"><img src="art/Colossus.png" width="64" alt="Colossus"></a><br><b>巨像</b><br><code>Colossus</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Command.png"><img src="art/Command.png" width="64" alt="Command"></a><br><b>指挥</b><br><code>Command</code></td>
<td align="center"><a href="art/Confidence.png"><img src="art/Confidence.png" width="64" alt="Confidence"></a><br><b>自信</b><br><code>Confidence</code></td>
<td align="center"><a href="art/ControlledAnger.png"><img src="art/ControlledAnger.png" width="64" alt="ControlledAnger"></a><br><b>受控怒火</b><br><code>ControlledAnger</code></td>
<td align="center"><a href="art/Convoke.png"><img src="art/Convoke.png" width="64" alt="Convoke"></a><br><b>召集</b><br><code>Convoke</code></td>
<td align="center"><a href="art/CounterHelix.png"><img src="art/CounterHelix.png" width="64" alt="CounterHelix"></a><br><b>反击螺旋</b><br><code>CounterHelix</code></td>
<td align="center"><a href="art/CoupDeGrace.png"><img src="art/CoupDeGrace.png" width="64" alt="CoupDeGrace"></a><br><b>恩赐解脱</b><br><code>CoupDeGrace</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Courage.png"><img src="art/Courage.png" width="64" alt="Courage"></a><br><b>勇气</b><br><code>Courage</code></td>
<td align="center"><a href="art/Creation.png"><img src="art/Creation.png" width="64" alt="Creation"></a><br><b>创造术</b><br><code>Creation</code></td>
<td align="center"><a href="art/CrossBow.png"><img src="art/CrossBow.png" width="64" alt="CrossBow"></a><br><b>十字弩</b><br><code>CrossBow</code></td>
<td align="center"><a href="art/CrossBowReload.png"><img src="art/CrossBowReload.png" width="64" alt="CrossBowReload"></a><br><b>十字弩-装填</b><br><code>CrossBowReload</code></td>
<td align="center"><a href="art/Crossroad.png"><img src="art/Crossroad.png" width="64" alt="Crossroad"></a><br><b>歧路</b><br><code>Crossroad</code></td>
<td align="center"><a href="art/Cryptic.png"><img src="art/Cryptic.png" width="64" alt="Cryptic"></a><br><b>神秘</b><br><code>Cryptic</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Cultivate.png"><img src="art/Cultivate.png" width="64" alt="Cultivate"></a><br><b>耕耘</b><br><code>Cultivate</code></td>
<td align="center"><a href="art/Curse.png"><img src="art/Curse.png" width="64" alt="Curse"></a><br><b>诅咒</b><br><code>Curse</code></td>
<td align="center"><a href="art/CycleOfLife.png"><img src="art/CycleOfLife.png" width="64" alt="CycleOfLife"></a><br><b>轮回</b><br><code>CycleOfLife</code></td>
</tr></table>


## D

<a id="group-d"></a>

<table><tr>
<td align="center"><a href="art/DancingSword.png"><img src="art/DancingSword.png" width="64" alt="DancingSword"></a><br><b>舞剑</b><br><code>DancingSword</code></td>
<td align="center"><a href="art/DarkElement.png"><img src="art/DarkElement.png" width="64" alt="DarkElement"></a><br><b>黯元素</b><br><code>DarkElement</code></td>
<td align="center"><a href="art/Dawn.png"><img src="art/Dawn.png" width="64" alt="Dawn"></a><br><b>黎明</b><br><code>Dawn</code></td>
<td align="center"><a href="art/Dead.png"><img src="art/Dead.png" width="64" alt="Dead"></a><br><b>死了！</b><br><code>Dead</code></td>
<td align="center"><a href="art/DeadlyVirus.png"><img src="art/DeadlyVirus.png" width="64" alt="DeadlyVirus"></a><br><b>致命病毒</b><br><code>DeadlyVirus</code></td>
<td align="center"><a href="art/Death.png"><img src="art/Death.png" width="64" alt="Death"></a><br><b>死亡</b><br><code>Death</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/DefensiveTactic.png"><img src="art/DefensiveTactic.png" width="64" alt="DefensiveTactic"></a><br><b>防御战术</b><br><code>DefensiveTactic</code></td>
<td align="center"><a href="art/Deflect.png"><img src="art/Deflect.png" width="64" alt="Deflect"></a><br><b>偏斜</b><br><code>Deflect</code></td>
<td align="center"><a href="art/Design.png"><img src="art/Design.png" width="64" alt="Design"></a><br><b>设计</b><br><code>Design</code></td>
<td align="center"><a href="art/Determination.png"><img src="art/Determination.png" width="64" alt="Determination"></a><br><b>决心</b><br><code>Determination</code></td>
<td align="center"><a href="art/DeusExMachina.png"><img src="art/DeusExMachina.png" width="64" alt="DeusExMachina"></a><br><b>机械降神</b><br><code>DeusExMachina</code></td>
<td align="center"><a href="art/Dice.png"><img src="art/Dice.png" width="64" alt="Dice"></a><br><b>至尊骰</b><br><code>Dice</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/DiceBonus.png"><img src="art/DiceBonus.png" width="64" alt="DiceBonus"></a><br><b>幸运骰子</b><br><code>DiceBonus</code></td>
<td align="center"><a href="art/DiceCharger.png"><img src="art/DiceCharger.png" width="64" alt="DiceCharger"></a><br><b>骰子充能器</b><br><code>DiceCharger</code></td>
<td align="center"><a href="art/DiceCurse.png"><img src="art/DiceCurse.png" width="64" alt="DiceCurse"></a><br><b>骰子诅咒</b><br><code>DiceCurse</code></td>
<td align="center"><a href="art/DiceMax.png"><img src="art/DiceMax.png" width="64" alt="DiceMax"></a><br><b>抬一手</b><br><code>DiceMax</code></td>
<td align="center"><a href="art/DiceMin.png"><img src="art/DiceMin.png" width="64" alt="DiceMin"></a><br><b>阴一手</b><br><code>DiceMin</code></td>
<td align="center"><a href="art/DiceTakeLuck.png"><img src="art/DiceTakeLuck.png" width="64" alt="DiceTakeLuck"></a><br><b>运气窃取</b><br><code>DiceTakeLuck</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/DiceWall.png"><img src="art/DiceWall.png" width="64" alt="DiceWall"></a><br><b>骰子墙</b><br><code>DiceWall</code></td>
<td align="center"><a href="art/Dicify.png"><img src="art/Dicify.png" width="64" alt="Dicify"></a><br><b>骰化术</b><br><code>Dicify</code></td>
<td align="center"><a href="art/Discernment.png"><img src="art/Discernment.png" width="64" alt="Discernment"></a><br><b>明辨</b><br><code>Discernment</code></td>
<td align="center"><a href="art/Disintegrate.png"><img src="art/Disintegrate.png" width="64" alt="Disintegrate"></a><br><b>解离术</b><br><code>Disintegrate</code></td>
<td align="center"><a href="art/DissipateForce.png"><img src="art/DissipateForce.png" width="64" alt="DissipateForce"></a><br><b>卸力</b><br><code>DissipateForce</code></td>
<td align="center"><a href="art/DivergentThinking.png"><img src="art/DivergentThinking.png" width="64" alt="DivergentThinking"></a><br><b>发散思维</b><br><code>DivergentThinking</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Doom.png"><img src="art/Doom.png" width="64" alt="Doom"></a><br><b>毁灭</b><br><code>Doom</code></td>
<td align="center"><a href="art/DoomsdayRocket.png"><img src="art/DoomsdayRocket.png" width="64" alt="DoomsdayRocket"></a><br><b>末日火箭炮</b><br><code>DoomsdayRocket</code></td>
<td align="center"><a href="art/DoomsdayRocketReload.png"><img src="art/DoomsdayRocketReload.png" width="64" alt="DoomsdayRocketReload"></a><br><b>末日火箭炮-装填</b><br><code>DoomsdayRocketReload</code></td>
<td align="center"><a href="art/DoorShield.png"><img src="art/DoorShield.png" width="64" alt="DoorShield"></a><br><b>大门盾</b><br><code>DoorShield</code></td>
<td align="center"><a href="art/DoubleStrike.png"><img src="art/DoubleStrike.png" width="64" alt="DoubleStrike"></a><br><b>连击</b><br><code>DoubleStrike</code></td>
<td align="center"><a href="art/Draupnir.png"><img src="art/Draupnir.png" width="64" alt="Draupnir"></a><br><b>德罗普尼尔</b><br><code>Draupnir</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Draupnir.png"><img src="art/Draupnir.png" width="64" alt="DraupnirDuplicate"></a><br><b>德罗普尼尔之复制</b><br><code>DraupnirDuplicate</code></td>
<td align="center"><a href="art/Dream.png"><img src="art/Dream.png" width="64" alt="Dream"></a><br><b>梦想</b><br><code>Dream</code></td>
<td align="center"><a href="art/DrunkenFist.png"><img src="art/DrunkenFist.png" width="64" alt="DrunkenFist"></a><br><b>醉拳</b><br><code>DrunkenFist</code></td>
<td align="center"><a href="art/DualBian.png"><img src="art/DualBian.png" width="64" alt="DualBian"></a><br><b>双鞭</b><br><code>DualBian</code></td>
<td align="center"><a href="art/DualSMG.png"><img src="art/DualSMG.png" width="64" alt="DualSMG"></a><br><b>双持冲锋枪</b><br><code>DualSMG</code></td>
<td align="center"><a href="art/DyingSurge.png"><img src="art/DyingSurge.png" width="64" alt="DyingSurge"></a><br><b>搏命</b><br><code>DyingSurge</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/DysonSphere.png"><img src="art/DysonSphere.png" width="64" alt="DysonSphere"></a><br><b>戴森球</b><br><code>DysonSphere</code></td>
<td align="center"><a href="art/DysonSphereProgram.png"><img src="art/DysonSphereProgram.png" width="64" alt="DysonSphereProgram"></a><br><b>戴森球计划</b><br><code>DysonSphereProgram</code></td>
</tr></table>


## E

<a id="group-e"></a>

<table><tr>
<td align="center"><a href="art/EarthElement.png"><img src="art/EarthElement.png" width="64" alt="EarthElement"></a><br><b>土元素</b><br><code>EarthElement</code></td>
<td align="center"><a href="art/ElasticDefence.png"><img src="art/ElasticDefence.png" width="64" alt="ElasticDefence"></a><br><b>弹性防御</b><br><code>ElasticDefence</code></td>
<td align="center"><a href="art/ElectricArc.png"><img src="art/ElectricArc.png" width="64" alt="ElectricArc"></a><br><b>电弧</b><br><code>ElectricArc</code></td>
<td align="center"><a href="art/ElectromagneticCoil.png"><img src="art/ElectromagneticCoil.png" width="64" alt="ElectromagneticCoil"></a><br><b>电磁线圈</b><br><code>ElectromagneticCoil</code></td>
<td align="center"><a href="art/ElectromagneticInduction.png"><img src="art/ElectromagneticInduction.png" width="64" alt="ElectromagneticInduction"></a><br><b>电磁感应</b><br><code>ElectromagneticInduction</code></td>
<td align="center"><a href="art/ElementalDance.png"><img src="art/ElementalDance.png" width="64" alt="ElementalDance"></a><br><b>唤灵之舞</b><br><code>ElementalDance</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/ElementEmergence.png"><img src="art/ElementEmergence.png" width="64" alt="ElementEmergence"></a><br><b>元素奔涌</b><br><code>ElementEmergence</code></td>
<td align="center"><a href="art/Ember.png"><img src="art/Ember.png" width="64" alt="Ember"></a><br><b>余烬</b><br><code>Ember</code></td>
<td align="center"><a href="art/EmptyCard.png"><img src="art/EmptyCard.png" width="64" alt="EmptyCard"></a><br><b>空白卡牌</b><br><code>EmptyCard</code></td>
<td align="center"><a href="art/Encircle.png"><img src="art/Encircle.png" width="64" alt="Encircle"></a><br><b>包围</b><br><code>Encircle</code></td>
<td align="center"><a href="art/EnergyBlast.png"><img src="art/EnergyBlast.png" width="64" alt="EnergyBlast"></a><br><b>能量爆破</b><br><code>EnergyBlast</code></td>
<td align="center"><a href="art/EnergyCore.png"><img src="art/EnergyCore.png" width="64" alt="EnergyCore"></a><br><b>能源核心</b><br><code>EnergyCore</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Enlarge.png"><img src="art/Enlarge.png" width="64" alt="Enlarge"></a><br><b>巨化术</b><br><code>Enlarge</code></td>
<td align="center"><a href="art/EntangledEnigma.png"><img src="art/EntangledEnigma.png" width="64" alt="EntangledEnigma"></a><br><b>交织谜团</b><br><code>EntangledEnigma</code></td>
<td align="center"><a href="art/Epiphany.png"><img src="art/Epiphany.png" width="64" alt="Epiphany"></a><br><b>顿悟</b><br><code>Epiphany</code></td>
<td align="center"><a href="art/Equanimity.png"><img src="art/Equanimity.png" width="64" alt="Equanimity"></a><br><b>沉着</b><br><code>Equanimity</code></td>
<td align="center"><a href="art/EssenceFlask.png"><img src="art/EssenceFlask.png" width="64" alt="EssenceFlask"></a><br><b>精华瓶</b><br><code>EssenceFlask</code></td>
<td align="center"><a href="art/EssenceOfChaos.png"><img src="art/EssenceOfChaos.png" width="64" alt="EssenceOfChaos"></a><br><b>扭曲之精粹</b><br><code>EssenceOfChaos</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/EssenceOfChaos.png"><img src="art/EssenceOfChaos.png" width="64" alt="EssenceOfDarkness"></a><br><b>黑暗之精粹</b><br><code>EssenceOfDarkness</code></td>
<td align="center"><a href="art/EssenceOfChaos.png"><img src="art/EssenceOfChaos.png" width="64" alt="EssenceOfEarth"></a><br><b>土之精粹</b><br><code>EssenceOfEarth</code></td>
<td align="center"><a href="art/EssenceOfChaos.png"><img src="art/EssenceOfChaos.png" width="64" alt="EssenceOfEssence"></a><br><b>精粹</b><br><code>EssenceOfEssence</code></td>
<td align="center"><a href="art/EssenceOfChaos.png"><img src="art/EssenceOfChaos.png" width="64" alt="EssenceOfFire"></a><br><b>火之精粹</b><br><code>EssenceOfFire</code></td>
<td align="center"><a href="art/EssenceOfChaos.png"><img src="art/EssenceOfChaos.png" width="64" alt="EssenceOfWater"></a><br><b>水之精粹</b><br><code>EssenceOfWater</code></td>
<td align="center"><a href="art/EverChangingSpell.png"><img src="art/EverChangingSpell.png" width="64" alt="EverChangingSpell"></a><br><b>万变术</b><br><code>EverChangingSpell</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Evil.png"><img src="art/Evil.png" width="64" alt="Evil"></a><br><b>邪恶</b><br><code>Evil</code></td>
<td align="center"><a href="art/Evolution.png"><img src="art/Evolution.png" width="64" alt="Evolution"></a><br><b>演化</b><br><code>Evolution</code></td>
<td align="center"><a href="art/Execute.png"><img src="art/Execute.png" width="64" alt="Execute"></a><br><b>处决</b><br><code>Execute</code></td>
<td align="center"><a href="art/Exhausted.png"><img src="art/Exhausted.png" width="64" alt="Exhausted"></a><br><b>疲惫</b><br><code>Exhausted</code></td>
<td align="center"><a href="art/Explore.png"><img src="art/Explore.png" width="64" alt="Explore"></a><br><b>探求</b><br><code>Explore</code></td>
<td align="center"><a href="art/Explosion.png"><img src="art/Explosion.png" width="64" alt="Explosion"></a><br><b>燃爆</b><br><code>Explosion</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/ExponentialGrowth.png"><img src="art/ExponentialGrowth.png" width="64" alt="ExponentialGrowth"></a><br><b>指数成长</b><br><code>ExponentialGrowth</code></td>
</tr></table>


## F

<a id="group-f"></a>

<table><tr>
<td align="center"><a href="art/Fallout.png"><img src="art/Fallout.png" width="64" alt="Fallout"></a><br><b>辐射</b><br><code>Fallout</code></td>
<td align="center"><a href="art/FastForward.png"><img src="art/FastForward.png" width="64" alt="FastForward"></a><br><b>快进</b><br><code>FastForward</code></td>
<td align="center"><a href="art/FateFavors.png"><img src="art/FateFavors.png" width="64" alt="FateFavors"></a><br><b>天选之人</b><br><code>FateFavors</code></td>
<td align="center"><a href="art/Fear.png"><img src="art/Fear.png" width="64" alt="Fear"></a><br><b>恐惧</b><br><code>Fear</code></td>
<td align="center"><a href="art/Feast.png"><img src="art/Feast.png" width="64" alt="Feast"></a><br><b>盛宴</b><br><code>Feast</code></td>
<td align="center"><a href="art/Feeble.png"><img src="art/Feeble.png" width="64" alt="Feeble"></a><br><b>萎靡</b><br><code>Feeble</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Feeding.png"><img src="art/Feeding.png" width="64" alt="Feeding"></a><br><b>投喂</b><br><code>Feeding</code></td>
<td align="center"><a href="art/FeelingLucky.png"><img src="art/FeelingLucky.png" width="64" alt="FeelingLucky"></a><br><b>手气不错！</b><br><code>FeelingLucky</code></td>
<td align="center"><a href="art/FeuxFollets.png"><img src="art/FeuxFollets.png" width="64" alt="FeuxFollets"></a><br><b>鬼火</b><br><code>FeuxFollets</code></td>
<td align="center"><a href="art/FightToLive.png"><img src="art/FightToLive.png" width="64" alt="FightToLive"></a><br><b>求生</b><br><code>FightToLive</code></td>
<td align="center"><a href="art/Filtering.png"><img src="art/Filtering.png" width="64" alt="Filtering"></a><br><b>滤波</b><br><code>Filtering</code></td>
<td align="center"><a href="art/Firecrackers.png"><img src="art/Firecrackers.png" width="64" alt="Firecrackers"></a><br><b>大炮仗</b><br><code>Firecrackers</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/FireElement.png"><img src="art/FireElement.png" width="64" alt="FireElement"></a><br><b>火元素</b><br><code>FireElement</code></td>
<td align="center"><a href="art/FirePunch.png"><img src="art/FirePunch.png" width="64" alt="FirePunch"></a><br><b>火焰拳</b><br><code>FirePunch</code></td>
<td align="center"><a href="art/FireSuppress.png"><img src="art/FireSuppress.png" width="64" alt="FireSuppress"></a><br><b>压制射击</b><br><code>FireSuppress</code></td>
<td align="center"><a href="art/FireWhirl.png"><img src="art/FireWhirl.png" width="64" alt="FireWhirl"></a><br><b>火龙卷</b><br><code>FireWhirl</code></td>
<td align="center"><a href="art/Firework.png"><img src="art/Firework.png" width="64" alt="Firework"></a><br><b>烟花</b><br><code>Firework</code></td>
<td align="center"><a href="art/FireworkArray.png"><img src="art/FireworkArray.png" width="64" alt="FireworkArray"></a><br><b>烟火阵列</b><br><code>FireworkArray</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/FireworkRocket.png"><img src="art/FireworkRocket.png" width="64" alt="FireworkRocket"></a><br><b>烟花火箭</b><br><code>FireworkRocket</code></td>
<td align="center"><a href="art/Flail.png"><img src="art/Flail.png" width="64" alt="Flail"></a><br><b>连枷</b><br><code>Flail</code></td>
<td align="center"><a href="art/Flame.png"><img src="art/Flame.png" width="64" alt="Flame"></a><br><b>烈焰</b><br><code>Flame</code></td>
<td align="center"><a href="art/FlameInRemnants.png"><img src="art/FlameInRemnants.png" width="64" alt="FlameInRemnants"></a><br><b>残躯化烈火</b><br><code>FlameInRemnants</code></td>
<td align="center"><a href="art/FlameSword.png"><img src="art/FlameSword.png" width="64" alt="FlameSword"></a><br><b>大火刀</b><br><code>FlameSword</code></td>
<td align="center"><a href="art/FlashGrenade.png"><img src="art/FlashGrenade.png" width="64" alt="FlashGrenade"></a><br><b>闪光弹</b><br><code>FlashGrenade</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Flow.png"><img src="art/Flow.png" width="64" alt="Flow"></a><br><b>心流</b><br><code>Flow</code></td>
<td align="center"><a href="art/Flustered.png"><img src="art/Flustered.png" width="64" alt="Flustered"></a><br><b>慌乱</b><br><code>Flustered</code></td>
<td align="center"><a href="art/FlyingAxe.png"><img src="art/FlyingAxe.png" width="64" alt="FlyingAxe"></a><br><b>旋风斧</b><br><code>FlyingAxe</code></td>
<td align="center"><a href="art/FoldingStool.png"><img src="art/FoldingStool.png" width="64" alt="FoldingStool"></a><br><b>折凳</b><br><code>FoldingStool</code></td>
<td align="center"><a href="art/Foolsluck.png"><img src="art/Foolsluck.png" width="64" alt="Foolsluck"></a><br><b>傻人有傻福</b><br><code>Foolsluck</code></td>
<td align="center"><a href="art/ForceOfNature.png"><img src="art/ForceOfNature.png" width="64" alt="ForceOfNature"></a><br><b>自然之力</b><br><code>ForceOfNature</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/FoulPlay.png"><img src="art/FoulPlay.png" width="64" alt="FoulPlay"></a><br><b>千术</b><br><code>FoulPlay</code></td>
<td align="center"><a href="art/FoundationModel.png"><img src="art/FoundationModel.png" width="64" alt="FoundationModel"></a><br><b>基础模型</b><br><code>FoundationModel</code></td>
<td align="center"><a href="art/Fries.png"><img src="art/Fries.png" width="64" alt="Fries"></a><br><b>薯条</b><br><code>Fries</code></td>
<td align="center"><a href="art/FullyArmed.png"><img src="art/FullyArmed.png" width="64" alt="FullyArmed"></a><br><b>全副武装</b><br><code>FullyArmed</code></td>
<td align="center"><a href="art/Fuse.png"><img src="art/Fuse.png" width="64" alt="Fuse"></a><br><b>融合</b><br><code>Fuse</code></td>
<td align="center"><a href="art/FusedElement.png"><img src="art/FusedElement.png" width="64" alt="FusedElement"></a><br><b>融合元素</b><br><code>FusedElement</code></td>
</tr></table>


## G

<a id="group-g"></a>

<table><tr>
<td align="center"><a href="art/Gamble.png"><img src="art/Gamble.png" width="64" alt="Gamble"></a><br><b>赌一手</b><br><code>Gamble</code></td>
<td align="center"><a href="art/Gathering.png"><img src="art/Gathering.png" width="64" alt="Gathering"></a><br><b>聚集</b><br><code>Gathering</code></td>
<td align="center"><a href="art/GatlingGun.png"><img src="art/GatlingGun.png" width="64" alt="GatlingGun"></a><br><b>加特林机枪</b><br><code>GatlingGun</code></td>
<td align="center"><a href="art/GaussRifle.png"><img src="art/GaussRifle.png" width="64" alt="GaussRifle"></a><br><b>高斯步枪</b><br><code>GaussRifle</code></td>
<td align="center"><a href="art/GiantClub.png"><img src="art/GiantClub.png" width="64" alt="GiantClub"></a><br><b>巨型棍棒</b><br><code>GiantClub</code></td>
<td align="center"><a href="art/GloriousEvolution.png"><img src="art/GloriousEvolution.png" width="64" alt="GloriousEvolution"></a><br><b>光荣进化</b><br><code>GloriousEvolution</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/GlowBerries.png"><img src="art/GlowBerries.png" width="64" alt="GlowBerries"></a><br><b>发光果</b><br><code>GlowBerries</code></td>
<td align="center"><a href="art/Block.png"><img src="art/Block.png" width="64" alt="GoodBlock"></a><br><b>优秀格挡</b><br><code>GoodBlock</code></td>
<td align="center"><a href="art/GoodStrike.png"><img src="art/GoodStrike.png" width="64" alt="GoodStrike"></a><br><b>优秀打击</b><br><code>GoodStrike</code></td>
<td align="center"><a href="art/GrandBattlePlan.png"><img src="art/GrandBattlePlan.png" width="64" alt="GrandBattlePlan"></a><br><b>决战计划</b><br><code>GrandBattlePlan</code></td>
<td align="center"><a href="art/GrandMaster.png"><img src="art/GrandMaster.png" width="64" alt="GrandMaster"></a><br><b>武学宗师</b><br><code>GrandMaster</code></td>
<td align="center"><a href="art/GrapeShot.png"><img src="art/GrapeShot.png" width="64" alt="GrapeShot"></a><br><b>葡萄弹</b><br><code>GrapeShot</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/GreenFlask.png"><img src="art/GreenFlask.png" width="64" alt="GreenFlask"></a><br><b>绿元素瓶</b><br><code>GreenFlask</code></td>
<td align="center"><a href="art/Grenade.png"><img src="art/Grenade.png" width="64" alt="Grenade"></a><br><b>手榴弹</b><br><code>Grenade</code></td>
<td align="center"><a href="art/GunBarrel.png"><img src="art/GunBarrel.png" width="64" alt="GunBarrel"></a><br><b>炮管</b><br><code>GunBarrel</code></td>
<td align="center"><a href="art/GunFu.png"><img src="art/GunFu.png" width="64" alt="GunFu"></a><br><b>枪斗术</b><br><code>GunFu</code></td>
</tr></table>


## H

<a id="group-h"></a>

<table><tr>
<td align="center"><a href="art/Hallucination.png"><img src="art/Hallucination.png" width="64" alt="Hallucination"></a><br><b>幻觉</b><br><code>Hallucination</code></td>
<td align="center"><a href="art/Hamburger.png"><img src="art/Hamburger.png" width="64" alt="Hamburger"></a><br><b>汉堡包</b><br><code>Hamburger</code></td>
<td align="center"><a href="art/Harpoon.png"><img src="art/Harpoon.png" width="64" alt="Harpoon"></a><br><b>鱼叉</b><br><code>Harpoon</code></td>
<td align="center"><a href="art/HeadShot.png"><img src="art/HeadShot.png" width="64" alt="HeadShot"></a><br><b>一枪头</b><br><code>HeadShot</code></td>
<td align="center"><a href="art/HeavyStrike.png"><img src="art/HeavyStrike.png" width="64" alt="HeavyStrike"></a><br><b>狠狠打击</b><br><code>HeavyStrike</code></td>
<td align="center"><a href="art/Herb.png"><img src="art/Herb.png" width="64" alt="Herb"></a><br><b>神奇药草</b><br><code>Herb</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Hesitation.png"><img src="art/Hesitation.png" width="64" alt="Hesitation"></a><br><b>犹豫</b><br><code>Hesitation</code></td>
<td align="center"><a href="art/HighNoon.png"><img src="art/HighNoon.png" width="64" alt="HighNoon"></a><br><b>正午</b><br><code>HighNoon</code></td>
<td align="center"><a href="art/Hope.png"><img src="art/Hope.png" width="64" alt="Hope"></a><br><b>希望</b><br><code>Hope</code></td>
<td align="center"><a href="art/HorseSlayer.png"><img src="art/HorseSlayer.png" width="64" alt="HorseSlayer"></a><br><b>斩马长刀</b><br><code>HorseSlayer</code></td>
<td align="center"><a href="art/Humility.png"><img src="art/Humility.png" width="64" alt="Humility"></a><br><b>谦逊</b><br><code>Humility</code></td>
<td align="center"><a href="art/HunterAndHunts.png"><img src="art/HunterAndHunts.png" width="64" alt="HunterAndHunts"></a><br><b>猎人与猎物</b><br><code>HunterAndHunts</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/HuntingNet.png"><img src="art/HuntingNet.png" width="64" alt="HuntingNet"></a><br><b>绳网</b><br><code>HuntingNet</code></td>
<td align="center"><a href="art/Filtering.png"><img src="art/Filtering.png" width="64" alt="Hysteresis"></a><br><b>迟滞</b><br><code>Hysteresis</code></td>
</tr></table>


## I

<a id="group-i"></a>

<table><tr>
<td align="center"><a href="art/IAMONFIRE.png"><img src="art/IAMONFIRE.png" width="64" alt="IAMONFIRE"></a><br><b>我着火了！</b><br><code>IAMONFIRE</code></td>
<td align="center"><a href="art/Ignite.png"><img src="art/Ignite.png" width="64" alt="Ignite"></a><br><b>点燃</b><br><code>Ignite</code></td>
<td align="center"><a href="art/Impatient.png"><img src="art/Impatient.png" width="64" alt="Impatient"></a><br><b>不耐烦</b><br><code>Impatient</code></td>
<td align="center"><a href="art/ImprovisedWeapon.png"><img src="art/ImprovisedWeapon.png" width="64" alt="ImprovisedWeapon"></a><br><b>临时武器</b><br><code>ImprovisedWeapon</code></td>
<td align="center"><a href="art/InnerExplosion.png"><img src="art/InnerExplosion.png" width="64" alt="InnerExplosion"></a><br><b>内向爆裂</b><br><code>InnerExplosion</code></td>
<td align="center"><a href="art/Innervate.png"><img src="art/Innervate.png" width="64" alt="Innervate"></a><br><b>激活</b><br><code>Innervate</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Insanity.png"><img src="art/Insanity.png" width="64" alt="Insanity"></a><br><b>疯狂</b><br><code>Insanity</code></td>
<td align="center"><a href="art/Instinct.png"><img src="art/Instinct.png" width="64" alt="Instinct"></a><br><b>本能</b><br><code>Instinct</code></td>
<td align="center"><a href="art/InvincibilityDevice.png"><img src="art/InvincibilityDevice.png" width="64" alt="InvincibilityDevice"></a><br><b>无敌装置</b><br><code>InvincibilityDevice</code></td>
<td align="center"><a href="art/IridescentRays.png"><img src="art/IridescentRays.png" width="64" alt="IridescentRays"></a><br><b>虹彩射线</b><br><code>IridescentRays</code></td>
<td align="center"><a href="art/IsItRight.png"><img src="art/IsItRight.png" width="64" alt="IsItRight"></a><br><b>对嘛？</b><br><code>IsItRight</code></td>
</tr></table>


## J

<a id="group-j"></a>

<table><tr>
<td align="center"><a href="art/Javelin.png"><img src="art/Javelin.png" width="64" alt="Javelin"></a><br><b>标枪</b><br><code>Javelin</code></td>
<td align="center"><a href="art/Joker.png"><img src="art/Joker.png" width="64" alt="Joker"></a><br><b>鬼牌</b><br><code>Joker</code></td>
<td align="center"><a href="art/JunkChest.png"><img src="art/JunkChest.png" width="64" alt="JunkChest"></a><br><b>垃圾宝箱</b><br><code>JunkChest</code></td>
<td align="center"><a href="art/JunkPile.png"><img src="art/JunkPile.png" width="64" alt="JunkPile"></a><br><b>垃圾堆</b><br><code>JunkPile</code></td>
<td align="center"><a href="art/Justice.png"><img src="art/Justice.png" width="64" alt="Justice"></a><br><b>正义</b><br><code>Justice</code></td>
</tr></table>


## L

<a id="group-l"></a>

<table><tr>
<td align="center"><a href="art/LeverShotgun.png"><img src="art/LeverShotgun.png" width="64" alt="LeverShotgun"></a><br><b>杠杆霰弹枪</b><br><code>LeverShotgun</code></td>
<td align="center"><a href="art/Life.png"><img src="art/Life.png" width="64" alt="Life"></a><br><b>生命</b><br><code>Life</code></td>
<td align="center"><a href="art/LifeTakerThreeThousand.png"><img src="art/LifeTakerThreeThousand.png" width="64" alt="LifeTakerThreeThousand"></a><br><b>要你命三千</b><br><code>LifeTakerThreeThousand</code></td>
<td align="center"><a href="art/LightingStrike.png"><img src="art/LightingStrike.png" width="64" alt="LightingStrike"></a><br><b>闪电打击</b><br><code>LightingStrike</code></td>
<td align="center"><a href="art/LimitBreak.png"><img src="art/LimitBreak.png" width="64" alt="LimitBreak"></a><br><b>突破极限</b><br><code>LimitBreak</code></td>
<td align="center"><a href="art/LinkedBlock.png"><img src="art/LinkedBlock.png" width="64" alt="LinkedBlock"></a><br><b>连还格挡</b><br><code>LinkedBlock</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/LinkedBlock.png"><img src="art/LinkedBlock.png" width="64" alt="LinkedPalms"></a><br><b>连还手</b><br><code>LinkedPalms</code></td>
<td align="center"><a href="art/LinkedBlock.png"><img src="art/LinkedBlock.png" width="64" alt="LinkedStrike"></a><br><b>连还打击</b><br><code>LinkedStrike</code></td>
<td align="center"><a href="art/Lizard.png"><img src="art/Lizard.png" width="64" alt="Lizard"></a><br><b>壁虎</b><br><code>Lizard</code></td>
<td align="center"><a href="art/LongView.png"><img src="art/LongView.png" width="64" alt="LongView"></a><br><b>长远目光</b><br><code>LongView</code></td>
<td align="center"><a href="art/LoomingPresence.png"><img src="art/LoomingPresence.png" width="64" alt="LoomingPresence"></a><br><b>浮光掠影</b><br><code>LoomingPresence</code></td>
<td align="center"><a href="art/LordOfWar.png"><img src="art/LordOfWar.png" width="64" alt="LordOfWar"></a><br><b>战争之王</b><br><code>LordOfWar</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Lore.png"><img src="art/Lore.png" width="64" alt="Lore"></a><br><b>传说</b><br><code>Lore</code></td>
<td align="center"><a href="art/LossAversion.png"><img src="art/LossAversion.png" width="64" alt="LossAversion"></a><br><b>损失规避</b><br><code>LossAversion</code></td>
<td align="center"><a href="art/BlackLotus.png"><img src="art/BlackLotus.png" width="64" alt="Lotus"></a><br><b>莲花</b><br><code>Lotus</code></td>
<td align="center"><a href="art/Lucky.png"><img src="art/Lucky.png" width="64" alt="Lucky"></a><br><b>幸运</b><br><code>Lucky</code></td>
<td align="center"><a href="art/LuckyCoin.png"><img src="art/LuckyCoin.png" width="64" alt="LuckyCoin"></a><br><b>幸运币</b><br><code>LuckyCoin</code></td>
<td align="center"><a href="art/LuckyStreak.png"><img src="art/LuckyStreak.png" width="64" alt="LuckyStreak"></a><br><b>好运连连</b><br><code>LuckyStreak</code></td>
</tr></table>


## M

<a id="group-m"></a>

<table><tr>
<td align="center"><a href="art/MagicSoil.png"><img src="art/MagicSoil.png" width="64" alt="MagicSoil"></a><br><b>魔法泥土</b><br><code>MagicSoil</code></td>
<td align="center"><a href="art/Maneuver.png"><img src="art/Maneuver.png" width="64" alt="Maneuver"></a><br><b>腾挪</b><br><code>Maneuver</code></td>
<td align="center"><a href="art/MarkOfBlue.png"><img src="art/MarkOfBlue.png" width="64" alt="MarkOfBlue"></a><br><b>蓝印记</b><br><code>MarkOfBlue</code></td>
<td align="center"><a href="art/MarkOfBlue.png"><img src="art/MarkOfBlue.png" width="64" alt="MarkOfGreen"></a><br><b>绿印记</b><br><code>MarkOfGreen</code></td>
<td align="center"><a href="art/MarkOfBlue.png"><img src="art/MarkOfBlue.png" width="64" alt="MarkOfPurple"></a><br><b>紫印记</b><br><code>MarkOfPurple</code></td>
<td align="center"><a href="art/MarkOfBlue.png"><img src="art/MarkOfBlue.png" width="64" alt="MarkOfRed"></a><br><b>红印记</b><br><code>MarkOfRed</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/MassProduction.png"><img src="art/MassProduction.png" width="64" alt="MassProduction"></a><br><b>大规模生产</b><br><code>MassProduction</code></td>
<td align="center"><a href="art/BalancedDriedFish.png"><img src="art/BalancedDriedFish.png" width="64" alt="MasterworkDriedFish"></a><br><b>极品咸鱼干</b><br><code>MasterworkDriedFish</code></td>
<td align="center"><a href="art/MatchPoint.png"><img src="art/MatchPoint.png" width="64" alt="MatchPoint"></a><br><b>赛末点</b><br><code>MatchPoint</code></td>
<td align="center"><a href="art/MazesEnd.png"><img src="art/MazesEnd.png" width="64" alt="MazesEnd"></a><br><b>迷宫终点</b><br><code>MazesEnd</code></td>
<td align="center"><a href="art/Memorandum.png"><img src="art/Memorandum.png" width="64" alt="Memorandum"></a><br><b>备忘夹</b><br><code>Memorandum</code></td>
<td align="center"><a href="art/MentalDisturbance.png"><img src="art/MentalDisturbance.png" width="64" alt="MentalDisturbance"></a><br><b>精神污染</b><br><code>MentalDisturbance</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/MetalParts.png"><img src="art/MetalParts.png" width="64" alt="MetalParts"></a><br><b>金属零件</b><br><code>MetalParts</code></td>
<td align="center"><a href="art/MeteorSwarm.png"><img src="art/MeteorSwarm.png" width="64" alt="MeteorSwarm"></a><br><b>陨星暴</b><br><code>MeteorSwarm</code></td>
<td align="center"><a href="art/Mimic.png"><img src="art/Mimic.png" width="64" alt="Mimic"></a><br><b>拟态</b><br><code>Mimic</code></td>
<td align="center"><a href="art/MindSpike.png"><img src="art/MindSpike.png" width="64" alt="MindSpike"></a><br><b>心灵冲击</b><br><code>MindSpike</code></td>
<td align="center"><a href="art/MistyStep.png"><img src="art/MistyStep.png" width="64" alt="MistyStep"></a><br><b>迷踪步</b><br><code>MistyStep</code></td>
<td align="center"><a href="art/Mitosis.png"><img src="art/Mitosis.png" width="64" alt="Mitosis"></a><br><b>有丝分裂</b><br><code>Mitosis</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Mjollnir.png"><img src="art/Mjollnir.png" width="64" alt="Mjollnir"></a><br><b>妙尔尼尔</b><br><code>Mjollnir</code></td>
<td align="center"><a href="art/Molotov.png"><img src="art/Molotov.png" width="64" alt="Molotov"></a><br><b>燃烧瓶</b><br><code>Molotov</code></td>
<td align="center"><a href="art/Monolith.png"><img src="art/Monolith.png" width="64" alt="Monolith"></a><br><b>磐石</b><br><code>Monolith</code></td>
<td align="center"><a href="art/Monosword.png"><img src="art/Monosword.png" width="64" alt="Monosword"></a><br><b>单分子剑</b><br><code>Monosword</code></td>
<td align="center"><a href="art/MountainLean.png"><img src="art/MountainLean.png" width="64" alt="MountainLean"></a><br><b>铁山靠</b><br><code>MountainLean</code></td>
<td align="center"><a href="art/Mozambique.png"><img src="art/Mozambique.png" width="64" alt="Mozambique"></a><br><b>莫桑比克</b><br><code>Mozambique</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/MultiStrike.png"><img src="art/MultiStrike.png" width="64" alt="MultiStrike"></a><br><b>连续打击</b><br><code>MultiStrike</code></td>
<td align="center"><a href="art/MuscleSmash.png"><img src="art/MuscleSmash.png" width="64" alt="MuscleSmash"></a><br><b>肌肉猛击</b><br><code>MuscleSmash</code></td>
<td align="center"><a href="art/MushroomBlue.png"><img src="art/MushroomBlue.png" width="64" alt="MushroomBlue"></a><br><b>蓝蘑菇</b><br><code>MushroomBlue</code></td>
<td align="center"><a href="art/MushroomBlue.png"><img src="art/MushroomBlue.png" width="64" alt="MushroomGreen"></a><br><b>绿蘑菇</b><br><code>MushroomGreen</code></td>
<td align="center"><a href="art/MushroomBlue.png"><img src="art/MushroomBlue.png" width="64" alt="MushroomRed"></a><br><b>红蘑菇</b><br><code>MushroomRed</code></td>
<td align="center"><a href="art/Mutation.png"><img src="art/Mutation.png" width="64" alt="Mutation"></a><br><b>变异</b><br><code>Mutation</code></td>
</tr></table>


## N

<a id="group-n"></a>

<table><tr>
<td align="center"><a href="art/NailGun.png"><img src="art/NailGun.png" width="64" alt="NailGun"></a><br><b>射钉枪</b><br><code>NailGun</code></td>
<td align="center"><a href="art/NailGun.png"><img src="art/NailGun.png" width="64" alt="NailGunReload"></a><br><b>射钉枪-装填</b><br><code>NailGunReload</code></td>
<td align="center"><a href="art/NasalGoo.png"><img src="art/NasalGoo.png" width="64" alt="NasalGoo"></a><br><b>黏鼻涕</b><br><code>NasalGoo</code></td>
<td align="center"><a href="art/NaturalRitual.png"><img src="art/NaturalRitual.png" width="64" alt="NaturalRitual"></a><br><b>大自然仪式</b><br><code>NaturalRitual</code></td>
<td align="center"><a href="art/Negation.png"><img src="art/Negation.png" width="64" alt="Negation"></a><br><b>逸散</b><br><code>Negation</code></td>
<td align="center"><a href="art/Nemesis.png"><img src="art/Nemesis.png" width="64" alt="Nemesis"></a><br><b>报应</b><br><code>Nemesis</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Buff.png"><img src="art/Buff.png" width="64" alt="Nerf"></a><br><b>削弱</b><br><code>Nerf</code></td>
<td align="center"><a href="art/Newborn.png"><img src="art/Newborn.png" width="64" alt="Newborn"></a><br><b>新生</b><br><code>Newborn</code></td>
<td align="center"><a href="art/NewPerspective.png"><img src="art/NewPerspective.png" width="64" alt="NewPerspective"></a><br><b>新视角</b><br><code>NewPerspective</code></td>
<td align="center"><a href="art/Nichijou.png"><img src="art/Nichijou.png" width="64" alt="Nichijou"></a><br><b>日常</b><br><code>Nichijou</code></td>
<td align="center"><a href="art/Night.png"><img src="art/Night.png" width="64" alt="Night"></a><br><b>黑夜</b><br><code>Night</code></td>
</tr></table>


## O

<a id="group-o"></a>

<table><tr>
<td align="center"><a href="art/Obsession.png"><img src="art/Obsession.png" width="64" alt="Obsession"></a><br><b>痴迷</b><br><code>Obsession</code></td>
<td align="center"><a href="art/OddOrEven.png"><img src="art/OddOrEven.png" width="64" alt="OddOrEven"></a><br><b>或奇或偶</b><br><code>OddOrEven</code></td>
<td align="center"><a href="art/Offering.png"><img src="art/Offering.png" width="64" alt="Offering"></a><br><b>祭品</b><br><code>Offering</code></td>
<td align="center"><a href="art/OminousMist.png"><img src="art/OminousMist.png" width="64" alt="OminousMist"></a><br><b>不祥黑雾</b><br><code>OminousMist</code></td>
<td align="center"><a href="art/Omniscience.png"><img src="art/Omniscience.png" width="64" alt="Omniscience"></a><br><b>全知</b><br><code>Omniscience</code></td>
<td align="center"><a href="art/GoodStrike.png"><img src="art/GoodStrike.png" width="64" alt="OmniStrike"></a><br><b>无敌打击</b><br><code>OmniStrike</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/OnCallService.png"><img src="art/OnCallService.png" width="64" alt="OnCallService"></a><br><b>随叫随到</b><br><code>OnCallService</code></td>
<td align="center"><a href="art/CrossBow.png"><img src="art/CrossBow.png" width="64" alt="One Time Crossbow"></a><br><b>一次性十字弩</b><br><code>One Time Crossbow</code></td>
<td align="center"><a href="art/OneInchPunch.png"><img src="art/OneInchPunch.png" width="64" alt="OneInchPunch"></a><br><b>寸劲</b><br><code>OneInchPunch</code></td>
<td align="center"><a href="art/Onslaught.png"><img src="art/Onslaught.png" width="64" alt="Onslaught"></a><br><b>攻势</b><br><code>Onslaught</code></td>
<td align="center"><a href="art/OrganGun.png"><img src="art/OrganGun.png" width="64" alt="OrganGun"></a><br><b>风琴炮</b><br><code>OrganGun</code></td>
<td align="center"><a href="art/OrganGun.png"><img src="art/OrganGun.png" width="64" alt="OrganGunReload"></a><br><b>风琴炮-装填</b><br><code>OrganGunReload</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/OrientedStupidity.png"><img src="art/OrientedStupidity.png" width="64" alt="OrientedStupidity"></a><br><b>愚蠢术</b><br><code>OrientedStupidity</code></td>
<td align="center"><a href="art/Originalize.png"><img src="art/Originalize.png" width="64" alt="Originalize"></a><br><b>归原术</b><br><code>Originalize</code></td>
<td align="center"><a href="art/Filtering.png"><img src="art/Filtering.png" width="64" alt="Oscillation"></a><br><b>震荡</b><br><code>Oscillation</code></td>
<td align="center"><a href="art/OutdatedDrink.png"><img src="art/OutdatedDrink.png" width="64" alt="OutdatedDrink"></a><br><b>过期饮料</b><br><code>OutdatedDrink</code></td>
<td align="center"><a href="art/OverwhelmingSplendor.png"><img src="art/OverwhelmingSplendor.png" width="64" alt="OverwhelmingSplendor"></a><br><b>非凡绝景</b><br><code>OverwhelmingSplendor</code></td>
</tr></table>


## P

<a id="group-p"></a>

<table><tr>
<td align="center"><a href="art/Pack.png"><img src="art/Pack.png" width="64" alt="Pack"></a><br><b>膏药</b><br><code>Pack</code></td>
<td align="center"><a href="art/PainKiller.png"><img src="art/PainKiller.png" width="64" alt="PainKiller"></a><br><b>止痛药</b><br><code>PainKiller</code></td>
<td align="center"><a href="art/Pandorasbox.png"><img src="art/Pandorasbox.png" width="64" alt="Pandorasbox"></a><br><b>潘多拉之盒</b><br><code>Pandorasbox</code></td>
<td align="center"><a href="art/PaperBall.png"><img src="art/PaperBall.png" width="64" alt="PaperBall"></a><br><b>纸团</b><br><code>PaperBall</code></td>
<td align="center"><a href="art/Parade.png"><img src="art/Parade.png" width="64" alt="Parade"></a><br><b>游行庆典</b><br><code>Parade</code></td>
<td align="center"><a href="art/ParadoxEngine.png"><img src="art/ParadoxEngine.png" width="64" alt="ParadoxEngine"></a><br><b>悖论引擎</b><br><code>ParadoxEngine</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Paragon.png"><img src="art/Paragon.png" width="64" alt="Paragon"></a><br><b>典范</b><br><code>Paragon</code></td>
<td align="center"><a href="art/Paranoid.png"><img src="art/Paranoid.png" width="64" alt="Paranoid"></a><br><b>偏执狂</b><br><code>Paranoid</code></td>
<td align="center"><a href="art/Parry.png"><img src="art/Parry.png" width="64" alt="Parry"></a><br><b>招架</b><br><code>Parry</code></td>
<td align="center"><a href="art/ParthianShot.png"><img src="art/ParthianShot.png" width="64" alt="ParthianShot"></a><br><b>回马箭</b><br><code>ParthianShot</code></td>
<td align="center"><a href="art/Payback.png"><img src="art/Payback.png" width="64" alt="Payback"></a><br><b>奉还</b><br><code>Payback</code></td>
<td align="center"><a href="art/Perfecting.png"><img src="art/Perfecting.png" width="64" alt="Perfecting"></a><br><b>精益求精</b><br><code>Perfecting</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/PerfectStick.png"><img src="art/PerfectStick.png" width="64" alt="PerfectStick"></a><br><b>完美木棍剑</b><br><code>PerfectStick</code></td>
<td align="center"><a href="art/Phantom.png"><img src="art/Phantom.png" width="64" alt="Phantom"></a><br><b>幻影</b><br><code>Phantom</code></td>
<td align="center"><a href="art/Filtering.png"><img src="art/Filtering.png" width="64" alt="PhasePower"></a><br><b>能量</b><br><code>PhasePower</code></td>
<td align="center"><a href="art/PhoenixBag.png"><img src="art/PhoenixBag.png" width="64" alt="PhoenixBag"></a><br><b>大飞鸟治疗包</b><br><code>PhoenixBag</code></td>
<td align="center"><a href="art/PigRush.png"><img src="art/PigRush.png" width="64" alt="PigRush"></a><br><b>猪突</b><br><code>PigRush</code></td>
<td align="center"><a href="art/Pipe.png"><img src="art/Pipe.png" width="64" alt="Pipe"></a><br><b>铁管</b><br><code>Pipe</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/PipeGun.png"><img src="art/PipeGun.png" width="64" alt="PipeGun"></a><br><b>铁管枪</b><br><code>PipeGun</code></td>
<td align="center"><a href="art/PlanB.png"><img src="art/PlanB.png" width="64" alt="PlanB"></a><br><b>后备方案</b><br><code>PlanB</code></td>
<td align="center"><a href="art/PlotTwist.png"><img src="art/PlotTwist.png" width="64" alt="PlotTwist"></a><br><b>震撼反转</b><br><code>PlotTwist</code></td>
<td align="center"><a href="art/Ponder.png"><img src="art/Ponder.png" width="64" alt="Ponder"></a><br><b>沉思</b><br><code>Ponder</code></td>
<td align="center"><a href="art/PortableBonfires.png"><img src="art/PortableBonfires.png" width="64" alt="PortableBonfires"></a><br><b>篝火</b><br><code>PortableBonfires</code></td>
<td align="center"><a href="art/PoweredArmor.png"><img src="art/PoweredArmor.png" width="64" alt="PoweredArmor"></a><br><b>动力装甲</b><br><code>PoweredArmor</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Prey.png"><img src="art/Prey.png" width="64" alt="Prey"></a><br><b>掠食</b><br><code>Prey</code></td>
<td align="center"><a href="art/Prism.png"><img src="art/Prism.png" width="64" alt="Prism"></a><br><b>三棱镜</b><br><code>Prism</code></td>
<td align="center"><a href="art/Crossroad.png"><img src="art/Crossroad.png" width="64" alt="PrologueCrossroad"></a><br><b>歧路</b><br><code>PrologueCrossroad</code></td>
<td align="center"><a href="art/Dice.png"><img src="art/Dice.png" width="64" alt="PrologueDice"></a><br><b>至尊骰</b><br><code>PrologueDice</code></td>
<td align="center"><a href="art/PrologueImprovise.png"><img src="art/PrologueImprovise.png" width="64" alt="PrologueImprovise"></a><br><b>随机应变</b><br><code>PrologueImprovise</code></td>
<td align="center"><a href="art/PromisedEnd.png"><img src="art/PromisedEnd.png" width="64" alt="PromisedEnd"></a><br><b>绝望终局</b><br><code>PromisedEnd</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/PunchingTheAir.png"><img src="art/PunchingTheAir.png" width="64" alt="PunchingTheAir"></a><br><b>刮痧术</b><br><code>PunchingTheAir</code></td>
<td align="center"><a href="art/PureAnger.png"><img src="art/PureAnger.png" width="64" alt="PureAnger"></a><br><b>急</b><br><code>PureAnger</code></td>
<td align="center"><a href="art/PureElement.png"><img src="art/PureElement.png" width="64" alt="PureElement"></a><br><b>纯粹元素</b><br><code>PureElement</code></td>
<td align="center"><a href="art/Purify.png"><img src="art/Purify.png" width="64" alt="Purify"></a><br><b>净化</b><br><code>Purify</code></td>
<td align="center"><a href="art/PurifyingHammer.png"><img src="art/PurifyingHammer.png" width="64" alt="PurifyingHammer"></a><br><b>净化之锤</b><br><code>PurifyingHammer</code></td>
</tr></table>


## Q

<a id="group-q"></a>

<table><tr>
<td align="center"><a href="art/QuillSpray.png"><img src="art/QuillSpray.png" width="64" alt="QuillSpray"></a><br><b>针刺扫射</b><br><code>QuillSpray</code></td>
</tr></table>


## R

<a id="group-r"></a>

<table><tr>
<td align="center"><a href="art/Filtering.png"><img src="art/Filtering.png" width="64" alt="RandomBuff"></a><br><b>正向</b><br><code>RandomBuff</code></td>
<td align="center"><a href="art/Filtering.png"><img src="art/Filtering.png" width="64" alt="RandomDeBuff"></a><br><b>负向</b><br><code>RandomDeBuff</code></td>
<td align="center"><a href="art/RazzleDazzle.png"><img src="art/RazzleDazzle.png" width="64" alt="RazzleDazzle"></a><br><b>眼花缭乱</b><br><code>RazzleDazzle</code></td>
<td align="center"><a href="art/RecklessCharge.png"><img src="art/RecklessCharge.png" width="64" alt="RecklessCharge"></a><br><b>无谋冲锋</b><br><code>RecklessCharge</code></td>
<td align="center"><a href="art/RecoillessRifle.png"><img src="art/RecoillessRifle.png" width="64" alt="RecoillessRifle"></a><br><b>无后坐力炮</b><br><code>RecoillessRifle</code></td>
<td align="center"><a href="art/Reconcile.png"><img src="art/Reconcile.png" width="64" alt="Reconcile"></a><br><b>和解</b><br><code>Reconcile</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Reconsider.png"><img src="art/Reconsider.png" width="64" alt="Reconsider"></a><br><b>重理思路</b><br><code>Reconsider</code></td>
<td align="center"><a href="art/Recycle.png"><img src="art/Recycle.png" width="64" alt="Recycle"></a><br><b>回收</b><br><code>Recycle</code></td>
<td align="center"><a href="art/RedFlask.png"><img src="art/RedFlask.png" width="64" alt="RedFlask"></a><br><b>红元素瓶</b><br><code>RedFlask</code></td>
<td align="center"><a href="art/Grenade.png"><img src="art/Grenade.png" width="64" alt="RedHatBomb"></a><br><b>烫手炸弹！</b><br><code>RedHatBomb</code></td>
<td align="center"><a href="art/Reinforcement.png"><img src="art/Reinforcement.png" width="64" alt="Reinforcement"></a><br><b>加固板</b><br><code>Reinforcement</code></td>
<td align="center"><a href="art/Release.png"><img src="art/Release.png" width="64" alt="Release"></a><br><b>宣泄</b><br><code>Release</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/RepulsionField.png"><img src="art/RepulsionField.png" width="64" alt="RepulsionField"></a><br><b>拒斥力场</b><br><code>RepulsionField</code></td>
<td align="center"><a href="art/Research.png"><img src="art/Research.png" width="64" alt="Research"></a><br><b>钻研</b><br><code>Research</code></td>
<td align="center"><a href="art/Revolver.png"><img src="art/Revolver.png" width="64" alt="Revolver"></a><br><b>转轮手枪</b><br><code>Revolver</code></td>
<td align="center"><a href="art/Revolver.png"><img src="art/Revolver.png" width="64" alt="RevolverReload"></a><br><b>转轮手枪-装填</b><br><code>RevolverReload</code></td>
<td align="center"><a href="art/RiseAndFall.png"><img src="art/RiseAndFall.png" width="64" alt="RiseAndFall"></a><br><b>跌宕起伏</b><br><code>RiseAndFall</code></td>
<td align="center"><a href="art/Ritual.png"><img src="art/Ritual.png" width="64" alt="Ritual"></a><br><b>仪式</b><br><code>Ritual</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/RitualDagger.png"><img src="art/RitualDagger.png" width="64" alt="RitualDagger"></a><br><b>仪式匕首</b><br><code>RitualDagger</code></td>
<td align="center"><a href="art/Roar.png"><img src="art/Roar.png" width="64" alt="Roar"></a><br><b>怒吼</b><br><code>Roar</code></td>
<td align="center"><a href="art/RoboticArm.png"><img src="art/RoboticArm.png" width="64" alt="RoboticArm"></a><br><b>机械臂</b><br><code>RoboticArm</code></td>
<td align="center"><a href="art/RockItLauncher.png"><img src="art/RockItLauncher.png" width="64" alt="RockItLauncher"></a><br><b>垃圾喷射器</b><br><code>RockItLauncher</code></td>
<td align="center"><a href="art/RockItLauncherReload.png"><img src="art/RockItLauncherReload.png" width="64" alt="RockItLauncherReload"></a><br><b>垃圾喷射器-装填</b><br><code>RockItLauncherReload</code></td>
<td align="center"><a href="art/Roulette.png"><img src="art/Roulette.png" width="64" alt="Roulette"></a><br><b>轮盘赌</b><br><code>Roulette</code></td>
</tr></table>


## S

<a id="group-s"></a>

<table><tr>
<td align="center"><a href="art/SandThrow.png"><img src="art/SandThrow.png" width="64" alt="SandThrow"></a><br><b>飞沙</b><br><code>SandThrow</code></td>
<td align="center"><a href="art/GoodStrike.png"><img src="art/GoodStrike.png" width="64" alt="SavageStrike"></a><br><b>凶残打击</b><br><code>SavageStrike</code></td>
<td align="center"><a href="art/SaveLoadCurse.png"><img src="art/SaveLoadCurse.png" width="64" alt="SaveLoadCurse"></a><br><b>读档之诅咒</b><br><code>SaveLoadCurse</code></td>
<td align="center"><a href="art/Scavage.png"><img src="art/Scavage.png" width="64" alt="Scavage"></a><br><b>拾荒</b><br><code>Scavage</code></td>
<td align="center"><a href="art/Scavenger.png"><img src="art/Scavenger.png" width="64" alt="Scavenger"></a><br><b>食腐</b><br><code>Scavenger</code></td>
<td align="center"><a href="art/ScorchedEarth.png"><img src="art/ScorchedEarth.png" width="64" alt="ScorchedEarth"></a><br><b>焦土</b><br><code>ScorchedEarth</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Scratch.png"><img src="art/Scratch.png" width="64" alt="Scratch"></a><br><b>挠伤</b><br><code>Scratch</code></td>
<td align="center"><a href="art/ScrochedBook.png"><img src="art/ScrochedBook.png" width="64" alt="ScrochedBook"></a><br><b>烧焦书籍</b><br><code>ScrochedBook</code></td>
<td align="center"><a href="art/SeeThrough.png"><img src="art/SeeThrough.png" width="64" alt="SeeThrough"></a><br><b>看破</b><br><code>SeeThrough</code></td>
<td align="center"><a href="art/ShardOfGlass.png"><img src="art/ShardOfGlass.png" width="64" alt="ShardOfGlass"></a><br><b>玻璃碎片</b><br><code>ShardOfGlass</code></td>
<td align="center"><a href="art/SharkTyphoon.png"><img src="art/SharkTyphoon.png" width="64" alt="SharkTyphoon"></a><br><b>鲨龙卷</b><br><code>SharkTyphoon</code></td>
<td align="center"><a href="art/ShieldAttack.png"><img src="art/ShieldAttack.png" width="64" alt="ShieldAttack"></a><br><b>枪盾</b><br><code>ShieldAttack</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/ShitLuck.png"><img src="art/ShitLuck.png" width="64" alt="ShitLuck"></a><br><b>狗屎运</b><br><code>ShitLuck</code></td>
<td align="center"><a href="art/ShockWave.png"><img src="art/ShockWave.png" width="64" alt="ShockWave"></a><br><b>冲击波</b><br><code>ShockWave</code></td>
<td align="center"><a href="art/SiegeTent.png"><img src="art/SiegeTent.png" width="64" alt="SiegeTent"></a><br><b>攻城帐篷</b><br><code>SiegeTent</code></td>
<td align="center"><a href="art/Sleep.png"><img src="art/Sleep.png" width="64" alt="Sleep"></a><br><b>睡觉觉</b><br><code>Sleep</code></td>
<td align="center"><a href="art/SleightOfHand.png"><img src="art/SleightOfHand.png" width="64" alt="SleightOfHand"></a><br><b>变戏法</b><br><code>SleightOfHand</code></td>
<td align="center"><a href="art/Slime.png"><img src="art/Slime.png" width="64" alt="Slime"></a><br><b>粘液</b><br><code>Slime</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Sling.png"><img src="art/Sling.png" width="64" alt="Sling"></a><br><b>投石索</b><br><code>Sling</code></td>
<td align="center"><a href="art/Slingshot.png"><img src="art/Slingshot.png" width="64" alt="Slingshot"></a><br><b>弹弓</b><br><code>Slingshot</code></td>
<td align="center"><a href="art/SlowSword.png"><img src="art/SlowSword.png" width="64" alt="SlowSword"></a><br><b>缓慢剑</b><br><code>SlowSword</code></td>
<td align="center"><a href="art/SmallRock.png"><img src="art/SmallRock.png" width="64" alt="SmallRock"></a><br><b>小石头</b><br><code>SmallRock</code></td>
<td align="center"><a href="art/Smoker.png"><img src="art/Smoker.png" width="64" alt="Smoker"></a><br><b>喷烟器</b><br><code>Smoker</code></td>
<td align="center"><a href="art/SmokerReload.png"><img src="art/SmokerReload.png" width="64" alt="SmokerReload"></a><br><b>喷烟器-装填</b><br><code>SmokerReload</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/SniperRifle.png"><img src="art/SniperRifle.png" width="64" alt="SniperRifle"></a><br><b>狙击枪</b><br><code>SniperRifle</code></td>
<td align="center"><a href="art/SortedOut.png"><img src="art/SortedOut.png" width="64" alt="SortedOut"></a><br><b>安排明白</b><br><code>SortedOut</code></td>
<td align="center"><a href="art/FireElement.png"><img src="art/FireElement.png" width="64" alt="SpaceFireElement"></a><br><b>星际火元素</b><br><code>SpaceFireElement</code></td>
<td align="center"><a href="art/Sparks.png"><img src="art/Sparks.png" width="64" alt="Sparks"></a><br><b>火花</b><br><code>Sparks</code></td>
<td align="center"><a href="art/Grenade.png"><img src="art/Grenade.png" width="64" alt="SpecialGrenade"></a><br><b>特制手榴弹</b><br><code>SpecialGrenade</code></td>
<td align="center"><a href="art/Spike.png"><img src="art/Spike.png" width="64" alt="Spike"></a><br><b>尖刺</b><br><code>Spike</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/SpikeTrap.png"><img src="art/SpikeTrap.png" width="64" alt="SpikeTrap"></a><br><b>尖刺陷阱</b><br><code>SpikeTrap</code></td>
<td align="center"><a href="art/SplinterTwin.png"><img src="art/SplinterTwin.png" width="64" alt="SplinterTwin"></a><br><b>分裂双身</b><br><code>SplinterTwin</code></td>
<td align="center"><a href="art/Stance.png"><img src="art/Stance.png" width="64" alt="Stance"></a><br><b>架势</b><br><code>Stance</code></td>
<td align="center"><a href="art/StandardizedSummon.png"><img src="art/StandardizedSummon.png" width="64" alt="StandardizedSummon"></a><br><b>标准化召唤</b><br><code>StandardizedSummon</code></td>
<td align="center"><a href="art/SteamEngine.png"><img src="art/SteamEngine.png" width="64" alt="SteamEngine"></a><br><b>蒸汽引擎</b><br><code>SteamEngine</code></td>
<td align="center"><a href="art/SteamTank.png"><img src="art/SteamTank.png" width="64" alt="SteamTank"></a><br><b>蒸汽坦克</b><br><code>SteamTank</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Stellaris.png"><img src="art/Stellaris.png" width="64" alt="Stellaris"></a><br><b>群星</b><br><code>Stellaris</code></td>
<td align="center"><a href="art/StillWater.png"><img src="art/StillWater.png" width="64" alt="StillWater"></a><br><b>止水</b><br><code>StillWater</code></td>
<td align="center"><a href="art/Stockpile.png"><img src="art/Stockpile.png" width="64" alt="Stockpile"></a><br><b>囤积</b><br><code>Stockpile</code></td>
<td align="center"><a href="art/StoneAxe.png"><img src="art/StoneAxe.png" width="64" alt="StoneAxe"></a><br><b>石斧头</b><br><code>StoneAxe</code></td>
<td align="center"><a href="art/StoreFighter.png"><img src="art/StoreFighter.png" width="64" alt="StoreFighter"></a><br><b>百货战神</b><br><code>StoreFighter</code></td>
<td align="center"><a href="art/GoodStrike.png"><img src="art/GoodStrike.png" width="64" alt="StraightSwordPlusNine"></a><br><b>直剑+9</b><br><code>StraightSwordPlusNine</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Strength.png"><img src="art/Strength.png" width="64" alt="Strength"></a><br><b>力量</b><br><code>Strength</code></td>
<td align="center"><a href="art/Strengthen.png"><img src="art/Strengthen.png" width="64" alt="Strengthen"></a><br><b>变强</b><br><code>Strengthen</code></td>
<td align="center"><a href="art/GoodStrike.png"><img src="art/GoodStrike.png" width="64" alt="Strike"></a><br><b>打击</b><br><code>Strike</code></td>
<td align="center"><a href="art/GoodStrike.png"><img src="art/GoodStrike.png" width="64" alt="StrikeNormal"></a><br><b>普通打击</b><br><code>StrikeNormal</code></td>
<td align="center"><a href="art/GoodStrike.png"><img src="art/GoodStrike.png" width="64" alt="StrikeRare"></a><br><b>传说打击</b><br><code>StrikeRare</code></td>
<td align="center"><a href="art/GoodStrike.png"><img src="art/GoodStrike.png" width="64" alt="StrikeUncommon"></a><br><b>稀有打击</b><br><code>StrikeUncommon</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/StunFist.png"><img src="art/StunFist.png" width="64" alt="StunFist"></a><br><b>震慑拳</b><br><code>StunFist</code></td>
<td align="center"><a href="art/StupidFist.png"><img src="art/StupidFist.png" width="64" alt="StupidFist"></a><br><b>没脑子拳</b><br><code>StupidFist</code></td>
<td align="center"><a href="art/Sufficient%20Supplies.png"><img src="art/Sufficient%20Supplies.png" width="64" alt="Sufficient Supplies"></a><br><b>充足物资</b><br><code>Sufficient Supplies</code></td>
<td align="center"><a href="art/SuperiorFirepower.png"><img src="art/SuperiorFirepower.png" width="64" alt="SuperiorFirepower"></a><br><b>优势火力</b><br><code>SuperiorFirepower</code></td>
<td align="center"><a href="art/Supernova.png"><img src="art/Supernova.png" width="64" alt="Supernova"></a><br><b>超新星</b><br><code>Supernova</code></td>
<td align="center"><a href="art/Superposition.png"><img src="art/Superposition.png" width="64" alt="Superposition"></a><br><b>叠加态</b><br><code>Superposition</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/SurgicalExtraction.png"><img src="art/SurgicalExtraction.png" width="64" alt="SurgicalExtraction"></a><br><b>手术摘除</b><br><code>SurgicalExtraction</code></td>
<td align="center"><a href="art/SurvivalOfTheFittest.png"><img src="art/SurvivalOfTheFittest.png" width="64" alt="SurvivalOfTheFittest"></a><br><b>适者生存</b><br><code>SurvivalOfTheFittest</code></td>
<td align="center"><a href="art/Swarm.png"><img src="art/Swarm.png" width="64" alt="Swarm"></a><br><b>虫群</b><br><code>Swarm</code></td>
<td align="center"><a href="art/Swipe.png"><img src="art/Swipe.png" width="64" alt="Swipe"></a><br><b>横扫</b><br><code>Swipe</code></td>
<td align="center"><a href="art/SwordToPlowshares.png"><img src="art/SwordToPlowshares.png" width="64" alt="SwordToPlowshares"></a><br><b>化剑为犁</b><br><code>SwordToPlowshares</code></td>
</tr></table>


## T

<a id="group-t"></a>

<table><tr>
<td align="center"><a href="art/TachyonLance.png"><img src="art/TachyonLance.png" width="64" alt="TachyonLance"></a><br><b>速子长矛</b><br><code>TachyonLance</code></td>
<td align="center"><a href="art/TacPlan.png"><img src="art/TacPlan.png" width="64" alt="TacPlan"></a><br><b>计划战术</b><br><code>TacPlan</code></td>
<td align="center"><a href="art/TacticReload.png"><img src="art/TacticReload.png" width="64" alt="TacticReload"></a><br><b>战术上弹</b><br><code>TacticReload</code></td>
<td align="center"><a href="art/TaiJi.png"><img src="art/TaiJi.png" width="64" alt="TaiJi"></a><br><b>太极</b><br><code>TaiJi</code></td>
<td align="center"><a href="art/TakeCover.png"><img src="art/TakeCover.png" width="64" alt="TakeCover"></a><br><b>寻找掩蔽</b><br><code>TakeCover</code></td>
<td align="center"><a href="art/TarotDeck.png"><img src="art/TarotDeck.png" width="64" alt="TarotDeck"></a><br><b>塔牌盒</b><br><code>TarotDeck</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/BalancedDriedFish.png"><img src="art/BalancedDriedFish.png" width="64" alt="TemperedDriedFish"></a><br><b>回火的咸鱼干</b><br><code>TemperedDriedFish</code></td>
<td align="center"><a href="art/TensionAndRelaxation.png"><img src="art/TensionAndRelaxation.png" width="64" alt="TensionAndRelaxation"></a><br><b>张弛</b><br><code>TensionAndRelaxation</code></td>
<td align="center"><a href="art/TeslaCoil.png"><img src="art/TeslaCoil.png" width="64" alt="TeslaCoil"></a><br><b>磁暴线圈</b><br><code>TeslaCoil</code></td>
<td align="center">（无图）<br><b>测试牌</b><br><code>Test</code><br><sub>卡没有任何卡面图（CardSo.cardArt 为 null）</sub></td>
<td align="center"><a href="art/ThatsHandy.png"><img src="art/ThatsHandy.png" width="64" alt="ThatsHandy"></a><br><b>这个顺手</b><br><code>ThatsHandy</code></td>
<td align="center"><a href="art/ThatsRight.png"><img src="art/ThatsRight.png" width="64" alt="ThatsRight"></a><br><b>对的对的</b><br><code>ThatsRight</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/ThatsWrong.png"><img src="art/ThatsWrong.png" width="64" alt="ThatsWrong"></a><br><b>不对不对</b><br><code>ThatsWrong</code></td>
<td align="center"><a href="art/TheAltar.png"><img src="art/TheAltar.png" width="64" alt="TheAltar"></a><br><b>祭坛</b><br><code>TheAltar</code></td>
<td align="center"><a href="art/TheBadCompanions.png"><img src="art/TheBadCompanions.png" width="64" alt="TheBadCompanions"></a><br><b>坑人伙伴</b><br><code>TheBadCompanions</code></td>
<td align="center"><a href="art/TheChariot.png"><img src="art/TheChariot.png" width="64" alt="TheChariot"></a><br><b>战车</b><br><code>TheChariot</code></td>
<td align="center"><a href="art/TheCursedIdol.png"><img src="art/TheCursedIdol.png" width="64" alt="TheCursedIdol"></a><br><b>诅咒神像</b><br><code>TheCursedIdol</code></td>
<td align="center"><a href="art/TheEmperor.png"><img src="art/TheEmperor.png" width="64" alt="TheEmperor"></a><br><b>皇帝</b><br><code>TheEmperor</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/TheExplosionSpell.png"><img src="art/TheExplosionSpell.png" width="64" alt="TheExplosionSpell"></a><br><b>大爆裂术</b><br><code>TheExplosionSpell</code></td>
<td align="center"><a href="art/TheFool.png"><img src="art/TheFool.png" width="64" alt="TheFool"></a><br><b>愚者</b><br><code>TheFool</code></td>
<td align="center"><a href="art/TheHangedMan.png"><img src="art/TheHangedMan.png" width="64" alt="TheHangedMan"></a><br><b>倒吊人</b><br><code>TheHangedMan</code></td>
<td align="center"><a href="art/TheHermit.png"><img src="art/TheHermit.png" width="64" alt="TheHermit"></a><br><b>隐者</b><br><code>TheHermit</code></td>
<td align="center"><a href="art/TheJudgement.png"><img src="art/TheJudgement.png" width="64" alt="TheJudgement"></a><br><b>审判</b><br><code>TheJudgement</code></td>
<td align="center"><a href="art/TheKingsFoot.png"><img src="art/TheKingsFoot.png" width="64" alt="TheKingsFoot"></a><br><b>国王之脚</b><br><code>TheKingsFoot</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/TheLovers.png"><img src="art/TheLovers.png" width="64" alt="TheLovers"></a><br><b>恋人</b><br><code>TheLovers</code></td>
<td align="center"><a href="art/TheMagician.png"><img src="art/TheMagician.png" width="64" alt="TheMagician"></a><br><b>魔术师</b><br><code>TheMagician</code></td>
<td align="center"><a href="art/TheMoon.png"><img src="art/TheMoon.png" width="64" alt="TheMoon"></a><br><b>月亮</b><br><code>TheMoon</code></td>
<td align="center"><a href="art/TheMythicalManMonth.png"><img src="art/TheMythicalManMonth.png" width="64" alt="TheMythicalManMonth"></a><br><b>人月神话</b><br><code>TheMythicalManMonth</code></td>
<td align="center"><a href="art/TheNexus.png"><img src="art/TheNexus.png" width="64" alt="TheNexus"></a><br><b>连接点</b><br><code>TheNexus</code></td>
<td align="center"><a href="art/TheOldWay.png"><img src="art/TheOldWay.png" width="64" alt="TheOldWay"></a><br><b>遵循古道</b><br><code>TheOldWay</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/TheStabilizer.png"><img src="art/TheStabilizer.png" width="64" alt="TheStabilizer"></a><br><b>稳定器</b><br><code>TheStabilizer</code></td>
<td align="center"><a href="art/TheStar.png"><img src="art/TheStar.png" width="64" alt="TheStar"></a><br><b>星星</b><br><code>TheStar</code></td>
<td align="center"><a href="art/TheStupidWay.png"><img src="art/TheStupidWay.png" width="64" alt="TheStupidWay"></a><br><b>傻瓜之道</b><br><code>TheStupidWay</code></td>
<td align="center"><a href="art/TheSun.png"><img src="art/TheSun.png" width="64" alt="TheSun"></a><br><b>太阳</b><br><code>TheSun</code></td>
<td align="center"><a href="art/TheTemperance.png"><img src="art/TheTemperance.png" width="64" alt="TheTemperance"></a><br><b>节制</b><br><code>TheTemperance</code></td>
<td align="center"><a href="art/TheTower.png"><img src="art/TheTower.png" width="64" alt="TheTower"></a><br><b>高塔</b><br><code>TheTower</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/TheWheelOfFortune.png"><img src="art/TheWheelOfFortune.png" width="64" alt="TheWheelOfFortune"></a><br><b>命运之轮</b><br><code>TheWheelOfFortune</code></td>
<td align="center"><a href="art/TheWorld.png"><img src="art/TheWorld.png" width="64" alt="TheWorld"></a><br><b>世界</b><br><code>TheWorld</code></td>
<td align="center"><a href="art/TheWorldsEnd.png"><img src="art/TheWorldsEnd.png" width="64" alt="TheWorldsEnd"></a><br><b>世界尽头</b><br><code>TheWorldsEnd</code></td>
<td align="center"><a href="art/ThinkingWithFeet.png"><img src="art/ThinkingWithFeet.png" width="64" alt="ThinkingWithFeet"></a><br><b>急中生智</b><br><code>ThinkingWithFeet</code></td>
<td align="center"><a href="art/ThirteenOrphans.png"><img src="art/ThirteenOrphans.png" width="64" alt="ThirteenOrphans"></a><br><b>十三幺</b><br><code>ThirteenOrphans</code></td>
<td align="center"><a href="art/ThornWall.png"><img src="art/ThornWall.png" width="64" alt="ThornWall"></a><br><b>荆棘墙</b><br><code>ThornWall</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/ThousandYearStorm.png"><img src="art/ThousandYearStorm.png" width="64" alt="ThousandYearStorm"></a><br><b>千年风暴</b><br><code>ThousandYearStorm</code></td>
<td align="center"><a href="art/ThrowingKnife.png"><img src="art/ThrowingKnife.png" width="64" alt="ThrowingKnife"></a><br><b>飞刀</b><br><code>ThrowingKnife</code></td>
<td align="center"><a href="art/TigerClaw.png"><img src="art/TigerClaw.png" width="64" alt="TigerClaw"></a><br><b>黑虎掏心</b><br><code>TigerClaw</code></td>
<td align="center"><a href="art/TimingAndPlace.png"><img src="art/TimingAndPlace.png" width="64" alt="TimingAndPlace"></a><br><b>天时地利</b><br><code>TimingAndPlace</code></td>
<td align="center"><a href="art/ToolBox.png"><img src="art/ToolBox.png" width="64" alt="ToolBox"></a><br><b>工具箱</b><br><code>ToolBox</code></td>
<td align="center"><a href="art/Torment.png"><img src="art/Torment.png" width="64" alt="Torment"></a><br><b>折磨</b><br><code>Torment</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/TornadoDecimatesTrailerPark.png"><img src="art/TornadoDecimatesTrailerPark.png" width="64" alt="TornadoDecimatesTrailerPark"></a><br><b>龙卷风摧毁停车场</b><br><code>TornadoDecimatesTrailerPark</code></td>
<td align="center"><a href="art/TotalDisorientation.png"><img src="art/TotalDisorientation.png" width="64" alt="TotalDisorientation"></a><br><b>完全错乱术</b><br><code>TotalDisorientation</code></td>
<td align="center"><a href="art/ToyDice.png"><img src="art/ToyDice.png" width="64" alt="ToyDice"></a><br><b>玩具骰子</b><br><code>ToyDice</code></td>
<td align="center"><a href="art/TrashBin.png"><img src="art/TrashBin.png" width="64" alt="TrashBin"></a><br><b>垃圾桶</b><br><code>TrashBin</code></td>
<td align="center"><a href="art/Trebuchet.png"><img src="art/Trebuchet.png" width="64" alt="Trebuchet"></a><br><b>抛石机</b><br><code>Trebuchet</code></td>
<td align="center"><a href="art/Trick.png"><img src="art/Trick.png" width="64" alt="Trick"></a><br><b>恶作剧</b><br><code>Trick</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/TriPointDoubleEdgedSpear.png"><img src="art/TriPointDoubleEdgedSpear.png" width="64" alt="TriPointDoubleEdgedSpear"></a><br><b>三尖两刃枪</b><br><code>TriPointDoubleEdgedSpear</code></td>
<td align="center"><a href="art/Tulpenmanie.png"><img src="art/Tulpenmanie.png" width="64" alt="Tulpenmanie"></a><br><b>郁金香</b><br><code>Tulpenmanie</code></td>
</tr></table>


## U

<a id="group-u"></a>

<table><tr>
<td align="center"><a href="art/UltimateLifeform.png"><img src="art/UltimateLifeform.png" width="64" alt="UltimateLifeform"></a><br><b>究极生物</b><br><code>UltimateLifeform</code></td>
<td align="center"><a href="art/UltimateStrikeBlock.png"><img src="art/UltimateStrikeBlock.png" width="64" alt="UltimateStrikeBlock"></a><br><b>打击格挡奥义</b><br><code>UltimateStrikeBlock</code></td>
<td align="center"><a href="art/UnexplainedFear.png"><img src="art/UnexplainedFear.png" width="64" alt="UnexplainedFear"></a><br><b>莫名恐惧</b><br><code>UnexplainedFear</code></td>
<td align="center"><a href="art/UnimaginablePath.png"><img src="art/UnimaginablePath.png" width="64" alt="UnimaginablePath"></a><br><b>未设想的道路</b><br><code>UnimaginablePath</code></td>
<td align="center"><a href="art/UnknowBox.png"><img src="art/UnknowBox.png" width="64" alt="UnknowBox"></a><br><b>盲盒</b><br><code>UnknowBox</code></td>
<td align="center"><a href="art/UnknownCard.png"><img src="art/UnknownCard.png" width="64" alt="UnknownCard"></a><br><b>未知卡牌</b><br><code>UnknownCard</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/UnstablePortal.png"><img src="art/UnstablePortal.png" width="64" alt="UnstablePortal"></a><br><b>不稳定传送门</b><br><code>UnstablePortal</code></td>
<td align="center"><a href="art/Unyielding.png"><img src="art/Unyielding.png" width="64" alt="Unyielding"></a><br><b>不屈</b><br><code>Unyielding</code></td>
</tr></table>


## V

<a id="group-v"></a>

<table><tr>
<td align="center"><a href="art/Vertigo.png"><img src="art/Vertigo.png" width="64" alt="Vertigo"></a><br><b>眩晕</b><br><code>Vertigo</code></td>
<td align="center"><a href="art/VeryVeryAnger.png"><img src="art/VeryVeryAnger.png" width="64" alt="VeryVeryAnger"></a><br><b>生气气</b><br><code>VeryVeryAnger</code></td>
<td align="center"><a href="art/ViciousMockery.png"><img src="art/ViciousMockery.png" width="64" alt="ViciousMockery"></a><br><b>恶毒咒骂</b><br><code>ViciousMockery</code></td>
<td align="center"><a href="art/Vicissitudes.png"><img src="art/Vicissitudes.png" width="64" alt="Vicissitudes"></a><br><b>枯荣</b><br><code>Vicissitudes</code></td>
<td align="center"><a href="art/Vigilance.png"><img src="art/Vigilance.png" width="64" alt="Vigilance"></a><br><b>警觉</b><br><code>Vigilance</code></td>
<td align="center"><a href="art/Vindicate.png"><img src="art/Vindicate.png" width="64" alt="Vindicate"></a><br><b>雪恨</b><br><code>Vindicate</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/VioletSmoke.png"><img src="art/VioletSmoke.png" width="64" alt="VioletSmoke"></a><br><b>紫烟</b><br><code>VioletSmoke</code></td>
<td align="center"><a href="art/Vision.png"><img src="art/Vision.png" width="64" alt="Vision"></a><br><b>幻象</b><br><code>Vision</code></td>
<td align="center"><a href="art/Vitality.png"><img src="art/Vitality.png" width="64" alt="Vitality"></a><br><b>活力</b><br><code>Vitality</code></td>
</tr></table>


## W

<a id="group-w"></a>

<table><tr>
<td align="center"><a href="art/WaitingForChance.png"><img src="art/WaitingForChance.png" width="64" alt="WaitingForChance"></a><br><b>伺机待发</b><br><code>WaitingForChance</code></td>
<td align="center"><a href="art/WasteNot.png"><img src="art/WasteNot.png" width="64" alt="WasteNot"></a><br><b>可别浪费</b><br><code>WasteNot</code></td>
<td align="center"><a href="art/WaterBottles.png"><img src="art/WaterBottles.png" width="64" alt="WaterBottles"></a><br><b>矿泉水瓶</b><br><code>WaterBottles</code></td>
<td align="center"><a href="art/WaterElement.png"><img src="art/WaterElement.png" width="64" alt="WaterElement"></a><br><b>水元素</b><br><code>WaterElement</code></td>
<td align="center"><a href="art/WellPrepared.png"><img src="art/WellPrepared.png" width="64" alt="WellPrepared"></a><br><b>万全准备</b><br><code>WellPrepared</code></td>
<td align="center"><a href="art/PrologueImprovise.png"><img src="art/PrologueImprovise.png" width="64" alt="WhatIsThis"></a><br><b>这是啥？</b><br><code>WhatIsThis</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/WhirlWind.png"><img src="art/WhirlWind.png" width="64" alt="WhirlWind"></a><br><b>旋风斩</b><br><code>WhirlWind</code></td>
<td align="center"><a href="art/WildernessReclamation.png"><img src="art/WildernessReclamation.png" width="64" alt="WildernessReclamation"></a><br><b>复归荒野</b><br><code>WildernessReclamation</code></td>
<td align="center"><a href="art/WildGrowth.png"><img src="art/WildGrowth.png" width="64" alt="WildGrowth"></a><br><b>野性成长</b><br><code>WildGrowth</code></td>
<td align="center"><a href="art/WildMight.png"><img src="art/WildMight.png" width="64" alt="WildMight"></a><br><b>野性之力</b><br><code>WildMight</code></td>
<td align="center"><a href="art/WishForFishing.png"><img src="art/WishForFishing.png" width="64" alt="WishForFishing"></a><br><b>钓鱼之愿</b><br><code>WishForFishing</code></td>
<td align="center"><a href="art/WolfPack.png"><img src="art/WolfPack.png" width="64" alt="WolfPack"></a><br><b>狼群</b><br><code>WolfPack</code></td>
</tr></table>

<table><tr>
<td align="center"><a href="art/Wonder.png"><img src="art/Wonder.png" width="64" alt="Wonder"></a><br><b>好奇</b><br><code>Wonder</code></td>
<td align="center"><a href="art/WorkOvertime.png"><img src="art/WorkOvertime.png" width="64" alt="WorkOvertime"></a><br><b>加班加点</b><br><code>WorkOvertime</code></td>
</tr></table>


## Y

<a id="group-y"></a>

<table><tr>
<td align="center"><a href="art/YouAreAlready.png"><img src="art/YouAreAlready.png" width="64" alt="YouAreAlready"></a><br><b>你已经......</b><br><code>YouAreAlready</code></td>
</tr></table>


## # (数字/符号)

<a id="group-num"></a>

<table><tr>
<td align="center"><a href="art/404Card.png"><img src="art/404Card.png" width="64" alt="404Card"></a><br><b>404</b><br><code>404Card</code></td>
</tr></table>



---

## §3 原版遗物名（205 条）—— ✅ 已接入实机导出

遗物 `artTemplate` 的取值就是下表的名字，**lint 会硬校验**（不在表里 ⇒ `[Error]` 并给相近候选）。

| 项 | 值 |
|---|---|
| 名单来源 | 实机导出能力表 `..\DICEOMANCER\BepInEx\ModKitDump\capabilities.json` |
| 探测路径 | `DataManager.Instance.GetRelicDataManager().GetAllInGameRelicSo()[].relicInfo.relicName` |
| 条数 | **205** |
| 手工快照里的旧值 | 1 项（`Abacus`）——**已作废** |

> **⚠️ 但 lint 用的仍是 `modkit\mods\capabilities.json`，不是导出文件。**
> 本文档只是**告诉你真名是什么**；要让 lint 也认这些名字，
> 需要把 `vanillaRelicNames` 同步进 `modkit\mods\capabilities.json`（见文末「重新生成」）。

|  |  |  |  |  |  |
|---|---|---|---|---|---|
| `Abacus` | `AdaptiveCapacitor` | `Airdrop` | `AlphaCore` | `AngelsHalo` | `Anvil` |
| `Astrolabe` | `BagOfHolding` | `Bamboo` | `BattlePass` | `Belt` | `BendedSpoon` |
| `BigGlove` | `BlackDice` | `BlackStone` | `BladeArmor` | `BlankPaper` | `BlockerFluid` |
| `Bloodthirster` | `Bonfire` | `BookOfEvasion` | `BottleOfWater` | `BoxingGlove` | `BrokenHeart` |
| `BrokenPencil` | `BrokenSword` | `BucketMeal` | `Cake` | `Can` | `CardBag` |
| `CardDealer` | `CardPack` | `Carousel` | `CataphractArmor` | `ChainedDice` | `Chainsticks` |
| `ChaosPendant` | `ChickenWing` | `Chip` | `Clock` | `Cockroach` | `ColorStone` |
| `CreepyDice` | `Crown` | `Dancing Dice` | `Detergent` | `DiceGenerator` | `DiceShard` |
| `DiceUpgradeKit` | `DinosaurSculpture` | `Dirt` | `DiscoBall` | `DisconnectedHand` | `Donut` |
| `DouLi` | `DoubleEdged` | `DoublePendulum` | `Drumstick` | `Dumbbel` | `ElementalLantern` |
| `EmptyBattery` | `EmptyRelic` | `Eraser` | `Ether` | `ExpansionPack` | `FireBelt` |
| `FirstAidKit` | `FirstFire` | `FishTank` | `FlashLight` | `Flint` | `FloridaWater` |
| `FluorescentStone` | `Fly` | `FoamSpray` | `FoldingFan` | `ForgetMeNot` | `FourLeafClover` |
| `FullPiggyBank` | `FutureProjector` | `Gashapon` | `Gemstone` | `GiantAxe` | `GiantDice` |
| `GiftBox` | `Glasses` | `GoldenPotion` | `Hammer` | `HandCrankGenerator` | `HazyDice` |
| `HeartOfSteel` | `HitchhikersGuide` | `Incinerator` | `InsuranceForDanger` | `InsuranceForEngagement` | `IronHelmet` |
| `IronStomach` | `Jacket` | `JadeRing` | `Jigsaw` | `JinGuBang` | `Juice` |
| `KneePads` | `Knuckle` | `Leech` | `LightningBand` | `LightningDagger` | `LightningShield` |
| `LittleFlame` | `Loader` | `LolliPop` | `Loudspeaker` | `MagicGlove` | `MagicHat` |
| `MagneticDice` | `Maraca` | `MartialArtsManual` | `MechanicalClaw` | `MeltedDice` | `Microbot` |
| `MidasTouch` | `Mine` | `MiniDice` | `MiniKeyBoard` | `MiniMotor` | `MiniTrebuchet` |
| `MintCandy` | `Monument` | `MultiversePortal` | `MysteryMask` | `Nut` | `OldCoin` |
| `Ooze` | `Ouroboros` | `PaintBucket` | `PepperDish` | `PiggyToy` | `PirateCannon` |
| `PlatinumChip` | `Pony` | `Poop` | `Pot` | `PowerBank` | `PowerRing` |
| `PsychicShockLance` | `PureAir` | `Radiance` | `RainbowCandy` | `Refresher` | `ReverseCard` |
| `RoadSign` | `RouletteDice` | `RubberDice` | `Sandevistan` | `Scalpel` | `Scissor` |
| `SharpeningStone` | `ShatteredPhoto` | `Shredder` | `ShrinkRay` | `SmallMirror` | `SmokeBelt` |
| `SmokelessPowder` | `Soap` | `Soda` | `SoftArmor` | `SpearInFlame` | `StrangeFragment` |
| `SuperBattery` | `TacticalBelt` | `TalismanOfPBV` | `TalismanOfPGB` | `TalismanOfPGV` | `TalismanOfPRB` |
| `TalismanOfPRG` | `TalismanOfPRV` | `TheOneDice` | `TheWhiteMoon` | `ThermalPulseCannon` | `Thermite` |
| `ThornMaile` | `TrashCan` | `TreasureBox` | `Trumpet` | `Turtle` | `Twig` |
| `Vambrace` | `Vest` | `VineArmour` | `VipCard` | `Voodoo` | `WalkingStick` |
| `WallPainter` | `WarDrum` | `WarehouseModel` | `WeightBag` | `Wheelchair` | `WoodenBlocker` |
| `WoodenDice` | `WoodenDrone` | `WoodenStick` | `WoodenSword` | `Wrench` | `WritOfMandate` |
| `YogaMat` |  |  |  |  |  |

---

## §4 已知关键词全表（36 条）

这是卡牌 `keywords[].name` 的**校验用清单**（`knownKeywords`）。

> **⚠️ 36 是"官方名单"，不等于"都能用"。**
> 按实机探针，内容里真正在用的只有 **32** 个；剩下 4 个从未出现，
> 另有一批"代码里有分支"的是死代码，硬挂到卡上会**崩游戏**。
> 谁可用、谁不能用，以《[ModKit-效果字典](ModKit-效果字典.md)》§2.4 黑名单为准——
> **本文件只负责说"lint 认这 36 个名字"，不负责说"这 36 个都能用"。**

|  |  |  |  |  |  |
|---|---|---|---|---|---|
| `Assault` | `Bonus` | `Check` | `Choose` | `Construct` | `Constructed` |
| `Coordinated` | `Distorted` | `Duplicate` | `Evolve` | `Exert` | `Flow` |
| `Fragile` | `Glitch` | `Innate` | `Item` | `Keep` | `Kick` |
| `Limited` | `Loaded` | `Malady` | `Miracle` | `Modified` | `Modify` |
| `Perishable` | `Phantom` | `Rainbow` | `Rebound` | `Reload` | `Sacrifice` |
| `Storm` | `Summon` | `Temporary` | `Vengeance` | `Virtue` | `Wild` |

---

## §5 已发出的触发点全表（32 条）

清单键 `knownTriggerNames`。**注意它不是 `trigger` 的取值域**：

- **取值域**（写错就 `[Error]`）：反编译的 `TriggerTargetInfo.AllTriggerType`，**33 个**，
  见 `ModKit.Core\TriggerTypeNames.cs`（可读清单也在《[ModKit-作者手册](ModKit-作者手册.md)》§10「名字从哪查」）。
- **本清单**（32 个）：探针实测"游戏**实际会发出**"的触发点。
  取值合法但落在这里面之外 ⇒ 只给 `[Warn]`："原版代码里没有任何地方实际发出它 —— 你的遗物很可能永远不会触发"。
  （判据出处：`ModKit.Core\ContentValidator.cs` 第 158–166 行）

所以：**照着这 32 个写，既不会报错，也不会收到"永远不会触发"的警告。**

|  |  |  |  |  |  |
|---|---|---|---|---|---|
| `AbilityStandardTrigger` | `AcquireRelic` | `BattleStart` | `BeenAttacked` | `BuyInStore` | `CardEffectNoCast` |
| `CardKeywordTrigger` | `CastCard` | `CastCardFinished` | `ChangeBattlePhase` | `ChangeWorldPhase` | `CharacterCheck` |
| `CharacterLoseLife` | `CharacterRollDice` | `DamageBeenDealt` | `DrawCard` | `EnemyDieStart` | `EnemyHealthBarBreak` |
| `EnterUnvisitedNode` | `ExertCard` | `HandCardCountChange` | `ManaConsumed` | `ManaOverflow` | `OrganismPerish` |
| `RelicActive` | `RelicCorrupted` | `ShuffleGraveToLibrary` | `SummonResolve` | `TakeRest` | `TimePass` |
| `UseTheOneDice` | `WholeNewTurnStart` |  |  |  |  |

---

## §6 可用费用色（6 个）与多色槽注意事项

清单键 `availableCostColors`，用于卡牌 `cost[]`（费用槽）与 `colors[]`（卡面流派色）。

| 颜色名 | 中文 |
|---|---|
| `Red` | 红 |
| `Green` | 绿 |
| `Blue` | 蓝 |
| `Purple` | 紫 |
| `Void` | 虚色 |
| `Black` | 黑 |

### 多色槽：两条规则，以及它们**从哪来**

一个费用槽可以写多个颜色（`[["Void", "Red"]]` = 这一格虚色或红色都能付）。规则两条：

1. **不是任意组合都行** —— 只有游戏**预制了图标**的组合才能用；
2. **顺序参与匹配** —— `["Void","Red"]` 与 `["Red","Void"]` 是**两个不同的键**。

**出处（不是我编的）**：

| 环节 | 位置 |
|---|---|
| 手册表述 | 《[ModKit-作者手册](ModKit-作者手册.md)》§3.1（"多色槽不是任意组合都合法——卡面图标是按**有序颜色序列**预制在游戏资产里的，顺序也参与匹配"） |
| 校验器注释 | `ModKit.Core\ContentValidator.Costs.cs` 第 98–105 行（"合法组合是**预制在资产里的有限集合**……见 `GameCapabilitiesProbe.ProbeCostCombos`"） |
| 游戏侧表 | `ManaManager.ManaIconDic`（`ModKit.Api\ContentDefs.cs` 第 263 / 504 行） |
| 探针 | `ModKit.Game\GameCapabilitiesProbe.cs::ProbeCostCombos`（旧版第 318–354 行）枚举 `ManaIconDic` **全部键**；日志行搜 `[cost-combos] 费用槽组合探测` |

### ✅ 本 build **实际预制**的费用槽组合（21 个）—— 数据源：实机导出

这是 `ManaManager.ManaIconDic` 的**全部键**（游戏里真正能用的组合，一个不多一个不少）。
`cost[]` 里写的槽，**必须与下面某一行完全一致（含顺序）**，否则进游戏会抛 `KeyNotFoundException`。

| # | 槽（顺序参与匹配） | 颜色数 |
|---|---|---|
| 1 | `["Black"]` | 1 |
| 2 | `["Black", "Void"]` | 2 |
| 3 | `["Blue"]` | 1 |
| 4 | `["Blue", "Black"]` | 2 |
| 5 | `["Blue", "Green"]` | 2 |
| 6 | `["Blue", "Void"]` | 2 |
| 7 | `["Green"]` | 1 |
| 8 | `["Green", "Black"]` | 2 |
| 9 | `["Green", "Void"]` | 2 |
| 10 | `["Purple"]` | 1 |
| 11 | `["Purple", "Black"]` | 2 |
| 12 | `["Purple", "Blue"]` | 2 |
| 13 | `["Purple", "Green"]` | 2 |
| 14 | `["Purple", "Red"]` | 2 |
| 15 | `["Purple", "Void"]` | 2 |
| 16 | `["Red"]` | 1 |
| 17 | `["Red", "Black"]` | 2 |
| 18 | `["Red", "Blue"]` | 2 |
| 19 | `["Red", "Green"]` | 2 |
| 20 | `["Red", "Void"]` | 2 |
| 21 | `["Void"]` | 1 |

> **⚠️ lint 仍然校验不了这一层。**
> `capabilities.json` 没有"组合"这个键，所以 `ModKit.Tools\Capabilities.cs` 的
> `FileCapabilities.IsKnownCostCombo` 只检查"槽里每个颜色都在 6 个可用色里"。
> 写得"颜色都对、组合没预制"时 lint 会放过，进游戏才炸。**上表就是标准答案，照着挑。**
> （导出的 `capabilities.json` 里有机器可读的 `knownCostCombos` 键，见文末。）

---

## §7 仓库里的其它 `capabilities*.json`（实时计算）

本节**全部按实际文件现算**：清单是**扫描仓库**得来的（截至生成时共 **4** 份，合计 **130.4 KB**），
大小 / 最后修改 / SHA256 / 关键词条数都读自磁盘本身 —— 不再印任何写死的常量。

| 文件（相对仓库根） | 大小 | 最后修改 | SHA256（前 16 位） | `knownKeywords` | 是什么 |
|---|---|---|---|---|---|
| `modkit\capabilities.json` | 40,844 B | 2026-10-14 00:53 | `921c4779905415e7` | **36** 项 | ⚠️ **不是 lint 用的那份**。按 `ModLinter.LoadCapabilities` 的查找顺序（`<目标目录>\capabilities.json` → `<目标目录的父目录>\capabilities.json`），它**正躺在 `modkit\mods` 的父目录**上，且**与 lint 那份字节相同** ⇒ 命中哪一份结果都一样 |
| `modkit\mods\capabilities.json` | 40,844 B | 2026-10-14 00:53 | `921c4779905415e7` | **36** 项 | ✅ **lint 用的就是它**，也是本文件的唯一数据源 |
| `state\archive\dump-import-20261014\capabilities.json` | 24,932 B | 2026-10-14 00:45 | `02fd4ca60ad0d953` | **36** 项 | ⚠️ **不是 lint 用的那份**。按 `ModLinter.LoadCapabilities` 的查找顺序（`<目标目录>\capabilities.json` → `<目标目录的父目录>\capabilities.json`），它不在 lint 的查找路径上（仅作资料/备份保留） |
| `state\capabilities-broken-backup.json` | 26,882 B | 2026-10-12 13:33 | `ff1c9b44ff979ac5` | — | ❌ **不是合法 JSON**：`JSONDecodeError: Expecting value: line 551 column 24 (char 24446)`（第 551 行）。仅作备份保留 |

> ✅ **两份已同步且字节相同** —— `modkit\mods\capabilities.json` 与父目录的 `modkit\capabilities.json`：
> 都是 **40,844 B**（最后修改 2026-10-14 00:53），SHA256 `921c4779905415e7…`。
> 所以无论 lint 命中哪一份，**读到的能力表完全一样**，不存在"静默命中旧表"的问题。
> （`ModLinter.LoadCapabilities` 的回落顺序确实先看目标目录、再看父目录；
> 两份一致时这一步没有可观察后果。）
---

## §8 重新生成（幂等）

```
python tools\gen-vanilla-assets.py
```

- **默认幂等**：只要数据源没变，反复生成的字节**完全一致**
  （SHA256 相同）⇒ 可以安全地无脑重跑，不会产生噪音 diff。
- 为此，文件里的"本文件生成时间"写的是**数据源快照时间**，真实运行时刻只打到 stdout。
  确实需要把运行时刻写进文件时：`--stamp`（**该模式下不再字节稳定**）。
- 脚本会把每个清单写出的**条数**打印到 stdout，便于和"§数据来源与快照"那张表核对。

### 相关参数

| 参数 | 作用 |
|---|---|
| `--source <路径>` | 换掉 `modkit\mods\capabilities.json` 这份数据源 |
| `--out <路径>` | 换掉输出文件（默认 `docs\ModKit-原版素材索引.md`） |
| `--cards-json <路径>` | 指定游戏导出的 `cards.json`（默认去常见位置找，见上） |
| `--dump-capabilities <路径>` | 指定游戏导出的 `capabilities.json`（遗物全名 / 费用槽组合） |
| `--dump-dir <路径>` | 只在这一个目录里找上面两个导出文件 |
| `--no-cards` | **强制**退回"未导入实机导出"的旧行为（回退用） |
| `--no-dump-capabilities` | 忽略游戏导出的能力表 |
| `--no-art` | 卡表不渲染卡图列（也不拷贝卡图） |
| `--art-dir <目录>` | 卡图副本落地目录（默认 `docs\art\`） |
| `--no-art-copy` | **只生成索引**，不往 `docs\art\` 拷贝/重编码任何图 |
| `--art-max-edge <px>` | 体积开关：**拷进仓库的副本**按最长边缩到 px（需 Pillow；**源图不动**） |
| `--art-jpeg` | 体积开关：副本转成 JPEG（需 Pillow；**源图不动**） |
| `--art-quality <1-100>` | JPEG 质量，默认 82（配合 `--art-jpeg`） |
| `--art-rel <前缀>` | 覆盖卡图的相对路径前缀（看不到卡图副本时的回退路径） |
| `--stamp` | 额外写入本次运行时刻（破坏字节稳定性） |

### 卡图：从哪来、怎么用体积开关

卡图是**游戏运行期导出**的原版卡面（导出源最长边 256 px），生成器把它们
**同步到 `docs\art\`**（当前档：重编码，最长边 64 px；2.7 MB / 493 个）—— 索引里的卡图列就是这些图的
**内嵌缩略图**，链接指向同目录的原图 ⇒ **删掉游戏目录也能看**。

几条体积命令（都只改**副本**，源 PNG 与游戏目录不受影响）：

```
python tools\gen-vanilla-assets.py                      # 默认档：缩到最长边 64 px
python tools\gen-vanilla-assets.py --art-max-edge 0    # 原样拷贝（≈35 MB）
python tools\gen-vanilla-assets.py --art-jpeg          # 默认档 + 转 JPEG，最小
```

同步是**幂等**的：原样档按「与源字节相同」跳过；重编码档按「目标像素尺寸 == 源按
max-edge 缩放后的尺寸」跳过（都打印「跳过 N 张」）—— 两种判据都不会让反复运行产生噪音 diff。
重编码档的输入**始终是源 PNG**，不会拿上次的产物再压一次，所以结果字节稳定。

> **版权**：卡面版权属游戏开发者（UltraPiggyStudio），随仓库公开仅供查阅 / 写卡对照；
> MIT 许可证不覆盖它们，请勿单独再分发或商用。说明见 `docs/art/README.md`。
> 想把它们从仓库里去掉：去掉根目录 `.gitignore` 里 `docs/art/*.png` 一行的注释，
> 再执行 `git rm -r --cached docs/art` 并重新提交。

### 游戏内导出在哪、怎么来

平台在**进入战斗、运行时事实就绪后**导出一次，写到：

```
<游戏根>\BepInEx\ModKitDump\capabilities.json   # 完整能力表（含完整遗物名、费用槽组合）
<游戏根>\BepInEx\ModKitDump\cards.json          # 卡清单（内部名/中文名/稀有度/费用/图文件名）
<游戏根>\BepInEx\ModKitDump\art\<内部名>.png    # 卡面缩略图（最长边 256 px）
```

开关在 `<游戏根>\BepInEx\config\diceomancer.modkit.cfg` 的 `[Diagnostics]` 段：
`DumpCapabilitiesFile` / `ExportCardsArt` / `ExportCardsArtFull` / `ExportCardsArtThumbEdge` /
`ExportCardsArtMax` / `ExportCardsArtReuse` / `ExportCardsArtFlipY` / `DumpDir`。

> **遗物名已经喂给 lint 了**（见 §7：`modkit\mods\capabilities.json` 的内容就是实机导出）。
> 本文件只负责把"真名是什么"连同卡面一起摊开给你看。
