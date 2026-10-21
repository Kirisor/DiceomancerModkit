// =============================================================================
//  ModKit.Core · 内容校验器（分片）—— 关键词的命名派生 + 升级块
//
//  由 ContentValidator.cs 按子域机械拆分而来（2026-10-13），只做剪切 + 粘贴：
//    · ReportKeywordId / Capitalize
//        —— id → 游戏类名的派生规则
//    · CheckKeywords            —— 卡牌 keywords 引用是否注册
//    · CheckUpgrade / CheckUpgradeKeywordRefs / CheckUpgradeIndexes
//        —— upgrade 块的效果下标越界与新增关键词引用
//  公共入口 ValidateKeyword 仍在 ContentValidator.cs。
// =============================================================================

using ModKit.Api;
using Newtonsoft.Json.Linq;

namespace ModKit.Core
{
    public static partial class ContentValidator
    {
        /// <summary>
        /// 关键词 id 校验 —— 与其它内容**同一格式**（小写），并自动派生类名。
        /// </summary>
        /// <remarks>
        /// 为什么关键词也要小写：平台的 id 规则统一是
        /// <c>^[a-z][a-z0-9_]{2,40}$</c>（便于加 mod 前缀、避免大小写歧义）。
        /// 而游戏的类名约定是 PascalCase。两者的桥是**平台自动派生类名**：
        /// <c>id = "tenacious"</c> → 类名 <c>CardKeywordTenacious</c>。
        /// <para>
        /// 所以作者只需要保证：<b>类名 = "CardKeyword" + id 的首字母大写形式</b>。
        /// 这条规则会在下面以 Info 诊断明确写出来 ——
        /// 因为"类名写错"在游戏里是**静默退化成基类**（不报错、行为丢失）。
        /// </para>
        /// </remarks>
        private static void ReportKeywordId(string id, ValidationReport report)
        {
            if (string.IsNullOrEmpty(id))
            {
                report.Error(null, "id", "缺少 id。");
                return;
            }

            if (!IdPattern.IsMatch(id))
            {
                report.Error(id, "id",
                    "id 不符合 ^[a-z][a-z0-9_]{2,40}$（小写字母开头，只含小写字母/数字/下划线，长度 3-41）。" +
                    "注意关键词 id 也要小写 —— 平台会自动派生类名，例如 " +
                    "id \"tenacious\" → 类 Keyword.CardKeywordTenacious。");
            }
        }

