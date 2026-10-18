# ModKit Studio 使用手册

ModKit Studio 是 DICEOMANCER ModKit 的图形化编辑器：把内容写成表单，保存成平台认识的 JSON，并同步文案。

- 字段的**含义**与合法性规则见《ModKit-作者手册.md》；
- 效果 / 关键词 / 触发点 / 能力的**名单**见《ModKit-效果字典.md》；
- 本手册只讲**怎么用这个软件**。

---

## 一、能做什么、不做什么

**能做**

- 新建 / 编辑 / 删除 卡牌、遗物、关键词的 JSON
- 下拉选择效果、关键词、触发点、能力（中文名 + 英文 id）
- 编辑关键词的 C# 行为类
- 编译 mod 的 C# 部分，输出 `<modid>.dll`
- 保存时自动同步 i18n（`i18n/zh-Hans.json`，有英文名时同时写 `en.json`）
- 预览（近似卡面 / 文字）

**不做**

- 不改游戏本体文件
- 不自动启动游戏，也不做游戏内测试
- 预览不是最终渲染：卡面的真实外观以游戏内为准

---

## 二、启动与配置

### 2.1 首次启动

1. 运行 `ModKit.Studio.exe`；
2. 顶栏「mods 根目录」会显示自动探测到的位置（从 Steam 库清单里找 `DICEOMANCER`）；
3. 如果显示的位置不对，点「浏览…」，选**游戏根目录**（含 `Diceomancer_Data` 的那一层）；
   Studio 会自动补成 `<游戏根>\BepInEx\DiceomancerMods`；
4. 点「重新加载」，左栏出现该目录下的 mod 列表。

### 2.2 配置存在哪

```text
%APPDATA%\ModKitStudio\config.json
{
  "gameRoot": "……\\DICEOMANCER",
  "modsRoot": "……\\DICEOMANCER\\BepInEx\\DiceomancerMods"
}
```

首次探测成功后会自动写入，之后启动直接读。想手工改也可以，改完重启 Studio。

> 还没装 BepInEx 或平台 dll？装好 Studio 后可以用顶栏的「部署到游戏」一键完成，见 §十二。

### 2.3 找不到 mod 怎么办

