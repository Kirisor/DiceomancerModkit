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
using Trigger;
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
    /// <summary>关键词 id。游戏侧名字可能是 <c>milk_frog</c>，平台加前缀后是 <c>naifrog.milk_frog</c>。</summary>
    internal const string MilkFrogLeaf = "milk_frog";

    /// <summary>
    /// 当前拉取模式：0 = 自动；1 = 玩家从奶蛙卡里选；2 = 玩家从任意卡里选。
    /// ★ 由**能力**决定（不再用 mod 静态字段）：
    ///   「蛙母的号令」挂 <c>milkfrog_choice</c>（选奶蛙卡）或
    ///   <c>milkfrog_choice_full</c>（选任意卡）；
    ///   能力由游戏的战斗生命周期管理，战斗结束自动清 —— 与「压制射击 / 战争之王」同构。
    /// </summary>
    internal static int SelectionMode
    {
        get
        {
            if (HasAbility("milkfrog_choice_full")) { return 2; }
            if (HasAbility("milkfrog_choice")) { return 1; }
            return 0;
        }
    }

    /// <summary>玩家是否持有某个能力（容忍任意 mod 前缀：按**叶子名**比）。</summary>
    /// <remarks>
    /// 实机教训：mod 能力在游戏里注册成 <c>&lt;modId&gt;.&lt;id&gt;</c>（如 <c>naifrog.milkfrog_choice</c>），
    /// 而作者在卡里写的是短名。只按固定前缀猜（上一版写死 <c>naifrog.</c>）会在 mod 再改名时
    /// 静默失效 —— 症状是「以母之名」打了、能力也在身上，但奶蛙拉取仍走自动。
    /// 这里改为逐条比对玩家能力列表的叶子名，任何前缀都能命中。
    /// </remarks>
    internal static bool HasAbility(string abilityName)
    {
        try
        {
            var player = CharacterPlayer.Instance;
            if (player == null) { return false; }
            Ability.AbilityBase _;
            if (player.HasCertainAbility(abilityName, out _)) { return true; }

            var list = player.abilities;
            if (list == null) { return false; }
            string leaf = LeafOf(abilityName);
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a == null || string.IsNullOrEmpty(a.abilityName)) { continue; }
                if (string.Equals(LeafOf(a.abilityName), leaf, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ModKit] 奶蛙：查询能力 '" + abilityName + "' 失败 —— " + e.Message);
        }
        return false;
    }

    /// <summary>取名字的最后一段（<c>naifrog.milkfrog_choice</c> → <c>milkfrog_choice</c>）。</summary>
    internal static string LeafOf(string name)
    {
        if (string.IsNullOrEmpty(name)) { return name; }
        int dot = name.LastIndexOf('.');
        return dot >= 0 && dot + 1 < name.Length ? name.Substring(dot + 1) : name;
    }

    /// <summary>诊断用：玩家身上带 <paramref name="leaf"/> 的那个能力的**完整名**；没有则 null。</summary>
    internal static string FindAbilityNameByLeaf(string leaf)
    {
        try
        {
            var player = CharacterPlayer.Instance;
            if (player == null || player.abilities == null) { return null; }
            foreach (var a in player.abilities)
            {
                if (a == null || string.IsNullOrEmpty(a.abilityName)) { continue; }
                if (string.Equals(LeafOf(a.abilityName), leaf, StringComparison.OrdinalIgnoreCase))
                {
                    return a.abilityName;
                }
            }
        }
        catch { }
        return null;
    }

    static CardKeywordMilkFrog()
    {
        // 关键字（静态构造函数）在游戏侧 Activator.CreateInstance 的那一刻先跑，
        // 而 CardKeyWordUtil 读数字是在 CreateInstance 之后 —— 正好赶得上打补丁。
        MilkFrogNumberPatch.EnsureInstalled();
    }

    /// <summary>关键词名是否是「奶蛙」（容忍 <c>naifrog.milk_frog</c> 这类带 mod 前缀的写法）。</summary>
    internal static bool IsMilkFrogName(string keywordName)
    {
        if (string.IsNullOrEmpty(keywordName)) { return false; }
        int dot = keywordName.LastIndexOf('.');
        string leaf = dot >= 0 ? keywordName.Substring(dot + 1) : keywordName;
        return string.Equals(leaf, MilkFrogLeaf, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 本卡结算收尾：只有**正常打出**的奶蛙才继续拉别人；连锁进来的奶蛙不再扩散。
    /// </summary>
    /// <remarks>
    /// 「这一张是不是连锁结算的」不再由本关键词记账 —— 它读的是
    /// <see cref="MilkFrogCascade.IsResolving"/>（连锁结算命令自己开合的窗口）。
    /// 这样其它卡只要继承 <see cref="CascadeTriggeredEffect"/> 就能共用同一个触发点。
    /// </remarks>
    public override UniTask CardResolvePhaseAfterEffect(CardBase card)
    {
        try
        {
            if (!MilkFrogCascade.IsResolving) { PullOthers(card); }
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


        CardManager manager = CardManager.Instance;
        if (manager == null) { return; }

        int limit = RNumber != null ? RNumber.GetStableIntNumber() : 1;
        if (limit <= 0) { return; }

        List<CardBase> found = new List<CardBase>();
        Collect(manager.GetCardLibrary(), card, limit, found);
        Collect(manager.GetCardGrave(), card, limit, found);

        // 蛙母的号令：改成玩家选择（候选来自抽牌堆 + 弃牌堆）
        int mode = SelectionMode;
        if (mode > 0)
        {
            Debug.Log("[ModKit] 奶蛙：蛙母的号令生效（mode=" + mode + "），改为玩家选择。");
            TryPromptSelection(manager, card, limit);
            return;
        }

        // 诊断：玩家身上明明有「蛙母的号令」能力，却没被认出来 —— 正常情况下不会出现；
        // 出现就说明能力名比对又漂了（例如游戏/平台改了能力名的拼法）。
        string stray = FindAbilityNameByLeaf("milkfrog_choice_full");
        if (stray == null) { stray = FindAbilityNameByLeaf("milkfrog_choice"); }
        if (stray != null)
        {
            Debug.LogWarning("[ModKit] 奶蛙：玩家身上有「" + stray + "」，但 SelectionMode=0 —— 能力名没匹配上。");
        }

        if (found.Count == 0) { return; }

        string names = "";
        foreach (CardBase other in found)
        {
            if (names.Length > 0) { names += "、"; }
            names += other.CardName;
            // 走平台的连锁结算入口：它用 AutoPlay（需要对手目标的卡才会真的结算完），
            // 同时把「被奶蛙结算时」触发点的窗口包在这张卡的整个结算过程上。
            MilkFrogCascade.ResolveAsCascade(other);
        }
        Debug.Log("[ModKit] 奶蛙：本卡结算后，额外结算 " + found.Count + " 张奶蛙（" + names + "）。");

        // 通知遗物系统："奶蛙"关键词刚刚触发了一次。
        // 「奶蛙之护符」等的触发点正是 CardKeywordTrigger（匹配 extraString）。
        try
        {
            TriggerBase keywordTrigger = new TriggerBase("CardKeywordTrigger")
            {
                Card = card,
                ExtraString = "milk_frog"
            };
            TriggerManager.Instance.AddTrigger(keywordTrigger);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ModKit] 奶蛙：抛关键词触发事件失败 —— " + e.Message);
        }

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
            // （BepInEx\DiceomancerMods\naifrog\naifrog.dll），素材放它旁边的 sfx\ 里。
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
        string names = "";
        foreach (CardBase c in cards)
        {
            if (c == null) { continue; }
            if (names.Length > 0) { names += "、"; }
            names += c.CardName;
            MilkFrogCascade.ResolveAsCascade(c);
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
            new Harmony("modkit.naifrog.milk_frog").PatchAll(typeof(MilkFrogNumberPatch).Assembly);
            Debug.Log("[ModKit] 奶蛙：已把 milk_frog 加入「关键词带数字」名单。");
        }
        catch (Exception e)
        {
            Debug.LogError("[ModKit] 奶蛙：打补丁失败，数字将不会显示 —— " + e);
        }
    }
}
