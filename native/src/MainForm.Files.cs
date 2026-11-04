using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace HuanYin
{
    internal sealed partial class MainForm
    {
        private readonly BlockingCollection<MediaItem> probeQueue = new BlockingCollection<MediaItem>();
        private bool probeWorkerStarted;
        private string lastBrowseDir;

        // ------------------------------------------------------------------
        //  打开文件对话框
        // ------------------------------------------------------------------
        private void BrowseFiles()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择视频和音频（可多选）";
                dlg.Multiselect = true;
                dlg.Filter =
                    "视频或音频文件|*.mp4;*.m4v;*.mov;*.mkv;*.webm;*.avi;*.ts;*.wav;*.flac;*.mp3;*.m4a;*.aac;*.ogg;*.opus" +
                    "|视频文件|*.mp4;*.m4v;*.mov;*.mkv;*.webm;*.avi;*.ts" +
                    "|音频文件|*.wav;*.flac;*.mp3;*.m4a;*.aac;*.ogg;*.opus" +
                    "|所有文件|*.*";
                dlg.FilterIndex = 1;
                if (!string.IsNullOrEmpty(lastBrowseDir) && Directory.Exists(lastBrowseDir))
                    dlg.InitialDirectory = lastBrowseDir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                lastBrowseDir = Path.GetDirectoryName(dlg.FileNames[0]);
                AddFiles(dlg.FileNames);
            }
        }

        // ------------------------------------------------------------------
        //  添加文件
        // ------------------------------------------------------------------
        private void AddFiles(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            List<MediaItem> added = new List<MediaItem>();
            List<string> rejected = new List<string>();

            foreach (string raw in paths)
            {
                string path = raw;
                if (string.IsNullOrEmpty(path)) continue;
                if (MediaProbe.IsFfmpegBinary(path))
                {
                    InstallFfmpegFromPath(path);
                    continue;
                }
                if (!File.Exists(path)) continue;
                if (Directory.Exists(path)) continue;

                bool dup = false;
                foreach (MediaItem exist in items)
                {
                    if (string.Equals(exist.Path, path, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                }
                if (dup) continue;

                MediaKind kind = MediaProbe.DetectKind(path);
                if (kind == MediaKind.Unknown)
                {
                    rejected.Add(Path.GetFileName(path));
                    continue;
                }

                MediaItem it = new MediaItem();
                it.Id = nextId++;
                it.Path = path;
                it.Kind = kind;
                try { it.Size = new FileInfo(path).Length; } catch { it.Size = 0; }
                items.Add(it);
                added.Add(it);

                if (kind == MediaKind.Audio && Path.GetExtension(path).ToLowerInvariant() == ".wav")
                {
                    WavInfo wi = MediaProbe.ReadWavHeader(path);
                    if (wi != null)
                    {
                        it.Wav = wi;
                        it.Duration = wi.Duration;
                        it.AudioSampleRate = wi.SampleRate;
                        it.AudioChannels = wi.Channels;
                        it.AudioCodec = "pcm";
                    }
                }
            }

            if (added.Count == 0)
            {
                if (rejected.Count > 0)
                    SetStatus("无法识别的文件类型：" + string.Join("、", rejected.ToArray()), AppTheme.Warn);
                else
                    SetStatus("这些文件已经在列表里了", AppTheme.Warn);
                return;
            }

            // 自动设置当前视频 / 音频
            foreach (MediaItem it in added)
            {
                if (it.Kind == MediaKind.Video && currentVideo == null) currentVideo = it;
                else if (it.Kind == MediaKind.Audio && currentAudio == null) currentAudio = it;
            }

            RebuildRows();
            RefreshState();
            SetStatus("已添加 " + added.Count + " 个文件，点击列表中的文件可切换「当前视频 / 当前音频」");

            foreach (MediaItem it in added) probeQueue.Add(it);
            EnsureProbeWorker();
        }

        private void EnsureProbeWorker()
        {
            if (probeWorkerStarted) return;
            probeWorkerStarted = true;
            Thread t = new Thread(ProbeWorker);
            t.IsBackground = true;
            t.Name = "probe";
            t.Start();
        }

        private void ProbeWorker()
        {
            foreach (MediaItem it in probeQueue.GetConsumingEnumerable())
            {
                try
                {
                    if (!items.Contains(it)) continue;
                    bool needFfmpeg = it.Wav == null || it.Duration <= 0 || (it.Kind == MediaKind.Video && it.Width == 0);
                    string exe = ffmpegPath;
                    if (needFfmpeg && !string.IsNullOrEmpty(exe))
                    {
                        string err;
                        MediaProbe.ProbeWithFfmpeg(it, exe, out err, 20000);
                    }
                }
                catch { }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        foreach (FileRow row in rows) if (row.Item == it) row.Invalidate();
                        RefreshState();
                    });
                }
                catch { }
            }
        }

        private void RequeueUnprobed()
        {
            bool any = false;
            foreach (MediaItem it in items)
            {
                if (!it.Probed && it.Wav == null) { probeQueue.Add(it); any = true; }
                else if (it.Kind == MediaKind.Video && it.Width == 0) { probeQueue.Add(it); any = true; }
            }
            if (any) EnsureProbeWorker();
        }

        // ------------------------------------------------------------------
        //  行重建 / 选中
        // ------------------------------------------------------------------
        private void RebuildRows()
        {
            foreach (FileRow r in rows)
            {
                r.RowClicked -= RowClickedHandler;
                r.RemoveClicked -= RowRemoveHandler;
                listPanel.Controls.Remove(r);
                r.Dispose();
            }
            rows.Clear();

            foreach (MediaItem it in items)
            {
                FileRow row = new FileRow(it);
                row.IsCurrentVideo = currentVideo != null && currentVideo.Id == it.Id;
                row.IsCurrentAudio = currentAudio != null && currentAudio.Id == it.Id;
                row.RowClicked += RowClickedHandler;
                row.RemoveClicked += RowRemoveHandler;
                rows.Add(row);
                listPanel.Controls.Add(row);
                row.BringToFront();
            }
            lblEmptyList.BringToFront();
            RelayoutRows();
        }

        private void RowClickedHandler(object sender, EventArgs e)
        {
            FileRow row = sender as FileRow;
            if (row == null) return;
            SelectItem(row.Item);
        }

        private void RowRemoveHandler(object sender, EventArgs e)
        {
            FileRow row = sender as FileRow;
            if (row == null) return;
            RemoveItem(row.Item);
        }

        private void SelectItem(MediaItem it)
        {
            if (busy) return;
            if (it.Kind == MediaKind.Video) currentVideo = it;
            else if (it.Kind == MediaKind.Audio) currentAudio = it;
            else return;
            RefreshState();
        }

        private void RemoveItem(MediaItem it)
        {
            if (busy) return;
            if (currentVideo == it) currentVideo = null;
            if (currentAudio == it) currentAudio = null;
            items.Remove(it);
            RebuildRows();
            RefreshState();
        }

        private void ClearAll()
        {
            if (busy) return;
            items.Clear();
            currentVideo = null;
            currentAudio = null;
            RebuildRows();
            RefreshState();
            SetStatus("列表已清空");
        }

        // ------------------------------------------------------------------
        //  刷新状态
        // ------------------------------------------------------------------
        private void RefreshState()
        {
            foreach (FileRow r in rows)
            {
                bool v = currentVideo != null && currentVideo.Id == r.Item.Id;
                bool a = currentAudio != null && currentAudio.Id == r.Item.Id;
                if (r.IsCurrentVideo != v || r.IsCurrentAudio != a)
                {
                    r.IsCurrentVideo = v;
                    r.IsCurrentAudio = a;
                    r.Invalidate();
                }
            }

            string vName = currentVideo != null ? currentVideo.FileName : "未选择";
            string aName = currentAudio != null ? currentAudio.FileName : "未选择";
            lblSummary.Text = "当前视频：" + vName + "     当前音频：" + aName;

            int videoCount = 0;
            foreach (MediaItem it in items) if (it.Kind == MediaKind.Video) videoCount++;
            chkBatch.Visible = videoCount > 1;

            bool canRun = !busy && currentVideo != null && currentAudio != null;
            btnRun.Enabled = canRun;
            btnRun.Text = "替换音轨";

            if (!busy)
            {
                if (currentVideo == null && currentAudio == null) SetStatus("等待添加文件…");
                else if (currentVideo == null) SetStatus("还需要一个视频文件（点击列表里的视频设为「当前视频」）", AppTheme.Warn);
                else if (currentAudio == null) SetStatus("还需要一个音频文件（点击列表里的音频设为「当前音频」）", AppTheme.Warn);
                else SetStatus(ReadySummary(), AppTheme.Dim);
            }

            // 音频信息提示
            if (currentAudio != null && currentAudio.Wav != null && codecName() == "aac" && currentAudio.Wav.Bits > 16)
                SetStatus("提示：源音频是 " + currentAudio.Wav.Bits + "bit，AAC 会降为有损 16bit，想要无损请选 ALAC", AppTheme.Warn);
        }

        private string ReadySummary()
        {
            if (currentAudio == null || currentVideo == null) return "就绪";
            string codec = codecName();
            string target = "ALAC 无损";
            if (codec == "aac") target = "AAC " + (segBitrate.SelectedItem ?? "320k");
            else if (codec == "pcm") target = "PCM 直封（输出 MOV）";
            string len = chkKeepLen.Checked ? "，按视频长度对齐" : "";
            return "就绪：视频流复制 + 音频→" + target + len;
        }

        private string codecName()
        {
            int i = segCodec.SelectedIndex;
            if (i == 1) return "aac";
            if (i == 2) return "pcm";
            return "alac";
        }

        private void OnCodecChanged()
        {
            if (loadingSettings)
            {
                lblBitrateCap.Visible = codecName() == "aac";
                segBitrate.Visible = codecName() == "aac";
                return;
            }
            bool aac = codecName() == "aac";
            lblBitrateCap.Visible = aac;
            segBitrate.Visible = aac;
            if (currentAudio != null && currentAudio.Wav != null && aac && currentAudio.Wav.Bits > 16)
                SetStatus("提示：AAC 会降为有损 16bit；要保真请选 ALAC", AppTheme.Warn);
            else
                SetStatus(ReadySummary());
        }

        private void OnOutModeChanged()
        {
            if (loadingSettings)
            {
                txtOutDir.Enabled = segOut.SelectedIndex == 1;
                return;
            }
            bool custom = segOut.SelectedIndex == 1;
            txtOutDir.Enabled = custom;
            if (custom && string.IsNullOrEmpty(txtOutDir.Text)) PickOutputDir();
            RefreshState();
        }

        private void PickOutputDir()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择输出文件夹";
                dlg.ShowNewFolderButton = true;
                if (!string.IsNullOrEmpty(txtOutDir.Text) && Directory.Exists(txtOutDir.Text))
                    dlg.SelectedPath = txtOutDir.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    txtOutDir.Text = dlg.SelectedPath;
                    segOut.SelectedIndex = 1;
                }
                else if (string.IsNullOrEmpty(txtOutDir.Text))
                {
                    segOut.SelectedIndex = 0;
                }
            }
        }

        // ------------------------------------------------------------------
        //  拖放
        // ------------------------------------------------------------------
        private void DropDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                drop.SetDragging(true);
            }
            else e.Effect = DragDropEffects.None;
        }

        private void DropDragOver(object sender, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
                drop.SetDragging(true);
            }
            else e.Effect = DragDropEffects.None;
        }

        private void DropDragDrop(object sender, DragEventArgs e)
        {
            drop.SetDragging(false);
            if (busy)
            {
                SetStatus("正在处理中，稍后再添加文件", AppTheme.Warn);
                return;
            }
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null) return;
            AddFiles(paths);
        }
    }
}
