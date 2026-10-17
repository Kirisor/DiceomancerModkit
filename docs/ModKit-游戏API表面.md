# ModKit · 游戏 API 表面与必需补丁

> **性质**：平台与游戏之间的**契约登记**。任何"平台调用了游戏什么"都必须在这里有记录。
> **编写日期**：2026-10-11｜**证据**：独立只读侦察（逐行核实 `research/decompiled/Assembly-CSharp/`）+ 编译验证
> **纪律**：本文件里每条都标了证据出处；未核实的写"未核实"，不写推测。

---

## 1. 必需补丁（没有它平台必然失效）

### 1.1 Transpiler 桥 —— **装配任何内容之前的硬前置**

**问题**：游戏用反射按类名解析内容类：

```csharp
// Utilities/EffectUtils.cs:23-40（原文）
if (effectInfo.effectName == "SpecialEffect")
{
    empty = "EffectSpEffect" + effectInfo.specialEffectString;
    empty2 = "Effects.Special.CardEffect.";
}
...
EffectBase effectBase = Activator.CreateInstance(Type.GetType(empty2 + empty)) as EffectBase;
effectBase.EffectOwner = effectOwner;   // ← 第 41 行：上一行为 null 时这里 NRE
```

**而 `Type.GetType(string)` 只搜索「调用方程序集 + 核心库」。** 调用方是 `Assembly-CSharp`，
所以 `EffectSpEffect<名>` **必须在 Assembly-CSharp 内**才找得到。

**后果**：mod 的效果类住在自己的程序集里 → 解析返回 null → 第 41 行 `NullReferenceException`。
**这是"装配出的卡必炸"的原因，不是可以绕过的细节。**

**平台的处置**：把该调用替换成平台解析器（跨 AppDomain 搜索），即一条 `HarmonyTranspiler`。
现役实现与踩坑见 [`mod/DiceomancerMod/Patches/TypeResolutionPatches.cs`](../../mod/DiceomancerMod/Patches/TypeResolutionPatches.cs)：

```
覆盖 4 个内容工厂（同一病根）：
  · Utilities.EffectUtils.GenerateEffectFromEffectInfo      → 卡牌/能力效果类
  · Keyword.CardKeyWordUtil.GenerateKeywordFromKeywordInfo  → 关键词类
  · Relic.RelicManager.GenerateRelicEffectFromRelicInfo     → 遗物效果类（私有方法）
  · Combat.Character.GenSpecialState                        → 特殊状态类（私有方法）
实测替换 7 处（11 处 Type.GetType 中的 7 处）。
```

| 项 | 值 |
|---|---|
| 状态 | 现役 mod **已实证跑通**（4 个 Transpiler 共替换 7 处） |
| 平台侧 | ⏳ **尚未接入**（P1b-5 待做） |
| 硬性要求 | **必须在装配任何卡之前完成**。未装桥就装配 = 静默造出必崩的卡 |
| 踩坑 | Transpiler 必须**就地改 `CodeInstruction`**（改 opcode/operand）；新建对象会丢 `labels`/`blocks` → Mono **无异常中止进程** |

> **架构含义**：这使"Transpiler 桥"从"优化项"变成**平台启动序列的第 0 步**。
> 平台引导流程必须：装桥 → 探测 → 加载 mod → 装配内容。顺序不可换。

---

## 2. 读取类 API（探测用，全部有 public 入口）

| 能力 | 权威 API | 证据 |
|---|---|---|
| 原版卡名清单 | `CardDataManager.Instance.GetAllCardsCardDateCollectionsInGame()` → `CardSo[]` | public，已验证可用 |
| 原版遗物名清单 | `DataManager.Instance.GetRelicDataManager().GetAllInGameRelicSo()` | public |
| 关键词清单 | `CardKeyWordUtil.AllKeywordStrings`（public static，36 项） | 已验证 |
| 可用费用色 | `ManaManager.Instance.ManaIconDic`（public readonly `Dictionary<ColorEArray, Sprite>`） | 已验证；比较器是有序 `SequenceEqual` |

