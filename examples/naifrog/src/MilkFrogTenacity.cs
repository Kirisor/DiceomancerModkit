// =============================================================================
//  奶蛙 · 「本回合每结算 1 张奶蛙卡，额外获得 N 点坚韧」
//
//  为什么需要单独一个模块：内置效果表达不了"按本回合奶蛙结算次数累加"这件事
//  —— 它既不是某个卡的效果（一次结算），也不是能力（能力只能挂在触发点上，
//  且平台的能力 JSON 没有 extraString/triggerLimit 过滤 CardKeywordTrigger）。
//
//  ★ "本回合"怎么保证：不挂回合结束钩子，而是记住**开启时的回合号**，
//    读取时比对 BattleManager.GetTurnCount() —— 换回合自然失效，零钩子、零泄漏。
// =============================================================================

using System;
using Ability;
using Card;
using Combat;
using Command;
using Combat.BattleWorld;
using GameData;
using UnityEngine;

/// <summary>「本回合每结算 1 张奶蛙卡，额外获得 N 点坚韧」的回合内状态。</summary>
public static class MilkFrogTenacity
{
    private static int _amount;
    private static int _turn = -1;

    /// <summary>由卡片效果开启（本回合有效）。</summary>
    public static void Enable(int amount)
    {
        _amount = amount < 1 ? 0 : amount;
        _turn = CurrentTurn();
    }

    /// <summary>当前是否处于"本回合"窗口内；换回合自动清零。</summary>
    public static int ActiveAmount
    {
        get
        {
            if (_amount <= 0) { return 0; }
            int now = CurrentTurn();
            if (_turn < 0 || now < 0 || now != _turn)
            {
                _amount = 0;
                _turn = -1;
                return 0;
            }
            return _amount;
        }
    }

    /// <summary>给玩家施加 <paramref name="amount"/> 层坚韧（原版能力 <c>Tough</c>）。</summary>
    public static void ApplyTough(int amount)
    {
        if (amount <= 0) { return; }
        try
        {
            Character player = CharacterPlayer.Instance;
            DataManager dm = DataManager.Instance;
            if (player == null || dm == null) { return; }

            var adm = dm.GetAbilityDataManager();
            if (adm == null) { return; }

            AbilitySo so = adm.GetAbilitySo("Tough");
            if (so == null || so.abilityInfo == null)
            {
                Debug.LogWarning("[ModKit] 坚韧：游戏里取不到能力 Tough。");
                return;
            }
            new SubCommandApplyAbility(player, so.abilityInfo, amount);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ModKit] 坚韧：施加失败 —— " + e.Message);
        }
    }

    private static int CurrentTurn()
    {
        try
        {
            BattleManager bm = BattleManager.Instance;
            return bm == null ? -1 : bm.GetTurnCount();
        }
        catch (Exception)
        {
            return -1;
        }
    }
}
