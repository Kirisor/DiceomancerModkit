# 变更记录

## 奶蛙 mod 0.5.0（独立发行版 `naifrog-v0.5.0`）

> 独立于平台版本号发行；**需要平台 0.4.1 及以上**（遗物的稀有度 / 职业专属池 /
> 卡牌过滤 / 触发上限是 0.4.1 才有的能力，旧平台会把这些字段忽略掉）。
> 内容规模：卡 41 张（职业卡池 34 / 初始牌组 10 / 衍生 6）＋ 遗物 4 ＋ 关键词 2 ＋ 能力 4。

### 新增遗物（绿黑组 · 职业专属）

三件都带 `poolColors: ["Purple","Green","Black"]` ⇒ 只进奶蛙的遗物池，其它职业刷不到
（原版 15 个色组专属遗物集合的结构：每组 3 件、1 普通 + 1 罕见 + 1 传说）。

- **奶■**（普通）：敌人的生命值被击破时，结算一张奶蛙牌（手牌优先，其次抽牌堆）。每回合 1 次。
- **微型奶蛙**（稀有）：你打出奶蛙牌、结算完成时抽 1 张。每场战斗 3 次。
- **奶妈**（传说）：打出奶蛙牌时检定 10（D20 + 修正）；成功则该牌的**全部数字**在本场战斗内 +1。

### 职业池

- 战役池从 52 条点名列到 **58 条**（34 张 mod 卡 + 24 张原版卡）。
  去重与实际可刷出张数由平台侧保证：本局共 71 张不同卡（34 mod + 37 原版）。
- 新增 6 张黑球系疫病卡进池：乌黑触手、幻象、灾厄、不祥黑雾、折磨、祭坛
  （精神污染 / 黑洞 原本就在池中，未重复列入）。疫病牌的唯一机制副作用是「不能被转换为能量」。

### 平台侧修复（0.4.1，同时生效）

- 战役池按 `CardSo` 去重：mod 卡此前会在池里出现两次（职业集合 + 通用集合各算一次），掉率翻倍。
- 初始牌组里的打击/格挡不再进随机池（判据：在 StartDeck 而不在 PoolCards）。
- 遗物装配不再继承模板的多余效果表与充能数。

## 奶蛙 mod 0.3.2（独立发行版 `naifrog-v0.3.2`）

> 独立于平台版本号发行；需要平台 0.4.0 及以上的 ModKit 平台。
> 内容规模：卡 41 张（职业卡池 22 / 初始牌组 10 / 衍生 6）＋ 遗物 1 ＋ 关键词 2 ＋ 能力 4。

### 新增卡牌（12 张）

- **奶蛙王朝**（`mf_dynasty`）：获得「奶家人 +3（含次数）」—— 之后每次召唤奶家人，
  生命与效果数字都加上能力层数×3。能力层数可叠加。
- **应奶尽奶**（`mf_all_summons`）：让场上**所有**召唤物按各自生命各结算一次（终结技，不设上限）。
- **笑到最后**（`mf_settle_all`）：本次「奶蛙结算」把抽牌堆与弃牌堆里所有奶蛙卡再结算一遍；
  这些结算本身也算奶蛙结算，因此同一张卡可能结算两次。
- **圣奶蛙降临大地**（`mf_holy_frog`）：治疗 4 ＋ 奶蛙 3（纯 JSON）。
- **生母之惧**（`mf_mother_fear`）：无力、脆皮、目盲各 2 层 ＋ 易逝。
- **奶之哀**（`mf_mourning`）：伤害 5 × 4 次（游戏原生 MultiDamage）＋ 自身 4 层易伤。
- **黄桃罐头**（`mf_peach`）：格挡 6；被奶蛙结算时给 6 层加固。
- **豆奶之恋**（`mf_soy_love`）：把目标的**流血**层数换成等量**燃烧**。
- **豆奶之殇**（`mf_soy_ruin`）：移除所有敌人的减益，按移除的总层数造成失血（黑结局）。
- **豆奶之誓**（`mf_soy_vow`）：移除所有敌人的减益，按总层数随机获得增益（绿结局）。
- **呼叫奶家人**（`mf_call_family`）：看抽牌堆顶 3 张，数其中绿色牌，然后抽 3 张、每数到 1 张绿色回 1 能量。
- **奶蛙存在吗？**（`mf_exist`）：获得能力「奶蛙存在时抽 1 弃 1」—— 之后每张奶蛙结算完触发一次。

豆奶之殇与豆奶之誓是互斥的两个结局（黑 / 绿），同一套牌只能走一边。

### 新增能力（2 个）

- `mf_dynasty_aura`：奶蛙王朝的 +3 光环（按能力层数算）。
- `mf_exist_watch`：奶蛙存在吗？的触发能力。

### 稀有度与数值调整

