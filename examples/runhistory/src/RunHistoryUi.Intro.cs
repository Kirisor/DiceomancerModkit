// =============================================================================
//  Run History（战绩）· 打开时的「打光」：环境压暗 + 面板落位
//
//  为什么要这一层：桌面是一块亮木纹板，纸色面板直接贴上去会和环境糊在一起，
//  看不出面板是浮在上面的。打开时改成：
//    ① 全屏压暗（暖黑 0.55）—— 环境退到后面，面板自己亮出来
//    ② 面板从上方 16px 落位 + 透明度渐显（0.20s，ease-out）
//    ③ 投影（见 RunHistoryUi.Skin）跟着面板一起落位
//
//  ★ 压暗层铺得比画布大得多：画布只是「一屏」的 19.2×10.8 世界单位（0.01 缩放、
//    相机前 60），桌面木纹在画布之外还有，只铺画布大小会在屏幕边上留一圈亮边。
//  ★ 压暗层**不接收射线**（raycastTarget = false）：输入隔断由 BlockOtherUI 的
//    Blocker 负责；这样右上角的入口牌子虽然被压暗，仍然可以再点一下收起面板。
//  ★ 动画只有 0.2 秒，但万一宿主 Update 卡住会让面板停在半透明，
//    所以另有兜底：宿主 0.5 秒那拍会发现超时并直接置为终值（FinishIntro）。
// =============================================================================

using UnityEngine;
using UnityEngine.UI;

namespace RunHistory
{
    internal static partial class RunHistoryUi
    {
        /// <summary>压暗的终值：越大环境越暗（0.55 ≈ 只剩四成半亮度）。</summary>
        private const float ScrimAlpha = 0.55f;

        /// <summary>压暗层颜色：暖黑而不是纯黑，跟纸面/木纹是同一色系。</summary>
        private static readonly Color ScrimTint = new Color(0.055f, 0.048f, 0.040f, 1f);

        /// <summary>落位动画时长（秒）。</summary>
        private const float IntroSeconds = 0.20f;

        /// <summary>落位位移：从上方这么多像素处落到原位。</summary>
        private const float IntroRise = 16f;

        private static GameObject _scrimGo;
        private static Image _scrimImg;
        private static CanvasGroup _panelGroup;

        private static bool _introActive;
        private static float _introTime;
        private static float _introDeadline;

        /// <summary>建压暗层（只建一次）：铺得比画布大一圈，垫在面板下面。</summary>
        private static void EnsureScrim()
        {
            if (_scrimGo != null || _canvasGo == null) { return; }

            _scrimGo = new GameObject("RunHistory.Scrim", typeof(RectTransform));
            _scrimGo.transform.SetParent(_canvasGo.transform, false);
            var rt = _scrimGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-2600f, -2600f);
            rt.offsetMax = new Vector2(2600f, 2600f);

            _scrimImg = _scrimGo.AddComponent<Image>();
            var c = ScrimTint;
            c.a = 0f;
            _scrimImg.color = c;
            _scrimImg.raycastTarget = false;   // 隔断归 Blocker，压暗层只负责画

            _scrimGo.SetActive(false);
            Debug.Log("[RunHistory] 压暗层已创建（打开面板时环境亮度降至 " +
                Mathf.RoundToInt((1f - ScrimAlpha) * 100f) + "%）。");
        }

        /// <summary>开始「压暗 + 落位」：由 OpenPanel 调用。</summary>
        private static void StartIntro()
        {
            EnsureScrim();
            if (_scrimGo != null)
            {
                if (_panelGo != null)
                {
                    // 排在面板下面、入口牌子上面：环境与入口一起压暗，面板自己不受影响
                    _scrimGo.transform.SetSiblingIndex(_panelGo.transform.GetSiblingIndex());
                }
                if (_scrimImg != null)
                {
                    var c = _scrimImg.color;
                    c.a = 0f;
                    _scrimImg.color = c;
                }
                _scrimGo.SetActive(true);
            }

            if (_panelGo != null)
            {
                _panelGo.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, IntroRise);
            }
            if (_panelGroup != null) { _panelGroup.alpha = 0f; }

            _introActive = true;
            _introTime = 0f;
            _introDeadline = Time.realtimeSinceStartup + IntroSeconds + 0.5f;
            StepIntro(0f);
        }

        /// <summary>收尾（正常播完 / 超时兜底 / 关面板，三条路都走这里）。</summary>
        private static void FinishIntro()
        {
            _introActive = false;
            if (_scrimImg != null)
            {
                var c = _scrimImg.color;
                c.a = ScrimAlpha;
                _scrimImg.color = c;
            }
            if (_panelGo != null)
            {
                _panelGo.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
            }
            if (_panelGroup != null) { _panelGroup.alpha = 1f; }
        }

        /// <summary>关面板：动画中断，压暗层收起，位置复位（下次打开重新落位）。</summary>
        private static void StopIntro()
        {
            _introActive = false;
            if (_scrimGo != null) { _scrimGo.SetActive(false); }
            if (_panelGo != null)
            {
                _panelGo.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
            }
            if (_panelGroup != null) { _panelGroup.alpha = 1f; }
        }

        private static void StepIntro(float dt)
        {
            if (!_introActive) { return; }

            _introTime += Mathf.Max(0f, dt);
            float k = Mathf.Clamp01(_introTime / IntroSeconds);
            float e = 1f - (1f - k) * (1f - k) * (1f - k);   // ease-out cubic

            if (_scrimImg != null)
            {
                var c = _scrimImg.color;
                c.a = ScrimAlpha * e;
                _scrimImg.color = c;
            }
            if (_panelGo != null)
            {
                _panelGo.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, IntroRise * (1f - e));
            }
            if (_panelGroup != null) { _panelGroup.alpha = Mathf.Clamp01(e * 1.4f); }

            if (k >= 1f) { FinishIntro(); }
        }

        /// <summary>
        /// 每帧驱动（宿主 Update 在 0.5 秒限流之前调用；不做限流，动画要连续）。
        /// </summary>
        internal static void OnFrame(float dt)
        {
            if (!_introActive) { return; }
            try
            {
                StepIntro(dt);
                // 兜底：万一 Update 只跑了几帧就断了，超时后直接落到终值，不留半透明面板
                if (_introActive && Time.realtimeSinceStartup > _introDeadline) { FinishIntro(); }
            }
            catch
            {
                _introActive = false;
                FinishIntro();
            }
        }
    }
}
