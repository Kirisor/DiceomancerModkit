# ModKit P0 · 内容契约与 API 规格

> **阶段**：P0（契约与骨架）｜**编写日期**：2026-10-11｜**状态更新**：2026-10-11（P0–P4 全部完工并实机验收）
> **上位文档**：[`docs/Mod平台设计.md`](Mod平台设计.md)（v1.0，三决策已确认）
> **本文件性质**：**契约冻结文件**。各阶段已实现的代码必须与本文一致；改契约须先改本文并记版本。
> **硬约束来源**：平台的接缝模型与错误处理纪律；认知纪律 C9 —— **未做 ≠ 已验证**。

---

## 1. 两条贯穿全局的硬规则

### 规则 1 · ModKit.Api 不得引用任何游戏类型

`ModKit.Api` 只允许引用 **BCL**（`System.*`）。**禁止** `using Card;` / `using Effects;` / `using Combat;` / `using UnityEngine;` / `using Cysharp.Threading.Tasks;`。

**为什么**：这是"作者不用懂游戏"的**机械保证**——不是文档承诺，而是编译器强制。作者引用 Api 时若发现某个参数要传 `CardSo`，那说明契约漏了。

**校验方式（P0 已实施并跑通）**：分层两道——

| 层 | 手段 | P0 实测结果 |
|---|---|---|
| **编译期** | 编译 Api 时只喂 net472 引用程序集（131 个），**不喂** `Diceomancer_Data\Managed`。引用游戏类型即编译失败 | ✅ 编译通过（`ModKit.Api.dll` 15,872 字节） |
| **产物期** | 读产物的**程序集引用表**，断言不含游戏/第三方运行时程序集 | ✅ **只引用 1 个程序集：`mscorlib`** |

> 产物期这一层才是"引用即违约"的机械证据——它读的是编译器写进元数据的真实依赖，不是扫注释。脚本：`modkit/build-api.ps1`（退出码 0 = 两道全过）。
>
> **这条规律本身就是可复用的手法**：**约定"不许引用"是纸面约束，读产物引用表才是机械约束**。前者靠自觉，后者靠元数据——编译器写进程序集清单的依赖骗不了人。任何"作者不该拿到某类类型"的分层设计，都应该配一道这样的产物期断言，而不是只在文档里写"禁止"。

### 规则 2 · 每个 API 表面都必须带实证标记

沿用认知纪律 C9。三档：

| 标记 | 含义 | 可否对外承诺 |
|---|---|---|
| ✅ **已实证** | 本工作区实机跑通过 | 可以 |
| ⚠️ **实现就绪** | 游戏代码可见、平台已实现，**但未实机验证** | 可以发布，**文档必须标"未经实机验证"** |
| 🚫 **暂不支持** | 探针未通过 / 机制不存在 | 不可以，必须明说 |

> 档位的**权威来源是代码里的一张表**，不是本文的散文：内置语义见 `modkit/ModKit.Api/Effects.cs` 的 `BuiltinEffectCatalog`（逐项 `EvidenceStatus`）。
> 截至 2026-10-11，该表里 ✅ 的项目为 **`block` / `draw` / `gain_ability`** 三项，其余 12 项内置语义为 ⚠️（已实现、未实机验证）。档位随实机验证推进而变，**以代码表为准**。
> 本章第 4 节的每个方法都标了档位。**未标或标 🚫 的一律不进 API。**

---

## 2. JSON Schema 版本策略

| schema | 版本串 | 稳定性 |
|---|---|---|
| 卡牌 | `modkit/card@1` | 冻结（P1） |
| 遗物 | `modkit/relic@1` | 冻结（P1） |
| 本地化 | `modkit/i18n@1` | 冻结（P1） |
| 关键词 | `modkit/keyword@1` | 冻结（P2 已落地） |
| 能力 | `modkit/ability@1` | 冻结（P2 已落地） |
| 剧情后果 | `modkit/story@1` | **草案**（探针判定部分可行，平台尚未实现） |

**规则**：`$schema` 缺失或版本不认识 → **平台拒绝加载该文件并报 Error**（不静默跳过）。这是把"静默失败"变"加载期报错"的第一道闸。

**为什么必带 `$schema`**：游戏侧的教训是"命名写错静默不生效"（四条反射挂钩点全部如此）。平台不能重复这个错误——所以要求每个内容文件自报契约版本。

---

## 3. 卡牌 schema（`modkit/card@1`）—— 冻结

### 3.1 作者写的样子

`mods/<modid>/cards/*.json`：

