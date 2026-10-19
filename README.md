# 骰子浪游者 ModKit

[![最新版本](https://img.shields.io/github/v/release/Kirisor/DiceomancerModkit?label=%E6%9C%80%E6%96%B0%E7%89%88%E6%9C%AC&sort=semver)](https://github.com/Kirisor/DiceomancerModkit/releases/latest)
[![下载量](https://img.shields.io/github/downloads/Kirisor/DiceomancerModkit/total?label=%E4%B8%8B%E8%BD%BD)](https://github.com/Kirisor/DiceomancerModkit/releases)
[![许可](https://img.shields.io/github/license/Kirisor/DiceomancerModkit?label=%E8%AE%B8%E5%8F%AF)](LICENSE)

DICEOMANCER（骰子浪游者）的 mod 内容平台：用 **JSON 声明内容**（卡牌 / 遗物 / 关键词），需要特殊行为时再写少量 C#；配套图形化编辑器 **ModKit Studio**。

---

# ⬇ 下载

| 我要… | 下载 | 说明 |
|---|---|---|
| **玩别人做的 mod** | **[ModKit-Platform-0.2.2.zip](https://github.com/Kirisor/DiceomancerModkit/releases/download/v0.2.2/ModKit-Platform-0.2.2.zip)** | **150 KB · 必装**：平台本体（4 个 dll） |
| **自己做 mod** | **[ModKit-Studio-0.2.2-win-x64.zip](https://github.com/Kirisor/DiceomancerModkit/releases/download/v0.2.2/ModKit-Studio-0.2.2-win-x64.zip)** | **55 MB · 双击即用**：编辑器，自包含，不用装 .NET |
| **参考示例** | **[Example-Mod-0.2.2.zip](https://github.com/Kirisor/DiceomancerModkit/releases/download/v0.2.2/Example-Mod-0.2.2.zip)** | 553 KB：13 张卡 + 2 遗物 + 关键词 + 文案 |
| **核对完整性** | [SHA256SUMS.txt](https://github.com/Kirisor/DiceomancerModkit/releases/download/v0.2.2/SHA256SUMS.txt) | 全校验和 |

> 做 mod 的人：**平台包 + Studio 包**都要下（平台包让内容能在游戏里生效）。
> 全部历史版本见 [Releases](https://github.com/Kirisor/DiceomancerModkit/releases)。

前置：[BepInEx 5.4.23.2 x64](https://github.com/BepInEx/BepInEx/releases)，解压到游戏根目录（与 `Diceomancer.exe` 同级）。

> 懒人做法：装好 **ModKit Studio** 后用顶栏「**部署到游戏**」一键安装平台包（Studio 包里带了 BepInEx，会自动补装）。


---

## 安装（玩别人做的 mod）

1. 装好 BepInEx（见上）
2. 把 **平台包**里 `BepInEx\plugins\ModKit.*.dll`（**四个文件，缺一不可**）复制到 `<游戏根>\BepInEx\plugins\`
3. 把要玩的 mod 目录放到 `<游戏根>\BepInEx\DiceomancerMods\<modid>\`
4. 启动游戏

验证：`BepInEx\LogOutput.log` 里搜 `[ModKit]`，应出现 `mod 已加载：<modid>：卡 N / 遗物 M / 关键词 K`。

---

## 自己做 mod

1. 装好 BepInEx + 平台包，**启动游戏一次**（自动生成 `BepInEx\ModKitDump\` 字典数据）
2. 解压并双击 `ModKit.Studio.exe`，首次启动选一下游戏根目录
3. 新建 / 编辑卡牌、遗物、关键词 → **保存**（自动同步文案）
4. 需要 C# 行为时：关键词页「生成 / 载入行为类」→ 写代码 → 顶栏「编译本 mod」
5. **重启游戏** → 看日志 → 把卡打出去验证

> 只有写 C# 时才需要额外安装 .NET SDK；纯 JSON 内容不需要。

文档（也随 Studio 包附带）：

- [作者手册](docs/ModKit-作者手册.md) —— 字段、写法、报错速查
- [Studio 使用手册](docs/ModKit-Studio使用手册.md) —— 编辑器怎么用
- [效果字典](docs/ModKit-效果字典.md) —— 效果 / 关键词 / 触发点 / 能力名单
- [变更记录](CHANGELOG.md)

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
