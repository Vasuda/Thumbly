using System;
using System.Drawing;
using System.IO;
using Xunit;

namespace Thumbly.Tests
{
    public class FolderCatalogTests : IDisposable
    {
        private readonly TempDir _t = new TempDir();
        public void Dispose() => _t.Dispose();

        [Fact]
        public void Round_trips_entries_through_the_file()
        {
            var path = _t.Path_("folders.txt");
            var c = new FolderCatalog();
            c.Upsert(new FolderEntry
            {
                Id = "aaaaaaaaaaaa",
                Folder = @"D:\Work\Pro=jects",
                IconPath = @"C:\Icons\Projects.ico",
                Source = new IconSource { ImagePath = @"C:\Images\hero.png", Crop = new RectangleF(0.1f, 0.2f, 0.5f, 0.5f), RemoveBackground = true, Tolerance = 40 },
                Applied = new DateTimeOffset(2026, 10, 10, 12, 34, 56, TimeSpan.FromHours(3)),
                SetReadOnly = true,
            });
            c.Upsert(new FolderEntry { Id = "bbbbbbbbbbbb", Folder = @"E:\Ünïcode", IconPath = @"C:\Icons\U.ico" });
            c.Save(path);

            var l = FolderCatalog.Load(path);
            Assert.Equal(2, l.Entries.Count);
            var a = l.FindById("aaaaaaaaaaaa");
            Assert.Equal(@"D:\Work\Pro=jects", a.Folder);
            Assert.Equal(@"C:\Icons\Projects.ico", a.IconPath);
            Assert.Equal(@"C:\Images\hero.png", a.Source.ImagePath);
            Assert.Equal(0.1f, a.Source.Crop.X, 4);
            Assert.Equal(0.5f, a.Source.Crop.Width, 4);
            Assert.True(a.Source.RemoveBackground);
            Assert.Equal(40, a.Source.Tolerance);
            Assert.Equal(new DateTimeOffset(2026, 10, 10, 12, 34, 56, TimeSpan.FromHours(3)), a.Applied);
            Assert.True(a.SetReadOnly);
            var b = l.FindById("bbbbbbbbbbbb");
            Assert.Equal(@"E:\Ünïcode", b.Folder);
            Assert.Null(b.Source.ImagePath);
            Assert.True(b.Source.Crop.IsEmpty);
        }

        [Fact]
        public void Missing_file_loads_empty_and_unknown_keys_are_ignored()
        {
            Assert.Empty(FolderCatalog.Load(_t.Path_("nope.txt")).Entries);

            var path = _t.Path_("odd.txt");
            File.WriteAllText(path, "; comment\r\nstray=1\r\n[cccccccccccc]\r\nFolder=C:\\X\r\nFuture=thing\r\n[dddddddddddd]\r\nIcon=C:\\no-folder.ico\r\n");
            var c = FolderCatalog.Load(path);
            Assert.Single(c.Entries); // the entry without a folder is skipped
            Assert.Equal(@"C:\X", c.Entries[0].Folder);
        }

        [Fact]
        public void Find_ignores_case_and_trailing_separator()
        {
            var c = new FolderCatalog();
            c.Upsert(new FolderEntry { Id = "aaaaaaaaaaaa", Folder = @"D:\Work\Projects" });
            Assert.NotNull(c.Find(@"d:\work\PROJECTS\"));
            Assert.Null(c.Find(@"D:\Work\Other"));
        }

        [Fact]
        public void Upsert_replaces_by_id_and_Remove_drops_it()
        {
            var c = new FolderCatalog();
            c.Upsert(new FolderEntry { Id = "aaaaaaaaaaaa", Folder = @"C:\A" });
            c.Upsert(new FolderEntry { Id = "aaaaaaaaaaaa", Folder = @"C:\B" });
            Assert.Single(c.Entries);
            Assert.Equal(@"C:\B", c.Entries[0].Folder);
            c.Remove("aaaaaaaaaaaa");
            Assert.Empty(c.Entries);
        }

        [Fact]
        public void Save_overwrites_an_existing_file()
        {
            var path = _t.Path_("folders.txt");
            var c = new FolderCatalog();
            c.Upsert(new FolderEntry { Id = "aaaaaaaaaaaa", Folder = @"C:\A" });
            c.Save(path);
            c.Remove("aaaaaaaaaaaa");
            c.Save(path);
            Assert.Empty(FolderCatalog.Load(path).Entries);
            Assert.False(File.Exists(path + ".tmp"));
        }

        [Fact]
        public void Check_maps_each_situation()
        {
            var central = _t.Path_("central.ico");
            using (var img = Converter.LoadImage(_t.Png("p.png")))
            using (var fs = File.Create(central)) IcoWriter.Write(img, fs, new[] { 16 });
            var folder = _t.Folder("F");
            var e = new FolderEntry { Id = "aaaaaaaaaaaa", Folder = folder, IconPath = central };

            Assert.Equal(FolderStatus.NeedsReapply, FolderCatalog.Check(e, out _)); // never applied
            FolderIconService.Apply(folder, central, e.Id);
            Assert.Equal(FolderStatus.Ok, FolderCatalog.Check(e, out _));

            File.Delete(central);
            Assert.Equal(FolderStatus.NeedsReapply, FolderCatalog.Check(e, out var r));
            Assert.Equal("central copy missing", r);

            e.Folder = _t.Path_("gone");
            Assert.Equal(FolderStatus.FolderMissing, FolderCatalog.Check(e, out _));
        }

        [Fact]
        public void IconSource_round_trips_with_invariant_decimals()
        {
            var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("lt-LT"); // decimal comma
                var s = new IconSource { Crop = new RectangleF(0.125f, 0.25f, 0.5f, 0.5f), RemoveBackground = true, BackgroundAi = true, Tolerance = 7 };
                var text = s.Serialize();
                Assert.Equal("crop=0.125,0.25,0.5,0.5;bg=1;bgm=ai;tol=7", text);
                var p = IconSource.Parse("x.png", text);
                Assert.Equal(s.Crop, p.Crop);
                Assert.True(p.RemoveBackground);
                Assert.True(p.BackgroundAi);
                Assert.Equal(7, p.Tolerance);
                Assert.Equal("x.png", p.ImagePath);
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = prev; }
        }

        [Fact]
        public void IconSource_parse_tolerates_junk()
        {
            var p = IconSource.Parse(null, "crop=a,b;bg=1;tol=999;zzz");
            Assert.True(p.Crop.IsEmpty);
            Assert.True(p.RemoveBackground);
            Assert.Equal(IconSource.MaxTolerance, p.Tolerance);
            Assert.False(IconSource.Parse(null, "").RemoveBackground);
        }
    }
}