**无需反射** —— 这是探测层比预期简单的原因。

---

## 3. 装配类 API（写回游戏，部分必须反射）

### 3.1 卡牌

| 项 | 声明 | 可访问性 |
|---|---|---|
| `CardSo.cardInfo` | `private CardInfo cardInfo` | **private → 必须反射**（`CardInfo` 是 **struct**，反射 SetValue 走装箱） |
| `CardSo.cardName` / `cardArt` / `cardAnimationInfo` / `extraTooltip` / `specialUpgradedCardName` | public 字段 | 直接写 |
| `CardDataManager._allInGameCardSoDictionary` | `private readonly Dictionary<string, CardSo>` | **private → 反射**（`readonly` 只禁重新赋值，不阻止改字典内容） |
| `CardDataManager._allInGameCardSoPathDictionary` | 同上 | ⚠️ **死容器**：唯一写入者 `GenerateCardPathsDictionary()` 全项目**无调用点**。现役代码里能看到这个名字，**不要被误导** |
| `CampaignManager.campaignCardPools` | **public** `List<CardSo>` | 直接写；⚠️ `SetCampaignCardPool()` 会**整体重建**它 → 追加必须在那之后 |
| `CardDateCollection` | **public class**，字段全 public（`commonCards`/`unCommonCards`/`rareCards` 等），`GetAllCards()` | 可直接构造/修改 |

### 3.2 遗物

| 项 | 声明 | 可访问性 |
|---|---|---|
| `RelicSo.relicInfo` | **public** `RelicInfo`（class，字段 public） | **无需反射** |
| `RelicDataManager._allInGameRelicSo` | `private readonly Dictionary<string, RelicSo>` | 反射 |
| `RelicDataManager._allRelicDataCollections` | `private readonly List<RelicDataCollection>` | 反射 |
| `RelicDataCollection` | **public class**，字段全 public，**无自定义构造** → 可 `new` | 直接构造 |

**⚠️ 时序陷阱（必须处理）**：`DataManager.LoadAllGameData()` 每次都会
**new 一个新的 `RelicDataManager`** 并 `Initialize()`；
`RelicDataManager.LoadAllRelic()` 开头 `Clear()` 掉 `_allStandPoolRelicSo` 与 `_allRelicDataCollections`
（**唯独不清 `_allInGameRelicSo`**，那里用 `TryAdd`）。
→ 对前两者的注入**一旦再过一次 `LoadAllGameData` 就被清空**；`_allInGameRelicSo` 反而能存活。
**平台必须在 `LoadAllGameData` 之后（其 Postfix）注入。**

**遗物进战役池的 4 个必要条件**（依据 `SetCampaignRelicPool` 原文）：
① 所属 `RelicDataCollection` 在 `_allRelicDataCollections` 里；
② `requireUnlock == false`，或对应升级已解锁；
③ 类型 `Standard`，或 `Colored` 且 `colorEs ⊆ usedColorE`；
④ **`Utils` 类型永远进不了战役池**（switch 无该分支）。

---

## 4. 结构定义（写装配代码必须照抄的精确形状）

### 4.1 `CardInfo`（struct，8 个 public 字段）

```csharp
[Serializable]
public struct CardInfo
{
    public string cardName;                                  // 必填
    [HideInInspector] public string cardArtAlterName;
    [FormerlySerializedAs("cardRarity")] public Rarity rarity;
    public ColorE[] cardColorEs;                             // 必填（null → ArgumentNullException）
    [FormerlySerializedAs("cardManaCostColorEList")]
    public ColorEArray[] cardManaCostColorEArrays;           // 每项必须是 ManaIconDic 注册过的单色
    public CardKeyWordInfo[] cardKeyWordInfos;
    public CardEffectInfo[] cardEffectInfos;
    public CardUpgradeInfo cardUpgradeInfo;                  // struct 默认值安全
}
```

### 4.2 `EffectInfo`（class，**27 个**实例字段）

> 纠正一处流传的说法：**"28" 是静态数组 `EffectNameArray` 的长度，不是字段数**。

