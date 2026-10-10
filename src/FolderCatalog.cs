using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Thumbly
{
    public enum FolderStatus { Ok, FolderMissing, NeedsReapply }

    /// <summary>One folder that has (or had) a Thumbly icon.</summary>
    public class FolderEntry
    {
        public string Id;
        public string Folder;
        /// <summary>The central copy in the .ico folder.</summary>
        public string IconPath;
        public IconSource Source = new IconSource();
        public DateTimeOffset Applied;
        /// <summary>Whether Thumbly set the folder's ReadOnly attribute (so removing the icon clears it again).</summary>
        public bool SetReadOnly;

        public string Name => FolderCatalog.FolderName(Folder);

        public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 12);
    }

    /// <summary>
    /// The list of folders Thumbly has assigned icons to, in %AppData%\Thumbly\folders.txt.
    /// INI-style text, one [id] section per folder, written by hand like settings.txt (no JSON dependency on net48).
    /// </summary>
    public class FolderCatalog
    {
        public static string DefaultPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Thumbly", "folders.txt");

        public List<FolderEntry> Entries { get; } = new List<FolderEntry>();

        /// <summary>The file this catalog was loaded from and saves to.</summary>
        public string FilePath { get; private set; }

        public static FolderCatalog Load(string path = null)
        {
            var c = new FolderCatalog { FilePath = path ?? DefaultPath };
            if (!File.Exists(c.FilePath)) return c;

            FolderEntry cur = null;
            string image = null, source = null;
            void Finish()
            {
                if (cur == null) return;
                cur.Source = IconSource.Parse(image, source);
                if (!string.IsNullOrEmpty(cur.Folder)) c.Entries.Add(cur);
            }

            foreach (var raw in File.ReadAllLines(c.FilePath, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    Finish();
                    cur = new FolderEntry { Id = line.Substring(1, line.Length - 2).Trim() };
                    image = source = null;
                    continue;
                }
                int eq = line.IndexOf('=');
                if (cur == null || eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                var val = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "Folder": cur.Folder = val; break;
                    case "Icon": cur.IconPath = val; break;
                    case "Image": image = val.Length > 0 ? val : null; break;
                    case "Source": source = val; break;
                    case "Applied":
                        if (DateTimeOffset.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) cur.Applied = d;
                        break;
                    case "SetReadOnly": cur.SetReadOnly = val == "true"; break;
                }
            }
            Finish();
            return c;
        }

        public void Save(string path = null)
        {
            path = path ?? FilePath ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var sb = new StringBuilder();
            foreach (var e in Entries)
            {
                sb.Append('[').Append(e.Id).AppendLine("]");
                sb.Append("Folder=").AppendLine(e.Folder);
                sb.Append("Icon=").AppendLine(e.IconPath ?? "");
                sb.Append("Image=").AppendLine(e.Source?.ImagePath ?? "");
                sb.Append("Source=").AppendLine((e.Source ?? new IconSource()).Serialize());
                sb.Append("Applied=").AppendLine(e.Applied.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture));
                sb.Append("SetReadOnly=").AppendLine(e.SetReadOnly ? "true" : "false");
                sb.AppendLine();
            }
            // Write to a temp file and swap, so a crash mid-write never leaves a truncated catalog.
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
            FilePath = path;
        }

        public FolderEntry Find(string folder)
        {
            var key = Normalize(folder);
            return Entries.FirstOrDefault(e => string.Equals(Normalize(e.Folder), key, StringComparison.OrdinalIgnoreCase));
        }

        public FolderEntry FindById(string id) => Entries.FirstOrDefault(e => e.Id == id);

        public void Upsert(FolderEntry entry)
        {
            int i = Entries.FindIndex(e => e.Id == entry.Id);
            if (i >= 0) Entries[i] = entry;
            else Entries.Add(entry);
        }

        public void Remove(string id) => Entries.RemoveAll(e => e.Id == id);

        public static FolderStatus Check(FolderEntry e, out string reason)
        {
            reason = null;
            if (!Directory.Exists(e.Folder)) { reason = "folder missing"; return FolderStatus.FolderMissing; }
            if (!FolderIconService.IsApplied(e.Folder, out reason)) return FolderStatus.NeedsReapply;
            if (string.IsNullOrEmpty(e.IconPath) || !File.Exists(e.IconPath)) { reason = "central copy missing"; return FolderStatus.NeedsReapply; }
            return FolderStatus.Ok;
        }

        public static string Normalize(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return "";
            string full;
            try { full = Path.GetFullPath(folder); } catch { full = folder; }
            return full.TrimEnd('\\', '/');
        }

        public static string FolderName(string folder)
        {
            var n = Normalize(folder);
            var name = Path.GetFileName(n);
            return string.IsNullOrEmpty(name) ? n : name; // a drive root has no file name
        }
    }
}
