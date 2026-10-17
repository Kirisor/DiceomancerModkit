# ModKit 能力缺口与下一步

> 来源：2026-10-12 一整天**实机迭代**中真实撞到的问题（不是设想）。
> 每条都标注了**证据**（当时的实机症状）与**为什么平台该管**。

---

## 〇、逐条状态复核（2026-10-14 12:55 · 按代码证据，不按记忆）

> **本文的机制分析（那些 file:line 与源码结论）仍然有效**；但"待做清单"是**快照** ——
> 本文正文冻结于 **2026-10-13 21:08**，其后还发生了：④ 拆 `ContentAssembler`、⑤ P2/P3/P4 三道加载期闸门、
> **实机导出通道落地**、能力表换真源、遗物与组合两个缺口的修复、原版 205 件遗物的结构 dump、**遗物触发点附加字段（Tier 1）落地中**。**这些都晚于正文。**
>
> **纪律：引用本文任何一条"还没做"之前，先在 `diceomancer-mod\modkit` 下重跑它那一行的「证据命令」。**
> 本项目已经吃过两次亏 —— 一次是"文档说没做、其实早做了"，一次是"文档说做了、其实只是名字像"。
> （⚠️ 另有一个**方法学坑**：`Select-String -Path '目录\*.cs'` **不递归**，会把"没搜到"错报成"没实现"。
> 核查必须 `Get-ChildItem -Recurse -Filter *.cs`。）

