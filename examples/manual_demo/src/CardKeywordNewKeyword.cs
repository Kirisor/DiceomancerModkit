using Cysharp.Threading.Tasks;
using Keyword;
using Card;

/// <summary>
/// 关键词「新关键词」（id: new_keyword）的行为实现。
/// 平台只负责把关键词标签放到卡上，真正的行为由游戏在对应时机调用这里的方法。
/// </summary>
public sealed class CardKeywordNewKeyword : CardKeyword
{
    // 卡牌结算完成之后 —— 最常见的时机；不需要就删掉这个重写
    public override UniTask CardResolvePhaseAfterEffect(CardBase card)
    {
        // TODO: 在这里写效果，例如：
        // CharacterPlayer player = CharacterPlayer.Instance;
        // if (player != null) { player.ApplyBlock(3); }
        return UniTask.CompletedTask;
    }
}
