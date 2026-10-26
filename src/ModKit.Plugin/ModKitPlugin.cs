// =============================================================================
//  ModKit.Plugin · BepInEx 插件入口
//
//  这是平台装进游戏的**唯一入口**。它刻意保持极薄：
//    ① 在 Awake 里做"不需要游戏数据"的事（装 Transpiler 桥 + 读配置）；
//    ② 挂一个 Postfix 到 DataManager.LoadAllGameData —— 真正的流水线在那里跑。
//
//  ★ 为什么不能直接在 Awake 里跑流水线（三条实测依据）：
//    ① 探测要读 CardDataManager / RelicDataManager，而它们由
//       DataManager.LoadAllGameData() 才 new 出来（GameData/DataManager.cs:58-66）；
//    ② **遗物有时序陷阱**：LoadAllGameData 每次都会 new 掉整个 RelicDataManager
//       并 Clear 掉 _allRelicDataCollections / _allStandPoolRelicSo
//       （唯独 _allInGameRelicSo 用 TryAdd 能存活）→ 注入必须在它**之后**；
//    ③ 插件自己的 MonoBehaviour 会在**首个场景加载时被销毁**
//       （DontDestroyOnLoad 被拒）
//       → 常驻逻辑不能挂在插件实例上。
//
//  ★ 必须在 Awake 里装桥（不能等到 LoadAllGameData）：
//    Transpiler 桥负责让游戏找得到 mod 的内容类。它要在**任何内容类被实例化之前**
//    生效。放在 Awake 最早处是最安全的。
// =============================================================================

using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using ModKit.Core;
using ModKit.Game;
using Utilities;