| 条目 | 复核结论（2026-10-14） | 证据命令（在 `modkit\` 下跑） |
|---|---|---|
| **P0-1** 触发点→字段映射 | 🟡 **部分**：触发点名已在**加载期校验**（`ContentValidator.cs:134`，校验写进 `triggerTargetInfo.triggerType` 的那张表）；**"幽灵触发点"（取值合法但原版从不发出）会警告**（`GameFacts.ActualTriggerNames` → `ContentValidator.cs:160-164`）。但 `ChangeBattlePhase` / `WholeNewTurnStart` 依赖的两个 **class 字段（`battlePhase` / `certainTurnStartInfo`）兜底仍未做** ⇒ **是否仍会崩，未复验** | `grep -rn "battlePhase\|certainTurnStartInfo" .`（只在名字表/注释里命中，无兜底逻辑） |
| **P0-1b** 触发条件 `triggerCondition` | ❌ **仍未做** | `grep -rn triggerCondition ModKit.Api` → 0 |
| **P0-2** 能力名清单 | 🟡 **能力名探测的代码写了、但没进导出文件**：`GameCapabilitiesProbe.cs:996-999` 明说要"补上能力名 + 每个能力的类别探测 + 命名冲突那笔欠账"，而实机导出的 `capabilities.json` **只有 8 个键、没有能力名** ⇒ 要么没探测到、要么没写盘（**待查日志**） | `grep -n 能力名 ModKit.Game\GameCapabilitiesProbe.cs`；`(Get-Content ..\..\DICEOMANCER\BepInEx\ModKitDump\capabilities.json \| ConvertFrom-Json).PSObject.Properties.Name` |
| **P0-3** 文案键加载期检查 | 🟡 **只做了一半**：**格式串风险已拦**（`ContentValidator.Text.cs::CheckNoFormatBraces`：文案含 `{}` 即 Error），但**"缺文案键 ⇒ 直接告诉作者该写哪个键"仍未做** | `grep -n "CheckNoFormatBraces" ModKit.Core\ContentValidator.Text.cs` |
| **#4** 关键词接游戏事件 | ❌ 仍未做 | `grep -rn TriggerByOtherEffect .` → 0 |
| **#6** 静默失败防线 | 🟡 **有进展**：新增"幽灵触发点"警告；另有 **P2/P3 两道加载期闸门**（一个效果多数字 ⇒ Warning；文案含花括号 ⇒ Error） | `bin\TestRunner.exe`（90 例，含这两条回归） |
| **#7** 热重载 | ❌ 仍未做 | `grep -rn HotReload .` → 0 |
| **#8** `capabilities.json` 是手工快照 | ✅ **已解决，且比本文设想更彻底**：改为**游戏内机器导出**（`ModKit.Game\CapabilitiesDump.cs`，含卡/遗物/**费用槽组合**/关键词/触发点），两份副本已同步且**字节相同** | `grep -n knownCostCombos ModKit.Game\CapabilitiesDump.cs` |
| **#9** `modkit new` 模板 | ❓ **本次未核实** | — |
| **#10** 渲染探针覆盖 | ❓ **本次未核实** | — |
| **#11** 自定义效果作者路径端到端 | ✅ **已解决**：`[ModEffect]` 作者路径**实机验证 21/21**（平台用 Reflection.Emit 生成适配壳，作者不写 `EffectSpEffect*`） | 见 `docs\ModKit-效果字典.md`、作者手册 §7「自定义效果 `[ModEffect]`」（§11.5 已随 2026-10-14 手册重写失效） |
| **#12** 卡名撞原版名 ⇒ 静默失效 | ❌ **仍未做**（`ContentValidator.cs` 内 0 命中"重名/撞/conflict"）。⚠️ **防误读**：`vanillaCardNames` 的那些引用是 **`artTemplate` 借用校验**（`CheckArtTemplate`，`:82`/`:114`），**不是**撞名检测 | `grep -n "CheckArtTemplate\|重名" ModKit.Core\ContentValidator.cs` |
| **#13** 关键词附加字段白名单 | ❌ **仍未做**（`ContentValidator.Effects.cs` 里唯一的"白名单"是 P2 的 `AllowsMultipleAmounts` 例外表，与关键词无关）⇒ **自定义关键词写 `effects` 仍会被静默丢弃** | `grep -n 白名单 ModKit.Core\ContentValidator.Effects.cs` |
| **#14** 遗物 trigger 被改写成 Passive | ✅ 已修（`ContentAssembler.AssembleRelic` 显式写 `relicMainType = NoChargeTrigger`） | `grep -n NoChargeTrigger ModKit.Game\*.cs` |
| **#15** rNumbers 长度契约 | ❓ **本次未核实** | — |
| **#16** 能力特效描述键恒空 | ❓ **本次未核实** | — |
| **#17** 4 个幽灵效果名 | ❌ **仍未做**（`NumberRoll` / `SpImprovise` / `AddPower` 全库 0 命中 ⇒ 没有任何拒绝逻辑，写了照样 `ArgumentNullException`） | `grep -rn "NumberRoll\|AddPower\|SpImprovise" .` → 0 |
| **#18** `add_random_card` 无法表达 | ✅ 已实现（`special` / `career` 可用；`specific` 停用） | `grep -n "def.Special" ModKit.Game\ContentAssembler.CardPools.cs` |
| **#19** `number_plus_sub` 只能加不能减 | ❌ **仍未做**（Api 无 `negative` 字段；装配固定 `isPositive = true`，只有 `deal_damage`/`lose_life` 写 false） | `grep -n isPositive ModKit.Game\ContentAssembler.Effects.cs` |
| **#20** 指定卡表（`pool.specific`） | ❌ **仍未做**：本次读了装配代码 —— `specific` **故意不解析卡名**（只报错 + 留空池，抽不到时游戏用 `404Card` 兜底），理由写在 `ContentAssembler.CardPools.cs:82-95`（CardSo 进存档图 ⇒ `InvalidDataContractException` **截断战役存档**）⇒ 「运行时命名池」这条**唯一正路还没走** | `grep -n "specificPool" ModKit.Game\ContentAssembler.CardPools.cs` |
| **新增 A** `knownCostCombos` 未接校验器 | ❌ **数据已同步、读侧未实现**（`FileCapabilities` 只 `ReadStrings` 了 cards / relics / keywords / triggers 四个键）⇒ **"颜色合法但组合未预制"仍 lint 放过、进游戏才 `KeyNotFoundException`** | `grep -n ReadStrings ModKit.Tools\Capabilities.cs` |
| **新增 B** `EffectContext` 17 个成员未接通 | ❌ **未变**（`WarnOnce` 28 处；`GameCombatant` 只有一处注释：「补齐时要写一个 `GameCombatant : ICombatant` 包住 `Combat.Character`」） | `grep -rn WarnOnce ModKit.Game\CustomEffectRuntime.cs` |
| **新增 C** `upgrade_card` 映射错误 | ❌ **2026-10-14 确认是 bug、已降级为「暂勿使用」**：装配写 `effectName="ImproviseCard"`（`ContentAssembler.Effects.cs:292`）**却不给卡池** ⇒ 打出大概率 `NullReferenceException`；游戏**没有 `EffectUpgradeCard`**，升级只能由**特殊效果** `EffectSpEffectUpgrade*` → `SubCommandUpgradeCards` 触发。**当前 0 个 mod 在用**（无受害者，但是作者地雷）。修法（需先定"升级哪一张"的语义）见效果字典 **§1.2.9** | `grep -n "ImproviseCard" ModKit.Game\ContentAssembler.Effects.cs`（看 `:291` 的分支） |
| **新增 D** `behaviour`（特殊遗物）**运行期无消费者** | ✅ **已按裁决彻底删除（2026-10-14）**：API（`IRelicBehaviour` / `[ModRelicBehaviour]` / `IModContext.RegisterRelicBehaviour`）、JSON 解析、校验器分支、注册表索引与 `ResolveRelicBehaviour`、lint 的 dll 扫描、能力表字段（`KnownRelicBehaviourIds`）、样例遗物 `relics\fx_relic_behaviour.json`、`schemas\relic@1.json` 的字段、全部相关文档行 —— **一律移除**；`modkit` 内该类命中 **0**（负例控制已过）。⇒ **作者侧再无任何表达途径**（原版那 12 件 `isSpecialRelic=true` 的遗物**不可复刻**）。<br>—— 以下为**删除前**的复核证据（历史留存）：❌ **仍未做**（2026-10-14 第二轮逐处复核）：① `ContentRegistry.ResolveRelicBehaviour`（`ContentRegistry.cs:439`）在 `ModKit.Game` **零调用**（全仓只有定义 + `RegistryTests.cs:56` 一条测试 + `sample_starter\src\SampleContent.cs:113` 的一句注释）；② `ContentAssembler.AssembleRelic`（`ContentAssembler.cs:256-419`）**从不读 `RelicDef.Behaviour`**，且**无条件**写 `so.relicInfo.isSpecialRelic = false`（`:319`）；③ `EmitFallback` 只挂了 `EffectBaseAdapterEmitter.TryEmit`（`PlatformRuntime.cs:215`），而它**只认 `Effects.Special.CardEffect.EffectSpEffect<X>`**（`CustomEffectRuntime.cs:41/66/68`），**不认** `Relic.RelicEffects.Relic<带点 mod id>`。<br>**后果**：只填 `behaviour` 的遗物会退化成"像借来的模板遗物" —— 克隆 `artTemplate` 那件原版遗物并**继承它的 `mainEffectInfo`**（行为不可预测、随模板变），而作者声明的行为**永不执行**，且**不报任何错**。<br>**（原）修法 —— 已作废（2026-10-14 裁决删除该通路，不再做）**：新增桥补丁（`RelicManager.cs:231` + `RelicBase.cs:574` **两处** `Type.GetType` 解析）+ `RelicBase` 宿主类（把 `SpecialTriggeredEffect` / `BattleStart` / `WholeNewTurn` 等 virtual 钩子映射到 `IRelicBehaviour.Trigger`）+ 新公开 API；**还必须处理 `RelicBase.GetRelicInfo():574` 那处未打补丁的 `Type.GetType`** —— 它按 `Type.GetType` 结果**回写** `isSpecialRelic`，不补则**存档往返会把 `isSpecialRelic` 变回 `false`**（行为在存档后静默消失）。<br>**规模（2026-10-14 实机 dump）**：原版有 **12 件**走这条路（`isSpecialRelic=true`，见《[ModKit-原版遗物版图](ModKit-原版遗物版图.md)》§1.2） | `Get-ChildItem ModKit.Game -Recurse -Filter *.cs \| Select-String 'def\.Behaviour'` → 0；`Get-ChildItem . -Recurse -Filter *.cs \| Select-String 'ResolveRelicBehaviour'` → ModKit.Game 0 命中；`Get-ChildItem ModKit.Game\CustomEffectRuntime.cs \| Select-String 'Prefix'` → `EffectSpEffect` |
| **新增 E** `EnemyHealthBarBreak` / `AbilityStandardTrigger` **静默死** | 🟡 **进行中 / 部分解决**（2026-10-14：Tier 1 触发点附加字段落地中，加载期已兜出）：两者已进 `TriggerTypeNames.NeedsExtraFields` 并**点名缺的字段**（`ExtraFieldName` ⇒ `relativeCharacterType` / `extraString`）⇒ 作者写了就在 lint 期拿到 `[Warning]`；`SimpleNames` **22 → 20**，数字断言同步（`ModKit.Tests` 现 **120 例**全通过）。<br>**机制（未变）**：`EnemyHealthBarBreak` 比较 `relativeCharacterType`，平台不写 ⇒ 默认 `Owner`，而该事件的 owner 是**敌人** ⇒ **对敌人血条永不匹配**；`AbilityStandardTrigger` 比较 `extraString`（能力名），平台为 `null` ⇒ 只在"能力名也是 null"时匹配 ⇒ **永不匹配**。两者都**不崩、不报错**（这正是最坏的一类：看起来一切正常）。<br>**Tier 1 进展（触发点附加字段）**：新增可选字段后，`EnemyHealthBarBreak`（填 `relativeCharacter`）、`AbilityStandardTrigger` 与 `TakeRest`（填 `extraString`）、`ChangeBattlePhase`（填 `battlePhase`）、`WholeNewTurnStart`（填 `certainTurn`）、`ManaConsumed`（填 `colorE`）**都能表达**；**给了对应字段即不再警告**；**不给则按严重级别分两档** —— 缺 `battlePhase` / `certainTurn` ⇒ ❌ `[Error]`（lint 拒绝、**该文件在游戏里不会被加载**；平台装配时会补中性默认值（`battlePhase=(Main,false)` / `certainTurn={require:false,turnCount:0,Before}`）⇒ **不会崩**，但会把触发条件固定成那个默认值、与作者预期多半不符），缺 `relativeCharacter` / `extraString` / `colorE` ⇒ ⚠️ 警告（**不崩、不报错，但永不匹配**）；枚举值非法则 `[Error]` 并列出合法值。**这样设计是为了让作者不会带着与预期不符的配置进游戏。**⚠️ **字段契约已实机验证的前提是「新增 I」已修**（2026-10-14：`ref` 写入 + 装配后回读校验）；`AbilityStandardTrigger` 还要求 `extraString` 与原版能力名**逐字相同**（原版实例：`Burn`）—— 该触发点本轮仍未实机跑出结果 | `bin\TestRunner.exe`（106 例全通过）；`Get-ChildItem ModKit.Core\TriggerTypeNames.cs \| Select-String 'EnemyHealthBarBreak\|AbilityStandardTrigger'` |
| **新增 F** `build-mod.ps1` 曾缺 UTF-8 BOM ⇒ "构建脚本静默 exit 1" | ✅ **已修（2026-10-14）**：`.ps1` 缺 BOM 时 PS 5.1 按 GBK/ANSI 解码，遇到中文注释即报 `Unexpected token` 并**静默 exit 1**（不留可用报错）。修法 = 给文件加 UTF-8 BOM（全仓 9 个构建/自检 `.ps1` 现已全部 `EF BB BF`）。**纪律**：新写的 `.ps1` 必须带 BOM；且 **`edit` 工具会剥掉 BOM，改完必须回补并复核**（本轮实测再次复现） | `[IO.File]::ReadAllBytes('G:\deepseek space\diceomancer-mod\modkit\build-mod.ps1')[0..2]` ⇒ `EF BB BF` |
| **新增 G** 「`CSxxxx` 0 条」曾是**假 0**（验收仪式漏洞）| ✅ **已修（2026-10-14）**：`build-modkit.common.ps1:189` 的 `& $csc $argv.ToArray()` 把 csc 输出**返回给调用点**，而 5 个单层构建脚本的调用点全写成 `$null = Invoke-ModKitCompile …` ⇒ **诊断被整包丢弃**，那几层编译失败也一条 `CSxxxx` 都看不到（旧基线的「`CS[0-9]{4}` 0 条」对这些层**无信息量**）。修法 = **在 `Invoke-ModKitCompile` 内部就打印**（`Write-Host` 不进管线、调用点吞不掉）＋ 每次编译留一份日志（`%TEMP%\modkit-<层>-<时刻>.log`）＋ 抛错文案**带上错误码清单与日志路径**；调用点改回 `$dll = …`（成功时返回 `$null`）。<br>**纪律（本条的真正产出）**：任何「**0 条**」类断言都必须配**负例控制** —— 故意让一个文件编译失败，确认计数器变成 **≥1**，那个 0 才算证据；否则量到的可能是"没测到"。<br>**注意 `build-mod.ps1` 不在此洞内**（它 `& $csc $args` 未加 `$null =`，诊断本来就到控制台）—— 负例实测：坏源码 ⇒ 输出含 `error CS0126`、exit 1。 | 负例：`Set-Content modkit\ModKit.Core\_tmp_negcontrol.cs 'namespace X { public class B { public void M() { int x = ; } } }'`；`powershell -NoProfile -ExecutionPolicy Bypass -File build-core.ps1`；`exit code` ≠ 0 且输出含 `error CS1525` + `ModKit.Core 编译失败；错误码：CS1525`；删除该文件后重跑 6 层 ⇒ 全 `exit 0`、`error`/`warning` 均 0 条（**此次是真 0**） |
| **新增 H** `artTemplate` 克隆会**继承模板的额外效果与充能需求** | 🟡 **2026-10-14 实测确认，平台暂无字段可覆盖**：装配后 dump 逐件显示 `effects=主+X` 与 `charge=当前/需求` —— 模板 `Clock` 的克隆为 `effects=1+2 \| charge=12/-1`，模板 `Drumstick` 的对照件为 `effects=1+0 \| charge=4/-1`；另有 `Astrolabe`→`1+1`、`ChickenWing`→`charge=1`、`Anvil`→`charge=2`。⇒ 克隆**不止借图标**：模板的 `extraRelicEffectInfos` 与 `requiredChargeCount` 全部带进来（与「新增 D」的退化路径是同一个机制），而 `RelicDef` 既无 `mainType`（平台恒写 `NoChargeTrigger`）也无充能字段 ⇒ **作者无法覆盖、也无法清空**。选模板时应挑「`effects=N+0` 且 `charge=0/-1`」的原版遗物（`DumpRelics=*` 可查） | 证据：`state\archive\sample-relics-tested-20261014\assembly-evidence.md`（源自 `BepInEx\LogOutput.log` 第 18953–19308 行 `[relic]` 段） |
| **新增 I** 遗物触发点字段**写入丢失**（`TriggerTargetInfo` 是 **struct**，装配方法按值收参） | ✅ **已修（2026-10-14）**：`ApplyRelicTriggerFields` 曾以 `TriggerTargetInfo info`（**没有 `ref`**）收参 ⇒ 作者声明的 `battlePhase` / `certainTurn` / `relativeCharacter` / `extraString` / `colorE` **全部写进副本后被丢弃**，运行期只剩平台补的中性默认值。**实机现象**（用户 2026-10-14 报告）：JSON 写 `battlePhase{Upkeep,isStart:true}`（回合开始抽牌），实际却在 `(Main,isStart=false)`（玩家按下**结束回合**那一刻）触发 ⇒ 抽到的牌随即被 End 阶段的 `CharacterPlayer.EndPhaseStart()` → `CardManager.TurnEndTriggerCardsInHand()` 弃掉 ⇒「回合结束给了牌，但那张牌从抽牌区直接进弃牌区、净效果为零」。<br>**为什么两层门禁都没抓到**：lint 与 120 个单测只走 JSON→defs→校验器，**从不读装配出来的游戏对象**；而 dump 里那行 `battlePhase=Main isStart=False` 早就在归档日志里、两轮无人与作者 JSON 对照（「推断被自己用错证据推翻」的代价）。<br>**修法（两道防线）**：① 调用点与签名改 `ref TriggerTargetInfo`；② 新增 `VerifyRelicTriggerFields`，**装配后从最终对象回读**并与作者声明逐字段比对，不一致 ⇒ `result.Errors.Add` + `return null`（拒绝装配该遗物，不让「写对了但不生效」的配置上实机）。<br>**回归护栏**：新增源码级用例（断言 `ref` 调用点 / `ref` 签名 / 回读调用 / 拒绝分支都在），并做了**负例控制** —— 故意去掉 `ref` ⇒ `TestRunner` exit 1、`通过 119 / 失败 1`；还原后 `120 / 0`，且 `ContentAssembler.cs` SHA256 逐字节回到 `7E9B699C…015C3`。 | 负例实证见 `ModKit.Tests\Program.cs`「护栏 · 遗物触发点字段必须 ref 写入 + 装配后从最终对象回读」；`Select-String -Path ModKit.Game\ContentAssembler.cs -Pattern 'ApplyRelicTriggerFields\(ref'` |
| **新增 J** `lint <单个 mod 目录>` 会把 mod **自己声明的关键词**判成「未注册」 | 🟡 **2026-10-14 发现（平台缺陷，未修）**：`ModKit.Core\ModLoader.cs:260 LoadAll` 会先跑 `:287 var declaredKeywords = CollectDeclaredKeywordNames(root, dirs);`（辅助方法 `:341`）把「本批 mod 自己声明的关键词」预扫进能力表，再逐 mod 装配；而 `:400 LoadOneDirectory`（`:406 return LoadOne(root, directory, caps);`）**没有这一步** ⇒ 单目录口径下，卡牌 `keywords[]` 引用的、正是同一个 mod 自己声明的关键词，会被判成未知关键词 ⇒ `✗ [Error] … 关键词 'x' 未注册`、exit 1。**后果**：作者按手册跑 `lint <自己的 mod 目录>` 会看到**假错误**（实测：`state\scratch\manual-examples\manual_demo` 单目录 lint = 错误 1 / exit 1，而根目录 `lint` 同一份内容 = 错误 0 / exit 0）。`LoadOneDirectory` 的 doc-comment（`:396`）明写其目标就是"单个 mod 目录"，故这是**功能与文档不符的真缺陷**，不是用法问题。<br>**当前规避**（已写进作者手册 §1）：自检一律用 `lint <mods 根目录>`。修法（未做）：在 `LoadOneDirectory` 里对目标目录先跑一次关键词预扫描再 `LoadOne`。 | 复现：`bin\ModKit.Tools.exe lint <某一 mod 目录>`（exit 1，报自身关键词未注册）与 `bin\ModKit.Tools.exe lint <mods 根目录>`（exit 0）对照 |
| **数据源 · 原版遗物版图** | ✅ **已有权威数据源**（2026-10-14 dump）：原版 205 件遗物的真实结构已采集并归档（`state/archive/relic-dump-20261014/relic-dump.log`；两遍 dump **装配前 205 / 装配后 206**，多出的 1 件是 mod 遗物 ⇒ 证明同一容器）。分布、触发点频次、原版参数取样、三层路线图与 5 件照抄样本见《[ModKit-原版遗物版图](ModKit-原版遗物版图.md)》 | `(Get-Content state\archive\relic-dump-20261014\relic-dump.log) -match 'triggerType=' | ForEach-Object { ($_ -split 'triggerType=')[1].Split(' ')[0] } | Group-Object | Sort-Object Count -Descending` |

**复核后的口径**：正文里 **P0-1 / P0-2 / P0-3 / #6** 属"部分完成"，#8 / #11 / #14 / #18 属"已完成"，
其余仍为"未做"；**#12 与 #13 是最容易误判为已完成的两条**（名字命中 ≠ 功能落地）。

> **2026-10-14 增补**：**新增 D** 仍是「未做」，但现在有了量化规模 —— 原版 **12 件**特殊遗物需要它；
> **新增 E** 随 Tier 1 附加字段转为「**进行中 / 部分解决**」；**新增 H** 是 2026-10-14 八件样例遗物实机后的新增条目
> （模板继承 `extraRelicEffectInfos` 与 `requiredChargeCount`，是「新增 D」退化路径的另一面）；
> **新增 I** 是同日用户在实机上照出的「触发点字段写入丢失」缺陷（`struct` 按值传参），**已修 + 已配负例控制的源码级护栏**
> —— 这一条同时解释了「新增 E 的两个静默死触发点为什么在修好前**永远**不可能触发」。
> 遗物侧的权威数据见
> 《[ModKit-原版遗物版图](ModKit-原版遗物版图.md)》。

> **编号保持不重排**：**#5 已于 2026-10-14 随其所属特性整体删除**（用户裁定不做该特性）——
> 代码 / schema / 测试用例 / 作者向文档均已同步移除，**回归基线 94 → 90**。
> 为免破坏散落在多份文档与记忆里的「§11.5」「§16」等交叉引用，正文编号一律**不重排**（缺口编号同理）。

---

## 一、作者踩坑率最高的三处（P0）

### 1. 触发点 → 所需字段的映射缺失

**实机症状**：能力写 `trigger: "WholeNewTurnStart"` → 一进入新回合就抛
`NullReferenceException`（`Trigger.TriggerManager.CheckTriggerFitTargetTrigger`），
**报错信息完全不指向原因**，而且**不是静默失效、是直接崩**。
> ⚠️ **2026-10-14 更新**：下面这段症状描述的是**平台兜底之前**的行为。装配层现在会给
> `battlePhase` / `certainTurnStartInfo` 补中性默认值 + 装配期自检 ⇒ **不再崩**；
> 缺字段的真实后果变成「触发条件被固定成默认值」。详见本节表格后的「硬性不变量」一段。

**根因**：`TriggerTargetInfo` 是 struct，含 11 个字段，**每个触发点只解引用其中一部分**。

> **★★ 2026-10-13 源码通读后的更正（下面这张表原先有一半是错的）**：
> "必须非空"要看字段是 **class 还是 struct** —— struct 字段永远不可能为 null，
> 只会在语义上"不匹配"。逐字段核实（`Trigger/TriggerTargetInfo.cs`）：

| 触发点 | 解引用的字段 | 字段类型 | 为 null/默认时的真实后果 |
|---|---|---|---|
| `WholeNewTurnStart` | **`certainTurnStartInfo`**（`:122`） | **class** | **直接 NRE** ✔ |
| `ChangeBattlePhase` | **`battlePhase`**（`:97`） | **class** | **直接 NRE** ✔ |
| `ChangeWorldPhase` | `worldPhaseInfo`（`:79`） | struct | 只是相位不匹配，**不崩** |
| `DrawCard` / `CastCard` / `DiscardCard` / `CastCardFinished` / `ExertCard` | `cardCoreInfoCheck`（`:108`） | struct | **不崩**；且默认值经 `Card/CardUtils.cs:76-114` 一路短路到 `return true` ⇒ 语义是「**任意卡都匹配**」 |
| `CharacterRollDice` / `CharacterCheck` / `EnemyHealthBarBreak` | `ownerCharacter` + `relativeCharacterType` | class + enum | 不崩（只做比较）；`relativeCharacterType` 默认 `Owner` |
| `BeenAttacked` / `CharacterLoseLife` / `DamageBeenDealt` | `ownerCharacter` | class | 只做比较，不崩 |
| `ManaConsumed` | `colorE` | enum | 默认色相等即匹配 |
| `CardKeywordTrigger` / `AbilityStandardTrigger` / `TakeRest` | `extraString` | string | 两边同为 null 时 `==` 成立（即匹配） |
**★ 硬性不变量（2026-10-14 实机崩溃后补；两件事别混）**

- **① 平台侧不变量（平台替你守）**：注册进 `TriggerManager` 的**每个 target**，`battlePhase` 与
  `certainTurnStartInfo` 两个 **class** 字段必须非 null —— `TriggerManager.CallTriggerEvent` 会遍历**所有** target
  逐个调用 `CheckTriggerFitTargetTrigger`（`Trigger/TriggerManager.cs:97` / `:122`）。原版实证 **288/288 全非 null**。
  **平台现已自动补中性默认值**（`ModKit.Game/ContentAssembler.cs:392-429`）+ **装配期自检**
  ⇒ **任何遗物都不会因这两个字段而崩**（2026-10-14 实机 NRE 修复）。
- **② 作者侧责任**：你要的触发条件**仍然必须自己写**；不写 ⇒ 条件 = 中性默认值 ⇒ `lint` 报 `[Error]`。
- **验证通道**：`BepInEx\config\diceomancer.modkit.cfg` 里 `DumpRelics = *` 后进战斗，日志里每个 target 会打
  `class 字段判定（为 null 会 NRE 崩游戏）：battlePhase=…｜certainTurnStartInfo=…`；
  **「装配后」那一段若出现 `=null ⇒ 崩` 就是必崩配置**（本轮崩溃正是被这行照出来的）。
  <br>→ 上表「为 null/默认时的真实后果」列描述的是**游戏侧对 null 的反应**（这一条仍然成立）；
  但平台装配层已保证**不会再把 null 交给游戏**，所以那一列对 mod 作者的实际含义是「条件会静默变成默认值」。

> ⇒ **真正会崩的只有 `ChangeBattlePhase` 与 `WholeNewTurnStart` 两个**（都是 class 字段）；
> 平台既有的"借模板 + 只覆盖 `triggerType`"正好命中这两处，所以现状比原先描述的乐观。
> 原表把 4 个触发点都标成"必须非空否则崩"，会让排期高估风险。
>
> 另外三条相关事实（同一轮源码通读）：
> - `TriggerManager.RegisterTriggerTarget`（`:29`）**丢弃 `triggerType` 为空或 `"Passive"` 的注册**；
>   `CallTriggerEvent`（`:55`）对**未登记的触发名静默跳过**（无日志）。
> - `TriggerTargetInfo.AllTriggerType` 的 33 项里，`DiscardCard` / `ResolveCard` /
>   `ResolveCardFinished` / `Passive` / `OwnerCardRelated` / `Special` **从未被发出**（挂上即死监听）；
>   另有 6 个名字（`HandCardCountChange` / `CardEffectNoCast` / `SummonResolve` /
>   `RelicActive` / `RelicCorrupted` / `AcquireRelic`）**在枚举外且绕过 TriggerManager 直接派发**。
> - 完整的机制底稿见 `docs/游戏源码-机制补遗-2026-10-13.md`。

平台目前的策略是「**借模板的 `triggerTargetInfo`，只改 `triggerType`**」——
**换触发点时，模板的字段未必匹配**（上表两个 class 字段就是崩溃来源）。

**该做什么**：
- 对 `ChangeBattlePhase` / `WholeNewTurnStart` 两个触发点**主动兜底** `battlePhase` / `certainTurnStartInfo`，
  或**在加载期报错**，而不是进游戏才崩；
- 其余触发点可以只做"语义校验"（默认值不会崩，但可能永不匹配）。

**顺带一个高价值的暴露**：`cardCoreInfoCheck` 的语义正是
「**这张牌要满足什么条件**」—— 那就是作者想表达"响应条件 X"时该配的东西。
**平台应该把它作为一等字段暴露出来**（如 `triggerCondition`）。

> **2026-10-13 进展**：这个 struct **在效果侧已经暴露并实机验证**了 ——
> `seek_card` 的 `require` 字段 → `ContentAssembler.BuildCardCoreInfoCheck`（效果字典 §1.2.5）。
> 触发侧的 `triggerCondition` 可以直接**复用同一个构造器**，不必再设计一遍；
> 剩下的只是"触发点 → 需要哪个字段"的映射与兜底（本节第 1 条）。

### 2. 能力名没有原版清单

**实机症状**：作者给自定义关键词起名「坚韧」→ **与游戏自带的角色增益重名**
（游戏内「战斗百科 → 角色增益」里就有），直到有人翻百科才发现。

**根因**：平台启动时探测了卡名(534) / 遗物名(205) / 关键词(36)，
**唯独没有「能力名」清单**，所以重名检测无从谈起。

**该做什么**：把 `AbilityDataManager` 的原版名字也探测出来，
在 lint 期做**重名检测**（跨类型也算 —— 关键词撞能力名同样会让人困惑）。

### 3. 文案键有三套不同约定，作者不可能猜对

| 内容 | 名字键 | 描述键 |
|---|---|---|
| 能力 | `<全名>` | **`<全名>_Des`**（平台自动补） |
| 关键词 | `<叶子名>` | **`<叶子名>Description`** ← 与其他都不同 |
| 卡牌 | `<全名>` | 无（描述由效果生成） |

而且**名字一律用无前缀叶子名**（`sample_starter.tenacious` → 查 `tenacious`）。

**实机症状**：卡面与浮窗出现 `No translation found for 'tenacious' in KeyWord`
/ `'tenaciousDescription'` ——
**这些错误只在游戏里才暴露**，作者要反复试。

**该做什么**：加载期检查「声明的关键词/能力有没有对应的文案键」，
缺了就 **warn 并直接告诉作者该写什么键**（与"类名不存在"那类提示同一个思路）。

---

## 二、能力边界上的两处空白（P1）

### 4. 关键词接不到游戏的事件系统

**实机症状**：想做「响应」= 「另一张卡打出后，结算本卡效果」——
`TriggerByOtherEffect()` **不会被调用**，也没有任何报错。

**根因**：`CardKeyWordUtil.GetHasTriggerTargetInfoFromName` 的整个实现是
`if (name == "Progress") return true; return false;` ——
**只有遗留的 `Progress` 会把 `triggerTargetInfo` 接给 `TriggerManager`**。
⇒ **关键词系统在游戏里实际没有接入事件系统。**

**两条路各缺一半**：
- **关键词路径**：有卡实例（`CardKeyword.OwnerCard` ✅）但没有事件 ❌
- **能力路径**：有事件（`TriggerManager` ✅）但**拿不到卡实例**
  （平台 `EffectContext` 刻意不暴露 `CardBase`；游戏 `EffectBase` 只有 `cardSo`）❌

**该做什么**（唯一可行解）：**平台加一个 Harmony patch，在卡牌被结算等时机，
向"这张卡身上的关键词"派发事件**。工作量大，但这是解锁「响应」类设计的唯一途径。

### 6. 「静默失败」还没有系统性的防线

平台的核心价值主张是「**把进游戏才发现的问题提前到加载时**」，但本次撞到的
**静默失败**（不报错、也没效果）就有五类：

| 静默失败 | 表现 |
|---|---|
| `toCard` 指向不存在的卡 | 变「空白卡牌」 |
| 效果时机在逻辑上不可能发生 | 永远不触发（如"战斗开始"给一张战斗中才打出的牌） |
| 关键词接不到事件 | 永远不触发 |
| 触发点字段不匹配 | （这个是崩溃，反倒"好"） |
| 类名不对 | 静默退化成基类 |

**该做什么**：建一份**「静默失败清单」**，逐条加加载期检查。
这是本平台区别于"手写 Harmony mod"的核心竞争力，值得专门投入。

### 7. 没有内容热重载

改一个 JSON 就要重启游戏。本会话为验证功能重启了**几十次**。
对作者的迭代体验影响**大于任何单个功能**。

### 8. `capabilities.json` 是手工快照

每次 lint 都会报一条假的 `custom: BlockGainAddSub 未注册` ——
因为它缺 `knownCustomEffectIds`。**应改为平台自己生成**（探针已有全部数据）。

### 9. `modkit new` 只生成纯数据骨架

不带 `entryAssembly`、不带 C# 项目、不带构建脚本。
**本次才补上 `build-mod.ps1`**，它应该成为模板的一部分（可选生成 C# 骨架）。

### 10. 渲染探针只覆盖卡牌

`RenderProbe` 挂钩的是 `CardBase.SetCardNameTMP` ——
**能力 / 遗物的显示没有等价的探针**。

### 11. 自定义效果（`[ModEffect]`）的作者路径没有端到端验证

目前只验过**平台内置**的 `BlockGainAddSub`。作者自己写一个 `[ModEffect]` 类
能不能被注册、能不能被调用，**还没走过一遍**。

---

## 三点五、2026-10-13 游戏源码通读新增的缺口（六条）

> 完整证据与机制底稿见 **`docs/游戏源码-机制补遗-2026-10-13.md`**（含 file:line）。
> 这一节只列"该改什么"，按建议优先级排。

| # | 缺口 | 游戏侧证据 | 建议动作 |
|---|---|---|---|
| **12** | **卡名/内容 id 撞原版名 ⇒ 静默失效** | `GameData/CardDataManager.cs:71/:74` 用 `TryAdd` 注册，同名保留先注册者，**无任何日志** | lint 增加"内容 id 与原版 534 张卡名重名"检测（跨 mod 冲突已由前缀解决，这条防的是"作者写了个原版名"） |
| **13** | **关键词的附加字段受游戏侧名字白名单 gate** | `Keyword/CardKeyWordUtil.cs`：`number` 21 名、**`effects` 21 名**、`toCard` 5 名、`mana` 仅 `Storm`、`colorE` 仅 `Resonance`、`genRandomCardInfoPack` 仅 `Improvise`、`triggerTargetInfo` 仅 `Progress` | lint 按名字白名单提示"该关键词不吃这个字段"；**尤其**：自定义关键词写 `effects` 会被**静默丢弃**（`EffectBases` 根本不构造） |
| **14** | **遗物 trigger 可能被运行期改写成 `Passive` 而永不注册** | `Relic/RelicBase.cs:299-305` 对 `relicMainType ≤ 1`（Active/ChargedTrigger）强改 `Passive`，而 `RegisterTrigger()` 在 `:332/:335` 才调用；`TriggerManager.cs:29` 丢弃 `Passive` | **已修（2026-10-13）**：`ContentAssembler.AssembleRelic` 现在显式写 `relicMainType = NoChargeTrigger` + 充能字段归零（此前平台从不写该字段，完全继承 `artTemplate`，能跑是模板运气） |
| **15** | **rNumbers 长度契约**：`effectNumberInfos.Count` 与传入的 `RNumber[]` 长度不等时，**整组 rNumbers 为 null** | `Utilities/EffectUtils.cs:51-61`；槽数权威表 `GetEffectRNumberCountByName`（6 个名字 0 槽，其余 1 槽） | 装配按该表定槽数，并断言长度一致；否则后续 `rNumbers[0].GetCollapsedNumber()` 必 NRE |
| **16** | **能力侧特殊效果（`EffectSpAbility*`）的描述键恒为空** | `Effects/EffectBase.cs:89-99` 只认类名里的 `"SpEffect"` | 平台自写能力特效时不要依赖 `EffectDescription`（能力文案走 `AbilityDescription` / `<全名>_Des`） |
| **17** | **4 个"幽灵效果名"**：`NumberRoll` / `SpImprovise` / `SubtractAllNumber` / `AddPower` | 出现在 `EffectUtils.EffectStrings`（22 项）里，但**没有对应类** | 与 `CleanMana`/`EndTurn` 同坑：写了就 `ArgumentNullException: type`。catalog/校验器应对这批名字给出明确拒绝 |
| **18** | ~~**`add_random_card` 无法表达**~~ **（2026-10-13 已实现）** | 已补 `pool` 字段（`specific` / `special` / `career` + `require`），`special` 路径已实机验证 | ⚠️ `specific`（写死卡表）**已停用** —— 它把 `CardSo` 对象引用放进 `EffectInfo`，经"牌组共享 EffectInfo"进战役存档 ⇒ `InvalidDataContractException` + **存档被截断**。恢复它需要 #20 |
| **19** | **`number_plus_sub` 只能加、不能减** | `Effects/EffectNumberPlusSub.cs:17` 用 `IsPositive` 决定加减，而平台 `ContentAssembler.BuildEffectInfo` **固定写 `isPositive = true`**（只有 `deal_damage`/`lose_life` 写 false） | 暴露一个作者字段（如 `negative: true`），或至少在 lint 期把这条限制说清楚 |
| **20** | **"指定卡表"的卡池无法表达**（`pool.specific` 已停用） | 游戏唯一能"列出一批卡"的字段是 `GenRandomCardInfoPack.specificPool : CardSo[]`，而它**是对象引用**：牌组 `CardInfo` 与卡资产共享 `EffectInfo` ⇒ 本局结束 `SaveCampaign`（DataContractSerializer，`FileMode.Create` 先截断再写）会为 `Card.CardSo` 建契约并抛 `InvalidDataContractException`，**存档被截断**（2026-10-13 实机事故） | 实现「**运行时命名池**」：把作者给的卡名表**留在 `CardDataManager`**（注册成一个具名 `CardDateCollection`），`EffectInfo` 里只放**池名字符串** + `cardPool = SpecialPool` ⇒ 存档图里只有字符串。这是恢复该能力的**唯一正路** |

**顺带记两条已定位的历史缺陷根因**（不再是"原因不明"）：
- 平台当年"每张卡被登记两遍"→ `CardManager.AddCardToPlayerCards:555` **无去重**，
  而 `GenerateCardObjFromCardInfo:569` 内部**已经** `allPlayerCards.Add` + 进牌库。
- 关键词 tooltip 崩 → `Utilities/DitwUtil.cs:808-811` 的 `ContainsLink` **无 null 检查**
  （调用点 `TooltipBuilder:57` / `RelicBase:463` / `RuleDetail:62/:176`）；
  另一处是 `UI.KeywordIndicator/KeywordIndicatorBase.cs:40-75` 的硬编码类型链，未命中时
  `ActiveSprite` 留 null，之后赋值即崩。

---

## 建议的推进顺序

| 优先级 | 做什么 | 状态与理由（2026-10-14 重排） |
|---|---|---|
| **1** | **`knownCostCombos` 接进 `FileCapabilities`** | **数据已经躺在 `mods\capabilities.json` 里（21 组、含顺序），只差读**。不做的后果：作者写"颜色都合法但组合未预制"的槽位，**lint 放过、进游戏 `KeyNotFoundException`**。当前性价比最高的一条 |
| **2** | **触发点两个 class 字段兜底 + 实机复验**（P0-1） | 仍是唯一"**会崩、且崩溃不指向原因**"的地方（`ChangeBattlePhase` / `WholeNewTurnStart`）。触发点名校验与"幽灵触发点"警告**已做**（见 §〇），只剩字段兜底；顺带做 **P0-1b `triggerCondition`**：效果侧 `cardCoreInfoCheck` 已实机验证，**直接复用同一构造器**即可 |
| **3** | **能力名/类别探测 → 写进导出 + lint 重名检测**（P0-2 + #12 的一半） | 探测代码**已经写在 `GameCapabilitiesProbe.cs:996-999`**，只是**没出现在导出文件里** ⇒ 先查为什么没落盘，再补"作者内容 id 撞原版名"的检测（`CardDataManager` 用 `TryAdd`，撞名**静默保留先注册者**） |
| **4** | **文案键加载期检查**（P0-3 的另一半） | 三套约定（能力 `<全名>_Des` / 关键词 `<叶子名>Description` / 状态 `<叶子名>_Des`）作者不可能猜对，且**只在游戏里暴露**。花括号那半边已做 |
| **5** | **#13 关键词附加字段白名单** | 自定义关键词写 `effects` 会被**静默丢弃**（`EffectBases` 根本不构造）—— 高踩坑率、低实现成本 |
| **6** | **#12 撞名检测 / #19 `negative` / #17 幽灵效果名拒绝** | 每条都是"加载期一句话"级别的防线，直接减少静默失败 |
| **7** | **`EffectContext` 补齐（`GameCombatant : ICombatant`）** | 解锁的自定义效果能力面最大（现 17 个成员只记 Warn），但工作量也最大 |
| **8** | **#20 运行时命名池** | 恢复"指定卡表"的**唯一正路**（把卡名表留在 `CardDataManager`、`EffectInfo` 只放池名字符串），须避开存档图 |
| **9** | 热重载 / 渲染探针覆盖 / `new` 模板升级 / #15 #16 #9 #10 复核 | 作者体验与历史欠账；其中"capabilities 自动生成"**已完成**（见 §〇 #8），已从本行移除 |
