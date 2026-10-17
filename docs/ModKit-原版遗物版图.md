# ModKit · 原版遗物版图

> **这份文档回答三件事**：原版 205 件遗物**实际**长什么样（dump 出来的真实字段值，不是推测）、
> 平台现在能表达其中多少、差的那些要补什么。
>
> 文档性质：**参考手册**（给人读的），不是进度报告。此处只描述**原版数据的版图**；
> 平台侧字段是否已经落地，**以代码为准**。

---

## 0. 数据来源与复现方式

| 项 | 值 |
|---|---|
| 采集通道 | `Diagnostics/DumpRelics`（ModKit 的日志型导出，只读、不改游戏状态） |
| 采集日期 | **2026-10-14** |
| 归档路径 | `diceomancer-mod/state/archive/relic-dump-20261014/relic-dump.log`（1.83 MB，`[relic]` 段） |
| 全量日志 | 同目录 `LogOutput.full.log` |
| 采集遍数 | **两遍**：`阶段=装配前` **205 件** → `阶段=装配后` **206 件** |
| 差额解释 | 多出的 1 件是 **mod 自己的遗物** ⇒ 证明 mod 内容与原版内容**确实落在同一个容器**里，这份版图是"实机容器里真实存在的集合" |

**复现步骤**

1. 打开 `BepInEx\config\diceomancer.modkit.cfg`，在 `[Diagnostics]` 段写：

   ```ini
   DumpRelics = *
   ```

2. 启动游戏并**进一次战斗**（dump 在战斗容器就绪后执行）。
3. 到 `BepInEx\LogOutput.log` 里搜 `[relic] `。

也可以只点名要看的几件（按原版遗物名，逗号分隔）：

```ini
DumpRelics = Anvil,磨盘
```

单件记录的结构长这样（节选）：

```text
[relic] FishTank | special=False | mainType=NoChargeTrigger | rarity=Normal | colors=1[Green] | charge=0/-1 | trigLimit=none | effects=1+0 | pool=Colored(UnlockRelics_Green)
[relic] ===== FishTank 的声明结构 =====
[relic]   [RelicInfo] 全字段（布尔/枚举/null 都显式打出来）：
[relic]     relicMainType=NoChargeTrigger
[relic]   mainEffectInfo.triggerTargetInfo（11 字段全打）：
[relic]     [1/11] triggerType=BattleStart
[relic]     [4/11] battlePhase(class)=battlePhase=Upkeep isStart=False
[relic]     [6/11] relativeCharacterType=Owner
[relic]     [9/11] extraString=(空)
```

**统计口径（读下面任何数字前先看这条）**

- §1 的分布表一律以**装配前那遍**为准，**n = 204**（该遍 205 件中，有 1 件未产出可统计的摘要行；差额原因未查）。
- 任何数字与代码 / 实测冲突时，**以代码为准**。

---

## 1. 整体分布

### 1.1 `relicMainType`（n = 204）

| `relicMainType` | 件数 | 占比 | 平台当前 |
|---|---:|---:|---|
| `NoChargeTrigger` | **124** | **61%** | ✅ 唯一能表达的形态 |
| `Active` | **41** | **20%** | ❌ 需 Tier 2 |
| `ChargedTrigger` | **18** | **9%** | ❌ 需 Tier 2 |
| `WhenPickUp` | **15** | **7%** | ❌ 需 Tier 2 |
| `DoNothing` | **6** | **3%** | ❌ 需 Tier 2 |

> 平台目前**只支持 `NoChargeTrigger`**（装配器 `ContentAssembler.AssembleRelic` 无条件写这个值），
> 其余 **39%（80 件）**按当前契约**表达不了**。

### 1.2 特殊遗物 `isSpecialRelic`

| 值 | 件数 |
|---|---:|
| `true` | **12** |
| `false` | 192 |

`isSpecialRelic=true` 的 12 件需要 `Relic.RelicEffects.Relic<遗物名>` 这个**行为类**。
平台目前**运行期没有消费者**（见《[ModKit-能力缺口与下一步](ModKit-能力缺口与下一步.md)》§0「新增 D」）⇒ 缺口 D。

### 1.3 稀有度（n = 204）

| `relicRarity` | 件数 |
|---|---:|
| `Normal` | 87 |
| `Uncommon` | 64 |
| `Rare` | 53 |

### 1.4 效果条数（按**效果条数**计，合计 409 条）

| 一件遗物带几条效果 | 件数 |
|---|---:|
| 1 条 | 349 |
| 2 条 | 46 |
| 3 条 | 6 |
| 0 条 | 8 |

