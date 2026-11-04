using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace HuanYin
{
    internal enum MediaKind
    {
        Unknown,
        Video,
        Audio
    }

    internal sealed class WavInfo
    {
        public int FormatTag;
        public int Channels;
        public int SampleRate;
        public int ByteRate;
        public int Bits;
        public long DataSize;

        public double Duration
        {
            get { return ByteRate > 0 ? (double)DataSize / ByteRate : 0.0; }
        }

        public string PcmCodec
        {
            get
            {
                if (FormatTag == 3)
                    return Bits == 64 ? "pcm_f64le" : "pcm_f32le";
                switch (Bits)
                {
                    case 8: return "pcm_u8";
                    case 16: return "pcm_s16le";
                    case 24: return "pcm_s24le";
                    case 32: return "pcm_s32le";
                    default: return "pcm_s16le";
                }
            }
        }

        public string FormatName
        {
            get
            {
                if (FormatTag == 1) return "PCM 整数";
                if (FormatTag == 3) return "IEEE 浮点";
                if (FormatTag == 6) return "A-law";
                if (FormatTag == 7) return "μ-law";
                return "格式 " + FormatTag;
            }
        }

        public int PcmBitrate { get { return SampleRate * Bits * Channels; } }
    }

    internal sealed class MediaItem
    {
        public int Id;
        public string Path;
        public MediaKind Kind;
        public long Size;
        public double Duration;          // seconds, 0 = unknown
        public int Width;
        public int Height;
        public string VideoCodec = "";
        public string AudioCodec = "";
        public int AudioChannels;
        public int AudioSampleRate;
        public WavInfo Wav;
        public bool Probed;
        public string ProbeNote = "";

        public string FileName { get { return System.IO.Path.GetFileName(Path); } }

        public string DisplayMeta()
        {
            List<string> parts = new List<string>();
            parts.Add(FormatSize(Size));
            if (Kind == MediaKind.Video)
            {
                parts.Add("视频");
                if (Width > 0 && Height > 0) parts.Add(Width + "×" + Height);
                if (!string.IsNullOrEmpty(VideoCodec)) parts.Add(VideoCodec);
            }
            else if (Kind == MediaKind.Audio)
            {
                if (Wav != null)
                {
                    parts.Add(Wav.SampleRate + " Hz");
                    parts.Add(Wav.Bits + " bit");
                    parts.Add(Wav.Channels + " 声道");
                }
                else
                {
                    if (AudioCodec.Length > 0) parts.Add(AudioCodec);
                    if (AudioSampleRate > 0) parts.Add(AudioSampleRate + " Hz");
                    if (AudioChannels > 0) parts.Add(AudioChannels + " 声道");
                }
            }
            else parts.Add("未知类型");

            if (Duration > 0) parts.Add(FormatTime(Duration));
            return string.Join(" · ", parts.ToArray());
        }

        public static string FormatSize(long bytes)
        {
            if (bytes >= 1073741824L) return (bytes / 1073741824.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
            if (bytes >= 1048576L) return (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            if (bytes >= 1024L) return (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
            return bytes + " B";
        }

        public static string FormatTime(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return "—";
            TimeSpan t = TimeSpan.FromSeconds(seconds);
            if (t.TotalHours >= 1)
                return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", t.Minutes, t.Seconds);
        }
    }

    internal static class MediaProbe
    {
        private static readonly string[] VideoExts = { ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".flv", ".wmv", ".ts", ".3gp", ".mpg", ".mpeg", ".m2ts" };
        private static readonly string[] AudioExts = { ".wav", ".mp3", ".flac", ".aac", ".ogg", ".m4a", ".opus", ".wma", ".aiff", ".aif", ".ape", ".alac" };

        public static MediaKind DetectKind(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            for (int i = 0; i < VideoExts.Length; i++) if (ext == VideoExts[i]) return MediaKind.Video;
            for (int i = 0; i < AudioExts.Length; i++) if (ext == AudioExts[i]) return MediaKind.Audio;
            return MediaKind.Unknown;
        }

        public static bool IsFfmpegBinary(string path)
        {
            try
            {
                return string.Equals(System.IO.Path.GetFileName(path), "ffmpeg.exe", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>Reads the RIFF/WAVE header directly (fast, no process spawn).</summary>
        public static WavInfo ReadWavHeader(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int len = (int)Math.Min(65536, fs.Length);
                    byte[] buf = new byte[len];
                    int read = 0;
                    while (read < len)
                    {
                        int n = fs.Read(buf, read, len - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read < 44) return null;
                    if (Encoding.ASCII.GetString(buf, 0, 4) != "RIFF") return null;
                    if (Encoding.ASCII.GetString(buf, 8, 4) != "WAVE") return null;

                    int off = 12;
                    WavInfo info = null;
                    long dataSize = 0;
                    while (off + 8 <= read)
                    {
                        string id = Encoding.ASCII.GetString(buf, off, 4);
                        long size = BitConverter.ToUInt32(buf, off + 4);
                        if (id == "fmt " && off + 8 + 16 <= read)
                        {
                            int p = off + 8;
                            int af = BitConverter.ToUInt16(buf, p);
                            info = new WavInfo();
                            info.FormatTag = af;
                            info.Channels = BitConverter.ToUInt16(buf, p + 2);
                            info.SampleRate = (int)BitConverter.ToUInt32(buf, p + 4);
                            info.ByteRate = (int)BitConverter.ToUInt32(buf, p + 8);
                            info.Bits = BitConverter.ToUInt16(buf, p + 14);
                            if (af == 0xFFFE && size >= 40 && p + 26 <= read)
                            {
                                int sf = BitConverter.ToUInt16(buf, p + 24);
                                if (sf > 0) info.FormatTag = sf;
                            }
                        }
                        else if (id == "data")
                        {
                            dataSize = size;
                            break;
                        }
                        off += 8 + (int)size + (int)(size % 2);
                    }
                    if (info == null) return null;
                    info.DataSize = dataSize;
                    return info;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Runs "ffmpeg -i" and parses the first video/audio stream info.</summary>
        public static bool ProbeWithFfmpeg(MediaItem item, string ffmpegPath, out string stderr, int timeoutMs)
        {
            stderr = "";
            if (string.IsNullOrEmpty(ffmpegPath)) return false;

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = ffmpegPath;
            psi.Arguments = "-hide_banner -nostdin -i " + Quote(item.Path);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            psi.StandardErrorEncoding = Encoding.UTF8;

            try
            {
                using (Process p = Process.Start(psi))
                {
                    StringBuilder sb = new StringBuilder();
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null)
                        {
                            lock (sb) sb.AppendLine(e.Data);
                        }
                    };
                    p.BeginErrorReadLine();
                    p.BeginOutputReadLine();  // discard
                    if (!p.WaitForExit(timeoutMs > 0 ? timeoutMs : 20000))
                    {
                        try { p.Kill(); } catch { }
                        return false;
                    }
                    lock (sb) stderr = sb.ToString();
                }
            }
            catch (Exception ex)
            {
                stderr = "探测失败: " + ex.Message;
                return false;
            }

            ParseProbe(stderr, item);
            return item.Probed;
        }

        private static readonly Regex ReDuration = new Regex(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);
        private static readonly Regex ReVideo = new Regex(@"Stream #\d+:\d+(?:\[[^\]]*\])?(?:\([^)]*\))?:\s*Video:\s*([A-Za-z0-9_\-]+).*?(\d{2,5})x(\d{2,5})", RegexOptions.Compiled);
        private static readonly Regex ReAudio = new Regex(@"Stream #\d+:\d+(?:\[[^\]]*\])?(?:\([^)]*\))?:\s*Audio:\s*([A-Za-z0-9_\-]+).*?(\d{4,6})\s*Hz,\s*([^,]+)", RegexOptions.Compiled);

        public static void ParseProbe(string text, MediaItem item)
        {
            if (string.IsNullOrEmpty(text)) return;
            Match m = ReDuration.Match(text);
            if (m.Success)
            {
                double h = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                double mi = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                double se = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                item.Duration = h * 3600 + mi * 60 + se;
            }
            Match mv = ReVideo.Match(text);
            if (mv.Success)
            {
                item.VideoCodec = mv.Groups[1].Value;
                item.Width = int.Parse(mv.Groups[2].Value, CultureInfo.InvariantCulture);
                item.Height = int.Parse(mv.Groups[3].Value, CultureInfo.InvariantCulture);
                if (item.Kind == MediaKind.Unknown) item.Kind = MediaKind.Video;
            }
            Match ma = ReAudio.Match(text);
            if (ma.Success)
            {
                item.AudioCodec = ma.Groups[1].Value;
                item.AudioSampleRate = int.Parse(ma.Groups[2].Value, CultureInfo.InvariantCulture);
                string ch = ma.Groups[3].Value.Trim();
                if (ch.Contains("mono")) item.AudioChannels = 1;
                else if (ch.Contains("stereo")) item.AudioChannels = 2;
                else
                {
                    Match mc = Regex.Match(ch, @"(\d+)\s*channels");
                    if (mc.Success) item.AudioChannels = int.Parse(mc.Groups[1].Value, CultureInfo.InvariantCulture);
                }
                if (item.Kind == MediaKind.Unknown) item.Kind = MediaKind.Audio;
            }
            item.Probed = !string.IsNullOrEmpty(item.VideoCodec) || !string.IsNullOrEmpty(item.AudioCodec) || item.Duration > 0;
        }

        public static string Quote(string s)
        {
            return "\"" + s + "\"";
        }
    }
}
