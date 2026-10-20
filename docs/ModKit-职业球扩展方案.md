# ModKit 职业球扩展方案：四条路线与一个排除项

| 项目 | 值 |
|---|---|
| 文档日期 | 2026-10-20 |
| 对象版本 | 0.2.2（`Directory.Build.props:5`） |
| 代码基线 | `G:\Modkit` HEAD `4170b0b`；反编译基线 `G:\deepseek space\diceomancermod\research\decompiled` |
| 文档定位 | **方案设计真源**（"怎么再加一颗球"）；现状与缺陷见《[ModKit-实施报告](ModKit-实施报告.md)》§1、§4、§5 |
| 证据口径 | 代码引用写 `文件:行`；游戏内部行为一律标反编译相对路径（`Assembly-CSharp/<类型>.cs:行`）。标「待实机」的结论**没有**游戏内证据 |

> **落地前置（先读这一条）**：本仓库当前**编译不了平台层**——`src/ModKit.Api/ClassApi.cs`（`ClassSpec` / `IModClassProvider` 的定义）缺失、`src/` 无任何 `.csproj`（报告 B-01、B-02）。因此本文所有代码改动只能在旧工作树 `G:\deepseek space\diceomancermod\modkit` 内施工，或先完成 B-01/B-02 的迁回。这不是建议，是"改了也编不过"的事实。

---

## 0. 结论先行

**真正的瓶颈不是"怎么加一颗球"，而是"球的身份只有一个枚举值，而职业的身份是三色组合"。** 前者被引擎锁死（6 个枚举值、5 个已用），后者是免费空间（6 色共 20 个三元组）。四条路线本质上是在回答同一个问题：**把"职业的身份"放在哪一层**。

> 先说一个读者最容易想到、但必须排除的近亲：**把原版颜色"套皮"成新颜色**（如绿球换黄皮）。它不增加任何身份，还会吃掉被套的那个颜色，使 `{紫,黄,绿}` 这类组合永久不可得——完整论证见 §8.1。
>
> 第二条必须提前说的：**"三色组合"是有容量的全局命名空间**（含 Black 只有 10 组），且两个 mod 完全可能都想用 `{Black, Green, Purple}`。同组时游戏会同时踩四个消费点（职业名、牌组、卡池、统计），可能出现"名字来自 A、牌组来自 B、卡池 A+B"。解法不是再算组合数，而是**把身份从颜色移到玩家选择上**（§6.8 的 C+，4 个数据层补丁）。

| 方案 | 一句话 | 职业数上限 | 玩家能看到新球 | 工时 | 定位 |
|---|---|---|---|---|---|
| **A** 物理球复用 `Black` | 启用原版闲置的第 6 色，仍走原版选球界面 | **1**（受 MustUse 锁槽限制） | 是（第 6 颗黑球） | 0.5 天 | 降级预案 |
| **B** 多物理球（OrbId 侧信道） | N 颗球复用 `Black`，靠平台侧 `OrbId` 区分 | N | 是（N 颗同色球） | 5–7 天 | 暂缓（补丁面最大） |
| **C** 自建多球面板 | 接管选球界面，每职业一颗球，写三色直接开局 | **≤10**（Black + 2 个原版色，零侧信道） | 是（面板内每职业一颗） | 3–5 天 | **推荐主线**（已建成约 60%） |
| **D** 零新增球 | 放开原版 5 球状态，用闲置三元组当职业 | ≤10 − 原版占用 | 否（复用 5 球） | 1–2 天 | 低成本扩容（需拍板） |
| **E** 扩枚举 / 改资产 | 运行期给 `ColorE` 加成员 | — | 是 | — | **不可行，排除** |

**推荐组合**：C 为主线；D 作为"只要多几个职业、不要新球美术"的低成本选项（唯一需要产品拍板的是"允许 Purple/Void 从 MustUse 变成可移动"）；A 保留为 C 失效时的降级；B 暂缓；E 明确不做。

---

## 1. 问题定义：一件事，三个层面

"新增一颗职业球"在《骰子浪游者》里不是一个动作，而是三层同时成立才算数：

| 层面 | 要成立什么 | 对应游戏机制 |
|---|---|---|
| **数据层** | 一个 `CardDateCollection`（`type = Class`、`colorEs` 恰好 3 个）提供职业名与卡池；一条 `StartColorESDecks` 提供初始牌组与起始遗物 | `CardDataManager.GetClassNameByColorE`（`Assembly-CSharp/GameData/CardDataManager.cs:106-121`）、`SubSmallGameChooseColor.GetStartDeckByColorE`（`Assembly-CSharp/SmallGames.WorldBetween/SubSmallGameChooseColor.cs:187-197`） |
| **界面层** | 玩家"能看见"一颗球、能给球上色、能把它拖进槽、槽满能确认 | `CalculateColorEAndStates`（`:266-294`）、`InitializeOrb`（`:108-132`）、`ChooseColorEOrbForSubGame`（`Assembly-CSharp/SmallGames.WorldBetween.SubGame/ChooseColorEOrbForSubGame.cs:56-123`） |
| **存档层** | 玩家选的三色要写进主菜单并由开局流程消费 | `SmallGameMainMenu.mainMenuSmallGameColorEs`（`Assembly-CSharp/SmallGames.WorldBetween/SmallGameMainMenu.cs:65`）、启动时 `SetCampaignUsedColorE`（`:228`） |

**已建成的部分**：数据层（`CareerAssembler` 全流程）+ 存档层（`CareerPicker.WriteMainMenuColors`）。**真正的分歧点只在界面层**——这也是四个方案唯一实质不同的地方。

---

## 2. 六条硬约束（方案必须建立在这六条上）

