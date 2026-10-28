// =============================================================================
//  Run History（战绩）· 面板（详情页）
//
//  详情页分四块：
//    ① 摘要：职业 · 结果 · 日期 · 种子 · 时长 · 进阶 · 收益
//    ② 路线条：按顺序的节点色块（类型用颜色与短名区分，腐化节点描红）
//    ③ 三个页签：卡组 / 遗物 / 统计
//    ④ 悬停高亮：把鼠标放到某张卡/某件遗物上，路线条里高亮"它是在第几个节点拿到的"
//       —— 数据来自采集期的节点快照差集（nodes[].newCards / newRelics），不需要额外采集。
// =============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.UI;

namespace RunHistory
{
    internal static partial class RunHistoryUi
    {
        private static RunRecord _detail;
        private static int _detailTab;          // 0=卡组 1=遗物 2=统计
        private static int _detailPageIndex;

        private static TextMeshProUGUI _detailTitle;
        private static TextMeshProUGUI _detailMeta;
        private static TextMeshProUGUI _detailToast;
        private static GameObject _detailContent;
        private static TextMeshProUGUI _detailPageText;
        private static Button _detailPrev;
        private static Button _detailNext;
        private static Button[] _tabButtons;
        private static TextMeshProUGUI[] _tabTexts;

        private static readonly List<Image> _chipBg = new List<Image>();
        private static readonly List<TextMeshProUGUI> _chipText = new List<TextMeshProUGUI>();
        private static readonly List<int> _chipNode = new List<int>();

        /// <summary>卡/遗物名 → 第一次出现的节点序号（采集期差集的结果）。</summary>
        private static Dictionary<string, int> _acquiredAt = new Dictionary<string, int>(StringComparer.Ordinal);

        private const int TilesPerPage = 42;   // 14 列 × 3 行

        // ---------------- 构建 ----------------

        private static void BuildDetailPage()
        {
            _detailPage = new GameObject("DetailPage", typeof(RectTransform));
            _detailPage.transform.SetParent(_panelGo.transform, false);
            Stretch(_detailPage.GetComponent<RectTransform>());

            var back = NewButton(_detailPage.transform, "← 返回列表", Face, Ink, 26f,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(44f, -32f), new Vector2(180f, 54f));
            back.onClick.AddListener(ShowList);

            // 用这局的种子重开（写进"待用种子"，回桌面按开始游戏时生效）
            var reuse = NewButton(_detailPage.transform, "用这局种子重开", Accent, Ink, 26f,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-44f, -32f), new Vector2(250f, 54f));
            reuse.onClick.AddListener(ReuseSeedFromDetail);

            _detailToast = NewText(_detailPage.transform, "", 22f, Dim,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(252f, -128f), new Vector2(1200f, 30f), false);

            _detailTitle = NewText(_detailPage.transform, "", 42f, Ink,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(250f, -44f), new Vector2(1100f, 54f), false);

            _detailMeta = NewText(_detailPage.transform, "", 24f, Dim,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(252f, -96f), new Vector2(1300f, 36f), false);

            // 路线条
            NewText(_detailPage.transform, "路线", 24f, Dim,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -142f), new Vector2(80f, 30f), false);

            var routeRoot = new GameObject("Route", typeof(RectTransform));
            routeRoot.transform.SetParent(_detailPage.transform, false);
            var rrt = routeRoot.GetComponent<RectTransform>();
            rrt.anchorMin = new Vector2(0f, 1f);
            rrt.anchorMax = new Vector2(0f, 1f);
            rrt.pivot = new Vector2(0f, 1f);
            rrt.anchoredPosition = new Vector2(120f, -136f);
            rrt.sizeDelta = new Vector2(1420f, 120f);

