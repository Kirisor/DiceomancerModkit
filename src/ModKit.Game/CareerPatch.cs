// =============================================================================
//  ModKit.Game · 职业 / 色球接缝（新职业「奶蛙」Stage 1）
//
//  目标：把原版**留着没启用**的第 6 种颜色（Card.ColorE.Black）变成一颗球。
//  为什么只能用 Black：ColorE 是编译进 Assembly-CSharp 的枚举
//  （Card/ColorE.cs = Red/Green/Blue/Purple/Void/Black），外部无法添加成员，
//  所以"新增一颗球"在 mod 层面等价于"启用原版没做成球的那颗"。
//
//  实机证据（2026-10-15 勘察）：
//    · Utilities/DitwUtil.GetAllBasicColorEArray() 硬编码返回 5 色，**没有 Black**；
//      而 GetAllColorEArray() 返回完整 6 色 ⇒ Black 有枚举值但没有球。
//    · SmallGames.WorldBetween/SubSmallGameChooseColor.CalculateColorEAndStates()
//      里的 switch 也没有 case Black ⇒ 即使色表里有 Black 也不会出现在选球界面。
//
//  两处 hook 都是 **postfix**，不重写原逻辑：
//    ① GetAllBasicColorEArray  → 追加 Black（让"球"进入候选）
//    ② CalculateColorEAndStates → 追加 ColorEAndState(Black, <状态>)
//
//  ★ Stage 1 状态先给 Locked：界面上看得见、点不动，避免"能选中但没有职业数据"
//    导致 GetClassNameByColorE 返回 InvalidClass 之后的连锁问题。
//    职业数据（新 Class 集合 + StartColorESDecks）装配完成后，由 CareerAssembler
//    把它放成 Free。
// =============================================================================

using System;
using System.Collections.Generic;
using Card;
using Career;
using GameData;
using HarmonyLib;

namespace ModKit.Game
{
    /// <summary>职业 / 色球接缝：让原版未启用的 Black 成为第 6 颗球。</summary>
    public static class CareerPatch
    {
        private static IGameLog _log;
        private static bool _installed;

        /// <summary>每帧驱动宿主对象（自己建的根对象）。</summary>
        private static UnityEngine.GameObject _hostGo;

        /// <summary>
        /// **本局/本机是否有 mod 声明了职业**。
        /// 这是"可随时拆装"的总开关：平台启动时虽然挂好了补丁（Harmony 补丁本身是惰性的），
        /// 但所有补丁体、界面准备、拖拽驱动都在这个标志为 false 时**直接返回**——
        /// 也就是说：不装任何职业 mod 时，游戏行为与原版完全一致；
        /// 把 mod 目录删掉重启，一切自动还原。
        /// </summary>
        internal static bool ClassDeclared;

        /// <summary>
        /// 创建每帧驱动宿主（**只在有职业声明时调用**）。
        /// 为什么必须自建根对象：实机证明挂在游戏对象上的 MonoBehaviour 与 `OrbHolderForSubGame.Update`
        /// 的 Harmony 前缀在这套界面里都不触发；自建 + `DontDestroyOnLoad` 才必定每帧跑。
        /// </summary>
        internal static void EnsureHost(IGameLog log)
        {
            try
            {
                if (_hostGo != null) { return; }
                _hostGo = new UnityEngine.GameObject("ModKit.CareerOrbTicker");
                UnityEngine.Object.DontDestroyOnLoad(_hostGo);
                _hostGo.AddComponent<CareerOrbTickerHost>();
                if (log != null) { log.Info("职业接缝：每帧驱动宿主已创建（有职业声明才会建）。"); }
            }
            catch (Exception ex)
            {
                if (log != null) { log.Warn("职业接缝：创建每帧驱动宿主失败 —— " + ex.GetType().Name + ": " + ex.Message); }
            }
        }

        /// <summary>职业界面（选球界面）的对象；它隐藏/销毁即代表玩家退出了职业界面。</summary>
        internal static UnityEngine.GameObject NewOrbPickerGo;

        /// <summary>
        /// **职业界面是否开着**（显式生命周期，用户 2026-10-17 拍板）：
        /// 进入职业界面（含重新进入）⇒ true 并由每帧宿主把自造球造出来；
        /// 退出（确定确认 / 未确定退出 / 界面被隐藏或销毁）⇒ false 且**立刻杀掉自造球**。
        /// </summary>
        internal static bool PickerOpen;

