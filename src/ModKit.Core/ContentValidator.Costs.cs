// =============================================================================
//  ModKit.Core · 内容校验器（分片）—— 费用与卡牌色数组
//
//  由 ContentValidator.cs 按子域机械拆分而来（2026-10-13），只做剪切 + 粘贴：
//    · CheckCost              —— 费用槽：逐色合法性 + 有序颜色组合是否已注册图标键
//    · CheckOptionalColorArray —— 可选 colors 数组里每个颜色名是否存在
//  卡牌颜色的**组合上限**（最多两种）另见 ContentValidator.CardColors.cs。
//  公共入口 ValidateCard 与共享小工具仍在 ContentValidator.cs。
// =============================================================================

using System.Collections.Generic;
using ModKit.Api;
using Newtonsoft.Json.Linq;

namespace ModKit.Core
{
    public static partial class ContentValidator
    {
        /// <summary>
        /// 费用校验——这是本平台**最有价值的一条**。
        /// </summary>
        /// <remarks>
        /// 游戏侧的失败模式：<c>ManaCost.SetCostColorEs</c> 用 <c>ManaIconDic[colorEs]</c> 索引，
        /// 多色或不存在的组合抛 <c>KeyNotFoundException</c>，而索引器异常不带消息，
        /// 报错只显示 <c>Card.ColorEArray</c>。平台在加载期用探测得到的真实色集合拦截，
        /// 作者看到的是"Void 可用、Purple 在本 build 未注册"这样的可操作信息。
        /// </remarks>
        private static void CheckCost(JObject root, IContentCapabilities caps,
            ValidationReport report, string id)
        {
            var token = root["cost"];
            if (token == null || token.Type != JTokenType.Array)
            {
                report.Error(id, "cost",
                    "缺少 cost，且必须是数组。无费用卡写 []；单色槽如 [\"Void\"]；多色混合槽如 [[\"Void\", \"Red\"]]。");
                return;
            }

            var arr = (JArray)token;
            if (arr.Count == 0)
            {
                // ★ 空数组 = 无费用卡，**合法**。
                //   游戏侧原版「喷烟器 Smoker」的 cardManaCostColorEArrays 就是空数组（见 ModKitDump/cards.json）；
                //   装配层 BuildManaCost 对空列表本来就返回空数组，这里只是不要再拦。
                //   （旧版报错并让作者改写成 ["Void"] —— 那是**错**的：["Void"] 表示"花 1 个虚色"。）
                return;
            }

            for (int i = 0; i < arr.Count; i++)
            {
                string path = "cost[" + i + "]";

                // ---- 一个费用槽：字符串=单色；数组=多色混合（任选其一）----
                var slotNames = new List<string>();
                if (arr[i].Type == JTokenType.String)
                {
                    slotNames.Add((string)arr[i]);
                }
                else if (arr[i].Type == JTokenType.Array)
                {
                    foreach (var t in (JArray)arr[i])
                    {
                        if (t.Type != JTokenType.String)
                        {
                            report.Error(id, path, "多色费用槽里只能写颜色名字符串。");
                            return;
                        }
                        slotNames.Add((string)t);
                    }
                    if (slotNames.Count == 0)
                    {
                        report.Error(id, path, "多色费用槽不能是空数组。");
                        continue;
                    }
                }
                else
                {
                    report.Error(id, path,
                        "费用槽必须写成字符串（单色）或字符串数组（多色混合）。" +
                        "例如 \"Void\"，或 [\"Void\", \"Red\"] 表示这一格虚色或红色都能付。");
                    continue;
                }

                // ---- ① 逐色查合法性 ----
                var slot = new List<ManaColor>();
                bool allColorsOk = true;
                for (int k = 0; k < slotNames.Count; k++)
                {
                    var lookup = ManaColorNames.Resolve(slotNames[k]);
                    if (!lookup.Mapped)
                    {
                        report.Error(id, path, "未知颜色名 '" + slotNames[k] + "'。合法值：" +
                            string.Join(" / ", ToArray(ManaColorNames.AllNames)) + "。" + (lookup.Hint ?? ""));
                        allColorsOk = false;
                        continue;
                    }
                    slot.Add(lookup.Value);
                }
                if (!allColorsOk) { continue; }

                // ---- ② 整个槽的组合是否注册过（**有序**匹配）----
                //
                // ★ 这里的旧版本把多色槽判为"非法输入" —— 那是**错的**。
                //   游戏 Card.ColorEArray 本身就是颜色数组，一个槽可以装多个颜色，
                //   语义正是"其中任意一个都能付这一格"；卡面图标按**完整有序序列**
                //   在 ManaIconDic 里查表，所以合法组合是**预制在资产里的有限集合**。
                //   平台启动时枚举该字典的全部键得到真实组合（见 GameCapabilitiesProbe.ProbeCostCombos）。
                if (!caps.IsKnownCostCombo(slot))
                {
                    if (slot.Count == 1)
                    {
                        report.Error(id, path,
                            "颜色 '" + slotNames[0] + "' 在**当前游戏版本里未注册图标键**，因此无法作为费用。" +
                            "平台启动时探测真实可用色；本 build 可用的单色是：" +
                            JoinColors(caps.AvailableCostColors) + "。");
                    }
                    else
                    {
                        report.Error(id, path,
                            "多色费用槽 [" + string.Join(", ", slotNames) + "] **在本 build 里没有注册图标**。" +
                            "多色槽不是任意组合都行 —— 卡面图标是按**有序颜色序列**预制在游戏资产里的，" +
                            "顺序也参与匹配（[\"Void\",\"Red\"] 与 [\"Red\",\"Void\"] 是两个不同的键）。" +
                            "请查游戏日志里平台打印的「费用槽组合探测」一行，改用其中已注册的组合。");
                    }
                }
            }
        }

        private static void CheckOptionalColorArray(JObject root, string field,
            IContentCapabilities caps, ValidationReport report, string id)
        {
            var token = root[field];
            if (token == null || token.Type == JTokenType.Null) { return; }

            var arr = token as JArray;
            if (arr == null)
            {
                report.Error(id, field, field + " 必须是数组。");
                return;
            }

            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i].Type != JTokenType.String)
                {
                    report.Error(id, field + "[" + i + "]", "必须是颜色名字符串。");
                    continue;
                }

                var lookup = ManaColorNames.Resolve((string)arr[i]);
                if (!lookup.Mapped)
                {
                    report.Error(id, field + "[" + i + "]", "未知颜色名 '" + arr[i] + "'。" + (lookup.Hint ?? ""));
                }
            }
        }
    }
}