> "1 条"是绝对主流 —— 原版遗物基本是「一个时机 + 一个效果」。

### 1.5 所属池（按**效果条数**计，合计 409 条）

| 池 | 条数 |
|---|---:|
| `Standard` | 303 |
| `Colored` | 88 |
| `Utils` | 18 |

---

## 2. 触发点频次：原版用了什么，平台能不能接

统计对象是原版遗物 `triggerTargetInfo.triggerType` 的**出现次数**（同一件遗物的主效果 + 附加效果各算一次）。

| 触发点 | 原版使用次数 | 平台当前能否表达 | 缺什么 |
|---|---:|---|---|
| `Passive` | 106 | ✅ 能 | — |
| `BattleStart` | 35 | ✅ 能 | — |
| `EnemyHealthBarBreak` | 23 | 🟡 **需附加字段**（Tier 1 正在补） | `relativeCharacter` |
| `ChangeBattlePhase` | 22 | 🟡 **需附加字段**（Tier 1 正在补） | `battlePhase`（**class；平台已补中性默认值 `(Main,false)` ⇒ 不崩，但不写就没有你要的条件**） |
| `WholeNewTurnStart` | 18 | 🟡 **需附加字段**（Tier 1 正在补） | `certainTurn`（**class；平台已补中性默认值 `{require:false,turnCount:0,Before}` ⇒ 不崩，但不写就变成「每回合都触发」**） |
| `CharacterLoseLife` | 14 | ✅ 能 | — |
| `TakeRest` | 12 | 🟡 **需附加字段**（Tier 1 正在补） | `extraString` |
| `TimePass` | 10 | ✅ 能 | — |
| `CastCardFinished` | 10 | 🟡 能跑，但**不精确** | `cardCoreInfoCheck`（struct，留空 ⇒ 语义变宽成"任意卡"） |
| `ManaOverflow` | 7 | ✅ 能 | — |
| `CastCard` | 7 | 🟡 能跑，但**不精确** | `cardCoreInfoCheck`（同上） |
| `ManaConsumed` | 6 | 🟡 **需附加字段**（Tier 1 正在补） | `colorE`（**以代码为准**：该字段是否被采纳由实现方决定） |
| `UseTheOneDice` | 6 | ✅ 能 | — |
| `EnterUnvisitedNode` | 6 | ✅ 能 | — |
| `ShuffleGraveToLibrary` | 1 | ✅ 能 | — |
| `BuyInStore` | 1 | ✅ 能 | — |
| `OrganismPerish` | 1 | ✅ 能 | — |
| `CharacterCheck` | 1 | 🟡 需附加字段（Tier 1 正在补） | `relativeCharacter` + `extraString`（写 `AnyCheck` = 任意检定） |
| `AbilityStandardTrigger` | 1 | 🟡 **需附加字段**（Tier 1 正在补） | `extraString`（**能力名**，必须与原版能力名逐字相同） |

> **统计口径（2026-10-14 复核后定稿）**
> 上表数字取自归档 dump 的**「装配前」那一段**（共 **287 条** `triggerType`），复核命令：
>
> ```bash
> cd diceomancer-mod/state/archive/relic-dump-20261014
> grep -oE "triggerType=[A-Za-z]+" relic-dump.log | sort | uniq -c | sort -rn
> ```
>
> 「装配后」那一段只有 `BattleStart` 从 **35 → 36**（+1 = 我们 mod 的遗物），其余完全相同。
> ⚠️ 早先一版曾把**两遍 dump 的合并计数**误当成单遍（把尾部队列写成"各 2"），已按实测更正；
> **本节结论不受影响**（要补的字段与先后顺序不变）。

---

## 3. 原版参数取样：到底要"合成什么样"

下面每个平台字段，都给出**原版 dump 里的真实取值**以及**游戏侧的消费代码**。
写 mod 时用这些值当镜像，不要凭想象写。

