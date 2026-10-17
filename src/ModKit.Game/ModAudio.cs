// =============================================================================
//  ModKit.Game · 音效桥
//
//  接上 ModKit.Api 的两个针脚：
//    ① 原版音效：名字 → 游戏自己的 UtilSoundDataManager 名单 → AudioManager.PlaySFXOnce(name)
//       走的是游戏原有的 FMOD 通路 ⇒ 自动跟随设置里的 SFX 音量，也不需要我们碰音频资源。
//    ② 作者自带 wav：磁盘文件 → ModKit.Core.WavDecoder → Unity AudioSource。
//       （FMOD 事件不能凭空造，所以自定义音频走 Unity 音频通路；
//         音量用反射读 FMOD 的 vca:/SFX，尽量跟游戏设置保持一致。）
//
//  ★ 名单是**懒探测**的：BepInEx 插件启动时游戏还没 Awake，UtilSoundDataManager.Instance
//    必然为 null ⇒ 推迟到第一次真要播音效时再取。顺带在播放前判一次"名字在不在名单里"，
//    从源头避开游戏自己那句 Debug.LogError("SoundRef not found by name: …") 红字。
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using ModKit.Api;
using ModKit.Core;
using UnityEngine;
using Utilities;
using Utilities.Audio;

namespace ModKit.Game
{
    /// <summary>把"播原版音效 / 播自带 wav"接成 <see cref="ModKitSfx"/> 的静态针脚。</summary>
    public static class ModAudio
    {
        private const string Prefix = "[ModKit] 音效：";

        private static IGameLog _log;
        private static bool _dumpRequested;
        private static bool _dumped;

        private static UtilSoundDataManager.SoundRef[] _refs;
        private static readonly List<string> _names = new List<string>();
        private static readonly HashSet<string> _nameSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, AudioClip> _clips =
            new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private static AudioSource _source;

        /// <summary>FMOD Core 的 Sound 句柄缓存（按路径）。刻意不 release：一首曲子一个句柄，生命周期 = 进程。</summary>
        private static readonly Dictionary<string, FMOD.Sound> _sounds =
            new Dictionary<string, FMOD.Sound>(StringComparer.OrdinalIgnoreCase);

        /// <summary>已经报过"播放成功"的路径（避免每次连锁都刷日志）。</summary>
        private static readonly HashSet<string> _announced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 装配针脚。由插件在 BepInEx 启动时调用一次。
        /// **此处不要碰任何游戏 Manager**（游戏还没 Awake），一切留到第一次播放时。
        /// </summary>
        public static void Install(IGameLog log, bool dumpSfx)
        {
            _log = log;
            _dumpRequested = dumpSfx;
            ModKitSfx.PlayHandler = PlayVanilla;
            ModKitSfx.PlayFileHandler = PlayFile;
        }

        // ---------------------------------------------------------------------
        //  ① 原版音效
        // ---------------------------------------------------------------------

        /// <summary>懒探测原版音效名单。管理器还没就绪 ⇒ false（调用方按"播放失败"处理）。</summary>
        private static bool EnsurePalette()
        {
            if (_refs != null) { return true; }

            UtilSoundDataManager mgr = UtilSoundDataManager.Instance;
            if (mgr == null || mgr.soundRefs == null) { return false; }

            _refs = mgr.soundRefs;
            _names.Clear();
            _nameSet.Clear();
            for (int i = 0; i < _refs.Length; i++)
            {
                string n = _refs[i].refName;
                if (string.IsNullOrEmpty(n) || _nameSet.Contains(n)) { continue; }
                _nameSet.Add(n);
                _names.Add(n);
            }
            _names.Sort(StringComparer.Ordinal);
            ModKitSfx.SetVanillaNames(_names);

            if (_log != null) { _log.Info(Prefix + "探测到原版音效 " + _names.Count + " 个。"); }
            if (_dumpRequested && !_dumped)
            {
                _dumped = true;
                if (_log != null) { _log.Info(Prefix + "全部原版音效名：" + string.Join("、", _names.ToArray())); }
            }
            return true;
        }

