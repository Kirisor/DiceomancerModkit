// =============================================================================
//  ModKit.Api · EffectContext —— mod 作者能碰到的全部游戏世界
//
//  设计原则 P1「概念暴露，不暴露类型」在这里落地：
//  作者看到的只有 ICombatant / ICardRef / ManaColor / DamageMode 这些平台概念，
//  **看不到** Character / CardBase / CardSo / EffectInfo / ColorEArray。
//
//  平台在适配层（ModKit.Runtime）把这里的每一次调用翻译成游戏调用，
//  并负责三件作者不需要知道的事：
//    ① 时序：游戏中 Character 层是同步的，CardManager/Command 层是异步入队的 —— 平台统一；
//    ② 幂等：游戏 SubCommand 的构造函数即入队（有副作用），平台为每个效果实例提供单次守卫；
//    ③ 隔离：效果体抛异常不拖垮游戏，平台记 Error 后降级为原版行为。
// =============================================================================

using System;
using System.Collections.Generic;

namespace ModKit.Api
{
    /// <summary>战斗单位的最小只读视图（快照语义，不持有游戏对象引用）。</summary>
    public interface ICombatant
    {
        string Name { get; }
        int Health { get; }
        int MaxHealth { get; }
        bool IsDead { get; }

        /// <summary>读取特殊状态层数（如 <c>"Block"</c>、<c>"Injure"</c>）。</summary>
        int GetState(string stateName);

        /// <summary>读取该单位当前持有状态的名字集合。</summary>
        IReadOnlyList<string> GetStateNames();
    }

    /// <summary>卡牌的只读引用（作者用它指认"哪张牌"，不需要知道 CardBase）。</summary>
    public interface ICardRef
    {
        /// <summary>卡牌标识（平台命名空间下的 id）。</summary>
        string Id { get; }

        /// <summary>当前语言下的显示名。</summary>
        string Name { get; }

        /// <summary>是否已升级。</summary>
        bool IsUpgraded { get; }
    }

    /// <summary>触发事件的只读视图。</summary>
    /// <remarks>
    /// <para>
    /// 平台只暴露这 4 个字段，而不是游戏的 <c>TriggerBase</c> 全字段——后者是数据袋，
    /// 暴露它等于把游戏内部结构写进契约。
    /// </para>
    /// </remarks>
    public sealed class TriggerEvent
    {
        public TriggerEvent(string triggerName, string sourceName, string extraString, int extraInt)
        {
            TriggerName = triggerName;
            SourceName = sourceName;
            ExtraString = extraString;
            ExtraInt = extraInt;
        }

        /// <summary>触发点名（取自"游戏实际发出的名字"集合，不是枚举）。</summary>
        public string TriggerName { get; private set; }

        /// <summary>触发来源的显示名。</summary>
        public string SourceName { get; private set; }

        /// <summary>附带字符串（部分触发点才有，可能为 null）。</summary>
        public string ExtraString { get; private set; }

        /// <summary>附带整数（部分触发点才有）。</summary>
        public int ExtraInt { get; private set; }
    }

    /// <summary>
    /// 效果执行上下文。作者在这里读写游戏世界。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>读</b>是快照语义：不会因为你在效果体里改了世界而自动刷新，需要时重新读。
    /// <b>写</b>由平台翻译成游戏调用，并保证一个效果实例内的重复调用不会重复入队。
    /// </para>
    /// </remarks>
    public abstract class EffectContext
    {
        // ---------------------------------------------------------------------
        // 读
        // ---------------------------------------------------------------------

        /// <summary>效果发起者（打出这张牌 / 持有这个遗物的角色）。</summary>
        public abstract ICombatant Self { get; }

        /// <summary>效果目标。<see cref="CombatantSelector.Opponent"/> 等选择器的解析结果。</summary>
        public abstract ICombatant Opponent { get; }

        /// <summary>本次触发的选择器解析出的全部目标（自己或对方或全体）。</summary>
        public abstract IReadOnlyList<ICombatant> Targets { get; }

        /// <summary>发起者的显示名。</summary>
        public abstract string OwnerName { get; }

        /// <summary>第 1 个数字槽的取值（骰子数已按"同一命令内一致"的口径坍缩）。</summary>
        public abstract int Amount { get; }

