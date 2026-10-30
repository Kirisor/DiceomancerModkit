# DICEOMANCER ModKit · 作者手册

本手册写给**做 mod 的人**：写什么文件、字段怎么填、什么写法会失败。
效果名、关键词名、能力名、触发点名这类**名单**不在这里，见《ModKit-效果字典.md》。

---

## 一、五分钟上手

### 1.1 一个 mod = 一个目录

```text
<mods 根目录>\<你的 mod id>\
├─ mod.json            ← 清单，唯一必填的"总入口"
├─ cards\              卡牌 JSON
├─ relics\             遗物 JSON
├─ keywords\           关键词 JSON
├─ abilities\          能力 JSON（可选）
├─ i18n\               文案（zh-Hans.json / en.json）
├─ art\                自定义卡面图（可选）
├─ sfx\                自定义音效（可选）
└─ <modid>.dll         有 C# 代码时才需要
```

`mods 根目录` 默认是 `<游戏根>\BepInEx\DiceomancerMods\`。
若 `BepInEx\config\diceomancer.modkit.cfg` 的 `[General] ModsRoot` 写了别的目录，以它为准；**同一个 mod 只放一处**。

### 1.2 最小可用 mod：清单 + 一张卡 + 一条文案

`mod.json`

```json
{
  "$schema": "modkit/mod@1",
  "id": "my_first",
  "name": "我的第一个 mod",
  "version": "1.0.0",
  "cards": ["cards/starter.json"],
  "i18n": ["i18n/zh-Hans.json"]
}
```

`cards/starter.json`

```json
{
  "$schema": "modkit/card@1",
  "id": "starter",
  "name": { "zh-Hans": "起手" },
  "cost": ["Red"],
  "rarity": "Common",
  "resolve": "OnCast",
  "artTemplate": "GoodStrike",
  "effects": [
    { "builtin": "deal_damage", "amounts": [6], "target": "Opponent", "mode": "Direct" }
  ]
}
```

`i18n/zh-Hans.json`

```json
{
  "$schema": "modkit/i18n@1",
  "language": "zh-Hans",
  "tables": {
    "CardName": { "my_first.starter": "起手" }
  }
}
```

### 1.3 装进游戏

1. 把整个 mod 目录放进 `mods 根目录`；
2. **重启游戏** —— 内容只在启动时装配一次，改完 JSON 必须重启；
3. 进一局，把卡拿到手上并**打出去**：卡面正确 ≠ 逻辑正确；
4. 出问题时看 `BepInEx\LogOutput.log`，搜 `[ModKit]`。

调试时想直接拿到 mod 内容：在 `diceomancer.modkit.cfg` 设 `[Diagnostics] GrantOnFirstBattle = true`（默认关闭），第一场战斗的发牌阶段会把 mod 内容发给你。

---

## 二、`mod.json` 字段表

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `$schema` | string | ✅ | 固定 `"modkit/mod@1"` |
| `id` | string | ✅ | 全局唯一，`^[a-z][a-z0-9_]{2,40}$` |
| `name` | string | ✅ | 显示名 |
| `version` | string | — | 如 `"1.0.0"` |
| `author` | string | — | 作者名 |
| `gameVersion` | string | — | 如 `"2022.3.3f1c1"` |
| `entryAssembly` | string | — | 有 C# 代码时填 dll 文件名，如 `"my_first.dll"` |
| `cards[]` | string[] | — | 卡牌文件路径（相对 mod 目录） |
| `relics[]` | string[] | — | 遗物文件路径 |
| `keywords[]` | string[] | — | 关键词文件路径 |
| `abilities[]` | string[] | — | 能力文件路径 |
| `i18n[]` | string[] | — | 文案文件路径 |

---

## 三、卡牌 `modkit/card@1`

### 3.1 字段表

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `$schema` | string | ✅ | `"modkit/card@1"` |
| `id` | string | ✅ | `^[a-z][a-z0-9_]{2,40}$` |
| `name` | object | ✅ | `{ "zh-Hans": "…", "en": "…" }`，至少一种语言 |
| `cost` | array | ✅ | 费用槽序列：颜色名 = 单色槽；颜色名数组 = 多色槽（该格任一色可付）；`[]` = 无费用卡 |
| `rarity` | string | ✅ | `Common` / `Uncommon` / `Rare` |
| `resolve` | string | ✅ | `OnCast` / `OnDraw` / `OnDiscard` / `EndInHand` |
| `artTemplate` | string | ✅ | 原版卡名（§11），用于继承卡面 / 动画 / tooltip |
| `art` | string | — | 自定义卡面图，相对 mod 目录，如 `art/my_card.png`；只收 `.png`/`.jpg`/`.jpeg`，建议正方形 256~512 px |
| `effects` | array | ✅ | 与 `keywords` 至少有一个 |
| `keywords` | array | — | 字符串，或 `{ "name": "…", "effects": [ … ] }` |
| `colors` | string[] | — | 卡牌**归属**颜色，最多 2 个 |
| `upgrade` | object | — | 升级增量：`effectNumbers` / `cost` / `colors` / `addKeywords` / `removeKeywords` / `replaceEffects` / `insertEffects` / `keywordNumbers` |

两条容易混的规则：

- **`cost` 与 `colors` 是两套系统**：`cost` 决定打出去花什么，`colors` 决定卡属于哪个颜色。要改花费只改 `cost`。
- **多色槽不是任意组合**：合法组合查 `knownCostCombos`（21 条，见 §11），颜色名必须在 `availableCostColors`（6 个）里。

### 3.2 实例一：攻击卡（带升级）

```json
{
  "$schema": "modkit/card@1",
  "id": "manual_strike",
  "name": { "zh-Hans": "手册·打击", "en": "Manual Strike" },
  "cost": ["Red"],
  "rarity": "Common",
  "colors": ["Red"],
  "resolve": "OnCast",
  "artTemplate": "GoodStrike",
  "effects": [
    { "builtin": "deal_damage", "amounts": [6], "target": "Opponent", "mode": "Direct" },
    { "builtin": "block", "amounts": [3] }
  ],
  "keywords": ["manual_bulwark"],
  "upgrade": {
    "effectNumbers": [
      { "index": 0, "amounts": [9] },
      { "index": 1, "amounts": [5] }
    ],
    "removeKeywords": ["manual_bulwark"],
    "cost": [["Red", "Green"]]
  }
}
```

- `deal_damage` **必须显式写 `target` 和 `mode`**：不写 `target` 时兜底为 `Self`，这张"造成伤害"的卡会打自己。
- `effectNumbers` 的 `index` 指向**本卡自己的 `effects`**，越界会被拦下。
- `cost: [["Red","Green"]]` 是升级后的多色槽（红或绿都能付）。

### 3.3 实例二：多效果 / 检索卡

```json
{
  "$schema": "modkit/card@1",
  "id": "manual_seek",
  "name": { "zh-Hans": "手册·检索", "en": "Manual Seek" },
  "cost": [["Purple", "Blue"]],
  "rarity": "Uncommon",
  "colors": ["Purple", "Blue"],
  "resolve": "OnCast",
  "artTemplate": "DysonSphereProgram",
  "effects": [
    {
      "builtin": "seek_card",
      "amounts": [1],
      "require": { "colors": ["Blue"], "describeKey": "manual_demo.req_blue" }
    },
    { "builtin": "gain_mana", "amounts": [1], "color": "Blue" },
    { "builtin": "draw", "amounts": [1] }
  ]
}
```

- `cost: [["Purple","Blue"]]` 是**一个**多色槽；`["Red"]` 是一个单色槽。
- 检索类效果**必须给 `require` 和 `require.describeKey`**：漏写 `require` 不报错，但会静默变成"检索任意牌"。
- `describeKey` 指向 `CardRequirementDes` 表里的键，文案写在 i18n 里。

### 3.4 实例三：自带卡面图

```json
{
  "$schema": "modkit/card@1",
  "id": "manual_art",
  "name": { "zh-Hans": "手册·自绘", "en": "Manual Art" },
  "cost": ["Blue"],
  "rarity": "Common",
  "colors": ["Blue"],
  "resolve": "OnCast",
  "artTemplate": "Block",
  "art": "art/manual_art.png",
  "effects": [ { "builtin": "block", "amounts": [5] } ]
}
```

- `art` 是**相对 mod 目录**的路径，无法越出 mod 目录；文件不存在会在加载期就报错。
- `artTemplate` 仍必填：自定义图只换卡面那一张，动画与 tooltip 仍继承模板卡。
- 图按正方形拉伸显示；单边超过 4096 px 或文件超过 16 MB 会被拒收并退回模板卡面。

---

## 四、效果写法

名单（26 个内置效果及各自的额外字段）见《ModKit-效果字典.md》§二。

### 4.1 基本形状

```json
{ "builtin": "block", "amounts": [5] }
```

- `builtin` 与 `custom` 二选一，都填或都不填都会报错。
- `amounts` 是数组，每项是整数或骰子对象 `{ "dice": { "min": 1, "max": 6 } }`。
- **一条效果只读第一个数字**。要多个数值就拆成多条 effects。
- `target` 只能是 `Self` / `Opponent` / `AllOpponents` / `All` / `RandomOpponent`。
- `effects` 数组的**顺序就是结算顺序**。

### 4.2 卡池：`add_random_card` / `improvise_card`

```json
{
  "builtin": "add_random_card",
  "amounts": [1],
  "pool": {
    "entries": [
      { "p": 65, "special": "CardsFromOtherRealm", "require": { "rarities": ["Common"] } },
      { "p": 35, "career": true, "require": { "rarities": ["Uncommon", "Rare"] } }
    ],
    "describeKey": "manual_demo.pool_career"
  }
}
```

- `pool` 必须给，否则卡面预览会抛异常。
- 取卡路径三选一：`special`（原版池名）/ `career: true`（全部标准卡）/ `specific`（**不要用**，见 §4.3）。
- `entries` 每条必须显式写 `p`（整数 ≥ 0）；权重之和必须 ≥ 1。
- 单条目池可直接写在 `pool` 上：`{ "career": true, "describeKey": "…" }`。

### 4.3 不要用的写法

| 写法 | 后果 |
|---|---|
| `pool.specific`（写死卡名表） | 存档时抛 `InvalidDataContractException`，可能损坏战役存档 |
| 一条效果写多个数字 | 只有第一个生效，其余静默忽略 |
| `target` 写 `AllAllies` / `OtherAllies` | 不在合法值里，报错 |
| 实现异步 `ApplyAsync` | 适配层未接通，运行期只告警并跳过；自写效果请用 `SyncCardEffect` |
| 文案里出现 `{` / `}` | 格式化解析异常，战斗中弹框（见 §8.3） |

---

## 五、遗物 `modkit/relic@1`

### 5.1 字段表

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `$schema` | string | ✅ | `"modkit/relic@1"` |
| `id` | string | ✅ | `^[a-z][a-z0-9_]{2,40}$` |
| `name` | object | ✅ | `{ "zh-Hans": "…" }` |
| `artTemplate` | string | ✅ | 原版遗物名（§11） |
| `trigger` | string | ✅ | 触发点（见《效果字典》§四） |
| `effects` | array | ✅ | 与卡牌同一套写法（§4） |
| `addToPool` | bool | — | 缺省 `true` 进随机遗物池；`false` 则不由随机池获得 |
| `rarity` | string | — | `Common`/`Uncommon`/`Rare`。不写则沿用 `artTemplate` 的稀有度（平台是克隆模板，别靠挑模板控制档位） |
| `poolColors` | string[] | — | 写了 ⇒ 进「按色组专属」的 `Colored` 集合（游戏只放行 `colorEs ⊆ 本局三色` 的职业）；不写 ⇒ 进共用 `Standard` 集合（所有职业都能刷） |
| `colors` | string[] | — | 遗物自己的色味（显示用），不写则沿用模板 |
| `triggerLimit` | int | — | 触发次数上限，必须与 `triggerLimitType` 成对 |
| `triggerLimitType` | string | — | `EachTurn`（每回合重置）/ `EachBattle`（整场 N 次） |
| `cardRequirement` | object | — | 触发点的卡牌过滤：`keyword` / `colors` / `excludeColors` / `rarities` / **`describeKey`（必填）**。只对 `CastCard` / `CastCardFinished` / `DrawCard` / `DiscardCard` / `ExertCard` 生效 |
| 触发点附加字段 | 视触发点 | 视触发点 | 见 §5.2 |

遗物**必须**同时有 `trigger` 和 `effects`。

### 5.2 触发点附加字段

| 触发点 | 必需字段 | 缺了会怎样 |
|---|---|---|
| `ChangeBattlePhase` | `battlePhase: { "phase": …, "isStart": … }`，`phase` ∈ `Upkeep`/`Main`/`End`/`CleanUp` | 报错 |
| `WholeNewTurnStart` | `certainTurn: { "require": …, "turnCount": …, "relation": … }`，`relation` ∈ `Before`/`Exactly`/`After` | 报错 |
| `EnemyHealthBarBreak` | `relativeCharacter`：`Owner`/`Opponents`/`All` | 警告，且永远不匹配 |
| `ManaConsumed` | `colorE`：`Red`/`Green`/`Blue`/`Purple`/`Void`/`Black` | 警告，且永远不匹配 |
| `CardKeywordTrigger` / `AbilityStandardTrigger` / `TakeRest` | `extraString`（如能力名 `"Burn"`） | 警告，且永远不匹配 |
| `CharacterRollDice` | `relativeCharacter` | 用默认值，能用但语义受限 |
| 其余触发点 | 无 | — |

给触发点**不读**的字段不会崩，但会被忽略并给一条警告（例如在 `BattleStart` 上写 `colorE`）。

### 5.3 遗物池归属：共用还是职业专属

`RelicDataManager.SetCampaignRelicPool` 只收两类集合：`Standard`（全都收）与 `Colored`（仅当 `colorEs ⊆ 本局三色`）。所以：

- **不写 `poolColors`** ⇒ 平台建 `Standard` 集合 ⇒ **所有职业都能刷到**（这是旧行为，别指望它专属）。
- **写 `poolColors`** ⇒ 平台按同色组另建 `Colored` 集合。例如奶蛙（紫绿黑）写
  `"poolColors": ["Purple","Green","Black"]`，而没有任何原版职业的三色含 Black
  ⇒ 这三件遗物**只有奶蛙能刷到**，且和原版的 15 个色组遗物集合同构（各 3 件、1 普通 + 1 罕见 + 1 传说）。

### 5.4 `artTemplate` 怎么挑


平台是**克隆**一个原版遗物来造你的遗物，模板的 `extraRelicEffectInfos`（额外效果表）与 `requiredChargeCount`（充能数）会**原样继承**。
所以要挑 `effects=N+0`（没有额外效果）、`charge=0/-1`（不要充能）的干净模板：在 `BepInEx\LogOutput.log` 里搜 `[relic] ` 摘要行，按这两个条件挑名字。

### 5.5 实例

```json
{
  "$schema": "modkit/relic@1",
  "id": "manual_relic_turn_draw",
  "name": { "zh-Hans": "手册·回合抽牌", "en": "Manual Turn Draw" },
  "artTemplate": "Bonfire",
  "trigger": "WholeNewTurnStart",
  "certainTurn": { "require": true, "turnCount": 2, "relation": "Exactly" },
  "effects": [ { "builtin": "draw", "amounts": [2] } ],
  "addToPool": true
}
```

`WholeNewTurnStart` 必须给 `certainTurn`；上例表示"只在第 2 回合触发"，想每回合都触发就写 `"require": false`。

---

## 六、关键词 `modkit/keyword@1`

### 6.1 字段表

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `$schema` | string | ✅ | `"modkit/keyword@1"` |
| `id` | string | ✅ | `^[a-z][a-z0-9_]{2,40}$`，必须小写 |
| `name` | object | ✅ | `{ "zh-Hans": "…" }` |
| `hasNumber` | bool | — | 关键词是否带数字 |
| `artTemplate` | string | — | 关键词图标借用的原版模板 |
| `effects` | array | — | 关键词自带的效果（写法同 §4） |

### 6.2 类名规则

平台**按名字拼类名**：`id` 为 `manual_bulwark` ⇒ 找类 `Keyword.CardKeywordManualBulwark`（下划线分段各自首字母大写）。

类名不存在时游戏会**静默退化成基类** —— 不报错、不崩，但关键词没有任何行为。所以自定义关键词要么只当标签用（不写 C#），要么把类名拼对。

### 6.3 实例

`keywords/manual_bulwark.json`

```json
{
  "$schema": "modkit/keyword@1",
  "id": "manual_bulwark",
  "name": { "zh-Hans": "手册坚壁", "en": "Manual Bulwark" },
  "hasNumber": false
}
```

行为实现：

```csharp
using Card;
using Cysharp.Threading.Tasks;
using Keyword;

