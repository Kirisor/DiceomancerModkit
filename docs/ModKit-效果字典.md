# ModKit 效果字典（名单 + 写法）

> 本文件只有**四张名单**和**它们的写法**。所有实验过程、验证记录、排查叙事一律不写在这里。
>
> - 校验依据：`ModKitDump\capabilities.json`（就是 `ModKit.Tools.exe lint` 读的那份能力表）。
> - 卡名 / 遗物名的取值（`artTemplate`）见《[ModKit-原版素材索引](ModKit-原版素材索引.md)》，本文件不内联。
> - 踩坑与约定见《[ModKit-作者手册](ModKit-作者手册.md)》。

---

## 一、写法总览（写进 JSON 的位置）

```jsonc
// 卡（cards/xxx.json）
{
  "id": "my_card",
  "cost": ["Void"],                       // 费用槽：颜色见 §六；多色槽 [["Void","Red"]]
  "colors": ["Green"],                    // 流派色：同上（决定卡面颜色）
  "effects": [                            // ← 效果名单（§二）
    { "builtin": "block", "amounts": [3] },
    { "custom": "MyEffect", "amounts": [3] }   // 作者自写 C# 类（[ModEffect]）
  ],
  "keywords": [                           // ← 关键词名单（§三）
    "Exert",
    { "name": "Miracle", "effects": [ { "builtin": "draw", "amounts": [1] } ] }
  ]
}

// 遗物（relics/xxx.json）
{ "trigger": "BattleStart" }              // ← 触发点名单（§四）

// 能力（C# 侧）
{ "builtin": "gain_ability", "name": "DiceBonus", "amounts": [1] }   // ← 能力名单（§五）
```

**三条通读规则**

1. `builtin` 后面那个字符串 = §二表格第一列的 id；`custom` 后面是作者自己 `[ModEffect]` 类的 id。
2. 名字一律写**无前缀叶子名**（`my_mod.tenacious` → JSON 里写 `tenacious`）。
3. 加一个内置效果要同步四处（枚举 / 映射表 / 落地开关 / catalog），少一处就会"写了却走不到"。

---

## 二、效果名单（内置 27 个）

### 2.1 两种写法

```jsonc
{ "builtin": "block", "amounts": [3] }        // 内置语义
{ "custom": "MyEffect", "amounts": [3] }      // 作者自写 C# 类
```

### 2.2 完整名单

`amounts` 列 ✅ = 必填（数组，可多个）；❌ = 该效果不吃数字。

| `builtin` id | 含义 | `amounts` | 额外字段 | 可用性 |
|---|---|---|---|---|
| `deal_damage` | 造成伤害 | ✅ | `mode` | 可用 |
| `heal` | 治疗 | ✅ | — | 可用 |
| `block` | 获得格挡 | ✅ | — | 可用 |
| `lose_life` | 失去生命 | ✅ | `mode` | 可用 |
| `draw` | 抽 N 张 | ✅ | — | 可用 |
| `discard_random` | 随机弃 N 张 | ✅ | — | 可用 |
| `discard_hand` | 弃掉整手牌 | ❌ | — | 可用 |
| `discard_one_random` | 随机弃一张 | ❌ | — | 可用 |
| `gain_mana` | 获得法力 | ❌ | `color` | 可用 |
| `set_max_mana` | 改变法力上限 | ✅ | — | 可用 |
| `clean_mana` | 清空法力 | ❌ | — | 可用（平台自建特殊效果） |
| `add_card` | 加一张卡 | ✅ | `name` | 可用 |
| `add_temporary_card` | 加一张临时卡 | ✅ | `name` | 可用 |
| `add_random_card` | 随机加 N 张 | ✅ | `pool`（§2.4） | 可用（`specific` 路径已停用） |
| `gain_ability` | 获得能力 | ✅ | `name`（§五） | 可用 |
| `apply_prim_ability` | 获得基础能力 | ✅ | `name` | 可用（平台自建） |
| `apply_util_ability` | 获得功能能力 | ✅ | `name` | 可用（平台自建） |
| `change_max_health` | 改变生命上限 | ✅ | — | 可用 |
| `recover` | 恢复（耗竭类资源） | ✅ | — | 可用 |
| `set_number` | 设定数字 | ✅ | `setter`（§2.5） | 可用 |
| `number_plus_sub` | 数值加减 | ✅ | `setter` | 可用但**只能加**（正负写死在平台里） |
| `end_turn` | 结束回合 | ❌ | — | 可用（原版特殊效果） |
| `seek_card` | 从牌库检索符合条件的牌 | ✅ | `require`（§2.3） | 可用 |
| `improvise_card` | 探寻：抽候选卡给玩家挑，选中的进手牌 | ✅ | `pool` | 可用 |
| `apply_state` | 加特殊状态 | ✅ | `name` | ⚠️ 原版内容**从未用过** |
| `upgrade_card` | 升级一张卡 | ✅ | — | 🚫 **暂勿使用**（游戏无对应类，打出会 NRE） |
| ~~`draw_one`~~ | 立即抽一张 | — | — | 🚫 **不可用**（映射表里没登记，从 JSON 走不到） |

