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
                "mf_milk_king", "mf_milk_clock", "mf_naidan_question",

                // ---- 原版卡（2026-10-30 点名纳入职业池）----
                //   ★ 这里写的是**原版卡名**（游戏的 CardSo.cardName），不是 mod 内 id：
                //     平台的解析顺序与初始牌组一致 —— 先按 "<modId>.<id>" 找本 mod 的卡，
                //     找不到再当原版卡名去游戏数据里查（CareerAssembler.ResolveDeckCard）。
                //   ★ 选卡规则：卡面流派色 ⊆ {紫, 绿, 黑}（与职业三色一致），紫 / 绿紫 / 黑 / 紫黑 / 绿黑都有。
                //   ★★ **不要写已经在通用集合里的卡**（那会让它在战役池里计两次、掉率翻倍）。
                //     奶蛙三色自动吃进的原版集合只有三个（运行期实测 capabilities.json）：
                //       Standard_Purple(7)       = 变戏法/冲击波/骰化术/平衡调整/万象无常牌/坍塌/完全错乱术
                //       Standard_Green(3)        = 水元素/扭曲元素/有丝分裂
                //       Standard_PurpleGreen(3)  = 演化/混乱转变/莲花
                //     这 13 张一律不列（它们本来就在池子里）；六个 Standard_Class_* 职业集合
                //     的真实色组里没有一个 ⊆ {紫,绿,黑}（PGB 其实是 紫+绿+蓝），所以职业池也不会混进别人的牌。
                //   ★★ 黑系（[疫病 Malady] 卡）正常**不在战役池**里 —— 只由事件或其它卡生成。
                //     作为 mod 只保留 3 张最贴黑球主题的（精神污染 / 黑莲花 / 黑洞）；
                //     紫印记 / 绿印记 / 千术 是纯事件卡（剧情奖励），已剔除；
                //     乌黑触手 / 灾厄 / 折磨 / 幻象 / 究极生物 也是衍生卡，一并剔除。

                // ---- 第一批：非通用集合，只能靠点名进池 ----
                "Monolith",            // 磐石：格挡（原属 PGV 职业池，不会被自动带进来）
                "Catalyst",            // 催化剂：数字翻倍 / 叠加态
                "QuillSpray",          // 针刺扫射：伤害全体敌人
                "Mutation",            // 变异：多次随机增益
                "Ascend",              // 飞升：堆叠增益

                // ---- 第二批：骰子 / 数值改写 ----
                "DiceBonus",           // 幸运骰子：获得 DiceBonus 能力
                "DiceTakeLuck",        // 运气窃取：抢敌人的骰运
                "Nerf",                // 削弱：数值增减
                "Trick",               // 恶作剧：把数字改成骰子数
                "Superposition",       // 叠加态：主能力（多色槽，含紫可付）
                "WhatIsThis",          // 这是啥？：探寻
                // ---- 第二批：回复 / 召唤 / 演化 ----
                "Unyielding",          // 不屈：回复
                "NasalGoo",            // 黏鼻涕：虚弱 + 无力
                "TheHangedMan",        // 倒吊人：闪避
                "GlowBerries",         // 发光果：治疗 + 随机增益
                "Swarm",               // 虫群：多段伤害
                "BiggerIsBetter",      // 多就是好 大就是棒：全体召唤物加数
                "Life",                // 生命：洗回牌库 + 检索
                "Apocalypse",          // 末日：大数字→数值
                // ---- 第二批：黑系保留（疫病卡，正常只能由事件/其它卡生成）----
                "MentalDisturbance",   // 精神污染：虚弱 + 易伤
                "BlackLotus",          // 黑莲花
                "BlackHole"            // 黑洞：全体多段 + 无力
            }
        };
    }
}
