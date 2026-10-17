// =============================================================================
//  ModKit.Game · 新球的「自造球 + 每帧驱动」
//
//  为什么自己造球（2026-10-15 实机六轮定位，全部有日志佐证）：
//    ① 原版 `InitializeOrb` 对"已选中色"会跳过摆放 ⇒ `_holder` 为 null；
//    ② 主动调 `PutOrb` 也没把球挪过去（内部 `await Task.Delay(200)` 的续体没生效，位置一直 (0,0,0)）；
//    ③ `OnMouseDown/OnMouseDrag` 从不派发（补碰撞体无用）；
//    ④ 挂在球身上的 `MonoBehaviour.Update()` 不跑；
//    ⑤ 给 `OrbHolderForSubGame.Update` 打 Harmony 前缀也不触发；
//    ⑥ 自建 `DontDestroyOnLoad` 根对象挂 MonoBehaviour —— 这条一定会跑，作为驱动宿主。
//
//  ⇒ 视觉不再依赖游戏那颗球：我们自己造一颗球（SpriteRenderer + 黑球贴图，用游戏自带资源，
//     拿不到就当场画一个圆），钉在底座上、跟手拖拽；游戏那颗"看不见的真球"只在松手时
//     被 `PutOrb` 放进槽里 —— 满足它的选职业/开局牌组逻辑（`GetPickedColorES()` 读的是槽里的球）。
// =============================================================================

using System;
using System.Reflection;
using Card;
using UnityEngine;

namespace ModKit.Game
{
    /// <summary>自造球 + 拖拽驱动（每帧由常驻宿主驱动）。</summary>
    internal static class CareerOrbTicker
    {
        private const float GrabRadiusPx = 70f;

        private static SmallGames.WorldBetween.SubGame.ChooseColorEOrbForSubGame _orb;   // 游戏那颗（逻辑用）
        private static GameObject _ball;                                                  // 我们造的（视觉用）
        private static SpriteRenderer _ballSr;
        private static SmallGames.WorldBetween.SubGame.OrbHolderForSubGame _ballHolder;   // 球当前"家"
        private static Camera _cam;
        private static bool _dragging;
        private static int _lastFrame = -1;

        /// <summary>确认职业后若界面没被原版切走，这里兜底强切（见 <see cref="QueueForceExit"/>）。</summary>
        private static readonly System.Collections.Generic.List<SmallGames.WorldBetween.SubSmallGameChooseColor> _forceExit =
            new System.Collections.Generic.List<SmallGames.WorldBetween.SubSmallGameChooseColor>();
        private static float _forceExitAt;

        /// <summary>
        /// 登记一次"到点检查是否还停在选球界面"。
        /// 原版 `BackFromSubSmallGame` 把"显示桌面 + 隐藏选球界面"放在 DOTween 的 `OnComplete` 里，
        /// 实机出现该回调未执行 ⇒ 确认后界面不切走。这里 0.8 秒后若仍开着就手动完成切换。
        /// </summary>
        internal static void QueueForceExit(SmallGames.WorldBetween.SubSmallGameChooseColor picker)
        {
            if (picker == null) { return; }
            _forceExit.Add(picker);
            _forceExitAt = Time.unscaledTime + 0.8f;
            CareerPatch.WarnOnceStatic("force-queued",
                "职业接缝·诊断[兜底·登记]：0.8 秒后检查选球界面是否被原版切走。");
        }

        /// <summary>
        /// 强行切回房间（显示桌面 + 隐藏选球界面 + 刷新按钮）。
        /// 每一步独立兜底：任何一步失败都不影响其余步骤（早期版本整段一个 try，中间一步炸就全废）。
        /// </summary>
        internal static void ForceSwitchBack(SmallGames.WorldBetween.SubSmallGameChooseColor picker, string reason)
        {
            if (picker == null || picker.gameObject == null) { return; }

            var mainMenu = ReflectField<Component>(picker, "_mainMenu");
            if (mainMenu == null)
            {
                CareerPatch.WarnOnceStatic("force-nomenu", "职业接缝：强行切回失败 —— 找不到主菜单（_mainMenu）。");
                return;
            }

            bool deskOk = false;
            try
            {
                var desk = ReflectField<GameObject>(mainMenu, "deskScene");
                if (desk != null) { desk.SetActive(true); deskOk = true; }
            }
            catch (Exception ex) { WarnInner("force-desk", "显示桌面", ex); }

            try
            {
                picker.gameObject.SetActive(false);
            }
            catch (Exception ex) { WarnInner("force-hide", "隐藏选球界面", ex); }

            TryInvoke(mainMenu, "SetColorsOnMainMenu");
            TryInvoke(mainMenu, "RefreshPlayButton");
            TryInvoke(mainMenu, "ResetAllButtons");

            CareerPatch.WarnOnceStatic("force-done",
                "职业接缝：已强行切回房间（原因=" + reason + "｜显示桌面=" + deskOk + "｜选球界面已隐藏）。");
        }