        private static bool PlayVanilla(string soundName)
        {
            if (!EnsurePalette())
            {
                WarnOnce("palette-not-ready",
                    Prefix + "音效管理器还没就绪，本次跳过音效「" + soundName + "」。");
                return false;
            }
            if (!_nameSet.Contains(soundName))
            {
                WarnOnce(soundName,
                    Prefix + "找不到原版音效「" + soundName +
                    "」（名字表可用配置 Diagnostics/SfxDump = true 打进日志查看）。");
                return false;
            }

            AudioManager am = AudioManager.Instance;
            if (am == null)
            {
                WarnOnce("no-audiomanager", Prefix + "AudioManager 尚未就绪，本次跳过音效「" + soundName + "」。");
                return false;
            }
            try
            {
                am.PlaySFXOnce(soundName);
                return true;
            }
            catch (Exception ex)
            {
                if (_log != null) { _log.Warn(Prefix + "播放原版音效「" + soundName + "」失败：" + ex.Message); }
                return false;
            }
        }

        // ---------------------------------------------------------------------
        //  ② 作者自带的 wav
        // ---------------------------------------------------------------------

        private static bool PlayFile(string wavPath)
        {
            // 文件不存在 = 作者没自带音效，属于正常情况：不记警告，让调用方回退到原版音效。
            if (string.IsNullOrEmpty(wavPath) || !File.Exists(wavPath)) { return false; }

            // 自家解码器只做"体检"：解得出来说明这是我们认识的 PCM，顺便拿时长/格式写日志。
            // ★ 真正播放交给 FMOD —— 见 PlayThroughFmod 的注释（本作 Unity 音频不可用）。
            WavData data = null;
            try
            {
                string error;
                data = WavDecoder.Decode(File.ReadAllBytes(wavPath), out error);
                if (data == null && _log != null)
                {
                    _log.Warn(Prefix + "自带音频不是标准 PCM wav（" + error + "），仍交给 FMOD 尝试：" + wavPath);
                }
            }
            catch (Exception ex)
            {
                if (_log != null) { _log.Warn(Prefix + "读取自带音频失败：" + ex.Message + "（" + wavPath + "）"); }
            }

            if (PlayThroughFmod(wavPath, data)) { return true; }

            // 兜底：Unity 自己的音频通路（本作多半用不上，见下面注释）。
            return PlayThroughUnity(wavPath, data);
        }

        /// <summary>
        /// 走 FMOD Core API 播放磁盘音频文件。
        /// ★ 2026-10-15 实测教训：本作只用 FMOD（Unity 内置音频被关掉），
        ///   走 Unity 的 <c>AudioClip.Create</c> + <c>SetData</c> 会**返回 false**
        ///   （日志实证：「音效：AudioClip.SetData 失败：…\naifrog.wav」），
        ///   所以作者自带音频必须交给 FMOD 播放。
        /// 音量：Core 声音不经过 Studio 的 vca:/SFX，这里手动乘上该 VCA 的音量来近似跟随设置。
        /// </summary>
        private static bool PlayThroughFmod(string path, WavData data)
        {
            try
            {
                // ★ FMOD 的托管包装是**结构体**（不是类）：不能拿 null 比较，ChannelGroup 也只能用 default。
                FMOD.System core = FMODUnity.RuntimeManager.CoreSystem;

                FMOD.Sound sound;
                if (!_sounds.TryGetValue(path, out sound))
                {
                    FMOD.RESULT created = core.createSound(path,
                        FMOD.MODE.LOOP_OFF | FMOD.MODE._2D | FMOD.MODE.CREATESAMPLE, out sound);
                    if (created != FMOD.RESULT.OK)
                    {
                        if (_log != null)
                        {
                            _log.Warn(Prefix + "FMOD 载入音频失败（" + created + "）：" + path +
                                "（ERR_INVALID_HANDLE 通常表示 CoreSystem 还没初始化）");
                        }
                        return false;
                    }
                    _sounds[path] = sound;
                }

                FMOD.ChannelGroup master;
                if (core.getMasterChannelGroup(out master) != FMOD.RESULT.OK)
                {
                    master = default(FMOD.ChannelGroup);
                }

                FMOD.Channel channel;
                FMOD.RESULT played = core.playSound(sound, master, false, out channel);
                if (played != FMOD.RESULT.OK)
                {
                    if (_log != null) { _log.Warn(Prefix + "FMOD 播放失败（" + played + "）：" + path); }
                    return false;
                }
                channel.setVolume(SfxVolume());

                if (_announced.Add(path) && _log != null)
                {
                    _log.Info(Prefix + "已用 FMOD 播放自带音频 " + Path.GetFileName(path) +
                        (data != null
                            ? "（" + data.SampleRate + " Hz / " + data.Channels + " 声道 / " + data.SampleCount + " 帧）"
                            : "（自家解码器读不懂，FMOD 仍接受了它）") + "。");
                }
                return true;
            }
            catch (Exception ex)
            {
                if (_log != null) { _log.Warn(Prefix + "FMOD 播放自带音频异常：" + ex.Message + "（" + path + "）"); }
                return false;
            }
        }

