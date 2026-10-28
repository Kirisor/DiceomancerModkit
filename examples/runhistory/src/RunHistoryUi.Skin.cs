// =============================================================================
//  Run History（战绩）· 投影（柔影精灵）
//
//  为什么不用「叠几层半透明矩形」：那样每一层都是一条硬边，叠在纸面/木纹上会
//  留下两三道台阶，看着像脏边而不是影子。这里按距离场生成**边缘渐隐**的圆角矩形
//  贴图，九宫格拉伸 —— 大面板和小钥匙牌共用同一套，只是渐隐带宽不同。
//
//  ★ 分两档用（两段式投影，真实光照就是这样）：
//      · 贴身影 fringe≈20：紧、实，负责"贴着纸边的那道暗"
//      · 环境影 fringe≈46：宽、淡，负责"东西浮起来"的大范围渐变
//    只用宽影会显得虚，只用窄影会显得脏。
//  ★ 本 mod 是独立插件（不引用 ModKit.Game），所以这套生成代码在这里自备一份，
//    算法与平台图鉴的 UiSkin 同源：圆角矩形 SDF → 边缘 smoothstep 渐隐。
//  ★ 精灵内部（轮廓以内）是**不透明**的：被面板/卡片盖住的部分看不见，
//    只有轮廓外那一圈渐隐带露出来，这才是影子。
// =============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RunHistory
{
    internal static partial class RunHistoryUi
    {
        /// <summary>影子的颜色：暖黑（纯黑会显脏，暖黑配纸面更沉）。</summary>
        private static readonly Color ShadowTint = new Color(0.035f, 0.028f, 0.022f, 1f);

        private const int ShadowTexSize = 256;      // 贴图边长
        private const float ShadowCorner = 2f;      // 轮廓圆角：战页面板是直角，影子也跟直角
        private const int ShadowBorder = 104;       // 九宫格边框（> 圆角 + 最大渐隐带）

        private static readonly Dictionary<float, Sprite> _shadowSprites = new Dictionary<float, Sprite>();

        /// <summary>按渐隐带宽取柔影精灵（同一档只生成一次）。</summary>
        private static Sprite ShadowSprite(float fringe)
        {
            Sprite cached;
            if (_shadowSprites.TryGetValue(fringe, out cached) && cached != null) { return cached; }

            const int n = ShadowTexSize;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var px = new Color32[n * n];
            float half = n * 0.5f;
            float hw = half - fringe;      // 实心轮廓的半宽：渐隐带从这里往外铺

            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float fx = (x + 0.5f) - half;
                    float fy = (y + 0.5f) - half;
                    float d = SdfRoundRect(fx, fy, hw, hw, ShadowCorner);   // > 0 在轮廓外
                    float t = Mathf.Clamp01(d / fringe);
                    float a = 1f - t * t * (3f - 2f * t);                   // smoothstep
                    px[y * n + x] = new Color32(255, 255, 255,
                        (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);

            var sprite = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect,
                new Vector4(ShadowBorder, ShadowBorder, ShadowBorder, ShadowBorder));
            sprite.name = "RunHistorySoftShadow" + (int)fringe;
            _shadowSprites[fringe] = sprite;
            return sprite;
        }

        /// <summary>圆角矩形的有符号距离：轮廓内为负、外为正。</summary>
        private static float SdfRoundRect(float x, float y, float hw, float hh, float r)
        {
            float qx = Mathf.Abs(x) - hw + r;
            float qy = Mathf.Abs(y) - hh + r;
            float ax = Mathf.Max(qx, 0f);
            float ay = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        /// <summary>
        /// 给 parent 垫一层柔影：四边各探出 <paramref name="spread"/>、整体下移 <paramref name="drop"/>。
        /// 渐隐带 = <paramref name="fringe"/>，是 spread 的一部分（轮廓正好贴在 parent 边上）。
        /// </summary>
        private static Image AddSoftShadow(Transform parent, float spread, float drop, float alpha, float fringe)
        {
            if (spread < fringe) { spread = fringe; }   // 否则渐隐带会被矩形硬切，露出边

            var tint = ShadowTint;
            tint.a = alpha;
            var img = NewImage(parent, tint, Vector2.zero, Vector2.one,
                new Vector2(0f, -drop), new Vector2(spread * 2f, spread * 2f), "SoftShadow");
            img.sprite = ShadowSprite(fringe);
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            img.transform.SetAsFirstSibling();   // 永远垫在最底层
            return img;
        }

        /// <summary>面板/浮动小卡的两段式投影：贴身实影 + 环境柔影。</summary>
        private static void AddLiftedShadow(Transform parent, float ambient, float ambientDrop, float contact, float contactDrop)
        {
            AddSoftShadow(parent, ambient, ambientDrop, 0.50f, 46f);
            AddSoftShadow(parent, contact, contactDrop, 0.45f, 20f);
        }
    }
}
