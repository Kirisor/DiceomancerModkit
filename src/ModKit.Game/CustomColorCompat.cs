// =============================================================================
//  ModKit.Game · 自定义颜色接缝（真·第 7 色 / (ColorE)6 及以后）
//
//  背景：`ColorE` 是编译进 Assembly-CSharp 的枚举（Red/Green/Blue/Purple/Void/Black），
//  mod 无法新增成员；但 C# 枚举本质是整数，`(ColorE)6` 是**合法取值**（CLR 不校验）。
//  问题不在"能不能造出来"，而在"游戏各处有没有接住它"。
//
//  本文件按「接不住会怎样」逐点接管（编号沿用设计文档《ModKit-职业球扩展方案》§8 的节点表）：
//
//    ① MainMenuCircles.GetColorByColorE        → 已在 CareerColorCompat.cs 修（会先查本注册表）
//    ② WorldManager.GetColorFromEnumColor /
//       GetBrightManaColorFromEnumColor /
//       GetManaSpriteFromEnumColor             → 本文件的 3 个 prefix
//    ③ AssetRefSo.ColorEToColorDict / ColorEToSpriteDict
//                                                → 本文件 InjectAssetRefs（public 字典，直接注入）
//    ④ ManaManager.ManaManagerInitialize        → 本文件 postfix：**每换一个战场都重新注入**
//                                                  （Instance 是每战场的，初始化会从序列化列表重建字典）
//    ⑤ CardBase 卡面三色槽                      → **不改**：`_cardColorEs` 按枚举值降序取前三，
//                                                  自定义色排在最前 = 当主色。修正 ② 之后颜色是对的，
//                                                  "新色优先做主色"是可接受语义（见文末）。
//    ⑥ 音效名 "GainMana_<ColorE>"               → 本文件 prefix：自定义色改播 GainMana_Black，
//                                                  否则 UtilSoundDataManager 会 Debug.LogError 刷屏
//    ⑦ DitwUtil.GetAllColorEArray（字面量 6 色） → 本文件 postfix：把已注册色追加进"全部颜色"，
//                                                  否则清空/随机改色效果永远不会产出自定义色
//    ⑧ 文案键 Encyclopedia/Orb_<n>(_Des)       → 本文件注入（走既有的 LocalizationPump）
//    ⑨ 玩家自定义配色 UI（ManaColorManager）    → **不改**：它硬校验 5 项且要求 Enum.IsDefined，
//                                                  即"玩家不能给自定义色换皮"。战斗不受影响，
//                                                  属可接受的已知限制（改它要动它的 5 件 UI 槽位）。
//
//  行为边界：**注册表为空时，所有补丁直接放行原逻辑，零行为变化。**
//  也就是说这个文件在没有任何自定义色时，等价于不存在。
//
//  自检入口：配置 [Diagnostics] CustomColorSmokeTest = true 会注册一个"黄"（(ColorE)6），
//  并在第一场战斗投递 1 颗黄色法力，用来端到端验证 ②③④⑥⑦⑧。
// =============================================================================

using System;
using System.Collections.Generic;
using Card;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Command;

namespace ModKit.Game
{
    /// <summary>自定义颜色注册表 + 各消费点的接管补丁。</summary>
    internal static class CustomColorCompat
    {
        /// <summary>一个自定义颜色的全部运行期形态。</summary>
        internal sealed class CustomColor
        {
            public ColorE Value;      // 游戏侧取值（枚举里没有定义，靠强转）
            public Color Base;        // 主体色（卡框/提示）
            public Color Bright;      // 亮色（法力球高光）
            public Sprite Sprite;     // 法力图标（可空：空则沿用游戏路径）
            public string NameZh;     // 中文名（Orb_<n> 文案）
            public string DescZh;     // 描述（Orb_<n>_Des，对齐原版「XX之力！」句式）
            public Sprite PipSprite;  // 卡面「流派色小点」贴图（可空：空则用全局配置/游戏小点）
            public string OwnerMod;   // 声明它的 mod id（诊断用）
        }

        private static readonly List<CustomColor> _colors = new List<CustomColor>();
        private static readonly HashSet<int> _warned = new HashSet<int>();
        private static IGameLog _log;
        private static bool _localizationDone;
        private static bool _smokeSpawned;
        private static bool _color6Logged;

        internal static int Count { get { return _colors.Count; } }

        /// <summary>只读视图：供能力探测把自定义色一并列为"可用费用色"。</summary>
        internal static System.Collections.Generic.IReadOnlyList<CustomColor> AllRegistered()
        {
            return _colors;
        }

        // ------------------------------------------------------------------
        //  注册表
        // ------------------------------------------------------------------

