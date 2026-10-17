// =============================================================================
//  ModKit.Game · 验收辅助：进入第一场战斗后把 mod 内容发给玩家
//
//  ★ 为什么需要它（用户提出，2026-10-11）：
//    "一局局去找效率太低了" —— 遗物/能力/多效果卡在池子里靠随机碰，可能几十局都遇不到。
//    要在一局里把**全部内容类型**都验证到，就得主动发放。
//
//  ★ 为什么上一版（GrantToHand）失败了：
//    它把卡塞进手牌，但那发生在**初始发牌之前**——随后发牌把手牌重排，塞的卡就没了。
//    用户原话："在一局游戏开始之前就把卡牌发放到了玩家手里，结果就是会被选择好职业之后、
//    开始游戏前清除掉"。
//
//  ★ 现在的做法：**挂在发牌之后**。
//    挂钩 `CardManager.DrawCards(int)` —— 回合开始时由 `CharacterPlayer` 调用
//    （`CharacterPlayer.cs:304`）。第一次发牌发生时启动一个协程，
//    **等两帧**（让发牌真正落地）再发放。协程宿主用游戏自己的 `CardManager`（MonoBehaviour），
//    所以不存在"我的常驻对象被场景销毁"的问题。
//
//  ★ 默认关闭。它是**验收辅助**，会改变玩法（凭空多卡、多遗物、多能力）。
//    正式游玩必须为 false。
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using Ability;
using Card;
using Combat;
using GameData;
using HarmonyLib;
using Relic;
using UnityEngine;

namespace ModKit.Game
{
    /// <summary>验收辅助：进入第一场战斗后把 mod 内容发给玩家。</summary>
    public static class VerificationGrant
    {
        /// <summary>是否启用（由插件从配置读入；默认关闭）。</summary>
        public static bool Enabled = false;

        /// <summary>
        /// 验收发放时**每张卡发几张**（配置 <c>GrantCardCopies</c>，默认 1；小于 1 按 1 处理）。
        /// 用途：像「奶蛙」这种"同一张卡互相联动"的关键词，牌库里只有 1 张根本测不出效果。
        /// </summary>
        public static int CardCopies = 1;

        private static readonly object Gate = new object();
        private static readonly List<string> CardNames = new List<string>();
        private static readonly List<string> RelicNames = new List<string>();
        private static readonly List<string> AbilityNames = new List<string>();

        private static IGameLog _log;
        private static bool _armed;     // 已挂钩
        private static bool _done;      // 已发放过（每次游戏会话只发一次）

        public static bool Installed { get { return _armed; } }

        /// <summary>登记要发放的内容名（由流水线装配成功后调用）。</summary>
        public static void Register(List<string> cards, List<string> relics, List<string> abilities, IGameLog log)
        {
            _log = log;
            lock (Gate)
            {
                CardNames.Clear(); RelicNames.Clear(); AbilityNames.Clear();
                if (cards != null) { CardNames.AddRange(cards); }
                if (relics != null) { RelicNames.AddRange(relics); }
                if (abilities != null) { AbilityNames.AddRange(abilities); }
            }
        }

        /// <summary>装挂钩。返回是否成功。</summary>
        public static bool Install(IGameLog log)
        {
            _log = log;
            if (_armed) { return true; }

            try
            {
                var target = AccessTools.Method(typeof(CardManager), "DrawCards", new[] { typeof(int) });
                if (target == null)
                {
                    log.Warn("验收辅助：找不到 CardManager.DrawCards(int) —— 跳过发放。");
                    return false;
                }

                var harmony = new Harmony("modkit.verifygrant");
                harmony.Patch(target, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(VerificationGrant), nameof(Postfix))));