        private static void CheckForceExit()
        {
            if (_forceExit.Count == 0) { return; }
            if (Time.unscaledTime < _forceExitAt) { return; }

            var pending = _forceExit.ToArray();
            _forceExit.Clear();

            foreach (var picker in pending)
            {
                bool alive = (picker != null);
                bool active = alive && picker.gameObject != null && picker.gameObject.activeSelf;
                CareerPatch.WarnOnceStatic("force-check",
                    "职业接缝·诊断[兜底·检查]：界面对象存活=" + alive + "｜仍显示=" + active);

                if (!active) { continue; }
                ForceSwitchBack(picker, "原版延迟检查");
            }
        }

        private static void TryInvoke(object target, string name)
        {
            try
            {
                var m = target.GetType().GetMethod(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null) { m.Invoke(target, null); }
            }
            catch (Exception ex) { WarnInner("force-" + name, name, ex); }
        }

        /// <summary>把反射调用的内层异常打出来（`TargetInvocationException` 本身没有信息量）。</summary>
        private static void WarnInner(string key, string what, Exception ex)
        {
            var inner = (ex is TargetInvocationException && ex.InnerException != null) ? ex.InnerException : ex;
            CareerPatch.WarnOnceStatic(key, "职业接缝：兜底步骤「" + what + "」失败 —— " +
                inner.GetType().Name + ": " + inner.Message);
        }

        private static T ReflectField<T>(object target, string name) where T : class
        {
            var f = target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return (f == null) ? null : (f.GetValue(target) as T);
        }

