// =============================================================================
//  ModKit.Api · 职业（Class）契约 —— 2026-10-24 补回。
//
//  ★ 为什么是"补回"：这两个类型一直存在于**已发布的 ModKit.Api.dll** 里
//    （mod 的 `naifrog.dll` 就是实现 `IModClassProvider` 编译出来的），但仓库源码里
//    从来没有对应的 .cs —— 属于"四件套不能从源码重建"的欠账之一。
//    本轮（给骰子加 sides/count）必须重编 Api，因此先把它们按已发布 DLL 的定义补进来。
//
//  ★ 定义来源：反编译 `BepInEx/plugins/ModKit.Api.dll`（2026-10-24），字段名/类型逐字照抄。
// =============================================================================

using System.Collections.Generic;

namespace ModKit.Api
{
    /// <summary>mod 的「职业」声明：一个职业的全部静态配置。</summary>
    /// <remarks>
    /// 由 mod 的 <see cref="IModClassProvider"/> 返回，平台在装配期读取。
    /// 字段都是**公开字段**（不是属性）—— 与已发布 DLL 的表面一致，别改成属性，
    /// 否则 mod 侧二进制就不兼容了。
    /// </remarks>
    public sealed class ClassSpec
    {
        /// <summary>职业 id（写进 `<modId>.<id>` 作为职业 key）。</summary>
        public string Id;

        /// <summary>职业名（中文，写进图鉴 `Encyclopedia` 表）。</summary>
        public string NameZh;

        /// <summary>职业介绍（中文）。</summary>
        public string IntroZh;

        /// <summary>职业三色（游戏按三色组合认职业）。</summary>
        public List<ManaColor> Colors;

        /// <summary>初始牌组（卡 id，可重复表示多张）。</summary>
        public List<string> StartDeck;

        /// <summary>本职业占用的球。</summary>
        public ManaColor OrbColor = ManaColor.Black;

        /// <summary>球的贴图路径（相对 mod 目录）。</summary>
        public string OrbSprite;

        /// <summary>球的缩放。</summary>
        public float BallScale = 1f;

        /// <summary>战役卡池（整局能抽到的卡）。</summary>
        public List<string> PoolCards;

        /// <summary>起始遗物（至少 2 件：职业介绍卡硬取第 2 件做护符预览）。</summary>
        public List<string> StartRelics;
    }

    /// <summary>mod 声明一个职业的入口：实现它，平台就会在装配期调用 <see cref="ProvideClass"/>。</summary>
    public interface IModClassProvider
    {
        /// <summary>返回职业声明；返回 null 表示本 mod 没有职业。</summary>
        ClassSpec ProvideClass();
    }
}
