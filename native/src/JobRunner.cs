using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace HuanYin
{
    internal sealed class JobRequest
    {
        public string VideoPath;
        public string AudioPath;
        public string OutputPath;
        public string AudioCodec = "alac";   // alac | aac | pcm
        public string Bitrate = "320k";
        public bool KeepVideoLength = true;
        public double VideoDuration;
        public WavInfo SourceWav;
        public string OverrideExt = "";      // 用旧版 ffmpeg 时退化为 mov

        public string OutputExt
        {
            get
            {
                if (!string.IsNullOrEmpty(OverrideExt)) return OverrideExt;
                return AudioCodec == "pcm" ? "mov" : "mp4";
            }
        }
    }

    internal sealed class JobResult
    {
        public bool Success;
        public bool Cancelled;
        public string Error = "";
        public string OutputPath = "";
        public long OutputSize;
        public double ElapsedSeconds;
        public string VerifiedSummary = "";
    }

    internal static class JobRunner
    {
        public static string[] BuildArgs(JobRequest req)
        {
            List<string> a = new List<string>();
            a.Add("-hide_banner");
            a.Add("-nostdin");
            a.Add("-y");
            a.Add("-i"); a.Add(req.VideoPath);
            a.Add("-i"); a.Add(req.AudioPath);

            a.Add("-map"); a.Add("0:v:0");
            a.Add("-map"); a.Add("1:a:0");
            a.Add("-map"); a.Add("0:s?");
            a.Add("-c:v"); a.Add("copy");
            a.Add("-c:s"); a.Add("copy");
            a.Add("-map_metadata"); a.Add("0");
            a.Add("-map_chapters"); a.Add("0");

            if (req.AudioCodec == "aac")
            {
                a.Add("-c:a"); a.Add("aac");
                a.Add("-b:a"); a.Add(string.IsNullOrEmpty(req.Bitrate) ? "320k" : req.Bitrate);
            }
            else if (req.AudioCodec == "pcm")
            {
                a.Add("-c:a"); a.Add(req.SourceWav != null ? req.SourceWav.PcmCodec : "pcm_s16le");
            }
            else
            {
                a.Add("-c:a"); a.Add("alac");
            }

            if (req.KeepVideoLength && req.VideoDuration > 0.05)
            {
                a.Add("-af"); a.Add("apad");
                a.Add("-t"); a.Add(req.VideoDuration.ToString("0.###", CultureInfo.InvariantCulture));
            }

            a.Add("-movflags"); a.Add("+faststart");
            a.Add("-progress"); a.Add("pipe:1");
            a.Add("-nostats");
            a.Add(req.OutputPath);
            return a.ToArray();
        }

        public static string BuildCommandLine(string ffmpegPath, JobRequest req)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(MediaProbe.Quote(ffmpegPath));
            foreach (string s in BuildArgs(req))
            {
                sb.Append(' ');
                if (s.IndexOf(' ') >= 0) sb.Append(MediaProbe.Quote(s));
                else sb.Append(s);
            }
            return sb.ToString();
        }

        public sealed class Callbacks
        {
            public Action<string> Log;
            public Action<string> Status;
            public Action<double, string> Progress;   // fraction (0..1 or -1), detail
            public Func<bool> IsCancelled;
        }

        public static JobResult Run(string ffmpegPath, JobRequest req, Callbacks cb)
        {
            JobResult result = new JobResult();
            result.OutputPath = req.OutputPath;
            Stopwatch sw = Stopwatch.StartNew();

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = ffmpegPath;
            psi.Arguments = JoinArgs(BuildArgs(req));
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;

            Process proc = null;
            try
            {
                proc = Process.Start(psi);
            }
            catch (Exception ex)
            {
                result.Error = "无法启动 ffmpeg：" + ex.Message;
                return result;
            }

            object gate = new object();
            List<string> errLines = new List<string>();
            double outTime = 0;
            string speed = "";
            bool cancelled = false;
            int endSeen = 0;

            DataReceivedEventHandler onOut = delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                string line = e.Data.Trim();
                if (line.Length == 0) return;
                int eq = line.IndexOf('=');
                if (eq <= 0) return;
                string key = line.Substring(0, eq);
                string val = line.Substring(eq + 1);
                if (key == "out_time")
                {
                    double t;
                    if (TryParseTime(val, out t)) outTime = t;
                    double frac = req.VideoDuration > 0.05 ? Math.Min(1.0, outTime / req.VideoDuration) : -1;
                    if (cb != null && cb.Progress != null)
                        cb.Progress(frac, MediaItem.FormatTime(outTime) +
                            (req.VideoDuration > 0.05 ? " / " + MediaItem.FormatTime(req.VideoDuration) : "") +
                            (speed.Length > 0 ? " · " + speed : ""));
                }
                else if (key == "speed") speed = val.Trim();
                else if (key == "progress" && val.Trim() == "end") System.Threading.Interlocked.Exchange(ref endSeen, 1);
            };

            DataReceivedEventHandler onErr = delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                string line = e.Data;
                lock (gate)
                {
                    errLines.Add(line);
                    if (errLines.Count > 400) errLines.RemoveRange(0, 100);
                }
                if (cb != null && cb.Log != null) cb.Log(line);
            };

            proc.OutputDataReceived += onOut;
            proc.ErrorDataReceived += onErr;
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            while (!proc.WaitForExit(120))
            {
                if (cb != null && cb.IsCancelled != null && cb.IsCancelled())
                {
                    cancelled = true;
                    try { proc.Kill(); } catch { }
                    try { proc.WaitForExit(3000); } catch { }
                    break;
                }
            }
            try { proc.WaitForExit(); } catch { }

            sw.Stop();
            result.ElapsedSeconds = sw.Elapsed.TotalSeconds;
            int code = -1;
            try { code = proc.ExitCode; } catch { }
            proc.Dispose();

            if (cancelled)
            {
                result.Cancelled = true;
                result.Error = "已取消";
                SafeDelete(req.OutputPath);
                return result;
            }

            if (code != 0)
            {
                result.Error = "ffmpeg 退出码 " + code;
                string tail = "";
                lock (gate)
                {
                    int start = Math.Max(0, errLines.Count - 12);
                    StringBuilder sb = new StringBuilder();
                    for (int i = start; i < errLines.Count; i++)
                    {
                        string l = errLines[i].Trim();
                        if (l.Length == 0) continue;
                        if (sb.Length > 0) sb.Append('\n');
                        sb.Append(l);
                    }
                    tail = sb.ToString();
                }
                if (tail.Length > 0) result.Error += "\n" + tail;
                SafeDelete(req.OutputPath);
                return result;
            }

            FileInfo fi = new FileInfo(req.OutputPath);
            if (!fi.Exists || fi.Length == 0)
            {
                result.Error = "输出文件没有生成";
                return result;
            }
            result.OutputSize = fi.Length;
            result.Success = true;
            return result;
        }

        /// <summary>Re-probes the produced file and returns a human readable one-liner.</summary>
        public static string VerifyOutput(string ffmpegPath, string outputPath)
        {
            MediaItem mi = new MediaItem();
            mi.Path = outputPath;
            mi.Kind = MediaKind.Video;
            string err;
            MediaProbe.ProbeWithFfmpeg(mi, ffmpegPath, out err, 15000);
            if (!mi.Probed) return "";
            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(mi.VideoCodec))
                sb.Append(string.Format("{0} {1}×{2}", mi.VideoCodec.ToUpperInvariant(), mi.Width, mi.Height));
            if (!string.IsNullOrEmpty(mi.AudioCodec))
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(mi.AudioCodec.ToUpperInvariant());
                if (mi.AudioSampleRate > 0) sb.Append(" " + mi.AudioSampleRate + "Hz");
                if (mi.AudioChannels > 0) sb.Append(" " + (mi.AudioChannels == 1 ? "单声道" : mi.AudioChannels == 2 ? "立体声" : mi.AudioChannels + "声道"));
            }
            if (mi.Duration > 0)
            {
                sb.Append(" · ");
                sb.Append(MediaItem.FormatTime(mi.Duration));
            }
            return sb.ToString();
        }

        public static string BuildOutputPath(string videoPath, string suffix, string outDir, string ext)
        {
            string dir;
            if (!string.IsNullOrEmpty(outDir)) dir = outDir;
            else dir = Path.GetDirectoryName(videoPath);
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(dir);

            string baseName = Path.GetFileNameWithoutExtension(videoPath);
            if (string.IsNullOrEmpty(baseName)) baseName = "output";
            if (string.IsNullOrEmpty(suffix)) suffix = "_newaudio";

            string candidate = Path.Combine(dir, baseName + suffix + "." + ext);
            int i = 2;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(dir, baseName + suffix + " (" + i + ")." + ext);
                i++;
                if (i > 999) break;
            }
            return candidate;
        }

        private static bool TryParseTime(string s, out double seconds)
        {
            seconds = 0;
            if (string.IsNullOrEmpty(s)) return false;
            string[] parts = s.Split(':');
            if (parts.Length != 3) return false;
            double h, m, sec;
            if (!double.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out h)) return false;
            if (!double.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out m)) return false;
            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out sec)) return false;
            seconds = h * 3600 + m * 60 + sec;
            return true;
        }

        private static string JoinArgs(string[] args)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string a in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                if (a.Length == 0) { sb.Append("\"\""); continue; }
                bool needQuote = a.IndexOf(' ') >= 0 || a.IndexOf('\t') >= 0 || a.IndexOf('"') >= 0 || a.IndexOf('&') >= 0 || a.IndexOf('^') >= 0;
                if (!needQuote) { sb.Append(a); continue; }
                sb.Append('"');
                foreach (char c in a)
                {
                    if (c == '"') sb.Append("\\\"");
                    else sb.Append(c);
                }
                sb.Append('"');
            }
            return sb.ToString();
        }

        private static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