```json
{
  "$schema": "modkit/card@1",
  "id": "my_strike",
  "name": { "zh-Hans": "我的打击", "en": "My Strike" },
  "cost": ["Void"],
  "rarity": "Common",
  "colors": ["Red", "Green", "Blue"],
  "resolve": "OnCast",
  "artTemplate": "Strike",
  "effects": [
    { "builtin": "deal_damage", "amount": 8, "target": "Opponent" },
    { "builtin": "draw", "amount": 1 }
  ],
  "keywords": ["Temporary"]
}
```

### 3.2 字段规格

| 字段 | 类型 | 必填 | 取值与约束 | 映射到游戏 |
|---|---|---|---|---|
| `$schema` | string | ✅ | 必须 `modkit/card@1` | 平台校验 |
| `id` | string | ✅ | `^[a-z][a-z0-9_]{2,40}$`，**mod 内唯一**；平台加 mod 前缀防跨 mod 冲突 | `CardInfo.cardName` |
| `name` | object | ✅ | `{语言标签: 文本}`，至少含一种 | 本地化表 `CardName` |
| `cost` | string[] | ✅ | **每项必须是单色**，取值 `Red/Green/Blue/Purple/Void/Black`；**平台启动探测该 build 真实注册的色集合，未注册即加载期报错** | `CardInfo.cardManaCostColorEArrays`（`ColorEArray[]`） |
| `rarity` | string | ✅ | `Common/Uncommon/Rare` | `CardInfo.rarity` |
| `colors` | string[] | ⚠️ | 卡面流派色，同上取值域 | `CardInfo.cardColorEs` |
| `resolve` | string | ✅ | `OnCast/OnDraw/OnDiscard/EndInHand` | `CardEffectResolveType` |
| `artTemplate` | string | ✅ | **原版卡名**（Q2；模板是外观与动画的来源）；平台启动时校验它确实存在 | 克隆该 `CardSo` |
| `art` | string | ⚠️ | **自带卡面图**：相对 mod 目录的路径，只收 `.png`/`.jpg`/`.jpeg`，不接受绝对路径与 `..`（Q2 更新 2026-10-14）；留空 = 沿用模板卡面（详见《作者手册》§3.4） | `CardSo.cardArt` |
| `effects` | object[] | ✅ | 见 §3.3，至少 1 项 | `CardEffectInfo[]` |
| `keywords` | string[] | ❌ | 关键词名；未注册关键词 → 加载期报错 | `CardKeyWordInfo[]` |

### 3.3 `effects[]` 两种形态

**形态 A · 内置语义**（推荐，覆盖主流）

```json
{ "builtin": "<语义名>", "amount": 8, "target": "Opponent" }
```

- `amount`：整数，或 `{ "dice": {"min":1,"max":6} }` 表示骰子数（对应游戏的 `RNumberInfo(isDice:true)`）
- `target`：`Self/Opponent/AllOpponents/All/RandomOpponent`（**仅这 5 个**——见 §4.6 已实证的坑）

**形态 B · 自定义效果**（C# 兜底）

```json
{ "custom": "my_sacrifice" }
```

指向作者用 `ModKit.Api` 实现、并以 `[ModEffect("my_sacrifice")]` 标注的类。

### 3.4 加载期校验清单（平台必须逐条挡，全部报错而不静默）

1. `$schema` 版本正确；
2. `id` 格式合法且 mod 内唯一、全局（含前缀）唯一；
3. `cost` 每项 ∈ 探测得到的**真实合法色集合**（见 §4.6）；
4. `cost` 非空（费用为 0 的卡用 `["Void"]` 语义表达，需在文档写明）；
5. `artTemplate` 在**本 build 的原版卡名清单**里存在；
6. `rarity` / `resolve` / `effects[].target` 在枚举白名单内；
7. 每个 `{ "builtin": X }` 的 X 在**内置语义表**里存在；
8. 每个 `{ "custom": Y }` 的 Y 有对应的已注册效果类；
9. `keywords[]` 每项已注册；
10. `name` 至少一种语言；缺 `description` 时平台生成兜底描述并记 Warn。

> **本清单共 10 条**（全文凡提"加载期校验"均指这 10 条）。10 条里任何一条不过 → 该卡**不加载**、记 Error 并附**具体字段路径**，其余内容照常加载。这是"失败隔离"的最小单元。

### 3.5 平台必须吸收的脏活（对应设计文档 P3）

