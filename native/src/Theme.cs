using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace HuanYin
{
    internal static class AppTheme
    {
        public static readonly Color Bg = Color.FromArgb(15, 18, 22);
        public static readonly Color BgSoft = Color.FromArgb(20, 25, 31);
        public static readonly Color Card = Color.FromArgb(23, 28, 35);
        public static readonly Color CardHover = Color.FromArgb(27, 35, 45);
        public static readonly Color Line = Color.FromArgb(38, 45, 55);
        public static readonly Color LineSoft = Color.FromArgb(48, 58, 71);
        public static readonly Color Txt = Color.FromArgb(231, 237, 245);
        public static readonly Color Dim = Color.FromArgb(139, 151, 168);
        public static readonly Color Dim2 = Color.FromArgb(96, 107, 122);
        public static readonly Color Acc = Color.FromArgb(79, 195, 247);
        public static readonly Color Acc2 = Color.FromArgb(41, 182, 246);
        public static readonly Color AccDeep = Color.FromArgb(4, 34, 47);
        public static readonly Color Ok = Color.FromArgb(102, 187, 106);
        public static readonly Color Err = Color.FromArgb(239, 83, 80);
        public static readonly Color Warn = Color.FromArgb(255, 183, 77);
        public static readonly Color Field = Color.FromArgb(13, 17, 22);
        public static readonly Color LogBg = Color.FromArgb(10, 13, 17);
        public static readonly Color ProgressTrack = Color.FromArgb(30, 38, 48);

        public static float DpiScale = 1f;

        public static int S(int v) { return (int)Math.Round(v * DpiScale); }
        public static float SF(float v) { return v * DpiScale; }

        private static string uiFamily = "Microsoft YaHei UI";

        public static void InitDpi()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                DpiScale = g.DpiX / 96f;
            }
            if (DpiScale <= 0f) DpiScale = 1f;
            if (DpiScale > 4f) DpiScale = 4f;
            try
            {
                using (Font f = new Font(uiFamily, 9f))
                {
                    if (!string.Equals(f.FontFamily.Name, uiFamily, StringComparison.OrdinalIgnoreCase))
                        uiFamily = "Segoe UI";
                }
            }
            catch { uiFamily = "Segoe UI"; }
        }

        public static Font F(float pt, FontStyle style)
        {
            try { return new Font(uiFamily, pt, style, GraphicsUnit.Point); }
            catch { return new Font(FontFamily.GenericSansSerif, pt, style, GraphicsUnit.Point); }
        }

        public static Font Mono(float pt)
        {
            try { return new Font("Consolas", pt, FontStyle.Regular, GraphicsUnit.Point); }
            catch { return new Font(FontFamily.GenericMonospace, pt, FontStyle.Regular, GraphicsUnit.Point); }
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            if (radius <= 0)
            {
                p.AddRectangle(r);
                return p;
            }
            d = Math.Max(4, d);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color Mix(Color a, Color b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)Math.Round(a.A + (b.A - a.A) * t),
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static void DrawGlowText(Graphics g, string text, Font font, Color color, Point pt)
        {
            TextRenderer.DrawText(g, text, font, pt, color, TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Base for custom painted controls: inherits the parent background.</summary>
    internal abstract class DarkControl : Control
    {
        protected DarkControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected Color ParentBg()
        {
            try
            {
                if (Parent != null) return Parent.BackColor;
            }
            catch { }
            return AppTheme.Bg;
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            BackColor = ParentBg();
            Invalidate();
        }
    }

    /// <summary>Scrollable container with a slim custom scrollbar.</summary>
    internal sealed class RowList : Panel
    {
        private int contentH;
        private int scroll;
        private bool barHover;

        public event EventHandler ScrollChanged;

        public RowList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = AppTheme.Bg;
            TabStop = false;
        }

        public int ScrollOffset { get { return scroll; } }

        private int MaxScroll { get { return Math.Max(0, contentH - ClientSize.Height); } }

        public void SetContent(int h)
        {
            contentH = h;
            int before = scroll;
            ClampScroll();
            Invalidate();
            if (before != scroll && ScrollChanged != null) ScrollChanged(this, EventArgs.Empty);
        }

        public void ScrollBy(int dy)
        {
            int before = scroll;
            scroll += dy;
            ClampScroll();
            if (before != scroll)
            {
                Invalidate();
                if (ScrollChanged != null) ScrollChanged(this, EventArgs.Empty);
            }
        }

        private void ClampScroll()
        {
            if (scroll > MaxScroll) scroll = MaxScroll;
            if (scroll < 0) scroll = 0;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollBy(-e.Delta * AppTheme.S(56) / 120);
            base.OnMouseWheel(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ClampScroll();
            if (ScrollChanged != null) ScrollChanged(this, EventArgs.Empty);
            Invalidate();
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
            g.Clear(BackColor);
            if (MaxScroll <= 0) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            int barW = barHover ? AppTheme.S(7) : AppTheme.S(5);
            int x = Width - barW - AppTheme.S(4);
            int pad = AppTheme.S(2);
            int trackH = ClientSize.Height - pad * 2;
            double view = (double)ClientSize.Height / contentH;
            int thumbH = Math.Max(AppTheme.S(30), (int)(ClientSize.Height * view));
            if (thumbH > trackH) thumbH = trackH;
            double frac = MaxScroll > 0 ? (double)scroll / MaxScroll : 0;
            int thumbY = pad + (int)Math.Round((trackH - thumbH) * frac);

            using (GraphicsPath p = AppTheme.RoundRect(new Rectangle(x, thumbY, barW, thumbH), barW / 2))
            {
                Color c = barHover ? AppTheme.LineSoft : Color.FromArgb(52, 62, 75);
                using (SolidBrush b = new SolidBrush(c)) g.FillPath(b, p);
            }
        }
    }

    /// <summary>Rounded flat button with hover / disabled states.</summary>
    internal sealed class FlatButton : DarkControl
    {
        private bool hover;
        private bool down;
        private int radius = 8;

        public enum Style { Primary, Secondary, Ghost, Danger }

        public Style Kind = Style.Secondary;

        public FlatButton()
        {
            Font = AppTheme.F(9f, FontStyle.Bold);
            ForeColor = AppTheme.Txt;
            Cursor = Cursors.Hand;
        }

        public int Radius { get { return radius; } set { radius = value; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ParentBg());

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bgTop, bgBottom, border, fg;

            switch (Kind)
            {
                case Style.Primary:
                    bgTop = down ? AppTheme.Acc2 : (hover ? AppTheme.Mix(AppTheme.Acc, Color.White, 0.12) : AppTheme.Acc);
                    bgBottom = down ? AppTheme.Acc : AppTheme.Acc2;
                    border = Color.Transparent;
                    fg = AppTheme.AccDeep;
                    break;
                case Style.Danger:
                    bgTop = hover ? Color.FromArgb(58, 32, 32) : Color.FromArgb(40, 26, 26);
                    bgBottom = bgTop;
                    border = Color.FromArgb(90, 45, 45);
                    fg = AppTheme.Err;
                    break;
                case Style.Ghost:
                    bgTop = hover ? AppTheme.CardHover : Color.FromArgb(0, 0, 0, 0);
                    bgBottom = bgTop;
                    border = hover ? AppTheme.LineSoft : Color.Transparent;
                    fg = AppTheme.Dim;
                    break;
                default:
                    bgTop = hover ? AppTheme.CardHover : AppTheme.Card;
                    bgBottom = bgTop;
                    border = hover ? AppTheme.LineSoft : AppTheme.Line;
                    fg = AppTheme.Txt;
                    break;
            }

            if (!Enabled)
            {
                bgTop = AppTheme.Mix(AppTheme.Bg, bgTop, 0.35);
                bgBottom = bgTop;
                border = AppTheme.Line;
                fg = AppTheme.Dim2;
            }

            using (GraphicsPath p = AppTheme.RoundRect(r, radius))
            {
                if (Kind == Style.Primary)
                {
                    using (LinearGradientBrush lb = new LinearGradientBrush(
                        new Rectangle(0, 0, Width, Math.Max(Height, 2)), bgTop, bgBottom, LinearGradientMode.Vertical))
                        g.FillPath(lb, p);
                }
                else if (bgTop.A > 0)
                {
                    using (SolidBrush b = new SolidBrush(bgTop)) g.FillPath(b, p);
                }

                if (border.A > 0)
                {
                    using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
                }
            }

            TextRenderer.DrawText(g, Text, Font, r, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Row of mutually exclusive pill options.</summary>
    internal sealed class Segmented : DarkControl
    {
        private string[] items = new string[0];
        private int selected;
        private int hoverIndex = -1;
        private int radius = 8;
        private bool compact;

        public event EventHandler SelectedIndexChanged;

        public Segmented()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = AppTheme.F(8.75f, FontStyle.Regular);
            Cursor = Cursors.Hand;
        }

        public bool Compact { get { return compact; } set { compact = value; Invalidate(); } }

        public string[] Items
        {
            get { return items; }
            set { items = value ?? new string[0]; if (selected >= items.Length) selected = 0; Invalidate(); }
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value < 0) value = 0;
                if (value >= items.Length) value = items.Length - 1;
                if (value == selected) return;
                selected = value;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        public string SelectedItem
        {
            get { return (selected >= 0 && selected < items.Length) ? items[selected] : null; }
        }

        private Rectangle SegmentRect(int i)
        {
            if (items.Length == 0) return Rectangle.Empty;
            double w = (double)Width / items.Length;
            int x0 = (int)Math.Round(w * i);
            int x1 = (int)Math.Round(w * (i + 1));
            return new Rectangle(x0, 0, Math.Max(1, x1 - x0), Height);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = -1;
            for (int i = 0; i < items.Length; i++) if (SegmentRect(i).Contains(e.Location)) { idx = i; break; }
            if (idx != hoverIndex) { hoverIndex = idx; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hoverIndex = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (SegmentRect(i).Contains(e.Location))
                {
                    SelectedIndex = i;
                    if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
                    break;
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ParentBg());
            if (items.Length == 0) return;

            Rectangle outer = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = AppTheme.RoundRect(outer, radius))
            {
                using (SolidBrush b = new SolidBrush(AppTheme.Field)) g.FillPath(b, p);
                using (Pen pen = new Pen(AppTheme.Line)) g.DrawPath(pen, p);

                for (int i = 0; i < items.Length; i++)
                {
                    Rectangle r = SegmentRect(i);
                    bool sel = i == selected;
                    int inset = 2;
                    Rectangle ir = new Rectangle(r.X + inset, r.Y + inset, r.Width - inset * 2, r.Height - inset * 2);
                    if (ir.Width < 6) continue;

                    if (sel)
                    {
                        using (GraphicsPath sp = AppTheme.RoundRect(ir, Math.Max(2, radius - 2)))
                        {
                            using (LinearGradientBrush lb = new LinearGradientBrush(
                                new Rectangle(ir.X, ir.Y, ir.Width, Math.Max(2, ir.Height)),
                                AppTheme.Acc, AppTheme.Acc2, LinearGradientMode.Vertical))
                                g.FillPath(lb, sp);
                        }
                    }
                    else if (i == hoverIndex)
                    {
                        using (GraphicsPath sp = AppTheme.RoundRect(ir, Math.Max(2, radius - 2)))
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(24, 255, 255, 255)))
                            g.FillPath(b, sp);
                    }

                    TextRenderer.DrawText(g, items[i], Font, r,
                        sel ? AppTheme.AccDeep : (i == hoverIndex ? AppTheme.Txt : AppTheme.Dim),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                }
            }
        }
    }

    /// <summary>Flat checkbox with custom glyph.</summary>
    internal sealed class DarkCheck : DarkControl
    {
        private bool isChecked;
        private bool hover;
        private bool keyFocus;

        public event EventHandler CheckedChanged;

        public DarkCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = AppTheme.F(9f, FontStyle.Regular);
            ForeColor = AppTheme.Dim;
            Cursor = Cursors.Hand;
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (value == isChecked) return;
                isChecked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { keyFocus = true; Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { keyFocus = false; Invalidate(); base.OnLostFocus(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Focus();
                Checked = !Checked;
            }
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Space || base.IsInputKey(keyData); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ParentBg());

            int box = AppTheme.S(15);
            Rectangle br = new Rectangle(0, (Height - box) / 2, box, box);

            using (GraphicsPath p = AppTheme.RoundRect(br, AppTheme.S(4)))
            {
                using (SolidBrush b = new SolidBrush(isChecked ? AppTheme.Acc : AppTheme.Field)) g.FillPath(b, p);
                Color border = isChecked ? AppTheme.Acc : (hover || keyFocus ? AppTheme.LineSoft : AppTheme.Line);
                using (Pen pen = new Pen(border)) g.DrawPath(pen, p);

                if (isChecked)
                {
                    using (Pen pen = new Pen(AppTheme.AccDeep, Math.Max(1.6f, AppTheme.SF(1.7f))))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        PointF a = new PointF(br.X + box * 0.24f, br.Y + box * 0.52f);
                        PointF b2 = new PointF(br.X + box * 0.43f, br.Y + box * 0.71f);
                        PointF c = new PointF(br.X + box * 0.78f, br.Y + box * 0.30f);
                        g.DrawLines(pen, new PointF[] { a, b2, c });
                    }
                }
            }

            Rectangle tr = new Rectangle(box + AppTheme.S(7), 0, Math.Max(1, Width - box - AppTheme.S(7)), Height);
            TextRenderer.DrawText(g, Text, Font, tr, ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Slim progress bar with animated indeterminate mode.</summary>
    internal sealed class ThinProgress : DarkControl
    {
        private double value;
        private bool indeterminate;
        private Timer timer;
        private float phase;

        public ThinProgress()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public double Value
        {
            get { return value; }
            set
            {
                this.value = value;
                if (this.value < 0) this.value = 0;
                if (this.value > 1) this.value = 1;
                Invalidate();
            }
        }

        public bool Indeterminate
        {
            get { return indeterminate; }
            set
            {
                if (indeterminate == value) return;
                indeterminate = value;
                if (indeterminate)
                {
                    if (timer == null)
                    {
                        timer = new Timer();
                        timer.Interval = 33;
                        timer.Tick += delegate { phase += 0.035f; if (phase > 1f) phase -= 1f; Invalidate(); };
                    }
                    timer.Start();
                }
                else if (timer != null) timer.Stop();
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ParentBg());

            Rectangle r = new Rectangle(0, (Height - AppTheme.S(8)) / 2, Width, AppTheme.S(8));
            using (GraphicsPath p = AppTheme.RoundRect(r, r.Height / 2))
            {
                using (SolidBrush b = new SolidBrush(AppTheme.ProgressTrack)) g.FillPath(b, p);

                if (indeterminate)
                {
                    int w = Math.Max(AppTheme.S(60), Width / 5);
                    int x = (int)((Width + w) * phase) - w;
                    Rectangle rr = new Rectangle(x, r.Y, w, r.Height);
                    rr.Intersect(r);
                    using (GraphicsPath ip = AppTheme.RoundRect(rr, Math.Min(rr.Height / 2, Math.Max(1, rr.Height / 2))))
                    using (LinearGradientBrush lb = new LinearGradientBrush(
                        new Rectangle(rr.X, rr.Y, Math.Max(2, rr.Width), Math.Max(2, rr.Height)),
                        AppTheme.Acc, AppTheme.Acc2, LinearGradientMode.Horizontal))
                        g.FillPath(lb, ip);
                }
                else if (value > 0)
                {
                    int w = (int)Math.Round(Width * value);
                    if (w > 0)
                    {
                        Rectangle rr = new Rectangle(0, r.Y, Math.Max(r.Height, w), r.Height);
                        using (GraphicsPath ip = AppTheme.RoundRect(rr, r.Height / 2))
                        using (LinearGradientBrush lb = new LinearGradientBrush(
                            new Rectangle(0, rr.Y, Math.Max(2, rr.Width), Math.Max(2, rr.Height)),
                            AppTheme.Acc, AppTheme.Ok, LinearGradientMode.Horizontal))
                            g.FillPath(lb, ip);
                    }
                }
            }
        }
    }

    /// <summary>Panel with a 1px border and configurable fill.</summary>
    internal sealed class CardPanel : Panel
    {
        public Color FillColor = AppTheme.Card;
        public Color BorderColor = AppTheme.Line;

        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = AppTheme.Card;
            ForeColor = AppTheme.Txt;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(FillColor);
            if (BorderColor.A > 0)
            {
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                using (Pen pen = new Pen(BorderColor)) g.DrawRectangle(pen, r);
            }
        }
    }

    /// <summary>Drop / click target with dashed rounded border.</summary>
    internal sealed class DropZone : DarkControl
    {
        private bool hover;
        private bool dragging;

        public event EventHandler BrowseRequested;

        public DropZone()
        {
            Cursor = Cursors.Hand;
            Font = AppTheme.F(9f, FontStyle.Regular);
            AllowDrop = true;
        }

        public void SetHover(bool on) { if (hover != on) { hover = on; Invalidate(); } }
        public void SetDragging(bool on) { if (dragging != on) { dragging = on; Invalidate(); } }

        protected override void OnClick(EventArgs e)
        {
            if (BrowseRequested != null) BrowseRequested(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ParentBg());
            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
            bool hot = hover || dragging;

            using (GraphicsPath p = AppTheme.RoundRect(r, AppTheme.S(11)))
            {
                if (hot)
                {
                    using (SolidBrush b = new SolidBrush(AppTheme.CardHover)) g.FillPath(b, p);
                }
                using (Pen pen = new Pen(hot ? AppTheme.Acc : AppTheme.LineSoft))
                {
                    pen.DashStyle = DashStyle.Dash;
                    pen.DashPattern = new float[] { 4f, 3f };
                    pen.Width = Math.Max(1f, AppTheme.SF(1.3f));
                    g.DrawPath(pen, p);
                }
            }

            int iconSize = AppTheme.S(30);
            int totalH = AppTheme.S(30) + AppTheme.S(14) + AppTheme.S(16);
            int top = (Height - totalH) / 2 - AppTheme.S(2);

            // download icon
            Rectangle ir = new Rectangle((Width - iconSize) / 2, top, iconSize, AppTheme.S(30));
            using (Pen pen = new Pen(hot ? AppTheme.Acc : AppTheme.Acc2, Math.Max(1.6f, AppTheme.SF(1.8f))))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                int cx = ir.X + ir.Width / 2;
                g.DrawLine(pen, cx, ir.Y + AppTheme.S(3), cx, ir.Y + AppTheme.S(18));
                g.DrawLines(pen, new PointF[] {
                    new PointF(cx - AppTheme.S(6), ir.Y + AppTheme.S(12)),
                    new PointF(cx, ir.Y + AppTheme.S(19)),
                    new PointF(cx + AppTheme.S(6), ir.Y + AppTheme.S(12)) });
                g.DrawLines(pen, new PointF[] {
                    new PointF(ir.X + AppTheme.S(4), ir.Y + AppTheme.S(23)),
                    new PointF(ir.X + AppTheme.S(4), ir.Y + AppTheme.S(27)),
                    new PointF(ir.Right - AppTheme.S(4), ir.Y + AppTheme.S(27)),
                    new PointF(ir.Right - AppTheme.S(4), ir.Y + AppTheme.S(23)) });
            }

            using (Font tf = AppTheme.F(10f, FontStyle.Bold))
            {
                Rectangle tr = new Rectangle(0, ir.Bottom + AppTheme.S(6), Width, AppTheme.S(20));
                TextRenderer.DrawText(g, "点击选择文件，或把文件拖到这里", tf, tr,
                    hot ? AppTheme.Acc : AppTheme.Txt,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            using (Font hf = AppTheme.F(8.5f, FontStyle.Regular))
            {
                Rectangle hr = new Rectangle(0, ir.Bottom + AppTheme.S(28), Width, AppTheme.S(18));
                TextRenderer.DrawText(g, "支持 MP4 / MKV / MOV / WAV / FLAC / MP3 / M4A，可一次选多个",
                    hf, hr, AppTheme.Dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}
