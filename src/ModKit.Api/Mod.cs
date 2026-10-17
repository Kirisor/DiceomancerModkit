// =============================================================================
//  ModKit.Api · 代码 mod 的入口契约（C# 兜底路径）
//
//  纯数据 mod 不需要实现任何东西 —— 只写 mod.json + JSON 内容即可。
//  只有"游戏原生没有的新机制"才需要这里。
// =============================================================================

using System;
using System.Collections.Generic;

namespace ModKit.Api
{
    /// <summary>
    /// 代码 mod 的入口。实现类需带 <see cref="ModEntryAttribute"/>，
    /// 或与 mod.json 的 <c>id</c> 同名（平台按 id 匹配）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 平台保证：<see cref="OnLoad"/> 抛异常时该 mod 被标记失败并跳过，
    /// 其余 mod 与游戏本体照常运行。
    /// </para>
    /// </remarks>
    public interface IMod
    {
        /// <summary>
        /// mod 被加载时调用一次。
        /// </summary>
        /// <param name="ctx">
        /// 加载期上下文：登记自定义效果、订阅触发点、写日志。
        /// <b>此时不要直接操作游戏状态</b>——战斗还没开始，很多 Manager 尚未初始化。
        /// </param>
        void OnLoad(IModContext ctx);

        /// <summary>mod 被停用/卸载时调用。用于撤销订阅。可能不被调用（进程退出不等）。</summary>
        void OnUnload();
    }

    /// <summary>标注 mod 入口实现类，便于平台按特性索引。</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ModEntryAttribute : Attribute
    {
    }

    /// <summary>
    /// 加载期上下文。作者在这里做"登记"类动作，不做游戏状态修改。
    /// </summary>
    public interface IModContext
    {
        /// <summary>当前 mod 的 id。</summary>
        string ModId { get; }

        /// <summary>平台日志。</summary>
        IModLog Log { get; }

        /// <summary>
        /// 登记一个自定义效果类。
        /// </summary>
        /// <remarks>
        /// 平台也支持用 <see cref="ModEffectAttribute"/> 自动扫描；
        /// 显式登记用于"一个程序集里放多个 mod"或"效果类在别的程序集"的场景。
        /// </remarks>
        void RegisterEffect(string effectId, Func<ICardEffect> factory);

        /// <summary>
        /// 平台探测得到的能力表——作者可以据此在加载期决定是否启用某个功能。
        /// </summary>
        IPlatformCapabilities Capabilities { get; }
    }

    /// <summary>
    /// 平台启动时**探测**得到的能力表。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是"作者不用懂游戏"的关键机制：平台不硬编码"哪些颜色能用"，
    /// 而是在启动时探测目标 build 的真实能力，作者据此判断自己的内容能否加载。
    /// 例如 <c>Cost</c> 只列出**该 build 的场景里真的注册了图标键**的颜色。
    /// </para>
    /// </remarks>
    public interface IPlatformCapabilities
    {
        /// <summary>本次探测确认可用的单色集合。</summary>
        IReadOnlyList<ManaColor> AvailableCostColors { get; }

        /// <summary>本 build 的原版卡名清单（用于挑选 <c>artTemplate</c>）。</summary>
        IReadOnlyList<string> VanillaCardNames { get; }

        /// <summary>本 build 的原版遗物名清单。</summary>
        IReadOnlyList<string> VanillaRelicNames { get; }

        /// <summary>
        /// **实际发出**的触发点名集合。
        /// </summary>
        /// <remarks>
        /// 注意：不是游戏的 <c>AllTriggerType</c> 枚举——探针实测两者不一致
        /// （枚举里有的从不发出，发出的有的不在枚举里）。平台以"实际发出"为准。
        /// </remarks>
        IReadOnlyList<string> TriggerNames { get; }
    }
}
