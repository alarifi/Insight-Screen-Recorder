using System.Runtime.InteropServices;

namespace InsightScreenRecorder;

internal sealed class RecordingCountdown : Form
{
    private readonly Label _number = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Font = new Font("Segoe UI", 64, FontStyle.Bold) };
    private RecordingCountdown(Rectangle bounds)
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        BackColor = MainForm.Background; StartPosition = FormStartPosition.Manual; ClientSize = new Size(180, 180);
        Location = new Point(bounds.Left + (bounds.Width - Width) / 2, bounds.Top + (bounds.Height - Height) / 2);
        Controls.Add(_number); AccessibleName = "Recording countdown";
    }
    internal static async Task ShowAsync(Form owner, Rectangle bounds, int seconds, Action<int> tick)
    {
        if (seconds <= 0) return;
        using var countdown = new RecordingCountdown(bounds);
        countdown._number.Text = seconds.ToString(); countdown.Show(owner);
        try {
            for (int remaining = seconds; remaining > 0; remaining--) {
                countdown._number.Text = remaining.ToString(); countdown._number.AccessibleName = remaining.ToString();
                tick(remaining); countdown.Refresh(); await Task.Delay(1000);
            }
        } finally { countdown.Hide(); countdown.Close(); }
    }
}

internal static class CaptureVisibility
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    internal static void Configure(Form form, bool allowCapture) => SetWindowDisplayAffinity(form.Handle, allowCapture ? 0u : 0x11u);
}

internal sealed class RecordingPanelForm : Form
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private readonly Func<TimeSpan> _elapsed;
    private readonly bool _ball;
    private readonly Label? _duration;
    private bool _stopping;
    private readonly Func<Task<bool>> _stop;
    private readonly Func<Task> _togglePause;
    private readonly Func<bool> _isPaused, _busy;
    private ActionButton? _pauseButton, _saveButton;
    private ToolStripMenuItem? _pauseMenu;
    internal RecordingPanelForm(RecorderSettings settings, Func<TimeSpan> elapsed, Func<Task<bool>> stop, Action screenshot, Func<Task> togglePause, Func<bool> isPaused, Func<bool> busy)
    {
        _stop = stop; _elapsed = elapsed; _togglePause = togglePause; _isPaused = isPaused; _busy = busy; _ball = settings.PanelStyle == "Floating ball";
        Text = "Insight recording controls"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        BackColor = MainForm.Surface; ForeColor = MainForm.Ink; Opacity = settings.PanelOpacity / 100d;
        StartPosition = FormStartPosition.Manual; Size = _ball ? new Size(112, 112) : new Size(540, 68);
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720); Location = new Point(screen.Right - Width - 24, screen.Top + 24);
        async void Stop() { try { await RequestStopAsync(); } catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "Unable to stop recording", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
        if (_ball) {
            using var ellipse = new System.Drawing.Drawing2D.GraphicsPath(); ellipse.AddEllipse(ClientRectangle); Region = new Region(ellipse);
            MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) Stop(); };
            var tip = new ToolTip(); tip.SetToolTip(this, "Double-click to stop and save"); Disposed += (_, _) => tip.Dispose();
            var menu = new ContextMenuStrip(); _pauseMenu = new ToolStripMenuItem("Pause", null, async (_, _) => await PauseAsync()); menu.Items.Add(_pauseMenu); menu.Items.Add("Stop & save", null, (_, _) => Stop()); menu.Items.Add("Screenshot", null, (_, _) => screenshot()); ContextMenuStrip = menu;
        } else {
            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(10), Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126)); row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _duration = new Label { Dock = DockStyle.Fill, ForeColor = MainForm.Teal, Font = new Font("Segoe UI", 18, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, AutoEllipsis = true };
            var shot = new ActionButton("Screenshot") { Dock = DockStyle.Fill }; shot.Click += (_, _) => screenshot();
            _pauseButton = new ActionButton("Pause") { Dock = DockStyle.Fill }; _pauseButton.Click += async (_, _) => await PauseAsync();
            _saveButton = new ActionButton("Stop & save", true) { Dock = DockStyle.Fill }; _saveButton.Click += (_, _) => Stop();
            row.Controls.Add(_duration, 0, 0); row.Controls.Add(_pauseButton, 1, 0); row.Controls.Add(shot, 2, 0); row.Controls.Add(_saveButton, 3, 0); Controls.Add(row);
        }
        _timer.Tick += (_, _) => RefreshState(); _timer.Start(); RefreshState();
        Shown += (_, _) => CaptureVisibility.Configure(this, settings.AllowPanelCapture);
    }
    private async Task PauseAsync()
    {
        if (_stopping || _busy()) return;
        try { await _togglePause(); }
        catch (Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "Recording controls", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        if (!IsDisposed) RefreshState();
    }
    internal void RefreshState()
    {
        if (IsDisposed) return;
        string text = _isPaused() ? "Resume" : "Pause"; bool enabled = !_busy() && !_stopping;
        if (_pauseButton != null) { _pauseButton.Text = text; _pauseButton.AccessibleName = text; _pauseButton.Enabled = enabled; }
        if (_pauseMenu != null) { _pauseMenu.Text = text; _pauseMenu.Enabled = enabled; }
        if (_saveButton != null) _saveButton.Enabled = enabled;
        if (_duration != null) { _duration.Text = _elapsed().ToString(@"hh\:mm\:ss"); _duration.ForeColor = _isPaused() ? Color.FromArgb(255, 193, 80) : MainForm.Teal; }
        else Invalidate();
    }
    internal async Task RequestStopAsync()
    {
        if (_stopping) return;
        _stopping = true;
        try { _stopping = await _stop(); }
        catch { _stopping = false; throw; }
    }
    protected override bool ShowWithoutActivation => true;
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if (!_ball) return; e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(_isPaused() ? Color.FromArgb(125, 85, 22) : Color.FromArgb(23, 94, 102)); e.Graphics.FillEllipse(brush, ClientRectangle);
        using var font = new Font("Segoe UI", 14, FontStyle.Bold); TextRenderer.DrawText(e.Graphics, _elapsed().ToString(@"mm\:ss"), font, ClientRectangle, MainForm.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.PreserveGraphicsClipping);
    }
    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}

internal sealed class RecordingBoundaryForm : Form
{
    internal RecordingBoundaryForm(Rectangle region)
    {
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
        region.Inflate(2, 2); Bounds = region; BackColor = Color.Magenta; TransparencyKey = Color.Magenta;
        Shown += (_, _) => CaptureVisibility.Configure(this, false);
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80 | 0x20; return p; } }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using var pen = new Pen(MainForm.Teal, 2); e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3); }
}