        /// <summary>我们给新球克隆出来的底座（每帧驱动在找不到游戏球时用它做锚点）。</summary>
        internal static SmallGames.WorldBetween.SubGame.ChooseColorPedestalForSubGame NewOrbPedestal;

        /// <summary>
        /// 我们那颗球（在 `InitializeOrb` 之后缓存下来）。
        /// ★ 每帧驱动**只读这个字段**：早期版本在找不到球时每帧 `Resources.FindObjectsOfTypeAll`
        ///   全场景扫描，主线程被拖死（实机表现为确认职业后整个界面卡住、日志停滞）—— 绝不再扫。
        /// </summary>
        internal static SmallGames.WorldBetween.SubGame.ChooseColorEOrbForSubGame NewOrbOrb;

        /// <summary>是否已挂钩。</summary>
        public static bool Installed { get { return _installed; } }

        /// <summary>
        /// 是否启用"自造第 6 颗球"（Black）。自建职业选择上线后不再需要玩家拖球，
        /// 因此关闭：不造球、不加底座、也不给颜色表补 Black。
        /// </summary>
        internal static readonly bool EnableCustomOrb = false;

        /// <summary>当前职业界面对象（自建职业选择要用它调游戏的确认入口）。</summary>
        internal static SmallGames.WorldBetween.SubSmallGameChooseColor CurrentPicker;

        /// <summary>新球用的颜色（原版存在但没做成球）。</summary>
        /// <summary>
        /// 本机要启用的"新球"颜色。**由 mod 的 `ClassSpec.OrbColor` 决定**（默认 Black），
        /// 平台只负责校验与启用 —— 平台自己不知道任何具体职业。
        /// </summary>
        public static ColorE NewOrbColor = ColorE.Black;

        /// <summary>新球贴图（mod 目录里的 PNG，已解析成绝对路径；空 = 用游戏资源/自绘兜底）。</summary>
        internal static string OrbSpritePath;

        /// <summary>新球显示大小倍率。</summary>
        internal static float BallScale = 1f;

        /// <summary>
        /// 由 `CareerAssembler` 在**有 mod 声明职业**时调用：把该职业声明的球颜色 / 贴图 / 大小落下来。
        /// </summary>
        internal static void ConfigureClass(ColorE orbColor, string orbSpriteAbsolutePath, float ballScale, IGameLog log)
        {
            NewOrbColor = orbColor;
            OrbSpritePath = orbSpriteAbsolutePath;
            BallScale = (ballScale <= 0f) ? 1f : ballScale;
            if (log != null)
            {
                log.Info("职业接缝：本职业占用新球 = " + orbColor +
                    "（贴图=" + (string.IsNullOrEmpty(OrbSpritePath) ? "游戏资源/自绘" : OrbSpritePath) +
                    "，大小倍率=" + BallScale + "）。");
            }
        }

        /// <summary>
        /// 新球的球状态。Stage 1 = <see cref="ColorEState.Locked"/>（看得见、点不动）；
        /// 职业数据装配好后由 CareerAssembler 改成 Free。
        /// </summary>
        public static ColorEState OrbState = ColorEState.Locked;

        /// <summary>装挂钩。可重复调用，只会生效一次。</summary>
        public static bool Install(IGameLog log)
        {
            _log = log;
            if (_installed) { return true; }

            try
            {
                var harmony = new Harmony("modkit.career");
                int ok = 0;

                // ① 色球候选列表
                var getAll = AccessTools.Method(typeof(Utilities.DitwUtil), "GetAllBasicColorEArray");
                if (getAll != null)
                {
                    harmony.Patch(getAll, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(CareerPatch), nameof(AfterGetAllBasicColor))));
                    ok++;
                }
                else
                {
                    log.Warn("职业接缝：找不到 DitwUtil.GetAllBasicColorEArray —— 第 6 颗球不会出现。");
                }

