using System;
using ModKit.Api;

namespace ManualDemo
{
    /// <summary>蛙毒牙（基础形态）：若本卡由「奶蛙」结算，对目标敌人施加 Amount 层流血。</summary>
    [ModEffect("mf_bleed_if_cascade")]
    public sealed class MfBleedIfCascade : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            if (!CardKeywordMilkFrog.IsCascadeResolve) { return; }
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
    /// 蛙群瞪视：对所有敌人施加 Amount 层易伤；若本卡由「奶蛙」结算，再对所有敌人造成 Amount/2 点伤害。
    /// </summary>
    /// <remarks>
    /// 自定义效果只有 1 个数字槽，所以"易伤层数"与"群伤"共享同一个数字（群伤 = 层数/2）。
    /// 基础 8 → 4 伤；升级 16 → 8 伤（原设计想给 6，差 2 点，属于可接受的近似）。
    /// </remarks>
    [ModEffect("mf_glare")]
    public sealed class MfGlare : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            int layers = Math.Max(1, ctx.Amount);
            ctx.GiveAbility("Vulnerable", layers, CombatantSelector.AllOpponents);

            if (CardKeywordMilkFrog.IsCascadeResolve)
            {
                int dmg = ctx.AmountAt(1);           // 第二个数字 = 被奶蛙时的群伤
                if (dmg > 0) { ctx.DealDamage(dmg, DamageMode.Direct, CombatantSelector.AllOpponents); }
            }
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
            CardKeywordMilkFrog.SelectionMode = mode;
            ctx.Log("蛙母的号令：奶蛙拉取改为「" + (mode >= 2 ? "任意卡" : "奶蛙卡") + "」（玩家选择）。");
        }
    }

    /// <summary>蛙母的号令（升级）：可以选任意卡。</summary>
    [ModEffect("mf_mother_law_full")]
    public sealed class MfMotherLawFull : SyncCardEffect
    {
        public override void Apply(EffectContext ctx) { MfMotherLaw.ApplyMode(ctx, 2); }
    }
}
