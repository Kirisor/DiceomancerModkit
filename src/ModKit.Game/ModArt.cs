// =============================================================================
//  ModKit.Game · mod 自带卡面图 → Sprite
//
//  解决什么问题：平台的 Q2 决策是"只借原版资产"——卡面来自 artTemplate 那张原版卡。
//  于是作者画的卡面没地方放，只能挑一张"长得像"的原版卡，玩家在卡池与图鉴里
//  看到的是别人的画。这里让 mod 把**自己目录里的图片**带进来。
//
//  ★ 为什么不走 Resources / AssetBundle：Resources.LoadAll 的内容烘焙在
//    resources.assets 里，运行期无法追加资源（第 1 步不变量 I3）。
//    运行期唯一能从零造出贴图的路径就是 new Texture2D + LoadImage。
//
//  ★ 为什么只要一张 Texture 就够：游戏卡面不是"SpriteRenderer 画一张 Sprite"，
//    而是把 cardArt.texture 塞进卡面材质的 _CardArtTex 槽按 UV 采样
//    （反编译 Card.CardBase 第 284 行）。所以：
//      · Sprite 的 pivot / pixelsPerUnit 对卡面**没有影响**；
//      · 但图的宽高比会被直接拉伸 ⇒ 必须提醒作者用正方形。
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ModKit.Game
{
    /// <summary>
    /// 把 mod 目录里的图片文件加载成 <see cref="Sprite"/>（卡面用）。
    /// </summary>
    /// <remarks>
    /// 静态缓存：同一张图被 N 张卡引用时只解码一次（贴图是显存对象，不能按卡复制）。
    /// </remarks>
    public static class ModArt
    {
        /// <summary>单边像素上限，超过即拒绝 —— 防"一张 8000×8000 的图把显存吃光"。</summary>
        public const int MaxEdge = 4096;

        /// <summary>文件体积上限（解码之前的粗护栏）。</summary>
        public const int MaxBytes = 16 * 1024 * 1024;

        private static readonly Dictionary<string, Sprite> Cache =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 把 mod 目录与 JSON 里写的相对路径拼成绝对路径。**只拼接，不校验存在性**。
        /// </summary>
        public static string Resolve(string modRoot, string relative)
        {
            if (string.IsNullOrEmpty(modRoot) || string.IsNullOrEmpty(relative)) { return null; }

            string combined = Path.Combine(modRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            try { return Path.GetFullPath(combined); }
            catch { return combined; }
        }

        /// <summary>
        /// 加载一张图。失败返回 <c>null</c>，原因写在 <paramref name="error"/>（不抛异常）。
        /// </summary>
        /// <remarks>
        /// 不抛是刻意的：一张坏图不该把整个 mod 的装配炸掉。上层把 error 收进装配报告，
        /// 与"模板卡不存在"同一级别 —— 装配层统一处理"这张卡装配失败"。
        /// </remarks>
        public static Sprite Load(string absolutePath, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(absolutePath))
            {
                error = "图片路径为空。";
                return null;
            }

            Sprite cached;
            if (Cache.TryGetValue(absolutePath, out cached) && cached != null) { return cached; }

            if (!File.Exists(absolutePath))
            {
                error = "找不到图片文件：" + absolutePath;
                return null;
            }

            long length;
            try { length = new FileInfo(absolutePath).Length; }
            catch (Exception ex) { error = "读不到图片文件信息：" + ex.Message; return null; }

            if (length <= 0) { error = "图片是 0 字节的空文件：" + absolutePath; return null; }
            if (length > MaxBytes)
            {
                error = "图片太大（" + (length / 1024 / 1024) + " MB，上限 " +
                        (MaxBytes / 1024 / 1024) + " MB）：" + absolutePath;
                return null;
            }

            byte[] bytes;
            try { bytes = File.ReadAllBytes(absolutePath); }
            catch (Exception ex) { error = "读图片失败：" + ex.Message; return null; }

            // 先按魔数判一次格式：比 Unity 的失败信息可诊断得多
            string unsupported = DescribeUnsupportedFormat(bytes);
            if (unsupported != null) { error = unsupported + "：" + absolutePath; return null; }

            Texture2D texture = null;
            try
            {
                // 2×2 只是占位尺寸 —— LoadImage 会按图片真实尺寸重建贴图
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes))
                {
                    error = "Unity 解不开这张图（格式不支持或文件损坏）：" + absolutePath;
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                if (texture.width > MaxEdge || texture.height > MaxEdge)
                {
                    error = "图片尺寸 " + texture.width + "×" + texture.height +
                            " 超过单边上限 " + MaxEdge + " px（改小这张图，别指望平台帮你缩）：" + absolutePath;
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }

                // 卡面是材质按 UV 采样的：Repeat 会在边缘串色，Clamp 才是对的
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                texture.name = "ModKitArt_" + Path.GetFileName(absolutePath);

                // pixelsPerUnit = 100 = Unity 导入 Sprite 的默认值，与原版卡面同口径
                // （卡面本身用不到，但 GetCardArtSprite() 会给"召唤物立绘"用）
                var sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                sprite.name = texture.name;

                Cache[absolutePath] = sprite;
                return sprite;
            }
            catch (Exception ex)
            {
                if (texture != null) { UnityEngine.Object.Destroy(texture); }
                error = "加载图片抛出异常：" + ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }

        /// <summary>按文件头判断是不是 PNG / JPEG；不是就返回一句人话，是就返回 null。</summary>
        private static string DescribeUnsupportedFormat(byte[] bytes)
        {
            if (bytes.Length >= 8 &&
                bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            {
                return null;   // PNG 魔数
            }

            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            {
                return null;   // JPEG 魔数
            }

            return "只支持 PNG / JPEG（这个文件的文件头既不是 PNG 也不是 JPEG）";
        }
    }
}
