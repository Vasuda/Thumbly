using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Thumbly
{
    /// <summary>
    /// The Windows side of folder icons: a hidden icon file inside the folder, a merged desktop.ini pointing at it
    /// with a relative path (so moving the folder keeps the icon), and the folder's ReadOnly attribute, which is what
    /// makes Explorer read desktop.ini. Only keys Thumbly wrote are ever removed.
    /// </summary>
    public static class FolderIconService
    {
        public const string IconFileName = "thumbly-icon.ico";
        public const string IniFileName = "desktop.ini";
        private const string ShellSection = ".ShellClassInfo";
        private const string ThumblySection = "Thumbly";
        private const string IconResourceValue = IconFileName + ",0";

        public static string IniPath(string folder) => Path.Combine(folder, IniFileName);
        public static string IconPath(string folder) => Path.Combine(folder, IconFileName);

        /// <summary>Copies the icon in, writes desktop.ini, sets ReadOnly. Returns true when ReadOnly was newly set.</summary>
        public static bool Apply(string folder, string icoSource, string id)
        {
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("Folder not found: " + folder);

            var ico = IconPath(folder);
            if (File.Exists(ico)) File.SetAttributes(ico, FileAttributes.Normal);
            File.Copy(icoSource, ico, true);
            File.SetAttributes(ico, FileAttributes.Hidden | FileAttributes.System);

            var ini = IniPath(folder);
            if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
            else File.WriteAllBytes(ini, new byte[] { 0xFF, 0xFE }); // UTF-16 BOM: the profile API then writes Unicode
            Write(ini, ShellSection, "IconResource", IconResourceValue);
            Write(ini, ShellSection, "IconFile", null);  // legacy keys would take precedence over IconResource
            Write(ini, ShellSection, "IconIndex", null);
            Write(ini, ThumblySection, "Id", id);
            Flush(ini);
            File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);

            var attrs = File.GetAttributes(folder);
            bool newlySet = (attrs & FileAttributes.ReadOnly) == 0;
            if (newlySet) File.SetAttributes(folder, attrs | FileAttributes.ReadOnly);

            // Explorer keeps a cached content preview for folders it has already shown, and editing desktop.ini does not
            // invalidate it, so the custom icon would never appear in icon views. The shell's own "Change Icon" API does.
            // It rewrites the same IconResource value (relative path kept) and leaves the [Thumbly] section alone.
            ShellSetIcon(folder, IconFileName);
            if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);

            NotifyExplorer(folder);
            return newlySet;
        }

        /// <summary>Undoes Apply: removes Thumbly's keys and icon file, deletes desktop.ini if nothing else is left in it.</summary>
        public static void Remove(string folder, bool clearReadOnly)
        {
            if (!Directory.Exists(folder)) return;

            var ini = IniPath(folder);
            if (File.Exists(ini) && IsOurIconResource(Read(ini, ShellSection, "IconResource")))
                ShellSetIcon(folder, null); // invalidates Explorer's cached icon; leaves "IconResource=,0" behind
            if (File.Exists(ini))
            {
                File.SetAttributes(ini, FileAttributes.Normal);
                var current = Read(ini, ShellSection, "IconResource");
                if (IsOurIconResource(current) || IsEmptyIconResource(current))
                    Write(ini, ShellSection, "IconResource", null);
                DeleteSection(ini, ThumblySection);
                Flush(ini);
                bool hasOtherKeys = File.ReadAllLines(ini).Any(l => l.IndexOf('=') > 0);
                if (hasOtherKeys) File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
                else File.Delete(ini);
            }

            var ico = IconPath(folder);
            if (File.Exists(ico))
            {
                File.SetAttributes(ico, FileAttributes.Normal);
                File.Delete(ico);
            }

            if (clearReadOnly)
            {
                var attrs = File.GetAttributes(folder);
                if ((attrs & FileAttributes.ReadOnly) != 0) File.SetAttributes(folder, attrs & ~FileAttributes.ReadOnly);
            }

            NotifyExplorer(folder);
        }

        /// <summary>True when everything Apply set up is still in place; otherwise a short reason.</summary>
        public static bool IsApplied(string folder, out string reason)
        {
            reason = null;
            if (!Directory.Exists(folder)) { reason = "folder missing"; return false; }
            if (!File.Exists(IconPath(folder))) { reason = "icon file missing"; return false; }
            var ini = IniPath(folder);
            if (!File.Exists(ini) || !IsOurIconResource(Read(ini, ShellSection, "IconResource")))
            { reason = "desktop.ini changed"; return false; }
            if ((File.GetAttributes(folder) & (FileAttributes.ReadOnly | FileAttributes.System)) == 0)
            { reason = "attribute cleared"; return false; }
            return true;
        }

        /// <summary>The Id Thumbly stored in the folder's desktop.ini, or null.</summary>
        public static string ReadId(string folder)
        {
            var ini = IniPath(folder);
            if (!File.Exists(ini)) return null;
            var id = Read(ini, ThumblySection, "Id");
            return string.IsNullOrEmpty(id) ? null : id;
        }

        private static bool IsOurIconResource(string value) =>
            value != null && value.Trim().StartsWith(IconFileName, StringComparison.OrdinalIgnoreCase);

        /// <summary>"IconResource=,0" is what the shell API leaves after clearing an icon.</summary>
        private static bool IsEmptyIconResource(string value) =>
            value != null && value.Trim().StartsWith(",");

        /// <summary>Best effort: asks Explorer to re-read the folder.</summary>
        public static void NotifyExplorer(string folder)
        {
            try { SHChangeNotify(SHCNE_UPDATEITEM, SHCNF_PATHW | SHCNF_FLUSHNOWAIT, folder, IntPtr.Zero); }
            catch { }
        }

        /// <summary>SHGetSetFolderCustomSettings(FCSM_ICONFILE, FCS_FORCEWRITE); null clears the icon. Best effort.</summary>
        private static void ShellSetIcon(string folder, string iconFile)
        {
            try
            {
                var s = new SHFOLDERCUSTOMSETTINGS
                {
                    dwSize = Marshal.SizeOf(typeof(SHFOLDERCUSTOMSETTINGS)),
                    dwMask = FCSM_ICONFILE,
                    pszIconFile = iconFile,
                    iIconIndex = 0,
                };
                SHGetSetFolderCustomSettings(ref s, folder, FCS_FORCEWRITE);
            }
            catch { }
        }

        /// <summary>The folder's current 32 px icon as Explorer resolves it, or null.</summary>
        public static System.Drawing.Bitmap ShellIcon(string path)
        {
            try
            {
                var info = new SHFILEINFO();
                var r = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), SHGFI_ICON | SHGFI_LARGEICON);
                if (r == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
                try
                {
                    using (var icon = System.Drawing.Icon.FromHandle(info.hIcon))
                        return icon.ToBitmap();
                }
                finally { DestroyIcon(info.hIcon); }
            }
            catch { return null; }
        }

        // ---- interop ----

        private static string Read(string ini, string section, string key)
        {
            var sb = new StringBuilder(1024);
            GetPrivateProfileString(section, key, "", sb, sb.Capacity, ini);
            return sb.ToString();
        }

        private static void Write(string ini, string section, string key, string value)
        {
            if (!WritePrivateProfileString(section, key, value, ini))
                throw new IOException("Could not write " + ini + " (error " + Marshal.GetLastWin32Error() + ").");
        }

        private static void DeleteSection(string ini, string section) =>
            WritePrivateProfileString(section, null, null, ini); // a null key deletes the section; a missing one is fine

        private static void Flush(string ini) => WritePrivateProfileString(null, null, null, ini);

        private const uint SHCNE_UPDATEITEM = 0x00002000;
        private const uint SHCNF_PATHW = 0x0005;
        private const uint SHCNF_FLUSHNOWAIT = 0x3000;
        private const uint FCSM_ICONFILE = 0x10;
        private const uint FCS_FORCEWRITE = 0x2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFOLDERCUSTOMSETTINGS
        {
            public int dwSize;
            public uint dwMask;
            public IntPtr pvid;
            public IntPtr pszWebViewTemplate;
            public int cchWebViewTemplate;
            public IntPtr pszWebViewTemplateVersion;
            public IntPtr pszInfoTip;
            public int cchInfoTip;
            public IntPtr pclsid;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.LPWStr)] public string pszIconFile;
            public int cchIconFile;
            public int iIconIndex;
            public IntPtr pszLogo;
            public int cchLogo;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetSetFolderCustomSettings(ref SHFOLDERCUSTOMSETTINGS pfcs, string path, uint readWrite);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WritePrivateProfileString(string section, string key, string value, string fileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetPrivateProfileString(string section, string key, string defaultValue, StringBuilder result, int size, string fileName);

        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_LARGEICON = 0x0;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(uint eventId, uint flags, string item1, IntPtr item2);
    }
}
