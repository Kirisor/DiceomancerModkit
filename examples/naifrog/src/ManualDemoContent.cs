// =============================================================================
//  naifrog 的 C# 部分 —— 手册 §6 自定义关键词 + §7 自定义效果的最小实例。
//
//  由 modkit\build-mod.ps1 编译成 naifrog.dll：
//    powershell -ExecutionPolicy Bypass -File build-mod.ps1 -Mod naifrog ^
//        -ModsRoot G:\deepseek space\diceomancer-mod\state\scratch\manual-examples
//
//  这个文件里有两个类：
//    · CardKeywordManualBulwark —— 关键词「手册坚壁」。类名 = "CardKeyword" + id 首字母大写。
//    · ManualGainBlockEffect    —— [ModEffect("ManualGainBlock")]，被 cards/manual_custom.json 引用。
// =============================================================================

using Card;                      // CardBase
using Combat;                    // CharacterPlayer
using Cysharp.Threading.Tasks;   // UniTask
using Keyword;                   // CardKeyword
using ModKit.Api;                // [ModEffect] / SyncCardEffect / EffectContext

/// <summary>关键词「手册坚壁」：本卡结算完成后，额外获得 3 点格挡。</summary>
public sealed class CardKeywordManualBulwark : CardKeyword
{
    public override UniTask CardResolvePhaseAfterEffect(CardBase card)
    {
        CharacterPlayer player = CharacterPlayer.Instance;
        if (player != null)
        {
            player.ApplyBlock(3);
        }
        return UniTask.CompletedTask;
    }
}

/// <summary>作者自写效果：获得 Amount 点格挡。</summary>
[ModEffect("ManualGainBlock")]
public sealed class ManualGainBlockEffect : SyncCardEffect
{
    public override void Apply(EffectContext ctx)
    {
        ctx.Block(ctx.Amount);
    }
}
