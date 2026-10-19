# 变更记录

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
