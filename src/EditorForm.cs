using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Thumbly
{
    /// <summary>
    /// The image editor: crop frame over a fixed image (move, resize from corners or edges, draw a new frame by dragging
    /// outside it, wheel to zoom, arrows to nudge), optional square lock, background removal, and live previews at real
    /// icon sizes. The frame may extend past the image; that part becomes transparent padding.
    /// Works on downscaled copies for speed; the crop is stored normalized, so the icon is built from the full image.
    /// </summary>
    public class EditorForm : Form
    {
        private const int WorkMax = 1600;
        private const int PreviewMax = 512;

        private readonly Bitmap _work, _preview;
        private readonly Size _original;
        private readonly string _imagePath;
        private readonly CropCanvas _canvas;
        private readonly CheckBox _square = new CheckBox { Text = "Square", AutoSize = true, Checked = true, Margin = new Padding(3, 8, 10, 3) };
        private readonly CheckBox _removeBg = new CheckBox { Text = "Remove background", AutoSize = true, Margin = new Padding(3, 14, 3, 0) };
        private readonly TrackBar _tolerance = new TrackBar { Minimum = 0, Maximum = IconSource.MaxTolerance, TickStyle = TickStyle.None, Width = 225 };
        private readonly Label _tolLabel = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(40, 2, 3, 0) };
        private readonly RadioButton _ai = new RadioButton { Text = "AI (" + AiMatting.ModelName + ")", AutoSize = true, Margin = new Padding(20, 4, 3, 0) };
        private readonly RadioButton _flat = new RadioButton { Text = "Flat colour", AutoSize = true, Margin = new Padding(20, 6, 3, 0) };
        private readonly Label _aiNote = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(38, 0, 3, 0), MaximumSize = new Size(230, 0) };
        private readonly string _modelPath;
        private bool _matting; // AI mask being computed in the background
        private readonly Label _info = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(3, 12, 3, 3), MaximumSize = new Size(260, 0) };
        private readonly PictureBox _pBig, _p48, _p32, _p16;
        private readonly Timer _throttle = new Timer { Interval = 60 };

        public IconSource Result { get; private set; }

        public EditorForm(Image image, IconSource current, string modelPath = null)
        {
            current = current ?? new IconSource();
            _modelPath = modelPath;
            _imagePath = current.ImagePath;
            _original = image.Size;
            _work = Downscale(image, WorkMax);
            _preview = Downscale(image, PreviewMax);

            Text = "Edit image" + (string.IsNullOrEmpty(_imagePath) ? "" : " – " + System.IO.Path.GetFileName(_imagePath));
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1000, 720);
            MinimumSize = new Size(760, 600);
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.5f);
            ShowInTaskbar = false;
            MinimizeBox = false;
            KeyPreview = true;

            _canvas = new CropCanvas(_work) { Dock = DockStyle.Fill, BackColor = Theme.Back, PixelScale = (double)image.Width / _work.Width };
            _canvas.FrameChanged += () => { UpdateInfo(); if (!_throttle.Enabled) _throttle.Start(); };

            var tools = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8, 2, 8, 2), WrapContents = false };
            var fit = Theme.Button("Fit square");
            var whole = Theme.Button("Whole image");
            fit.Click += (s, e) => { _square.Checked = true; _canvas.FitSquare(); };
            whole.Click += (s, e) => _canvas.WholeImage();
            _square.CheckedChanged += (s, e) => _canvas.Square = _square.Checked;
            tools.Controls.Add(_square);
            tools.Controls.Add(fit);
            tools.Controls.Add(whole);
            var hint = new Label
            {
                Dock = DockStyle.Bottom, Height = 22, ForeColor = Theme.Muted, Padding = new Padding(12, 0, 0, 4),
                Text = "Drag to move · corners / edges resize · drag outside to draw a new frame · wheel zooms · arrows nudge",
            };

            var left = new Panel { Dock = DockStyle.Fill };
            left.Controls.Add(_canvas);
            left.Controls.Add(tools);
            left.Controls.Add(hint);

            var right = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(10, 10, 6, 0) };
            right.Controls.Add(new Label { Text = "Result", AutoSize = true, ForeColor = Theme.Muted });
            right.Controls.Add(MakePreview(192, null, out _pBig, PictureBoxSizeMode.Zoom));
            var small = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
            small.Controls.Add(MakePreview(48, "48", out _p48, PictureBoxSizeMode.CenterImage));
            small.Controls.Add(MakePreview(32, "32", out _p32, PictureBoxSizeMode.CenterImage));
            small.Controls.Add(MakePreview(16, "16", out _p16, PictureBoxSizeMode.CenterImage));
            right.Controls.Add(small);
            right.Controls.Add(_removeBg);
            var methods = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
            methods.Controls.Add(_ai);
            methods.Controls.Add(_aiNote);
            methods.Controls.Add(_flat);
            methods.Controls.Add(new Label
            {
                Text = "Instant. For flat backgrounds (logos, clipart, renders).",
                AutoSize = true, MaximumSize = new Size(230, 0), ForeColor = Theme.Muted, Margin = new Padding(38, 0, 3, 2),
            });
            methods.Controls.Add(_tolLabel);
            _tolerance.BackColor = Theme.Back;
            _tolerance.Margin = new Padding(34, 0, 3, 3);
            _tolerance.Width = 200;
            methods.Controls.Add(_tolerance);
            right.Controls.Add(methods);
            right.Controls.Add(_info);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8, 4, 8, 8) };
            var apply = Theme.Button("Apply");
            var cancel = Theme.Button("Cancel");
            apply.Click += (s, e) => { Result = BuildSource(); DialogResult = DialogResult.OK; };
            cancel.Click += (s, e) => DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(apply);
            buttons.Controls.Add(cancel);
            AcceptButton = apply;
            CancelButton = cancel;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(left, 0, 0);
            root.Controls.Add(right, 1, 0);
            root.Controls.Add(buttons, 0, 1);
            root.SetColumnSpan(buttons, 2);
            Controls.Add(root);

            _removeBg.CheckedChanged += (s, e) => { UpdateBgControls(); RenderPreviews(); };
            _ai.CheckedChanged += (s, e) => { UpdateBgControls(); if (_ai.Checked) RenderPreviews(); };
            _flat.CheckedChanged += (s, e) => { UpdateBgControls(); if (_flat.Checked) RenderPreviews(); };
            _tolerance.ValueChanged += (s, e) => { UpdateTolLabel(); if (!_throttle.Enabled) _throttle.Start(); };
            _throttle.Tick += (s, e) => { _throttle.Stop(); RenderPreviews(); };

            // Initial state: an edited crop is restored as-is; an unedited image starts with the largest centred square.
            if (current.Crop.IsEmpty) _canvas.FitSquare();
            else
            {
                var f = ImagePipeline.CropFrame(current, _work.Size);
                _square.Checked = Math.Abs(f.Width - f.Height) < 1.5f;
                _canvas.Square = _square.Checked;
                _canvas.SetFrame(f);
            }
            bool aiAvailable = AiMatting.ModelConfigured(_modelPath);
            _ai.Enabled = aiAvailable;
            // An existing choice is kept; a fresh image defaults to AI when the model is set up.
            bool useAi = current.RemoveBackground ? current.BackgroundAi : aiAvailable;
            (useAi && aiAvailable ? _ai : _flat).Checked = true;
            _removeBg.Checked = current.RemoveBackground;
            _tolerance.Value = Math.Max(0, Math.Min(IconSource.MaxTolerance, current.Tolerance));
            UpdateBgControls();
            UpdateTolLabel();
            UpdateInfo();
            RenderPreviews();
        }

        private static Bitmap Downscale(Image image, int max)
        {
            double k = Math.Min(1.0, (double)max / Math.Max(image.Width, image.Height));
            return IcoWriter.DrawScaled(image, new Rectangle(Point.Empty, image.Size),
                Math.Max(1, (int)Math.Round(image.Width * k)), Math.Max(1, (int)Math.Round(image.Height * k)));
        }

        private static Control MakePreview(int box, string caption, out PictureBox pb, PictureBoxSizeMode mode)
        {
            var p = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 4, 12, 4) };
            pb = new PictureBox { Size = new Size(Math.Max(box, 28), Math.Max(box, 28)), SizeMode = mode, BackgroundImage = Theme.Checker, BackgroundImageLayout = ImageLayout.Tile, Margin = new Padding(0) };
            p.Controls.Add(pb);
            if (caption != null) p.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 2, 0, 0) });
            return p;
        }

        private void UpdateTolLabel() => _tolLabel.Text = "Tolerance  " + _tolerance.Value;

        private void UpdateBgControls()
        {
            bool on = _removeBg.Checked;
            _ai.Enabled = on && AiMatting.ModelConfigured(_modelPath);
            _flat.Enabled = on;
            _tolerance.Visible = _tolLabel.Visible = on && _flat.Checked; // only the flat-colour remover has a tolerance
            if (_matting) return;
            _aiNote.Text = !AiMatting.ModelConfigured(_modelPath)
                ? "Set the model in Settings to enable."
                : "Best for photos and hair. Takes a few seconds the first time for each image.";
            _aiNote.ForeColor = Theme.Muted;
        }

        /// <summary>Computes the AI mask off the UI thread (once per image), then refreshes the previews.</summary>
        private void StartMatting()
        {
            if (_matting) return;
            _matting = true;
            _aiNote.Text = "Removing the background… (first run for this image)";
            _aiNote.ForeColor = Theme.Accent;
            var copy = new Bitmap(_work); // GDI+ images are not shared across threads
            Task.Run(() =>
            {
                try { AiMatting.GetMatte(_imagePath, copy, _modelPath); return null; }
                catch (Exception ex) { return ex.Message; }
                finally { copy.Dispose(); }
            }).ContinueWith(t =>
            {
                if (IsDisposed) return;
                _matting = false;
                UpdateBgControls();
                if (t.Result != null) { _aiNote.Text = t.Result; _aiNote.ForeColor = Theme.Error; }
                RenderPreviews();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void UpdateInfo()
        {
            var f = _canvas.Frame;
            double k = (double)_original.Width / _work.Width;
            _info.Text = "Source " + _original.Width + " × " + _original.Height + " px\nFrame " + (int)Math.Round(f.Width * k) + " × " + (int)Math.Round(f.Height * k) + " px"
                + (_canvas.ExtendsPastImage ? "\nThe part outside the image becomes transparent." : "")
                + (Math.Abs(f.Width - f.Height) > 1.5f ? "\nNot square: the icon is padded to a square." : "");
        }

        private IconSource BuildSource()
        {
            var f = _canvas.Frame;
            return new IconSource
            {
                ImagePath = _imagePath,
                Crop = new RectangleF(f.X / _work.Width, f.Y / _work.Height, f.Width / _work.Width, f.Height / _work.Height),
                RemoveBackground = _removeBg.Checked,
                BackgroundAi = _ai.Checked,
                Tolerance = _tolerance.Value,
            };
        }

        private void RenderPreviews()
        {
            var source = BuildSource();
            if (source.RemoveBackground && source.BackgroundAi && !AiMatting.HasMatte(_imagePath, _modelPath))
            {
                // Never compute the mask on the UI thread (or from the small preview copy): show the crop now,
                // the cut-out as soon as the background task finishes.
                if (!_matting && _aiNote.ForeColor != Theme.Error) StartMatting();
                source.RemoveBackground = false;
            }
            Bitmap prepared;
            try { prepared = ImagePipeline.Prepare(_preview, source, _modelPath); }
            catch (Exception ex)
            {
                _aiNote.Text = ex.Message;
                _aiNote.ForeColor = Theme.Error;
                source.RemoveBackground = false;
                prepared = ImagePipeline.Prepare(_preview, source);
            }
            using (prepared)
            using (var square = IcoWriter.ToSquare(prepared))
            {
                Set(_pBig, IcoWriter.Resize(square, 256));
                Set(_p48, IcoWriter.Resize(square, 48));
                Set(_p32, IcoWriter.Resize(square, 32));
                Set(_p16, IcoWriter.Resize(square, 16));
            }
        }

        private static void Set(PictureBox pb, Image img)
        {
            var old = pb.Image;
            pb.Image = img;
            old?.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _throttle.Dispose();
                foreach (var pb in new[] { _pBig, _p48, _p32, _p16 }) pb?.Image?.Dispose();
                _work.Dispose();
                _preview.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