        /// <summary>把下划线分隔的小写名转成 PascalCase（用于派生类名提示）。</summary>
        private static string Capitalize(string id)
        {
            if (string.IsNullOrEmpty(id)) { return id; }

            var parts = id.Split('_');
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                if (p.Length == 0) { continue; }
                // 跳过 mod 前缀段（含 '.' 的段在 split('_') 里不会出现，但保险起见）
                sb.Append(char.ToUpperInvariant(p[0]));
                if (p.Length > 1) { sb.Append(p.Substring(1)); }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 升级（<c>upgrade</c>）块校验 —— 目前专查**效果下标越界**。
        /// </summary>
        /// <remarks>
        /// ★ 为什么这条非查不可：游戏应用升级时的边界判断写成了
        /// <c>if (effectIndex &gt; cardEffectInfos.Length)</c> —— 是 <b>&gt; 而不是 &gt;=</b>。
        /// 于是 <c>index == Length</c> 会被**直接放行**，紧接着访问
        /// <c>cardEffectInfos[Length]</c> 抛 <c>IndexOutOfRangeException</c>，把游戏搞崩。
        /// 平台在**加载期**就拦住它：作者只会看到一条可读的报错。
        /// <para>
        /// ★ 另一件必须让作者知道的事：升级里的下标**只能指向卡牌自己的 <c>effects</c>**。
        /// 游戏全部效果类增量都打在 <c>cardEffectInfos</c> 上，
        /// <b>改不到关键词自带的效果</b>（<c>cardKeyWordInfos[].effectInfos</c>）——
        /// 详见 <see cref="ModKit.Api.KeywordRefDef"/> 的说明。
        /// </para>
        /// </remarks>
        private static void CheckUpgrade(JObject root, IContentCapabilities caps,
            ValidationReport report, string id)
        {
            var up = root["upgrade"];
            if (up == null || up.Type == JTokenType.Null) { return; }
            if (up.Type != JTokenType.Object)
            {
                report.Error(id, "upgrade", "upgrade 必须是对象。");
                return;
            }

            // 卡牌自己的效果数量 —— 升级里的下标只能指向这一段
            int effectCount = 0;
            var effects = root["effects"] as JArray;
            if (effects != null) { effectCount = effects.Count; }

            var obj = (JObject)up;
            CheckUpgradeIndexes(obj, "effectNumbers", effectCount, report, id);
            CheckUpgradeNumberCounts(obj, report, id);
            CheckUpgradeIndexes(obj, "replaceEffects", effectCount, report, id);
            CheckUpgradeIndexes(obj, "insertEffects", effectCount, report, id);
            CheckUpgradeKeywordRefs(obj, "addKeywords", caps, report, id);
        }

        /// <summary>
        /// 校验 <c>upgrade.effectNumbers[i].amounts</c> 的长度必须与目标效果一致。
        /// </summary>
        /// <remarks>
        /// ★ 实机 2026-10-21 的报错：自定义效果「蛙王号令」原效果有 2 个数字
        /// （伤害 12 / 流血 2），升级却只给了 1 个 → 升级后该效果的数字槽变 1 个，
        /// 而 <c>EffectDescription</c> 模板里还有 <c>{Number1}</c>
        /// ⇒ 战斗/卡面渲染时抛
        /// <c>OperationException: Error parsing format string: Could not evaluate the selector</c>。
        /// 游戏侧不会校验这个长度差，平台必须拦。
        /// </remarks>
        private static void CheckUpgradeNumberCounts(JObject root, ValidationReport report, string id)
        {
            var up = root["upgrade"] as JObject;
            if (up == null) { return; }

            var nums = up["effectNumbers"] as JArray;
            var effects = root["effects"] as JArray;
            if (nums == null || effects == null) { return; }

            for (int i = 0; i < nums.Count; i++)
            {
                var item = nums[i] as JObject;
                if (item == null) { continue; }

                int index = -1;
                var idxToken = item["index"];
                if (idxToken != null) { try { index = (int)idxToken; } catch { continue; } }
                if (index < 0 || index >= effects.Count) { continue; }

                var target = effects[index] as JObject;
                if (target == null) { continue; }

                int want = (target["amounts"] as JArray) == null ? 0 : ((JArray)target["amounts"]).Count;
                var gotToken = item["amounts"] as JArray;
                int got = gotToken == null ? 0 : gotToken.Count;

                if (want != got)
                {
                    report.Error(id, "upgrade.effectNumbers[" + i + "].amounts",
                        "数字个数必须是 " + want + " 个（与 effects[" + index + "].amounts 一致），当前 " +
                        got + " 个。升级会用这份数组**整份替换**该效果的数字槽：" +
                        "少了，描述模板里的 {Number…} 占位符取不到值，" +
                        "战斗/卡面渲染会抛 SmartFormat 解析异常（实机 2026-10-21 复现）。");
                }
            }
        }

        /// <summary>
        /// 校验升级里的 <c>addKeywords</c>（新增关键词，可带自带效果）。
        /// </summary>
        /// <remarks>
        /// ★ 这一段非查不可：**它是"升级关键词效果"的唯一正路**（原版做法 ——
        /// 删掉旧关键词、换上数值更大的新关键词）。所以它的形态必须和卡牌
        /// <c>keywords</c> 完全一致，关键词名也必须真注册过，
        /// 否则奇迹会安静地变成"未激活"。
        /// </remarks>
        private static void CheckUpgradeKeywordRefs(JObject up, string field, IContentCapabilities caps,
            ValidationReport report, string id)
        {
            var arr = up[field] as JArray;
            if (arr == null) { return; }

            for (int i = 0; i < arr.Count; i++)
            {
                string path = "upgrade." + field + "[" + i + "]";
                string name;

                if (arr[i].Type == JTokenType.String)
                {
                    name = (string)arr[i];
                }
                else if (arr[i].Type == JTokenType.Object)
                {
                    var o = (JObject)arr[i];
                    var nameToken = o["name"];
                    name = (nameToken != null && nameToken.Type == JTokenType.String)
                        ? (string)nameToken : null;

                    if (string.IsNullOrEmpty(name))
                    {
                        report.Error(id, path, "对象形式的关键词必须写 name。");
                        continue;
                    }
                    // 自带效果走同一套校验（路径显示成 effects[...]，含义是这个关键词的效果）
                    CheckEffects(o, caps, report, id, false);
                }
                else
                {
                    report.Error(id, path,
                        "必须是关键词名字符串，或含 name（可选 resolve / effects）的对象。");
                    continue;
                }

                if (!Contains(caps.KnownKeywords, name))
                {
                    report.Error(id, path, "关键词 '" + name + "' 未注册。" + Near(name, caps.KnownKeywords));
                }
            }
        }

        /// <summary>
        /// 校验升级里某一组增量的 <c>index</c> 是否指向卡牌自己的效果。
        /// </summary>
        /// <remarks>
        /// <c>insertEffects</c> 允许 <c>index == effectCount</c>（追加到末尾），其余不允许。
        /// </remarks>
        private static void CheckUpgradeIndexes(JObject up, string field, int effectCount,
            ValidationReport report, string id)
        {
            var arr = up[field] as JArray;
            if (arr == null) { return; }

            bool isInsert = field == "insertEffects";
            int upper = isInsert ? effectCount : effectCount - 1;

            for (int i = 0; i < arr.Count; i++)
            {
                var o = arr[i] as JObject;
                if (o == null) { continue; }

                string path = "upgrade." + field + "[" + i + "].index";
                var t = o["index"];
                if (t == null || t.Type != JTokenType.Integer)
                {
                    report.Error(id, path, "缺少 index（从 0 开始的效果下标）。");
                    continue;
                }

                int idx = t.Value<int>();
                if (idx < 0 || idx > upper)
                {
                    report.Error(id, path,
                        "效果下标 " + idx + " 越界：这张卡的 effects 只有 " + effectCount + " 项"
                        + (effectCount == 0 ? "（没有效果）" : "（合法下标 0.." + upper + "）") + "。"
                        + "★ 注意 upgrade 的下标**只能指向卡牌自己的 effects**，"
                        + "改不到关键词自带的效果（cardKeyWordInfos[].effectInfos）。"
                        + "若不拦住，游戏会在升级时越界崩掉（它自己的边界判断写成了 > 而不是 >=）。");
                }
            }
        }

        private static void CheckKeywords(JObject root, IContentCapabilities caps,
            ValidationReport report, string id)
        {
            var token = root["keywords"];
            if (token == null || token.Type == JTokenType.Null) { return; }

            var arr = token as JArray;
            if (arr == null)
            {
                report.Error(id, "keywords", "keywords 必须是数组。");
                return;
            }

            for (int i = 0; i < arr.Count; i++)
            {
                string path = "keywords[" + i + "]";

                // 两种形态：字符串（只要关键词本身）／对象（关键词 + 挂在它身上的效果）
                string name;
                if (arr[i].Type == JTokenType.String)
                {
                    name = (string)arr[i];
                }
                else if (arr[i].Type == JTokenType.Object)
                {
                    var holder = (JObject)arr[i];
                    var nameToken = holder["name"];
                    name = (nameToken != null && nameToken.Type == JTokenType.String)
                        ? (string)nameToken : null;

                    if (string.IsNullOrEmpty(name))
                    {
                        report.Error(id, path, "对象形式的关键词必须写 name。");
                        continue;
                    }

                    // 关键词自带的效果也走同一套校验 ——
                    // 否则这类错误会一直拖到装配期才暴露。
                    // （路径会显示成 effects[...]；含义是"这个关键词的效果"。）
                    CheckEffects(holder, caps, report, id, false);
                }
                else
                {
                    report.Error(id, path,
                        "必须是关键词名字符串，或含 name（可选 effects）的对象。" +
                        "例如 \"Exert\"，或 { \"name\": \"Miracle\", \"effects\": [ … ] }。");
                    continue;
                }

                if (!Contains(caps.KnownKeywords, name))
                {
                    report.Error(id, path, "关键词 '" + name + "' 未注册。" + Near(name, caps.KnownKeywords));
                }
            }
        }
    }
}
