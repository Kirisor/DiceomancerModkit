using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Card;
using Combat;
using Command;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using Keyword;
using ModKit.Api;
using UnityEngine;

/// <summary>
/// 关键词「奶蛙」（id <c>milk_frog</c>）：
/// 本卡结算完成后，把**仍在抽牌堆 / 弃牌堆**里的其它「奶蛙」也各结算一次
/// （等价于免费把它们打出一遍，不花能量；手牌与消耗堆不算）。
/// 同一次结算里被拉进来的奶蛙自己也会走这个钩子，但不会再带动别人（不连锁扩散）。
/// </summary>
public sealed class CardKeywordMilkFrog : CardKeyword
{
    /// <summary>关键词 id。游戏侧名字可能是 <c>milk_frog</c>，平台加前缀后是 <c>manual_demo.milk_frog</c>。</summary>
    internal const string MilkFrogLeaf = "milk_frog";

    /// <summary>本次「奶蛙连锁」里还有几张没结算完；&gt; 0 表示当前这次结算来自连锁，不再扩散。</summary>
    private static int _cascadeRemaining;

    /// <summary>当前这次卡牌结算是否来自「奶蛙连锁」（供自定义效果判断「被奶蛙时」）。</summary>
    internal static bool IsCascadeResolve;

    /// <summary>
    /// 拉取模式：0 = 自动（默认）；1 = 玩家从奶蛙卡里选；2 = 玩家从任意卡里选。
    /// 由「蛙母的号令」设置。
    /// </summary>
    internal static int SelectionMode;

    /// <summary>连锁开始时刻。兜底用：万一有张被拉进来的卡没跑到钩子，计数不会永久卡住。</summary>
    private static DateTime _cascadeStartedUtc;

    static CardKeywordMilkFrog()
    {
        // 关键字（静态构造函数）在游戏侧 Activator.CreateInstance 的那一刻先跑，
        // 而 CardKeyWordUtil 读数字是在 CreateInstance 之后 —— 正好赶得上打补丁。
        MilkFrogNumberPatch.EnsureInstalled();
    }

    /// <summary>关键词名是否是「奶蛙」（容忍 <c>manual_demo.milk_frog</c> 这类带 mod 前缀的写法）。</summary>
    internal static bool IsMilkFrogName(string keywordName)
    {
        if (string.IsNullOrEmpty(keywordName)) { return false; }
        int dot = keywordName.LastIndexOf('.');
        string leaf = dot >= 0 ? keywordName.Substring(dot + 1) : keywordName;
        return string.Equals(leaf, MilkFrogLeaf, StringComparison.OrdinalIgnoreCase);
    }

    public override UniTask CardResolvePhaseBeforeEffect(CardBase card)
    {
        IsCascadeResolve = _cascadeRemaining > 0 && !IsCascadeStale();
        return UniTask.CompletedTask;
    }

    public override UniTask CardResolvePhaseAfterEffect(CardBase card)
    {
        bool wasCascade = IsCascadeResolve;
        IsCascadeResolve = false;
        try
        {
            if (!wasCascade) { PullOthers(card); }
        }
        catch (Exception e)
        {
            Debug.LogError("[ModKit] 奶蛙：拉取其它奶蛙时出错 —— " + e);
        }
        return UniTask.CompletedTask;
    }

