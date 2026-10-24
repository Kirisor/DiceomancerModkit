// =============================================================================
//  「奶蛙」职业声明（mod 侧）
//
//  职业 = 一块**三色护符**。原版六职业都由「原版已做成球的 5 色」组成，
//  而平台的 CareerPatch 把原版**留着没启用**的第 6 色（ColorE.Black）放成了新球，
//  所以这个职业用 紫 + 绿 + 黑 组合 —— 黑球是它的标志。
//
//  卡与遗物仍然写在 JSON 里（cards/ relics/），这里只按 id 引用它们。
// =============================================================================

using System.Collections.Generic;
using ModKit.Api;

public sealed class MilkFrogClass : IModClassProvider
{
    public ClassSpec ProvideClass()
    {
        return new ClassSpec
        {
            Id = "milk_frog",
            NameZh = "奶蛙",
            IntroZh = "蛙群互相呼唤：每当你结算一张带「奶蛙」的卡，它就从牌库与弃牌堆里再拉出同伴免费结算 —— 连锁越长，收获越多。",

            // 恰好 3 个颜色；至少一个必须是原版没做成球的（黑），否则会与原版职业撞车
            Colors = new List<ManaColor> { ManaColor.Purple, ManaColor.Green, ManaColor.Black },

            // 本职业占用的"新球"：颜色 + 贴图（mod 目录内的相对路径）+ 显示大小。
            // ★ 这些都是**职业自己的实现**，放在 mod 里；平台不知道"奶蛙"任何事。
            OrbColor = ManaColor.Black,
            OrbSprite = "art/naifrogball.png",
            BallScale = 0.5f,

            // 卡 id 写 mod 内的短名即可（平台会归一化成 naifrog.<id>）；重复出现 = 多份
            StartDeck = new List<string>
            {
                // 4 种初始卡 × 数量，共 10 张
                "milkfrogstrike", "milkfrogstrike", "milkfrogstrike", "milkfrogstrike",    // 奶蛙打击
                "milkfrogblock", "milkfrogblock", "milkfrogblock",  "milkfrogblock", // 奶蛙格挡
                "milkfroglaugh", // 奶蛙大笑
                "Dice"           // 至尊骰（原版卡）
            },

            // ★ 至少 2 件：职业介绍卡硬取 relicSos[1] 做护符预览，少于 2 件会在开局界面抛异常
            StartRelics = new List<string> { "TheOneDice", "milk_frog_talisman" },

            // ★ 战役卡池：决定玩家整局能不能变强。只写初始牌组的话，玩家永远抽不到新卡，
            //   强度曲线是平的（第一层靠初始牌组还能打，第二层敌人一涨就崩）。
            //   这里把初始牌组也一并列进来，其余是连锁主题的卡。
            //   ⚠️ 必须与 mod.json 的 cards 清单一致：引用了不存在的卡会在装配期报错。
            PoolCards = new List<string>
            {
                // ★ 每个 id 只能出现一次，且必须是 mod.json cards 清单里真实存在的「卡 id」
                //   （写文件名、或把效果名写进来，都会报「卡池引用了不存在的卡」，该条被静默跳过）
                //   衍生卡不列：mf_essence_* 是 mf_bloodkiss / mf_bloodspawn 打出的精粹
                "mf_swarm_surge",
                "bigstomach",
                "mf_bloodkiss", "mf_bloodspawn", "mf_doomcroak",
                "mf_glare", "mf_mire_swell", "mf_mother_law", "mf_mudpuppet",
                "mf_pond_call", "mf_venom_fang", "milkfrogangre", "milkfroglaugh",
                "naifrogseek", "thonasflare", "nainaiphus",
                // 「奶家人」族（2026-10-24）：奶兔 / 奶狗 / 奶家人 / 奶king / 奶之刻 / 奶蛋？
                "mf_milk_rabbit", "mf_milk_dog", "mf_milk_family",
                "mf_milk_king", "mf_milk_clock", "mf_naidan_question"
            }
        };
    }
}
