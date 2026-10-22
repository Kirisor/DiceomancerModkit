// =============================================================================
//  「被奶蛙结算时」触发点 —— 可被任意卡共用的模块
//
//  游戏里的「抽到时：」是怎么实现的（照着它做）：
//    · 每个效果各自带一个 CardEffectResolveType（UponDraw / UponDiscard / EndInHand）；
//    · 卡面文案由 EffectBase.GetEffectLocalizeDescription 自动加前缀
//      （EffectDescription/Key_Card_UponDraw = 「抽到时：」）；
//    · 卡被抽到时 CardBase.CardBeenDrawn 只结算这些带 UponDraw 的效果，
//      打出时（CardResolve 只跑 Cast）不会结算它们。
//
//  本模块把同一形状搬给「奶蛙」：
//    · 触发点 = 奶蛙连锁结算某张卡的那段时间（窗口）；
//    · 效果只要继承 CascadeTriggeredEffect，就只在窗口里生效；
//    · 卡面文案由作者在效果描述键里写「被奶蛙结算时：…」，与游戏前缀同一版式。
//
//  ★ 窗口的开合由连锁结算命令自己保证（try/finally），不靠 static 开关的隐式约定：
//    连锁结算走 CommandCascadeResolveCard，窗口包住这张卡**整个**结算过程，
//    结算里任何异常 / 提前 return 都会在 finally 关窗 —— 不会漏关，也不会误开。
// =============================================================================

using Card;
using Command;
using Cysharp.Threading.Tasks;
using ModKit.Api;

/// <summary>
/// 「被奶蛙结算时」触发点。
/// </summary>
/// <remarks>
/// 两种用法（都只依赖这一个模块，不需要在效果里写任何连锁判定）：
/// <list type="number">
/// <item>整个效果只在此触发点生效 —— 继承 <see cref="CascadeTriggeredEffect"/>；</item>
/// <item>效果里只有一半逻辑吃这个触发点 —— 读 <see cref="IsResolving"/> 自己分流。</item>
/// </list>
/// </remarks>
public static class MilkFrogCascade
{
    private static int _depth;

    /// <summary>触发点当前是否打开（正处在「被奶蛙结算」的这张卡里）。</summary>
    public static bool IsResolving
    {
        get { return _depth > 0; }
    }

    /// <summary>
    /// 把一张卡按「奶蛙连锁」结算一次（不花能量、自动挑目标，与原版 AutoPlay 同一条流程）。
    /// </summary>
    /// <remarks>
    /// 与 <c>new CommandResolveCard(card, CardResolveSource.AutoPlay)</c> 的差别只有一处：
    /// 结算期间触发点窗口是打开的。所以**连锁里所有"再结算一张卡"的地方都该走这里**，
    /// 否则那张卡上的「被奶蛙结算时」效果不会生效。
    /// </remarks>
    public static void ResolveAsCascade(CardBase card)
    {
        if (card == null) { return; }
        new CommandCascadeResolveCard(card);   // 构造即入队（命令队列，与原版同语义）
    }

    internal static void Open()
    {
        _depth++;
    }

    internal static void Close()
    {
        if (_depth > 0) { _depth--; }
    }
}

/// <summary>
/// 连锁结算命令：走原版 <see cref="CommandResolveCard"/> 的全流程，
/// 只在结算期间把「被奶蛙结算时」触发点打开。
/// </summary>
internal sealed class CommandCascadeResolveCard : CommandResolveCard
{
    public CommandCascadeResolveCard(CardBase card)
        : base(card, CardResolveSource.AutoPlay)
    {
    }

    public override async UniTask Execute()
    {
        MilkFrogCascade.Open();
        try
        {
            await base.Execute();
        }
        finally
        {
            MilkFrogCascade.Close();
        }
    }
}

/// <summary>
/// 「被奶蛙结算时：」效果的基类 —— 继承它，整个效果就只在这个触发点生效。
/// </summary>
/// <remarks>
/// 卡面文案由效果描述键自己写（<c>Key_&lt;效果id&gt;</c>，形如
/// 「被奶蛙结算时：对所有敌人施加 {Number} 层易伤。」），
/// 与游戏「抽到时：」的前缀来源（<c>Key_Card_UponDraw</c>）是同一种做法：
/// 前缀是文案约定，触发点由机制保证。
/// </remarks>
public abstract class CascadeTriggeredEffect : SyncCardEffect
{
    /// <inheritdoc />
    public sealed override void Apply(EffectContext ctx)
    {
        if (!MilkFrogCascade.IsResolving) { return; }
        ApplyWhenCascading(ctx);
    }

    /// <summary>触发点打开时才执行的效果体。</summary>
    public abstract void ApplyWhenCascading(EffectContext ctx);
}
