// =============================================================================
//  ModKit.Game · 流水线编排（总装）
//
//  平台启动后的完整流程，顺序不可换：
//
//    ① 构建内容注册表（跨程序集）        —— 由 ModKitPlugin 在装桥时完成
//    ② 探测游戏能力（费用色/卡名/触发点） —— 必须等**运行时事实就绪**
//    ③ 扫描并加载 mod（清单 → 解析 → 校验）
//    ④ 注入文案
//    ⑤ 装配内容（卡/遗物/关键词/能力）并注册进游戏
//    ⑥ 读回断言（"游戏真的取得到吗"）
//
//  ★★ 为什么"注入文案"必须在"装配内容"**之前**（2026-10-11 实机证伪后调整）：
//    卡面文本是在**创建卡对象的那一刻**去本地化表里查的
//    （`CardBase.SetByCardInfo` → `CardName` setter → `SetCardNameTMP` → GetLocalizedString）。
//    装配过程中会创建卡对象，所以文案若放在装配之后，
//    那一批卡的卡面会显示 `No translation found for '…' in CardName`。
//    **一般化：写文案要先于任何会读取文案的动作。**
//
//  ★ 时序约束（两次实机修正的结果）：
//    探测所需的两类事实**只在战斗场地存在时才可用** ——
//    反编译 `ManaManager.cs:88` 证实
//      public static ManaManager Instance
//          => BattleMultipleWorldsManager.Instance.GetCurrentBattleField().manaManager;
//    所以本类只能在"运行时事实就绪"之后运行，由 ModKitBootstrap 的轮询器把关。
//
//  ★ 失败隔离：任何一步失败都只影响它自己那一块内容，平台整体继续。
//    这与 Loader 的口径一致（失败的最小单元是内容文件，不是 mod，更不是平台）。
// =============================================================================

using System;
using System.Collections.Generic;
using Card;
using Relic;
using ModKit.Api;
using ModKit.Core;

namespace ModKit.Game
{
    /// <summary>流水线运行的哨兵汇总 —— 全部写进日志，供验收核对。</summary>
    public sealed class PipelineSummary
    {
        public string ModsRoot;
        public int BridgePatchCount;
        public string Capabilities;
        public string LoadSummary;
        public string AssemblySummary;
        public string LocalizationSummary;
        public string ReadBackSummary;

        public readonly List<string> Errors = new List<string>();

        public bool HasErrors { get { return Errors.Count > 0; } }

        public override string ToString()
        {
            return string.Format(
                "桥补丁 {0}｜能力[{1}]｜加载[{2}]｜装配[{3}]｜读回[{4}]｜文案[{5}]｜错误 {6}",
                BridgePatchCount, Capabilities, LoadSummary, AssemblySummary,
                ReadBackSummary, LocalizationSummary, Errors.Count);
        }
    }

    /// <summary>
    /// 平台运行时。所有入口都是静态的 —— 不依赖任何 MonoBehaviour 生命周期
    /// （插件的 MonoBehaviour 会在首个场景被销毁，详见 ModKitBootstrap）。
    /// </summary>
    public static class PlatformRuntime
    {
        private static ContentRegistry _registry;
        private static bool _ran;

        /// <summary>已安装的内容注册表。</summary>
        public static ContentRegistry Registry { get { return _registry; } }

        /// <summary>最近一次运行的哨兵汇总。</summary>
        public static PipelineSummary LastSummary { get; private set; }

        /// <summary>是否已经跑过（幂等哨兵）。</summary>
        public static bool HasRun { get { return _ran; } }

        /// <summary>
        /// 跑一次完整流水线。**幂等**：重复调用直接返回上次的汇总。
        /// </summary>
        /// <remarks>
        /// 为什么必须幂等（实测发现）：就绪轮询器挂在 <c>SceneManager.sceneLoaded</c> 上，
        /// 而一局游戏会加载多个场景（地图 → 战斗 → …），于是轮询器会被创建多次、
        /// 流水线也会被跑多次 —— 实测日志里出现了两遍完整的流水线输出
        /// （第二次还探测到 536 张卡 vs 第一次 534 张）。
        /// <para>
        /// 重复跑的危害：内容被装配两次（同名卡重复注册）、文案重复注入、
        /// 日志被刷成两倍。所以这里加一道硬闸。
        /// </para>
        /// </remarks>
        /// <summary>去掉 mod 前缀，只留最后一段。</summary>
        /// <remarks>
        /// ★ 游戏侧认的是**无前缀名** —— 装配器给关键词设的名字、
        /// 以及类名解析（<c>CardKeyword</c> + PascalCase 叶子）
        /// 都是剥掉前缀的。文案注入必须用同一个名字，否则游戏查
        /// <c>KeyWord</c> 表时会 MISS，
        /// 表现为卡面出现「No translation found for 'xxx' in KeyWord」。
        /// </remarks>
        private static string LeafName(string id)
        {
            if (string.IsNullOrEmpty(id)) { return id; }
            int dot = id.LastIndexOf('.');
            return (dot >= 0 && dot < id.Length - 1) ? id.Substring(dot + 1) : id;
        }

