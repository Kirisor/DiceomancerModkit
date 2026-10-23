// =============================================================================
//  ModKit.Game · Transpiler 桥（平台启动序列的第 0 步）
//
//  作用：把游戏 4 个内容工厂里的 `Type.GetType(string)` 整体换成
//        `ModKitBridge.Resolve(string)` —— 后者搜索**全部已加载程序集**，
//        因此 mod 程序集里的内容类才可能被找到。
//
//  ★ 为什么这一步是硬前置（不是可选优化）：
//    Type.GetType(string) 只搜索「调用方程序集 + 核心库」；调用方是 Assembly-CSharp，
//    mod 的效果类不在里面 → 返回 null → EffectUtils 第 41 行 effectBase.EffectOwner
//    直接 NullReferenceException。**未装桥就装配 = 静默造出必崩的卡。**
//
//  ★ 一个必须避开的坑：
//    Transpiler 必须**就地改** CodeInstruction 的 opcode/operand。
//    若新建一个 CodeInstruction 去替换原指令，会丢掉挂在原指令上的
//    `labels`（跳转目标）与 `blocks`（异常块边界）→ 分支目标失效
//    → **Mono 无托管异常、无 WER 事件、无崩溃转储地直接中止进程**
//    （表现为日志戛然而止）。
//
//  ★ 覆盖范围：4 个工厂（同一个病根）。未覆盖 Story.StoryConsequence 的剧情后果类
//    —— 现役实现也没找到其方法名（诚实记录，见 ApiSurface.md）。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ModKit.Core;

namespace ModKit.Game
{
    /// <summary>
    /// 游戏侧调用点。Transpiler 把 <c>Type.GetType(string)</c> 的调用换成这里。
    /// </summary>
    /// <remarks>
    /// <b>必须是静态方法、签名与 <c>Type.GetType(string)</c> 兼容</b>
    /// （返回 <see cref="Type"/>、单参数 <see cref="string"/>），
    /// 否则 IL 里的 <c>call</c> 指令替换后栈不平衡 → 运行时炸。
    /// </remarks>
    public static class ModKitBridge
    {
        private static ContentRegistry _registry;

        /// <summary>已安装的桥的哨兵计数 —— 写进日志供验收核对。</summary>
        public static int ResolveCalls { get { return _registry == null ? 0 : _registry.ResolveCalls; } }
        public static int ResolveHits { get { return _registry == null ? 0 : _registry.ResolveHits; } }
        public static int ResolveMisses { get { return _registry == null ? 0 : _registry.ResolveMisses; } }

        /// <summary>安装注册表。必须在任何内容装配之前调用。</summary>
        public static void Install(ContentRegistry registry)
        {
            if (registry == null) { throw new ArgumentNullException("registry"); }
            _registry = registry;
        }

        /// <summary>是否已安装（装配前必须为 true）。</summary>
        public static bool Installed { get { return _registry != null; } }

        /// <summary>
        /// 替代 <c>Type.GetType(string)</c>。
        /// </summary>
        /// <remarks>
        /// 落空时返回 null —— 与原版 <c>Type.GetType</c> 语义一致。
        /// 刻意不抛异常：游戏原方法对 null 的失败模式（NRE / 静默降级）
        /// 必须保持不变（第 1 步不变量 I8）。
        /// </remarks>
        public static Type Resolve(string typeName)
        {
            var reg = _registry;
            if (reg == null)
            {
                // 未安装就落回原版语义 —— 但会记一次警告，方便定位"桥没装好"
                return Type.GetType(typeName);
            }
            return reg.Resolve(typeName);
        }
    }

    /// <summary>
    /// 把 <c>Type.GetType(string)</c> 替换成 <see cref="ModKitBridge.Resolve"/> 的 Transpiler。
    /// </summary>
    internal static class TypeGetTypeTranspiler
    {
        internal static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(Type), nameof(Type.GetType), new[] { typeof(string) });
            var replacement = AccessTools.Method(typeof(ModKitBridge), nameof(ModKitBridge.Resolve));

            var list = new List<CodeInstruction>(instructions);

            if (original == null || replacement == null)
            {
                // 找不到就原样放行 —— FailOpen：补丁不生效好过让游戏崩
                ModKitLog.Error(
                    "Transpiler 找不到 Type.GetType(string) 或 ModKitBridge.Resolve，补丁未生效。" +
                    " original=" + (original == null ? "null" : "ok") +
                    " replacement=" + (replacement == null ? "null" : "ok"));
                return list;
            }

            // 诊断：把方法体里所有"调用了某方法"的指令打出来（仅在 0 命中时输出）
            var callLog = new List<string>();

