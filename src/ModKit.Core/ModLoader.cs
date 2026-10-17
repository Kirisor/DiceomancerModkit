// =============================================================================
//  ModKit.Core · Loader（mod 发现 → 清单解析 → 校验 → 交给装配）
//
//  这是平台的"总装线"：把磁盘上的一个 mod 文件夹变成"可装配的内容包"，
//  并在任何一步出错时**只停掉这一个 mod**，不牵连其他 mod、不牵连游戏。
//
//  ★ 为什么 Loader 放在 Core（而不是 Game）：
//    它做的事——扫目录、读清单、解析、校验、汇总诊断——**全是纯逻辑**。
//    放在 Core 就能在没有游戏的机器上跑完整回归：
//    喂一个坏 mod 就能验证"失败隔离"真的生效，而不必启动游戏。
//    真正的"装配进游戏"通过 IContentSink 交给 Game 层。
//
//  ★ 失败隔离的最小单元是"内容文件"，不是"mod"：
//    一个 mod 里 3 张卡有 1 张写错，另外 2 张仍应加载。
//    这与契约 §3.4 的"该卡不加载、其余照常"一致，也避免了
//    "一个小笔误让整个 mod 消失"这种最伤作者体验的失败模式。
// =============================================================================

using System;
using System.Collections.Generic;
using ModKit.Api;

namespace ModKit.Core
{
    /// <summary>
    /// 文件读取抽象。
    /// </summary>
    /// <remarks>
    /// 做成接口是为了两件事：
    /// <list type="number">
    /// <item>让 Loader 能在测试里喂内存里的假文件（跑失败隔离的回归）；</item>
    /// <item>把"平台读了哪些文件"变成可枚举的事实（哨兵/审计）。</item>
    /// </list>
    /// 生产实现读磁盘；测试实现读字典。
    /// </remarks>
    public interface IModFileSource
    {
        /// <summary>列出 mod 目录（根下的直接子目录名）。</summary>
        IReadOnlyList<string> ListModDirectories(string root);

        /// <summary>读文本文件。文件不存在返回 null（不抛）。</summary>
        string ReadText(string path);

        /// <summary>文件是否存在。</summary>
        bool Exists(string path);

        /// <summary>拼路径（由实现决定分隔符，避免 Loader 依赖平台差异）。</summary>
        string Combine(string a, string b);
    }

    /// <summary>一个 mod 的加载结果。</summary>
    public sealed class ModLoadResult
    {
        public ModLoadResult(string directory, string path)
        {
            Directory = directory;
            Path = path;
            Report = new ValidationReport();
            Cards = new List<CardDef>();
            Relics = new List<RelicDef>();
            Keywords = new List<KeywordDef>();
            Abilities = new List<AbilityDef>();
            I18n = new List<I18nDef>();
            EntryAssemblyPath = null;
        }

        /// <summary>mod 目录名。</summary>
        public string Directory { get; private set; }

        /// <summary>mod 目录完整路径。</summary>
        public string Path { get; private set; }

        public ModManifest Manifest { get; private set; }
        public ValidationReport Report { get; private set; }

        public List<CardDef> Cards { get; private set; }
        public List<RelicDef> Relics { get; private set; }
        public List<KeywordDef> Keywords { get; private set; }

        /// <summary>本 mod 声明的能力。</summary>
        public List<AbilityDef> Abilities { get; private set; }
        public List<I18nDef> I18n { get; private set; }

        /// <summary>入口程序集完整路径；纯数据 mod 为 null。</summary>
        public string EntryAssemblyPath { get; set; }

        /// <summary>这个 mod 是否整体失败（清单读不出 / id 非法）。</summary>
        public bool Failed { get; private set; }

        /// <summary>失败原因（<see cref="Failed"/> 为 true 时非空）。</summary>
        public string FailureReason { get; private set; }

        internal void Fail(string reason)
        {
            Failed = true;
            FailureReason = reason;
            Report.Error(null, "(mod)", reason);
        }

        internal void SetManifest(ModManifest manifest)
        {
            Manifest = manifest;
        }

        public override string ToString()
        {
            if (Failed) { return Directory + "：失败（" + FailureReason + "）"; }
            return string.Format("{0}：卡 {1} / 遗物 {2} / 关键词 {3} / 文案 {4}，诊断 {5} 条（错误 {6}）",
                Directory, Cards.Count, Relics.Count, Keywords.Count, I18n.Count,
                Report.Items.Count, Report.ErrorCount);
        }
    }

