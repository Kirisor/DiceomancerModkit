// =============================================================================
//  ModKit.Core · 内容校验器（分片）—— 逐语义必填字段
//
//  由 ContentValidator.cs 按子域机械拆分而来（2026-10-13），只做剪切 + 粘贴：
//    · CheckSemanticFields   —— 按 BuiltinEffect 逐个语义检查必填字段
//    · HasNonEmptyArray / ValidateRequireColors
//        —— 上述检查里 require/pool 用到的两个小工具
//  效果列表本身的校验见 ContentValidator.Effects.cs。
// =============================================================================

using ModKit.Api;
using Newtonsoft.Json.Linq;

namespace ModKit.Core
{
    public static partial class ContentValidator
    {
        private static void CheckSemanticFields(JObject obj, string path,
            BuiltinEffect effect, ValidationReport report, string id)
        {
            switch (effect)
            {
                case BuiltinEffect.DealDamage:
                case BuiltinEffect.LoseLife:
                    // ★ 目标必须显式给。
                    //   装配层的缺省是 Self（ContentAssembler.SelectorToGame 的 fallback），
                    //   于是一张"造成 6 点伤害"的卡会**打自己** —— 这是个静默的、
                    //   方向完全反过来的错误，实机踩中过（弧刃）。和 mode 同理：
                    //   平台不做这个猜测，因为猜错的代价是玩法与预期相反。
                    //   （其他语义缺省 Self 是合理的：格挡/治疗/抽牌本来就对自己。）
                    if (string.IsNullOrEmpty(ReadString(obj, "target")))
                    {
                        report.Error(id, path + ".target",
                            "伤害类效果必须显式指定 target。合法值：" +
                            string.Join(" / ", ToArray(CombatantSelectorNames.AllNames)) + "。" +
                            "打敌人写 \"Opponent\"。平台不给默认值 —— " +
                            "装配层的兜底是 Self，漏写会让这张卡**打自己**，且玩法上完全看不出来是配置错误。");
                    }

                    // 伤害模式必须显式给——Direct/Indirect 会改变玩法强度，平台不给默认值
                    if (string.IsNullOrEmpty(ReadString(obj, "mode")))
                    {
                        report.Error(id, path + ".mode",
                            "伤害类效果必须显式指定 mode（" +
                            string.Join(" / ", ToArray(DamageModeNames.AllNames)) + "）。" +
                            "平台不提供默认值：Direct 走命中判定与伤害调整器，Indirect 跳过——" +
                            "选错会静默改变玩法强度。");
                    }
                    else
                    {
                        var m = DamageModeNames.Resolve(ReadString(obj, "mode"));
                        if (!m.Mapped)
                        {
                            report.Error(id, path + ".mode", "未知伤害模式 '" + ReadString(obj, "mode") + "'。" +
                                string.Join(" / ", ToArray(DamageModeNames.AllNames)) + "。" + (m.Hint ?? ""));
                        }
                    }
                    break;

                case BuiltinEffect.GainMana:
                    if (string.IsNullOrEmpty(ReadString(obj, "color")))
                    {
                        report.Error(id, path + ".color",
                            "gain_mana 必须指定 color（单色名）。多色组合会抛 KeyNotFoundException。");
                    }
                    else
                    {
                        var c = ManaColorNames.Resolve(ReadString(obj, "color"));
                        if (!c.Mapped)
                        {
                            report.Error(id, path + ".color", "未知颜色名。合法值：" +
                                string.Join(" / ", ToArray(ManaColorNames.AllNames)) + "。" + (c.Hint ?? ""));
                        }
                    }
                    break;

                case BuiltinEffect.ApplyState:
                    if (string.IsNullOrEmpty(ReadString(obj, "name")))
                    {
                        report.Error(id, path + ".name",
                            "apply_state 必须指定 name（状态名）。注意：游戏里只有 \"Block\" 与 \"Injure\"" +
                            "有内建效果体，其他名字仍可存层数但不会有实质效果。");
                    }
                    break;

                case BuiltinEffect.GainAbility:
                    if (string.IsNullOrEmpty(ReadString(obj, "name")))
                    {
                        report.Error(id, path + ".name", "gain_ability 必须指定 name（能力名）。");
                    }
                    break;

                case BuiltinEffect.AddCard:
                case BuiltinEffect.AddTemporaryCard:
                case BuiltinEffect.UpgradeCard:
                    if (string.IsNullOrEmpty(ReadString(obj, "name")))
                    {
                        report.Error(id, path + ".name", "必须指定 name（卡牌 id）。");
                    }
                    break;

                case BuiltinEffect.SeekCard:
                {
                    // ★ 为什么这里要两个"必填"：
                    //   游戏侧 CardCoreInfoCheck 是 **struct**、默认值 = 不加任何限制，
                    //   漏写不会崩，而是让这张卡静默变成「检索任意牌」—— 玩法完全不同且看不出来。
                    //   describeKey 则是 tooltip 的文案 key，空了玩家会看到 No translation found。
                    var req = obj["require"] as JObject;
                    if (req == null)
                    {
                        report.Error(id, path + ".require",
                            "seek_card 必须给 require（筛选条件）。★ 漏写的后果不是报错，而是「检索任意牌」——"
                            + "游戏侧条件对象是 struct，默认值语义就是「不加任何限制」。");
                        break;
                    }

                    if (string.IsNullOrEmpty(ReadString(req, "describeKey")))
                    {
                        report.Error(id, path + ".require.describeKey",
                            "必须给 describeKey —— 它是 CardRequirementDes 表里「条件文本」的 key（玩家 tooltip 用）。"
                            + "可复用原版 key（如 BlueCard），或填自己的 key 并在 mod 的 i18n 里加一张 CardRequirementDes 表。");
                    }

                    ValidateRequireColors(req, "colors", path, report, id);
                    ValidateRequireColors(req, "excludeColors", path, report, id);

                    bool hasAny = HasNonEmptyArray(req, "colors") || HasNonEmptyArray(req, "excludeColors")
                        || HasNonEmptyArray(req, "rarities") || !string.IsNullOrEmpty(ReadString(req, "keyword"));
                    if (!hasAny)
                    {
                        report.Error(id, path + ".require",
                            "require 至少要有一个条件（colors / excludeColors / rarities / keyword）——"
                            + "否则等价于「检索任意牌」，与不写没有区别。");
                    }

                    var amounts = obj["amounts"] as JArray;
                    if (amounts == null || amounts.Count < 1)
                    {
                        report.Error(id, path + ".amounts",
                            "seek_card 需要 1 个数字（检索几张）—— 游戏侧直接读 rNumbers[0]，缺了会 NullReferenceException。");
                    }
                    break;
                }

                case BuiltinEffect.AddRandomCard:
                    ValidateEffectPool(obj, path, report, id, "add_random_card",
                        "add_random_card 需要 1 个数字（加几张）—— 游戏侧直接读 rNumbers[0]，"
                        + "缺了会 NullReferenceException。");
                    break;

                case BuiltinEffect.ImproviseCard:
                    // ★ 探寻（原版「不稳定传送门」）。卡池校验与 add_random_card **完全共用**
                    //   （同一个 pool 字段、同一个装配构造器），差别只在数字的含义：
                    //   这里 rNumbers[0] = **能挑几张**；候选张数由游戏规则值 ImproviseCard 决定
                    //   （默认 3，+ ExtraCardsWhenImprovise 策略），卡上不写、也写不出来。
                    ValidateEffectPool(obj, path, report, id, "improvise_card",
                        "improvise_card 需要 1 个数字（**能挑几张**）—— 游戏侧直接读 rNumbers[0]"
                        + "（Effects/EffectImproviseCard.cs:28），缺了会 NullReferenceException。"
                        + "注意：**候选张数不写在卡上**，它由游戏规则值 ImproviseCard 决定（默认 3）。");
                    break;
            }
        }