        /// <summary>平台自建效果所需的卡面描述键（游戏自带的 EffectDescription 表里没有它们）。</summary>
        /// <remarks>
        /// ★ 为什么必须补：卡面描述是游戏运行时现拼的 ——
        ///   <c>new LocalizedString("EffectDescription", descriptionKey)</c>
        ///   （Effects/EffectBase.cs:288-330），键由效果类自己给。
        ///   平台自建的效果类（如 <see cref="Effects.Special.CardEffect.EffectSpEffectCleanMana"/>）
        ///   不属于游戏，缺条目就会在卡面显示
        ///   "No translation found for 'Key_CleanMana' in EffectDescription"。
        ///   这与"作者自写 custom: 效果必须自己提供 EffectDescription 条目"是同一条规则，
        ///   区别只是平台替**内置**效果把这条写掉。
        /// <para>
        /// 局限（已知）：只登记**当前语言**。玩家在游戏内切换语言后，平台内置效果没有
        /// 新语言的条目，会退回显示键名 —— 与 mod 内容的多语言行为一致，
        /// 不是本处引入的新问题。
        /// </para>
        /// </remarks>
        private static void AddBuiltinEffectDescriptions(LocalizationPump pump)
        {
            if (pump == null) { return; }

            string code = null;
            try
            {
                var locale = UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale;
                code = locale != null ? locale.Identifier.Code : null;
            }
            catch (Exception ex)
            {
                // 拿不到语言码不致命（内容仍可用，只是描述可能显示键名）
                UnityEngine.Debug.LogWarning("[ModKit] 取当前语言码失败，平台内置效果文案跳过：" + ex.Message);
            }
            if (string.IsNullOrEmpty(code)) { return; }

            bool zh = code.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

            // 目前只有 clean_mana 走平台自建效果。以后每加一个平台自建效果，就在这里加一条。
            pump.Add(code, "EffectDescription", "Key_CleanMana",
                zh ? "清空所有法力。" : "Clear all mana.");
        }

