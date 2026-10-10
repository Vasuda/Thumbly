using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace Thumbly.Tests
{
    /// <summary>
    /// Opt-in check against the real BiRefNet-lite model, which is too big for the repo. Set THUMBLY_REAL_MODEL to the
    /// .onnx file; optionally THUMBLY_REAL_IMAGE (a photo) and THUMBLY_REAL_OUT (where to save the cut-out PNG).
    /// Without THUMBLY_REAL_MODEL the test does nothing.
    /// </summary>
    [Trait("Category", "RealModel")]
    public class RealModelTests
    {
        private readonly ITestOutputHelper _out;
        public RealModelTests(ITestOutputHelper output) { _out = output; }

        [Fact]
        public void Real_model_cuts_out_a_subject_when_THUMBLY_REAL_MODEL_is_set()
        {
            var model = Environment.GetEnvironmentVariable("THUMBLY_REAL_MODEL");
            if (string.IsNullOrEmpty(model)) { _out.WriteLine("THUMBLY_REAL_MODEL not set: skipped."); return; }

            Assert.Null(AiMatting.Check(model));
            var imagePath = Environment.GetEnvironmentVariable("THUMBLY_REAL_IMAGE");
            using (var img = string.IsNullOrEmpty(imagePath) ? Synthetic() : Converter.LoadImage(imagePath))
            {
                var sw = Stopwatch.StartNew();
                var matte = AiMatting.GetMatte(imagePath ?? "synthetic-" + Guid.NewGuid(), img, model);
                _out.WriteLine("mask computed in " + sw.ElapsedMilliseconds + " ms");
                int fg = 0, bg = 0;
                foreach (var m in matte) { if (m > 200) fg++; else if (m < 55) bg++; }
                _out.WriteLine("subject " + fg * 100 / matte.Length + "%, background " + bg * 100 / matte.Length + "%");
                Assert.True(fg > matte.Length / 50 && bg > matte.Length / 50, "the mask should contain both subject and background");

                var outPath = Environment.GetEnvironmentVariable("THUMBLY_REAL_OUT");
                if (!string.IsNullOrEmpty(outPath))
                    using (var cut = AiMatting.CutOut(img, matte)) cut.Save(outPath, ImageFormat.Png);
            }
        }

        /// <summary>A simple figure on a busy two-tone background.</summary>
        private static Bitmap Synthetic()
        {
            var b = new Bitmap(800, 800);
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.SaddleBrown);
                for (int i = 0; i < 800; i += 40) g.FillRectangle(Brushes.Peru, i, 0, 20, 800);
                g.FillEllipse(Brushes.Bisque, 300, 120, 200, 240);
                g.FillRectangle(Brushes.SteelBlue, 240, 340, 320, 460);
            }
            return b;
        }
    }
}
