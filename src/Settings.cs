using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Thumbly
{
    /// <summary>Plain key=value settings file in %AppData%\Thumbly\settings.txt (no JSON dependency needed on net48).</summary>
    public class Settings
    {
        public string IconDirectory { get; set; }
        public string ImageDirectory { get; set; }
        public bool SaveImageCopy { get; set; } = true;
        public bool OpenFolderAfterSave { get; set; } = false;
        public int[] Sizes { get; set; } = IcoWriter.DefaultSizes;

        public static string DefaultPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Thumbly", "settings.txt");

        public static Settings CreateDefault()
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            return new Settings
            {
                IconDirectory = Path.Combine(pictures, "Thumbly", "Icons"),
                ImageDirectory = Path.Combine(pictures, "Thumbly", "Images"),
            };
        }

        public static Settings Load(string path = null)
        {
            path = path ?? DefaultPath;
            var s = CreateDefault();
            if (!File.Exists(path)) return s;
            foreach (var line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                var val = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "IconDirectory": if (val.Length > 0) s.IconDirectory = val; break;
                    case "ImageDirectory": if (val.Length > 0) s.ImageDirectory = val; break;
                    case "SaveImageCopy": s.SaveImageCopy = val == "true"; break;
                    case "OpenFolderAfterSave": s.OpenFolderAfterSave = val == "true"; break;
                    case "Sizes":
                        var parsed = val.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => int.TryParse(x.Trim(), out var n) ? n : 0)
                            .Where(n => n > 0).ToArray();
                        if (parsed.Length > 0) s.Sizes = parsed;
                        break;
                }
            }
            return s;
        }

        public void Save(string path = null)
        {
            path = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var lines = new List<string>
            {
                "IconDirectory=" + IconDirectory,
                "ImageDirectory=" + ImageDirectory,
                "SaveImageCopy=" + (SaveImageCopy ? "true" : "false"),
                "OpenFolderAfterSave=" + (OpenFolderAfterSave ? "true" : "false"),
                "Sizes=" + string.Join(",", Sizes),
            };
            File.WriteAllLines(path, lines);
        }
    }
}