        public static PipelineSummary RunOnce(string modsRootOverride, bool strictMode, bool dumpCapabilities)
        {
            if (_ran && LastSummary != null)
            {
                new BepInExGameLog("[ModKit]").Info("流水线已跑过，跳过重复执行（幂等）。");
                return LastSummary;
            }
            _ran = true;

            var log = new BepInExGameLog("[ModKit]");
            var summary = new PipelineSummary();
            LastSummary = summary;

            summary.ModsRoot = string.IsNullOrEmpty(modsRootOverride)
                ? DiskFileSource.DefaultModsRoot
                : modsRootOverride;

            log.Info("===== ModKit 流水线开始 =====");

            // ---- ① 颜色别名注入（必须最早）----
            // Core 的 ManaColorNames 是颜色名的唯一解析点（校验器 / 装配器 / 卡池检索都用它），
            // 所以 mod 声明的别名要在**解析任何 mod 内容之前**注入，否则
            // 「colors: ["Yellow"]」会被当成未知颜色。
            try
            {
                CardColorAlias.EnsureLoaded(summary.ModsRoot, log);
            }
            catch (Exception ex)
            {
                log.Warn("颜色别名注入失败：" + ex.GetType().Name + ": " + ex.Message);
            }

            // ---- 注册表 ----
            // ★ 顺序很关键：mod 的程序集必须在**建注册表之前**加载进 AppDomain。
            //   否则 mod 里的 CardKeyword* / [ModEffect] 类
            //   不在 AppDomain 里，ContentRegistry 扫不到 ——
            //   后果是加载期误报「关键词未注册」，mod 的 custom 效果也判未注册。
            //   但加载内容本身又需要注册表（校验 custom id），
            //   所以先做一次**只读清单预扫描**，只取 entryAssembly 路径。
            try
            {
                var preLoader = new ModLoader(
                    new DiskFileSource(log),
                    info: delegate (string s) { },
                    warn: delegate (string s) { log.Warn(s); },
                    error: delegate (string s) { log.Error(s); });

                var entries = preLoader.ScanEntryAssemblies(summary.ModsRoot);
                int want = 0, loaded = 0;

                foreach (var e in entries)
                {
                    if (string.IsNullOrEmpty(e.AssemblyPath)) { continue; }
                    want++;
                    try
                    {
                        var asm = System.Reflection.Assembly.LoadFrom(e.AssemblyPath);
                        loaded++;
                        log.Info("已加载 mod 程序集：" + e.ModId + " → " + asm.FullName);
                    }
                    catch (Exception ex)
                    {
                        log.Error("加载 mod 程序集失败：" + e.ModId + " —— " + e.AssemblyPath, ex);
                    }
                }

                log.Info("预扫描：候选 mod " + entries.Count + " 个，声明了程序集的 " + want
                    + " 个，成功加载 " + loaded + " 个。");
            }
            catch (Exception ex)
            {
                log.Warn("mod 程序集预加载失败（继续，但带 C# 的 mod 会失效）：" + ex.Message);
            }

            // 现在才建注册表 —— 此时 mod 程序集已在 AppDomain 里，能被扫到。
            _registry = ContentRegistryFactory.BuildFromAppDomain(log);

            // 让桥在"游戏按约定拼名字找类"时能拿到**平台现生成的适配类**（作者自写效果那条路）。
            ModKit.Core.ContentRegistry.EmitFallback = EffectBaseAdapterEmitter.TryEmit;

            // ★ 桥自己也持有一份注册表引用，重建后必须重新装一次，
            //   否则运行期解析 CardKeyword* 时用的还是旧表。
            try { ModKitBridge.Install(_registry); }
            catch (Exception ex) { log.Warn("重装桥（新注册表）失败：" + ex.Message); }

            summary.BridgePatchCount = BridgeInstaller.InstalledPatchCount;

            // ---- 渲染探针：把"游戏真的把我们的卡显示出来了"变成可断言的事件 ----
            // 它挂钩 CardBase.SetCardNameTMP，能同时证明"游戏在渲染我们的卡"
            // 与"平台注入的文案生效了"（取到的是中文名而不是 key 名）。
            if (DiagnosticSwitches.RenderProbe) { try { RenderProbe.Install(log); } catch (Exception ex) { log.Warn("渲染探针安装失败：" + ex.Message); } }   // 默认关闭，配置 [Diagnostics] RenderProbe = true 可开启

            // ---- 职业 / 色球接缝：让原版未启用的 Black 成为第 6 颗球 ----
            // 新职业「奶蛙」的地基：ColorE 枚举改不了，只能启用原版没做成球的 Black。
            // Stage 1 球状态是 Locked（界面看得见、点不动），职业数据装配好后再放成 Free。
            try { CareerPatch.Install(log); } catch (Exception ex) { log.Warn("职业接缝安装失败：" + ex.Message); }

            // ---- 自定义颜色接缝：把 (ColorE)6 及以后"枚举里没有、但强转合法"的颜色接住 ----
            // 覆盖：世界取色/亮色/法力贴图、每战场的 ManaIconDic 注入、AssetRefSo 两个字典、
            // "全部颜色"枚举、法力音效名、Orb 文案。注册表为空时所有补丁放行原逻辑（零行为变化）。
            try { CustomColorCompat.Install(log); } catch (Exception ex) { log.Warn("自定义颜色接缝安装失败：" + ex.Message); }

            // ---- 模组图鉴：主界面右上角挂一块牌子，点开只列 mod 内容的图鉴 ----
            // 为什么要另起炉灶：原版图鉴的六个职业页三色组是硬编码的，侧栏书签数量由 prefab 固定，
            // 往里塞第 7 页等于和原版 UI 结构较劲（游戏一更新就碎）。入口复刻原版牌子，面板自建。
            try { ModCompendium.Install(log); } catch (Exception ex) { log.Warn("模组图鉴安装失败：" + ex.Message); }
            try { CompendiumJumpPatch.Install(log); } catch (Exception ex) { log.Warn("图鉴跳转安装失败：" + ex.Message); }
            // 能力中文名只存在于游戏本地化表，运行期导出一次给编辑器用
            try { AbilityNameDump.Run(log); } catch (Exception ex) { log.Warn("能力中文名导出失败：" + ex.Message); }
            // 游戏本地化表整表导出（触发点/稀有度/时机等中文原文）
            try { LocalizedTextDump.Run(log); } catch (Exception ex) { log.Warn("本地化文本导出失败：" + ex.Message); }

            // ---- ①.5 读取 mod 声明的自定义颜色（colors.json）----
            // 必须在能力探测与装配之前：颜色得先进入游戏的色表/图标表，
            // 卡片的 cardColorEs 与费用槽才过校验（否则会被判"未知颜色"）。
            try
            {
                int colorCount = CustomColorDecl.LoadAll(summary.ModsRoot, log);
                if (colorCount > 0)
                {
                    log.Info("自定义颜色声明：共注册 " + colorCount + " 个（来源：mod 目录下的 " + CustomColorDecl.FileName + "）。");
                }
            }
            catch (Exception ex)
            {
                log.Warn("自定义颜色声明：读取失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }

            // ---- ② 探测能力 ----
            ProbeSummary probeSummary;
            GameCapabilities caps;
            try
            {
                caps = GameCapabilitiesProbe.Probe(log, out probeSummary);
                summary.Capabilities = probeSummary.ToString();

                // ★ 把注册表扫到的自定义效果回填给校验器（2026-10-11 补上）
                //   此前 `AddCustomEffectId` **没有任何调用者**，于是能力表里这一项**永远是 0 个**，
                //   校验器会把作者写的 `custom:` 一律判为"未注册"并拒绝加载文件
                //   （实机症状：能力被拒 → 卡牌取不到能力 → 图鉴 NRE）。
                //   注意：注册表本身是认得的（桥的 Resolve 能解析到类），只是没喂给校验器。
                if (_registry != null && caps != null)
                {
                    int fx = 0;
                    foreach (var id in _registry.RegisteredEffectIds) { caps.AddCustomEffectId(id); fx++; }
                    log.Info("自定义效果已回填给校验器：效果 " + fx + " 个。");
                }

                if (dumpCapabilities) { DumpCapabilities(log, caps, probeSummary); }

                // ★ 诊断导出（2026-10-14 新增）：把**完整能力表**写成 JSON 文件。
                //   动因：遗物名（~205 件）与费用槽组合此前**只有日志、没有落盘**，
                //   于是仓库里的 capabilities.json 只能是手工快照（vanillaRelicNames 仅 1 项）。
                //   注意：这段刻意放在"加载 mod"**之前** ——
                //     · 卡名清单要的是**原版**卡池（平台装配 mod 卡之后就混进 mod 卡了）；
                //     · 且要在"没有 mod"时也照常导出（后面 ModCount==0 会提前 return）。
                //   失败只记 Error，不影响平台其余流程（TryWrite 内部已兜住异常）。
                CapabilitiesDump.TryWrite(log, caps, probeSummary);

                // ★ 诊断导出：卡清单 + 卡面图（同上，只读、失败隔离）。
                //   它做的是"枚举卡 SO → 取 sprite → RenderTexture 缩放 → PNG"，
                //   第一次跑会有几秒的一次性开销（可用配置关掉；已存在的 PNG 默认复用）。
                CardArtExporter.TryExport(log);

                if (probeSummary.HasFailures)
                {
                    log.Warn("部分能力探测失败 —— 相关内容会被判为不可用（而不是静默放行）：");
                    foreach (var f in probeSummary.Failures) { log.Warn("  " + f); }
                }
            }
            catch (Exception ex)
            {
                // 探测失败 = 能力表为空 = 校验器会把一切内容判为不可用。
                // 这是"安全的失败"：宁可不加载，也不加载出必崩的内容。
                log.Error("能力探测失败，本次不加载任何 mod 内容。", ex);
                summary.Errors.Add("能力探测失败：" + ex.Message);
                return summary;
            }

            // ---- ③ 扫描并加载 mod ----
            LoadSummary loadSummary;
            try
            {
                var loader = new ModLoader(
                    new DiskFileSource(log),
                    info: delegate (string s) { log.Info(s); },
                    warn: delegate (string s) { log.Warn(s); },
                    error: delegate (string s) { log.Error(s); });

                loadSummary = loader.LoadAll(summary.ModsRoot, caps);
                summary.LoadSummary = loadSummary.ToString();

                if (loadSummary.ModCount == 0)
                {
                    log.Info("没有发现任何 mod（目录：" + summary.ModsRoot + "），平台空转。");
                    return summary;
                }
            }
            catch (Exception ex)
            {
                log.Error("加载 mod 失败，本次不装配任何内容。", ex);
                summary.Errors.Add("加载失败：" + ex.Message);
                return summary;
            }

            // ---- ④ 注入文案 ----
            // ★ 必须在装配**之前**（2026-10-11 实机证伪后调整）。
            // 原因：装配过程中会创建卡对象（如验收辅助塞手牌），而卡面文本是在
            // **创建卡对象的那一刻**就去本地化表里查的。原来文案放在装配之后，
            // 于是那一批卡拿不到文案 → 卡面显示 "No translation found for … in CardName"。
            // 一般化：**写文案要先于任何会读取文案的动作。**
            try
            {
                var pump = new LocalizationPump(log);

                // ★ 卡牌 JSON 里的 name 也要灌进 CardName 表。
                //   游戏用 so.cardName（= 平台加过 mod 前缀的 id）去查 CardName 表，
                //   查不到就渲染成 "No translation found for … in CardName(zh-Hans)"。
                //   以前**只有** i18n/*.json 里的 CardName 条目会被注入 ——
                //   于是在卡里老老实实写了 name 的作者照样会看到那个报错
                //   （8 张新卡实测踩中：描述全对，只有卡名是红的）。
                //   顺序：先灌内联名 → 再由 i18n 覆盖（i18n 是作者更明确的那一份）。
                foreach (var m in loadSummary.Mods)
                {
                    if (m == null) { continue; }

                    // 卡 → CardName
                    if (m.Cards != null)
                    {
                        foreach (var d in m.Cards)
                        {
                            if (d == null || d.Name == null) { continue; }
                            foreach (var kv in d.Name)
                            {
                                if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) { continue; }
                                pump.Add(kv.Key, "CardName", d.Id, kv.Value);
                            }
                        }
                    }

                    // 遗物 → Relic
                    if (m.Relics != null)
                    {
                        foreach (var d in m.Relics)
                        {
                            if (d == null || d.Name == null) { continue; }
                            foreach (var kv in d.Name)
                            {
                                if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) { continue; }
                                pump.Add(kv.Key, "Relic", d.Id, kv.Value);
                            }
                        }
                    }

                    // 关键词 → KeyWord
                    if (m.Keywords != null)
                    {
                        foreach (var d in m.Keywords)
                        {
                            if (d == null || d.Name == null) { continue; }
                            foreach (var kv in d.Name)
                            {
                                if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) { continue; }
                                pump.Add(kv.Key, "KeyWord", LeafName(d.Id), kv.Value);
                            }
                        }
                    }

                    // 能力 → Ability
                    if (m.Abilities != null)
                    {
                        foreach (var d in m.Abilities)
                        {
                            if (d == null || d.Name == null) { continue; }
                            foreach (var kv in d.Name)
                            {
                                if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) { continue; }
                                pump.Add(kv.Key, "Ability", d.Id, kv.Value);
                            }
                        }
                    }
                }