- 稀有度下调：奶家人、奶king、奶蛋？ → 精良（Uncommon）；奶兔、奶蛙回旋 普通/稀有 → 精良。
- **奶狗**：改为双数字 `amounts: [3, 2]`（伤害 3 × 2 次），文案同步为 `{Number}` / `{Number1}`。
- **奶蛙回旋**：效果换成 `mf_mire_whirl`（按目标减益层数 × N 造成伤害）。
- **奶奶弗斯**：抽牌 1 → 2，升级后 2 → 3。

### 打包

- `studio/pack.sh` 新增 `MOD_ONLY=1`：只打奶蛙 mod，跳过平台包与 Studio 包，
  且不清空 `dist/`（平台包取自游戏目录当前安装的 dll，同版本号重打会与已发布资产不一致）。
- 产出 `dist/Naifrog-Mod-0.3.2.zip`；清单完整性校验覆盖 `mod.json` 声明的
  卡 / 遗物 / 关键词 / 能力 / 文案 全部文件。

## v0.4.1

### 新增

- **遗物 `rarity`**：`Common` / `Uncommon` / `Rare`，直接写 `RelicInfo.relicRarity`。
  不写则沿用 `artTemplate` 的档位（旧行为），想精确控制档位不必再挑模板。
- **遗物 `poolColors`**：写了就按同色组另建 `Colored` 集合 ⇒ **职业专属遗物**
  （游戏 `SetCampaignRelicPool` 只收 `colorEs ⊆ 本局三色` 的 Colored 集合）。
  不写仍走共用 `Standard` 集合（所有职业可刷）。
- **遗物 `cardRequirement`**：触发点的卡牌过滤（`keyword` / `colors` / `excludeColors` /
  `rarities` / `describeKey`）→ `TriggerTargetInfo.cardCoreInfoCheck`。
  只对 `CastCard` / `CastCardFinished` / `DrawCard` / `DiscardCard` / `ExertCard` 生效。
- **遗物触发上限真正生效**：`triggerLimit` + `triggerLimitType`（`EachTurn` / `EachBattle`）
  此前装配层无条件写 `hasTriggerLimit = false`，作者写了也没用。
- **诊断导出 `relics.json`**：原版 19 个 `RelicDataSo` 集合（类型 / 色组 / 稀有度 / 成员 / 中文名）
  与 `poolByRelic` 反查表落盘到 `BepInEx/ModKitDump/`。
- **`tools/build-api-core.sh`**：Api / Core 首次可从源码重编（此前是纯发布产物，
  加字段无从落地）；`tools/build-game.sh` 改为优先引用 `dist/platform` 里的新 Api / Core。

### 修复

- 遗物装配清理克隆继承：`extraRelicEffectInfos` 置空、`mainType` 固定 `NoChargeTrigger`、
  `chargeCount = -1` ⇒ 不再需要"挑一件干净的原版模板"。
- 战役卡池去重（同一张 `CardSo` 只保留一份）+ 基础牌不进随机池。
- 新增 6 张黑球系疫病卡进池的奶蛙侧改动（详见 0.5.0）。

## v0.4.0

### 新增

- **骰子支持真骰型**：`amounts` 里可以写 `{"dice":{"sides":8}}` = 1d8、
  `{"dice":{"sides":4,"count":3}}` = 3d4（sides 只接受 4/6/8/12/20，count 1~10）。
  旧的 `{"dice":{"min":…,"max":…}}` 继续可用（行为不变：固定值或单颗 D6）。
- **`src/ModKit.Api/ClassApi.cs` 补回**：`ClassSpec` / `IModClassProvider` 按已发布 DLL
  的定义补进仓库，Api 现在可以从源码重建。同时给 `RelicDef` 补了
  `Colors` / `TriggerLimit` / `TriggerLimitType`（Core 早就在读这三项）。

### 修复

- **模组卡不再漏进原版职业的奖励池**：掉落取池出口（`LootManager.GetCardPoolByInfo`）
  与战役池重建（`CampaignManager.SetCampaignCardPool`）都按当前职业过滤，
  日志里会打印「本局三色 / 解析职业 / 池内模组卡张数」。

- **图鉴关键词说明不再抛异常**：`SummonDescription` / `LimitedDescription` / `KickDescription` /
  `ChooseDescription` 等模板带 `{Number}` / `{RuleNumber}`，图鉴直接取串时没有参数 ⇒
  SmartFormat 抛 `Could not evaluate the selector …`，而游戏错误弹窗挂在日志回调上，即使 catch 也会弹。
  现在按关键词名取规则值、连同关键词数值一起作为参数喂进去。
- **衍生卡（精粹等）彻底不进卡池**：此前只挡了"开局发放"，卡集合里仍带着它们 ⇒ 战役池会刷出精粹。
  现在装配期自动识别（任何卡 `toCard` 指向的卡名），集合、职业卡池一并过滤。