**传数字给能力的硬约束**：卡上给的数字个数**不能超过目标能力自己的数字参数位数**，否则游戏崩。

### 2.3 `require`：检索类效果的筛选条件（`seek_card`）

```jsonc
{ "builtin": "seek_card", "amounts": [2],
  "require": { "colors": ["Blue"], "describeKey": "my_mod.req_blue" } }
```

| `require` 字段 | 说明 |
|---|---|
| `colors` | 牌必须**含**这些流派色 |
| `excludeColors` | 牌必须**不含**这些色 |
| `rarities` | 稀有度白名单 |
| `keyword` | 牌必须带某关键词 |
| `describeKey` | **必填**：`CardRequirementDes` 表里的 key（玩家 tooltip 显示的条件文本） |

- `require` **不能漏写**：游戏侧默认值 = 不加任何限制 ⇒ 漏写不报错，而是静默变成「检索任意牌」。
- `describeKey` 是**键不是文本**；可复用原版 key（如 `BlueCard` / `GreenCard`），或填自己的 key 并在 mod i18n 里加一张 `CardRequirementDes` 表。
- `amounts` **至少 1 个**（游戏直接读第一个数，缺了 NRE）。

### 2.4 `pool`：随机加卡的卡池（`add_random_card` / `improvise_card`）

```jsonc
{ "builtin": "add_random_card", "amounts": [1],
  "pool": { "career": true, "describeKey": "my_mod.pool_all" } }
```

| `pool` 路径 | 说明 |
|---|---|
| `special` | **原版特殊池名**（实测存在：`Junk`、`Virtue`）。可省略 `describeKey`，平台默认用原版池名 |
| `career` | `true` = 全部标准卡（可叠 `require` 收窄） |
| ~~`specific`~~ | 🚫 **已停用**（会把对象引用写进存档，校验器直接报错）；请改用 `special` / `career` + `require` |

- `packName` / `describeKey` 是**本地化键**：走 `specific`/`career` 时必须给，并在 mod i18n 加一条 `CardRequirementDes`；漏写校验器报错，写了但表里没有 ⇒ 卡面显示 `No translation found for …`。
- `specific` 的卡名：**原版卡名不要加 mod 前缀**（平台策略是"先按原样查，查不到再补前缀"）。
- 🚨 **不要把任何对象引用（`CardSo` 等）放进 `EffectInfo`** —— 会让存档序列化抛异常并**截断存档**。

### 2.5 `setter`：数字改造类效果必需的配置对象

`set_number` / `number_plus_sub` 的卡面描述会直接解引用这个配置，**平台总是会构造它**（省略时用游戏默认值）。

```jsonc
{ "builtin": "number_plus_sub", "amounts": [3],
  "setter": { "permanent": true, "allowDice": true, "allowFromCard": false, "chooseCount": 1 } }
```

| `setter` 字段 | 省略时的默认 |
|---|---|
| `permanent` | `false`（本回合临时） |
| `allowDice` | `true`（可改造骰子） |
| `allowFromCard` | `false` |
| `chooseCount` | `1` |

`permanent` 同时决定描述键（`Key_SetNumber_Temp` / `…_Permanent`；两个 allow 都为 false 时多 `_NoDiceNoCard` 后缀）。

---

## 三、关键词名单

> **关键词只负责"把标签放上去"，行为由游戏自己的 `CardKeyword` 子类实现** —— 平台不实现行为。

