using System;
using System.Collections.Generic;
using System.Drawing;
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
        /// <summary>"Set icon with Thumbly" in Explorer's folder right-click menu.</summary>
        public bool ContextMenu { get; set; } = false;
        /// <summary>Open the image editor (crop / background) as soon as an image is added.</summary>
        public bool AlwaysEdit { get; set; } = false;
        /// <summary>Create and set the folder icon as soon as both a folder and an image are present.</summary>
        public bool AutoSet { get; set; } = false;
        /// <summary>BiRefNet-lite ONNX file for AI background removal; empty means only flat-colour removal is offered.</summary>
        public string MattingModelPath { get; set; } = "";
        /// <summary>Main window size, remembered between runs.</summary>
        public Size WindowSize { get; set; } = DefaultWindowSize;

        public static readonly Size DefaultWindowSize = new Size(1000, 620);

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

        public Settings Clone() => (Settings)MemberwiseClone();

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
                    case "ContextMenu": s.ContextMenu = val == "true"; break;
                    case "AlwaysEdit":
                    case "AdjustBeforeApply": // name used by the first folder-icon version
                        s.AlwaysEdit = val == "true"; break;
                    case "AutoSet": s.AutoSet = val == "true"; break;
                    case "MattingModelPath": s.MattingModelPath = val; break;
                    case "WindowSize":
                        var wh = val.Split(',');
                        if (wh.Length == 2 && int.TryParse(wh[0].Trim(), out var w) && int.TryParse(wh[1].Trim(), out var h) && w > 0 && h > 0)
                            s.WindowSize = new Size(w, h);
                        break;
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
                "ContextMenu=" + (ContextMenu ? "true" : "false"),
                "AlwaysEdit=" + (AlwaysEdit ? "true" : "false"),
                "AutoSet=" + (AutoSet ? "true" : "false"),
                "MattingModelPath=" + (MattingModelPath ?? ""),
                "WindowSize=" + WindowSize.Width + "," + WindowSize.Height,
            };
            File.WriteAllLines(path, lines);
        }
    }
}
