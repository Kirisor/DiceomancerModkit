// =============================================================================
//  ModKit.Core · JSON → 内容定义 的解析层
//
//  这一层的存在意义：让 Loader 与 Game 层**共用同一套"JSON 长什么样"的知识**。
//  如果各自解析一遍，两边迟早对不上（校验器认得的字段，运行时读不到）。
//
//  它是纯逻辑（只依赖 Newtonsoft + Api 定义），因此可以在没有游戏的机器上测。
// =============================================================================

using System;
using System.Collections.Generic;
using ModKit.Api;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ModKit.Core
{
    /// <summary>解析结果。失败时不抛异常——把原因放进 Report，由调用方决定怎么处置。</summary>
    public sealed class ParseResult<T> where T : class
    {
        public T Value { get; private set; }
        public ValidationReport Report { get; private set; }

        public bool Ok { get { return Value != null && !Report.HasErrors; } }

        internal ParseResult(T value, ValidationReport report)
        {
            Value = value;
            Report = report;
        }
    }

    /// <summary>
    /// 内容解析器。
    /// </summary>
    /// <remarks>
    /// 用 Newtonsoft 的 LINQ-to-JSON 手工映射，而不是直接反序列化成 DTO：
    /// 因为作者写错字段名时，直接反序列化会**静默忽略**那个字段
    /// （Newtonsoft 默认 MissingMemberHandling.Ignore），
    /// 而"静默忽略"正是本平台要根除的失败模式。
    /// 手工映射让每个字段都能报路径。
    /// </remarks>
    public static partial class ContentParser
    {
        /// <summary>把 JSON 文本解析成 <see cref="CardDef"/>。</summary>
        public static ParseResult<CardDef> ParseCard(string json)
        {
            var report = new ValidationReport();
            var root = TryObject(json, report, "卡牌");
            if (root == null) { return new ParseResult<CardDef>(null, report); }

            var def = new CardDef
            {
                Schema = Str(root, "$schema"),
                Id = Str(root, "id"),
                Name = LocalizedMap(root, "name"),
                Cost = CostSlots(root, "cost"),
                Rarity = Str(root, "rarity"),
                Colors = StrList(root, "colors"),
                Resolve = Str(root, "resolve"),
                ArtTemplate = Str(root, "artTemplate"),
                Art = Str(root, "art"),
                Effects = ParseEffects(root, report),
                Keywords = KeywordRefs(root, "keywords", report),
                Upgrade = ParseUpgrade(root, report)
            };

            return new ParseResult<CardDef>(def, report);
        }

        /// <summary>把 JSON 文本解析成 <see cref="KeywordDef"/>。</summary>
        public static ParseResult<KeywordDef> ParseKeyword(string json)
        {
            var report = new ValidationReport();
            var root = TryObject(json, report, "关键词");
            if (root == null) { return new ParseResult<KeywordDef>(null, report); }

            var def = new KeywordDef
            {
                Schema = Str(root, "$schema"),
                Id = Str(root, "id"),
                Name = LocalizedMap(root, "name"),
                ArtTemplate = Str(root, "artTemplate"),
                Effects = ParseEffects(root, report)
            };

            var hasNumber = root["hasNumber"];
            def.HasNumber = hasNumber != null && hasNumber.Type == JTokenType.Boolean && hasNumber.Value<bool>();

            return new ParseResult<KeywordDef>(def, report);
        }

        /// <summary>把 JSON 文本解析成 <see cref="RelicDef"/>。</summary>
        public static ParseResult<RelicDef> ParseRelic(string json)
        {
            var report = new ValidationReport();
            var root = TryObject(json, report, "遗物");
            if (root == null) { return new ParseResult<RelicDef>(null, report); }

            var def = new RelicDef
            {
                Schema = Str(root, "$schema"),
                Id = Str(root, "id"),
                Name = LocalizedMap(root, "name"),
                ArtTemplate = Str(root, "artTemplate"),
                Trigger = Str(root, "trigger"),
                // ---- 触发点附加字段（Tier 1）。取值合法性由校验器负责，这里只管读进来 ----
                BattlePhase = ParseBattlePhase(root),
                CertainTurn = ParseCertainTurn(root),
                RelativeCharacter = Str(root, "relativeCharacter"),
                ExtraString = Str(root, "extraString"),
                ColorE = Str(root, "colorE"),
                Effects = ParseEffects(root, report)
            };

            var pool = root["addToPool"];
            def.AddToPool = pool == null || pool.Type == JTokenType.Null || pool.Value<bool>();

            return new ParseResult<RelicDef>(def, report);
        }

        /// <summary>把 JSON 文本解析成 <see cref="I18nDef"/>。</summary>
        public static ParseResult<I18nDef> ParseI18n(string json)
        {
            var report = new ValidationReport();
            var root = TryObject(json, report, "本地化");
            if (root == null) { return new ParseResult<I18nDef>(null, report); }

            var def = new I18nDef
            {
                Schema = Str(root, "$schema"),
                Language = Str(root, "language"),
                Tables = new Dictionary<string, Dictionary<string, string>>()
            };

            var tables = root["tables"] as JObject;
            if (tables != null)
            {
                foreach (var table in tables.Properties())
                {
                    var entries = table.Value as JObject;
                    if (entries == null) { continue; }

                    var map = new Dictionary<string, string>();
                    foreach (var e in entries.Properties())
                    {
                        map[e.Name] = e.Value.Type == JTokenType.String ? (string)e.Value : null;
                    }
                    def.Tables[table.Name] = map;
                }
            }

            return new ParseResult<I18nDef>(def, report);
        }

        /// <summary>解析 <c>mod.json</c>。</summary>
        public static ParseResult<ModManifest> ParseManifest(string json)
        {
            var report = new ValidationReport();
            var root = TryObject(json, report, "清单");
            if (root == null) { return new ParseResult<ModManifest>(null, report); }

            var manifest = new ModManifest
            {
                Schema = Str(root, "$schema"),
                Id = Str(root, "id"),
                Name = Str(root, "name"),
                Version = Str(root, "version"),
                Author = Str(root, "author"),
                EntryAssembly = Str(root, "entryAssembly"),
                GameVersion = Str(root, "gameVersion"),
                Cards = StrList(root, "cards"),
                Relics = StrList(root, "relics"),
                Keywords = StrList(root, "keywords"),
                Abilities = StrList(root, "abilities"),
                I18n = StrList(root, "i18n")
            };

            if (string.IsNullOrEmpty(manifest.Id))
            {
                report.Error(null, "id", "mod.json 缺少 id。");
            }
            if (string.IsNullOrEmpty(manifest.Name))
            {
                // 名字只用于日志，缺了给个兜底，不阻断
                manifest.Name = manifest.Id;
                report.Warn(manifest.Id, "name", "缺少 name，已用 id 代替。");
            }

            return new ParseResult<ModManifest>(manifest, report);
        }

        // ---------------------------------------------------------------------
        // 内部
        // ---------------------------------------------------------------------

        private static JObject TryObject(string json, ValidationReport report, string what)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                report.Error(null, "(文件)", what + "文件为空。");
                return null;
            }

            try
            {
                return JObject.Parse(json);
            }
            catch (Exception ex)
            {
                report.Error(null, "(文件)", what + " JSON 解析失败：" + ex.Message);
                return null;
            }
        }

        private static string Str(JObject obj, string field)
        {
            var t = obj[field];
            if (t == null || t.Type == JTokenType.Null) { return null; }
            return t.Type == JTokenType.String ? (string)t : null;
        }

        private static bool Bool(JObject obj, string field)
        {
            var t = obj[field];
            if (t == null || t.Type == JTokenType.Null) { return false; }
            return t.Type == JTokenType.Boolean && t.Value<bool>();
        }

        /// <summary>解析遗物的 <c>battlePhase</c>（仅 <c>ChangeBattlePhase</c> 使用）。</summary>
        /// <remarks>
        /// ★ 只有**对象**形式才算提供；写成字符串等其他类型时返回 null，
        /// 由校验器报"形状不对"，而不是在这里静默吞掉（那样作者会以为什么都没写错）。
        /// 取值合法性（<c>phase</c> 是否是 Upkeep/Main/End/CleanUp）**不在这里判**。
        /// </remarks>
        private static BattlePhaseSpec ParseBattlePhase(JObject root)
        {
            var o = root["battlePhase"] as JObject;
            if (o == null) { return null; }
            return new BattlePhaseSpec
            {
                Phase = Str(o, "phase"),
                IsStart = Bool(o, "isStart")
            };
        }

        /// <summary>解析遗物的 <c>certainTurn</c>（仅 <c>WholeNewTurnStart</c> 使用）。</summary>
        /// <remarks>
        /// ★ 对应游戏字段 <c>certainTurnStartInfo</c>（platform JSON 改名为 <c>certainTurn</c>）。
        /// 缺省值刻意与原版 dump 一致：<c>require=false, turnCount=0</c>（relation 留空⇒校验器要求补全）。
        /// </remarks>
        private static CertainTurnSpec ParseCertainTurn(JObject root)
        {
            var o = root["certainTurn"] as JObject;
            if (o == null) { return null; }
            return new CertainTurnSpec
            {
                Require = Bool(o, "require"),
                TurnCount = IntOrNull(o, "turnCount") ?? 0,
                Relation = Str(o, "relation")
            };
        }

        /// <summary>
        /// 解析卡牌的 <c>keywords</c>：每项要么是关键词名字符串，要么是"关键词 + 自带效果"的对象。
        /// </summary>
        /// <remarks>
        /// ★ 为什么不能复用 <see cref="StrList"/>：对象形式会被读成 <c>null</c>，
        /// **静默丢掉关键词自带的效果** —— 而像「奇迹」这类关键词，
        /// 效果就是它唯一的实质内容（见 <see cref="KeywordRefDef"/> 的说明）。
        /// </remarks>
        private static List<KeywordRefDef> KeywordRefs(JObject obj, string field, ValidationReport report)
        {
            var arr = obj[field] as JArray;
            if (arr == null) { return null; }

            var list = new List<KeywordRefDef>();
            for (int i = 0; i < arr.Count; i++)
            {
                var item = arr[i];
                if (item.Type == JTokenType.String)
                {
                    // 只要关键词本身（关键词自己会做事，例如 Exert）
                    list.Add(new KeywordRefDef { Name = (string)item });
                }
                else if (item.Type == JTokenType.Object)
                {
                    // 关键词 + 挂在它身上的效果（例如 Miracle）
                    var o = (JObject)item;
                    list.Add(new KeywordRefDef
                    {
                        Name = Str(o, "name"),
                        // 复用效果解析：它读的正是 o["effects"]
                        Effects = ParseEffects(o, report),
                        // 可省略；省略时装配层按"打出时"（OnCast）处理
                        Resolve = Str(o, "resolve"),
                        Number = IntOrNull(o, "number"),
                        ToCard = Str(o, "toCard"),
                        Mana = Str(o, "mana"),
                        // 只有 Summon 用得上：指定召唤物用哪个原版召唤物的名字（拿它去查立绘与显示名）
                        Summon = Str(o, "summon")
                    });
                }
                else
                {
                    report.Error(null, field + "[" + i + "]",
                        "必须是关键词名字符串，或含 name（可选 effects）的对象。");
                }
            }
            return list;
        }

        private static List<string> StrList(JObject obj, string field)
        {
            var arr = obj[field] as JArray;
            if (arr == null) { return null; }

            var list = new List<string>();
            foreach (var item in arr)
            {
                list.Add(item.Type == JTokenType.String ? (string)item : null);
            }
            return list;
        }

        /// <summary>
        /// 解析 <c>cost</c>：每项要么是字符串（单色槽），要么是字符串数组（多色混合槽）。
        /// </summary>
        /// <remarks>
        /// ★ 为什么不能复用 <see cref="StrList"/>：<c>cost</c> 是**嵌套**结构 ——
        /// 外层是"费用槽"，内层是"这一格可用的颜色"。
        /// 用 <c>StrList</c> 会把数组槽读成 <c>null</c>，**静默丢掉多色信息**，
        /// 而那正是游戏 <c>ColorEArray</c> 表达能力的一部分。
        /// <para>
        /// 非法形态（数字 / 对象）留 <c>null</c>，交给校验器按"空槽"报错 ——
        /// 解析层不擅自"尽力解释"，这样错误信息里能带上准确的字段路径。
        /// </para>
        /// </remarks>
        private static List<CostSlotDef> CostSlots(JObject obj, string field)
        {
            var arr = obj[field] as JArray;
            if (arr == null) { return null; }

            var slots = new List<CostSlotDef>();
            foreach (var item in arr)
            {
                var slot = new CostSlotDef();
                if (item.Type == JTokenType.String)
                {
                    // 单色槽：["Void", ...]
                    slot.Colors = new List<string> { (string)item };
                }
                else if (item.Type == JTokenType.Array)
                {
                    // 多色混合槽：[["Void","Red"], ...] —— 这一格几个颜色任选其一
                    slot.Colors = new List<string>();
                    foreach (var c in (JArray)item)
                    {
                        slot.Colors.Add(c.Type == JTokenType.String ? (string)c : null);
                    }
                }
                else
                {
                    slot.Colors = null;   // 非法形态，交给校验器报路径
                }
                slots.Add(slot);
            }
            return slots;
        }

        private static Dictionary<string, string> LocalizedMap(JObject obj, string field)
        {
            var src = obj[field] as JObject;
            if (src == null) { return null; }

            var map = new Dictionary<string, string>();
            foreach (var p in src.Properties())
            {
                map[p.Name] = p.Value.Type == JTokenType.String ? (string)p.Value : null;
            }
            return map;
        }

        private static List<EffectSpec> ParseEffects(JObject root, ValidationReport report)
        {
            var arr = root["effects"] as JArray;
            if (arr == null) { return null; }

            var list = new List<EffectSpec>();
            for (int i = 0; i < arr.Count; i++)
            {
                var o = arr[i] as JObject;
                if (o == null)
                {
                    report.Error(null, "effects[" + i + "]", "不是对象。");
                    continue;
                }

                var spec = new EffectSpec
                {
                    Builtin = Str(o, "builtin"),
                    Custom = Str(o, "custom"),
                    Target = Str(o, "target"),
                    Mode = Str(o, "mode"),
                    Color = Str(o, "color"),
                    Name = Str(o, "name"),
                    Setter = ParseNumberSetter(o),
                    Require = ParseCardRequire(o),
                    Pool = ParseCardPool(o),
                    Amounts = ParseAmounts(o, report, "effects[" + i + "].amounts")
                };
                list.Add(spec);
            }
            return list;
        }

        /// <summary>
        /// 解析 <c>effects[].setter</c>（数字改造配置，仅 <c>set_number</c> / <c>number_plus_sub</c> 用）。
        /// </summary>
        /// <remarks>
        /// 全部字段可省略：省略 = <c>null</c> = 由装配层填**游戏自身的默认值**。
        /// 布尔字段宽松解析（写成字符串 "true" 也接受，与既有的 <see cref="Str"/> 同风格）；
        /// 数字字段复用文件里既有的 <c>IntOrNull</c>（只认整数 JSON）。
        /// 类型不对就当没写、不报错 —— 真正的判据是"游戏能不能跑"，由装配层兜底。
        /// </remarks>
        private static ModKit.Api.NumberSetterDef ParseNumberSetter(JObject effectObj)
        {
            var t = effectObj == null ? null : effectObj["setter"];
            if (t == null || t.Type != JTokenType.Object) { return null; }

            var o = (JObject)t;
            return new ModKit.Api.NumberSetterDef
            {
                Permanent = BoolOrNull(o, "permanent"),
                AllowDice = BoolOrNull(o, "allowDice"),
                AllowFromCard = BoolOrNull(o, "allowFromCard"),
                ChooseCount = IntOrNull(o, "chooseCount")
            };
        }

        /// <summary>
        /// 解析 <c>effects[].require</c>（检索条件，仅 <c>seek_card</c> 用）。
        /// </summary>
        /// <remarks>
        /// 字段全可省略；是否"该给而没给"由 <c>ContentValidator</c> 判定（它才知道是哪个语义），
        /// 这里只负责把能读的读出来。
        /// </remarks>
        private static ModKit.Api.CardRequireDef ParseCardRequire(JObject effectObj)
        {
            var t = effectObj == null ? null : effectObj["require"];
            if (t == null || t.Type != JTokenType.Object) { return null; }

            var o = (JObject)t;
            return new ModKit.Api.CardRequireDef
            {
                Colors = StrList(o, "colors"),
                ExcludeColors = StrList(o, "excludeColors"),
                Rarities = StrList(o, "rarities"),
                Keyword = Str(o, "keyword"),
                DescribeKey = Str(o, "describeKey")
            };
        }

        /// <summary>
        /// 解析 <c>effects[].pool</c>（卡池，<c>add_random_card</c> / <c>improvise_card</c> 共用）。
        /// </summary>
        /// <remarks>
        /// 三条路径（specific / special / career）是否**恰好给一条**、单条目与多条目
        /// （<c>entries</c>）是否**互斥**、权重是否合法，全部由校验器判定，
        /// 这里只负责把能读的读出来（同 <c>require</c> 的分工）。
        /// </remarks>
        private static ModKit.Api.CardPoolDef ParseCardPool(JObject effectObj)
        {
            var t = effectObj == null ? null : effectObj["pool"];
            if (t == null || t.Type != JTokenType.Object) { return null; }

            var o = (JObject)t;
            return new ModKit.Api.CardPoolDef
            {
                Specific = StrList(o, "specific"),
                Special = Str(o, "special"),
                Career = BoolOrNull(o, "career"),
                DescribeKey = Str(o, "describeKey"),
                Require = ParsePoolRequire(o),
                Entries = ParsePoolEntries(o)
            };
        }

        /// <summary>
        /// 解析 <c>pool.entries</c>（多条目加权卡池，2026-10-14 新增）。
        /// </summary>
        /// <remarks>
        /// 权重键是 <c>p</c> —— 与游戏 dump 里的形状一致
        /// （<c>pack{ name=… | p=65 … | p=30 … }</c>）。
        /// </remarks>
        private static List<ModKit.Api.PoolEntryDef> ParsePoolEntries(JObject poolObj)
        {
            var arr = poolObj == null ? null : poolObj["entries"] as JArray;
            if (arr == null) { return null; }

            var list = new List<ModKit.Api.PoolEntryDef>();
            foreach (var item in arr)
            {
                var o = item as JObject;
                if (o == null)
                {
                    // 形态错误由校验器报错；这里放一个空条目，让条目数/路径判定仍然能说清是哪一条。
                    list.Add(new ModKit.Api.PoolEntryDef());
                    continue;
                }

                list.Add(new ModKit.Api.PoolEntryDef
                {
                    P = IntOrNull(o, "p"),
                    Specific = StrList(o, "specific"),
                    Special = Str(o, "special"),
                    Career = BoolOrNull(o, "career"),
                    Require = ParsePoolRequire(o)
                });
            }
            return list;
        }

        private static ModKit.Api.PoolRequireDef ParsePoolRequire(JObject poolObj)
        {
            var t = poolObj == null ? null : poolObj["require"];
            if (t == null || t.Type != JTokenType.Object) { return null; }

            var o = (JObject)t;
            return new ModKit.Api.PoolRequireDef
            {
                Rarities = StrList(o, "rarities"),
                Colors = StrList(o, "colors"),
                OnlyColors = BoolOrNull(o, "onlyColors"),
                AllColors = BoolOrNull(o, "allColors"),
                ExcludeColors = StrList(o, "excludeColors"),
                SingleColor = BoolOrNull(o, "singleColor"),
                MultiColor = BoolOrNull(o, "multiColor"),
                Keyword = Str(o, "keyword"),
                CannotBe = Str(o, "cannotBe")
            };
        }

        private static bool? BoolOrNull(JObject o, string field)
        {
            var t = o[field];
            if (t == null || t.Type == JTokenType.Null) { return null; }
            if (t.Type == JTokenType.Boolean) { return t.Value<bool>(); }
            if (t.Type == JTokenType.String)
            {
                bool b;
                if (bool.TryParse(t.Value<string>(), out b)) { return b; }
            }
            return null;
        }

        /// <summary>
        /// 解析卡牌的 <c>upgrade</c> 块（升级"+"）。
        /// </summary>
        /// <remarks>
        /// ★ 为什么单列一个块、而不是让作者"再写一张升级后的卡"：
        /// 游戏的普通升级是**增量** —— 改数值 / 换费用 / 加删关键词 / 替换或插入效果，
        /// 由 <c>CardUpgradeInfo</c> 承载，作用在原卡的**深拷贝**上。
        /// <para>
        /// 另有一套"特殊升级"是 `specialUpgradedCardName` 指向**另一张卡**，
        /// 平台目前**不暴露**它 —— 而且装配时会显式清空该字段，
        /// 否则 mod 卡会"升级成"模板卡的那张卡（继承来的错指向）。
        /// </para>
        /// </remarks>
        private static CardUpgradeDef ParseUpgrade(JObject root, ValidationReport report)
        {
            var u = root["upgrade"] as JObject;
            if (u == null) { return null; }

            var def = new CardUpgradeDef();

            // ---- 改效果数值 ----
            var numbers = u["effectNumbers"] as JArray;
            if (numbers != null)
            {
                def.EffectNumbers = new List<EffectNumberChangeDef>();
                for (int i = 0; i < numbers.Count; i++)
                {
                    var o = numbers[i] as JObject;
                    if (o == null)
                    {
                        report.Error(null, "upgrade.effectNumbers[" + i + "]", "不是对象。");
                        continue;
                    }
                    def.EffectNumbers.Add(new EffectNumberChangeDef
                    {
                        Index = IntOrNull(o, "index"),
                        Amounts = ParseAmounts(o, report, "upgrade.effectNumbers[" + i + "].amounts")
                    });
                }
            }

            // ---- 整份替换型 ----
            def.Cost = CostSlots(u, "cost");
            def.Colors = StrList(u, "colors");

            // ---- 关键词增删 ----
            def.AddKeywords = KeywordRefs(u, "addKeywords", report);
            def.RemoveKeywords = StrList(u, "removeKeywords");

            // ---- 效果替换 / 插入 ----
            def.ReplaceEffects = ParseEffectReplacements(u, "replaceEffects", report);
            def.InsertEffects = ParseEffectReplacements(u, "insertEffects", report);

            // ---- 改关键词数值 ----
            var kwNumbers = u["keywordNumbers"] as JArray;
            if (kwNumbers != null)
            {
                def.KeywordNumbers = new List<KeywordNumberDef>();
                for (int i = 0; i < kwNumbers.Count; i++)
                {
                    var o = kwNumbers[i] as JObject;
                    if (o == null)
                    {
                        report.Error(null, "upgrade.keywordNumbers[" + i + "]", "不是对象。");
                        continue;
                    }
                    def.KeywordNumbers.Add(new KeywordNumberDef
                    {
                        Keyword = Str(o, "keyword"),
                        Number = IntOrNull(o, "number")
                    });
                }
            }

            return def;
        }

        private static List<EffectReplaceDef> ParseEffectReplacements(JObject u, string field, ValidationReport report)
        {
            var arr = u[field] as JArray;
            if (arr == null) { return null; }

            var list = new List<EffectReplaceDef>();
            for (int i = 0; i < arr.Count; i++)
            {
                var o = arr[i] as JObject;
                if (o == null)
                {
                    report.Error(null, "upgrade." + field + "[" + i + "]", "不是对象。");
                    continue;
                }
                list.Add(new EffectReplaceDef
                {
                    Index = IntOrNull(o, "index"),
                    // 复用效果解析：它读的正是 o["effects"]
                    Effects = ParseEffects(o, report)
                });
            }
            return list;
        }

        private static int? IntOrNull(JObject obj, string field)
        {
            var t = obj[field];
            if (t == null || t.Type == JTokenType.Null) { return null; }
            return t.Type == JTokenType.Integer ? (int?)t.Value<int>() : null;
        }

        private static List<NumberSpec> ParseAmounts(JObject obj, ValidationReport report, string path)
        {
            var arr = obj["amounts"] as JArray;
            if (arr == null) { return null; }

            var list = new List<NumberSpec>();
            for (int i = 0; i < arr.Count; i++)
            {
                var item = arr[i];

                if (item.Type == JTokenType.Integer)
                {
                    list.Add(new NumberSpec { Value = (int)item });
                    continue;
                }

                var o = item as JObject;
                var dice = o == null ? null : o["dice"] as JObject;
                if (dice == null)
                {
                    report.Error(null, path + "[" + i + "]", "既不是整数也不是骰子对象。");
                    list.Add(new NumberSpec());
                    continue;
                }

                int min = dice["min"] != null && dice["min"].Type == JTokenType.Integer ? (int)dice["min"] : 1;
                int max = dice["max"] != null && dice["max"].Type == JTokenType.Integer ? (int)dice["max"] : 6;

                list.Add(new NumberSpec { Dice = new DiceSpec { Min = min, Max = max } });
            }
            return list;
        }
    }
}
