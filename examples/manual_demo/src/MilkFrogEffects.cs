// =============================================================================
//  奶蛙职业 · 专属效果（12 个）
//
//  设计基准（来自原版德鲁伊 176 张卡的实测）：
//    · 稀有度的真正来源是"独有效果/能力"（Normal 38% / Uncommon 45% / Rare 71%）；
//    · 强度刹车靠关键词（耗竭 Exert / 限制 Limited / 易逝 Perishable / 一次性 Fragile），
//      德鲁伊 70–80% 的卡都挂着一个；
//    · 一张卡通常只有 1.10 个效果 ⇒ 这里刻意"一个效果做一件事"，不再堆叠动词。
//
//  两个流派：
//    联锁（Chain）  —— 用弃牌堆/手牌数当燃料，越打越强，但必须靠"耗竭/限制"封顶；
//    护壁（Bulwark）—— 把格挡当资源，读出来、换成伤害/治疗/手牌，代价是清空格挡或自伤。
// =============================================================================

using ModKit.Api;

namespace ManualDemo
{
    // ---------------------------------------------------------------- 联锁流

    /// <summary>群鸣回响：造成 Amount 伤害；若手牌 ≥ AmountAt(1) 张，再造成 AmountAt(2) 伤害。</summary>
    [ModEffect("mf_chorus_echo")]
    public sealed class MfChorusEcho : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            ctx.DealDamage(ctx.Amount, DamageMode.Direct);
            int need = System.Math.Max(2, ctx.AmountAt(1));
            if (ctx.HandCount >= need)
            {
                ctx.DealDamage(ctx.AmountAt(2), DamageMode.Direct);
            }
        }
    }

    /// <summary>群体牵引：抽 Amount 张；若弃牌堆 ≥ AmountAt(1)，再抽 1 张。</summary>
    [ModEffect("mf_swarm_pull")]
    public sealed class MfSwarmPull : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            ctx.Draw(System.Math.Max(1, ctx.Amount));
            if (ctx.GraveCount >= System.Math.Max(3, ctx.AmountAt(1)))
            {
                ctx.Draw(1);
            }
        }
    }

    /// <summary>连锁终结：造成 Amount + 弃牌堆/4（最多 +6）。</summary>
    [ModEffect("mf_chain_finish")]
    public sealed class MfChainFinish : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            int bonus = System.Math.Min(6, ctx.GraveCount / 4);
            ctx.DealDamage(ctx.Amount + bonus, DamageMode.Direct);
        }
    }

    /// <summary>蝌蚪涌动：把 Amount 张「蝌蚪」临时卡加入手牌。</summary>
    [ModEffect("mf_brood_call")]
    public sealed class MfBroodCall : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            int n = System.Math.Max(1, ctx.Amount);
            for (int i = 0; i < n; i++)
            {
                ctx.GiveTemporaryCard("mf_tadpole_token", CardDestination.Hand);
            }
        }
    }

    // ---------------------------------------------------------------- 护壁流

    /// <summary>盾击：造成等于"当前格挡一半"的伤害，然后清空格挡。</summary>
    [ModEffect("mf_shield_bash")]
    public sealed class MfShieldBash : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            int block = ctx.Self == null ? 0 : ctx.Self.GetState("Block");
            int dmg = block / 2;
            if (dmg <= 0)
            {
                ctx.Log("盾击：当前没有格挡，只结算基础伤害。");
                dmg = ctx.Amount;          // 没格挡也给个底线，避免空牌
            }
            ctx.DealDamage(dmg, DamageMode.Direct);
            if (block > 0) { ctx.ReduceState("Block", block); }
        }
    }

    /// <summary>壁祷：治疗等于"当前格挡的三分之一"（不足则按 Amount）。</summary>
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

    /// <summary>最后壁垒：生命 ≤ 50% 时，额外获得 AmountAt(1) 格挡与 AmountAt(2) 治疗。</summary>
    [ModEffect("mf_last_bastion")]
    public sealed class MfLastBastion : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            int block = System.Math.Max(1, ctx.Amount);
            ctx.Block(block);
            if (ctx.Self != null && ctx.Self.MaxHealth > 0 && ctx.Self.Health * 2 <= ctx.Self.MaxHealth)
            {
                ctx.Block(ctx.AmountAt(1));
                ctx.Heal(ctx.AmountAt(2));
            }
        }
    }

    /// <summary>蜕壳：清空全部格挡，抽 Amount 张。</summary>
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

    /// <summary>吞食：抽 Amount 张，弃 AmountAt(1) 张，回 1 点紫能量。</summary>
    [ModEffect("mf_feast")]
    public sealed class MfFeast : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            ctx.Draw(System.Math.Max(1, ctx.Amount));
            int discard = ctx.AmountAt(1);
            if (discard > 0) { ctx.DiscardRandom(discard); }
            ctx.GainMana(ManaColor.Purple, 1);
        }
    }

    /// <summary>暴食：随机弃 Amount 张，每弃 1 张回 1 点黑能量。</summary>
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

    /// <summary>新生：治疗 Amount；若手牌 ≤ 2，改为抽 2 张。</summary>
    [ModEffect("mf_renew")]
    public sealed class MfRenew : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            if (ctx.HandCount <= 2) { ctx.Draw(2); }
            else { ctx.Heal(System.Math.Max(1, ctx.Amount)); }
        }
    }

    /// <summary>虚空什一税：失去 Amount 生命，获得 AmountAt(1) 格挡与 AmountAt(2) 点紫能量。</summary>
    [ModEffect("mf_void_tithe")]
    public sealed class MfVoidTithe : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            ctx.LoseLife(System.Math.Max(1, ctx.Amount), CombatantSelector.Self);
            ctx.Block(ctx.AmountAt(1));
            ctx.GainMana(ManaColor.Purple, System.Math.Max(1, ctx.AmountAt(2)));
        }
    }

    /// <summary>蛙王号令：给对手挂「流血」能力，并按手牌数造成伤害（每 2 张 +1）。</summary>
    [ModEffect("mf_king_decree")]
    public sealed class MfKingDecree : SyncCardEffect
    {
        public override void Apply(EffectContext ctx)
        {
            // ★ 负伤(Injure) 是玩家专属状态，敌人用的是「流血」(Bleed) 能力。
            ctx.GiveAbility("Bleed", System.Math.Max(1, ctx.AmountAt(1)), CombatantSelector.Opponent);
            ctx.DealDamage(ctx.Amount + ctx.HandCount / 2, DamageMode.Direct);
        }
    }
}
