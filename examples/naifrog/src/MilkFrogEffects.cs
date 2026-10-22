// =============================================================================
//  奶蛙职业 · 专属效果（13 个）
// =============================================================================

using System;
using ModKit.Api;

namespace ManualDemo
{

    /// <summary>腐泥翻涌：造成 (弃牌堆张数 × Amount) 点伤害。</summary>
    /// <remarks>奶蛙把"耗竭"当资源：打得越快，弃牌堆越厚，这张越强。</remarks>
    [ModEffect("mf_grave_swell")]
    public sealed class MfGraveSwell : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            int multiplier = Math.Max(1, ctx.Amount);
            int dmg = ctx.GraveCount * multiplier;
            if (dmg <= 0) { ctx.Log("腐泥翻涌：弃牌堆为空，没有造成伤害。"); return; }
            ctx.DealDamage(dmg, DamageMode.Direct);
        }
    }

    /// <summary>鼓气长鸣：获得 Amount 层「临时力量」；被奶蛙结算时，再获得 Amount1 层「力量」。</summary>
    /// <remarks>
    /// 这个效果的「被奶蛙结算时」只占后半段（要拆成两条效果就得改卡面数值与文案），
    /// 所以直接读触发点状态 <see cref="MilkFrogCascade.IsResolving"/> 分流；
    /// 整个效果都只在触发点生效时，请改用基类 <see cref="CascadeTriggeredEffect"/>。
    /// </remarks>
    [ModEffect("mf_bellow")]
    public sealed class MfBellow : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            ctx.GiveAbility("Excitement", Math.Max(1, ctx.Amount));

            if (MilkFrogCascade.IsResolving)
            {
                int power = ctx.AmountAt(1);          // 第二个数字 = 被奶蛙时的力量层数
                if (power > 0) { ctx.GiveAbility("Power", power); }
            }
        }
    }
}
