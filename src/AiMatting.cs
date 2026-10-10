using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Thumbly
{
    /// <summary>
    /// AI background removal with BiRefNet-lite (ONNX, MIT licence), run on the CPU through ONNX Runtime.
    /// The model sees the whole image stretched to 1024 × 1024 and returns a soft mask (logits) at the same size.
    /// A mask belongs to an image, not to a crop, so it is computed once per image and cached: re-cropping is instant.
    /// Same pre- and post-processing as ImagineStudio's matting.
    /// </summary>
    public static class AiMatting
    {
        public const int Size = 1024;
        public const string ModelName = "BiRefNet-lite";
        public const string DownloadPage = "https://huggingface.co/onnx-community/BiRefNet_lite-ONNX";
        private static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
        private static readonly float[] Std = { 0.229f, 0.224f, 0.225f };
        private const int CacheLimit = 6;

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Lazy<byte[]>> Cache = new Dictionary<string, Lazy<byte[]>>();
        private static readonly LinkedList<string> Recent = new LinkedList<string>();

        public static bool ModelConfigured(string modelPath) => !string.IsNullOrWhiteSpace(modelPath) && File.Exists(modelPath);

        /// <summary>True when the mask for this image is already computed (so using it is instant).</summary>
        public static bool HasMatte(string imagePath, string modelPath)
        {
            lock (Gate)
                return Cache.TryGetValue(Key(imagePath, modelPath), out var lazy) && lazy.IsValueCreated;
        }

        /// <summary>
        /// The 1024 × 1024 mask (0 = background, 255 = subject) for the image at imagePath, computed from img on first
        /// use. Pass the largest copy of the image you have; it is only read, never kept. Concurrent callers for the
        /// same image share one computation. Throws InvalidOperationException with a readable message on failure.
        /// </summary>
        public static byte[] GetMatte(string imagePath, Image img, string modelPath)
        {
            if (!ModelConfigured(modelPath))
                throw new InvalidOperationException("AI background removal needs the " + ModelName + " model. Set its location in Settings.");
            var key = Key(imagePath, modelPath);
            Lazy<byte[]> lazy;
            lock (Gate)
            {
                if (!Cache.TryGetValue(key, out lazy))
                {
                    var input = BuildInput(img); // read the image now, on the caller's thread
                    lazy = new Lazy<byte[]>(() => ToMatte(Run(input, modelPath)), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
                    Cache[key] = lazy;
                }
                Recent.Remove(key);
                Recent.AddFirst(key);
                while (Recent.Count > CacheLimit) { Cache.Remove(Recent.Last.Value); Recent.RemoveLast(); }
            }
            try { return lazy.Value; }
            catch
            {
                lock (Gate) { Cache.Remove(key); Recent.Remove(key); }
                throw;
            }
        }

        /// <summary>A 32 bpp copy of source whose alpha is the mask (existing transparency is kept: the lower wins).</summary>
        public static Bitmap CutOut(Image source, byte[] matte)
        {
            int w = source.Width, h = source.Height;
            var result = IcoWriter.DrawScaled(source, new Rectangle(0, 0, w, h), w, h);
            using (var mask = new Bitmap(Size, Size, PixelFormat.Format32bppArgb))
            {
                var md = mask.LockBits(new Rectangle(0, 0, Size, Size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                var mp = new int[Size * Size];
                for (int i = 0; i < mp.Length; i++) mp[i] = matte[i] << 24 | 0xFFFFFF;
                Marshal.Copy(mp, 0, md.Scan0, mp.Length);
                mask.UnlockBits(md);
                using (var scaled = IcoWriter.DrawScaled(mask, new Rectangle(0, 0, Size, Size), w, h))
                {
                    var sd = scaled.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    var rd = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                    try
                    {
                        var sp = new int[w * h];
                        var rp = new int[w * h];
                        Marshal.Copy(sd.Scan0, sp, 0, sp.Length);
                        Marshal.Copy(rd.Scan0, rp, 0, rp.Length);
                        for (int i = 0; i < rp.Length; i++)
                        {
                            int a = Math.Min(rp[i] >> 24 & 0xFF, sp[i] >> 24 & 0xFF);
                            rp[i] = a == 0 ? 0 : a << 24 | (rp[i] & 0xFFFFFF);
                        }
                        Marshal.Copy(rp, 0, rd.Scan0, rp.Length);
                    }
                    finally { scaled.UnlockBits(sd); result.UnlockBits(rd); }
                }
            }
            return result;
        }

        /// <summary>The model input: the image stretched to 1024 × 1024, ImageNet-normalised, RGB planes (NCHW).</summary>
        internal static float[] BuildInput(Image img)
        {
            const int plane = Size * Size;
            var input = new float[3 * plane];
            using (var bmp = IcoWriter.DrawScaled(img, new Rectangle(0, 0, img.Width, img.Height), Size, Size))
            {
                var d = bmp.LockBits(new Rectangle(0, 0, Size, Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var px = new int[plane];
                Marshal.Copy(d.Scan0, px, 0, plane);
                bmp.UnlockBits(d);
                for (int i = 0; i < plane; i++)
                {
                    int c = px[i];
                    input[i] = ((c >> 16 & 0xFF) / 255f - Mean[0]) / Std[0];
                    input[plane + i] = ((c >> 8 & 0xFF) / 255f - Mean[1]) / Std[1];
                    input[2 * plane + i] = ((c & 0xFF) / 255f - Mean[2]) / Std[2];
                }
            }
            return input;
        }

        /// <summary>Logits to a 0..255 mask (sigmoid).</summary>
        internal static byte[] ToMatte(float[] logits)
        {
            var m = new byte[logits.Length];
            for (int i = 0; i < m.Length; i++) m[i] = (byte)Math.Round(255.0 / (1.0 + Math.Exp(-logits[i])));
            return m;
        }

        private static string Key(string imagePath, string modelPath)
        {
            string stamp = "";
            try { if (imagePath != null && File.Exists(imagePath)) stamp = File.GetLastWriteTimeUtc(imagePath).Ticks.ToString(); } catch { }
            return (imagePath ?? "") + "|" + stamp + "|" + modelPath;
        }

        /// <summary>Kept out of line so ONNX Runtime's assemblies load only when AI removal is actually used.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static float[] Run(float[] input, string modelPath)
        {
            try { return RunCore(modelPath, input); }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DllNotFoundException || ex is TypeInitializationException || ex is BadImageFormatException)
            {
                throw new InvalidOperationException("AI background removal needs onnxruntime.dll and Microsoft.ML.OnnxRuntime.dll next to Thumbly.exe (" + ex.Message + ")", ex);
            }
        }

        /// <summary>Loads the model and checks it has BiRefNet's input and output. Returns null when fine, else the problem.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Check(string modelPath)
        {
            if (!ModelConfigured(modelPath)) return "File not found.";
            try { return CheckCore(modelPath); }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DllNotFoundException || ex is TypeInitializationException || ex is BadImageFormatException)
            { return "onnxruntime.dll / Microsoft.ML.OnnxRuntime.dll are missing next to Thumbly.exe (" + ex.Message + ")"; }
            catch (Exception ex) { return ex.Message; }
        }

        // Separate methods so a missing ONNX Runtime assembly fails inside the callers' try blocks, not while they JIT.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static float[] RunCore(string modelPath, float[] input) => OnnxModel.Run(modelPath, input);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string CheckCore(string modelPath) => OnnxModel.Check(modelPath);
    }
}
