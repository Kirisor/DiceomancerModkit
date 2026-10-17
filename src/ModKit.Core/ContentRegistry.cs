// =============================================================================
//  ModKit.Core · 内容类注册表（Type.GetType 的跨程序集替代品）
//
//  背景（本平台的架构硬前置，务必先读懂）：
//
//    游戏用 「字符串 → 类名 → Activator.CreateInstance」的反射工厂构造内容类：
//      EffectUtils.GenerateEffectFromEffectInfo:
//        Type.GetType("Effects.Special.CardEffect.EffectSpEffect" + specialEffectString)
//    而 **Type.GetType(string) 只搜索调用方程序集 + 核心库**。
//    调用方是 Assembly-CSharp → mod 的效果类住在别的程序集里 → 返回 null
//    → 下一行 effectBase.EffectOwner 直接 NullReferenceException。
//
//  所以平台必须提供一个"跨程序集"的解析器，并用项目既有的 Transpiler 手法
//  把游戏里那处 Type.GetType 调用整体替换掉。
//
//  ★ 本类被设计成**纯逻辑 + 可注入程序集集合**：
//    因此"类的命名约定是否正确""扫描能不能找到"这类最容易写错的地方，
//    可以完全脱离游戏、脱离 Harmony，在桌面 CLR 上跑回归。
//    游戏侧只负责把 AppDomain 的程序集喂进来（见 ModKit.Game）。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using ModKit.Api;

namespace ModKit.Core
{
    /// <summary>一次注册表构建的统计 —— 写进哨兵日志。</summary>
    public sealed class RegistrySummary
    {
        public int AppDomainAssemblies;
        public int ScannedTypes;
        public int RegisteredEffects;

        /// <summary>类名符合约定但缺少 public 无参构造的效果类（会被拒绝）。</summary>
        public readonly List<string> RejectedNoDefaultCtor = new List<string>();

        /// <summary>重复的 <c>[ModEffect]</c> id（先注册者保留）。</summary>
        public readonly List<string> DuplicateEffectIds = new List<string>();

        /// <summary>扫描过程中反射失败的类（TypeLoadException 等）。</summary>
        public int ScanFailures;

        public override string ToString()
        {
            return string.Format(
                "程序集 {0} / 类型 {1}｜效果 {2}｜重复 id {3}，无参构造被拒 {4}，扫描失败 {5}",
                AppDomainAssemblies, ScannedTypes, RegisteredEffects,
                DuplicateEffectIds.Count, RejectedNoDefaultCtor.Count, ScanFailures);
        }
    }

    /// <summary>
    /// 内容类注册表：把"游戏可能用名字找的类"索引起来，供 Transpiler 替换后的解析器使用。
    /// </summary>
    /// <remarks>
    /// 两条查找路径：
    /// <list type="number">
    /// <item><b>按完整类型名</b>（游戏原本的调用语义）：命中则直接返回，行为与原版一致；</item>
    /// <item><b>按内容名</b>（平台约定）：<c>EffectSpEffect&lt;名&gt;</c> /
    /// <c>EffectSpAbility&lt;名&gt;</c> /
    /// <c>Relic&lt;名&gt;</c> / <c>CardKeyword&lt;名&gt;</c>，并优先返回
    /// <c>[ModEffect]</c> 标注过的类。</item>
    /// </list>
    /// </remarks>
    public sealed class ContentRegistry
    {
        /// <summary>完整类型名 → 类型（大小写不敏感，兼容游戏拼写）。</summary>
        private readonly Dictionary<string, Type> _byFullName =
            new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        /// <summary>[ModEffect] 的 id → 类型。</summary>
        private readonly Dictionary<string, Type> _byEffectId =
            new Dictionary<string, Type>(StringComparer.Ordinal);

        /// <summary>解析统计，供 Transpiler 日志与哨兵核对。</summary>
        public RegistrySummary Summary { get; private set; }

        /// <summary>构建时喂进来的程序集 —— 用于兜底按类名查找（有些程序集不在 AppDomain 里）。</summary>
        private readonly List<Assembly> _scanAssemblies = new List<Assembly>();

        /// <summary>类名（不含命名空间）→ 类型，来自兜底扫描。</summary>
        private readonly Dictionary<string, Type> _byShortName =
            new Dictionary<string, Type>(StringComparer.Ordinal);