public sealed class CardKeywordManualBulwark : CardKeyword
{
    public override UniTask CardResolvePhaseAfterEffect(CardBase card)
    {
        CharacterPlayer player = CharacterPlayer.Instance;
        if (player != null) { player.ApplyBlock(3); }
        return UniTask.CompletedTask;
    }
}
```

卡片上引用：`"keywords": ["manual_bulwark"]`，或对象形式 `{ "name": "manual_bulwark" }`。
文案写在 i18n 的 `KeyWord` 表：`manual_bulwark`（名字）与 `manual_bulwarkDescription`（说明）。

---

## 七、自定义效果 `[ModEffect]`

### 7.1 契约

```csharp
[ModEffect("ManualGainBlock")]
public sealed class ManualGainBlockEffect : SyncCardEffect
{
    public override void Apply(EffectContext ctx)
    {
        ctx.Block(ctx.Amount);
    }
}
```

- 卡里的 `custom` 必须与 `[ModEffect("…")]` 的 id 完全一致（大小写敏感）。
- id 用 **PascalCase、不带下划线**：平台会在游戏侧生成 `Effects.Special.CardEffect.EffectSpEffect<id>` 适配类，游戏按字面拼接查找。
- 实现类必须 **public 且有无参构造函数**。
- 类名**不要**取成 `EffectSpEffect<id>`（那是适配壳的名字）。
- 继承 `SyncCardEffect` 实现 `Apply`；不要实现异步 `ApplyAsync`。
- 描述键固定为 `Key_<id>`，写在 `EffectDescription` 表里，纯文本。
- 卡上引用：`{ "custom": "ManualGainBlock", "amounts": [2] }`。
- 有 C# 代码时 `mod.json` 必须写 `entryAssembly`。

编译方式（二选一）：

```bash
bash studio/build-mod.sh <modId> [mods 根目录]
```

或用 ModKit Studio 顶栏的「编译本 mod」。

### 7.2 上下文 `EffectContext` 可用成员

**真的能用**：`Amount`、`AmountAt(i)`、`Block(n)`（只对 `Self` 生效）、`OwnerName`、`HandCount`、`Mana`、`Random`、`Log(msg)`。

**调了只会告警并跳过**（不报错，但什么也不发生）：
`DealDamage`、`Heal`、`LoseLife`、`Draw`、`DiscardRandom`、`GainMana`、`SetMaxMana`、`CleanMana`、`AddState`、`ReduceState`、`GiveAbility`、`GiveCard`、`GiveTemporaryCard`、`UpgradeCard`、`EndTurn`、`SubscribeTrigger`；属性 `Self`、`Opponent`、`Targets`、`LibraryCount`、`GraveCount`、`HandCards`、`MaxMana`。

> 需要这些行为时，用内置效果在 JSON 里组合，不要在 C# 里调它们。

---

## 八、文案 `modkit/i18n@1`

### 8.1 文件形状

```json
{
  "$schema": "modkit/i18n@1",
  "language": "zh-Hans",
  "tables": {
    "CardName": {
      "manual_demo.starter": "起手",
      "manual_demo.manual_strike": "手册·打击"
    },
    "FlavorText": { "manual_demo.starter": "万事开头难。" },
    "KeyWord": {
      "manual_bulwark": "手册坚壁",
      "manual_bulwarkDescription": "本卡结算完成后，额外获得 3 点格挡。"
    },
    "CardRequirementDes": { "manual_demo.req_blue": "蓝色牌" },
    "Relic": { "manual_demo.manual_relic_turn_draw": "手册·回合抽牌" }
  }
}
```

| 字段 | 类型 | 必填 |
|---|---|---|
| `$schema` | string | ✅ `"modkit/i18n@1"` |
| `language` | string | ✅ 如 `zh-Hans` / `en` |
| `tables` | object | ✅ `表名 → { 键: 文本 }` |

### 8.2 表名清单（27 个，大小写敏感）

```text
Ability  AbilityDescription  Ascension  CardName  CardRequirementDes  CareerUpgrade
Command  Dialogue  DiceRelated  EffectDescription  EffectDescriptionAbbreviation
Encyclopedia  EnemyInfo  EnemyIntention  FlavorText  Helper  KeyWord  PickCardConfigure
Relic  SpecialState  Story  StoryMarker  TachiE Dialogue  Tooltips  Trigger  Tutorials  UI
```

常用的几个：

| 表名 | 用途 | 键怎么起 |
|---|---|---|
| `CardName` | 卡牌名 | `<modid>.<卡id>` |
| `FlavorText` | 风味文本 | `<modid>.<卡id>` |
| `KeyWord` | 关键词名 / 说明 | 关键词 id；说明用 `<id>Description` |
| `Relic` | 遗物名 | `<modid>.<遗物id>` |
| `CardRequirementDes` | 卡池 / 检索条件的说明 | 自己起（如 `<modid>.req_blue`） |
| `EffectDescription` | 自定义效果描述 | `Key_<效果id>` |

遗物描述由游戏**程序化生成**，没有 `RelicDescription` 表。

### 8.3 键名规则与花括号禁令

- mod 内内容带 `<modid>.` 前缀；**关键词例外**，`KeyWord` 表的键就是关键词 id。
- **文案里不能出现 `{` 或 `}`**：会被当格式化模板，战斗中直接弹异常（`Error parsing format string`）。
  平台不支持占位符也不做转义 —— 要引用数值就用文字描述。

---

## 九、音效

两个调用，都返回 `bool`（`true` = 已交给游戏播放）：

```csharp
using ModKit.Api;

