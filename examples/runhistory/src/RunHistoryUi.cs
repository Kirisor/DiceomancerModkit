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
        private static readonly Color RowA = new Color(0.95f, 0.92f, 0.86f, 1f);
        private static readonly Color RowB = new Color(0.89f, 0.85f, 0.78f, 1f);
        private static readonly Color Chip = new Color(0.82f, 0.77f, 0.69f, 1f);
        private static readonly Color Highlight = new Color(0.99f, 0.85f, 0.45f, 1f);
        private static readonly Color Dim = new Color(0.45f, 0.40f, 0.35f, 1f);

        // ---- 描边与层次（手绘账本的思路：纸面 + 墨线框 + 细线分隔 + 抬起有影） ----
        private static readonly Color BorderInk = new Color(0.25f, 0.20f, 0.16f, 0.78f);
        private static readonly Color Hair = new Color(0.25f, 0.20f, 0.16f, 0.22f);
        private static readonly Color InnerLight = new Color(1f, 1f, 1f, 0.35f);
        // 投影：面板用柔影精灵（见 RunHistoryUi.Skin），按钮用压得更实的一层 —— 按钮小，
        // 渐隐带在这个尺寸上会糊成一团，所以「离纸越近的东西，影子越实」。
        private static readonly Color ButtonShadow = new Color(0.05f, 0.04f, 0.03f, 0.34f);
        private static readonly Color Face = new Color(0.88f, 0.83f, 0.75f, 1f);     // 次按钮面
        private static readonly Color FaceHot = new Color(0.82f, 0.76f, 0.67f, 1f);  // 悬停/按下
        private static readonly Color Accent = new Color(0.94f, 0.79f, 0.45f, 1f);   // 主按钮面
        private static readonly Color FooterBg = new Color(0.90f, 0.86f, 0.78f, 1f);

        private const float PanelW = 1600f;
        private const float PanelH = 900f;
        private const int RowsPerPage = 8;

        // ---- 列表版式（表头与数据共用同一张列位移表，避免错位） ----
        private const float RowX = 40f;              // 行左边缘
        private const float RowW = PanelW - 80f;     // 行宽
        private const float RowH = 72f;              // 行高
        private const float RowPitch = 78f;          // 行距（行之间留 6px 走分割线）
        private const float RowsTop = -146f;         // 第一行顶边（8 行 → 末行底边 -764，页脚上沿 -782）
        private const float ColHeadY = -102f;        // 表头文字顶边
        private const float ColRuleY = -140f;        // 表头下横线
        private const float FooterH = 118f;          // 页脚带高度
        // 列：日期 / 职业 / 进阶 / 连胜 / 结果 / 时长 / 节点 / 收益
        private static readonly float[] ColumnX = { 6f, 220f, 470f, 620f, 780f, 1010f, 1190f, 1380f };

        // ---------------- 状态 ----------------

        private static GameObject _canvasGo;
        private static Canvas _canvas;
        private static GameObject _entryGo;
        private static GameObject _panelGo;
        private static TextMeshProUGUI _panelTitle;
        private static readonly List<GameObject> _titleDecor = new List<GameObject>();
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
        private static readonly List<Image> _rowAccent = new List<Image>();
        private static readonly List<TextMeshProUGUI> _rowAsc = new List<TextMeshProUGUI>();
        private static readonly List<Image> _rowAscFill = new List<Image>();
        private static readonly List<TextMeshProUGUI> _rowStreak = new List<TextMeshProUGUI>();
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
                string reason;
                bool atDesk = IsAtDesk(out reason);

                if (atDesk != _lastInMenu)
                {
                    _lastInMenu = atDesk;
                    Debug.Log("[RunHistory] " + (atDesk
                        ? "在桌面 → 显示「战绩」入口（" + reason + "）"
                        : "离开桌面 → 隐藏入口与面板（" + reason + "）"));
                }

                if (atDesk)
                {
                    // ★ 画布**只在桌面时建**：这时的相机才是画布该对着的那台。
                    EnsureCanvas();
                    EnsureEntry();

                    var cam = Camera.main;
                    if (cam != null && _canvas != null && _canvas.worldCamera != cam)
                    {
                        _canvas.worldCamera = cam;
                    }
                    // 每拍跟随主相机：桌面的相机会移动/切换，画布不跟就会飘出视野
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

        // ---------------- 「桌面」判定 ----------------

        /// <summary>主菜单房间（桌面）那个组件。它管着 `deskScene` 的开关。</summary>
        private static SmallGames.WorldBetween.SmallGameMainMenu _menu;

        /// <summary>`SmallGameMainMenu.deskScene`（私有字段，反射取一次）。</summary>
        private static GameObject _deskScene;

        private static float _nextDeskSearch;
        private static bool _deskWarned;

        /// <summary>
        /// 是否正处在「桌面」（房间本体），而不是图鉴 / 升级 / 选职业 / 小游戏 / 地图 / 战斗。
        /// </summary>
        /// <remarks>
        /// ★ 判据是游戏自己的开关，不是猜的场景名：
        ///   `SmallGames.WorldBetween.SmallGameMainMenu` 持有 <c>deskScene</c>，
        ///   进任何子界面时 `deskScene.SetActive(false)`（<c>EnterSubGame</c>），
        ///   返回时才 `SetActive(true)`（<c>BackFromSubSmallGame</c>）。
        ///   所以「桌面可见」这一条就同时排除了：图鉴（Book）、升级、选职业、小游戏、
        ///   以及开局后的地图/战斗界面（那些也把桌面藏起来）。
        ///   拿不到这个组件时**一律不显示** —— 宁可少一个入口，也不要在战斗里糊一块牌子。
        /// </remarks>
        private static bool IsAtDesk(out string reason)
        {
            reason = "";
            try
            {
                // 缓存会随场景卸载而失效 → 定期重找（每 2 秒最多一次，避免每拍全场景扫描）
                if (_menu == null || _deskScene == null)
                {
                    if (Time.realtimeSinceStartup < _nextDeskSearch)
                    {
                        reason = "等待主菜单房间组件";
                        return false;
                    }
                    _nextDeskSearch = Time.realtimeSinceStartup + 2f;
                    _menu = null;
                    _deskScene = null;

                    foreach (var candidate in Resources.FindObjectsOfTypeAll<SmallGames.WorldBetween.SmallGameMainMenu>())
                    {
                        if (candidate == null || candidate.gameObject == null) { continue; }
                        if (!candidate.gameObject.scene.IsValid()) { continue; }   // 跳过资产里的模板
                        var field = typeof(SmallGames.WorldBetween.SmallGameMainMenu).GetField("deskScene",
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Public);
                        var desk = field == null ? null : field.GetValue(candidate) as GameObject;
                        if (desk == null) { continue; }
                        _menu = candidate;
                        _deskScene = desk;
                        break;
                    }

                    if (_menu == null)
                    {
                        if (!_deskWarned)
                        {
                            _deskWarned = true;
                            Debug.Log("[RunHistory] 暂未找到主菜单房间（SmallGameMainMenu.deskScene），入口不显示。");
                        }
                        reason = "没找到主菜单房间";
                        return false;
                    }
                }

                if (!_menu.gameObject.activeInHierarchy) { reason = "主菜单房间未激活"; return false; }
                if (!_deskScene.activeInHierarchy) { reason = "桌面已隐藏（图鉴/升级/选职业/地图/战斗）"; return false; }

                string scene = "";
                try
                {
                    var active = SceneManager.GetActiveScene();
                    scene = string.IsNullOrEmpty(active.name) ? (active.path ?? "") : active.name;
                }
                catch { }

                reason = "桌面可见｜场景=" + scene;
                return true;
            }
            catch (Exception ex)
            {
                reason = "判定异常：" + ex.GetType().Name;
                return false;
            }
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

            // 桌面上的入口牌：和面板里的按钮同一套语言（墨边 + 投影），
            // 否则它在书桌木纹上会是一块没有轮廓的浅色方块。
            NewImageAt(_entryGo.transform, ButtonShadow, new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f), new Vector2(3f, -9f), Vector2.zero, "Shadow");
            var border = NewImage(_entryGo.transform, BorderInk, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, "Border");
            border.raycastTarget = true;
            var face = NewImage(_entryGo.transform, Paper, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-6f, -6f), "Face");

            var btn = _entryGo.AddComponent<Button>();
            btn.targetGraphic = face;
            btn.colors = TintColors();
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
                _streakCache = null;      // 索引重读后连胜要重算（可能刚打完一局）
                _page = 0;
                ShowList();
                _panelGo.SetActive(true);
                _open = true;
                BlockOtherUI(true);
                PlaceCanvasInFrontOfCamera(Camera.main);
                StartIntro();      // 压暗环境 + 面板落位（见 RunHistoryUi.Intro）
                Debug.Log("[RunHistory] 面板已打开（" + RunHistoryStore.Index.entries.Count + " 局）" +
                    (_scrimGo == null || _panelGo == null ? "" :
                     "｜层级：压暗=" + _scrimGo.transform.GetSiblingIndex() +
                     " 面板=" + _panelGo.transform.GetSiblingIndex()));
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
                StopIntro();       // 先收压暗层，再藏面板（两帧内不会看到亮边）
                if (_panelGo != null) { _panelGo.SetActive(false); }
                _open = false;
                BlockOtherUI(false);
            }
            catch { }
        }

        // ---------------- 输入隔断 ----------------

        private static readonly List<Behaviour> _mutedRaycasters = new List<Behaviour>();
        private static readonly List<Collider> _mutedColliders = new List<Collider>();
        private static readonly List<Collider2D> _mutedColliders2D = new List<Collider2D>();
        private static GameObject _blockerGo;

        /// <summary>
        /// 开面板时把**别的画布的射线**与**场景里的碰撞体**关掉，并在自己画布最底层垫一张几乎透明的挡板。
        /// </summary>
        /// <remarks>
        /// ★ 这是照抄「模组图鉴」的做法（那边叫"硬隔断"，已实机验证）：
        ///   只加一张全屏挡板不够 —— WorldSpace 画布下面还有原版 UI 画布与 2D 碰撞体，
        ///   点击会同时打到它们（症状：隔着面板点到了下面的「升级 / 进阶 / 图鉴」）。
        ///   所以逐个关：其它 Canvas 的 GraphicRaycaster + 所有 Collider/Collider2D，并记账，关面板时原样恢复。
        /// </remarks>
        private static void BlockOtherUI(bool block)
        {
            try
            {
                if (block)
                {
                    _mutedColliders.Clear();
                    foreach (var col in Resources.FindObjectsOfTypeAll<Collider>())
                    {
                        if (col == null || !col.enabled || !col.gameObject.scene.IsValid()) { continue; }
                        col.enabled = false;
                        _mutedColliders.Add(col);
                    }
                    foreach (var col in Resources.FindObjectsOfTypeAll<Collider2D>())
                    {
                        if (col == null || !col.enabled || !col.gameObject.scene.IsValid()) { continue; }
                        col.enabled = false;
                        _mutedColliders2D.Add(col);
                    }

                    _mutedRaycasters.Clear();
                    foreach (var cv in Resources.FindObjectsOfTypeAll<Canvas>())
                    {
                        if (cv == null || cv == _canvas || !cv.gameObject.scene.IsValid()) { continue; }
                        if (!cv.gameObject.activeInHierarchy) { continue; }
                        var rc = cv.GetComponent<GraphicRaycaster>();
                        if (rc == null || !rc.enabled) { continue; }
                        rc.enabled = false;
                        _mutedRaycasters.Add(rc);
                    }

                    if (_blockerGo == null && _canvasGo != null)
                    {
                        _blockerGo = new GameObject("Blocker", typeof(RectTransform));
                        _blockerGo.transform.SetParent(_canvasGo.transform, false);
                        _blockerGo.transform.SetAsFirstSibling();   // 垫最底层：自己面板的点击优先
                        var brt = _blockerGo.GetComponent<RectTransform>();
                        brt.anchorMin = Vector2.zero;
                        brt.anchorMax = Vector2.one;
                        brt.offsetMin = Vector2.zero;
                        brt.offsetMax = Vector2.zero;
                        var img = _blockerGo.AddComponent<Image>();
                        img.color = new Color(0f, 0f, 0f, 0.004f);   // 肉眼不可见但接收射线
                        img.raycastTarget = true;
                    }
                    if (_blockerGo != null) { _blockerGo.SetActive(true); }

                    Debug.Log("[RunHistory] 已开启输入隔断（屏蔽 " + _mutedRaycasters.Count +
                        " 个画布射线 / " + _mutedColliders.Count + " 个3D碰撞体 / " +
                        _mutedColliders2D.Count + " 个2D碰撞体）");
                }
                else
                {
                    if (_blockerGo != null) { _blockerGo.SetActive(false); }
                    for (int i = 0; i < _mutedRaycasters.Count; i++)
                    {
                        var rc = _mutedRaycasters[i] as GraphicRaycaster;
                        if (rc != null) { rc.enabled = true; }
                    }
                    _mutedRaycasters.Clear();
                    for (int i = 0; i < _mutedColliders.Count; i++)
                    {
                        if (_mutedColliders[i] != null) { _mutedColliders[i].enabled = true; }
                    }
                    _mutedColliders.Clear();
                    for (int i = 0; i < _mutedColliders2D.Count; i++)
                    {
                        if (_mutedColliders2D[i] != null) { _mutedColliders2D[i].enabled = true; }
                    }
                    _mutedColliders2D.Clear();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[RunHistory] 输入隔断切换失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
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

            // 投影：两段式（贴身影 + 环境影）——让面板真的从桌面上浮起来
            AddLiftedShadow(_panelGo.transform, 48f, 24f, 22f, 8f);

            // 打开动画要整块渐显，所以挂一个 CanvasGroup（落位逻辑见 RunHistoryUi.Intro）
            _panelGroup = _panelGo.AddComponent<CanvasGroup>();
            _panelGroup.alpha = 1f;

            NewImage(_panelGo.transform, PanelPaper, new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero, "Bg");

            // 双描边：外 4px 墨线 + 内 1px 亮线（纸页的压边）
            AddFrame(_panelGo.transform, BorderInk, 4f, 0f);
            AddFrame(_panelGo.transform, InnerLight, 1f, 7f);

            // 标题带（标题 + 双线，把表头和标题分开）
            // ★ 注意 TMP 文本框是顶部锚点：anchoredPosition 定的是**上沿**，
            //   所以标题占 -26~-82，分隔线必须排在 -90 之后，否则会从字里穿过去。
            _panelTitle = NewText(_panelGo.transform, "战 绩", 46f, Ink,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(600f, 56f));
            // 标题下的双线也跟着标题一起显示/隐藏 —— 详情页有自己的大标题，线会横穿字
            _titleDecor.Clear();
            _titleDecor.Add(NewImage(_panelGo.transform, BorderInk, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -90f), new Vector2(-80f, 3f), "TitleRuleA").gameObject);
            _titleDecor.Add(NewImage(_panelGo.transform, Hair, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -97f), new Vector2(-80f, 1f), "TitleRuleB").gameObject);

            // 关闭（用汉字而不是 ✕ —— 游戏的 TMP 字体没有 ✕ 的字形，会显示成方框）
            var close = NewButton(_panelGo.transform, "关闭", Face, Ink, 26f,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -26f), new Vector2(112f, 54f));
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

            // 表头（列位移与行内同一张表，表头/数据不会再差 20px）
            string[] heads = { "日期", "职业", "进阶", "连胜", "结果", "时长", "节点", "收益" };
            for (int i = 0; i < heads.Length; i++)
            {
                var t = NewText(_listPage.transform, heads[i], 26f, Dim,
                    new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(RowX + ColumnX[i], ColHeadY), new Vector2(240f, 34f));
                t.alignment = TextAlignmentOptions.Left;
            }
            NewImage(_listPage.transform, BorderInk, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, ColRuleY), new Vector2(-2f * RowX, 2f), "ColumnRule");

            for (int i = 0; i < RowsPerPage; i++)
            {
                float y = RowsTop - i * RowPitch;

                var row = new GameObject("Row" + i, typeof(RectTransform));
                row.transform.SetParent(_listPage.transform, false);
                var rrt = row.GetComponent<RectTransform>();
                rrt.anchorMin = new Vector2(0f, 1f);
                rrt.anchorMax = new Vector2(0f, 1f);
                rrt.pivot = new Vector2(0f, 1f);
                rrt.anchoredPosition = new Vector2(RowX, y);
                rrt.sizeDelta = new Vector2(RowW, RowH);

                var bg = row.AddComponent<Image>();
                bg.color = (i % 2 == 0) ? RowA : RowB;
                bg.raycastTarget = true;

                var btn = row.AddComponent<Button>();
                btn.targetGraphic = bg;
                btn.colors = TintColors();
                int captured = i;
                btn.onClick.AddListener(delegate { OpenDetailRow(captured); });

                // 左侧结果色条（一眼扫出胜/负/中断）+ 行底细线（账本的横格）
                var accent = NewImageAt(row.transform, Color.clear, new Vector2(0f, 0f), new Vector2(0f, 1f),
                    new Vector2(0f, 0.5f), Vector2.zero, new Vector2(7f, 0f), "Accent");
                NewImageAt(row.transform, Hair, new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 2f), "RowRule");

                _rowDate.Add(Col(row.transform, ColumnX[0], 26f));
                _rowClass.Add(Col(row.transform, ColumnX[1], 26f));

                // 进阶：小子标签（墨边 + 纸色芯），表头已写「进阶」，格子里只放数字
                var ascBorder = NewImageAt(row.transform, new Color(0.25f, 0.20f, 0.16f, 0.50f),
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(ColumnX[2], 0f), new Vector2(76f, 40f), "AscChip");
                var ascFill = NewImage(ascBorder.transform, Chip, Vector2.zero, Vector2.one,
                    Vector2.zero, new Vector2(-4f, -4f), "Fill");
                _rowAscFill.Add(ascFill);
                _rowAsc.Add(NewText(ascBorder.transform, "", 24f, Ink,
                    new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero));

                // 连胜：文字即可（数字 + 「连胜」），2 连以上用暖色
                _rowStreak.Add(Col(row.transform, ColumnX[3], 24f));

                // 结果标签：深墨边 + 结果色芯，像盖上去的印章
                var chipBorder = NewImageAt(row.transform, new Color(0.25f, 0.20f, 0.16f, 0.55f),
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(ColumnX[4], 0f), new Vector2(120f, 46f), "ResultChip");
                var chipFill = NewImage(chipBorder.transform, Chip, Vector2.zero, Vector2.one,
                    Vector2.zero, new Vector2(-4f, -4f), "Fill");
                _rowResultBg.Add(chipFill);
                _rowResult.Add(NewText(chipBorder.transform, "", 24f, Color.white,
                    new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero));

                _rowTime.Add(Col(row.transform, ColumnX[5], 26f));
                _rowNodes.Add(Col(row.transform, ColumnX[6], 26f));
                _rowCoins.Add(Col(row.transform, ColumnX[7], 26f));

                _rowAccent.Add(accent);
                _rowGos.Add(row);
                row.SetActive(false);
            }

            _emptyText = NewText(_listPage.transform, "还没有战绩 —— 打完一局就会出现在这里", 32f, Dim,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -420f), new Vector2(1200f, 60f));

            // 页脚带：与列表用一条墨线隔开，行永远压不到按钮。
            // 注意要留出纸边（inset 10）—— 整宽拉伸会把面板底边那条 4px 墨线盖掉。
            NewImageAt(_listPage.transform, FooterBg, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(-20f, FooterH - 10f), "FooterBand");
            NewImageAt(_listPage.transform, BorderInk, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, FooterH), new Vector2(-20f, 2f), "FooterRule");

            // 翻页（靠右，给左边的种子区让位）
            var prev = NewButton(_listPage.transform, "< 上一页", Face, Ink, 26f,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-444f, 34f), new Vector2(168f, 52f));
            prev.onClick.AddListener(delegate { ChangePage(-1); });

            _pageText = NewText(_listPage.transform, "", 26f, Ink,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-232f, 44f), new Vector2(190f, 34f));

            var next = NewButton(_listPage.transform, "下一页 >", Face, Ink, 26f,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 34f), new Vector2(168f, 52f));
            next.onClick.AddListener(delegate { ChangePage(1); });

            // 种子：按钮在页脚最左，状态行贴在它右边（同一行，互不压行）
            var seedBtn = NewButton(_listPage.transform, "种子…", Accent, Ink, 26f,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 33f), new Vector2(152f, 52f));
            seedBtn.onClick.AddListener(OpenSeedPad);

            _seedStatusText = NewText(_listPage.transform, "", 22f, Dim,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(212f, 42f), new Vector2(680f, 34f), false);

            BuildSeedPad();
            RefreshSeedStatus();
        }

        // ---------------- 种子输入（自建数字键盘，不依赖文本输入框） ----------------

        private static GameObject _seedPadGo;
        private static TextMeshProUGUI _seedValueText;
        private static TextMeshProUGUI _seedStatusText;
        private static TextMeshProUGUI _seedHintText;
        private static string _seedTyped = "";

        /// <summary>
        /// 为什么是自建键盘而不是 TMP_InputField：这个项目用的是新版 Input System，
        /// 文本输入框在 WorldSpace 画布里能不能收到键盘事件没有把握；
        /// 而按钮点击走的是 EventSystem 射线，与官方 UI 同一套，确定能用。
        /// </summary>
        private static void BuildSeedPad()
        {
            _seedPadGo = new GameObject("SeedPad", typeof(RectTransform));
            _seedPadGo.transform.SetParent(_panelGo.transform, false);
            var rt = _seedPadGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(520f, 660f);

            // 键盘是一张浮在面板上的小卡：柔影 + 双描边，和面板同一套语言
            AddLiftedShadow(_seedPadGo.transform, 46f, 18f, 20f, 6f);
            NewImage(_seedPadGo.transform, PanelPaper, new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero, "Bg");
            AddFrame(_seedPadGo.transform, BorderInk, 3f, 0f);
            AddFrame(_seedPadGo.transform, InnerLight, 1f, 6f);

            // ★ 这张卡里的每个元素都按「距顶多少」排，并且各占一段互不重叠的带：
            //   标题 26~70 · 分隔线 78 · 数值框 96~158 · 提示 164~190
            //   数字键 204~528（4 行 × 72，行距 84）· 底部按钮 548~608
            //   之前把按钮锚在「距底 100」、数字键按「距顶 230 起」排，两套坐标系撞在一起，
            //   最后一行 0/退格/清空 就被「用这个种子 / 关闭」压住了。
            NewText(_seedPadGo.transform, "输入种子", 40f, Ink,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(360f, 44f));
            NewImage(_seedPadGo.transform, BorderInk, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -78f), new Vector2(-40f, 2f), "TitleRule");

            // 数值框：做成一个"输入框"的样子（墨边 + 浅面），输入时这一格在变
            var valueBox = NewImageAt(_seedPadGo.transform, BorderInk,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -96f), new Vector2(360f, 62f), "ValueBox");
            NewImage(valueBox.transform, Face, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-4f, -4f), "Fill");
            _seedValueText = NewText(valueBox.transform, "—", 40f, Ink,
                new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);

            _seedHintText = NewText(_seedPadGo.transform, "只能用数字；区间 1 ~ 2,147,483,647", 20f, Dim,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -164f), new Vector2(460f, 26f));

            // 3 列 × 4 行：1-9 / 0 / 退格 / 清空
            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "退格", "清空" };
            for (int i = 0; i < keys.Length; i++)
            {
                int col = i % 3, row = i / 3;
                string key = keys[i];
                var btn = NewButton(_seedPadGo.transform, key, Face, Ink, 30f,
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(-170f + col * 170f, -204f - row * 84f), new Vector2(150f, 72f));
                btn.onClick.AddListener(delegate { SeedKey(key); });
            }

            var apply = NewButton(_seedPadGo.transform, "用这个种子", Accent, Ink, 28f,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-85f, -548f), new Vector2(210f, 60f));
            apply.onClick.AddListener(ApplySeedPad);

            var close = NewButton(_seedPadGo.transform, "关闭", Face, Ink, 28f,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(115f, -548f), new Vector2(150f, 60f));
            close.onClick.AddListener(CloseSeedPad);

            _seedPadGo.SetActive(false);
        }

        private static void OpenSeedPad()
        {
            if (_seedPadGo == null) { return; }
            _seedTyped = RunHistorySeed.Pending.HasValue ? RunHistorySeed.Pending.Value.ToString() : "";
            RefreshSeedPadValue();
            _seedPadGo.SetActive(true);
        }

        private static void CloseSeedPad()
        {
            if (_seedPadGo != null) { _seedPadGo.SetActive(false); }
        }

        private static void SeedKey(string key)
        {
            if (key == "清空") { _seedTyped = ""; }
            else if (key == "退格") { if (_seedTyped.Length > 0) { _seedTyped = _seedTyped.Substring(0, _seedTyped.Length - 1); } }
            else if (_seedTyped.Length < 10) { _seedTyped += key; }
            RefreshSeedPadValue();
        }

        private static void RefreshSeedPadValue()
        {
            if (_seedValueText != null) { _seedValueText.text = _seedTyped.Length == 0 ? "—" : _seedTyped; }
        }

        private static void ApplySeedPad()
        {
            int seed;
            if (_seedTyped.Length == 0 || !int.TryParse(_seedTyped, out seed) || seed <= 0)
            {
                if (_seedHintText != null) { _seedHintText.text = "请输入 1 以上的整数"; }
                return;
            }
            RunHistorySeed.SetPending(seed, "手动输入");
            CloseSeedPad();
            RefreshSeedStatus();
        }

        private static void RefreshSeedStatus()
        {
            if (_seedStatusText == null) { return; }
            _seedStatusText.text = RunHistorySeed.Pending.HasValue
                ? "待用种子 " + RunHistorySeed.Pending.Value + "　·　下一次开局生效（用掉即清除）"
                : "待用种子：无";
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
            SetTitleVisible(true);
            RefreshList();
        }

        /// <summary>标题带（文字 + 下方双线）一起显隐：详情页用自己的大标题，线会横穿它。</summary>
        private static void SetTitleVisible(bool visible)
        {
            if (_panelTitle != null) { _panelTitle.gameObject.SetActive(visible); }
            for (int i = 0; i < _titleDecor.Count; i++)
            {
                if (_titleDecor[i] != null) { _titleDecor[i].SetActive(visible); }
            }
        }

        private static void RefreshList()
        {
            try
            {
                var entries = RunHistoryStore.Index.entries;
                _page = Mathf.Clamp(_page, 0, PageCount() - 1);
                var streaks = Streaks();

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
                    Color resultColor = ResultColor(e.result);
                    _rowResultBg[i].color = resultColor;
                    _rowAccent[i].color = new Color(resultColor.r, resultColor.g, resultColor.b, 0.92f);
                    _rowTime[i].text = Duration(e.durationSeconds);
                    _rowNodes[i].text = e.nodes > 0 ? e.nodes + " 节点" : "—";
                    _rowCoins[i].text = e.upgradeCoins > 0 ? e.upgradeCoins.ToString() : "—";

                    // 进阶：0 = 没开进阶（或这局还没到结算，值未知）
                    bool hasAsc = e.ascensionLevel > 0;
                    _rowAsc[i].text = hasAsc ? e.ascensionLevel.ToString() : "—";
                    _rowAsc[i].color = hasAsc ? Ink : Dim;
                    _rowAscFill[i].color = hasAsc ? Chip : new Color(0.90f, 0.87f, 0.81f, 1f);

                    // 连胜：只在「赢」的那一局显示它是第几连胜
                    int streak = 0;
                    streaks.TryGetValue(e.runId, out streak);
                    if (e.result == Results.Win && streak > 0)
                    {
                        _rowStreak[i].text = streak + " 连胜";
                        _rowStreak[i].color = streak >= 2
                            ? new Color(0.55f, 0.36f, 0.10f, 1f)     // 2 连以上：暖色
                            : new Color(0.35f, 0.31f, 0.27f, 1f);
                    }
                    else
                    {
                        _rowStreak[i].text = "—";
                        _rowStreak[i].color = Dim;
                    }
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

        // ---------------- 连胜（派生值，不落盘） ----------------
        //
        // ★ 为什么不把连胜写进每局记录：它是「从旧到新数一遍」的结果，写进去就要在
        //   结算时反查历史、还要处理中断/放弃，反而容易和历史数据不一致。
        //   列表是只读展示，现算最稳：赢 +1；负/放弃清零；中断与进行中**既不计数也不打断**
        //   （它们没有真正结算过，游戏自己的职业统计也不会把它们算成败局）。

        private static Dictionary<string, int> _streakCache;

        private static Dictionary<string, int> Streaks()
        {
            if (_streakCache != null) { return _streakCache; }

            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                var entries = RunHistoryStore.Index.entries;   // 已按时间倒序
                int run = 0;
                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    var e = entries[i];
                    if (e == null) { continue; }
                    if (e.result == Results.Win) { run++; map[e.runId] = run; }
                    else if (e.result == Results.Lose || e.result == Results.Abandon) { run = 0; }
                }
            }
            catch { }
            _streakCache = map;
            return map;
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

        /// <summary>
        /// 同 <see cref="NewImage"/>，但额外指定 pivot。
        /// 贴边的东西（左边色条、行底细线、页脚带）必须把 pivot 也对上，
        /// 否则 6px 的色条会有一半跑出行外。
        /// </summary>
        private static Image NewImageAt(Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 pos, Vector2 size, string name)
        {
            var img = NewImage(parent, color, anchorMin, anchorMax, pos, size, name);
            img.rectTransform.pivot = pivot;
            img.rectTransform.anchoredPosition = pos;
            return img;
        }

        /// <summary>
        /// 四条边拼出来的矩形描边（不用九宫格精灵 —— 这个项目里没有可直接取的圆角纸框）。
        /// 横条拉满宽度，竖条让开上下两条的厚度，四条合起来正好闭合。
        /// </summary>
        private static void AddFrame(Transform parent, Color color, float thickness, float inset)
        {
            float verticalInset = inset + thickness;
            NewImageAt(parent, color, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -inset), new Vector2(-2f * inset, thickness), "FrameTop");
            NewImageAt(parent, color, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, inset), new Vector2(-2f * inset, thickness), "FrameBottom");
            NewImageAt(parent, color, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(inset, 0f), new Vector2(thickness, -2f * verticalInset), "FrameLeft");
            NewImageAt(parent, color, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                new Vector2(-inset, 0f), new Vector2(thickness, -2f * verticalInset), "FrameRight");
        }

        /// <summary>按钮的悬停/按下变色：只压暗不漂白，保持纸质手感。</summary>
        private static ColorBlock TintColors()
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.97f, 0.96f, 0.93f, 1f);
            colors.pressedColor = new Color(0.84f, 0.80f, 0.74f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            colors.fadeDuration = 0.08f;
            return colors;
        }

        /// <summary>
        /// 按钮 = 投影 + 墨线描边 + 面色 + 文字。
        /// 之前是"一块纯色矩形"，跟纸面同色系、又没有边，所以在面板上看不出是按钮。
        /// 描边负责"看得见"，投影负责"抬起来"；面色交给 <see cref="TintColors"/> 做反馈。
        /// </summary>
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

            var btn = go.AddComponent<Button>();

            // 投影（最底层）：往下偏 6px、往右偏 2px
            var shadow = NewImage(go.transform, ButtonShadow, Vector2.zero, Vector2.one,
                new Vector2(2f, -6f), Vector2.zero, "Shadow");
            shadow.raycastTarget = false;

            // 描边（收点击射线：整个按钮范围都能点）
            var border = NewImage(go.transform, BorderInk, Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, "Border");
            border.raycastTarget = true;

            // 面色（2px 内缩，露出墨边；变色反馈挂在这块上）
            var face = NewImage(go.transform, bgColor, Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-4f, -4f), "Face");
            face.raycastTarget = false;

            btn.targetGraphic = face;
            btn.colors = TintColors();

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
        /// <remarks>
        /// ★ 原版职业的中文名在 <c>Encyclopedia</c> 表里，键就是内部名
        ///   （实测 <c>Encyclopedia/Standard_Class_PRV = 莽战士</c>），不是 CardName 也不是 UI 表。
        ///   之前只试了 ClassName/Card/UI，所以显示成了 <c>Standard_Class_PRV</c>。
        /// </remarks>
        private static string ClassLabel(string className)
        {
            if (string.IsNullOrEmpty(className)) { return "（未知职业）"; }
            foreach (var table in new[] { "Encyclopedia", "Card", "UI" })
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
            // 打开动画要连续播，放在 0.5 秒限流之前（不播时这里只是一个布尔判断）
            RunHistoryUi.OnFrame(Time.unscaledDeltaTime);

            if (Time.realtimeSinceStartup < _next) { return; }
            _next = Time.realtimeSinceStartup + 0.5f;
            try { RunHistoryUi.OnHostTick(); }
            catch (Exception ex) { Debug.LogWarning("[RunHistory] 宿主异常 —— " + ex.Message); }
        }
    }
}