    /// <summary>整批 mod 的加载汇总 —— 写进哨兵日志。</summary>
    public sealed class LoadSummary
    {
        public readonly List<ModLoadResult> Mods = new List<ModLoadResult>();

        /// <summary>
        /// 用单个 mod 的结果构造一个汇总（给"只加载一个目录"的作者工具用）。
        /// </summary>
        public static LoadSummary ForSingle(ModLoadResult result)
        {
            var s = new LoadSummary();
            if (result != null) { s.Mods.Add(result); }
            return s;
        }

        public int ModCount { get { return Mods.Count; } }

        public int FailedModCount
        {
            get
            {
                int n = 0;
                foreach (var m in Mods) { if (m.Failed) { n++; } }
                return n;
            }
        }

        public int CardCount { get { return Sum(delegate (ModLoadResult m) { return m.Cards.Count; }); } }
        public int RelicCount { get { return Sum(delegate (ModLoadResult m) { return m.Relics.Count; }); } }
        public int KeywordCount { get { return Sum(delegate (ModLoadResult m) { return m.Keywords.Count; }); } }
        public int AbilityCount { get { return Sum(delegate (ModLoadResult m) { return m.Abilities.Count; }); } }
        public int I18nCount { get { return Sum(delegate (ModLoadResult m) { return m.I18n.Count; }); } }

        public int ErrorCount
        {
            get
            {
                int n = 0;
                foreach (var m in Mods) { n += m.Report.ErrorCount; }
                return n;
            }
        }

        public int WarningCount
        {
            get
            {
                int n = 0;
                foreach (var m in Mods) { n += m.Report.WarningCount; }
                return n;
            }
        }

        private int Sum(Func<ModLoadResult, int> f)
        {
            int n = 0;
            foreach (var m in Mods) { n += f(m); }
            return n;
        }

        public override string ToString()
        {
            return string.Format("mod {0} 个（失败 {1}）｜卡 {2} / 遗物 {3} / 关键词 {4} / 文案 {5}｜错误 {6} 条，警告 {7} 条",
                ModCount, FailedModCount, CardCount, RelicCount, KeywordCount, I18nCount,
                ErrorCount, WarningCount);
        }
    }

    /// <summary>
    /// mod 加载器。
    /// </summary>
    /// <remarks>
    /// 用法（生产）：
    /// <code>
    /// var loader = new ModLoader(new DiskFileSource(), log);
    /// var summary = loader.LoadAll(root, caps);
    /// // 然后由 Game 层按 summary 装配
    /// </code>
    /// </remarks>
    public sealed partial class ModLoader
    {
        private readonly IModFileSource _fs;

        /// <summary>日志出口。Core 不认识 BepInEx，所以传委托。</summary>
        private readonly Action<string> _info;
        private readonly Action<string> _warn;
        private readonly Action<string> _error;

        public ModLoader(IModFileSource fs,
            Action<string> info = null, Action<string> warn = null, Action<string> error = null)
        {
            if (fs == null) { throw new ArgumentNullException("fs"); }
            _fs = fs;
            _info = info ?? delegate (string s) { };
            _warn = warn ?? delegate (string s) { };
            _error = error ?? delegate (string s) { };
        }

        /// <summary>清单文件名。</summary>
        public const string ManifestFileName = "mod.json";

        /// <summary>内容目录名 —— 清单里的文件**先按原样解析，找不到再在这个目录下找**。</summary>
        /// <remarks>
        /// 为什么要有这个兜底：作者在本地开发时习惯把东西摊平放
        /// （<c>my_mod/cards.json</c>），而平台文档与示例用分类子目录
        /// （<c>my_mod/cards/a.json</c>）。两者都该能用 —— 否则作者会因为
        /// "我明明写了文件却说读不到"而困惑，这正是最伤体验的一类失败。
        /// </remarks>
        public static readonly string[] ContentSubdirectories = { "content", "cards", "relics", "keywords", "i18n" };

