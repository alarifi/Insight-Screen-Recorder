using System.IO.Pipes;

namespace InsightScreenRecorder;

internal sealed class AudioTestForm : Form
{
    private DialogResult requestedResult = DialogResult.Cancel;
    private readonly bool loopback;
    private readonly int gain;
    private readonly ComboBox devices = new StyledComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = MainForm.Background, ForeColor = MainForm.Ink };
    private readonly List<(string Id, string Name)> choices = new();
    private readonly ProgressBar meter = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000, Style = ProgressBarStyle.Continuous, AccessibleName = "Actual audio input level" };
    private readonly Label level = MakeLabel("No signal yet", 16);
    private readonly Label status = MakeLabel("Opening selected audio device…");
    private readonly Label format = MakeLabel("");
    private readonly ActionButton toggle = new("Start test", true) { Dock = DockStyle.Fill };
    private readonly ActionButton use = new("Use for recording", true) { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 50 };
    private readonly SemaphoreSlim gate = new(1, 1);
    private WindowsAudioCapture? capture;
    private NamedPipeClientStream? client;
    private CancellationTokenSource? drainStop;
    private Task? drain;
    private readonly CancellationTokenSource lifetime = new();
    private bool busy, closing, closeReady, resourcesDisposed;
    private bool playbackMuted, inputMuted;
    private readonly System.Diagnostics.Stopwatch signalClock = new();
    internal string SelectedDeviceId => choices.Count == 0 ? "" : choices[Math.Max(0, devices.SelectedIndex)].Id;
    private static Label MakeLabel(string text, float points = 10) => new() { Text = text, Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = MainForm.Ink, Font = new Font("Segoe UI", points), TextAlign = ContentAlignment.MiddleLeft };
    internal AudioTestForm(bool loopback, string selectedDevice, int gain)
    {
        this.loopback = loopback; this.gain = gain;
        Text = loopback ? "System sound test" : "Microphone test";
        BackColor = MainForm.Background; ForeColor = MainForm.Ink; Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(660, 350); MinimumSize = new Size(560, 380);
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 7 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 34, 42, 48, 28, 42, 1, 44 }) grid.RowStyles.Add(new RowStyle(height == 1 ? SizeType.Percent : SizeType.Absolute, height == 1 ? 100 : height));
        grid.Controls.Add(MakeLabel(loopback ? "Play sound on your headphones to test recording." : "Speak into your headset microphone and watch the level."), 0, 0);
        grid.Controls.Add(devices, 0, 1); grid.Controls.Add(level, 0, 2); grid.Controls.Add(meter, 0, 3); grid.Controls.Add(format, 0, 4); grid.Controls.Add(status, 0, 5);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        var close = new ActionButton("Close") { Dock = DockStyle.Fill }; close.Click += (_, _) => Close();
        toggle.Click += async (_, _) => { if (busy) return; if (capture == null) await StartTestAsync(); else await StopTestAsync(); };
        use.Click += (_, _) => { if (choices.Count == 0) return; requestedResult = DialogResult.OK; Close(); };
        buttons.Controls.Add(toggle, 0, 0); buttons.Controls.Add(use, 1, 0); buttons.Controls.Add(close, 2, 0); grid.Controls.Add(buttons, 0, 6); Controls.Add(grid);
        timer.Tick += (_, _) => UpdateLevel();
        Shown += async (_, _) => {
            try {
                var detected = await Task.Run(() => WindowsAudioCapture.EnumerateDevices(loopback));
                if (closing) return;
                choices.Add(("", "Windows default (recommended)")); choices.AddRange(detected.Select(d => (d.Id, d.Name)));
                devices.Items.AddRange(choices.Select(d => d.Name).ToArray()); devices.SelectedIndex = Math.Max(0, choices.FindIndex(d => d.Id == selectedDevice));
                if (detected.Count == 0) throw new InvalidOperationException(loopback ? "No playback device is connected." : "No microphone is connected. Check the headset connection, then reopen Test.");
                await StartTestAsync();
            } catch (Exception ex) { status.Text = ex.Message; use.Enabled = false; }
        };
    }
    private async Task StartTestAsync()
    {
        if (busy || closing) return;
        await gate.WaitAsync(); busy = true; toggle.Enabled = false; devices.Enabled = false;
        try {
            var detected = await Task.Run(() => WindowsAudioCapture.EnumerateDevices(loopback));
            var selected = detected.FirstOrDefault(d => d.Id == SelectedDeviceId) ?? (SelectedDeviceId == "" ? detected.FirstOrDefault(d => d.IsDefault) ?? detected.FirstOrDefault() : null);
            if (selected == null) throw new InvalidOperationException("Selected device is disconnected. Select Windows default or another device.");
            capture = new WindowsAudioCapture(selected.Id, loopback, gain / 100f);
            try { var playback = WindowsAudioCapture.ReadEndpointState(true); playbackMuted = playback.Muted || playback.Volume <= 0; } catch { playbackMuted = false; }
            try { var input = WindowsAudioCapture.ReadEndpointState(loopback, selected.Id); inputMuted = !loopback && (input.Muted || input.Volume <= 0); } catch { inputMuted = false; }
            await capture.StartAsync(lifetime.Token);
            client = new NamedPipeClientStream(".", capture.PipePath.Split('\\')[^1], PipeDirection.In, PipeOptions.Asynchronous);
            await client.ConnectAsync(3000, lifetime.Token);
            drainStop = new CancellationTokenSource(); var stream = client; var token = drainStop.Token;
            drain = Task.Run(async () => { var buffer = new byte[32768]; try { while (await stream.ReadAsync(buffer, token) > 0) { } } catch (OperationCanceledException) { } catch (IOException) when (token.IsCancellationRequested) { } });
            format.Text = selected.Name + $" · {capture.SampleRate:N0} Hz · {capture.Channels} ch · gain {gain}%";
            status.Text = loopback ? "The meter shows actual sound being captured from this output device." : "Speak normally. If the level stays low, check the headset mute switch and microphone position.";
            signalClock.Restart(); toggle.Text = "Stop test"; use.Enabled = true; timer.Start();
        } catch (Exception ex) { await CleanupAsync(); status.Text = ex.Message; devices.Enabled = true; }
        finally { busy = false; toggle.Enabled = true; gate.Release(); }
    }
    private void UpdateLevel()
    {
        var source = capture; if (source == null) return;
        float peak = source.PeakLevel;
        double db = peak <= 0 ? -100 : 20 * Math.Log10(peak);
        meter.Value = Math.Clamp((int)((db + 60) / 60 * 1000), 0, 1000);
        // A logarithmic value makes quiet speech measurable even when the linear bar is small.
        level.Text = peak <= 0 ? "No signal · −∞ dBFS" : $"Input: {20 * Math.Log10(peak):F1} dBFS";
        level.ForeColor = peak >= .98f ? Color.OrangeRed : db < -35 ? Color.FromArgb(255, 190, 90) : MainForm.Teal;
        if (source.Error != null) { timer.Stop(); status.Text = source.Error; level.ForeColor = Color.OrangeRed; }
        else if (inputMuted) status.Text = "This microphone is muted in Windows. Unmute it before recording.";
        else if (playbackMuted) status.Text = "Windows playback is muted. Unmute headphones to hear your recorded video.";
        else if (peak >= .98f) status.Text = "Input is clipping. Reduce recording gain in Sound settings.";
        else if (!loopback && signalClock.Elapsed.TotalSeconds > 3 && db < -35) status.Text = "Weak microphone signal. Speak close to the headset mic; check its mute switch and input volume.";
        else status.Text = loopback ? "Actual output signal. Play audio to test system sound." : "Speak normally. Aim for speech peaks between -24 and -6 dBFS.";
    }
    private async Task StopTestAsync()
    {
        if (busy) return;
        await gate.WaitAsync(); busy = true; toggle.Enabled = false;
        try {
            float peak = capture?.MaxPeakLevel ?? 0; long frames = capture?.CapturedFrames ?? 0;
            await CleanupAsync();
            status.Text = frames == 0 ? "No audio packets received. Check the connection and selected device." : peak < .01f ? "Audio packets received, but the signal is very quiet. Speak closer to the microphone and check its mute switch." : "Audio signal received. Select Use for recording to enable this source.";
        } finally { devices.Enabled = true; busy = false; toggle.Enabled = true; gate.Release(); }
    }
    private async Task CleanupAsync()
    {
        timer.Stop();
        if (capture != null) await capture.StopAsync();
        drainStop?.Cancel(); client?.Dispose();
        if (drain != null) { try { await drain; } catch (IOException) { } catch (ObjectDisposedException) { } }
        capture?.Dispose(); capture = null; client = null; drain = null; drainStop?.Dispose(); drainStop = null;
        meter.Value = 0; toggle.Text = "Start test";
    }
    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (closeReady) { base.OnFormClosing(e); return; }
        e.Cancel = true; base.OnFormClosing(e); if (closing) return; closing = true; lifetime.Cancel();
        await gate.WaitAsync();
        try { await CleanupAsync(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine(ex); }
        finally { gate.Release(); closeReady = true; if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)(() => { DialogResult = requestedResult; Close(); })); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed) { resourcesDisposed = true; lifetime.Cancel(); timer.Dispose(); capture?.Dispose(); client?.Dispose(); drainStop?.Cancel(); drainStop?.Dispose(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
