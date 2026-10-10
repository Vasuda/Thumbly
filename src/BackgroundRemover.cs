using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Thumbly
{
    /// <summary>
    /// CPU background removal for flat backgrounds (logos, clipart, renders). The background colours are the ones
    /// that dominate the image border; every region connected to the border and within tolerance of one of those
    /// colours becomes transparent. Enclosed areas of the same colour inside the subject are kept, unlike a colour key.
    /// Photos with busy backgrounds will not come out well; that needs an ML matting model, which Thumbly does not ship.
    /// </summary>
    public static class BackgroundRemover
    {
        /// <summary>A border colour bucket is treated as background when it holds at least this share of border pixels.</summary>
        private const double KeyShare = 0.15;

        /// <summary>In place on a 32bpp ARGB bitmap. Tolerance is the max per-channel RGB distance (0..128).</summary>
        public static void Remove(Bitmap bmp, int tolerance)
        {
            if (bmp.PixelFormat != PixelFormat.Format32bppArgb)
                throw new ArgumentException("Expected a 32bpp ARGB bitmap.", nameof(bmp));
            int w = bmp.Width, h = bmp.Height;
            var rect = new Rectangle(0, 0, w, h);
            var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                if (data.Stride != w * 4) throw new InvalidOperationException("Unexpected stride.");
                var px = new int[w * h];
                Marshal.Copy(data.Scan0, px, 0, px.Length);
                var bg = FindBackground(px, w, h, tolerance);
                if (bg == null) return;
                Feather(px, bg, w, h);
                Marshal.Copy(px, 0, data.Scan0, px.Length);
            }
            finally { bmp.UnlockBits(data); }
        }

        /// <summary>Returns the background mask (true = background), or null when the border has no dominant colour.</summary>
        private static bool[] FindBackground(int[] px, int w, int h, int tol)
        {
            var keys = BorderKeys(px, w, h);
            if (keys.Count == 0) return null;

            var bg = new bool[px.Length];
            var queue = new Queue<int>();
            bool IsBg(int c)
            {
                if ((c >> 24 & 0xFF) == 0) return true; // already transparent
                foreach (var k in keys) if (Near(c, k, tol)) return true;
                return false;
            }
            void Visit(int i)
            {
                if (bg[i] || !IsBg(px[i])) return;
                bg[i] = true;
                queue.Enqueue(i);
            }

            for (int x = 0; x < w; x++) { Visit(x); Visit((h - 1) * w + x); }
            for (int y = 0; y < h; y++) { Visit(y * w); Visit(y * w + w - 1); }

            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % w, y = i / w;
                if (x > 0) Visit(i - 1);
                if (x < w - 1) Visit(i + 1);
                if (y > 0) Visit(i - w);
                if (y < h - 1) Visit(i + w);
            }
            return bg;
        }

        /// <summary>Average colours of the 5-bit-per-channel buckets that dominate the opaque border pixels.</summary>
        private static List<int> BorderKeys(int[] px, int w, int h)
        {
            var count = new Dictionary<int, long[]>(); // bucket -> [n, sumR, sumG, sumB]
            int total = 0;
            void Add(int c)
            {
                if ((c >> 24 & 0xFF) < 128) return;
                int r = c >> 16 & 0xFF, g = c >> 8 & 0xFF, b = c & 0xFF;
                int key = (r >> 3) << 10 | (g >> 3) << 5 | (b >> 3);
                if (!count.TryGetValue(key, out var s)) count[key] = s = new long[4];
                s[0]++; s[1] += r; s[2] += g; s[3] += b;
                total++;
            }
            for (int x = 0; x < w; x++) { Add(px[x]); if (h > 1) Add(px[(h - 1) * w + x]); }
            for (int y = 1; y < h - 1; y++) { Add(px[y * w]); if (w > 1) Add(px[y * w + w - 1]); }

            var keys = new List<int>();
            if (total == 0) return keys;
            long[] best = null;
            foreach (var s in count.Values)
            {
                if (best == null || s[0] > best[0]) best = s;
                if (s[0] >= total * KeyShare) keys.Add(Average(s));
            }
            if (keys.Count == 0) keys.Add(Average(best));
            return keys;
        }

        private static int Average(long[] s) =>
            (int)(s[1] / s[0]) << 16 | (int)(s[2] / s[0]) << 8 | (int)(s[3] / s[0]);

        private static bool Near(int a, int b, int tol) =>
            Math.Abs((a >> 16 & 0xFF) - (b >> 16 & 0xFF)) <= tol
            && Math.Abs((a >> 8 & 0xFF) - (b >> 8 & 0xFF)) <= tol
            && Math.Abs((a & 0xFF) - (b & 0xFF)) <= tol;

        /// <summary>Background becomes transparent; foreground pixels on the boundary fade by their share of foreground neighbours.</summary>
        private static void Feather(int[] px, bool[] bg, int w, int h)
        {
            var alpha = new byte[px.Length];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                int a = px[i] >> 24 & 0xFF;
                if (bg[i]) { alpha[i] = 0; continue; }
                int fg = 0, n = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                    n++;
                    if (!bg[yy * w + xx]) fg++;
                }
                alpha[i] = (byte)(a * fg / n);
            }
            for (int i = 0; i < px.Length; i++)
                px[i] = alpha[i] == 0 ? 0 : (alpha[i] << 24) | (px[i] & 0x00FFFFFF);
        }
    }
}