        /// <summary>
        /// 兜底扫描的**全量**类名索引（懒建一次，之后只查字典）。
        /// </summary>
        /// <remarks>
        /// ★ 2026-10-15 实机定位（用户报"鼠标移到卡上，游戏会卡一下"）：
        /// 旧实现是**每次落空都遍历 <c>_scanAssemblies</c> 调 <c>Assembly.GetTypes()</c>** ——
        /// 生产环境喂进来的是 AppDomain 全部程序集（含体量巨大的 Assembly-CSharp），
        /// 而游戏在**每次造卡 / 建卡面提示**时都会拿原版名字来问
        /// （如 <c>Keyword.CardKeywordExert</c>），这些名字**永远不在**平台的注册表里
        /// ⇒ 每问一次就全量扫一遍程序集元数据，落到帧上就是肉眼可见的一卡。
        /// 现在改成：第一次真的需要扫时建一张「名字 → 类型」索引，之后全是字典查询。
        /// </remarks>
        private Dictionary<string, Type> _scannedByName;

        /// <summary>
        /// 落空缓存：问过一次、确认不存在的类型名直接判落空，不再重扫程序集。
        /// 索引只在 Build 期间写入（<see cref="RegisterConventionType"/> 会清空本缓存兜底），
        /// 构建完成后不再变化。
        /// </summary>
        private readonly HashSet<string> _missCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>解析计数：游戏工厂一共问了多少次、命中多少、落空多少。</summary>
        public int ResolveCalls;
        public int ResolveHits;
        public int ResolveMisses;

        /// <summary>
        /// 可插拔的类型兜底 —— 由**游戏层**装进来（Core 不引用游戏类型，这是平台契约规则 1）。
        /// </summary>
        /// <remarks>
        /// ★ 为什么需要它：作者自写效果在游戏侧的类名是**游戏按约定拼出来的**
        /// （<c>"Effects.Special.CardEffect." + "EffectSpEffect" + specialEffectString</c>，
        /// 见 <c>Utilities/EffectUtils.cs:27-28</c>），作者**并不写**那个类 ——
        /// 所以平台必须在这里现生成一个 <c>: Effects.EffectBase</c> 的薄壳，
        /// 把 <c>ApplyEffectForTarget</c> 转发给作者的 <c>ICardEffect</c>。
        /// 2026-10-13 实机定位：不这么做时 <c>Type.GetType</c> 返回 null（<c>ArgumentNullException</c>）
        /// 或作者类不是 EffectBase（<c>as EffectBase</c> 得 null → <c>NullReferenceException</c>）。
        /// </remarks>
        public static Func<string, Type> EmitFallback;

        private ContentRegistry()
        {
            Summary = new RegistrySummary();
        }

        /// <summary>已注册的效果 id 快照（供校验器消费）。</summary>
        public IReadOnlyList<string> RegisteredEffectIds
        {
            get { return new List<string>(_byEffectId.Keys); }
        }

        /// <summary>
        /// 从一组程序集构建注册表。
        /// </summary>
        /// <param name="assemblies">要扫描的程序集（生产环境传 AppDomain 全部；测试传假的）。</param>
        /// <param name="log">可选日志。</param>
        public static ContentRegistry Build(IEnumerable<Assembly> assemblies, Action<string> log = null)
        {
            var reg = new ContentRegistry();
            var list = new List<Assembly>();
            if (assemblies != null)
            {
                foreach (var a in assemblies) { if (a != null) { list.Add(a); } }
            }

            reg.Summary.AppDomainAssemblies = list.Count;
            reg._scanAssemblies.AddRange(list);

            foreach (var asm in list)
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // 部分类型加载失败是常见情况（缺依赖），能拿到的要照常处理
                    types = ex.Types;
                    reg.Summary.ScanFailures++;
                }
                catch (Exception)
                {
                    reg.Summary.ScanFailures++;
                    continue;
                }

                if (types == null) { continue; }

                foreach (var t in types)
                {
                    if (t == null) { continue; }
                    reg.Summary.ScannedTypes++;
                    reg.Index(t);
                }
            }