                _armed = true;
                log.Info("验收辅助已挂钩：CardManager.DrawCards(int) —— " +
                    (Enabled ? "**已启用**，进入第一场战斗发牌后会把 mod 内容发给玩家（会改变玩法）"
                             : "当前**未启用**（配置 GrantOnFirstBattle=false）。"));
                return true;
            }
            catch (Exception ex)
            {
                log.Warn("验收辅助挂钩失败：" + ex.GetType().Name + " - " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Postfix：<c>DrawCards</c> 是 async UniTask，postfix 跑在它**首次让出**之前，
        /// 那时发牌还没落地。所以这里只负责"起个协程，等两帧再发"。
        /// </summary>
        private static void Postfix(CardManager __instance)
        {
            try
            {
                if (!Enabled || _done || __instance == null) { return; }
                _done = true;

                // 协程宿主用游戏自己的 CardManager（MonoBehaviour）——
                // 避免"我的常驻对象被场景销毁"那类生命周期问题。
                __instance.StartCoroutine(GrantAfterDrawRoutine());
            }
            catch (Exception ex)
            {
                if (_log != null) { _log.Warn("验收辅助：启动发放协程失败 —— " + ex.Message); }
            }
        }

        private static IEnumerator GrantAfterDrawRoutine()
        {
            // 等两帧：一帧让 DrawCards 的异步体跑完，一帧让手牌布局落地。
            yield return null;
            yield return null;

            try { GrantAll(); }
            catch (Exception ex)
            {
                if (_log != null) { _log.Error("验收辅助：发放失败（不影响游戏继续）。", ex); }
            }
        }

        private static void GrantAll()
        {
            List<string> cards, relics, abilities;
            lock (Gate)
            {
                cards = new List<string>(CardNames);
                relics = new List<string>(RelicNames);
                abilities = new List<string>(AbilityNames);
            }

            int okCards = 0, okRelics = 0, okAbilities = 0;

            // ---------- 卡牌 → 手牌 ----------
            var cm = CardManager.Instance;
            if (cm != null)
            {
                foreach (var name in cards)
                {
                    try
                    {
                        var so = CardDataManager.Instance.GetCardSoByName(name);
                        if (so == null) { _log.Warn("验收辅助：取不到卡 " + name); continue; }

                        // 每张卡发几张（配置 GrantCardCopies）。
                        // ★ 仍然只调 GenerateCardObjFromCardInfo —— 见下面那段注释，不能顺手加 AddCardToPlayerCards。
                        int copies = CardCopies < 1 ? 1 : CardCopies;
                        for (int copy = 0; copy < copies; copy++)
                        {
                            var cb = cm.GenerateCardObjFromCardInfo(so.CardInfo, OwnerShip.Player);
                            if (cb == null) { _log.Warn("验收辅助：卡对象生成为 null " + name); break; }
                            okCards++;
                        }

                        // ★ 只调 GenerateCardObjFromCardInfo，**绝不再调 AddCardToPlayerCards**（2026-10-11 实机证伪后改正）
                        //   反编译 CardManager：
                        //     GenerateCardObjFromCardInfo(...) 内部 already:
                        //         allPlayerCards.Add(cardBase);
                        //         ChangeCardPlace(cardBase, CardPlace.Library);   // ← 已经进牌库了
                        //     AddCardToPlayerCards(...) 内部又:
                        //         allPlayerCards.Add(card);                        // ← 第二次登记！
                        //   上一版两个都调 → **每张卡被登记两遍**，实机症状是"多打一场后牌库被污染、
                        //   整手只能抽到同一张卡"。用户确认关掉辅助后恢复正常，缺陷已定位。
                        //   现在只调第一个：卡进**牌库**，会被正常抽到 —— 既不重复登记，也正好是"发牌"。
                    }
                    catch (Exception ex)
                    {
                        _log.Warn("验收辅助：发卡 " + name + " 失败 —— " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
            else { _log.Warn("验收辅助：CardManager.Instance 为 null，跳过发卡。"); }

            // ---------- 遗物 → 玩家 ----------
            var rm = RelicManager.Instance;
            if (rm != null)
            {
                foreach (var name in relics)
                {
                    try
                    {
                        // GenerateAndGainNewRelic 是游戏自己的一站式入口：
                        // 生成 holder → 定位 → TryAddRelicToPlayer（RelicManager.cs:118）
                        var so = DataManager.Instance.GetRelicDataManager().GetRelicSo(name);
                        if (so == null) { _log.Warn("验收辅助：取不到遗物 " + name); continue; }

                        rm.GenerateAndGainNewRelic(so.relicInfo);
                        okRelics++;
                    }
                    catch (Exception ex)
                    {
                        _log.Warn("验收辅助：发遗物 " + name + " 失败 —— " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
            else { _log.Warn("验收辅助：RelicManager.Instance 为 null，跳过发遗物。"); }

            // ---------- 能力 → 玩家 ----------
            var cp = CharacterPlayer.Instance;
            if (cp != null)
            {
                foreach (var name in abilities)
                {
                    try
                    {
                        var so = DataManager.Instance.GetAbilityDataManager().GetAbilitySo(name);
                        if (so == null) { _log.Warn("验收辅助：取不到能力 " + name); continue; }

                        var ab = AbilityUtil.GenerateAbility(so.abilityInfo, 1,
                            cp.transform.position, cp.transform, cp);
                        if (ab == null) { _log.Warn("验收辅助：能力对象生成为 null " + name); continue; }

                        okAbilities++;
                    }
                    catch (Exception ex)
                    {
                        _log.Warn("验收辅助：发能力 " + name + " 失败 —— " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
            else { _log.Warn("验收辅助：CharacterPlayer.Instance 为 null，跳过发能力。"); }

            _log.Info("【验收辅助】已在首场战斗发牌后发放 mod 内容：" +
                "卡 " + okCards + "/" + cards.Count + "（每张 ×" + (CardCopies < 1 ? 1 : CardCopies) + "）" +
                "、遗物 " + okRelics + "/" + relics.Count +
                "、能力 " + okAbilities + "/" + abilities.Count +
                "（GrantOnFirstBattle=true，会改变玩法，仅用于验收）。");

            // 解析计数快照：注册表只被 4 个游戏工厂调用。把"启动后 / 走到首场战斗"两个
            // 时刻的计数一比，就能判断菜单里悬停卡面这类操作是否真的在解析类型
            // （2026-10-15 排查"鼠标移到卡上卡一下"时的观测点）。
            _log.Info("【解析统计】注册表：调用 " + ModKit.Game.ModKitBridge.ResolveCalls +
                "｜命中 " + ModKit.Game.ModKitBridge.ResolveHits +
                "｜落空 " + ModKit.Game.ModKitBridge.ResolveMisses);
        }
    }
}