        /// <summary>
        /// 解析内容文件路径：先按清单原样，再依次在各内容子目录下试。
        /// </summary>
        /// <returns>命中的完整路径；都没命中返回 null（由调用方报"读不到"）。</returns>
        private string ResolveContentPath(string modPath, string relative)
        {
            if (string.IsNullOrEmpty(relative)) { return null; }

            string direct = _fs.Combine(modPath, relative);
            if (_fs.Exists(direct)) { return direct; }

            // 清单里写了子目录前缀（如 "cards/a.json"）但也想摊平放时，用文件名再试
            string fileName = relative;
            int slash = relative.LastIndexOfAny(new[] { '/', '\\' });
            if (slash >= 0 && slash < relative.Length - 1) { fileName = relative.Substring(slash + 1); }

            for (int i = 0; i < ContentSubdirectories.Length; i++)
            {
                string candidate = _fs.Combine(_fs.Combine(modPath, ContentSubdirectories[i]), fileName);
                if (_fs.Exists(candidate)) { return candidate; }
            }

            // 最后再试一次"原样但在 mod 根下只取文件名"
            string atRoot = _fs.Combine(modPath, fileName);
            if (_fs.Exists(atRoot)) { return atRoot; }

            return null;
        }

        /// <summary>
        /// 加载 root 下的全部 mod。
        /// </summary>
        /// <remarks>
        /// **任何单个 mod 的失败都不影响其他 mod** —— 每个 mod 独立 try/catch，
        /// 失败信息记进它自己的 Report 并写日志，然后继续下一个。
        /// </remarks>
        public LoadSummary LoadAll(string root, IContentCapabilities caps)
        {
            var summary = new LoadSummary();

            IReadOnlyList<string> dirs;
            try
            {
                dirs = _fs.ListModDirectories(root);
            }
            catch (Exception ex)
            {
                _error("扫描 mod 目录失败：" + root + " —— " + ex.Message);
                return summary;
            }

            if (dirs == null || dirs.Count == 0)
            {
                _info("未发现任何 mod（目录：" + root + "）。");
                return summary;
            }

            _info("发现 " + dirs.Count + " 个候选目录。");

            // ---- 预扫描：先收集所有 mod 声明的关键词 ----
            // ★ 为什么必须两遍：一张卡可能引用**同一个 mod 声明的新关键词**。
            //   若先校验卡再收关键词，作者会看到"关键词未注册"的假报错
            //   —— 而那正是最让人困惑的一类失败（明明在本 mod 里声明了）。
            var declaredKeywords = CollectDeclaredKeywordNames(root, dirs);
            if (declaredKeywords.Count > 0)
            {
                _info("预扫描：mod 声明的关键词 " + declaredKeywords.Count + " 个（" +
                    string.Join(", ", declaredKeywords.ToArray()) + "）");
            }

            // 把新关键词并入能力表（校验器据此放行引用它们的卡）
            IContentCapabilities effectiveCaps = declaredKeywords.Count == 0
                ? caps
                : new ExtendedCapabilities(caps, declaredKeywords);

            foreach (var dir in dirs)
            {
                ModLoadResult result;
                try
                {
                    result = LoadOne(root, dir, effectiveCaps);
                }
                catch (Exception ex)
                {
                    // 兜底：连 LoadOne 都没能跑完（例如文件系统异常）
                    result = new ModLoadResult(dir, _fs.Combine(root, dir));
                    result.Fail("加载时发生未处理异常：" + ex.GetType().Name + ": " + ex.Message
                        + "  @ " + (ex.StackTrace ?? "(无堆栈)"));
                }

                summary.Mods.Add(result);

                if (result.Failed) { _error("mod 失败：" + result); }
                else if (result.Report.HasErrors) { _warn("mod 部分内容被拒：" + result); }
                else { _info("mod 已加载：" + result); }

                // 把每条诊断原样打出来 —— 作者要能直接看到字段路径
                foreach (var d in result.Report.Items)
                {
                    var line = "  " + d;
                    if (d.Level == DiagnosticLevel.Error) { _error(line); }
                    else if (d.Level == DiagnosticLevel.Warning) { _warn(line); }
                    else { _info(line); }
                }
            }

            _info("加载汇总：" + summary);
            return summary;
        }