必填 6 项（依据 `GenerateEffectFromEffectInfo` 的实际消费）：
`effectName` / `specialEffectString`（`effectName=="SpecialEffect"` 时）/ `effectTargetEnum` /
`isPositive` / `cardEffectResolveType` / `effectNumberInfos`（**Count 决定 `rNumbers` 长度**）。

**其余留默认值安全**，包括那 8 个 `specialEffectHasXxx`：

> **实测结论**：8 个 `specialEffectHasXxx` 布尔在**运行时是死字段** ——
> 全量反编译源码（Assembly-CSharp + Unity.Localization）中除声明行与 `CreateDeepCopy` 的复制行外
> **零读取点**；`GenerateEffectFromEffectInfo` 完全不读它们（它按 `IsNullOrEmpty` 判断）。
> 它们是 Editor 期自定义 Inspector 的开关（Editor 程序集不在反编译范围）。
> **现役 mod 里显式写 `specialEffectHasExtraStringInfo = false` 其实是不必要的。**

### 4.3 其它 struct

```csharp
[Serializable] public struct CardEffectInfo { public CardEffectResolveType cardEffectResolveType; public EffectInfo effectInfo; }
// ↑ cardEffectResolveType 会**覆盖** EffectBase.CardEffectResolveType，是最终生效值

[Serializable] public struct RNumberInfo(int number, bool isDice = false, string state = "Standard", DiceInfo[] dices = null)
{ public bool isDiceNumber; public int oriNumber; public DiceInfo[] diceInfos; }
// ↑ state 参数**被静默丢弃**（无字段承载）；RNumberStates 数组零读取

[Serializable] public struct DiceInfo(DiceType diceType) { public DiceType diceType; }
// DiceType: D4, D6, D8, D12, D20   ← 是**骰子类型**，不是 min/max！

[Serializable] public struct CardKeyWordInfo(string keywordName, int number = 0) { ... }
// ↑ isTemporarily 是死字段；"临时牌"靠关键词**字符串** "Temporary" 判定
```

**骰子的硬约束**：`isDice=true` 时 `diceInfos` **必填且长度 ≥1**，
否则 `RNumber.SetRNumberByRNumberInfo` 直接 `foreach` null → NRE。

### 4.4 死代码/死字段清单（避免照抄现役代码时被误导）

| 名称 | 判定 |
|---|---|
| `CardDataManager._allInGameCardSoPathDictionary` / `GenerateCardPathsDictionary()` | **死容器/死方法**，无调用点 |
| `RNumberInfo.state` 参数 / `RNumberStates` 数组 | 死语义，零读取 |
| `CardKeyWordInfo.isTemporarily` | 死字段 |
| 8 个 `EffectInfo.specialEffectHasXxx` | 运行时零读取（Editor-only） |

---

## 5. 本地化

| 项 | 精确签名 | 证据 |
|---|---|---|
| 取表 | `LocalizationSettings.StringDatabase.GetTable(TableReference, Locale = null)` | `LocalizedDatabase.cs:246` |
| 加条目 | `DetailedLocalizationTable.AddEntry(string key, string localized)` → `StringTableEntry` | `DetailedLocalizationTable.cs:74` |
| **返回值可能为 null** | `FindKeyId` 失败时 `return null` | 同上 |
| `IsSmart` | **`bool`，在 `StringTableEntry` 上**（不是 `TableEntry`） | `StringTableEntry.cs:45` |

**不设 `IsSmart` 的后果**：`{Number}` 这类命名占位符会走 `String.Format` 语义 → `FormatException`
→ 游戏内弹「哦不！有错误！」，而**日志零 Error**。平台把它写死为不可绕过的步骤。

---

## 6. 编译期验证状况（本文件的可靠性）

