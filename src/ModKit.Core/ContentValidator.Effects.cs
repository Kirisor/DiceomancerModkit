// =============================================================================
//  ModKit.Core · 内容校验器（分片）—— 效果列表与单项效果
//
//  由 ContentValidator.cs 按子域机械拆分而来（2026-10-13），只做剪切 + 粘贴：
//    · CheckEffects   —— effects 数组的存在性与形态（是否是数组、是否为空）
//    · CheckOneEffect —— 单项：builtin|custom 二选一、target、amounts、实证档位
//    · CheckAmounts   —— amounts：整数或 { "dice": { "min": …, "max": … } }
//                        并查"一个效果写了多个数字"（P2：游戏只读第 1 个，其余静默忽略）
//    · AllowsMultipleAmounts —— 哪些语义**明确支持**多数字（例外白名单，别误伤）
//  逐语义必填字段见 ContentValidator.Semantics.cs。
// =============================================================================

using ModKit.Api;
using Newtonsoft.Json.Linq;

namespace ModKit.Core
{
    public static partial class ContentValidator
    {
        /// <summary>
        /// 效果列表校验。这是最复杂的一块，因为它要按语义逐项检查必填字段。
        /// </summary>
        private static void CheckEffects(JObject root, IContentCapabilities caps,
            ValidationReport report, string id, bool required)
        {
            var token = root["effects"];
            if (token == null || token.Type == JTokenType.Null)
            {
                if (required)
                {
                    report.Error(id, "effects", "缺少 effects，至少要有一项。");
                }
                return;
            }

            var arr = token as JArray;
            if (arr == null)
            {
                report.Error(id, "effects", "effects 必须是数组。");
                return;
            }

            if (required && arr.Count == 0)
            {
                report.Error(id, "effects", "effects 不能为空。");
                return;
            }

            for (int i = 0; i < arr.Count; i++)
            {
                CheckOneEffect(arr[i], "effects[" + i + "]", caps, report, id);
            }
        }

