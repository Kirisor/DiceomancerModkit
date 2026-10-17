# P2 三类探针报告 · 关键词 / 特殊状态 / 能力

> 日期：2026-10-11 ｜ 方法：反编译源码 + 实机桥日志**只读**取证 ｜ 纪律：**探针通过才实现**（C9）
> **状态更新（2026-10-11）**：本报告正文的探针判定保持原样（那是它的价值）。**平台实现进度**已前进：
> 关键词、特殊状态、**能力**三类均已装配落地；其中**能力已实机验证**（打出「护盾强化」→ 游戏日志出现
> 「你获得了1层护盾提升」，能力按名可取 1/1）。关键词与特殊状态的装配已实现，**待实机验证**。
> 剧情后果仍未实现（探针判定其 `Special` 分支需要新补丁，且失败形态静默）。

## 0. 结论速览

| 内容类型 | 探针判据 | 结论 | 平台实现状态 |
|---|---|---|---|
| **关键词** | 共用 `Type.GetType` 工厂 | ✅ **通过**（可用关键词同款机制） | **装配已实现**，待实机验证 |
| **特殊状态** | 共用 `Type.GetType` 工厂 | ✅ **通过**（可复用关键词同款机制） | **装配已实现**，待实机验证（**零新增补丁**） |
| **能力** | 触发类名是**常量**，非按名拼接 | ✅ **可行**（效果侧走既有桥；装配走独立路径） | ✅ **已实现并实机验证**（1/1 按名可取） |
| **剧情后果** | 大部分是 `case` 分派；`"Special"` 走未接管的工厂 | ⚠️ **部分可行** | **未实现**（`Special` 需新补丁，失败静默） |

---

## 1. 全部 `Type.GetType` 工厂清单（决定"要不要新补丁"的唯一依据）

对全库扫描 `Type.GetType(`，共 **10 处**。逐条判定如下：

| # | 位置 | 是否按名字拼接 | 桥是否已接管 | 说明 |
|---|---|---|---|---|
| 1 | `EffectUtils.cs:40` | ✅ | ✅ | **卡牌效果** —— 已 patched（替换 1 处） |
| 2 | `CardKeyWordUtil.cs:80` | ✅ | ✅ | **关键词** —— 已 patched（2 处） |
| 3 | `RelicManager.cs:231` | ✅ | ✅ | **遗物效果** —— 已 patched（2 处） |
| 4 | `RelicBase.cs:574` | ✅ | ✅ | 与 #3 同一工厂的 `isSpecialRelic` 判定 |
| 5 | `Character.cs:501` | ✅ | ✅ | **特殊状态** —— 已 patched（2 处）★ |
| 6 | `Character.cs:504` | 常量（`…Empty`） | ✅ | 兜底类型，已被上一条的替换覆盖 |
| 7 | `AbilityUtil.cs:67` | ❌ **常量** `"Ability.AbilityTriggerBase"` | ❌ | **能力** —— 见 §3 |
| 8 | `StoryConsequence.cs:93` | ✅ | ❌ **未接管** | **剧情后果** —— 见 §4 |
| 9 | `StoryConsequence.cs:95` | ✅ | ❌ **未接管** | 同上（同一类型查两次） |
| 10 | `WorldManager.cs:860` | 数据驱动（`sectionType` 全名） | ❌ | 关卡分节类型，与 mod 内容无关 |

**关键点**：#5 就是特殊状态，它的 patch 命中可以在**平台的桥日志里直接看到**（每次启动都会打印）：

```
[ModKit]   patched → Combat.Character.GenSpecialState
[ModKit] Transpiler 替换 Type.GetType 调用 2 处（指令总数 39）。
```

→ **特殊状态不需要新增任何补丁**，与关键词完全同构。

---

## 2. 特殊状态 —— ✅ 通过

### 2.1 游戏侧机制（`Character.cs:497-511` 原文）

```csharp
private SpecialStateBase GenSpecialState(string stateName)
{
    SpecialStateBase b = UnityEngine.Object.Instantiate(GameManager.Instance.specialStateBasePrefab, specialStateTransform);
    specialStates.Add(b);
    Type type = Type.GetType("SpecialState.SpecialStateEffect" + stateName);   // ← 按名拼接
    if (type == null)
    {
        type = Type.GetType("SpecialState.SpecialStateEffectEmpty");            // ← 静默退化兜底
    }
    b.SpecialStateEffect = Activator.CreateInstance(type) as SpecialStateEffectBase;
    b.specialNumberName = stateName;
    b.ownerCharacter = this;
    b.InitializeSpecialState();
    ArrangeSpecialState();
    return b;
}
```