| 脏活 | 出处 |
|---|---|
| 克隆原版 `CardSo` 借卡面/动画/Tooltip | `ModKit.Game/ContentAssembler.cs` |
| 反射写私有字段 `CardSo.cardInfo` | 同上（`Reflect.cs` 集中式访问器） |
| 装配 `CardInfo` 的 struct 全字段（含必填最小集） | 同上 |
| `AddEntry` 后**必须** `IsSmart = true` | 项目既有硬约束（否则 `{Number}` 抛 `FormatException`） |
| 把卡塞进 `_allInGameCardSoDictionary` / 随机池私有字典 | 同上 |
| 效果类命名与注册 | 四条反射挂钩点 + `ModKit.Core/ContentRegistry.cs` |

---

## 4. ModKit.Api 表面规格（C# 兜底路径）

### 4.1 效果作者唯一要实现的东西

```csharp
public interface ICardEffect
{
    Task ApplyAsync(EffectContext ctx);
}
```

**为什么是 `Task` 而不是游戏的 `UniTask`，也不是 `ValueTask`**：

- `UniTask` 是 `Cysharp` 第三方类型，属游戏侧运行时依赖。Api 引用它就违反规则 1。
- **`ValueTask` 在 net472 的 BCL 里不存在** —— 首次编译即以此失败（`CS0234: System.Threading.Tasks 中不存在 ValueTask`），它属于 `System.Threading.Tasks.Extensions` 包。让 mod 作者多引一个包与"零门槛"目标冲突。
- 所以用 `Task`（BCL，C# 5+ 即可 `await`）。代价只是每个异步效果一次分配，而**同步效果**用下面的基类复用同一个已完成 Task，无额外分配。

> **这条修正是"契约必须被编译验证"的直接证据**：如果只写文档不编译，这个错误会一直留到后续阶段才暴露。

**返回 `void` 的便捷基类**（消掉 async 样板）：

```csharp
public abstract class SyncCardEffect : ICardEffect
{
    public abstract void Apply(EffectContext ctx);
    public Task ApplyAsync(EffectContext ctx) { Apply(ctx); return CompletedTask; }
}
```

> 与 `ICardEffect` 一致：`ApplyAsync` 返回 **`Task`**（不是 `ValueTask`）。`CompletedTask` 是基类里缓存的单例（`Task.FromResult(true)`），因为 net472 的 BCL 没有 `Task.CompletedTask`。
> 实现见 `modkit/ModKit.Api/Effects.cs`。

### 4.2 `EffectContext` —— 作者能碰的全部游戏世界

**读**（快照语义，不暴露游戏对象）

| 成员 | 类型 | 档位 |
|---|---|---|
| `Self` / `Opponent` | `ICombatant` | ⚠️ |
| `OwnerName` | `string` | ⚠️ |
| `Amount` | `int`（第 1 个数字参数位的取值） | ✅ |
| `AmountAt(int index)` | `int` | ⚠️ |
| `HandCount` / `LibraryCount` / `GraveCount` | `int` | ⚠️ |
| `HandCards` | `IReadOnlyList<ICardRef>` | ⚠️ |
| `Mana` / `MaxMana` | `int` | ⚠️ |
| `GetState(string name)` | `int` | ⚠️ |
| `GetStateNames()` | `IReadOnlyList<string>` | ⚠️ |
| `Log(string message)` | `void` | ✅ |
| `Random` | `Random`（**平台注入的确定性种子**，不直接用 `UnityEngine.Random`） | ⚠️ |

**改**（平台负责入队/时序与幂等）

| 方法 | 语义 | 档位 |
|---|---|---|
| `DealDamage(int, ICombatant?, DamageMode)` | 造成伤害；`DamageMode.Direct/Indirect` **必须显式** | ⚠️ |
| `Block(int, ICombatant?)` | 加格挡 | ✅ |
| `Heal(int, ICombatant?)` | 治疗 | ⚠️ |
| `LoseLife(int, ICombatant?)` | 失去生命（无视格挡） | ⚠️ |
| `Draw(int)` | 抽牌 | ✅ |
| `DiscardRandom(int)` | 随机弃牌 | ⚠️ |
| `AddState(string, int, CombatantSelector?)` | 加特殊状态 | ⚠️ |
| `ReduceState(string, int, CombatantSelector?)` | 减特殊状态 | ⚠️ |
| `GainMana(string color, int)` | 获得法力（**color 必须单色**） | ⚠️ |
| `GiveCard(string cardId, string place, int amount)` | 生成卡到 手牌/牌库/弃牌堆 | ⚠️ |
| `GiveAbility(string abilityName, int level, CombatantSelector?)` | 给 buff/debuff | ✅（2026-10-11 实机验证：卡打出 → 能力按名装配并生效） |
| `UpgradeCard(ICardRef)` | 升级一张牌 | ⚠️ |
| `AddRelic(string relicId, CombatantSelector?)` | 给遗物 | ⚠️ |
| `SubscribeTrigger(string triggerName, Action<TriggerEvent> handler)` | 订阅触发点 | ⚠️ |

