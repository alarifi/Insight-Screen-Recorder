using System.Drawing.Drawing2D;

namespace InsightScreenRecorder;

internal sealed class MediaInputCard : UserControl
{
    private static readonly Color TileColor = Color.FromArgb(20, 33, 49);
    private readonly Label _state;
    private readonly MediaGlyph _glyph;
    private readonly ActionButton _toggle;
    private readonly ActionButton? _test;
    private readonly TableLayoutPanel _layout;
    private readonly StyledComboBox _device = new() { Name = "SourceDevice", Dock = DockStyle.Fill, BackColor = TileColor, ForeColor = MainForm.Ink, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 7.5f), Margin = new Padding(0, 2, 0, 2) };
    private readonly AudioLevelBar _level = new() { Dock = DockStyle.Fill, Margin = new Padding(2, 2, 2, 2) };
    private readonly ToolTip _tooltip = new();
    private bool _updatingDevice;
    private string[] _deviceIds = Array.Empty<string>();
    private string[] _deviceCaptions = Array.Empty<string>();
    internal event Action<string>? DeviceSelected;
    internal MediaInputCard(string title, string glyph, Action settings, Action? toggle = null, Action? test = null)
    {
        Text = title; AccessibleName = title; BackColor = TileColor; ForeColor = MainForm.Ink; Padding = new Padding(8);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        var layout = _layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = new Padding(0), BackColor = TileColor };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));
        _glyph = new MediaGlyph(glyph) { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = TileColor }; layout.Controls.Add(_glyph, 0, 0);
        layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Margin = new Padding(0), ForeColor = MainForm.Ink, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true }, 0, 1);
        _state = new Label { Text = "Detecting…", Dock = DockStyle.Fill, Margin = new Padding(0), ForeColor = MainForm.Muted, Font = new Font("Segoe UI", 8), TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true };
        _toggle = new ActionButton("OFF") { Dock = DockStyle.Fill, Margin = new Padding(0), Font = new Font("Segoe UI", 9, FontStyle.Bold), Enabled = false };
        _toggle.Click += (_, _) => toggle?.Invoke();
        layout.Controls.Add(toggle == null ? _state : _toggle, 0, 2);
        var configure = new ActionButton("Settings") { AccessibleName = title + " settings", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(0, 4, 0, 0) };
        configure.Click += (_, _) => settings();
        if (test == null) layout.Controls.Add(configure, 0, 3);
        else {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            configure.Text = "Setup"; configure.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold); configure.Margin = new Padding(0, 4, 3, 0);
            _test = new ActionButton("Test") { AccessibleName = title + " test", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 7.5f, FontStyle.Bold), Margin = new Padding(0, 4, 0, 0) };
            _test.Click += (_, _) => test(); row.Controls.Add(configure, 0, 0); row.Controls.Add(_test, 1, 0); layout.Controls.Add(row, 0, 3);
        }
        Controls.Add(layout);
        if (test != null) {
            layout.Controls.Add(_device, 0, 4); layout.Controls.Add(_level, 0, 5);
            _device.SelectedIndexChanged += (_, _) => { if (_updatingDevice || _device.SelectedIndex < 0 || _device.SelectedIndex >= _deviceIds.Length) return; _device.CompactCaption = _deviceCaptions[_device.SelectedIndex]; _device.Invalidate(); _tooltip.SetToolTip(_device, _device.Text); DeviceSelected?.Invoke(_deviceIds[_device.SelectedIndex]); };
        }
    }
    internal void UpdateDevices(IReadOnlyList<AudioDeviceInfo> devices, string selected, bool editable)
    {
        if (_test == null || _device.DroppedDown) return; _updatingDevice = true;
        try {
            _device.Items.Clear(); var ids = new List<string> { "" }; _device.Items.Add("Default: " + (devices.FirstOrDefault(d => d.IsDefault)?.Name ?? "No device"));
            foreach (var item in devices) { ids.Add(item.Id); _device.Items.Add(item.Name); }
            int index = ids.IndexOf(selected);
            if (index < 0) { index = ids.Count; ids.Add(selected); _device.Items.Add("Device unavailable"); }
            string Short(string name) { int suffix = name.IndexOf(" (", StringComparison.Ordinal); return suffix > 0 ? name[..suffix] : name; }
            var captions = new List<string> { devices.FirstOrDefault(d => d.IsDefault) is { } current ? Short(current.Name) : "No device" }; captions.AddRange(devices.Select(d => Short(d.Name))); if (captions.Count < ids.Count) captions.Add("Unavailable");
            _deviceIds = ids.ToArray(); _deviceCaptions = captions.ToArray(); _device.CompactCaption = _deviceCaptions[index]; _device.SelectedIndex = index; _device.Enabled = editable && devices.Count > 0; _device.Invalidate();
            string name = selected.Length == 0 ? "Windows default: " + (devices.FirstOrDefault(d => d.IsDefault)?.Name ?? "No device") : devices.FirstOrDefault(d => d.Id == selected)?.Name ?? "Selected device is disconnected";
            _tooltip.SetToolTip(_device, name); _device.AccessibleName = Text + " device: " + name;
        } finally { _updatingDevice = false; }
    }
    internal void UpdateLevel(float peak) { _level.Peak = Math.Clamp(float.IsFinite(peak) ? peak : 0, 0, 1); _level.Invalidate(); }
    protected override void Dispose(bool disposing) { if (disposing) _tooltip.Dispose(); base.Dispose(disposing); }
    internal void UpdateState(bool active, bool available, bool editable = true)
    {
        _glyph.Active = active; _glyph.Invalidate();
        _toggle.Text = !available ? "No device" : active ? "ON" : "OFF";
        _toggle.Selected = active; _toggle.Enabled = available && editable; if (_test != null) _test.Enabled = available && editable;
        _toggle.AccessibleName = Text + " " + _toggle.Text;
        _state.Text = _toggle.Text; AccessibleName = Text + " " + _toggle.Text;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if (Width < 20 || Height < 20) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var outline = Shape.Round(new RectangleF(1, 1, Width - 3, Height - 3), 10); using var pen = new Pen(Color.FromArgb(52, 74, 94)); e.Graphics.DrawPath(pen, outline);
    }
    private sealed class MediaGlyph(string kind) : Control
    {
        [System.ComponentModel.DefaultValue(false)] public bool Active { get; set; }
        protected override void OnPaint(PaintEventArgs e)
        {
            using var background = new SolidBrush(BackColor); e.Graphics.FillRectangle(background, ClientRectangle);
            if (Width < 20 || Height < 20) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Math.Min(1, Math.Min(Width / 68f, Height / 78f));
            e.Graphics.TranslateTransform(Width / 2f, Height / 2f); e.Graphics.ScaleTransform(scale, scale);
            using var pen = new Pen(Active ? MainForm.Teal : MainForm.Muted, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (kind == "camera") {
                e.Graphics.DrawEllipse(pen, -22, -26, 44, 44); e.Graphics.DrawEllipse(pen, -8, -12, 16, 16);
                e.Graphics.DrawLine(pen, 0, 18, 0, 29); e.Graphics.DrawLine(pen, -17, 30, 17, 30);
            } else if (kind == "speaker") {
                e.Graphics.DrawPolygon(pen, new[] { new PointF(-25, -9), new PointF(-13, -9), new PointF(1, -22), new PointF(1, 22), new PointF(-13, 9), new PointF(-25, 9) });
                e.Graphics.DrawArc(pen, -6, -23, 30, 46, -55, 110); e.Graphics.DrawArc(pen, -6, -31, 44, 62, -55, 110);
            } else {
                using var microphone = Shape.Round(new RectangleF(-9, -27, 18, 35), 9); e.Graphics.DrawPath(pen, microphone);
                e.Graphics.DrawArc(pen, -17, -22, 34, 43, 0, 180); e.Graphics.DrawLine(pen, -17, -3, -17, 0); e.Graphics.DrawLine(pen, 17, -3, 17, 0);
                e.Graphics.DrawLine(pen, 0, 21, 0, 31); e.Graphics.DrawLine(pen, -12, 31, 12, 31);
            }
            if (!Active) { using var slash = new Pen(Color.FromArgb(192, 145, 113), 2.5f); e.Graphics.DrawLine(slash, -27, 31, 27, -30); }
        }
    }
}

internal sealed class AudioLevelBar : Control
{
    internal float Peak;
    internal AudioLevelBar() { DoubleBuffered = true; AccessibleName = "Recording audio level"; }
    protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Color.FromArgb(11, 22, 33)); using var brush = new SolidBrush(Peak >= .95f ? Color.Salmon : MainForm.Teal); e.Graphics.FillRectangle(brush, 0, 0, (int)(Width * Peak), Height); }
}