        private static bool PlayThroughUnity(string path, WavData data)
        {
            if (data == null) { return false; }

            AudioClip clip = LoadClip(path, data);
            if (clip == null) { return false; }

            AudioSource src = EnsureSource();
            if (src == null) { return false; }

            try
            {
                src.PlayOneShot(clip, SfxVolume());
                return true;
            }
            catch (Exception ex)
            {
                if (_log != null) { _log.Warn(Prefix + "播放自带音效失败：" + ex.Message); }
                return false;
            }
        }

        private static AudioClip LoadClip(string path, WavData data)
        {
            AudioClip cached;
            if (_clips.TryGetValue(path, out cached) && cached != null) { return cached; }

            try
            {
                AudioClip clip = AudioClip.Create(
                    Path.GetFileNameWithoutExtension(path),
                    data.SampleCount,
                    data.Channels,
                    data.SampleRate,
                    false);
                if (clip == null)
                {
                    if (_log != null) { _log.Warn(Prefix + "AudioClip.Create 返回 null：" + path); }
                    return null;
                }
                if (!clip.SetData(data.Samples, 0))
                {
                    if (_log != null) { _log.Warn(Prefix + "AudioClip.SetData 失败：" + path); }
                    return null;
                }

                _clips[path] = clip;
                if (_log != null)
                {
                    _log.Info(Prefix + "已加载自带音效 " + Path.GetFileName(path) +
                        "（" + data.SampleRate + " Hz / " + data.Channels + " 声道 / " + data.SampleCount + " 帧）。");
                }
                return clip;
            }
            catch (Exception ex)
            {
                if (_log != null) { _log.Warn(Prefix + "加载自带音效失败：" + ex.Message + "（" + path + "）"); }
                return null;
            }
        }

        private static AudioSource EnsureSource()
        {
            if (_source != null) { return _source; }
            try
            {
                GameObject go = new GameObject("ModKitSfxPlayer");
                UnityEngine.Object.DontDestroyOnLoad(go);
                AudioSource src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 0f;      // 纯 2D：和 UI/卡牌音一样不做空间衰减
                _source = src;
            }
            catch (Exception ex)
            {
                if (_log != null) { _log.Warn(Prefix + "创建 AudioSource 失败：" + ex.Message); }
            }
            return _source;
        }

        /// <summary>自带音频的音量：跟随游戏设置里的 SFX 音量（FMOD 的 vca:/SFX）。读不到就按 1.0。</summary>
        private static float SfxVolume()
        {
            try
            {
                Type rm = Type.GetType("FMODUnity.RuntimeManager, FMODUnity");
                if (rm == null) { return 1f; }
                System.Reflection.MethodInfo getVca = rm.GetMethod("GetVCA", new Type[] { typeof(string) });
                if (getVca == null) { return 1f; }
                object vca = getVca.Invoke(null, new object[] { "vca:/SFX" });
                if (vca == null) { return 1f; }
                System.Reflection.MethodInfo getVolume =
                    vca.GetType().GetMethod("getVolume", new Type[] { typeof(float).MakeByRefType() });
                if (getVolume == null) { return 1f; }
                object[] args = new object[] { 0f };
                getVolume.Invoke(vca, args);
                float v = (float)args[0];
                return v > 0f ? v : 1f;
            }
            catch
            {
                return 1f;
            }
        }

        private static void WarnOnce(string key, string message)
        {
            if (!_warned.Add(key)) { return; }
            if (_log != null) { _log.Warn(message); }
        }
    }
}