**关键设计决定（来自探针）**：

1. **`DamageMode` 必须显式传**——探针 §G 证实 `DirectDamage` 走命中判定与伤害调整器，`IndirectDamage` 跳过。**平台不给默认值**，因为选错会静默改变玩法强度。
2. **同步方法直接生效，平台内部处理入队**——游戏中 `Character` 层的伤害/格挡/治疗/状态/掷骰**本来就是同步的**；异步只在 `CardManager`/`ManaManager`/`Command.*`。作者不该被这个差异污染。
3. **`Draw(int)` 的语义歧义已被平台消除**——游戏 `CardManager.DrawCards(n)` 实为"入队"（返回 ≠ 牌到手），而 `DrawOneCard()` 是立即抽。平台统一为"请求抽 n 张，完成后再继续"，作者不需要知道有两套。
4. **SubCommand 构造即入队（副作用）** → 平台为每个 `EffectContext` 实例提供**单次执行守卫**，同一效果实例内的重复调用不会被重复入队。

### 4.3 不宜暴露的东西（明确排除）

| 排除项 | 原因 |
|---|---|
| `Character` / `CardBase` / `CardSo` / `EffectInfo` 等游戏类型 | 违反规则 1；且它们随游戏版本漂移 |
| `Cysharp.Threading.Tasks.UniTask` | 第三方运行时类型，Api 不得引用 |
| `ColorEArray` 多色组合 | 探针证实 `SequenceEqual` 要求**有序逐元素匹配**，多色键在场景里未必注册 → 平台只放单色 |
| `RNumberInfo.state` | 探针 §E-1 实测：**构造参数被完全丢弃，无任何代码读取** → 是死语义，暴露会把作者引向错误 |
| `IHaveEffect` 自定义实现 | 探针 §C-4：接口只有 2 个方法，但 `EffectBase` 内部还调 `GetOwnerLocalizedName`/`Corrupted`（不在接口上）→ 自造实现会在 `TriggeredApplyEffect` 抛 `MissingMethodException` |

### 4.4 遗物的两个挂钩点（探针新增）

遗物**只有一种受支持形态**（原"特殊遗物"挂钩点已于 2026-10-14 删除）：

| 形态 | 机制 | 平台暴露 |
|---|---|---|
| **触发型** | `RelicEffect.TriggerTargetInfo` + `RegisterTriggerTarget` | 数据化：`"trigger": "<触发点名>"` + `"effects": [...]` |
| ~~**特殊遗物**~~ | 覆写 `RelicBase.SpecialTriggeredEffect()`，类名必须是 `Relic<RelicName>`（游戏侧机制仍存在，但带点号的 mod id 拼不中） | **❌ 已删除（2026-10-14）—— 不可用**：平台的 `IRelicBehaviour` 兜底、遗物 JSON 的钩子字段与注册表入口已整体移除，作者侧**再无**表达途径 |

### 4.5 触发点表（**以游戏侧的权威枚举为准**，不靠猜名字）

游戏侧 `TriggerTargetInfo.AllTriggerType` 是**定长的字符串数组**（33 项），另有若干名字在代码里被直接 `new TriggerBase("…")` 发出而**不在**该枚举里（`HandCardCountChange`/`AcquireRelic`/`RelicActive`/`RelicCorrupted` 等）。

**平台规则**：内置触发点名表抄**游戏侧的权威枚举**（`ModKit.Core/TriggerTypeNames.cs`），保证 lint 阶段就能拦住拼错的名字；对"代码里发出但不在枚举里"的名字，加载期给出 Warn 并列出，方便作者排查"我写的触发点为什么没反应"。

### 4.6 已实证的坑（平台必须在加载期挡住）

