// =============================================================================
//  Run History（战绩）· 面板（外壳 + 列表页）
//
//  形态照抄游戏自己的「模组图鉴」那套（平台已验证过的做法）：
//    · 自建 WorldSpace 画布（sortingOrder 1200，比图鉴的 1000 高），跟着主相机摆
//    · 桌面场景（Scenes/Main）右上角一块「战绩」牌子，点了开面板
//    · 面板 = 列表页（一局一行，可翻页）+ 详情页（见 RunHistoryUi.Detail.cs）
//
//  ★ 为什么不用 UIManager 的原版面板体系：原版 UI 结构随版本变，自建画布
//    不依赖任何原版对象，游戏更新也不会碎（图鉴就是这么做的）。
//  ★ 全部构建过程带 try/catch：面板坏了最多少一个入口，不能影响游戏。
// =============================================================================

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RunHistory
{
    internal static partial class RunHistoryUi
    {
        // ---------------- 外观常量 ----------------

        /// <summary>排序号：比模组图鉴（1000）高，两个面板同时开时这个在上。</summary>
        private const int CanvasOrder = 1200;

        private static readonly Color Paper = new Color(0.96f, 0.93f, 0.86f, 1f);
        private static readonly Color Ink = new Color(0.25f, 0.20f, 0.16f, 1f);
        private static readonly Color PanelPaper = new Color(0.94f, 0.91f, 0.85f, 0.99f);
        private static readonly Color RowA = new Color(0.90f, 0.86f, 0.79f, 1f);
        private static readonly Color RowB = new Color(0.86f, 0.82f, 0.75f, 1f);
        private static readonly Color Chip = new Color(0.82f, 0.77f, 0.69f, 1f);
        private static readonly Color Highlight = new Color(0.99f, 0.85f, 0.45f, 1f);
        private static readonly Color Dim = new Color(0.45f, 0.40f, 0.35f, 1f);

        private const float PanelW = 1600f;
        private const float PanelH = 900f;
        private const int RowsPerPage = 8;

        // ---------------- 状态 ----------------

        private static GameObject _canvasGo;
        private static Canvas _canvas;
        private static GameObject _entryGo;
        private static GameObject _panelGo;
        private static GameObject _listPage;
        private static GameObject _detailPage;

        private static bool _lastInMenu;
        private static string _lastScene = "";
        private static bool _open;
        private static int _page;

        private static readonly List<GameObject> _rowGos = new List<GameObject>();
        private static readonly List<TextMeshProUGUI> _rowDate = new List<TextMeshProUGUI>();
        private static readonly List<TextMeshProUGUI> _rowClass = new List<TextMeshProUGUI>();
        private static readonly List<TextMeshProUGUI> _rowResult = new List<TextMeshProUGUI>();
        private static readonly List<Image> _rowResultBg = new List<Image>();
        private static readonly List<TextMeshProUGUI> _rowTime = new List<TextMeshProUGUI>();
        private static readonly List<TextMeshProUGUI> _rowNodes = new List<TextMeshProUGUI>();
        private static readonly List<TextMeshProUGUI> _rowCoins = new List<TextMeshProUGUI>();
        private static TextMeshProUGUI _pageText;
        private static TextMeshProUGUI _emptyText;

        // ---------------- 宿主（每 0.5 秒一个节拍） ----------------
        internal static void OnHostTick()
        {
            try
            {
                bool inMenu = IsDeskHub(out string sceneName);
                if (sceneName != _lastScene)
                {
                    _lastScene = sceneName;
                    Debug.Log("[RunHistory] 当前场景 = " + sceneName + "（桌面界面才显示入口）");
                }

                if (inMenu != _lastInMenu)
                {
                    _lastInMenu = inMenu;
                    Debug.Log("[RunHistory] " + (inMenu ? "进入桌面界面 → 显示「战绩」入口" : "离开桌面界面 → 隐藏入口与面板"));
                }

                if (inMenu)
                {
                    // ★ 画布**只在这个场景里建**：这里的相机才是画布该对着的那台。
                    //   （在标题界面建的话，画布会被摆到另一台相机前面，进桌面后可能看不见。）
                    EnsureCanvas();
                    EnsureEntry();

                    var cam = Camera.main;
                    if (cam != null && _canvas != null && _canvas.worldCamera != cam)
                    {
                        _canvas.worldCamera = cam;
                    }
                    // 每拍跟随主相机：桌面界面的相机会移动/切换，画布不跟就会飘出视野
                    PlaceCanvasInFrontOfCamera(cam);

                    if (_entryGo != null && !_entryGo.activeSelf) { _entryGo.SetActive(true); }
                }
                else
                {
                    if (_entryGo != null && _entryGo.activeSelf) { _entryGo.SetActive(false); }
                    if (_open) { ClosePanel(); }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RunHistory] 宿主节拍失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool IsDeskHub(out string sceneName)
        {
            sceneName = "";
            try
            {
                var scene = SceneManager.GetActiveScene();
                sceneName = string.IsNullOrEmpty(scene.name) ? (scene.path ?? "") : scene.name;
                return scene.name == "Main"
                    || (!string.IsNullOrEmpty(scene.path) &&
                        scene.path.EndsWith("/Main.unity", StringComparison.OrdinalIgnoreCase));
            }
            catch { return false; }
        }

        // ---------------- 画布与入口 ----------------

        private static void EnsureCanvas()
        {
            if (_canvasGo != null) { return; }

            _canvasGo = new GameObject("RunHistory.Canvas", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);

            var canvas = _canvasGo.AddComponent<Canvas>();
            _canvas = canvas;

            var cam = Camera.main ?? UnityEngine.Object.FindObjectOfType<Camera>();
            if (cam != null)
            {
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = cam;
                var rt0 = _canvasGo.GetComponent<RectTransform>();
                rt0.sizeDelta = new Vector2(1920f, 1080f);
                rt0.localScale = new Vector3(0.01f, 0.01f, 0.01f);
                PlaceCanvasInFrontOfCamera(cam);
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            canvas.sortingOrder = CanvasOrder;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            _canvasGo.AddComponent<GraphicRaycaster>();

            Debug.Log("[RunHistory] 面板画布已创建（" + canvas.renderMode + " / sortingOrder " + CanvasOrder + "）");
        }

        private static void EnsureEntry()
        {
            if (_entryGo != null || _canvasGo == null) { return; }

            _entryGo = new GameObject("RunHistory.Entry", typeof(RectTransform));
            _entryGo.transform.SetParent(_canvasGo.transform, false);
            var rt = _entryGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            // 模组图鉴的牌子在 (-30,-110)，我们排在它下面，避免重叠
            rt.anchoredPosition = new Vector2(-30f, -220f);
            rt.sizeDelta = new Vector2(240f, 96f);

            var img = _entryGo.AddComponent<Image>();
            img.color = Paper;
            img.raycastTarget = true;

            var btn = _entryGo.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(TogglePanel);

            NewText(_entryGo.transform, "战 绩", 34f, Ink,
                new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        }

        private static void PlaceCanvasInFrontOfCamera(Camera cam)
        {
            if (_canvas == null || cam == null || _canvas.renderMode != RenderMode.WorldSpace) { return; }
            try
            {
                var t = _canvas.transform;
                t.position = cam.transform.position + cam.transform.forward * 60f;
                t.rotation = cam.transform.rotation;
            }
            catch { }
        }

        // ---------------- 面板开合 ----------------

        private static void TogglePanel()
        {
            if (_open) { ClosePanel(); } else { OpenPanel(); }
        }

        internal static void OpenPanel()
        {
            try
            {
                EnsurePanel();
                if (_panelGo == null) { return; }

                RunHistoryStore.ReloadIndex();
                _page = 0;
                ShowList();
                _panelGo.SetActive(true);
                _open = true;
                PlaceCanvasInFrontOfCamera(Camera.main);
                Debug.Log("[RunHistory] 面板已打开（" + RunHistoryStore.Index.entries.Count + " 局）");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RunHistory] 打开面板失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        internal static void ClosePanel()
        {
            try
            {
                if (_panelGo != null) { _panelGo.SetActive(false); }
                _open = false;
            }
            catch { }
        }

        private static void EnsurePanel()
        {
            if (_panelGo != null || _canvasGo == null) { return; }

            _panelGo = new GameObject("RunHistory.Panel", typeof(RectTransform));
            _panelGo.transform.SetParent(_canvasGo.transform, false);
            var rt = _panelGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(PanelW, PanelH);

            NewImage(_panelGo.transform, PanelPaper, new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero, "Bg");

            // 标题
            NewText(_panelGo.transform, "战 绩", 52f, Ink,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(400f, 60f));

            // 关闭
            var close = NewButton(_panelGo.transform, "✕", new Color(0.80f, 0.74f, 0.66f, 1f), Ink, 34f,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -30f), new Vector2(64f, 64f));
            close.onClick.AddListener(ClosePanel);

            BuildListPage();
            BuildDetailPage();

            _panelGo.SetActive(false);
        }

        // ---------------- 列表页 ----------------

        private static void BuildListPage()
        {
            _listPage = new GameObject("ListPage", typeof(RectTransform));
            _listPage.transform.SetParent(_panelGo.transform, false);
            Stretch(_listPage.GetComponent<RectTransform>());

            // 表头
            string[] heads = { "日期", "职业", "结果", "时长", "节点", "收益" };
            float[] xs = { 46f, 330f, 700f, 940f, 1110f, 1290f };
            for (int i = 0; i < heads.Length; i++)
            {
                var t = NewText(_listPage.transform, heads[i], 26f, Dim,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(xs[i], -112f), new Vector2(220f, 34f));
                t.alignment = TextAlignmentOptions.Left;
            }

            for (int i = 0; i < RowsPerPage; i++)
            {
                float y = -158f - i * 88f;

                var row = new GameObject("Row" + i, typeof(RectTransform));
                row.transform.SetParent(_listPage.transform, false);
                var rrt = row.GetComponent<RectTransform>();
                rrt.anchorMin = new Vector2(0f, 1f);
                rrt.anchorMax = new Vector2(0f, 1f);
                rrt.pivot = new Vector2(0f, 1f);
                rrt.anchoredPosition = new Vector2(40f, y);
                rrt.sizeDelta = new Vector2(PanelW - 80f, 76f);

                var bg = row.AddComponent<Image>();
                bg.color = (i % 2 == 0) ? RowA : RowB;
                bg.raycastTarget = true;

                var btn = row.AddComponent<Button>();
                btn.targetGraphic = bg;
                int captured = i;
                btn.onClick.AddListener(delegate { OpenDetailRow(captured); });

                _rowDate.Add(Col(row.transform, 6f, 26f));
                _rowClass.Add(Col(row.transform, 290f, 26f));
                var chipBg = NewImage(row.transform, Chip,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(660f, 0f), new Vector2(108f, 46f), "ResultChip");
                _rowResultBg.Add(chipBg);
                _rowResult.Add(NewText(chipBg.transform, "", 24f, Color.white,
                    new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero));
                _rowTime.Add(Col(row.transform, 900f, 26f));
                _rowNodes.Add(Col(row.transform, 1090f, 26f));
                _rowCoins.Add(Col(row.transform, 1270f, 26f));

                _rowGos.Add(row);
                row.SetActive(false);
            }

            _emptyText = NewText(_listPage.transform, "还没有战绩 —— 打完一局就会出现在这里", 32f, Dim,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -420f), new Vector2(1200f, 60f));

            // 翻页
            var prev = NewButton(_listPage.transform, "‹ 上一页", RowA, Ink, 28f,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-260f, 40f), new Vector2(200f, 60f));
            prev.onClick.AddListener(delegate { ChangePage(-1); });

            _pageText = NewText(_listPage.transform, "", 28f, Ink,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(300f, 60f));

            var next = NewButton(_listPage.transform, "下一页 ›", RowA, Ink, 28f,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(260f, 40f), new Vector2(200f, 60f));
            next.onClick.AddListener(delegate { ChangePage(1); });
        }

        private static TextMeshProUGUI Col(Transform parent, float x, float size)
        {
            var t = NewText(parent, "", size, Ink,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(300f, 50f), false);
            t.alignment = TextAlignmentOptions.Left;
            return t;
        }

        private static void ChangePage(int delta)
        {
            int pages = PageCount();
            if (pages <= 1) { return; }
            _page = Mathf.Clamp(_page + delta, 0, pages - 1);
            RefreshList();
        }

        private static int PageCount()
        {
            int total = RunHistoryStore.Index.entries.Count;
            return Mathf.Max(1, (total + RowsPerPage - 1) / RowsPerPage);
        }

        private static void ShowList()
        {
            if (_detailPage != null) { _detailPage.SetActive(false); }
            if (_listPage != null) { _listPage.SetActive(true); }
            RefreshList();
        }

        private static void RefreshList()
        {
            try
            {
                var entries = RunHistoryStore.Index.entries;
                _page = Mathf.Clamp(_page, 0, PageCount() - 1);

                for (int i = 0; i < RowsPerPage; i++)
                {
                    int at = _page * RowsPerPage + i;
                    bool used = at < entries.Count;
                    _rowGos[i].SetActive(used);
                    if (!used) { continue; }

                    var e = entries[at];
                    _rowDate[i].text = ShortDate(e.startedAt);
                    _rowClass[i].text = ClassLabel(e.className);
                    _rowResult[i].text = Results.Label(e.result);
                    _rowResultBg[i].color = ResultColor(e.result);
                    _rowTime[i].text = Duration(e.durationSeconds);
                    _rowNodes[i].text = e.nodes > 0 ? e.nodes + " 节点" : "—";
                    _rowCoins[i].text = e.upgradeCoins > 0 ? e.upgradeCoins.ToString() : "—";
                }

                if (_emptyText != null) { _emptyText.gameObject.SetActive(entries.Count == 0); }
                if (_pageText != null)
                {
                    _pageText.text = entries.Count == 0 ? "" : ("第 " + (_page + 1) + " / " + PageCount() + " 页");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RunHistory] 刷新列表失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void OpenDetailRow(int rowIndex)
        {
            try
            {
                int at = _page * RowsPerPage + rowIndex;
                var entries = RunHistoryStore.Index.entries;
                if (at < 0 || at >= entries.Count) { return; }

                var record = RunHistoryStore.LoadEntry(entries[at]);
                if (record == null)
                {
                    Debug.LogWarning("[RunHistory] 战绩文件读不出来：" + entries[at].file);
                    return;
                }
                ShowDetail(record);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RunHistory] 打开详情失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // ---------------- 小工具 ----------------

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
        }

        private static TextMeshProUGUI NewText(Transform parent, string text, float size, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 sizeDelta, bool center = true)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = (anchorMin == anchorMax) ? anchorMin : new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = sizeDelta;

            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = center ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
            t.raycastTarget = false;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Ellipsis;

            var font = ResolveFont();
            if (font != null) { t.font = font; }
            return t;
        }

        private static Image NewImage(Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pos, Vector2 size, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = (anchorMin == anchorMax) ? anchorMin : new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static Button NewButton(Transform parent, string label, Color bgColor, Color textColor, float size,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 sizeDelta)
        {
            var go = new GameObject("Btn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = (anchorMin == anchorMax) ? anchorMin : new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = sizeDelta;

            var img = go.AddComponent<Image>();
            img.color = bgColor;
            img.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            NewText(go.transform, label, size, textColor,
                new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            return btn;
        }

        private static TMP_FontAsset _font;

        private static TMP_FontAsset ResolveFont()
        {
            if (_font != null) { return _font; }
            try { _font = TMP_Settings.defaultFontAsset; } catch { }
            return _font;
        }

        private static Color ResultColor(string result)
        {
            switch (result)
            {
                case Results.Win: return new Color(0.25f, 0.52f, 0.28f, 1f);
                case Results.Lose: return new Color(0.62f, 0.24f, 0.22f, 1f);
                case Results.Abandon: return new Color(0.45f, 0.42f, 0.38f, 1f);
                case Results.Interrupted: return new Color(0.35f, 0.42f, 0.50f, 1f);
                default: return new Color(0.72f, 0.55f, 0.20f, 1f);
            }
        }

        private static string ShortDate(string stamp)
        {
            if (string.IsNullOrEmpty(stamp)) { return "—"; }
            // "yyyy-MM-dd HH:mm:ss" → "MM-dd HH:mm"
            return stamp.Length >= 16 ? stamp.Substring(5, 11) : stamp;
        }

        private static string Duration(double seconds)
        {
            if (seconds <= 0) { return "—"; }
            var ts = TimeSpan.FromSeconds(seconds);
            return (ts.Hours > 0 ? ts.Hours + ":" : "") + ts.Minutes.ToString("00") + ":" + ts.Seconds.ToString("00");
        }

        /// <summary>职业显示名：先查本地化表，查不到就显示内部名（mod 职业也可能只有内部名）。</summary>
        private static string ClassLabel(string className)
        {
            if (string.IsNullOrEmpty(className)) { return "（未知职业）"; }
            foreach (var table in new[] { "ClassName", "Card", "UI" })
            {
                try
                {
                    string text = new LocalizedString(table, className).GetLocalizedString();
                    if (!string.IsNullOrEmpty(text) && text != className &&
                        text.IndexOf("No translation", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return text;
                    }
                }
                catch { }
            }
            return className;
        }
    }

    /// <summary>常驻宿主：每 0.5 秒驱动一次节拍（面板只在桌面场景显示）。</summary>
    internal sealed class RunHistoryUiHost : MonoBehaviour
    {
        /// <summary>当前活着的宿主（场景加载回调据此避免重复创建）。</summary>
        internal static RunHistoryUiHost Instance { get; private set; }

        private float _next;

        private void Awake() { Instance = this; }

        private void OnDestroy() { if (Instance == this) { Instance = null; } }

        private void Update()
        {
            if (Time.realtimeSinceStartup < _next) { return; }
            _next = Time.realtimeSinceStartup + 0.5f;
            try { RunHistoryUi.OnHostTick(); }
            catch (Exception ex) { Debug.LogWarning("[RunHistory] 宿主异常 —— " + ex.Message); }
        }
    }
}