### 3.1 实际可用（32 个 —— 内容里真的在用）

| 关键词 | 中文※ | number | toCard | mana | effects | 类型 |
|---|---|---|---|---|---|---|
| `Bonus` | 奖励 | — | — | — | ✅ | 自带效果 |
| `Check` | 检定 | ✅ | — | — | ✅ | 数值 + 效果 |
| `Choose` | 抉择 | — | — | — | ✅ | 自带效果 |
| `Construct` | 建造 | ✅ | ✅ | — | — | 变身（不可重复叠加） |
| `Constructed` | 已建造 | — | ✅ | — | — | 变身（不可重复叠加） |
| `Coordinated` | 协同 | ✅ | — | — | — | 数值 |
| `Distorted` | 扭曲 | — | — | — | — | 标记 |
| `Evolve` | 进化 | ✅ | — | — | — | 数值 |
| `Exert` | 耗竭 | — | — | — | — | 标记 |
| `Flow` | 流转 | — | — | — | ✅ | 自带效果 |
| `Fragile` | 脆弱 | — | — | — | — | 标记 |
| `Innate` | 固有 | — | — | — | — | 标记 |
| `Item` | 道具 | ✅ | — | — | — | 数值（不可重复叠加） |
| `Keep` | 保留 | — | — | — | — | 标记 |
| `Kick` | 增幅 | ✅ | — | — | — | 数值 |
| `Limited` | 限制 | — | — | — | — | 标记 |
| `Loaded` | 装填 | ✅ | ✅ | — | — | 变身（不可重复叠加） |
| `Malady` | 病痛 | — | — | — | — | 标记 |
| `Miracle` | 奇迹 | — | — | — | ✅ | 自带效果 |
| `Modify` | 改造 | — | — | — | ✅ | 自带效果 |
| `Perishable` | 易腐 | — | — | — | — | 标记 |
| `Phantom` | 幻影 | — | — | — | — | 标记 |
| `Rainbow` | 虹彩 | ✅ | — | — | ✅ | 数值 + 效果 |
| `Rebound` | 回弹 | — | — | — | — | 标记 |
| `Reload` | 装填完成 | ✅ | ✅ | — | — | 变身（不可重复叠加） |
| `Sacrifice` | 献祭 | — | — | — | — | 标记 |
| `Storm` | 风暴 | — | — | ✅ | — | 按费用颜色重复结算 |
| `Summon` | 召唤 | ✅ | ✅ | — | ✅ | 召唤 |
| `Temporary` | 临时 | — | — | — | — | 标记 |
| `Vengeance` | 复仇 | ✅ | — | — | ✅ | 数值 + 效果 |
| `Virtue` | 美德 | — | — | — | — | 标记 |
| `Wild` | 狂野 | — | — | — | — | 标记 |

「不可重复叠加」名单（`Item` / `Reload` / `Loaded` / `Construct` / `Constructed`）。

> ※ **中文列是不保准的参考译名**：平台的名单只有内部名（英文），游戏内实际显示的中文来自游戏本地化表（未导出）。已按实机核对过：`Kick` = 增幅、`Exert` = 耗竭、`Miracle` = 奇迹。**以游戏内文案为准。**

### 3.2 lint 认可、但内容从未使用（4 个 —— 别写）

```
Modified   Glitch   Duplicate   Assault
```

> lint 校验清单是 36 个名字（上面 32 + 这 4 个）。**能写哪些看 3.1；lint 认哪些名字看这里。**

### 3.3 危险名单（19 个 —— 写了直接崩游戏）

```
Powered    Resonance  Scorching  Opening    Cycle      Bloodthirsty  Growth
Pursuit    Tension    Release    Fracture   Improvise  Charge        Decay
Recover    Surge      GatherQi   Undying    Evolution
```

这 19 个在游戏私有判定函数里有分支，但内容里从未使用、`KeyWord` 本地化表里也没有条目 ⇒ 硬挂到卡上会让 tooltip 抛 `NullReferenceException`（**不是没效果，是崩游戏**）。

### 3.4 写法：字符串 / 对象，三个可选字段

```jsonc
"keywords": ["Exert"]                                     // 只要标签
"keywords": [{ "name": "Miracle", "effects": [ … ] }]     // 标签 + 配置
```

