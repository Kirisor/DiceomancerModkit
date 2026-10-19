// =============================================================================
//  ModKit.Game · 自建 UI 的皮肤资源（全部程序化生成，不引用任何外部/游戏素材）
//
//  三张精灵：
//    Paper()      256×256 可平铺的纸纹（米色底 + 细噪点 + 纤维），配 Image.Type.Tiled
//    PanelFrame() 96×96 九宫格边框（圆角 + 深棕外描边 + 内金线 + 内阴影），配 Image.Type.Sliced
//    RowFrame()   48×48 九宫格细边框（条目/按钮用）
//
//  全部走 sRGB 颜色 + 2×2 超采样边缘，避免锯齿。
// =============================================================================

using UnityEngine;

namespace ModKit.Game
{
    internal static class UiSkin
    {
        private static Sprite _paper;
        private static Sprite _panelFrame;
        private static Sprite _rowFrame;
        private static Texture2D _paperTex;

        /// <summary>纸纹底图（可平铺）。</summary>
        internal static Sprite Paper
        {
            get
            {
                if (_paper != null) { return _paper; }
                const int N = 256;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.filterMode = FilterMode.Bilinear;

                uint seed = 0x9E3779B9u;
                System.Func<float> rnd = () =>
                {
                    seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
                    return (seed & 0xFFFFFF) / 16777215f;
                };

                var px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                {
                    for (int x = 0; x < N; x++)
                    {
                        // 底：暖米色
                        float r = 0.965f, g = 0.945f, b = 0.895f;
                        // 细噪点（模拟纸纤维颗粒）
                        float n = (rnd() - 0.5f) * 0.035f;
                        // 长纤维：按行做低频起伏
                        float fiber = Mathf.Sin(y * 0.35f + Mathf.Sin(x * 0.07f) * 2.2f) * 0.008f;
                        // 大块淡斑
                        float blotch = (Mathf.Sin(x * 0.045f) * Mathf.Sin(y * 0.038f)) * 0.012f;
                        float k = n + fiber + blotch;
                        px[y * N + x] = new Color32(
                            (byte)Mathf.Clamp((r + k) * 255f, 0f, 255f),
                            (byte)Mathf.Clamp((g + k * 0.95f) * 255f, 0f, 255f),
                            (byte)Mathf.Clamp((b + k * 0.9f) * 255f, 0f, 255f),
                            255);
                    }
                }
                tex.SetPixels32(px);
                tex.Apply(false, false);
                _paperTex = tex;
                // 用整张图、无 border；Tiled 模式下需要 wrapMode = Repeat
                _paper = Sprite.Create(tex, new Rect(0f, 0f, N, N), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, Vector4.zero);
                _paper.name = "ModKitPaper";
                return _paper;
            }
        }

        /// <summary>面板九宫格边框（圆角 + 外描边 + 内金线 + 内阴影）。</summary>
        internal static Sprite PanelFrame
        {
            get
            {
                if (_panelFrame != null) { return _panelFrame; }
                _panelFrame = BuildFrame(96, 22f, 28, 3.2f, 2.0f);
                _panelFrame.name = "ModKitPanelFrame";
                return _panelFrame;
            }
        }

        /// <summary>条目/按钮用的细边框。</summary>
        internal static Sprite RowFrame
        {
            get
            {
                if (_rowFrame != null) { return _rowFrame; }
                _rowFrame = BuildFrame(48, 12f, 14, 2.2f, 0f);
                _rowFrame.name = "ModKitRowFrame";
                return _rowFrame;
            }
        }

        /// <summary>
        /// 生成一张"中间透明、四周描边"的九宫格边框。
        /// </summary>
        private static Sprite BuildFrame(int size, float radius, int border, float strokeW, float goldW)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var px = new Color32[size * size];
            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < px.Length; i++) { px[i] = clear; }

            float hw = size * 0.5f;
            float hh = size * 0.5f;
            var stroke = new Color(0.155f, 0.125f, 0.105f, 1f);      // 深棕描边
            var gold = new Color(0.788f, 0.635f, 0.153f, 0.95f);      // 内金线
            var shadow = new Color(0.10f, 0.08f, 0.06f, 0.16f);       // 内阴影

            // 2×2 超采样抗锯齿
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
                            acc += SampleFrame(fx, fy, hw, hh, radius, strokeW, goldW, stroke, gold, shadow);
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

        /// <summary>圆角矩形 SDF（<0 在内部）。</summary>
        private static float SdfRoundRect(float x, float y, float hw, float hh, float r)
        {
            float qx = Mathf.Abs(x) - hw + r;
            float qy = Mathf.Abs(y) - hh + r;
            float ax = Mathf.Max(qx, 0f);
            float ay = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        private static Color SampleFrame(float x, float y, float hw, float hh, float radius,
            float strokeW, float goldW, Color stroke, Color gold, Color shadow)
        {
            float d = SdfRoundRect(x, y, hw, hh, radius);
            float ad = Mathf.Abs(d);

            // 外描边
            if (ad <= strokeW) { return stroke; }

            // 内金线（仅面板用）
            if (goldW > 0f)
            {
                float goldT = 1f - Mathf.Abs(ad - (strokeW + goldW * 0.9f)) / 1.0f;
                if (goldT > 0f)
                {
                    var c = gold;
                    c.a *= Mathf.Clamp01(goldT);
                    return c;
                }
            }

            // 内阴影：从描边内侧往里 8px 逐渐淡出（只在内部一侧）
            if (d < 0f && d > -9f)
            {
                float t = 1f - (-d) / 9f;
                var c = shadow;
                c.a *= Mathf.Clamp01(t * t);
                return c;
            }

            return Color.clear;
        }
    }
}
