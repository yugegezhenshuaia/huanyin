using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace HuanYin
{
    internal sealed partial class MainForm
    {
        private bool installing;
        private volatile bool installCancel;
        private string lastOutputDir = "";

        // ==================================================================
        //  ffmpeg 定位 / 安装
        // ==================================================================
        private void RefreshFfmpegState(bool autoInstallIfEmbedded)
        {
            string found = FfmpegSetup.Locate();
            if (found != null && File.Exists(found))
            {
                ffmpegPath = found;
                banner.Visible = false;
                LayoutUi();
                RequeueUnprobed();
                return;
            }

            ffmpegPath = null;
            if (autoInstallIfEmbedded && FfmpegSetup.HasEmbeddedPayload)
            {
                StartInstall();
                return;
            }

            banner.Visible = true;
            lblBanner.Text = "缺少 ffmpeg 组件：先安装一次（约 30 MB），之后完全离线使用。";
            btnDownload.Text = "自动下载";
            btnDownload.Enabled = true;
            LayoutUi();
            SetStatus("缺少 ffmpeg 组件，点击上方「自动下载」安装", AppTheme.Warn);
        }

        private void StartInstall()
        {
            if (installing) return;
            installing = true;
            installCancel = false;
            btnDownload.Enabled = false;
            btnPickFfmpeg.Enabled = false;
            btnRun.Enabled = false;
            lblBanner.Text = "正在准备 ffmpeg …";
            progress.Indeterminate = true;
            SetStatus("正在准备 ffmpeg 组件…", AppTheme.Acc);

            Thread t = new Thread(InstallWorker);
            t.IsBackground = true;
            t.Name = "ffmpeg-install";
            t.Start();
        }

        private void InstallWorker()
        {
            bool ok = false;
            string version = "";
            try
            {
                if (FfmpegSetup.HasEmbeddedPayload)
                {
                    ok = FfmpegSetup.InstallFromEmbedded(delegate(string msg, double frac)
                    {
                        InstallReport(msg, frac);
                    });
                }
                if (!ok)
                {
                    ok = FfmpegSetup.Download(
                        delegate(string msg) { InstallReport(msg, -1); },
                        delegate(double frac, string detail) { InstallProgress(frac, detail); },
                        delegate { return installCancel; });
                }
                if (ok)
                {
                    string p = FfmpegSetup.CacheFfmpeg;
                    version = FfmpegSetup.VersionText(p);
                }
            }
            catch (Exception ex)
            {
                InstallReport("安装失败：" + ex.Message, -1);
            }

            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    installing = false;
                    progress.Indeterminate = false;
                    progress.Value = 0;
                    btnDownload.Enabled = true;
                    btnPickFfmpeg.Enabled = true;

                    if (ok)
                    {
                        ffmpegPath = FfmpegSetup.CacheFfmpeg;
                        banner.Visible = false;
                        LayoutUi();
                        SetStatus("ffmpeg 已就绪：" + ShortVersion(version), AppTheme.Ok);
                        Log("[ffmpeg] " + version);
                        RefreshState();
                        RequeueUnprobed();
                    }
                    else if (!installCancel)
                    {
                        lblBanner.Text = "自动下载失败。可以点「选择本地 ffmpeg.exe」，或到 gyan.dev/ffmpeg/builds 下载后拖进本窗口。";
                        SetStatus("ffmpeg 安装失败，请手动指定 ffmpeg.exe", AppTheme.Err);
                        banner.Visible = true;
                        LayoutUi();
                        RefreshState();
                    }
                    else
                    {
                        lblBanner.Text = "已取消安装。";
                        SetStatus("已取消安装", AppTheme.Warn);
                    }
                });
            }
            catch { }
        }

        private void InstallReport(string msg, double frac)
        {
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    lblBanner.Text = msg;
                    SetStatus(msg, AppTheme.Acc);
                    if (frac >= 0) progress.Value = frac;
                });
            }
            catch { }
        }

        private void InstallProgress(double frac, string detail)
        {
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (frac >= 0) progress.Value = frac;
                    lblBanner.Text = "正在下载 ffmpeg … " + detail;
                    lblPct.Text = frac >= 0 ? (frac * 100.0).ToString("0.0") + "%" : "";
                });
            }
            catch { }
        }

        private static string ShortVersion(string v)
        {
            if (string.IsNullOrEmpty(v)) return "已安装";
            int i = v.IndexOf(" Copyright", StringComparison.OrdinalIgnoreCase);
            if (i > 0) v = v.Substring(0, i);
            return v.Trim();
        }

        private void PickFfmpegFile()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择 ffmpeg.exe";
                dlg.Filter = "ffmpeg.exe|ffmpeg.exe|可执行文件|*.exe";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                InstallFfmpegFromPath(dlg.FileName);
            }
        }

        private void InstallFfmpegFromPath(string path)
        {
            if (!MediaProbe.IsFfmpegBinary(path))
            {
                MessageBox.Show(this, "请选择 ffmpeg.exe 本身。", "焕音", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string err;
            SetStatus("正在校验 ffmpeg.exe …", AppTheme.Acc);
            Application.DoEvents();
            if (FfmpegSetup.InstallFromFile(path, FfmpegSetup.CacheFfmpeg, out err))
            {
                ffmpegPath = FfmpegSetup.CacheFfmpeg;
                banner.Visible = false;
                LayoutUi();
                SetStatus("ffmpeg 已就绪：" + ShortVersion(FfmpegSetup.VersionText(ffmpegPath)), AppTheme.Ok);
                Log("[ffmpeg] 使用 " + ffmpegPath);
                RequeueUnprobed();
            }
            else
            {
                SetStatus("ffmpeg 校验失败：" + err, AppTheme.Err);
                MessageBox.Show(this, "这个文件不能用作 ffmpeg：\n\n" + err, "焕音",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ==================================================================
        //  执行替换
        // ==================================================================
        private void StartRun()
        {
            if (busy) return;
            if (currentVideo == null || currentAudio == null) return;

            if (installing || (string.IsNullOrEmpty(ffmpegPath) || !File.Exists(ffmpegPath)))
            {
                if (installing)
                {
                    SetStatus("ffmpeg 正在安装，完成后会自动可用…", AppTheme.Warn);
                    return;
                }
                RefreshFfmpegState(true);
                if (installing)
                {
                    SetStatus("正在准备 ffmpeg，请稍候…", AppTheme.Warn);
                    return;
                }
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    MessageBox.Show(this, "还没有可用的 ffmpeg，请先点击上方「自动下载」。", "焕音",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            List<MediaItem> videos = new List<MediaItem>();
            if (chkBatch.Checked && chkBatch.Visible)
            {
                foreach (MediaItem it in items) if (it.Kind == MediaKind.Video) videos.Add(it);
            }
            else videos.Add(currentVideo);

            string outDir = "";
            if (segOut.SelectedIndex == 1)
            {
                outDir = txtOutDir.Text;
                if (string.IsNullOrEmpty(outDir) || !Directory.Exists(outDir))
                {
                    MessageBox.Show(this, "请先选择有效的输出目录。", "焕音", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    PickOutputDir();
                    return;
                }
            }

            busy = true;
            cancelRequested = false;
            btnCancel.Visible = true;
            btnCancel.Enabled = true;
            btnRun.Enabled = false;
            progress.Value = 0;
            progress.Indeterminate = false;
            lblPct.Text = "";
            logView.Clear();
            drop.Enabled = false;
            LayoutUi();

            MediaItem audio = currentAudio;
            string exe = ffmpegPath;
            Thread t = new Thread(delegate () { RunWorker(exe, videos, audio, outDir); });
            t.IsBackground = true;
            t.Name = "run";
            t.Start();
        }

        private void RunWorker(string exe, List<MediaItem> videos, MediaItem audio, string outDir)
        {
            int total = videos.Count;
            int index = 0;
            int success = 0;
            List<string> produced = new List<string>();
            string firstError = "";

            foreach (MediaItem video in videos)
            {
                if (cancelRequested) break;
                index++;

                string headerPrefix = total > 1 ? "(" + index + "/" + total + ") " : "";
                RunUi(delegate { SetStatus(headerPrefix + "准备： " + video.FileName, AppTheme.Acc); });

                // 补全视频时长
                if (video.Duration <= 0.05)
                {
                    RunUi(delegate { SetStatus(headerPrefix + "读取视频信息…", AppTheme.Acc); });
                    try
                    {
                        string err;
                        MediaProbe.ProbeWithFfmpeg(video, exe, out err, 20000);
                    }
                    catch { }
                }

                JobRequest req = new JobRequest();
                req.VideoPath = video.Path;
                req.AudioPath = audio.Path;
                req.AudioCodec = codecName();
                req.Bitrate = segBitrate.SelectedItem ?? "320k";
                req.KeepVideoLength = chkKeepLen.Checked;
                req.VideoDuration = video.Duration;
                req.SourceWav = audio.Wav;
                req.OutputPath = JobRunner.BuildOutputPath(video.Path, "_newaudio", outDir, req.OutputExt);

                RunUi(delegate { Log(""); Log("▶ " + JobRunner.BuildCommandLine(exe, req)); });
                RunUi(delegate { SetStatus(headerPrefix + "正在封装（视频流复制，音频编码）…", AppTheme.Acc); });

                JobRunner.Callbacks cb = new JobRunner.Callbacks();
                cb.Log = delegate(string line) { RunUi(delegate { Log(line); }); };
                cb.IsCancelled = delegate { return cancelRequested; };
                cb.Progress = delegate(double frac, string detail)
                {
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate { ApplyProgress(frac, headerPrefix + detail); });
                    }
                    catch { }
                };

                JobResult res = JobRunner.Run(exe, req, cb);

                // 旧版 ffmpeg 不支持把 ALAC 封装进 MP4，自动改用 .mov（同样无损）
                if (!res.Success && !res.Cancelled && req.AudioCodec == "alac" && req.OutputExt == "mp4" &&
                    res.Error.IndexOf("codec alac", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    RunUi(delegate
                    {
                        Log("[提示] 当前 ffmpeg 不支持把 ALAC 封装进 MP4，自动改用 .mov 容器（音频依然无损）。");
                        SetStatus(headerPrefix + "改用 .mov 容器重试…", AppTheme.Warn);
                    });
                    req.OverrideExt = "mov";
                    req.OutputPath = JobRunner.BuildOutputPath(video.Path, "_newaudio", outDir, req.OutputExt);
                    res = JobRunner.Run(exe, req, cb);
                }

                if (res.Cancelled)
                {
                    RunUi(delegate { SetStatus("已取消", AppTheme.Warn); });
                    break;
                }
                if (!res.Success)
                {
                    if (firstError.Length == 0) firstError = res.Error;
                    RunUi(delegate
                    {
                        SetStatus(headerPrefix + "失败了： " + FirstLine(res.Error), AppTheme.Err);
                        Log("[失败] " + res.Error);
                    });
                    continue;
                }

                success++;
                produced.Add(res.OutputPath);
                string summary = "";
                try { summary = JobRunner.VerifyOutput(exe, res.OutputPath); } catch { }
                long size = res.OutputSize;
                double secs = res.ElapsedSeconds;
                string outPath = res.OutputPath;

                RunUi(delegate
                {
                    ApplyProgress(1.0, "");
                    Log("[完成] " + outPath + "  (" + MediaItem.FormatSize(size) + ", 用时 " +
                        secs.ToString("0.0") + " 秒)");
                    if (summary.Length > 0) Log("[校验] " + summary);
                    SetStatus(headerPrefix + "完成：" + Path.GetFileName(outPath) + "  ·  " + MediaItem.FormatSize(size),
                        AppTheme.Ok);
                });
            }

            string finalMsg;
            Color color;
            if (cancelRequested)
            {
                finalMsg = "已取消";
                color = AppTheme.Warn;
            }
            else if (success == total && total > 0)
            {
                finalMsg = total > 1 ? "全部完成 ✅ 共 " + success + " 个文件" : "完成 ✅ 视频流未重压，画质无损";
                color = AppTheme.Ok;
            }
            else if (success > 0)
            {
                finalMsg = "部分完成：" + success + "/" + total + " 成功";
                color = AppTheme.Warn;
            }
            else
            {
                finalMsg = "失败：" + FirstLine(firstError);
                color = AppTheme.Err;
            }

            string lastDir = produced.Count > 0 ? Path.GetDirectoryName(produced[produced.Count - 1]) : "";

            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    busy = false;
                    cancelRequested = false;
                    btnCancel.Visible = false;
                    drop.Enabled = true;
                    progress.Indeterminate = false;
                    if (color == AppTheme.Err) progress.Value = 0;
                    lblPct.Text = "";
                    SetStatus(finalMsg, color);
                    RefreshState();
                    LayoutUi();
                    if (taskbar != null)
                    {
                        taskbar.SetState(color == AppTheme.Err ? TaskbarProgressState.Error :
                                         color == AppTheme.Ok ? TaskbarProgressState.NoProgress : TaskbarProgressState.Paused);
                        if (color == AppTheme.Err) taskbar.SetValue(1);
                    }

                    if (produced.Count > 0)
                    {
                        lastOutputDir = lastDir;
                        SetStatus(finalMsg + "   ·   " + Path.GetDirectoryName(produced[0]), color);
                        if (chkOpenFolder.Checked) OpenFolder(lastDir);
                    }
                });
            }
            catch { }
        }

        private void ApplyProgress(double frac, string detail)
        {
            if (frac >= 0)
            {
                progress.Indeterminate = false;
                progress.Value = frac;
                lblPct.Text = (frac * 100.0).ToString("0") + "%" + (detail.Length > 0 ? "   " + detail : "");
                if (taskbar != null)
                {
                    taskbar.SetState(TaskbarProgressState.Normal);
                    taskbar.SetValue(frac);
                }
            }
            else
            {
                progress.Indeterminate = true;
                lblPct.Text = detail;
                if (taskbar != null)
                {
                    taskbar.SetState(TaskbarProgressState.Indeterminate);
                }
            }
        }

        private void RunUi(MethodInvoker action)
        {
            try
            {
                if (IsHandleCreated) BeginInvoke(action);
            }
            catch { }
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "未知错误";
            int i = s.IndexOf('\n');
            if (i > 0) s = s.Substring(0, i);
            s = s.Trim();
            if (s.Length > 160) s = s.Substring(0, 160) + "…";
            return s;
        }

        private void OpenFolder(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch { }
        }
    }
}