                foreach (var mod in loadSummary.Mods)
                {
                    foreach (var i18n in mod.I18n) { pump.AddI18n(i18n); }
                }

                // ★ 平台自建效果的卡面描述键（游戏自带的表里没有）—— 必须在 Flush 之前登记，
                //   否则卡面会显示 "No translation found for 'Key_CleanMana' in EffectDescription"。
                AddBuiltinEffectDescriptions(pump);

                var pumpResult = pump.Flush();
                summary.LocalizationSummary = pumpResult == null ? "(跳过或语言未变)" : pumpResult.ToString();

                // 读回自检失败 = 作者会看到 key 名/缺失提示。大声报出来，不要静默。
                if (pumpResult != null && pumpResult.ReadBackFailed > 0)
                {
                    summary.Errors.Add("文案读回自检失败 " + pumpResult.ReadBackFailed +
                        " 条（游戏会显示「No translation found」或 key 名）。样例：" +
                        string.Join(", ", pumpResult.FailedSamples.ToArray()));
                }
            }
            catch (Exception ex)
            {
                // 文案失败不致命：卡仍可用，只是显示 key 名
                log.Warn("文案注入失败（内容仍可用）：" + ex.Message);
                summary.LocalizationSummary = "失败（不影响内容可用性）";
            }

