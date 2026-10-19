using System;
using System.Collections.Generic;
using System.Linq;
using Collection;
using HarmonyLib;
using Card;
using Combat;
using Relic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ModKit.Game
{
    /// <summary>
    /// 模组图鉴：主界面右上角一块「模组图鉴」牌子 → 点开只列 mod 内容的图鉴。
    ///
    /// 实现范式：自建根对象 + DontDestroyOnLoad 常驻宿主 + 自造 UI。
    /// 一个游戏对象都不克隆、一个游戏方法都不打补丁（悬停解释除外，仅挂后置补丁）。
    ///
    /// 版面：左右两页，每页一侧的格数由页签决定：
    ///   卡牌 / 关键词 / 能力 = 每侧 2 列 × 2 行（每页 8 格）
    ///   遗物                = 每侧 3 列 × 2 行（每页 12 格）
    /// </summary>
    internal static partial class ModCompendium
    {
        // ---- 素材路径（Resources 下的路径，不含扩展名；留空则使用纯色）----
        // 例：图片放在 Assets/Resources/ModKit/paper.png，则填 "ModKit/paper"
        private const string PaperTexturePath = "";     // 纸页底图
        private const string BorderTexturePath = "";    // 回纹花边（外框）
        private const string ArrowTexturePath = "";     // 翻页箭头（留空则用文字按钮）

        // ---- 内容（跨 mod 累积去重）----
        private static readonly List<CardSo> _cards = new List<CardSo>();
        private static readonly List<RelicSo> _relics = new List<RelicSo>();
        private static readonly List<string> _keywords = new List<string>();
        private static readonly List<string> _abilities = new List<string>();

        private static IGameLog _log;
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _noted = new HashSet<string>(StringComparer.Ordinal);

        // ---- 自建 UI ----
        private const int SlotCount = 12;   // 数组长度按最大每页格数（遗物页）建立
        private static ModCompendiumHost _host;
        private static GameObject _canvasGo;
        private static Canvas _canvas;
        private static GameObject _entryGo;
        private static GameObject _panelGo;
        private static TextMeshProUGUI _entryText;
        private static TextMeshProUGUI _titleText;
        private static TextMeshProUGUI _pageText;
        private static readonly Image[] _slotArt = new Image[SlotCount];
        private static readonly TextMeshProUGUI[] _slotName = new TextMeshProUGUI[SlotCount];
        private static readonly TextMeshProUGUI[] _slotSub = new TextMeshProUGUI[SlotCount];
        private static readonly Image[] _slotHit = new Image[SlotCount];
        private static readonly Button[] _tabButtons = new Button[4];
        private static readonly TextMeshProUGUI[] _tabTexts = new TextMeshProUGUI[4];
        private static Button _prevButton, _nextButton, _closeButton;

        /// <summary>图鉴卡牌页是否显示「升级版」（与游戏自己的图鉴开关同义）。</summary>
        private static bool _showUpgraded;
        private static Button _upgradedButton;
        private static readonly RectTransform[] _pageRoots = new RectTransform[2];
        private static readonly RectTransform[] _slotRoots = new RectTransform[SlotCount];
        private static readonly List<CardBase> _spawned = new List<CardBase>();

        // 卡牌缩放与距离：CardScale 决定尺寸，CardDepth 只决定与相机的距离（不改大小）
        private const float CardScale = 1.2f;
        private const float CardDepth = 6f;
        private const float RelicScale = 1.2f;   // 与原版图鉴 SubCollectionRelics 同值
        private const int CanvasOrder = 1000;    // 自建画布排序号
        private static int _tab;      // 0=卡 1=遗物 2=关键词 3=能力
        private static int _page;
        private static bool _lastInMenu;

        private static readonly string[] TabTitles = { "卡牌", "遗物", "关键词", "能力" };

        private static readonly Color Paper = new Color(0.96f, 0.93f, 0.86f, 1f);
        private static readonly Color Ink = new Color(0.25f, 0.20f, 0.16f, 1f);
        private static readonly Color PanelPaper = new Color(0.94f, 0.91f, 0.85f, 0.99f);
        private static readonly Color BorderInk = new Color(0.25f, 0.20f, 0.16f, 1f);

        // ------------------------------------------------------------------
        //  装配
        // ------------------------------------------------------------------

        /// <summary>安装：读取说明文本、挂悬停补丁、创建常驻宿主。</summary>
        internal static void Install(IGameLog log)
        {
            _log = log;

            try { LoadKeywordText(DiskFileSource.DefaultModsRoot); } catch (Exception ex) { Warn("说明", "关键词文本装载失败 —— " + Describe(ex)); }
            try { LoadRelicText(DiskFileSource.DefaultModsRoot); } catch (Exception ex) { Warn("说明", "遗物文本装载失败 —— " + Describe(ex)); }

            // 悬停解释：挂在 CardBase / RelicHolder 的鼠标进入离开上（后置补丁，不改原逻辑）
            try
            {
                var harmony3 = new Harmony("modkit.compendium.info");
                int okHooks = 0;
                var enter = AccessTools.Method(typeof(CardBase), "OnMouseEnter");
                if (enter != null)
                {
                    harmony3.Patch(enter, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(ModCompendium), nameof(AfterCardEnter))));
                    okHooks++;
                }
                var exit = AccessTools.Method(typeof(CardBase), "OnMouseExit");
                if (exit != null)
                {
                    harmony3.Patch(exit, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(ModCompendium), nameof(AfterCardExit))));
                    okHooks++;
                }
                var relicEnter = AccessTools.Method(typeof(RelicHolder), "OnMouseEnter");
                if (relicEnter != null)
                {
                    harmony3.Patch(relicEnter, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(ModCompendium), nameof(AfterRelicEnter))));
                    okHooks++;
                }
                var relicExit = AccessTools.Method(typeof(RelicHolder), "OnMouseExit");
                if (relicExit != null)
                {
                    harmony3.Patch(relicExit, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(ModCompendium), nameof(AfterRelicExit))));
                    okHooks++;
                }
                if (log != null) { log.Info("模组图鉴：悬停解释已挂钩 " + okHooks + " 处（关键词 " + _kwText.Count + " 个）。"); }
            }
            catch (Exception ex) { Warn("说明", "挂钩悬停解释失败 —— " + Describe(ex)); }

            // 接管原版图鉴的卡列表（备用能力，正常路径不启用）
            try
            {
                var harmony2 = new Harmony("modkit.compendium.view");
                var getDict = AccessTools.Method(typeof(SubCollectionCards), "GetCardDictionaryByBookmarkTitle");
                if (getDict != null)
                {
                    harmony2.Patch(getDict, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(ModCompendium), nameof(AfterGetCardDictionaryByBookmarkTitle))));
                    if (log != null) { log.Info("模组图鉴：已接管原版图鉴的卡列表（mod 卡按原版排版显示）。"); }
                }
                else { Warn("接管", "找不到 GetCardDictionaryByBookmarkTitle，无法接管原版卡列表。"); }
            }
            catch (Exception ex) { Warn("接管", "接管原版图鉴卡列表失败 —— " + Describe(ex)); }

            try
            {
                if (_host != null) { return; }
                var go = new GameObject("ModKit.CompendiumHost");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _host = go.AddComponent<ModCompendiumHost>();
                if (log != null)
                {
                    log.Info("模组图鉴：常驻宿主已创建（自建 DontDestroyOnLoad 对象，不依赖任何游戏 UI 对象）。");
                }
            }
            catch (Exception ex)
            {
                Warn("宿主", "创建模组图鉴宿主失败 —— " + Describe(ex));
            }
        }

        /// <summary>登记一个 mod 的内容（装配流水线在每个 mod 装配完后调用；可多次、自动去重）。</summary>
        internal static void SetContent(string modId, IEnumerable<CardSo> cards, IEnumerable<RelicSo> relics,
            IEnumerable<string> keywords, IEnumerable<string> abilities, IGameLog log)
        {
            _log = log ?? _log;
            try
            {
                int c = AddRange(_cards, cards, x => x != null && !string.IsNullOrEmpty(x.cardName));
                int r = AddRange(_relics, relics, x => x != null && x.relicInfo != null);
                int k = AddRange(_keywords, keywords, x => !string.IsNullOrEmpty(x));
                int a = AddRange(_abilities, abilities, x => !string.IsNullOrEmpty(x));
                if (_log != null)
                {
                    _log.Info("模组图鉴：已收录 " + modId + " 的内容（卡 +" + c + " / 遗物 +" + r +
                        " / 关键词 +" + k + " / 能力 +" + a + "；累计 卡 " + _cards.Count + " / 遗物 " +
                        _relics.Count + " / 关键词 " + _keywords.Count + " / 能力 " + _abilities.Count + "）。");
                }
            }
            catch (Exception ex)
            {
                Warn("收录", "登记 mod 内容到图鉴失败 —— " + Describe(ex));
            }
        }

        private static int AddRange<T>(List<T> target, IEnumerable<T> source, Func<T, bool> ok) where T : class
        {
            if (source == null) { return 0; }
            int added = 0;
            foreach (var item in source)
            {
                if (item == null || !ok(item) || target.Contains(item)) { continue; }
                target.Add(item);
                added++;
            }
            return added;
        }

        private static int AddRange(List<string> target, IEnumerable<string> source, Func<string, bool> ok)
        {
            if (source == null) { return 0; }
            int added = 0;
            foreach (var item in source)
            {
                if (string.IsNullOrEmpty(item) || !ok(item) || target.Contains(item)) { continue; }
                target.Add(item);
                added++;
            }
            return added;
        }

        // ------------------------------------------------------------------
        //  常驻宿主的每个节拍（每 0.5 秒一次）
        // ------------------------------------------------------------------

        internal static void OnHostTick()
        {
            try
            {
                EnsureCanvas();

                // 只在"开始游戏之后"的桌面界面展示
                // 标题界面是 Scenes/Main Menu，桌面界面是 Scenes/Main
                string sceneName;
                bool inMenu = IsDeskHub(out sceneName);
                if (sceneName != _lastScene)
                {
                    _lastScene = sceneName;
                    Note("scene", "当前场景 = " + sceneName + "（桌面界面才显示入口）。");
                }
                if (inMenu != _lastInMenu)
                {
                    _lastInMenu = inMenu;
                    Note("state", inMenu ? "进入桌面界面 → 显示「模组图鉴」入口。" : "离开桌面界面 → 隐藏入口与面板。");
                }

                if (inMenu)
                {
                    EnsureEntry();
                    if (_entryGo != null && !_entryGo.activeSelf) { _entryGo.SetActive(true); }
                }
                else
                {
                    if (_entryGo != null && _entryGo.activeSelf) { _entryGo.SetActive(false); }
                    if (_panelGo != null && _panelGo.activeSelf) { Close(); }
                }
            }
            catch (Exception ex)
            {
                Warn("节拍", "模组图鉴宿主节拍失败 —— " + Describe(ex));
            }
        }

        internal static void HostError(Exception ex)
        {
            Warn("宿主", "宿主 Update 抛出异常 —— " + Describe(ex));
        }

        private static string _lastScene = "";

        /// <summary>
        /// 是否处于「桌面界面」（点"开始游戏"之后的房间）。
        /// 标题界面 = Scenes/Main Menu，桌面/战役界面 = Scenes/Main。
        /// 整个战役流程都在 Scenes/Main 里，战斗中也会显示入口。
        /// </summary>
        private static bool IsDeskHub(out string sceneName)
        {
            sceneName = "";
            try
            {
                var scene = SceneManager.GetActiveScene();
                sceneName = string.IsNullOrEmpty(scene.name) ? (scene.path ?? "") : scene.name;
                bool inMain = scene.name == "Main"
                    || (!string.IsNullOrEmpty(scene.path) &&
                        scene.path.EndsWith("/Main.unity", StringComparison.OrdinalIgnoreCase));
                return inMain;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------
        //  自建 UI
        // ------------------------------------------------------------------

        private static void EnsureCanvas()
        {
            if (_canvasGo != null) { return; }
            _canvasGo = new GameObject("ModKit.CompendiumCanvas", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            _canvas = canvas;
            // 必须是 Screen Space - Camera：原版的卡是世界空间对象，Overlay 画布会永远盖在它们之上
            var cam = Camera.main ?? UnityEngine.Object.FindObjectOfType<Camera>();
            if (cam != null)
            {
                // 照原版 CollectionCanvas：WorldSpace + 0.01 缩放 + 相机前 60，槽位世界坐标天然落在视野内
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = cam;
                var canvasRt0 = _canvasGo.GetComponent<RectTransform>();
                if (canvasRt0 != null)
                {
                    canvasRt0.sizeDelta = new Vector2(1920f, 1080f);
                    canvasRt0.localScale = new Vector3(0.01f, 0.01f, 0.01f);
                }
                PlaceCanvasInFrontOfCamera(cam);
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;   // 兜底：找不到相机只能 Overlay（卡会被纸面遮住）
                Warn("画布", "找不到相机，退回 Overlay 画布（真卡会被纸面遮住）。");
            }
            canvas.sortingOrder = CanvasOrder;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            _canvasGo.AddComponent<GraphicRaycaster>();
            if (_log != null) { _log.Info("模组图鉴：自建画布已创建（" + canvas.renderMode + " / sortingOrder " + canvas.sortingOrder + "）。"); }
        }

        /// <summary>入口牌子：自建（纸色底 + 文字 + Button），摆在右上角。</summary>
        private static void EnsureEntry()
        {
            if (_entryGo != null) { return; }
            _entryGo = new GameObject("ModKit_CompendiumEntry", typeof(RectTransform));
            _entryGo.transform.SetParent(_canvasGo.transform, false);
            var rt = _entryGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-30f, -110f);
            rt.sizeDelta = new Vector2(240f, 96f);

            var img = _entryGo.AddComponent<Image>();
            img.color = Paper;
            img.raycastTarget = true;

            var btn = _entryGo.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(Toggle);

            _entryText = NewText(_entryGo.transform, "模组图鉴", 34f, Ink, new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero);
            if (_log != null)
            {
                var wp = rt.position;
                _log.Info("模组图鉴：入口牌子已挂上（世界坐标 " + wp.x.ToString("0") + "," + wp.y.ToString("0") + "）。");
            }
        }

        /// <summary>是否处于"显示 mod 卡"的原版图鉴视图（备用能力，正常路径不启用）。</summary>
        internal static bool ModCardView;

        private static void Toggle()
        {
            ModCardView = false;
            if (_panelGo != null && _panelGo.activeSelf) { Close(); return; }
            Open();
        }

        /// <summary>后置补丁：mod 卡视图下，把原版图鉴当前书签的卡字典整体换成 mod 卡（备用）。</summary>
        private static void AfterGetCardDictionaryByBookmarkTitle(string bookMarkerName, ref Dictionary<string, CardSo> __result)
        {
            try
            {
                if (!ModCardView) { return; }
                var dict = new Dictionary<string, CardSo>();
                foreach (var c in _cards)
                {
                    if (c == null || string.IsNullOrEmpty(c.cardName)) { continue; }
                    dict[c.cardName] = c;
                }
                __result = dict;
            }
            catch (Exception ex) { Warn("接管", "替换图鉴卡列表失败 —— " + Describe(ex)); }
        }

        private static void Open()
        {
            try
            {
                if (_panelGo == null) { BuildPanel(); }
                if (_panelGo == null) { return; }
                _panelGo.SetActive(true);
                BlockOtherUI(true);          // 隔断：屏蔽其它画布射线与碰撞体，点击不再穿透
                _tab = 0;
                _page = 0;
                Refresh();
            }
            catch (Exception ex) { Warn("面板", "打开模组图鉴失败 —— " + Describe(ex)); }
        }

        private static void Close()
        {
            try { BlockOtherUI(false); } catch { }
            try { ClearSpawnedCards(); } catch { }
            try { ClearSpawnedRelics(); } catch { }   // 关闭时遗物实例必须一并销毁
            try { if (_panelGo != null) { _panelGo.SetActive(false); } }
            catch (Exception ex) { Warn("面板", "关闭模组图鉴失败 —— " + Describe(ex)); }
        }

        private static void BuildPanel()
        {
            _panelGo = new GameObject("ModKit_CompendiumPanel", typeof(RectTransform));
            _panelGo.transform.SetParent(_canvasGo.transform, false);
            var prt = _panelGo.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.5f);
            prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            prt.sizeDelta = new Vector2(1680f, 920f);

            // ① 边框（回纹花边）：比纸页大一圈，最底层。顶边下移 40px（offsetMax.y = -34）
            var border = new GameObject("Border", typeof(RectTransform));
            border.transform.SetParent(_panelGo.transform, false);
            var brt = border.GetComponent<RectTransform>();
            brt.anchorMin = Vector2.zero;
            brt.anchorMax = Vector2.one;
            brt.offsetMin = new Vector2(-6f, -6f);
            brt.offsetMax = new Vector2(6f, -34f);
            var borderImg = border.AddComponent<Image>();
            borderImg.color = Color.white;
            borderImg.raycastTarget = false;
            borderImg.sprite = UiSkin.PanelFrame;          // 程序化九宫格边框（圆角 + 金线 + 内阴影）
            borderImg.type = Image.Type.Sliced;
            ApplyTexture(borderImg, BorderTexturePath, "回纹花边");

            // ② 纸页底（拦截点击）。顶边下移 40px（offsetMax.y = -40）
            var paper = new GameObject("Paper", typeof(RectTransform));
            paper.transform.SetParent(_panelGo.transform, false);
            var pprt = paper.GetComponent<RectTransform>();
            pprt.anchorMin = Vector2.zero;
            pprt.anchorMax = Vector2.one;
            pprt.offsetMin = Vector2.zero;
            pprt.offsetMax = new Vector2(0f, -40f);
            var bg = paper.AddComponent<Image>();
            bg.color = Color.white;
            bg.raycastTarget = true;
            bg.sprite = UiSkin.Paper;                      // 程序化纸纹（可平铺）
            bg.type = Image.Type.Tiled;
            ApplyTexture(bg, PaperTexturePath, "纸页底图");

            // ③ 标题底块（先建，保证画在标题文字下面）
            var titleBg = new GameObject("TitleBg", typeof(RectTransform));
            titleBg.transform.SetParent(_panelGo.transform, false);
            var tbrt = titleBg.GetComponent<RectTransform>();
            tbrt.anchorMin = new Vector2(0.5f, 1f);
            tbrt.anchorMax = new Vector2(0.5f, 1f);
            tbrt.pivot = new Vector2(0.5f, 1f);
            tbrt.anchoredPosition = new Vector2(0f, 42f);
            tbrt.sizeDelta = new Vector2(320f, 70f);
            var tbImg = titleBg.AddComponent<Image>();
            tbImg.color = Paper;
            tbImg.raycastTarget = false;
            tbImg.sprite = UiSkin.RowFrame;                // 标题底块也用细边框
            tbImg.type = Image.Type.Sliced;

            _titleText = NewText(_panelGo.transform, "模组图鉴", 46f, Ink, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 42f), new Vector2(900f, 60f));
            _titleText.alignment = TextAlignmentOptions.Center;

            // 页签：顶部，带描边
            float tx = 790f;
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                _tabButtons[i] = NewButton(_panelGo.transform, TabTitles[i], new Vector2(0f, 1f),
                    new Vector2(tx, -27f), new Vector2(150f, 66f), out _tabTexts[i]);
                tx += 165f;
                if (_tabButtons[i] != null)
                {
                    _tabButtons[i].onClick.AddListener(() => { _tab = idx; _page = 0; Refresh(); });
                }
            }

            // ④ 左侧分类书签（mod 专属，探出面板左边缘）
            var modTab = NewButton(_panelGo.transform, "模组", new Vector2(0f, 0.5f),
                new Vector2(30f, 180f), new Vector2(120f, 56f), out _);
            if (modTab != null)
            {
                var mrt = modTab.transform as RectTransform;
                mrt.pivot = new Vector2(1f, 0.5f);
                mrt.anchoredPosition = new Vector2(30f, 180f);
                modTab.onClick.AddListener(() => { _tab = 0; _page = 0; Refresh(); });
            }

            // ⑤ 翻页按钮：落在面板内侧左右边缘
            _prevButton = NewButton(_panelGo.transform, "上一页", new Vector2(0f, 0.5f), Vector2.zero,
                new Vector2(150f, 66f), out _);
            if (_prevButton != null)
            {
                var prt2 = _prevButton.transform as RectTransform;
                prt2.pivot = new Vector2(0f, 0.5f);
                prt2.anchoredPosition = new Vector2(20f, 0f);
                _prevButton.onClick.AddListener(() => { if (_page > 0) { _page--; Refresh(); } });
            }
            _nextButton = NewButton(_panelGo.transform, "下一页", new Vector2(1f, 0.5f), Vector2.zero,
                new Vector2(150f, 66f), out _);
            if (_nextButton != null)
            {
                var nrt = _nextButton.transform as RectTransform;
                nrt.pivot = new Vector2(1f, 0.5f);
                nrt.anchoredPosition = new Vector2(-20f, 0f);
                _nextButton.onClick.AddListener(() => { _page++; Refresh(); });
            }
            ApplyTexture(_prevButton != null ? _prevButton.GetComponent<Image>() : null, ArrowTexturePath, "翻页箭头");
            ApplyTexture(_nextButton != null ? _nextButton.GetComponent<Image>() : null, ArrowTexturePath, "翻页箭头");

            // 升级版开关（卡牌页专用）

            _upgradedButton = NewButton(_panelGo.transform, UpgradedLabel(), new Vector2(0f, 0f), Vector2.zero,

                new Vector2(170f, 60f), out _);

            if (_upgradedButton != null)

            {

                var urt = _upgradedButton.transform as RectTransform;

                urt.pivot = new Vector2(0f, 0f);

                urt.anchoredPosition = new Vector2(180f, 18f);

                _upgradedButton.onClick.AddListener(() =>

                {

                    _showUpgraded = !_showUpgraded;

                    UpdateUpgradedLabel();

                    Refresh();

                });

                ApplyTexture(_upgradedButton.GetComponent<Image>(), ArrowTexturePath, "升级开关");

            }


            _jumpText = NewText(_panelGo.transform, "", 26f, Ink, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),


                new Vector2(0f, -150f), new Vector2(1200f, 40f));



            _pageText = NewText(_panelGo.transform, "", 28f, Ink, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 25f), new Vector2(320f, 44f));
            BuildInfoStrip();
            _closeButton = NewButton(_panelGo.transform, "关闭", new Vector2(1f, 1f), new Vector2(-45f, -27f),
                new Vector2(150f, 66f), out _);
            if (_closeButton != null) { _closeButton.onClick.AddListener(Close); }

            // ⑥ 书脊：两页之间的竖线
            var spine = new GameObject("Spine", typeof(RectTransform));
            spine.transform.SetParent(_panelGo.transform, false);
            var srt = spine.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0.5f, 0f);
            srt.anchorMax = new Vector2(0.5f, 1f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = Vector2.zero;
            srt.sizeDelta = new Vector2(8f, 0f);
            var spineImg = spine.AddComponent<Image>();
            spineImg.color = new Color(0.55f, 0.45f, 0.35f, 0.6f);
            spineImg.raycastTarget = false;

            // ⑦ 左右两页容器（格子在 LayoutSlots 里按页挂到对应容器下）
            for (int page = 0; page < 2; page++)
            {
                var pageRoot = new GameObject("Page" + page, typeof(RectTransform));
                pageRoot.transform.SetParent(_panelGo.transform, false);
                var pageRt = pageRoot.GetComponent<RectTransform>();
                pageRt.anchorMin = new Vector2(0.5f, 0.5f);
                pageRt.anchorMax = new Vector2(0.5f, 0.5f);
                pageRt.pivot = new Vector2(0.5f, 0.5f);
                pageRt.anchoredPosition = new Vector2(page == 0 ? -380f : 380f, 0f);
                pageRt.sizeDelta = new Vector2(760f, 800f);
                _pageRoots[page] = pageRt;
            }

            // ⑧ 12 个格子（卡牌页只用前 8 个，遗物页用满 12 个）
            for (int slot = 0; slot < SlotCount; slot++)
            {
                var cell = new GameObject("Cell" + slot, typeof(RectTransform));
                cell.transform.SetParent(_pageRoots[0], false);
                var crt = cell.GetComponent<RectTransform>();
                _slotRoots[slot] = crt;

                // 透明命中区：只在有说明时开启射线，用于关键词/能力的悬停解释
                var hit = cell.AddComponent<Image>();
                hit.color = new Color(1f, 1f, 1f, 0f);
                hit.raycastTarget = false;
                _slotHit[slot] = hit;
                var hover = cell.AddComponent<SlotHover>();
                hover.Slot = slot;

                var icon = new GameObject("Icon", typeof(RectTransform));
                icon.transform.SetParent(cell.transform, false);
                var irt = icon.GetComponent<RectTransform>();
                irt.anchorMin = new Vector2(0.5f, 1f);
                irt.anchorMax = new Vector2(0.5f, 1f);
                irt.pivot = new Vector2(0.5f, 1f);
                irt.anchoredPosition = Vector2.zero;
                irt.sizeDelta = new Vector2(165f, 218f);
                var img = icon.AddComponent<Image>();
                img.preserveAspect = true;
                img.raycastTarget = false;
                _slotArt[slot] = img;

                _slotName[slot] = NewText(cell.transform, "", 27f, Ink, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -196f), new Vector2(250f, 40f));
                _slotSub[slot] = NewText(cell.transform, "", 21f, new Color(0.45f, 0.38f, 0.30f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -232f), new Vector2(250f, 40f));
            }

            _panelGo.SetActive(false);
            if (_log != null) { _log.Info("模组图鉴：面板已建立（左右两页，卡牌页每页 8 格 / 遗物页每页 12 格）。"); }
        }

        /// <summary>给 Image 挂上 Resources 里的素材。路径为空或加载失败时保持纯色。</summary>
        private static void ApplyTexture(Image img, string path, string label)
        {
            if (img == null || string.IsNullOrEmpty(path)) { return; }
            var sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
            {
                Warn("素材", "找不到" + label + "：Resources/" + path + "（已退回纯色）。");
                return;
            }
            img.sprite = sprite;
            img.color = Color.white;
        }

        private static Button NewButton(Transform parent, string text, Vector2 anchor, Vector2 pos,
            Vector2 size, out TextMeshProUGUI label)
        {
            var go = new GameObject("Btn_" + text, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = Paper;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0.45f, 0.35f, 0.25f, 1f);
            outline.effectDistance = new Vector2(2f, -2f);

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            label = NewText(go.transform, text, 30f, Ink, new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            return btn;
        }

        private static TextMeshProUGUI NewText(Transform parent, string text, float size, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pos, Vector2 sizeDelta)
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
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            var font = ResolveFont();
            if (font != null) { t.font = font; }
            return t;
        }

        /// <summary>字体：优先游戏 TMP 默认字体（保证中文能显示）。</summary>
        private static TMP_FontAsset _font;
        private static TMP_FontAsset ResolveFont()
        {
            if (_font != null) { return _font; }
            try { _font = TMP_Settings.defaultFontAsset; } catch { }
            return _font;
        }

        // ------------------------------------------------------------------
        //  版面：按页签决定每侧格数，并把格子挂到对应的左/右页容器下
        // ------------------------------------------------------------------

        /// <summary>每页格数：遗物页 12（每侧 3×2），其余页 8（每侧 2×2）。</summary>
        private static int PerPage()
        {
            return _tab == 1 ? 12 : 8;
        }

        /// <summary>
        /// 版面：每页分左右两侧，格子按顺序先填满左页再填右页。
        /// 卡牌/关键词/能力：每侧 2 列 × 2 行（大格）；遗物：每侧 3 列 × 2 行（紧凑）。
        /// </summary>
        private static void LayoutSlots(int perPage)
        {
            bool relicPage = _tab == 1;
            int perSide = perPage / 2;
            int cols = relicPage ? 3 : 2;
            float cellW = relicPage ? 195f : 292f;
            float cellH = relicPage ? 215f : 385f;
            float pitchX = relicPage ? 205f : 300f;
            float pitchY = relicPage ? 235f : 360f;
            float originX = 207f;   // 拍屏校准：整体右移 ~137px
            float originY = relicPage ? -205f : -215f;   // 整体下移 ~165px

            for (int i = 0; i < SlotCount; i++)
            {
                var crt = _slotRoots[i];
                if (crt == null) { continue; }
                bool used = i < perPage;
                crt.gameObject.SetActive(used);
                if (!used) { continue; }

                int side = i / perSide;          // 0 = 左页，1 = 右页
                int local = i % perSide;         // 页内序号
                int col = local % cols;
                int row = local / cols;

                if (crt.parent != _pageRoots[side]) { crt.SetParent(_pageRoots[side], false); }
                crt.anchorMin = new Vector2(0f, 1f);
                crt.anchorMax = new Vector2(0f, 1f);
                crt.pivot = new Vector2(0f, 1f);
                crt.sizeDelta = new Vector2(cellW, cellH);
                crt.anchoredPosition = new Vector2(originX + col * pitchX, originY - row * pitchY);
            }
        }

        // ------------------------------------------------------------------
        //  内容刷新
        // ------------------------------------------------------------------

        /// <summary>用游戏自己的卡牌绘制生成一张真卡（与原版图鉴同一套 CardManager.GenerateCardObjFromCardInfo）。</summary>
        /// <summary>WorldSpace 画布跟随相机摆放（原版 CollectionCanvas 在相机前约 60 单位）。</summary>
        private static void PlaceCanvasInFrontOfCamera(Camera cam)
        {
            if (_canvas == null || cam == null) { return; }
            if (_canvas.renderMode != RenderMode.WorldSpace) { return; }
            try
            {
                var t = _canvas.transform;
                t.position = cam.transform.position + cam.transform.forward * 60f;
                t.rotation = cam.transform.rotation;
            }
            catch { }
        }

        private static string UpgradedLabel()

        {

            return _showUpgraded ? "升级版 ✓" : "升级版";

        }


        private static void UpdateUpgradedLabel()

        {

            try

            {

                if (_upgradedButton == null) { return; }

                var txt = _upgradedButton.GetComponentInChildren<TMPro.TMP_Text>();

                if (txt != null) { txt.text = UpgradedLabel(); }

            }

            catch { }

        }


        private static bool SpawnRealCard(CardSo so, int slot)
        {
            try
            {
                var cam = Camera.main;
                var slotRt = _slotRoots[slot];
                if (cam == null || slotRt == null || so == null) { return false; }
                var cm = CardManager.Instance;
                if (cm == null) { return false; }

                // WorldSpace 画布下槽位世界坐标即相机世界坐标（原版也直接传 cardPos[i].position）

                Vector3 world = slotRt.position;
                var cb = cm.GenerateCardObjFromCardInfo(so.CardInfo, world, OwnerShip.Preview, CardScale);
                if (cb == null) { return false; }

                // 升级版开关：与游戏自己的图鉴同一套做法（CardManager.UpgradeCard 就地换卡）
                if (_showUpgraded)
                {
                    try
                    {
                        if (!cm.UpgradeCard(cb))
                        {
                            Warn("升级", "这张卡没有可用的升级数据（upgrade 块），保持原样：" + so.cardName);
                        }
                    }
                    catch (Exception ex) { Warn("升级", "切到升级版失败：" + Describe(ex)); }
                }
                // 开碰撞体，鼠标悬停才能触发游戏的 TooltipTrigger
                try { cb.SetCollider(isActive: true); } catch { }

                // 卡的渲染器排序号低于画布，整体抬高到画布之上（保留内部层次）
                var rs = cb.GetComponentsInChildren<Renderer>(true);
                int minOrder = int.MaxValue;
                foreach (var r in rs) { if (r != null && r.sortingOrder < minOrder) { minOrder = r.sortingOrder; } }
                if (minOrder == int.MaxValue) { minOrder = 0; }
                int delta = (CanvasOrder + 10) - minOrder;
                foreach (var r in rs)
                {
                    if (r == null) { continue; }
                    r.sortingLayerID = 0;
                    r.sortingOrder = r.sortingOrder + delta;
                }
                // 卡内嵌套 Canvas 必须 overrideSorting 才压得过纸面
                var cvs = cb.GetComponentsInChildren<Canvas>(true);
                foreach (var cv in cvs)
                {
                    if (cv == null || cv == _canvas) { continue; }
                    cv.overrideSorting = true;
                    cv.sortingOrder = CanvasOrder + 500;
                }
                _spawned.Add(cb);
                return true;
            }
            catch (Exception ex)
            {
                Warn("真卡", "用游戏卡牌绘制生成卡失败，回退缩略图 —— " + Describe(ex));
                return false;
            }
        }

        /// <summary>回收真卡（翻页 / 切页签 / 关闭面板时调用）。</summary>
        private static void ClearSpawnedCards()
        {
            if (_spawned.Count > 0)
            {
                try
                {
                    CardManager cm = null;
                    try { cm = CardManager.Instance; } catch { }
                    for (int i = 0; i < _spawned.Count; i++)
                    {
                        var cb = _spawned[i];
                        if (cb == null) { continue; }
                        if (cm != null) { cm.RemoveCardFromGame(cb); }
                        else { UnityEngine.Object.Destroy(cb.gameObject); }
                    }
                }
                catch (Exception ex) { Warn("清理", "清理真卡失败 —— " + Describe(ex)); }
                _spawned.Clear();
            }
            ClearSpawnedRelics();
            HideInfo();
        }

        // ---- 悬停解释框（平台自建：卡面效果文字 + 关键词说明）----
        private static readonly Dictionary<string, string[]> _kwText = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<KeyValuePair<string, int>>> _cardKeywords =
            new Dictionary<string, List<KeyValuePair<string, int>>>(StringComparer.Ordinal);
        /// <summary>遗物文本：id → 显示名 / 说明 / 触发点。</summary>
        private static readonly Dictionary<string, string[]> _relicText = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private static readonly List<RelicHolder> _spawnedRelics = new List<RelicHolder>();
        private static readonly List<GameObject> _relicAnchors = new List<GameObject>();
        private static readonly Dictionary<RelicHolder, RelicSo> _relicSoOf = new Dictionary<RelicHolder, RelicSo>();
        private static GameObject _tipGo;
        private static TextMeshProUGUI _tipText;

        /// <summary>读取各 mod 的 keywords/*.json 与 cards/*.json 中的关键词，取显示名与说明（zh-Hans 优先）。</summary>
        private static void LoadKeywordText(string modsRoot)
        {
            try
            {
                if (string.IsNullOrEmpty(modsRoot) || !System.IO.Directory.Exists(modsRoot)) { return; }
                foreach (var modDir in System.IO.Directory.GetDirectories(modsRoot))
                {
                    var kd = System.IO.Path.Combine(modDir, "keywords");
                    if (!System.IO.Directory.Exists(kd)) { continue; }
                    var cd = System.IO.Path.Combine(modDir, "cards");
                    if (System.IO.Directory.Exists(cd))
                    {
                        foreach (var cf in System.IO.Directory.GetFiles(cd, "*.json"))
                        {
                            try
                            {
                                var croot = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(cf));
                                var cid = (string)croot["id"];
                                var kws = croot["keywords"] as Newtonsoft.Json.Linq.JArray;
                                if (string.IsNullOrEmpty(cid) || kws == null || _cardKeywords.ContainsKey(cid)) { continue; }
                                var list = new List<KeyValuePair<string, int>>();
                                foreach (var node in kws)
                                {
                                    if (node.Type == Newtonsoft.Json.Linq.JTokenType.String)
                                    {
                                        list.Add(new KeyValuePair<string, int>((string)node, 0));
                                    }
                                    else
                                    {
                                        var kn = (string)node["name"];
                                        var num = node["number"] == null ? 0 : (int)node["number"];
                                        if (!string.IsNullOrEmpty(kn)) { list.Add(new KeyValuePair<string, int>(kn, num)); }
                                    }
                                }
                                _cardKeywords[cid] = list;
                            }
                            catch { }
                        }
                    }

                    foreach (var file in System.IO.Directory.GetFiles(kd, "*.json"))
                    {
                        try
                        {
                            var root = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(file));
                            var id = (string)root["id"];
                            if (string.IsNullOrEmpty(id) || _kwText.ContainsKey(id)) { continue; }
                            _kwText[id] = new[] { PickZh(root["name"]), PickZh(root["description"]) };
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { Warn("说明", "读取关键词文本失败 —— " + Describe(ex)); }
        }

        private static void LoadRelicText(string modsRoot)
        {
            try
            {
                if (string.IsNullOrEmpty(modsRoot) || !System.IO.Directory.Exists(modsRoot)) { return; }
                foreach (var modDir in System.IO.Directory.GetDirectories(modsRoot))
                {
                    var rd = System.IO.Path.Combine(modDir, "relics");
                    if (!System.IO.Directory.Exists(rd)) { continue; }
                    foreach (var file in System.IO.Directory.GetFiles(rd, "*.json"))
                    {
                        try
                        {
                            var root = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(file));
                            var id = (string)root["id"];
                            if (string.IsNullOrEmpty(id) || _relicText.ContainsKey(id)) { continue; }
                            _relicText[id] = new[] { PickZh(root["name"]), PickZh(root["description"]), (string)root["trigger"] ?? string.Empty };
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex) { Warn("说明", "读取遗物文本失败 —— " + Describe(ex)); }
        }

        /// <summary>取游戏本地化表里的文本（表名：Relic / KeyWord / EffectDescription 等，键为"modId.内容id"）。</summary>
        private static string Localized(string table, string key)
        {
            if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(key)) { return string.Empty; }
            try
            {
                var s = new UnityEngine.Localization.LocalizedString(table, key).GetLocalizedString();
                if (IsMissingTranslation(s, key)) { return string.Empty; }
                return s;
            }
            catch { return string.Empty; }
        }

        /// <summary>Unity 本地化缺条目时会返回占位串，一律当缺失处理。</summary>
        private static bool IsMissingTranslation(string text, string key)
        {
            if (string.IsNullOrEmpty(text)) { return true; }
            if (!string.IsNullOrEmpty(key) && text == key) { return true; }
            var lower = text.ToLowerInvariant();
            return lower.Contains("no translation") || lower.Contains("no translate")
                || lower.Contains("notranslation") || lower.Contains("missing translation")
                || lower.Contains("no entry") || lower.Contains("找不到");
        }

        /// <summary>先按原样取，取不到再尝试补各 mod 前缀。</summary>
        private static string LocalizedAny(string table, string key)
        {
            var s = Localized(table, key);
            if (!string.IsNullOrEmpty(s)) { return s; }
            if (string.IsNullOrEmpty(key) || key.IndexOf('.') >= 0) { return string.Empty; }
            foreach (var id in _cards.Select(x => x.cardName).Concat(_relics.Select(x => x.name)))
            {
                if (string.IsNullOrEmpty(id)) { continue; }
                int dot = id.IndexOf('.');
                if (dot <= 0) { continue; }
                s = Localized(table, id.Substring(0, dot) + "." + key);
                if (!string.IsNullOrEmpty(s)) { return s; }
            }
            return string.Empty;
        }

        private static string RelicDisplayName(string id)
        {
            var loc = LocalizedAny("Relic", id);
            if (!string.IsNullOrEmpty(loc)) { return loc; }
            string[] v;
            if (!string.IsNullOrEmpty(id) && _relicText.TryGetValue(id, out v) && !string.IsNullOrEmpty(v[0])) { return v[0]; }
            return Short(id);
        }

        private static string PickZh(Newtonsoft.Json.Linq.JToken node)
        {
            if (node == null) { return string.Empty; }
            if (node.Type == Newtonsoft.Json.Linq.JTokenType.String) { return (string)node; }
            var zh = (string)node["zh-Hans"];
            if (!string.IsNullOrEmpty(zh)) { return zh; }
            var en = (string)node["en"];
            return string.IsNullOrEmpty(en) ? string.Empty : en;
        }

        /// <summary>面板底部的说明框（悬停时显示，不遮挡卡牌）。</summary>
        private static void BuildInfoStrip()
        {
            if (_panelGo == null || _tipGo != null) { return; }
            _tipGo = new GameObject("ModKit_CardInfo", typeof(RectTransform));
            _tipGo.transform.SetParent(_canvasGo.transform, false);
            var rt = _tipGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(560f, 160f);
            rt.anchoredPosition = new Vector2(100f, -120f);
            // 独立子画布：说明框要压在卡（world sprite）之上
            var tipCanvas = _tipGo.AddComponent<Canvas>();
            tipCanvas.overrideSorting = true;
            tipCanvas.sortingOrder = CanvasOrder + 200;
            var img = _tipGo.AddComponent<Image>();
            img.color = new Color(0.16f, 0.14f, 0.12f, 0.94f);
            img.raycastTarget = false;

            var tgo = new GameObject("Text", typeof(RectTransform));
            tgo.transform.SetParent(_tipGo.transform, false);
            var trt = tgo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(26f, 18f);
            trt.offsetMax = new Vector2(-26f, -18f);
            _tipText = tgo.AddComponent<TextMeshProUGUI>();
            if (_titleText != null) { _tipText.font = _titleText.font; }
            _tipText.fontSize = 24f;
            _tipText.color = new Color(0.94f, 0.92f, 0.88f);
            _tipText.alignment = TextAlignmentOptions.TopLeft;
            _tipText.textWrappingMode = TextWrappingModes.Normal;
            _tipText.raycastTarget = false;
            _tipGo.SetActive(false);
        }

        /// <summary>悬停真卡：生成卡面关键词说明。</summary>
        private static void ShowInfo(CardBase cb)
        {
            if (cb == null || _tipText == null) { return; }
            if (!_spawned.Contains(cb)) { return; }
            try
            {
                var sb = new System.Text.StringBuilder();

                CardInfo info = default(CardInfo);
                bool have = false;
                try { info = cb.GetCardDeepCopyInfo(); have = true; } catch { }

                var kwList = new List<KeyValuePair<string, int>>();
                if (have && info.cardKeyWordInfos != null && info.cardKeyWordInfos.Length > 0)
                {
                    for (int i = 0; i < info.cardKeyWordInfos.Length; i++)
                    {
                        var kwi = info.cardKeyWordInfos[i];
                        kwList.Add(new KeyValuePair<string, int>(kwi.keywordName ?? string.Empty, kwi.number));
                    }
                }
                else if (have)
                {
                    // 运行时关键词为空时，退回 mod 的卡 JSON（游戏侧 cardName 带 "modId." 前缀）
                    string cardId = info.cardName ?? string.Empty;
                    List<KeyValuePair<string, int>> fromJson;
                    if (!_cardKeywords.TryGetValue(cardId, out fromJson))
                    {
                        int dot = cardId.IndexOf('.');
                        if (dot > 0) { _cardKeywords.TryGetValue(cardId.Substring(dot + 1), out fromJson); }
                    }
                    if (fromJson != null) { kwList.AddRange(fromJson); }
                }

                for (int i = 0; i < kwList.Count; i++)
                {
                    var kw = kwList[i];
                    var kwId = kw.Key ?? string.Empty;

                    // 名称与说明优先取游戏本地化（KeyWord 表：<id> = 名称，<id>Description = 说明）
                    string[] kvJson = KwJson(kwId);
                    var kwName = (kvJson != null && !string.IsNullOrEmpty(kvJson[0])) ? kvJson[0] : LocalizedAny("KeyWord", kwId);
                    var kwDesc = (kvJson != null && !string.IsNullOrEmpty(kvJson[1])) ? kvJson[1] : LocalizedAny("KeyWord", kwId + "Description");
                    if (string.IsNullOrEmpty(kwName) || string.IsNullOrEmpty(kwDesc))
                    {
                        string[] kv;
                        if (_kwText.TryGetValue(kwId, out kv))
                        {
                            if (string.IsNullOrEmpty(kwName)) { kwName = kv[0]; }
                            if (string.IsNullOrEmpty(kwDesc)) { kwDesc = kv[1]; }
                        }
                    }
                    if (string.IsNullOrEmpty(kwName) && string.IsNullOrEmpty(kwDesc)) { continue; }

                    sb.Append('\n').Append("<b>").Append(kwName);
                    if (kw.Value > 0) { sb.Append(kw.Value); }
                    sb.Append("</b>\n");
                    if (!string.IsNullOrEmpty(kwDesc)) { sb.Append(kwDesc).Append('\n'); }
                }

                var body = sb.ToString().TrimEnd();
                if (body.Length == 0) { HideInfo(); return; }
                _tipText.text = body;
                _tipText.ForceMeshUpdate();

                var rt = _tipGo.GetComponent<RectTransform>();
                float h = Mathf.Clamp(_tipText.preferredHeight + 32f, 80f, 520f);
                rt.sizeDelta = new Vector2(560f, h);
                PlaceTipNear(cb.transform.position, 560f, h, 170f);
                _tipGo.SetActive(true);
            }
            catch (Exception ex) { Warn("说明", "生成解释内容失败 —— " + Describe(ex)); }
        }

        /// <summary>把说明框放到某个世界坐标的旁边（右侧放不下则翻到左侧）。</summary>
        private static void PlaceTipNear(Vector3 worldPos, float tipWidth, float h, float offsetX)
        {
            var rt = _tipGo.GetComponent<RectTransform>();
            var cam = Camera.main;
            if (cam == null) { return; }
            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            float cx = sp.x * 1920f / Mathf.Max(1f, Screen.width);
            float cy = (Screen.height - sp.y) * 1080f / Mathf.Max(1f, Screen.height);
            float bx = cx + offsetX;
            if (bx + tipWidth > 1880f) { bx = cx - tipWidth - offsetX; }
            float by = cy - h * 0.45f;
            bx = Mathf.Clamp(bx, 8f, 1880f - tipWidth);
            by = Mathf.Clamp(by, 8f, 1060f);
            rt.anchoredPosition = new Vector2(bx, -by);
        }

        private static void HideInfo()
        {
            if (_tipGo != null && _tipGo.activeSelf) { _tipGo.SetActive(false); }
        }

        private static void AfterCardEnter(CardBase __instance)
        {
            if (__instance == null) { return; }
            try { ShowInfo(__instance); } catch { }
        }

        private static void AfterCardExit(CardBase __instance)
        {
            if (__instance == null) { return; }
            if (!_spawned.Contains(__instance)) { return; }
            HideInfo();
        }

        /// <summary>
        /// 用游戏自己的遗物对象渲染一格（与原版 SubCollectionRelics.ShowRelicsInThisPage 同一套调用）。
        /// RelicHolder 内部会把 localPosition 归零，所以先给它一个格子锚点当父节点。
        /// </summary>
        private static bool SpawnRealRelic(RelicSo so, int slot)
        {
            try
            {
                var cam = Camera.main;
                var slotRt = _slotRoots[slot];
                var rm = RelicManager.Instance;
                if (cam == null || slotRt == null || so == null || rm == null) { return false; }

                // WorldSpace 画布下槽位世界坐标即相机世界坐标（原版也直接传 cardPos[i].position）

                Vector3 world = slotRt.position;

                var anchor = new GameObject("ModKit_RelicSlot" + slot);
                anchor.transform.position = world;
                anchor.transform.localScale = Vector3.one;
                _relicAnchors.Add(anchor);

                var holder = rm.GenerateRelic(so.relicInfo, OwnerShip.Preview);
                if (holder == null) { return false; }
                try { holder.col2D.enabled = true; } catch { }
                holder.transform.SetParent(anchor.transform, true);
                holder.transform.localPosition = Vector3.zero;
                holder.transform.localScale = Vector3.one * RelicScale;

                int minOrder = int.MaxValue;
                var rs = holder.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++) { if (rs[i] != null && rs[i].sortingOrder < minOrder) { minOrder = rs[i].sortingOrder; } }
                if (minOrder == int.MaxValue) { minOrder = 0; }
                int delta = (CanvasOrder + 10) - minOrder;
                for (int i = 0; i < rs.Length; i++)
                {
                    if (rs[i] == null) { continue; }
                    rs[i].sortingLayerID = 0;
                    rs[i].sortingOrder = rs[i].sortingOrder + delta;
                }
                var cvs = holder.GetComponentsInChildren<Canvas>(true);
                for (int i = 0; i < cvs.Length; i++)
                {
                    if (cvs[i] == null || cvs[i] == _canvas) { continue; }
                    cvs[i].overrideSorting = true;
                    cvs[i].sortingOrder = CanvasOrder + 500;
                }

                _spawnedRelics.Add(holder);
                _relicSoOf[holder] = so;
                return true;
            }
            catch (Exception ex)
            {
                Warn("遗物", "生成遗物对象失败，回退缩略图 —— " + Describe(ex)); 
                return false;
            }
        }

        private static void ClearSpawnedRelics()
        {
            if (_spawnedRelics.Count > 0)
            {
                try
                {
                    RelicManager rm = null;
                    try { rm = RelicManager.Instance; } catch { }
                    for (int i = 0; i < _spawnedRelics.Count; i++)
                    {
                        var h = _spawnedRelics[i];
                        if (h == null) { continue; }
                        if (rm != null) { rm.RemoveRelicFromGame(h); }
                        else { UnityEngine.Object.Destroy(h.gameObject); }
                    }
                }
                catch (Exception ex) { Warn("遗物", "清理遗物对象失败 —— " + Describe(ex)); }
            }
            for (int i = 0; i < _relicAnchors.Count; i++) { if (_relicAnchors[i] != null) { UnityEngine.Object.Destroy(_relicAnchors[i]); } }
            _relicAnchors.Clear();
            _relicSoOf.Clear();
            _spawnedRelics.Clear();
        }

        /// <summary>遗物悬停说明：名称走 Relic 本地化表，效果句用 RelicHolder.Relic.GetEffectDescription(true)。</summary>
        private static void ShowRelicInfo(RelicHolder holder)
        {
            if (holder == null || _tipText == null || _tipGo == null) { return; }
            if (!_spawnedRelics.Contains(holder)) { return; }
            try
            {
                RelicSo so;
                _relicSoOf.TryGetValue(holder, out so);
                var id = so != null && so.relicInfo != null ? so.relicInfo.relicName : null;

                var sb = new System.Text.StringBuilder();
                var nameText = LocalizedAny("Relic", id);
                if (string.IsNullOrEmpty(nameText)) { nameText = RelicDisplayName(id); }
                sb.Append("<b>").Append(nameText).Append("</b>\n");

                var effect = string.Empty;
                try { effect = holder.Relic.GetEffectDescription(true); } catch { }
                if (!string.IsNullOrEmpty(effect)) { sb.Append(effect).Append('\n'); }
                else
                {
                    var desc = LocalizedAny("EffectDescription", id);
                    if (!string.IsNullOrEmpty(desc)) { sb.Append(desc).Append('\n'); }
                }

                _tipText.text = sb.ToString().TrimEnd();
                _tipText.ForceMeshUpdate();
                const float tipWidth = 440f;
                float h = Mathf.Clamp(_tipText.preferredHeight + 32f, 80f, 520f);
                _tipGo.GetComponent<RectTransform>().sizeDelta = new Vector2(tipWidth, h);
                PlaceTipNear(holder.transform.position, tipWidth, h, 60f);
                _tipGo.SetActive(true);
            }
            catch (Exception ex) { Warn("遗物", "显示遗物说明失败 —— " + Describe(ex)); }
        }

        private static void AfterRelicEnter(RelicHolder __instance) { try { ShowRelicInfo(__instance); } catch { } }

        private static void AfterRelicExit(RelicHolder __instance)
        {
            if (__instance == null) { return; }
            if (!_spawnedRelics.Contains(__instance)) { return; }
            HideInfo();
        }

        /// <summary>格子的悬停组件：关键词/能力格进入时显示全文。</summary>
        internal sealed class SlotHover : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
        {
            public int Slot;

            public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData) { ShowSlotInfo(Slot); }

            public void OnPointerExit(UnityEngine.EventSystems.PointerEventData eventData) { HideInfo(); }
        }

        private static void ShowSlotInfo(int slot)
        {
            try
            {
                if (_tipText == null || _tipGo == null) { return; }
                var entries = CurrentEntries();
                int idx = _page * PerPage() + slot;
                if (idx < 0 || idx >= entries.Count) { return; }
                var info = entries[idx].Info;
                if (string.IsNullOrEmpty(info)) { return; }

                _tipText.text = info;
                _tipText.ForceMeshUpdate();
                const float tipWidth = 440f;
                float h = Mathf.Clamp(_tipText.preferredHeight + 32f, 80f, 520f);
                _tipGo.GetComponent<RectTransform>().sizeDelta = new Vector2(tipWidth, h);

                var slotRt = (slot >= 0 && slot < _slotRoots.Length) ? _slotRoots[slot] : null;
                if (slotRt != null)
                {
                    PlaceTipNear(slotRt.TransformPoint(slotRt.rect.center), tipWidth, h, 60f);
                }
                _tipGo.SetActive(true);
            }
            catch (Exception ex) { Warn("说明", "显示格子说明失败 —— " + Describe(ex)); }
        }

        // ---- 输入隔断 ----
        private static readonly List<Behaviour> _mutedRaycasters = new List<Behaviour>();
        private static readonly List<Collider> _mutedColliders = new List<Collider>();
        private static readonly List<Collider2D> _mutedColliders2D = new List<Collider2D>();
        private static GameObject _blockerGo;

        /// <summary>
        /// 隔断：开面板时关闭其它画布的 GraphicRaycaster 与游戏对象的碰撞体，
        /// 并在我们画布最底层放一张全屏挡板（几乎透明但接收射线）。
        /// </summary>
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
                        _blockerGo.transform.SetAsFirstSibling();
                        var brt = _blockerGo.GetComponent<RectTransform>();
                        brt.anchorMin = Vector2.zero;
                        brt.anchorMax = Vector2.one;
                        brt.offsetMin = Vector2.zero;
                        brt.offsetMax = Vector2.zero;
                        var img = _blockerGo.AddComponent<Image>();
                        img.color = new Color(0f, 0f, 0f, 0.004f);
                        img.raycastTarget = true;
                    }
                    if (_blockerGo != null) { _blockerGo.SetActive(true); }
                    if (_log != null) { _log.Info("模组图鉴：硬隔断已开启（屏蔽 " + _mutedRaycasters.Count + " 个画布射线 / " + _mutedColliders.Count + " 个3D碰撞体 / " + _mutedColliders2D.Count + " 个2D碰撞体）。"); }
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
                    for (int i = 0; i < _mutedColliders.Count; i++) { if (_mutedColliders[i] != null) { _mutedColliders[i].enabled = true; } }
                    for (int i = 0; i < _mutedColliders2D.Count; i++) { if (_mutedColliders2D[i] != null) { _mutedColliders2D[i].enabled = true; } }
                    _mutedColliders.Clear();
                    _mutedColliders2D.Clear();
                }
            }
            catch (Exception ex) { Warn("隔断", "输入隔断切换失败 —— " + Describe(ex)); }
        }

        private static void Refresh()
        {
            try
            {
                PlaceCanvasInFrontOfCamera(Camera.main);
                var entries = CurrentEntries();
                int perPage = PerPage();
                LayoutSlots(perPage);
                int pages = Math.Max(1, (entries.Count + perPage - 1) / perPage);
                if (_page >= pages) { _page = pages - 1; }
                if (_page < 0) { _page = 0; }

                if (_titleText != null) { _titleText.text = "模组图鉴 · " + TabTitles[_tab]; }
                if (_pageText != null) { _pageText.text = (_page + 1) + " / " + pages + "　共 " + entries.Count + " 项"; }
                if (_upgradedButton != null)
                {
                    _upgradedButton.gameObject.SetActive(_tab == 0);
                    UpdateUpgradedLabel();
                }
                if (_prevButton != null) { _prevButton.interactable = _page > 0; }
                if (_nextButton != null) { _nextButton.interactable = _page < pages - 1; }

                ClearSpawnedCards();   // 内部会同时清理遗物
                HideInfo();

                for (int i = 0; i < perPage; i++)
                {
                    int idx = _page * perPage + i;
                    bool has = idx < entries.Count;
                    var e = has ? entries[idx] : null;

                    // 卡与遗物优先用游戏自己的对象渲染，失败才回退成缩略图 + 文字
                    bool realCard = (e != null && e.Card != null) && SpawnRealCard(e.Card, i);
                    bool realRelic = (e != null && e.Relic != null) && SpawnRealRelic(e.Relic, i);
                    bool real = realCard || realRelic;

                    bool showIcon = !real && e != null && e.Icon != null;
                    _slotArt[i].sprite = showIcon ? e.Icon : null;
                    _slotArt[i].color = showIcon ? Color.white : new Color(1f, 1f, 1f, 0f);

                    _slotName[i].text = (real || e == null) ? "" : e.Name;
                    _slotSub[i].text = (real || e == null) ? "" : e.Sub;

                    // 关键词页把整段说明直接写在格子里（自建 uGUI 的指针悬停不可靠，不依赖它）
                    if (_tab == 2 && _slotSub[i] != null)
                    {
                        if (e != null && !real && !string.IsNullOrEmpty(e.Desc))
                        {
                            _slotSub[i].text = e.Desc;
                            _slotSub[i].fontSize = 16f;
                            _slotSub[i].enableWordWrapping = true;
                            _slotSub[i].alignment = TextAlignmentOptions.Top;
                        }
                        else if (e != null && !real && string.IsNullOrEmpty(e.Desc))
                        {
                            _slotSub[i].text = "（该关键词的 JSON 未填 description）";
                            _slotSub[i].fontSize = 15f;
                        }
                    }
                    // 只有带说明的格子（关键词/能力）才接收悬停
                    _slotHit[i].raycastTarget = e != null && !string.IsNullOrEmpty(e.Info);
                }
            }
            catch (Exception ex) { Warn("刷新", "刷新图鉴内容失败 —— " + Describe(ex)); }
        }

        private sealed class Entry
        {
            public string Name;
            public string Sub;
            public Sprite Icon;
            public CardSo Card;
            public RelicSo Relic;
            public string Info;
            public string Desc;
        }

        private static List<Entry> CurrentEntries()
        {
            var list = new List<Entry>();
            switch (_tab)
            {
                case 0:
                    foreach (var c in _cards.OrderBy(x => x.cardName, StringComparer.Ordinal))
                    {
                        list.Add(new Entry { Name = Short(c.cardName), Icon = c.cardArt, Card = c });
                    }
                    break;
                case 1:
                    foreach (var r in _relics.OrderBy(x => x.name, StringComparer.Ordinal))
                    {
                        list.Add(new Entry { Name = "", Icon = r.relicIcon, Relic = r });
                    }
                    break;
                case 2:
                    foreach (var k in _keywords.OrderBy(x => x, StringComparer.Ordinal))
                    {
                        // 名称与说明优先取 mod 自己的 keywords/*.json（含中文），本地化表作兜底
                        string[] kv = KwJson(k);
                        var kwName = (kv != null && !string.IsNullOrEmpty(kv[0])) ? kv[0] : LocalizedAny("KeyWord", k);
                        if (string.IsNullOrEmpty(kwName)) { kwName = Short(k); }
                        var kwDesc = (kv != null && !string.IsNullOrEmpty(kv[1])) ? kv[1] : LocalizedAny("KeyWord", k + "Description");
                        list.Add(new Entry { Name = kwName, Sub = Clamp(kwDesc, 26), Info = kwName + "\n" + kwDesc, Desc = kwDesc });
                    }
                    break;
                default:
                    foreach (var a in _abilities.OrderBy(x => x, StringComparer.Ordinal))
                    {
                        list.Add(new Entry { Name = Short(a), Sub = "能力" });
                    }
                    break;
            }
            return list;
        }

        /// <summary>截断长文本用于副标题（悬停可看全文）。</summary>
        /// <summary>
        /// 取关键词 JSON 文本。`_kwText` 的键是 "modId.keywordId"，而 `_keywords` 里存的是短名，
        /// 所以这里两种形态都尝试匹配。
        /// </summary>
        private static string[] KwJson(string id)
        {
            if (string.IsNullOrEmpty(id)) { return null; }
            string[] v;
            if (_kwText.TryGetValue(id, out v)) { return v; }
            foreach (var kvp in _kwText)
            {
                if (kvp.Key.EndsWith("." + id, StringComparison.Ordinal) || id.EndsWith("." + kvp.Key, StringComparison.Ordinal))
                {
                    return kvp.Value;
                }
            }
            return null;
        }

        private static string Clamp(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) { return string.Empty; }
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }

        private static string Short(string name)
        {
            if (string.IsNullOrEmpty(name)) { return ""; }
            int dot = name.IndexOf('.');
            return (dot >= 0 && dot < name.Length - 1) ? name.Substring(dot + 1) : name;
        }

        // ------------------------------------------------------------------
        //  日志
        // ------------------------------------------------------------------

        private static void Warn(string key, string message)
        {
            if (_log == null || !_warned.Add(key)) { return; }
            _log.Warn("模组图鉴·" + key + "：" + message);
        }

        private static void Note(string key, string message)
        {
            if (_log == null || !_noted.Add(key)) { return; }
            _log.Info("模组图鉴：" + message);
        }

        private static string Describe(Exception ex)
        {
            var e = ex;
            while (e.InnerException != null) { e = e.InnerException; }
            return e.GetType().Name + ": " + e.Message;
        }
    }

    /// <summary>
    /// 常驻宿主：自建根对象（DontDestroyOnLoad）上的每帧组件。
    /// 自限流 0.5 秒一次，不做每帧全场景扫描。
    /// </summary>
    internal sealed class ModCompendiumHost : MonoBehaviour
    {
        private float _next;

        private void Update()
        {
            if (Time.realtimeSinceStartup < _next) { return; }
            _next = Time.realtimeSinceStartup + 0.5f;
            try { ModCompendium.OnHostTick(); }
            catch (Exception ex) { ModCompendium.HostError(ex); }
        }
    }
}