using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Thumbly.Tests
{
    public class FolderIconServiceTests : IDisposable
    {
        private readonly TempDir _t = new TempDir();
        public void Dispose() => _t.Dispose();

        private string MakeIco()
        {
            var ico = _t.Path_("src.ico");
            using (var img = Converter.LoadImage(_t.Png("src.png")))
            using (var fs = File.Create(ico))
                IcoWriter.Write(img, fs, new[] { 16, 32 });
            return ico;
        }

        private static bool Has(string path, FileAttributes a) => (File.GetAttributes(path) & a) == a;

        [Fact]
        public void Apply_writes_ini_icon_and_attributes()
        {
            var folder = _t.Folder("Projects");
            bool newly = FolderIconService.Apply(folder, MakeIco(), "abc123abc123");

            Assert.True(newly);
            var ini = FolderIconService.IniPath(folder);
            var ico = FolderIconService.IconPath(folder);
            Assert.True(File.Exists(ini));
            Assert.True(File.Exists(ico));
            Assert.True(Has(ini, FileAttributes.Hidden | FileAttributes.System));
            Assert.True(Has(ico, FileAttributes.Hidden | FileAttributes.System));
            Assert.True(Has(folder, FileAttributes.ReadOnly));
            var text = File.ReadAllText(ini);
            Assert.Contains("IconResource=thumbly-icon.ico,0", text);
            Assert.Contains("[Thumbly]", text);
            Assert.Equal("abc123abc123", FolderIconService.ReadId(folder));
            Assert.True(FolderIconService.IsApplied(folder, out _));
        }

        [Fact]
        public void Apply_keeps_existing_ini_keys_and_drops_legacy_icon_keys()
        {
            var folder = _t.Folder("Docs");
            File.WriteAllText(FolderIconService.IniPath(folder), "[.ShellClassInfo]\r\nInfoTip=hello\r\nIconFile=old.ico\r\nIconIndex=0\r\n");
            FolderIconService.Apply(folder, MakeIco(), "id0000000001");

            var text = File.ReadAllText(FolderIconService.IniPath(folder));
            Assert.Contains("InfoTip=hello", text);
            Assert.DoesNotContain("IconFile=", text);
            Assert.DoesNotContain("IconIndex=", text);
            Assert.Contains("IconResource=thumbly-icon.ico,0", text);
        }

        [Fact]
        public void Apply_twice_is_idempotent()
        {
            var folder = _t.Folder("Twice");
            var ico = MakeIco();
            Assert.True(FolderIconService.Apply(folder, ico, "id0000000001"));
            Assert.False(FolderIconService.Apply(folder, ico, "id0000000001")); // ReadOnly already set (by us)
            var text = File.ReadAllText(FolderIconService.IniPath(folder));
            Assert.Equal(1, text.Split('\n').Count(l => l.StartsWith("IconResource=")));
        }

        [Fact]
        public void Remove_restores_a_clean_folder()
        {
            var folder = _t.Folder("Clean");
            File.WriteAllText(Path.Combine(folder, "keep.txt"), "x");
            bool newly = FolderIconService.Apply(folder, MakeIco(), "id0000000001");
            FolderIconService.Remove(folder, newly);

            Assert.False(File.Exists(FolderIconService.IniPath(folder)));
            Assert.False(File.Exists(FolderIconService.IconPath(folder)));
            Assert.False(Has(folder, FileAttributes.ReadOnly));
            Assert.True(File.Exists(Path.Combine(folder, "keep.txt")));
            Assert.Null(FolderIconService.ReadId(folder));
        }

        [Fact]
        public void Remove_keeps_ini_with_other_keys()
        {
            var folder = _t.Folder("Tip");
            File.WriteAllText(FolderIconService.IniPath(folder), "[.ShellClassInfo]\r\nInfoTip=hello\r\n");
            FolderIconService.Apply(folder, MakeIco(), "id0000000001");
            FolderIconService.Remove(folder, true);

            var ini = FolderIconService.IniPath(folder);
            Assert.True(File.Exists(ini));
            var text = File.ReadAllText(ini);
            Assert.Contains("InfoTip=hello", text);
            Assert.DoesNotContain("IconResource", text);
            Assert.DoesNotContain("[Thumbly]", text);
        }

        [Fact]
        public void Remove_leaves_a_folder_readonly_when_it_already_was()
        {
            var folder = _t.Folder("WasRO");
            File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);
            bool newly = FolderIconService.Apply(folder, MakeIco(), "id0000000001");
            Assert.False(newly);
            FolderIconService.Remove(folder, newly);
            Assert.True(Has(folder, FileAttributes.ReadOnly));
        }

        [Fact]
        public void Remove_leaves_an_icon_resource_someone_else_set()
        {
            var folder = _t.Folder("Foreign");
            FolderIconService.Apply(folder, MakeIco(), "id0000000001");
            var ini = FolderIconService.IniPath(folder);
            File.SetAttributes(ini, FileAttributes.Normal);
            File.WriteAllText(ini, "[.ShellClassInfo]\r\nIconResource=C:\\other.ico,0\r\n[Thumbly]\r\nId=id0000000001\r\n");
            FolderIconService.Remove(folder, true);
            Assert.Contains(@"IconResource=C:\other.ico,0", File.ReadAllText(ini));
        }

        [Fact]
        public void IsApplied_reports_why_not()
        {
            var folder = _t.Folder("Drift");
            FolderIconService.Apply(folder, MakeIco(), "id0000000001");

            File.SetAttributes(folder, File.GetAttributes(folder) & ~FileAttributes.ReadOnly);
            Assert.False(FolderIconService.IsApplied(folder, out var r1));
            Assert.Equal("attribute cleared", r1);

            File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);
            var ico = FolderIconService.IconPath(folder);
            File.SetAttributes(ico, FileAttributes.Normal);
            File.Delete(ico);
            Assert.False(FolderIconService.IsApplied(folder, out var r2));
            Assert.Equal("icon file missing", r2);

            Assert.False(FolderIconService.IsApplied(_t.Path_("nope"), out var r3));
            Assert.Equal("folder missing", r3);
        }

        [Fact]
        public void IsApplied_false_when_ini_points_elsewhere()
        {
            var folder = _t.Folder("Edited");
            FolderIconService.Apply(folder, MakeIco(), "id0000000001");
            var ini = FolderIconService.IniPath(folder);
            File.SetAttributes(ini, FileAttributes.Normal);
            File.WriteAllText(ini, "[.ShellClassInfo]\r\nIconResource=other.ico,0\r\n");
            Assert.False(FolderIconService.IsApplied(folder, out var r));
            Assert.Equal("desktop.ini changed", r);
        }

        [Fact]
        public void Works_with_non_ascii_folder_names()
        {
            var folder = _t.Folder("Žalgiris – ąčę");
            FolderIconService.Apply(folder, MakeIco(), "id0000000001");
            Assert.True(FolderIconService.IsApplied(folder, out _));
            Assert.Equal("id0000000001", FolderIconService.ReadId(folder));
        }
    }
}
