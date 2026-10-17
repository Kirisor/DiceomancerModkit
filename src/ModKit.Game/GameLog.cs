// =============================================================================
//  ModKit.Game · 日志适配
//
//  平台的其余部分（Api / Core）不依赖 BepInEx —— 它们通过委托或接口拿日志。
//  只有这一层知道 BepInEx 的存在。
//
//  ★ 一个实测过的坑：BepInEx 的 ManualLogSource **不能重复创建**同名 source，
//    而且插件自己的 MonoBehaviour 会在首个场景加载时销毁
//    （DontDestroyOnLoad 被拒）。
//    所以日志源做成**一次性静态**，不挂在任何 MonoBehaviour 上。
// =============================================================================

using System;
using BepInEx.Logging;

namespace ModKit.Game
{
    /// <summary>平台在游戏侧的日志接口。Api / Core 不认识 BepInEx，只认识这个。</summary>
    public interface IGameLog
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
        void Error(string message, Exception ex);
    }

    /// <summary>
    /// 把日志同时送到 BepInEx 控制台与 Unity 日志。
    /// </summary>
    /// <remarks>
    /// 双写是刻意的：验收脚本（`mod/verify.ps1`）读的是
    /// `BepInEx\LogOutput.log`，而游戏内的错误弹窗看的是 Unity 日志。
    /// 只写一边会漏掉一半的观测面。
    /// </remarks>
    public sealed class BepInExGameLog : IGameLog
    {
        private static ManualLogSource _source;

        private static ManualLogSource Source
        {
            get
            {
                if (_source == null)
                {
                    // 必须全限定：UnityEngine 也导出名为 Logger 的类型，
                    // 若把 UnityEngine 引进来（比如为了 Debug.Log），这里会变成二义引用。
                    _source = BepInEx.Logging.Logger.CreateLogSource("ModKit");
                }
                return _source;
            }
        }

        /// <summary>日志前缀，方便在混合日志里一眼分辨平台输出。</summary>
        public string Prefix { get; set; }

        public BepInExGameLog(string prefix)
        {
            Prefix = prefix ?? "[ModKit]";
        }

        public void Info(string message)
        {
            Source.LogInfo(Prefix + " " + message);
        }

        public void Warn(string message)
        {
            Source.LogWarning(Prefix + " " + message);
        }

        public void Error(string message)
        {
            Source.LogError(Prefix + " " + message);
            
        }

        public void Error(string message, Exception ex)
        {
            Source.LogError(Prefix + " " + message);
            Source.LogError(Prefix + " " + (ex == null ? "(null exception)" : ex.ToString()));
        }
    }

    /// <summary>
    /// 兜底日志：BepInEx 不可用时退回 Unity 日志（测试宿主或早期启动阶段）。
    /// </summary>
    public sealed class UnityFallbackLog : IGameLog
    {
        private readonly string _prefix;

        public UnityFallbackLog(string prefix)
        {
            _prefix = prefix ?? "[ModKit]";
        }

        public void Info(string message) { UnityEngine.Debug.Log(_prefix + " " + message); }
        public void Warn(string message) { UnityEngine.Debug.LogWarning(_prefix + " " + message); }
        public void Error(string message) { UnityEngine.Debug.LogError(_prefix + " " + message); }

        public void Error(string message, Exception ex)
        {
            UnityEngine.Debug.LogError(_prefix + " " + message);
            UnityEngine.Debug.LogError(_prefix + " " + (ex == null ? "(null exception)" : ex.ToString()));
        }
    }
}
