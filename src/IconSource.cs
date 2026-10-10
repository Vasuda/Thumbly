using System;
using System.Drawing;
using System.Globalization;

namespace Thumbly
{
    /// <summary>What a folder icon is built from: the image, an optional square crop, and background removal.</summary>
    public class IconSource
    {
        public const int DefaultTolerance = 24;
        public const int MaxTolerance = 128;

        /// <summary>The file the icon is built from (the saved copy when "save a copy" is on).</summary>
        public string ImagePath;
        /// <summary>Normalized 0..1 rectangle; Empty means the whole image.</summary>
        public RectangleF Crop = RectangleF.Empty;
        public bool RemoveBackground;
        /// <summary>With RemoveBackground: true uses the AI model (BiRefNet), false the flat-colour flood fill.</summary>
        public bool BackgroundAi;
        /// <summary>Flat-colour removal only.</summary>
        public int Tolerance = DefaultTolerance;

        public IconSource Clone() => (IconSource)MemberwiseClone();

        /// <summary>"crop=x,y,w,h;bg=1;bgm=ai;tol=24". The image path is stored separately.</summary>
        public string Serialize()
        {
            var inv = CultureInfo.InvariantCulture;
            var parts = new System.Collections.Generic.List<string>();
            if (!Crop.IsEmpty)
                parts.Add(string.Format(inv, "crop={0:0.#####},{1:0.#####},{2:0.#####},{3:0.#####}", Crop.X, Crop.Y, Crop.Width, Crop.Height));
            parts.Add("bg=" + (RemoveBackground ? "1" : "0"));
            parts.Add("bgm=" + (BackgroundAi ? "ai" : "flat"));
            parts.Add("tol=" + Tolerance.ToString(inv));
            return string.Join(";", parts);
        }

        public static IconSource Parse(string imagePath, string text)
        {
            var s = new IconSource { ImagePath = imagePath };
            if (string.IsNullOrEmpty(text)) return s;
            var inv = CultureInfo.InvariantCulture;
            foreach (var part in text.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                var key = part.Substring(0, eq).Trim();
                var val = part.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "crop":
                        var n = val.Split(',');
                        if (n.Length == 4
                            && float.TryParse(n[0], NumberStyles.Float, inv, out var x)
                            && float.TryParse(n[1], NumberStyles.Float, inv, out var y)
                            && float.TryParse(n[2], NumberStyles.Float, inv, out var w)
                            && float.TryParse(n[3], NumberStyles.Float, inv, out var h)
                            && w > 0 && h > 0)
                            s.Crop = new RectangleF(x, y, w, h);
                        break;
                    case "bg": s.RemoveBackground = val == "1"; break;
                    case "bgm": s.BackgroundAi = val == "ai"; break;
                    case "tol":
                        if (int.TryParse(val, NumberStyles.Integer, inv, out var t))
                            s.Tolerance = Math.Max(0, Math.Min(MaxTolerance, t));
                        break;
                }
            }
            return s;
        }
    }
}
