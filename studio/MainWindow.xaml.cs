using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ModKit.Studio
{
    public partial class MainWindow : Window
    {
        /// <summary>mods 根目录：来自 StudioConfig（首次启动自动探测游戏目录）。</summary>
        private static string DefaultModsRoot
        {
            get { return StudioConfig.Current.ModsRoot ?? ""; }
        }

        private static readonly string[] Rarities = { "Common", "Uncommon", "Rare" };
        private static readonly string[] Resolves = { "OnCast", "OnDraw", "OnDiscard", "EndInHand" };

        /// <summary>平台认可的内置效果名（取自 ModKit.Core.BuiltinEffectNames.ByName 的实际键）。</summary>
        private static readonly string[] BuiltinNames =
        {
            "deal_damage", "heal", "block", "lose_life", "draw", "discard_random",
            "gain_mana", "set_max_mana", "clean_mana", "apply_state", "gain_ability",
            "add_card", "add_temporary_card", "upgrade_card", "end_turn", "add_random_card",
            "apply_prim_ability", "apply_util_ability", "change_max_health", "discard_hand",
            "discard_one_random", "number_plus_sub", "recover", "set_number", "seek_card", "improvise_card",
        };
        private static Dictionary<string, Dictionary<string, string>> _localeTables;

        /// <summary>
        /// 读取游戏运行期导出的本地化表（BepInEx\ModKitDump\localization-zh.json）。
        /// 结构：{ data: { 表名: { 键: 中文 } } }
        /// </summary>
        private static void LoadLocaleTables()
        {
            if (_localeTables != null) { return; }
            _localeTables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            try
            {
                var file = StudioConfig.Current.DumpFile(LocalizedFile);
                if (!File.Exists(file)) { return; }
                var root = JsonNode.Parse(File.ReadAllText(file, Encoding.UTF8)) as JsonObject;
                var data = root == null ? null : root["data"] as JsonObject;
                if (data == null) { return; }
                foreach (var table in data)
                {
                    var map = new Dictionary<string, string>(StringComparer.Ordinal);
                    var obj = table.Value as JsonObject;
                    if (obj != null)
                    {
                        foreach (var kv in obj)
                        {
                            var v = kv.Value == null ? null : kv.Value.ToString();
                            if (!string.IsNullOrEmpty(v)) { map[kv.Key] = v; }
                        }
                    }
                    _localeTables[table.Key] = map;
                }
            }
            catch { }
        }

        private const string LocalizedFile = "localization-zh.json";

        /// <summary>
        /// 游戏本地化文本里带富文本标记与占位符（例如 `当打出{CardRequirement}时`、`&lt;link="…"&gt;`），
        /// 作为下拉标签要清掉，否则显示成半截带符号的句子。
        /// </summary>
        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text)) { return text; }
            var t = Regex.Replace(text, "<link=\"[^\"]*\">", "");
            t = t.Replace("</link>", "");
            t = Regex.Replace(t, "\\{[^}]*\\}", "");
            return t.Trim();
        }

        /// <summary>先查游戏本地化表，查不到再用备用释义。</summary>
        private static string GameText(string table, string key, Dictionary<string, string> fallback)
        {
            LoadLocaleTables();
            Dictionary<string, string> map;
            if (_localeTables.TryGetValue(table, out map))
            {
                string v;
                if (map.TryGetValue(key, out v) && !string.IsNullOrEmpty(v)) { return Sanitize(v); }
            }
            string fb;
            if (fallback != null && fallback.TryGetValue(key, out fb)) { return fb; }
            return null;
        }

        /// <summary>触发点中文释义（英文 id 写盘，中文只做下拉标签）。</summary>
        private static readonly Dictionary<string, string> TriggerZh = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AbilityStandardTrigger"] = "能力标准触发",
            ["AcquireRelic"] = "获得遗物",
            ["BattleStart"] = "战斗开始",
            ["BeenAttacked"] = "被攻击",
            ["BuyInStore"] = "商店购买",
            ["CardEffectNoCast"] = "卡牌效果（未打出）",
            ["CardKeywordTrigger"] = "卡牌关键词触发",
            ["CastCard"] = "打出卡牌",
            ["CastCardFinished"] = "卡牌结算完成",
            ["ChangeBattlePhase"] = "战斗阶段变化",
            ["ChangeWorldPhase"] = "世界阶段变化",
            ["CharacterCheck"] = "角色检定",
            ["CharacterLoseLife"] = "角色失去生命",
            ["CharacterRollDice"] = "角色掷骰",
            ["DamageBeenDealt"] = "伤害已造成",
            ["DrawCard"] = "抽牌",
            ["EnemyDieStart"] = "敌人死亡",
            ["EnemyHealthBarBreak"] = "敌人血条击破",
            ["EnterUnvisitedNode"] = "进入未访问节点",
            ["ExertCard"] = "耗竭卡牌",
            ["HandCardCountChange"] = "手牌数量变化",
            ["ManaConsumed"] = "法力消耗",
            ["ManaOverflow"] = "法力溢出",
            ["OrganismPerish"] = "生物死亡",
            ["RelicActive"] = "遗物主动使用",
            ["RelicCorrupted"] = "遗物被腐蚀",
            ["ShuffleGraveToLibrary"] = "弃牌堆洗回牌库",
            ["SummonResolve"] = "召唤结算",
            ["TakeRest"] = "休息",
            ["TimePass"] = "时间流逝",
            ["UseTheOneDice"] = "使用唯一骰",
            ["WholeNewTurnStart"] = "全新回合开始",
        };

        /// <summary>把英文取值列表包装成「中文（英文）」下拉项。</summary>
        private static List<Choice> ToChoices(IEnumerable<string> keys, Dictionary<string, string> zh, string table = null, Dictionary<string, string> fallback = null)
        {
            var list = new List<Choice>();
            foreach (var k in keys)
            {
                var cn = table == null ? null : GameText(table, k, fallback);
                if (string.IsNullOrEmpty(cn) && zh != null) { zh.TryGetValue(k, out cn); }
                list.Add(new Choice { Key = k, Caption = string.IsNullOrEmpty(cn) ? k : cn + "（" + k + "）" });
            }
            return list;
        }

        private static void SelectByKey(ComboBox box, string key)
        {
            var items = box.ItemsSource as IEnumerable<Choice>;
            if (items != null)
            {
                var hit = items.FirstOrDefault(c => string.Equals(c.Key, key, StringComparison.Ordinal));
                if (hit != null) { box.SelectedItem = hit; return; }
            }
            box.Text = key;
        }

        private static string[] _triggerNames;

        /// <summary>
        /// 遗物/能力的触发点：来自《效果字典》§四 名单（32 个「游戏实际会发出」的），
        /// 文档注明"不在名单里的即使合法也不会触发"，所以下拉只列这 32 个（仍可手填）。
        /// </summary>
        private static string[] TriggerNames
        {
            get
            {
                if (_triggerNames != null) { return _triggerNames; }
                var set = new SortedSet<string>(StringComparer.Ordinal);
                try
                {
                    var path = DocsFile;
                    if (File.Exists(path))
                    {
                        var lines = File.ReadAllLines(path, Encoding.UTF8);
                        bool inSection = false;
                        bool inCode = false;
                        foreach (var raw in lines)
                        {
                            var line = raw.Trim();
                            if (line.StartsWith("## 四")) { inSection = true; continue; }
                            if (line.StartsWith("## 五")) { break; }
                            if (!inSection) { continue; }
                            if (line.StartsWith("```")) { inCode = !inCode; continue; }
                            if (!inCode) { continue; }
                            foreach (var tok in line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                if (tok.Length > 2 && char.IsLetter(tok[0])) { set.Add(tok); }
                            }
                        }
                    }
                }
                catch { }
                _triggerNames = set.ToArray();
                return _triggerNames;
            }
        }

        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private enum Kind { Card, Relic, Keyword }

        /// <summary>下拉项：Caption 给人看（中文），Key 写进 JSON（英文 id）。</summary>
        private sealed class Choice
        {
            public string Key;
            public string Caption;
            public override string ToString() => Caption;
        }

        private sealed class Item
        {
            public string Path;
            public JsonObject Root;
            public string Id => (string)Root["id"] ?? "";
            public override string ToString()
            {
                var zh = (string)Root["name"]?["zh-Hans"] ?? "";
                return string.IsNullOrEmpty(zh) ? Id : zh + "  (" + Id + ")";
            }
        }

        private sealed class ModEntry
        {
            public readonly string Dir;
            public ModEntry(string dir) { Dir = dir; }
            public string Id => System.IO.Path.GetFileName(Dir);
            public override string ToString() => Id;
        }

        private static string[] _vanillaCardNames;

        /// <summary>原版卡的内部名清单 —— artTemplate 的合法取值（来自游戏运行期导出的 ModKitDump/cards.json）。</summary>
        private static string[] VanillaCardNames
        {
            get
            {
                if (_vanillaCardNames != null) { return _vanillaCardNames; }
                try
                {
                    var dump = StudioConfig.Current.DumpFile("cards.json");
                    if (!File.Exists(dump)) { return _vanillaCardNames = new string[0]; }
                    var root = JsonNode.Parse(File.ReadAllText(dump, Encoding.UTF8)) as JsonObject;
                    var list = new List<string>();
                    if (root != null && root["cards"] is JsonArray cards)
                    {
                        foreach (var c in cards.OfType<JsonObject>())
                        {
                            var n = (string)c["internalName"];
                            if (!string.IsNullOrEmpty(n)) { list.Add(n); }
                        }
                    }
                    _vanillaCardNames = list.OrderBy(x => x, StringComparer.Ordinal).ToArray();
                }
                catch { _vanillaCardNames = new string[0]; }
                return _vanillaCardNames;
            }
        }

        private static string[] _keywordNames;

        /// <summary>可选关键词：游戏原版关键词（capabilities.json）+ 本 mod 自定义关键词。</summary>
        private static string[] VanillaKeywordNames
        {
            get
            {
                if (_keywordNames != null) { return _keywordNames; }
                var set = new SortedSet<string>(StringComparer.Ordinal);
                try
                {
                    var dump = StudioConfig.Current.DumpFile("capabilities.json");
                    if (File.Exists(dump))
                    {
                        var root = JsonNode.Parse(File.ReadAllText(dump, Encoding.UTF8)) as JsonObject;
                        if (root != null && root["knownKeywords"] is JsonArray kws)
                        {
                            foreach (var k in kws) { var v = k == null ? null : k.ToString(); if (!string.IsNullOrEmpty(v)) { set.Add(v); } }
                        }
                    }
                }
                catch { }
                _keywordNames = set.ToArray();
                return _keywordNames;
            }
        }

        /// <summary>
        /// 关键词资料：来自 BepInEx\docs\ModKit-效果字典.md 的 §3.1 表（英文名 / 中文参考译名 / 是否吃数值），
        /// 以及 §3.3 的危险名单（写了会让游戏 tooltip 抛 NullReferenceException，直接崩）。
        /// </summary>
        private sealed class KeywordInfo
        {
            public string Key;
            public string Zh;
            public bool TakesNumber;
        }

        /// <summary>效果资料：来自《效果字典》§2.2 表（含义 / 是否吃数字 / 额外字段 / 是否可用）。</summary>
        private sealed class EffectInfo
        {
            public string Key;
            public string Zh;
            public bool NeedsNumber;
            public string Extra;      // 例如 "name"、"mode"、"color"、"pool"
            public bool Usable;
        }

        private static Dictionary<string, EffectInfo> _effectInfo;
        private static string[] _abilityNames;

        private static Dictionary<string, string> _abilityZh;
        private static Choice[] _abilityChoices;
        private static Dictionary<string, string> _abilityGroup;

        /// <summary>§五 的四个小节 ⇒ 分组名（Buff / DeBuff / PrimAbility / Util），用于在下拉里归类。</summary>
        private static void LoadAbilityGroups()
        {
            if (_abilityGroup != null) { return; }
            _abilityGroup = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var path = DocsFile;
                if (!File.Exists(path)) { return; }
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                string group = null;
                foreach (var raw in lines)
                {
                    var line = raw.Trim();
                    if (line.StartsWith("### 5.2")) { group = "增益"; continue; }
                    if (line.StartsWith("### 5.3")) { group = "减益"; continue; }
                    if (line.StartsWith("### 5.4")) { group = "基础"; continue; }
                    if (line.StartsWith("### 5.5")) { group = "功能"; continue; }
                    if (line.StartsWith("## 六")) { group = null; continue; }
                    if (group == null || !line.StartsWith("|") || !line.Contains("`")) { continue; }
                    var cells = line.Split('|');
                    if (cells.Length < 3) { continue; }
                    var name = cells[1].Replace("★", "").Trim().Trim('`').Trim();
                    if (name.Length > 0 && char.IsLetter(name[0]) && name != "名字" && !_abilityGroup.ContainsKey(name))
                    {
                        _abilityGroup[name] = group;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 能力下拉项：中文名来自游戏运行期导出的 ModKitDump/abilities-zh.json
        /// （能力名只在游戏本地化表 `Ability` 里，离线拿不到），英文名单兜底用《效果字典》§五。
        /// </summary>
        private static Choice[] AbilityChoices()
        {
            if (_abilityChoices != null) { return _abilityChoices; }
            _abilityZh = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var dump = StudioConfig.Current.DumpFile("abilities-zh.json");
                if (File.Exists(dump))
                {
                    var root = JsonNode.Parse(File.ReadAllText(dump, Encoding.UTF8)) as JsonObject;
                    var abilities = root == null ? null : root["abilities"] as JsonObject;
                    if (abilities != null)
                    {
                        foreach (var kv in abilities)
                        {
                            var zh = kv.Value == null ? null : kv.Value.ToString();
                            if (!string.IsNullOrEmpty(zh)) { _abilityZh[kv.Key] = zh; }
                        }
                    }
                }
            }
            catch { }

            LoadEffectDictionary();
            LoadAbilityGroups();
            var keys = new SortedSet<string>(_abilityNames ?? new string[0], StringComparer.Ordinal);
            foreach (var k in _abilityZh.Keys) { keys.Add(k); }

            var order = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["增益"] = 0, ["减益"] = 1, ["基础"] = 2, ["功能"] = 3,
            };
            var list = new List<Choice>();
            foreach (var k in keys.OrderBy(k => {
                string g0;
                return _abilityGroup.TryGetValue(k, out g0) && order.ContainsKey(g0) ? order[g0] : 9;
            }).ThenBy(k => k, StringComparer.Ordinal))
            {
                string zh;
                string g;
                var label = _abilityZh.TryGetValue(k, out zh) ? zh + "（" + k + "）" : k;
                var prefix = _abilityGroup.TryGetValue(k, out g) ? "【" + g + "】" : "【其它】";
                list.Add(new Choice { Key = k, Caption = prefix + label });
            }
            _abilityChoices = list.ToArray();
            return _abilityChoices;
        }

        private static void LoadEffectDictionary()
        {
            if (_effectInfo != null) { return; }
            _effectInfo = new Dictionary<string, EffectInfo>(StringComparer.Ordinal);
            var abilities = new SortedSet<string>(StringComparer.Ordinal);
            try
            {
                var path = DocsFile;
                if (!File.Exists(path)) { return; }
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                bool inEffectTable = false;
                bool inAbility = false;
                foreach (var raw in lines)
                {
                    var line = raw.Trim();
                    if (line.StartsWith("### 2.2")) { inEffectTable = true; inAbility = false; continue; }
                    if (line.StartsWith("## 三")) { inEffectTable = false; continue; }
                    if (line.StartsWith("## 五")) { inAbility = true; inEffectTable = false; continue; }
                    if (line.StartsWith("## 六")) { inAbility = false; continue; }

                    if (inEffectTable && line.StartsWith("|") && line.Contains("`"))
                    {
                        var cells = line.Split('|');
                        if (cells.Length < 6) { continue; }
                        var key = cells[1].Trim().Trim('`').Trim();
                        if (key.Length == 0 || key.StartsWith("~~") || !char.IsLetter(key[0])) { continue; }
                        var extraCell = cells[4];
                        var extra = "";
                        if (extraCell.Contains("`name`")) { extra = "name"; }
                        else if (extraCell.Contains("`mode`")) { extra = "mode"; }
                        else if (extraCell.Contains("`color`")) { extra = "color"; }
                        else if (extraCell.Contains("`pool`")) { extra = "pool"; }
                        else if (extraCell.Contains("`require`")) { extra = "require"; }
                        else if (extraCell.Contains("`setter`")) { extra = "setter"; }
                        _effectInfo[key] = new EffectInfo
                        {
                            Key = key,
                            Zh = cells[2].Trim(),
                            NeedsNumber = cells[3].Contains("✅"),
                            Extra = extra,
                            Usable = !cells[5].Contains("🚫"),
                        };
                        continue;
                    }

                    if (inAbility && line.StartsWith("|") && line.Contains("`"))
                    {
                        var cells = line.Split('|');
                        if (cells.Length < 3) { continue; }
                        var name = cells[1].Trim().Trim('`').Trim();
                        if (name.Length > 0 && char.IsLetter(name[0]) && name != "名字") { abilities.Add(name); }
                    }
                }
            }
            catch { }
            _abilityNames = abilities.ToArray();
        }

        private static Dictionary<string, KeywordInfo> _keywordInfo;
        private static HashSet<string> _dangerKeywords;

        private static string DocsFile
        {
            get
            {
                return StudioConfig.Current.DocsFile("ModKit-效果字典.md");
            }
        }

        private static void LoadKeywordDictionary()
        {
            if (_keywordInfo != null) { return; }
            _keywordInfo = new Dictionary<string, KeywordInfo>(StringComparer.Ordinal);
            _dangerKeywords = new HashSet<string>(StringComparer.Ordinal);
            // §3.2「lint 认可但内容从未使用」——编辑器也不提供（文档：别写）
            foreach (var n in new[] { "Modified", "Glitch", "Duplicate", "Assault" }) { _dangerKeywords.Add(n); }
            try
            {
                var path = DocsFile;
                if (!File.Exists(path)) { return; }
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                bool inDanger = false;
                bool inKeywordTable = false;   // 只在 §3.1 的表格里取关键词，避免把 §2 的效果表也收进来
                foreach (var raw in lines)
                {
                    var line = raw.Trim();
                    if (line.StartsWith("### 3.1")) { inKeywordTable = true; continue; }
                    if (line.StartsWith("### 3.2")) { inKeywordTable = false; continue; }
                    if (line.StartsWith("### 3.3")) { inDanger = true; inKeywordTable = false; continue; }
                    if (line.StartsWith("### 3.4")) { inDanger = false; continue; }

                    if (inDanger)
                    {
                        foreach (var tok in line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (char.IsLetter(tok[0])) { _dangerKeywords.Add(tok); }
                        }
                        continue;
                    }

                    if (!inKeywordTable || !line.StartsWith("|") || !line.Contains("`")) { continue; }
                    var cells = line.Split('|');
                    if (cells.Length < 4) { continue; }
                    var key = cells[1].Trim().Trim('`').Trim();
                    if (key.Length == 0 || key == "关键词") { continue; }
                    if (!char.IsLetter(key[0])) { continue; }
                    _keywordInfo[key] = new KeywordInfo
                    {
                        Key = key,
                        Zh = cells[2].Trim(),
                        TakesNumber = cells[3].Contains("✅"),
                    };
                }
            }
            catch { }
        }

        private string _modsRoot;
        private ModEntry _mod;
        private Item _current;
        private Kind _kind = Kind.Card;

        public MainWindow()
        {
            InitializeComponent();

            RarityBox.ItemsSource = ToChoices(Rarities, null, "Rarity", new Dictionary<string, string>
            {
                ["Common"] = "普通", ["Uncommon"] = "罕见", ["Rare"] = "稀有",
            });
            ResolveBox.ItemsSource = ToChoices(Resolves, null, "ResolveTiming", new Dictionary<string, string>
            {
                ["OnCast"] = "打出时", ["OnDraw"] = "抽到牌时", ["OnDiscard"] = "被弃掉时", ["EndInHand"] = "回合结束仍在手中",
            });
            var triggerChoices = new List<Choice>();
            foreach (var t in TriggerNames)
            {
                var zhText = GameText("Trigger", t, TriggerZh);
                triggerChoices.Add(new Choice
                {
                    Key = t,
                    Caption = string.IsNullOrEmpty(zhText) ? t : zhText + "（" + t + "）",
                });
            }
            TriggerBox.ItemsSource = triggerChoices;   // 32 个真实触发点，中文标签来自 TriggerZh
            EffectKindBox.ItemsSource = BuildEffectChoices();
            RelicEffectKindBox.ItemsSource = BuildEffectChoices();
            var cfg0 = StudioConfig.Current;
            ModsRootBox.Text = Directory.Exists(cfg0.ModsRoot) ? cfg0.ModsRoot : AppContext.BaseDirectory;

            Loaded += delegate { Reload(); };
        }

        private void Reload()
        {
            _modsRoot = ModsRootBox.Text.Trim();
            ModList.Items.Clear();
            ContentList.Items.Clear();
            _current = null;
            if (!Directory.Exists(_modsRoot))
            {
                Status("找不到 mods 根目录：" + _modsRoot + " —— 点「浏览…」选择游戏根目录");
                return;
            }
            foreach (var dir in Directory.GetDirectories(_modsRoot).OrderBy(x => x, StringComparer.Ordinal))
            {
                if (Directory.Exists(System.IO.Path.Combine(dir, "cards")) ||
                    Directory.Exists(System.IO.Path.Combine(dir, "relics")) ||
                    Directory.Exists(System.IO.Path.Combine(dir, "keywords")))
                {
                    ModList.Items.Add(new ModEntry(dir));
                }
            }
            Status("找到 " + ModList.Items.Count + " 个模组（" + _modsRoot + "）");
            if (ModList.Items.Count > 0) { ModList.SelectedIndex = 0; }
            UpdateKindButtons();
        }

        private static string DirOf(Kind kind)
        {
            return kind == Kind.Card ? "cards" : kind == Kind.Relic ? "relics" : "keywords";
        }

        private static string SchemaOf(Kind kind)
        {
            return kind == Kind.Card ? "modkit/card@1" : kind == Kind.Relic ? "modkit/relic@1" : "modkit/keyword@1";
        }

        /// <summary>新建模组：在 mods 根目录下建 <id> 目录 + 清单 + 空目录，然后直接载入。</summary>
        private void OnNewModClick(object sender, RoutedEventArgs e)
        {
            var root = ModsRootBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(root))
            {
                Status("先在顶栏选择游戏目录（点「浏览…」），再新建模组");
                return;
            }
            try { Directory.CreateDirectory(root); }
            catch (Exception ex) { Status("mods 根目录不可用：" + ex.Message); return; }

            var id = Prompt("新建模组", "模组 id（小写字母开头，可含小写字母 / 数字 / 下划线）", "my_mod");
            if (id == null) { return; }
            id = id.Trim().ToLowerInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z][a-z0-9_]*$"))
            {
                Status("id 不合法：" + id + "（只能小写字母开头，含小写字母/数字/下划线）");
                return;
            }
            var dir = Path.Combine(root, id);
            if (Directory.Exists(dir))
            {
                Status("已存在同名模组：" + id);
                return;
            }

            var name = Prompt("新建模组", "显示名（留空则用 id）", id);
            if (name == null) { return; }
            if (string.IsNullOrWhiteSpace(name)) { name = id; }

            try
            {
                CreateModSkeleton(dir, id, name.Trim());
            }
            catch (Exception ex)
            {
                Status("建立模组失败：" + ex.Message);
                return;
            }

            Reload();
            SelectMod(id);
            Status("已创建模组：" + id + "（" + dir + "）");
        }

        /// <summary>生成最小可用模组的目录结构与清单。</summary>
        private static void CreateModSkeleton(string dir, string id, string displayName)
        {
            foreach (var sub in new[] { "cards", "relics", "keywords", "i18n", "art" })
            {
                Directory.CreateDirectory(Path.Combine(dir, sub));
            }

            var manifest = new JsonObject
            {
                ["$schema"] = "modkit/mod@1",
                ["id"] = id,
                ["name"] = displayName,
                ["version"] = "1.0.0",
                ["author"] = "",
                ["cards"] = new JsonArray(),
                ["relics"] = new JsonArray(),
                ["keywords"] = new JsonArray(),
                ["i18n"] = new JsonArray("i18n/zh-Hans.json"),
            };
            File.WriteAllText(Path.Combine(dir, "mod.json"),
                manifest.ToJsonString(WriteOptions).Replace("\r\n", "\n") + "\n",
                new UTF8Encoding(false));

            var i18n = new JsonObject
            {
                ["$schema"] = "modkit/i18n@1",
                ["language"] = "zh-Hans",
                ["tables"] = new JsonObject(),
            };
            File.WriteAllText(Path.Combine(dir, "i18n", "zh-Hans.json"),
                i18n.ToJsonString(WriteOptions).Replace("\r\n", "\n") + "\n",
                new UTF8Encoding(false));

            var readme = new StringBuilder();
            readme.AppendLine("# " + displayName);
            readme.AppendLine();
            readme.AppendLine("模组 id：`" + id + "`");
            readme.AppendLine();
            readme.AppendLine("目录说明：");
            readme.AppendLine();
            readme.AppendLine("- `cards/`  卡牌 JSON");
            readme.AppendLine("- `relics/` 遗物 JSON");
            readme.AppendLine("- `keywords/` 关键词 JSON");
            readme.AppendLine("- `i18n/zh-Hans.json` 卡名 / 说明等文案（key 用 `<modid>.<条目id>`）");
            readme.AppendLine("- `art/` 卡面与职业球贴图（可选）");
            readme.AppendLine();
            readme.AppendLine("新条目进编辑器后会自动写入 `mod.json` 的清单。");
            File.WriteAllText(Path.Combine(dir, "README.md"), readme.ToString(), new UTF8Encoding(false));
        }

        /// <summary>在左侧列表里选中指定 id 的模组。</summary>
        private void SelectMod(string id)
        {
            foreach (var obj in ModList.Items)
            {
                var me = obj as ModEntry;
                if (me != null && string.Equals(me.Id, id, StringComparison.Ordinal))
                {
                    ModList.SelectedItem = me;
                    return;
                }
            }
        }

        /// <summary>极简输入框：返回 null 表示取消。</summary>
        private static string Prompt(string title, string message, string initial)
        {
            var win = new Window
            {
                Title = title,
                Width = 520,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = Application.Current != null && Application.Current.MainWindow != null
                    ? Application.Current.MainWindow : null,
            };
            var root = new StackPanel { Margin = new Thickness(18) };
            root.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
            });
            var box = new TextBox { Text = initial ?? "", Padding = new Thickness(6), FontSize = 15 };
            root.Children.Add(box);
            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0),
            };
            var ok = new Button { Content = "确定", Width = 96, Height = 32, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancel = new Button { Content = "取消", Width = 96, Height = 32, IsCancel = true };
            bar.Children.Add(ok);
            bar.Children.Add(cancel);
            root.Children.Add(bar);
            win.Content = root;

            string result = null;
            ok.Click += delegate { result = box.Text; win.Close(); };
            win.Loaded += delegate { box.Focus(); box.SelectAll(); };
            win.ShowDialog();
            return result;
        }

        private void OnReloadClick(object sender, RoutedEventArgs e) { Reload(); }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择 DICEOMANCER 游戏根目录（含 Diceomancer_Data 的那层）",
            };
            if (dlg.ShowDialog() != true) { return; }

            var picked = dlg.FolderName;
            // 用户可能选的是 BepInEx 或 DiceomancerMods，统一往上找游戏根
            var root = picked;
            for (int i = 0; i < 3 && root != null; i++)
            {
                if (Directory.Exists(Path.Combine(root, "Diceomancer_Data", "Managed"))) { break; }
                root = Path.GetDirectoryName(root);
            }

            var cfg = StudioConfig.Current;
            cfg.GameRoot = root ?? picked;
            cfg.ModsRoot = Path.Combine(cfg.GameRoot, "BepInEx", "DiceomancerMods");
            cfg.Save();

            ModsRootBox.Text = cfg.ModsRoot;
            Reload();
            Status("已保存游戏目录：" + cfg.GameRoot);
        }

        private void OnModSelected(object sender, RoutedEventArgs e)
        {
            var mod = ModList.SelectedItem as ModEntry;
            if (mod == null) { return; }
            _mod = mod;
            RefreshArtSources();
            LoadItems();
        }

        private void OnKindClick(object sender, RoutedEventArgs e)
        {
            var b = sender as Button;
            if (b == null) { return; }
            var tag = b.Tag as string;
            if (tag == "card") { _kind = Kind.Card; }
            else if (tag == "relic") { _kind = Kind.Relic; }
            else { _kind = Kind.Keyword; }
            UpdateKindButtons();
            LoadItems();
        }

        private void UpdateKindButtons()
        {
            var accent = new SolidColorBrush(Color.FromRgb(0x2E, 0x7A, 0x4F));
            var normal = new SolidColorBrush(Color.FromRgb(0xE8, 0xDF, 0xCB));
            var ink = new SolidColorBrush(Color.FromRgb(0x4A, 0x3A, 0x2E));

            ApplyKindButton(KindCardBtn, Kind.Card, accent, normal, ink);
            ApplyKindButton(KindRelicBtn, Kind.Relic, accent, normal, ink);
            ApplyKindButton(KindKeywordBtn, Kind.Keyword, accent, normal, ink);

            CardForm.Visibility = _kind == Kind.Card ? Visibility.Visible : Visibility.Collapsed;
            RelicForm.Visibility = _kind == Kind.Relic ? Visibility.Visible : Visibility.Collapsed;
            KeywordForm.Visibility = _kind == Kind.Keyword ? Visibility.Visible : Visibility.Collapsed;
            CardPreviewHost.Visibility = _kind == Kind.Card ? Visibility.Visible : Visibility.Collapsed;
            TextPreviewHost.Visibility = _kind == Kind.Card ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ApplyKindButton(Button button, Kind kind, Brush accent, Brush normal, Brush ink)
        {
            bool on = kind == _kind;
            button.Background = on ? accent : normal;
            button.Foreground = on ? Brushes.White : ink;
        }

        private void LoadItems()
        {
            ContentList.Items.Clear();
            _current = null;
            if (_mod == null) { return; }
            var dir = System.IO.Path.Combine(_mod.Dir, DirOf(_kind));
            if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
            foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(x => x, StringComparer.Ordinal))
            {
                try
                {
                    var node = JsonNode.Parse(File.ReadAllText(f, Encoding.UTF8)) as JsonObject;
                    if (node != null) { ContentList.Items.Add(new Item { Path = f, Root = node }); }
                }
                catch (Exception ex) { Status("跳过 " + System.IO.Path.GetFileName(f) + "：" + ex.Message); }
            }
            if (ContentList.Items.Count > 0) { ContentList.SelectedIndex = 0; }
            Status("载入 " + ContentList.Items.Count + " 项（" + DirOf(_kind) + "）");
        }

        private void OnItemSelected(object sender, RoutedEventArgs e)
        {
            var item = ContentList.SelectedItem as Item;
            if (item == null) { return; }
            _current = item;
            FillForm(item);
            RefreshPreview();
        }

        private void FillForm(Item item)
        {
            var r = item.Root;
            var zh = (string)r["name"]?["zh-Hans"] ?? "";
            var en = (string)r["name"]?["en"] ?? "";

            if (_kind == Kind.Card)
            {
                CardIdBox.Text = (string)r["id"] ?? "";
                CardNameZhBox.Text = zh;
                CardNameEnBox.Text = en;
                SelectByKey(RarityBox, (string)r["rarity"] ?? "Common");
                ColorsBox.Text = JoinArray(r["colors"]);
                SelectByKey(ResolveBox, (string)r["resolve"] ?? "OnCast");
                CostBox.Text = FormatCosts(r["cost"]);
                CardArtBox.Text = (string)r["artTemplate"] ?? "";
                CardArtFileBox.Text = (string)r["art"] ?? "";
                FlavorBox.Text = "";
                KeywordsBox.Text = Pretty(r["keywords"]);
                RefreshKeywordKindSource();
                RefreshKeywordList();
                EffectsBox.Text = Pretty(r["effects"]);
                UpgradeBox.Text = Pretty(r["upgrade"]);
                RefreshEffectList(EffectsBox, EffectList);
                RefreshArtSources();
            }
            else if (_kind == Kind.Relic)
            {
                RelicIdBox.Text = (string)r["id"] ?? "";
                RelicNameZhBox.Text = zh;
                RelicNameEnBox.Text = en;
                RelicArtBox.Text = (string)r["artTemplate"] ?? "";
                var triggerId = (string)r["trigger"] ?? "";
            var match = (TriggerBox.ItemsSource as IEnumerable<Choice>);
            if (match != null)
            {
                var hit = match.FirstOrDefault(c => string.Equals(c.Key, triggerId, StringComparison.Ordinal));
                if (hit != null) { TriggerBox.SelectedItem = hit; } else { TriggerBox.Text = triggerId; }
            }
            else { TriggerBox.Text = triggerId; }
                AddToPoolBox.IsChecked = r["addToPool"] == null || (bool)r["addToPool"];
                RelicEffectsBox.Text = Pretty(r["effects"]);
                RefreshEffectList(RelicEffectsBox, RelicEffectList);
                RefreshArtSources();
            }
            else
            {
                KwIdBox.Text = (string)r["id"] ?? "";
                KwNameZhBox.Text = zh;
                KwNameEnBox.Text = en;
                KwDescZhBox.Text = (string)r["description"]?["zh-Hans"] ?? "";
                KwDescEnBox.Text = (string)r["description"]?["en"] ?? "";
                ShowKeywordClass((string)r["id"] ?? "");
            }
            HintText.Text = "";
        }

        private bool ApplyForm()
        {
            if (_current == null) { Status("没有选中内容"); return false; }
            var r = _current.Root;

            if (_kind == Kind.Card)
            {
                var id = CardIdBox.Text.Trim();
                if (id.Length == 0) { Status("id 不能为空"); return false; }
                r["id"] = id;
                r["name"] = NameNode(CardNameZhBox.Text, CardNameEnBox.Text);
                var pickedRarity = RarityBox.SelectedItem as Choice;
                r["rarity"] = pickedRarity != null ? pickedRarity.Key : (RarityBox.Text ?? "Common");
                r["colors"] = ParseList(ColorsBox.Text);
                var pickedResolve = ResolveBox.SelectedItem as Choice;
                r["resolve"] = pickedResolve != null ? pickedResolve.Key : (ResolveBox.Text ?? "OnCast");
                string costError;
                var costNode = ParseCosts(CostBox.Text, out costError);
                if (costError != null) { Status("费用：" + costError); return false; }
                r["cost"] = costNode;
                var art = CardArtBox.Text.Trim();
                if (art.Length == 0)
                {
                    r.Remove("artTemplate");
                }
                else
                {
                    // artTemplate 只能是原版卡名：填错会被平台装配阶段拒绝，整张卡不加载（日志表现为“找不到模板卡”）。
                    var vanilla = VanillaCardNames;
                    if (vanilla.Length > 0 && !vanilla.Contains(art))
                    {
                        Status("artTemplate '" + art + "' 不是原版卡名 —— 游戏会装配失败并整张跳过，已阻止保存");
                        return false;
                    }
                    r["artTemplate"] = art;
                }
                var artFile = CardArtFileBox.Text.Trim();
                if (artFile.Length == 0) { r.Remove("art"); } else { r["art"] = artFile; }
                if (!ApplyJson(KeywordsBox, r, "keywords", true)) { return false; }
                if (!ApplyJson(EffectsBox, r, "effects", true)) { return false; }
                return ApplyJson(UpgradeBox, r, "upgrade", false);
            }

            if (_kind == Kind.Relic)
            {
                var id = RelicIdBox.Text.Trim();
                if (id.Length == 0) { Status("id 不能为空"); return false; }
                r["id"] = id;
                r["name"] = NameNode(RelicNameZhBox.Text, RelicNameEnBox.Text);
                var art = RelicArtBox.Text.Trim();
                if (art.Length == 0) { r.Remove("artTemplate"); } else { r["artTemplate"] = art; }
                var pickedTrigger = TriggerBox.SelectedItem as Choice;
                var trig = pickedTrigger != null ? pickedTrigger.Key : TriggerBox.Text.Trim();
                if (trig.Length == 0) { r.Remove("trigger"); } else { r["trigger"] = trig; }
                r["addToPool"] = AddToPoolBox.IsChecked == true;
                return ApplyJson(RelicEffectsBox, r, "effects", true);
            }

            var kid = KwIdBox.Text.Trim();
            if (kid.Length == 0) { Status("id 不能为空"); return false; }
            r["id"] = kid;
            r["name"] = NameNode(KwNameZhBox.Text, KwNameEnBox.Text);
            var dz = KwDescZhBox.Text.Trim();
            var de = KwDescEnBox.Text.Trim();
            if (dz.Length == 0 && de.Length == 0) { r.Remove("description"); }
            else { r["description"] = new JsonObject { ["zh-Hans"] = dz, ["en"] = de }; }
            return true;
        }

        private static JsonObject NameNode(string zh, string en)
        {
            return new JsonObject { ["zh-Hans"] = zh.Trim(), ["en"] = en.Trim() };
        }

        private static string JoinArray(JsonNode node)
        {
            var arr = node as JsonArray;
            if (arr == null) { return ""; }
            return string.Join(",", arr.Select(x => x == null ? "" : x.ToString()));
        }

        private static string Pretty(JsonNode node)
        {
            return node == null ? "" : node.ToJsonString(WriteOptions);
        }

        private bool ApplyJson(TextBox box, JsonObject r, string key, bool expectArray)
        {
            var text = box.Text.Trim();
            if (text.Length == 0) { r.Remove(key); return true; }
            try
            {
                var node = JsonNode.Parse(text);
                if (expectArray && !(node is JsonArray)) { Status(key + " 必须是 JSON 数组"); return false; }
                r[key] = node;
                return true;
            }
            catch (Exception ex)
            {
                Status(key + " 不是合法 JSON：" + ex.Message);
                return false;
            }
        }

        private static JsonArray ParseList(string text)
        {
            var arr = new JsonArray();
            foreach (var s in text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)) { arr.Add(s); }
            return arr;
        }

        private static readonly string[] CostColors = { "Red", "Green", "Blue", "Purple", "Void", "Black" };

        /// <summary>
        /// 费用槽解析。规则（与平台日志 cost-combos 的说明一致）：
        ///   · 逗号分隔「槽」，每个槽默认是单一颜色；
        ///   · 方括号表示**融合槽**（任一颜色都可支付），平台里写成嵌套数组，如 [["Green","Void"]]；
        ///   · 颜色只接受 Red / Green / Blue / Purple / Void / Black。
        /// 例："Green, [Green, Void]" ⇒ ["Green", ["Green","Void"]]
        /// </summary>
        private static JsonArray ParseCosts(string text, out string error)
        {
            error = null;
            var slots = new JsonArray();
            var src = (text ?? "").Replace("，", ",");

            var cur = new System.Text.StringBuilder();
            var slotText = new List<string>();
            var slotHybrid = new List<bool>();
            bool inBracket = false;
            bool curHybrid = false;

            Action flush = delegate
            {
                var t = cur.ToString().Trim();
                if (t.Length > 0) { slotText.Add(t); slotHybrid.Add(curHybrid); }
                cur.Clear();
                curHybrid = false;
            };

            foreach (var ch in src)
            {
                if (ch == '[') { inBracket = true; curHybrid = true; continue; }
                if (ch == ']') { inBracket = false; continue; }
                if (ch == ',' && !inBracket) { flush(); continue; }
                cur.Append(ch);
            }
            flush();

            for (int i = 0; i < slotText.Count; i++)
            {
                var colors = slotText[i].Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                for (int c = 0; c < colors.Count; c++)
                {
                    var hit = CostColors.FirstOrDefault(x => string.Equals(x, colors[c], StringComparison.OrdinalIgnoreCase));
                    if (hit == null)
                    {
                        error = "费用槽 " + (i + 1) + " 里的 \"" + colors[c] + "\" 不是合法颜色（Red / Green / Blue / Purple / Void / Black）";
                        return slots;
                    }
                    colors[c] = hit;
                }

                if (slotHybrid[i] && colors.Count > 1)
                {
                    var hy = new JsonArray();
                    foreach (var c in colors) { hy.Add(c); }
                    slots.Add(hy);
                }
                else
                {
                    int n;
                    if (colors.Count == 1 && int.TryParse(colors[0], out n)) { slots.Add(n); }
                    else if (colors.Count == 1) { slots.Add(colors[0]); }
                    else { error = "费用槽 " + (i + 1) + " 有多个颜色但没有用方括号包起来"; return slots; }
                }
            }
            return slots;
        }

        /// <summary>把费用序列显示回文本框：融合槽显示成 [Green,Void]。</summary>
        private static string FormatCosts(JsonNode node)
        {
            var arr = node as JsonArray;
            if (arr == null) { return ""; }
            var parts = new List<string>();
            foreach (var slot in arr)
            {
                var inner = slot as JsonArray;
                if (inner == null) { parts.Add(slot == null ? "" : slot.ToString()); }
                else { parts.Add("[" + string.Join(",", inner.Select(x => x == null ? "" : x.ToString())) + "]"); }
            }
            return string.Join(", ", parts);
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            if (_current == null) { Status("没有选中内容"); return; }
            if (!ApplyForm()) { return; }

            var r = _current.Root;
            if (r["$schema"] == null)
            {
                var rebuilt = new JsonObject { ["$schema"] = SchemaOf(_kind) };
                foreach (var kv in r.ToList())
                {
                    if (kv.Key != "$schema") { rebuilt[kv.Key] = kv.Value == null ? null : kv.Value.DeepClone(); }
                }
                r = rebuilt;
                _current.Root = r;
            }

            try
            {
                File.WriteAllText(_current.Path, r.ToJsonString(WriteOptions).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
                var i18n = SyncI18n(r);
                Status("已保存 " + System.IO.Path.GetFileName(_current.Path) + (i18n ? "，i18n 已同步" : "，i18n 跳过"));
                RefreshListKeepingSelection();
                RefreshPreview();
            }
            catch (Exception ex) { Status("保存失败：" + ex.Message); }
        }

        private void RefreshListKeepingSelection()
        {
            var idx = ContentList.SelectedIndex;
            var items = ContentList.Items.Cast<Item>().ToList();
            ContentList.Items.Clear();
            foreach (var it in items) { ContentList.Items.Add(it); }
            if (idx >= 0 && idx < ContentList.Items.Count) { ContentList.SelectedIndex = idx; }
        }

        /// <summary>
        /// 把显示名写进 mod 的 i18n 表。游戏侧读取的是本地化表，键的约定为 "modId.内容id"
        /// （与现有 manual_demo 数据一致），表名对应 CardName / Relic / KeyWord。
        /// </summary>
        private bool SyncI18n(JsonObject r)
        {
            if (_mod == null) { return false; }
            var id = (string)r["id"] ?? "";
            if (id.Length == 0) { return false; }
            var zh = (string)r["name"]?["zh-Hans"] ?? "";
            var en = (string)r["name"]?["en"] ?? "";
            var table = _kind == Kind.Card ? "CardName" : _kind == Kind.Relic ? "Relic" : "KeyWord";
            var key = _mod.Id + "." + id;

            var ok = WriteTable("zh-Hans", table, key, zh);
            if (!string.IsNullOrEmpty(en)) { ok |= WriteTable("en", table, key, en); }

            if (_kind == Kind.Card)
            {
                var flavor = FlavorBox.Text.Trim();
                if (flavor.Length > 0) { ok |= WriteTable("zh-Hans", "FlavorText", key, flavor); }
            }
            return ok;
        }

        private bool WriteTable(string language, string table, string key, string value)
        {
            try
            {
                var dir = System.IO.Path.Combine(_mod.Dir, "i18n");
                Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, language + ".json");

                JsonObject root;
                if (File.Exists(path))
                {
                    root = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject;
                    if (root == null) { root = new JsonObject(); }
                }
                else
                {
                    root = new JsonObject
                    {
                        ["$schema"] = "modkit/i18n@1",
                        ["language"] = language,
                        ["tables"] = new JsonObject(),
                    };
                }

                var tables = root["tables"] as JsonObject;
                if (tables == null) { tables = new JsonObject(); root["tables"] = tables; }

                var tb = tables[table] as JsonObject;
                if (tb == null) { tb = new JsonObject(); tables[table] = tb; }

                tb[key] = value;
                File.WriteAllText(path, root.ToJsonString(WriteOptions).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                Status("i18n 写入失败（" + table + "）：" + ex.Message);
                return false;
            }
        }

        private void OnPreviewClick(object sender, RoutedEventArgs e)
        {
            ApplyForm();
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            if (_current == null) { return; }
            var r = _current.Root;

            if (_kind == Kind.Card)
            {
                PreviewName.Text = (string)r["name"]?["zh-Hans"] ?? (string)r["id"] ?? "";
                PreviewCost.Text = FormatCosts(r["cost"]);
                PreviewEffects.Text = DescribeEffects(r["effects"]);
                PreviewKeywords.Text = DescribeKeywords(r["keywords"]);
                PreviewFlavor.Text = FlavorBox.Text.Trim();
                PreviewArt.Source = LoadCardArt(r);
                return;
            }

            PreviewRelicIcon.Source = LoadArt((string)r["artTemplate"]);
            PreviewTextTitle.Text = (string)r["name"]?["zh-Hans"] ?? (string)r["id"] ?? "";
            if (_kind == Kind.Relic)
            {
                var trig = (string)r["trigger"];
                PreviewTextBody.Text = "触发：" + (string.IsNullOrEmpty(trig) ? "（未设置）" : trig) + "\n\n" + DescribeEffects(r["effects"]);
            }
            else
            {
                var desc = (string)r["description"]?["zh-Hans"];
                PreviewTextBody.Text = string.IsNullOrEmpty(desc) ? "（未填说明）" : desc;
            }
        }

        /// <summary>卡面预览：优先 mod 自带的 art 图，其次按 artTemplate 名字找 mod 图。</summary>
        private BitmapImage LoadCardArt(JsonObject r)
        {
            var rel = (string)r["art"];
            if (!string.IsNullOrEmpty(rel))
            {
                var modDir = _mod == null ? "" : _mod.Dir;
                var full = System.IO.Path.Combine(modDir, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
                var bmp = LoadImageFile(full);
                if (bmp != null) { return bmp; }
            }
            return LoadArt((string)r["artTemplate"]);
        }

        private static BitmapImage LoadImageFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) { return null; }
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                return bmp;
            }
            catch { return null; }
        }

        private BitmapImage LoadArt(string template)
        {
            if (string.IsNullOrEmpty(template)) { return null; }
            var modDir = _mod == null ? "" : _mod.Dir;
            var candidates = new[]
            {
                System.IO.Path.Combine(modDir, "art", template + ".png"),
                System.IO.Path.Combine(modDir, "art", "card", template + ".png"),
            };
            foreach (var p in candidates)
            {
                try
                {
                    if (!File.Exists(p)) { continue; }
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(p);
                    bmp.EndInit();
                    return bmp;
                }
                catch { }
            }
            return null;
        }

        private static readonly Dictionary<string, string> EffectWords = new Dictionary<string, string>
        {
            ["draw"] = "抽{0}张牌",
            ["block"] = "获得{0}点格挡",
            ["damage"] = "造成{0}点伤害",
            ["heal"] = "恢复{0}点生命",
            ["gain_ability"] = "获得能力 {1} × {0}",
        };

        private static string DescribeEffects(JsonNode node)
        {
            var arr = node as JsonArray;
            if (arr == null) { return "（无效果）"; }
            var lines = new List<string>();
            foreach (var e in arr.OfType<JsonObject>())
            {
                var builtin = (string)e["builtin"] ?? (string)e["name"];
                var amounts = e["amounts"] is JsonArray a ? string.Join("/", a.Select(x => x == null ? "" : x.ToString())) : "";
                var name = (string)e["name"] ?? "";
                string fmt;
                if (builtin != null && EffectWords.TryGetValue(builtin, out fmt))
                {
                    lines.Add(fmt.Contains("{1}") ? string.Format(fmt, amounts, name) : string.Format(fmt, amounts));
                }
                else if (builtin != null)
                {
                    lines.Add(builtin + (amounts.Length > 0 ? " " + amounts : ""));
                }
            }
            return lines.Count == 0 ? "（无效果）" : string.Join("\n", lines.ToArray());
        }

        private static string DescribeKeywords(JsonNode node)
        {
            var arr = node as JsonArray;
            if (arr == null) { return ""; }
            var parts = new List<string>();
            foreach (var k in arr.OfType<JsonObject>())
            {
                var n = (string)k["name"];
                var num = k["number"] == null ? "" : k["number"].ToString();
                parts.Add(string.IsNullOrEmpty(num) ? n : n + " " + num);
            }
            return parts.Count == 0 ? "" : "关键词：" + string.Join(" · ", parts.ToArray());
        }

        private void OnNewClick(object sender, RoutedEventArgs e)
        {
            if (_mod == null) { Status("先选一个模组"); return; }
            var dir = System.IO.Path.Combine(_mod.Dir, DirOf(_kind));
            Directory.CreateDirectory(dir);

            var baseId = _kind == Kind.Card ? "new_card" : _kind == Kind.Relic ? "new_relic" : "new_keyword";
            var id = baseId;
            var n = 1;
            while (File.Exists(System.IO.Path.Combine(dir, id + ".json"))) { n++; id = baseId + n; }

            JsonObject root;
            if (_kind == Kind.Card)
            {
                root = new JsonObject
                {
                    ["$schema"] = SchemaOf(_kind),
                    ["id"] = id,
                    ["name"] = new JsonObject { ["zh-Hans"] = "新卡牌", ["en"] = "New Card" },
                    ["cost"] = new JsonArray("Void"),
                    ["rarity"] = "Common",
                    ["colors"] = new JsonArray("Green"),
                    ["resolve"] = "OnCast",
                    ["effects"] = new JsonArray(new JsonObject { ["builtin"] = "block", ["amounts"] = new JsonArray(3) }),
                };
            }
            else if (_kind == Kind.Relic)
            {
                root = new JsonObject
                {
                    ["$schema"] = SchemaOf(_kind),
                    ["id"] = id,
                    ["name"] = new JsonObject { ["zh-Hans"] = "新遗物", ["en"] = "New Relic" },
                    ["artTemplate"] = "BottleOfWater",
                    ["trigger"] = "BattleStart",
                    ["effects"] = new JsonArray(new JsonObject { ["builtin"] = "block", ["amounts"] = new JsonArray(4) }),
                    ["addToPool"] = true,
                };
            }
            else
            {
                root = new JsonObject
                {
                    ["$schema"] = SchemaOf(_kind),
                    ["id"] = id,
                    ["name"] = new JsonObject { ["zh-Hans"] = "新关键词", ["en"] = "New Keyword" },
                    ["description"] = new JsonObject { ["zh-Hans"] = "", ["en"] = "" },
                };
            }

            var path = System.IO.Path.Combine(dir, id + ".json");
            File.WriteAllText(path, root.ToJsonString(WriteOptions).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));

            LoadItems();
            foreach (Item it in ContentList.Items)
            {
                if (System.IO.Path.GetFileName(it.Path) == id + ".json") { ContentList.SelectedItem = it; break; }
            }
            Status("已新建 " + id + ".json");
        }

        private void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (_current == null) { Status("没有选中内容"); return; }
            var name = System.IO.Path.GetFileName(_current.Path);
            var answer = MessageBox.Show("删除 " + name + "？（不会自动备份；i18n 里的键不会一并删除）", "确认删除",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK) { return; }
            try
            {
                File.Delete(_current.Path);
                LoadItems();
                Status("已删除 " + name);
            }
            catch (Exception ex) { Status("删除失败：" + ex.Message); }
        }

        /// <summary>扫描本 mod 的 art/ 目录，供美术模板下拉选择（不含扩展名）。</summary>
        private void RefreshArtSources()
        {
            if (_mod == null) { return; }
            var dir = System.IO.Path.Combine(_mod.Dir, "art");
            string[] names;
            try
            {
                names = Directory.Exists(dir)
                    ? Directory.GetFiles(dir, "*.png").Select(x => System.IO.Path.GetFileNameWithoutExtension(x)).OrderBy(x => x, StringComparer.Ordinal).ToArray()
                    : new string[0];
            }
            catch { names = new string[0]; }

            var keepTemplate = CardArtBox.Text;
            var keepArtFile = CardArtFileBox.Text;
            var keepRelic = RelicArtBox.Text;

            CardArtBox.ItemsSource = VanillaCardNames;
            CardArtFileBox.ItemsSource = names;
            // 遗物的 artTemplate 同样是"原版遗物名"，不接受 mod 图文件名，因此保持自由输入

            CardArtBox.Text = keepTemplate;
            CardArtFileBox.Text = keepArtFile;
            RelicArtBox.Text = keepRelic;
            Status("原版卡名 " + VanillaCardNames.Length + " 个｜mod 图 " + names.Length + " 个");
        }

        private void OnRefreshArtClick(object sender, RoutedEventArgs e) { RefreshArtSources(); }

        private void RefreshKeywordKindSource()
        {
            var set = new SortedSet<string>(VanillaKeywordNames, StringComparer.Ordinal);
            if (_mod != null)
            {
                var dir = System.IO.Path.Combine(_mod.Dir, "keywords");
                try
                {
                    if (Directory.Exists(dir))
                    {
                        foreach (var f in Directory.GetFiles(dir, "*.json"))
                        {
                            var id = System.IO.Path.GetFileNameWithoutExtension(f);
                            if (!string.IsNullOrEmpty(id)) { set.Add(id); }
                        }
                    }
                }
                catch { }
            }
            var zh = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_mod != null)
            {
                var kd = System.IO.Path.Combine(_mod.Dir, "keywords");
                try
                {
                    if (Directory.Exists(kd))
                    {
                        foreach (var f in Directory.GetFiles(kd, "*.json"))
                        {
                            try
                            {
                                var node = JsonNode.Parse(File.ReadAllText(f, Encoding.UTF8)) as JsonObject;
                                var id = node == null ? null : (string)node["id"];
                                var name = node == null ? null : (string)node["name"]?["zh-Hans"];
                                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name)) { zh[id] = name; }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            LoadKeywordDictionary();
            var choices = new List<Choice>();
            foreach (var id in set)
            {
                if (_dangerKeywords.Contains(id)) { continue; }   // 危险名单：挂上去会崩游戏

                string cn;
                if (!zh.TryGetValue(id, out cn))
                {
                    KeywordInfo info;
                    cn = _keywordInfo.TryGetValue(id, out info) ? info.Zh : null;
                }
                var takesNum = false;
                KeywordInfo ki;
                if (_keywordInfo.TryGetValue(id, out ki)) { takesNum = ki.TakesNumber; }

                choices.Add(new Choice
                {
                    Key = id,
                    Caption = (string.IsNullOrEmpty(cn) ? id : cn + "（" + id + "）")
                              + (takesNum ? "  ·带数值" : ""),
                });
            }
            KeywordKindBox.ItemsSource = choices;
        }

        private static List<JsonObject> ParseKeywordArray(string text)
        {
            var list = new List<JsonObject>();
            var t = (text ?? "").Trim();
            if (t.Length == 0) { return list; }
            try
            {
                var arr = JsonNode.Parse(t) as JsonArray;
                if (arr != null)
                {
                    foreach (var n in arr) { var o = n as JsonObject; if (o != null) { list.Add(o); } }
                }
            }
            catch { }
            return list;
        }

        private void RefreshKeywordList()
        {
            KeywordList.Items.Clear();
            foreach (var k in ParseKeywordArray(KeywordsBox.Text))
            {
                var name = (string)k["name"] ?? "";
                var num = k["number"] == null ? "" : k["number"].ToString();
                KeywordList.Items.Add(string.IsNullOrEmpty(num) ? name : name + "  " + num);
            }
        }

        private void OnKeywordKindChanged(object sender, SelectionChangedEventArgs e)
        {
            var picked = KeywordKindBox.SelectedItem as Choice;
            if (picked == null) { return; }
            LoadKeywordDictionary();
            KeywordInfo info;
            bool takesNum = _keywordInfo.TryGetValue(picked.Key, out info) && info.TakesNumber;
            KeywordNumberBox.IsEnabled = takesNum;
            if (!takesNum) { KeywordNumberBox.Text = ""; }
            KeywordNumberBox.ToolTip = takesNum ? "该关键词支持数值（卡面显示为 [关键词 N]）" : "该关键词不吃数值，填写会被忽略";
        }

        /// <summary>
        /// 在卡牌编辑里就地新建一个自定义关键词（只要标签、不要行为时足够用）。
        /// 建好会切到「关键词」页并选中它，方便填名字和说明。
        /// </summary>
        private void OnCreateKeywordClick(object sender, RoutedEventArgs e)
        {
            if (_mod == null) { Status("先选一个模组"); return; }
            var dir = System.IO.Path.Combine(_mod.Dir, "keywords");
            Directory.CreateDirectory(dir);

            var baseId = "new_keyword";
            var id = baseId;
            var n = 1;
            while (File.Exists(System.IO.Path.Combine(dir, id + ".json"))) { n++; id = baseId + n; }

            var root = new JsonObject
            {
                ["$schema"] = "modkit/keyword@1",
                ["id"] = id,
                ["name"] = new JsonObject { ["zh-Hans"] = "新关键词", ["en"] = "New Keyword" },
                ["description"] = new JsonObject { ["zh-Hans"] = "", ["en"] = "" },
            };
            File.WriteAllText(
                System.IO.Path.Combine(dir, id + ".json"),
                root.ToJsonString(WriteOptions).Replace("\r\n", "\n") + "\n",
                new UTF8Encoding(false));

            // 切到关键词页并选中刚建的
            _kind = Kind.Keyword;
            UpdateKindButtons();
            LoadItems();
            foreach (Item it in ContentList.Items)
            {
                if (System.IO.Path.GetFileName(it.Path) == id + ".json") { ContentList.SelectedItem = it; break; }
            }
            RefreshKeywordKindSource();
            Status("已新建关键词 " + id + ".json —— 请填写中文名与说明；若要它有行为，再用「生成 C# 行为类模板」");
        }

        /// <summary>
        /// 生成自定义关键词的 C# 行为类骨架（平台只负责标签，行为必须由 CardKeyword 子类实现）。
        /// 文件写到 <mod>/src/，随后用 build-mod 脚本编译成 dll。
        /// </summary>
        private void OnGenerateKeywordClassClick(object sender, RoutedEventArgs e)
        {
            if (_mod == null) { Status("先选一个模组"); return; }
            if (_current == null) { Status("先在关键词列表里选一个"); return; }
            var id = (string)_current.Root["id"] ?? "";
            if (string.IsNullOrEmpty(id)) { Status("关键词 id 为空"); return; }

            var classSuffix = string.Concat(id.Split(new[] { '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries)
                                             .Select(x => x.Length == 0 ? "" : char.ToUpperInvariant(x[0]) + x.Substring(1)));
            if (classSuffix.Length == 0) { Status("关键词 id 不能全是符号"); return; }
            var className = "CardKeyword" + classSuffix;

            var srcDir = System.IO.Path.Combine(_mod.Dir, "src");
            Directory.CreateDirectory(srcDir);
            var file = System.IO.Path.Combine(srcDir, className + ".cs");
            if (File.Exists(file))
            {
                if (MessageBox.Show(classSuffix + " 的行为类已存在：" + System.IO.Path.GetFileName(file) + "\n\n要覆盖吗？",
                        "已存在", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) { return; }
            }

            var zh = (string)_current.Root["name"]?["zh-Hans"] ?? id;
            var code = "using Cysharp.Threading.Tasks;\n" +
                       "using Keyword;\n" +
                       "using Card;\n\n" +
                       "/// <summary>\n" +
                       "/// 关键词「" + zh + "」（id: " + id + "）的行为实现。\n" +
                       "/// 平台只负责把关键词标签放到卡上，真正的行为由游戏在对应时机调用这里的方法。\n" +
                       "/// </summary>\n" +
                       "public sealed class " + className + " : CardKeyword\n" +
                       "{\n" +
                       "    // 卡牌结算完成之后 —— 最常见的时机；不需要就删掉这个重写\n" +
                       "    public override UniTask CardResolvePhaseAfterEffect(CardBase card)\n" +
                       "    {\n" +
                       "        // TODO: 在这里写效果，例如：\n" +
                       "        // CharacterPlayer player = CharacterPlayer.Instance;\n" +
                       "        // if (player != null) { player.ApplyBlock(3); }\n" +
                       "        return UniTask.CompletedTask;\n" +
                       "    }\n" +
                       "}\n";
            File.WriteAllText(file, code, new UTF8Encoding(false));

            KeywordClassHint.Text = "已生成 src/" + className + ".cs —— 编译后再进游戏";
            Status("已生成行为类模板 " + className + ".cs");
        }

        private void OnAddKeywordClick(object sender, RoutedEventArgs e)
        {
            var pickedKw = KeywordKindBox.SelectedItem as Choice;
            var name = pickedKw != null ? pickedKw.Key : KeywordKindBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) { Status("先选择关键词"); return; }
            var known = VanillaKeywordNames.Contains(name);
            var modKnows = false;
            if (_mod != null)
            {
                modKnows = File.Exists(System.IO.Path.Combine(_mod.Dir, "keywords", name + ".json"));
            }
            if (!known && !modKnows) { Status("警告：" + name + " 不是已知关键词，保存后游戏可能装配失败"); }

            var list = ParseKeywordArray(KeywordsBox.Text);
            var node = new JsonObject { ["name"] = name };
            int number;
            var numText = KeywordNumberBox.Text.Trim();
            if (numText.Length > 0)
            {
                if (!int.TryParse(numText, out number)) { Status("参数必须是整数"); return; }
                node["number"] = number;
            }
            list.Add(node);
            KeywordsBox.Text = Pretty(new JsonArray(list.Select(x => (JsonNode)JsonNode.Parse(x.ToJsonString())).ToArray()));
            RefreshKeywordList();
            KeywordNumberBox.Text = "";
            Status("已添加关键词 " + name);
        }

        private void OnRemoveKeywordClick(object sender, RoutedEventArgs e)
        {
            var idx = KeywordList.SelectedIndex;
            if (idx < 0) { Status("先在列表里选一个关键词"); return; }
            var list = ParseKeywordArray(KeywordsBox.Text);
            if (idx >= list.Count) { return; }
            list.RemoveAt(idx);
            KeywordsBox.Text = list.Count == 0 ? "" : Pretty(new JsonArray(list.Select(x => (JsonNode)JsonNode.Parse(x.ToJsonString())).ToArray()));
            RefreshKeywordList();
            Status("已删除该关键词");
        }

        private static List<Choice> BuildEffectChoices()
        {
            LoadEffectDictionary();
            var list = new List<Choice>();
            foreach (var key in BuiltinNames)
            {
                EffectInfo info;
                _effectInfo.TryGetValue(key, out info);
                if (info != null && !info.Usable) { continue; }   // 文档标 🚫（如 upgrade_card）不提供

                string cn = info != null && !string.IsNullOrEmpty(info.Zh) ? info.Zh : null;
                if (string.IsNullOrEmpty(cn)) { EffectLabel.TryGetValue(key, out cn); }
                var needsExtra = info != null && !string.IsNullOrEmpty(info.Extra);
                list.Add(new Choice
                {
                    Key = key,
                    Caption = (string.IsNullOrEmpty(cn) ? key : cn + "（" + key + "）")
                              + (needsExtra ? "  ·" + ExtraLabel(info.Extra) : ""),
                });
            }
            return list;
        }

        private static string ExtraLabel(string extra)
        {
            switch (extra)
            {
                case "name": return "需名称";
                case "mode": return "需模式";
                case "color": return "需颜色";
                case "pool": return "需卡池";
                case "require": return "需条件";
                case "setter": return "需改造";
                default: return "";
            }
        }

        private static readonly Dictionary<string, string> EffectLabel = new Dictionary<string, string>
        {
            ["deal_damage"] = "造成伤害",
            ["block"] = "获得格挡",
            ["draw"] = "抽牌",
            ["heal"] = "治疗",
            ["lose_life"] = "失去生命",
            ["gain_mana"] = "获得法力",
            ["gain_ability"] = "获得能力",
            ["apply_state"] = "施加状态",
            ["clean_mana"] = "清空法力",
            ["set_max_mana"] = "设置法力上限",
            ["end_turn"] = "结束回合",
            ["discard_random"] = "随机弃牌",
            ["discard_hand"] = "弃掉手牌",
            ["discard_one_random"] = "随机弃 1 张",
            ["add_card"] = "生成卡牌",
            ["add_random_card"] = "随机生成卡牌",
            ["add_temporary_card"] = "生成临时卡",
            ["upgrade_card"] = "升级卡牌",
            ["seek_card"] = "检索卡牌",
            ["improvise_card"] = "即兴创作",
            ["change_max_health"] = "改变生命上限",
            ["apply_prim_ability"] = "施加主要能力",
            ["apply_util_ability"] = "施加辅助能力",
            ["number_plus_sub"] = "数值增减",
            ["recover"] = "恢复",
            ["set_number"] = "设置数值",
        };

        private static string EffectDisplay(JsonObject e)
        {
            var builtin = (string)e["builtin"] ?? "";
            var amounts = e["amounts"] is JsonArray a ? string.Join(", ", a.Select(x => x == null ? "" : x.ToString())) : "";
            string label;
            EffectLabel.TryGetValue(builtin, out label);
            if (label == null) { label = ""; }
            var extraName = (string)e["name"];
            var extraMode = (string)e["mode"];
            var extraColor = (string)e["color"];
            var extraTarget = (string)e["target"];
            var extraText = !string.IsNullOrEmpty(extraName) ? extraName
                : !string.IsNullOrEmpty(extraMode) ? ((string.IsNullOrEmpty(extraTarget) ? "" : "→") + extraMode + (string.IsNullOrEmpty(extraTarget) ? "" : "@" + extraTarget))
                : extraColor;
            return (label.Length > 0 ? label + "  (" + builtin + ")" : builtin)
                + (string.IsNullOrEmpty(extraText) ? "" : "  [" + extraText + "]")
                + (amounts.Length > 0 ? "  →  " + amounts : "");
        }

        private void RefreshEffectList(TextBox box, ListBox list)
        {
            list.Items.Clear();
            var arr = ParseEffectArray(box.Text);
            foreach (var e in arr) { list.Items.Add(EffectDisplay(e)); }
            box.Text = Pretty(new JsonArray(arr.Select(x => (JsonNode)JsonNode.Parse(x.ToJsonString())).ToArray()));
        }

        private static List<JsonObject> ParseEffectArray(string text)
        {
            var list = new List<JsonObject>();
            var t = (text ?? "").Trim();
            if (t.Length == 0) { return list; }
            try
            {
                var arr = JsonNode.Parse(t) as JsonArray;
                if (arr == null) { return list; }
                foreach (var n in arr) { var o = n as JsonObject; if (o != null) { list.Add(o); } }
            }
            catch { }
            return list;
        }

        private static JsonObject NewEffect(string builtin, string amountsText)
        {
            var arr = new JsonArray();
            foreach (var raw in (amountsText ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                int n;
                if (int.TryParse(raw, out n)) { arr.Add(n); } else { arr.Add(raw); }
            }
            return new JsonObject { ["builtin"] = builtin, ["amounts"] = arr };
        }

        private void AddEffect(TextBox box, ComboBox kind, TextBox amounts, ComboBox target, ComboBox target2)
        {
            var picked = kind.SelectedItem as Choice;
            var builtin = picked != null ? picked.Key : kind.Text.Trim();
            if (string.IsNullOrEmpty(builtin)) { Status("先选择效果"); return; }

            LoadEffectDictionary();
            EffectInfo info;
            _effectInfo.TryGetValue(builtin, out info);
            var extra = info == null ? "" : info.Extra;
            var targetText = target == null ? "" : (target.SelectedItem as Choice) != null
                ? ((Choice)target.SelectedItem).Key
                : target.Text.Trim();

            if ((extra == "name" || extra == "mode" || extra == "color") && targetText.Length == 0)
            {
                Status(builtin + " 必须填写「" + ExtraLabel(extra) + "」");
                return;
            }
            if (extra == "pool" || extra == "require" || extra == "setter")
            {
                Status(builtin + " 需要额外 JSON 配置（" + extra + "），请用下方的「效果 JSON（高级）」写");
                return;
            }

            var node = NewEffect(builtin, amounts.Text);
            if (targetText.Length > 0)
            {
                if (extra == "name") { node["name"] = targetText; }
                else if (extra == "mode") { node["mode"] = targetText; }
                else if (extra == "color") { node["color"] = targetText; }
            }
            if (extra == "mode")
            {
                var t2 = target2 == null ? "" : (target2.SelectedItem != null ? target2.SelectedItem.ToString() : target2.Text.Trim());
                if (string.IsNullOrEmpty(t2)) { t2 = "Opponent"; }
                node["target"] = t2;
            }

            var arr = ParseEffectArray(box.Text);
            arr.Add(node);
            box.Text = Pretty(new JsonArray(arr.Select(x => (JsonNode)JsonNode.Parse(x.ToJsonString())).ToArray()));
            RefreshEffectList(box, box == EffectsBox ? EffectList : RelicEffectList);
            Status("已添加 " + builtin);
        }

        private void RemoveEffect(TextBox box, ListBox list)
        {
            var idx = list.SelectedIndex;
            if (idx < 0) { Status("先在列表里选一条效果"); return; }
            var arr = ParseEffectArray(box.Text);
            if (idx >= arr.Count) { return; }
            arr.RemoveAt(idx);
            box.Text = arr.Count == 0 ? "" : Pretty(new JsonArray(arr.Select(x => (JsonNode)JsonNode.Parse(x.ToJsonString())).ToArray()));
            RefreshEffectList(box, list);
            Status("已删除该效果");
        }

        private static readonly string[] DamageModes = { "Direct", "Indirect", "LoseLife" };

        /// <summary>攻击对象（对应 ModKit.Api.CombatantSelector）。</summary>
        private static readonly string[] CombatantSelectors = { "Self", "Opponent", "AllOpponents", "All", "RandomOpponent" };

        /// <summary>按选中的效果配置该行的"名称/模式/颜色"输入与数值可用性。</summary>
        private void ConfigureEffectRow(Choice picked, TextBlock targetLabel, ComboBox targetBox, TextBlock amountLabel, TextBox amounts,
                                        TextBlock target2Label, ComboBox target2Box,
                                        System.Windows.Controls.Panel targetGroup, System.Windows.Controls.Panel target2Group,
                                        System.Windows.Controls.Panel amountGroup)
        {
            LoadEffectDictionary();
            var show = System.Windows.Visibility.Visible;
            var hide = System.Windows.Visibility.Collapsed;

            if (picked == null)
            {
                if (targetGroup != null) { targetGroup.Visibility = hide; }
                if (target2Group != null) { target2Group.Visibility = hide; }
                return;
            }
            EffectInfo info;
            _effectInfo.TryGetValue(picked.Key, out info);
            var extra = info == null ? "" : info.Extra;

            switch (extra)
            {
                case "name":
                    targetLabel.Text = "名称";
                    targetBox.ItemsSource = AbilityChoices();
                    targetBox.IsEnabled = true;
                    break;
                case "mode":
                    targetLabel.Text = "模式";
                    targetBox.ItemsSource = DamageModes;
                    targetBox.IsEnabled = true;
                    if (target2Box != null)
                    {
                        target2Label.Text = "对象";
                        target2Box.ItemsSource = CombatantSelectors;
                        target2Box.IsEnabled = true;
                        if (string.IsNullOrEmpty(target2Box.Text)) { target2Box.Text = "Opponent"; }
                    }
                    break;
                case "color":
                    targetLabel.Text = "颜色";
                    targetBox.ItemsSource = CostColors;
                    targetBox.IsEnabled = true;
                    break;
                case "pool":
                case "require":
                case "setter":
                    targetLabel.Text = "需高级 JSON";
                    targetBox.ItemsSource = null;
                    targetBox.IsEnabled = false;
                    targetBox.Text = "";
                    break;
                default:
                    targetLabel.Text = "";
                    targetBox.ItemsSource = null;
                    targetBox.IsEnabled = false;
                    targetBox.Text = "";
                    break;
            }
            if (targetGroup != null)
            {
                targetGroup.Visibility = (extra == "name" || extra == "mode" || extra == "color") ? show : hide;
            }
            if (target2Group != null)
            {
                target2Group.Visibility = extra == "mode" ? show : hide;
            }
            if (extra != "mode" && target2Box != null)
            {
                target2Box.IsEnabled = false;
                target2Box.ItemsSource = null;
                target2Box.Text = "";
                if (target2Label != null) { target2Label.Text = ""; }
            }
            if (amountGroup != null)
            {
                amountGroup.Visibility = (info == null || info.NeedsNumber) ? show : show;   // 不吃数值也显示，只是禁用
            }

            var needsNumber = info == null || info.NeedsNumber;
            amounts.IsEnabled = needsNumber;
            if (amountLabel != null)
            {
                amountLabel.Text = needsNumber ? "数值" : "不吃数值";
            }
        }

        private void OnEffectKindChanged(object sender, SelectionChangedEventArgs e)
        {
            ConfigureEffectRow(EffectKindBox.SelectedItem as Choice, EffectTargetLabel, EffectTargetBox, EffectAmountLabel, EffectAmountsBox,
                EffectTarget2Label, EffectTarget2Box, EffectTargetGroup, EffectTarget2Group, EffectAmountGroup);
        }

        private void OnRelicEffectKindChanged(object sender, SelectionChangedEventArgs e)
        {
            ConfigureEffectRow(RelicEffectKindBox.SelectedItem as Choice, RelicEffectTargetLabel, RelicEffectTargetBox, null, RelicEffectAmountsBox,
                RelicEffectTarget2Label, RelicEffectTarget2Box, RelicEffectTargetGroup, RelicEffectTarget2Group, RelicEffectAmountGroup);
        }

        private void OnAddEffectClick(object sender, RoutedEventArgs e) { AddEffect(EffectsBox, EffectKindBox, EffectAmountsBox, EffectTargetBox, EffectTarget2Box); }

        private void OnRemoveEffectClick(object sender, RoutedEventArgs e) { RemoveEffect(EffectsBox, EffectList); }

        private void OnAddRelicEffectClick(object sender, RoutedEventArgs e) { AddEffect(RelicEffectsBox, RelicEffectKindBox, RelicEffectAmountsBox, RelicEffectTargetBox, RelicEffectTarget2Box); }

        private void OnRemoveRelicEffectClick(object sender, RoutedEventArgs e) { RemoveEffect(RelicEffectsBox, RelicEffectList); }

        private void Status(string text) { StatusText.Text = text; }
    }
}