string dir = System.IO.Path.GetDirectoryName(typeof(MyKeyword).Assembly.Location);
string wav = System.IO.Path.Combine(dir, "sfx", "naifrog.wav");

if (!ModKitSfx.PlayFile(wav))   // 文件不存在 / 格式不支持 ⇒ false
{
    ModKitSfx.Play("CopyCard"); // 退回原版音效
}
```

- 文件放在**当前生效的 mod 目录**下，推荐 `sfx\` 子目录；用 `Assembly.Location` 定位，不要写死游戏路径。
- 标准 PCM WAV：8/16/24/32 位整数或 32 位浮点，单声道或立体声。
- 替换 `.wav` 不需要重新编译，重启游戏即可。
- 可用原版音效名：在 `diceomancer.modkit.cfg` 设 `[Diagnostics] SfxDump = true`，启动后日志会打印全部名字。

---

## 十、报错速查

| 现象 | 原因 | 改法 |
|---|---|---|
| `关键词 'xxx' 未注册` | 关键词文件没进 `mod.json` 的 `keywords[]` | 补上清单条目 |
| `模板 'xxx' 不在本 build 的原版卡名清单里` | `artTemplate` 写了不存在的名字（最常见是把 mod 自绘图名填进去） | 填原版卡名（§11），自定义图放 `art` |
| `触发类型 'xxx' 不在 …AllTriggerType 表里` | `trigger` 拼错或用了不存在的名字 | 用《效果字典》§四 的 32 个 |
| `触发类型 'WholeNewTurnStart' 需要附加字段 certainTurn` | 缺附加字段 | 见 §5.2 |
| `需要附加字段 relativeCharacter / colorE / extraString` | 同上 | 见 §5.2 |
| `id 不符合 ^[a-z][a-z0-9_]{2,40}$` | id 有大写 / 太短 / 以数字开头 | 小写+下划线，≥3 字符 |
| `未知内置语义 'xxx'` | `builtin` 写错 | 用《效果字典》§2.2 的 26 个名字 |
| `必须有 builtin 或 custom 之一` / `只能填一个` | 写法冲突 | 只留一个 |
| `这个效果写了 N 个数字，只读第 1 个` | 一条效果塞了多个数值 | 拆成多条 effects |
| `pool.specific 暂不支持` | 用了写死卡名表 | 改 `special` 或 `career: true` |
| `必须显式给 p` | 卡池条目缺权重 | 每条写 `p` |
| `缺少 cost` | 卡片没写费用 | 无费用写 `[]`，单色写 `["Void"]`，多色槽写 `[["Void","Red"]]` |
| `颜色 '…' 未注册图标键` | 费用颜色名写错 | 用 6 个合法颜色 |
| `卡牌颜色最多只能有 2 种` | `colors` 超限 | 减到 2 个；改花费请改 `cost` |
| `游戏里没有名为 "xxx" 的本地化表` | 表名拼错 | 照 §8.2 的 27 个 |
| `文案里出现了 '{'` | 文案含花括号 | 改成纯文本 |
| `JSON 解析失败` | 语法错误（多逗号、缺引号） | 按报错位置修 |
| `缺少 $schema` | 没写契约版本 | 显式写 `"modkit/card@1"` 等 |
| `缺少 effects，且 keywords 也为空` | 空卡 | 至少给一项 |
| `找不到模板卡` | `artTemplate` 被写成了 mod 图片名 | 改回原版卡名，图片放 `art` |

---

## 十一、名字从哪查

| 要查找 | 字段 | 条数 |
|---|---|---|
| 卡牌 `artTemplate` | `vanillaCardNames` | 534 |
| 遗物 `artTemplate` | `vanillaRelicNames` | 205 |
| 关键词名 | `knownKeywords` | 36 |
| 可用触发点 | 《效果字典》§四 | 32 |
| 单色费用名 | `availableCostColors` | 6 |
| 多色费用槽合法组合 | `knownCostCombos` | 21 |

以上清单都在 `BepInEx\ModKitDump\capabilities.json`（游戏运行一次后生成）。
同一目录下还有 `cards.json`（卡清单）与 `art\*.png`（约 500 张卡面缩略图）。
