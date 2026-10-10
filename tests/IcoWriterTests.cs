using System;
using System.Drawing;
using System.IO;
using System.Linq;
using Xunit;

namespace Thumbly.Tests
{
    public class IcoWriterTests
    {
        private static Bitmap MakeImage(int w, int h, Color fill)
        {
            var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp)) g.Clear(fill);
            return bmp;
        }

        /// <summary>Parses an ICO directory: returns (width, height, bpp, size, offset, isPng) per entry.</summary>
        private static (int w, int h, int bpp, int size, int offset, bool png)[] ParseIco(byte[] d)
        {
            Assert.Equal(0, BitConverter.ToUInt16(d, 0));
            Assert.Equal(1, BitConverter.ToUInt16(d, 2));
            int n = BitConverter.ToUInt16(d, 4);
            var entries = new (int, int, int, int, int, bool)[n];
            for (int i = 0; i < n; i++)
            {
                int o = 6 + 16 * i;
                int w = d[o] == 0 ? 256 : d[o];
                int h = d[o + 1] == 0 ? 256 : d[o + 1];
                int bpp = BitConverter.ToUInt16(d, o + 6);
                int size = BitConverter.ToInt32(d, o + 8);
                int off = BitConverter.ToInt32(d, o + 12);
                bool png = d[off] == 0x89 && d[off + 1] == (byte)'P' && d[off + 2] == (byte)'N' && d[off + 3] == (byte)'G';
                entries[i] = (w, h, bpp, size, off, png);
            }
            return entries;
        }

        [Fact]
        public void Writes_all_default_sizes_as_png_entries()
        {
            using (var img = MakeImage(300, 300, Color.Red))
            using (var ms = new MemoryStream())
            {
                IcoWriter.Write(img, ms, IcoWriter.DefaultSizes);
                var entries = ParseIco(ms.ToArray());
                Assert.Equal(IcoWriter.DefaultSizes, entries.Select(e => e.w).ToArray());
                Assert.All(entries, e => Assert.Equal(e.w, e.h));
                Assert.All(entries, e => Assert.Equal(32, e.bpp));
                Assert.All(entries, e => Assert.True(e.png));
            }
        }

        [Fact]
        public void Entry_offsets_and_sizes_are_contiguous_and_match_file_length()
        {
            using (var img = MakeImage(64, 64, Color.Blue))
            using (var ms = new MemoryStream())
            {
                IcoWriter.Write(img, ms, new[] { 16, 32 });
                var d = ms.ToArray();
                var e = ParseIco(d);
                Assert.Equal(6 + 16 * 2, e[0].offset);
                Assert.Equal(e[0].offset + e[0].size, e[1].offset);
                Assert.Equal(e[1].offset + e[1].size, d.Length);
            }
        }

        [Fact]
        public void Windows_can_load_the_resulting_icon()
        {
            using (var img = MakeImage(128, 128, Color.Green))
            using (var ms = new MemoryStream())
            {
                IcoWriter.Write(img, ms, IcoWriter.DefaultSizes);
                ms.Position = 0;
                using (var icon = new Icon(ms, 48, 48))
                    Assert.Equal(48, icon.Width);
            }
        }

        [Fact]
        public void Non_square_image_is_padded_with_transparency_and_centered()
        {
            using (var img = MakeImage(100, 200, Color.Red))
            using (var sq = IcoWriter.ToSquare(img))
            {
                Assert.Equal(200, sq.Width);
                Assert.Equal(200, sq.Height);
                Assert.Equal(0, sq.GetPixel(10, 100).A);          // left padding transparent
                Assert.Equal(0, sq.GetPixel(190, 100).A);         // right padding transparent
                Assert.Equal(Color.Red.ToArgb(), sq.GetPixel(100, 100).ToArgb()); // content in the middle
            }
        }

        [Fact]
        public void Duplicate_and_invalid_sizes_are_ignored_and_sorted()
        {
            using (var img = MakeImage(32, 32, Color.Black))
            using (var ms = new MemoryStream())
            {
                IcoWriter.Write(img, ms, new[] { 48, 16, 16, 0, 512, 32 });
                Assert.Equal(new[] { 16, 32, 48 }, ParseIco(ms.ToArray()).Select(e => e.w).ToArray());
            }
        }

        [Fact]
        public void Throws_when_no_valid_sizes()
        {
            using (var img = MakeImage(32, 32, Color.Black))
            using (var ms = new MemoryStream())
                Assert.Throws<ArgumentException>(() => IcoWriter.Write(img, ms, new int[0]));
        }
    }
}