        private static bool HasNonEmptyArray(JObject o, string field)
        {
            var a = o[field] as JArray;
            return a != null && a.Count > 0;
        }

        /// <summary>
        /// 校验 <c>effects[].pool</c>（卡池）—— <c>add_random_card</c> 与 <c>improvise_card</c> 共用。
        /// </summary>
        /// <remarks>
        /// ★ 两个语义的卡池**是同一个字段、同一个装配构造器**（<c>BuildCardPool</c>），
        /// 所以校验也只有一份 —— 两处各写一份必然漂移。
        /// <para>
        /// ★ 漏写 pool 的后果是"静默变形"级别的坏事：游戏读
        /// <c>GenRandomCardInfoPack.genRandomCardInfoWithProbability</c>，
        /// 且 <c>GetCardInfoPreview</c> 与 <c>EffectBase.AddRandomCardInfoDesStringToLocalizedString</c>
        /// （EffectBase.cs:352-361，读 <c>packName</c>）都解引用同一个对象
        /// ⇒ 不给就在**描述/预览**阶段 NRE（还没打出就崩）。
        /// </para>
        /// </remarks>
        /// <param name="semantic">语义名，用进错误文案（<c>add_random_card</c> / <c>improvise_card</c>）。</param>
        /// <param name="amountsMessage">该语义缺 amounts 时的完整错误文案（数字含义两者不同）。</param>
        private static void ValidateEffectPool(JObject obj, string path, ValidationReport report, string id,
            string semantic, string amountsMessage)
        {
            var pool = obj["pool"] as JObject;
            if (pool == null)
            {
                report.Error(id, path + ".pool",
                    semantic + " 必须给 pool（卡池）。游戏会读卡池对象来抽卡，" +
                    "而且卡面预览也解引用它 —— 不给会在描述/预览阶段就 NullReferenceException。");
                return;
            }

            string poolPath = path + ".pool";

            bool bySpecific = HasNonEmptyArray(pool, "specific");
            bool bySpecial = !string.IsNullOrEmpty(ReadString(pool, "special"));
            var careerToken = pool["career"];
            bool byCareer = careerToken != null && careerToken.Type == JTokenType.Boolean && careerToken.Value<bool>();

            var entriesToken = pool["entries"];
            if (entriesToken != null && entriesToken.Type != JTokenType.Null)
            {
                // ---------- 多条目加权（混池）----------
                if (bySpecific || bySpecial || byCareer)
                {
                    report.Error(id, poolPath,
                        "pool 的**单条目字段**（specific / special / career）与**多条目** entries 互斥，"
                        + "当前两者都写了 —— 游戏侧只有一个卡池结构，同时给会让人以为「混了池」，"
                        + "实际却按其中一条走。要单条目就删掉 entries；要混池就把取卡路径写进每一条 entries[i]。");
                }

                // ★ describeKey 只在"每条都写对了取卡路径"时才判定 ——
                //   条目形态/路径本身写错时，再叠一条"缺 describeKey"只会分散注意力。
                bool entriesWellFormed = ValidatePoolEntries(pool, poolPath, report, id);
                if (entriesWellFormed)
                {
                    ValidateEntriesDescribeKey(pool, poolPath, report, id);
                }
            }
            else
            {
                // ---------- 单条目（原有行为，一字不改）----------
                // ★★ specific 路径**已停用**（2026-10-13 实机崩在存档）：
                //   它把 CardSo **对象引用**放进 EffectInfo，而牌组的 CardInfo 与卡资产共享同一个
                //   EffectInfo 实例 ⇒ 序列化器会为 Card.CardSo 建 DataContract 并抛
                //   InvalidDataContractException，而 SaveCampaign 是"先截断再写" ⇒ 可能损坏战役存档。
                if (bySpecific)
                {
                    report.Error(id, path + ".pool.specific",
                        "**暂不支持 specific（写死卡名表）** —— 它会把 CardSo 对象引用放进效果信息，"
                        + "而牌组的 CardInfo 与卡资产共享同一个 EffectInfo 实例，于是本局结束存档时"
                        + "（SaveCampaign 走 DataContractSerializer）会为 Card.CardSo 建契约并抛 "
                        + "InvalidDataContractException，**可能损坏战役存档**。"
                        + "请改用 special（原版池名，如 Junk）或 career: true + require 过滤。");
                }

                int paths = (bySpecific ? 1 : 0) + (bySpecial ? 1 : 0) + (byCareer ? 1 : 0);
                if (paths == 0)
                {
                    report.Error(id, path + ".pool",
                        "pool 必须**三选一**：specific（写死卡名表）/ special（原版特殊池名）/ career: true（全部标准池）。");
                }
                else if (paths > 1)
                {
                    report.Error(id, path + ".pool",
                        "pool 的三条取卡路径只能选一条（当前填了 " + paths + " 条）——" +
                        "游戏侧 cardPool 是单个枚举值，给多条只会静默用其中一条。");
                }

                var poolReq = pool["require"] as JObject;
                if (poolReq != null)
                {
                    ValidateRequireColors(poolReq, "colors", path + ".pool", report, id);
                    ValidateRequireColors(poolReq, "excludeColors", path + ".pool", report, id);
                }

                // ★ packName 就是卡池说明的本地化键（EffectBase.cs:356）。
                //   special 路径可以省（默认用原版池名，原版自带文案）；
                //   specific / career 必须给，否则卡面会出现 "No translation found for …"。
                if (bySpecific || byCareer)
                {
                    if (string.IsNullOrEmpty(ReadString(pool, "describeKey")))
                    {
                        report.Error(id, path + ".pool.describeKey",
                            "走 specific / career 路径时**必须给 describeKey** —— 它是卡池说明的本地化键，"
                            + "游戏用它取卡面上「加 N 张 … 到你的手中」里那句说明"
                            + "（CardRequirementDes 表）。请在 mod 的 i18n 里加一条该表的条目；"
                            + "走 special 路径时可省略（默认复用原版池名的文案，如 \"Junk\"）。");
                    }
                }
            }

            var amounts = obj["amounts"] as JArray;
            if (amounts == null || amounts.Count < 1)
            {
                report.Error(id, path + ".amounts", amountsMessage);
            }
        }

