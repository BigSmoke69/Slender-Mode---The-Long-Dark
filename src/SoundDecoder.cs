using System;
using System.Collections.Generic;
using System.IO;

namespace SlenderMode
{
    /// <summary>
    /// Decodes MP3 (via the bundled NLayer decoder) and WAV files to PCM in pure managed code.
    /// Unity's own decoder can't be called from mods in this game version.
    /// Long tracks are mixed to mono at 22 kHz to keep memory use reasonable.
    /// </summary>
    internal sealed class DecodedSound
    {
        public string Name;
        public float[] Samples;   // interleaved
        public int Channels;
        public int Frequency;
    }

    internal static class SoundDecoder
    {
        const float LongTrackSeconds = 60f;
        const int LongTrackRate = 22050;

        public static DecodedSound Decode(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            float[] pcm; int ch, rate;
            if (ext == ".mp3") DecodeMp3(path, out pcm, out ch, out rate);
            else if (ext == ".wav") DecodeWav(path, out pcm, out ch, out rate);
            else throw new NotSupportedException("Only .mp3 and .wav are supported (got " + ext + ")");

            float seconds = pcm.Length / (float)(ch * rate);
            if (seconds > LongTrackSeconds && (ch > 1 || rate > LongTrackRate))
            {
                pcm = ToMono(pcm, ch);
                ch = 1;
                pcm = Resample(pcm, rate, LongTrackRate);
                rate = LongTrackRate;
            }
            return new DecodedSound { Name = Path.GetFileName(path), Samples = pcm, Channels = ch, Frequency = rate };
        }

        static void DecodeMp3(string path, out float[] pcm, out int ch, out int rate)
        {
            using (var mp3 = new NLayer.MpegFile(path))
            {
                ch = mp3.Channels;
                rate = mp3.SampleRate;
                var chunks = new List<float[]>();
                var buf = new float[ch * 16384];
                int total = 0, n;
                while ((n = mp3.ReadSamples(buf, 0, buf.Length)) > 0)
                {
                    var c = new float[n];
                    Array.Copy(buf, c, n);
                    chunks.Add(c);
                    total += n;
                }
                pcm = new float[total];
                int o = 0;
                foreach (var c in chunks) { Array.Copy(c, 0, pcm, o, c.Length); o += c.Length; }
            }
        }

        static void DecodeWav(string path, out float[] pcm, out int ch, out int rate)
        {
            using (var br = new BinaryReader(File.OpenRead(path)))
            {
                if (new string(br.ReadChars(4)) != "RIFF") throw new InvalidDataException("Not a WAV file");
                br.ReadInt32();
                if (new string(br.ReadChars(4)) != "WAVE") throw new InvalidDataException("Not a WAV file");
                int fmt = 1, bits = 16; ch = 2; rate = 44100;
                pcm = null;
                while (br.BaseStream.Position + 8 <= br.BaseStream.Length)
                {
                    string id = new string(br.ReadChars(4));
                    int size = br.ReadInt32();
                    long next = br.BaseStream.Position + size + (size & 1);
                    if (id == "fmt ")
                    {
                        fmt = br.ReadInt16(); ch = br.ReadInt16(); rate = br.ReadInt32();
                        br.ReadInt32(); br.ReadInt16(); bits = br.ReadInt16();
                        if (fmt == -2 && size >= 26) { br.ReadInt16(); br.ReadInt16(); br.ReadInt32(); fmt = br.ReadInt16(); } // extensible
                    }
                    else if (id == "data")
                    {
                        int count = size / (bits / 8);
                        pcm = new float[count];
                        for (int i = 0; i < count; i++)
                        {
                            if (fmt == 3 && bits == 32) pcm[i] = br.ReadSingle();
                            else if (bits == 16) pcm[i] = br.ReadInt16() / 32768f;
                            else if (bits == 24) { int b0 = br.ReadByte(), b1 = br.ReadByte(), b2 = (sbyte)br.ReadByte(); pcm[i] = ((b2 << 16) | (b1 << 8) | b0) / 8388608f; }
                            else if (bits == 8) pcm[i] = (br.ReadByte() - 128) / 128f;
                            else if (bits == 32) pcm[i] = br.ReadInt32() / 2147483648f;
                            else throw new NotSupportedException("Unsupported WAV bit depth: " + bits);
                        }
                        break;
                    }
                    br.BaseStream.Position = next;
                }
                if (pcm == null) throw new InvalidDataException("WAV has no data");
            }
        }

        static float[] ToMono(float[] src, int ch)
        {
            if (ch == 1) return src;
            int frames = src.Length / ch;
            var dst = new float[frames];
            for (int f = 0; f < frames; f++)
            {
                float s = 0f;
                for (int c = 0; c < ch; c++) s += src[f * ch + c];
                dst[f] = s / ch;
            }
            return dst;
        }

        /// <summary>Mono resample with a simple averaging low-pass (good enough for ambience).</summary>
        static float[] Resample(float[] src, int from, int to)
        {
            if (from == to) return src;
            double ratio = from / (double)to;
            int outLen = (int)(src.Length / ratio);
            var dst = new float[outLen];
            for (int i = 0; i < outLen; i++)
            {
                double start = i * ratio, end = start + ratio;
                int a = (int)start, b = Math.Min(src.Length, (int)Math.Ceiling(end));
                float sum = 0f; int n = 0;
                for (int k = a; k < b; k++) { sum += src[k]; n++; }
                dst[i] = n > 0 ? sum / n : 0f;
            }
            return dst;
        }
    }
}