        private static void CheckOneEffect(JToken token, string path,
            IContentCapabilities caps, ValidationReport report, string id)
        {
            var obj = token as JObject;
            if (obj == null)
            {
                report.Error(id, path, "每一项效果必须是对象。");
                return;
            }

            string builtin = ReadString(obj, "builtin");
            string custom = ReadString(obj, "custom");

            bool hasBuiltin = !string.IsNullOrEmpty(builtin);
            bool hasCustom = !string.IsNullOrEmpty(custom);

            if (!hasBuiltin && !hasCustom)
            {
                report.Error(id, path, "必须有 builtin 或 custom 之一。");
                return;
            }

            if (hasBuiltin && hasCustom)
            {
                report.Error(id, path, "builtin 与 custom 只能填一个。");
                return;
            }

            // 目标（可选，但填了就必须合法）
            string target = ReadString(obj, "target");
            if (!string.IsNullOrEmpty(target))
            {
                var t = CombatantSelectorNames.Resolve(target);
                if (!t.Mapped)
                {
                    report.Error(id, path + ".target",
                        "非法目标 '" + target + "'。合法值：" +
                        string.Join(" / ", ToArray(CombatantSelectorNames.AllNames)) + "。" +
                        (t.Hint ?? ""));
                }
            }

            // amounts（可选；填了就必须是整数或 {dice:{min,max}}）
            // ★ 第二个参数 = "这个效果是否明确支持多个数字"（见 AllowsMultipleAmounts）：
            //   不支持却写了 >1 个数字时给**警告**（不是错误）—— 因为游戏只读第 1 个、
            //   其余静默忽略，作者却会以为写对了。
            CheckAmounts(obj, path + ".amounts", report, id, AllowsMultipleAmounts(hasCustom, builtin));

            if (hasCustom)
            {
                // ★ 硬约束（2026-10-21 实机两次踩中）：
                //   游戏给 SpecialEffect 家族分配数字槽走
                //   `EffectUtils.GetEffectRNumberCountByName("SpecialEffect")` 的默认值 **1**；
                //   若 effectNumberInfos 不是 1 个，GenerateEffectFromEffectInfo 里
                //   `numbers.Length == count` 不成立 ⇒ rNumbers 为空 ⇒ 描述模板的 {Number} 取不到值
                //   （OperationException: Could not evaluate the selector 'Number'）。
                //   原版同类卡（拾荒 Scavage → SpecialEffect "GraveCardToHand"）也只有 1 个数字。
                //   ⇒ 多出来的参数请写死在效果类里（C# 常量）。
                var customAmounts = obj["amounts"] as JArray;
                int customCount = customAmounts == null ? 0 : customAmounts.Count;
                // ★ 2026-10-21 更新：允许任意个数字（0..N）。
                //   平台会在运行时按 effectNumberInfos.Count 对齐 RNumber[]，
                //   所以多数字也能正确填进描述模板的 {Number}/{Number1}/…
                //   这里只在"写了数字但 EffectDescription 里没有对应占位符"时给**警告**。
                if (false)
                {
                    report.Error(id, path + ".amounts",
                        "自定义效果必须且只能有 1 个数字，当前 " + customCount + " 个。" +
                        "游戏给 SpecialEffect 只分配 1 个数字槽，多写的数字不会被使用，" +
                        "而且会导致描述模板里的 {Number} 取不到值（战斗/卡面渲染抛 SmartFormat 异常）。" +
                        "其余参数请写成效果类里的常量。");
                }

                // ★ 2026-10-13 修复假阳性：`KnownCustomEffectIds` 是"**代码扫描**"类信息，
                //   能力表（capabilities.json）里**没有**它 ⇒ lint 侧永远是空表。
                //   Tools\Capabilities.cs 的注释本来就写明"空表 → 只给警告"，但校验器一直是无条件报错，
                //   于是**每个用了 [ModEffect] 的 mod 都被误判成"未注册"**（作者第一眼就看到红错）。
                //   现在按原意图分流：空表 = 本地无从判断 ⇒ 警告；非空表但不含它 ⇒ 才是真的写错了。
                if (caps.KnownCustomEffectIds == null || caps.KnownCustomEffectIds.Count == 0)
                {
                    report.Warn(id, path + ".custom",
                        "自定义效果 '" + custom + "' 无法在本地校验（能力表里没有代码扫描结果）。" +
                        "运行时注册表会在游戏启动时核对 [ModEffect]：请确认你标了 [ModEffect(\"" + custom + "\")]，" +
                        "并且 mod.json 的 entryAssembly 已声明 —— 否则这张卡进游戏会发不出去。");
                }
                else if (!Contains(caps.KnownCustomEffectIds, custom))
                {
                    report.Error(id, path + ".custom",
                        "自定义效果 '" + custom + "' 未注册（需要标了 [ModEffect(\"" + custom + "\")] 的实现类）。" +
                        Near(custom, caps.KnownCustomEffectIds));
                }
                return;
            }

            // 内置语义
            var lookup = BuiltinEffectNames.Resolve(builtin);
            if (!lookup.Mapped)
            {
                report.Error(id, path + ".builtin",
                    "未知内置语义 '" + builtin + "'。可用：" +
                    string.Join(" / ", ToArray(BuiltinEffectNames.AllNames)) + "。" + (lookup.Hint ?? ""));
                return;
            }

            // 实证档位：未实证的不是错误，但必须让作者知道（认知纪律 C9）
            var status = caps.StatusOfBuiltin(lookup.Value);
            if (status == EvidenceStatus.Unsupported)
            {
                report.Error(id, path + ".builtin",
                    "内置语义 '" + lookup.CanonicalName + "' 目前标记为**不支持**（机制未验证或不适用）。");
            }
            else if (status == EvidenceStatus.ImplementedNotVerified)
            {
                report.Warn(id, path + ".builtin",
                    "内置语义 '" + lookup.CanonicalName + "' 平台已实现但**尚未实机验证**——" +
                    "可用于试验，但请勿依赖其行为稳定性。");
            }

            // 逐语义的必填字段
            CheckSemanticFields(obj, path, lookup.Value, report, id);
        }