| 字段 | 含义 |
|---|---|
| `number` | 关键词自带的数值，卡面渲染成「`[关键词 N]`」。**只有吃数字的关键词才生效**（见 3.1 的 number 列），写在别的关键词上会被忽略 |
| `mana` | 费用颜色串，缩写连写：`R`红 `G`绿 `B`蓝 `P`紫 `V`虚 `K`黑。目前只有 `Storm` 用得上 |
| `toCard` | 变身/恢复的目标卡（本 mod 内的**短 id**，平台自动补前缀）。写错不报错，但那张卡打出时会变成空白卡（平台有加载期跨卡校验拦这个） |

**⚠️ 平台只负责"填目标"，绝不实现"什么时候切换"** —— 切换时机全在游戏的 `CardKeyword` 子类里。

### 3.5 机制型关键词的完整写法

**`Loaded` / `Reload`** —— 同一张牌的两种形态，两张卡都要声明，`toCard` 互指，都不可叠加：

```jsonc
{ "name": "Loaded", "number": 1, "toCard": "crossbow_reload" }
{ "name": "Reload", "number": 1, "toCard": "crossbow" }
```

**`Construct` / `Constructed`** —— 建造（`Construct` 要 `number` 当进度，`Constructed` 不要）：

```jsonc
{ "name": "Construct", "number": 3, "toCard": "gatling_gun" }
{ "name": "Constructed", "toCard": "metal_parts" }
```

**`Storm`** —— 按费用颜色重复结算：

```jsonc
{ "name": "Storm", "mana": "RR" }     // 每 2 点红法力触发一次重复结算
```

**`Miracle`** —— 关键词自带效果，**结算时机与卡牌自己的 `resolve` 是两码事，缺省 `OnCast`**：

```jsonc
{ "keywords": [ { "name": "Miracle", "effects": [ { "builtin": "block", "amounts": [3] } ] } ] }
```

> 绝不要让它继承"卡牌自己"的 `resolve`（例：整张卡是 `EndInHand`，但奇迹效果是打出时触发）。

**`Summon`** —— 四步链：① 打出召唤卡（进弃牌堆）② `AfterCardResolvePhase` 生成召唤物 ③ 召唤物生命 = `number` ④ 召唤物死亡 → 手牌发一张 `toCard` 指定的卡。

```jsonc
{ "name": "Summon", "number": 1, "summon": "FireElement", "toCard": "my_essence" }
```

- `summon` 是**平台扩展**：指定借用哪个原版召唤物的立绘与名字（留空 = 原版行为，即用卡名去查表；mod 卡名带前缀会查不到立绘，但不致命）。
- 要复刻完整链，必须自建一张带 `Temporary` 的"精粹牌"，`toCard` 指向它（不要自引用）。

---

## 四、触发点名单（32 个 —— 游戏实际会发出的）

```
AbilityStandardTrigger  AcquireRelic            BattleStart
BeenAttacked            BuyInStore              CardEffectNoCast
CardKeywordTrigger      CastCard                CastCardFinished
ChangeBattlePhase       ChangeWorldPhase        CharacterCheck
CharacterLoseLife       CharacterRollDice       DamageBeenDealt
DrawCard                EnemyDieStart           EnemyHealthBarBreak
EnterUnvisitedNode      ExertCard               HandCardCountChange
ManaConsumed            ManaOverflow            OrganismPerish
RelicActive             RelicCorrupted          ShuffleGraveToLibrary
SummonResolve           TakeRest                TimePass
UseTheOneDice           WholeNewTurnStart
```

**写法**：遗物用 `"trigger": "BattleStart"`；能力用 `"triggerType": "CastCard"`（写在 C# 能力类上）。

**合法性规则**（取值域是游戏的 `TriggerTargetInfo.AllTriggerType`，33 个）：

| 情况 | 结果 |
|---|---|
| 不在取值域里 | `[Error]`（拼错了） |
| 在取值域里但不在上面 32 个里 | 合法，只给 `[Warn]`：原版代码里没有任何地方发出它，写了也不会触发 |
| 在上面 32 个里 | 既合法、又真的会触发 —— **照这份写最省事** |

---

## 五、能力名单（原版 105 个）

### 5.1 写法

