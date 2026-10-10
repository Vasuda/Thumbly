using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace Thumbly
{
    /// <summary>
    /// Image + folder in, folder icon out: builds the .ico (central copy in the .ico folder), applies it to the folder
    /// via <see cref="FolderIconService"/>, and records it in the <see cref="FolderCatalog"/>.
    /// </summary>
    public static class FolderAssigner
    {
        public static FolderEntry Assign(string folder, IconSource source, Settings settings, FolderCatalog catalog)
        {
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Folder not found: " + folder);
            if (source?.ImagePath == null || !File.Exists(source.ImagePath)) throw new FileNotFoundException("Image not found.", source?.ImagePath);

            var src = source.Clone();
            src.ImagePath = Converter.KeepSourceImage(src.ImagePath, settings);

            Directory.CreateDirectory(settings.IconDirectory);
            var name = SafeName(FolderCatalog.FolderName(folder));
            var central = Converter.UniquePath(settings.IconDirectory, name, ".ico");
            BuildIcon(src, central, settings);

            var existing = catalog.Find(folder);
            var entry = new FolderEntry
            {
                Id = existing?.Id ?? FolderEntry.NewId(),
                Folder = FolderCatalog.Normalize(folder),
                IconPath = central,
                Source = src,
            };
            bool newlyReadOnly = FolderIconService.Apply(entry.Folder, central, entry.Id);
            entry.SetReadOnly = newlyReadOnly || (existing?.SetReadOnly ?? false);
            entry.Applied = DateTimeOffset.Now;
            catalog.Upsert(entry);
            catalog.Save();
            return entry;
        }

        /// <summary>
        /// Rebuilds the central copy from the entry's source (overwriting it) and re-applies it to the folder.
        /// When the source image is gone, the existing central copy is applied as-is.
        /// </summary>
        public static void Reapply(FolderEntry entry, Settings settings, FolderCatalog catalog)
        {
            if (!Directory.Exists(entry.Folder)) throw new DirectoryNotFoundException("Folder not found: " + entry.Folder);

            bool haveImage = entry.Source?.ImagePath != null && File.Exists(entry.Source.ImagePath);
            bool haveCentral = !string.IsNullOrEmpty(entry.IconPath) && File.Exists(entry.IconPath);
            if (haveImage)
            {
                if (string.IsNullOrEmpty(entry.IconPath))
                {
                    Directory.CreateDirectory(settings.IconDirectory);
                    entry.IconPath = Converter.UniquePath(settings.IconDirectory, SafeName(entry.Name), ".ico");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(entry.IconPath));
                BuildIcon(entry.Source, entry.IconPath, settings);
            }
            else if (!haveCentral)
                throw new FileNotFoundException("Neither the source image nor the central .ico exists any more.");

            bool newlyReadOnly = FolderIconService.Apply(entry.Folder, entry.IconPath, entry.Id);
            entry.SetReadOnly |= newlyReadOnly;
            entry.Applied = DateTimeOffset.Now;
            catalog.Upsert(entry);
            catalog.Save();
        }

        /// <summary>Restores the folder's default icon and forgets the entry. The central copy is kept.</summary>
        public static void RemoveIcon(FolderEntry entry, FolderCatalog catalog)
        {
            FolderIconService.Remove(entry.Folder, entry.SetReadOnly);
            catalog.Remove(entry.Id);
            catalog.Save();
        }

        /// <summary>
        /// Saves an in-memory image (clipboard, drag from a browser) as a PNG in the image folder so it can be edited and
        /// rebuilt later. Named after the folder when one is given, otherwise "clipboard".
        /// </summary>
        public static string SaveBitmapSource(Image img, string folder, Settings settings)
        {
            var dir = SourceDirectory(settings);
            Directory.CreateDirectory(dir);
            var prefix = string.IsNullOrEmpty(folder) ? "clipboard" : SafeName(FolderCatalog.FolderName(folder));
            var path = Converter.UniquePath(dir, prefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), ".png");
            img.Save(path, ImageFormat.Png);
            return path;
        }

        /// <summary>Builds the .ico at icoPath from the source (crop, background removal, sizes).</summary>
        public static void BuildIcon(IconSource source, string icoPath, Settings settings)
        {
            using (var img = Converter.LoadImage(source.ImagePath))
            using (var prepared = ImagePipeline.Prepare(img, source, settings.MattingModelPath))
            using (var ms = new MemoryStream())
            {
                IcoWriter.Write(prepared, ms, settings.Sizes);
                File.WriteAllBytes(icoPath, ms.ToArray()); // only touch the file once the icon built successfully
            }
        }

        private static string SourceDirectory(Settings settings) =>
            !string.IsNullOrEmpty(settings.ImageDirectory)
                ? settings.ImageDirectory
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Thumbly", "Images");

        public static string SafeName(string name)
        {
            var bad = Path.GetInvalidFileNameChars();
            var s = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
            return s.Length == 0 ? "folder" : s;
        }
    }
}