            // ---- ④.5 按名字 dump 指定卡（配置 Diagnostics/DumpCards；留空则什么都不做）----
            // 放在**文案注入之后**：卡的中文名要去 CardName 表查，早于注入/本地化就绪时会查成 key 名。
            // 用途：把「中文卡名 → 内部卡名 → 该卡的 effects/keywords/数字 全貌」打进日志。
            try
            {
                GameCapabilitiesProbe.DumpCardsByName(log);
            }
            catch (Exception ex)
            {
                log.Warn("按名 dump 卡失败（不影响内容）：" + ex.Message);
            }

            // ---- ④.5b 按名字 dump 原版遗物（配置 Diagnostics/DumpRelics；留空则什么都不做）----
            // 与 DumpCards 同一时机：文案注入之后（中文遗物名要去 Relic 表查）、**装配之前**
            //   ⇒ 这里看到的全是**原版**遗物的真实序列化取值（mod 遗物此刻还没注册）。
            // 阶段标签"装配前"用来区分 ⑤.5b 那次读回 dump。只读，不写游戏数据。
            try
            {
                GameCapabilitiesProbe.DumpRelicsByName(log, "装配前");
            }
            catch (Exception ex)
            {
                log.Warn("按名 dump 遗物失败（不影响内容）：" + ex.Message);
            }

            // ---- ⑤ 装配内容 ----
            AssemblyResult assembly;
            try
            {
                assembly = AssembleAll(loadSummary, caps, log, strictMode);
                summary.AssemblySummary = assembly.ToString();
                summary.ReadBackSummary = assembly.ReadBackSummary;
                foreach (var e in assembly.Errors) { summary.Errors.Add(e); }
            }
            catch (Exception ex)
            {
                if (strictMode) { throw; }
                log.Error("装配内容失败。", ex);
                summary.Errors.Add("装配失败：" + ex.Message);
                return summary;
            }

            // ---- ⑤.5 装配后再 dump 一次（★ 只有这时才看得到 mod 自己的卡）----
            // ④.5 那次是在装配**之前**：mod 卡尚未注册进 CardDataManager，按名找不到。
            // 装配后 dump 等于**读回自检** —— 能看到平台究竟把 `cardCoreInfoCheck` /
            // `setterRNumberConfig` 这类配置对象写成了什么，而不是只看作者 JSON 的意图。
            try
            {
                GameCapabilitiesProbe.DumpCardsByName(log);
            }
            catch (Exception ex)
            {
                log.Warn("装配后按名 dump 卡失败（不影响内容）：" + ex.Message);
            }

            // ---- ⑤.5b 装配后再 dump 一次遗物（★ 只有这时才看得到 mod 自己的遗物）----
            // 与 ④.5b 同一命令、同一配置键，靠阶段标签区分。用途是**读回自检**：
            // 看平台究竟把 isSpecialRelic / relicMainType / mainEffectInfo 写成了什么。
            try
            {
                GameCapabilitiesProbe.DumpRelicsByName(log, "装配后");
            }
            catch (Exception ex)
            {
                log.Warn("装配后按名 dump 遗物失败（不影响内容）：" + ex.Message);
            }