| # | 约束 | 证据 | 对方案的影响 |
|---|---|---|---|
| **C1** | **球的身份 = 一个 `ColorE` 枚举值**，且被 4 个地方消费：底座匹配、贴图匹配、tooltip 键、取值 | `GetPedestalByColor`（`SubSmallGameChooseColor.cs:143-146`）、`SetColorE`（`ChooseColorEOrbForSubGame.cs:97-109`）、`Orb_<ColorE>`（`:231`）、`GetPickedColorES` 回 `GetColorE()`（`:228-236`） | 两颗球同值时，底座/贴图/提示/取值**全部不可区分**——这是 B 方案必须解决的核心难题 |
| **C2** | **枚举不可扩展**：`ColorE` = Red/Green/Blue/Purple/Void/Black，5 个已做成球，`Black` 是唯一空闲值 | `CareerPatch.cs:4-12`；`Assembly-CSharp/Utilities/DitwUtil.cs:642-650`（`GetAllBasicColorEArray` 硬编码 5 色） | 想"多一颗物理球"就只能在 Black 上做文章 |
| **C3** | **Black 是合法法力色**（不是死值） | `ModKitDump/capabilities.json`：`availableCostColors` = 6（含 Black）、`knownCostCombos` 21 组含 `["Black"]`/`["Blue","Black"]` | 用 Black 做职业色不会让卡牌无法施放；这是 Black 路线成立的前提 |
| **C4** | **职业身份 = 恰好 3 色 + `colorEs ⊆ 选中三色` 的 Class 集合**，与"有几颗球"无关 | `GetStandardCardDataCollectionByColorE`（`CardDataManager.cs:80-91`）、`GetClassNameByColorE`（`:106-121`，长度≠3 直接 `InvalidClass`） | 6 色 → **20 个三元组**是免费的职业空间；加球不是必须的 |
| **C5** | **原版界面可达的三元组只有一个**：Purple 恒 `MustUse`、Void 默认 `MustUse`，3 槽被锁 2 个 | `CalculateColorEAndStates`（`SubSmallGameChooseColor.cs:266-294`）；`MustUse` 拒绝移动（`ChooseColorEOrbForSubGame.cs:116-123`） | 走原版界面（方案 A）**上限就是 1 个新职业**，且三色只能是 `{Black, Purple, Void}` |
| **C6** | **现状是"原版造球链路关断"**：六处挂钩全部早退，但装配期校验仍强制每职业占一个非原版色 | `CareerPatch.cs:100`（`EnableCustomOrb = false`）、`:265`/`:275`/`:307`（三处早退）；`CareerAssembler.cs:73-94` | B/C/D 都必须**放宽装配校验**，否则"三色不含 Black"直接被拒 |

---

## 3. 现状盘点：哪些能直接用

| 能力 | 状态 | 证据 |
|---|---|---|
| 数据层：Class 集合 + 开局牌组 + 起始遗物 + 职业文案 | **已实机**（奶蛙 Stage 1 用户打完一局） | `CareerAssembler.cs:158-290`；报告 1.5 表 |
| 存档层：把三色写进 `mainMenuSmallGameColorEs` 并强切回房间 | **仅代码，未实机** | `CareerPicker.cs:364-389`、`CareerOrbDriver.cs:60-91` |
| 界面层：自建面板（列表/滚动/详情/确认） | **仅代码，未实机** | `CareerPicker.cs:150-362` |
| 界面层：原版第 6 颗球（自造视觉球 + 底座克隆 + 贴图表项） | **实机过（Stage 1 时开启），现已关断** | `CareerPatch.cs:298-363`、`CareerOrbDriver.cs:1-15` |
| 认定"这局用了非原版球组合"并强制切回 | **仅代码** | `CareerPatch.cs:490-522` |
| 作者侧文档：职业怎么声明 | **缺失**（作者手册 0 处提到职业） | `grep -n "职业" docs/ModKit-作者手册.md` 无命中 |

**这张表决定了两件事**：① 数据层不用重做，四个方案的差别只在界面层；② 无论选哪个方案，"给作者写一节职业文档"都是必做项。

---

## 4. 方案 A · 物理球复用 `Black`（原版界面，1 个职业）

### 4.1 机制

打开 `EnableCustomOrb`，让原版选球界面多出第 6 颗黑球：

1. `GetAllBasicColorEArray` postfix 追加 Black → 球进入候选（`CareerPatch.cs:263-270`）；
2. `CalculateColorEAndStates` postfix 追加 `ColorEAndState(Black, Free)` → 有状态（`:273-283`）；
3. `StartSubSmallGame` prefix：给 `colorEToOrbSprites` 补一条 Black 贴图、克隆第 6 个底座（`:298-363`，因为 `GetPedestalByColor(Black)` 原本返回 null，会在 `InitializeOrb` 里取到 null 的 `_holder`）；
4. 每帧宿主自造一颗视觉球跟手拖拽，松手时把游戏那颗"看不见的真球"`PutOrb` 进槽（`CareerOrbDriver.cs:1-15`、`:388+`）。

### 4.2 改动清单

| 文件:行 | 改动 | 为什么不能少 |
|---|---|---|
| `CareerPatch.cs:100` | `EnableCustomOrb = true` | 否则三处早退，等于没做 |
| `CareerAssembler.cs:73-101` | 追加校验：`Colors` 必须**同时包含** Purple、Void、Black | C5 决定原版界面凑不出别的组合；提前报错胜过开局才发现选不到 |
| `CareerAssembler.cs:106-110` | `ConfigureClass` 前检测"全局单例已被占用"：第二个职业直接 `result.Errors.Add` 并 `return` | `NewOrbColor`/`OrbSpritePath`/`BallScale` 是**静态单值**（`CareerPatch.cs:105-132`），第二个职业会静默覆盖第一个 |
| `CareerAssembler.cs:250-262` | 写 `allStartDeck` 前检测"同三元组已存在"→ Error | `GetStartDeckByColorE` 用全等匹配取**第一个**（`SubSmallGameChooseColor.cs:187-197`），重复写入会让后者永不生效 |
| `CareerAssembler.cs:266-270` | `colorEAndStates` 只在颜色不同时追加 | 现状已按颜色去重，保持 |

### 4.3 API / 数据流

- `ClassSpec`（定义在旧树 `ModKit.Api/ClassApi.cs:21-77`）**零改动**；`OrbColor` 必须是 `Black`。
- 数据流：mod JSON → `ClassSpec` → Class 集合 + `StartColorESDecks` → 原版选球界面（Black 球）→ `GetPickedColorES()` 返回 `{Black, Purple, Void}` → `GetClassNameByColorE` 命中我们的集合 → `GetStartDeckByColorE` 命中我们的牌组。

### 4.4 多职业与多 mod

**上限 1 个**。两个 mod 都声明职业时：只有先装配的那个能生效，另一个必须**显式报错**（而不是现状的静默覆盖）。这是 A 方案唯一必须新写的逻辑。

### 4.5 风险与失效模式

| 风险 | 表现 | 判据 |
|---|---|---|
| 6 个补丁点任一改名 | 球不出现 / 状态不对 / 点不动 | 日志 `找不到 DitwUtil.GetAllBasicColorEArray` 等（报告 R-01） |
| 第 6 个底座克隆错位 | 球叠在第 5 颗上、或跑到屏幕外 | 需要实机看构图，代码无法自证 |
| 自造球与真球不同步 | 拖拽看起来成功但槽里没球 | `GetPickedColorES` 返回的数组含 null |
| 存档往返后职业失效 | 第二局仍显示原版 5 球 | 需 Q-13 实验 |

