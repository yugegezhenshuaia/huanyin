using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace HuanYin
{
    internal static class Program
    {
        private const string MutexName = "Global\\HuanYin_SingleInstance";

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int processId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && (args[0] == "--cli" || args[0] == "-cli"))
            {
                AttachConsole(-1);
                return Cli.Run(SubArray(args, 1));
            }
            if (args.Length > 0 && args[0] == "--ffmpeg-info")
            {
                AttachConsole(-1);
                Cli.FfmpegInfo();
                return 0;
            }
            if (args.Length > 0 && args[0] == "--install-ffmpeg")
            {
                AttachConsole(-1);
                return Cli.InstallFfmpeg();
            }

            bool created;
            using (Mutex mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    ActivateExistingWindow();
                    return 0;
                }

                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    AppTheme.InitDpi();
                    try { Native.SetProcessDPIAware(); }
                    catch { }

                    MainForm form = new MainForm();
                    string[] files = FileArgs(args);
                    bool autoRun = false;
                    foreach (string a in args) if (a == "--gui-run") autoRun = true;
                    if (files.Length > 0)
                    {
                        form.Shown += delegate
                        {
                            form.AddPathsOnStartup(files);
                            if (autoRun) form.DevRun();
                        };
                    }
                    Application.Run(form);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("程序启动失败：\n\n" + ex.Message + "\n\n" + ex.StackTrace,
                        "焕音", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally
                {
                    GC.KeepAlive(mutex);
                }
            }
            return 0;
        }

        private static void ActivateExistingWindow()
        {
            try
            {
                Process current = Process.GetCurrentProcess();
                foreach (Process p in Process.GetProcessesByName(current.ProcessName))
                {
                    if (p.Id == current.Id) continue;
                    IntPtr h = p.MainWindowHandle;
                    if (h != IntPtr.Zero)
                    {
                        ShowWindow(h, 9);
                        SetForegroundWindow(h);
                        break;
                    }
                }
            }
            catch { }
        }

        private static string[] SubArray(string[] arr, int from)
        {
            if (from >= arr.Length) return new string[0];
            string[] r = new string[arr.Length - from];
            Array.Copy(arr, from, r, 0, r.Length);
            return r;
        }

        private static string[] FileArgs(string[] args)
        {
            List<string> files = new List<string>();
            foreach (string a in args)
            {
                if (string.IsNullOrEmpty(a)) continue;
                if (a[0] == '-') continue;
                try { if (File.Exists(a)) files.Add(Path.GetFullPath(a)); }
                catch { }
            }
            return files.ToArray();
        }
    }

    internal static class Cli
    {
        private static StreamWriter outWriter;

        private static void W(string s)
        {
            try
            {
                if (outWriter == null)
                {
                    outWriter = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                    outWriter.AutoFlush = true;
                }
                outWriter.WriteLine(s);
            }
            catch { }
        }

        public static void FfmpegInfo()
        {
            string p = FfmpegSetup.Locate();
            W("exe dir      : " + FfmpegSetup.ExeDir);
            W("cache dir    : " + FfmpegSetup.HomeDir);
            W("embedded     : " + (FfmpegSetup.HasEmbeddedPayload ? "yes" : "no"));
            W("ffmpeg found : " + (p ?? "(none)"));
            if (p != null) W("version      : " + FfmpegSetup.VersionText(p));
        }

        public static int InstallFfmpeg()
        {
            string p = FfmpegSetup.Locate();
            if (p != null)
            {
                W("已有可用 ffmpeg: " + p);
                return 0;
            }
            if (FfmpegSetup.HasEmbeddedPayload)
            {
                W("正在解压内置 ffmpeg …");
                if (FfmpegSetup.InstallFromEmbedded(delegate(string m, double f) { W(m); }))
                {
                    W("完成: " + FfmpegSetup.CacheFfmpeg);
                    return 0;
                }
            }
            W("开始下载 ffmpeg（多镜像自动重试）…");
            bool ok = FfmpegSetup.Download(
                delegate(string msg) { W(msg); },
                delegate(double frac, string detail)
                {
                    Console.Error.Write("\r" + (frac >= 0 ? (frac * 100).ToString("0.0") + "% " : "") + detail + "     ");
                },
                delegate { return false; });
            Console.Error.WriteLine();
            if (ok)
            {
                W("完成: " + FfmpegSetup.CacheFfmpeg);
                W("版本: " + FfmpegSetup.VersionText(FfmpegSetup.CacheFfmpeg));
                return 0;
            }
            W("失败：所有下载源都不可用（可手动下载 ffmpeg.exe 放到 " + FfmpegSetup.CacheBinDir + "）");
            return 1;
        }

        public static int Run(string[] args)
        {
            string video = null, audio = null, output = null, codec = "alac", bitrate = "320k";
            bool trim = true;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "-o" || a == "--output") { if (i + 1 < args.Length) output = args[++i]; }
                else if (a == "-c" || a == "--codec") { if (i + 1 < args.Length) codec = args[++i]; }
                else if (a == "-b" || a == "--bitrate") { if (i + 1 < args.Length) bitrate = args[++i]; }
                else if (a == "--no-trim") trim = false;
                else if (video == null) video = a;
                else if (audio == null) audio = a;
            }

            if (video == null || audio == null)
            {
                W("用法: 焕音.exe --cli <视频> <音频> [-o 输出] [-c alac|aac|pcm] [-b 320k] [--no-trim]");
                return 2;
            }
            if (!File.Exists(video) || !File.Exists(audio))
            {
                W("错误: 找不到输入文件");
                return 2;
            }

            string ffmpeg = FfmpegSetup.Locate();
            if (ffmpeg == null)
            {
                if (FfmpegSetup.HasEmbeddedPayload)
                {
                    W("正在解压内置 ffmpeg …");
                    FfmpegSetup.InstallFromEmbedded(delegate(string m, double f) { W(m); });
                    ffmpeg = FfmpegSetup.CacheFfmpeg;
                }
                if (ffmpeg == null || !File.Exists(ffmpeg))
                {
                    W("错误: 没有找到 ffmpeg.exe（可放在程序同目录，或用 GUI 的「自动下载」）");
                    return 3;
                }
            }

            MediaItem vi = new MediaItem();
            vi.Path = video;
            vi.Kind = MediaKind.Video;
            string err;
            MediaProbe.ProbeWithFfmpeg(vi, ffmpeg, out err, 30000);
            W("视频: " + Path.GetFileName(video) + "  " + vi.Width + "x" + vi.Height + " " + vi.VideoCodec +
              "  时长 " + vi.Duration.ToString("0.000") + "s");

            WavInfo wi = null;
            if (Path.GetExtension(audio).ToLowerInvariant() == ".wav") wi = MediaProbe.ReadWavHeader(audio);
            MediaItem ai = new MediaItem();
            ai.Path = audio;
            ai.Kind = MediaKind.Audio;
            MediaProbe.ProbeWithFfmpeg(ai, ffmpeg, out err, 30000);
            W("音频: " + Path.GetFileName(audio) + "  " + ai.AudioSampleRate + "Hz " + ai.AudioChannels + "ch " +
              ai.AudioCodec + "  时长 " + ai.Duration.ToString("0.000") + "s");

            JobRequest req = new JobRequest();
            req.VideoPath = video;
            req.AudioPath = audio;
            req.AudioCodec = codec;
            req.Bitrate = bitrate;
            req.KeepVideoLength = trim;
            req.VideoDuration = vi.Duration;
            req.SourceWav = wi;
            if (output != null) req.OutputPath = Path.GetFullPath(output);
            else req.OutputPath = JobRunner.BuildOutputPath(video, "_newaudio", null, req.OutputExt);

            W("");
            W("命令: " + JobRunner.BuildCommandLine(ffmpeg, req));
            W("");

            JobRunner.Callbacks cb = new JobRunner.Callbacks();
            cb.Log = delegate(string line)
            {
                if (line.IndexOf("frame=", StringComparison.Ordinal) < 0)
                    W("  " + line);
            };
            cb.Progress = delegate(double frac, string detail)
            {
                if (frac >= 0) Console.Error.Write("\r进度 " + (frac * 100).ToString("0") + "%  " + detail);
            };

            Stopwatch sw = Stopwatch.StartNew();
            JobResult res = JobRunner.Run(ffmpeg, req, cb);
            if (!res.Success && !res.Cancelled && req.AudioCodec == "alac" && req.OutputExt == "mp4" &&
                res.Error.IndexOf("codec alac", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                W("[提示] 当前 ffmpeg 不支持把 ALAC 封装进 MP4，自动改用 .mov（音频依然无损）");
                req.OverrideExt = "mov";
                if (output != null)
                    req.OutputPath = Path.ChangeExtension(req.OutputPath, ".mov");
                else
                    req.OutputPath = JobRunner.BuildOutputPath(video, "_newaudio", null, req.OutputExt);
                res = JobRunner.Run(ffmpeg, req, cb);
            }
            sw.Stop();
            Console.Error.WriteLine();

            if (!res.Success)
            {
                W("失败: " + res.Error);
                return 1;
            }
            W("完成: " + res.OutputPath);
            W("大小: " + MediaItem.FormatSize(res.OutputSize) + "   用时 " +
              sw.Elapsed.TotalSeconds.ToString("0.00") + "s");
            string v = JobRunner.VerifyOutput(ffmpeg, res.OutputPath);
            if (v.Length > 0) W("校验: " + v);
            return 0;
        }
    }
}