| 平台字段 | 对应触发点 | dump 里的真实取值 | 消费方（反编译路径） |
|---|---|---|---|
| `battlePhase` | `ChangeBattlePhase` | `battlePhase=Main isStart=False`（另一例 `battlePhase=Upkeep isStart=False`）；也见过 `Main isStart=True` | `Trigger/TriggerManager.cs:97-100`：先比 `battlePhase`，相等再比 `isStart` |
| `certainTurn` | `WholeNewTurnStart` | `requireCertainTurn=False turnCount=0 certainTurnRelationInfo=Before` | `Trigger/TriggerManager.cs:122-132`：`require=False` 时**直接放行**；`Before` ⇒ `GetTurnCount() <= turnCount` |
| `relativeCharacter` | `EnemyHealthBarBreak`、`CharacterCheck`、`ChangeBattlePhase` | `Owner` / `Opponents` | `Trigger/TriggerManager.cs:187+` `CheckCharacterFitRelativeRequirement` |
| `extraString` | `TakeRest`、`AbilityStandardTrigger`、`CardKeywordTrigger` | 多数为 `(空)`；`TakeRest` 的 **12 件全部** = `ShortRest`；`AbilityStandardTrigger` 实例 = `Burn`（能力名）；`CharacterCheck` 用 `AnyCheck` | `Trigger/TriggerManager.cs:181`：`trigger.ExtraString == triggerTargetInfo.extraString`（**逐字相等**，大小写敏感） |
| `colorE` | `ManaConsumed` | `Red` / `Blue`（同一件遗物可以两条效果各用一种色） | `Trigger/TriggerManager.cs:158`：`trigger.ColorE == triggerTargetInfo.colorE` |

**另外一条重要的"原版也这么干"的证据**（打消"这样做是不是野路子"的疑虑）：

```text
Abacus: effectName=DoNothing numbers=6,6 spStr=GainBlockIfHandExceed
```

原版自己也用「**`DoNothing` + 特殊效果字符串**」来表达逻辑，与平台的 `custom:` / 特殊效果通路是**同一个机制**。

**★ 硬性不变量：`battlePhase` / `certainTurnStartInfo` 必须非 null（两件事，别混）**

- **① 平台侧不变量（平台替你守）**：`TriggerManager.CallTriggerEvent` 会遍历**所有**已注册 target 并逐个调用
  `CheckTriggerFitTargetTrigger`；其中 `ChangeBattlePhase` 分支解引用 `target.battlePhase`
  （`Trigger/TriggerManager.cs:97`）、`WholeNewTurnStart` 分支解引用 `target.certainTurnStartInfo`（`:122`），
  两者都是 **class ⇒ null 就抛 NRE**。原版实证 **288/288 个 target 两个字段全非 null** ⇒ 游戏的硬性不变量。
  **平台现已自动补中性默认值**（`ModKit.Game/ContentAssembler.cs:392-429`，值见上表）+ **装配期自检**
  ⇒ **任何遗物都不会因这两个字段而崩**（2026-10-14 实机 NRE 修复）。
- **② 作者侧责任**：你要的触发条件**必须自己写**；不写 ⇒ 条件 = 中性默认值 ⇒ `lint` 报 `[Error]`。
- **验证通道**：`BepInEx\config\diceomancer.modkit.cfg` 里 `DumpRelics = *` 后进战斗，日志里每个 target 会打
  `class 字段判定（为 null 会 NRE 崩游戏）：battlePhase=…｜certainTurnStartInfo=…`；
  **「装配后」那一段若出现 `=null ⇒ 崩` 就是必崩配置**（2026-10-14 的崩溃就是被这行照出来的）。

---

## 4. 能力覆盖结论与三层路线图

**一句话结论**：平台当前只覆盖原版遗物的 **61%（`NoChargeTrigger` 那 124 件）**；
但只要补上**一小组附加字段**，就能把最常用的几个触发点接上，**边际收益最大、改动面最小**。

| 层 | 补什么 | 解锁什么 | 量级 |
|---|---|---|---|
| **Tier 1**（附加字段） | `battlePhase` / `certainTurn` / `relativeCharacter` / `extraString`（+ 可能 `colorE`） | `EnemyHealthBarBreak`、`ChangeBattlePhase`、`WholeNewTurnStart`、`TakeRest`、`AbilityStandardTrigger`、`CharacterCheck`、`ManaConsumed` 等触发点从"不要用"变为"可用" | **直接解锁 83 次触发使用**（按上表逐项相加：23 + 22 + 18 + 12 + 6 + 1 + 1 = 83；口径见 §2）。若把 `cardCoreInfoCheck` 类的两次也计入（`CastCardFinished` 10 + `CastCard` 7 = 17，属"能跑但语义变宽"），**"需额外字段"名单合计 100 次** |
| **Tier 2**（形态） | `mainType` + 充能 / 触发上限字段（`requiredChargeCount`、`triggerLimitType`、`triggerLimit`、`canChargeBeyondRequirement`、`noTriggerAfterFullCharge`） | `Active` 41 + `ChargedTrigger` 18 + `WhenPickUp` 15 + `DoNothing` 6 | **解锁 80 件** |
| **Tier 3**（行为类） | **❌ 已删除（2026-10-14 裁决）—— 不可复刻**：原规划的 `behaviour` / 特殊遗物通路已从平台整体移除（桥补丁方案不再做，见《[ModKit-能力缺口与下一步](ModKit-能力缺口与下一步.md)》"新增 D"）。以下为**删除前**的规划记录：`behaviour` / 特殊遗物：需要**桥补丁**（`RelicManager.cs:231` + `RelicBase.cs:574` 两处 `Type.GetType` 解析） | `isSpecialRelic=true` 的遗物（作者侧**再无**表达途径） | **12 件** |

