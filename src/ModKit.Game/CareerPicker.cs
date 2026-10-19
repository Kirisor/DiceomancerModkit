// =============================================================================
//  ModKit.Game · 自建职业选择（替代原版「拖三颗球」的小游戏界面）
//
//  思路（最小侵入）：
//    原版确认职业的唯一链路是 SubSmallGameChooseColor.LeaveSubSmallGame()，
//    而它有两个前置判断：IsAllSlotHasOrb()（三个槽都放了球）与 GetPickedColorES()（取三个颜色）。
//    我们在这两个方法上打补丁：
//      · 玩家在我们的面板里选了 mod 职业 ⇒ IsAllSlotHasOrb 直接返回 true、GetPickedColorES 返回该职业的三色；
//      · 未选择（或选的是原版职业）⇒ 原样走原版逻辑。
//    于是「确认 → 写主菜单颜色 → 写开局牌组/遗物 → 初始化飞升 → 结束小游戏」全部由游戏自己完成，
//    我们不需要重写任何开局状态机。
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Card;
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

        private static GameObject _canvasGo;
        private static GameObject _panelGo;
        private static TextMeshProUGUI _titleText;
        private static TextMeshProUGUI _hintText;
        private static ClassSpec _selected;
        private static readonly List<Button> _entryButtons = new List<Button>();

        private static IGameLog _log;

        internal static void Init(IGameLog log) { _log = log; }

        private static readonly Color Paper = new Color(0.96f, 0.94f, 0.88f, 1f);
        private static readonly Color Ink = new Color(0.16f, 0.13f, 0.11f, 1f);
        private static readonly Color Accent = new Color(0.45f, 0.72f, 0.42f, 1f);

        /// <summary>进入职业界面时调用：有 mod 职业就显示自建面板。</summary>
        internal static void Show()
        {
            try
            {
                var specs = CareerAssembler.DeclaredSpecs;
                if (specs == null || specs.Count == 0) { return; }
                ChosenColors = null;
                _selected = null;
                EnsureCanvas();
                BuildPanel(specs);
                if (_panelGo != null) { _panelGo.SetActive(true); }
            }
            catch (Exception ex) { Warn("自建职业界面显示失败：" + ex.GetType().Name + " " + ex.Message); }
        }

        /// <summary>离开职业界面时调用：清理。</summary>
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
            _entryButtons.Clear();

            _panelGo = new GameObject("ModKit_CareerPickerPanel", typeof(RectTransform));
            _panelGo.transform.SetParent(_canvasGo.transform, false);
            var rt = _panelGo.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(980f, 720f);

            var bg = _panelGo.AddComponent<Image>();
            bg.color = Paper;

            _titleText = NewText(rt, "选择模组职业", 40f, Ink, new Vector2(0.5f, 1f), new Vector2(0f, -46f), new Vector2(900f, 56f));

            float y = -130f;
            foreach (var spec in specs)
            {
                if (spec == null || string.IsNullOrEmpty(spec.Id)) { continue; }
                var captured = spec;
                var btn = NewButton(rt, spec.NameZh + "（" + spec.Id + "）", new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(880f, 84f));
                if (btn == null) { continue; }
                var label = btn.GetComponentInChildren<TextMeshProUGUI>();
                var sub = NewText(rt, OneLine(spec.IntroZh, 46), 20f, new Color(0.3f, 0.26f, 0.22f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(0f, y - 54f), new Vector2(860f, 26f));
                btn.onClick.AddListener(() =>
                {
                    _selected = captured;
                    RefreshSelection();
                    if (_hintText != null)
                    {
                        _hintText.text = "已选：" + captured.NameZh + "　（颜色 " + ColorsText(captured.Colors) + "）";
                    }
                });
                _entryButtons.Add(btn);
                if (label != null) { label.color = Ink; }
                if (sub != null) { sub.color = new Color(0.3f, 0.26f, 0.22f, 1f); }
                y -= 118f;
            }

            _hintText = NewText(rt, "点一个职业，再点下面的「开始」", 22f, new Color(0.3f, 0.26f, 0.22f, 1f),
                new Vector2(0.5f, 0f), new Vector2(0f, 128f), new Vector2(880f, 30f));

            var start = NewButton(rt, "开始", new Vector2(0.5f, 0f), new Vector2(0f, 56f), new Vector2(320f, 76f));
            if (start != null)
            {
                start.onClick.AddListener(Commit);
            }

            var cancel = NewButton(rt, "使用原版选球", new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(320f, 56f));
            if (cancel != null)
            {
                cancel.onClick.AddListener(() =>
                {
                    ChosenColors = null;
                    Hide();
                    Warn("已切回原版选球界面（自建面板关闭）");
                });
            }
        }

        private static void RefreshSelection()
        {
            for (int i = 0; i < _entryButtons.Count; i++)
            {
                var b = _entryButtons[i];
                if (b == null) { continue; }
                var img = b.GetComponent<Image>();
                if (img != null)
                {
                    var isSel = _selected != null && b.GetComponentInChildren<TextMeshProUGUI>() != null
                                && b.GetComponentInChildren<TextMeshProUGUI>().text.Contains(_selected.Id);
                    img.color = isSel ? Accent : new Color(1f, 1f, 1f, 0.92f);
                }
            }
        }

        /// <summary>
        /// 确认：不调用原版 LeaveSubSmallGame（它只认 5 种原版三色组合，遇到 mod 组合会 NRE，
        /// 而且会先把 _submitted 置 true ⇒ 之后永远静默返回）。
        /// 改为直接写主菜单的三色，再复用平台的「强行切回房间」，玩家点主菜单的「开始游戏」即可。
        /// </summary>
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

                // ① 写主菜单三色（原版 public 字段 mainMenuSmallGameColorEs）
                bool wrote = WriteMainMenuColors(picker, _selected.Colors);
                // ② 关闭选球界面 + 回到房间（平台已有实现）
                CareerOrbTicker.ForceSwitchBack(picker, "自建职业选择：" + _selected.Id);

                var msg = wrote
                    ? "已选「" + _selected.NameZh + "」——回到房间后点「开始游戏」即可"
                    : "已选「" + _selected.NameZh + "」但写主菜单颜色失败，请看日志";
                if (_hintText != null) { _hintText.text = msg; }
                Warn(msg);
                Hide();
            }
            catch (Exception ex) { Warn("自建职业确认失败：" + ex.GetType().Name + " " + ex.Message); }
        }

        /// <summary>反射写 SmallGameMainMenu.mainMenuSmallGameColorEs。</summary>
        private static bool WriteMainMenuColors(SmallGames.WorldBetween.SubSmallGameChooseColor picker, List<ManaColor> colors)
        {
            try
            {
                var mainMenu = FindPrivateField(picker, "_mainMenu");
                if (mainMenu == null) { Warn("写主菜单颜色失败：找不到 _mainMenu"); return false; }

                var field = mainMenu.GetType().GetField("mainMenuSmallGameColorEs",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                if (field == null) { Warn("写主菜单颜色失败：找不到 mainMenuSmallGameColorEs"); return false; }

                var list = field.GetValue(mainMenu) as List<ColorE>;
                if (list == null)
                {
                    list = new List<ColorE>();
                    field.SetValue(mainMenu, list);
                }
                list.Clear();
                foreach (var c in colors) { list.Add((ColorE)(int)c); }

                // 让主菜单刷新自己的圆环/开始按钮
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
            catch (Exception ex) { Warn("写主菜单颜色异常：" + ex.Message); return false; }
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

        private static string ColorsText(List<ManaColor> colors)
        {
            try { return string.Join(" / ", colors.Select(c => c.ToString())); } catch { return "?"; }
        }

        private static string OneLine(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) { return ""; }
            s = s.Replace("\n", " ").Replace("\r", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        // ---------------- 极简 UI 辅助 ----------------

        private static TextMeshProUGUI NewText(Transform parent, string text, float size, Color color,
            Vector2 anchor, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject("T_" + (text ?? "").Length, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text ?? "";
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            return t;
        }

        private static Button NewButton(Transform parent, string text, Vector2 anchor, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject("Btn_" + text, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.92f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            NewText(go.transform, text, 26f, Ink, new Vector2(0.5f, 0.5f), Vector2.zero, sizeDelta);
            return btn;
        }

        private static void Warn(string message)
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
                if (CareerPicker.ChosenColors != null)
                {
                    __result = true;
                    return false;
                }
            }
            catch { }
            return true;
        }
    }

    [HarmonyPatch(typeof(SmallGames.WorldBetween.SubSmallGameChooseColor), "GetPickedColorES")]
    internal static class PickerColorsPatch
    {
        private static void Postfix(ref ColorE[] __result)
        {
            try
            {
                var chosen = CareerPicker.ChosenColors;
                if (chosen == null) { return; }
                var arr = new ColorE[chosen.Count];
                for (int i = 0; i < chosen.Count; i++) { arr[i] = (ColorE)(int)chosen[i]; }
                __result = arr;
            }
            catch { }
        }
    }
}
