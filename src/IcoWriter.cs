using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Thumbly
{
    /// <summary>Builds a multi-size Windows .ico (PNG-compressed entries) from a source bitmap.</summary>
    public static class IcoWriter
    {
        public static readonly int[] DefaultSizes = { 16, 32, 48, 64, 128, 256 };

        public static void Write(Image source, Stream output, IEnumerable<int> sizes)
        {
            var layers = new List<byte[]>();
            var dims = new List<int>();
            using (var square = ToSquare(source))
            {
                foreach (var size in Distinct(sizes))
                {
                    using (var bmp = Resize(square, size))
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        layers.Add(ms.ToArray());
                        dims.Add(size);
                    }
                }
            }
            if (layers.Count == 0) throw new ArgumentException("No icon sizes selected.", nameof(sizes));

            var w = new BinaryWriter(output);
            w.Write((ushort)0);               // reserved
            w.Write((ushort)1);               // type: icon
            w.Write((ushort)layers.Count);
            int offset = 6 + 16 * layers.Count;
            for (int i = 0; i < layers.Count; i++)
            {
                int d = dims[i];
                w.Write((byte)(d >= 256 ? 0 : d)); // 0 means 256
                w.Write((byte)(d >= 256 ? 0 : d));
                w.Write((byte)0);             // palette count
                w.Write((byte)0);             // reserved
                w.Write((ushort)1);           // color planes
                w.Write((ushort)32);          // bits per pixel
                w.Write(layers[i].Length);
                w.Write(offset);
                offset += layers[i].Length;
            }
            foreach (var png in layers) w.Write(png);
            w.Flush();
        }

        /// <summary>Pads a non-square image onto a transparent square canvas, centered.</summary>
        public static Bitmap ToSquare(Image source)
        {
            int side = Math.Max(source.Width, source.Height);
            var bmp = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.DrawImage(source, (side - source.Width) / 2, (side - source.Height) / 2, source.Width, source.Height);
            }
            return bmp;
        }

        public static Bitmap Resize(Image square, int size) =>
            DrawScaled(square, new Rectangle(0, 0, square.Width, square.Height), size, size);

        /// <summary>Draws srcRect of the source into a new 32bpp bitmap of the given size with high-quality resampling.</summary>
        public static Bitmap DrawScaled(Image source, Rectangle srcRect, int width, int height)
        {
            var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var attrs = new ImageAttributes())
                {
                    attrs.SetWrapMode(WrapMode.TileFlipXY); // avoids the half-pixel edge halo GDI+ otherwise produces
                    g.DrawImage(source, new Rectangle(0, 0, width, height), srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height, GraphicsUnit.Pixel, attrs);
                }
            }
            return bmp;
        }

        /// <summary>
        /// Reads the layer closest to <paramref name="size"/> (preferring larger) from an .ico as a bitmap, or null when
        /// the file cannot be read. PNG layers are decoded directly; anything else goes through System.Drawing.Icon.
        /// </summary>
        public static Bitmap LoadFrame(string path, int size)
        {
            try
            {
                var d = File.ReadAllBytes(path);
                if (d.Length < 6 || BitConverter.ToUInt16(d, 2) != 1) return null;
                int n = BitConverter.ToUInt16(d, 4), best = -1, bestDim = 0;
                for (int i = 0; i < n && 6 + 16 * i + 16 <= d.Length; i++)
                {
                    int dim = d[6 + 16 * i] == 0 ? 256 : d[6 + 16 * i];
                    bool better = best < 0
                        || (dim >= size && (bestDim < size || dim < bestDim))
                        || (dim < size && bestDim < size && dim > bestDim);
                    if (better) { best = i; bestDim = dim; }
                }
                if (best < 0) return null;
                int o = 6 + 16 * best;
                int len = BitConverter.ToInt32(d, o + 8), off = BitConverter.ToInt32(d, o + 12);
                if (off < 0 || len <= 8 || off + len > d.Length) return null;
                bool png = d[off] == 0x89 && d[off + 1] == (byte)'P' && d[off + 2] == (byte)'N' && d[off + 3] == (byte)'G';
                if (png)
                    using (var ms = new MemoryStream(d, off, len))
                    using (var img = Image.FromStream(ms))
                        return new Bitmap(img);
                using (var icon = new Icon(path, size, size))
                    return icon.ToBitmap();
            }
            catch { return null; }
        }

        private static IEnumerable<int> Distinct(IEnumerable<int> sizes)
        {
            var seen = new SortedSet<int>();
            foreach (var s in sizes)
                if (s >= 1 && s <= 256) seen.Add(s);
            return seen;
        }
    }
}