        /// <summary>
        /// 预扫描所有 mod 的关键词声明，返回 mod 侧的关键词名集合。
        /// </summary>
        /// <remarks>
        /// 只做"读清单 + 解析关键词文件 + 取 id"，不做完整校验 ——
        /// 完整校验仍由正式的 <see cref="LoadOne"/> 负责（避免同一份文件被校验两次而重复报错）。
        /// </remarks>
        private List<string> CollectDeclaredKeywordNames(string root, IReadOnlyList<string> dirs)
        {
            var names = new List<string>();

            foreach (var dir in dirs)
            {
                try
                {
                    string modPath = _fs.Combine(root, dir);
                    string manifestText = _fs.ReadText(_fs.Combine(modPath, ManifestFileName));
                    if (manifestText == null) { continue; }

                    var parsed = ContentParser.ParseManifest(manifestText);
                    if (parsed.Value == null || parsed.Value.Keywords == null) { continue; }

                    foreach (var rel in parsed.Value.Keywords)
                    {
                        if (string.IsNullOrEmpty(rel)) { continue; }

                        string full = ResolveContentPath(modPath, rel);
                        string text = full == null ? null : _fs.ReadText(full);
                        if (text == null) { continue; }

                        var kw = ContentParser.ParseKeyword(text);
                        if (kw.Value == null || string.IsNullOrEmpty(kw.Value.Id)) { continue; }

                        // 与卡/遗物同一口径：关键词名也加 mod 前缀，防跨 mod 撞名
                        string prefixed = kw.Value.Id;
                        if (parsed.Value.Id != null &&
                            !prefixed.StartsWith(parsed.Value.Id + ".", StringComparison.Ordinal))
                        {
                            prefixed = parsed.Value.Id + "." + prefixed;
                        }

                        if (!names.Contains(prefixed)) { names.Add(prefixed); }

                        // 同时登记**未加前缀**的写法：作者在卡里可能写短名。
                        // 这是刻意的宽容 —— 报错应指向真问题（拼错），而不是前缀用法。
                        if (!names.Contains(kw.Value.Id)) { names.Add(kw.Value.Id); }
                    }
                }
                catch (Exception ex)
                {
                    _warn("预扫描关键词时跳过 " + dir + "：" + ex.GetType().Name);
                }
            }

            return names;
        }

        /// <summary>
        /// 只加载**一个**指定目录名的 mod（不做全根扫描）。
        /// </summary>
        /// <remarks>
        /// 给作者工具用：<c>modkit lint 我的mod目录</c> 与 <c>modkit pack</c>
        /// 的目标都是"单个 mod 目录"，而 <see cref="LoadAll"/> 只接受 mods 根目录。
        /// 若不做这层区分，工具会去找 <c>我的mod目录/我的mod目录/mod.json</c>，
        /// 报出"缺少 mod.json"这种**指向错误地方**的假诊断（实测发生过）。
        /// </remarks>
        public ModLoadResult LoadOneDirectory(string root, string directory, IContentCapabilities caps)
        {
            if (string.IsNullOrEmpty(directory))
            {
                throw new ArgumentException("directory 不能为空", "directory");
            }
            return LoadOne(root, directory, caps);
        }

        /// <summary>一个 mod 的入口程序集信息（只读预扫描的产物）。</summary>
        public sealed class ModAssemblyEntry
        {
            /// <summary>mod 的 id。</summary>
            public string ModId { get; set; }

            /// <summary>mod 目录的绝对路径。</summary>
            public string ModPath { get; set; }

            /// <summary>入口程序集绝对路径；该 mod 没有 C# 代码时为 null。</summary>
            public string AssemblyPath { get; set; }
        }

        /// <summary>
        /// **只读清单预扫描**：只解析各 mod 的 <c>mod.json</c>，返回它们的入口程序集路径。
        /// **不做任何内容校验**（那要等注册表建好之后）。
        /// </summary>
        /// <remarks>
        /// ★ 为什么单独开这一步：mod 的程序集必须在**构建内容注册表之前**加载进 AppDomain，
        /// 否则 mod 里的 <c>CardKeyword*</c> / <c>[ModEffect]</c>
        /// 类扫不到，加载期检查会误报「未注册」，mod 的 custom 效果也会被判未注册。
        /// <para>
        /// 而 <c>LoadAll</c> 是一趟到底的（它自己需要用注册表来校验 custom id），
        /// 拿不到「已知道有哪些程序集、但还没校验内容」这个中间态 —— 所以只能预扫描。
        /// </para>
        /// </remarks>
        public List<ModAssemblyEntry> ScanEntryAssemblies(string root)
        {
            var found = new List<ModAssemblyEntry>();

            IReadOnlyList<string> dirs;
            try
            {
                dirs = _fs.ListModDirectories(root);
            }
            catch (Exception ex)
            {
                _warn("预扫描：列举 mod 目录失败 —— " + ex.Message);
                return found;
            }
            if (dirs == null) { return found; }

            foreach (var dir in dirs)
            {
                try
                {
                    string modPath = _fs.Combine(root, dir);
                    string manifestText = _fs.ReadText(_fs.Combine(modPath, ManifestFileName));
                    if (manifestText == null) { continue; }

                    var parsed = ContentParser.ParseManifest(manifestText);
                    if (parsed.Value == null) { continue; }

                    var entry = new ModAssemblyEntry();
                    entry.ModId = parsed.Value.Id;
                    entry.ModPath = modPath;

                    if (!string.IsNullOrEmpty(parsed.Value.EntryAssembly))
                    {
                        string asmPath = _fs.Combine(modPath, parsed.Value.EntryAssembly);
                        if (_fs.Exists(asmPath))
                        {
                            entry.AssemblyPath = asmPath;
                        }
                        else
                        {
                            _warn("预扫描：" + dir + " 声明了 entryAssembly="
                                + parsed.Value.EntryAssembly + "，但文件不存在，跳过加载。");
                        }
                    }

                    found.Add(entry);
                }
                catch (Exception ex)
                {
                    _warn("预扫描：" + dir + " 失败 —— " + ex.Message);
                }
            }

            return found;
        }