    private void PullOthers(CardBase card)
    {
        if (card == null) { return; }

        // 我是被这次连锁拉进来的那一张：只把计数减一，不再拉人。
        if (_cascadeRemaining > 0)
        {
            if (IsCascadeStale())
            {
                _cascadeRemaining = 0;   // 上一次连锁没走完（异常/中断），当作已结束
            }
            else
            {
                _cascadeRemaining--;
                return;
            }
        }

        // 从上往下打出来的那一张 = 连锁的起点。
        _cascadeRemaining = 0;

        CardManager manager = CardManager.Instance;
        if (manager == null) { return; }

        int limit = RNumber != null ? RNumber.GetStableIntNumber() : 1;
        if (limit <= 0) { return; }

        List<CardBase> found = new List<CardBase>();
        Collect(manager.GetCardLibrary(), card, limit, found);
        Collect(manager.GetCardGrave(), card, limit, found);

        // 蛙母的号令：改成玩家选择（候选来自抽牌堆 + 弃牌堆）
        if (SelectionMode > 0)
        {
            TryPromptSelection(manager, card, limit);
            return;
        }

        if (found.Count == 0) { return; }

        _cascadeRemaining = found.Count;
        _cascadeStartedUtc = DateTime.UtcNow;

        string names = "";
        foreach (CardBase other in found)
        {
            if (names.Length > 0) { names += "、"; }
            names += other.CardName;
            // 用 AutoPlay 而不是 Util：CardBase.cs:1363 里，只要这张卡「需要对手目标」，
            // 而 cardResolveSource 不是 Copy/Spawn/AutoPlay，CardResolveAndCardFeedback 会直接
            // return —— 整张卡什么都不做。AutoPlay 会自动给它挑一个敌人，卡才会真的结算完。
            new CommandResolveCard(other, CardResolveSource.AutoPlay);
        }
        Debug.Log("[ModKit] 奶蛙：本卡结算后，额外结算 " + found.Count + " 张奶蛙（" + names + "）。");
        PlayCascadeSound();
    }

    /// <summary>
    /// 连锁**真的发生**时出声（2026-10-15 新增）。
    /// 先试作者自带的 <c>&lt;mod 目录&gt;/sfx/naifrog.wav</c>；没有就回退原版音效「CopyCard」。
    /// 音效失败一律静默（平台侧已记警告）——绝不能因为没声音打断结算。
    /// </summary>
    private static void PlayCascadeSound()
    {
        try
        {
            // mod DLL 是平台用 Assembly.LoadFrom 载入的 ⇒ Assembly.Location 就是 mod 目录
            // （BepInEx\DiceomancerMods\manual_demo\manual_demo.dll），素材放它旁边的 sfx\ 里。
            string dir = Path.GetDirectoryName(typeof(CardKeywordMilkFrog).Assembly.Location);
            if (!string.IsNullOrEmpty(dir))
            {
                string wav = Path.Combine(Path.Combine(dir, "sfx"), "naifrog.wav");
                if (ModKitSfx.PlayFile(wav)) { return; }
            }
        }
        catch
        {
            // 拿不到路径 = 当作"没自带音效"，走下面的原版回退
        }

        ModKitSfx.Play("CopyCard");
    }

    private static bool IsCascadeStale()
    {
        return (DateTime.UtcNow - _cascadeStartedUtc).TotalSeconds > 20.0;
    }

    /// <summary>
    /// 「蛙母的号令」形态：把候选卡摆给玩家挑，挑中的按奶蛙连锁再结算一次。
    /// </summary>
    private static void TryPromptSelection(CardManager manager, CardBase self, int limit)
    {
        try
        {
            List<CardBase> candidates = new List<CardBase>();
            CollectCandidates(manager.GetCardLibrary(), self, candidates);
            CollectCandidates(manager.GetCardGrave(), self, candidates);
            if (candidates.Count == 0)
            {
                Debug.Log("[ModKit] 蛙母的号令：牌库与弃牌堆里没有可选的卡。");
                return;
            }

            PickCardManager.PickCardConfigure cfg = new PickCardManager.PickCardConfigure(
                PickCardManager.PickCardPoolType.DefiniteCardList,
                null,
                PickCardManager.PickCardCountMode.Upto,
                Math.Min(limit, candidates.Count),
                candidates.ToArray());
            cfg.actionNameKeyword = "Key_Kick";
            cfg.titleKeyword = "Title_Kick";
            cfg.PickCardsArraySubmitAction = cards => ResolveChosen(cards);
            new SubCommandPickCardAndAction(cfg);
        }
        catch (Exception e)
        {
            Debug.LogError("[ModKit] 蛙母的号令：弹出选择失败，回退为自动 —— " + e);
        }
    }