### 4.6 工时

**0.5 人日**（改校验 + 冲突检测 + 重新打开开关）+ **1 局实机**（约 40 分钟）。

### 4.7 验收

1. 进入选球界面可见第 6 颗黑球，且可拖入第 3 个槽；
2. 三槽 = `Black/Purple/Void`，确认后回房间，主菜单显示三色圆点；
3. 开局牌组张数 = `StartDeck` 声明数，起始遗物 2 件；
4. 日志含 `职业装配完成：<classKey>（… 球 Black=Free …）`；
5. **负例**：第二个 mod 声明职业 → 日志出现明确 Error，且不覆盖第一个职业。

**kill criteria**：若 `GetAllBasicColorEArray`、`CalculateColorEAndStates`、`StartSubSmallGame`、`InitializeOrb` 中任一个在目标版本找不到 → **放弃 A**，转 C（C 只依赖 2 个公开方法）。

---

## 5. 方案 B · 多物理球：`OrbId` 侧信道（N 颗球复用 `Black`）

### 5.1 机制

保留"一颗物理球一个 `ColorE` 值"的外观，但**身份另立一套**。注意：B **不是**"把原版颜色套皮"（那条路见 §8.1，必然自毁），而是让 N 颗球**全部复用 `Black`**——Black 原本没有球，所以"多出 N 颗黑球"不会吃掉任何原版颜色；代价是 N 颗球在数组里长得一模一样，必须靠 `OrbId` 侧信道才能区分：

1. 新增平台侧 `CareerOrbRegistry`：`List<OrbSpec{ int OrbId, ManaColor[] Triple, string SpritePath, string Label }>` + `Dictionary<GameObject,int>`（视觉球 → OrbId）；
2. `AfterCalculateColorEAndStates` 追加 **N 条** Black 条目（原版按条目逐一 `Instantiate`，N 条 = N 颗球）；
3. `BeforeStartSubSmallGame` 克隆 N 个底座，并给每个底座记 `OrbId`（顺序对应注册表）；
4. `InitializeOrb` 必须**替换**（不能只 postfix）：原版 `GetPedestalByColor(colorE)` 对 N 颗同色球永远返回**第一个**底座（`SubSmallGameChooseColor.cs:143-146`），第 2..N 颗会摆错位置；
5. 视觉层复用 `CareerOrbDriver`：把"单颗视觉球"（`:29-35`）改成 `List<VisualBall>`，每颗带 `OrbId`、各自贴图与标签；
6. 确认链路复用已有侧信道：`PickerColorsPatch`（`CareerPicker.cs:518-534`）按"槽内球的 OrbId"回 `spec.Colors`，而不是回全局 `ChosenColors`；
7. 补两个前缀补丁：`GetClassNameByColorE`（三元组来自 OrbId → 直接回 `classKey`）、`GetStartDeckByColorE`（回对应 `StartColorESDecks`）。**这两个补丁是硬需求**：否则三色数组里三个 Black 会让 `CheckTwoArraySame` 与 Class 查找全部失配。

### 5.2 改动清单

| 文件 | 改动 |
|---|---|
| 新增 `src/ModKit.Game/CareerOrbRegistry.cs` | OrbId ↔ 三元组 ↔ 贴图/标签；进入界面时清空重建 |
| `CareerPatch.cs:273-283` | 追加 N 条同色条目（不是 1 条） |
| `CareerPatch.cs:298-363` | 克隆 N 个底座；`colorEToOrbSprites` 仍只加一条（同色共用） |
| `CareerPatch.cs:193-199` | `InitializeOrb` 由 postfix 改为 **prefix 重写**（含 `homePedestal` 按 OrbId 赋值） |
| `CareerOrbDriver.cs:29-35`、`:388+` | 单球 → 多球；`DropOrb` 按"第 k 颗视觉球 → 第 k 颗真球"投递 |
| `CareerPicker.cs:518-534` | 侧信道从"全局三色"升级为"按 OrbId 查表" |
| 新增 2 个 Harmony 前缀 | `GetClassNameByColorE`、`GetStartDeckByColorE` |
| `CareerAssembler.cs:73-94` | 放宽"必须含非原版色"，改为"三元组全局唯一" |

### 5.3 API / 数据流

`ClassSpec` 不变。数据流在界面层分叉：**视觉球（我们造的，带 OrbId）** 与 **逻辑球（游戏的，`colorE = Black`）** 一一对应；玩家选完后，逻辑球给出 `ColorE[]`（全是 Black 或含 Black），再由侧信道把它翻译成"职业三元组"。

### 5.4 多职业与多 mod

**上限 ≤10，且零侧信道**：每个职业用 `{Black, X, Y}`（X、Y 取 5 个原版色中的两个），共 C(5,2) = **10** 个互不相同的三元组。因为 `ColorE[]` 数组本身就能区分它们（Black 相同、X/Y 不同），`GetClassNameByColorE` 与 `GetStartDeckByColorE` **不需要任何补丁**——数组在 `CareerPicker.cs:379` 写入时就是 `(Card.ColorE)(int)c`，游戏开局按 `SetCampaignUsedColorE`（`CampaignManager.cs:110-114`）原样消费。若还要再扩，只能再用"纯原版三元组"（需 Q-15 的占用表，上限 +（10 − 原版占用数））。多 mod 时三元组冲突在装配期报错。

### 5.5 风险与失效模式

| 风险 | 说明 |
|---|---|
| **补丁面最大** | 6–8 个私有方法/字段（`_orbs`、`pedestals`、`spriteRenderer`、`_holder`），全部可能在游戏更新后改名 |
| `_orbs` 顺序不稳定 | `InitializeOrbs` 先 `Object.Destroy`（延迟到帧末生效）再重建（`:78-106`），跨帧读 `_orbs` 可能拿到旧引用 |
| 同色球恢复错位 | `InitializeOrbs:98-105` 用 `FirstOrDefault(o => o.colorE == colorE)` 恢复已选球，多颗同色时会**把第 1 颗当成所有**——必须一并重写 |
| 视觉/逻辑不同步 | 视觉球是我们造的，逻辑球是游戏的；一旦 `PutOrb` 失败，界面看起来"选好了"但开局牌组是空的 |

### 5.6 工时

**5–7 人日** + **至少 2 局实机**（含"重进界面"与"存档退出重进"）。

### 5.7 验收

1. 界面出现 N 颗球，每颗贴图不同、都能拖进槽；
2. 任意两职业组合分别开局，牌组/遗物/职业名各自正确；
3. 退出界面再进入，已选球**恢复原样**（这一条最容易碎）；
4. 存档退出重进后职业仍生效；
5. **负例**：两个职业三元组相同 → 装配期 Error。