        /// <summary>加载单个 mod 目录。</summary>
        private ModLoadResult LoadOne(string root, string dir, IContentCapabilities caps)
        {
            string modPath = _fs.Combine(root, dir);
            var result = new ModLoadResult(dir, modPath);

            // --- 1. 清单 ---
            string manifestText = _fs.ReadText(_fs.Combine(modPath, ManifestFileName));
            if (manifestText == null)
            {
                result.Fail("缺少 " + ManifestFileName + "（每个 mod 目录都必须有清单）。");
                return result;
            }

            var parsed = ContentParser.ParseManifest(manifestText);
            result.Report.Merge(parsed.Report);
            if (parsed.Value == null)
            {
                result.Fail("清单解析失败，见上面的诊断。");
                return result;
            }

            result.SetManifest(parsed.Value);
            string modId = parsed.Value.Id;

            // 清单级校验（id 格式、schema 版本）
            CheckManifest(parsed.Value, result.Report);

            // --- 2. 入口程序集（可选）---
            if (!string.IsNullOrEmpty(parsed.Value.EntryAssembly))
            {
                string asmPath = _fs.Combine(modPath, parsed.Value.EntryAssembly);
                if (!_fs.Exists(asmPath))
                {
                    result.Report.Error(modId, "entryAssembly",
                        "清单声明了 entryAssembly=" + parsed.Value.EntryAssembly + "，但文件不存在。");
                }
                else
                {
                    result.EntryAssemblyPath = asmPath;
                }
            }

            // --- 3. 内容文件：逐个独立处理，一个坏不影响其他 ---
            LoadCards(result, modPath, modId, caps);
            LoadRelics(result, modPath, modId, caps);
            LoadKeywords(result, modPath, modId, caps);
            LoadAbilities(result, modPath, modId, caps);
            LoadI18n(result, modPath, modId, caps);

            // --- 4. id 前缀：防止跨 mod 撞名 ---
            foreach (var c in result.Cards) { PrefixId(c, modId); }
            foreach (var r in result.Relics) { PrefixId(r, modId); }

            // --- 5. 跨卡引用：必须等 id 加完前缀、且本 mod 的卡全读完才做得了 ---
            CheckToCardReferences(result);

            return result;
        }

        /// <summary>
        /// 跨卡引用检查：关键词的 <c>toCard</c> 必须指向**本 mod 里真实存在的卡**。
        /// </summary>
        /// <remarks>
        /// ★ 为什么非要这一层：<c>toCard</c> 对应游戏的 <c>keywordCardSoName</c>。
        /// 写错时游戏**不会报任何错**，而是在变身那一刻给你一张「空白卡牌」
        /// （2026-10-12 实机踩中：`试作弩·装填` 打出后变成空白卡，同时
        /// `SubCommandReloadAlterError` 抛 <c>NullReferenceException</c>）。
        /// <para>
        /// 单文件校验看不到别的卡，所以只能放在**读完全部卡、并且 id 加完 mod 前缀之后**。
        /// 短 id 与带前缀的全名都接受。
        /// </para>
        /// </remarks>
        private static void CheckToCardReferences(ModLoadResult result)
        {
            if (result == null || result.Report == null || result.Cards == null) { return; }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in result.Cards)
            {
                if (c != null && !string.IsNullOrEmpty(c.Id)) { ids.Add(c.Id); }
            }
            if (ids.Count == 0) { return; }

