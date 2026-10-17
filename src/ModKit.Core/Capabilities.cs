// =============================================================================
//  ModKit.Core · 校验层的输入契约
//
//  ★ 本程序集是**纯逻辑**：不引用游戏类型，也不引用 UnityEngine。
//    它只知道"平台对内容的要求"，不知道游戏长什么样。
//    真实能力表由 ModKit.Game 探测后注入（见 IPlatformCapabilities 的 Game 实现）。
//
//  这个分层是刻意的：校验逻辑是平台最核心的价值（把静默失败变成加载期报错），
//  把它做成纯逻辑，就能在不启动游戏的情况下用真实 JSON 跑回归测试。
//  —— 与第 5 步"逻辑提取可测试"同一思路，但这里连 Unity 都不需要。
// =============================================================================

using System.Collections.Generic;
using ModKit.Api;

namespace ModKit.Core
{
    /// <summary>
    /// 校验期间允许出现的告警级别。错误会阻止**该条内容**加载，但不影响同一 mod 的其他内容。
    /// </summary>
    public enum DiagnosticLevel
    {
        /// <summary>内容有问题，该条不加载。</summary>
        Error = 0,

        /// <summary>可疑但不阻止加载（例如缺少描述，平台会生成兜底描述）。</summary>
        Warning = 1,

        /// <summary>信息性提示（例如某个触发点名不在实际发出列表里）。</summary>
        Info = 2
    }

    /// <summary>一条诊断。字段路径是必须的——作者要能一眼定位到自己 JSON 的哪一行。</summary>
    public sealed class Diagnostic
    {
        public Diagnostic(DiagnosticLevel level, string contentId, string path, string message)
        {
            Level = level;
            ContentId = contentId;
            Path = path;
            Message = message;
        }

        public DiagnosticLevel Level { get; private set; }

        /// <summary>出问题的内容 id（可能为 null，例如清单本身格式错）。</summary>
        public string ContentId { get; private set; }

        /// <summary>JSON 字段路径，如 <c>cost[0]</c> 或 <c>effects[1].mode</c>。</summary>
        public string Path { get; private set; }

        public string Message { get; private set; }

        public override string ToString()
        {
            // 注意 path 与 message 之间必须有分隔符 —— 少了它日志里会显示成
            // "effects[0].builtin内置语义…"，作者无法分辨字段路径到哪结束。
            return string.Format("[{0}] {1} · {2} — {3}",
                Level, ContentId ?? "(清单)", Path ?? "-", Message);
        }
    }

    /// <summary>校验结果。一个内容的校验结果是"它自己"的诊断集合。</summary>
    public sealed class ValidationReport
    {
        private readonly List<Diagnostic> _items = new List<Diagnostic>();