    private static UniTask ResolveChosen(CardBase[] cards)
    {
        if (cards == null || cards.Length == 0) { return UniTask.CompletedTask; }
        _cascadeRemaining = cards.Length;
        _cascadeStartedUtc = DateTime.UtcNow;
        string names = "";
        foreach (CardBase c in cards)
        {
            if (c == null) { continue; }
            if (names.Length > 0) { names += "、"; }
            names += c.CardName;
            new CommandResolveCard(c, CardResolveSource.AutoPlay);
        }
        Debug.Log("[ModKit] 蛙母的号令：玩家选择了 " + cards.Length + " 张（" + names + "）。");
        PlayCascadeSound();
        return UniTask.CompletedTask;
    }

    private static void CollectCandidates(CardBase[] pile, CardBase self, List<CardBase> found)
    {
        if (pile == null) { return; }
        foreach (CardBase other in pile)
        {
            if (other == null || other == self) { continue; }
            if (found.Contains(other)) { continue; }
            // mode 1：只允许奶蛙卡；mode 2：任意卡
            if (SelectionMode < 2 && !HasMilkFrog(other)) { continue; }
            found.Add(other);
        }
    }

    private static void Collect(CardBase[] pile, CardBase self, int limit, List<CardBase> found)
    {
        if (pile == null) { return; }
        foreach (CardBase other in pile)
        {
            if (found.Count >= limit) { return; }
            if (other == null || other == self) { continue; }
            if (found.Contains(other)) { continue; }
            if (!HasMilkFrog(other)) { continue; }
            found.Add(other);
        }
    }

    private static bool HasMilkFrog(CardBase card)
    {
        List<CardKeyword> keywords = card.CardKeywords;
        if (keywords == null) { return false; }
        foreach (CardKeyword keyword in keywords)
        {
            if (keyword != null && IsMilkFrogName(keyword.KeywordName)) { return true; }
        }
        return false;
    }
}

/// <summary>
/// 「奶蛙」带数字所需要的第 2 步（第 1 步是关键词 JSON 里的 <c>"hasNumber": true</c>）。
/// <para>
/// 游戏判断一个关键词有没有数字，看的是 <see cref="CardKeyWordUtil.GetHasNumberFromName"/> ——
/// 那是一张**写死的原版关键词名单**（Fracture / Loaded / Summon … 共 21 个）。
/// 自定义关键词不在名单里，游戏就会**静默跳过**整个数字分支（不报错、不崩），
/// 卡面显示成一个光秃秃的「奶蛙」，<c>RNumber</c> 也是 null。
/// 这里只往那个判断里加一条「奶蛙」，不改动它的任何原有行为。
/// </para>
/// </summary>
[HarmonyPatch(typeof(CardKeyWordUtil), nameof(CardKeyWordUtil.GetHasNumberFromName))]
internal static class MilkFrogNumberPatch
{
    private static bool _installed;

    private static void Postfix(string name, ref bool __result)
    {
        if (__result) { return; }
        if (CardKeywordMilkFrog.IsMilkFrogName(name)) { __result = true; }
    }

    /// <summary>幂等安装；失败只记日志，不抛（否则会把关键词实例化一起带崩，静默退化）。</summary>
    internal static void EnsureInstalled()
    {
        if (_installed) { return; }
        _installed = true;
        try
        {
            new Harmony("modkit.manual_demo.milk_frog").PatchAll(typeof(MilkFrogNumberPatch).Assembly);
            Debug.Log("[ModKit] 奶蛙：已把 milk_frog 加入「关键词带数字」名单。");
        }
        catch (Exception e)
        {
            Debug.LogError("[ModKit] 奶蛙：打补丁失败，数字将不会显示 —— " + e);
        }
    }
}
