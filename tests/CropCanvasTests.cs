using System;
using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace Thumbly.Tests
{
    /// <summary>Drives the editor's crop frame with real mouse and keyboard events. Image is 600 × 900 (portrait).</summary>
    public class CropCanvasTests : IDisposable
    {
        private sealed class Canvas : CropCanvas
        {
            public Canvas(Bitmap b) : base(b) { Size = new Size(600, 600); }
            private void Mouse(Action<MouseEventArgs> on, PointF img, MouseButtons b = MouseButtons.Left)
            {
                var p = ToControl(img);
                on(new MouseEventArgs(b, 1, p.X, p.Y, 0));
            }
            public void Drag(float x1, float y1, float x2, float y2)
            {
                Mouse(OnMouseDown, new PointF(x1, y1));
                Mouse(OnMouseMove, new PointF((x1 + x2) / 2, (y1 + y2) / 2));
                Mouse(OnMouseMove, new PointF(x2, y2));
                Mouse(OnMouseUp, new PointF(x2, y2));
            }
            public void Tap(float x, float y) { Mouse(OnMouseDown, new PointF(x, y)); Mouse(OnMouseUp, new PointF(x, y)); }
            public void Wheel(int delta) => OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, 300, 300, delta));
            public void Key(Keys k) => OnKeyDown(new KeyEventArgs(k));
        }

        private readonly Bitmap _image = new Bitmap(600, 900);
        private readonly Canvas _c;
        private int _changes;

        public CropCanvasTests()
        {
            _c = new Canvas(_image);
            _c.FrameChanged += () => _changes++;
            _c.FitSquare();
        }

        public void Dispose() { _c.Dispose(); _image.Dispose(); }

        /// <summary>One screen pixel is about 1.6 image pixels at this size, so allow a few.</summary>
        private void AssertFrame(float x, float y, float w, float h)
        {
            var f = _c.Frame;
            Assert.True(Math.Abs(f.X - x) <= 3 && Math.Abs(f.Y - y) <= 3 && Math.Abs(f.Width - w) <= 3 && Math.Abs(f.Height - h) <= 3,
                $"expected ({x},{y},{w},{h}) but got ({f.X:0.#},{f.Y:0.#},{f.Width:0.#},{f.Height:0.#})");
        }

        [Fact]
        public void Starts_with_the_largest_centred_square() => AssertFrame(0, 150, 600, 600);

        [Fact]
        public void Dragging_inside_moves_the_frame()
        {
            _c.Drag(300, 450, 300, 400);
            AssertFrame(0, 100, 600, 600);
        }

        [Fact]
        public void Moving_is_limited_to_the_padding_square()
        {
            _c.Drag(300, 450, 300, -2000); // far above
            AssertFrame(0, 0, 600, 600);
            _c.Drag(300, 300, 2000, 300);   // far right: may go into the side padding, up to 750
            AssertFrame(150, 0, 600, 600);
        }

        [Fact]
        public void Dragging_a_corner_resizes_from_the_opposite_corner_and_stays_square()
        {
            _c.Drag(600, 750, 500, 600);
            AssertFrame(0, 150, 500, 500);
        }

        [Fact]
        public void Dragging_outside_the_frame_draws_a_new_one()
        {
            _c.Drag(50, 40, 250, 260); // starts above the frame
            AssertFrame(50, 40, 220, 220);
        }

        [Fact]
        public void Edge_drag_with_square_lock_grows_around_the_centre()
        {
            _c.Drag(600, 450, 700, 450); // right edge, +100
            AssertFrame(0, 100, 700, 700);
        }

        [Fact]
        public void Edge_drag_without_square_lock_moves_only_that_edge()
        {
            _c.Square = false;
            _c.Drag(300, 750, 300, 650); // bottom edge, -100
            AssertFrame(0, 150, 600, 500);
        }

        [Fact]
        public void Turning_square_back_on_squares_the_frame_around_its_centre()
        {
            _c.Square = false;
            _c.Drag(300, 750, 300, 650);
            _c.Square = true;
            AssertFrame(50, 150, 500, 500);
        }

        [Fact]
        public void A_click_without_movement_changes_nothing()
        {
            int before = _changes;
            _c.Tap(300, 450);
            _c.Tap(10, 10);
            AssertFrame(0, 150, 600, 600);
            Assert.Equal(before, _changes);
        }

        [Fact]
        public void Wheel_zooms_around_the_centre()
        {
            _c.Wheel(120);
            AssertFrame(30, 180, 540, 540);
            _c.Wheel(-120);
            _c.Wheel(-120);
            Assert.True(_c.Frame.Width > 600);
        }

        [Fact]
        public void Arrow_keys_nudge()
        {
            _c.Key(Keys.Up);
            Assert.True(_c.Frame.Y < 150);
        }

        [Fact]
        public void Whole_image_frames_the_padded_square()
        {
            _c.WholeImage();
            AssertFrame(-150, 0, 900, 900);
            Assert.True(_c.ExtendsPastImage);
        }
    }
}
