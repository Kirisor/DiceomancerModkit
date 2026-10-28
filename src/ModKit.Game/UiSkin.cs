// =============================================================================
//  ModKit.Game · 自建 UI 的皮肤资源（全部程序化生成，不引用任何外部/游戏素材）
//
//  精灵：
//    Paper       256×256 可平铺纸纹（柏林噪声 + 纸纤维），配 Image.Type.Tiled
//    PanelRound  96×96  圆角面板底（白填充 + 4px 描边），配 Sliced；也可当 Mask 用
//    RowRound    48×48  圆角条目底（白填充 + 4px 描边），配 Sliced；条目/按钮/详情用
//    PanelFrame  96×96  九宫格外框（圆角 + 4px 深棕描边 + 内金线 + 内阴影）
//    DropShadow 256×256 柔影（轮廓以内不透明，外侧 46px 渐隐），九宫格拉伸
//
//  圆角一致性：底图与外框用同一半径，四角自然形成倒角，不再露出直角。
// =============================================================================

using UnityEngine;
using UnityEngine.UI;

namespace ModKit.Game
{
    internal static class UiSkin
    {
        private static Sprite _paper;
        private static Sprite _panelFrame;
        private static Sprite _panelRound;
        private static Sprite _rowRound;

        /// <summary>面板圆角半径。</summary>
        internal const float PanelRadius = 20f;

        /// <summary>条目/按钮圆角半径。</summary>
        internal const float RowRadius = 11f;

        /// <summary>描边宽度（像素）。</summary>
        internal const float StrokeWidth = 4f;

        // ---------------- 投影 ----------------

        private static Sprite _dropShadow;
        private static Sprite _dropShadowTight;

        /// <summary>
        /// 柔影精灵：轮廓以内不透明、外侧 <paramref name="fringe"/> 像素渐隐的圆角矩形
        /// （白 RGB + alpha 曲线）。九宫格拉伸，面板/条目/卡片托板都能复用。
        /// </summary>
        /// <remarks>
        /// 为什么要贴图而不是「叠几层半透明矩形」：矩形叠出来的影子每一层都是一条硬边，
        /// 落在纸面/木纹上会看出两三道台阶，像脏边。距离场渐隐是连续的，边缘才像光。
        /// </remarks>
        internal static Sprite DropShadow { get { return BuildDropShadow(46f, ref _dropShadow, "ModKitDropShadow"); } }

        /// <summary>贴身实影（渐隐带 20px）：只负责"贴着纸边的那道暗"，配 <see cref="DropShadow"/> 用。</summary>
        internal static Sprite DropShadowTight { get { return BuildDropShadow(20f, ref _dropShadowTight, "ModKitDropShadowTight"); } }