            if (log != null) { log("[ModKit] 内容注册表：" + reg.Summary); }
            return reg;
        }

        private void Index(Type t)
        {
            // 完整类型名索引（含 FullName 与 Name 两种拼法）
            if (!string.IsNullOrEmpty(t.FullName) && !_byFullName.ContainsKey(t.FullName))
            {
                _byFullName[t.FullName] = t;
            }

            // 类名（不含命名空间）索引 —— 兜底用
            if (!string.IsNullOrEmpty(t.Name) && !_byShortName.ContainsKey(t.Name))
            {
                _byShortName[t.Name] = t;
            }

            IndexEffectAttribute(t);
            IndexByConvention(t);
        }

        private void IndexEffectAttribute(Type t)
        {
            try
            {
                var attrs = t.GetCustomAttributes(typeof(ModEffectAttribute), false);
                if (attrs == null || attrs.Length == 0) { return; }

                var attr = (ModEffectAttribute)attrs[0];
                if (string.IsNullOrEmpty(attr.Id)) { return; }

                // 游戏用 Activator.CreateInstance 实例化 → 没有 public 无参构造的类不能注册
                if (!HasPublicDefaultConstructor(t))
                {
                    Summary.RejectedNoDefaultCtor.Add(t.FullName + "（[ModEffect(\"" + attr.Id + "\")]）");
                    return;
                }

                if (_byEffectId.ContainsKey(attr.Id))
                {
                    Summary.DuplicateEffectIds.Add(attr.Id + " → " + t.FullName);
                    return;   // 先注册者保留
                }

                _byEffectId[attr.Id] = t;
                Summary.RegisteredEffects++;
            }
            catch (Exception)
            {
                Summary.ScanFailures++;
            }
        }

        /// <summary>
        /// 按游戏的命名约定登记类 —— 这样**没写 [ModEffect] 的类也能被找到**，
        /// 与游戏原版行为一致（原版类都没有特性）。
        /// </summary>
        private void IndexByConvention(Type t)
        {
            try
            {
                if (t.IsAbstract || t.IsInterface) { return; }

                string name = t.Name;
                string ns = t.Namespace ?? string.Empty;

                // 效果类：Effects.Special.CardEffect.EffectSpEffect<名>
                if (name.StartsWith("EffectSpEffect", StringComparison.Ordinal) &&
                    ns == "Effects.Special.CardEffect")
                {
                    RegisterConventionType(t, name.Substring("EffectSpEffect".Length));
                }
                // 能力效果类：Effects.Special.AbilityEffect.EffectSpAbility<名>
                else if (name.StartsWith("EffectSpAbility", StringComparison.Ordinal) &&
                         ns == "Effects.Special.AbilityEffect")
                {
                    RegisterConventionType(t, name.Substring("EffectSpAbility".Length));
                }
                // 遗物类：Relic.RelicEffects.Relic<遗物名>
                else if (name.StartsWith("Relic", StringComparison.Ordinal) &&
                         ns == "Relic.RelicEffects")
                {
                    RegisterConventionType(t, name.Substring("Relic".Length));
                }
                // 关键词类：Keyword.CardKeyword<关键词名>
                else if (name.StartsWith("CardKeyword", StringComparison.Ordinal) &&
                         ns == "Keyword")
                {
                    RegisterConventionType(t, name.Substring("CardKeyword".Length));
                }
            }
            catch (Exception)
            {
                Summary.ScanFailures++;
            }
        }

        private void RegisterConventionType(Type t, string contentName)
        {
            if (string.IsNullOrEmpty(contentName)) { return; }
            if (!HasPublicDefaultConstructor(t)) { return; }   // 约定类也必须有 public 无参构造

            // 只补空缺：[ModEffect] 显式注册的优先
            if (!_byEffectId.ContainsKey(contentName))
            {
                _byEffectId[contentName] = t;
            }

            // 新类型进表 ⇒ 之前"确认不存在"的结论可能失效（注册只发生在 Build 期间，
            // 这里清一次是廉价兜底，避免落空缓存把后注册的内容挡在外面）。
            _missCache.Clear();
        }

        private static bool HasPublicDefaultConstructor(Type t)
        {
            // 值类型天然有无参构造；引用类型要有显式 public 无参构造
            if (t.IsValueType) { return true; }
            return t.GetConstructor(Type.EmptyTypes) != null;
        }

        /// <summary>
        /// 解析一个类型名 —— 这是被 Transpiler 替换后**游戏实际调用的入口**。
        /// </summary>
        /// <remarks>
        /// 输入可能是：
        /// <list type="bullet">
        /// <item>完整类型名（游戏原样传进来的，如 <c>Effects.Special.CardEffect.EffectSpEffectModHeadStart</c>）；</item>
        /// <item>其他任何游戏会用 <c>Type.GetType</c> 查的名字。</item>
        /// </list>
        /// 命中就返回，落空返回 null（与原版 <c>Type.GetType</c> 语义一致 —— 不抛异常，
        /// 让游戏的后续 NRE 保持原样的失败模式）。
        /// </remarks>
        public Type Resolve(string typeName)
        {
            ResolveCalls++;

            if (string.IsNullOrEmpty(typeName))
            {
                ResolveMisses++;
                return null;
            }

            // 0) 落空缓存：游戏会**反复**问同一个名字 —— 原版内容（关键词/效果/遗物类）
            //    都在平台的注册表里落空，然后由桥回落到游戏自己的 Type.GetType 找到。
            //    没有这层缓存，每问一次都要重扫全部程序集元数据（实机表现：鼠标移上去卡一下）。
            if (_missCache.Contains(typeName))
            {
                ResolveMisses++;
                return null;
            }

            // 1) 直接命中完整名
            Type t;
            if (_byFullName.TryGetValue(typeName, out t))
            {
                ResolveHits++;
                return t;
            }

            // 2) 去掉命名空间再试（游戏可能只给了类名）
            int dot = typeName.LastIndexOf('.');
            if (dot >= 0 && dot < typeName.Length - 1)
            {
                string shortName = typeName.Substring(dot + 1);
                if (_byShortName.TryGetValue(shortName, out t))
                {
                    ResolveHits++;
                    return t;
                }
                if (_byFullName.TryGetValue(shortName, out t))
                {
                    ResolveHits++;
                    return t;
                }
            }

            // 2.5) 游戏的「按名字拼类」查找 —— 归一化后再找一次。
            //      游戏侧是拿串问的（Keyword/CardKeyWordUtil.cs:80）：
            //          Type.GetType("Keyword.CardKeyword" + keywordName)
            //      这里的 keywordName 是**作者的原始 id**（下划线小写，甚至可能带 mod 前缀，
            //      如 manual_demo.milk_frog），而约定类的类名是 PascalCase（CardKeywordMilkFrog）。
            //      2026-10-15 实机定位：不做这步，「奶蛙」的 CardKeywordMilkFrog **从未被实例化**
            //      —— 静态构造没跑、Harmony 补丁没装、卡面没数字、连锁钩子不触发，
            //      而游戏侧一声不响地退化成基类 CardKeyword（手册承诺的"按名字拼类"并未生效）。
            string conventionName = ModKit.Core.ConventionNames.TryConventionClassName(typeName);
            if (conventionName != null)
            {
                if (_byShortName.TryGetValue(conventionName, out t) ||
                    _byFullName.TryGetValue(conventionName, out t))
                {
                    ResolveHits++;
                    return t;
                }

                var byConvention = SearchAssembliesByName(conventionName);
                if (byConvention != null)
                {
                    ResolveHits++;
                    return byConvention;
                }
            }

            // 3) 兜底：在**构建时喂进来的程序集**里按类名找。
            //    场景：某些程序集（如用 Assembly.LoadFrom 加载的 mod）不在
            //    AppDomain.GetAssemblies() 的默认加载上下文里，只按约定命名空间
            //    索引会漏掉它们。这里补一次按类名的直查。
            var found = SearchAssembliesByName(typeName);
            if (found != null)
            {
                ResolveHits++;
                return found;
            }

            // 4) 可插拔兜底：让**游戏层**按需生成类型（作者自写效果那条路，见 EmitFallback 的说明）。
            var emitted = EmitFallback == null ? null : EmitFallback(typeName);
            if (emitted != null)
            {
                ResolveHits++;
                return emitted;
            }

            ResolveMisses++;
            _missCache.Add(typeName);
            return null;
        }

        /// <summary>在已扫描程序集里按（可能是限定的）类名查找。</summary>
        private Type SearchAssembliesByName(string typeName)
        {
            string wanted = typeName;
            int dot = typeName.LastIndexOf('.');
            if (dot >= 0 && dot < typeName.Length - 1)
            {
                wanted = typeName.Substring(dot + 1);
            }

            Type cached;
            if (_byShortName.TryGetValue(wanted, out cached)) { return cached; }

            var index = EnsureScannedIndex();
            Type t;
            if (index.TryGetValue(wanted, out t))
            {
                _byShortName[wanted] = t;   // 命中过一层，之后连索引都不用查
                return t;
            }

            return null;
        }

        /// <summary>
        /// 懒建「类名 → 类型」全量索引 —— <c>Assembly.GetTypes()</c> 只在这里调用，
        /// 且整个注册表生命周期内只扫一遍（这是 2026-10-15 那次"移上去卡一下"的修复核心）。
        /// </summary>
        private Dictionary<string, Type> EnsureScannedIndex()
        {
            if (_scannedByName != null) { return _scannedByName; }

            var map = new Dictionary<string, Type>(StringComparer.Ordinal);
            for (int i = 0; i < _scanAssemblies.Count; i++)
            {
                Type[] types;
                try
                {
                    types = _scanAssemblies[i].GetTypes();
                }
                catch (Exception)
                {
                    continue;   // 部分可加载的程序集会抛，跳过（与旧实现一致）
                }
                if (types == null) { continue; }

                for (int j = 0; j < types.Length; j++)
                {
                    var t = types[j];
                    if (t == null || t.Name == null) { continue; }
                    if (!map.ContainsKey(t.Name)) { map[t.Name] = t; }   // 先到先得
                }
            }

            _scannedByName = map;
            return map;
        }

        /// <summary>按内容名取效果类（平台自己的查找路径，不走完整类型名）。</summary>
        public Type ResolveEffect(string effectId)
        {
            if (string.IsNullOrEmpty(effectId)) { return null; }
            Type t;
            return _byEffectId.TryGetValue(effectId, out t) ? t : null;
        }
    }
}