**kill criteria**：若 `InitializeOrbs` 或 `InitializeOrb` 在目标版本找不到 → 直接放弃 B（这两个是私有方法，且没有任何公开替代入口）。

---

## 6. 方案 C · 自建多球面板（接管选球界面）★ 推荐主线

### 6.1 机制

**不进原版小游戏，用自己的面板出球**：

1. `BeforeStartSubSmallGame` 在 `ClassDeclared` 时**返回 `false`**（跳过原版小游戏的启动），但仍记下 `CurrentPicker`（后续要拿它的 `_mainMenu`）；
2. `CareerPicker` 面板从"文字列表"升级为"**每职业一颗球**"：贴图走 `ModArt.Load(spec.OrbSprite)`（已有，`CareerPicker.cs:209-223`），没图就按 `OrbColor(spec)` 画一个圆（`:443-455`，已实现）；
3. 玩家点一颗球 = 选一个职业；确认时把该职业的三色写进 `mainMenuSmallGameColorEs`（已有，`:364-389`）、`ChosenColors = spec.Colors`（已有，`:350`）、并**补一次 `AscensionBox.Initialize(classKey)`**（原版在 `LeaveSubSmallGame:155` 做，我们跳过它就必须自己补）；
4. `ForceSwitchBack` 显示桌面 + 隐藏界面 + 刷新按钮（已有，`CareerOrbDriver.cs:60-91`）；
5. 玩家点主菜单「开始游戏」→ 游戏按 `mainMenuSmallGameColorEs` 的三色走 `SetCampaignUsedColorE`（`SmallGameMainMenu.cs:228`）→ 职业数据由 `CareerAssembler` 早已写好的 Class 集合 + `StartColorESDecks` 接管。

### 6.2 改动清单

| 文件:行 | 改动 | 说明 |
|---|---|---|
| `CareerPatch.cs:298-319` | `BeforeStartSubSmallGame` 增加"跳过原版小游戏"分支（返回 false），保留 `CurrentPicker` 记录 | 现状会 `CareerPicker.Show()` 后继续让原版界面铺球 |
| `CareerPicker.cs:150-237` | 列表行 → 球网格：球贴图 + 职业名 + 三色小圆点 | 视觉工作量在此 |
| `CareerPicker.cs:330-362` | 确认后补 `_mainMenu.GetAscensionManager().Initialize(classKey)` | 否则升魔盒显示的是**上一个职业**的等级（`AscensionBox.cs:71-82` `_currentClass` 不更新；`GetClassMaxAscensionLevel` 对未知职业回落到 1，不会崩） |
| `CareerPicker.cs:518-534` | `PickerColorsPatch` 保持（原版 `RefreshButtonAndIntroCard` 仍可能被调用） | 已有 |
| `CareerPatch.cs:100` | 保持 `false` | 不造原版球 |
| `CareerAssembler.cs:73-94` | 放宽"必须含非原版色" → "三元组全局唯一 + 不与原版重复" | 见 C6 |
| `docs/ModKit-作者手册.md` | 新增「职业」一节：`IModClassProvider`、`ClassSpec` 七个字段、`colors` 恰好 3 个、`startRelics ≥ 2` | 当前完全缺失 |

### 6.3 API / 数据流

- `ClassSpec` 不变；`OrbColor` 语义从"物理球颜色"变为"**面板图标底色**"（当 `OrbSprite` 为空时兜底）——这句话必须写进 XML 注释，否则作者会以为它还是物理球。
- 数据流：mod → `ClassSpec` → `DeclaredSpecs` 列表（`CareerAssembler.cs:35-36`）→ `CareerPicker` 渲染 → 玩家选 → 三色写入主菜单 → 开局。
- **不经过** `GetPickedColorES`/`IsAllSlotHasOrb`/`CalculateColorEAndStates`/`InitializeOrb`/`GetAllBasicColorEArray`：这正是 C 抗游戏更新的原因（依赖面从 6 个方法缩到 2 个）。

### 6.4 多职业与多 mod

N 个职业（`DeclaredSpecs` 天然是列表，`AssembleFromModAssembly` 会遍历一个 mod 内所有 `IModClassProvider`，`CareerAssembler.cs:354-379`）。跨 mod/跨 spec 的**三元组唯一性**校验是起步动作（否则第二个职业在 `GetClassNameByColorE` 里永远输给第一个，属报告 R-07 的同型静默冲突）——但它只是"挡住冲突"，**不能解决"两个 mod 都想要这组三色"**：那需要把身份从颜色移到面板选择上，见 **§6.8**（C+）。

### 6.5 风险与失效模式

| 风险 | 表现 | 缓解 |
|---|---|---|
| 跳过 `StartSubSmallGame` 后原版对象处于"半初始化" | 取消/ESC 时黑屏或卡住 | 面板自带关闭按钮 + 保留 `ForceSwitchBack` 兜底 |
| 忘记调 `AscensionBox.Initialize` | 升魔盒等级显示成上个职业 | 6.2 第 3 行 |
| 主菜单颜色只认 `Count == 3` | 写失败则「开始游戏」按钮不可点 | 已写 `RefreshPlayButton`（`:383-386`） |
| 三色里含 Black，原版图鉴/卡池过滤 | mod 卡可能出现在原版职业页 | 已有"职业隔离"收紧集合 `colorEs`（`CareerAssembler.cs:216-240`），但**需重测** |
| 面板遮挡原版护符/介绍卡 | 交互冲突 | 现状是左抽屉（`:59-63` 常量），实机确认 |

### 6.6 工时

**3–5 人日**（面板球网格 + 跳过原版 + Ascension 补调 + 校验）+ **2 局实机**（正常开局 + 存档退出重进）。

### 6.7 验收

1. 进入职业界面：面板出现 N 颗球，原版小游戏**不出现**；
2. 点任意一颗 → 确认 → 回房间 → 三色圆点正确 → 「开始游戏」可点；
3. 开局牌组与遗物与该职业声明一致；职业名显示中文（`Encyclopedia.<classKey>`）；
4. 升魔盒显示的是**本职业**的等级；
5. 存档退出重进：职业仍在，再次进面板仍可选（**待实机**）；
6. **负例**：两个职业三元组相同 → 装配期 Error 且不覆盖。

**kill criteria**：若面板在 3 个不同分辨率/窗口模式下都有遮挡或输入穿透 → 退回 D（列表形态，不做球）。

### 6.8 三元组冲突：两个 mod 要同一组三色怎么办

这是"用三元组当身份"必然撞上的一堵墙，**不是实现细节**：A mod 做 `{Black, Green, Purple}`，B mod 也做 `{Black, Green, Purple}`，玩家装了两个，游戏怎么知道你要哪个？