        /// <summary>第 index 个数字槽的取值（0 基）。越界返回 0。</summary>
        public abstract int AmountAt(int index);

        public abstract int HandCount { get; }
        public abstract int LibraryCount { get; }
        public abstract int GraveCount { get; }

        /// <summary>手牌快照。</summary>
        public abstract IReadOnlyList<ICardRef> HandCards { get; }

        /// <summary>当前法力值。</summary>
        public abstract int Mana { get; }

        /// <summary>当前法力上限。</summary>
        public abstract int MaxMana { get; }

        /// <summary>
        /// 平台注入的随机源。
        /// </summary>
        /// <remarks>
        /// 不要直接用 <c>UnityEngine.Random</c>：平台为了可复现的验收（同一存档、同一输入 → 同一结果）
        /// 统一提供随机源。用平台这个引用才能保证你的 mod 能被回归测试复现。
        /// </remarks>
        public abstract Random Random { get; }

        /// <summary>写平台日志（会自动带 mod 前缀）。</summary>
        public abstract void Log(string message);

        // ---------------------------------------------------------------------
        // 写 —— 注意：平台保证"同一效果实例内重复调用不会重复入队"
        // ---------------------------------------------------------------------

        /// <summary>造成伤害。<paramref name="mode"/> 必须显式给——直接/间接会改变玩法强度。</summary>
        public abstract void DealDamage(int amount, DamageMode mode, CombatantSelector target = CombatantSelector.Opponent);

        /// <summary>加格挡。</summary>
        public abstract void Block(int amount, CombatantSelector target = CombatantSelector.Self);

        /// <summary>治疗。</summary>
        public abstract void Heal(int amount, CombatantSelector target = CombatantSelector.Self);

        /// <summary>直接失去生命（无视格挡）。</summary>
        public abstract void LoseLife(int amount, CombatantSelector target = CombatantSelector.Opponent);

        /// <summary>抽牌。平台统一"请求抽 N 张，完成后继续"的语义。</summary>
        public abstract void Draw(int amount);

        /// <summary>随机弃牌。</summary>
        public abstract void DiscardRandom(int amount);

        /// <summary>获得法力。<b>颜色必须是平台探测确认可用的单色。</b></summary>
        public abstract void GainMana(ManaColor color, int amount);

        /// <summary>设置法力上限（游戏内部会 Clamp 到规则上限）。</summary>
        public abstract void SetMaxMana(int amount);

        /// <summary>清空当前法力。</summary>
        public abstract void CleanMana();

        /// <summary>加特殊状态层数（数值型状态只有 <c>"Block"</c> 与 <c>"Injure"</c> 有内建效果体）。</summary>
        public abstract void AddState(string stateName, int amount, CombatantSelector target = CombatantSelector.Self);

        /// <summary>减特殊状态层数。</summary>
        public abstract void ReduceState(string stateName, int amount, CombatantSelector target = CombatantSelector.Self);

        /// <summary>给 buff / debuff / 技能。</summary>
        public abstract void GiveAbility(string abilityName, int level, CombatantSelector target = CombatantSelector.Self);

        /// <summary>生成卡牌到指定去向。</summary>
        public abstract void GiveCard(string cardId, CardDestination destination, int amount = 1);

        /// <summary>生成"临时牌"（游戏会自动挂 Temporary 关键词）。</summary>
        public abstract void GiveTemporaryCard(string cardId, CardDestination destination, int amount = 1);

        /// <summary>升级一张牌。</summary>
        public abstract void UpgradeCard(ICardRef card);

        /// <summary>结束当前回合。</summary>
        public abstract void EndTurn();

        /// <summary>
        /// 订阅触发点。
        /// </summary>
        /// <remarks>
        /// <paramref name="triggerName"/> 必须取自平台内置的**实际发出**触发点表
        /// （不是游戏的 <c>AllTriggerType</c> 枚举——探针实测两者不一致）。
        /// 写了表里没有的名字，平台在加载期报错并给出近似候选。
        /// </remarks>
        public abstract void SubscribeTrigger(string triggerName, Action<TriggerEvent> handler);
    }
}