| 坑 | 实测证据 | 平台对策 |
|---|---|---|
| 多色费用 → `KeyNotFoundException`，报错只显示 `Card.ColorEArray` | 探针 §B-5（`ManaCost.cs:24`） | 启动 `ManaIconDic.ContainsKey(new ColorEArray(c))` 探测 6 色；多色一律拒绝 |
| `target: AllAllies/OtherAllies` **静默不执行** | 探针 §F：`EffectBase.ResolveEffectAndGetCharacter:247` 的 switch 只处理 5 个值 | 只放行 5 个目标；另 2 个在加载期报错并说明原因 |
| 类名/命名空间写错 → **静默不生效、无异常** | 探针 §H-4（四条反射挂钩点） | 平台主动校验命名并注册；写错在加载期报错 |
| `AddEntry` 不设 `IsSmart` → `{Number}` 抛 `FormatException`（且**日志零 Error**） | 项目既有硬约束 | 平台强制 `IsSmart = true` |
| `RNumberBounds` 类型**根本不存在** | 探针 §E-1（全库零命中） | 不要写进任何契约（本文件已剔除） |

---

## 5. 与既有约定的继承关系（不另立口径）

> 本章原本按"见某文档"的方式引用早期研究产出。那些文件现已删除，故**把规律本身直接写出**——规律不依赖任何文档存在。

| 既有规律 | 内容 | ModKit 如何继承 |
|---|---|---|
| **总开关** | `Enabled=false` → 逐条还原原版行为 | Loader 暴露 `ModKit.Enabled`；关闭即不加载任何 mod 内容，游戏按原版跑 |
| **`FailOpen`** | 出错放行原版行为，绝不把失败升级成崩溃 | 平台内部同类：单个 mod 失败 → 该 mod 停用，**不崩游戏**；配套 `StrictMode` 供测试期暴露 bug |
| **`LogSink` 委托解耦** | 日志出口用委托/接口注入，不绑定具体插件类型 | `ModKit.Api` 的 `IModLog` 同构；平台不强迫作者引用 BepInEx |
| **接缝卡计数** | 每个接缝点维护 `Applied/Skipped/Failed/LastError` 四元组 | Loader 哨兵：`mods 加载数 / 内容数 / 校验失败数 / 效果执行失败数` 全部写日志 |
| **三层行为等价判据** | L-A / L-B / L-C 三个层级各自定义"改动前后行为等价" | P4 验收直接复用，不新增层级 |
| **资产闸门** | 每个阶段前后 `verify` 必须退出码 0 | 平台全程只读游戏目录；每个阶段前后跑 |

---

## 6. P0 交付物与后续阶段入口

### 6.1 P0 交付（全部已落盘并验证）

| 产物 | 路径 | 规模 | 验证 |
|---|---|---|---|
| 本契约文件 | `docs/ModKit-P0-内容契约与API规格.md` | （本文件） | — |
| Api 契约 · 核心 | `modkit/ModKit.Api/Contracts.cs` | 4,901 B | 编译通过 |
| Api 契约 · 效果 | `modkit/ModKit.Api/Effects.cs` | 7,971 B | 编译通过 |
| Api 契约 · 上下文 | `modkit/ModKit.Api/EffectContext.cs` | 9,447 B | 编译通过 |
| Api 契约 · 内容定义 | `modkit/ModKit.Api/ContentDefs.cs` | 15,453 B | 编译通过 |
| Api 契约 · mod 入口 | `modkit/ModKit.Api/Mod.cs` | 5,275 B | 编译通过 |
| 构建脚本（含规则 1 双道校验） | `modkit/build-api.ps1` | 1,725 B | 退出码 0 |
| 编译产物 | `modkit/bin/ModKit.Api.dll` | 18,432 B | 引用表 = `mscorlib` |
| JSON Schema · 卡牌 | `modkit/schemas/card@1.json` | 5,367 B | — |
| JSON Schema · 遗物 | `modkit/schemas/relic@1.json` | 1,738 B | — |
| JSON Schema · 本地化 | `modkit/schemas/i18n@1.json` | 1,128 B | — |

### 6.2 P0 验收记录（2026-10-11）