                // ② 选球界面的球状态
                var calc = AccessTools.Method(typeof(SmallGames.WorldBetween.SubSmallGameChooseColor), "CalculateColorEAndStates");
                if (calc != null)
                {
                    harmony.Patch(calc, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(CareerPatch), nameof(AfterCalculateColorEAndStates))));
                    ok++;
                }
                else
                {
                    log.Warn("职业接缝：找不到 SubSmallGameChooseColor.CalculateColorEAndStates —— 选球界面不会显示第 6 颗球。");
                }

                // ③ 选球界面：新球没有"底座"（pedestals 里只有原版 5 色），
                //    缺少底座时 InitializeOnPedestal 里会取到 null 的 _holder → 空引用，球根本不出现。
                //    所以在界面开始铺球之前，给它补一条贴图表项 + 克隆一个底座。
                var start = AccessTools.Method(typeof(SmallGames.WorldBetween.SubSmallGameChooseColor), "StartSubSmallGame");
                if (start != null)
                {
                    harmony.Patch(start, prefix: new HarmonyMethod(
                        AccessTools.Method(typeof(CareerPatch), nameof(BeforeStartSubSmallGame))));
                    ok++;
                }
                else
                {
                    log.Warn("职业接缝：找不到 SubSmallGameChooseColor.StartSubSmallGame —— 新球的底座不会被补上。");
                }

                // ④ 新球的初始化收尾：强制状态为 Free（原版 switch 没有 case Black）并把球缓存给每帧驱动
                var initOrb = AccessTools.Method(typeof(SmallGames.WorldBetween.SubSmallGameChooseColor), "InitializeOrb");
                if (initOrb != null)
                {
                    harmony.Patch(initOrb, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(CareerPatch), nameof(AfterInitializeOrb))));
                    ok++;
                }

                // ⑤ 确认按钮那条路（`LeaveSubSmallGame`）：跑完之后由插件自己判断
                //    「这局用的是不是原版白名单之外的球组合」，是则**强行切回房间**
                //    （原版那次界面切换挂在 DOTween 的 OnComplete 上，实机不会执行）。
                var leave = AccessTools.Method(typeof(SmallGames.WorldBetween.SubSmallGameChooseColor), "LeaveSubSmallGame");
                if (leave != null)
                {
                    harmony.Patch(leave,
                        postfix: new HarmonyMethod(AccessTools.Method(typeof(CareerPatch), nameof(AfterLeaveSubSmallGame))));
                    ok++;
                }

                // ⑥ 选球界面的两个收尾入口：无论走哪条（游戏自己的结束 / 隐藏），都标记"已关闭"并收球
                var endSub = AccessTools.Method(typeof(SmallGames.SubSmallGameBase), "EndSubSmallGame");
                if (endSub != null)
                {
                    harmony.Patch(endSub, postfix: new HarmonyMethod(AccessTools.Method(typeof(CareerPatch), nameof(AfterPickerClosed))));
                    ok++;
                }
                var hideSub = AccessTools.Method(typeof(SmallGames.WorldBetween.SubSmallGameChooseColor), "HideSubSmallGame");
                if (hideSub != null)
                {
                    harmony.Patch(hideSub, postfix: new HarmonyMethod(AccessTools.Method(typeof(CareerPatch), nameof(AfterPickerClosed))));
                    ok++;
                }

                // ⑦ 主菜单三色圆点：原版 `MainMenuCircles.GetColorByColorE` 是穷举 switch，
                //    只认原版 5 色，其余一律 `throw new ArgumentOutOfRangeException`
                //    —— **连 Black 都没覆盖**。奶蛙职业（Black/Purple/Void）确认后，
                //    `SetColorsOnMainMenu` 会在 `BackFromSubSmallGame` 的 DOTween 回调里抛，
                //    打断 `RefreshPlayButton` / 隐藏小游戏，表现为"回房间后按钮不亮、界面没退"。
                //    这是独立于"第 6 颗球"的缺陷：与 EnableCustomOrb 无关，装了平台就生效。
                try { MainMenuCircleColorCompat.Install(harmony, log); }
                catch (Exception ex) { log.Warn("主菜单圆点接管失败：" + ex.Message); }

                _installed = ok > 0;
                CareerPicker.Init(_log);
                _installed = ok > 0;
                CareerPicker.Init(_log);
                _installed = ok > 0;
                if (_installed)
                {
                    log.Info("职业接缝已挂钩 " + ok + " 处（色球候选 + 球状态）—— 新球 = " + NewOrbColor +
                        "，状态 = " + OrbState + "。");
                }

                // ★ 每帧宿主**不在这里建**：只有当真的有 mod 声明了职业时才需要它。
                //   `CareerAssembler` 装配成功后调 `EnsureHost`（见下）——
                //   这样"不装任何职业 mod"时平台在游戏里零足迹（可随时拆装）。

                return _installed;
            }
            catch (Exception ex)
            {
                log.Warn("职业接缝挂钩失败：" + ex.GetType().Name + " - " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 职业界面的两个收尾入口（<c>EndSubSmallGame</c> / <c>HideSubSmallGame</c>）的公共后置：
        /// 标记"职业界面已关闭"并立刻收掉自造球；再次进入时会重新创建。
        /// </summary>
        private static void AfterPickerClosed()
        {
            try { CareerPicker.Reset(); } catch { }
            CurrentPicker = null;
            if (!ClassDeclared) { return; }
            PickerOpen = false;
            NewOrbPickerGo = null;
            try { CareerOrbTicker.DestroyBallNow("职业界面关闭（EndSubSmallGame / HideSubSmallGame）"); } catch { }
        }

        /// <summary>把新颜色补进"基础色球"列表（原版只返回 5 色）。无职业声明时不动。</summary>
        private static void AfterGetAllBasicColor(ref ColorE[] __result)
        {
            if (!EnableCustomOrb) { return; }
            if (!ClassDeclared) { return; }
            var list = new List<ColorE>(__result ?? new ColorE[0]);
            if (!list.Contains(NewOrbColor)) { list.Add(NewOrbColor); }
            __result = list.ToArray();
        }

        /// <summary>给选球界面的状态表补一条（原版 switch 没有 case Black）。无职业声明时不动。</summary>
        private static void AfterCalculateColorEAndStates(ref List<ColorEAndState> __result)
        {
            if (!EnableCustomOrb) { return; }
            if (!ClassDeclared) { return; }
            if (__result == null) { return; }
            for (int i = 0; i < __result.Count; i++)
            {
                if (__result[i] != null && __result[i].colorE == NewOrbColor) { return; }   // 已有，别重复加
            }
            __result.Add(new ColorEAndState(NewOrbColor, OrbState));
        }

        // ---------------------------------------------------------------------
        //  选球界面：给新球补「贴图表项 + 底座」
        //
        //  为什么必须做（2026-10-15 实机：日志显示职业装配全部成功、但界面上找不到新球）：
        //    · 界面按 `CalculateColorEAndStates()` 逐个 Instantiate 球；
        //    · `ColorEToOrbSprite[] colorEToOrbSprites` 里没有 Black ⇒ 球没有贴图；
        //    · `pedestals` 里没有 Black ⇒ `homePedestal` 为 null ⇒
        //      `InitializeOnPedestal` 里 `transform.position = _holder.orbHolder.position` 取到 null 的 _holder
        //      ⇒ 空引用，球根本不会摆出来。
        //  ⇒ 所以在界面开始铺球之前：补一条贴图表项 + 克隆一个底座（绕原底座质心镜像摆位）。
        // ---------------------------------------------------------------------

        /// <summary>界面开始铺球之前的准备（无职业声明时不动）。</summary>
        private static void BeforeStartSubSmallGame(SmallGames.WorldBetween.SubSmallGameChooseColor __instance)
        {
            if (!ClassDeclared) { return; }
            if (__instance == null) { return; }
            // ★ 记住职业界面的对象：它一被隐藏/销毁，就说明玩家退出了职业界面（确定或未确定）
            //   ⇒ 自造球必须跟着收掉（否则球会一直挂在最顶层，直到进游戏）。
            NewOrbPickerGo = __instance.gameObject;
            CurrentPicker = __instance;
            try { CareerPicker.Show(); } catch { }
            if (!EnableCustomOrb) { return; }   // 自建面板接管 ⇒ 不造球、不准备底座
            PickerOpen = true;     // ★ 进入职业界面 ⇒ 允许（并由宿主）造球
            WarnOnceStatic("picker-open", "职业接缝：进入职业界面 → 自造球将（重新）创建。");
            try
            {
                EnsureOrbSpriteEntry(__instance);
                EnsurePedestal(__instance);
            }
            catch (Exception ex)
            {
                if (_log != null) { _log.Warn("职业接缝：准备新球的贴图/底座失败 —— " + ex.GetType().Name + ": " + ex.Message); }
            }
        }

        /// <summary>给 `colorEToOrbSprites` 补一条新颜色（优先用游戏自带资源，否则借 Void 的图）。</summary>
        private static void EnsureOrbSpriteEntry(SmallGames.WorldBetween.SubSmallGameChooseColor ui)
        {
            var arr = ui.colorEToOrbSprites;
            if (arr != null)
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i].colorE == NewOrbColor) { return; }   // 已经有了
                }
            }

            UnityEngine.Sprite orbSprite = TryGameSpriteForNewColor();
            UnityEngine.Sprite pedestalSprite = null;
            if (arr != null)
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i].colorE == ColorE.Void)
                    {
                        if (orbSprite == null) { orbSprite = arr[i].orbSprite; }
                        pedestalSprite = arr[i].pedestalSprite;
                        break;
                    }
                }
            }

            var list = new List<SmallGames.WorldBetween.ColorEToOrbSprite>(
                arr ?? new SmallGames.WorldBetween.ColorEToOrbSprite[0]);
            list.Add(new SmallGames.WorldBetween.ColorEToOrbSprite
            {
                colorE = NewOrbColor,
                orbSprite = orbSprite,
                pedestalSprite = pedestalSprite
            });
            ui.colorEToOrbSprites = list.ToArray();

            if (_log != null)
            {
                _log.Info("职业接缝：已补新球贴图表项（" + NewOrbColor + "，贴图来源=" +
                    (orbSprite == null ? "无（可能是空白球）" : orbSprite.name) + "）。");
            }
        }

        /// <summary>
        /// 用游戏自己的颜色资源取新球的贴图 —— `WorldManager.GetManaSpriteFromEnumColor` 走的
        /// 就是 `colorEAssetRefSo`，而 `GetColorFromEnumColor(Black)` 专门为 Black 分了支，
        /// 说明这个 build 里有 Black 的美术资源。
        /// </summary>
        private static UnityEngine.Sprite TryGameSpriteForNewColor()
        {
            try
            {
                var wm = World.WorldManager.Instance;
                if (wm == null) { return null; }

                var field = wm.GetType().GetField("colorEAssetRefSo",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                if (field == null) { return null; }

                var so = field.GetValue(wm);
                if (so == null) { return null; }

                var getSprite = so.GetType().GetMethod("GetSpriteByColorE");
                if (getSprite == null) { return null; }

                return getSprite.Invoke(so, new object[] { NewOrbColor }) as UnityEngine.Sprite;
            }
            catch { return null; }
        }

        /// <summary>克隆一个原版底座给新颜色，并把位置镜像到现有底座之外。</summary>
        private static void EnsurePedestal(SmallGames.WorldBetween.SubSmallGameChooseColor ui)
        {
            if (ui.pedestals == null)
            {
                ui.pedestals = new List<SmallGames.WorldBetween.SubGame.ChooseColorPedestalForSubGame>();
            }
            for (int i = 0; i < ui.pedestals.Count; i++)
            {
                if (ui.pedestals[i] != null && ui.pedestals[i].pedestalColorE == NewOrbColor) { return; }   // 已经有了
            }

            SmallGames.WorldBetween.SubGame.ChooseColorPedestalForSubGame template = null;
            for (int i = 0; i < ui.pedestals.Count; i++)
            {
                if (ui.pedestals[i] != null) { template = ui.pedestals[i]; break; }
            }
            if (template == null)
            {
                if (_log != null) { _log.Warn("职业接缝：没有可克隆的底座，新球不会出现。"); }
                return;
            }

            var cloneGo = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent);
            cloneGo.name = "ModKitOrbPedestal_" + NewOrbColor;

            // 上一次界面留下的克隆底座要清掉（否则每次重开界面都会多一个）
            if (NewOrbPedestal != null) { UnityEngine.Object.Destroy(NewOrbPedestal.gameObject); }
            var clone = cloneGo.GetComponent<SmallGames.WorldBetween.SubGame.ChooseColorPedestalForSubGame>();
            if (clone == null)
            {
                UnityEngine.Object.Destroy(cloneGo);
                return;
            }

            // 摆位：绕现有底座质心把模板镜像过去 —— 原版 4 个底座成对分布，镜像点通常正好是空位
            var centroid = UnityEngine.Vector3.zero;
            int count = 0;
            for (int i = 0; i < ui.pedestals.Count; i++)
            {
                if (ui.pedestals[i] != null) { centroid += ui.pedestals[i].transform.position; count++; }
            }
            if (count > 0)
            {
                centroid /= count;
                clone.transform.position = 2f * centroid - template.transform.position;
            }

            clone.pedestalColorE = NewOrbColor;
            clone.subSmallGameChooseCampaignColorE = ui;
            NewOrbPedestal = clone;

            ui.pedestals.Add(clone);
            clone.Initialize();   // 按 colorEToOrbSprites 取底座贴图（我们刚补过 Black 这一条）

            if (_log != null)
            {
                _log.Info("职业接缝：已为新球克隆底座 " + clone.name + "（位置 " + clone.transform.position + "）。");
            }
        }

        // ---------------------------------------------------------------------
        //  新球初始化收尾 + 确认流程
        //
        //  实机证明（2026-10-15）：Unity 的 `OnMouseDown/OnMouseDrag` 从来不派发到这颗球上，
        //  所以**拖拽改由插件自己的每帧驱动**（`CareerOrbDriver.CareerOrbTicker`）负责；
        //  这里只做两件事：把球状态兜成 Free、把球引用缓存给驱动。
        // ---------------------------------------------------------------------

        private static void AfterInitializeOrb(ref SmallGames.WorldBetween.SubGame.ChooseColorEOrbForSubGame __result)
        {
            if (!ClassDeclared) { return; }
            if (__result == null || __result.colorE != NewOrbColor) { return; }

            // 原版 `CalculateColorEAndStates` 的 switch 没有 case Black，球在这里再兜一次状态：
            // Locked 会被 SetActive(false) 藏起来，Free 才是"可以拖进槽"。
            if (__result.colorEState != ColorEState.Free)
            {
                __result.colorEState = ColorEState.Free;
            }

            // 缓存给每帧驱动（`CareerOrbTicker` 只读这个字段，绝不做任何扫描）
            NewOrbOrb = __result;
        }

        /// <summary>确认按钮流程：跑完之后判断这局是不是"非原版球组合"（见下）。</summary>
        private static void AfterLeaveSubSmallGame(SmallGames.WorldBetween.SubSmallGameChooseColor __instance)
        {
            if (!ClassDeclared) { return; }
            WarnOnceStatic("leave-out", "职业接缝·诊断[确认·完成]：LeaveSubSmallGame 已执行到底。");
            // ★ 退出职业界面 ⇒ 立刻收球（显式生命周期）
            PickerOpen = false;
            try { CareerOrbTicker.DestroyBallNow("LeaveSubSmallGame（确认/退出职业界面）"); } catch { }

            // ★ 主动检测：这次确认用到的球组合里有没有**原版白名单之外**的颜色（目前只有 Black）。
            //   有 ⇒ 原版界面不会自己切走（那个 DOTween 的 OnComplete 回调实机不执行），
            //   于是不等游戏逻辑，直接**强行切回房间**。
            bool nonVanilla = false;
            string pickedText = "?";
            try
            {
                var picked = ReadPickedColors(__instance);
                if (picked != null)
                {
                    var names = new List<string>();
                    foreach (var c in picked)
                    {
                        names.Add(c.ToString());
                        if (!IsVanillaOrbColor(c)) { nonVanilla = true; }
                    }
                    pickedText = string.Join("/", names.ToArray());
                }
            }
            catch (Exception ex)
            {
                WarnOnceStatic("leave-scan", "职业接缝：读取已选球失败 —— " + ex.Message);
            }

            if (nonVanilla)
            {
                WarnOnceStatic("nonvanilla",
                    "职业接缝·诊断[非原版组合]：槽=" + pickedText + " ⇒ 直接强行切回房间。");
                CareerOrbTicker.ForceSwitchBack(__instance, "非原版职业球组合 " + pickedText);
                return;
            }

            // 原版组合：仍留一次延迟兜底（覆盖原版回调偶发不执行的情况）
            CareerOrbTicker.QueueForceExit(__instance);
        }

        /// <summary>反射读选球界面里三个槽的颜色（原版 private `GetPickedColorES`）。</summary>
        private static Card.ColorE[] ReadPickedColors(SmallGames.WorldBetween.SubSmallGameChooseColor ui)
        {
            var m = ui.GetType().GetMethod("GetPickedColorES",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);
            return (m == null) ? null : (m.Invoke(ui, null) as Card.ColorE[]);
        }

        /// <summary>原版能做成球的 5 色（Black 不在其中 ⇒ 用了它就说明这局走的是 mod 职业）。</summary>
        private static bool IsVanillaOrbColor(ColorE c)
        {
            return c == ColorE.Red || c == ColorE.Green || c == ColorE.Blue ||
                   c == ColorE.Purple || c == ColorE.Void;
        }

        /// <summary>同一个 key 只警告一次（拖拽驱动每帧都可能出错）。</summary>
        internal static void WarnOnceStatic(string key, string message)
        {
            if (!_warnOnce.Add(key)) { return; }
            if (_log != null) { _log.Warn(message); }
        }

        private static readonly HashSet<string> _warnOnce = new HashSet<string>();
    }
}
