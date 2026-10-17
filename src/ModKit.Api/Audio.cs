// =============================================================================
//  ModKit.Api · 音效针脚
//
//  ★ 规则 1：本程序集**不引用任何游戏类型**（下面只用 System / System.Collections.Generic）。
//  与 SeamRegistry.LogSink 同构：Api 只声明"能播音效"，**怎么播**由 ModKit.Game 装上实现。
//
//  作者视角只有两个方法：
//      ModKitSfx.Play("CopyCard")                      // 播原版音效（名字）
//      ModKitSfx.PlayFile(@"……\sfx\naifrog.wav")        // 播自带的 wav
//  两者都返回 bool；找不到/播不了时**只记一条平台警告，不抛异常**——
//  音效永远不该打断卡牌结算。
// =============================================================================

using System;
using System.Collections.Generic;

namespace ModKit.Api
{
    /// <summary>
    /// 播放音效的出口：原版 FMOD 音效（按名字）或作者自带的 wav 文件。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 平台在启动时把 <see cref="PlayHandler"/> / <see cref="PlayFileHandler"/> 接上；
    /// 作者在战斗逻辑里直接调 <see cref="Play"/> / <see cref="PlayFile"/> 即可，
    /// 不必知道游戏底层用的是 FMOD 还是 Unity 音频。
    /// </para>
    /// <para>
    /// 想知道"有哪些原版音效名可用"：读 <see cref="VanillaNames"/>（平台首次播放时才探测；
    /// 也可以在配置里打开 <c>Diagnostics/SfxDump</c>，平台会把整张名字表写进日志）。
    /// </para>
    /// </remarks>
    public static class ModKitSfx
    {
        /// <summary>播原版音效的实现（由平台装配）。参数是音效名，返回是否真的播了。</summary>
        public static Func<string, bool> PlayHandler;

        /// <summary>播磁盘 wav 的实现（由平台装配）。参数是绝对路径，返回是否真的播了。</summary>
        public static Func<string, bool> PlayFileHandler;

        private static IReadOnlyList<string> _vanillaNames = new string[0];

        /// <summary>
        /// 平台探测到的原版音效名。**首次播放之前是空表**（游戏的管理器要等它 Awake 才能读名单）。
        /// </summary>
        public static IReadOnlyList<string> VanillaNames { get { return _vanillaNames; } }

        /// <summary>平台在探测到名单后填入（作者一般不用调）。</summary>
        public static void SetVanillaNames(IReadOnlyList<string> names)
        {
            _vanillaNames = names ?? new string[0];
        }

        /// <summary>
        /// 播一个原版音效（名字见 <see cref="VanillaNames"/>，例如 <c>CopyCard</c>）。
        /// 名字不存在、管理器未就绪、平台没装配 ⇒ 返回 false（并已记警告）。
        /// </summary>
        public static bool Play(string soundName)
        {
            Func<string, bool> handler = PlayHandler;
            if (handler == null || string.IsNullOrEmpty(soundName)) { return false; }
            try { return handler(soundName); }
            catch { return false; }
        }

        /// <summary>
        /// 播一个 wav 文件（绝对路径）。文件不存在 ⇒ 返回 false **且不记警告**
        /// （"作者没自带音效"是正常情况，调用方据此回退到原版音效）。
        /// </summary>
        public static bool PlayFile(string wavPath)
        {
            Func<string, bool> handler = PlayFileHandler;
            if (handler == null || string.IsNullOrEmpty(wavPath)) { return false; }
            try { return handler(wavPath); }
            catch { return false; }
        }
    }
}
