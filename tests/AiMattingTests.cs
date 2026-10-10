using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Xunit;

namespace Thumbly.Tests
{
    /// <summary>
    /// The AI path through ONNX Runtime, with a tiny stand-in model that has BiRefNet's input and output:
    /// light pixels come out opaque, dark ones transparent. The real 224 MB model is not needed.
    /// </summary>
    public class AiMattingTests : IDisposable
    {
        private static readonly string Fixture = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "birefnet-fixture.onnx");
        private readonly TempDir _t = new TempDir();
        public void Dispose() => _t.Dispose();

        /// <summary>White left half, black right half, saved so the mask cache has a real path to key on.</summary>
        private (string path, Bitmap bmp) HalfAndHalf(string name, int w = 200, int h = 100)
        {
            var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.Black);
                g.FillRectangle(Brushes.White, 0, 0, w / 2, h);
            }
            var path = _t.Path_(name);
            b.Save(path, ImageFormat.Png);
            return (path, b);
        }

        [Fact]
        public void Fixture_is_copied_next_to_the_tests() => Assert.True(File.Exists(Fixture), Fixture);

        [Fact]
        public void ToMatte_is_the_sigmoid_of_the_logits()
        {
            var m = AiMatting.ToMatte(new[] { 0f, 20f, -20f, 2f });
            Assert.Equal(128, m[0]);
            Assert.Equal(255, m[1]);
            Assert.Equal(0, m[2]);
            Assert.Equal(225, m[3]);
        }

        [Fact]
        public void BuildInput_is_imagenet_normalised_rgb_planes()
        {
            using (var b = new Bitmap(10, 10))
            {
                using (var g = Graphics.FromImage(b)) g.Clear(Color.FromArgb(255, 0, 0));
                var input = AiMatting.BuildInput(b);
                const int plane = AiMatting.Size * AiMatting.Size;
                Assert.Equal(3 * plane, input.Length);
                Assert.Equal((1f - 0.485f) / 0.229f, input[0], 3);          // R
                Assert.Equal((0f - 0.456f) / 0.224f, input[plane], 3);      // G
                Assert.Equal((0f - 0.406f) / 0.225f, input[2 * plane], 3);  // B
            }
        }

        [Fact]
        public void Model_check_accepts_birefnet_shaped_models()
        {
            Assert.Null(AiMatting.Check(Fixture));
            Assert.Equal("File not found.", AiMatting.Check(_t.Path_("missing.onnx")));
        }

        [Fact]
        public void Model_check_rejects_a_file_that_is_not_a_model()
        {
            var junk = _t.Path_("junk.onnx");
            File.WriteAllText(junk, "not a model");
            Assert.NotNull(AiMatting.Check(junk));
        }

        [Fact]
        public void CutOut_keeps_the_subject_and_clears_the_background()
        {
            var (path, b) = HalfAndHalf("half.png");
            using (b)
            {
                var matte = AiMatting.GetMatte(path, b, Fixture);
                Assert.True(AiMatting.HasMatte(path, Fixture));
                using (var cut = AiMatting.CutOut(b, matte))
                {
                    Assert.Equal(b.Size, cut.Size);
                    Assert.Equal(255, cut.GetPixel(20, 50).A);
                    Assert.Equal(Color.White.ToArgb(), cut.GetPixel(20, 50).ToArgb());
                    Assert.Equal(0, cut.GetPixel(180, 50).A);
                }
            }
        }

        [Fact]
        public void The_mask_is_computed_once_per_image()
        {
            var (path, b) = HalfAndHalf("once.png");
            using (b)
            {
                var first = AiMatting.GetMatte(path, b, Fixture);
                var again = AiMatting.GetMatte(path, null, Fixture); // cached: the image is not even read
                Assert.Same(first, again);
            }
        }

        [Fact]
        public void Prepare_with_ai_masks_then_crops()
        {
            var (path, b) = HalfAndHalf("prep.png");
            using (b)
            {
                var src = new IconSource { ImagePath = path, RemoveBackground = true, BackgroundAi = true, Crop = new RectangleF(0.25f, 0, 0.5f, 1f) };
                using (var r = ImagePipeline.Prepare(b, src, Fixture))
                {
                    Assert.Equal(new Size(100, 100), r.Size);
                    Assert.Equal(255, r.GetPixel(10, 50).A);  // white half kept
                    Assert.Equal(0, r.GetPixel(90, 50).A);    // black half removed
                }
            }
        }

        [Fact]
        public void Missing_model_gives_a_readable_error()
        {
            var (path, b) = HalfAndHalf("nomodel.png");
            using (b)
            {
                var src = new IconSource { ImagePath = path, RemoveBackground = true, BackgroundAi = true };
                var ex = Assert.Throws<InvalidOperationException>(() => ImagePipeline.Prepare(b, src, null));
                Assert.Contains("Settings", ex.Message);
            }
        }

        [Fact]
        public void Assign_builds_the_folder_icon_with_ai_removal()
        {
            var (path, b) = HalfAndHalf("assign.png", 100, 100);
            b.Dispose();
            var settings = new Settings
            {
                IconDirectory = _t.Path_("icons"), ImageDirectory = _t.Path_("images"), SaveImageCopy = false,
                Sizes = new[] { 32, 256 }, MattingModelPath = Fixture,
            };
            var cat = FolderCatalog.Load(_t.Path_("folders.txt"));
            var e = FolderAssigner.Assign(_t.Folder("AiFolder"), new IconSource { ImagePath = path, RemoveBackground = true, BackgroundAi = true }, settings, cat);
            using (var frame = IcoWriter.LoadFrame(e.IconPath, 256))
            {
                Assert.Equal(255, frame.GetPixel(30, 128).A);
                Assert.Equal(0, frame.GetPixel(220, 128).A);
            }
            Assert.True(FolderCatalog.Load(_t.Path_("folders.txt")).Entries[0].Source.BackgroundAi);
        }
    }
}