        private static void InvokePrivate(object target, string name)
        {
            var m = target.GetType().GetMethod(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (m != null) { m.Invoke(target, null); }
        }

        /// <summary>立刻销毁自造球（退出职业界面时调用；再次进入职业界面会重新创建）。</summary>
        internal static void DestroyBallNow(string reason)
        {
            try
            {
                if (_ball == null) { return; }
                UnityEngine.Object.Destroy(_ball);
                _ball = null;
                _ballSr = null;
                _ballHolder = null;
                CareerPatch.WarnOnceStatic("ball-gone", "职业接缝：自造球已销毁（" + reason + "）。");
            }
            catch (Exception ex)
            {
                CareerPatch.WarnOnceStatic("ball-gone-err", "职业接缝：销毁自造球失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        internal static void Tick()
        {
            if (Time.frameCount == _lastFrame) { return; }
            _lastFrame = Time.frameCount;
            if (!CareerPatch.ClassDeclared) { return; }   // 没有职业声明 ⇒ 平台在游戏里零足迹
            CareerPatch.WarnOnceStatic("tick-alive", "职业接缝：每帧驱动已启动（常驻宿主）。");

            // ★★ 显式生命周期（用户拍板 2026-10-17）：**退出职业界面 ⇒ 杀球；再次进入 ⇒ 重新造球**。
            try
            {
                // 看门狗：界面对象被隐藏/销毁 ⇒ 视为已退出（兜住"游戏没走我们的钩子"的情况）
                if (CareerPatch.PickerOpen)
                {
                    var picker = CareerPatch.NewOrbPickerGo;
                    if (picker == null || !picker.activeInHierarchy)
                    {
                        CareerPatch.PickerOpen = false;
                        CareerPatch.WarnOnceStatic("picker-closed-watchdog", "职业接缝：职业界面已关闭（看门狗判定）→ 收球。");
                    }
                }

                if (!CareerPatch.PickerOpen)
                {
                    DestroyBallNow("不在职业界面");
                    return;      // 不在职业界面 ⇒ 本帧不做任何球相关的事（球不显示、不跟随）
                }
            }
            catch (Exception ex)
            {
                CareerPatch.WarnOnceStatic("ball-gone-err", "职业接缝：收尾自造球失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }

            CheckForceExit();

            try
            {
                if (_orb == null)
                {
                    // ★ 只读缓存字段（零成本）。**绝不做任何 FindObjects/FindObjectsOfTypeAll 扫描**：
                    //   早期版本在球被销毁后每帧全场景扫描，主线程被拖死
                    //   （实机表现：确认职业后整个界面卡住、日志停滞）。
                    var cached = CareerPatch.NewOrbOrb;
                    if (cached == null)
                    {
                        // 界面关掉了：把自己的球收掉，然后什么都不做
                        if (_ball != null)
                        {
                            UnityEngine.Object.Destroy(_ball);
                            _ball = null;
                            _ballSr = null;
                        }
                        _ballHolder = null;
                        return;
                    }
                    _orb = cached;
                    CareerPatch.WarnOnceStatic("found", "职业接缝：每帧驱动已拿到球（缓存引用，无扫描）。");
                }

                if (_ballHolder == null) { _ballHolder = _orb.homePedestal; }

                if (_cam == null) { _cam = Camera.main; }

                // 拖拽中：球跟手；否则：球待在它的 holder 上
                if (_dragging && _cam != null)
                {
                    Vector3 w = _cam.ScreenToWorldPoint(Input.mousePosition);
                    w.z = -5f;
                    EnsureBall(w);
                }
                else
                {
                    EnsureBall(HolderAnchor(_ballHolder));
                }

                if (_cam == null)
                {
                    CareerPatch.WarnOnceStatic("nocam",
                        "职业接缝：Camera.main 取不到 —— 球已造出来并钉在底座上，但屏幕空间拖拽不可用。");
                    return;
                }

                Vector3 screenPoint = _cam.WorldToScreenPoint(BallPosition());
                Vector3 mouse = Input.mousePosition;

                if (!_dragging)
                {
                    if (!Input.GetMouseButtonDown(0)) { return; }
                    float dist = Vector2.Distance(new Vector2(screenPoint.x, screenPoint.y),
                                                  new Vector2(mouse.x, mouse.y));
                    if (dist > GrabRadiusPx) { return; }

                    _dragging = true;
                    CareerPatch.WarnOnceStatic("grab", "职业接缝·诊断[驱动·按下]：抓住新球。");
                    return;
                }

                if (Input.GetMouseButtonUp(0))
                {
                    _dragging = false;
                    DropOrb(BallPosition());
                }
            }
            catch (Exception ex)
            {
                CareerPatch.WarnOnceStatic("driver", "职业接缝：拖拽驱动异常 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        //  自造球
        // ------------------------------------------------------------------

        /// <summary>屏幕空间命中测试：按下且离球心足够近 → 开始拖。</summary>
        private static void CheckGrab()
        {
            if (_cam == null) { return; }
            if (!Input.GetMouseButtonDown(0)) { return; }

            Vector3 sp = _cam.WorldToScreenPoint(BallPosition());
            Vector3 m = Input.mousePosition;
            if (Vector2.Distance(new Vector2(sp.x, sp.y), new Vector2(m.x, m.y)) > GrabRadiusPx) { return; }

            _dragging = true;
            CareerPatch.WarnOnceStatic("grab", "职业接缝·诊断[驱动·按下]：抓住新球。");
        }

        private static Vector3 BallPosition()
        {
            return _ball == null ? HolderAnchor(_ballHolder) : _ball.transform.position;
        }

        private static Vector3 HolderAnchor(SmallGames.WorldBetween.SubGame.OrbHolderForSubGame holder)
        {
            if (holder == null) { return new Vector3(3f, 2f, -1f); }   // 兜底：右上（克隆底座的默认位）
            Vector3 p = (holder.orbHolder != null) ? holder.orbHolder.position : holder.transform.position;
            return p + new Vector3(0f, 0f, -1f);
        }

        /// <summary>保证自造球存在，并摆到指定位置。</summary>
        private static void EnsureBall(Vector3 position)
        {
            if (_ball == null)
            {
                _ball = new GameObject("ModKitOrb_" + CareerPatch.NewOrbColor);
                UnityEngine.Object.DontDestroyOnLoad(_ball);
                _ballSr = _ball.AddComponent<UnityEngine.SpriteRenderer>();
                _ballSr.sprite = MakeOrbSprite();
                _ballSr.sortingOrder = 32000;   // 尽量盖在底座/其它球之上
                CareerPatch.WarnOnceStatic("ball",
                    "职业接缝：已创建自造球（贴图=" + (_ballSr.sprite == null ? "null" : _ballSr.sprite.name) +
                    "，可见=" + (_ballSr.enabled && _ballSr.sprite != null).ToString() + "）。");
            }
            _ball.transform.position = position;
            var want = Vector3.one * CareerPatch.BallScale;
            if (_ball.transform.localScale != want) { _ball.transform.localScale = want; }
        }

        /// <summary>取新球的贴图：**优先 mod 自带图**（`ClassSpec.OrbSprite`）→ 游戏自带该颜色图 → 当场画一个圆。</summary>
        private static Sprite MakeOrbSprite()
        {
            // ① mod 自带的球贴图（作者想让它长什么样就长什么样）
            if (!string.IsNullOrEmpty(CareerPatch.OrbSpritePath))
            {
                try
                {
                    string err;
                    var s = ModArt.Load(CareerPatch.OrbSpritePath, out err);
                    if (s != null) { return s; }
                    CareerPatch.WarnOnceStatic("sprite-mod",
                        "职业接缝：加载 mod 自带球贴图失败，回退 —— " + err);
                }
                catch (Exception ex)
                {
                    CareerPatch.WarnOnceStatic("sprite-mod-err", "职业接缝：加载 mod 球贴图异常 —— " + ex.Message);
                }
            }

            try
            {
                var wm = World.WorldManager.Instance;
                if (wm != null)
                {
                    var f = wm.GetType().GetField("colorEAssetRefSo",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var so = (f == null) ? null : f.GetValue(wm);
                    var m = (so == null) ? null : so.GetType().GetMethod("GetSpriteByColorE");
                    if (m != null)
                    {
                        var s = m.Invoke(so, new object[] { CareerPatch.NewOrbColor }) as Sprite;
                        if (s != null) { return s; }
                    }
                }
            }
            catch (Exception ex)
            {
                CareerPatch.WarnOnceStatic("sprite", "职业接缝：取游戏 Black 贴图失败，改用自绘 —— " + ex.Message);
            }

            const int size = 72;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - size / 2f) / (size / 2f - 2f);
                    float dy = (y - size / 2f) / (size / 2f - 2f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    Color c;
                    if (d > 1f) { c = new Color(0f, 0f, 0f, 0f); }
                    else if (d > 0.80f) { c = new Color(0.52f, 0.40f, 0.86f, 1f); }   // 紫边（呼应职业配色）
                    else { c = new Color(0.10f, 0.10f, 0.13f, 1f); }                  // 黑色球体
                    px[y * size + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        // ------------------------------------------------------------------
        //  落位（把"看不见的真球"放进槽里，满足游戏的选职业逻辑）
        // ------------------------------------------------------------------

        private static void DropOrb(Vector3 world)
        {
            var ui = _orb.chooseCampaignColorESubGame;
            SmallGames.WorldBetween.SubGame.OrbHolderForSubGame best = null;
            float bestDist = float.MaxValue;

            if (ui != null && ui.chooseColorESlots != null)
            {
                foreach (var slot in ui.chooseColorESlots)
                {
                    if (slot == null || slot.GetHasOrb()) { continue; }
                    float d = Vector2.Distance(slot.transform.position, world);
                    if (d < bestDist) { bestDist = d; best = slot; }
                }
            }
            if (_orb.homePedestal != null && !_orb.homePedestal.GetHasOrb())
            {
                float d = Vector2.Distance(_orb.homePedestal.transform.position, world);
                if (d < bestDist) { bestDist = d; best = _orb.homePedestal; }
            }

            if (best == null) { best = _orb.homePedestal; }
            if (best != null)
            {
                try { best.PutOrb(_orb); } catch (Exception ex) { CareerPatch.WarnOnceStatic("put", "职业接缝：PutOrb 失败 —— " + ex.Message); }
                _ballHolder = best;
            }

            if (ui != null)
            {
                try
                {
                    var m = ui.GetType().GetMethod("RefreshButtonAndIntroCard",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m != null) { m.Invoke(ui, null); }
                }
                catch (Exception ex)
                {
                    CareerPatch.WarnOnceStatic("refresh", "职业接缝：刷新确认按钮失败 —— " + ex.Message);
                }
            }

            CareerPatch.WarnOnceStatic("drop",
                "职业接缝·诊断[驱动·松手]：落到 " + (best == null ? "无（回底座）" : best.name) +
                "，球位置=" + HolderAnchor(_ballHolder));
        }

        // 说明：**故意不提供任何"找球"方法**。
        // 早期版本用 FindObjectsOfType / Resources.FindObjectsOfTypeAll 找球：
        // 前者找不到未激活对象（球一直不显示），后者在球被销毁后每帧全场景扫描，
        // 直接把主线程拖死（实机：确认职业后卡住）。现在只读 CareerPatch.NewOrbOrb 缓存引用。
    }

    /// <summary>
    /// 常驻宿主：挂在**自己新建的根 GameObject**（`DontDestroyOnLoad`）上。
    /// 游戏里的对象（界面对象初始化期 inactive、球身上的 MonoBehaviour、`OrbHolderForSubGame.Update` 的
    /// Harmony 前缀）在实机上都**没有触发**，只有自建根对象的 `Update` 必定每帧跑。
    /// </summary>
    public sealed class CareerOrbTickerHost : MonoBehaviour
    {
        private void Update()
        {
            CareerOrbTicker.Tick();
        }
    }
}