**答案：它不知道，而且失败方式比"选错一个"更糟。** 同一三元组会同时踩四个消费点：

| # | 消费点 | 代码 | 同组三色时的实际行为 |
|---|---|---|---|
| 1 | 职业名 | `GetClassNameByColorE` 取 `GetStandardCardDataCollectionByColorE` 的**第一个** Class 集合（`CardDataManager.cs:106-121`、`:80-91`） | 谁先被写进 `_colorCardDateCollections`（= 装配顺序）谁赢，另一个永不出现 |
| 2 | 初始牌组/遗物 | `GetStartDeckByColorE` 取**第一个**全等匹配（`SubSmallGameChooseColor.cs:187-197`） | 可能与 1 不是同一个 mod ⇒ **名字来自 A、牌组来自 B** |
| 3 | 战役卡池 | `SetCampaignCardPool` 把**所有** `colorEs ⊆ 三色` 的集合整包并入（`CampaignManager.cs:87-103`） | **A 与 B 的卡同时进池**——选 A 的职业能抽到 B 的卡 |
| 4 | 存档统计 | `CampaignManager.cs:74`/`:219`、`SaveLoadManager.cs:250`（`SaveAbandonCampaignStatistics`） | 胜场/升魔等级记到"第一个"职业，另一个永远 0 |

⇒ 这不是"二选一"，而是**四个消费点可能各选一个 mod**。任何"三元组当身份"的形态（A / C 的原样、D）都必须先解决它。

#### 6.8.1 三条仲裁路线

| 路线 | 做法 | 能共存吗 | 改动 | 代价 |
|---|---|---|---|---|
| **抢注表** | 平台在加载期汇总所有 mod 的 `Colors`，同组冲突则**后者 Error**、面板里灰显并注明"被 A mod 占用" | 不能（先到先得） | 无补丁，纯校验 | 全局只有 10 组（含 Black），**第 11 个作者没得选**；作者无法预知别人占了什么 |
| **玩家选择即身份**（C+，推荐） | 颜色只作**主题**，身份由面板选择决定：选定 spec 后置 `ChosenSpec`，把 4 个消费点按 spec 覆盖 | **能**（任意多个 mod 可同组） | 4 个数据层补丁 | 补丁要覆盖读档/统计路径，漏一处就"开局对、读档错" |
| 混合 | 先抢注（把冲突变成可见错误），再上 C+（把稀缺性去掉）；抢注退化为"提示" | 能 | 先 0.5 天，再 3–4 天 | 无额外代价；是推荐落地顺序 |

#### 6.8.2 C+ 的补丁清单（4 处，全在数据层，不碰 UI 内部）

| # | 补丁点 | 语义 |
|---|---|---|
| a | `CardDataManager.GetClassNameByColorE` **prefix** | `ChosenSpec != null` ⇒ 直接回 `spec` 的 `classKey`（覆盖 1、4 两处消费点，因为 `:74`/`:219`/`SaveLoadManager.cs:250` 都走它） |
| b | `SubSmallGameChooseColor.GetStartDeckByColorE` **prefix** | 回 `spec` 自己的 `StartColorESDecks`（否则开局牌组来自"第一个"） |
| c | `CampaignManager.SetCampaignCardPool`（private）**postfix** | 把 `campaignCardPools` 过滤成"原版色集合 + 本 spec 的 Class 集合"，剔除同组其他 mod 的 Class 集合 |
| d | `LoadCampaignInfo` **postfix**（`SaveLoadManager`） | 按存档里已存的 `campaignStatisticsInfo.className` 反查 spec 并恢复 `ChosenSpec`——**不补这条，读档后 `ChosenSpec` 为空，a 会回落到"第一个"** |

`ChosenSpec` 的生命周期：面板确认时置位 → 本局结束/回主菜单时清空 → 读档时按存档恢复。

#### 6.8.3 契约影响与校验

- `ClassSpec.Id` 的全局唯一性由 `modId + "." + Id` 保证（`CareerAssembler.cs:155-156` 的 `classKey`）⇒ **C+ 下身份是 `classKey`，不是颜色**。
- `Colors` 从"身份"降级为"主题/展示"：作者可以按美术需要选三色，不必协调；**唯一硬约束变成"恰好 3 个"**（`GetClassNameByColorE` 长度必须为 3）。
- 抢注路线仍保留为**默认兜底**：C+ 未启用时，冲突必须 Error，绝不允许静默（现状是静默覆盖，属报告 R-07 同型缺陷）。

#### 6.8.4 验收

1. 两个 mod 声明同三元组 + C+ 开启：面板列出**两个**职业，分别开局 → 职业名/牌组/遗物/卡池**各归各的**（卡池里不出现另一个 mod 的卡）；
2. 存档 → 退出 → 重进 → 继续：职业名与卡池仍是同一个 mod 的（**这条验的是补丁 d**）；
3. 放弃本局（触发 `SaveAbandonCampaignStatistics`）：统计记到**正确**的职业；
4. C+ 关闭 + 抢注：第二个 mod 在加载期得到明确 Error，面板灰显，不出现"名字来自 A、牌组来自 B"；
5. **负例**：故意只打 a、b、c 不打 d → 读档后职业应错，证明 d 必要（负例控制）。

**kill criteria**：若 `SetCampaignCardPool` 或 `LoadCampaignInfo` 在目标版本找不到 ⇒ C+ 降级为抢注路线，并在文档中把"同组只能有一个职业"写成已知限制。

---

## 7. 方案 D · 零新增球：放开 5 球状态 + 闲置三元组

### 7.1 机制

**不加球，只加"可组合性"**：

1. 一条补丁把 `CalculateColorEAndStates` 的 `MustUse` 改 `Free`（Purple 恒 Free、Void 恒 Free，可选把 Red/Green 也置 Free 以摆脱"升级解锁"）→ 玩家可在原版界面自由选任意 3 颗；
2. 5 个原版色 → C(5,3) = **10 个三元组**；原版职业占用的三元组之外，全是新职业空位；
3. 职业仍然完全走数据层（Class 集合 + `StartColorESDecks`），零 UI 改动；
4. 需要一个**新探针**先 dump 出"原版占用了哪几个三元组"（从 `_colorCardDateCollections` 里 `type == Class` 的集合），否则不知道哪些是空位。

### 7.2 改动清单

