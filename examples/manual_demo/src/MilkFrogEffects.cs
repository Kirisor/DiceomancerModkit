// =============================================================================
//  奶蛙职业 · 专属效果（13 个）
// =============================================================================

using System;
using ModKit.Api;

namespace ManualDemo
{
    // ---------------------------------------------------------------- 联锁流

    /// <summary>群鸣回响：造成 Number 伤害；若手牌 ≥3 张，再造成 4 点。</summary>
    [ModEffect("mf_chorus_echo")]
    public sealed class MfChorusEcho : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            ctx.DealDamage(ctx.Amount, DamageMode.Direct);
            if (ctx.HandCount >= 3) { ctx.DealDamage(4, DamageMode.Direct); }
        }
    }

    /// <summary>群体牵引：抽 Number 张；若弃牌堆 ≥5 张，再抽 1 张。</summary>
    [ModEffect("mf_swarm_pull")]
    public sealed class MfSwarmPull : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            ctx.Draw(System.Math.Max(1, ctx.Amount));
            if (ctx.GraveCount >= 5) { ctx.Draw(1); }
        }
    }

    /// <summary>连锁终结：造成 Number + 弃牌堆/4（最多 +6）。</summary>
    [ModEffect("mf_chain_finish")]
    public sealed class MfChainFinish : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            int bonus = System.Math.Min(6, ctx.GraveCount / 4);
            ctx.DealDamage(ctx.Amount + bonus, DamageMode.Direct);
        }
    }

    /// <summary>蝌蚪涌动：把 Number 张「蝌蚪」临时卡加入手牌。</summary>
    [ModEffect("mf_brood_call")]
    public sealed class MfBroodCall : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            int n = System.Math.Max(1, ctx.Amount);
            for (int i = 0; i < n; i++) { ctx.GiveTemporaryCard("mf_tadpole_token", CardDestination.Hand); }
        }
    }

    // ---------------------------------------------------------------- 护壁流

    /// <summary>盾击：造成"当前格挡一半"的伤害（无格挡时按 Number），然后清空格挡。</summary>
    [ModEffect("mf_shield_bash")]
    public sealed class MfShieldBash : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            int block = ctx.Self == null ? 0 : ctx.Self.GetState("Block");
            int dmg = block / 2;
            if (dmg <= 0) { dmg = ctx.Amount; }
            ctx.DealDamage(dmg, DamageMode.Direct);
            if (block > 0) { ctx.ReduceState("Block", block); }
        }
    }

    /// <summary>壁祷：治疗"当前格挡三分之一"（至少 Number）。</summary>
    [ModEffect("mf_bulwark_pray")]
    public sealed class MfBulwarkPray : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            int block = ctx.Self == null ? 0 : ctx.Self.GetState("Block");
            int heal = block / 3;
            if (heal < ctx.Amount) { heal = ctx.Amount; }
            ctx.Heal(heal);
        }
    }

    /// <summary>最后壁垒：获得 Number 格挡；半血以下额外 +10 格挡（不治疗——治疗是稀缺资源）。</summary>
    [ModEffect("mf_last_bastion")]
    public sealed class MfLastBastion : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            ctx.Block(System.Math.Max(1, ctx.Amount));
            if (ctx.Self != null && ctx.Self.MaxHealth > 0 && ctx.Self.Health * 2 <= ctx.Self.MaxHealth)
            {
                ctx.Block(10);
            }
        }
    }

    /// <summary>蜕壳：清空全部格挡，抽 Number 张。</summary>
    [ModEffect("mf_molt_shell")]
    public sealed class MfMoltShell : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            if (ctx.Self != null)
            {
                int block = ctx.Self.GetState("Block");
                if (block > 0) { ctx.ReduceState("Block", block); }
            }
            ctx.Draw(System.Math.Max(1, ctx.Amount));
        }
    }

    // ---------------------------------------------------------------- 通用

    /// <summary>吞食：抽 Number 张，弃 1 张随机手牌，回 1 点紫能量。</summary>
    [ModEffect("mf_feast")]
    public sealed class MfFeast : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            ctx.Draw(System.Math.Max(1, ctx.Amount));
            ctx.DiscardRandom(1);
            ctx.GainMana(ManaColor.Purple, 1);
        }
    }

    /// <summary>暴食：随机弃 Number 张，每弃 1 张回 1 点黑能量。</summary>
    [ModEffect("mf_glut")]
    public sealed class MfGlut : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            int n = System.Math.Max(1, ctx.Amount);
            ctx.DiscardRandom(n);
            ctx.GainMana(ManaColor.Black, n);
        }
    }

    /// <summary>新生：治疗 Number；若手牌 ≤2，改为抽 2 张。</summary>
    [ModEffect("mf_renew")]
    public sealed class MfRenew : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            if (ctx.HandCount <= 2) { ctx.Draw(2); }
            else { ctx.Heal(System.Math.Max(1, ctx.Amount)); }
        }
    }

    /// <summary>虚空什一税：失去 Number 生命，获得 8 格挡与 2 点紫能量。</summary>
    [ModEffect("mf_void_tithe")]
    public sealed class MfVoidTithe : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            ctx.LoseLife(System.Math.Max(1, ctx.Amount), CombatantSelector.Self);
            ctx.Block(8);
            ctx.GainMana(ManaColor.Purple, 2);
        }
    }

    /// <summary>蛙王号令：造成 Number 伤害（每张手牌 +2），并给对手挂 2 层流血。</summary>
    [ModEffect("mf_king_decree")]
    public sealed class MfKingDecree : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            ctx.GiveAbility("Bleed", 2+ ctx.HandCount * 2, CombatantSelector.Opponent);
            ctx.DealDamage(ctx.Amount + ctx.HandCount * 2, DamageMode.Direct,CombatantSelector.Opponent);
        }
    }

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

    /// <summary>鼓气长鸣：获得 Amount 层「临时力量」；若本卡由奶蛙结算，再获得 Amount1 层「力量」。</summary>
    [ModEffect("mf_bellow")]
    public sealed class MfBellow : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            ctx.GiveAbility("Excitement", Math.Max(1, ctx.Amount));

            if (CardKeywordMilkFrog.IsCascadeResolve)
            {
                int power = ctx.AmountAt(1);          // 第二个数字 = 被奶蛙时的力量层数
                if (power > 0) { ctx.GiveAbility("Power", power); }
            }
        }
    }
}