        public IReadOnlyList<Diagnostic> Items
        {
            get { return _items; }
        }

        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i].Level == DiagnosticLevel.Error) { return true; }
                }
                return false;
            }
        }

        public int ErrorCount { get { return Count(DiagnosticLevel.Error); } }
        public int WarningCount { get { return Count(DiagnosticLevel.Warning); } }

        private int Count(DiagnosticLevel level)
        {
            int n = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Level == level) { n++; }
            }
            return n;
        }

        public void Add(DiagnosticLevel level, string contentId, string path, string message)
        {
            _items.Add(new Diagnostic(level, contentId, path, message));
        }

        public void Error(string contentId, string path, string message)
        {
            Add(DiagnosticLevel.Error, contentId, path, message);
        }

        public void Warn(string contentId, string path, string message)
        {
            Add(DiagnosticLevel.Warning, contentId, path, message);
        }

        public void Info(string contentId, string path, string message)
        {
            Add(DiagnosticLevel.Info, contentId, path, message);
        }

        /// <summary>把另一份报告的条目并入本报告（用于"内容文件级"与"卡牌级"结果合并）。</summary>
        public void Merge(ValidationReport other)
        {
            if (other == null) { return; }
            for (int i = 0; i < other._items.Count; i++) { _items.Add(other._items[i]); }
        }
    }

    /// <summary>
    /// 平台能力表——校验器需要知道的"世界事实"。
    /// </summary>
    /// <remarks>
    /// <b>这是"作者不用懂游戏"的关键机制</b>：平台不硬编码"哪些颜色能用"，
    /// 而是启动时探测目标 build 的真实能力，校验器据此判断内容是否可加载。
    /// <para>
    /// 生产实现由 ModKit.Game 注入（读 <c>ManaManager.Instance.ManaIconDic</c> 等）；
    /// 测试实现是一个手写的假表——这让校验逻辑可以脱离游戏做回归。
    /// </para>
    /// </remarks>
    public interface IContentCapabilities
    {
        /// <summary>本次探测确认可用的单色集合。</summary>
        IReadOnlyList<ManaColor> AvailableCostColors { get; }

        /// <summary>
        /// 这个**费用槽组合**在本 build 里注册过图标吗？
        /// </summary>
        /// <remarks>
        /// 用于校验多色混合槽（例如"虚色或红色"）。
        /// 游戏按**完整的有序颜色序列**在 <c>ManaIconDic</c> 里查图标，
        /// 所以混合槽的合法组合是**预制集合**、不是任意排列都行 ——
        /// 判定必须**有序匹配**（顺序不同算不同的键）。
        /// </remarks>
        bool IsKnownCostCombo(IReadOnlyList<ManaColor> combo);

        /// <summary>本 build 的原版卡名清单（用于校验 <c>artTemplate</c>）。</summary>
        IReadOnlyList<string> VanillaCardNames { get; }

        /// <summary>本 build 的原版遗物名清单。</summary>
        IReadOnlyList<string> VanillaRelicNames { get; }

        /// <summary>已注册的关键词名（含平台内置与原版）。</summary>
        IReadOnlyList<string> KnownKeywords { get; }

        /// <summary>
        /// 已注册的触发点名——**必须取自"游戏实际发出的名字"集合**。
        /// </summary>
        /// <remarks>
        /// 探针实测：游戏 <c>TriggerTargetInfo.AllTriggerType</c> 枚举与实际 <c>new TriggerBase("…")</c>
        /// 发出的名字<b>不一致</b>（枚举里有的从不发出，发出的有的不在枚举里）。
        /// 用枚举做校验会让作者"写了个永远不触发的钩子"却毫无提示。
        /// </remarks>
        IReadOnlyList<string> KnownTriggerNames { get; }

        /// <summary>
        /// 已注册的自定义效果 id（来自 <c>[ModEffect]</c> 扫描与显式登记）。
        /// </summary>
        IReadOnlyList<string> KnownCustomEffectIds { get; }

        /// <summary>
        /// 游戏里真实存在的本地化表名（来源见 <see cref="GameFacts.LocalizationTableNames"/>）。
        /// </summary>
        /// <remarks>
        /// 作者写了不存在的表名时，游戏只会"取表失败 → 跳过"，表现为**文案不生效**。
        /// 表名是纯字符串、没有编译期检查，所以平台替他查。
        /// </remarks>
        IReadOnlyList<string> KnownTableNames { get; }

        /// <summary>
        /// 平台内置效果语义的真实可用性。
        /// 校验器用它把"未实证"的语义降级为 Warning 而不是 Error——
        /// 允许作者试用，但会在日志里明确标注"未经实机验证"（认知纪律 C9）。
        /// </summary>
        EvidenceStatus StatusOfBuiltin(BuiltinEffect effect);
    }

    /// <summary>
    /// <see cref="IContentCapabilities"/> 的默认实现 —— **以后新增能力字段只需要改这一处**。
    /// </summary>
    /// <remarks>
    /// 2026-10-13 新增的"防腐"基类。背景：同一个接口此前在 **6 个地方各写一遍**
    /// （`ModKit.Tools` 的 2 个实现、`ModKit.Tests` 的 2 个 fake、`ModKit.Game` 的探针、`ModKit.Core.ModLoader` 的包装），
    /// 加一个字段要改 6 处，**漏一处就是静默错**（不是编译错，是行为悄悄不对）。
    /// <para>
    /// 继承本类后，各实现只覆写自己**真正有数据**的成员；默认值一律取"**不知道就别拦**"：
    /// </para>
    /// <list type="bullet">
    /// <item>所有清单默认<b>空</b> —— 空 = 本地无从判断，校验器据此**只给警告**而不是报错；</item>
    /// <item><see cref="IsKnownCostCombo"/> 默认 <c>true</c>（不知道就不拦）；</item>
    /// <item><see cref="KnownTableNames"/> 默认走 <see cref="GameFacts.TableNameList"/>（所有实现共用同一份常量）；</item>
    /// <item><see cref="StatusOfBuiltin"/> 默认走**唯一真源** <see cref="BuiltinEffectCatalog.StatusOf"/>，不允许各实现自己写一套。</item>
    /// </list>
    /// <para>
    /// ⚠️ 把某个实现改成继承本类时，**必须同时删掉它里面与本类同名的成员**（否则 C# 会给 CS0114/CS0108 隐藏警告）。
    /// </para>
    /// </remarks>
    public abstract class CapabilitiesBase : IContentCapabilities
    {
        private static readonly IReadOnlyList<string> Empty = new List<string>();
        private static readonly IReadOnlyList<ManaColor> EmptyColors = new List<ManaColor>();

        /// <summary>默认空 = 本次探测没拿到可用颜色集合 ⇒ 校验器只给警告。</summary>
        public virtual IReadOnlyList<ManaColor> AvailableCostColors { get { return EmptyColors; } }

        /// <summary>默认"不知道 ⇒ 不拦"。</summary>
        public virtual bool IsKnownCostCombo(IReadOnlyList<ManaColor> combo) { return true; }

        public virtual IReadOnlyList<string> VanillaCardNames { get { return Empty; } }
        public virtual IReadOnlyList<string> VanillaRelicNames { get { return Empty; } }
        public virtual IReadOnlyList<string> KnownKeywords { get { return Empty; } }
        public virtual IReadOnlyList<string> KnownTriggerNames { get { return Empty; } }
        public virtual IReadOnlyList<string> KnownCustomEffectIds { get { return Empty; } }
        public virtual IReadOnlyList<string> KnownTableNames { get { return GameFacts.TableNameList; } }

        public virtual EvidenceStatus StatusOfBuiltin(BuiltinEffect effect)
        {
            return BuiltinEffectCatalog.StatusOf(effect);
        }
    }
}
