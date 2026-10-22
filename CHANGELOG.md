# 变更记录

## 未发布

### 平台修复

- **初始牌组点名原版卡会变成「空白卡牌」**：游戏的 `CardDataManager.GetCardSoByName`
  查不到时返回 `EmptyCard`（空白卡牌）而不是 `null`，平台按 `null` 判断"没找到"，
  于是"按原版卡名兜底"那一步永远不执行 —— 初始牌组里点名原版卡（例：至尊骰 `Dice`）
  进战斗就成了一张空白卡。判定改为"名字必须对得上"。
- 同一陷阱一并修掉：`CustomEffectRuntime.FindCardSo`（`give_card` 会静默发空白卡）、
  `ContentAssembler` 的 `artTemplate` 模板查找（模板写错会拿空白卡当模板）。
- **能力描述键**：`AbilityDescription` 的键是 `<能力全名>`（平台自动补 `_Des`），
  写裸 id 会显示 `No translation found for '<mod>.<ability>_Des'`；文档已注明。

### 示例 mod：`manual_demo` → `naifrog`（奶蛙）

- mod id / 显示名 / 程序集/目录全部改为 `naifrog` / 奶蛙 / `naifrog.dll` /
  `examples/naifrog`；打包脚本产出 `Naifrog-Mod-<mod 版本>.zip`。
- 修「蛙母的选择」「蛙母的全权」的能力描述键（补 mod 前缀），中文悬浮提示不再报缺翻译。
- 蛙群瞪视重写：**原版效果**对所有敌人造成 4 点伤害 + 「被奶蛙结算时」8 层易伤；
  新增 `MilkFrogCascade` 触发点模块（连锁结算命令开合窗口，任意卡可继承
  `CascadeTriggeredEffect` 共用），替换掉原来的静态开关。
- 文案表按现有内容整理（删掉已删卡/遗物的死键），补英文文案并启用 `i18n/en.json`。

## v0.2.3-studio

### 修复

- **卡面图路径**：Studio 保存卡牌时把卡面图写成了裸文件名，而平台的 Loader 按
  **相对 mod 目录的路径**查文件 ⇒ 整张卡被拒。现在几种写法统一归一化为
  相对 mod 目录的 `.png` 路径，保存前校验文件存在，美术下拉框也改为列出相对路径。
- **关键词面板**：「添加」按钮在半栏宽的布局里被挤出可视区域，导致选好关键词后
  无法添加。顶部布局改为 `WrapPanel`，空间不足自动折行。

## v0.3.0

自定义颜色：平台把颜色从"游戏硬编码的 6 种"扩展为 **6–15 的开放空间**，颜色内容由 mod 声明。

### 新增

- **颜色声明（mod 侧 `colors.json`）**：`value`（6–15）/ `name` / `desc` / `color` / `icon` /
  `alias` / `aliases`。平台注册后注入游戏的取色、亮色、法力贴图、`AssetRefSo` 两本字典与
  `ManaManager.ManaIconDic`（每个战场初始化后重注入），并写入图鉴文案。
- **颜色别名解析下沉 Core**：`ManaColorNames.Resolve` 认识别名（如 `"Yellow"`），
  因此卡牌、遗物、卡池、效果参数的 `colors` / `cost` 都可直接写别名，校验器同样认。
- **卡面流派色独立材质槽**：一张卡可带多个自定义色，不再借用原版 Black 槽。
- **战役存档编解码**：`CampaignSave.xml` 走 DataContract（枚举按名字写），自定义色会被换成
  Void 占位并把路径写入 `CampaignSave.colors.json`（sidecar），读档时还原。
- 法力/费用图标按原版 sprite 的世界尺寸对齐 PPU（此前 72px 贴图会被 `SetNativeSize` 撑大）。

### 修复