        /// <summary>
        /// 校验多条目加权卡池（<c>pool.entries</c>，2026-10-14 新增）。
        /// </summary>
        /// <remarks>
        /// 三条硬约束（每条都对应一个"不报错就会静默变形"的后果）：
        /// <list type="number">
        /// <item>条目数 ≥ 1 —— 空数组等于"没有卡池"；</item>
        /// <item>每条必须显式给权重 <c>p</c>、且是 ≥0 的整数 —— 游戏侧
        ///   <c>ElementWithProbability.probability</c> 是 <c>int</c>，平台**不给默认值**
        ///   （默认 1 会让作者以为在等分，而实际比例取决于别的条目写了几）；</item>
        /// <item>权重之和 ≥ 1 —— 全 0 时 <c>max = Σp = 0</c>，
        ///   <c>DistributeProbabilityAmongInfos</c> 里没有任何桶能落进去 ⇒ **一张都抽不出来**
        ///   （玩家只会拿到游戏兜底的 404Card）。</item>
        /// </list>
        /// </remarks>
        /// <returns>
        /// <c>true</c> 表示每条都是对象、权重合法且**取卡路径恰好一条** ——
        /// 只有这时才值得继续判 <c>describeKey</c>（否则是叠加噪声）。
        /// </returns>
        private static bool ValidatePoolEntries(JObject pool, string poolPath, ValidationReport report, string id)
        {
            var arr = pool["entries"] as JArray;
            if (arr == null)
            {
                report.Error(id, poolPath + ".entries",
                    "entries 必须是数组（每项形如 { \"p\": 65, \"special\": \"Junk\", "
                    + "\"require\": { \"rarities\": [\"Common\"] } }）。");
                return false;
            }

            if (arr.Count == 0)
            {
                report.Error(id, poolPath + ".entries",
                    "entries 不能是空数组 —— 要么不写 entries（走单条目字段 specific / special / career），"
                    + "要么至少给 1 条。");
                return false;
            }

            int total = 0;
            bool allWeightsOk = true;
            bool allPathsOk = true;

            for (int i = 0; i < arr.Count; i++)
            {
                string p = poolPath + ".entries[" + i + "]";
                var o = arr[i] as JObject;
                if (o == null)
                {
                    report.Error(id, p, "每一项必须是对象。");
                    allWeightsOk = false;
                    allPathsOk = false;
                    continue;
                }

                // ---- 权重 ----
                var pToken = o["p"];
                if (pToken == null || pToken.Type == JTokenType.Null)
                {
                    report.Error(id, p + ".p",
                        "必须显式给 p（权重，整数 ≥ 0）。★ 平台不给默认值 —— 权重是**相对比例**，"
                        + "默认 1 会让你以为在等分，而实际比例取决于别的条目写了几。");
                    allWeightsOk = false;
                }
                else if (pToken.Type != JTokenType.Integer)
                {
                    report.Error(id, p + ".p",
                        "p 必须是整数（游戏侧 ElementWithProbability.probability 就是 int，"
                        + "不支持小数/字符串）。");
                    allWeightsOk = false;
                }
                else
                {
                    int w = pToken.Value<int>();
                    if (w < 0)
                    {
                        report.Error(id, p + ".p",
                            "权重不能是负数（当前 " + w + "）。游戏按 RandomRange(0, Σp) 累计落桶，"
                            + "负数会让整个 Σp 变小、比例全乱。");
                        allWeightsOk = false;
                    }
                    else
                    {
                        total += w;
                    }
                }

                // ---- 取卡路径：三选一（与单条目形态同一套规则）----
                bool entrySpecific = HasNonEmptyArray(o, "specific");
                bool entrySpecial = !string.IsNullOrEmpty(ReadString(o, "special"));
                var entryCareerToken = o["career"];
                bool entryCareer = entryCareerToken != null && entryCareerToken.Type == JTokenType.Boolean
                    && entryCareerToken.Value<bool>();

                if (entrySpecific)
                {
                    report.Error(id, p + ".specific",
                        "**暂不支持 specific（写死卡名表）** —— 它会把 CardSo 对象引用放进效果信息，"
                        + "本局结束存档时会抛 InvalidDataContractException，**可能损坏战役存档**。"
                        + "请改用 special（原版池名）或 career: true + require 过滤。");
                }

                int paths = (entrySpecific ? 1 : 0) + (entrySpecial ? 1 : 0) + (entryCareer ? 1 : 0);
                if (paths == 0)
                {
                    report.Error(id, p,
                        "entries[" + i + "] 必须**三选一**：specific（写死卡名表）/ special（原版特殊池名）"
                        + "/ career: true（全部标准池）。");
                    allPathsOk = false;
                }
                else if (paths > 1)
                {
                    report.Error(id, p,
                        "entries[" + i + "] 的三条取卡路径只能选一条（当前填了 " + paths + " 条）——"
                        + "游戏侧 cardPool 是单个枚举值，给多条只会静默用其中一条。");
                    allPathsOk = false;
                }

                var req = o["require"] as JObject;
                if (req != null)
                {
                    ValidateRequireColors(req, "colors", p, report, id);
                    ValidateRequireColors(req, "excludeColors", p, report, id);
                }
            }

            if (allWeightsOk && total <= 0)
            {
                report.Error(id, poolPath + ".entries",
                    "所有条目的权重加起来是 0 ⇒ **一张卡都抽不出来**：游戏侧 max = Σp = 0，"
                    + "DistributeProbabilityAmongInfos 里没有任何桶能落进去，"
                    + "玩家只会拿到游戏兜底的 404Card。请给至少一条 ≥1 的权重。");
            }

            return allWeightsOk && allPathsOk;
        }