| 层 | 验证方式 | 结果 |
|---|---|---|
| 读取类 API | `ModKit.Game` 编译通过（引用真实 `Assembly-CSharp.dll`） | ✅ 签名都存在 |
| 装配类 API | `ContentAssembler` 编译通过（含反射字段名的字符串常量） | ✅ 类型/字段名正确 |
| 反射字段名 | ⚠️ **只在编译期无法验证字符串** —— 靠 `Reflect` 的失败诊断 + 运行时自检覆盖 | ⚠️ 待实机 |
| 运行时行为 | ❌ 未实机验证（需要启动游戏 + 装 Transpiler 桥） | 未验证 |

> **诚实边界**：本文件的所有**声明**都已核实，但**行为**尚未实机验证。
> 按第 9 步认知纪律 C9：未做的不得当成已验证 —— 平台在实机跑通前，
> 对作者的手册必须标注"未经实机验证"。

---

## 附录 A · P4 实机验收结果（2026-10-11）

首次在真实游戏进程里验证平台。**结论：桥与装配链路已实证可用；探测时机有一个已定位、待修的设计缺陷。**

### A.1 已实证通过

| 项 | 实测证据 |
|---|---|
| 插件加载 | `Loading [DICEOMANCER ModKit (Mod 平台) 0.1.0]` |
| 内容注册表 | 扫描 **159 程序集 / 25,919 类型**，0 失败 |
| Transpiler 桥补丁 | **4 / 期望 4**，含两个私有方法 `GenerateRelicEffectFromRelicInfo`、`GenSpecialState` |
| `Type.GetType` 替换 | **1 / 2 / 2 / 2 处**（四个工厂），与上游 mod 在同一 build 上的结果一致 |
| 流水线五步 | 探测 → 加载 → 校验 → 装配 → 文案，全部执行完毕 |
| 探测（结构类） | 原版卡名 **534** 张、关键词 **36** 个、触发点 **32** 个 |
| 文案注入 | **写入 6 条**（zh-Hans），无异常 |
| 失败隔离 | 两张卡被拒时，mod 未整体失败、文案照常注入 |
| 资产闸门 | 全程退出码 0（A 级 224 文件逐文件哈希一致） |

### A.2 ~~暴露的缺陷（待修）~~ → ✅ 已修复并复验 · 探测时机

**现象**：
```
费用色：取 ManaManager 失败 —— NullReferenceException
原版遗物名：GetRelicDataManager() 为 null
→ 可用费用色为空 → 卡片 cost:"Void" 被校验器合理拒绝
```

**根因（已在反编译源码中确认）**：
```csharp
// ManaManager.cs:88
public static ManaManager Instance => BattleMultipleWorldsManager.Instance.GetCurrentBattleField().manaManager;
```
`ManaManager.Instance` **只在战斗场地存在时才可用**。而在 `CardDataManager.InitializeData`
挂点触发时，战斗场地尚未创建 → 探测必然拿到空颜色集 → 校验器据此拒绝作者的内容。

**这不是校验器的问题**：校验器按"能力表为空 → 该颜色不可用"拒绝，逻辑正确且不静默放行。
错的是**在错误时机提问**。

**修法（✅ 已实施并实机验证，2026-10-11 16:39）**

原先计划的"两阶段拆分"没有采用 —— 更简单也更可靠的做法是**就地等就绪**：

1. **不依赖任何钩子时机**：插件订阅 `SceneManager.sceneLoaded`，等场景就绪后创建
   一个常驻 `MonoBehaviour`，每 0.5 秒检查 `BattleMultipleWorldsManager.Instance
   .GetCurrentBattleField()` + `ManaManager.Instance` + `GetRelicDataManager()`
   是否都可用，**全部就绪才跑流水线**。
   - 为什么不能在插件 `Awake` 里创建：那时还没有活动场景，Unity 会拒绝
     `DontDestroyOnLoad`（`Player-prev.log` 实测报
     「only works for root GameObjects or components on root GameObjects」），
     对象随第一个场景加载被销毁 → `Start()`/`Update()` 永不执行。
     上游 `ModRunner.cs` 的注释已记录此坑，此处按同一做法处理。
2. **幂等硬闸**：`sceneLoaded` 每场景都会触发，实测导致流水线跑两遍
   （第二次探测到 536 张卡 vs 第一次 534 张）。`PlatformRuntime.RunOnce`
   加 `_ran` 标志，`ModKitBootstrap.EnsureCreated` 见 `HasRun` 即跳过。