| 文件:行 | 改动 |
|---|---|
| `CareerPatch.cs:273-283` | `AfterCalculateColorEAndStates`：把 Purple/Void 的 `MustUse` 覆写为 `Free`（并记录日志，便于取证） |
| `CareerPatch.cs:100` | 保持 `false`（不造球） |
| `CareerAssembler.cs:73-94` | 放宽：允许**全原版三色**；新校验 = "三元组唯一 + 不在原版占用表内" |
| `CareerAssembler.cs:216-240` | 职业隔离逻辑重估：现状把通用集合收紧成"含 Black 的三色"以躲开原版职业页；全原版三色时这条不再成立，需改为"按职业 key 精确匹配" |
| `CapabilitiesDump.cs:142`、`:240` | 新增 `classCollections` 段：dump `cardCollectionName` + `colorEs` + `type`，作为"占用表"的数据源 |
| `docs/ModKit-作者手册.md` | 同上：必须新增职业章节 |

### 7.3 API / 数据流

`ClassSpec` 不变，但**校验语义变了**：`OrbColor` 变成可选（面板/界面不再需要它），`Colors` 不再要求含 Black。数据流与原版完全一致：玩家拖 3 颗原版球 → `GetPickedColorES` → `GetClassNameByColorE` 命中我们的集合。

### 7.4 多职业与多 mod

≤ 10 − 原版占用数（原版职业页数报告 §3.4 记为 6 页，**需探针确认**）。三元组唯一性仍是硬约束。

### 7.5 风险与失效模式

| 风险 | 说明 |
|---|---|
| **动了原版开局手感** | `MustUse` 是原版设计（Purple/Void 强制入槽）；放开后原版职业也能被玩家改成别的组合 → 可能触发原版未覆盖的分支。**这是产品取舍，需拍板** |
| 原版职业可达性变化 | 原版 5 个球任意组合后，某些组合没有对应 Class 集合 → `GetClassNameByColorE` 返回 `InvalidClass` → `AscensionBox.Initialize("InvalidClass")` 不崩但显示回落等级 |
| 职业隔离失效 | 见 7.2 第 4 行；不处理会让 mod 卡出现在原版职业图鉴页 |
| 旧存档 | 已有存档的 `mainMenuSmallGameColorEs` 是 3 个原版色，若恰好等于某新职业三元组 → **老玩家进游戏直接变成新职业**。必须做"三元组占用表 + 启动期冲突告警" |

### 7.6 工时

**1–2 人日** + **1 局实机**（重点看：原版职业是否仍可正常选、老存档是否被劫持）。

### 7.7 验收

1. 原版界面 5 颗球全部可移动，任意 3 颗能凑成组合并确认；
2. 新职业三元组（如 Red+Blue+Green）→ 进入该职业、牌组正确；
3. 原版 5 个职业各自仍能正常开局（回归）；
4. **负例**：两个 mod 声明同一三元组 → 验证期 Error；
5. **回归**：老存档（无 mod 职业）读档后职业不变。

**kill criteria**：若放开 MustUse 后原版任一职业开局行为改变（牌组/遗物/介绍卡异常）→ 放弃 D，仅保留 A/C。

---

## 8. 排除项 E · 运行期扩充 `ColorE` 枚举 / 改游戏资产

| 做法 | 为什么不可行 |
|---|---|
| 改 `Assembly-CSharp` 或 `resources.assets` 加枚举成员 | 违反"平台全程只读游戏目录"（`DiskFileSource.cs:1-14`）与分发红线（`docs/Mod平台设计.md:281-287`）；Steam 校验直接报错，游戏更新即失效 |
| Harmony patch `Enum.GetValues` / `Enum.IsDefined` 伪造第 7 个值 | 游戏代码里的 `switch (colorE)` 被编译成常量比较/跳转表，拦不住；`GetAllBasicColorEArray` 本身就是 `new ColorE[5]` 硬编码（`DitwUtil.cs:642-650`） |
| 用 `ColorE.Black + 1`（未定义值）当第 7 色 | 未定义值能强制转换，但会命中所有 `default`：`InitializeOrb` 的 `switch` 落空 → 球不摆；`ManaIconDic` 无此键 → 费用渲染崩；存档序列化把未定义值写进档案 → 读档不可预测 |
| 加第二个 mod 程序集定义同名 `Card.ColorE` | 枚举是**值类型 + 类型标识**，同名程序集代码不改变 `Assembly-CSharp` 里的类型 → 传进游戏 API 时无法转换 |

**结论：写进文档的唯一目的是防止重复尝试。** 四条路线里没有一条需要碰游戏资产。

### 8.1 近亲反例：把**原版颜色套皮**成新颜色（同样是排除项）

这是最容易被想到的一条："不用 Black，把绿色那颗球换成黄色贴图/名字，就多了一个黄色"。**它比 E 更隐蔽，也更糟**，因为它看起来能用，实际必然自毁。三条机制原因：

1. **一个枚举值只产生一颗球**。`CalculateColorEAndStates` 每个颜色回一条状态（`SubSmallGameChooseColor.cs:266-294`），`InitializeOrbs` 按条目逐一 `Instantiate`（`:94-97`）⇒ 一个 `ColorE` = 一颗球 = 一个底座（`GetPedestalByColor` 按颜色取**第一个**，`:143-146`）= 一条贴图（`SetColorE` 按颜色匹配 `colorEToOrbSprites`，`ChooseColorEOrbForSubGame.cs:97-109`）= 一条 tooltip 键（`Orb_<ColorE>`，`:231`）。**套皮是"改名"，不是"复制"**——原来那颗球没有多出来，只是换了皮。
2. **颜色值是三色数组的键**。游戏的职业识别、开局牌组、卡池过滤、存档统计全部只读 `ColorE[]`：`GetClassNameByColorE`（`CardDataManager.cs:106-121`）、`GetStartDeckByColorE`（全等匹配，`SubSmallGameChooseColor.cs:187-197`）、`SetCampaignUsedColorE` → `SetCampaignPoolByUsedColorE`（`CampaignManager.cs:110-114`、`:90`）、存档统计回填（`SaveLoadManager.cs:250`）。数组里出现 `Green` 时，游戏只知道"绿色"，**没有任何字段能承载"黄色"这个意图**。你的判断完全正确：套皮后游戏无法区分"要原版绿"还是"要套皮黄"。
3. **套皮会吃掉被套的颜色，组合空间不增反减**。设颜色集合 S，职业 = S 的三元组，数量 C(|S|,3)。套皮不改变 |S| ⇒ 三元组数量不变；而被套的那个颜色（绿）在界面上已不存在，于是 `{紫, 黄, 绿}`、`{紫, 黄, 蓝}` 这类"既要黄又要绿"的组合**永远凑不出来**（黄只是绿的马甲，两者是同一个枚举值，不能同时占两个槽）。

