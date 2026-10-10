using System.Drawing;
using System.Windows.Forms;

namespace Thumbly
{
    /// <summary>The dark colours MainForm already uses, shared by the Folders view and the Adjust dialog.</summary>
    internal static class Theme
    {
        public static readonly Color Back = Color.FromArgb(32, 32, 36);
        public static readonly Color Panel = Color.FromArgb(42, 42, 48);
        public static readonly Color Control = Color.FromArgb(55, 55, 62);
        public static readonly Color Border = Color.FromArgb(90, 90, 100);
        public static readonly Color Text = Color.Gainsboro;
        public static readonly Color Muted = Color.Gray;
        public static readonly Color Link = Color.SkyBlue;
        public static readonly Color Accent = Color.Gold;
        public static readonly Color Ok = Color.LightGreen;
        public static readonly Color Error = Color.Salmon;

        public static Button Button(string text)
        {
            var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Control, ForeColor = Text, Margin = new Padding(4), Padding = new Padding(6, 1, 6, 1) };
            b.FlatAppearance.BorderColor = Border;
            return b;
        }

        public static void Style(TextBox t)
        {
            t.BackColor = Control;
            t.ForeColor = Text;
            t.BorderStyle = BorderStyle.FixedSingle;
        }

        private static Bitmap _checker;

        /// <summary>A small dark checkerboard tile, drawn behind previews so transparency is visible.</summary>
        public static Bitmap Checker
        {
            get
            {
                if (_checker != null) return _checker;
                var b = new Bitmap(16, 16);
                using (var g = Graphics.FromImage(b))
                using (var a = new SolidBrush(Color.FromArgb(58, 58, 64)))
                using (var c = new SolidBrush(Color.FromArgb(78, 78, 86)))
                {
                    g.FillRectangle(a, 0, 0, 16, 16);
                    g.FillRectangle(c, 0, 0, 8, 8);
                    g.FillRectangle(c, 8, 8, 8, 8);
                }
                return _checker = b;
            }
        }
    }
}
