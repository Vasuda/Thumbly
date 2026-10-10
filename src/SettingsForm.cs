using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Thumbly
{
    /// <summary>Output folders, sizes to include, and two toggles. Returns a new Settings in <see cref="Result"/>.</summary>
    public class SettingsForm : Form
    {
        private static readonly int[] AllSizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };

        private readonly TextBox _iconDir = new TextBox();
        private readonly TextBox _imageDir = new TextBox();
        private readonly CheckBox _saveCopy = new CheckBox { Text = "Also save a copy of the source image", AutoSize = true };
        private readonly CheckBox _openFolder = new CheckBox { Text = "Reveal the .ico in Explorer after saving", AutoSize = true };
        private readonly CheckBox[] _sizeBoxes;

        public Settings Result { get; private set; }

        public SettingsForm(Settings current)
        {
            Text = "Thumbly settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9.5f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(480, 330);

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
            Result = new Settings
            {
                IconDirectory = _iconDir.Text.Trim(),
                ImageDirectory = _imageDir.Text.Trim(),
                SaveImageCopy = _saveCopy.Checked,
                OpenFolderAfterSave = _openFolder.Checked,
                Sizes = sizes,
            };
        }
    }
}