| 做法 | 可选颜色数 | 三元组总数 | 原来那颗球还在吗 | 结论 |
|---|---|---|---|---|
| 原版（不干预） | 5（Red/Green/Blue/Purple/Void） | C(5,3) = **10** | — | 原版职业可占 5~6 组，余下是空位（需 Q-15 dump 确认） |
| **套皮原版色** | **5**（其中被套色只以新皮出现） | **10**（查表仍按被套色的值走） | **不在**（绿=黄，同一个枚举值，一颗球只能占一个槽） | **自毁**：组合不增、意图不可分辨、原职业被改名 |
| 占用 `Black`（A） | 6 | C(6,3) = **20** | 5 个原版球全在 | 可行，但原版界面只到 1 个职业（MustUse 锁 2 槽） |
| 面板 + 三元组（C） | 6 | 20 | 5 个原版球全在 | **可行且零歧义**：身份是三元组本身，不靠"球"承载 |
| 多颗同色球（B） | 6（同值多实例） | 键塌缩 | 需要重写 `InitializeOrb` | 只能在**颜色之外**另立 `OrbId`，否则数组键重复、查表全失配 |

**由此得到的两条原则**：

- **要增加职业数，扩"组合"，不要扩"颜色"。** 身份是三元组，三元组自带唯一性；给它换皮不产生新身份。
- **只有当"同一屏要出现两颗新颜色球"时才需要侧信道**，因为那时颜色值不够用，身份必须移出 `ColorE`——这正是 B 的代价来源，也是 C 用面板绕开它的原因。

---

## 9. 对比矩阵与推荐

### 9.1 矩阵

| 维度 | A | B | C | D |
|---|---|---|---|---|
| 职业数上限 | **1** | N（同色多实例，必须侧信道） | **≤10**（Black + 2 原版色） | ≤10 − 原版占用 |
| 玩家看到新球 | 是（第 6 颗黑球） | 是（N 颗同色球） | 是（面板内每职业一颗） | **否**（复用 5 球） |
| 补丁点数量 | 6（方法名）+ 1 私有字段 | 6–8 + `_orbs`/`pedestals`/`spriteRenderer` | **2**（已有）+ 面板自持 | **1** |
| 抗游戏更新 | 低（6 个方法名任一改即断） | 最低 | **较高** | **最高** |
| 改原版体验 | 无 | 中（同色球易混） | 无（界面被替换） | **有**（MustUse 放开） |
| 多 mod 共存 | 需报错 | 需注册表排序 | 抢注报错；**C+ 可真正共存**（同组三色不冲突） | 需三元组唯一 |
| 实机成本 | 1 局 | 2 局 + 存档往返 | 2 局 + 存档往返 | 1 局 + 老存档回归 |
| 工时 | 0.5 d | 5–7 d | 3–5 d | 1–2 d |
| 现有完成度 | 代码齐全（关断） | 视觉层已有 1 颗 | 面板已有 60% | 无（需探针） |

### 9.2 推荐与顺序

1. **第一步（本周可做，无实机依赖）**：给 `CapabilitiesDump` 加 `classCollections` 探针（D 的前置，也是 C/B 的校验数据源）——**任何方案都需要知道原版占用了哪几个三元组**。
2. **主线 C**（3–5 天）：已建成 60%，依赖面最小，唯一能做"多职业 + 新球观感"的组合。
3. **紧接着做 C+**（3–4 天，§6.8.2 的 4 个补丁）：否则 C 的 10 组三元组是**全局稀缺资源**，第 11 个作者无组可用、两个 mod 撞组时还会出现"名字来自 A、牌组来自 B、卡池 A+B"的三重错位。C+ 把身份移到面板选择上，颜色降级为主题，冲突问题消失。
4. **并行 D**（1–2 天，需拍板）：如果目标只是"多几个职业"，D 比 C 更快；代价是改 MustUse。
5. **A 作为降级预案**（0.5 天）：C 的面板若被判定过重，A 至少保证"1 个职业能用"。
6. **B 暂缓**：只有当 C 的"接管界面"被否、又必须要"原版界面 + 多颗球"时才启动。

**三条路线共用的前置件**（不论选哪条都要做）：

- 装配期**三元组唯一性**校验（防 R-07 同型静默冲突；C+ 上线后降级为提示）；
- `ClassSpec.OrbColor` 的语义重写（物理球颜色 vs 图标底色）+ 校验放宽（C6）；
- 作者手册补「职业」章节，并写清"`Colors` 在 C+ 之后只是主题，不是身份"。

---

## 10. 对作者可见契约的影响

| 方案 | `Colors` 约束 | `OrbColor` 约束 | `OrbSprite` / `BallScale` | 作者需要知道的新事实 |
|---|---|---|---|---|
| A | 恰好 3 个，且必须 = `{Black, Purple, Void}` | 必须 `Black` | 物理球贴图 / 大小 | 一个存档只能有一个 mod 职业 |
| B | 恰好 3 个，含 Black | `Black`（仅物理色） | 物理球贴图 / 大小 | 界面里会出现 N 颗黑球，靠标签区分 |
| C | 恰好 3 个，任意 | **图标底色**（可为任意色） | 面板图标 | 选职业在自建面板里，不在原版选球界面 |
| D | 恰好 3 个，全原版色也行 | 可选 / 忽略 | 忽略 | 原版球状态变了（Purple/Void 可移动） |

`ClassSpec` 的七个字段（旧树 `ModKit.Api/ClassApi.cs:21-77`）：`Id`、`NameZh`、`IntroZh`、`Colors`、`StartDeck`、`OrbColor`、`OrbSprite`、`BallScale`、`PoolCards`、`StartRelics`——其中 `StartRelics` 至少 2 件是硬约束（介绍卡硬取 `relicSos[1]`，`CareerAssembler.cs:96-101`），四个方案都不变。

**API 变更只能在 B-01 修复之后落地**：`ClassApi.cs` 现在不在仓库里，改注释/加字段都无处可改。

---

## 11. 开放实验项（每条都写清"怎么算有结论"）