- 卡牌转色：`ManaConverter.GetPossibleColorEs` 原本写死 5 色白名单，自定义色卡无法被转换。
- 法力不足提示：`DitwUtil.GetColorTextFromColorE` 对未定义枚举值抛
  `ArgumentOutOfRangeException`，现对自定义色返回空串。
- 费用校验：`ProbeColors` / `TryToPlatformColor` 对自定义色放行，否则带自定义费用的卡
  会被判"未注册图标键"并整张拒收。

### 已知边界

- `ModKit.Api` 的 `ManaColor` 枚举尚无 6–15 成员（Api 源码缺 `ClassSpec`，暂无法重建）；
  平台内部以数值强转，作者侧无感知。
- 自定义色的**球位**不会出现在原版选球界面；职业需由 mod 用 `IModClassProvider` 声明，
  通过平台自建的选择器进入。


## v0.2.2

### 修复

- **示例 mod 缺少 `manual_demo.dll` 与 `src\`**：导致 `mod.json` 里的 `entryAssembly` 指向不存在的文件、
  并且没有职业声明 ⇒ 职业球（「奶蛙」）不会出现。现示例包同时包含 dll 与源码。
- 示例包打包时不再删除 `*.dll`（只剔除 `.bak` / `.pdb`），缺少 dll 时会打印提示。

## v0.2.1

### 修复

- **一键部署把 `core` 装错位置**：0.2.0 会把 BepInEx 的 `core\*.dll` 平铺到游戏根目录，
  导致游戏根本启动不了 BepInEx。现改为按目标布局安装：
  `payload\bepinex\root\` → 游戏根（`winhttp.dll`、`doorstop_config.ini`）、
  `payload\bepinex\BepInEx\` → 游戏根 `BepInEx\`
- 新增自愈：检测到游戏根下有错位的 `core\BepInEx.dll` 时，自动并入 `BepInEx\core\`
  （0.2.0 部署过的用户再点一次「部署到游戏」即可修好）

## v0.2.0

### 新增

- **一键部署**：Studio 顶栏「部署到游戏」把 BepInEx（随包）、平台四件套、当前 mod 一次装好
  - 游戏运行时拒绝部署；覆盖前备份到 `.modkit-bak\`；内容一致时跳过
  - 部署结果逐项显示「安装 / 更新 / 跳过 / 失败」
- Studio 包携带 `payload\`（平台 dll + 配置；`WITH_BEPINEX=1` 时含 BepInEx 5.4.23.2）

### 变更

- 版本号提升到 0.2.0

## v0.1.0

首个可发布版本。

### 平台

- JSON 内容契约：卡牌 `modkit/card@1`、遗物 `modkit/relic@1`、关键词 `modkit/keyword@1`、文案 `modkit/i18n@1`
- 26 个内置效果、32 个真实触发点、105 个原版能力、6 种费用颜色
- 自定义效果 `[ModEffect]`、自定义关键词（`CardKeyword` 子类）
- 模组图鉴：卡牌 / 遗物 / 关键词 / 能力四个标签页，卡牌用游戏自身渲染（局内、局外都能打开）
- 文案自动注入与自检、能力中文名与本地化表导出（供编辑器使用）

### ModKit Studio

- 卡牌 / 遗物 / 关键词三类内容的图形编辑，保存时同步 i18n
- 效果行按「模式 / 对象 / 数值」填写，不适用项自动隐藏
- 下拉项全部中文（触发点、稀有度、结算时机、能力、关键词），写盘仍为英文 id
- 关键词行为类内嵌编辑 + 一键编译（`dotnet exec csc.dll`，旧 dll 自动备份）
- 首次启动自动探测游戏目录（Steam 库 → 常见路径），可手动选择
- 自包含发布：玩家不需要安装 .NET

### 已知限制

- 卡面预览是近似效果，最终外观以游戏内为准
- Studio 的 C# 编译需要本机安装 .NET SDK（纯 JSON 内容不需要）
- 未内置一键安装器：BepInEx、平台 dll、mod 目录需按说明放置