            var result = new List<CodeInstruction>();
            int replaced = 0;

            foreach (var instruction in list)
            {
                if (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                {
                    var m = instruction.operand as System.Reflection.MethodBase;
                    if (m != null && m.DeclaringType != null &&
                        (m.DeclaringType == typeof(Type) || m.DeclaringType.Name == "Type"))
                    {
                        callLog.Add(m.DeclaringType.FullName + "." + m.Name +
                            "(" + JoinParams(m) + ")");
                    }
                }

                if (instruction.Calls(original))
                {
                    // ★★ 就地改，不要 new —— 见文件头的致命坑说明。
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    replaced++;
                }
                result.Add(instruction);
            }

            ModKitLog.Info("Transpiler 替换 Type.GetType 调用 " + replaced + " 处（指令总数 " +
                list.Count + "）。");

            if (replaced == 0)
            {
                // 这一条是排查"补丁挂上但没生效"的关键证据。
                // 打**全部**方法调用（不只 Type.*）—— 因为可能根本不是 Type.GetType。
                var allCalls = new List<string>();
                var foreignResolvers = new List<string>();
                foreach (var ins in list)
                {
                    if (ins.opcode == OpCodes.Call || ins.opcode == OpCodes.Callvirt)
                    {
                        var mm = ins.operand as System.Reflection.MethodBase;
                        if (mm != null)
                        {
                            string decl = mm.DeclaringType == null ? "?" : mm.DeclaringType.Name;
                            allCalls.Add(decl + "." + mm.Name);

                            // ★ 共存冲突检测（实机发现）：另一个 mod 可能已经装了自己的桥，
                            //   把 Type.GetType 换成了它自己的解析器。此时本桥测到 0 处，
                            //   若只记一条普通警告，作者会以为"平台正常但内容没效果" ——
                            //   属于 C9 口径下不可接受的**静默失效**。所以这里点名。
                            if (mm.Name.IndexOf("Resolve", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                decl.IndexOf("Resolver", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                foreignResolvers.Add(decl + "." + mm.Name);
                            }
                        }
                        else
                        {
                            allCalls.Add("(non-MethodBase operand: " +
                                (ins.operand == null ? "null" : ins.operand.GetType().Name) + ")");
                        }
                    }
                }

                if (foreignResolvers.Count > 0)
                {
                    ModKitLog.Error("★ 桥冲突：本方法里的类型解析已被**另一个 mod 的解析器**接管（" +
                        string.Join(", ", foreignResolvers.ToArray()) + "）。" +
                        "平台与它的桥互斥 —— 后装者会测到 0 处替换并静默失效。" +
                        "请只保留一个：移除其它装了 Type.GetType 桥的 mod，或改用其中一方。");
                }
                else
                {
                    ModKitLog.Warn("  未命中。本方法里有 " + allCalls.Count + " 条调用指令：" +
                        (allCalls.Count == 0 ? "(无)" : string.Join(", ", allCalls.ToArray())));
                }
            }

            return result;
        }

        private static string JoinParams(System.Reflection.MethodBase m)
        {
            try
            {
                var ps = m.GetParameters();
                var parts = new string[ps.Length];
                for (int i = 0; i < ps.Length; i++) { parts[i] = ps[i].ParameterType.Name; }
                return string.Join(", ", parts);
            }
            catch
            {
                return "?";
            }
        }
    }

    /// <summary>日志出口。避免 Game 层各处重复拼接前缀。</summary>
    internal static class ModKitLog
    {
        internal static IGameLog Sink;

        internal static void Info(string m) { if (Sink != null) { Sink.Info(m); } }
        internal static void Warn(string m) { if (Sink != null) { Sink.Warn(m); } }
        internal static void Error(string m) { if (Sink != null) { Sink.Error(m); } }
    }

    // ---------------------------------------------------------------------
    // 4 个内容工厂的补丁
    // ---------------------------------------------------------------------

    /// <summary>卡牌/能力效果类工厂。</summary>
    [HarmonyPatch(typeof(Utilities.EffectUtils), nameof(Utilities.EffectUtils.GenerateEffectFromEffectInfo))]
    internal static class PatchEffectFactory
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            return TypeGetTypeTranspiler.Replace(instructions);
        }
    }

    /// <summary>关键词类工厂。</summary>
    [HarmonyPatch(typeof(Keyword.CardKeyWordUtil), nameof(Keyword.CardKeyWordUtil.GenerateKeywordFromKeywordInfo))]
    internal static class PatchKeywordFactory
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            return TypeGetTypeTranspiler.Replace(instructions);
        }
    }

    /// <summary>
    /// 遗物效果类工厂。
    /// </summary>
    /// <remarks>
    /// 注意：这是**私有方法**，只能按**字符串名**绑定。
    /// 游戏一旦给它改名，补丁会失配 —— 表现为启动时报错或静默失效
    /// （第 9 步 §10.1 记录的"补丁绑定方法签名"风险）。
    /// </remarks>
    [HarmonyPatch(typeof(Relic.RelicManager), "GenerateRelicEffectFromRelicInfo")]
    internal static class PatchRelicFactory
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            return TypeGetTypeTranspiler.Replace(instructions);
        }
    }

    /// <summary>特殊状态类工厂（同为私有方法，按字符串名绑定）。</summary>
    [HarmonyPatch(typeof(Combat.Character), "GenSpecialState")]
    internal static class PatchSpecialStateFactory
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            return TypeGetTypeTranspiler.Replace(instructions);
        }
    }

    /// <summary>
    /// 安装桥并获得哨兵计数。
    /// </summary>
    /// <remarks>
    /// 用 <c>harmony.PatchAll</c> 之后调 <c>GetPatchedMethods()</c> 核对命中数 ——
    /// 这是项目既有的做法（现役 Plugin.cs 把"补丁数量: 10"打进日志当哨兵）。
    /// 若数量不足 4，说明某个私有方法名漂移了，必须报错而不是静默继续。
    /// </remarks>
    public static class BridgeInstaller
    {
        /// <summary>期望的补丁数量（4 个内容工厂）。</summary>
        public const int ExpectedPatchCount = 4;

        /// <summary>最近一次安装实际命中的补丁数（供哨兵汇总读取）。</summary>
        public static int InstalledPatchCount { get; private set; }

        /// <summary>是否已成功安装过（幂等保护：Awake 可能因 BepInEx 重载而多次执行）。</summary>
        public static bool Installed { get; private set; }

        /// <summary>
        /// 安装桥。返回实际命中的补丁数。
        /// </summary>
        /// <param name="registry">内容注册表（必须先构建）。</param>
        /// <param name="log">日志。</param>
        public static int Install(ContentRegistry registry, IGameLog log)
        {
            if (registry == null) { throw new ArgumentNullException("registry"); }
            if (Installed)
            {
                if (log != null) { log.Info("Transpiler 桥已安装过，跳过（幂等）。"); }
                ModKitBridge.Install(registry);
                return InstalledPatchCount;
            }

            ModKitLog.Sink = log;

            ModKitBridge.Install(registry);

            var harmony = new Harmony("modkit.bridge");

            // ★ 逐个补丁类安装，单个失败只跳过它自己。
            //   2026-10-23 实机事故：某个可选补丁的目标方法在游戏里不存在
            //   （LocalizedStringDatabase.GetTable(string) 不在该类上声明），
            //   PatchAll(assembly) 直接抛异常 → 整座桥判定安装失败 → 平台整体停用。
            //   一个"锦上添花"的补丁不该让整个平台下线。
            int failedTypes = 0;
            Type[] allTypes;
            try { allTypes = typeof(BridgeInstaller).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex)
            {
                var loaded = new List<Type>();
                foreach (var t in ex.Types) { if (t != null) { loaded.Add(t); } }
                allTypes = loaded.ToArray();
            }

            foreach (var type in allTypes)
            {
                if (type == null) { continue; }
                try { harmony.PatchAll(type); }
                catch (Exception ex)
                {
                    failedTypes++;
                    if (log != null)
                    {
                        log.Warn("补丁跳过：" + type.Name + " —— " + ex.GetType().Name +
                                 "（该功能不生效，其余照常）");
                    }
                }
            }
            if (failedTypes > 0 && log != null)
            {
                log.Warn("本次有 " + failedTypes + " 个补丁类安装失败（不影响平台其它功能）。");
            }

            int patched = 0;
            foreach (var m in harmony.GetPatchedMethods())
            {
                patched++;
                if (log != null)
                {
                    log.Info("  patched → " + m.DeclaringType.FullName + "." + m.Name);
                }
            }

            InstalledPatchCount = patched;
            Installed = true;

            if (log != null)
            {
                log.Info("Transpiler 桥：命中 " + patched + " / 期望 " + ExpectedPatchCount + " 个工厂。");
                // 哨兵：数量不足是**契约漂移**的信号，必须显式报出来
                if (patched < ExpectedPatchCount)
                {
                    log.Warn("补丁数不足 —— 可能有私有方法被改名（GenerateRelicEffectFromRelicInfo / GenSpecialState）。" +
                             "对应内容类型将静默失效，请核对 ApiSurface.md。");
                }
            }

            return patched;
        }
    }
}