            var sorted = new List<string>(ids);
            sorted.Sort(StringComparer.Ordinal);

            foreach (var c in result.Cards)
            {
                if (c == null) { continue; }
                CheckToCardList(c, c.Keywords, "keywords", ids, sorted, result.Report);
                if (c.Upgrade != null)
                {
                    CheckToCardList(c, c.Upgrade.AddKeywords,
                        "upgrade.addKeywords", ids, sorted, result.Report);
                }
            }
        }

        private static void CheckToCardList(CardDef card, List<KeywordRefDef> refs, string path,
            HashSet<string> ids, List<string> existing, ValidationReport report)
        {
            if (refs == null) { return; }

            for (int i = 0; i < refs.Count; i++)
            {
                var k = refs[i];
                if (k == null || string.IsNullOrEmpty(k.ToCard)) { continue; }

                var shortId = k.ToCard.Trim();
                if (ids.Contains(shortId)) { continue; }

                int dot = card.Id == null ? -1 : card.Id.IndexOf('.');
                var full = dot > 0 ? card.Id.Substring(0, dot) + "." + shortId : shortId;
                if (ids.Contains(full)) { continue; }

                report.Error(card.Id, path + "[" + i + "].toCard",
                    "变身目标卡 '" + shortId + "' 在本 mod 里不存在。"
                    + "写错不会报错，但那张卡打出时会变成一张「空白卡牌」。"
                    + "本 mod 现有的卡：" + string.Join(", ", existing.ToArray()));
            }
        }

        private static void CheckManifest(ModManifest m, ValidationReport report)
        {
            if (string.IsNullOrEmpty(m.Schema))
            {
                report.Error(m.Id, "$schema",
                    "清单缺少 $schema。必须显式声明契约版本（应为 \"" + ContentValidator.ManifestSchema + "\"）。");
            }
            else if (!string.Equals(m.Schema, ContentValidator.ManifestSchema, StringComparison.Ordinal))
            {
                report.Error(m.Id, "$schema",
                    "清单契约版本不匹配：文件声明 \"" + m.Schema + "\"，平台要求 \"" +
                    ContentValidator.ManifestSchema + "\"。");
            }

            if (!string.IsNullOrEmpty(m.Id) &&
                !System.Text.RegularExpressions.Regex.IsMatch(m.Id, "^[a-z][a-z0-9_]{2,40}$"))
            {
                report.Error(m.Id, "id",
                    "id 不符合 ^[a-z][a-z0-9_]{2,40}$（小写字母开头，只含小写字母/数字/下划线，长度 3-41）。");
            }

            if (!string.IsNullOrEmpty(m.Version) &&
                !System.Text.RegularExpressions.Regex.IsMatch(m.Version, @"^\d+\.\d+(\.\d+)?$"))
            {
                report.Warn(m.Id, "version", "version 建议用语义化版本（如 1.0 或 1.0.0）。");
            }
        }