**修后实测（p4-run9，从存档基线 `20261011-100224` 启动）**：

| 项 | 修前 | 修后 |
|---|---|---|
| 可用费用色 | **空集**（卡被拒） | **6 / 6**（Red, Green, Blue, Purple, Void, Black） |
| 原版遗物名 | **探测失败** | **205 件** |
| 就绪时机 | — | `运行时事实已就绪（20 次检查 / realtime 13.6s）` |
| mod 加载 | 卡 0（2 张被拒） | **卡 2 / 文案 2，诊断 0 条（错误 0）** |
| 流水线次数 | — | **1 次**（幂等生效） |

> 一个能自证的旁证：两次探测到的原版卡数不同（**534 vs 536**）——
> 说明**能力表本身随战斗进程增长**。这正面印证了"在错误时机提问会得到
> 不完整答案，进而误判作者内容"这一判断。

### A.3 其他实机发现（都与平台无关，但会影响验收操作）

| 发现 | 说明 |
|---|---|
| **BepInEx 按 `[BepInPlugin]` 特性加载插件，不看文件名** | 把 `DiceomancerMod.dll` 改名成 `_disabled_*.dll` 无效，必须**移出** `plugins\` 目录 |
| **`EnableAssemblyCache = true` 时缓存会保留已移走的插件元数据** | 需同时把 `BepInEx\config\BepInEx.cfg` 的该项设为 `false` 并清 `BepInEx\cache\` |
| **两个桥会抢同一批调用点** | 上游 mod 的 transpiler 先跑并替换掉全部 `Type.GetType`，导致平台的桥测到"0 处"并静默失效。**共存需要显式仲裁**（见下） |

### A.4 待决策：与既有 mod 的桥共存

上游 mod（`DiceomancerMod.dll`）装了一个**功能等价**的桥（`ModTypeResolver.Resolve`）。
两个桥 patch 同一批方法时，**先装的那个赢**，后装者测到 0 处替换。

三种处理方式，需选定其一：
1. **平台优先**：检测到已有桥时，由平台接管并替换掉对方的解析调用（需对方配合或强制改写）；
2. **检测并告知**：检测到已有桥时**明确警告**"本平台与 X 的桥冲突，请二选一"，不静默失效；
3. **链式回退**：平台的解析器在自身落空时，回退调用先前安装的解析器。

当前实现是 **FailOpen（不生效但不崩）**，但**静默** —— 这在 C9 口径下不可接受，
至少要做到方式 2（明确警告）。**在修好之前，验收必须只装平台一个插件。**
---

## 附录 B · 卡池缺陷与读回断言（2026-10-11 第二轮实机）

### B.1 查明的缺陷：卡"存在却抽不到"

**症状**：卡成功注册进 `_allInGameCardSoDictionary`（`GetCardSoByName` 取得到），
但**永远不出现在牌库里**，且**没有任何报错**。

**根因**（`CampaignManager.SetCampaignCardPool` 原文）：

```csharp
private void SetCampaignCardPool()
{
    campaignCardPools = new List<CardSo>();                            // ← 整个重建
    foreach (col in CardDataManager.Instance.GetStandardCardDataCollectionByColorE(campaignUsedColorE))
        campaignCardPools.AddRange(col.GetAllCards());                 // ← 只从"卡数据集合"灌
    foreach (col in CardDataManager.Instance.GetUnLockCardDataCollectionByColorE(campaignUsedColorE))
        if (CareerManager.Instance.GetUpgradeInfo(col.cardCollectionName).upgraded)
            campaignCardPools.AddRange(col.GetAllCards());
}
```

它**只从"卡数据集合"重建池子**。而平台原先的做法是直接
`campaignCardPools.Add(so)` —— 于是 `SetCampaignUsedColorE`（换色/开新局都会走到
`SetCampaignPoolByUsedColorE`）一执行，**mod 卡就被冲掉了**。

> 这个缺陷是"平台只验证了自己写成功、没验证游戏真能用"的直接后果。
> 它属于最坏的一类：**静默 + 只在特定操作后发生**。

### B.2 正确做法：让卡成为池子的**来源**

```
① 建一个 CardDateCollection，装本 mod 的卡
② 追加进 CardDataManager._colorCardDateCollections（私有 → 反射）
③ 反射调用 CampaignManager.SetCampaignCardPool() 重建池子
```

**为什么 collection 的 `colorEs` 给空数组**：过滤条件是
`collection.colorEs.All(campaignUsedColorE.Contains)` —— 空集是任意集合的子集，
所以空数组恒为真。这样就不必去读 `campaignUsedColorE`（那是个私有字段）。

**幂等**：按 `cardCollectionName = "ModKit_" + modid` 查重，重跑不会堆积重复集合。

### B.3 新增的读回断言

装配完成后平台会**按游戏自己的入口读回来验证**：

```
[ModKit] 读回断言通过：sample_starter：按名可取 2/2，在战役池 2/2
```

| 断言 | 走什么入口 | 拦住的失败形态 |
|---|---|---|
| 按名可取 | `CardDataManager.GetCardSoByName` | 写进了错的容器 |
| 在战役池 | `CampaignManager.campaignCardPools.Contains` | 池子被重建冲掉（B.1 那个缺陷） |

两条都是**静默**失败的形态，所以必须显式断言 —— 否则平台会一直报"装配成功"，
而作者只会看到"我的卡从来没出现过"。

### B.4 修后实测（p4-run10，从存档基线启动）

| 项 | 结果 |
|---|---|
| 桥 | 4/4 命中 |
| 就绪时机 | 20 次检查 / realtime 13.7s |
| 能力 | 费用色 6/6、卡 534、遗物 205、关键词 36、触发点 32 |
| 加载 | 卡 2 / 文案 2，诊断 0 条（错误 0） |
| 卡池 | 已注册集合 `ModKit_sample_starter`（2 张）→ 已重建战役卡池 |
| **读回** | **按名可取 2/2，在战役池 2/2** |
| 文案 | zh-Hans 写入 6 条 |

### B.5 仍未验证的边界（如实记录）

读回断言证明的是"**游戏的数据层真的持有并能取到这张卡**"。
它**不等于**"卡面在游戏里渲染正确、效果结算正确" ——
那需要进战斗后实际把卡抽到手上观察，属于 P4 之后的进一步验收。
---

## 附录 C · 遗物池的**同构缺陷**（2026-10-11 第三轮实机）

附录 B 修完卡池后，按"同一缺陷模式是否在遗物上原样存在"去核对 —— **确认存在**。

### C.1 遗物侧的三重结构（`RelicDataManager`）

```csharp
private void SetUnlockedRelicDataCollections()
{
    _unlockRelicDataCollections.Clear();
    foreach (col in _allRelicDataCollections)                       // ← 从"全部"重建
        if (!col.requireUnlock || CareerManager…GetUpgradeInfo(col.unlockUpgradeName).upgraded)
            _unlockRelicDataCollections.Add(col);
}