```jsonc
{ "builtin": "gain_ability",        "name": "DiceBonus", "amounts": [1] }
{ "builtin": "apply_prim_ability",  "name": "DeadLock",  "amounts": [1] }
{ "builtin": "apply_util_ability",  "name": "ExtraTurn", "amounts": [1] }
```

- **数字个数的硬约束**：卡上给的数字个数不能超过该能力自己的数字参数位数（超了崩游戏）。
- **能力名没有校验清单**：给自定义关键词/能力起名之前，自己在游戏内「战斗百科 → 角色增益」查一遍有没有撞名。
- 文案：能力用 `<全名>` + `<全名>_Des`（平台自动补）。

### 5.2 Buff（14）

| 名字 | effect | trigger |
|---|---|---|
| `CheatMax` | SpecialEffect | Special |
| `CriticalHit` | SpecialEffect | Passive |
| `DiceBonus` | SpecialEffect | Special |
| `Energetic` | AddMana | CastCard |
| `Evade` | SpecialEffect | Special |
| `Excitement` | AbilityPassiveAttackerDamageAddSub | Special |
| `Fortified` | Block | ChangeBattlePhase |
| `Fury` | SpecialEffect | ChangeBattlePhase |
| `Haste` | DrawOneCard | CastCard |
| `Power` | AbilityPassiveAttackerDamageAddSub | Passive |
| `StockPile` | SpecialEffect | ChangeBattlePhase |
| `ThickSkin` | AbilityPassiveDefenderDamageAddSub | — |
| `Thorn` | SpecialEffect | — |
| `Tough` | SpecialEffect | Passive |

### 5.3 DeBuff（12）

| 名字 | effect | trigger |
|---|---|---|
| `Bleed` | LoseLife | ChangeBattlePhase |
| `Blind` | SpecialEffect | Passive |
| `Burn` | SpecialEffect | ChangeBattlePhase |
| `CheatMin` | SpecialEffect | — |
| `DicePenalty` | SpecialEffect | — |
| `Nani` | SpecialEffect | DamageBeenDealt |
| `Panic` | SpecialEffect | CastCard |
| `Powerless` | AbilityPassiveAttackerDamageAddSub | — |
| `Slow` | SpecialEffect | CastCardFinished |
| `ThinSkin` | AbilityPassiveDefenderDamageAddSub | Passive |
| `Vulnerable` | SpecialEffect | — |
| `Weak` | SpecialEffect | — |

### 5.4 PrimAbility（54）—— 带 ★ 的是 unique

| 名字 | effect | trigger |
|---|---|---|
| `Barbarous` | DoNothing | Passive |
| `BirdMultiStrike` | DoNothing | Passive |
| `BadShooter` | SpecialEffect | Passive |
| `BloodRain` | AddMana | CastCard |
| `BodyBurning` | ApplyAbility | CastCard |
| `BreakCocoon` | SpecialEffect | CharacterLoseLife |
| `ChipStorm` | SpecialEffect | CharacterRollDice |
| `Construct` | SpecialEffect | CharacterLoseLife |
| `CounterHelix` | LoseLife | DamageBeenDealt |
| `CrazyCat` | SpecialEffect | ChangeBattlePhase |
| `CycleOfLife` | SpecialEffect | OrganismPerish |
| `DeadLock` ★ | SpecialEffect | BattleStart |
| `DefensiveTactic` | Block | CastCard |
| `DefensiveTacticPlus` | Block | CastCard |
| `DysonSphere` | AddMana | DrawCard |
| `ElasticDefence` | Block | CharacterLoseLife |
| `FateFavors` | DoNothing | Passive |
| `FireSuppress` | ApplyAbility | CastCardFinished |
| `FireworkArray` | AddCard | ChangeBattlePhase |
| `GloriousEvolution` | SpecialEffect | CastCardFinished |
| `GrandMaster` | SpecialEffect | Special |
| `GunFu` | DealDamage | CardKeywordTrigger |
| `Ignite` | AddCard | ChangeBattlePhase |
| `IncarnationOfDeath` | DealDamage | CastCardFinished |
| `IncarnationOfLife` | Heal | CastCardFinished |
| `LifeLink` | DoNothing | Passive |
| `LordofWar` | SpecialEffect | CastCard |
| `LuckyStreak` | ApplyAbility | ChangeBattlePhase |
| `MassProduction` | SpecialEffect | CardKeywordTrigger |
| `MixedFeeling` | SpecialEffect | CastCard |
| `Nichijou` | SeekCard | CastCardFinished |
| `ParadoxEngine` | Draw | CastCard |
| `PotionFighter` ★ | DoNothing | Passive |
| `Roulette` | SpecialEffect | ChangeBattlePhase |
| `ScatteredItems` | SpecialEffect | ChangeBattlePhase |
| `Scavenger` | AddMana | OrganismPerish |
| `SharkTyphoon` | DealDamage | DrawCard |
| `SkeletonHead` | DoNothing | Passive |
| `Sparks` | SpecialEffect | CastCardFinished |
| `StandardizedSummon` | SpecialEffect | ChangeBattlePhase |
| `Squirrel_TopMaster` | ApplyAbility | CastCardFinished |
| `Squirrel_TooSlow` | ApplyAbility | CharacterCheck |
| `SuperPigSkin` | DoNothing | Passive |
| `SuperTroubleMaker` | DoNothing | Passive |
| `Superposition` | SpecialEffect | Passive |
| `ThatsHandy` | DealDamage | CastCard |
| `TroubleMaker` | DoNothing | Passive |
| `Tulpenmanie` | SpecialEffect | Passive |
| `Vicissitudes` | SpecialEffect | OrganismPerish |
| `VoidError` | DoNothing | Passive |
| `WasteNot` | AddRandomCard | ChangeBattlePhase |
| `WhatDoesntKillMe` | ApplyAbility | ExertCard |
| `WildernessReclamation` | SpecialEffect | ChangeBattlePhase |
| `WolfPack` | DoNothing | Passive |

