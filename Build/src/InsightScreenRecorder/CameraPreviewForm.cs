namespace InsightScreenRecorder;

/// <summary>Live local preview. A failed camera open is displayed explicitly; no placeholder frames are used.</summary>
internal sealed class CameraPreviewForm : Form
{
    private readonly MediaFoundationCamera camera;
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    private readonly PictureBox preview = new() { Dock = DockStyle.Fill, BackColor = Color.Black, SizeMode = PictureBoxSizeMode.Zoom, AccessibleName = "Live camera preview" };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 4, 12, 4), Text = "Opening camera…" };
    private Task? cleanup;
    private bool closing, closeReady, resourcesDisposed;

    internal CameraPreviewForm(CameraDeviceInfo device)
    {
        ArgumentNullException.ThrowIfNull(device);
        camera = new MediaFoundationCamera(device.Id);
        Text = "Camera preview — " + device.Name;
        BackColor = Color.FromArgb(12, 20, 32); ForeColor = Color.FromArgb(233, 241, 247);
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterParent; MinimumSize = new Size(480, 360); ClientSize = new Size(800, 540);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        var close = new Button { Text = "Close preview", Dock = DockStyle.Right, Width = 144, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(24, 38, 54), ForeColor = ForeColor, AccessibleName = "Close camera preview" };
        close.Click += (_, _) => Close();
        var footer = new Panel { Dock = DockStyle.Fill }; footer.Controls.Add(close);
        layout.Controls.Add(preview, 0, 0); layout.Controls.Add(status, 0, 1); layout.Controls.Add(footer, 0, 2); Controls.Add(layout);
        CancelButton = close;
        timer.Tick += (_, _) => RefreshFrame();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            await camera.StartAsync(lifetime.Token);
            if (closing || IsDisposed) return;
            RefreshFrame(); timer.Start();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!closing && !IsDisposed) ShowFailure(error.Message);
            _ = ObserveCleanupAsync();
        }
    }

    private void RefreshFrame()
    {
        if (closing || IsDisposed) return;
        if (camera.Error != null)
        {
            timer.Stop(); ShowFailure(camera.Error); _ = ObserveCleanupAsync(); return;
        }
        try
        {
            var frame = camera.GetPreviewFrame();
            if (frame == null) return;
            var old = preview.Image; preview.Image = frame; old?.Dispose();
            status.Text = $"Live camera · {camera.Width} × {camera.Height} · {camera.FrameCount} frames received";
        }
        catch (Exception error) {timer.Stop(); ShowFailure(error.Message); _ = ObserveCleanupAsync();}
    }

    private void ShowFailure(string message)
    {
        // Native diagnostics can include long device paths. Keep the dialog usable while retaining the HRESULT.
        message = message.Replace('\r', ' ').Replace('\n', ' ');
        if (message.Length > 600) message = message[..600] + "…";
        status.Text = message.Contains("80040154", StringComparison.OrdinalIgnoreCase) ? "Windows could not activate the camera driver (0x80040154). Repair the HP/Realtek camera driver, then try again. " + message : "Camera unavailable. " + message;
        status.AccessibleDescription = status.Text;
        var old=preview.Image; preview.Image=null; old?.Dispose();
    }

    private Task CleanupAsync()
    {
        return cleanup ??= Task.Run(async () =>
        {
            try { await camera.StopAsync(); }
            finally { camera.Dispose(); }
        });
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (closeReady) {base.OnFormClosing(e);return;}
        e.Cancel = true; base.OnFormClosing(e);
        if (closing) return;
        closing = true; timer.Stop(); lifetime.Cancel(); status.Text = "Closing camera…";
        try {await CleanupAsync();}
        catch (Exception error) {System.Diagnostics.Trace.WriteLine("Camera preview cleanup: " + error.Message);}
        finally {closeReady = true; if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)Close);}
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true; timer.Dispose(); lifetime.Cancel(); lifetime.Dispose();
            var old=preview.Image; preview.Image=null; old?.Dispose();
            // A direct Dispose must not block the UI on a device driver. Normal Close awaits this same cleanup task.
            _ = ObserveCleanupAsync();
        }
        base.Dispose(disposing);
    }

    private async Task ObserveCleanupAsync()
    {
        try {await CleanupAsync();}
        catch (Exception error) {System.Diagnostics.Trace.WriteLine("Camera preview cleanup: " + error.Message);}
    }
}

