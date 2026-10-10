using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Thumbly
{
    public class ConvertResult
    {
        public string IconPath;
        public string ImagePath; // null when no copy was saved
    }

    /// <summary>Turns a source image into an .ico in the icon folder, optionally copying the source into the image folder.</summary>
    public static class Converter
    {
        public static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

        public static bool IsSupported(string path)
        {
            var ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return false;
            return Array.IndexOf(SupportedExtensions, ext.ToLowerInvariant()) >= 0;
        }

        public static ConvertResult ConvertFile(string sourcePath, Settings settings)
        {
            var baseName = Path.GetFileNameWithoutExtension(sourcePath);
            using (var img = LoadImage(sourcePath))
            {
                var result = WriteIcon(img, baseName, settings);
                if (settings.SaveImageCopy)
                {
                    Directory.CreateDirectory(settings.ImageDirectory);
                    var dest = UniquePath(settings.ImageDirectory, baseName, Path.GetExtension(sourcePath));
                    File.Copy(sourcePath, dest);
                    result.ImagePath = dest;
                }
                return result;
            }
        }

        /// <summary>For in-memory images (clipboard paste). The image copy is saved as PNG.</summary>
        public static ConvertResult ConvertBitmap(Image img, string baseName, Settings settings)
        {
            var result = WriteIcon(img, baseName, settings);
            if (settings.SaveImageCopy)
            {
                Directory.CreateDirectory(settings.ImageDirectory);
                var dest = UniquePath(settings.ImageDirectory, baseName, ".png");
                img.Save(dest, ImageFormat.Png);
                result.ImagePath = dest;
            }
            return result;
        }

        private static ConvertResult WriteIcon(Image img, string baseName, Settings settings)
        {
            Directory.CreateDirectory(settings.IconDirectory);
            var icoPath = UniquePath(settings.IconDirectory, baseName, ".ico");
            using (var fs = File.Create(icoPath))
                IcoWriter.Write(img, fs, settings.Sizes);
            return new ConvertResult { IconPath = icoPath };
        }

        /// <summary>Loads via a memory copy so the file on disk is never locked.</summary>
        public static Image LoadImage(string path)
        {
            var ms = new MemoryStream(File.ReadAllBytes(path));
            return Image.FromStream(ms); // GDI+ needs the stream alive for the image's lifetime; it is collected with the image
        }

        public static string UniquePath(string dir, string baseName, string ext)
        {
            var candidate = Path.Combine(dir, baseName + ext);
            int n = 2;
            while (File.Exists(candidate))
                candidate = Path.Combine(dir, baseName + " (" + n++ + ")" + ext);
            return candidate;
        }
    }
}
