using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Thumbly
{
    /// <summary>
    /// Left: a folder area and an image area, stacked, with "Edit image…" and "Create & set icon" under them.
    /// Right: the folders that have Thumbly icons. Drops work anywhere: folders go to the folder area, images to the
    /// image area. With no folder chosen, the main button just creates an .ico (the original Thumbly behaviour).
    /// </summary>
    public class MainForm : Form
    {
        private Settings _settings = Settings.Load();
        private FolderCatalog _catalog = FolderCatalog.Load();
        private readonly DropSlot _folderSlot;
        private readonly DropSlot _imageSlot;
        private readonly Button _editButton;
        private readonly Button _createButton;
        private readonly Label _status;
        private readonly FoldersPanel _foldersPanel;
        private string _folder;
        private IconSource _source;
        private string _revealTarget;

        public MainForm(string folder = null)
        {
            Text = "Thumbly";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = _settings.WindowSize;
            MinimumSize = new Size(820, 500);
            AllowDrop = true;
            KeyPreview = true;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.5f);
            Icon = SystemIcons.Application;

            _folderSlot = new DropSlot
            {
                Dock = DockStyle.Fill,
                Title = "1 · Folder",
                EmptyText = "Drop a folder here\n(optional: without one you just get an .ico)",
                Arrange = DropSlot.Arrangement.Row,
            };
            _folderSlot.BrowseClicked += BrowseFolder;
            _folderSlot.ClearClicked += () => SetFolder(null);

            _imageSlot = new DropSlot
            {
                Dock = DockStyle.Fill,
                Title = "2 · Image",
                EmptyText = "Drop an image here\nor paste with Ctrl+V",
                Arrange = DropSlot.Arrangement.Stack,
                CheckerBehindPreview = true,
            };
            _imageSlot.BrowseClicked += BrowseImage;
            _imageSlot.ClearClicked += () => SetImage(null);

            _editButton = Theme.Button("Edit image…");
            _editButton.Click += (s, e) => EditCurrent();
            _createButton = Theme.Button("Create && set icon");
            _createButton.BackColor = Color.FromArgb(40, 92, 138);
            _createButton.FlatAppearance.BorderColor = Color.FromArgb(70, 130, 180);
            _createButton.Font = new Font(Font, FontStyle.Bold);
            _createButton.Click += (s, e) => CreateAndSet();
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(2, 2, 2, 4), WrapContents = false };
            buttons.Controls.Add(_createButton);
            buttons.Controls.Add(_editButton);

            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(6, 0, 0, 0) };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.Controls.Add(_folderSlot, 0, 0);
            left.Controls.Add(_imageSlot, 0, 1);
            left.Controls.Add(buttons, 0, 2);

            _foldersPanel = new FoldersPanel { Dock = DockStyle.Fill, Margin = new Padding(8, 0, 6, 0) };
            _foldersPanel.AssignRequested += OnAssignToRow;
            _foldersPanel.FilesDropped += HandlePaths;
            _foldersPanel.LoadRequested += LoadEntry;
            _foldersPanel.RescanRequested += RefreshFolders;
            _foldersPanel.ActionRequested += OnFolderAction;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(left, 0, 0);
            root.Controls.Add(_foldersPanel, 1, 0);

            var topBar = new Panel { Dock = DockStyle.Top, Height = 28 };
            var settingsLink = new LinkLabel { Text = "Settings", Dock = DockStyle.Right, AutoSize = true, LinkColor = Theme.Link, ActiveLinkColor = Color.White, Padding = new Padding(0, 6, 10, 0) };
            settingsLink.LinkClicked += (s, e) => OpenSettings();
            var hint = new Label
            {
                Text = "Drop folders and images anywhere in this window. Shift-drop an image to edit it first.",
                Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 2, 0, 0),
            };
            topBar.Controls.Add(hint);
            topBar.Controls.Add(settingsLink);

            _status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.Muted,
                AutoEllipsis = true,
                Padding = new Padding(12, 0, 8, 0),
            };
            _status.Click += (s, e) => RevealLast();

            Controls.Add(root);
            Controls.Add(_status);
            Controls.Add(topBar);

            var dropTargets = new List<Control> { this, root, left, buttons, topBar, hint };
            dropTargets.AddRange(_folderSlot.DropTargets);
            dropTargets.AddRange(_imageSlot.DropTargets);
            foreach (var c in dropTargets)
            {
                c.AllowDrop = true;
                c.DragEnter += OnDragEnter;
                c.DragLeave += OnDragLeave;
                c.DragDrop += OnDragDrop;
            }
            KeyDown += OnKeyDown;
            FormClosing += (s, e) => SaveWindowSize();

            UpdateButtons();
            UpdateStatusWithTarget();
            RefreshFolders();

            if (folder != null)
            {
                var f = folder.Trim().Trim('"');
                if (Directory.Exists(f)) SetFolder(f);
                else SetStatus("Folder not found: " + f, error: true);
            }
        }

        // ---------------- the two areas ----------------

        /// <summary>Puts a folder in the folder area (null clears it). Does not create anything by itself.</summary>
        private void SetFolder(string folder)
        {
            if (folder == null)
            {
                _folder = null;
                _folderSlot.ClearContent();
                UpdateButtons();
                return;
            }
            _folder = FolderCatalog.Normalize(folder);
            var entry = _catalog.Find(_folder);
            var preview = entry != null && File.Exists(entry.IconPath ?? "") ? IcoWriter.LoadFrame(entry.IconPath, 64) : null;
            preview = preview ?? FolderIconService.ShellIcon(_folder);
            var parent = Path.GetDirectoryName(_folder);
            _folderSlot.SetContent(preview, FolderCatalog.FolderName(_folder),
                (parent ?? _folder) + (entry != null ? "\nHas a Thumbly icon: it will be replaced" : ""));
            UpdateButtons();
        }

        /// <summary>Puts an image (with its crop / background settings) in the image area; null clears it.</summary>
        private void SetImage(IconSource source)
        {
            _source = source;
            if (source == null) { _imageSlot.ClearContent(); UpdateButtons(); return; }
            Bitmap preview = null;
            string size = "";
            bool waitingForAi = source.RemoveBackground && source.BackgroundAi && !AiMatting.HasMatte(source.ImagePath, _settings.MattingModelPath);
            try
            {
                using (var img = Converter.LoadImage(source.ImagePath))
                {
                    size = img.Width + " × " + img.Height;
                    if (waitingForAi) StartMatting(source, img);
                    var shown = source;
                    if (waitingForAi) { shown = source.Clone(); shown.RemoveBackground = false; }
                    double k = Math.Min(1.0, 512.0 / Math.Max(img.Width, img.Height));
                    using (var small = IcoWriter.DrawScaled(img, new Rectangle(Point.Empty, img.Size), Math.Max(1, (int)(img.Width * k)), Math.Max(1, (int)(img.Height * k))))
                    using (var prepared = ImagePipeline.Prepare(small, shown, _settings.MattingModelPath))
                        preview = IcoWriter.ToSquare(prepared);
                }
            }
            catch (Exception ex)
            {
                _source = null;
                _imageSlot.ClearContent();
                UpdateButtons();
                SetStatus("Could not open the image: " + ex.Message, error: true);
                return;
            }
            var notes = new List<string> { size };
            if (!source.Crop.IsEmpty) notes.Add("cropped");
            if (source.RemoveBackground) notes.Add(waitingForAi ? "removing background…" : source.BackgroundAi ? "background removed (AI)" : "background removed");
            _imageSlot.SetContent(preview, Path.GetFileName(source.ImagePath), string.Join(" · ", notes) + "  ·  shown as the icon will look");
            UpdateButtons();
        }

        /// <summary>Computes the AI mask off the UI thread, then refreshes the image area if it still shows that image.</summary>
        private void StartMatting(IconSource source, Image fullImage)
        {
            var copy = new Bitmap(fullImage); // the mask is computed from the full image, not the small preview
            var model = _settings.MattingModelPath;
            Task.Run(() =>
            {
                try { AiMatting.GetMatte(source.ImagePath, copy, model); return null; }
                catch (Exception ex) { return ex.Message; }
                finally { copy.Dispose(); }
            }).ContinueWith(t =>
            {
                if (IsDisposed) return;
                if (t.Result != null) SetStatus("Background removal: " + t.Result, error: true);
                else if (ReferenceEquals(_source, source)) SetImage(source);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>Runs work with a wait cursor, telling the user when an AI mask still has to be computed.</summary>
        private void Busy(IconSource source, Action work)
        {
            if (source != null && source.RemoveBackground && source.BackgroundAi && !AiMatting.HasMatte(source.ImagePath, _settings.MattingModelPath))
            {
                SetStatus("Removing the background with " + AiMatting.ModelName + "… this takes a few seconds.");
                _status.ForeColor = Theme.Accent;
                _status.Refresh();
            }
            UseWaitCursor = true;
            Cursor.Current = Cursors.WaitCursor;
            try { work(); }
            finally { UseWaitCursor = false; Cursor.Current = Cursors.Default; }
        }

        private void UpdateButtons()
        {
            _editButton.Enabled = _source != null;
            _createButton.Enabled = _source != null;
            _createButton.Text = _folder != null ? "Create && set icon" : "Create icon";
        }

        /// <summary>A new image arrived. Opens the editor first when asked (Shift) or when "always edit" is on.
        /// Returns false when the editor was cancelled.</summary>
        private bool AddImage(string path, bool edit)
        {
            var source = new IconSource { ImagePath = path };
            bool ok = true;
            if (edit || _settings.AlwaysEdit)
            {
                var edited = Edit(source);
                if (edited != null) source = edited;
                else ok = false;
            }
            SetImage(source);
            return ok && _source != null;
        }

        private void MaybeAutoSet()
        {
            if (_settings.AutoSet && _folder != null && _source != null) CreateAndSet();
        }

        private void EditCurrent()
        {
            if (_source == null) return;
            var edited = Edit(_source);
            if (edited == null) return;
            SetImage(edited);
            MaybeAutoSet();
        }

        /// <summary>Shows the editor; returns the edited source, or null when cancelled or unreadable.</summary>
        private IconSource Edit(IconSource source)
        {
            Image img;
            try { img = Converter.LoadImage(source.ImagePath); }
            catch (Exception ex) { SetStatus("Could not open the image: " + ex.Message, error: true); return null; }
            using (img)
            using (var dlg = new EditorForm(img, source, _settings.MattingModelPath))
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.Result : null;
        }

        private void BrowseFolder()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Pick the folder to give an icon", SelectedPath = _folder ?? "" })
                if (dlg.ShowDialog(this) == DialogResult.OK) { SetFolder(dlg.SelectedPath); MaybeAutoSet(); }
        }

        private void BrowseImage()
        {
            using (var dlg = new OpenFileDialog { Title = "Pick an image", Filter = "Images|" + string.Join(";", Converter.SupportedExtensions.Select(x => "*" + x)) })
                if (dlg.ShowDialog(this) == DialogResult.OK && AddImage(dlg.FileName, (ModifierKeys & Keys.Shift) != 0)) MaybeAutoSet();
        }

        // ---------------- create ----------------

        private void CreateAndSet()
        {
            if (_source == null) return;
            Busy(_source, CreateAndSetCore);
        }

        private void CreateAndSetCore()
        {
            if (_folder == null) { CreatePlainIcon(); return; }

            var name = FolderCatalog.FolderName(_folder);
            try
            {
                _catalog = FolderCatalog.Load(); // another Thumbly window may have changed it
                var existing = _catalog.Find(_folder);
                if (existing != null && string.Equals(existing.Source?.ImagePath, _source.ImagePath, StringComparison.OrdinalIgnoreCase))
                {
                    // Same image, new crop or background settings: rebuild the existing icon in place.
                    existing.Source = _source.Clone();
                    FolderAssigner.Reapply(existing, _settings, _catalog);
                }
                else FolderAssigner.Assign(_folder, _source, _settings, _catalog);

                var folder = _folder;
                _revealTarget = folder;
                SetFolder(null);
                SetImage(null);
                _foldersPanel.Display(_catalog, folder);
                SetStatus("Applied to " + name + ". It shows in Explorer right away; press F5 there if a window was already open.  (click to reveal)");
                if (_settings.OpenFolderAfterSave) RevealLast();
            }
            catch (Exception ex) { SetStatus(name + ": " + ex.Message, error: true); }
        }

        private void CreatePlainIcon()
        {
            try
            {
                var r = Converter.ConvertSource(_source, _settings);
                _revealTarget = r.IconPath;
                SetImage(null);
                SetStatus("Saved " + Path.GetFileName(r.IconPath) + " to " + _settings.IconDirectory + "  (click to reveal)");
                if (_settings.OpenFolderAfterSave) RevealLast();
            }
            catch (Exception ex) { SetStatus(ex.Message, error: true); }
        }

        /// <summary>Several images dropped with no folder anywhere: convert them all straight to .ico files.</summary>
        private void ConvertMany(string[] files)
        {
            int ok = 0;
            string lastError = null, lastIcon = null;
            foreach (var f in files)
            {
                try { lastIcon = Converter.ConvertFile(f, _settings).IconPath; ok++; }
                catch (Exception ex) { lastError = Path.GetFileName(f) + ": " + ex.Message; }
            }
            _revealTarget = lastIcon;
            if (lastError == null) SetStatus("Saved " + ok + " icons to " + _settings.IconDirectory + "  (click to reveal the last)");
            else SetStatus((ok > 0 ? ok + "/" + files.Length + " saved. " : "") + lastError, error: true);
        }

        // ---------------- drag and drop / paste ----------------

        private static string[] DroppedPaths(DragEventArgs e) =>
            e.Data.GetDataPresent(DataFormats.FileDrop) ? (string[])e.Data.GetData(DataFormats.FileDrop) ?? new string[0] : new string[0];

        private static (string[] images, string[] folders) Classify(IEnumerable<string> paths)
        {
            var list = paths.ToArray();
            return (list.Where(p => Converter.IsSupported(p) && File.Exists(p)).ToArray(), list.Where(Directory.Exists).ToArray());
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            var (images, folders) = Classify(DroppedPaths(e));
            bool bitmap = e.Data.GetDataPresent(DataFormats.Bitmap);
            e.Effect = images.Length > 0 || folders.Length > 0 || bitmap ? DragDropEffects.Copy : DragDropEffects.None;
            _folderSlot.Hover = folders.Length > 0;   // light up where things will land
            _imageSlot.Hover = images.Length > 0 || bitmap;
        }

        private void OnDragLeave(object sender, EventArgs e) => _folderSlot.Hover = _imageSlot.Hover = false;

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            _folderSlot.Hover = _imageSlot.Hover = false;
            bool shift = (e.KeyState & 4) != 0;
            var paths = DroppedPaths(e);
            if (paths.Length > 0) { HandlePaths(paths, shift); return; }
            if (e.Data.GetDataPresent(DataFormats.Bitmap)) HandleBitmap((Image)e.Data.GetData(DataFormats.Bitmap), shift);
        }

        /// <summary>The single place deciding what dropped or pasted paths mean.</summary>
        private void HandlePaths(string[] paths, bool shift)
        {
            var (images, folders) = Classify(paths);
            if (folders.Length > 1) { SetStatus("Drop one folder at a time.", error: true); return; }
            if (folders.Length == 0 && images.Length == 0) { SetStatus("No folder or supported image in the drop.", error: true); return; }
            if (folders.Length == 0 && images.Length > 1 && _folder == null) { ConvertMany(images); return; }

            if (folders.Length == 1) SetFolder(folders[0]);
            if (images.Length > 0 && !AddImage(images[0], shift)) return;
            if (images.Length > 1) SetStatus("Only the first image was used.", error: true);
            MaybeAutoSet();
        }

        private void HandleBitmap(Image img, bool shift)
        {
            if (img == null) { SetStatus("Could not read the image.", error: true); return; }
            string path;
            try { path = FolderAssigner.SaveBitmapSource(img, _folder, _settings); }
            catch (Exception ex) { SetStatus(ex.Message, error: true); return; }
            finally { img.Dispose(); }
            if (AddImage(path, shift)) MaybeAutoSet();
        }

        private void PasteFromClipboard()
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList().Cast<string>().ToArray();
                var (images, folders) = Classify(files);
                if (images.Length > 0 || folders.Length > 0) { HandlePaths(files, false); return; }
            }
            if (Clipboard.ContainsImage()) { HandleBitmap(Clipboard.GetImage(), false); return; }
            SetStatus("Clipboard has no image or folder.", error: true);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (ActiveControl is TextBox) return; // let the filter box have its keys
            if (e.Control && e.KeyCode == Keys.V) { PasteFromClipboard(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.Oemcomma) { OpenSettings(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.E) { EditCurrent(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.Enter) { CreateAndSet(); e.Handled = true; }
            else if (e.KeyCode == Keys.F5) { RefreshFolders(); e.Handled = true; }
        }

        // ---------------- folder list ----------------

        private void RefreshFolders()
        {
            _catalog = FolderCatalog.Load();
            _foldersPanel.Display(_catalog);
        }

        /// <summary>Double-click / Edit… on a row: load its folder and source image for re-editing.</summary>
        private void LoadEntry(FolderEntry entry)
        {
            SetFolder(entry.Folder);
            if (entry.Source?.ImagePath != null && File.Exists(entry.Source.ImagePath))
            {
                SetImage(entry.Source.Clone());
                SetStatus("Loaded " + entry.Name + ". Edit the image, then Create & set icon to update it.");
            }
            else
            {
                SetImage(null);
                SetStatus("The source image for " + entry.Name + " is gone. Drop a new image.", error: true);
            }
        }

        /// <summary>An image dropped onto a row: that folder, that image, set now.</summary>
        private void OnAssignToRow(string folder, string image, bool edit)
        {
            SetFolder(folder);
            if (AddImage(image, edit)) CreateAndSet();
        }

        private void OnFolderAction(FolderAction action, List<FolderEntry> selected)
        {
            _catalog = FolderCatalog.Load();
            var entries = selected.Select(x => _catalog.FindById(x.Id)).Where(x => x != null).ToList();
            if (entries.Count == 0) { RefreshFolders(); return; }

            switch (action)
            {
                case FolderAction.Reapply: RunEach(entries, "Reapplied", x => Busy(x.Source, () => FolderAssigner.Reapply(x, _settings, _catalog))); break;
                case FolderAction.RemoveIcon:
                    if (MessageBox.Show(this, "Remove the Thumbly icon from " + Describe(entries) + "?\nThe folder goes back to the default icon; the .ico in your icon folder is kept.",
                            "Thumbly", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                    RunEach(entries, "Removed the icon from", x => FolderAssigner.RemoveIcon(x, _catalog));
                    break;
                case FolderAction.Forget:
                    foreach (var x in entries) _catalog.Remove(x.Id);
                    try { _catalog.Save(); SetStatus("Forgot " + Describe(entries) + ". The folders keep their icons."); }
                    catch (Exception ex) { SetStatus(ex.Message, error: true); }
                    break;
                case FolderAction.Locate: Locate(entries[0]); break;
            }
            _foldersPanel.Display(_catalog);
        }

        private void RunEach(List<FolderEntry> entries, string verb, Action<FolderEntry> work)
        {
            int ok = 0;
            string lastError = null;
            foreach (var x in entries)
            {
                try { work(x); ok++; }
                catch (Exception ex) { lastError = x.Name + ": " + ex.Message; }
            }
            if (lastError == null) SetStatus(verb + " " + Describe(entries) + ".");
            else SetStatus((ok > 0 ? ok + "/" + entries.Count + " done. " : "") + lastError, error: true);
        }

        private static string Describe(List<FolderEntry> entries) =>
            entries.Count == 1 ? entries[0].Name : entries.Count + " folders";

        private void Locate(FolderEntry entry)
        {
            string chosen;
            using (var dlg = new FolderBrowserDialog { Description = "Where is " + entry.Name + " now?" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                chosen = FolderCatalog.Normalize(dlg.SelectedPath);
            }
            var other = _catalog.Find(chosen);
            if (other != null && other.Id != entry.Id)
            {
                SetStatus(FolderCatalog.FolderName(chosen) + " already has its own row in the list.", error: true);
                return;
            }
            if (FolderIconService.ReadId(chosen) != entry.Id
                && MessageBox.Show(this, "This folder has no Thumbly icon from this row.\nLink it anyway and apply the icon?", "Thumbly",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            entry.Folder = chosen;
            try
            {
                FolderAssigner.Reapply(entry, _settings, _catalog);
                SetStatus("Linked " + entry.Name + ".");
            }
            catch (Exception ex) { SetStatus(entry.Name + ": " + ex.Message, error: true); }
        }

        // ---------------- status, settings ----------------

        private void RevealLast()
        {
            if (string.IsNullOrEmpty(_revealTarget) || !(File.Exists(_revealTarget) || Directory.Exists(_revealTarget))) return;
            Process.Start("explorer.exe", "/select,\"" + _revealTarget + "\"");
        }

        private void SetStatus(string text, bool error = false)
        {
            _status.Text = text;
            _status.ForeColor = error ? Theme.Error : Theme.Ok;
        }

        private void UpdateStatusWithTarget()
        {
            _status.Text = "Icons are saved to " + _settings.IconDirectory;
            _status.ForeColor = Theme.Muted;
        }

        private void SaveWindowSize()
        {
            if (WindowState != FormWindowState.Normal || ClientSize == _settings.WindowSize) return;
            _settings.WindowSize = ClientSize;
            try { _settings.Save(); } catch { }
        }

        private void OpenSettings()
        {
            using (var dlg = new SettingsForm(_settings))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                bool menuChanged = dlg.Result.ContextMenu != _settings.ContextMenu;
                _settings = dlg.Result;
                _settings.Save();
                UpdateStatusWithTarget();
                if (!menuChanged) return;
                try
                {
                    if (_settings.ContextMenu) ShellIntegration.Install(Application.ExecutablePath);
                    else ShellIntegration.Uninstall();
                    SetStatus(_settings.ContextMenu ? "Added \"Set icon with Thumbly\" to the folder right-click menu." : "Removed the folder right-click menu entry.");
                }
                catch (Exception ex) { SetStatus("Context menu: " + ex.Message, error: true); }
            }
        }
    }
}