namespace ModKit.Plugin
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class ModKitPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "diceomancer.modkit";
        public const string PluginName = "DICEOMANCER ModKit (Mod 平台)";
        public const string PluginVersion = "0.1.0";

        /// <summary>总开关。关掉即不加载任何 mod 内容，游戏按原版跑。</summary>
        internal static ConfigEntry<bool> CfgEnabled;

        /// <summary>mod 根目录（留空用默认的 BepInEx\DiceomancerMods）。</summary>
        internal static ConfigEntry<string> CfgModsRoot;

        /// <summary>严格模式：内容装配出错时抛异常而不是降级（**仅测试期使用**）。</summary>
        internal static ConfigEntry<bool> CfgStrictMode;

        /// <summary>是否把探测到的能力表打进日志（排查"作者说没效果"时必开）。</summary>
        internal static ConfigEntry<bool> CfgDumpCapabilities;

        /// <summary>验收用：进入第一场战斗发牌后把 mod 内容发给玩家（默认 false）。</summary>
        /// <remarks>
        /// ★ 只在需要一次性验完所有内容类型时开：它会凭空给玩家发卡/遗物/能力，**改变玩法**。
        ///   正式游玩必须为 false。挂钩点在 CardManager.DrawCards 之后，
        ///   所以不会被初始发牌冲掉（上一版就栽在这里）。
        /// </remarks>
        internal static ConfigEntry<bool> CfgGrantOnFirstBattle;

        /// <summary>
        /// 验收用：GrantOnFirstBattle 发牌时**每张 mod 卡发几张**（默认 1）。
        /// 像「奶蛙」这种"同一张卡互相联动"的关键词，牌库里只有 1 张根本测不出来。
        /// </summary>
        internal static ConfigEntry<int> CfgGrantCardCopies;

        /// <summary>诊断：把探测到的原版音效名整张写进日志（默认 false，避免刷屏）。</summary>
        internal static ConfigEntry<bool> CfgSfxDump;

        /// <summary>流水线钩子用的日志（Hook 类是静态的，拿不到实例日志）。</summary>
        internal static BepInExGameLog HookLog;

        /// <summary>按名字 dump 卡的完整结构（逗号/分号分隔）。匹配规则：内部卡名精确相等，
        /// **或**中文卡名（<c>CardName</c> 表里的显示名）包含该串。留空 = 不 dump。
        /// </summary>
        /// <remarks>
        /// ★ 为什么需要它：卡的**中文名与内部名是两套东西** —— 中文名在本地化表里，
        ///   调试时手里往往只有中文名（"这张卡是怎么拼出来的"），而这个对照关系
        ///   光看反编译代码是查不到的。日志里会同时打出内部名与中文名。
        /// </remarks>
        internal static ConfigEntry<string> DumpCards;

        /// <summary>按名字 dump 原版**遗物**的完整声明结构（逗号/分号分隔）。
        /// 取值语义：<c>*</c> = dump **全部**原版遗物；否则内部名精确相等，**或**中文名
        /// （本地化 <c>Relic</c> 表里的显示名）包含该串。留空 = 不 dump。
        /// </summary>
        /// <remarks>
        /// ★ 为什么需要它：卡有 <c>DumpCards</c> 这条通道，**遗物此前一条都没有** ——
        ///   原版遗物的真实参数（<c>relicMainType</c> / 充能要求 / 触发字段 / 效果数字）
        ///   是**资产里的序列化值**，读反编译源码只能看到字段名、看不到取值。
        ///   只读，不写任何游戏数据。
        /// </remarks>
        internal static ConfigEntry<string> DumpRelics;

        /// <summary>把**完整能力表**（卡名/遗物名/费用槽组合/关键词/触发点）写成 JSON 文件。</summary>
        /// <remarks>
        /// ★ 与 <see cref="CfgDumpCapabilities"/> 的分工：后者只决定"打不打进日志"，
        ///   本项决定"落不落盘"。日志里只有遗物**数量**，拿不到那 ~205 个名字；
        ///   要修 `capabilities.json` 的遗物缺口，必须开这一项。
        /// </remarks>
        internal static ConfigEntry<bool> CfgDumpCapabilitiesFile;

        /// <summary>导出卡清单与卡面图（PNG）。</summary>
        internal static ConfigEntry<bool> CfgExportCardsArt;

        /// <summary>除缩略图外再导出一份原图（默认关 —— 文档只用到缩略图）。</summary>
        internal static ConfigEntry<bool> CfgExportCardsArtFull;

        /// <summary>缩略图最长边（像素）。</summary>
        internal static ConfigEntry<int> CfgExportCardsArtThumbEdge;

        /// <summary>导出卡的总量上限（安全阀）。</summary>
        internal static ConfigEntry<int> CfgExportCardsArtMax;

        /// <summary>已存在的 PNG 是否复用（默认复用 —— 避免每次启动重复做 GPU 取回 + PNG 编码）。</summary>
        internal static ConfigEntry<bool> CfgExportCardsArtReuse;

        /// <summary>取回像素时是否上下翻转（实机若发现导出的图是倒的，把它设 true）。</summary>
        internal static ConfigEntry<bool> CfgExportCardsArtFlipY;

        /// <summary>诊断导出目录。留空 = <c>&lt;游戏根&gt;\BepInEx\ModKitDump</c>。</summary>
        internal static ConfigEntry<string> CfgDumpDir;

        /// <summary>验收用：把 mod 卡放进起始牌组（默认 false）。</summary>
        /// <remarks>
        /// ★ 只为验证"游戏内表现"而存在：mod 卡在 534+ 张的池子里随机抽到概率极低，
        ///   要让渲染探针有确定性的输入，就得让它必定出现在起手牌里。
        ///   **默认关闭** —— "作者的卡必定进起始牌组"是玩法改动，不该由平台擅自决定。
        /// </remarks>

        private void Awake()
        {
            var log = new BepInExGameLog("[ModKit]");
            HookLog = log;

            CfgEnabled = Config.Bind("General", "Enabled", true,
                "是否启用 Mod 平台。关闭后不加载任何 mod 内容（游戏按原版跑）。");
            CfgModsRoot = Config.Bind("General", "ModsRoot", "",
                "mod 根目录。留空 = <游戏根>\\BepInEx\\DiceomancerMods");
            CfgStrictMode = Config.Bind("Diagnostics", "StrictMode", false,
                "严格模式：内容出错时抛异常而不是降级。仅测试期使用——正式游玩必须为 false。");
            CfgDumpCapabilities = Config.Bind("Diagnostics", "DumpCapabilities", true,
                "把启动探测到的能力表（可用费用色/原版卡名数/触发点数等）打进日志。");
            CfgGrantOnFirstBattle = Config.Bind("Diagnostics", "GrantOnFirstBattle", false,
                "验收用：进入第一场战斗发牌后把 mod 内容（卡/遗物/能力）直接发给玩家。会改变玩法，正式游玩请设 false。");

            CfgGrantCardCopies = Config.Bind("Diagnostics", "GrantCardCopies", 1,
                "验收用：GrantOnFirstBattle 发牌时每张 mod 卡发几张（默认 1）。" +
                "要测同一张卡多张联动（如「奶蛙」的关键词），调到 2~3。");

            // 音效（2026-10-15 新增）：默认关。打开后平台会把探测到的**全部原版音效名**写进日志，
            // 方便作者挑一个名字用在 ModKitSfx.Play("…")。
            CfgSfxDump = Config.Bind("Diagnostics", "SfxDump", false,
                "把游戏里可用的原版音效名整张打进日志（挑音效名用）。默认关。");

            // 按名字 dump 指定卡的完整结构（排查"某张卡是怎么拼出来的"用）。
            // 匹配规则：内部卡名精确相等，**或**中文卡名（CardName 表）包含该串。逗号/分号分隔。
            DumpCards = Config.Bind("Diagnostics", "DumpCards", "",
                "按名字 dump 卡的完整声明结构（效果/数字/关键词/升级）：内部名精确匹配 或 中文卡名包含匹配；逗号分隔。留空=不 dump。");
            ModKit.Game.GameCapabilitiesProbe.DumpCardsSpec = DumpCards.Value;

            // 按名字 dump 指定**遗物**的完整结构（与 DumpCards 同构的第二条诊断通道，2026-10-14 新增）。
            // "*" = dump 全部原版遗物；否则内部名精确匹配 或 中文名（Relic 表）包含匹配。逗号/分号分隔。
            DumpRelics = Config.Bind("Diagnostics", "DumpRelics", "",
                "按名字 dump 原版遗物的完整声明结构（relicInfo 全字段 / 触发字段 11 个 / 效果）：" +
                "\"*\" = dump 全部原版遗物；否则内部名精确匹配 或 中文遗物名包含匹配；逗号分隔。留空=不 dump。");
            ModKit.Game.GameCapabilitiesProbe.DumpRelicsSpec = DumpRelics.Value;

            // ---- 诊断导出（2026-10-14 新增）：完整能力表落盘 + 卡清单/卡面图 ----
            // 每一条都只影响"导出"自身，关掉即完全不写盘。配置键一律新增，不改动既有键的语义。
            CfgDumpCapabilitiesFile = Config.Bind("Diagnostics", "DumpCapabilitiesFile", true,
                "把完整能力表（原版卡名/完整遗物名/费用槽组合/关键词/触发点）写成 JSON 文件。" +
                "关闭后只打日志（日志里没有遗物名字）。");
            CfgExportCardsArt = Config.Bind("Diagnostics", "ExportCardsArt", true,
                "导出卡清单 cards.json 与卡面缩略图 PNG。首次运行会有几秒一次性开销；" +
                "拿到文件后可设为 false。");
            CfgExportCardsArtFull = Config.Bind("Diagnostics", "ExportCardsArtFull", false,
                "除缩略图外再导出一份原尺寸卡图（体积大，文档用不到，默认关）。");
            CfgExportCardsArtThumbEdge = Config.Bind("Diagnostics", "ExportCardsArtThumbEdge", 256,
                "卡面缩略图的最长边（像素）。");
            CfgExportCardsArtMax = Config.Bind("Diagnostics", "ExportCardsArtMax", 2000,
                "导出卡的总量上限（安全阀）。超过时按内部名排序取前 N 张并在清单里标注。");
            CfgExportCardsArtReuse = Config.Bind("Diagnostics", "ExportCardsArtReuse", true,
                "已存在的卡图 PNG 直接复用、不重新生成（避免每次启动都做 GPU 取回 + PNG 编码）。" +
                "改了缩略图尺寸或要重新生成时设为 false。");
            CfgExportCardsArtFlipY = Config.Bind("Diagnostics", "ExportCardsArtFlipY", false,
                "取回像素时上下翻转。默认 false；若实机发现导出的卡图是倒的，改成 true 再导出一次。");
            CfgDumpDir = Config.Bind("Diagnostics", "DumpDir", "",
                "诊断导出目录。留空 = <游戏根>\\BepInEx\\ModKitDump");

            ModKit.Game.CapabilitiesDump.Enabled = CfgDumpCapabilitiesFile.Value;
            ModKit.Game.CardArtExporter.Enabled = CfgExportCardsArt.Value;
            ModKit.Game.CardArtExporter.ExportFullArt = CfgExportCardsArtFull.Value;
            ModKit.Game.CardArtExporter.ThumbnailMaxEdge = CfgExportCardsArtThumbEdge.Value;
            ModKit.Game.CardArtExporter.MaxCards = CfgExportCardsArtMax.Value;
            ModKit.Game.CardArtExporter.ReuseExistingArt = CfgExportCardsArtReuse.Value;
            ModKit.Game.CardArtExporter.FlipVertically = CfgExportCardsArtFlipY.Value;
            ModKit.Game.CapabilitiesDump.DirectoryOverride = CfgDumpDir.Value;
            ModKit.Game.CardArtExporter.DirectoryOverride = CfgDumpDir.Value;

            // 把验收开关交给发放模块（与 CfgEnabled 等一起读，避免静态状态漂移）
            ModKit.Game.VerificationGrant.Enabled = CfgGrantOnFirstBattle.Value;
            ModKit.Game.VerificationGrant.CardCopies = CfgGrantCardCopies.Value < 1 ? 1 : CfgGrantCardCopies.Value;

            // 音效针脚（2026-10-15）：这里只装配 Func，**不碰 UtilSoundDataManager/AudioManager** ——
            // 插件 Awake 时游戏还没 Awake，名单留到第一次真正播放时懒探测。
            ModKit.Game.ModAudio.Install(log, CfgSfxDump.Value);


            log.Info(PluginName + " v" + PluginVersion + " 加载中…");
            log.Info("游戏目录: " + Paths.GameRootPath);

            if (!CfgEnabled.Value)
            {
                log.Info("平台已在配置中关闭（Enabled=false），不装桥、不加载 mod。");
                return;
            }

            // ---- 第 0 步：装 Transpiler 桥（架构硬前置）----
            // 必须在任何内容类被实例化之前完成，否则解析落空 → EffectUtils 第 41 行 NRE。
            try
            {
                // ★ 2026-10-26 时序修复：先把各 mod 的入口程序集预加载进来。
                //   继续游戏时 GameManager.Start 会**立刻**反序列化存档并重建卡牌，
                //   卡上的关键词效果要按类名解析成游戏类型；若注册表里还没有 mod 程序集，
                //   Type.GetType 落空 → Activator.CreateInstance(null) → 启动即崩。
                //   预加载只做文件 + 反射，不碰游戏数据，可以在 Awake 安全执行。
                var preloaded = ModAssemblyPreload.Load(CfgModsRoot.Value, log);

                var assemblies = new List<System.Reflection.Assembly>();
                assemblies.AddRange(AppDomain.CurrentDomain.GetAssemblies());
                assemblies.AddRange(preloaded);

                var registry = ContentRegistry.Build(assemblies, delegate (string s) { log.Info(s); });

                // ★ 2026-10-26 时序修复（第二半，缺了它上一轮的修复等于没做完）：
                //   把**效果适配层**也提前接上。ContentRegistry.Resolve 的"现生成适配类"
                //   这一步是委托挂钩（EmitFallback），原来只在流水线里赋值 ——
                //   而继续游戏时存档反序列化发生在流水线**之前** ⇒ 挂钩为 null ⇒
                //   作者自写效果的类型永远生成不出来 ⇒ Activator.CreateInstance(null) 崩溃。
                PlatformRuntime.InstallEarlyTypeFallbacks(log);

                // 记下配置项：继续游戏的"提前装配"发生在 InitializeGame 内部（Game 层），
                // 那边拿不到插件的 ConfigEntry，所以在这里把值传进去。
                PlatformRuntime.RememberRunSettings(
                    CfgModsRoot.Value, CfgStrictMode.Value, CfgDumpCapabilities.Value);

                int patched = BridgeInstaller.Install(registry, log);
                log.Info("Transpiler 桥安装完成，命中 " + patched + " 个工厂。");
            }
            catch (Exception ex)
            {
                // 桥装不上 → 后续装配必然造出必崩的卡 → 干脆整体停用（FailOpen 到"平台不生效"）
                log.Error("Transpiler 桥安装失败，平台已停用（游戏按原版运行）。", ex);
                return;
            }

            // ---- 时序修复：在**场景加载完成后**创建就绪轮询器 ----
            // ★ 为什么不能在这里直接 new GameObject + AddComponent（实测 4 次丢心跳的根因）：
            //   插件 Awake 时**还没有活动场景**，Unity 会拒绝 DontDestroyOnLoad
            //   （日志报「only works for root GameObjects」），对象随后随第一个场景加载被销毁。
            //   症状：AddComponent 成功、"已创建"日志打得出来，
            //   但 Start()/Update() 永远不执行 —— 既不成功也不超时，一片静默。
            //   同一原因也让"在插件上 StartCoroutine"的协程第一次 yield 后再不恢复。
            //   （这条经验上游 mod 已记录在 Diagnostics/ModRunner.cs 的注释里，此处按同一做法处理。）
            // 正确做法：订阅 SceneManager.sceneLoaded，等场景就绪后再创建。
            try
            {
                UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
                log.Info("已订阅 sceneLoaded —— 就绪轮询器将在场景加载完成后创建。");
            }
            catch (Exception ex)
            {
                log.Error("订阅 sceneLoaded 失败，平台已停用（游戏按原版运行）。", ex);
            }
        }

        /// <summary>场景加载完成 → 此时创建常驻对象才有效。</summary>
        private static void OnSceneLoaded(
            UnityEngine.SceneManagement.Scene scene,
            UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            try
            {
                ModKitBootstrap.EnsureCreated();
            }
            catch (Exception ex)
            {
                if (HookLog != null) { HookLog.Error("创建就绪轮询器失败：" + ex.Message, ex); }
            }
        }
    }
}