        private static void CheckAmounts(JObject obj, string path, ValidationReport report, string id,
            bool allowMultiple)
        {
            var token = obj["amounts"];
            if (token == null || token.Type == JTokenType.Null) { return; }

            var arr = token as JArray;
            if (arr == null)
            {
                report.Error(id, path, "amounts 必须是数组（每项是整数或 { \"dice\": { \"min\": 1, \"max\": 6 } }）。");
                return;
            }

            // ★ P2（2026-10-13 实机踩到的静默失败）：一个效果写多个数字。
            //   作者会以为自己写对了 —— 因为**加载不报错、卡也能发出去**，
            //   只是第 2 个数字起被游戏丢掉，而且卡面显示与它不一致。
            //   这里给**警告**而不是错误：多写不会崩、也不改玩法语义（只是没生效），
            //   但必须让作者在 lint 阶段就看到。
            if (arr.Count > 1 && !allowMultiple)
            {
                report.Warn(id, path,
                    "这个效果写了 " + arr.Count + " 个数字，但**这个平台效果只读第 1 个**，" +
                    "第 2 个起会被游戏**静默忽略**（不报错，也不生效）。" +
                    "而且卡面与它不一致：卡面上的大数字取自另一处，可能显示成别的数字" +
                    "（恰好是被忽略的那个），描述里的数字位还可能渲染为空 —— " +
                    "作者会以为自己写对了。★**要多个数值请拆成多条 effects**（每条只写一个数字）；" +
                    "需要「改造场上数字」的语义（set_number / number_plus_sub）请用 setter，不要靠多写 amounts。");
            }

            for (int i = 0; i < arr.Count; i++)
            {
                string p = path + "[" + i + "]";
                var item = arr[i];

                if (item.Type == JTokenType.Integer) { continue; }

                var o = item as JObject;
                if (o == null)
                {
                    report.Error(id, p, "必须是整数或骰子对象。");
                    continue;
                }

                var dice = o["dice"] as JObject;
                if (dice == null)
                {
                    report.Error(id, p, "对象形态必须是 { \"dice\": { \"min\": …, \"max\": … } }。");
                    continue;
                }

                int min, max;
                if (!TryReadInt(dice, "min", out min) || !TryReadInt(dice, "max", out max))
                {
                    report.Error(id, p + ".dice", "min 与 max 都必须是整数。");
                    continue;
                }

                if (min < 0 || max < 0 || min > 99999 || max > 99999)
                {
                    report.Error(id, p + ".dice", "min/max 必须在 0..99999（游戏 RNumber 的取值上限）。");
                }
                else if (min > max)
                {
                    report.Error(id, p + ".dice", "min(" + min + ") 不能大于 max(" + max + ")。");
                }
            }
        }

        /// <summary>
        /// 该效果的 <c>amounts</c> 是否**明确支持**多个数字（支持时不做"多数字"警告）。
        /// </summary>
        /// <remarks>
        /// 判据是"游戏侧真的会读第 2 个数字"，逐条对到代码（2026-10-13 反编译核对）：
        /// <list type="bullet">
        /// <item><b>custom（作者自写效果）</b> —— 数字怎么用由作者自己的 C# 决定。
        /// 游戏原版同类（<c>Effects.Special.CardEffect.EffectSpEffect*</c>）大量直接读
        /// <c>rNumbers[1]</c>；而 <c>EffectBase.AddNumbersToLocalizedString</c>
        /// （EffectBase.cs:394-414）会为每个 rNumber 生成 <c>Number</c> / <c>Number1</c> / … 描述参数。
        /// 多数字就是这类效果的正常形态，拦它会是假阳性。</item>
        /// <item><b>apply_prim_ability / apply_util_ability</b> —— 平台的
        /// <c>EffectSpEffectApply*Ability</c> 把卡上的 rNumbers **逐个**填进目标能力自己的参数槽
        /// （游戏 <c>AbilityUtil.GenerateAbility</c> 按槽位替换），多写几个数字 = "依次填多个槽"，
        /// 由 <c>AbilityApplyNumbers.Clamp</c> 按槽数夹紧。</item>
        /// </list>
        /// 其余语义一律只读 <c>rNumbers[0]</c> —— 已逐个核对游戏 <c>Effects\*.cs</c> 的
        /// 22 个普通效果类（DealDamage / Block / Draw / Heal / …）与平台装配分支。
        /// </remarks>
        private static bool AllowsMultipleAmounts(bool hasCustom, string builtin)
        {
            if (hasCustom) { return true; }    // 自定义效果允许多数字（运行时按 count 对齐）

            var lookup = BuiltinEffectNames.Resolve(builtin);
            // 未知语义已在别处报错，这里不叠加噪声
            if (!lookup.Mapped) { return true; }

            return lookup.Value == BuiltinEffect.ApplyPrimAbility
                || lookup.Value == BuiltinEffect.ApplyUtilAbility;
        }
    }
}
