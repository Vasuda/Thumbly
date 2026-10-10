using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Xunit;

namespace Thumbly.Tests
{
    /// <summary>
    /// Asks the Windows shell for the image Explorer draws for a folder (IShellItemImageFactory, same as the large-icon
    /// view). Guards the bug where a folder Explorer had already previewed kept its content thumbnail after the icon was
    /// set. Uses only its own temp folder.
    /// </summary>
    [Trait("Category", "Shell")]
    public class ExplorerIconTests : IDisposable
    {
        private readonly TempDir _t = new TempDir();
        public void Dispose() => _t.Dispose();

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }

        [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItemImageFactory item);

        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);

        /// <summary>What Explorer shows for the item at 256 px: a thumbnail if there is one, otherwise the icon.</summary>
        private static Bitmap ExplorerImage(string path)
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var f);
            Assert.Equal(0, f.GetImage(new SIZE { cx = 256, cy = 256 }, 0, out var h));
            try { using (var img = Image.FromHbitmap(h)) return new Bitmap(img); }
            finally { DeleteObject(h); Marshal.ReleaseComObject(f); }
        }

        private string MakeIco(Color c)
        {
            var path = _t.Path_("icon.ico");
            using (var b = new Bitmap(256, 256))
            {
                using (var g = Graphics.FromImage(b)) g.Clear(c);
                using (var fs = File.Create(path)) IcoWriter.Write(b, fs, new[] { 16, 32, 48, 256 });
            }
            return path;
        }

        private static bool IsColor(Color px, Color want) =>
            Math.Abs(px.R - want.R) < 30 && Math.Abs(px.G - want.G) < 30 && Math.Abs(px.B - want.B) < 30;

        [Fact]
        public void Icon_replaces_an_already_cached_folder_preview()
        {
            var folder = _t.Folder("Previewed");
            foreach (var c in new[] { Color.Red, Color.Green, Color.Blue })
                _t.Png(Path.Combine("Previewed", c.Name + ".png"), 300, 300, c);
            using (ExplorerImage(folder)) { } // Explorer builds and caches the content preview

            FolderIconService.Apply(folder, MakeIco(Color.Magenta), "shelltest001");

            using (var shown = ExplorerImage(folder))
                Assert.True(IsColor(shown.GetPixel(128, 128), Color.Magenta), "Explorer still shows " + shown.GetPixel(128, 128));
        }

        [Fact]
        public void Removing_the_icon_shows_a_folder_again()
        {
            var folder = _t.Folder("Removed");
            bool newly = FolderIconService.Apply(folder, MakeIco(Color.Magenta), "shelltest002");
            using (var shown = ExplorerImage(folder)) Assert.True(IsColor(shown.GetPixel(128, 128), Color.Magenta));

            FolderIconService.Remove(folder, newly);
            using (var shown = ExplorerImage(folder)) Assert.False(IsColor(shown.GetPixel(128, 128), Color.Magenta));
            Assert.False(File.Exists(FolderIconService.IniPath(folder)));
        }
    }
}
