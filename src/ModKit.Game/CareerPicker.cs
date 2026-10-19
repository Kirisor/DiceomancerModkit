// =============================================================================
//  ModKit.Game · 自建职业选择（替代原版「拖三颗球」的小游戏界面）
//
//  确认链路（最小侵入）：
//    原版 LeaveSubSmallGame() 只认 5 种原版三色组合，遇到 mod 组合会在
//    GetClassNameByColorE 处抛异常，而且会先把 _submitted 置 true ⇒ 之后点击静默无效。
//    因此这里不调它：直接把三色写进 SmallGameMainMenu.mainMenuSmallGameColorEs，
//    再复用平台已有的 ForceSwitchBack 回房间，玩家点主菜单「开始游戏」即按该职业开局
//    （初始牌组/遗物由 CareerAssembler 写进 CareerInformationForGameInitialize）。
//
//  界面：左侧抽屉（不遮住原版护符与右侧介绍卡），职业列表可滚动。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ModKit.Api;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModKit.Game
{
    internal static class CareerPicker
    {
        /// <summary>玩家在自建面板里选定的职业颜色；非 null 表示由我们接管确认。</summary>
        internal static List<ManaColor> ChosenColors;

        // 面板
        private static GameObject _canvasGo;
        private static GameObject _panelGo;
        private static TextMeshProUGUI _countText;
        private static TextMeshProUGUI _detailText;
        private static TextMeshProUGUI _colorsText;
        private static TextMeshProUGUI _hintText;
        private static ScrollRect _scroll;
        private static RectTransform _content;
        private static ClassSpec _selected;
        private static readonly List<Row> _rows = new List<Row>();

        private static IGameLog _log;

        // 贴图加载交给 ModArt（带缓存 + 坏图不炸装配）

        internal static void Init(IGameLog log) { _log = log; }

        // 视觉常量
        private static readonly Color Paper = new Color(1f, 0.984f, 0.945f, 1f);
        private static readonly Color PaperDim = new Color(0.945f, 0.91f, 0.85f, 1f);
        private static readonly Color Ink = new Color(0.16f, 0.13f, 0.11f, 1f);
        private static readonly Color InkSoft = new Color(0.42f, 0.36f, 0.30f, 1f);
        private static readonly Color Accent = new Color(0.42f, 0.72f, 0.42f, 1f);
        private static readonly Color Gold = new Color(0.79f, 0.64f, 0.15f, 1f);
        private static readonly Color RowBg = new Color(0.99f, 0.97f, 0.93f, 1f);
        private static readonly Color RowSel = new Color(0.88f, 0.95f, 0.85f, 1f);
        private static readonly Color Track = new Color(0.85f, 0.81f, 0.72f, 1f);

        private const float PanelW = 430f;
        private const float PanelH = 830f;
        private const float RowH = 84f;
        private const float RowGap = 8f;
        private const float ListH = 340f;

        private sealed class Row
        {
            public ClassSpec Spec;
            public Image Bg;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Id;
            public GameObject Go;
        }

        internal static void Show()
        {
            try
            {
                var specs = new List<ClassSpec>(CareerAssembler.DeclaredSpecs);
                if (specs == null || specs.Count == 0) { return; }
                ChosenColors = null;
                _selected = specs.Count > 0 ? specs[0] : null;
                EnsureCanvas();
                BuildPanel(specs);
                if (_panelGo != null) { _panelGo.SetActive(true); }
                RefreshSelection();
                WriteDetail();
            }
            catch (Exception ex) { Info("界面显示失败：" + ex.GetType().Name + " " + ex.Message); }
        }

        internal static void Hide()
        {
            try { if (_panelGo != null) { _panelGo.SetActive(false); } } catch { }
        }

        internal static void Reset()
        {
            ChosenColors = null;
            _selected = null;
            Hide();
        }

        private static void EnsureCanvas()
        {
            if (_canvasGo != null) { return; }
            _canvasGo = new GameObject("ModKit_CareerPickerCanvas");
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();
        }

        private static void BuildPanel(List<ClassSpec> specs)
        {
            if (_panelGo != null) { UnityEngine.Object.Destroy(_panelGo); _panelGo = null; }
            _rows.Clear();

            _panelGo = new GameObject("ModKit_CareerPickerPanel", typeof(RectTransform));
            _panelGo.transform.SetParent(_canvasGo.transform, false);

            var prt = _panelGo.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0f, 0.5f);
            prt.anchorMax = new Vector2(0f, 0.5f);
            prt.pivot = new Vector2(0f, 0.5f);
            // 整体缩到 86%：避免盖住左下角的「返回」，也减少误触
            prt.anchoredPosition = new Vector2(140f, 24f);
            prt.sizeDelta = new Vector2(PanelW, PanelH);
            prt.localScale = new Vector3(0.86f, 0.86f, 1f);

            var bg = _panelGo.AddComponent<Image>();
            bg.color = new Color(0.965f, 0.945f, 0.90f, 1f);
            bg.sprite = UiSkin.PanelRound;                 // 圆角底：四角倒角
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = true;
            UiSkin.AddPaperLayer(prt);

            // 外框：程序化九宫格边框（比纸页大一圈）
            var frameRt = NewRect(prt, "Frame", new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(PanelW + 14f, PanelH + 14f));
            var frameImg = frameRt.gameObject.AddComponent<Image>();
            frameImg.sprite = UiSkin.PanelFrame;
            frameImg.type = Image.Type.Sliced;
            frameImg.color = Color.white;
            frameImg.raycastTarget = false;

            // 标题
            NewText(prt, "模组职业", 34f, Ink, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(PanelW - 60f, 46f), true);
            _countText = NewText(prt, "", 17f, InkSoft, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(PanelW - 60f, 24f), true);
            _countText.text = "共 " + specs.Count + " 个 · 滚轮或拖动浏览";

            // 分隔线
            var line = NewRect(prt, "Line", new Vector2(0.5f, 1f), new Vector2(0f, -102f), new Vector2(PanelW - 66f, 3f));
            var lineImg = line.gameObject.AddComponent<Image>();
            lineImg.color = new Color(0.85f, 0.80f, 0.70f, 1f);
            lineImg.raycastTarget = false;

            // ---------- 滚动列表 ----------
            float listTop = -112f;
            var viewport = NewRect(prt, "List", new Vector2(0.5f, 1f), new Vector2(-8f, listTop), new Vector2(PanelW - 78f, ListH));
            viewport.pivot = new Vector2(0.5f, 1f);
            var vpMask = viewport.gameObject.AddComponent<RectMask2D>();
            vpMask.padding = Vector4.zero;
            // 透明底板：让 ScrollRect 收到拖拽
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(1f, 1f, 1f, 0.02f);
            vpImg.raycastTarget = true;

            _content = NewRect(viewport, "Content", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(PanelW - 78f, 10f));
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;

            _scroll = viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.content = _content;
            _scroll.viewport = viewport;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 36f;
            _scroll.inertia = true;
            _scroll.decelerationRate = 0.12f;

            float y = 0f;
            for (int i = 0; i < specs.Count; i++)
            {
                var spec = specs[i];
                if (spec == null || string.IsNullOrEmpty(spec.Id)) { continue; }

                var rowRt = NewRect(_content, "Row_" + spec.Id, new Vector2(0.5f, 1f), new Vector2(0f, -y), new Vector2(PanelW - 88f, RowH));
                rowRt.pivot = new Vector2(0.5f, 1f);

                var rowBg = rowRt.gameObject.AddComponent<Image>();
                rowBg.color = RowBg;
                rowBg.raycastTarget = true;
                rowBg.sprite = UiSkin.RowRound;             // 圆角 + 4px 边
                rowBg.type = Image.Type.Sliced;

                var btn = rowRt.gameObject.AddComponent<Button>();
                btn.targetGraphic = rowBg;
                var captured = spec;
                btn.onClick.AddListener(() => { _selected = captured; RefreshSelection(); WriteDetail(); });

                // 职业球图标：优先用 mod 自带的球贴图，没有就退回 ● 字形
                var sprite = LoadOrbSprite(spec);
                if (sprite != null)
                {
                    var iconRt = NewRect(rowRt, "Icon", new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(46f, 46f));
                    var iconImg = iconRt.gameObject.AddComponent<Image>();
                    iconImg.sprite = sprite;
                    iconImg.preserveAspect = true;
                    iconImg.raycastTarget = false;
                }
                else
                {
                    NewText(rowRt, "●", 30f, OrbColor(spec), TextAlignmentOptions.Center,
                        new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(44f, 44f), false);
                }
                var nameT = NewText(rowRt, spec.NameZh, 23f, Ink, TextAlignmentOptions.Left,
                    new Vector2(0f, 0.5f), new Vector2(72f, 12f), new Vector2(240f, 30f), false);
                nameT.rectTransform.pivot = new Vector2(0f, 0.5f);
                nameT.alignment = TextAlignmentOptions.Left;
                var idT = NewText(rowRt, spec.Id, 16f, InkSoft, TextAlignmentOptions.Left,
                    new Vector2(0f, 0.5f), new Vector2(72f, -14f), new Vector2(240f, 22f), false);
                idT.rectTransform.pivot = new Vector2(0f, 0.5f);
                idT.alignment = TextAlignmentOptions.Left;

                _rows.Add(new Row { Spec = spec, Bg = rowBg, Name = nameT, Id = idT, Go = rowRt.gameObject });
                y += RowH + RowGap;
            }

            _content.sizeDelta = new Vector2(PanelW - 78f, Mathf.Max(ListH, y));

            // 滚动条（细条 + 滑块）
            var track = NewRect(prt, "Scrollbar", new Vector2(0.5f, 1f), new Vector2(PanelW / 2f - 26f, listTop), new Vector2(6f, ListH));
            track.pivot = new Vector2(0.5f, 1f);
            var trackImg = track.gameObject.AddComponent<Image>();
            trackImg.color = Track;
            trackImg.raycastTarget = false;

            var handle = NewRect(track, "Handle", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(6f, 100f));
            handle.pivot = new Vector2(0.5f, 1f);
            var handleImg = handle.gameObject.AddComponent<Image>();
            handleImg.color = InkSoft;

            var sb = track.gameObject.AddComponent<Scrollbar>();
            sb.handleRect = handle;
            sb.targetGraphic = handleImg;
            sb.direction = Scrollbar.Direction.BottomToTop;
            _scroll.verticalScrollbar = sb;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            // ---------- 详情区 ----------
            float detailTop = listTop - ListH - 18f;
            var detailBg = NewRect(prt, "Detail", new Vector2(0.5f, 1f), new Vector2(0f, detailTop), new Vector2(PanelW - 66f, 190f));
            detailBg.pivot = new Vector2(0.5f, 1f);
            var detailImg = detailBg.gameObject.AddComponent<Image>();
            detailImg.color = new Color(0.98f, 0.96f, 0.91f, 1f);
            detailImg.raycastTarget = false;
            detailImg.sprite = UiSkin.RowRound;
            detailImg.type = Image.Type.Sliced;

            _detailText = NewText(detailBg, "", 18f, InkSoft, TextAlignmentOptions.TopLeft,
                new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(PanelW - 106f, 128f), true);
            _detailText.rectTransform.pivot = new Vector2(0.5f, 1f);
            _detailText.enableWordWrapping = true;

            _colorsText = NewText(detailBg, "", 18f, InkSoft, TextAlignmentOptions.Left,
                new Vector2(0f, 0f), new Vector2(20f, 20f), new Vector2(PanelW - 120f, 24f), false);
            _colorsText.rectTransform.pivot = new Vector2(0f, 0f);
            _colorsText.alignment = TextAlignmentOptions.Left;

            // ---------- 按钮 ----------
            float btnTop = detailTop - 190f - 16f;
            var start = NewButton(prt, "开始游戏", new Vector2(0.5f, 1f), new Vector2(0f, btnTop), new Vector2(PanelW - 66f, 66f), Accent, Ink);
            if (start != null) { start.onClick.AddListener(Commit); }

            var cancel = NewButton(prt, "用原版选球", new Vector2(0.5f, 1f), new Vector2(0f, btnTop - 76f), new Vector2(PanelW - 66f, 50f), PaperDim, InkSoft);
            if (cancel != null)
            {
                cancel.onClick.AddListener(() =>
                {
                    ChosenColors = null;
                    Info("已切回原版选球界面");
                    Hide();
                });
            }

            _hintText = NewText(prt, "", 17f, InkSoft, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0f, btnTop - 132f), new Vector2(PanelW - 66f, 22f), false);
        }

        private static void RefreshSelection()
        {
            foreach (var row in _rows)
            {
                if (row == null || row.Bg == null) { continue; }
                bool sel = _selected != null && row.Spec == _selected;
                row.Bg.color = sel ? RowSel : RowBg;
                row.Bg.rectTransform.sizeDelta = new Vector2(sel ? PanelW - 82f : PanelW - 88f, RowH);
                if (row.Name != null) { row.Name.fontStyle = sel ? FontStyles.Bold : FontStyles.Normal; }
            }
        }

        private static void WriteDetail()
        {
            try
            {
                if (_detailText == null) { return; }
                if (_selected == null) { _detailText.text = ""; return; }
                var intro = (_selected.IntroZh ?? "").Replace("\\n", " ");
                _detailText.text = intro;
                if (_colorsText != null)
                {
                    _colorsText.text = "●  ●  ●   " + string.Join(" / ", _selected.Colors.Select(c => ColorName(c)));
                }
                if (_hintText != null) { _hintText.text = "已选：" + _selected.NameZh; }
            }
            catch (Exception ex) { Info("刷新详情失败：" + ex.Message); }
        }

        /// <summary>写盘：把三色交给主菜单，然后回到房间（见文件头说明）。</summary>
        private static void Commit()
        {
            try
            {
                if (_selected == null)
                {
                    if (_hintText != null) { _hintText.text = "先选一个职业"; }
                    return;
                }
                if (_selected.Colors == null || _selected.Colors.Count != 3)
                {
                    if (_hintText != null) { _hintText.text = "这个职业的颜色不是 3 个，无法开局"; }
                    return;
                }

                var picker = CareerPatch.CurrentPicker;
                if (picker == null)
                {
                    if (_hintText != null) { _hintText.text = "找不到职业界面对象，无法开始"; }
                    return;
                }

                ChosenColors = new List<ManaColor>(_selected.Colors);
                bool wrote = WriteMainMenuColors(picker, _selected.Colors);
                CareerOrbTicker.ForceSwitchBack(picker, "自建职业选择：" + _selected.Id);

                var msg = wrote
                    ? "已选「" + _selected.NameZh + "」——回到房间后点「开始游戏」即可"
                    : "已选「" + _selected.NameZh + "」但写主菜单颜色失败，请看日志";
                if (_hintText != null) { _hintText.text = msg; }
                Info(msg);
                Hide();
            }
            catch (Exception ex) { Info("确认失败：" + ex.GetType().Name + " " + ex.Message); }
        }

        private static bool WriteMainMenuColors(SmallGames.WorldBetween.SubSmallGameChooseColor picker, List<ManaColor> colors)
        {
            try
            {
                var mainMenu = FindPrivateField(picker, "_mainMenu");
                if (mainMenu == null) { Info("写主菜单颜色失败：找不到 _mainMenu"); return false; }

                var field = mainMenu.GetType().GetField("mainMenuSmallGameColorEs",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                if (field == null) { Info("写主菜单颜色失败：找不到 mainMenuSmallGameColorEs"); return false; }

                var list = field.GetValue(mainMenu) as List<Card.ColorE>;
                if (list == null) { list = new List<Card.ColorE>(); field.SetValue(mainMenu, list); }
                list.Clear();
                foreach (var c in colors) { list.Add((Card.ColorE)(int)c); }

                try
                {
                    var refresh = mainMenu.GetType().GetMethod("RefreshPlayButton",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);
                    if (refresh != null) { refresh.Invoke(mainMenu, null); }
                }
                catch { }
                return true;
            }
            catch (Exception ex) { Info("写主菜单颜色异常：" + ex.Message); return false; }
        }

        private static object FindPrivateField(object target, string name)
        {
            try
            {
                var t = target.GetType();
                while (t != null)
                {
                    var f = t.GetField(name, System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                    if (f != null) { return f.GetValue(target); }
                    t = t.BaseType;
                }
            }
            catch { }
            return null;
        }

        // ---------------- 辅助 ----------------

        private static Sprite LoadOrbSprite(ClassSpec spec)
        {
            try
            {
                string path;
                if (spec == null) { return null; }
                if (!CareerAssembler.SpecOrbSprites.TryGetValue(spec, out path)) { return null; }
                if (string.IsNullOrEmpty(path)) { return null; }
                string error;
                var sp = ModArt.Load(path, out error);   // ModArt 自带缓存，坏图返回 null
                if (sp == null && !string.IsNullOrEmpty(error)) { Info("职业球贴图没加载出来：" + error); }
                return sp;
            }
            catch { return null; }
        }

        private static string ColorName(ManaColor c)
        {
            switch (c)
            {
                case ManaColor.Red: return "红";
                case ManaColor.Green: return "绿";
                case ManaColor.Blue: return "蓝";
                case ManaColor.Purple: return "紫";
                case ManaColor.Void: return "虚空";
                case ManaColor.Black: return "黑";
            }
            return c.ToString();
        }

        private static Color OrbColor(ClassSpec spec)
        {
            var c = spec.OrbColor;
            switch (c)
            {
                case ManaColor.Red: return new Color(0.85f, 0.35f, 0.30f, 1f);
                case ManaColor.Green: return new Color(0.45f, 0.75f, 0.40f, 1f);
                case ManaColor.Blue: return new Color(0.40f, 0.60f, 0.85f, 1f);
                case ManaColor.Purple: return new Color(0.65f, 0.45f, 0.80f, 1f);
                case ManaColor.Void: return new Color(0.75f, 0.75f, 0.78f, 1f);
                default: return new Color(0.18f, 0.15f, 0.20f, 1f);
            }
        }

        private static RectTransform NewRect(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        private static TextMeshProUGUI NewText(Transform parent, string text, float size, Color color,
            TextAlignmentOptions align, Vector2 anchor, Vector2 pos, Vector2 sizeDelta, bool wrap)
        {
            var rt = NewRect(parent, "T_" + (text ?? "").Length, anchor, pos, sizeDelta);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text ?? "";
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.enableWordWrapping = wrap;
            t.raycastTarget = false;
            return t;
        }

        private static Button NewButton(Transform parent, string text, Vector2 anchor, Vector2 pos, Vector2 size, Color bgColor, Color textColor)
        {
            var rt = NewRect(parent, "Btn_" + text, anchor, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = bgColor;
            img.sprite = UiSkin.RowRound;
            img.type = Image.Type.Sliced;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            NewText(rt, text, 24f, textColor, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), Vector2.zero, size, false);
            return btn;
        }

        private static void Info(string message)
        {
            try { if (_log != null) { _log.Info("[自建职业] " + message); } } catch { }
        }
    }

    /// <summary>接管「三颗球是否都放好」与「取到哪三种颜色」。</summary>
    [HarmonyPatch(typeof(SmallGames.WorldBetween.SubSmallGameChooseColor), "IsAllSlotHasOrb")]
    internal static class PickerSlotPatch
    {
        private static bool Prefix(ref bool __result)
        {
            try
            {
                if (CareerPicker.ChosenColors != null) { __result = true; return false; }
            }
            catch { }
            return true;
        }
    }

    [HarmonyPatch(typeof(SmallGames.WorldBetween.SubSmallGameChooseColor), "GetPickedColorES")]
    internal static class PickerColorsPatch
    {
        private static void Postfix(ref Card.ColorE[] __result)
        {
            try
            {
                var chosen = CareerPicker.ChosenColors;
                if (chosen == null) { return; }
                var arr = new Card.ColorE[chosen.Count];
                for (int i = 0; i < chosen.Count; i++) { arr[i] = (Card.ColorE)(int)chosen[i]; }
                __result = arr;
            }
            catch { }
        }
    }
}