            // ---- 登记我们的内容名（渲染探针 + 验收辅助共用）----
            try
            {
                var allCards = new List<string>();
                var allRelics = new List<string>();
                var allAbilities = new List<string>();
                // ★ 衍生卡（被别的卡 toCard 指向的卡）**不参与初始发放**（2026-10-13 用户指正）：
                //   精粹卡 / 装填卡 / 建造完成卡这类卡本来就是"由效果生成"的 ——
                //   直接塞进牌库既不符合原版玩法，也掩盖了"效果到底有没有真的生成它"。
                //   平台**自动识别**（凡被 toCard 指向即为衍生卡），作者不用手写标记。
                var derivedCards = new HashSet<string>(StringComparer.Ordinal);
                foreach (var m in loadSummary.Mods)
                {
                    foreach (var c in m.Cards)
                    {
                        allCards.Add(c.Id);
                        if (c.Keywords == null) { continue; }
                        foreach (var k in c.Keywords)
                        {
                            if (k == null || string.IsNullOrEmpty(k.ToCard)) { continue; }
                            // toCard 写的是本 mod 内的短 id，补上 mod 前缀才是全名。
                            // 前缀从这张卡自己的全名里取 —— ModLoadResult 没有 Id 字段，
                            // 但 c.Id 一定是「前缀.短id」形式（平台装配时加的）。
                            int dot = c.Id == null ? -1 : c.Id.IndexOf('.');
                            if (dot <= 0) { continue; }
                            derivedCards.Add(c.Id.Substring(0, dot) + "." + k.ToCard);
                        }
                    }
                    foreach (var r in m.Relics) { allRelics.Add(r.Id); }
                    foreach (var a in m.Abilities) { allAbilities.Add(a.Id); }
                }
                int derivedCount = allCards.Count;
                // ★ 只影响**发放**，不影响**渲染探针**：探针要盯着包括衍生卡在内的全部卡
                //   （它正是用来证明"效果真的把衍生卡生成出来了"的那只眼睛）。
                var grantCards = new List<string>(allCards);
                grantCards.RemoveAll(x => derivedCards.Contains(x));
                derivedCount -= grantCards.Count;
                if (derivedCount > 0)
                {
                    log.Info("验收辅助：已排除 " + derivedCount + " 张衍生卡（由效果生成，不直接发放）。");
                }
                RenderProbe.RegisterModCards(allCards, log);

                // ---- 验收辅助：进入第一场战斗发牌后把内容发给玩家 ----
                // 挂钩 CardManager.DrawCards，等两帧（发牌落地）再发。
                // 上一版失败的教训：发在初始发牌**之前**，会被发牌冲掉。
                //
                // ★ 为什么**不发能力**（2026-10-11 用户指正 + 设计一致性）：
                //   原版游戏里增益能力的来源**只有卡牌与遗物**，且战斗结束即清除。
                //   直接用 AbilityUtil.GenerateAbility 强塞是一种**游戏里根本不存在的路径**，
                //   之前正是它让能力表现出怪异行为（"被攻击时获得护盾"）。
                //   能力应当由卡牌的 `gain_ability` 施加 —— 那条链才是真实的、要被验证的。
                //   所以这里只发卡与遗物，能力留空。
                VerificationGrant.Install(log);
                VerificationGrant.Register(grantCards, allRelics, new List<string>(), log);
            }
            catch (Exception ex) { log.Warn("登记探针/验收辅助目标失败：" + ex.Message); }

            log.Info("===== ModKit 流水线结束 =====");
            log.Info("哨兵汇总：" + summary);

            if (summary.HasErrors)
            {
                log.Warn("本次运行有 " + summary.Errors.Count + " 条装配错误 —— 相关内容未加载，其余照常。");
            }