        private void LoadCards(ModLoadResult result, string modPath, string modId, IContentCapabilities caps)
        {
            var files = result.Manifest.Cards;
            if (files == null || files.Count == 0) { return; }

            var seen = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var rel in files)
            {
                if (string.IsNullOrEmpty(rel)) { continue; }

                string full = ResolveContentPath(modPath, rel);
                string text = full == null ? null : _fs.ReadText(full);
                if (text == null)
                {
                    result.Report.Error(modId, "cards", "清单声明了 " + rel + "，但文件读不到。");
                    continue;
                }

                var parsed = ContentParser.ParseCard(text);
                var report = parsed.Report;

                if (parsed.Value != null)
                {
                    // 内容级校验（含费用色探测结果、artTemplate 存在性等）
                    report.Merge(ContentValidator.ValidateCard(text, caps));

                    // mod 内 id 唯一性（跨文件也要查）
                    string fileKey = parsed.Value.Id ?? "(无 id)";
                    string firstSeen;
                    if (seen.TryGetValue(fileKey, out firstSeen))
                    {
                        report.Error(parsed.Value.Id, "id",
                            "id 在本 mod 内重复：已在 " + firstSeen + " 中出现过。");
                    }
                    else
                    {
                        seen[fileKey] = rel;
                    }

                    // mod 自带卡面：声明的文件必须真的在 mod 目录里。
                    // ★ 为什么在 Loader 查而不在 ContentValidator：校验器不碰文件系统
                    //   （要能在没有磁盘的测试里跑）。而"声明了却没有这个文件"正是本平台
                    //   要根除的静默失败模式 —— 在这里就断掉，不让它走到运行期。
                    if (!string.IsNullOrEmpty(parsed.Value.Art))
                    {
                        string artFull = _fs.Combine(modPath, parsed.Value.Art);
                        if (!_fs.Exists(artFull))
                        {
                            report.Error(parsed.Value.Id, "art",
                                "声明了 art=\"" + parsed.Value.Art + "\"，但 mod 目录里找不到这个文件" +
                                "（art 是**相对 mod 目录**的路径）。期望位置：" + artFull);
                        }
                    }
                }

                result.Report.Merge(report);

                // 该文件无错误 → 接受它；有错误 → 只丢这一张
                if (parsed.Value != null && !report.HasErrors)
                {
                    result.Cards.Add(parsed.Value);
                }
                else
                {
                    _warn("卡牌文件被拒：" + rel + "（" + report.ErrorCount + " 条错误）");
                }
            }
        }

        private void LoadRelics(ModLoadResult result, string modPath, string modId, IContentCapabilities caps)
        {
            var files = result.Manifest.Relics;
            if (files == null || files.Count == 0) { return; }

            foreach (var rel in files)
            {
                if (string.IsNullOrEmpty(rel)) { continue; }

                string full = ResolveContentPath(modPath, rel);
                string text = full == null ? null : _fs.ReadText(full);
                if (text == null)
                {
                    result.Report.Error(modId, "relics", "清单声明了 " + rel + "，但文件读不到。");
                    continue;
                }

                var parsed = ContentParser.ParseRelic(text);
                var report = parsed.Report;
                if (parsed.Value != null)
                {
                    report.Merge(ContentValidator.ValidateRelic(text, caps));
                }

                result.Report.Merge(report);

                if (parsed.Value != null && !report.HasErrors)
                {
                    result.Relics.Add(parsed.Value);
                }
                else
                {
                    _warn("遗物文件被拒：" + rel + "（" + report.ErrorCount + " 条错误）");
                }
            }
        }

        /// <summary>
        /// 加载关键词声明。
        /// </summary>
        /// <remarks>
        /// 关键词与卡/遗物**共用同一个游戏工厂**（<c>CardKeyWordUtil</c> 的
        /// <c>Type.GetType("Keyword.CardKeyword" + name)</c>），所以 Transpiler 桥顺带接管了它 ——
        /// 这一步只做"读声明 + 校验 + 记下来"，真正的类由 C# 侧提供。
        /// </remarks>
        private void LoadKeywords(ModLoadResult result, string modPath, string modId, IContentCapabilities caps)
        {
            var files = result.Manifest.Keywords;
            if (files == null || files.Count == 0) { return; }

            foreach (var rel in files)
            {
                if (string.IsNullOrEmpty(rel)) { continue; }

                string full = ResolveContentPath(modPath, rel);
                string text = full == null ? null : _fs.ReadText(full);
                if (text == null)
                {
                    result.Report.Error(modId, "keywords", "清单声明了 " + rel + "，但文件读不到。");
                    continue;
                }

                var parsed = ContentParser.ParseKeyword(text);
                var report = parsed.Report;
                if (parsed.Value != null)
                {
                    report.Merge(ContentValidator.ValidateKeyword(text, caps));
                }

                result.Report.Merge(report);

                if (parsed.Value != null && !report.HasErrors)
                {
                    // 与卡/遗物同一口径：加 mod 前缀防跨 mod 撞名
                    if (!string.IsNullOrEmpty(parsed.Value.Id) &&
                        !parsed.Value.Id.StartsWith(modId + ".", StringComparison.Ordinal))
                    {
                        parsed.Value.Id = modId + "." + parsed.Value.Id;
                    }
                    result.Keywords.Add(parsed.Value);
                }
                else
                {
                    _warn("关键词文件被拒：" + rel + "（" + report.ErrorCount + " 条错误）");
                }
            }
        }

        private void LoadI18n(ModLoadResult result, string modPath, string modId, IContentCapabilities caps)        {
            var files = result.Manifest.I18n;
            if (files == null || files.Count == 0) { return; }

            foreach (var rel in files)
            {
                if (string.IsNullOrEmpty(rel)) { continue; }

                string full = ResolveContentPath(modPath, rel);
                string text = full == null ? null : _fs.ReadText(full);
                if (text == null)
                {
                    result.Report.Error(modId, "i18n", "清单声明了 " + rel + "，但文件读不到。");
                    continue;
                }

                var parsed = ContentParser.ParseI18n(text);
                var report = parsed.Report;
                if (parsed.Value != null)
                {
                    report.Merge(ContentValidator.ValidateI18n(text, caps));
                }

                result.Report.Merge(report);

                if (parsed.Value != null && !report.HasErrors)
                {
                    result.I18n.Add(parsed.Value);
                }
                else
                {
                    _warn("本地化文件被拒：" + rel + "（" + report.ErrorCount + " 条错误）");
                }
            }
        }

        /// <summary>
        /// 给内容 id 加 mod 前缀，防止跨 mod 撞名。
        /// </summary>
        /// <remarks>
        /// 游戏侧所有内容都是**全局按名字查**的（`GetCardSoByName` 等），
        /// 两个 mod 都叫 <c>my_strike</c> 就会互相覆盖。平台在装载时统一加
        /// <c>&lt;modid&gt;.&lt;id&gt;</c> 前缀，作者不需要关心命名空间。
        /// </remarks>
        private static void PrefixId(CardDef def, string modId)
        {
            if (def == null || string.IsNullOrEmpty(def.Id)) { return; }
            if (def.Id.StartsWith(modId + ".", StringComparison.Ordinal)) { return; }
            def.Id = modId + "." + def.Id;
        }

        private static void PrefixId(RelicDef def, string modId)
        {
            if (def == null || string.IsNullOrEmpty(def.Id)) { return; }
            if (def.Id.StartsWith(modId + ".", StringComparison.Ordinal)) { return; }
            def.Id = modId + "." + def.Id;
        }
    }

    /// <summary>
    /// 在已有能力表之上叠加"本批 mod 新声明的关键词"。
    /// </summary>
    /// <remarks>
    /// 这是**两遍制的关键**：卡可能引用同一 mod 声明的新关键词，
    /// 所以校验卡时必须已经知道这些新名字。其余能力（费用色、原版卡名、触发点）
    /// 仍来自游戏探测 —— 平台绝不放开"颜色/模板/触发点"这类**只能由游戏回答**的事实。
    /// </remarks>
    internal sealed class ExtendedCapabilities : CapabilitiesBase
    {
        private readonly IContentCapabilities _base;
        private readonly List<string> _keywords;

        internal ExtendedCapabilities(IContentCapabilities baseCaps, List<string> extraKeywords)
        {
            _base = baseCaps;
            _keywords = new List<string>();

            if (baseCaps != null && baseCaps.KnownKeywords != null)
            {
                foreach (var k in baseCaps.KnownKeywords) { _keywords.Add(k); }
            }
            if (extraKeywords != null)
            {
                foreach (var k in extraKeywords)
                {
                    if (!_keywords.Contains(k)) { _keywords.Add(k); }
                }
            }
        }

        // 其余能力一律转发给游戏探测结果 —— 不在这里"顺手放宽"。
        public override IReadOnlyList<ManaColor> AvailableCostColors
        {
            get { return _base == null ? new List<ManaColor>() : _base.AvailableCostColors; }
        }

        // 费用槽组合**只能由游戏回答**（图标是预制资产），所以原样转发、绝不放宽。
        public override bool IsKnownCostCombo(IReadOnlyList<ManaColor> combo)
        {
            return _base != null && _base.IsKnownCostCombo(combo);
        }

        public override IReadOnlyList<string> VanillaCardNames
        {
            get { return _base == null ? new List<string>() : _base.VanillaCardNames; }
        }

        public override IReadOnlyList<string> VanillaRelicNames
        {
            get { return _base == null ? new List<string>() : _base.VanillaRelicNames; }
        }

        public override IReadOnlyList<string> KnownKeywords { get { return _keywords; } }

        public override IReadOnlyList<string> KnownTriggerNames
        {
            get { return _base == null ? new List<string>() : _base.KnownTriggerNames; }
        }

        public override IReadOnlyList<string> KnownCustomEffectIds
        {
            get { return _base == null ? new List<string>() : _base.KnownCustomEffectIds; }
        }

        public override EvidenceStatus StatusOfBuiltin(BuiltinEffect effect)
        {
            return _base == null ? EvidenceStatus.Unsupported : _base.StatusOfBuiltin(effect);
        }
    }
}