        private static Sprite BuildDropShadow(float fringe, ref Sprite cache, string name)
        {
            if (cache != null) { return cache; }

            const int N = 256;
            const float corner = PanelRadius;   // 轮廓圆角跟面板一致（20px），角上才不露尖
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var px = new Color32[N * N];
            float half = N * 0.5f;
            float hw = half - fringe;      // 实心轮廓的半宽

            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float d = SdfRoundRect((x + 0.5f) - half, (y + 0.5f) - half, hw, hw, corner);
                    float t = Mathf.Clamp01(d / fringe);
                    float a = 1f - t * t * (3f - 2f * t);          // smoothstep
                    px[y * N + x] = new Color32(255, 255, 255,
                        (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);
            cache = Sprite.Create(tex, new Rect(0f, 0f, N, N), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(104f, 104f, 104f, 104f));
            cache.name = name;
            return cache;
        }

        /// <summary>
        /// 给 parent 垫一层柔影：四边各探出 <paramref name="spread"/>（≥ 渐隐带，否则会被
        /// 矩形硬切），整体下移 <paramref name="drop"/>。
        /// <paramref name="topInset"/> 给「纸页比面板矩形矮一截」的情况用（图鉴纸页顶边低 40px），
        /// 让影子跟着纸页走，而不是在顶上多出一圈。
        /// </summary>
        internal static Image AddDropShadow(Transform parent, float spread, float drop, float alpha,
            float topInset = 0f, bool tight = false)
        {
            float fringe = tight ? 20f : 46f;
            if (spread < fringe) { spread = fringe; }

            var go = new GameObject(tight ? "DropShadowTight" : "DropShadow", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(-spread, -spread);
            rt.offsetMax = new Vector2(spread, spread - topInset);
            rt.anchoredPosition = new Vector2(0f, -drop);

            var img = go.AddComponent<Image>();
            img.sprite = tight ? DropShadowTight : DropShadow;
            img.type = Image.Type.Sliced;
            img.color = new Color(0.035f, 0.028f, 0.022f, alpha);   // 暖黑
            img.raycastTarget = false;
            go.transform.SetAsFirstSibling();
            return img;
        }

        /// <summary>两段式投影：环境柔影（宽淡）+ 贴身实影（窄实）。</summary>
        internal static void AddLiftedShadow(Transform parent, float ambient, float ambientDrop,
            float contact, float contactDrop, float topInset = 0f)
        {
            AddDropShadow(parent, ambient, ambientDrop, 0.50f, topInset, false);
            AddDropShadow(parent, contact, contactDrop, 0.45f, topInset, true);
        }

        // ---------------- 纸纹 ----------------

        /// <summary>纸纹底图（柏林噪声，可平铺）。</summary>
        internal static Sprite Paper
        {
            get
            {
                if (_paper != null) { return _paper; }
                const int N = 256;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.filterMode = FilterMode.Bilinear;

                var px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        // 多倍频柏林噪声（周期化 ⇒ 可无缝平铺）
                        float n = 0f;
                        n += 0.52f * (PerlinTileable(x, y, 64) - 0.5f);
                        n += 0.26f * (PerlinTileable(x + 97, y + 41, 32) - 0.5f);
                        n += 0.13f * (PerlinTileable(x + 13, y + 71, 16) - 0.5f);
                        n += 0.09f * (PerlinTileable(x + 55, y + 5, 8) - 0.5f);

                        // 纸纤维：纵向细纹
                        float fiber = Mathf.Sin(y * 0.9f + PerlinTileable(x, y, 128) * 3.5f) * 0.006f;

                        float k = n * 0.070f + fiber;

                        px[y * N + x] = new Color32(
                            (byte)Mathf.Clamp((0.968f + k) * 255f, 0f, 255f),
                            (byte)Mathf.Clamp((0.950f + k * 0.95f) * 255f, 0f, 255f),
                            (byte)Mathf.Clamp((0.902f + k * 0.88f) * 255f, 0f, 255f),
                            255);
                    }
                }
                tex.SetPixels32(px);
                tex.Apply(false, false);

                _paper = Sprite.Create(tex, new Rect(0f, 0f, N, N), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, Vector4.zero);
                _paper.name = "ModKitPaper";
                return _paper;
            }
        }

        // ---------------- 圆角填充 ----------------

        /// <summary>面板圆角底（白填充 + 4px 描边），适合当底图或 Mask。</summary>
        internal static Sprite PanelRound
        {
            get
            {
                if (_panelRound != null) { return _panelRound; }
                _panelRound = BuildRounded(96, PanelRadius, 28, StrokeWidth, false);
                _panelRound.name = "ModKitPanelRound";
                return _panelRound;
            }
        }

        /// <summary>条目/按钮圆角底（白填充 + 4px 描边）。</summary>
        internal static Sprite RowRound
        {
            get
            {
                if (_rowRound != null) { return _rowRound; }
                _rowRound = BuildRounded(48, RowRadius, 14, StrokeWidth, false);
                _rowRound.name = "ModKitRowRound";
                return _rowRound;
            }
        }

        // ---------------- 外框 ----------------

        /// <summary>面板九宫格外框（圆角 + 4px 深棕描边 + 内金线 + 内阴影）。</summary>
        internal static Sprite PanelFrame
        {
            get
            {
                if (_panelFrame != null) { return _panelFrame; }
                _panelFrame = BuildRounded(96, PanelRadius, 28, StrokeWidth, true);
                _panelFrame.name = "ModKitPanelFrame";
                return _panelFrame;
            }
        }


        /// <summary>
        /// 在 parent 上盖一层"圆角纸纹"。Mask 只包住这一层，不会裁到外框/按钮等兄弟节点。
        /// </summary>
        internal static void AddPaperLayer(Transform parent)
        {
            var holder = new GameObject("PaperLayer", typeof(RectTransform));
            holder.transform.SetParent(parent, false);
            var hrt = holder.GetComponent<RectTransform>();
            hrt.anchorMin = Vector2.zero;
            hrt.anchorMax = Vector2.one;
            hrt.offsetMin = Vector2.zero;
            hrt.offsetMax = Vector2.zero;

            var img = holder.AddComponent<Image>();
            img.sprite = PanelRound;
            img.type = Image.Type.Sliced;
            img.color = Color.white;
            img.raycastTarget = false;

            var mask = holder.AddComponent<Mask>();
            mask.showMaskGraphic = false;      // 只要遮罩形状，不显示这层白底

            var tex = new GameObject("PaperTex", typeof(RectTransform));
            tex.transform.SetParent(holder.transform, false);
            var trt = tex.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            var ti = tex.AddComponent<Image>();
            ti.sprite = Paper;
            ti.type = Image.Type.Tiled;
            ti.color = new Color(1f, 1f, 1f, 0.9f);
            ti.raycastTarget = false;
        }

