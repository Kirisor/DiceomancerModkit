# DiceomancerModkit

DICEOMANCER 的 mod 内容平台：JSON 声明内容 + C# 行为，配套图形化编辑器 **ModKit Studio**。

## 目录

| 路径 | 内容 |
|---|---|
| `src/ModKit.Api/` | 对外契约（枚举、内容定义、效果上下文），零游戏依赖 |
| `src/ModKit.Core/` | 解析 / 校验 / 注册表 |
| `src/ModKit.Game/` | 唯一引用游戏程序集的层（装配、补丁、图鉴） |
| `studio/` | ModKit Studio（WPF 编辑器），经 `contracts/` 直接编译 Api+Core 源码 |
| `schemas/` | 内容 JSON 的 schema（card / relic / i18n @1） |
| `docs/` | 作者手册、效果字典、素材索引等 |
| `lib/` | 构建所需第三方程序集（Newtonsoft.Json，MIT） |

## 构建

```bash
bash studio/build.sh          # 构建契约库 + Studio
bash studio/build-mod.sh      # 编译某个 mod 的 C# 部分
```

插件侧仍按 net472 编译（BepInEx / Mono 要求）；Studio 侧用 net8.0 契约库 —— 两边编译同一批源码，所以枚举与校验规则不会漂移。