**顺序建议**：Tier 1 → Tier 2。**Tier 3 已删除、不再做**（2026-10-14 裁决：原"特殊遗物 / behaviour"通路在运行期没有任何消费者，作者侧表达途径已整体移除）。
Tier 1 只加可选字段 + 校验器分支，不动装配结构、不动桥，风险最低而覆盖面最广。

> **Tier 1 的严重级别口径（2026-10-14 定稿 / 同日平台兜底后校准）**：缺字段分两档 ——
> 缺 **`battlePhase`** / **`certainTurn`** ⇒ ❌ **`[Error]`**（lint 拒绝、**该文件在游戏里不会被加载**）。
> 理由**不是崩溃**：平台装配时会给这两个 class 字段补中性默认值
> （`battlePhase = (Main,false)`、`certainTurnStartInfo = { require:false, turnCount:0, Before }`，
> 见 `ModKit.Game/ContentAssembler.cs:392-429` + 装配期自检）⇒ **不会崩**；
> 但触发条件会被**固定成那个默认值**（`ChangeBattlePhase` 只在 `Main` 阶段匹配；`WholeNewTurnStart` **每回合都触发**），
> 与作者预期多半不符，所以仍然拦在加载之前；
> 缺 **`relativeCharacter`** / **`extraString`** / **`colorE`** ⇒ ⚠️ 警告（**不崩、不报错，但永不匹配** —— 静默死）。
> 已给全字段的配置仍然**零错误零警告**。
> **这样设计是为了让作者不会带着与预期不符的配置进游戏。**

---

## 5. 照原版镜像：5 件代表性遗物

以下 5 件全部**逐字摘自** `relic-dump.log`（装配前那遍），覆盖 5 种不同形态 / 触发点。
**要写 mod 遗物时，照这几件的结构抄。**

### 5.1 `FishTank` —— 最普通的形态（`NoChargeTrigger` + `BattleStart`）

| 字段 | 原版值 |
|---|---|
| 摘要 | `special=False \| mainType=NoChargeTrigger \| rarity=Normal \| colors=1[Green] \| charge=0/-1 \| trigLimit=none \| effects=1+0 \| pool=Colored(UnlockRelics_Green)` |
| `relicMainType` | `NoChargeTrigger` |
| `requiredChargeCount` / `chargeCount` | `0` / `-1` |
| `hasTriggerLimit` / `triggerLimitType` / `triggerLimit` | `False` / `EachBattle` / `0` |
| `extraRelicEffectInfos` | `0` |
| 触发 | `triggerType=BattleStart`、`relativeCharacterType=Owner` |
| 效果[0] | `effectName=SpecialEffect`、`effectTargetEnum=Self`、`spStr=SummonSelectedElement`、`numbers=(无)` |
| 行为类 | `Relic.RelicEffects.RelicFishTank 存在=False` ⇒ 走 `RelicBase`（纯数据驱动） |

### 5.2 `Cake` —— `Active` 形态 + 主效果 `Passive`、附加效果 `EnemyHealthBarBreak`

| 字段 | 原版值 |
|---|---|
| 摘要 | `special=False \| mainType=Active \| rarity=Rare \| colors=2[Purple,Green] \| charge=7/-1 \| trigLimit=none \| effects=1+1 \| pool=Colored(UnlockRelics_PurpleGreen)` |
| `relicMainType` | `Active` |
| `requiredChargeCount` | `7`（**`Active` 的费用门槛**） |
| `extraRelicEffectInfos` | `1` |
| 主效果触发 | `triggerType=Passive`、`relativeCharacterType=Opponents`、`battlePhase=Main isStart=True` |
| 主效果[0] | `effectName=SpecialEffect`、`effectTargetEnum=Self`、`spStr=TempAddAllCardNumber`、`numbers=3` |
| 附加效果触发 | `triggerType=EnemyHealthBarBreak`、`relativeCharacterType=Opponents` |
| 附加效果[0] | `effectName=ChargeRelic`、`effectTargetEnum=Self` |
| 行为类 | `RelicCake 存在=False` ⇒ `RelicBase` |