        /// <summary>注册一个自定义颜色。value 必须 &gt; Black（≥6）。返回是否新注册。</summary>
        internal static bool Register(int rawValue, Color color, string nameZh, Sprite sprite,
                                      string descZh = null, Sprite pipSprite = null, string ownerMod = null)
        {
            try
            {
                if (rawValue <= (int)ColorE.Black || rawValue > 15)
                {
                    WarnOnce(rawValue, "自定义颜色：取值 " + rawValue + " 非法（必须 > " + (int)ColorE.Black +
                        "，且暂限 ≤15）。");
                    return false;
                }
                var value = (ColorE)rawValue;
                CustomColor existing;
                if (TryGet(value, out existing)) { return false; }

                var entry = new CustomColor
                {
                    Value = value,
                    Base = color,
                    Bright = Brighten(color),
                    Sprite = sprite,
                    NameZh = string.IsNullOrEmpty(nameZh) ? ("自定义" + rawValue) : nameZh,
                    DescZh = descZh,
                    PipSprite = pipSprite,
                    OwnerMod = ownerMod
                };
                _colors.Add(entry);

                // 已存在的容器立刻补一遍（战场可能还没建，失败不致命）
                SafeInjectRuntime();
                TryInjectLocalization();

                if (_log != null)
                {
                    _log.Info("自定义颜色：已注册 " + entry.NameZh + "（ColorE=" + rawValue +
                        "，贴图=" + (sprite == null ? "无（沿用游戏路径）" : sprite.name) + "）→ 注册表共 " + _colors.Count + " 个。");
                }
                return true;
            }
            catch (Exception ex)
            {
                WarnOnce(rawValue, "自定义颜色：注册 " + rawValue + " 失败 —— " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>查注册表。</summary>
        internal static bool TryGet(ColorE value, out CustomColor color)
        {
            for (int i = 0; i < _colors.Count; i++)
            {
                if (_colors[i].Value == value) { color = _colors[i]; return true; }
            }
            color = null;
            return false;
        }

        /// <summary>诊断用：注册"黄"（(ColorE)6）。图标优先用配置指定文件，否则现画一个圆。</summary>
        internal static void RegisterSmokeYellow()
        {
            var c = new Color(0.639f, 0.573f, 0.071f, 1f);

            // [Diagnostics] CustomColorSprite：给作者/维护者一个"可以直接改"的贴图文件
            var fileSprite = TryLoadConfiguredSprite(DiagnosticSwitches.CustomColorSprite, "法力球图标");
            Register(6, c, "黄色能量球", fileSprite != null ? fileSprite : MakeCircleSprite(c), "秩序与平衡之力！");
        }

        /// <summary>按配置路径加载 PNG（绝对路径，或相对游戏根目录，如 BepInEx/ModKitCustomColors/color6.png）。</summary>
        private static Sprite TryLoadConfiguredSprite(string path, string label)
        {
            if (string.IsNullOrEmpty(path)) { return null; }
            try
            {
                var abs = path;
                if (!System.IO.Path.IsPathRooted(abs))
                {
                    var plugins = System.IO.Path.GetDirectoryName(typeof(CustomColorCompat).Assembly.Location);
                    var bep = System.IO.Path.GetDirectoryName(plugins ?? "");
                    var game = System.IO.Path.GetDirectoryName(bep ?? "");
                    abs = System.IO.Path.Combine(game ?? "", path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                }

                string err;
                var sp = ModArt.Load(abs, out err);
                if (sp == null)
                {
                    WarnOnce(-13, "自定义颜色：" + label + "加载失败（" + path + " → " + abs + "）—— " + err);
                }
                else if (_log != null)
                {
                    _log.Info("自定义颜色：" + label + "使用文件 " + abs + "（改图后重启游戏即可生效）。");
                }
                return sp;
            }
            catch (Exception ex)
            {
                WarnOnce(-14, "自定义颜色：" + label + "读取异常 —— " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>自检用的自定义色取值（当前固定为黄 = 6）。</summary>
        internal static ColorE DemoValue { get { return (ColorE)6; } }

        /// <summary>
        /// 诊断：把一张卡的**费用**整份替换成 1 槽自定义色（默认黄），并把它加进卡面流派色。
        /// 目的：验证消费链路（卡面费用图标 / 能否用该色法力支付 / 打出后结算 / 卡框主色）。
        /// 由 <c>[Diagnostics] CustomColorCostCard</c> 指定目标卡；不指定时原样返回。
        /// </summary>
        internal static CardInfo ApplyCostTest(string cardId, CardInfo info, IGameLog log)
        {
            try
            {
                var wanted = DiagnosticSwitches.CustomColorCostCard;
                if (string.IsNullOrEmpty(wanted)) { return info; }
                wanted = wanted.Trim();

                bool match = !string.IsNullOrEmpty(cardId) &&
                             (cardId.Equals(wanted, StringComparison.OrdinalIgnoreCase) ||
                              cardId.EndsWith("." + wanted, StringComparison.OrdinalIgnoreCase) ||
                              cardId.EndsWith(wanted, StringComparison.OrdinalIgnoreCase));
                if (!match) { return info; }

                RegisterSmokeYellow();      // 确保注册表里有这个颜色（否则支付/图标都查不到）

                var value = DemoValue;
                info.cardManaCostColorEArrays = new ColorEArray[] { new ColorEArray(value) };

                // 同时把它加进卡面流派色，顺带验证节点⑤（卡框主色）：
                // CardBase 按枚举值降序取前三，6 会排在最前当主色。
                var colors = new List<ColorE>(info.cardColorEs ?? new ColorE[0]);
                if (!colors.Contains(value)) { colors.Add(value); }
                info.cardColorEs = colors.ToArray();

                if (log != null)
                {
                    log.Warn("自定义颜色（费用自检）：已把卡 " + cardId + " 的费用替换为 1 槽「黄」（ColorE=" +
                        (int)value + "），并加入卡面流派色。仅用于验证，正式游玩请把 [Diagnostics] CustomColorCostCard 清空。");
                }
            }
            catch (Exception ex)
            {
                if (log != null) { log.Warn("自定义颜色（费用自检）失败：" + ex.GetType().Name + ": " + ex.Message); }
            }
            return info;
        }

        // ------------------------------------------------------------------
        //  安装
        // ------------------------------------------------------------------

        /// <summary>装挂钩。可重复调用；找不到方法时逐项降级并 WARN，不影响其余补丁。</summary>
        internal static void Install(IGameLog log)
        {
            _log = log;
            try
            {
                var harmony = new Harmony("modkit.customcolor");
                int ok = 0;

                // ② 世界取色三件套
                ok += PatchPrefix(harmony, typeof(World.WorldManager), "GetColorFromEnumColor",
                    nameof(BeforeGetColorFromEnumColor), "世界取色");
                ok += PatchPrefix(harmony, typeof(World.WorldManager), "GetBrightManaColorFromEnumColor",
                    nameof(BeforeGetBrightManaColorFromEnumColor), "世界亮色");
                ok += PatchPrefix(harmony, typeof(World.WorldManager), "GetManaSpriteFromEnumColor",
                    nameof(BeforeGetManaSpriteFromEnumColor), "世界法力贴图");

                // ④ 每个战场的法力管理器初始化后重新注入
                ok += PatchPostfix(harmony, typeof(Combat.ManaManager), "ManaManagerInitialize",
                    nameof(AfterManaManagerInitialize), "法力图标字典注入");

                // ⑦ "全部颜色"枚举
                ok += PatchPostfix(harmony, typeof(Utilities.DitwUtil), "GetAllColorEArray",
                    nameof(AfterGetAllColorEArray), "全部颜色枚举");

                // ⑥ 音效名兜底
                ok += PatchPrefix(harmony, typeof(Utilities.UtilSoundDataManager), "GetSoundRefByName",
                    nameof(BeforeGetSoundRefByName), "法力音效名");

                if (log != null)
                {
                    log.Info("自定义颜色接缝：已挂钩 " + ok + "/6 处（注册表 " + _colors.Count +
                        " 个；为空时全部补丁放行原逻辑）。");
                }
            }
            catch (Exception ex)
            {
                if (log != null) { log.Warn("自定义颜色接缝：安装失败 —— " + ex.GetType().Name + ": " + ex.Message); }
            }

            // ★ 这里**不再**提前注册自检颜色。
            //   颜色现在由各 mod 的 colors.json 声明（平台 ①.5 步读取，发生在本方法之后）。
            //   若此处抢先注册 value=6，就会用"现画的描边圆"占位，
            //   导致 mod 的图标因"该值已被占用"被跳过 —— 实机复现过（贴图退回描边圆）。
            //   自检需要颜色时改为**惰性**注册：见 ApplyCostTest / SpawnSmokeMana，
            //   那时若 mod 已声明同值，Register 会返回 false 并保留 mod 的图标。
        }

        private static int PatchPrefix(Harmony harmony, Type type, string method, string prefixName, string label)
        {
            var target = AccessTools.Method(type, method);
            if (target == null)
            {
                WarnOnce(-1, "自定义颜色：" + label + " —— 找不到 " + type.Name + "." + method +
                    "（游戏更新改签名？该点退化为原版行为）。");
                return 0;
            }
            try
            {
                harmony.Patch(target, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(CustomColorCompat), prefixName)));
                return 1;
            }
            catch (Exception ex)
            {
                WarnOnce(-2, "自定义颜色：" + label + " 补丁失败 —— " + ex.GetType().Name + ": " + ex.Message);
                return 0;
            }
        }

        private static int PatchPostfix(Harmony harmony, Type type, string method, string postfixName, string label)
        {
            var target = AccessTools.Method(type, method);
            if (target == null)
            {
                WarnOnce(-1, "自定义颜色：" + label + " —— 找不到 " + type.Name + "." + method +
                    "（游戏更新改签名？该点退化为原版行为）。");
                return 0;
            }
            try
            {
                harmony.Patch(target, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(CustomColorCompat), postfixName)));
                return 1;
            }
            catch (Exception ex)
            {
                WarnOnce(-2, "自定义颜色：" + label + " 补丁失败 —— " + ex.GetType().Name + ": " + ex.Message);
                return 0;
            }
        }

        // ------------------------------------------------------------------
        //  ② 世界取色三件套（prefix）
        // ------------------------------------------------------------------

        private static bool BeforeGetColorFromEnumColor(ColorE colorE, ref Color __result)
        {
            CustomColor c;
            if (!TryGet(colorE, out c)) { return true; }
            __result = c.Base;
            CardManaSlotPatch.LogColorOnce(colorE, __result);   // 诊断：确认渲染取到的是注册表颜色，而不是兜底白
            return false;
        }

        /// <summary>诊断：卡面设色时，把传进来的颜色数组打出来（含自定义色的一次）。</summary>
        [HarmonyPatch(typeof(Card.CardBase), "SetCardColorE")]
        internal static class CardSetColorDiagnostic
        {
            private static bool _logged;

            private static void Postfix(ColorE[] colorEs)
            {
                if (_logged || colorEs == null) { return; }
                bool hasCustom = false;
                for (int i = 0; i < colorEs.Length; i++) { if ((int)colorEs[i] > (int)ColorE.Black) { hasCustom = true; break; } }
                if (!hasCustom) { return; }
                _logged = true;

                var parts = new string[colorEs.Length];
                for (int i = 0; i < colorEs.Length; i++) { parts[i] = ((int)colorEs[i]).ToString(); }
                LogOnce("自定义颜色：CardBase.SetCardColorE 收到 [" + string.Join("/", parts) +
                        "]（含自定义色，说明卡对象拿到了 6；若界面仍显示虚色，问题在材质/着色器取色）。");
            }
        }

        private static bool BeforeGetBrightManaColorFromEnumColor(ColorE colorE, ref Color __result)
        {
            CustomColor c;
            if (!TryGet(colorE, out c)) { return true; }
            __result = c.Bright;
            return false;
        }

        private static bool BeforeGetManaSpriteFromEnumColor(ColorE colorE, ref Sprite __result)
        {
            CustomColor c;
            if (!TryGet(colorE, out c) || c.Sprite == null) { return true; }   // 没贴图就交还原版（返 null）
            __result = c.Sprite;
            return false;
        }

        // ------------------------------------------------------------------
        //  ④ 每个战场的法力管理器初始化后：注入字典 + 补文案 + 自检投递法力
        // ------------------------------------------------------------------

        private static void AfterManaManagerInitialize()
        {
            if (_colors.Count == 0) { return; }
            try
            {
                InjectIntoLiveManaManager();
                InjectAssetRefs();
                TryInjectLocalization();
                MaybeSpawnSmokeMana();
            }
            catch (Exception ex)
            {
                WarnOnce(-3, "自定义颜色：战场注入失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void SafeInjectRuntime()
        {
            try
            {
                InjectIntoLiveManaManager();
                InjectAssetRefs();
            }
            catch (Exception ex)
            {
                WarnOnce(-4, "自定义颜色：立即注入失败（等下一场战斗）—— " + ex.Message);
            }
        }

        /// <summary>
        /// 把注册表里的颜色补进当前战场的 `ManaIconDic`（法力球与费用图标的唯一来源）。
        /// </summary>
        private static void InjectIntoLiveManaManager()
        {
            Combat.ManaManager mm;
            try { mm = Combat.ManaManager.Instance; }
            catch { return; }                     // 不在战场里（Instance 会解引用当前战场）
            if (mm == null) { return; }

            NormalizeSpriteSizes(mm);

            for (int i = 0; i < _colors.Count; i++)
            {
                var c = _colors[i];
                if (c.Sprite == null) { continue; }
                var key = new ColorEArray(c.Value);
                // ★ 每次都覆盖：字典里若残留"归一化之前"的旧 sprite，跳过会让巨大图标一直留着
                mm.ManaIconDic[key] = c.Sprite;
                if (_log != null)
                {
                    _log.Info("自定义颜色：已注入法力图标 " + c.NameZh + "（" + (int)c.Value + "，世界高 " +
                        (c.Sprite.rect.height / c.Sprite.pixelsPerUnit).ToString("0.000") + "）。");
                }
            }
        }

        /// <summary>
        /// 把颜色/贴图补进 `AssetRefSo` 的两个 public 字典。
        /// 这覆盖了不经 WorldManager 而直接查 SO 的调用点（ManaColorManager、CareerUpgradeOption 等）。
        /// ★ 全程走反射：`AssetRefSo` 继承 `SerializedScriptableObject`（Sirenix.Serialization），
        ///   而平台编译链没有引用该程序集（`build-game.sh` 的引用清单里没有）——
        ///   直接写类型会报 CS0012。这也是 `CareerPatch.TryGameSpriteForNewColor` 早就用反射的原因。
        /// </summary>
        private static void InjectAssetRefs()
        {
            World.WorldManager wm;
            try { wm = World.WorldManager.Instance; }
            catch { return; }
            if (wm == null) { return; }

            object so;
            try
            {
                var f = wm.GetType().GetField("colorEAssetRefSo",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                so = (f == null) ? null : f.GetValue(wm);
            }
            catch { return; }
            if (so == null) { return; }

            var colorDict = ReadDictionary(so, "ColorEToColorDict");
            var spriteDict = ReadDictionary(so, "ColorEToSpriteDict");

            NormalizeSpriteSizes();

            for (int i = 0; i < _colors.Count; i++)
            {
                var c = _colors[i];
                try
                {
                    if (colorDict != null && !colorDict.Contains(c.Value)) { colorDict[c.Value] = c.Base; }
                    if (spriteDict != null && c.Sprite != null)
                    {
                        spriteDict[c.Value] = c.Sprite;   // 始终覆盖，避免旧引用残留
                    }
                }
                catch (Exception ex)
                {
                    WarnOnce(-9, "自定义颜色：注入 AssetRefSo 失败 —— " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        /// <summary>
        /// 把自定义色的法力贴图**对齐到原版法力图标的实际显示尺寸**。
        ///
        /// 原版机制（反编译口径）：
        ///   CardBase.SetManaCost → Instantiate(manaCostPrefab)（缩放 1，相邻间距 0.275 世界单位）
        ///   ManaCost.SetCostColorEs / Mana.InitializeManaColor
        ///     → spriteRenderer.sprite = ManaManager.Instance.ManaIconDic[colorEs]
        ///   ⇒ 屏幕尺寸 = sprite 世界尺寸 = rect.size / pixelsPerUnit。
        ///
        /// ★ 参考 sprite 必须取 ManaIconDic 里的原版条目（由 ManaManagerInitialize 从
        ///   colorEsToSpiteList 建立）。早先误用 WorldManager.GetManaSpriteFromEnumColor(Red)，
        ///   那是另一张更大的图（世界高约 1.0），照它对齐全把图标撑得更大——实机已复现。
        ///
        /// 修法：以 ManaIconDic[Red] 的世界宽为基准，用 Sprite.Create(..., ppu) 重建 sprite，
        /// 使世界宽一致；贴图本身不缩放。CustomColorIconScale 可微调（1.0 = 同尺寸）。
        /// </summary>
        private static void NormalizeSpriteSizes(Combat.ManaManager mm = null)
        {
            try
            {
                var reference = ResolveReferenceSprite(mm);
                if (reference == null || reference.pixelsPerUnit <= 0f) { return; }

                float refWorldW = reference.rect.width / reference.pixelsPerUnit;
                float scale = DiagnosticSwitches.CustomColorIconScale;
                float target = Mathf.Max(0.001f, refWorldW * scale);

                if (Mathf.Abs(target - _lastTargetWorld) < 0.0005f) { return; }
                _lastTargetWorld = target;

                for (int i = 0; i < _colors.Count; i++)
                {
                    var c = _colors[i];
                    var sp = c.Sprite;
                    if (sp == null || sp.texture == null) { continue; }

                    var tex = sp.texture;
                    float ppu = tex.width / target;
                    c.Sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);

                    if (_log != null)
                    {
                        _log.Info("自定义颜色：法力图标按 ManaIconDic 原版尺寸对齐（原版 " + reference.name + " " +
                            reference.rect.width + "x" + reference.rect.height + "px/" + reference.pixelsPerUnit +
                            "ppu = 世界宽 " + refWorldW.ToString("0.000") + " -> 本图 " + tex.width + "x" + tex.height +
                            "px/" + ppu.ToString("0") + "ppu，倍率 " + scale.ToString("0.##") + "）。");
                    }
                }
            }
            catch (Exception ex)
            {
                WarnOnce(-15, "自定义颜色：法力图标尺寸对齐失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>取基准 sprite：优先 ManaIconDic 的原版 Red 条目，其次 WorldManager（仅兜底）。</summary>
        private static Sprite ResolveReferenceSprite(Combat.ManaManager mm)
        {
            try
            {
                if (mm == null) { mm = Combat.ManaManager.Instance; }
            }
            catch { mm = null; }

            if (mm != null)
            {
                try
                {
                    var key = new ColorEArray(ColorE.Red);
                    Sprite s;
                    if (mm.ManaIconDic.TryGetValue(key, out s) && s != null) { return s; }
                }
                catch { }
            }

            // 刻意不退回 WorldManager：那里的 Red sprite 与费用/法力球实际使用的不是同一张
            // （世界高约 1.0），照它对齐全把图标撑大（实机已复现）。宁可这次不对齐，
            // 等进了战场由 ④（ManaManagerInitialize 之后的注入）用 ManaIconDic 的基准重算。
            return null;
        }

        private static float _lastTargetWorld = -1f;

        /// <summary>反射取 SO 上的 `Dictionary&lt;ColorE, T&gt;` 字段，按 IDictionary 操作。</summary>
        private static System.Collections.IDictionary ReadDictionary(object target, string fieldName)
        {
            try
            {
                var f = target.GetType().GetField(fieldName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                return (f == null) ? null : (f.GetValue(target) as System.Collections.IDictionary);
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------
        //  ⑦ "全部颜色"枚举（postfix）
        // ------------------------------------------------------------------

        private static void AfterGetAllColorEArray(ref ColorE[] __result)
        {
            if (_colors.Count == 0) { return; }
            try
            {
                var list = new List<ColorE>(__result ?? new ColorE[0]);
                for (int i = 0; i < _colors.Count; i++)
                {
                    if (!list.Contains(_colors[i].Value)) { list.Add(_colors[i].Value); }
                }
                __result = list.ToArray();
            }
            catch (Exception ex)
            {
                WarnOnce(-5, "自定义颜色：扩充 GetAllColorEArray 失败 —— " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        //  ⑥ 音效名兜底（prefix）："GainMana_6" → "GainMana_Black"
        // ------------------------------------------------------------------

        private static void BeforeGetSoundRefByName(ref string sfxName)
        {
            if (_colors.Count == 0 || string.IsNullOrEmpty(sfxName)) { return; }
            const string prefix = "GainMana_";
            if (!sfxName.StartsWith(prefix, StringComparison.Ordinal)) { return; }

            int value;
            if (!int.TryParse(sfxName.Substring(prefix.Length), out value)) { return; }
            CustomColor c;
            if (!TryGet((ColorE)value, out c)) { return; }

            sfxName = prefix + "Black";           // Black 一定在音效表里
            WarnOnce(-6, "自定义颜色：音效 \"GainMana_" + value + "\" 不存在，改用 GainMana_Black（只提示一次）。");
        }

        // ------------------------------------------------------------------
        //  ⑧ 文案：Encyclopedia/Orb_<n> 与 Orb_<n>_Des
        // ------------------------------------------------------------------

        private static void TryInjectLocalization()
        {
            if (_localizationDone || _colors.Count == 0) { return; }
            try
            {
                var pump = new LocalizationPump(_log);
                for (int i = 0; i < _colors.Count; i++)
                {
                    var c = _colors[i];
                    pump.Add("zh-Hans", "Encyclopedia", "Orb_" + (int)c.Value, c.NameZh);
                    pump.Add("zh-Hans", "Encyclopedia", "Orb_" + (int)c.Value + "_Des",
                        string.IsNullOrEmpty(c.DescZh)
                            ? ("平台自定义颜色：" + c.NameZh + "（原始枚举值 " + (int)c.Value + "）")
                            : c.DescZh);
                }
                pump.Flush();
                _localizationDone = true;
                if (_log != null) { _log.Info("自定义颜色：已写入 " + _colors.Count * 2 + " 条 Orb 文案。"); }
            }
            catch (Exception ex)
            {
                WarnOnce(-7, "自定义颜色：写入文案失败（下一场战斗重试）—— " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        //  自检：投递 1 颗自定义色法力（只在诊断开关打开时、且只投一次）
        // ------------------------------------------------------------------

        private static void MaybeSpawnSmokeMana()
        {
            if (!DiagnosticSwitches.CustomColorSmokeTest || _smokeSpawned || _colors.Count == 0) { return; }
            try
            {
                var c = _colors[0];
                new SubCommandAddManaIfHasSpace(c.Value);
                _smokeSpawned = true;
                if (_log != null)
                {
                    _log.Info("自定义颜色自检：已投递 1 颗「" + c.NameZh + "」法力（ColorE=" + (int)c.Value +
                        "）。看：法力球图标是否黄色、是否报 Color 6 not found / SoundRef not found。");
                }
            }
            catch (Exception ex)
            {
                WarnOnce(-8, "自定义颜色自检：投递法力失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        //  工具
        // ------------------------------------------------------------------

        /// <summary>与游戏 `CustomColorData.GetBrighterManaColor` 同策略：抬高 V。</summary>
        private static Color Brighten(Color color)
        {
            float h, s, v;
            Color.RGBToHSV(color, out h, out s, out v);
            v = Mathf.Min(v + 0.15f, 1f);
            return Color.HSVToRGB(h, s, v);
        }

        /// <summary>造一个圆形色块贴图（零素材依赖，用于自检与"作者没给贴图"的兜底）。</summary>
        /// <summary>
        /// 按 mod 的 colors.json → cardRecolor 把一张卡的身份色/费用改成自定义色。
        /// 为什么不走 JSON 的颜色名字：颜色名由 Core 的 ManaColorNames 解析，而 Core 目前
        /// 没有自定义色成员（重建 Core 会踩 B-01），所以改成"先写占位色，装配期重着色"。
        /// </summary>
        internal static CardInfo ApplyCardRecolor(string cardId, CardInfo info, IGameLog log)
        {
            try
            {
                var rec = CustomColorDecl.FindRecolor(cardId);
                if (rec == null) { return info; }

                if (rec.Colors != null && rec.Colors.Length > 0)
                {
                    var arr = new ColorE[rec.Colors.Length];
                    for (int i = 0; i < arr.Length; i++) { arr[i] = (ColorE)rec.Colors[i]; }
                    info.cardColorEs = arr;
                }

                if (rec.Cost != null && rec.Cost.Length > 0)
                {
                    var slots = new ColorEArray[rec.Cost.Length];
                    for (int i = 0; i < slots.Length; i++)
                    {
                        var src = rec.Cost[i];
                        var inner = new ColorE[src.Length];
                        for (int j = 0; j < inner.Length; j++) { inner[j] = (ColorE)src[j]; }
                        slots[i] = new ColorEArray(inner);
                    }
                    info.cardManaCostColorEArrays = slots;
                }

                if (log != null)
                {
                    log.Info("自定义颜色：卡 " + cardId + " 已按 cardRecolor 重着色（身份色 " +
                        (rec.Colors == null ? "-" : JoinInts(rec.Colors)) +
                        "，费用槽 " + (rec.Cost == null ? "沿用" : CostText(rec.Cost)) + "）。");
                }
            }
            catch (Exception ex)
            {
                if (log != null) { log.Warn("自定义颜色：卡 " + cardId + " 重着色失败 —— " + ex.GetType().Name + ": " + ex.Message); }
            }
            return info;
        }

        private static string JoinInts(int[] v)
        {
            var parts = new string[v.Length];
            for (int i = 0; i < v.Length; i++) { parts[i] = v[i].ToString(); }
            return string.Join("/", parts);
        }

        private static string CostText(int[][] cost)
        {
            var parts = new string[cost.Length];
            for (int i = 0; i < cost.Length; i++) { parts[i] = "[" + JoinInts(cost[i]) + "]"; }
            return string.Join("", parts);
        }

        /// <summary>给声明读取器用：只声明颜色、没给图标时现画一个圆。</summary>
        internal static Sprite MakeCircleSpriteFor(Color color) { return MakeCircleSprite(color); }

        private static Sprite MakeCircleSprite(Color color)
        {
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
                    else if (d > 0.82f) { c = new Color(color.r * 0.72f, color.g * 0.72f, color.b * 0.72f, 1f); }
                    else { c = color; }
                    px[y * size + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static void WarnOnce(int key, string message)
        {
            if (!_warned.Add(key)) { return; }
            if (_log != null) { _log.Warn(message); }
            else { Debug.LogWarning("[ModKit] " + message); }
        }

        /// <summary>给同文件的补丁类写"只提示一次"的日志用。</summary>
        internal static void WarnOnceExposed(string message) { WarnOnce(-11, message); }

        private static readonly HashSet<string> _loggedOnce = new HashSet<string>();

        /// <summary>只打一次的 INFO 日志（诊断用）。</summary>
        internal static void LogOnce(string message)
        {
            if (!_loggedOnce.Add(message)) { return; }
            if (_log != null) { _log.Info(message); }
            else { Debug.Log("[ModKit] " + message); }
        }
    }

    /// <summary>
    /// 卡面「流派色」的自定义色槽（节点⑩ 的正式解法，替代"借 Black 材质"）。
    ///
    /// 原版结构（CardManaAttribute）：
    ///   · 6 个 Image 字段（red/green/blue/purple/void/black）+ 1 个 materialTemplate；
    ///   · `InitializeAllMaterials()` 每次调用都为 6 个槽 new 一个 Material；
    ///   · `SetCardManaAttribute` 用 `colorE switch { …Black, _ => throw }` 选 Image，
    ///     再 `SetOneMaterial(image, color, isOn, isRare)` 设 `_isOn/_Color/_isRare`。
    ///   ⇒ 6 以外的取值没有槽位，直接抛 `ArgumentOutOfRangeException`
    ///     （实机：抽卡界面「从3张卡中选」弹错误框）。
    ///
    /// 本补丁的做法：**不抢 Black 的槽，给自定义色现造一个槽**——
    ///   1. prefix：把自定义色从入参数组里摘出来，只把原版 6 色交给原方法；
    ///   2. postfix：为每个自定义色克隆 Black 槽的 Image（同父节点/尺寸/精灵/锚点，
    ///      位置往右排开），new 一份独立 Material，按原版同一套属性渲染
    ///      （`_isOn=1`、`_Color=自定义色`、`_isRare` 与本次稀有度一致）；
    ///   3. 槽位按 `CardManaAttribute` 实例缓存（ConditionalWeakTable，卡销毁即回收），
    ///      同一张卡反复刷新不会每次 new 材质。
    /// 这样多个自定义色、以及"自定义色 + 真 Black"同卡也不会互相覆盖。
    /// </summary>
    [HarmonyPatch(typeof(Card.CardManaAttribute), "SetCardManaAttribute")]
    internal static class CardManaSlotPatch
    {
        private static readonly int IsOn = Shader.PropertyToID("_isOn");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");
        private static readonly int IsRare = Shader.PropertyToID("_isRare");

        private static readonly List<ColorE> Pending = new List<ColorE>();
        private static int PendingRarity;
        private static bool _logged;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Card.CardManaAttribute, Dictionary<int, Image>> Slots =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Card.CardManaAttribute, Dictionary<int, Image>>();

        private static void Prefix(Card.CardManaAttribute __instance, ref ColorE[] colorEs, int cardRarity)
        {
            Pending.Clear();
            PendingRarity = cardRarity;
            try
            {
                if (colorEs == null || colorEs.Length == 0) { return; }

                var kept = new List<ColorE>(colorEs.Length);
                foreach (var c in colorEs)
                {
                    CustomColorCompat.CustomColor cc;
                    if (CustomColorCompat.TryGet(c, out cc)) { Pending.Add(c); }
                    else { kept.Add(c); }
                }
                if (Pending.Count > 0) { colorEs = kept.ToArray(); }   // 原方法只处理它认识的 6 色
            }
            catch (Exception ex)
            {
                CustomColorCompat.WarnOnceExposed("自定义颜色：流派色预处理失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void Postfix(Card.CardManaAttribute __instance)
        {
            try
            {
                Dictionary<int, Image> dict;
                if (!Slots.TryGetValue(__instance, out dict))
                {
                    dict = new Dictionary<int, Image>();
                    Slots.Add(__instance, dict);
                }

                // 每次刷新先全部关掉（原版 ResetAllMaterials 只复位它自己的 6 个）
                foreach (var kv in dict)
                {
                    if (kv.Value != null) { kv.Value.gameObject.SetActive(false); }
                }
                if (Pending.Count == 0) { return; }

                bool isRare = PendingRarity == 2;
                int order = 0;
                foreach (var value in Pending)
                {
                    var img = EnsureSlot(__instance, dict, value, order);
                    if (img == null) { continue; }
                    order++;

                    var mat = img.material;
                    if (mat != null)
                    {
                        mat.SetInt(IsOn, 1);
                        mat.SetColor(ColorProp, ResolveColor(value));
                        mat.SetInt(IsRare, isRare ? 1 : 0);
                    }
                    img.gameObject.SetActive(true);
                    try { ApplyPipSize(img); } catch { }
                }

                if (!_logged)
                {
                    _logged = true;
                    CustomColorCompat.WarnOnceExposed("自定义颜色：已为卡面流派色启用独立材质槽（每卡每色一个，克隆 Black 槽布局；不占用原版 Black 槽）。");
                }
            }
            catch (Exception ex)
            {
                CustomColorCompat.WarnOnceExposed("自定义颜色：流派色槽渲染失败 —— " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// 取/建该卡实例上某个自定义色的槽。
        /// ★ 模板用 <c>purpleMana</c>（游戏自己的**彩色**流派色小点），不是 blackMana —
        ///   黑卡槽用的是另一套更大更圆的素材（实机 2026-10-20：用它当模板会得到一个巨大的黄圆盘）。
        /// ★ 不再调 <c>SetNativeSize()</c>：那会按贴图原始像素铺满，盖住卡框；
        ///   这里直接沿用模板的 sizeDelta × `[Diagnostics] CustomColorPipScale`。
        /// </summary>
        private static Image EnsureSlot(Card.CardManaAttribute host, Dictionary<int, Image> dict, ColorE value, int order)
        {
            Image existing;
            if (dict.TryGetValue((int)value, out existing) && existing != null) { return existing; }

            var template = ReadImage(host, "purpleMana");
            if (template == null) { template = ReadImage(host, "blackMana"); }
            if (template == null) { return null; }

            var src = template.rectTransform;
            var go = new GameObject("ModKit_CustomMana_" + (int)value, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(src.parent, false);

            var img = go.GetComponent<Image>();
            // 卡面小点贴图优先级：该颜色自己声明的 pip（colors.json）> 全局 [Diagnostics] > 游戏小点
            img.sprite = GetPipSprite(value) ?? template.sprite;
            img.type = template.type;
            img.preserveAspect = template.preserveAspect;
            img.raycastTarget = false;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = src.anchorMin;
            rt.anchorMax = src.anchorMax;
            rt.pivot = src.pivot;
            rt.localScale = src.localScale;

            // 尺寸：模板尺寸 × 配置缩放（不用 SetNativeSize，避免按像素原尺寸铺开）
            float baseW = Mathf.Abs(src.sizeDelta.x) > 1f ? Mathf.Abs(src.sizeDelta.x) : 20f;
            float baseH = Mathf.Abs(src.sizeDelta.y) > 1f ? Mathf.Abs(src.sizeDelta.y) : baseW;
            float scale = DiagnosticSwitches.CustomColorPipScale;
            rt.sizeDelta = new Vector2(baseW * scale, baseH * scale);

            float step = baseW * scale + 4f;
            rt.anchoredPosition = src.anchoredPosition + new Vector2(step * (order + 1), 0f);

            // 独立材质：每个槽一份（与原版 InitializeAllMaterials 同策略）
            try { img.material = new Material(template.material); } catch { }

            dict[(int)value] = img;
            return img;
        }

        /// <summary>按当前 sizeDelta 再缩一遍（配置在运行中被改时，下次刷新生效）。</summary>
        private static void ApplyPipSize(Image img)
        {
            try
            {
                var rt = img.rectTransform;
                float scale = DiagnosticSwitches.CustomColorPipScale;
                // 以"当前尺寸 / 上次用的缩放"反推基准，避免反复叠加
                float last = _lastScale <= 0f ? 1f : _lastScale;
                if (Mathf.Approximately(scale, last)) { return; }
                rt.sizeDelta = new Vector2(rt.sizeDelta.x / last * scale, rt.sizeDelta.y / last * scale);
                _lastScale = scale;
            }
            catch { }
        }

        private static float _lastScale = 1f;
        private static Sprite _pipSprite;
        private static string _pipSpritePath;

        /// <summary>配置了 <c>[Diagnostics] CustomColorPipSprite</c> 就用那张图，否则 null（沿用游戏贴图）。</summary>
        private static Sprite GetPipSprite(ColorE value)
        {
            try
            {
                CustomColorCompat.CustomColor own;
                if (CustomColorCompat.TryGet(value, out own) && own != null && own.PipSprite != null)
                {
                    return own.PipSprite;        // mod 在 colors.json 里声明的小点图
                }
            }
            catch { }

            try
            {
                var path = DiagnosticSwitches.CustomColorPipSprite;
                if (string.IsNullOrEmpty(path)) { return null; }
                if (_pipSprite != null && _pipSpritePath == path) { return _pipSprite; }

                var abs = path;
                if (!System.IO.Path.IsPathRooted(abs))
                {
                    var plugins = System.IO.Path.GetDirectoryName(typeof(CustomColorCompat).Assembly.Location);
                    var bep = System.IO.Path.GetDirectoryName(plugins ?? "");
                    var game = System.IO.Path.GetDirectoryName(bep ?? "");
                    abs = System.IO.Path.Combine(game ?? "", path);
                }

                string err;
                _pipSprite = ModArt.Load(abs, out err);
                _pipSpritePath = path;
                if (_pipSprite == null)
                {
                    CustomColorCompat.WarnOnceExposed("自定义颜色：流派色小图标加载失败（" + path + " → " + abs + "）—— " + err);
                }
                return _pipSprite;
            }
            catch { return null; }
        }

        private static Image ReadImage(object host, string fieldName)
        {
            try
            {
                var f = host.GetType().GetField(fieldName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                return (f == null) ? null : (f.GetValue(host) as Image);
            }
            catch { return null; }
        }

        /// <summary>诊断用：确认 WorldManager 在取自定义色时返回的是注册表里的暗金，而不是兜底白。</summary>
        internal static void LogColorOnce(ColorE value, Color c)
        {
            if ((int)value != 6) { return; }
            CustomColorCompat.LogOnce("自定义颜色：GetColorFromEnumColor(6) 返回 rgb(" +
                Mathf.RoundToInt(c.r * 255) + "," + Mathf.RoundToInt(c.g * 255) + "," + Mathf.RoundToInt(c.b * 255) + ")。");
        }

        private static Color ResolveColor(ColorE value)
        {
            try
            {
                var wm = World.WorldManager.Instance;
                if (wm != null) { return wm.GetColorFromEnumColor(value); }   // ② 已接管，会走注册表
            }
            catch { }
            return new Color(0.639f, 0.573f, 0.071f, 1f);
        }
    }
}
