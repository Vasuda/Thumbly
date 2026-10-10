using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Thumbly
{
    /// <summary>A single drop zone. Drop image files (or paste with Ctrl+V) and they are converted immediately.</summary>
    public class MainForm : Form
    {
        private Settings _settings = Settings.Load();
        private readonly Panel _dropZone;
        private readonly Label _hint;
        private readonly Label _status;
        private readonly LinkLabel _settingsLink;
        private bool _hover;
        private Image _lastPreview;

        public MainForm()
        {
            Text = "Thumbly";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(360, 300);
            MinimumSize = new Size(300, 260);
            AllowDrop = true;
            KeyPreview = true;
            BackColor = Color.FromArgb(32, 32, 36);
            ForeColor = Color.Gainsboro;
            Font = new Font("Segoe UI", 9.5f);
            Icon = SystemIcons.Application;

            _dropZone = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), AllowDrop = true };
            _dropZone.Paint += DropZone_Paint;
            _dropZone.Resize += (s, e) => _dropZone.Invalidate();

            _hint = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "Drop an image here\nor press Ctrl+V to paste",
                Font = new Font("Segoe UI", 12f),
                ForeColor = Color.Silver,
                BackColor = Color.Transparent,
                AllowDrop = true,
            };

            _status = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Gray,
                AutoEllipsis = true,
                Padding = new Padding(8, 0, 8, 0),
            };

            _settingsLink = new LinkLabel
            {
                Text = "Settings",
                Dock = DockStyle.Top,
                Height = 24,
                TextAlign = ContentAlignment.MiddleRight,
                LinkColor = Color.SkyBlue,
                ActiveLinkColor = Color.White,
                Padding = new Padding(0, 4, 10, 0),
            };
            _settingsLink.LinkClicked += (s, e) => OpenSettings();

            _dropZone.Controls.Add(_hint);
            Controls.Add(_dropZone);
            Controls.Add(_status);
            Controls.Add(_settingsLink);

            foreach (Control c in new Control[] { this, _dropZone, _hint })
            {
                c.DragEnter += OnDragEnter;
                c.DragLeave += OnDragLeave;
                c.DragDrop += OnDragDrop;
            }
            KeyDown += OnKeyDown;
            _status.Click += (s, e) => RevealLast();
            UpdateStatusWithTarget();
        }

        private void DropZone_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = _dropZone.ClientRectangle;
            r.Inflate(-12, -12);
            using (var pen = new Pen(_hover ? Color.SkyBlue : Color.FromArgb(90, 90, 100), 2f) { DashStyle = DashStyle.Dash })
            using (var path = RoundedRect(r, 14))
                g.DrawPath(pen, path);

            if (_lastPreview != null)
            {
                // Fit inside a square box while keeping the image's own aspect ratio.
                int side = Math.Min(r.Width, r.Height) / 2;
                float scale = Math.Min((float)side / _lastPreview.Width, (float)side / _lastPreview.Height);
                int w = Math.Max(1, (int)(_lastPreview.Width * scale));
                int h = Math.Max(1, (int)(_lastPreview.Height * scale));
                var dst = new Rectangle(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2 - 12, w, h);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(_lastPreview, dst);
            }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            bool ok = GetDroppedFiles(e).Any() || e.Data.GetDataPresent(DataFormats.Bitmap);
            e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
            _hover = ok;
            _dropZone.Invalidate();
        }

        private void OnDragLeave(object sender, EventArgs e)
        {
            _hover = false;
            _dropZone.Invalidate();
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            _hover = false;
            _dropZone.Invalidate();
            var files = GetDroppedFiles(e).ToArray();
            if (files.Length > 0) { ConvertFiles(files); return; }
            if (e.Data.GetDataPresent(DataFormats.Bitmap))
                ConvertBitmap((Image)e.Data.GetData(DataFormats.Bitmap), "dropped");
        }

        private static string[] GetDroppedFiles(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return new string[0];
            var all = (string[])e.Data.GetData(DataFormats.FileDrop) ?? new string[0];
            return all.Where(Converter.IsSupported).ToArray();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.V) { PasteFromClipboard(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.Oemcomma) { OpenSettings(); e.Handled = true; }
        }

        private void PasteFromClipboard()
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList().Cast<string>().Where(Converter.IsSupported).ToArray();
                if (files.Length > 0) { ConvertFiles(files); return; }
            }
            if (Clipboard.ContainsImage())
            {
                ConvertBitmap(Clipboard.GetImage(), "clipboard-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                return;
            }
            SetStatus("Clipboard has no image.", error: true);
        }

        private void ConvertFiles(string[] files)
        {
            int ok = 0;
            ConvertResult last = null;
            string lastError = null;
            foreach (var f in files)
            {
                try
                {
                    last = Converter.ConvertFile(f, _settings);
                    ok++;
                    SetPreview(f);
                }
                catch (Exception ex) { lastError = Path.GetFileName(f) + ": " + ex.Message; }
            }
            ReportResult(ok, files.Length, last, lastError);
        }

        private void ConvertBitmap(Image img, string baseName)
        {
            if (img == null) { SetStatus("Could not read the image.", error: true); return; }
            try
            {
                var r = Converter.ConvertBitmap(img, baseName, _settings);
                _lastPreview?.Dispose();
                _lastPreview = (Image)img.Clone();
                _dropZone.Invalidate();
                ReportResult(1, 1, r, null);
            }
            catch (Exception ex) { ReportResult(0, 1, null, ex.Message); }
            finally { img.Dispose(); }
        }

        private void ReportResult(int ok, int total, ConvertResult last, string lastError)
        {
            _lastResult = last;
            if (ok == total && last != null)
            {
                SetStatus(total == 1
                    ? "Saved " + Path.GetFileName(last.IconPath) + "  (click to reveal)"
                    : "Saved " + ok + " icons to " + _settings.IconDirectory);
                _hint.Visible = false;
                if (_settings.OpenFolderAfterSave) RevealLast();
            }
            else
                SetStatus((ok > 0 ? ok + "/" + total + " saved. " : "") + (lastError ?? "Failed."), error: true);
        }

        private ConvertResult _lastResult;

        private void RevealLast()
        {
            if (_lastResult == null || !File.Exists(_lastResult.IconPath)) return;
            Process.Start("explorer.exe", "/select,\"" + _lastResult.IconPath + "\"");
        }

        private void SetPreview(string file)
        {
            try
            {
                _lastPreview?.Dispose();
                _lastPreview = Converter.LoadImage(file);
            }
            catch { _lastPreview = null; }
            _dropZone.Invalidate();
        }

        private void SetStatus(string text, bool error = false)
        {
            _status.Text = text;
            _status.ForeColor = error ? Color.Salmon : Color.LightGreen;
        }

        private void UpdateStatusWithTarget()
        {
            _status.Text = "→ " + _settings.IconDirectory;
            _status.ForeColor = Color.Gray;
        }

        private void OpenSettings()
        {
            using (var dlg = new SettingsForm(_settings))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _settings = dlg.Result;
                    _settings.Save();
                    UpdateStatusWithTarget();
                }
            }
        }
    }
}
