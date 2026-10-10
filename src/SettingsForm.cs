using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Thumbly
{
    /// <summary>Output folders, sizes to include, and toggles. Returns a new Settings in <see cref="Result"/>.</summary>
    public class SettingsForm : Form
    {
        private static readonly int[] AllSizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };

        private readonly TextBox _iconDir = new TextBox();
        private readonly TextBox _imageDir = new TextBox();
        private readonly CheckBox _saveCopy = new CheckBox { Text = "Also save a copy of the source image", AutoSize = true };
        private readonly CheckBox _openFolder = new CheckBox { Text = "Reveal the .ico in Explorer after saving", AutoSize = true };
        private readonly CheckBox _contextMenu = new CheckBox { Text = "Add \"Set icon with Thumbly\" to the folder right-click menu", AutoSize = true };
        private readonly CheckBox _alwaysEdit = new CheckBox { Text = "Always open the editor (crop / background) when an image is added", AutoSize = true };
        private readonly CheckBox _autoSet = new CheckBox { Text = "Create and set the icon automatically when both a folder and an image are present", AutoSize = true };
        private readonly CheckBox[] _sizeBoxes;
        private readonly Settings _current;
        private readonly TextBox _model = new TextBox();
        private readonly Label _modelCheck = new Label { AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(3, 2, 3, 3) };

        public Settings Result { get; private set; }

        public SettingsForm(Settings current)
        {
            _current = current;
            Text = "Thumbly settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(640, 600);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 3, AutoSize = true };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            AddDirRow(layout, 0, ".ico folder", _iconDir, current.IconDirectory);
            AddDirRow(layout, 1, "Image folder", _imageDir, current.ImageDirectory);

            layout.Controls.Add(new Label { Text = "Sizes", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 10, 3, 3) }, 0, 2);
            var sizesPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(3, 6, 3, 3) };
            _sizeBoxes = AllSizes.Select(s => new CheckBox
            {
                Text = s.ToString(),
                AutoSize = true,
                Checked = current.Sizes.Contains(s),
                Margin = new Padding(3, 3, 10, 3),
            }).ToArray();
            foreach (var cb in _sizeBoxes) sizesPanel.Controls.Add(cb);
            layout.Controls.Add(sizesPanel, 1, 2);
            layout.SetColumnSpan(sizesPanel, 2);

            _saveCopy.Checked = current.SaveImageCopy;
            _openFolder.Checked = current.OpenFolderAfterSave;
            layout.Controls.Add(_saveCopy, 1, 3);
            layout.SetColumnSpan(_saveCopy, 2);
            layout.Controls.Add(_openFolder, 1, 4);
            layout.SetColumnSpan(_openFolder, 2);

            layout.Controls.Add(new Label { Text = "Folders", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 12, 3, 3) }, 0, 5);
            _contextMenu.Checked = current.ContextMenu;
            _alwaysEdit.Checked = current.AlwaysEdit;
            _autoSet.Checked = current.AutoSet;
            _contextMenu.Margin = new Padding(3, 12, 3, 3);
            layout.Controls.Add(_contextMenu, 1, 5);
            layout.SetColumnSpan(_contextMenu, 2);
            layout.Controls.Add(_alwaysEdit, 1, 6);
            layout.SetColumnSpan(_alwaysEdit, 2);
            layout.Controls.Add(_autoSet, 1, 7);
            layout.SetColumnSpan(_autoSet, 2);

            // AI background removal model
            layout.Controls.Add(new Label { Text = "AI model", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 16, 3, 3) }, 0, 8);
            _model.Text = current.MattingModelPath ?? "";
            _model.Dock = DockStyle.Fill;
            _model.Margin = new Padding(3, 14, 3, 3);
            _model.TextChanged += (s, e) => _modelCheck.Text = "";
            layout.Controls.Add(_model, 1, 8);
            var modelButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 11, 0, 0) };
            var browseModel = new Button { Text = "…", AutoSize = true, Width = 32 };
            browseModel.Click += (s, e) =>
            {
                using (var dlg = new OpenFileDialog { Title = "Pick the " + AiMatting.ModelName + " ONNX file", Filter = "ONNX model|*.onnx", FileName = _model.Text })
                    if (dlg.ShowDialog(this) == DialogResult.OK) _model.Text = dlg.FileName;
            };
            var check = new Button { Text = "Check", AutoSize = true };
            check.Click += (s, e) => CheckModel();
            modelButtons.Controls.Add(browseModel);
            modelButtons.Controls.Add(check);
            layout.Controls.Add(modelButtons, 2, 8);

            var info = new LinkLabel
            {
                AutoSize = true,
                MaximumSize = new Size(560, 0),
                Margin = new Padding(3, 2, 3, 2),
                Text = "Used by \"Remove background → AI\" in the editor. It must be " + AiMatting.ModelName + " in ONNX format (MIT licence, about 224 MB): "
                    + "the file onnx/model.onnx from " + AiMatting.DownloadPage + " (not model_fp16.onnx). "
                    + "If ImagineStudio already downloaded it, point here at its copy: <models folder>\\Matting\\birefnet-lite.onnx. "
                    + "Leave empty to use only the flat-colour remover.",
            };
            int linkStart = info.Text.IndexOf(AiMatting.DownloadPage, StringComparison.Ordinal);
            info.LinkArea = new LinkArea(linkStart, AiMatting.DownloadPage.Length);
            info.LinkClicked += (s, e) => System.Diagnostics.Process.Start(AiMatting.DownloadPage);
            layout.Controls.Add(info, 1, 9);
            layout.SetColumnSpan(info, 2);
            layout.Controls.Add(_modelCheck, 1, 10);
            layout.SetColumnSpan(_modelCheck, 2);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8) };
            var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            ok.Click += OnSave;
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(layout);
            Controls.Add(buttons);
        }

        private void AddDirRow(TableLayoutPanel layout, int row, string label, TextBox box, string value)
        {
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            box.Text = value;
            box.Dock = DockStyle.Fill;
            layout.Controls.Add(box, 1, row);
            var browse = new Button { Text = "…", AutoSize = true, Width = 32 };
            browse.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog { SelectedPath = box.Text })
                    if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = dlg.SelectedPath;
            };
            layout.Controls.Add(browse, 2, row);
        }

        private void CheckModel()
        {
            var path = _model.Text.Trim();
            if (path.Length == 0) { _modelCheck.Text = "No model set: only the flat-colour remover is available."; _modelCheck.ForeColor = SystemColors.GrayText; return; }
            UseWaitCursor = true;
            _modelCheck.Text = "Loading the model…";
            _modelCheck.ForeColor = SystemColors.GrayText;
            _modelCheck.Refresh();
            var problem = AiMatting.Check(path);
            UseWaitCursor = false;
            _modelCheck.Text = problem == null ? "OK: " + AiMatting.ModelName + " loaded and has the expected input and output." : "Problem: " + problem;
            _modelCheck.ForeColor = problem == null ? Color.DarkGreen : Color.Firebrick;
        }

        private void OnSave(object sender, EventArgs e)
        {
            var sizes = _sizeBoxes.Where(cb => cb.Checked).Select(cb => int.Parse(cb.Text)).ToArray();
            if (sizes.Length == 0)
            {
                MessageBox.Show(this, "Pick at least one size.", "Thumbly", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            if (_iconDir.Text.Trim().Length == 0 || (_saveCopy.Checked && _imageDir.Text.Trim().Length == 0))
            {
                MessageBox.Show(this, "Both folders are required.", "Thumbly", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            var modelPath = _model.Text.Trim();
            if (modelPath.Length > 0 && !System.IO.File.Exists(modelPath))
            {
                MessageBox.Show(this, "The AI model file does not exist:\n" + modelPath, "Thumbly", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            var r = _current.Clone();
            r.IconDirectory = _iconDir.Text.Trim();
            r.ImageDirectory = _imageDir.Text.Trim();
            r.SaveImageCopy = _saveCopy.Checked;
            r.OpenFolderAfterSave = _openFolder.Checked;
            r.Sizes = sizes;
            r.ContextMenu = _contextMenu.Checked;
            r.AlwaysEdit = _alwaysEdit.Checked;
            r.AutoSet = _autoSet.Checked;
            r.MattingModelPath = _model.Text.Trim();
            Result = r;
        }
    }
}
