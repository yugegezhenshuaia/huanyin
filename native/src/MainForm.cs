using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace HuanYin
{
    internal sealed partial class MainForm : Form
    {
        // ---------- 状态 ----------
        private readonly List<MediaItem> items = new List<MediaItem>();
        private readonly List<FileRow> rows = new List<FileRow>();
        private MediaItem currentVideo;
        private MediaItem currentAudio;
        private int nextId = 1;

        private string ffmpegPath;
        private bool busy;
        private volatile bool cancelRequested;
        private bool loadingSettings;
        private TaskbarProgress taskbar;

        // ---------- 控件 ----------
        private Label lblTitle;
        private Label lblSub;
        private Label lblVersion;

        private CardPanel banner;
        private Label lblBanner;
        private FlatButton btnDownload;
        private FlatButton btnPickFfmpeg;

        private DropZone drop;
        private RowList listPanel;
        private Label lblEmptyList;
        private Label lblSummary;
        private FlatButton btnClear;

        private CardPanel optCard;
        private Label lblCodecCap;
        private Segmented segCodec;
        private Label lblBitrateCap;
        private Segmented segBitrate;
        private Label lblOutCap;
        private Segmented segOut;
        private CardPanel outDirHost;
        private TextBox txtOutDir;
        private FlatButton btnBrowseOut;
        private DarkCheck chkKeepLen;
        private DarkCheck chkOpenFolder;
        private DarkCheck chkBatch;
        private DarkCheck chkLog;

        private FlatButton btnRun;
        private FlatButton btnCancel;
        private ThinProgress progress;
        private Label lblStatus;
        private Label lblPct;
        private CardPanel logCard;
        private LogView logView;

        private readonly Font fTitle = AppTheme.F(15f, FontStyle.Bold);
        private readonly Font fSub = AppTheme.F(9f, FontStyle.Regular);
        private readonly Font fSmall = AppTheme.F(8.5f, FontStyle.Regular);
        private readonly Font fCaption = AppTheme.F(8.75f, FontStyle.Regular);
        private readonly Font fBody = AppTheme.F(9f, FontStyle.Regular);

        public MainForm()
        {
            Text = "焕音 · MP4 视频换高音质音轨";
            // 所有尺寸都由 LayoutUi() 按 DPI 手工计算，关闭 WinForms 的自动缩放，
            // 否则每次 ResumeLayout 都会按字体比例二次缩放，窗口会越缩越小。
            AutoScaleMode = AutoScaleMode.None;
            BackColor = AppTheme.Bg;
            ForeColor = AppTheme.Txt;
            Font = fBody;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(AppTheme.S(860), AppTheme.S(700));
            ClientSize = new Size(AppTheme.S(920), AppTheme.S(780));
            DoubleBuffered = true;
            AllowDrop = true;
            KeyPreview = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            BuildUi();
            LoadSettings();
            LayoutUi();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.UseDarkTitleBar(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            taskbar = new TaskbarProgress(Handle);
            RefreshFfmpegState(true);
        }

        // =====================================================================
        //  构建界面
        // =====================================================================
        private void BuildUi()
        {
            lblTitle = new Label();
            lblTitle.Text = "焕音";
            lblTitle.Font = fTitle;
            lblTitle.ForeColor = AppTheme.Txt;
            lblTitle.BackColor = AppTheme.Bg;
            lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(lblTitle);

            lblVersion = new Label();
            lblVersion.Text = "v3.0 · 原生 Windows 版";
            lblVersion.Font = fSmall;
            lblVersion.ForeColor = AppTheme.Dim2;
            lblVersion.BackColor = AppTheme.Bg;
            lblVersion.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(lblVersion);

            lblSub = new Label();
            lblSub.Text = "视频流原样复制（不重压，画质无损）；音频换成 ALAC 无损 / AAC / PCM。全程本地处理，不联网、不上传。";
            lblSub.Font = fSub;
            lblSub.ForeColor = AppTheme.Dim;
            lblSub.BackColor = AppTheme.Bg;
            lblSub.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(lblSub);

            // ---- 缺少 ffmpeg 的提示条 ----
            banner = new CardPanel();
            banner.Visible = false;
            Controls.Add(banner);

            lblBanner = new Label();
            lblBanner.Text = "缺少 ffmpeg 组件，需要先安装一次。";
            lblBanner.Font = fBody;
            lblBanner.ForeColor = AppTheme.Warn;
            lblBanner.BackColor = AppTheme.Card;
            lblBanner.TextAlign = ContentAlignment.MiddleLeft;
            banner.Controls.Add(lblBanner);

            btnDownload = new FlatButton();
            btnDownload.Text = "自动下载";
            btnDownload.Kind = FlatButton.Style.Primary;
            btnDownload.Click += delegate { StartInstall(); };
            banner.Controls.Add(btnDownload);

            btnPickFfmpeg = new FlatButton();
            btnPickFfmpeg.Text = "选择本地 ffmpeg.exe";
            btnPickFfmpeg.Kind = FlatButton.Style.Secondary;
            btnPickFfmpeg.Click += delegate { PickFfmpegFile(); };
            banner.Controls.Add(btnPickFfmpeg);

            // ---- 拖放区 ----
            drop = new DropZone();
            drop.AllowDrop = true;
            drop.BrowseRequested += delegate { BrowseFiles(); };
            drop.DragEnter += DropDragEnter;
            drop.DragOver += DropDragOver;
            drop.DragLeave += delegate { drop.SetDragging(false); };
            drop.DragDrop += DropDragDrop;
            Controls.Add(drop);

            // ---- 文件列表 ----
            listPanel = new RowList();
            listPanel.AllowDrop = true;
            listPanel.DragEnter += DropDragEnter;
            listPanel.DragOver += DropDragOver;
            listPanel.DragLeave += delegate { drop.SetDragging(false); };
            listPanel.DragDrop += DropDragDrop;
            listPanel.ScrollChanged += delegate { RelayoutRows(); };
            Controls.Add(listPanel);

            lblEmptyList = new Label();
            lblEmptyList.Text = "还没有文件";
            lblEmptyList.Font = fBody;
            lblEmptyList.ForeColor = AppTheme.Dim2;
            lblEmptyList.BackColor = AppTheme.Bg;
            lblEmptyList.TextAlign = ContentAlignment.MiddleCenter;
            listPanel.Controls.Add(lblEmptyList);

            lblSummary = new Label();
            lblSummary.Text = "当前视频：未选择     当前音频：未选择";
            lblSummary.Font = fSub;
            lblSummary.ForeColor = AppTheme.Dim;
            lblSummary.BackColor = AppTheme.Bg;
            lblSummary.TextAlign = ContentAlignment.MiddleLeft;
            lblSummary.AutoEllipsis = true;
            Controls.Add(lblSummary);

            btnClear = new FlatButton();
            btnClear.Text = "清空列表";
            btnClear.Kind = FlatButton.Style.Ghost;
            btnClear.Font = fSmall;
            btnClear.Click += delegate { ClearAll(); };
            Controls.Add(btnClear);

            // ---- 选项卡片 ----
            optCard = new CardPanel();
            optCard.AllowDrop = true;
            optCard.DragEnter += DropDragEnter;
            optCard.DragOver += DropDragOver;
            optCard.DragDrop += DropDragDrop;
            Controls.Add(optCard);

            lblCodecCap = MakeCaption("音频编码", optCard);
            segCodec = new Segmented();
            segCodec.Items = new string[] { "ALAC 无损", "AAC 有损", "PCM 直封" };
            segCodec.SelectedIndexChanged += delegate { OnCodecChanged(); };
            optCard.Controls.Add(segCodec);

            lblBitrateCap = MakeCaption("AAC 码率", optCard);
            segBitrate = new Segmented();
            segBitrate.Items = new string[] { "128k", "192k", "256k", "320k" };
            segBitrate.SelectedIndex = 3;
            segBitrate.Compact = true;
            optCard.Controls.Add(segBitrate);

            lblOutCap = MakeCaption("输出位置", optCard);
            segOut = new Segmented();
            segOut.Items = new string[] { "视频同目录", "指定目录" };
            segOut.SelectedIndexChanged += delegate { OnOutModeChanged(); };
            optCard.Controls.Add(segOut);

            txtOutDir = new TextBox();
            txtOutDir.BorderStyle = BorderStyle.None;
            txtOutDir.BackColor = AppTheme.Field;
            txtOutDir.ForeColor = AppTheme.Dim;
            txtOutDir.Font = fSmall;
            txtOutDir.ReadOnly = true;
            txtOutDir.TabStop = false;
            txtOutDir.Dock = DockStyle.Fill;

            outDirHost = new CardPanel();
            outDirHost.FillColor = AppTheme.Field;
            outDirHost.BorderColor = AppTheme.Line;
            outDirHost.BackColor = AppTheme.Field;
            outDirHost.Padding = new Padding(AppTheme.S(9), AppTheme.S(6), AppTheme.S(9), AppTheme.S(6));
            outDirHost.Controls.Add(txtOutDir);
            optCard.Controls.Add(outDirHost);

            btnBrowseOut = new FlatButton();
            btnBrowseOut.Text = "浏览…";
            btnBrowseOut.Kind = FlatButton.Style.Secondary;
            btnBrowseOut.Font = fSmall;
            btnBrowseOut.Click += delegate { PickOutputDir(); };
            optCard.Controls.Add(btnBrowseOut);

            chkKeepLen = new DarkCheck();
            chkKeepLen.Text = "按视频长度对齐（截断 / 补静音）";
            chkKeepLen.Checked = true;
            optCard.Controls.Add(chkKeepLen);

            chkOpenFolder = new DarkCheck();
            chkOpenFolder.Text = "完成后打开文件夹";
            chkOpenFolder.Checked = true;
            optCard.Controls.Add(chkOpenFolder);

            chkBatch = new DarkCheck();
            chkBatch.Text = "批量处理所有视频";
            chkBatch.Visible = false;
            optCard.Controls.Add(chkBatch);

            chkLog = new DarkCheck();
            chkLog.Text = "显示日志";
            chkLog.Checked = true;
            chkLog.CheckedChanged += delegate { LayoutUi(); };
            optCard.Controls.Add(chkLog);

            // ---- 运行 / 取消 ----
            btnRun = new FlatButton();
            btnRun.Text = "替换音轨";
            btnRun.Kind = FlatButton.Style.Primary;
            btnRun.Font = AppTheme.F(10.5f, FontStyle.Bold);
            btnRun.Radius = AppTheme.S(10);
            btnRun.Enabled = false;
            btnRun.Click += delegate { StartRun(); };
            Controls.Add(btnRun);

            btnCancel = new FlatButton();
            btnCancel.Text = "取消";
            btnCancel.Kind = FlatButton.Style.Danger;
            btnCancel.Font = AppTheme.F(9.5f, FontStyle.Bold);
            btnCancel.Visible = false;
            btnCancel.Click += delegate { cancelRequested = true; SetStatus("正在取消…", AppTheme.Warn); };
            Controls.Add(btnCancel);

            progress = new ThinProgress();
            progress.Value = 0;
            Controls.Add(progress);

            lblPct = new Label();
            lblPct.Text = "";
            lblPct.Font = fSmall;
            lblPct.ForeColor = AppTheme.Dim;
            lblPct.BackColor = AppTheme.Bg;
            lblPct.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(lblPct);

            lblStatus = new Label();
            lblStatus.Text = "等待添加文件…";
            lblStatus.Font = fSmall;
            lblStatus.ForeColor = AppTheme.Dim;
            lblStatus.BackColor = AppTheme.Bg;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.AutoEllipsis = true;
            Controls.Add(lblStatus);
            lblStatus.BringToFront();

            logView = new LogView();
            logView.Dock = DockStyle.Fill;

            logCard = new CardPanel();
            logCard.FillColor = AppTheme.LogBg;
            logCard.BorderColor = AppTheme.Line;
            logCard.BackColor = AppTheme.LogBg;
            logCard.Padding = new Padding(AppTheme.S(8), AppTheme.S(6), AppTheme.S(2), AppTheme.S(6));
            logCard.Controls.Add(logView);
            Controls.Add(logCard);
        }

        private Label MakeCaption(string text, Control parent)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = fCaption;
            l.ForeColor = AppTheme.Dim;
            l.BackColor = AppTheme.Card;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.AutoSize = false;
            parent.Controls.Add(l);
            return l;
        }

        // =====================================================================
        //  布局
        // =====================================================================
        private void LayoutUi()
        {
            if (lblTitle == null) return;
            SuspendLayout();

            int pad = AppTheme.S(24);
            int w = ClientSize.Width - pad * 2;
            int y = pad;

            lblTitle.SetBounds(pad, y - AppTheme.S(2), AppTheme.S(240), AppTheme.S(26));
            lblVersion.SetBounds(pad + w - AppTheme.S(240), y, AppTheme.S(240), AppTheme.S(22));
            y += AppTheme.S(25);

            lblSub.SetBounds(pad, y, w, AppTheme.S(19));
            y += AppTheme.S(19) + AppTheme.S(12);

            // banner
            if (banner.Visible)
            {
                int bh = AppTheme.S(46);
                banner.SetBounds(pad, y, w, bh);
                int bx = AppTheme.S(14);
                int wDown = AppTheme.S(92);
                int wPick = AppTheme.S(160);
                btnDownload.SetBounds(banner.Width - wDown - wPick - AppTheme.S(24), AppTheme.S(8), wDown, bh - AppTheme.S(16));
                btnPickFfmpeg.SetBounds(banner.Width - wPick - AppTheme.S(12), AppTheme.S(8), wPick, bh - AppTheme.S(16));
                lblBanner.SetBounds(bx, AppTheme.S(2), banner.Width - wDown - wPick - AppTheme.S(46), bh - AppTheme.S(4));
                y += bh + AppTheme.S(12);
            }

            int dropH = AppTheme.S(92);
            drop.SetBounds(pad, y, w, dropH);
            y += dropH + AppTheme.S(14);

            // bottom block heights
            int summaryH = AppTheme.S(26);
            int optH = AppTheme.S(136);
            int runH = AppTheme.S(44);
            int progH = AppTheme.S(10);
            int statusH = AppTheme.S(18);
            int logH = chkLog.Checked ? AppTheme.S(126) : 0;
            int bottomPad = AppTheme.S(20);

            int bottomBlock = summaryH + AppTheme.S(8) + optH + AppTheme.S(14) + runH + AppTheme.S(16) +
                              progH + AppTheme.S(4) + statusH + AppTheme.S(10) + logH + bottomPad;

            int listH = ClientSize.Height - y - bottomBlock;
            if (listH < AppTheme.S(110)) listH = AppTheme.S(110);
            listPanel.SetBounds(pad, y, w, listH);
            y += listH + AppTheme.S(8);

            lblSummary.SetBounds(pad, y, w - AppTheme.S(100), summaryH);
            btnClear.SetBounds(pad + w - AppTheme.S(86), y + AppTheme.S(1), AppTheme.S(86), summaryH - AppTheme.S(2));
            y += summaryH + AppTheme.S(8);

            // ---- 选项卡片三行 ----
            optCard.SetBounds(pad, y, w, optH);
            int ix = AppTheme.S(16);
            int iw = w - AppTheme.S(32);
            int rowY = AppTheme.S(12);
            int segH = AppTheme.S(30);

            lblCodecCap.SetBounds(ix, rowY, AppTheme.S(60), segH);
            segCodec.SetBounds(ix + AppTheme.S(62), rowY, AppTheme.S(300), segH);
            lblBitrateCap.SetBounds(ix + AppTheme.S(378), rowY, AppTheme.S(60), segH);
            segBitrate.SetBounds(ix + AppTheme.S(440), rowY, iw - AppTheme.S(440), segH);

            rowY += segH + AppTheme.S(10);
            lblOutCap.SetBounds(ix, rowY, AppTheme.S(60), segH);
            segOut.SetBounds(ix + AppTheme.S(62), rowY, AppTheme.S(190), segH);
            int browseW = AppTheme.S(58);
            outDirHost.SetBounds(ix + AppTheme.S(260), rowY, iw - AppTheme.S(260) - browseW - AppTheme.S(8), segH);
            btnBrowseOut.SetBounds(ix + iw - browseW, rowY + AppTheme.S(2), browseW, segH - AppTheme.S(4));

            rowY += segH + AppTheme.S(12);
            int chkW = (iw - AppTheme.S(24)) / 4;
            chkKeepLen.SetBounds(ix, rowY + AppTheme.S(2), chkW, AppTheme.S(22));
            chkOpenFolder.SetBounds(ix + chkW, rowY + AppTheme.S(2), chkW, AppTheme.S(22));
            chkBatch.SetBounds(ix + chkW * 2, rowY + AppTheme.S(2), chkW, AppTheme.S(22));
            chkLog.SetBounds(ix + chkW * 3, rowY + AppTheme.S(2), chkW, AppTheme.S(22));

            y += optH + AppTheme.S(14);

            int cancelW = AppTheme.S(96);
            if (btnCancel.Visible)
            {
                btnRun.SetBounds(pad, y, w - cancelW - AppTheme.S(10), runH);
                btnCancel.SetBounds(pad + w - cancelW, y, cancelW, runH);
            }
            else
            {
                btnRun.SetBounds(pad, y, w, runH);
            }
            y += runH + AppTheme.S(16);

            progress.SetBounds(pad, y, w, progH);
            y += progH + AppTheme.S(4);
            int pctW = AppTheme.S(300);
            lblPct.SetBounds(pad + w - pctW, y, pctW, statusH);
            lblStatus.SetBounds(pad, y, w - pctW - AppTheme.S(12), statusH);
            y += statusH + AppTheme.S(10);

            if (chkLog.Checked)
            {
                logCard.Visible = true;
                logCard.SetBounds(pad, y, w, logH);
            }
            else
            {
                logCard.Visible = false;
            }

            RelayoutRows();
            ResumeLayout(true);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (lblTitle != null) LayoutUi();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (listPanel != null && listPanel.Visible && listPanel.Parent != null)
            {
                Point pt;
                try { pt = listPanel.PointToClient(PointToScreen(e.Location)); }
                catch { pt = e.Location; }
                if (listPanel.ClientRectangle.Contains(pt))
                {
                    listPanel.ScrollBy(-e.Delta * AppTheme.S(56) / 120);
                    return;
                }
            }
            base.OnMouseWheel(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5 && btnRun.Enabled) { StartRun(); e.Handled = true; return; }
            if (e.KeyCode == Keys.Escape && busy) { cancelRequested = true; e.Handled = true; return; }
            base.OnKeyDown(e);
        }

        /// <summary>Test hook: start a job without user input.</summary>
        public void DevRun()
        {
            if (installing || string.IsNullOrEmpty(ffmpegPath))
            {
                Timer t = new Timer();
                t.Interval = 700;
                t.Tick += delegate { t.Stop(); t.Dispose(); DevRun(); };
                t.Start();
                return;
            }
            if (btnRun.Enabled) StartRun();
        }

        private void RelayoutRows()
        {
            if (listPanel == null) return;
            int rowH = AppTheme.S(58);
            int gap = AppTheme.S(8);
            int w = listPanel.ClientSize.Width - AppTheme.S(14);
            if (w < AppTheme.S(120)) w = Math.Max(0, listPanel.ClientSize.Width - AppTheme.S(6));

            lblEmptyList.Visible = rows.Count == 0;
            if (rows.Count == 0)
                lblEmptyList.SetBounds(0, 0, listPanel.ClientSize.Width, Math.Max(AppTheme.S(70), listPanel.ClientSize.Height));

            int total = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].SetBounds(0, total - listPanel.ScrollOffset, w, rowH);
                total += rowH + gap;
            }
            listPanel.SetContent(rows.Count == 0 ? 0 : total);
        }

        // =====================================================================
        //  提示 / 状态
        // =====================================================================
        private void SetStatus(string text, Color color)
        {
            if (lblStatus == null) return;
            lblStatus.Text = text;
            lblStatus.ForeColor = color;
            lblStatus.Refresh();
        }

        private void SetStatus(string text) { SetStatus(text, AppTheme.Dim); }

        private void Log(string line)
        {
            if (logView == null) return;
            logView.AppendLine(line);
        }

        private void UiLog(string line)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke((MethodInvoker)delegate { UiLog(line); }); } catch { }
                return;
            }
            Log(line);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy)
            {
                DialogResult r = MessageBox.Show(this,
                    "还有任务正在处理，确定要退出吗？", "焕音",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                cancelRequested = true;
            }
            SaveSettings();
            base.OnFormClosing(e);
        }
    }
}