public void SetCampaignRelicPool(ColorE[] usedColorE)
{
    SetUnlockedRelicDataCollections();
    _campaignRelicPool.Clear();                                     // ← 整体清空
    foreach (col in _unlockRelicDataCollections)                    // ← 只从 *_unlock* 灌
        switch (col.relicDataCollectionType)
        {
        case Standard: _campaignRelicPool.AddRange(col.relicSos); break;
        case Colored:  if (col.colorEs.All(usedColorE.Contains)) _campaignRelicPool.AddRange(col.relicSos); break;
        }                                                           // ← Utils 永不进池
}
```

所以"把遗物塞进 `_allRelicDataCollections`"**不够** —— 必须再触发一次池子重建。

### C.2 修法（与卡池同一套）

```
① 建 RelicDataCollection（requireUnlock=false、类型 Standard、colorEs 空数组）
② 追加进 _allRelicDataCollections
③ 反射调用 SetCampaignRelicPool(campaignUsedColorE) 重建
```

平台**原本的参数选择就是对的**（`requireUnlock=false` 过解锁过滤、`Standard` 过 switch），
**唯一缺的就是第 ③ 步**。原先那条"遗物可查但暂未进池"的警告路径因此成了死代码。

### C.3 修后实测（p4-run11，从存档基线启动）

```
[ModKit] 已装配遗物：sample_starter.stone_bracelet（模板 Abacus）
[ModKit] 已注册遗物集合：ModKit_sample_starter（1 件）
[ModKit] 已重建战役遗物池（SetCampaignRelicPool）。
[ModKit] 遗物读回断言通过：sample_starter(遗物)：按名可取 1/1，在战役池 1/1
```

| 项 | 结果 |
|---|---|
| 桥 | 4/4 |
| 能力 | 费用色 6/6、卡 534、遗物 205、关键词 36、触发点 32 |
| 加载 | 卡 2 / **遗物 1** / 文案 2，诊断 0 条 |
| 卡读回 | 按名可取 2/2，在战役池 2/2 |
| **遗物读回** | **按名可取 1/1，在战役池 1/1** |

### C.4 一条方法论结论（值得记住）

> **同一个数据层里，"直接写容器" 与 "让容器成为派生来源" 是两种不同的正确性等级。**
> 平台第一版在卡与遗物上都选了前者，结果两处都栽在同一个模式上。
> 凡是游戏里有"重建/派生"逻辑的容器（`SetCampaignCardPool` / `SetCampaignRelicPool`），
> **都必须写进它的上游来源，而不是它本身**。
---

## 附录 D · 能力装配（2026-10-11 第四轮实机）

### D.1 装配路径（与卡片同构）

```
① 克隆原版 AbilitySo 作模板（取 abilitySprite / abilityAnimationInfo / resolveAudioClip）
② 写 AbilityInfo：abilityName / abilityType / isUnique / effectInfo
③ 注册进 _allAbilitySos —— 唯一查询入口 GetAbilitySo
④ isInRandomPool 为真时按 type × owner 写对分类桶
⑤ 读回断言
```

**效果侧不需要新补丁**：能力的效果走 `EffectUtils.GenerateEffectFromEffectInfo`，
那个工厂已被 Transpiler 桥接管。平台复用了卡片的效果构建器
（把 `BuildEffectInfo` 的 `ApiCardDef` 参数改成 `string ownerId` —— 它只用于一处错误消息）。

### D.2 分桶规则（`AbilityDataManager.LoadAllAbility` 原文）

```csharp
if (!abilitySo.isInRandomPool) continue;
switch (abilitySo.abilityInfo.abilityType)
{
case Buff:   owner==Player||All → _allPlayerBuff;  owner==Enemy||All → _allEnemyBuff;  break;
case DeBuff: owner==Player||All → _allPlayerDeBuff; owner==Enemy||All → _allEnemyDeBuff; break;
}
```

**`Util` / `Other` / `PrimAbility` 不在这个 switch 里** → 只进全局字典、不进任何池。
这是游戏本来的设计，平台如实照做并打日志说明。

### D.3 ★ 能力侧的失败是**响亮的**（与关键词相反）

`AbilityUtil.GenerateAbility` 原文：

```csharp
AbilitySo abilitySo = DataManager.Instance.GetAbilityDataManager().GetAbilitySo(abilityInfo.abilityName);
if (abilitySo == null) { Debug.Log("Ability So is null" + abilityInfo.abilityName); }   // ← 只打日志
RNumberInfo[] array = abilitySo.abilityInfo.effectInfo.effectNumberInfos.ToArray();     // ← 紧接着解引用 → NRE
```

那个 `null` 检查是**假的** —— 取不到就直接崩。
所以"按名可取"对能力不是"最好验证一下"，而是**硬要求**。平台的读回断言覆盖它。

### D.4 修后实测（p4-run12，从存档基线启动）

```
[ModKit] 已装配能力：sample_starter.shield_up（模板 Nani，type=Buff，owner=Player）
[ModKit] 能力读回断言通过：sample_starter(能力)：按名可取 1/1
```

| 项 | 结果 |
|---|---|
| 桥 | 4/4 |
| 能力 | 费用色 6/6、卡 534、遗物 205、关键词 36、触发点 32 |
| 加载 | 卡 2 / 遗物 1 / 文案 2，诊断 1 条（**错误 0**；那 1 条是平台自己如实标注"能力装配未实机验证"的 Info） |
| 卡读回 | 2/2 在池 2/2 |
| 遗物读回 | 1/1 在池 1/1 |
| **能力读回** | **按名可取 1/1** |
| 文案 | zh-Hans **9 条** |

### D.5 仍未验证的边界（如实记录）

- 能力**没有"池子被重建"的问题**（桶是初始化时一次性建的）——这点与卡/遗物不同，已核对。
- 但**"能力在游戏里真的显示出来、效果真的结算"没验证过**，与卡牌同样的空白。
- `Util`/`Other`/`PrimAbility` 三种类型**没有实机验证**（只实现并对照了源码分桶逻辑）。
---

## 附录 E · 文案注入顺序缺陷与最终端到端验收（2026-10-11 第五轮实机）

### E.1 症状

玩家看到卡面写着：

```
No translation found for 'sample_starter.wooden_shield' in CardName
```

同时界面弹 `OperationException : Error parsing format string: Could not evaluate the selector 'Number' …… 抽{Number}张牌。`

而平台日志说 `文案注入完成：语言 zh-Hans：写入 9 条`、**无任何错误**。

### E.2 根因：流水线顺序 + 自欺的"写入条数"

**顺序错**：原来是「装配内容（④）→ 注入文案（⑥）」。
而卡面文本是在**创建卡对象那一刻**去查本地化表的：

```
CardBase.SetByCardInfo → CardName setter → SetCardNameTMP → GetLocalizedString("CardName", key, …)
```

装配期间创建的卡，那时文案还没注入 → 卡面拿到"找不到翻译"。

**自欺的计数**：`Written++` 只统计"我调了几次 AddEntry"，**不验证游戏能不能查到**。

**一个根因解释两个症状**：描述文字的模板查找失败 → 游戏拿到解析不了的格式串 →
才抛 `{Number}` 错。文案提前后两个问题一起消失。

### E.3 修法（三处）

1. **重排**：文案注入 ④ → 装配 ⑤。
   一般化：**写文案必须先于任何会读取文案的动作。**
2. **读回自检**：写完用**游戏自己的那个 4 参重载**
   （`GetLocalizedString(table, key, null, FallbackBehavior.UseProjectSettings)`）读回来，
   统计 `ReadBackOk`/`ReadBackFailed`，失败进 `Errors`。
3. **探针判定重写**：不再自己重查本地化表，改读
   **`__instance.cardNameTextMeshPro.text`（游戏亲手写进卡面的字符串）**。
   ★ 上一版探针自己重查 → 读到"木盾"就报成功 → **假阳性**。
   **要断言"A 对 B 做了什么"，就去读 B，不要重算一遍 A。**

### E.4 修后实测（玩家亲自操作）

```
[ModKit] 文案注入完成：语言 zh-Hans：写入 7 条｜读回自检 通过 7 / 失败 0
[ModKit] ★★ 渲染探针命中 [第 1 张] sample_starter.wooden_shield
         —— 卡面实际文本 “木盾” —— ✓ 文案注入生效
（日志零 Exception、零 Error）
```

玩家回报：**图鉴**里看到两张未知卡；战斗节点的**卡牌奖励**拿到「木盾」并**实际打出** ——
**效果正常（获得 5 点格挡）、费用消耗正常、多色牌转色随机二选一正常**。

### E.5 仍未验证

- 只正面打通过「木盾」（单 `block` 效果）。「稳手」（格挡+抽牌）、**遗物**、**能力**
  还没有人在游戏里亲眼看到。
- 遗物/能力需要各自的渲染挂钩点（现在只挂了 `CardBase.SetCardNameTMP`）。