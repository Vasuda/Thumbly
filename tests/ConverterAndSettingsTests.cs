using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Xunit;

namespace Thumbly.Tests
{
    /// <summary>Each test works in its own temp folder which is removed afterwards; nothing outside it is touched.</summary>
    public class ConverterAndSettingsTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ThumblyTests-" + Guid.NewGuid().ToString("N"));

        public ConverterAndSettingsTests() { Directory.CreateDirectory(_root); }
        public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

        private Settings MakeSettings(bool saveCopy = true) => new Settings
        {
            IconDirectory = Path.Combine(_root, "icons"),
            ImageDirectory = Path.Combine(_root, "images"),
            SaveImageCopy = saveCopy,
            Sizes = new[] { 16, 256 },
        };

        private string MakePng(string name, int w = 40, int h = 90)
        {
            var p = Path.Combine(_root, name);
            using (var bmp = new Bitmap(w, h))
            {
                using (var g = Graphics.FromImage(bmp)) g.Clear(Color.Orange);
                bmp.Save(p, ImageFormat.Png);
            }
            return p;
        }

        [Fact]
        public void ConvertFile_writes_ico_and_image_copy_with_source_name()
        {
            var src = MakePng("hero.png");
            var r = Converter.ConvertFile(src, MakeSettings());

            Assert.Equal(Path.Combine(_root, "icons", "hero.ico"), r.IconPath);
            Assert.Equal(Path.Combine(_root, "images", "hero.png"), r.ImagePath);
            Assert.True(File.Exists(r.IconPath));
            Assert.Equal(File.ReadAllBytes(src), File.ReadAllBytes(r.ImagePath));
        }

        [Fact]
        public void ConvertFile_without_copy_only_writes_ico()
        {
            var r = Converter.ConvertFile(MakePng("a.png"), MakeSettings(saveCopy: false));
            Assert.Null(r.ImagePath);
            Assert.False(Directory.Exists(Path.Combine(_root, "images")));
        }

        [Fact]
        public void ConvertFile_never_overwrites_existing_output()
        {
            var src = MakePng("dup.png");
            var s = MakeSettings();
            var first = Converter.ConvertFile(src, s);
            var second = Converter.ConvertFile(src, s);
            Assert.Equal("dup.ico", Path.GetFileName(first.IconPath));
            Assert.Equal("dup (2).ico", Path.GetFileName(second.IconPath));
            Assert.Equal("dup (2).png", Path.GetFileName(second.ImagePath));
        }

        [Fact]
        public void ConvertFile_does_not_lock_the_source_file()
        {
            var src = MakePng("lock.png");
            Converter.ConvertFile(src, MakeSettings());
            File.Delete(src); // would throw if GDI+ still held the file
            Assert.False(File.Exists(src));
        }

        [Fact]
        public void ConvertBitmap_saves_copy_as_png()
        {
            using (var bmp = new Bitmap(20, 20))
            {
                var r = Converter.ConvertBitmap(bmp, "clip", MakeSettings());
                Assert.EndsWith("clip.ico", r.IconPath);
                Assert.EndsWith("clip.png", r.ImagePath);
                Assert.True(File.Exists(r.ImagePath));
            }
        }

        [Theory]
        [InlineData("x.png", true)]
        [InlineData("x.JPG", true)]
        [InlineData("x.ico", false)]
        [InlineData("x.txt", false)]
        [InlineData("noext", false)]
        public void IsSupported_checks_extension(string name, bool expected)
            => Assert.Equal(expected, Converter.IsSupported(name));

        [Fact]
        public void Settings_round_trip_through_file()
        {
            var path = Path.Combine(_root, "settings.txt");
            var s = new Settings
            {
                IconDirectory = @"D:\Some Dir\Icons",
                ImageDirectory = @"D:\Some Dir\Images",
                SaveImageCopy = false,
                OpenFolderAfterSave = true,
                Sizes = new[] { 24, 48, 256 },
            };
            s.Save(path);
            var loaded = Settings.Load(path);

            Assert.Equal(s.IconDirectory, loaded.IconDirectory);
            Assert.Equal(s.ImageDirectory, loaded.ImageDirectory);
            Assert.False(loaded.SaveImageCopy);
            Assert.True(loaded.OpenFolderAfterSave);
            Assert.Equal(s.Sizes, loaded.Sizes);
        }

        [Fact]
        public void Settings_missing_file_gives_defaults()
        {
            var s = Settings.Load(Path.Combine(_root, "nope.txt"));
            Assert.Equal(IcoWriter.DefaultSizes, s.Sizes);
            Assert.True(s.SaveImageCopy);
            Assert.False(string.IsNullOrEmpty(s.IconDirectory));
        }
    }
}
