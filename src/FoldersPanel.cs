using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Thumbly
{
    public enum FolderAction { Reapply, RemoveIcon, Forget, Locate }

    /// <summary>
    /// The list of folders with Thumbly icons. It only displays and raises events; MainForm does the file work,
    /// so this control has no dependency on <see cref="FolderAssigner"/>.
    /// </summary>
    public class FoldersPanel : UserControl
    {
        private readonly ListView _list;
        private readonly ImageList _icons = new ImageList { ImageSize = new Size(32, 32), ColorDepth = ColorDepth.Depth32Bit };
        private readonly TextBox _filter = new TextBox { Width = 180, Margin = new Padding(6, 6, 4, 4) };
        private readonly Label _summary = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(4, 9, 12, 4) };
        private readonly Button _reapply = Theme.Button("Reapply");
        private readonly Button _remove = Theme.Button("Remove icon");
        private readonly Label _empty;
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem _locateItem, _editItem, _revealFolderItem, _revealIconItem;
        private ListViewItem _dropTarget;
        private FolderCatalog _catalog;

        /// <summary>An image dropped onto a row: make it that folder's icon. The bool asks for the editor first (Shift).</summary>
        public event Action<string, string, bool> AssignRequested;
        /// <summary>Files dropped anywhere else on the list: handled like a drop on the left side.</summary>
        public event Action<string[], bool> FilesDropped;
        /// <summary>Load this entry's folder and source image into the folder and image areas for editing.</summary>
        public event Action<FolderEntry> LoadRequested;
        public event Action<FolderAction, List<FolderEntry>> ActionRequested;
        public event Action RescanRequested;

        public FoldersPanel()
        {
            Dock = DockStyle.Fill;
            BackColor = Theme.Back;
            ForeColor = Theme.Text;

            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(6, 4, 6, 0) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.Controls.Add(new Label { Text = "FOLDERS WITH THUMBLY ICONS", AutoSize = true, ForeColor = Theme.Muted, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Margin = new Padding(4, 10, 4, 4) }, 0, 0);
            top.Controls.Add(new Label { Text = "Filter", AutoSize = true, Margin = new Padding(4, 9, 0, 4) }, 1, 0);
            Theme.Style(_filter);
            _filter.TextChanged += (s, e) => Rebuild();
            top.Controls.Add(_filter, 2, 0);
            var rescan = Theme.Button("Rescan");
            rescan.Click += (s, e) => RescanRequested?.Invoke();
            top.Controls.Add(rescan, 3, 0);

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                SmallImageList = _icons,
                BackColor = Theme.Panel,
                ForeColor = Theme.Text,
                BorderStyle = BorderStyle.None,
                AllowDrop = true,
                OwnerDraw = true, // only to draw the column header dark; rows use the default drawing
            };
            _list.DrawColumnHeader += (s, e) =>
            {
                using (var b = new SolidBrush(Theme.Control)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var p = new Pen(Theme.Back)) e.Graphics.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 4);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, _list.Font, Rectangle.Inflate(e.Bounds, -6, 0), Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
            _list.DrawItem += (s, e) => e.DrawDefault = true;
            _list.DrawSubItem += (s, e) => e.DrawDefault = true;
            _list.Columns.Add("Folder", 170);
            _list.Columns.Add("Path", 260);
            _list.Columns.Add("Status", 170);
            _list.SelectedIndexChanged += (s, e) => UpdateButtons();
            _list.DoubleClick += (s, e) => LoadSelected();
            _list.KeyDown += OnListKeyDown;
            _list.DragEnter += OnDragOver;
            _list.DragOver += OnDragOver;
            _list.DragLeave += (s, e) => SetDropTarget(null);
            _list.DragDrop += OnDragDrop;
            _list.ContextMenuStrip = _menu;

            _empty = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.Muted,
                BackColor = Theme.Panel,
                AllowDrop = true,
                Text = "No folder icons yet.\n\nDrop a folder and an image on the left,\nthen press Create && set icon.",
                Visible = false,
            };
            _empty.DragEnter += OnDragOver;
            _empty.DragDrop += OnDragDrop;

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6, 2, 6, 2) };
            _reapply.Click += (s, e) => Raise(FolderAction.Reapply);
            _remove.Click += (s, e) => Raise(FolderAction.RemoveIcon);
            bottom.Controls.Add(_remove);
            bottom.Controls.Add(_reapply);
            bottom.Controls.Add(_summary);

            _editItem = new ToolStripMenuItem("Edit…", null, (s, e) => LoadSelected()) { ShortcutKeyDisplayString = "Enter" };
            _revealFolderItem = new ToolStripMenuItem("Reveal folder", null, (s, e) => { var x = Selected().FirstOrDefault(); if (x != null) Reveal(x.Folder); });
            _revealIconItem = new ToolStripMenuItem("Reveal icon file", null, (s, e) => { var x = Selected().FirstOrDefault(); if (x != null) Reveal(x.IconPath); });
            _locateItem = new ToolStripMenuItem("Locate moved folder…", null, (s, e) => Raise(FolderAction.Locate));
            _menu.Items.Add(_editItem);
            _menu.Items.Add(_revealFolderItem);
            _menu.Items.Add(_revealIconItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("Reapply", null, (s, e) => Raise(FolderAction.Reapply));
            _menu.Items.Add(_locateItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(new ToolStripMenuItem("Remove icon", null, (s, e) => Raise(FolderAction.RemoveIcon)) { ShortcutKeyDisplayString = "Del" });
            _menu.Items.Add("Forget (keep the icon, drop the row)", null, (s, e) => Raise(FolderAction.Forget));
            _menu.Opening += OnMenuOpening;

            var middle = new Panel { Dock = DockStyle.Fill };
            middle.Controls.Add(_list);
            middle.Controls.Add(_empty);

            Controls.Add(middle);
            Controls.Add(bottom);
            Controls.Add(top);
            UpdateButtons();
        }

        /// <summary>Shows the catalog, re-checking each folder's status. Optionally selects one folder.</summary>
        public void Display(FolderCatalog catalog, string selectFolder = null)
        {
            _catalog = catalog;
            Rebuild(selectFolder);
        }

        private void Rebuild(string selectFolder = null)
        {
            if (_catalog == null) return;
            var keep = new HashSet<string>(Selected().Select(x => x.Id));
            var select = selectFolder == null ? null : FolderCatalog.Normalize(selectFolder);
            var filter = _filter.Text.Trim();
            var entries = _catalog.Entries
                .Where(x => filter.Length == 0 || x.Folder.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _list.BeginUpdate();
            _list.Items.Clear();
            _icons.Images.Clear(); // the list keeps its own copies of the bitmaps
            int problems = 0;
            ListViewItem toShow = null;
            foreach (var x in entries)
            {
                var status = FolderCatalog.Check(x, out var reason);
                if (status != FolderStatus.Ok) problems++;
                _icons.Images.Add(LoadIcon(x.IconPath));
                var item = new ListViewItem(x.Name, _icons.Images.Count - 1) { Tag = x, UseItemStyleForSubItems = false };
                item.SubItems.Add(x.Folder).ForeColor = Theme.Muted;
                var sub = item.SubItems.Add(status == FolderStatus.Ok ? "OK" : status == FolderStatus.FolderMissing ? "Folder missing" : "Needs reapply – " + reason);
                sub.ForeColor = status == FolderStatus.Ok ? Theme.Ok : status == FolderStatus.FolderMissing ? Theme.Error : Theme.Accent;
                bool isTarget = select != null && string.Equals(FolderCatalog.Normalize(x.Folder), select, StringComparison.OrdinalIgnoreCase);
                item.Selected = select != null ? isTarget : keep.Contains(x.Id);
                if (isTarget) toShow = item;
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            toShow?.EnsureVisible();

            int total = _catalog.Entries.Count;
            _summary.Text = total + (total == 1 ? " folder" : " folders") + (problems > 0 ? " · " + problems + (problems == 1 ? " problem" : " problems") : "")
                + (entries.Count != total ? " · " + entries.Count + " shown" : "");
            _empty.Visible = total == 0;
            _list.Visible = total > 0;
            UpdateButtons();
        }

        private static Image LoadIcon(string path)
        {
            var bmp = string.IsNullOrEmpty(path) ? null : IcoWriter.LoadFrame(path, 32);
            if (bmp != null && bmp.Width == 32 && bmp.Height == 32) return bmp;
            var fitted = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(fitted))
            {
                if (bmp != null) { g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.DrawImage(bmp, 0, 0, 32, 32); }
                else using (var b = new SolidBrush(Theme.Control)) g.FillRectangle(b, 4, 8, 24, 18); // placeholder when the central copy is gone
            }
            bmp?.Dispose();
            return fitted;
        }

        private List<FolderEntry> Selected() =>
            _list.SelectedItems.Cast<ListViewItem>().Select(i => (FolderEntry)i.Tag).ToList();

        private void UpdateButtons() => _reapply.Enabled = _remove.Enabled = _list.SelectedItems.Count > 0;

        private void Raise(FolderAction action)
        {
            var sel = Selected();
            if (sel.Count > 0) ActionRequested?.Invoke(action, sel);
        }

        private void LoadSelected()
        {
            var x = Selected().FirstOrDefault();
            if (x != null) LoadRequested?.Invoke(x);
        }

        private void OnMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var sel = Selected();
            if (sel.Count == 0) { e.Cancel = true; return; }
            bool single = sel.Count == 1;
            bool missing = single && !Directory.Exists(sel[0].Folder);
            _editItem.Enabled = single && !missing;
            _revealFolderItem.Enabled = single && !missing;
            _revealIconItem.Enabled = single && File.Exists(sel[0].IconPath ?? "");
            _locateItem.Visible = missing;
        }

        private void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete) { Raise(FolderAction.RemoveIcon); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { LoadSelected(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.A) { foreach (ListViewItem i in _list.Items) i.Selected = true; e.Handled = true; }
        }

        private static void Reveal(string path)
        {
            if (string.IsNullOrEmpty(path) || !(File.Exists(path) || Directory.Exists(path))) return;
            Process.Start("explorer.exe", "/select,\"" + path + "\"");
        }

        // ---- drag and drop: an image onto a row reassigns it; anything else goes to MainForm ----

        private static string[] DroppedPaths(DragEventArgs e) =>
            e.Data.GetDataPresent(DataFormats.FileDrop) ? (string[])e.Data.GetData(DataFormats.FileDrop) ?? new string[0] : new string[0];

        private ListViewItem ItemAt(DragEventArgs e)
        {
            if (!_list.Visible) return null;
            var p = _list.PointToClient(new Point(e.X, e.Y));
            return _list.GetItemAt(p.X, p.Y);
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            var paths = DroppedPaths(e);
            bool hasImage = paths.Any(Converter.IsSupported);
            bool hasFolder = paths.Any(Directory.Exists);
            e.Effect = hasImage || hasFolder || e.Data.GetDataPresent(DataFormats.Bitmap) ? DragDropEffects.Copy : DragDropEffects.None;
            SetDropTarget(hasImage && !hasFolder ? ItemAt(e) : null);
        }

        private void SetDropTarget(ListViewItem item)
        {
            if (item == _dropTarget) return;
            if (_dropTarget != null && _dropTarget.ListView != null) _dropTarget.BackColor = Theme.Panel;
            _dropTarget = item;
            if (item != null) item.BackColor = Theme.Control;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            var target = _dropTarget;
            SetDropTarget(null);
            var paths = DroppedPaths(e);
            bool shift = (e.KeyState & 4) != 0;
            var image = paths.FirstOrDefault(Converter.IsSupported);
            if (target != null && image != null && !paths.Any(Directory.Exists))
                AssignRequested?.Invoke(((FolderEntry)target.Tag).Folder, image, shift);
            else if (paths.Length > 0)
                FilesDropped?.Invoke(paths, shift);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _icons.Dispose();
                _menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
