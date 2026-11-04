using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace HuanYin
{
    internal sealed partial class MainForm
    {
        private static string ConfigPath
        {
            get { return Path.Combine(FfmpegSetup.HomeDir, "config.ini"); }
        }

        private void LoadSettings()
        {
            loadingSettings = true;
            try { LoadSettingsCore(); }
            finally { loadingSettings = false; }
            OnCodecChanged();
            OnOutModeChangedQuiet();
            RefreshState();
        }

        private void LoadSettingsCore()
        {
            Dictionary<string, string> kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(ConfigPath))
                {
                    foreach (string line in File.ReadAllLines(ConfigPath, Encoding.UTF8))
                    {
                        string s = line.Trim();
                        if (s.Length == 0 || s.StartsWith("#")) continue;
                        int i = s.IndexOf('=');
                        if (i <= 0) continue;
                        kv[s.Substring(0, i).Trim()] = s.Substring(i + 1).Trim();
                    }
                }
            }
            catch { }

            segCodec.SelectedIndex = Int(kv, "codec", 0);
            segBitrate.SelectedIndex = Int(kv, "bitrate", 3);
            chkKeepLen.Checked = Bool(kv, "keepLen", true);
            chkOpenFolder.Checked = Bool(kv, "openFolder", true);
            chkBatch.Checked = Bool(kv, "batch", false);
            chkLog.Checked = Bool(kv, "showLog", true);
            segOut.SelectedIndex = Int(kv, "outMode", 0);
            txtOutDir.Text = Str(kv, "outDir", "");
            lastBrowseDir = Str(kv, "lastDir", "");
            lastOutputDir = Str(kv, "lastOutDir", "");

            int w = Int(kv, "winW", 0), h = Int(kv, "winH", 0);
            int x = Int(kv, "winX", int.MinValue), y = Int(kv, "winY", int.MinValue);
            if (w >= MinimumSize.Width && h >= MinimumSize.Height)
            {
                Rectangle screen = Screen.PrimaryScreen.WorkingArea;
                if (x != int.MinValue && y != int.MinValue &&
                    x > -w + 100 && y > -10 && x < screen.Right - 100 && y < screen.Bottom - 100)
                {
                    StartPosition = FormStartPosition.Manual;
                    SetBounds(x, y, w, h);
                }
                else
                {
                    ClientSize = new Size(w, h);
                }
            }
            if (Bool(kv, "maximized", false)) WindowState = FormWindowState.Maximized;
        }

        private void OnOutModeChangedQuiet()
        {
            txtOutDir.Enabled = segOut.SelectedIndex == 1;
        }

        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(FfmpegSetup.HomeDir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 焕音 配置");
                sb.AppendLine("codec=" + segCodec.SelectedIndex);
                sb.AppendLine("bitrate=" + segBitrate.SelectedIndex);
                sb.AppendLine("keepLen=" + (chkKeepLen.Checked ? 1 : 0));
                sb.AppendLine("openFolder=" + (chkOpenFolder.Checked ? 1 : 0));
                sb.AppendLine("batch=" + (chkBatch.Checked ? 1 : 0));
                sb.AppendLine("showLog=" + (chkLog.Checked ? 1 : 0));
                sb.AppendLine("outMode=" + segOut.SelectedIndex);
                sb.AppendLine("outDir=" + txtOutDir.Text);
                sb.AppendLine("lastDir=" + (lastBrowseDir ?? ""));
                sb.AppendLine("lastOutDir=" + (lastOutputDir ?? ""));
                sb.AppendLine("maximized=" + (WindowState == FormWindowState.Maximized ? 1 : 0));
                Rectangle b = (WindowState == FormWindowState.Normal) ? Bounds : RestoreBounds;
                sb.AppendLine("winW=" + b.Width);
                sb.AppendLine("winH=" + b.Height);
                sb.AppendLine("winX=" + b.X);
                sb.AppendLine("winY=" + b.Y);
                File.WriteAllText(ConfigPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        private static string Str(Dictionary<string, string> kv, string key, string def)
        {
            string v;
            if (kv.TryGetValue(key, out v)) return v;
            return def;
        }

        private static int Int(Dictionary<string, string> kv, string key, int def)
        {
            string v;
            if (kv.TryGetValue(key, out v))
            {
                int n;
                if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            }
            return def;
        }

        private static bool Bool(Dictionary<string, string> kv, string key, bool def)
        {
            string v;
            if (kv.TryGetValue(key, out v)) return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
            return def;
        }

        /// <summary>Files passed on the command line (drag files onto the exe).</summary>
        public void AddPathsOnStartup(string[] paths)
        {
            try { AddFiles(paths); }
            catch { }
        }
    }
}
