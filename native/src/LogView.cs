using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HuanYin
{
    /// <summary>Read-only, dark, monospace log with a slim custom scrollbar.</summary>
    internal sealed class LogView : DarkControl
    {
        private readonly List<string> raw = new List<string>();
        private readonly List<string> wrapped = new List<string>();
        private int scroll;
        private bool barHover;
        private readonly Font mono = AppTheme.Mono(8f);
        private int lineH;
        private int charW;

        private const int MaxRawLines = 1500;

        public LogView()
        {
            BackColor = AppTheme.LogBg;
            Cursor = Cursors.IBeam;
            lineH = Math.Max(10, TextRenderer.MeasureText("A", mono).Height);
            charW = Math.Max(4, TextRenderer.MeasureText("M", mono).Width);

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.BackColor = AppTheme.Card;
            menu.ForeColor = AppTheme.Txt;
            menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors());
            ToolStripMenuItem copy = new ToolStripMenuItem("复制全部日志");
            copy.Click += delegate { CopyAll(); };
            ToolStripMenuItem clear = new ToolStripMenuItem("清空日志");
            clear.Click += delegate { Clear(); };
            menu.Items.Add(copy);
            menu.Items.Add(clear);
            ContextMenuStrip = menu;
        }

        private sealed class DarkMenuColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected { get { return AppTheme.CardHover; } }
            public override Color MenuItemBorder { get { return AppTheme.Acc; } }
            public override Color ToolStripDropDownBackground { get { return AppTheme.Card; } }
            public override Color ImageMarginGradientBegin { get { return AppTheme.Card; } }
            public override Color ImageMarginGradientMiddle { get { return AppTheme.Card; } }
            public override Color ImageMarginGradientEnd { get { return AppTheme.Card; } }
        }

        public void Clear()
        {
            raw.Clear();
            wrapped.Clear();
            scroll = 0;
            Invalidate();
        }

        public void AppendLine(string text)
        {
            if (text == null) return;
            bool atBottom = scroll >= MaxScroll;
            string[] parts = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string p in parts)
            {
                raw.Add(p);
                WrapInto(p, wrapped);
            }
            if (raw.Count > MaxRawLines)
            {
                int drop = raw.Count - MaxRawLines;
                raw.RemoveRange(0, drop);
                RebuildWrapped();
            }
            if (atBottom) scroll = MaxScroll;
            Invalidate();
        }

        private void WrapInto(string s, List<string> target)
        {
            int maxChars = Math.Max(20, (Width - AppTheme.S(18)) / charW);
            if (s.Length <= maxChars) { target.Add(s); return; }
            int i = 0;
            while (i < s.Length)
            {
                int len = Math.Min(maxChars, s.Length - i);
                target.Add(s.Substring(i, len));
                i += len;
            }
        }

        private void RebuildWrapped()
        {
            wrapped.Clear();
            foreach (string s in raw) WrapInto(s, wrapped);
            ClampScroll();
        }

        private int LineHeight { get { return lineH + AppTheme.S(2); } }

        private int MaxScroll
        {
            get { return Math.Max(0, wrapped.Count * LineHeight - ClientSize.Height + AppTheme.S(6)); }
        }

        private void ClampScroll()
        {
            if (scroll > MaxScroll) scroll = MaxScroll;
            if (scroll < 0) scroll = 0;
        }

        private void CopyAll()
        {
            try
            {
                if (raw.Count == 0) return;
                Clipboard.SetText(string.Join("\r\n", raw.ToArray()));
            }
            catch { }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RebuildWrapped();
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int before = scroll;
            scroll -= e.Delta * LineHeight * 3 / 120;
            ClampScroll();
            if (before != scroll) Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool h = MaxScroll > 0 && e.X >= Width - AppTheme.S(12);
            if (h != barHover) { barHover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (barHover) { barHover = false; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(ParentBg());
            if (wrapped.Count == 0) return;

            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int lh = LineHeight;
            int first = Math.Max(0, scroll / lh);
            int last = Math.Min(wrapped.Count - 1, first + ClientSize.Height / lh + 1);
            int textW = Width - (MaxScroll > 0 ? AppTheme.S(16) : AppTheme.S(4));

            for (int i = first; i <= last; i++)
            {
                int y = i * lh - scroll;
                if (y > ClientSize.Height) break;
                Rectangle r = new Rectangle(0, y, textW, lh);
                Color c = LineColor(wrapped[i]);
                TextRenderer.DrawText(g, wrapped[i], mono, r, c,
                    TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }

            if (MaxScroll > 0)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int barW = barHover ? AppTheme.S(7) : AppTheme.S(5);
                int x = Width - barW - AppTheme.S(4);
                int pad = AppTheme.S(2);
                int trackH = ClientSize.Height - pad * 2;
                double view = (double)ClientSize.Height / Math.Max(1, wrapped.Count * lh);
                int thumbH = Math.Max(AppTheme.S(26), (int)(ClientSize.Height * view));
                if (thumbH > trackH) thumbH = trackH;
                double frac = MaxScroll > 0 ? (double)scroll / MaxScroll : 0;
                int thumbY = pad + (int)Math.Round((trackH - thumbH) * frac);
                using (GraphicsPath p = AppTheme.RoundRect(new Rectangle(x, thumbY, barW, thumbH), barW / 2))
                using (SolidBrush b = new SolidBrush(barHover ? AppTheme.LineSoft : Color.FromArgb(46, 56, 68)))
                    g.FillPath(b, p);
            }
        }

        private static Color LineColor(string line)
        {
            if (line.StartsWith("[完成]") || line.StartsWith("[校验]")) return Color.FromArgb(140, 214, 160);
            if (line.StartsWith("[失败]") || line.StartsWith("[error]")) return Color.FromArgb(239, 120, 117);
            if (line.StartsWith("[ffmpeg]") || line.StartsWith("[init]")) return Color.FromArgb(120, 190, 230);
            if (line.StartsWith("▶")) return Color.FromArgb(150, 165, 185);
            return Color.FromArgb(122, 134, 150);
        }
    }
}