            return summary;
        }

        /// <summary>把所有 mod 的卡、遗物、关键词、能力装配进游戏。</summary>
        private static AssemblyResult AssembleAll(LoadSummary load, GameCapabilities caps, IGameLog log, bool strictMode)
        {
            var result = new AssemblyResult();

            // 把探测到的可用费用色转成装配器需要的形状（游戏枚举）
            var assemblerCaps = new ContentAssembler.ContentCapabilities();
            foreach (var c in caps.AvailableCostColors)
            {
                assemblerCaps.AvailableCostColors.Add(GameCapabilitiesProbe.ToGameColor(c));
            }

            // ★ 费用槽组合也要转过去 —— 多色混合槽（"虚色或红色"那种）
            //   必须按游戏注册过的**有序组合**判定，否则游戏会在查图标时抛 KeyNotFoundException。
            foreach (var combo in caps.AvailableCostCombos)
            {
                var converted = new List<ColorE>();
                foreach (var c in combo) { converted.Add(GameCapabilitiesProbe.ToGameColor(c)); }
                assemblerCaps.AvailableCostCombos.Add(converted);
            }

            var assembler = new ContentAssembler(log, assemblerCaps);

            foreach (var mod in load.Mods)
            {
                if (mod.Failed) { continue; }

                // 本 mod 成功装配的卡（装配 + 集合注册 + 读回断言都要用同一份清单）
                var modCards = new List<CardSo>();
                // 本 mod 成功装配的遗物（同上；遗物与卡有**完全同构**的池子缺陷）
                var modRelics = new List<RelicSo>();
                // 本 mod 成功装配的能力（装配 + 读回断言共用）
                var modAbilities = new List<Ability.AbilitySo>();

                // 本 mod 在 i18n 的 FlavorText 表里声明过哪些键。
                // 用途：只有作者**确实写了**风味文本，才给卡打开游戏的那个显示开关
                // （详见 ContentAssembler.AssembleCard 的 2b 步骤注释）。
                var flavorTextKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var i18n in mod.I18n)
                {
                    if (i18n == null || i18n.Tables == null) { continue; }
                    Dictionary<string, string> table;
                    if (i18n.Tables.TryGetValue("FlavorText", out table) && table != null)
                    {
                        foreach (var k in table.Keys) { flavorTextKeys.Add(k); }
                    }
                }

                foreach (var card in mod.Cards)
                {
                    try
                    {
                        // mod.Path = 这个 mod 的目录完整路径，供"自带卡面图"解析相对路径
                        var so = assembler.AssembleCard(card, result, flavorTextKeys.Contains(card.Id), mod.Path);
                        if (so != null) { modCards.Add(so); }
                    }
                    catch (Exception ex)
                    {
                        if (strictMode) { throw; }
                        // 单张卡的失败不影响其他卡与 mod
                        result.Errors.Add(card.Id + "：装配抛出异常 —— " + ex.Message);
                        log.Error("装配卡失败：" + card.Id, ex);
                    }
                }

                foreach (var relic in mod.Relics)
                {
                    try
                    {
                        var rso = assembler.AssembleRelic(relic, result);
                        if (rso != null) { modRelics.Add(rso); }
                    }
                    catch (Exception ex)
                    {
                        if (strictMode) { throw; }
                        result.Errors.Add(relic.Id + "：装配抛出异常 —— " + ex.Message);
                        log.Error("装配遗物失败：" + relic.Id, ex);
                    }
                }

                // 关键词：只核验"实现类存在"，不构造实例（构造需要 CardBase，装配期拿不到）。
                // 这一步的价值在于拦住"类不存在 → 游戏静默退化成基类"这个最危险的失败模式。
                foreach (var keyword in mod.Keywords)
                {
                    try
                    {
                        assembler.AssembleKeyword(keyword, _registry, result);
                    }
                    catch (Exception ex)
                    {
                        if (strictMode) { throw; }
                        result.Errors.Add(keyword.Id + "：核验抛出异常 —— " + ex.Message);
                        log.Error("核验关键词失败：" + keyword.Id, ex);
                    }
                }

                // ---- 能力：装配 + 读回断言 ----
                // 与卡/遗物不同，能力**没有"池子会被重建"的问题**（桶是初始化时一次性建的），
                // 但它有更严重的一面：取不到时游戏**会崩**（AbilityUtil 的 null 检查是假的）。
                foreach (var ability in mod.Abilities)
                {
                    try
                    {
                        var aso = assembler.AssembleAbility(ability, result);
                        if (aso != null) { modAbilities.Add(aso); }
                    }
                    catch (Exception ex)
                    {
                        if (strictMode) { throw; }
                        result.Errors.Add(ability.Id + "：装配抛出异常 —— " + ex.Message);
                        log.Error("装配能力失败：" + ability.Id, ex);
                    }
                }

                try
                {
                    assembler.VerifyAbilitiesReadBack(mod.Manifest.Id, modAbilities, result);
                }
                catch (Exception ex)
                {
                    if (strictMode) { throw; }
                    result.Errors.Add(mod.Manifest.Id + "：能力读回断言抛出异常 —— " + ex.Message);
                }

                // ---- 卡池注册：必须走"集合"这条路，否则会被 SetCampaignCardPool 冲掉 ----
                try
                {
                    assembler.RegisterCardCollection(mod.Manifest.Id, modCards, result);
                }
                catch (Exception ex)
                {
                    if (strictMode) { throw; }
                    result.Warnings.Add(mod.Manifest.Id + "：注册卡集合抛出异常 —— " + ex.Message);
                }

                // ---- 模组图鉴：把这批内容登记给图鉴 UI（只读展示、全部直接显示）----
                try
                {
                    var kwIds = new List<string>();
                    foreach (var kw in mod.Keywords) { if (kw != null) { kwIds.Add(kw.Id); } }
                    var abIds = new List<string>();
                    foreach (var ab in mod.Abilities) { if (ab != null) { abIds.Add(ab.Id); } }
                    ModCompendium.SetContent(mod.Manifest.Id, modCards, modRelics, kwIds, abIds, log);
                }
                catch (Exception ex)
                {
                    if (strictMode) { throw; }
                    result.Warnings.Add(mod.Manifest.Id + "：登记模组图鉴内容抛出异常 —— " + ex.Message);
                }

                // ---- 读回断言：确认"游戏真的取得到"（而不只是"我写进去了"）----
                try
                {
                    assembler.VerifyCardsReadBack(mod.Manifest.Id, modCards, result);
                }
                catch (Exception ex)
                {
                    if (strictMode) { throw; }
                    result.Errors.Add(mod.Manifest.Id + "：读回断言抛出异常 —— " + ex.Message);
                }

                // ---- 遗物：与卡池**完全同构**的缺陷，修法也一样 ----
                // SetCampaignRelicPool() 会 Clear 掉 _campaignRelicPool 并只从
                // _unlockRelicDataCollections 重灌 → 直接塞字典不够，必须"建集合 + 重建池子"成对做。
                try
                {
                    assembler.RegisterRelicCollection(mod.Manifest.Id, modRelics, result);
                }
                catch (Exception ex)
                {
                    if (strictMode) { throw; }
                    result.Warnings.Add(mod.Manifest.Id + "：注册遗物集合抛出异常 —— " + ex.Message);
                }

                try
                {
                    assembler.VerifyRelicsReadBack(mod.Manifest.Id, modRelics, result);
                }
                catch (Exception ex)
                {
                    if (strictMode) { throw; }
                    result.Errors.Add(mod.Manifest.Id + "：遗物读回断言抛出异常 —— " + ex.Message);
                }

                // ---- 职业声明（可选）：mod 用 IModClassProvider 声明"新球 + 新职业 + 初始牌组" ----
                // 职业是**数据**（一个 CardDateCollection + 一条 StartColorESDecks）；
                // 但组成它的颜色球需要 CareerPatch 把原版没启用的 Black 放开（平台启动时已挂钩）。
                try
                {
                    CareerAssembler.AssembleFromModAssembly(
                        mod.Manifest.Id, mod.EntryAssemblyPath, modCards, modRelics, log, result);
                }
                catch (Exception ex)
                {
                    if (strictMode) { throw; }
                    result.Warnings.Add(mod.Manifest.Id + "：职业装配抛出异常 —— " + ex.Message);
                }

                // ---- 重建守卫：登记"游戏重建数据容器后要重做的注册" ----
                // 为什么需要：RelicDataManager.LoadAllRelic() 与 AbilityDataManager.LoadAllAbility()
                // 都会先 Clear 再只从 Resources 重灌 —— 平台运行期注册的遗物/能力会被无声冲掉。
                // （本类缺陷第三次出现；前两次是战役卡池与战役遗物池。）
                // 做法：不跟重建对抗，挂在重建之后重新注册。
                {
                    var relicsForReapply = new List<RelicSo>(modRelics);
                    string reapplyModId = mod.Manifest.Id;
                    RebuildGuard.AddReapply(delegate
                    {
                        assembler.ReRegisterRelics(reapplyModId, relicsForReapply, result);
                        ContentAssembler.ReRegisterAbilities(result);
                    }, log);
                }
            }

            // 装挂钩（只要有内容登记过就装；幂等）
            try { RebuildGuard.Install(log); }
            catch (Exception ex) { log.Warn("重建守卫安装失败：" + ex.Message); }

            return result;
        }

        /// <summary>
        /// 把探测到的能力表打进日志。
        /// </summary>
        /// <remarks>
        /// 这条日志是**排查"作者说没效果"的第一手证据**：
        /// 它能直接回答"本 build 到底认不认你用的那个颜色/触发点"。
        /// </remarks>
        private static void DumpCapabilities(IGameLog log, GameCapabilities caps, ProbeSummary probe)
        {
            log.Info("=== 能力表（本 build 实测）===");
            log.Info("  可用费用色：" + Join(caps.AvailableCostColors));
            log.Info("  原版卡名：" + caps.VanillaCardNames.Count + " 张");
            log.Info("  原版遗物名：" + caps.VanillaRelicNames.Count + " 件");
            log.Info("  关键词：" + caps.KnownKeywords.Count + " 个");
            log.Info("  触发点：" + caps.KnownTriggerNames.Count + " 个（取自游戏**实际发出**的名字，不是 AllTriggerType 枚举）");
            log.Info("  已注册自定义效果：" + caps.KnownCustomEffectIds.Count + " 个");
            log.Info("=== 能力表结束 ===");
        }

        private static string Join(IReadOnlyList<ManaColor> colors)
        {
            if (colors == null || colors.Count == 0) { return "(无 —— 探测失败或该 build 未注册任何颜色)"; }
            var parts = new string[colors.Count];
            for (int i = 0; i < colors.Count; i++) { parts[i] = colors[i].ToString(); }
            return string.Join(", ", parts);
        }
    }

    /// <summary>构建内容注册表：把 AppDomain 的程序集喂给它。</summary>
    public static class ContentRegistryFactory
    {
        private static ContentRegistry _cached;

        /// <summary>构建（并从此刻起缓存）。</summary>
        public static ContentRegistry BuildFromAppDomain(IGameLog log)
        {
            var assemblies = new List<System.Reflection.Assembly>();
            try
            {
                assemblies.AddRange(AppDomain.CurrentDomain.GetAssemblies());
            }
            catch (Exception ex)
            {
                if (log != null) { log.Warn("取 AppDomain 程序集失败：" + ex.Message); }
            }

            _cached = ContentRegistry.Build(
                assemblies,
                delegate (string s) { if (log != null) { log.Info(s); } });
            return _cached;
        }

        /// <summary>取缓存的注册表；没有就建。</summary>
        public static ContentRegistry Ensure(IGameLog log)
        {
            if (_cached == null) { return BuildFromAppDomain(log); }
            return _cached;
        }
    }
}