            const int perRow = 16;
            for (int i = 0; i < perRow * 2; i++)
            {
                int col = i % perRow, row = i / perRow;
                // 路线格：深墨边 + 节点色芯（与结果标签同一套，否则整页都是"没有边"的色块）
                var border = NewImage(routeRoot.transform, new Color(0.25f, 0.20f, 0.16f, 0.50f),
                    new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(col * 88f, -row * 56f), new Vector2(82f, 48f), "ChipBorder" + i);
                var bg = NewImage(border.transform, Chip, Vector2.zero, Vector2.one,
                    Vector2.zero, new Vector2(-4f, -4f), "Chip" + i);
                var text = NewText(bg.transform, "", 20f, Ink,
                    new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
                _chipBg.Add(bg);
                _chipText.Add(text);
                _chipNode.Add(-1);
                border.gameObject.SetActive(false);
            }

            // 页签
            _tabButtons = new Button[3];
            _tabTexts = new TextMeshProUGUI[3];
            string[] names = { "卡组", "遗物", "统计" };
            for (int i = 0; i < 3; i++)
            {
                int captured = i;
                var btn = NewButton(_detailPage.transform, names[i], Face, Ink, 28f,
                    new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(50f + i * 170f, -272f), new Vector2(160f, 56f));
                btn.onClick.AddListener(delegate { SwitchTab(captured); });
                _tabButtons[i] = btn;
                _tabTexts[i] = btn.GetComponentInChildren<TextMeshProUGUI>();
            }

            // 内容区
            _detailContent = new GameObject("DetailContent", typeof(RectTransform));
            _detailContent.transform.SetParent(_detailPage.transform, false);
            var crt = _detailContent.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(0f, 1f);
            crt.pivot = new Vector2(0f, 1f);
            crt.anchoredPosition = new Vector2(50f, -348f);
            crt.sizeDelta = new Vector2(1500f, 470f);

            // 翻页（只有卡组/遗物需要）
            _detailPrev = NewButton(_detailPage.transform, "‹ 上一页", Face, Ink, 26f,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-240f, 26f), new Vector2(190f, 54f));
            _detailPrev.onClick.AddListener(delegate { ChangeDetailPage(-1); });

            _detailPageText = NewText(_detailPage.transform, "", 26f, Ink,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(240f, 52f));

            _detailNext = NewButton(_detailPage.transform, "下一页 ›", Face, Ink, 26f,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(240f, 26f), new Vector2(190f, 54f));
            _detailNext.onClick.AddListener(delegate { ChangeDetailPage(1); });