### 2.2 与关键词的**逐项同构**对照

| 维度 | 关键词 | 特殊状态 |
|---|---|---|
| 解析方式 | `Type.GetType("Keyword.CardKeyword" + name)` | `Type.GetType("SpecialState.SpecialStateEffect" + name)` |
| 兜底行为 | `?? Type.GetType("…CardKeyword")` | `= Type.GetType("…SpecialStateEffectEmpty")` |
| **兜底后果** | **静默**退化成基类 | **静默**退化成 Empty |
| 实例化 | `Activator.CreateInstance` | `Activator.CreateInstance` |
| mod 侧基类 | `CardKeyword`（全 virtual 钩子） | `SpecialStateEffectBase`（**abstract + 10 个全 virtual 钩子**） |
| 桥 | ✅ 已 patched | ✅ **已 patched（同一批）** |

### 2.3 `SpecialStateEffectBase` 可供覆写的钩子（原文）

```
Initialize() ／ SpecialStateLevelChange(int) ／ MainTurnStart() ／ MainTurnEnd()
BattleEnd() ／ TimeMoves() ／ ShortRest()
GetSpecialStateColorEs() → ColorE[] ／ EffectResolve() → Task ／ EndPhaseStart()
```

全部为 `virtual`（类本身 `abstract`），mod 只需覆写关心的钩子。

### 2.4 判据与实现方案

**通过判据**：解析走共用工厂 ✅ + 桥已接管 ✅ + mod 侧有可继承的抽象基类 ✅

**实现方案**（**零新增补丁**，已落地）：
- schema `modkit/state@1`（`id` / `name` / `description` / 可选 `hasNumber` / 可选 `effects`）→ `modkit/schemas/state@1.json`
- 类名约定：**类名 = `SpecialStateEffect` + id 的首字母大写形式**（与关键词同一规则）
- 校验器以 Info 诊断给出精确类名要求（因为"类名写错"是**静默退化成 Empty**）
- 装配期核验类存在（拦静默退化）

**⚠️ 与关键词不同的一个风险**：`AddComponent`/`Instantiate` 路径下，找不到类型时
**不会报错**、特殊状态会以 Empty 形态存在（数字照常显示，但**没有任何行为**）。
这与关键词的静默退化是同一类危险，所以**装配期类存在性核验是必须的，不是可选**。

---

## 3. 能力 —— ✅ 可行（已实现并实机验证）

### 3.1 游戏侧机制

```csharp
// AbilityUtil.cs:64-67
Type type = Type.GetType("Ability.AbilityTriggerBase");      // ← 常量，非按名拼接
abilityBase.trigger = abilityBase.gameObject.AddComponent(type) as AbilityTriggerBase;
abilityBase.abilityEffect = EffectUtils.GenerateEffectFromEffectInfo(
    abilityInfo.effectInfo, abilityBase, abilityBase.rNumbersForEffect.ToArray(), isAbilityEffect: true);
```

### 3.2 判定

- **触发类不是逐能力分派的**：`"Ability.AbilityTriggerBase"` 是**一个常量名字**，
  游戏对**所有**能力都用同一个 Trigger 组件类。所以这里不是"内容工厂"，
  而是"取基类"——**没有逐内容的多态分派需要桥接**。
- **能力的行为来自效果**：`abilityEffect` 走 `EffectUtils.GenerateEffectFromEffectInfo`，
  而那个工厂**已被桥接管**。所以**能力的效果侧天然被平台覆盖**。
- **但有两个前提**：
  1. `AbilityDataManager` 用 **5 个私有 `Dictionary<string, AbilitySo>`**
     分类存放（`_allAbilitySos` / `_allPlayerBuff` / `_allPlayerDeBuff` /
     `_allEnemyDeBuff` / `_allEnemyBuff`）→ 装配必须写进**正确的那个桶**；
  2. `AbilitySo` 是烘焙资产，运行期加载需走"克隆原版 + 改数据"（与卡/遗物同法）。

### 3.3 ⚠️ 一个**必须记录**的差异

`AddComponent(null)` **会抛异常**（不像关键词是静默退化）。
这是好事：能力侧的失败是**响亮的**。但也意味着——**如果 `Ability.AbilityTriggerBase`
这个类型名在任何情况下解析不到，能力会直接崩，而不是静默失效**。平台装配时必须自己先确认它存在。