        // ---------------- 生成 ----------------

        /// <summary>周期化柏林噪声：把坐标折回 8×8 梯度网格，保证 N 平铺处首尾相接。</summary>
        private static float PerlinTileable(int x, int y, int cellSize)
        {
            float fx = (float)x / cellSize * 8f;
            float fy = (float)y / cellSize * 8f;
            int x0 = Mathf.FloorToInt(fx);
            int y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0;
            float ty = fy - y0;

            int px0 = Wrap8(x0), px1 = Wrap8(x0 + 1);
            int py0 = Wrap8(y0), py1 = Wrap8(y0 + 1);

            float g00 = Grad(px0, py0, tx, ty);
            float g10 = Grad(px1, py0, tx - 1f, ty);
            float g01 = Grad(px0, py1, tx, ty - 1f);
            float g11 = Grad(px1, py1, tx - 1f, ty - 1f);

            float u = Fade(tx), v = Fade(ty);
            float a = Mathf.Lerp(g00, g10, u);
            float b = Mathf.Lerp(g01, g11, u);
            return Mathf.Clamp01(Mathf.Lerp(a, b, v) * 0.7f + 0.5f);
        }

        private static int Wrap8(int v)
        {
            int r = v % 8;
            return r < 0 ? r + 8 : r;
        }

        private static float Fade(float t) { return t * t * t * (t * (t * 6f - 15f) + 10f); }

        private static float Grad(int ix, int iy, float dx, float dy)
        {
            int h = ((ix * 73856093) ^ (iy * 19349663)) & 7;
            switch (h)
            {
                case 0: return dx;
                case 1: return dx + dy;
                case 2: return dy;
                case 3: return -dx + dy;
                case 4: return -dx;
                case 5: return -dx - dy;
                case 6: return -dy;
                default: return dx - dy;
            }
        }

        /// <summary>
        /// 生成圆角精灵。<paramref name="ringOnly"/> = true 时中间透明（外框），
        /// false 时内部填充白色（底图/按钮底，可用 Image.color 染色）。
        /// </summary>
        private static Sprite BuildRounded(int size, float radius, int border, float strokeW, bool ringOnly)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var px = new Color32[size * size];
            float hw = size * 0.5f, hh = size * 0.5f;
            var stroke = new Color(0.155f, 0.125f, 0.105f, 1f);
            var gold = new Color(0.788f, 0.635f, 0.153f, 0.72f);
            var shadow = new Color(0.10f, 0.08f, 0.06f, 0.12f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color acc = Color.clear;
                    for (int sy = 0; sy < 2; sy++)
                    {
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float fx = x + 0.25f + sx * 0.5f - hw;
                            float fy = y + 0.25f + sy * 0.5f - hh;
                            acc += Sample(fx, fy, hw, hh, radius, strokeW, ringOnly, stroke, gold, shadow);
                        }
                    }
                    acc *= 0.25f;
                    px[y * size + x] = new Color32(
                        (byte)Mathf.Clamp(acc.r * 255f, 0f, 255f),
                        (byte)Mathf.Clamp(acc.g * 255f, 0f, 255f),
                        (byte)Mathf.Clamp(acc.b * 255f, 0f, 255f),
                        (byte)Mathf.Clamp(acc.a * 255f, 0f, 255f));
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);
            var b = new Vector4(border, border, border, border);
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, b);
        }

        private static float SdfRoundRect(float x, float y, float hw, float hh, float r)
        {
            float qx = Mathf.Abs(x) - hw + r;
            float qy = Mathf.Abs(y) - hh + r;
            float ax = Mathf.Max(qx, 0f);
            float ay = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        private static Color Sample(float x, float y, float hw, float hh, float radius, float strokeW,
            bool ringOnly, Color stroke, Color gold, Color shadow)
        {
            float d = SdfRoundRect(x, y, hw, hh, radius);
            float ad = Mathf.Abs(d);

            // 4px 描边
            if (ad <= strokeW * 0.5f) { return stroke; }
            if (d > 0f) { return Color.clear; }

            if (!ringOnly)
            {
                // 底图：内部填白（外面由 Image.color 染成需要的底色）
                return Color.white;
            }

            // 外框：内金线
            float goldT = 1f - Mathf.Abs(ad - (strokeW * 0.5f + 1.8f)) / 1.1f;
            if (goldT > 0f)
            {
                var c = gold;
                c.a *= Mathf.Clamp01(goldT);
                return c;
            }
            // 外框：内阴影
            if (d > -10f)
            {
                float t = 1f - (-d) / 10f;
                var c2 = shadow;
                c2.a *= Mathf.Clamp01(t * t);
                return c2;
            }
            return Color.clear;
        }
    }
}