| 现象 | 原因 | 处理 |
|---|---|---|
| 左栏空白 | `modsRoot` 指向的目录没有 mod | 确认 mod 目录放在 `BepInEx\DiceomancerMods\<你的 mod id>\` |
| 只有几个 mod | 游戏用的是别的根目录 | 检查 `BepInEx\config\diceomancer.modkit.cfg` 的 `[General] ModsRoot`，让 Studio 指向同一个地方 |
| 选完目录还是空 | 选错了层级 | 必须选含 `Diceomancer_Data` 的那一层，或直接选 `BepInEx\DiceomancerMods` |

---

## 三、界面

### 3.1 整体布局

| 位置 | 内容 |
|---|---|
| 顶栏 | mods 根目录输入框 +「浏览…」「重新加载」「编译本 mod」 |
| 左栏 | 模组下拉 → 内容类型（卡牌 / 遗物 / 关键词）→ 文件列表 →「新建」「删除」 |
| 中栏 | 当前内容的表单；底部「保存（含 i18n 同步）」「刷新预览」 |
| 右栏 | 预览：卡牌是近似卡面，关键词/遗物是文字 |
| 底部 | 状态提示与编译输出 |

### 3.2 内容类型

左侧三个按钮切换类型，列表只显示当前类型的文件：

- **卡牌** → `<mod>\cards\*.json`
- **遗物** → `<mod>\relics\*.json`
- **关键词** → `<mod>\keywords\*.json`

新建时会在对应目录生成一个模板文件，同时把路径写进 `mod.json` 的 `cards[]` / `relics[]` / `keywords[]`。

---

## 四、编辑卡牌

### 4.1 字段

| 表单字段 | 对应 JSON | 说明 |
|---|---|---|
| id | `id` | 小写下划线，3~41 字符 |
| 中文名 / 英文名 | `name` | 至少填一个 |
| 稀有度 | `rarity` | 普通（Common）/ 罕见（Uncommon）/ 稀有（Rare） |
| 结算时机 | `resolve` | 打出时 / 抽到牌时 / 被弃掉时 / 回合结束仍在手中 |
| 颜色 | `colors` | 卡牌归属颜色，最多 2 个 |
| 费用 | `cost` | 见 §4.2 |
| artTemplate | `artTemplate` | **必须是原版卡名**，填错保存会被拦下 |
| 卡面图 | `art` | 从 `<mod>\art` 里选，见 §4.5 |
| 关键词 | `keywords` | 见 §4.4 |
| 效果 | `effects` | 见 §4.3 |
| 升级增量 | `upgrade` | 见 §4.6 |
| 中文风味 / 英文风味 | `FlavorText` | 写进 i18n，不进卡牌 JSON |

### 4.2 费用写法

| 想表达 | 怎么写 |
|---|---|
| 1 个红槽 | `Red` |
| 2 个红槽 | `Red, Red` |
| 1 个"红或绿都行"的多色槽 | `[Green, Void]` |
| 2 个槽：一个红、一个多色 | `Red, [Green, Void]` |
| 无费用卡 | 留空 |

- 一对方括号 = **一个**槽，里面是候选颜色；
- 颜色只能是 `Red` / `Green` / `Blue` / `Purple` / `Void` / `Black`；
- 保存时旁边会回显解析结果；写错会直接报错，不会写坏文件。

### 4.3 效果行

1. 在下拉里选效果 —— 显示形式为 `中文名（英文 id）·需模式 / 需名称 / 需数值`；
2. 按提示填下面三项（不相关的会自动隐藏）：

| 项 | 何时出现 | 取值 |
|---|---|---|
| 模式 | 伤害类效果 | `Direct` / `Indirect` / `LoseLife` |
| 对象 | 需要指定目标的效果 | `Self` / `Opponent` / `AllOpponents` / `All` / `RandomOpponent` |
| 数值 | 大部分效果 | 整数 |

3. 点「添加」写入列表；
4. 需要的话用「上移 / 下移 / 删除」调整顺序 —— **顺序就是结算顺序**。

无法在表单里表达的写法（`pool` / `require` / `setter` 等复杂对象）会有提示，请用文本编辑器补 JSON；Studio 保存时会保留这些字段。

### 4.4 关键词

- 下拉里是原版关键词 + 本 mod 的关键词，显示 `中文（英文 id）·带数值`；
- 选带数值的关键词时，右侧「参数」框才可填；
- 点「新建关键词…」会立刻在 `<mod>\keywords\` 建一个关键词文件，自动切到「关键词」页并选中它。

### 4.5 自定义卡面

1. 把图片放到 `<mod>\art\<artTemplate>.png` 或 `<mod>\art\card\<artTemplate>.png`；
2. 重新「刷新预览」后，在「卡面图」下拉里能选到；
3. 只支持 `.png` / `.jpg` / `.jpeg`；建议正方形 256~512 px。

### 4.6 升级

展开「升级」区块可填：

- 每个效果的升级数值（按效果下标）
- 升级后的费用
- 升级后新增 / 移除的关键词

留空表示该卡不可升级。

---

## 五、编辑遗物

| 表单字段 | 对应 JSON | 说明 |
|---|---|---|
| id | `id` | 小写下划线 |
| 中文名 / 英文名 | `name` | |
| artTemplate | `artTemplate` | **必须是原版遗物名** |
| 触发点 | `trigger` | 32 个真实触发点，显示「中文（英文）」 |
| 效果 | `effects` | 与卡牌同一套写法 |
| 进随机池 | `addToPool` | 缺省 true |

选了需要附加字段的触发点后，表单会多出对应输入项：

| 触发点 | 出现字段 |
|---|---|
| `ChangeBattlePhase` | 阶段（`Upkeep`/`Main`/`End`/`CleanUp`）+ 是否阶段开始 |
| `WholeNewTurnStart` | 是否限定回合 + 回合数 + 关系（`Before`/`Exactly`/`After`） |
| `EnemyHealthBarBreak` | 相对角色（`Owner`/`Opponents`/`All`） |
| `ManaConsumed` | 颜色 |
| `CardKeywordTrigger` / `AbilityStandardTrigger` / `TakeRest` | 附加字符串（如能力名） |

---

## 六、编辑关键词

### 6.1 字段

| 表单字段 | 对应 JSON |
|---|---|
| id | `id` |
| 中文名 / 英文名 | `name` |
| 中文说明 / 英文说明 | 写进 i18n 的 `KeyWord` 表（`<id>Description`） |
| 带数值 | `hasNumber` |

### 6.2 行为类（C#）

关键词本身只是标签，**真实行为要写在 `CardKeyword` 子类里**：

1. 选中关键词；
2. 点「生成 / 载入行为类」：
   - 已经存在 `src/CardKeyword<Id>.cs` ⇒ 载入到编辑框；
   - 不存在 ⇒ 生成模板并载入；
3. 在编辑框里改代码，点「保存行为类」（旧文件自动备份成 `.时间戳.bak`）；
4. 点顶栏「编译本 mod」编成 dll。

类名规则：`id` 的下划线分段各自首字母大写，前面加 `CardKeyword`。
例：`milk_frog` → `CardKeywordMilkFrog`。

---

## 七、保存与文案同步

点「保存（含 i18n 同步）」时 Studio 会：

1. 校验必填字段与取值（例如 `artTemplate` 是否在原版名单里）；
2. 写回内容 JSON（UTF-8 无 BOM，保留 Studio 不认识的字段）；
3. 同步文案到 `<mod>\i18n\zh-Hans.json`：
   - 卡牌名 → `CardName`，键 `<modid>.<卡id>`
   - 遗物名 → `Relic`，键 `<modid>.<遗物id>`
   - 关键词名 / 说明 → `KeyWord`，键 `<关键词id>` / `<关键词id>Description`
   - 风味文本 → `FlavorText`
4. 填了英文名时同时写 `i18n\en.json`。

保存失败会提示原因，不会写半成品文件。

---

## 八、编译 C#

### 8.1 用按钮

顶栏「编译本 mod」：把 `<mod>\src\*.cs` 编成 `<mod>\<modid>.dll`

- 旧 dll 自动备份成 `<modid>.dll.时间戳.bak`
- 成功 / 失败都显示在底部状态栏；失败会显示前几行编译器输出
- 找不到 .NET SDK 时会提示「纯数据 mod 不需要编译」

### 8.2 用命令行

```bash
bash studio/build-mod.sh <modId> [mods 根目录]
# 例：bash studio/build-mod.sh manual_demo
```

### 8.3 常见编译错误

| 报错 | 原因 | 处理 |
|---|---|---|
| `CS0246 未能找到 HarmonyLib` | 缺 `0Harmony.dll` 引用 | 命令行脚本已带；不要在 Studio 外手工拼 csc 参数 |
| `CS0246 未能找到 UniTask` | 缺 `UniTask.dll` 引用 | 同上 |
| `CS1503 / CS1061` | 游戏 API 签名变了 | 对照反编译源码改代码 |
| `找不到 .NET SDK` | 机器没装 .NET SDK | 只有写 C# 才需要；纯 JSON 内容不受影响 |

---

## 九、进游戏验证

1. 确认 mod 目录在 `mods 根目录` 下；
2. **重启游戏**（内容只在启动时装配一次）；
3. 看 `BepInEx\LogOutput.log` 里搜 `[ModKit]`：
   - `mod 已加载：<modid>：卡 N / 遗物 M / 关键词 K` ⇒ 加载成功
   - 有 `[Error]` ⇒ 按《作者手册》§十 的报错速查处理
4. 把卡拿到手上并打出去 —— 卡面正确不等于逻辑正确。

调试用：`diceomancer.modkit.cfg` 设 `[Diagnostics] GrantOnFirstBattle = true`，第一场战斗会直接把 mod 内容发给你。

---

## 十、常见问题

| 问题 | 处理 |
|---|---|
| 保存时说 `artTemplate 不在原版名单里` | 那是原版卡名，不是你的图片名；自定义图放 `art` 字段 |
| 卡在游戏里看不到 | 先看日志有没有加载错误；再看 `mod.json` 是否收录了该文件 |
| 关键词有标签但没效果 | 缺 C# 行为类，或类名不对（§6.2） |
| 文案不生效 | 表名写错（必须用 27 个表名之一）或键少了 `<modid>.` 前缀 |
| 文案里有 `{` | 平台不支持占位符，会弹格式化异常，改成纯文字 |
| 改了 JSON 但游戏没变 | 没重启游戏 |
| 编译成功但游戏行为没变 | 没重启游戏，或游戏用的是另一个 mod 根目录 |
| 预览和游戏里不一样 | 预览是近似卡面，游戏内才是最终效果 |

---

## 十一、和手写 JSON 的关系

- Studio 写出的就是《作者手册》里描述的同一种 JSON，两者可以混用；
- 手写的复杂字段（`pool` / `require` / `setter` / 自定义 `upgrade` 结构）Studio 不会删，只会在表单里显示为不可编辑；
- 想用外部编辑器直接改也没问题 —— 改完在 Studio 里点「重新加载」即可看到。

---

## 十二、一键部署到游戏

顶栏「部署到游戏」把平台装进游戏目录，省掉手工复制。它做四件事：

1. **BepInEx**：游戏目录里没有 `BepInEx\core\BepInEx.dll` 时，从 Studio 包自带的 `payload\bepinex\` 安装
2. **平台四件套**：把 `ModKit.Api/Core/Game/Plugin.dll` 复制到 `<游戏根>\BepInEx\plugins\`
3. **配置**：`diceomancer.modkit.cfg` **只在缺失时**写入，不覆盖你已改过的开关
4. **当前 mod**：把左侧选中的 mod 复制到游戏真正读的 mods 根目录

规则：

- **游戏在运行时会拒绝部署**（dll 被占用会写坏）——先关游戏再点
- 覆盖任何文件前都会备份到 `.modkit-bak\`（在 `plugins\` 或 `DiceomancerMods\` 下）
- 内容一致时自动跳过，重复点击不会产生多余备份
- 部署结果显示在底部状态栏下方，逐项标注「安装 / 更新 / 跳过 / 失败」

失败处理：

| 提示 | 处理 |
|---|---|
| 这个版本不含部署文件 | 你用的是不含 `payload\` 的包，请下载完整 Studio 包 |
| 检测到未安装 BepInEx，且当前包未携带它 | 先手工安装 BepInEx 5.4.23.2 x64 |
| 没有写权限 | 游戏装在 Program Files 时，以管理员身份运行 Studio |
| 游戏正在运行 | 关闭游戏后重试 |

部署完成后**重启游戏**生效；验证看 `BepInEx\LogOutput.log` 里的 `[ModKit]` 行。
