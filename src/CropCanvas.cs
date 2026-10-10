using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Thumbly
{
    /// <summary>
    /// Image with a crop frame. <see cref="Frame"/> is in image pixels and may extend into the padding square around
    /// the image (side = the longer image side), never beyond it.
    /// </summary>
    internal class CropCanvas : Control
    {
        private enum Hit { None, Move, Draw, Corner, Edge }

        private readonly Bitmap _image;
        private readonly RectangleF _bounds; // padding square, image pixels
        private Bitmap _display;              // image scaled to its on-screen size, rebuilt on resize
        private Hit _hit;
        private int _which;                  // corner 0..3 (TL, TR, BR, BL) or edge 0..3 (L, T, R, B)
        private PointF _start;
        private RectangleF _startFrame;
        private bool _dragging;
        private bool _square = true;

        public RectangleF Frame { get; private set; }
        public event Action FrameChanged;
        /// <summary>Source pixels per working-copy pixel, for the size label shown while dragging.</summary>
        public double PixelScale { get; set; } = 1.0;

        public CropCanvas(Bitmap image)
        {
            _image = image;
            float m = Math.Max(image.Width, image.Height);
            _bounds = new RectangleF((image.Width - m) / 2, (image.Height - m) / 2, m, m);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
        }

        public bool Square
        {
            get => _square;
            set
            {
                _square = value;
                if (value && Math.Abs(Frame.Width - Frame.Height) > 0.5f)
                {
                    float s = Math.Min(Frame.Width, Frame.Height);
                    SetFrame(new RectangleF(Frame.X + (Frame.Width - s) / 2, Frame.Y + (Frame.Height - s) / 2, s, s));
                }
            }
        }

        public bool ExtendsPastImage =>
            Frame.Left < -0.5f || Frame.Top < -0.5f || Frame.Right > _image.Width + 0.5f || Frame.Bottom > _image.Height + 0.5f;

        private float MinSide => Math.Max(4f, _bounds.Width * 0.02f);

        public void SetFrame(RectangleF f)
        {
            f.Width = Math.Max(MinSide, Math.Min(_bounds.Width, f.Width));
            f.Height = Math.Max(MinSide, Math.Min(_bounds.Height, f.Height));
            Frame = Fit(f);
            Invalidate();
            FrameChanged?.Invoke();
        }

        public void FitSquare()
        {
            float s = Math.Min(_image.Width, _image.Height);
            SetFrame(new RectangleF((_image.Width - s) / 2, (_image.Height - s) / 2, s, s));
        }

        public void WholeImage() => SetFrame(_square ? _bounds : new RectangleF(0, 0, _image.Width, _image.Height));

        /// <summary>Shifts the frame back inside the padding square.</summary>
        private RectangleF Fit(RectangleF r) => new RectangleF(
            Math.Max(_bounds.Left, Math.Min(_bounds.Right - r.Width, r.X)),
            Math.Max(_bounds.Top, Math.Min(_bounds.Bottom - r.Height, r.Y)), r.Width, r.Height);

        // ---- view mapping ----

        private float ViewScale
        {
            get
            {
                var c = ClientRectangle; c.Inflate(-16, -16);
                return Math.Max(0.01f, Math.Min(c.Width / _bounds.Width, c.Height / _bounds.Height));
            }
        }

        private PointF ViewOrigin
        {
            get
            {
                float k = ViewScale;
                return new PointF((Width - _bounds.Width * k) / 2 - _bounds.X * k, (Height - _bounds.Height * k) / 2 - _bounds.Y * k);
            }
        }

        private RectangleF ToScreen(RectangleF r)
        {
            float k = ViewScale; var o = ViewOrigin;
            return new RectangleF(o.X + r.X * k, o.Y + r.Y * k, r.Width * k, r.Height * k);
        }

        /// <summary>Image pixel to control pixel (used by tests to aim the mouse).</summary>
    internal Point ToControl(PointF p)
    {
        float k = ViewScale; var o = ViewOrigin;
        return new Point((int)Math.Round(o.X + p.X * k), (int)Math.Round(o.Y + p.Y * k));
    }

    private PointF ToImage(Point p)
        {
            float k = ViewScale; var o = ViewOrigin;
            return new PointF((p.X - o.X) / k, (p.Y - o.Y) / k);
        }

        private PointF Clamp(PointF p) => new PointF(
            Math.Max(_bounds.Left, Math.Min(_bounds.Right, p.X)), Math.Max(_bounds.Top, Math.Min(_bounds.Bottom, p.Y)));

        // ---- painting ----

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _display?.Dispose();
            _display = null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var b = ToScreen(_bounds);
            var ir = Rectangle.Round(ToScreen(new RectangleF(0, 0, _image.Width, _image.Height)));
            if (ir.Width < 1 || ir.Height < 1) return;

            using (var checker = new TextureBrush(Theme.Checker)) g.FillRectangle(checker, b);
            if (_display == null || _display.Size != ir.Size)
            {
                _display?.Dispose();
                _display = IcoWriter.DrawScaled(_image, new Rectangle(0, 0, _image.Width, _image.Height), ir.Width, ir.Height);
            }
            g.DrawImageUnscaled(_display, ir.X, ir.Y);

            var f = ToScreen(Frame);
            using (var mask = new SolidBrush(Color.FromArgb(165, 10, 10, 12)))
            {
                g.FillRectangle(mask, b.Left, b.Top, b.Width, f.Top - b.Top);
                g.FillRectangle(mask, b.Left, f.Bottom, b.Width, b.Bottom - f.Bottom);
                g.FillRectangle(mask, b.Left, f.Top, f.Left - b.Left, f.Height);
                g.FillRectangle(mask, f.Right, f.Top, b.Right - f.Right, f.Height);
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var grid = new Pen(Color.FromArgb(70, 255, 255, 255), 1f))
                for (int i = 1; i <= 2; i++)
                {
                    g.DrawLine(grid, f.Left + f.Width * i / 3, f.Top, f.Left + f.Width * i / 3, f.Bottom);
                    g.DrawLine(grid, f.Left, f.Top + f.Height * i / 3, f.Right, f.Top + f.Height * i / 3);
                }
            using (var border = new Pen(Color.FromArgb(220, 255, 255, 255), 1.5f) { DashStyle = DashStyle.Dash })
                g.DrawRectangle(border, f.X, f.Y, f.Width, f.Height);

            using (var accent = new Pen(Theme.Accent, 3f) { StartCap = LineCap.Square, EndCap = LineCap.Square })
            {
                float l = Math.Min(18f, Math.Min(f.Width, f.Height) / 3);
                g.DrawLines(accent, new[] { new PointF(f.Left, f.Top + l), new PointF(f.Left, f.Top), new PointF(f.Left + l, f.Top) });
                g.DrawLines(accent, new[] { new PointF(f.Right - l, f.Top), new PointF(f.Right, f.Top), new PointF(f.Right, f.Top + l) });
                g.DrawLines(accent, new[] { new PointF(f.Right, f.Bottom - l), new PointF(f.Right, f.Bottom), new PointF(f.Right - l, f.Bottom) });
                g.DrawLines(accent, new[] { new PointF(f.Left + l, f.Bottom), new PointF(f.Left, f.Bottom), new PointF(f.Left, f.Bottom - l) });
                float h = Math.Min(7f, Math.Min(f.Width, f.Height) / 6), cx = f.Left + f.Width / 2, cy = f.Top + f.Height / 2;
                g.DrawLine(accent, cx - h, f.Top, cx + h, f.Top);
                g.DrawLine(accent, cx - h, f.Bottom, cx + h, f.Bottom);
                g.DrawLine(accent, f.Left, cy - h, f.Left, cy + h);
                g.DrawLine(accent, f.Right, cy - h, f.Right, cy + h);
            }

            if (_dragging)
            {
                double k = PixelScale;
                var text = (int)Math.Round(Frame.Width * k) + " × " + (int)Math.Round(Frame.Height * k);
                var size = g.MeasureString(text, Font);
                var pos = new PointF(f.Left + (f.Width - size.Width) / 2, Math.Min(f.Bottom + 4, Height - size.Height - 2));
                using (var bg = new SolidBrush(Color.FromArgb(180, 0, 0, 0))) g.FillRectangle(bg, pos.X - 4, pos.Y - 1, size.Width + 8, size.Height + 2);
                using (var fg = new SolidBrush(Color.White)) g.DrawString(text, Font, fg, pos);
            }
        }

        // ---- interaction ----

        private Hit HitTest(Point p, out int which)
        {
            which = -1;
            var f = ToScreen(Frame);
            var corners = new[] { new PointF(f.Left, f.Top), new PointF(f.Right, f.Top), new PointF(f.Right, f.Bottom), new PointF(f.Left, f.Bottom) };
            for (int i = 0; i < 4; i++)
                if (Math.Abs(p.X - corners[i].X) <= 12 && Math.Abs(p.Y - corners[i].Y) <= 12) { which = i; return Hit.Corner; }
            bool inY = p.Y >= f.Top && p.Y <= f.Bottom, inX = p.X >= f.Left && p.X <= f.Right;
            if (inY && Math.Abs(p.X - f.Left) <= 7) { which = 0; return Hit.Edge; }
            if (inX && Math.Abs(p.Y - f.Top) <= 7) { which = 1; return Hit.Edge; }
            if (inY && Math.Abs(p.X - f.Right) <= 7) { which = 2; return Hit.Edge; }
            if (inX && Math.Abs(p.Y - f.Bottom) <= 7) { which = 3; return Hit.Edge; }
            if (f.Contains(p)) return Hit.Move;
            return Hit.Draw;
        }

        private static Cursor CursorFor(Hit hit, int which)
        {
            switch (hit)
            {
                case Hit.Corner: return which == 0 || which == 2 ? Cursors.SizeNWSE : Cursors.SizeNESW;
                case Hit.Edge: return which == 0 || which == 2 ? Cursors.SizeWE : Cursors.SizeNS;
                case Hit.Move: return Cursors.SizeAll;
                default: return Cursors.Cross;
            }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Focus(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            _hit = HitTest(e.Location, out _which);
            _start = ToImage(e.Location);
            _startFrame = Frame;
            _dragging = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button != MouseButtons.Left || _hit == Hit.None)
            {
                Cursor = CursorFor(HitTest(e.Location, out var w), w);
                return;
            }
            var p = ToImage(e.Location);
            if (!_dragging)
            {
                float k = ViewScale;
                if (Math.Abs(p.X - _start.X) * k < 3 && Math.Abs(p.Y - _start.Y) * k < 3) return; // ignore jitter on click
                _dragging = true;
            }
            switch (_hit)
            {
                case Hit.Move:
                    Frame = Fit(new RectangleF(_startFrame.X + p.X - _start.X, _startFrame.Y + p.Y - _start.Y, _startFrame.Width, _startFrame.Height));
                    break;
                case Hit.Draw:
                    Frame = FromAnchor(Clamp(_start), Clamp(p));
                    break;
                case Hit.Corner:
                    var c = new[] { new PointF(_startFrame.Left, _startFrame.Top), new PointF(_startFrame.Right, _startFrame.Top),
                                    new PointF(_startFrame.Right, _startFrame.Bottom), new PointF(_startFrame.Left, _startFrame.Bottom) };
                    Frame = FromAnchor(c[(_which + 2) % 4], Clamp(p));
                    break;
                case Hit.Edge:
                    Frame = FromEdge(_which, Clamp(p));
                    break;
            }
            Invalidate();
            FrameChanged?.Invoke();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _hit = Hit.None;
            if (_dragging) { _dragging = false; Invalidate(); FrameChanged?.Invoke(); }
        }

        /// <summary>Rectangle from a fixed anchor to the mouse, square when locked, limited to the padding square.</summary>
        private RectangleF FromAnchor(PointF a, PointF p)
        {
            float dx = p.X - a.X, dy = p.Y - a.Y;
            int sx = dx < 0 ? -1 : 1, sy = dy < 0 ? -1 : 1;
            float roomX = sx > 0 ? _bounds.Right - a.X : a.X - _bounds.Left;
            float roomY = sy > 0 ? _bounds.Bottom - a.Y : a.Y - _bounds.Top;
            float w = Math.Min(Math.Abs(dx), roomX), h = Math.Min(Math.Abs(dy), roomY);
            if (_square) w = h = Math.Min(Math.Max(Math.Abs(dx), Math.Abs(dy)), Math.Min(roomX, roomY));
            w = Math.Max(w, MinSide);
            h = Math.Max(h, MinSide);
            return Fit(new RectangleF(sx > 0 ? a.X : a.X - w, sy > 0 ? a.Y : a.Y - h, w, h));
        }

        /// <summary>Moves one edge; with the square lock the other axis grows around the frame's centre.</summary>
        private RectangleF FromEdge(int edge, PointF p)
        {
            var f = _startFrame;
            bool horizontal = edge == 0 || edge == 2;
            float anchor = edge == 0 ? f.Right : edge == 1 ? f.Bottom : edge == 2 ? f.Left : f.Top;
            float pos = horizontal ? p.X : p.Y;
            float size = edge == 0 || edge == 1 ? anchor - pos : pos - anchor;
            float room = edge == 0 ? anchor - _bounds.Left : edge == 1 ? anchor - _bounds.Top : edge == 2 ? _bounds.Right - anchor : _bounds.Bottom - anchor;
            if (_square)
            {
                float centre = horizontal ? f.Top + f.Height / 2 : f.Left + f.Width / 2;
                float lo = horizontal ? _bounds.Top : _bounds.Left, hi = horizontal ? _bounds.Bottom : _bounds.Right;
                room = Math.Min(room, 2 * Math.Min(centre - lo, hi - centre));
                size = Math.Max(MinSide, Math.Min(room, size));
                float start = edge == 0 || edge == 1 ? anchor - size : anchor;
                return Fit(horizontal
                    ? new RectangleF(start, centre - size / 2, size, size)
                    : new RectangleF(centre - size / 2, start, size, size));
            }
            size = Math.Max(MinSide, Math.Min(room, size));
            switch (edge)
            {
                case 0: return new RectangleF(anchor - size, f.Y, size, f.Height);
                case 1: return new RectangleF(f.X, anchor - size, f.Width, size);
                case 2: return new RectangleF(anchor, f.Y, size, f.Height);
                default: return new RectangleF(f.X, anchor, f.Width, size);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            float factor = e.Delta > 0 ? 0.9f : 1.1f; // wheel up zooms in (smaller frame)
            float w = Frame.Width * factor, h = Frame.Height * factor;
            float fit = Math.Min(1f, Math.Min(_bounds.Width / w, _bounds.Height / h));
            w *= fit; h *= fit;
            if (Math.Min(w, h) < MinSide) return;
            float cx = Frame.X + Frame.Width / 2, cy = Frame.Y + Frame.Height / 2;
            SetFrame(new RectangleF(cx - w / 2, cy - h / 2, w, h));
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            float step = Math.Max(1f, _bounds.Width / 200) * (e.Shift ? 10 : 1);
            float dx = e.KeyCode == Keys.Left ? -step : e.KeyCode == Keys.Right ? step : 0;
            float dy = e.KeyCode == Keys.Up ? -step : e.KeyCode == Keys.Down ? step : 0;
            if (dx == 0 && dy == 0) return;
            SetFrame(new RectangleF(Frame.X + dx, Frame.Y + dy, Frame.Width, Frame.Height));
            e.Handled = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _display?.Dispose();
            base.Dispose(disposing);
        }
    }
}
