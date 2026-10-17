// =============================================================================
//  ModKit.Core · 卡牌颜色组合校验
//
//  ★ 这条规则的来历（用户实机指出，2026-10-11）：
//    平台最初的示例 mod 写了 `"colors": ["Red", "Green", "Blue"]`（三色），
//    用户实机看到后指出：**卡牌颜色最多只能出现两种颜色的组合**
//    （例如红蓝 / 红绿 / 绿蓝 / 白绿 / 紫蓝 / 黑蓝），三色是非法组合。
//
//  ★ 另一条同样重要的澄清（用户强调）：
//    **卡牌的"费用色"（cost）与"卡牌色"（colors）是两个完全独立的系统。**
//    费用色决定出牌要花什么颜色的法力；卡牌色决定卡属于哪个流派/池。
//    它们各自独立校验，**不存在谁必须包含谁的关系**。
//    平台此前把两者混在一起的暗示是错的 —— 本文件把这条写死成规则。
// =============================================================================

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ModKit.Core
{
    public static partial class ContentValidator
    {
        /// <summary>卡牌颜色的合法上限：最多两种。</summary>
        /// <remarks>
        /// 与游戏一致（用户实机确认）。三色及以上的组合是非法的。
        /// </remarks>
        public const int MaxCardColors = 2;

        /// <summary>
        /// 校验卡牌颜色（<c>colors</c>）的组合是否合法。
        /// </summary>
        /// <remarks>
        /// 只查"组合是否合法"，逐个颜色是否存在于游戏色表里由
        /// <see cref="CheckOptionalColorArray"/> 负责 —— 两者职责分开，报错更准。
        /// </remarks>
        private static void CheckCardColorCombination(JObject root, ValidationReport report, string id)
        {
            var token = root["colors"];
            if (token == null || token.Type == JTokenType.Null) { return; }

            var arr = token as JArray;
            if (arr == null) { return; }   // 不是数组的情况已由 CheckOptionalColorArray 报错

            var names = new List<string>();
            for (int i = 0; i < arr.Count; i++)
            {
                var s = arr[i] as JValue;
                if (s != null && s.Value != null) { names.Add(s.Value.ToString()); }
            }

            if (names.Count <= MaxCardColors) { return; }

            report.Error(id, "colors",
                "卡牌颜色最多只能有 " + MaxCardColors + " 种，你写了 " + names.Count + " 种（" +
                string.Join(" / ", names.ToArray()) + "）。" +
                "游戏里的合法卡牌色是两色组合，例如 红蓝 / 红绿 / 绿蓝 / 白绿 / 紫蓝 / 黑蓝。" +
                "注意：**费用色（cost）与卡牌色（colors）是两个独立的系统** —— " +
                "要改出牌花费请改 cost，不要往 colors 里加颜色。");
        }
    }
}
