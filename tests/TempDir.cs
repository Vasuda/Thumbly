using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Thumbly.Tests
{
    /// <summary>
    /// A temp folder owned by one test class. Dispose clears the attributes the folder-icon tests set
    /// (ReadOnly on folders, Hidden/System on desktop.ini) and deletes only this folder.
    /// </summary>
    public sealed class TempDir : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ThumblyTests-" + Guid.NewGuid().ToString("N"));

        public TempDir() { Directory.CreateDirectory(Root); }

        public string Path_(params string[] parts) => Path.Combine(Root, Path.Combine(parts));

        public string Folder(string name)
        {
            var p = Path.Combine(Root, name);
            Directory.CreateDirectory(p);
            return p;
        }

        public string Png(string name, int w = 40, int h = 40, Color? fill = null)
        {
            var p = Path.Combine(Root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            using (var bmp = new Bitmap(w, h))
            {
                using (var g = Graphics.FromImage(bmp)) g.Clear(fill ?? Color.Orange);
                bmp.Save(p, ImageFormat.Png);
            }
            return p;
        }

        public void Dispose()
        {
            try
            {
                if (!Directory.Exists(Root)) return;
                foreach (var f in Directory.GetFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                foreach (var d in Directory.GetDirectories(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(d, FileAttributes.Directory);
                Directory.Delete(Root, true);
            }
            catch { }
        }
    }
}