### 3.4 结论与落地

**可行**：效果侧（也是作者最关心的"能力干什么"）**已可用**；
`AbilitySo` 的装配（分类桶 + 资产克隆）走独立装配路径，**不需要新补丁**。

**已实现并实机验证**（2026-10-11）：
- 装配路径：`modkit/ModKit.Game/ContentAssembler.Abilities.cs`（分类桶 + 资产克隆）
  + `modkit/ModKit.Game/EffectSpAbilityBlockGainAddSub.cs`（示例自定义效果）
- 示例 mod `modkit/mods/sample_starter/abilities/shield_up.json`（`modkit/ability@1`，3 卡 + 1 遗物 + 1 能力）
- 实机结果：打出「护盾强化」→ 游戏日志出现「你获得了1层护盾提升」，**能力按名可取 1/1**

---

## 4. 剧情后果 —— ⚠️ 部分可行（平台未实现）

### 4.1 游戏侧机制（`StoryConsequence.cs` 原文）

`ResolveConsequence` 是**一个大 `case` 分派**，内建类型**纯数据**：

```
case "Battle"              → Battle(consequence.encounterEnemySo)
case "SmallGame"           → SmallGameManager…SetCurrentSmallGameAndStartGame(consequence.smallGame)
case "AddStoryMark"        → AddStoryMark(consequence.storyMark)
case "AddCareerStoryMark"  → AddCareerStoryMarker(consequence.storyMark)
case "AddCampaignStoryMark"→ AddCampaignStoryMarker(consequence.storyMark)
case "TimeMoves"           → new SubCommandTimeMove()
case "Special"             → 见下
```

`"Special"` 是唯一走类型查找的分支，且**有两条路径**：

```csharp
case "Special":
    if (typeof(StoryConsequence).GetMethod(consequence.specialConsequenceMethod) != null)
        await (Task)typeof(StoryConsequence).GetMethod(…).Invoke(null, null);   // ← 路径①：静态方法
    else if (Type.GetType("Story.SpecialStory.SpecialStory" + method) != null)  // ← 路径②：类型拼接
        … Activator.CreateInstance(…) as SpecialStoryConsequence …
```

### 4.2 判定

| 子能力 | 可行性 |
|---|---|
| **内建后果类型**（Battle / SmallGame / 三种 StoryMark / TimeMoves） | ✅ **纯数据，可直接表达** |
| 路径①：`StoryConsequence` 上的**静态方法**（`GetMethod`，非 `Type.GetType`） | ❌ **平台无法表达** —— 要往游戏的类上加静态方法，需 IL 级注入 |
| 路径②：`Story.SpecialStory.SpecialStory<名>` 类型 | ⚠️ **需新增补丁** —— `ResolveConsequence` 不在当前 4 个 patched 方法里 |

### 4.3 结论

**部分可行，但实现价值低、风险高，平台暂未实现**：
- 内建类型是纯数据，**平台本来就能表达**（不需要新机制，只要 schema 允许写后果）；
- 路径①**物理上无法用"数据化 + C# 兜底"表达**（它要求把方法加进游戏的类）；
- 路径② 需要给 `ResolveConsequence` 加第 5 个 transpiler 补丁 —— 可行，但**它的失败形态是静默的**
  （两个 `if` 都不成立时，`Special` 后果**什么都不做、不报错**）。

**如实标注**：平台首版对剧情后果的支持是
「**内建类型 ✅ / Special:方法名 ❌ 不支持 / Special:类型名 ⚠️ 需后续补丁**」。
按 C9，**不把"未实现的 Special"说成"支持"**。

---

## 5. 对平台的净影响

1. **特殊状态**：装配已实现，成本与关键词同量级（复用同一套解析/校验/核验基础设施）。
2. **能力**：效果侧已覆盖；装配侧已完成并**实机验证**（分类桶 + 资产克隆）。
3. **剧情后果**：纯数据部分可用；`Special` 分支需**新补丁**，且失败静默——
   若将来要做，**必须先加"类存在性核验"**，否则会重现关键词那种"没效果也不报错"的坑。
4. **桥的补丁数仍然是 4** —— 特殊状态搭的是既有便车，**这是本报告最有价值的发现**：
   原本预计要新增的补丁，实测不需要；能力也不需要新补丁（它缺的是装配路径，不是补丁）。
