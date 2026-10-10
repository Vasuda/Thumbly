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

        public static Bitmap Resize(Image square, int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
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
                    g.DrawImage(square, new Rectangle(0, 0, size, size), 0, 0, square.Width, square.Height, GraphicsUnit.Pixel, attrs);
                }
            }
            return bmp;
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
