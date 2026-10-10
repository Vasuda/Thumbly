using System;
using Microsoft.Win32;
using Xunit;

namespace Thumbly.Tests
{
    /// <summary>Works under a throwaway HKCU\Software\ThumblyTests-&lt;guid&gt; key and deletes only that key.</summary>
    public class ShellIntegrationTests : IDisposable
    {
        private readonly string _rootPath = @"Software\ThumblyTests-" + Guid.NewGuid().ToString("N");
        private readonly RegistryKey _root;

        public ShellIntegrationTests() { _root = Registry.CurrentUser.CreateSubKey(_rootPath); }

        public void Dispose()
        {
            _root.Dispose();
            Registry.CurrentUser.DeleteSubKeyTree(_rootPath, false);
        }

        [Fact]
        public void Install_writes_menu_text_icon_and_command()
        {
            const string exe = @"C:\Tools\Thumbly.exe";
            Assert.False(ShellIntegration.IsInstalled(_root));
            ShellIntegration.Install(exe, _root);

            Assert.True(ShellIntegration.IsInstalled(_root));
            using (var k = _root.OpenSubKey(ShellIntegration.KeyPath))
            {
                Assert.Equal("Set icon with Thumbly", k.GetValue(null));
                Assert.Equal("\"C:\\Tools\\Thumbly.exe\",0", k.GetValue("Icon"));
            }
            Assert.Equal("\"C:\\Tools\\Thumbly.exe\" --folder \"%1\"", ShellIntegration.InstalledCommand(_root));
        }

        [Fact]
        public void Uninstall_removes_it_and_tolerates_missing_key()
        {
            ShellIntegration.Uninstall(_root); // nothing there yet
            ShellIntegration.Install(@"C:\T.exe", _root);
            ShellIntegration.Uninstall(_root);
            Assert.False(ShellIntegration.IsInstalled(_root));
            Assert.Null(ShellIntegration.InstalledCommand(_root));
        }

        [Fact]
        public void RepairIfMoved_rewrites_only_when_the_exe_path_changed()
        {
            Assert.False(ShellIntegration.RepairIfMoved(@"C:\New\Thumbly.exe", _root)); // not installed: nothing to do
            Assert.False(ShellIntegration.IsInstalled(_root));

            ShellIntegration.Install(@"C:\Old\Thumbly.exe", _root);
            Assert.False(ShellIntegration.RepairIfMoved(@"C:\Old\Thumbly.exe", _root));
            Assert.True(ShellIntegration.RepairIfMoved(@"C:\New\Thumbly.exe", _root));
            Assert.Contains(@"C:\New\Thumbly.exe", ShellIntegration.InstalledCommand(_root));
        }
    }
}