| 验收项 | 判据 | 实测 |
|---|---|---|
| Api 可编译 | `build-api.ps1` 退出码 0 | ✅ 0（131 引用 / 5 源文件） |
| **规则 1 编译期** | 只喂 net472，不喂游戏 Managed | ✅ 通过（首次因 `ValueTask` 不存在而失败 → 已改为 `Task`，见 §4.1） |
| **规则 1 产物期** | 产物引用表不含游戏/第三方程序集 | ✅ **只有 `mscorlib` 一项** |
| 资产零污染 | 资产闸门 `verify` 退出码 0 | ✅ 0（A 级 224 文件逐文件一致） |
| 契约与探针一致 | 多色拒绝 / 5 目标白名单 / 4 条命名契约 / `RNumberBounds` 不出现 | ✅ 逐条对照探针报告 |
| 写操作范围 | 仅 `diceomancer-mod\modkit\` 与 `docs\` | ✅ 未触碰游戏目录 |

### 6.3 P1 校验层（2026-10-11 · 已完工）

P1 被切成两半，**先做不依赖游戏的那一半**（因为它是平台最核心的价值，且能立刻用真实 JSON 做回归）：

| 半 | 内容 | 状态 |
|---|---|---|
| **P1a 校验层** | 把 §3.4 的 10 条加载期校验实现为可执行代码 + 回归测试 | ✅ **已完成，76/76 通过** |
| **P1b 游戏装配层** | `ModKit.Game`（探测）+ `ModKit.Runtime`（卡/遗物装配）+ `ModKit.Loader`（发现/加载/隔离）+ Transpiler 桥 | ✅ **已完成**（装配产物落在 `modkit/ModKit.Game/ContentAssembler.cs` 等文件） |

**P1 产物与实测**：

| 产物 | 规模 | 验证 |
|---|---|---|
| `modkit/ModKit.Core/Capabilities.cs` | 7,910 B | 编译通过 |
| `modkit/ModKit.Core/Names.cs`（名字映射权威表） | 17,259 B | 编译通过 |
| `modkit/ModKit.Core/ContentValidator.cs` | 45,616 B | 编译通过 |
| `modkit/ModKit.Tests/Program.cs`（76 条断言） | 23,174 B | **76/76 PASS，退出码 0** |
| `modkit/bin/ModKit.Core.dll` | 60,928 B | 引用表 = `ModKit.Api, mscorlib, Newtonsoft.Json, System, System.Core`（**无任何游戏类型**） |
| `modkit/bin/TestRunner.exe` | 47,616 B | 在**桌面 CLR** 上运行，不需 Unity / 游戏进程 / .NET SDK |
| `modkit/build-modkit.common.ps1`、`build-core.ps1`、`build-tests.ps1` | 11,290 B（合计） | 语法 AST 校验通过、退出码 0 |

**P1 的工程决定与踩坑（全部实测）**：

1. **netstandard 必须用游戏自带的那份**：Newtonsoft.Json 13.0.0 是 netstandard2.0 目标程序集，而 `tools/refasm` 是 net472 目标包、**不含 netstandard.dll 门面本体** → 编译报数十条 `CS0012 类型"Object"在未引用的程序集中定义`。修法与既有 `mod/build.ps1` 一致：引用游戏 `Managed\netstandard.dll`（2.1.0.0）。**该引用只在需要 Newtonsoft 时添加**，以保住 Api 的纯净引用表。
2. **`.ps1` 必须带 UTF-8 BOM** —— 本项目既有纪律，又验证一次：编辑工具改过的脚本会丢 BOM，PowerShell 5.1 随即按 GBK 读 UTF-8 中文，字符串边界被拆坏、报出与真实位置无关的 `Missing closing '}'`。已在构建流程里加"AST 语法校验"作为固定检查点。
3. **PowerShell 函数返回多元素时给出定长数组**：`$refs.Add(...)` 会报 `Collection was of a fixed size`。必须显式建 `List[string]` 后 `AddRange`。
4. **诊断必须带分隔符** —— 回归测试抓到 `effects[0].builtin内置语义…` 这种拼接（作者分不清字段路径到哪结束），已修为 `[级别] id · path — message`。

**校验层的断言覆盖**：正向 4 条（合法卡/本地化/触发型遗物/仅已实证语义零警告）；规则 27 条，逐条对应 §3.4 的 10 条校验，含"费用用了本 build 未注册的颜色""伤害效果缺 mode""目标用了游戏会静默失败的 `AllAllies`""遗物触发点用枚举里存在但实际不发出的名字""未实证语义只警告不报错"等；其余为能力、卡牌颜色组合、注册表与 Loader 失败隔离各组。**合计 76 条，全部通过。**

### 6.4 P1b-1 引擎探测层（2026-10-11 · 已完工）

**引擎探测层（`ModKit.Game`）已完成并编译验证**：

| 产物 | 规模 | 说明 |
|---|---|---|
| `modkit/ModKit.Game/GameCapabilitiesProbe.cs` | 18,637 B | 启动探测：费用色 / 原版卡名 / 原版遗物名 / 关键词 / 触发点 |
| `modkit/ModKit.Game/GameLog.cs` | 3,835 B | `IGameLog` + `BepInExGameLog` + `UnityFallbackLog` |
| `modkit/ModKit.Game/Reflect.cs` | 5,394 B | 集中式反射访问器 + `ModKitReflectionException` |
| `modkit/ModKit.Game/ApiSurface.md` | 28,388 B | **游戏侧 API 表面清单（反编译整理，位于本目录，不在 `docs/`）** |
| `modkit/bin/ModKit.Game.dll` | 69,632 B | 引用表含 `Assembly-CSharp, BepInEx, ModKit.Api, ModKit.Core, mscorlib, UnityEngine.CoreModule` |
| `modkit/build-game.ps1` | 2,244 B | 构建 + 断言"确实连上了游戏" |

**探测的权威 API（全部实测，且优先用 public 而非反射）**：

| 能力 | 权威 API | 用反射？ |
|---|---|---|
| 原版卡名清单 | `CardDataManager.Instance.GetAllCardsCardDateCollectionsInGame()` → `CardSo[]` | **否** |
| 原版遗物名清单 | `DataManager.Instance.GetRelicDataManager().GetAllInGameRelicSo()` | **否** |
| 关键词清单 | `CardKeyWordUtil.AllKeywordStrings`（public static，36 项） | **否** |
| 可用费用色 | `ManaManager.Instance.ManaIconDic`（public readonly 字段） | 否 |
| 装配卡牌 | `CardSo.cardInfo`（private SerializeField） | **是** |
| 触发点表 | 抄游戏侧权威枚举 `TriggerTargetInfo.AllTriggerType`（33 项） | 不适用 |

> **比预期好的一点**：卡名与遗物名都有干净的 public 枚举口，不需要反射私有字典。
> 只有**装配**（把 `CardInfo` 写回 `CardSo`）才必须反射。

**探测层的工程决定**：

1. **探测层故意大量使用 try/catch** —— 这是"接缝边界"（平台自己的代码），
   接缝边界要求 try/catch + FailOpen 降级为原版行为。
   探测失败 → 该项能力为空 → 校验器把作者的内容判为不可用，
   而不是让游戏在运行几十分钟后以"玩法异常"暴露。
2. **失败可诊断**：探测失败项写进 `ProbeSummary.Failures` 并入日志（哨兵），
   使"平台到底探测到了什么"成为可核对的日志事实。
3. **颜色映射必须显式**：`ManaColor → ColorE` 用 switch 而非强制转换。
   编译期就抓到了"把平台枚举传给游戏函数"这个错误（`CS1503`）——
   强转会让它静默通过，而游戏一旦调整 `ColorE` 成员顺序就会静默给错颜色。
4. **`Logger` 必须全限定**：`BepInEx.Logging.Logger` 与 `UnityEngine.Logger` 同名（`CS0104`）。

### 6.5 P1b 后续阶段的完工状态（2026-10-11）

P1b 的后续工作**已全部完成并实机验收**。完成情况与产物落点如下：

| 工作 | 内容 | 产物 | 状态 |
|---|---|---|---|
| **P1b-2 内容装配** | 由 `CardDef`/`RelicDef` 装配游戏对象：克隆 `CardSo`/`RelicSo` → 反射写 `cardInfo`/`relicInfo` → 注册进池 | `modkit/ModKit.Game/ContentAssembler.cs`、`ContentAssembler.Abilities.cs`、`Reflect.cs` | ✅ 已完工，实机验证（卡/遗物/能力） |
| **P1b-3 本地化注入** | `DetailedLocalizationTable.AddEntry` + **强制 `IsSmart=true`** | `modkit/ModKit.Game/LocalizationPump.cs` | ✅ 已完工，实机验证（文案） |
| **P1b-4 Loader** | mod 目录发现 → `mod.json` 解析 → 调 `ContentValidator` → 失败隔离 → 哨兵日志 | `modkit/ModKit.Core/ModLoader.cs`、`ModLoader.Abilities.cs`、`ContentRegistry.cs` | ✅ 已完工，回归覆盖失败隔离 |
| **P1b-5 Transpiler 桥** | 把 4 个内容工厂的 `Type.GetType` 换成平台解析器 | `modkit/ModKit.Game/TranspilerBridge.cs` | ✅ 已完工（补丁数仍为 4） |

**装配所需的真实签名（实测，供维护者查）**：

```
CardSo        : ScriptableObject   public cardName / cardArt / alterArtSpriteInfo / cardAnimationInfo
                                   / extraTooltip / specialUpgradedCardName
                                   private CardInfo cardInfo            ← 必须反射
