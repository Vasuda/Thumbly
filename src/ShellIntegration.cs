using Microsoft.Win32;

namespace Thumbly
{
    /// <summary>
    /// The "Set icon with Thumbly" entry on folders in Explorer's right-click menu. Per-user (HKCU), no admin needed.
    /// The root key is a parameter only so tests can work in a throwaway key.
    /// </summary>
    public static class ShellIntegration
    {
        public const string KeyPath = @"Software\Classes\Directory\shell\Thumbly";
        public const string MenuText = "Set icon with Thumbly";

        public static string CommandFor(string exePath) => "\"" + exePath + "\" --folder \"%1\"";

        public static bool IsInstalled(RegistryKey root = null)
        {
            using (var k = (root ?? Registry.CurrentUser).OpenSubKey(KeyPath))
                return k != null;
        }

        /// <summary>The registered command line, or null when not installed.</summary>
        public static string InstalledCommand(RegistryKey root = null)
        {
            using (var k = (root ?? Registry.CurrentUser).OpenSubKey(KeyPath + @"\command"))
                return k?.GetValue(null) as string;
        }

        public static void Install(string exePath, RegistryKey root = null)
        {
            using (var k = (root ?? Registry.CurrentUser).CreateSubKey(KeyPath))
            {
                k.SetValue(null, MenuText);
                k.SetValue("Icon", "\"" + exePath + "\",0");
                using (var c = k.CreateSubKey("command"))
                    c.SetValue(null, CommandFor(exePath));
            }
        }

        public static void Uninstall(RegistryKey root = null) =>
            (root ?? Registry.CurrentUser).DeleteSubKeyTree(KeyPath, false);

        /// <summary>Re-registers when the exe has moved since the entry was installed. Returns true if it rewrote it.</summary>
        public static bool RepairIfMoved(string exePath, RegistryKey root = null)
        {
            var cmd = InstalledCommand(root);
            if (cmd == null || cmd == CommandFor(exePath)) return false;
            Install(exePath, root);
            return true;
        }
    }
}