> 这件是 **Tier 1 + Tier 2 的合体样本** —— 既需要 `mainType=Active`，又需要 `relativeCharacter=Opponents`。

### 5.3 `AlphaCore` —— `ChargedTrigger` 形态 + 附加效果 `WholeNewTurnStart`

| 字段 | 原版值 |
|---|---|
| 摘要 | `special=False \| mainType=ChargedTrigger \| rarity=Rare \| colors=1[Blue] \| charge=2/-1 \| trigLimit=none \| effects=1+1 \| pool=Colored(UnlockRelics_Blue)` |
| `relicMainType` | `ChargedTrigger` |
| `requiredChargeCount` | `2` |
| `extraRelicEffectInfos` | `1` |
| 主效果触发 | `triggerType=Passive`、`relativeCharacterType=Owner`、`certainTurnStartInfo=requireCertainTurn=False turnCount=0 certainTurnRelationInfo=Before` |
| 主效果[0] | `effectName=SpecialEffect`、`spStr=TakeCardFromLibrary`、`effectTargetEnum=Self` |
| 附加效果触发 | `triggerType=WholeNewTurnStart`、`certainTurnStartInfo=requireCertainTurn=False turnCount=0 certainTurnRelationInfo=Before` |
| 附加效果[0] | `effectName=ChargeRelic`、`effectTargetEnum=Self` |
| 行为类 | `RelicAlphaCore 存在=False` ⇒ `RelicBase` |

> 注意 `certainTurnStartInfo` 的**真实默认值**：`require=False / turnCount=0 / relation=Before` ——
> 这就是「不需要限定回合的每回合开始」的写法。

### 5.4 `MartialArtsManual` —— `ManaConsumed` + `colorE` 的**同件双色**样本

| 字段 | 原版值 |
|---|---|
| 摘要 | `special=False \| mainType=NoChargeTrigger \| rarity=Rare \| colors=2[Red,Blue] \| charge=0/-1 \| trigLimit=none \| effects=1+1 \| pool=Colored(UnlockRelics_RedBlue)` |
| 主效果触发 | `triggerType=ManaConsumed`、`colorE=Red`、`relativeCharacterType=Owner` |
| 主效果[0] | `effectName=ApplyAbility`、`abilityName=Evade`、`effectTargetEnum=Self`、`numbers=3` |
| 附加效果触发 | `triggerType=ManaConsumed`、`colorE=Blue`、`relativeCharacterType=Owner` |
| 附加效果[0] | `effectName=ApplyAbility`、`abilityName=CriticalHit`、`effectTargetEnum=Self`、`numbers=3` |

> 同一件遗物**两条效果各挂一种颜色** —— 没有 `colorE` 字段就完全表达不了。

### 5.5 `Incinerator` —— `AbilityStandardTrigger` + `extraString` 的样本

| 字段 | 原版值 |
|---|---|
| 摘要 | `special=False \| mainType=NoChargeTrigger \| rarity=Rare \| colors=2[Red,Void] \| charge=0/-1 \| trigLimit=none \| effects=1+1 \| pool=Colored(UnlockRelics_RedVoid)` |
| 主效果触发 | `triggerType=Passive`、`relativeCharacterType=Owner`、`extraString=(空)` |
| 主效果[0] | `effectName=ApplyAbility`、`abilityName=Burn`、`effectTargetEnum=RandomOpponent`、`numbers=3` |
| 附加效果触发 | `triggerType=AbilityStandardTrigger`、**`extraString=Burn`**、`relativeCharacterType=Owner` |
| 附加效果[0] | `effectName=AddMana`、`mana=R`、`effectTargetEnum=Self` |

> 这件把「**先给敌人挂 Burn，再在 Burn 触发时给自己加 R 法力**」串了起来 ——
> `extraString` 必须**逐字**等于能力名 `Burn`，平台不写就是 `null` ⇒ **永不匹配且不报错**。

---

## 6. 交叉引用

| 想知道什么 | 去哪 |
|---|---|
| 平台还差哪些能力、每条的证据命令 | 《[ModKit-能力缺口与下一步](ModKit-能力缺口与下一步.md)》§0 |
| 触发点的完整合法名单、每个触发点要哪些字段 | 《[ModKit-作者手册](ModKit-作者手册.md)》§5.2「触发点附加字段表」 |
| 205 个原版遗物名全表（`artTemplate` 取值来源） | 《[ModKit-原版素材索引](ModKit-原版素材索引.md)》§3 |
| 效果的写法与参数 | 《[ModKit-效果字典](ModKit-效果字典.md)》 |
| 原始数据 | `diceomancer-mod/state/archive/relic-dump-20261014/relic-dump.log` |