RelicSo       : ScriptableObject   public Sprite relicIcon; public RelicInfo relicInfo
RelicInfo     : class              relicName / isSpecialRelic / useSpecialDesKey / relicColors
                                   / relicRarity / relicMainType / requiredChargeCount
                                   / canBeActiveNotInBattle / mainEffectInfo / acquired / extraRelicEffectInfos
CardKeyWordInfo: struct            new CardKeyWordInfo("<keywordName>") 是"临时牌"的标准写法
RNumberInfo    : struct            Card 命名空间；注意其 state 构造参数被游戏丢弃（死语义，平台不暴露）
```

### 6.6 后续阶段入口条件（P1 开工时确认，现已全部满足）

1. §3.4 的 10 条加载期校验**全部有实现路径**；✅
2. §4.2 中标记 ✅ 的方法先做通，⚠️ 的按探针报告逐个实现并**在实机验证后升级档位**；✅（实机已验证的档位以 `Effects.cs` 的 `BuiltinEffectCatalog` 为准）
3. 启动探测（§4.6 的 `ManaIconDic` 探测、原版卡名清单、触发点表）作为 Loader 的第一步。✅

### 6.7 本文档自身的诚实边界

- §4.2 中标记 ✅ 的项**以实机验证为准**：`Block` / `Draw` / `Log` / `Amount`，加上 2026-10-11 新增的 `GiveAbility`（能力装配链路）；其余 `⚠️` 均为**代码可见、平台已实现但未实机验证**，**不得在作者手册里写成"可用"**；
- **10 条加载期校验**中，第 3、5 条（费用色、原版卡名）的实现依赖**启动探测结果**，而非硬编码；
- **关键词、能力**的 schema 已冻结并落地（其中能力已实机验证；关键词的装配已实现、待实机验证）；**剧情后果**的 schema 仍是草案——探针判定其"Special"分支需要新补丁且失败静默，平台尚未实现（认知纪律 C9）。

---

## 附：内容字段的硬性约束 —— 禁止对象引用（2026-10-13 事故后新增）

**规则**：写进游戏内容对象（`EffectInfo` / `CardInfo` / `RelicInfo`）的字段，**只能是字符串、枚举、数字、结构体、或它们的数组** —— **不得放对象引用**（`CardSo`、`MonoBehaviour`、任何 UnityEngine.Object）。

**为什么**（2026-10-13 实机事故，代价是一份战役存档）：

1. `CardInfo.CreateDeepCopy` **只浅拷数组** ⇒ **牌组的 `CardInfo` 与卡资产共享同一个 `EffectInfo` 实例**；
2. 本局结束时 `CommandGameOver → CampaignManager.CampaignDefeated → SaveLoadManager.SaveCampaign` 用 `DataContractSerializer` 序列化 `CampaignInformation`（含 `playerDeck: CardInfo[]`）；
3. 序列化器顺着共享的 `EffectInfo` 走到 `CardSo[]` 时，会尝试为 `Card.CardSo` 建 `DataContract`，而 `CardSo : UnityEngine.ScriptableObject` 没有 `[DataContract]`/`[Serializable]` ⇒ 抛 `InvalidDataContractException: Type 'Card.CardSo' cannot inherit from a type that is not marked ...`；
4. `SaveCampaign` 用 `FileMode.Create`（**先截断再写**）⇒ **存档被截断在崩点**；主菜单反序列化失败还会**删掉** `CampaignSave.xml` + `WorldSave.xml`。

**实例**：`add_random_card` 的 `pool.specific` 把 `CardSo[]` 放进 `EffectInfo.genRandomCardInfoPack.specificPool` ⇒ 崩（截断点正是 `...<c:cardPool>SpecificPool</c:cardPool>...`）。
原版 `ABitofEverything` 安全的原因：它走 `specialPoolName = "Junk"`、`specificPool` 是**空数组**（没有元素 ⇒ 序列化器根本不会去建 `CardSo` 的契约）。

⇒ 该路径**已停用**（校验器报 Error）。恢复"指定卡表"能力的**唯一正路**是「**运行时命名池**」：把卡表注册进 `CardDataManager` 成一个**具名 `CardDateCollection`**，`EffectInfo` 里只放**池名字符串**（`cardPool = SpecialPool` + `specialPoolName = <名字>`）。

**新增内容字段时的自检（两问）**：

1. 这个字段会不会被 `DataContractSerializer` 走到？（`CardInfo` → `cardEffectInfos[].effectInfo` 这条链是必经之路）
2. 走到的类型里有 Unity 对象吗？有 ⇒ 换成字符串 / 枚举 / 结构体，或改成"**运行时注册 + 名字引用**"。
