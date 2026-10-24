using System;
using ModKit.Api;

namespace ManualDemo
{
    /// <summary>胞兄之恨（基础形态）：被奶蛙结算时，对目标敌人施加 Amount 层流血。</summary>
    /// <remarks>整个效果只在「被奶蛙结算时」这个触发点生效 —— 继承 <see cref="CascadeTriggeredEffect"/> 即可。</remarks>
    [ModEffect("mf_bleed_if_cascade")]
    public sealed class MfBleedIfCascade : CascadeTriggeredEffect
    {
        public override void ApplyWhenCascading(EffectContext ctx)
        {
            ctx.GiveAbility("Bleed", Math.Max(1, ctx.Amount), CombatantSelector.Opponent);
        }
    }

    /// <summary>蛙毒牙（升级形态）：无条件对目标敌人施加 Amount 层流血。</summary>
    [ModEffect("mf_bleed_always")]
    public sealed class MfBleedAlways : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            ctx.GiveAbility("Bleed", Math.Max(1, ctx.Amount), CombatantSelector.Opponent);
        }
    }

    /// <summary>
    /// 蛙群瞪视（连锁部分）：被奶蛙结算时，对所有敌人施加 Amount 层易伤。
    /// </summary>
    /// <remarks>
    /// 卡面上「对所有敌人造成 X 点伤害」那一条走**原版效果**
    /// （<c>builtin: deal_damage</c> + <c>target: AllOpponents</c>，卡面文案由游戏的
    /// <c>Key_DealDamageAllOpponents</c> 提供），这里只负责「被奶蛙结算时」那一条。
    /// </remarks>
    [ModEffect("mf_glare_cascade")]
    public sealed class MfGlareCascade : CascadeTriggeredEffect
    {
        // 诊断：实机反馈「易伤被重复施加」（一次连锁里刷了 8 条），先数清楚到底触发了几次。
        private static int _calls;

        public override void ApplyWhenCascading(EffectContext ctx)
        {
            _calls++;
            ctx.Log("蛙群瞪视·连锁易伤：第 " + _calls + " 次触发（本卡层数 " + ctx.Amount + "）。");
            ctx.GiveAbility("Vulnerable", Math.Max(1, ctx.Amount), CombatantSelector.AllOpponents);
        }
    }

    /// <summary>
    /// 蛙母的号令：把奶蛙的"自动拉取"改成"玩家选择"。
    /// Amount = 1 → 只能从奶蛙卡里选；Amount = 2 → 可以选任何卡（升级形态）。
    /// </summary>
    /// <remarks>
    /// 通过静态开关 <see cref="CardKeywordMilkFrog.SelectionMode"/> 生效，
    /// 奶蛙关键词在拉取阶段读它决定走"自动"还是"弹选择框"。
    /// </remarks>
    /// <summary>蛙母的号令（基础）：只能从奶蛙卡里选。</summary>
    [ModEffect("mf_mother_law")]
    public sealed class MfMotherLaw : SyncCardEffect
    {
        public override void Apply(EffectContext ctx) { ApplyMode(ctx, 1); }

        internal static void ApplyMode(EffectContext ctx, int mode)
        {
            // ★ 模式不再由这里设置：卡片挂的是能力（milkfrog_choice / _full），
            //   奶蛙关键词按能力判断。能力由游戏管理，战斗结束自动清。
            ctx.Log("蛙母的号令：本场战斗内奶蛙拉取改为「" + (mode >= 2 ? "任意卡" : "奶蛙卡") + "」（玩家选择）。");
        }
    }

    /// <summary>蛙母的号令（升级）：可以选任意卡。</summary>
    [ModEffect("mf_mother_law_full")]
    public sealed class MfMotherLawFull : SyncCardEffect
    {
        public override void Apply(EffectContext ctx) { MfMotherLaw.ApplyMode(ctx, 2); }
    }

    /// <summary>空效果：给"必须写 effects 但又不需要实际行为"的能力占位用。</summary>
    [ModEffect("mf_noop")]
    public sealed class MfNoOp : SyncCardEffect
    {
        public override void Apply(EffectContext ctx) { }
    }
}