- **图鉴不单列衍生卡**：卡牌页跳过衍生卡，鼠标悬停在父卡上时显示「衍生：<卡名>」与该卡自身卡面文本。
### 奶蛙 mod v0.3.0（独立发行版 `naifrog-v0.3.0`）

- 「奶家人」召唤族 6 张：奶兔 / 奶狗 / 奶家人 / 奶king / 奶之刻 / 奶蛋？，外加 4 张精粹；
  卡池 16 → 22 张。
- 召唤类效果全部照原版实现（`CardKeywordSummon` / `EffectSpEffectCancer` /
  `EffectSpEffectAddLifeToAllSummon` / `EffectSpEffectGainAllDeBuff`），召唤物借用原版元素立绘。
- 改名：`mf_mudpuppet` 显示「奶蛋」、`mf_bloodspawn` 显示「奶鼠」（id 不变，旧存档不受影响）。
- 「抉择」的两个选项改挂到 `Choose` 关键词自带效果上 —— 与原版一致，卡面渲染成「[抉择]：A 或 B」。
- 修：自定义效果不再带数字槽（数字会浮在卡图外）；奶蛋？精粹改为「扣除 8 点生命上限 + 召唤」。

## v0.3.2

### 修复

- **能力里的自定义效果现在能生成**：游戏取效果类时按来源分两族 —— 卡/遗物走
  `Effects.Special.CardEffect.EffectSpEffect<id>`，**能力**走
  `Effects.Special.AbilityEffect.EffectSpAbility<id>`。平台此前只生成前者，
  于是"给玩家挂一个带自定义效果的能力"的卡（如「以母之名」）打出去后能力根本发不下去
  （`Activator.CreateInstance(null)` 抛异常）。现在两族都生成，共用同一个转发器。
- **单个坏补丁不再拖垮整个平台**：`PatchAll(assembly)` 改为逐个补丁类安装 + try/catch。
  此前一个可选补丁的目标方法在游戏里不存在（本机实例：
  `LocalizedStringDatabase.GetTable(string)` 不在该类上声明），整座桥"安装失败"、
  **平台整体停用**（症状：游戏按原版跑、所有 mod 文案变 key）。
- **生涯数据**：职业名在游戏重载本地化表后自动补写（改挂生涯数据面板的组装方法，
  并把所有 pump 的登记放进进程内共享副本）；旧职业 key 的统计按 旧→新 合并到当前职业并回写存档。

### 已知边界

- 四件套由本机已安装版本打包：仓库能从 `src` 复现 Game / Plugin，
  Api 源码缺 `ClassSpec` / `IModClassProvider`。

### 奶蛙 mod v0.2.0（独立发行版 `naifrog-v0.2.0`）

- 新增「蛙群怒涛」（10 点群伤，被奶蛙时 +5 群伤）与「大胃袋」（3 层坚韧 +
  本回合每结算 1 张奶蛙卡额外 +3 坚韧）。
- 「以母之名」的能力链路随之修好（需要本版平台）。
- 卡池与文案同步（中英双语；新增卡名与效果文本）。

## v0.3.1

### 修复

- **平台包缺启动入口**：0.3.0 的平台包只有 Api / Core / Game 三个 DLL，漏了 `ModKit.Plugin.dll`
  （BepInEx 入口，流水线由它启动）——按 README 手动装包的新用户其实什么都不会加载。
  0.3.1 包内补齐到四个 DLL，`CHECKSUMS.txt` 与 Release 的 `SHA256SUMS.txt` 同步给出。
- **初始牌组点名原版卡会变成「空白卡牌」**：游戏的 `CardDataManager.GetCardSoByName`
  查不到时返回 `EmptyCard`（空白卡牌）而不是 `null`，平台按 `null` 判断"没找到"，
  于是"按原版卡名兜底"那一步永远不执行 —— 初始牌组里点名原版卡（例：至尊骰 `Dice`）
  进战斗就成了一张空白卡。判定改为"名字必须对得上"。
- 同一陷阱一并修掉：`CustomEffectRuntime.FindCardSo`（`give_card` 会静默发空白卡）、
  `ContentAssembler` 的 `artTemplate` 模板查找（模板写错会拿空白卡当模板）。
- **能力描述键**：`AbilityDescription` 的键是 `<能力全名>`（平台自动补 `_Des`），
  写裸 id 会显示 `No translation found for '<mod>.<ability>_Des'`；文档已注明。

### 已知边界

- 四件套目前由本机已安装版本打包：仓库只能从 `src` 复现 Game / Plugin，
  Api 源码缺 `ClassSpec` / `IModClassProvider`，"发布物 ↔ 源码"的逐位对应仍未打通。

### 奶蛙 mod（独立发行版 `naifrog-v0.1.0`）

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