        /// <summary>
        /// 多条目混池的 <c>packName</c> 策略校验。
        /// </summary>
        /// <remarks>
        /// ★ <c>packName</c> 是**整包一份**的（<c>GenRandomCardInfoPack.packName</c> 是 pack 级字段，
        /// 条目 <c>GenRandomCardInfo</c> 没有自己的名字），所以多条目时也只有一句说明文本。
        /// <para>
        /// 可以省略 <c>describeKey</c> 的唯一情形：**所有条目都是同一个 <c>special</c> 池名**
        /// —— 那时默认复用该原版池名（原版自带文案；原版 <c>UnstablePortal</c> 正是这种：
        /// 三条 <c>CardsFromOtherRealm</c> 只差稀有度）。其余（混合池 / career / specific）
        /// 没有任何现成文案可借，必须自己给 —— 否则卡面直接显示
        /// <c>No translation found for …</c>。
        /// </para>
        /// </remarks>
        private static void ValidateEntriesDescribeKey(JObject pool, string poolPath, ValidationReport report, string id)
        {
            if (!string.IsNullOrEmpty(ReadString(pool, "describeKey"))) { return; }

            var arr = pool["entries"] as JArray;
            if (arr == null || arr.Count == 0) { return; }   // 条目本身的错误已经报过了

            string common = null;
            bool uniformSpecial = true;
            foreach (var item in arr)
            {
                var o = item as JObject;
                string s = o == null ? null : ReadString(o, "special");
                if (string.IsNullOrEmpty(s)) { uniformSpecial = false; break; }
                if (common == null) { common = s; }
                else if (common != s) { uniformSpecial = false; break; }
            }

            if (uniformSpecial) { return; }

            report.Error(id, poolPath + ".describeKey",
                "多条目混池**必须给 describeKey** —— 卡池说明（游戏侧 packName）是**整包一份**的文本，"
                + "条目自己没有名字，而混合池没有任何现成的原版文案可借。"
                + "请在 mod 的 i18n 的 CardRequirementDes 表里加一条该键的文案（否则卡面显示 "
                + "No translation found for …）。★ 唯一的例外：所有条目都是**同一个 special 池名**时"
                + "可以省略 —— 那种情况默认复用该池名的原版文案（原版 UnstablePortal 就是这种）。");
        }

        /// <summary>校验 require 里的颜色名数组（名字与卡牌 colors 同一套）。</summary>
        private static void ValidateRequireColors(JObject req, string field, string path,
            ValidationReport report, string id)
        {
            var arr = req[field] as JArray;
            if (arr == null) { return; }
            for (int i = 0; i < arr.Count; i++)
            {
                var name = arr[i] == null ? null : arr[i].Value<string>();
                if (string.IsNullOrEmpty(name)) { continue; }
                var c = ManaColorNames.Resolve(name);
                if (!c.Mapped)
                {
                    report.Error(id, path + ".require." + field + "[" + i + "]",
                        "未知颜色名 '" + name + "'。合法值：" +
                        string.Join(" / ", ToArray(ManaColorNames.AllNames)) + "。" + (c.Hint ?? ""));
                }
            }
        }
    }
}