            _detailPage.SetActive(false);
        }

        // ---------------- 打开 ----------------

        private static void ShowDetail(RunRecord record)
        {
            try
            {
                _detail = record;
                _detailTab = 0;
                _detailPageIndex = 0;
                RefreshDetailHeader();
                BuildRouteChips();
                RefreshDetailContent();
                if (_detailToast != null) { _detailToast.text = ""; }

                if (_listPage != null) { _listPage.SetActive(false); }
                SetTitleVisible(false);   // 详情页有自己的大标题，别叠；标题下那两条线也一起收掉
                if (_detailPage != null) { _detailPage.SetActive(true); }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RunHistory] 详情页渲染失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// 把这一局的种子设为「待用种子」：回桌面按「开始游戏」时，游戏生成的那个种子会被它替换掉。
        /// </summary>
        private static void ReuseSeedFromDetail()
        {
            try
            {
                if (_detail == null) { return; }
                RunHistorySeed.SetPending(_detail.seed, "来自战绩 " + _detail.runId);
                RefreshSeedStatus();
                if (_detailToast != null)
                {
                    _detailToast.text = "已设为待用种子 " + _detail.seed +
                        "　·　回桌面按「开始游戏」时生效（用掉即清除）";
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RunHistory] 设种子失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void RefreshDetailHeader()
        {
            if (_detail == null) { return; }

            if (_detailTitle != null)
            {
                _detailTitle.text = ClassLabel(_detail.className) + "　·　" + Results.Label(_detail.result);
                _detailTitle.color = ResultColor(_detail.result);
            }

            if (_detailMeta != null)
            {
                var parts = new List<string>();
                parts.Add(_detail.startedAt ?? "—");
                parts.Add("seed " + _detail.seed);
                parts.Add("时长 " + Duration(_detail.durationSeconds));
                parts.Add("进阶 " + _detail.ascensionLevel);
                int streak;
                if (_detail.result == Results.Win && Streaks().TryGetValue(_detail.runId, out streak) && streak > 0)
                {
                    parts.Add(streak + " 连胜");
                }
                if (_detail.resumedCount > 0) { parts.Add("续玩 " + _detail.resumedCount + " 次"); }
                if (_detail.recoveredFromSave) { parts.Add("从存档认领"); }
                int coins = _detail.final == null ? 0 : _detail.final.upgradeCoins;
                parts.Add("收益 " + coins);
                _detailMeta.text = string.Join("　·　", parts.ToArray());
            }
        }

        // ---------------- 路线条 ----------------

        private static void BuildRouteChips()
        {
            _acquiredAt = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int i = 0; i < _chipBg.Count; i++)
            {
                _chipBg[i].transform.parent.gameObject.SetActive(false);
                _chipNode[i] = -1;
            }

            if (_detail == null || _detail.nodes == null) { return; }

            // 先算出"每张卡/每件遗物第一次出现的节点"，供悬停高亮用
            foreach (var node in _detail.nodes)
            {
                if (node == null) { continue; }
                if (node.newCards != null)
                {
                    foreach (var card in node.newCards)
                    {
                        if (!_acquiredAt.ContainsKey(card)) { _acquiredAt[card] = node.index; }
                    }
                }
                if (node.newRelics != null)
                {
                    foreach (var relic in node.newRelics)
                    {
                        if (!_acquiredAt.ContainsKey(relic)) { _acquiredAt[relic] = node.index; }
                    }
                }
            }

            int shown = Mathf.Min(_detail.nodes.Count, _chipBg.Count);
            for (int i = 0; i < shown; i++)
            {
                var node = _detail.nodes[i];
                if (node == null) { continue; }

                string type = node.nodeType ?? "";
                _chipBg[i].transform.parent.gameObject.SetActive(true);
                _chipBg[i].color = node.corrupted ? new Color(0.62f, 0.30f, 0.42f, 1f) : NodeColor(type);
                _chipText[i].text = NodeShort(type);
                _chipText[i].color = Color.white;
                _chipNode[i] = node.index;
            }
        }

        private static void HighlightNode(int nodeIndex)
        {
            for (int i = 0; i < _chipBg.Count; i++)
            {
                if (_chipNode[i] < 0) { continue; }
                bool hit = _chipNode[i] == nodeIndex;
                _chipBg[i].color = hit
                    ? Highlight
                    : (Color)_chipBg[i].color;
                _chipText[i].color = hit ? Ink : Color.white;
            }
        }

        private static void ResetHighlight()
        {
            if (_detail == null || _detail.nodes == null) { return; }
            for (int i = 0; i < _chipBg.Count; i++)
            {
                int nodeIndex = _chipNode[i];
                if (nodeIndex < 0) { continue; }
                var node = FindNode(nodeIndex);
                _chipBg[i].color = node != null && node.corrupted
                    ? new Color(0.62f, 0.30f, 0.42f, 1f)
                    : NodeColor(node == null ? "" : node.nodeType);
                _chipText[i].color = Color.white;
            }
        }

        private static RunNode FindNode(int index)
        {
            if (_detail == null || _detail.nodes == null) { return null; }
            foreach (var n in _detail.nodes)
            {
                if (n != null && n.index == index) { return n; }
            }
            return null;
        }

        private static Color NodeColor(string type)
        {
            switch (type)
            {
                case "Enemy": return new Color(0.42f, 0.45f, 0.52f, 1f);
                case "EliteEnemy": return new Color(0.58f, 0.36f, 0.62f, 1f);
                case "Boss": return new Color(0.72f, 0.26f, 0.26f, 1f);
                case "RestPoint": return new Color(0.32f, 0.52f, 0.40f, 1f);
                case "Store": return new Color(0.72f, 0.58f, 0.26f, 1f);
                case "Treasure": return new Color(0.76f, 0.66f, 0.32f, 1f);
                case "Mystery": return new Color(0.40f, 0.40f, 0.48f, 1f);
                case "RemoveCard": return new Color(0.55f, 0.44f, 0.36f, 1f);
                case "Upgrade": return new Color(0.36f, 0.48f, 0.60f, 1f);
                case "SmallGame": return new Color(0.46f, 0.52f, 0.38f, 1f);
                case "ChapterTransition": return new Color(0.30f, 0.30f, 0.34f, 1f);
                default: return new Color(0.50f, 0.48f, 0.45f, 1f);
            }
        }

        private static string NodeShort(string type)
        {
            switch (type)
            {
                case "Enemy": return "战斗";
                case "EliteEnemy": return "精英";
                case "Boss": return "首领";
                case "RestPoint": return "休息";
                case "Store": return "商店";
                case "Treasure": return "宝箱";
                case "Mystery": return "未知";
                case "RemoveCard": return "删卡";
                case "Upgrade": return "升级";
                case "SmallGame": return "小游戏";
                case "ChapterTransition": return "章节";
                case "EmptyNode": return "空";
                case "TachiEDialogue": return "对话";
                default: return string.IsNullOrEmpty(type) ? "?" : type.Length > 3 ? type.Substring(0, 3) : type;
            }
        }

        // ---------------- 内容区 ----------------

        private static void SwitchTab(int tab)
        {
            _detailTab = tab;
            _detailPageIndex = 0;
            RefreshDetailContent();
        }

        private static void ChangeDetailPage(int delta)
        {
            int pages = DetailPageCount();
            if (pages <= 1) { return; }
            _detailPageIndex = Mathf.Clamp(_detailPageIndex + delta, 0, pages - 1);
            RefreshDetailContent();
        }

        private static List<string> CurrentList()
        {
            var list = new List<string>();
            if (_detail == null) { return list; }
            if (_detailTab == 0)
            {
                if (_detail.final != null) { list.AddRange(_detail.final.deck); }
            }
            else if (_detailTab == 1)
            {
                if (_detail.final != null) { list.AddRange(_detail.final.relics); }
            }
            return list;
        }

        private static int DetailPageCount()
        {
            if (_detailTab == 2) { return 1; }
            int total = CurrentList().Count;
            return Mathf.Max(1, (total + TilesPerPage - 1) / TilesPerPage);
        }

        private static void RefreshDetailContent()
        {
            try
            {
                for (int i = _detailContent.transform.childCount - 1; i >= 0; i--)
                {
                    UnityEngine.Object.Destroy(_detailContent.transform.GetChild(i).gameObject);
                }

                for (int i = 0; i < _tabButtons.Length; i++)
                {
                    bool active = i == _detailTab;
                    var img = _tabButtons[i].targetGraphic as Image;
                    if (img != null) { img.color = active ? Accent : Face; }
                    if (_tabTexts[i] != null) { _tabTexts[i].color = Ink; }
                }

                bool paged = _detailTab != 2;
                if (_detailPrev != null) { _detailPrev.gameObject.SetActive(paged && DetailPageCount() > 1); }
                if (_detailNext != null) { _detailNext.gameObject.SetActive(paged && DetailPageCount() > 1); }
                if (_detailPageText != null)
                {
                    _detailPageText.text = paged && DetailPageCount() > 1
                        ? "第 " + (_detailPageIndex + 1) + " / " + DetailPageCount() + " 页"
                        : "";
                }

                if (_detailTab == 2) { BuildStats(); return; }

                var items = CurrentList();
                if (items.Count == 0)
                {
                    NewText(_detailContent.transform,
                        _detail == null || _detail.final == null
                            ? "本局尚未结算 —— 卡组与遗物要等结算时才有终局快照"
                            : "（空）",
                        30f, Dim, new Vector2(0f, 1f), new Vector2(0f, 1f),
                        new Vector2(0f, -40f), new Vector2(1200f, 50f), false);
                    return;
                }

                int start = _detailPageIndex * TilesPerPage;
                int end = Mathf.Min(start + TilesPerPage, items.Count);
                for (int i = start; i < end; i++)
                {
                    int slot = i - start;
                    int col = slot % 14, row = slot / 14;
                    BuildTile(items[i], col * 107f, -row * 152f, _detailTab == 0);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RunHistory] 详情内容渲染失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>一张卡 / 一件遗物的格子（卡用卡面图，遗物用图标，取不到就纯色 + 名字）。</summary>
        private static void BuildTile(string rawName, float x, float y, bool isCard)
        {
            bool upgraded = false;
            string name = rawName;
            if (!string.IsNullOrEmpty(name) && name.EndsWith("+", StringComparison.Ordinal))
            {
                upgraded = true;
                name = name.Substring(0, name.Length - 1);
            }

            // 格子 = 上面是图、下面一行名字（墨色字写在面板纸面上，不再往卡图上压黑条）
            float artH = isCard ? 120f : 96f;
            var cell = new GameObject("Tile", typeof(RectTransform));
            cell.transform.SetParent(_detailContent.transform, false);
            var rt = cell.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(96f, artH + 30f);

            // 透明底板：只负责接收射线（悬停用）
            var hit = cell.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            // 图
            var artGo = new GameObject("Art", typeof(RectTransform));
            artGo.transform.SetParent(cell.transform, false);
            var artRt = artGo.GetComponent<RectTransform>();
            artRt.anchorMin = new Vector2(0f, 1f);
            artRt.anchorMax = new Vector2(0f, 1f);
            artRt.pivot = new Vector2(0f, 1f);
            artRt.anchoredPosition = Vector2.zero;
            artRt.sizeDelta = new Vector2(96f, artH);

            var img = artGo.AddComponent<Image>();
            img.raycastTarget = false;
            img.color = isCard ? new Color(0.84f, 0.80f, 0.73f, 1f) : new Color(0.78f, 0.74f, 0.66f, 1f);

            var sprite = ResolveSprite(name, isCard);
            if (sprite != null)
            {
                img.sprite = sprite;
                img.preserveAspect = true;
                img.color = Color.white;
            }

            if (upgraded)
            {
                // 升级态：左上角一枚小方标
                var mark = NewImage(artGo.transform, new Color(0.85f, 0.68f, 0.22f, 1f),
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(22f, 22f), "Up");
                NewText(mark.transform, "↑", 18f, Color.white,
                    new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            }

            // 名字（图下面那 30px）：墨色、单行、超长省略
            var label = NewText(cell.transform, DisplayName(name, isCard),
                isCard ? 15f : 16f, Ink,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -(artH + 3f)), new Vector2(2f, 26f));
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;

            // 悬停高亮：这张卡/这件遗物是在第几个节点拿到的
            var relay = cell.AddComponent<HoverRelay>();
            string key = rawName;
            relay.Handler = delegate (bool over)
            {
                try
                {
                    if (over)
                    {
                        int index;
                        if (_acquiredAt.TryGetValue(key, out index)) { HighlightNode(index); }
                    }
                    else { ResetHighlight(); }
                }
                catch { }
            };
        }

        private static Sprite ResolveSprite(string name, bool isCard)
        {
            try
            {
                if (isCard)
                {
                    var cdm = GameData.CardDataManager.Instance;
                    if (cdm == null) { return null; }
                    var so = cdm.GetCardSoByName(name);
                    if (so != null && so.cardName == name) { return so.cardArt; }
                }
                else
                {
                    var dm = GameData.DataManager.Instance;
                    var rdm = dm == null ? null : dm.GetRelicDataManager();
                    if (rdm == null) { return null; }
                    var so = rdm.GetRelicSo(name);
                    if (so != null) { return so.relicIcon; }
                }
            }
            catch { }
            return null;
        }

        private static string DisplayName(string name, bool isCard)
        {
            try
            {
                string table = isCard ? "CardName" : "Relic";
                string text = new LocalizedString(table, name).GetLocalizedString();
                if (!string.IsNullOrEmpty(text) && text != name &&
                    text.IndexOf("No translation", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return text;
                }
            }
            catch { }
            return name;
        }

        private static void BuildStats()
        {
            var lines = new List<string>();
            var stats = _detail == null || _detail.final == null ? null : _detail.final.stats;
            if (stats == null)
            {
                lines.Add("本局尚未结算 —— 统计数据要等结算时才有");
            }
            else
            {
                lines.Add("击杀　普通 " + stats.normalEnemySlainCount +
                          "　·　精英 " + stats.eliteSlainCount +
                          "　·　首领 " + stats.bossSlainCount);
                lines.Add("探索　新节点 " + stats.travelNewNodeCount +
                          "　·　本局节点 " + (_detail.nodes == null ? 0 : _detail.nodes.Count));
                lines.Add("骰子　掷出 1 共 " + stats.rollTheOneDiceCount +
                          " 次　·　掷出最大 " + stats.rollMaxNumberCount +
                          "　·　掷出最小 " + stats.rollMinNumberCount);
                lines.Add("检定　成功 " + stats.checkSuccessCount +
                          "　·　失败 " + stats.checkFailCount);
                lines.Add("其他　钓鱼 " + stats.fishCount +
                          "　·　被紫雾吞噬 " + stats.catchByPurpleMistCount);
                if (_detail.final != null)
                {
                    lines.Add("终局　血量 " + _detail.final.hp + "/" + _detail.final.maxHp +
                              "　·　金币 " + _detail.final.gold +
                              "　·　卡组 " + _detail.final.deck.Count +
                              " 张　·　遗物 " + _detail.final.relics.Count + " 件");
                    lines.Add("收益　升级币 " + _detail.final.upgradeCoins);
                }
            }

            for (int i = 0; i < lines.Count; i++)
            {
                NewText(_detailContent.transform, lines[i], 28f, Ink,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -10f - i * 56f),
                    new Vector2(1400f, 48f), false);
            }
        }
    }

    /// <summary>鼠标进出回调（悬停高亮用）。</summary>
    internal sealed class HoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal Action<bool> Handler;

        public void OnPointerEnter(PointerEventData eventData)
        {
            try { if (Handler != null) { Handler(true); } } catch { }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            try { if (Handler != null) { Handler(false); } } catch { }
        }
    }
}