### 5.5 Util（25）—— 带 ★ 的是 unique

| 名字 | effect | trigger |
|---|---|---|
| `Astrolabe` | SpecialEffect | Passive |
| `CanFreeCastCardThisTurn` ★ | DoNothing | Passive |
| `CantPlayLimitedCard` | SpecialEffect | Passive |
| `CardCostLifeThisTurn` | LoseLife | Passive |
| `CopyNextCardCasted` | SpecialEffect | CastCard |
| `Cultivate` | AddMana | ChangeBattlePhase |
| `DoubleDamageThisTurn` | SpecialEffect | Passive |
| `DrawCardNextMain` | Draw | ChangeBattlePhase |
| `Epiphany` | AddMana | CastCard |
| `ExertCardAddCopyNextTurn` | SpecialEffect | ChangeBattlePhase |
| `ExtraTurn` ★ | DoNothing | Passive |
| `FirePunch` ★ | SpecialEffect | Passive |
| `Intrepid` ★ | SpecialEffect | Passive |
| `InvincibilityField` ★ | SpecialEffect | Passive |
| `LoseLifeWhenNextShuffle` | LoseLife | ShuffleGraveToLibrary |
| `MaxHand` ★ | Recover | Passive |
| `Omniscience` | SpecialEffect | Passive |
| `OneTurnDicePenalty` | SpecialEffect | ChangeBattlePhase |
| `Onslaught` | DealDamage | ManaConsumed |
| `PreventNextDistorted` ★ | SpecialEffect | Passive |
| `Protection` | DoNothing | Passive |
| `Stabilized` ★ | DoNothing | Passive |
| `StrikeOnTheWay` | DealDamage | WholeNewTurnStart |
| `TimingAndPlace` | SpecialEffect | ChangeBattlePhase |
| `WaitingForChance` | AddMana | ChangeBattlePhase |

---

## 六、写卡会用到的其它取值

**可用费用色（6）** —— 卡 `cost[]` / `colors[]`，`capabilities.json` 键 `availableCostColors`：

```
Red     红
Green   绿
Blue    蓝
Purple  紫
Void    虚
Black   黑
```

**多色槽**（`[["Void","Red"]]` = 这一格虚色或红色都能付）两条规则：
① 只有游戏**预制了图标**的组合才能用；② **顺序参与匹配**（`["Void","Red"]` 与 `["Red","Void"]` 是两个不同的键）。

**`artTemplate` 的取值** —— 卡名 534 / 遗物名 205，见《[ModKit-原版素材索引](ModKit-原版素材索引.md)》（不内联：它们是脚本生成内容，抄进来会造成两份真源）。
