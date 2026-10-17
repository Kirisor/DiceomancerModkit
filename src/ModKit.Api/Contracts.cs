// =============================================================================
//  ModKit.Api · 核心契约
//
//  ★ 规则 1（机械保证）：本程序集**不得引用任何游戏类型**。
//    只允许 BCL（System.*）。禁止 using Card / Effects / Combat / UnityEngine /
//    Cysharp.Threading.Tasks。
//    校验方式：编译本工程时只喂 net472 引用程序集，不喂 Diceomancer_Data\Managed。
//    引用即编译失败 —— 与第 5 步「核只喂 net472 引用集」同一手法。
//
//  ★ 规则 2：每个对外成员都必须标实证档位（见 docs/ModKit-P0-内容契约与API规格.md §1）。
//
//  编译约束（与 mod/build.ps1 一致）：net472 / langversion:9 / nostdlib+
// =============================================================================

using System;

namespace ModKit.Api
{
    /// <summary>
    /// 平台日志出口。与既有 <c>SeamRegistry.LogSink</c> 同构：Api 不依赖 BepInEx，
    /// 因此本程序集可独立编译、也可在测试宿主里接自己的实现。
    /// </summary>
    public interface IModLog
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);

        /// <summary>错误 + 异常全文（平台统一在此处吞掉异常，避免一个 mod 拖垮游戏）。</summary>
        void Error(string message, Exception ex);
    }

    /// <summary>
    /// 战斗单位的选择器。作者只写"打谁"，平台负责解析成游戏的目标集。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>只有 5 个取值</b>——探针实测：游戏 <c>EffectBase.ResolveEffectAndGetCharacter</c> 的
    /// switch 只处理 5 个，<c>AllAllies</c>/<c>OtherAllies</c> 落到 switch 之外会<b>静默不执行</b>。
    /// 因此平台不暴露那两个，从源头消除"写了没反应"。
    /// </para>
    /// </remarks>
    public enum CombatantSelector
    {
        Self = 0,
        Opponent = 1,
        AllOpponents = 2,
        All = 3,
        RandomOpponent = 4
    }

    /// <summary>
    /// 伤害模式。<b>必须由作者显式指定，平台不提供默认值。</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 为什么不给默认值：探针实测 <c>DamageSourceType.DirectDamage</c> 会走命中判定与全部伤害调整器，
    /// 而 <c>IndirectDamage</c> 直接 <c>TakeDamage</c>（跳过 <c>HitCheck</c> 与 <c>AdjustDamage</c>）。
    /// 默认值会让作者在不知情的情况下改变玩法强度——这是静默的行为差异，必须由作者签字。
    /// </para>
    /// </remarks>
    public enum DamageMode
    {
        /// <summary>走命中判定与伤害调整器（等效游戏的 DirectDamage）。</summary>
        Direct = 0,

        /// <summary>跳过命中判定与调整器（等效游戏的 IndirectDamage）。</summary>
        Indirect = 1,

        /// <summary>直接扣生命，<b>无视格挡</b>（等效 DirectLoseLife）。</summary>
        LoseLife = 2
    }

    /// <summary>卡牌结算时机。</summary>
    public enum ResolveTiming
    {
        OnCast = 0,
        OnDraw = 1,
        OnDiscard = 2,
        EndInHand = 3
    }

    /// <summary>稀有度。</summary>
    public enum Rarity
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2
    }

    /// <summary>法力/流派颜色。<b>只有这 6 个单色</b>。</summary>
    /// <remarks>
    /// <para>
    /// <b>为什么只到单色</b>：游戏的费用键比较器用有序 <c>SequenceEqual</c>，
    /// 多色组合是否合法取决于目标 build 的场景是否注册了该图标键（源码中不可得）。
    /// 平台在启动时用 <c>ManaIconDic.ContainsKey</c> 探测真实可用集合，
    /// 未注册的颜色在加载期报错——而不是让游戏抛一个报错信息不可用的 <c>KeyNotFoundException</c>。
    /// </para>
    /// </remarks>
    public enum ManaColor
    {
        Red = 0,
        Green = 1,
        Blue = 2,
        Purple = 3,
        Void = 4,
        Black = 5
    }

    /// <summary>卡牌去向（生成卡牌时用）。</summary>
    public enum CardDestination
    {
        Hand = 0,
        Library = 1,
        Grave = 2
    }
}
