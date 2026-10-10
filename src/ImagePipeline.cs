using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace Thumbly
{
    /// <summary>Turns a source image plus an <see cref="IconSource"/> into the bitmap the icon is built from.</summary>
    public static class ImagePipeline
    {
        /// <summary>Everything after the crop works on at most this many pixels per side; icons never exceed 256.</summary>
        public const int MaxSide = 1024;

        /// <summary>
        /// Crop (the frame may extend past the image; that part becomes transparent padding), downscale to MaxSide,
        /// optionally remove the background (before padding, so the padding never confuses it). Caller disposes.
        /// AI removal masks the whole image first (the mask is cached per image), then crops; it needs modelPath.
        /// </summary>
        public static Bitmap Prepare(Image source, IconSource s, string modelPath = null)
        {
            if (s != null && s.RemoveBackground && s.BackgroundAi)
                using (var cut = AiMatting.CutOut(source, AiMatting.GetMatte(s.ImagePath, source, modelPath)))
                    return CropAndScale(cut, s, flatRemoval: false);
            return CropAndScale(source, s, flatRemoval: s != null && s.RemoveBackground);
        }

        private static Bitmap CropAndScale(Image source, IconSource s, bool flatRemoval)
        {
            var full = new RectangleF(0, 0, source.Width, source.Height);
            var frame = CropFrame(s, source.Size);

            double scale = Math.Min(1.0, MaxSide / Math.Max(frame.Width, frame.Height));
            int outW = Math.Max(1, (int)Math.Round(frame.Width * scale));
            int outH = Math.Max(1, (int)Math.Round(frame.Height * scale));

            var inside = RectangleF.Intersect(frame, full);
            if (inside.Width < 1 || inside.Height < 1) return new Bitmap(outW, outH, PixelFormat.Format32bppArgb); // frame misses the image

            var src = Rectangle.Round(inside);
            int iw = Math.Max(1, (int)Math.Round(inside.Width * scale));
            int ih = Math.Max(1, (int)Math.Round(inside.Height * scale));
            var part = IcoWriter.DrawScaled(source, src, iw, ih);
            if (flatRemoval) BackgroundRemover.Remove(part, s.Tolerance);

            if (iw == outW && ih == outH) return part;
            using (part)
            {
                var canvas = new Bitmap(outW, outH, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(canvas))
                {
                    int ox = (int)Math.Round((inside.X - frame.X) * scale);
                    int oy = (int)Math.Round((inside.Y - frame.Y) * scale);
                    g.DrawImageUnscaled(part, ox, oy);
                }
                return canvas;
            }
        }

        /// <summary>The crop frame in source pixels; Empty crop means the whole image. Never smaller than 1×1.</summary>
        public static RectangleF CropFrame(IconSource s, Size size)
        {
            if (s == null || s.Crop.IsEmpty) return new RectangleF(0, 0, size.Width, size.Height);
            var n = s.Crop;
            return new RectangleF(n.X * size.Width, n.Y * size.Height, Math.Max(1, n.Width * size.Width), Math.Max(1, n.Height * size.Height));
        }
    }
}
