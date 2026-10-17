# DiceomancerModkit

DICEOMANCER 的 mod 内容平台：用 **JSON 声明内容**（卡牌 / 遗物 / 关键词），需要特殊行为时再写少量 C#；配套图形化编辑器 **ModKit Studio**。

当前版本：**0.1.0**

---

## 我要玩别人做的 mod

下载 **`ModKit-Platform-<版本>.zip`**（约 150 KB）。

前置：先装 [BepInEx 5.4.23.2 x64](https://github.com/BepInEx/BepInEx/releases)，解压到游戏根目录（与 `Diceomancer.exe` 同级）。

安装：

1. 把包里的 `BepInEx\plugins\ModKit.*.dll`（**四个文件**）复制到 `<游戏根>\BepInEx\plugins\`
2. 把要玩的 mod 目录放到 `<游戏根>\BepInEx\DiceomancerMods\<modid>\`
3. 启动游戏

验证：`BepInEx\LogOutput.log` 里搜 `[ModKit]`，应出现 `mod 已加载：<modid>：卡 N / 遗物 M / 关键词 K`。

---

## 我要自己做 mod

需要三样：

| 下载 | 用途 |
|---|---|
| `ModKit-Platform-<版本>.zip` | 平台本体（**必须**，否则做的 mod 没地方生效） |
| `ModKit-Studio-<版本>-win-x64.zip` | 编辑器（自包含，**不用装 .NET**，双击即用） |
| `Example-Mod-<版本>.zip` | 示例 mod，可直接放进游戏对照 |

流程：

1. 装好 BepInEx + 平台包，启动游戏一次（会自动生成 `BepInEx\ModKitDump\` 的字典数据）
2. 打开 ModKit Studio，首次启动选一下游戏根目录
3. 新建 / 编辑卡牌、遗物、关键词 → **保存**（自动同步文案）
4. 需要 C# 行为时：关键词页「生成 / 载入行为类」→ 写代码 → 顶栏「编译本 mod」
5. **重启游戏** → 看日志 → 把卡打出去验证

> 只有写 C# 时才需要额外安装 .NET SDK；纯 JSON 内容不需要。

文档：

- [作者手册](docs/ModKit-作者手册.md) —— 字段、写法、报错速查
- [Studio 使用手册](docs/ModKit-Studio使用手册.md) —— 编辑器怎么用
- [效果字典](docs/ModKit-效果字典.md) —— 效果 / 关键词 / 触发点 / 能力名单

---

## 仓库结构

| 路径 | 内容 |
|---|---|
| `src/ModKit.Api/` | 对外契约（枚举、内容定义、效果上下文），零游戏依赖 |
| `src/ModKit.Core/` | 解析 / 校验 / 注册表 |
| `src/ModKit.Game/` | 唯一引用游戏程序集的层（装配、补丁、图鉴） |
| `studio/` | ModKit Studio（WPF），经 `contracts/` 直接编译 Api+Core 源码 |
| `schemas/` | 内容 JSON 的 schema（card / relic / i18n @1） |
| `docs/` | 作者手册、Studio 手册、效果字典、开发者参考 |
| `lib/` | 构建所需的第三方程序集（Newtonsoft.Json，MIT） |

## 构建

```bash
bash studio/build.sh                        # 契约库 + Studio
bash studio/build-mod.sh <modId> [mods根]    # 编译某个 mod 的 C#
SELF_CONTAINED=1 bash studio/pack.sh 0.1.0  # 打发布包（平台 / Studio / 示例）
```

插件侧按 net472 编译（BepInEx / Mono 要求）；Studio 侧用 net10 契约库 —— 两边编译同一批源码，枚举与校验规则不会漂移。

> 自包含打包会从 NuGet 取 .NET 运行时包；若网络受限，`studio/pack-sc-offline.sh` 用 curl 直下再本地还原。

## 许可

本仓库代码为 MIT。BepInEx 为 LGPL-2.1（其 `LICENSE` / `NOTICE` 随平台包分发）。

仓库与发布包**不含任何游戏素材**：卡面图属游戏版权，平台运行时也不落地。
