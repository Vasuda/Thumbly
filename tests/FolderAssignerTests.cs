using System;
using System.Drawing;
using System.IO;
using Xunit;

namespace Thumbly.Tests
{
    public class FolderAssignerTests : IDisposable
    {
        private readonly TempDir _t = new TempDir();
        public void Dispose() => _t.Dispose();

        private Settings MakeSettings(bool saveCopy = true) => new Settings
        {
            IconDirectory = _t.Path_("icons"),
            ImageDirectory = _t.Path_("images"),
            SaveImageCopy = saveCopy,
            Sizes = new[] { 16, 32, 256 },
        };

        private FolderCatalog MakeCatalog() => FolderCatalog.Load(_t.Path_("folders.txt"));

        [Fact]
        public void Assign_writes_central_copy_folder_icon_and_catalog_entry()
        {
            var folder = _t.Folder("Projects");
            var img = _t.Png("in\\hero.png");
            var cat = MakeCatalog();
            var e = FolderAssigner.Assign(folder, new IconSource { ImagePath = img }, MakeSettings(), cat);

            Assert.Equal(_t.Path_("icons", "Projects.ico"), e.IconPath);
            Assert.True(File.Exists(e.IconPath));
            Assert.True(File.Exists(FolderIconService.IconPath(folder)));
            Assert.Equal(File.ReadAllBytes(e.IconPath), File.ReadAllBytes(FolderIconService.IconPath(folder)));
            Assert.Equal(_t.Path_("images", "hero.png"), e.Source.ImagePath); // source copied into the image folder
            Assert.True(e.SetReadOnly);
            Assert.Equal(12, e.Id.Length);
            Assert.Equal(e.Id, FolderIconService.ReadId(folder));

            var reloaded = MakeCatalog();
            Assert.Single(reloaded.Entries);
            Assert.Equal(FolderStatus.Ok, FolderCatalog.Check(reloaded.Entries[0], out _));
        }

        [Fact]
        public void Assign_without_copy_keeps_original_image_path()
        {
            var folder = _t.Folder("A");
            var img = _t.Png("in\\a.png");
            var e = FolderAssigner.Assign(folder, new IconSource { ImagePath = img }, MakeSettings(saveCopy: false), MakeCatalog());
            Assert.Equal(img, e.Source.ImagePath);
            Assert.False(Directory.Exists(_t.Path_("images")));
        }

        [Fact]
        public void Assigning_the_same_folder_again_reuses_the_id_and_keeps_the_old_central_copy()
        {
            var folder = _t.Folder("Projects");
            var s = MakeSettings();
            var cat = MakeCatalog();
            var first = FolderAssigner.Assign(folder, new IconSource { ImagePath = _t.Png("in\\one.png") }, s, cat);
            var second = FolderAssigner.Assign(folder + "\\", new IconSource { ImagePath = _t.Png("in\\two.png", fill: Color.Blue) }, s, cat);

            Assert.Equal(first.Id, second.Id);
            Assert.Equal("Projects (2).ico", Path.GetFileName(second.IconPath));
            Assert.True(File.Exists(first.IconPath));
            Assert.True(second.SetReadOnly); // still ours, even though the second Apply found it already set
            Assert.Single(MakeCatalog().Entries);
        }

        [Fact]
        public void RemoveIcon_restores_folder_and_keeps_central_copy()
        {
            var folder = _t.Folder("Projects");
            var cat = MakeCatalog();
            var e = FolderAssigner.Assign(folder, new IconSource { ImagePath = _t.Png("in\\x.png") }, MakeSettings(), cat);
            FolderAssigner.RemoveIcon(e, cat);

            Assert.False(File.Exists(FolderIconService.IniPath(folder)));
            Assert.False(File.Exists(FolderIconService.IconPath(folder)));
            Assert.Equal(0, (int)(File.GetAttributes(folder) & FileAttributes.ReadOnly));
            Assert.True(File.Exists(e.IconPath));
            Assert.Empty(MakeCatalog().Entries);
        }

        [Fact]
        public void Reapply_fixes_drift_and_rebuilds_with_new_source_settings()
        {
            var folder = _t.Folder("Drift");
            var s = MakeSettings();
            var cat = MakeCatalog();
            var e = FolderAssigner.Assign(folder, new IconSource { ImagePath = _t.Png("in\\x.png", 80, 40) }, s, cat);
            var before = File.ReadAllBytes(e.IconPath);

            File.SetAttributes(folder, File.GetAttributes(folder) & ~FileAttributes.ReadOnly);
            Assert.Equal(FolderStatus.NeedsReapply, FolderCatalog.Check(e, out _));

            e.Source.Crop = new RectangleF(0, 0, 0.5f, 1f);
            FolderAssigner.Reapply(e, s, cat);
            Assert.Equal(FolderStatus.Ok, FolderCatalog.Check(e, out _));
            Assert.NotEqual(before, File.ReadAllBytes(e.IconPath)); // padded vs cropped give different pixels
            Assert.Equal(e.IconPath, MakeCatalog().Entries[0].IconPath); // central copy overwritten in place
        }

        [Fact]
        public void Reapply_uses_the_central_copy_when_the_source_image_is_gone()
        {
            var folder = _t.Folder("NoSource");
            var s = MakeSettings();
            var cat = MakeCatalog();
            var e = FolderAssigner.Assign(folder, new IconSource { ImagePath = _t.Png("in\\x.png") }, s, cat);
            File.Delete(e.Source.ImagePath);
            var ico = FolderIconService.IconPath(folder);
            File.SetAttributes(ico, FileAttributes.Normal);
            File.Delete(ico);

            FolderAssigner.Reapply(e, s, cat);
            Assert.True(File.Exists(ico));

            File.Delete(e.IconPath);
            Assert.Throws<FileNotFoundException>(() => FolderAssigner.Reapply(e, s, cat));
        }

        [Fact]
        public void Assign_fails_cleanly_for_missing_folder_or_image()
        {
            var s = MakeSettings();
            Assert.Throws<DirectoryNotFoundException>(() =>
                FolderAssigner.Assign(_t.Path_("missing"), new IconSource { ImagePath = _t.Png("in\\x.png") }, s, MakeCatalog()));
            Assert.Throws<FileNotFoundException>(() =>
                FolderAssigner.Assign(_t.Folder("F"), new IconSource { ImagePath = _t.Path_("no.png") }, s, MakeCatalog()));
            Assert.False(File.Exists(_t.Path_("folders.txt")));
        }

        [Fact]
        public void SaveBitmapSource_writes_a_png_named_after_the_folder()
        {
            using (var bmp = new Bitmap(10, 10))
            {
                var p = FolderAssigner.SaveBitmapSource(bmp, _t.Folder("Music"), MakeSettings());
                Assert.True(File.Exists(p));
                Assert.StartsWith("Music-", Path.GetFileName(p));
                Assert.Equal(_t.Path_("images"), Path.GetDirectoryName(p));
            }
        }

        [Theory]
        [InlineData("Projects", "Projects")]
        [InlineData("C:", "C_")]
        [InlineData("  ", "folder")]
        public void SafeName_replaces_invalid_characters(string input, string expected) =>
            Assert.Equal(expected, FolderAssigner.SafeName(input));
    }
}
