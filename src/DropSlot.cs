using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Thumbly
{
    /// <summary>
    /// A titled drop area with an empty state ("Drop … here or Browse") and a filled state (preview + two text lines +
    /// Clear). It only displays; the owner handles the drops and decides what goes where.
    /// </summary>
    public class DropSlot : Panel
    {
        public enum Arrangement { Row, Stack }

        private readonly LinkLabel _browse;
        private readonly LinkLabel _clear;
        private Image _preview;
        private bool _hover;

        public string Title { get; set; }
        public string EmptyText { get; set; }
        public Arrangement Arrange { get; set; } = Arrangement.Row;
        /// <summary>Draw a checkerboard behind the preview so transparency is visible.</summary>
        public bool CheckerBehindPreview { get; set; }
        public string Primary { get; private set; }
        public string Secondary { get; private set; }
        public bool HasContent => Primary != null;

        public event Action BrowseClicked;
        public event Action ClearClicked;

        public DropSlot()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AllowDrop = true;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;

            _browse = new LinkLabel { Text = "Browse…", AutoSize = true, LinkColor = Theme.Link, ActiveLinkColor = Color.White, BackColor = Color.Transparent, AllowDrop = true };
            _browse.LinkClicked += (s, e) => BrowseClicked?.Invoke();
            _clear = new LinkLabel { Text = "Clear", AutoSize = true, LinkColor = Theme.Muted, ActiveLinkColor = Color.White, BackColor = Color.Transparent, Visible = false, AllowDrop = true };
            _clear.LinkClicked += (s, e) => ClearClicked?.Invoke();
            Controls.Add(_browse);
            Controls.Add(_clear);
        }

        /// <summary>The slot and its child links: everything that should accept drops.</summary>
        public IEnumerable<Control> DropTargets => new Control[] { this, _browse, _clear };

        public bool Hover
        {
            get => _hover;
            set { if (_hover != value) { _hover = value; Invalidate(); } }
        }

        /// <summary>Shows content. The slot takes ownership of the preview image.</summary>
        public void SetContent(Image preview, string primary, string secondary)
        {
            _preview?.Dispose();
            _preview = preview;
            Primary = primary;
            Secondary = secondary;
            _clear.Visible = true;
            _browse.Text = "Change…";
            PerformLayout();
            Invalidate();
        }

        public void ClearContent()
        {
            _preview?.Dispose();
            _preview = null;
            Primary = Secondary = null;
            _clear.Visible = false;
            _browse.Text = "Browse…";
            PerformLayout();
            Invalidate();
        }

        private Rectangle Frame
        {
            get { var r = ClientRectangle; r.Inflate(-6, -6); return r; }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            var r = Frame;
            _clear.Location = new Point(r.Right - _clear.Width - 10, r.Top + 8);
            if (HasContent)
                _browse.Location = new Point(_clear.Left - _browse.Width - 10, r.Top + 8);
            else
            {
                var textHeight = TextRenderer.MeasureText(EmptyText ?? "", Font, new Size(r.Width - 20, 0), TextFormatFlags.WordBreak).Height;
                _browse.Location = new Point(r.Left + (r.Width - _browse.Width) / 2, r.Top + r.Height / 2 + textHeight / 2 + 4);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = Frame;
            var color = _hover ? Theme.Link : HasContent ? Color.FromArgb(110, 110, 122) : Theme.Border;
            using (var pen = new Pen(color, _hover ? 2.5f : 1.5f) { DashStyle = HasContent && !_hover ? DashStyle.Solid : DashStyle.Dash })
            using (var path = RoundedRect(r, 12))
            {
                if (HasContent) using (var fill = new SolidBrush(Theme.Panel)) g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            using (var titleFont = new Font(Font.FontFamily, 8.5f, FontStyle.Bold))
                TextRenderer.DrawText(g, (Title ?? "").ToUpperInvariant(), titleFont, new Point(r.Left + 12, r.Top + 9), Theme.Muted);

            var body = Rectangle.FromLTRB(r.Left + 12, r.Top + 32, r.Right - 12, r.Bottom - 10);
            if (!HasContent)
            {
                var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.WordBreak;
                var textRect = new Rectangle(r.Left + 10, r.Top, r.Width - 20, r.Height / 2 + 4);
                TextRenderer.DrawText(g, EmptyText ?? "", Font, textRect, Color.Silver, flags);
                return;
            }

            using (var bold = new Font(Font.FontFamily, 11f, FontStyle.Bold))
            {
                if (Arrange == Arrangement.Row)
                {
                    int side = Math.Min(64, body.Height);
                    var pr = new Rectangle(body.Left, body.Top + (body.Height - side) / 2, side, side);
                    DrawPreview(g, pr);
                    var tx = pr.Right + 12;
                    var tr = new Rectangle(tx, body.Top, body.Right - tx, body.Height);
                    int lh = TextRenderer.MeasureText("Ag", bold).Height;
                    int top = tr.Top + (tr.Height - lh * 2 - 4) / 2;
                    TextRenderer.DrawText(g, Primary, bold, new Rectangle(tr.Left, top, tr.Width, lh), Theme.Text, TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(g, Secondary ?? "", Font, new Rectangle(tr.Left, top + lh + 2, tr.Width, lh * 2),
                        Theme.Muted, TextFormatFlags.PathEllipsis | TextFormatFlags.WordBreak);
                }
                else
                {
                    int lh = TextRenderer.MeasureText("Ag", bold).Height;
                    int textBlock = lh + TextRenderer.MeasureText("Ag", Font).Height + 6;
                    int side = Math.Max(16, Math.Min(body.Width, body.Height - textBlock));
                    var pr = new Rectangle(body.Left + (body.Width - side) / 2, body.Top, side, side);
                    DrawPreview(g, pr);
                    var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis;
                    TextRenderer.DrawText(g, Primary, bold, new Rectangle(body.Left, pr.Bottom + 6, body.Width, lh), Theme.Text, flags);
                    TextRenderer.DrawText(g, Secondary ?? "", Font, new Rectangle(body.Left, pr.Bottom + 6 + lh, body.Width, lh), Theme.Muted, flags);
                }
            }
        }

        private void DrawPreview(Graphics g, Rectangle box)
        {
            if (CheckerBehindPreview) using (var checker = new TextureBrush(Theme.Checker)) g.FillRectangle(checker, box);
            if (_preview == null) return;
            float k = Math.Min((float)box.Width / _preview.Width, (float)box.Height / _preview.Height);
            int w = Math.Max(1, (int)(_preview.Width * k)), h = Math.Max(1, (int)(_preview.Height * k));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(_preview, new Rectangle(box.Left + (box.Width - w) / 2, box.Top + (box.Height - h) / 2, w, h));
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _preview?.Dispose();
            base.Dispose(disposing);
        }
    }
}
