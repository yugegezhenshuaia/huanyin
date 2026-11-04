using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace HuanYin
{
    /// <summary>One row in the file list, fully custom painted.</summary>
    internal sealed class FileRow : Control
    {
        private bool hover;

        public MediaItem Item;
        public bool IsCurrentVideo;
        public bool IsCurrentAudio;

        private static readonly Font fName = AppTheme.F(9.25f, FontStyle.Bold);
        private static readonly Font fMeta = AppTheme.F(8.25f, FontStyle.Regular);
        private static readonly Font fBadge = AppTheme.F(8f, FontStyle.Bold);

        public event EventHandler RowClicked;
        public event EventHandler RemoveClicked;

        public FileRow(MediaItem item)
        {
            Item = item;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = AppTheme.Bg;
            Cursor = Cursors.Hand;
            Font = AppTheme.F(9f, FontStyle.Regular);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        private Rectangle RemoveRect()
        {
            int w = AppTheme.S(30);
            return new Rectangle(Width - w - AppTheme.S(6), (Height - w) / 2, w, w);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor = RemoveRect().Contains(e.Location) ? Cursors.Hand : Cursors.Hand;
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (RemoveRect().Contains(e.Location))
                {
                    if (RemoveClicked != null) RemoveClicked(this, EventArgs.Empty);
                }
                else if (RowClicked != null) RowClicked(this, EventArgs.Empty);
            }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(AppTheme.Bg);

            bool selected = IsCurrentVideo || IsCurrentAudio;
            Rectangle r = new Rectangle(0, 1, Width - 1, Height - 3);
            using (GraphicsPath p = AppTheme.RoundRect(r, AppTheme.S(10)))
            {
                Color bg = selected ? Color.FromArgb(22, 41, 58) : (hover ? AppTheme.CardHover : AppTheme.Card);
                using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, p);
                Color border = selected ? AppTheme.Acc : (hover ? AppTheme.LineSoft : AppTheme.Line);
                using (Pen pen = new Pen(border)) g.DrawPath(pen, p);
            }

            int iconBox = AppTheme.S(30);
            Rectangle ir = new Rectangle(AppTheme.S(14), (Height - iconBox) / 2, iconBox, iconBox);
            DrawIcon(g, ir, Item.Kind);

            int rightPad = AppTheme.S(44);
            int badgeW = 0;
            string badge = "";
            Color badgeFg = AppTheme.Acc;
            Color badgeBg = Color.FromArgb(19, 58, 92);
            if (IsCurrentVideo) { badge = "当前视频"; badgeW = AppTheme.S(62); }
            else if (IsCurrentAudio) { badge = "当前音频"; badgeFg = Color.FromArgb(255, 209, 128); badgeBg = Color.FromArgb(58, 42, 20); badgeW = AppTheme.S(62); }

            int textX = ir.Right + AppTheme.S(12);
            int textW = Width - textX - rightPad - (badgeW > 0 ? badgeW + AppTheme.S(10) : 0);
            if (textW < AppTheme.S(40)) textW = AppTheme.S(40);

            Rectangle nameRect = new Rectangle(textX, AppTheme.S(9), textW, AppTheme.S(20));
            TextRenderer.DrawText(g, Item.FileName, fName, nameRect,
                selected ? AppTheme.Txt : AppTheme.Txt,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.PathEllipsis | TextFormatFlags.SingleLine);

            Rectangle metaRect = new Rectangle(textX, AppTheme.S(30), textW, AppTheme.S(18));
            TextRenderer.DrawText(g, Item.DisplayMeta(), fMeta, metaRect,
                AppTheme.Dim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            if (badgeW > 0)
            {
                Rectangle br = new Rectangle(Width - rightPad - badgeW, (Height - AppTheme.S(22)) / 2, badgeW, AppTheme.S(22));
                using (GraphicsPath bp = AppTheme.RoundRect(br, AppTheme.S(5)))
                {
                    using (SolidBrush b = new SolidBrush(badgeBg)) g.FillPath(b, bp);
                }
                TextRenderer.DrawText(g, badge, fBadge, br, badgeFg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            // remove glyph
            Rectangle rr = RemoveRect();
            int cx = rr.X + rr.Width / 2;
            int cy = rr.Y + rr.Height / 2;
            int s = AppTheme.S(5);
            using (Pen pen = new Pen(hover ? AppTheme.Err : AppTheme.Dim2, Math.Max(1.4f, AppTheme.SF(1.5f))))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
                g.DrawLine(pen, cx + s, cy - s, cx - s, cy + s);
            }
        }

        private void DrawIcon(Graphics g, Rectangle ir, MediaKind kind)
        {
            Color c = kind == MediaKind.Video ? AppTheme.Acc : (kind == MediaKind.Audio ? AppTheme.Warn : AppTheme.Dim2);
            using (Pen pen = new Pen(c, Math.Max(1.5f, AppTheme.SF(1.6f))))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;

                if (kind == MediaKind.Video)
                {
                    Rectangle v = new Rectangle(ir.X + AppTheme.S(1), ir.Y + AppTheme.S(4), ir.Width - AppTheme.S(2), ir.Height - AppTheme.S(8));
                    using (GraphicsPath p = AppTheme.RoundRect(v, AppTheme.S(4)))
                    {
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(26, 60, 88))) g.FillPath(b, p);
                        g.DrawPath(pen, p);
                    }
                    PointF[] tri = new PointF[] {
                        new PointF(v.X + v.Width * 0.38f, v.Y + v.Height * 0.28f),
                        new PointF(v.X + v.Width * 0.38f, v.Y + v.Height * 0.72f),
                        new PointF(v.X + v.Width * 0.72f, v.Y + v.Height * 0.5f)
                    };
                    using (SolidBrush b = new SolidBrush(c)) g.FillPolygon(b, tri);
                }
                else
                {
                    int cx = ir.X + AppTheme.S(11);
                    int top = ir.Y + AppTheme.S(4);
                    g.DrawLine(pen, cx, top, cx, ir.Y + AppTheme.S(21));
                    g.DrawLine(pen, cx, top, cx + AppTheme.S(8), top + AppTheme.S(3));
                    using (SolidBrush b = new SolidBrush(c))
                    {
                        g.FillEllipse(b, cx - AppTheme.S(5), ir.Y + AppTheme.S(18), AppTheme.S(9), AppTheme.S(8));
                        g.FillEllipse(b, cx + AppTheme.S(3), ir.Y + AppTheme.S(21), AppTheme.S(9), AppTheme.S(8));
                    }
                }
            }
        }
    }
}
