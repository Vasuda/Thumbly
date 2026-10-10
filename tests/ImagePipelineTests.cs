using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using Xunit;

namespace Thumbly.Tests
{
    public class ImagePipelineTests
    {
        private static Bitmap Solid(int w, int h, Color c)
        {
            var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b)) g.Clear(c);
            return b;
        }

        /// <summary>White canvas with an orange square in the middle (inset px from each edge).</summary>
        private static Bitmap Logo(int size, int inset, Color? background = null)
        {
            var b = Solid(size, size, background ?? Color.White);
            using (var g = Graphics.FromImage(b))
            using (var o = new SolidBrush(Color.Orange))
                g.FillRectangle(o, inset, inset, size - 2 * inset, size - 2 * inset);
            return b;
        }

        [Fact]
        public void CropFrame_maps_normalized_to_pixels_and_empty_to_whole()
        {
            var size = new Size(100, 50);
            Assert.Equal(new RectangleF(0, 0, 100, 50), ImagePipeline.CropFrame(null, size));
            Assert.Equal(new RectangleF(0, 0, 100, 50), ImagePipeline.CropFrame(new IconSource(), size));
            Assert.Equal(new RectangleF(10, 5, 50, 25), ImagePipeline.CropFrame(new IconSource { Crop = new RectangleF(0.1f, 0.1f, 0.5f, 0.5f) }, size));
            Assert.Equal(new RectangleF(-25, 0, 150, 50), ImagePipeline.CropFrame(new IconSource { Crop = new RectangleF(-0.25f, 0, 1.5f, 1f) }, size));
        }

        [Fact]
        public void Prepare_pads_a_frame_that_extends_past_the_image()
        {
            using (var b = Solid(100, 50, Color.Red))
            using (var r = ImagePipeline.Prepare(b, new IconSource { Crop = new RectangleF(0, -0.5f, 1f, 2f) })) // 100x100 frame, image centred
            {
                Assert.Equal(new Size(100, 100), r.Size);
                Assert.Equal(0, r.GetPixel(50, 5).A);    // padding above
                Assert.Equal(0, r.GetPixel(50, 94).A);   // padding below
                Assert.Equal(Color.Red.ToArgb(), r.GetPixel(50, 50).ToArgb());
            }
        }

        [Fact]
        public void Prepare_returns_transparent_when_the_frame_misses_the_image()
        {
            using (var b = Solid(10, 10, Color.Red))
            using (var r = ImagePipeline.Prepare(b, new IconSource { Crop = new RectangleF(5, 5, 1, 1) }))
                Assert.Equal(0, r.GetPixel(5, 5).A);
        }

        [Fact]
        public void Prepare_removes_background_before_padding()
        {
            using (var b = Logo(100, 20))
            using (var r = ImagePipeline.Prepare(b, new IconSource { Crop = new RectangleF(-0.5f, 0, 2f, 1f), RemoveBackground = true }))
            {
                Assert.Equal(new Size(200, 100), r.Size);
                Assert.Equal(0, r.GetPixel(60, 10).A);   // white background removed
                Assert.Equal(255, r.GetPixel(100, 50).A); // subject kept
            }
        }

        [Fact]
        public void Prepare_crops_to_the_requested_region()
        {
            using (var b = Solid(100, 100, Color.Red))
            {
                using (var g = Graphics.FromImage(b)) g.FillRectangle(Brushes.Blue, 25, 25, 50, 50);
                using (var r = ImagePipeline.Prepare(b, new IconSource { Crop = new RectangleF(0.3f, 0.3f, 0.4f, 0.4f) }))
                {
                    Assert.Equal(new Size(40, 40), r.Size);
                    Assert.Equal(Color.Blue.ToArgb(), r.GetPixel(0, 0).ToArgb());
                    Assert.Equal(Color.Blue.ToArgb(), r.GetPixel(39, 39).ToArgb());
                }
            }
        }

        [Fact]
        public void Prepare_caps_large_images_and_keeps_aspect()
        {
            using (var b = Solid(3000, 1500, Color.Red))
            using (var r = ImagePipeline.Prepare(b, new IconSource()))
                Assert.Equal(new Size(1024, 512), r.Size);
        }

        [Fact]
        public void Prepare_with_empty_crop_keeps_small_images_as_is()
        {
            using (var b = Solid(64, 32, Color.Red))
            using (var r = ImagePipeline.Prepare(b, null))
                Assert.Equal(new Size(64, 32), r.Size);
        }

        [Fact]
        public void Remove_clears_a_flat_background_and_keeps_the_subject()
        {
            using (var b = Logo(100, 20))
            {
                BackgroundRemover.Remove(b, 24);
                Assert.Equal(0, b.GetPixel(0, 0).A);
                Assert.Equal(0, b.GetPixel(99, 99).A);
                Assert.Equal(0, b.GetPixel(10, 50).A);
                Assert.Equal(255, b.GetPixel(50, 50).A);
                Assert.Equal(Color.Orange.ToArgb(), b.GetPixel(50, 50).ToArgb());
                var edge = b.GetPixel(20, 50); // boundary pixel is feathered, not removed
                Assert.InRange(edge.A, 1, 254);
            }
        }

        [Fact]
        public void Remove_keeps_enclosed_areas_of_the_background_colour()
        {
            using (var b = Logo(100, 20))
            {
                using (var g = Graphics.FromImage(b)) g.FillRectangle(Brushes.White, 45, 45, 10, 10); // white hole inside the subject
                BackgroundRemover.Remove(b, 24);
                Assert.Equal(255, b.GetPixel(50, 50).A);
                Assert.Equal(0, b.GetPixel(2, 2).A);
            }
        }

        [Fact]
        public void Remove_respects_tolerance()
        {
            using (var b = Solid(60, 60, Color.White))
            {
                using (var g = Graphics.FromImage(b))
                using (var near = new SolidBrush(Color.FromArgb(240, 240, 240)))
                    g.FillRectangle(near, 26, 0, 8, 8); // small off-white patch on the top edge, too small to count as a background colour
                using (var exact = (Bitmap)b.Clone())
                {
                    BackgroundRemover.Remove(exact, 0);
                    Assert.Equal(0, exact.GetPixel(30, 40).A);   // exact white: removed
                    Assert.NotEqual(0, exact.GetPixel(30, 2).A); // off-white: kept at tolerance 0
                }
                BackgroundRemover.Remove(b, 24);
                Assert.Equal(0, b.GetPixel(30, 2).A);
            }
        }

        [Fact]
        public void Remove_leaves_already_transparent_images_alone()
        {
            using (var b = Logo(50, 10, Color.Transparent))
            {
                BackgroundRemover.Remove(b, 24);
                Assert.Equal(0, b.GetPixel(0, 0).A);
                Assert.Equal(255, b.GetPixel(25, 25).A);
            }
        }

        [Fact]
        public void Remove_is_fast_enough_for_the_pipeline_cap()
        {
            using (var b = Logo(1024, 200))
            {
                var sw = Stopwatch.StartNew();
                BackgroundRemover.Remove(b, 24);
                sw.Stop();
                Assert.True(sw.ElapsedMilliseconds < 500, "took " + sw.ElapsedMilliseconds + " ms");
            }
        }

        [Fact]
        public void Prepare_applies_background_removal()
        {
            using (var b = Logo(100, 20))
            using (var r = ImagePipeline.Prepare(b, new IconSource { RemoveBackground = true }))
            {
                Assert.Equal(0, r.GetPixel(0, 0).A);
                Assert.Equal(255, r.GetPixel(50, 50).A);
            }
        }

        [Fact]
        public void LoadFrame_reads_the_closest_layer()
        {
            using (var b = Logo(100, 20))
            using (var ms = new System.IO.MemoryStream())
            {
                IcoWriter.Write(b, ms, new[] { 16, 48, 256 });
                var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ThumblyTests-" + System.Guid.NewGuid().ToString("N") + ".ico");
                System.IO.File.WriteAllBytes(path, ms.ToArray());
                try
                {
                    using (var f = IcoWriter.LoadFrame(path, 32)) Assert.Equal(48, f.Width);
                    using (var f = IcoWriter.LoadFrame(path, 256)) Assert.Equal(256, f.Width);
                    using (var f = IcoWriter.LoadFrame(path, 8)) Assert.Equal(16, f.Width);
                    Assert.Null(IcoWriter.LoadFrame(path + ".missing", 32));
                }
                finally { System.IO.File.Delete(path); }
            }
        }
    }
}