| 编号 | 问题 | 为什么现在定不了 | 实验与判据 |
|---|---|---|---|
| **Q-11** | 跳过 `LeaveSubSmallGame` 后，`AscensionBox.Initialize(classKey)` 是否必须由我们补调？ | `LeaveSubSmallGame:155` 调它，C 绕开了这一步 | C 落地时先不补调，进界面看升魔盒显示的职业等级；若显示的是上个职业 → 必须补调 |
| **Q-12** | 放开 `MustUse` 后原版 5 个职业是否仍完整可用？ | 没有实测数据；`MustUse` 参与 `SetColorsOnMainMenu` 的默认填充（`SmallGameMainMenu.cs:244-253`） | 逐个原版职业开局一次，比对牌组/遗物/介绍卡；任一异常 → D 的 kill criteria 触发 |
| **Q-13** | 多职业在存档往返后是否稳定？ | `StartColorESDecks` 挂在 `CareerManager.GetCareerInformationForInitialize()` 的返回对象上，是否随存档序列化**未取证** | 选新职业 → 开局 → 存档 → 退出游戏 → 重进 → 继续：职业名/牌组/图鉴是否仍对 |
| **Q-14** | 反编译基线与发行 DLL 是否同版本？ | 报告 E-07 记录了四个不同的 `ModKit.Game.dll` | 实机前先 `grep -c` 类名/方法名与反编译对照（6 个补丁点 + 4 个反射字段名）；任一不符 → 先对齐基线再动手 |
| **Q-15** | 原版占用了哪几个三元组？ | 无 dump；`_colorCardDateCollections` 是运行期数据 | D 的探针产出 `classCollections` 后，按 `type=Class` 过滤统计；这也是 A/B/C 的冲突检测输入 |
| **Q-16** | C+ 的三个有效补丁（a/b/c）是否足够，`LoadCampaignInfo` 恢复 `ChosenSpec`（d）是否必须？ | 代码静态阅读只能证明"d 在 `ChosenSpec` 为空时必要"，但读档链路是否真的会重算 className **未取证** | 按 §6.8.4 的第 2、5 条做：先不打 d，存档→退出→重进→继续，若职业名/卡池变成另一个 mod 的 ⇒ d 必要；再补 d 复验 |

---

## 12. 附录 · 定位表

| 主题 | 仓库内（可改） | 反编译（只读证据） |
|---|---|---|
| 第 6 颗球的 6 个挂钩 | `src/ModKit.Game/CareerPatch.cs:151-224` | — |
| 球候选与状态 | `CareerPatch.cs:263-283` | `SubSmallGameChooseColor.cs:266-294`、`DitwUtil.cs:642-650` |
| 底座/贴图补齐 | `CareerPatch.cs:298-363`、`CareerOrbDriver.cs:330-382` | `SubSmallGameChooseColor.cs:143-146`、`ChooseColorEOrbForSubGame.cs:89-109` |
| 球的状态语义 | `CareerPatch.cs:138`、`CareerAssembler.cs:286-290` | `ColorEState`：Locked/Free/MustUse；`ChooseColorEOrbForSubGame.cs:116-131` |
| 职业识别 | `CareerAssembler.cs:67-101` | `CardDataManager.cs:80-121` |
| Class 集合写入 | `CareerAssembler.cs:158-213` | `CardDateCollection`（`type=Class`、`colorEs`、`startDeck`、`startRelic`） |
| 开局牌组匹配 | `CareerAssembler.cs:250-262` | `SubSmallGameChooseColor.cs:187-197`（全等匹配） |
| 自建面板与确认 | `CareerPicker.cs:150-389`、`:504-534` | `SmallGameMainMenu.cs:65`、`:228`、`:244-253` |
| 强切回房间 | `CareerOrbDriver.cs:47-111` | `SmallGameMainMenu.cs:255-272`（`BackFromSubSmallGame` 的 DOTween 回调） |
| **三色数组的 5 个消费点** | `CareerPicker.cs:364-389`（写入） | `CardDataManager.cs:106-121`（职业名）、`SubSmallGameChooseColor.cs:187-197`（牌组全等）、`CampaignManager.cs:90`/`:110-114`（卡池）、`CampaignManager.cs:74`/`:219`（统计）、`SaveLoadManager.cs:250`（读档回填） |
| 升魔盒 | 待补（C 方案） | `SmallGames/AscensionBox.cs:71-82`、`:220-253` |
| 能力表导出 | `src/ModKit.Game/CapabilitiesDump.cs:142`、`:240` | `capabilities.json`（`availableCostColors` 6 色） |

**引用纪律**：反编译路径一律相对 `G:\deepseek space\diceomancermod\research\decompiled\Assembly-CSharp\`；仓库内路径相对 `G:\Modkit\`。反编译文件**不作为可分发物**，仅作设计证据（报告 R-12）。

---

## 13. 附录 · 引用抽查

全文共 **114 处** `文件.cs:行` 引用（79 个不同位置）。定稿前逐条校验"文件存在 + 行号不越界"，并抽 10 处核对**行内内容**：

| # | 引用 | 抽查到的内容 | 结论 |
|---|---|---|---|
| 1 | `CareerPatch.cs:100` | `internal static readonly bool EnableCustomOrb = false;` | ✅ |
| 2 | `CareerPatch.cs:273` | `private static void AfterCalculateColorEAndStates(ref List<ColorEAndState> __result)` | ✅ |
| 3 | `CareerAssembler.cs:73` | 注释「这个职业要占用的"新球"…平台只校验它确实是原版没做成球的颜色」（74-81 即 `orbColor` 校验） | ✅ |
| 4 | `CareerAssembler.cs:216` | 注释「2.5 职业隔离：把该 mod 的「通用卡集合」的 colorEs 收紧成职业三色」 | ✅ |
| 5 | `CareerPicker.cs:383-386` | `GetMethod("RefreshPlayButton", …)` + `refresh.Invoke(mainMenu, null)` | ✅ |
| 6 | `CareerPicker.cs:504` | `[HarmonyPatch(typeof(SubSmallGameChooseColor), "IsAllSlotHasOrb")]` | ✅ |
| 7 | `CareerOrbDriver.cs:60` | `internal static void ForceSwitchBack(SubSmallGameChooseColor picker, string reason)` | ✅ |
| 8 | `SubSmallGameChooseColor.cs:187` | `private StartColorESDecks GetStartDeckByColorE(ColorE[] pickedArray)` | ✅ |
| 9 | `ChooseColorEOrbForSubGame.cs:116` | `private void OnMouseDown()`（117-123 即 `MustUse` 拒绝移动） | ✅ |
| 10 | `SmallGameMainMenu.cs:244` | `private void SetColorsOnMainMenu()`（245-253 即按 `MustUse` 补默认填充） | ✅ |

**抽查修正记录**：初稿有 2 处锚点偏移（`CareerPicker.cs` 的 `RefreshPlayButton` 调用、`SmallGameMainMenu` 的 `SetColorsOnMainMenu`），已按上表改为精确行号；其余 8 处首查即命中。

**未做的验证**：本文件所有"界面层"结论都来自代码/反编译**静态阅读**，没有一局实机运行；因此第四节起每处"待实机"标记都必须在落地时补日志证据（判据见各方案 4.7/5.7/6.7/7.7）。
