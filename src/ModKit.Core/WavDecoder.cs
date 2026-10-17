// =============================================================================
//  ModKit.Core · 极简 WAV 解码（作者自带音效）
//
//  为什么放 Core：解码是**纯字节运算**，不碰 Unity、不碰游戏 ——
//  所以能在这台机器的桌面 CLR 上直接跑测试（见 ModKit.Tests/WavTests.cs）。
//  "放出声"的那半边（AudioClip / AudioSource）在 ModKit.Game/ModAudio.cs。
//
//  刻意支持的窄范围：RIFF/WAVE、PCM(1) 8/16/24/32 位、IEEE float(3) 32 位、
//  单声道或立体声。其它一律**明确报错**（错误串会进日志），绝不静默出噪声。
// =============================================================================

using System;
using System.Text;

namespace ModKit.Core
{
    /// <summary>解码结果：交错浮点样本（范围 [-1,1]）+ 采样率 / 声道数。</summary>
    public sealed class WavData
    {
        /// <summary>采样率（Hz）。</summary>
        public int SampleRate;

        /// <summary>声道数（1 或 2）。</summary>
        public int Channels;

        /// <summary>每声道的帧数（= <see cref="Samples"/>.Length / <see cref="Channels"/>）。</summary>
        public int SampleCount;

        /// <summary>交错样本（长度 = SampleCount * Channels）；Unity 的 AudioClip.SetData 要的就是这个形状。</summary>
        public float[] Samples;
    }

    /// <summary>把 wav 字节解成 Unity 能吃的浮点样本。</summary>
    public static class WavDecoder
    {
        /// <summary>
        /// 解码。<paramref name="bytes"/> 非法时返回 null 并给出中文 <paramref name="error"/>。
        /// </summary>
        public static WavData Decode(byte[] bytes, out string error)
        {
            error = null;
            if (bytes == null || bytes.Length < 44) { error = "文件太小，不是完整的 wav"; return null; }
            if (Ascii(bytes, 0, 4) != "RIFF" || Ascii(bytes, 8, 4) != "WAVE")
            {
                error = "不是 RIFF/WAVE 格式";
                return null;
            }

            int format = -1;
            int channels = 0;
            int sampleRate = 0;
            int bits = 0;
            int dataOffset = -1;
            int dataLength = 0;
            bool sawFmt = false;

            int pos = 12;
            while (pos + 8 <= bytes.Length)
            {
                string id = Ascii(bytes, pos, 4);
                int size = BitConverter.ToInt32(bytes, pos + 4);
                int body = pos + 8;

                if (size < 0 || body + size > bytes.Length)
                {
                    // 有些工具把 data 的长度写成 0 或越界 —— 只在 data 上容忍（按剩余字节算）
                    if (id == "data") { size = bytes.Length - body; }
                    else { error = "块 " + id + " 的长度越界"; return null; }
                }

                if (id == "fmt ")
                {
                    if (size < 16) { error = "fmt 块太短"; return null; }
                    format = BitConverter.ToInt16(bytes, body);
                    channels = BitConverter.ToInt16(bytes, body + 2);
                    sampleRate = BitConverter.ToInt32(bytes, body + 4);
                    bits = BitConverter.ToInt16(bytes, body + 14);
                    sawFmt = true;
                }
                else if (id == "data")
                {
                    dataOffset = body;
                    dataLength = size;
                    break;
                }

                pos = body + size + (size % 2);
            }

            if (!sawFmt) { error = "缺少 fmt 块"; return null; }
            if (dataOffset < 0) { error = "缺少 data 块"; return null; }
            if (format == unchecked((short)0xFFFE)) { format = 1; }   // WAVE_FORMAT_EXTENSIBLE：按 PCM 处理
            if (format != 1 && format != 3) { error = "只支持 PCM 与 float32（format=" + format + "）"; return null; }
            if (channels != 1 && channels != 2) { error = "只支持单声道或立体声（channels=" + channels + "）"; return null; }
            if (sampleRate <= 0) { error = "采样率非法（" + sampleRate + "）"; return null; }
            if (bits != 8 && bits != 16 && bits != 24 && bits != 32) { error = "位深不支持（" + bits + " 位）"; return null; }
            if (format == 3 && bits != 32) { error = "float 只支持 32 位"; return null; }

            int bytesPerSample = bits / 8;
            int frameBytes = bytesPerSample * channels;
            int frames = dataLength / frameBytes;
            if (frames <= 0) { error = "没有采样数据（data " + dataLength + " 字节）"; return null; }

            float[] samples = new float[frames * channels];
            int p = dataOffset;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = ReadSample(bytes, p, format, bits);
                p += bytesPerSample;
            }

            WavData d = new WavData();
            d.SampleRate = sampleRate;
            d.Channels = channels;
            d.SampleCount = frames;
            d.Samples = samples;
            return d;
        }

        private static float ReadSample(byte[] b, int at, int format, int bits)
        {
            if (format == 3) { return BitConverter.ToSingle(b, at); }        // float32
            if (bits == 8) { return (b[at] - 128) / 128f; }                   // 8 位 wav 是无符号
            if (bits == 16) { return BitConverter.ToInt16(b, at) / 32768f; }
            if (bits == 24)
            {
                int v = b[at] | (b[at + 1] << 8) | (b[at + 2] << 16);
                if ((v & 0x800000) != 0) { v |= unchecked((int)0xFF000000); }
                return v / 8388608f;
            }
            return BitConverter.ToInt32(b, at) / 2147483648f;
        }

        private static string Ascii(byte[] b, int at, int len)
        {
            if (at < 0 || at + len > b.Length) { return ""; }
            return Encoding.ASCII.GetString(b, at, len);
        }
    }
}
