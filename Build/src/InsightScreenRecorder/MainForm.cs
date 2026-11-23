using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace InsightScreenRecorder;

public sealed class MainForm : Form
{
    internal static readonly Color Background = Color.FromArgb(12, 20, 32);
    internal static readonly Color Surface = Color.FromArgb(24, 38, 54);
    internal static readonly Color Teal = Color.FromArgb(15, 211, 195);
    internal static readonly Color Ink = Color.FromArgb(233, 241, 247);
    internal static readonly Color Muted = Color.FromArgb(148, 170, 190);
    private RecorderSettings _settings = RecorderSettings.Load(RecorderSettings.DefaultPath);
    private readonly RecordingSession _engine = new();
    private readonly Panel _body = new() { Dock = DockStyle.Fill, Margin = new Padding(3, 0, 3, 0), Padding = new Padding(24, 6, 24, 8) };
    private readonly Label _status = TextLabel("Ready to capture", 10, Muted);
    private ActionButton? _shortcutWarning;
    private Label? _shortcutConflict;
    private string _shortcutFailure = "";
    private readonly ToolTip _statusTip = new();
    private readonly Label _duration = TextLabel("00:00:00", 20, Ink, true);
    private readonly NumericUpDown _fps = Number(5, 60, 30);
    private readonly NumericUpDown _crf = Number(0, 51, 23);
    private readonly NumericUpDown _x = Number(-20000, 20000, 0);
    private readonly NumericUpDown _y = Number(-20000, 20000, 0);
    private readonly NumericUpDown _width = Number(2, 20000, 1280);
    private readonly NumericUpDown _height = Number(2, 20000, 720);
    private readonly ComboBox _screen = Combo();
    private readonly ComboBox _outputSize = Combo();
    private readonly Label _outputPath = TextLabel("", 10, Ink);
    private readonly RecordButton _record = new();
    private readonly ActionButton _stop = new("Stop", true);
    private bool _recordingTransition;
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer _deviceRefresh = new() { Interval = 5000 };
    private readonly Dictionary<string, ActionButton> _navigation = new();
    private string _folder;
    private string? _ffmpeg;
    private bool _starting, _custom;
    private string _page = "Record Studio";
    private Label? _stateBadge;
    private ActionButton? _full, _region;
    private ActionButton? _browse, _chooseEncoder;
    private Control? _studioPage;
    private Label? _captureSummary;
    private HotkeyService? _hotkeys;
    private RecordingPanelForm? _recordingPanel;
    private RecordingBoundaryForm? _boundary;
    private NotifyIcon? _tray;
    private bool _exitRequested;
    private bool _screenshotBusy;
    private readonly MediaDeviceCatalog _devices = new();
    private readonly LicenseClient _license = new();
    private Label? _licenseStatusLabel;
    private readonly List<MediaInputCard> _mediaCards = new();
    private Label? _mediaStatus;
    private bool _refreshingDevices;
    private async Task RefreshDevicesAsync()
    {
        if (_refreshingDevices) return;
        _refreshingDevices = true;
        try { await Task.Run(_devices.Refresh); if (!IsDisposed) UpdateMediaCards(); }
        finally { _refreshingDevices = false; }
    }
    private void UpdateMediaCards()
    {
        if (_licenseStatusLabel != null && !_licenseStatusLabel.IsDisposed) { _licenseStatusLabel.Text = _license.Status; _licenseStatusLabel.ForeColor = _license.IsPro ? Teal : Muted; }
        if (_mediaCards.Count != 3) return;
        bool edit = !_starting && !_engine.IsRecording;
        _mediaCards[0].UpdateState(_settings.CameraEnabled, _devices.Cameras.Count > 0, edit);
        _mediaCards[1].UpdateState(_settings.SystemAudioEnabled, _devices.Speakers.Count > 0, edit);
        _mediaCards[2].UpdateState(_settings.MicrophoneEnabled, _devices.Microphones.Count > 0, edit);
        _mediaCards[1].UpdateDevices(_devices.Speakers, _settings.SystemAudioDevice, edit);
        _mediaCards[2].UpdateDevices(_devices.Microphones, _settings.MicrophoneDevice, edit);
        if (_mediaStatus != null) _mediaStatus.Text = _devices.Error.Length > 0 ? _devices.Error : "Choose ON to include a source · Settings to configure";
    }
    private void ToggleMedia(int index)
    {
        var draft = CurrentSettingsSnapshot();
        if (index == 0) { if (!_license.IsPro && !draft.CameraEnabled) { MessageBox.Show(this, "Webcam overlay is a Pro feature. Activate your license in About.", "Insight Screen Recorder Pro", MessageBoxButtons.OK, MessageBoxIcon.Information); return; } draft.CameraEnabled = !draft.CameraEnabled; if (!_devices.Cameras.Any(d => d.Id == draft.CameraDevice)) draft.CameraDevice = _devices.Cameras.FirstOrDefault()?.Id ?? ""; }
        if (index == 1) { draft.SystemAudioEnabled = !draft.SystemAudioEnabled; if (draft.SystemAudioDevice.Length > 0 && !_devices.Speakers.Any(d => d.Id == draft.SystemAudioDevice)) draft.SystemAudioDevice = ""; }
        if (index == 2) { draft.MicrophoneEnabled = !draft.MicrophoneEnabled; if (draft.MicrophoneDevice.Length > 0 && !_devices.Microphones.Any(d => d.Id == draft.MicrophoneDevice)) draft.MicrophoneDevice = ""; }
        string? error = ApplyPreferences(draft); if (error != null) MessageBox.Show(this, error, "Media input");
    }
    private void PreviewCamera(string id)
    {
        if (_starting || _engine.IsRecording) { MessageBox.Show(this, "Stop recording before opening camera preview."); return; }
        var device = _devices.Cameras.FirstOrDefault(d => d.Id == id);
        if (device == null) { MessageBox.Show(this, "Camera disconnected. Refresh devices."); return; }
        using var preview = new CameraPreviewForm(device); preview.ShowDialog(this);
    }

    public MainForm()
    {
        Text = "Insight Screen Recorder";
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10); ForeColor = Ink; BackColor = Background;
        MinimumSize = new Size(1000, 575); Size = new Size(1200, 575);
        StartPosition = FormStartPosition.CenterScreen; DoubleBuffered = true;
        string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(icon)) Icon = new Icon(icon);
        _folder = _settings.OutputFolder; _fps.Value = _settings.Fps; _crf.Value = _settings.Crf;
        _ffmpeg = ResolveEncoder();
        _screen.Items.Add("All displays");
        int screenNumber = 0;
        foreach (var monitor in Screen.AllScreens) _screen.Items.Add(ScreenNames.SelectionLabel(++screenNumber, monitor.Primary));
        _screen.SelectedIndex = ScreenSelectionIndex(_settings.KeepRecordingMode ? _settings.LastScreenDevice : "", Screen.AllScreens.Select(s => s.DeviceName).ToArray(), Array.FindIndex(Screen.AllScreens, s => s.Primary));
        _screen.SelectedIndexChanged += (_, _) => { if (!_custom) SetRegion(CurrentScreen()); };
        SetRegion(CurrentScreen());
        var previousRegion = new Rectangle(_settings.LastX, _settings.LastY, _settings.LastWidth, _settings.LastHeight);
        if (_settings.KeepRecordingMode && _settings.LastCustomMode && previousRegion.Width >= 2 && previousRegion.Height >= 2 && SystemInformation.VirtualScreen.Contains(previousRegion)) { _custom = true; SetRegion(previousRegion); }
        _outputSize.Items.AddRange(new object[] { "Original size", "1920 px wide", "1280 px wide" });
        _outputSize.SelectedIndex = _settings.OutputWidth == 1920 ? 1 : _settings.OutputWidth == 1280 ? 2 : 0;
        _record.Click += async (sender, args) => { if (_engine.IsRecording) await TogglePauseAsync(); else StartClicked(sender, args); }; _stop.Click += StopClicked; _stop.Enabled = false;
        _stop.Name = "StopSaveButton";
        _clock.Tick += (_, _) => { _duration.Text = _engine.Elapsed.ToString(@"hh\:mm\:ss"); if (_mediaCards.Count == 3) { var levels = _engine.AudioLevels; _mediaCards[1].UpdateLevel(levels.System); _mediaCards[2].UpdateLevel(levels.Microphone); } };
        _deviceRefresh.Tick += async (_, _) => { if (!_starting && !_engine.IsRecording) await RefreshDevicesAsync(); }; _deviceRefresh.Start();
        _engine.Message += _ => { }; // Full diagnostics appear in Settings; completion status stays stable.
        _engine.Finished += (path, code) => UI(() => {
            _clock.Stop(); _duration.Text = _engine.Elapsed.ToString(@"hh\:mm\:ss"); _record.Recording = false; _record.Paused = false; _record.Enabled = true; _stop.Enabled = false;
            CloseRecordingWindows();
            if (_mediaCards.Count == 3) { _mediaCards[1].UpdateLevel(0); _mediaCards[2].UpdateLevel(0); }
            SetInputsEnabled(true);
            if (_stateBadge != null) { _stateBadge.Text = "●  READY"; _stateBadge.ForeColor = Teal; }
            bool saved = code == 0 && File.Exists(path) && new FileInfo(path).Length > 0;
            SetStatus(saved ? "Saved • " + Path.GetFileName(path) : $"Recording failed (exit {code}) • see Settings > Advanced > Recording log", !saved);
            if (saved && _settings.OpenFolderAfterSave) OpenFolder(Path.GetDirectoryName(path)!);
            try { Directory.CreateDirectory(Path.GetDirectoryName(RecorderSettings.DefaultPath)!); File.WriteAllText(Path.Combine(Path.GetDirectoryName(RecorderSettings.DefaultPath)!, "last-recording.log"), _engine.Diagnostics); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        });
        BuildShell(); ShowPage("Record Studio");
        Shown += async (_, _) => {
            await _license.VerifySavedAsync();
            if (!IsDisposed) UpdateMediaCards();
            await RefreshDevicesAsync();
            try {
                var playback = await Task.Run(() => WindowsAudioCapture.ReadEndpointState(true));
                if (playback.Muted || playback.Volume <= 0) SetStatus("Playback is muted in Windows. Unmute your headphones to hear recordings.", true);
            } catch (Exception ex) { Debug.WriteLine(ex); }
        };
        _tray = new NotifyIcon { Icon = Icon ?? SystemIcons.Application, Text = "Insight Screen Recorder", Visible = false };
        var trayMenu = new ContextMenuStrip(); trayMenu.Items.Add("Open Insight Screen Recorder", null, (_, _) => RestoreFromTray()); trayMenu.Items.Add("Exit", null, (_, _) => { _exitRequested = true; try { Close(); } finally { _exitRequested = false; } }); _tray.ContextMenuStrip = trayMenu; _tray.DoubleClick += (_, _) => RestoreFromTray();
        FormClosing += (_, e) => {
            if (_starting || _engine.IsRecording) { e.Cancel = true; MessageBox.Show(this, "Stop & save before closing to finalize your MP4.", "Recording in progress", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (_body.Controls.OfType<PreferencesPage>().FirstOrDefault() is { } preferences && !preferences.CanLeave()) { e.Cancel = true; return; }
            try { SaveSettings(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Unable to save settings"); }
            if (!_exitRequested && _settings.CloseToTray) { e.Cancel = true; Hide(); _tray.Visible = true; }
        };
    }

    private static NumericUpDown Number(int min, int max, int value) => new() { Minimum = min, Maximum = max, Value = value, Dock = DockStyle.Fill, BackColor = Color.FromArgb(17, 29, 43), ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 11), Margin = new Padding(0, 3, 12, 0) };
    private static ComboBox Combo() => new StyledComboBox { Dock = DockStyle.Fill, MinimumSize = new Size(150, 0), DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(17, 29, 43), ForeColor = Ink, Margin = new Padding(0, 3, 10, 0) };
    private static Label TextLabel(string text, float size, Color color, bool bold = false) => new() { Text = text, AutoSize = false, Dock = DockStyle.Fill, ForeColor = color, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = new Padding(0) };
    private static TableLayoutPanel Table(int columns, params float[] widths)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0), BackColor = Color.Transparent };
        foreach (float width in widths) t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        Row(t, SizeType.Percent, 100);
        return t;
    }
    private static void Row(TableLayoutPanel table, SizeType type, float height) => table.RowStyles.Add(new RowStyle(type, height));
    private static Label Caption(string text) => TextLabel(text, 10, Muted);
    private static Panel Card(Control content)
    {
        var panel = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(18), Margin = new Padding(0, 0, 14, 0) };
        panel.Controls.Add(content); return panel;
    }
    private void BuildShell()
    {
        var shell = new TableLayoutPanel { Name = "ApplicationShell", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = Padding.Empty, Margin = Padding.Empty, BackColor = Background };
        Row(shell, SizeType.Absolute, 74); Row(shell, SizeType.Percent, 100); Row(shell, SizeType.Absolute, 44);
        var header = new TableLayoutPanel { Name = "ApplicationHeader", Dock = DockStyle.Fill, ColumnCount = 4, Margin = new Padding(3, 0, 3, 0), Padding = new Padding(24, 16, 24, 4), BackColor = Color.FromArgb(16, 28, 43) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 555)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
        Row(header, SizeType.Percent, 100);
        var picture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 12, 0) };
        string logo = Path.Combine(AppContext.BaseDirectory, "Assets", "app.png"); if (File.Exists(logo)) picture.Image = Image.FromFile(logo);
        header.Controls.Add(picture, 0, 0);
        var branding = new TableLayoutPanel { Name = "Branding", Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        Row(branding, SizeType.Percent, 65); Row(branding, SizeType.Percent, 35);
        var title = TextLabel("Insight Screen Recorder", 19, Ink, true);
        branding.Controls.Add(title); branding.Controls.Add(TextLabel("by INSIGHT Software", 10, Muted)); header.Controls.Add(branding, 1, 0);
        var navigation = new FlowLayoutPanel { Name = "HeaderNavigation", Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 3, 0, 0), Margin = Padding.Empty, BackColor = header.BackColor, AutoScroll = false };
        foreach (string name in new[] { "Record Studio", "Recordings", "Settings", "About" }) {
            var button = new ActionButton(name) { Polished = true, Width = 132, Height = 36, Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(0, 0, 8, 0) }; button.Click += (_, _) => ShowPage(name); _navigation.Add(name, button); navigation.Controls.Add(button);
        }
        header.Controls.Add(navigation, 2, 0);
        _stateBadge = TextLabel("●  READY", 10, Teal, true); _stateBadge.TextAlign = ContentAlignment.MiddleRight; header.Controls.Add(_stateBadge, 3, 0);
        header.SizeChanged += (_, _) => {
            bool compact = header.ClientSize.Width - header.Padding.Horizontal < 1050;
            header.ColumnStyles[2].Width = compact ? 515 : 555; header.ColumnStyles[3].Width = compact ? 90 : 122;
            foreach (var button in _navigation.Values) { button.Width = compact ? 122 : 132; float navPoints = compact ? 8 : 9; if (button.Font.Size != navPoints) { var old = button.Font; button.Font = new Font("Segoe UI", navPoints, FontStyle.Bold); old.Dispose(); } }
            float points = compact ? 13 : 19;
            if (title.Font.Size != points) { var old = title.Font; title.Font = new Font("Segoe UI", points, FontStyle.Bold); old.Dispose(); }
        };
        shell.Controls.Add(header, 0, 0); shell.Controls.Add(_body, 0, 1);
        var status = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(3, 0, 3, 0), Padding = new Padding(24, 0, 24, 0), BackColor = header.BackColor };
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145)); status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145)); status.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); status.Controls.Add(_status, 0, 0);
        _shortcutConflict = TextLabel("Shortcut conflict", 9, Color.FromArgb(255, 199, 100)); _shortcutConflict.TextAlign = ContentAlignment.MiddleRight; status.Controls.Add(_shortcutConflict, 1, 0);
        _shortcutWarning = new ActionButton("Fix shortcut") { Polished = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(8, 5, 0, 5) };
        _shortcutWarning.Click += (_, _) => OpenPreferences("Hotkeys"); status.Controls.Add(_shortcutWarning, 2, 0); SetShortcutWarning(_shortcutFailure); shell.Controls.Add(status, 0, 2); Controls.Add(shell);
    }
    private void ShowPage(string name)
    {
        if (_body.Controls.OfType<PreferencesPage>().FirstOrDefault() is { } preferences) { if (name == "Settings") return; if (!preferences.CanLeave()) return; }
        _licenseStatusLabel = null;
        void ReleaseImages(Control root) { foreach (Control child in root.Controls) { if (child is PictureBox picture) { picture.Image?.Dispose(); picture.Image = null; } ReleaseImages(child); } }
        foreach (Control oldPage in _body.Controls.Cast<Control>().ToArray()) { if (ReferenceEquals(oldPage, _studioPage)) _body.Controls.Remove(oldPage); else { ReleaseImages(oldPage); oldPage.Dispose(); } }
        _body.Controls.Clear(); _chooseEncoder = null; _page = name;
        foreach (var pair in _navigation) { pair.Value.Selected = pair.Key == name; pair.Value.Invalidate(); }
        if (name == "Record Studio") { if (_studioPage == null) { Studio(); _studioPage = _body.Controls[0]; } else _body.Controls.Add(_studioPage); }
        else if (name == "Settings") Settings(); else if (name == "Recordings") Recordings(); else About();
        // Keep the studio's native controls and DPI context intact across navigation.
        static void Reflow(Control parent) { parent.PerformLayout(); foreach (Control child in parent.Controls) Reflow(child); }
        Reflow(_body); Reflow(_body);
    }
    private void Studio()
    {
        var layout = new TableLayoutPanel { Name = "StudioLayout", Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty };
        Row(layout, SizeType.Percent, 100); Row(layout, SizeType.Absolute, 16); Row(layout, SizeType.Absolute, 64);
        var cards = Table(3, 29, 49, 22); cards.Name = "StudioCards";
        var capture = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
        Row(capture, SizeType.Absolute, 28); Row(capture, SizeType.Percent, 100); Row(capture, SizeType.Absolute, 36); Row(capture, SizeType.Absolute, 26);
        var captureTitle = Caption("CAPTURE AREA"); captureTitle.TextAlign = ContentAlignment.MiddleCenter; capture.Controls.Add(captureTitle, 0, 0);
        var captureButtons = Table(2, 50, 50);
        _full = new ActionButton("Full screen") { Dock = DockStyle.Fill, Glyph = "screen", Selected = !_custom, Margin = new Padding(0, 4, 8, 8) };
        _region = new ActionButton("Custom area") { Dock = DockStyle.Fill, Glyph = "region", Selected = _custom, Margin = new Padding(0, 4, 0, 8) };
        foreach (var mode in new[] { _full, _region }) mode.SizeChanged += (_, _) => { float points = mode.Width < 125 ? 9 : 10; if (mode.Font.Size != points) { var previous = mode.Font; mode.Font = new Font("Segoe UI", points, FontStyle.Bold); previous.Dispose(); } };
        _full.Click += (_, _) => { _custom = false; SetRegion(CurrentScreen()); UpdateMode(); };
        _region.Click += (_, _) => SelectRegion();
        captureButtons.Controls.Add(_full, 0, 0); captureButtons.Controls.Add(_region, 1, 0); capture.Controls.Add(captureButtons, 0, 1); capture.Controls.Add(_screen, 0, 2); var captureCard = Card(capture); captureCard.Name = "CaptureCard"; cards.Controls.Add(captureCard, 0, 0);
        _captureSummary = TextLabel("", 9, Muted); capture.Controls.Add(_captureSummary, 0, 3); UpdateCaptureSummary();
        var mediaCard = Card(CreateMediaInputs()); mediaCard.Name = "MediaCard"; cards.Controls.Add(mediaCard, 1, 0);
        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Margin = Padding.Empty };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Row(actions, SizeType.Absolute, 28); Row(actions, SizeType.Absolute, 22); Row(actions, SizeType.Percent, 100); Row(actions, SizeType.Absolute, 4); Row(actions, SizeType.Absolute, 46); Row(actions, SizeType.Absolute, 4); Row(actions, SizeType.Absolute, 40);
        var recordingTitle = Caption("SCREEN CAPTURE"); recordingTitle.TextAlign = ContentAlignment.MiddleCenter; actions.Controls.Add(recordingTitle, 0, 0);
        _record.Dock = DockStyle.Fill; _record.Enabled = !_starting && !_engine.IsRecording; _record.Recording = _engine.IsRecording;
        var recordStage = new Panel { Name = "RecordStage", Dock = DockStyle.Fill, BackColor = Surface, Padding = Padding.Empty, Margin = Padding.Empty };
        recordStage.Controls.Add(_record); actions.Controls.Add(recordStage, 0, 2);
        _duration.TextAlign = ContentAlignment.MiddleCenter; actions.Controls.Add(_duration, 0, 4);
        _stop.Dock = DockStyle.Fill; _stop.Margin = new Padding(12, 3, 12, 3);
        actions.Controls.Add(_stop, 0, 6);
        var actionCard = Card(actions); actionCard.Name = "RecordingCard"; actionCard.Margin = Padding.Empty;
        cards.Controls.Add(actionCard, 2, 0); layout.Controls.Add(cards, 0, 0);
        var output = Table(3, 29, 49, 22); output.Name = "OutputBar"; output.BackColor = Surface;
        var destination = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty, Padding = new Padding(16, 8, 14, 8) };
        destination.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80)); destination.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); destination.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116)); Row(destination, SizeType.Percent, 100);
        destination.Controls.Add(Caption("SAVE TO"), 0, 0); _outputPath.Text = _folder; _outputPath.Margin = new Padding(0, 0, 10, 0); destination.Controls.Add(_outputPath, 1, 0);
        _browse = new ActionButton("Browse") { Polished = true, Name = "BrowseFolderButton", Dock = DockStyle.Fill, Enabled = !_engine.IsRecording && !_starting }; _browse.Click += (_, _) => BrowseFolder(); destination.Controls.Add(_browse, 2, 0);
        output.Controls.Add(destination, 0, 0); output.SetColumnSpan(destination, 2);
        var openFolder = new ActionButton("Open folder") { Polished = true, Name = "OpenFolderButton", Anchor = AnchorStyles.None, Size = new Size(150, 40), Margin = Padding.Empty }; openFolder.Click += (_, _) => OpenFolder(_folder); output.Controls.Add(openFolder, 2, 0); layout.Controls.Add(output, 0, 2);
        _body.Controls.Add(layout); SetInputsEnabled(!_engine.IsRecording && !_starting);
    }
    private Control CreateMediaInputs()
    {
        var group = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0) };
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); Row(group, SizeType.Absolute, 28); Row(group, SizeType.Percent, 100); Row(group, SizeType.Absolute, 42);
        var mediaTitle = Caption("MEDIA INPUTS"); mediaTitle.TextAlign = ContentAlignment.MiddleCenter; group.Controls.Add(mediaTitle, 0, 0);
        var tiles = Table(3, 33.333f, 33.333f, 33.334f);
        _mediaCards.Clear();
        string[] titles = { "Webcam", "System sound", "Microphone" }; string[] glyphs = { "camera", "speaker", "microphone" };
        for (int i = 0; i < 3; i++) {
            int source = i;
            var tile = new MediaInputCard(titles[i], glyphs[i], () => OpenPreferences(source == 0 ? "Camera" : "Sound"), () => ToggleMedia(source), source == 0 ? null : () => TestAudioSource(source == 1)) { Dock = DockStyle.Fill, Margin = new Padding(0, 4, i == 2 ? 0 : 8, 8) };
            _mediaCards.Add(tile); tiles.Controls.Add(tile, i, 0);
            if (source > 0) tile.DeviceSelected += device => { var draft = CurrentSettingsSnapshot(); if (source == 1) draft.SystemAudioDevice = device; else draft.MicrophoneDevice = device; string? error = ApplyPreferences(draft); if (error != null) { MessageBox.Show(this, error, "Audio device"); UpdateMediaCards(); } };
        }
        group.Controls.Add(tiles, 0, 1);
        _mediaStatus = TextLabel("Detecting connected devices…", 9, Muted); group.Controls.Add(_mediaStatus, 0, 2); UpdateMediaCards();
        return group;
    }
    private string? ShowAudioTest(bool loopback, string device, int gain)
    {
        if (_starting || _engine.IsRecording) { MessageBox.Show(this, "Stop recording before testing an audio device."); return null; }
        using var dialog = new AudioTestForm(loopback, device, gain);
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedDeviceId : null;
    }
    private void TestAudioSource(bool loopback)
    {
        var draft = CurrentSettingsSnapshot();
        string? selected = ShowAudioTest(loopback, loopback ? draft.SystemAudioDevice : draft.MicrophoneDevice, loopback ? draft.SystemAudioGain : draft.MicrophoneGain);
        if (selected == null) return;
        if (loopback) { draft.SystemAudioDevice = selected; draft.SystemAudioEnabled = true; } else { draft.MicrophoneDevice = selected; draft.MicrophoneEnabled = true; }
        string? error = ApplyPreferences(draft); if (error != null) MessageBox.Show(this, error, "Audio settings");
    }
    private void OpenPreferences(string category) { ShowPage("Settings"); if (_body.Controls[0] is PreferencesPage preferences) preferences.SelectCategory(category); }
    private void UpdateCaptureSummary() { if (_captureSummary != null) _captureSummary.Text = $"{(_custom ? "Custom area" : "Full screen")} • {_width.Value} × {_height.Value}"; }
    private static TableLayoutPanel Labeled(string text, Control input)
    {
        var field = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) }; field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); Row(field, SizeType.Absolute, 24); Row(field, SizeType.Percent, 100); field.Controls.Add(Caption(text), 0, 0); field.Controls.Add(input, 0, 1); return field;
    }
    internal static int ScreenSelectionIndex(string remembered, IReadOnlyList<string> devices, int primaryIndex)
    {
        if (remembered == "*") return 0;
        for (int i = 0; i < devices.Count; i++) if (string.Equals(devices[i], remembered, StringComparison.OrdinalIgnoreCase)) return i + 1;
        return devices.Count == 0 ? 0 : Math.Clamp(primaryIndex, 0, devices.Count - 1) + 1;
    }
    private string SelectedScreenDevice() => _screen.SelectedIndex <= 0 ? "*" : Screen.AllScreens[Math.Min(_screen.SelectedIndex - 1, Screen.AllScreens.Length - 1)].DeviceName;
    private Rectangle CurrentScreen() => _screen.SelectedIndex <= 0 ? SystemInformation.VirtualScreen : Screen.AllScreens[Math.Min(_screen.SelectedIndex - 1, Screen.AllScreens.Length - 1)].Bounds;
    private void SetRegion(Rectangle r) { _x.Value = Math.Clamp(r.X, -20000, 20000); _y.Value = Math.Clamp(r.Y, -20000, 20000); _width.Value = Math.Clamp(r.Width / 2 * 2, 2, 20000); _height.Value = Math.Clamp(r.Height / 2 * 2, 2, 20000); UpdateCaptureSummary(); }
    private void UpdateMode() { if (_full != null) { _full.Selected = !_custom; _full.Invalidate(); } if (_region != null) { _region.Selected = _custom; _region.Invalidate(); } SetInputsEnabled(!_starting && !_engine.IsRecording); }
    private void SelectRegion()
    {
        if (_starting || _engine.IsRecording) return;
        Hide(); try { using var selector = new RegionSelector(); if (selector.ShowDialog() == DialogResult.OK) { _custom = true; SetRegion(selector.SelectedRegion); UpdateMode(); } } finally { Show(); Activate(); }
    }
    private void BrowseFolder() { using var dialog = new FolderBrowserDialog { SelectedPath = Directory.Exists(_folder) ? _folder : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) }; if (dialog.ShowDialog(this) == DialogResult.OK) { _folder = dialog.SelectedPath; _outputPath.Text = _folder; } }
    private void OpenFolder(string folder) { try { Directory.CreateDirectory(folder); Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); } catch (Exception ex) { SetStatus("Unable to open folder • " + ex.Message, true); } }
    private string? ResolveEncoder() => RecordingEngine.FindFfmpeg(AppContext.BaseDirectory);
    private async void StartClicked(object? sender, EventArgs e)
    {
        if (_starting || _engine.IsRecording) return;
        if (!_license.IsPro && (int)_fps.Value > 30) { MessageBox.Show(this, "Recording above 30 FPS is a Pro feature. Activate a license in About or choose 30 FPS.", "Insight Screen Recorder Pro", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (!_license.IsPro && _settings.CameraEnabled) { MessageBox.Show(this, "Webcam overlay is a Pro feature. Turn off Webcam or activate Pro in About.", "Insight Screen Recorder Pro", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        _starting = true; _record.Enabled = false; SetInputsEnabled(false); SetStatus("Checking recording engine…");
        try {
            _ffmpeg = ResolveEncoder(); if (_ffmpeg is null) throw new FileNotFoundException("The recording component is missing. Restore the complete application package.");
            await RecordingEngine.InspectAsync(_ffmpeg);
            var bounds = new Rectangle((int)_x.Value, (int)_y.Value, (int)_width.Value, (int)_height.Value);
            if (!SystemInformation.VirtualScreen.Contains(bounds)) throw new ArgumentException("Capture bounds must be inside the desktop. Select a screen or choose Custom area.");
            if (string.IsNullOrWhiteSpace(_folder)) throw new ArgumentException("Choose an output folder.");
            if (!_settings.SystemAudioEnabled && !_settings.MicrophoneEnabled && MessageBox.Show(this, "System sound and microphone are both OFF. This recording will have no audio. Continue without sound?", "Audio is off", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) { SetStatus("Enable System sound or Microphone, then record."); return; }
            SaveSettings();
            await RecordingCountdown.ShowAsync(this, bounds, _settings.CountdownSeconds, remaining => SetStatus($"Recording starts in {remaining}…"));
            string file = RecorderSettings.NewRecordingPath(_folder, _settings.VideoFormat);
            await _engine.StartAsync(_ffmpeg, file, (int)_fps.Value, bounds.X, bounds.Y, bounds.Width, bounds.Height, (int)_crf.Value, _settings.OutputWidth, _settings.ShowMouseCursor, _settings.KeyframeSeconds, _settings.ConstantFrameRate, _settings);
            if (!_engine.IsRecording) throw new IOException("The recording engine stopped during startup. " + _engine.Diagnostics);
            _clock.Start(); _record.Recording = true; RefreshRecordingControls(); SetStatus("Recording • Pause temporarily, or Stop & save to finish " + _settings.VideoFormat);
            if (_settings.BeepOnStart) System.Media.SystemSounds.Beep.Play();
            if (_settings.ShowRecordingBoundary) { _boundary = new RecordingBoundaryForm(bounds); _boundary.Show(); }
            if (_settings.ShowRecordingBar && !_settings.HidePanelWhileRecording) { CreateRecordingPanel(); _recordingPanel!.Show(); }
            if (_stateBadge != null) { _stateBadge.Text = "●  RECORDING"; _stateBadge.ForeColor = Color.FromArgb(255, 86, 101); }
        } catch (Exception ex) { if (!_engine.IsRecording) CloseRecordingWindows(); SetStatus("Unable to record • " + ex.Message, true); MessageBox.Show(this, ex.Message, "Unable to record", MessageBoxButtons.OK, MessageBoxIcon.Error); SetInputsEnabled(!_engine.IsRecording); }
        finally { _starting = false; RefreshRecordingControls(); SetInputsEnabled(!_engine.IsRecording); }
    }
    private async void StopClicked(object? sender, EventArgs e) => await StopRecordingAsync();
    private async Task<bool> StopRecordingAsync()
    {
        if (_recordingTransition) return false;
        if (!_engine.IsRecording) return true;
        _recordingTransition = true; RefreshRecordingControls(); SetStatus("Finalizing " + _settings.VideoFormat + "…");
        try { await _engine.StopAsync(); }
        catch (Exception ex) { SetStatus(ex.Message, true); _record.Enabled = !_engine.IsRecording; _stop.Enabled = _engine.IsRecording; }
        finally { _recordingTransition = false; RefreshRecordingControls(); }
        if (!_engine.IsRecording && _stateBadge != null) { _stateBadge.Text = "●  READY"; _stateBadge.ForeColor = Teal; }
        return !_engine.IsRecording;
    }
    private async Task TogglePauseAsync()
    {
        if (_starting || _recordingTransition || !_engine.IsRecording) return;
        bool resuming = _engine.IsPaused; _recordingTransition = true; RefreshRecordingControls();
        SetStatus(resuming ? "Resuming recording…" : "Pausing recording…");
        try {
            if (resuming) { await _engine.ResumeAsync(); SetStatus("Recording resumed • Stop & save when finished"); }
            else { await _engine.PauseAsync(); SetStatus("Paused • Resume continues this recording; Stop & save finishes it"); }
        } catch (Exception ex) { SetStatus(ex.Message, true); MessageBox.Show(this, ex.Message, "Recording controls", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { _recordingTransition = false; RefreshRecordingControls(); }
    }
    private void RefreshRecordingControls()
    {
        bool ready = !_starting && !_recordingTransition;
        _record.Recording = _engine.IsRecording; _record.Paused = _engine.IsPaused;
        _stop.Enabled = _engine.IsRecording && ready; _record.Enabled = ready;
        if (_stateBadge != null && _engine.IsRecording) { _stateBadge.Text = _recordingTransition ? "●  WAIT" : _engine.IsPaused ? "●  PAUSED" : "●  RECORDING"; _stateBadge.ForeColor = _engine.IsPaused ? Color.FromArgb(255, 193, 80) : Color.FromArgb(255, 86, 101); }
        _recordingPanel?.RefreshState();
    }
    private void Settings()
    {
        _body.Controls.Add(new PreferencesPage(CurrentSettingsSnapshot(), ApplyPreferences, () => ShowPage("Record Studio"), _devices, PreviewCamera, ShowAudioTest));
    }
    private string? ApplyPreferences(RecorderSettings draft)
    {
        if (_starting || _engine.IsRecording) return "Stop recording before applying preferences.";
        try {
            if (!_license.IsPro && draft.Fps > 30) throw new InvalidOperationException("Frame rates above 30 FPS require Insight Screen Recorder Pro.");
            if (!_license.IsPro && draft.CameraEnabled) throw new InvalidOperationException("Webcam overlay requires Insight Screen Recorder Pro. Activate a license in About.");
            draft.Validate(); HotkeyService.Bindings(draft);
            var area = new Rectangle(draft.LastX, draft.LastY, draft.LastWidth, draft.LastHeight);
            if (draft.LastCustomMode && !SystemInformation.VirtualScreen.Contains(area)) throw new ArgumentException("Capture bounds must be inside the desktop.");
            bool hotkeysChanged = draft.StartStopHotkey != _settings.StartStopHotkey || draft.ScreenshotHotkey != _settings.ScreenshotHotkey || draft.ToolbarHotkey != _settings.ToolbarHotkey;
            ValidateOutputDirectory(draft.OutputFolder); ValidateOutputDirectory(draft.ScreenshotFolder);
            if (hotkeysChanged) _hotkeys?.Apply(draft);
            try { draft.Save(RecorderSettings.DefaultPath); } catch { if (hotkeysChanged) _hotkeys?.Apply(_settings); throw; }
            _settings = draft; _folder = draft.OutputFolder; _outputPath.Text = _folder;
            if (hotkeysChanged) SetShortcutWarning("");
            _fps.Value = draft.Fps; _crf.Value = draft.Crf; _outputSize.SelectedIndex = draft.OutputWidth == 1920 ? 1 : draft.OutputWidth == 1280 ? 2 : 0;
            _custom = draft.LastCustomMode; SetRegion(_custom ? area : CurrentScreen()); UpdateMode();
            UpdateMediaCards(); SetStatus("Preferences saved."); return null;
        } catch (Exception ex) { return ex.Message; }
    }
    private void Recordings()
    {
        string encoder = ResolveEncoder() ?? Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe"); _body.Controls.Add(new RecordingLibraryPage(_folder, Path.Combine(Path.GetDirectoryName(encoder)!, "ffprobe.exe")));
    }
    internal static void ValidateOutputDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory)) throw new ArgumentException("Choose an absolute output folder path.");
        Directory.CreateDirectory(directory); string check = Path.Combine(directory, ".insight-write-check-" + Guid.NewGuid().ToString("N"));
        using var file = new FileStream(check, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose); file.WriteByte(0);
    }
    private void About()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(34, 10, 34, 8) }; Row(layout, SizeType.Absolute, 82); Row(layout, SizeType.Absolute, 38); Row(layout, SizeType.Percent, 100); Row(layout, SizeType.Absolute, 28);
        var heading = Table(2, 15, 85);
        var logo = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 14, 0) }; string asset = Path.Combine(AppContext.BaseDirectory, "Assets", "app.png"); if (File.Exists(asset)) logo.Image = Image.FromFile(asset); heading.Controls.Add(logo, 0, 0);
        var brand = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty }; Row(brand, SizeType.Percent, 65); Row(brand, SizeType.Percent, 35);
        brand.Controls.Add(TextLabel("Insight Screen Recorder", 21, Ink, true), 0, 0);
        brand.Controls.Add(TextLabel("Version " + Application.ProductVersion + "  ·  INSIGHT Software", 10, Teal), 0, 1); heading.Controls.Add(brand, 1, 0); layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(TextLabel("License and edition", 16, Teal, true), 0, 1);
        var card = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(22, 14, 22, 14), Margin = new Padding(0, 4, 0, 4) };
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Surface, Margin = Padding.Empty }; Row(content, SizeType.Absolute, 30); Row(content, SizeType.Absolute, 40); Row(content, SizeType.Absolute, 40); Row(content, SizeType.Absolute, 55); Row(content, SizeType.Percent, 100);
        var status = TextLabel(_license.Status, 10, Muted, true); status.Dock = DockStyle.Fill; content.Controls.Add(status, 0, 0);
        var keyRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Surface, Margin = Padding.Empty }; keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        var key = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "Enter your license key", BorderStyle = BorderStyle.FixedSingle, BackColor = Background, ForeColor = Ink, Margin = new Padding(0, 3, 10, 3) };
        var activate = new ActionButton("Activate license", true) { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 1) }; keyRow.Controls.Add(key, 0, 0); keyRow.Controls.Add(activate, 1, 0); content.Controls.Add(keyRow, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Surface, Padding = Padding.Empty, Margin = Padding.Empty };
        var deactivate = new ActionButton("Deactivate this device") { Width = 190, Height = 34, Margin = new Padding(0, 2, 10, 0) };
        var refresh = new ActionButton("Check license status") { Width = 180, Height = 34, Margin = new Padding(0, 2, 0, 0) }; actions.Controls.Add(deactivate); actions.Controls.Add(refresh); content.Controls.Add(actions, 0, 2);
        var benefits = TextLabel("Free: screen recording, system audio and microphone · 30 FPS\nPro: webcam overlay and recording up to 60 FPS", 10, Muted); benefits.Dock = DockStyle.Fill; content.Controls.Add(benefits, 0, 3);
        var note = TextLabel("Online validation through INSIGHT Software Hub. Your key is encrypted for this Windows account.", 9, Muted); note.Dock = DockStyle.Fill; content.Controls.Add(note, 0, 4);
        card.Controls.Add(content); layout.Controls.Add(card, 0, 2);
        layout.Controls.Add(TextLabel("FFmpeg is a separate recording engine; its license applies.", 9, Muted), 0, 3); _body.Controls.Add(layout); _licenseStatusLabel = status;
        async Task UpdateLicenseAsync(Func<Task<string>> action)
        {
            activate.Enabled = deactivate.Enabled = refresh.Enabled = false; status.Text = "Contacting the license server…";
            try { status.Text = await action(); key.Clear(); }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { activate.Enabled = deactivate.Enabled = refresh.Enabled = true; UpdateMediaCards(); }
        }
        activate.Click += async (_, _) => { if (string.IsNullOrWhiteSpace(key.Text)) { status.Text = "Enter your license key."; return; } await UpdateLicenseAsync(() => _license.ActivateAsync(key.Text)); };
        deactivate.Click += async (_, _) => await UpdateLicenseAsync(_license.DeactivateThisDeviceAsync);
        refresh.Click += async (_, _) => await UpdateLicenseAsync(_license.VerifySavedAsync);
    }
    private void SaveSettings() { _settings.OutputFolder = _folder; _settings.Fps = (int)_fps.Value; _settings.Crf = (int)_crf.Value; _settings.OutputWidth = _outputSize.SelectedIndex == 1 ? 1920 : _outputSize.SelectedIndex == 2 ? 1280 : 0; _settings.LastScreenDevice = SelectedScreenDevice(); _settings.LastCustomMode = _custom; _settings.LastX = (int)_x.Value; _settings.LastY = (int)_y.Value; _settings.LastWidth = (int)_width.Value; _settings.LastHeight = (int)_height.Value; _settings.Save(RecorderSettings.DefaultPath); }
    private RecorderSettings CurrentSettingsSnapshot() { var draft = _settings.Clone(); draft.OutputFolder = _folder; draft.Fps = (int)_fps.Value; draft.Crf = (int)_crf.Value; draft.OutputWidth = _outputSize.SelectedIndex == 1 ? 1920 : _outputSize.SelectedIndex == 2 ? 1280 : 0; draft.LastScreenDevice = SelectedScreenDevice(); draft.LastCustomMode = _custom; draft.LastX = (int)_x.Value; draft.LastY = (int)_y.Value; draft.LastWidth = (int)_width.Value; draft.LastHeight = (int)_height.Value; return draft; }
    private void SetInputsEnabled(bool enabled) { foreach (Control c in new Control?[] { _fps, _crf, _outputSize, _full, _region, _browse, _chooseEncoder }.OfType<Control>()) c.Enabled = enabled; foreach (Control c in new Control[] { _x, _y, _width, _height }) c.Enabled = enabled && _custom; _screen.Enabled = enabled && !_custom; UpdateMediaCards(); }
    private void SetStatus(string text, bool error = false) { _status.Text = text; _status.ForeColor = error ? Color.FromArgb(255, 156, 134) : Muted; _statusTip.SetToolTip(_status, text); }
    private void SetShortcutWarning(string text) { _shortcutFailure = text; if (_shortcutWarning == null) return; bool visible = text.Length > 0; _shortcutWarning.Visible = visible; _statusTip.SetToolTip(_shortcutWarning, text); if (_shortcutConflict != null) { _shortcutConflict.Visible = visible; _statusTip.SetToolTip(_shortcutConflict, text); } if (_shortcutWarning.Parent is TableLayoutPanel bar) { bar.ColumnStyles[1].Width = visible ? 145 : 0; bar.ColumnStyles[2].Width = visible ? 145 : 0; } }
    private void UI(Action action) { if (IsDisposed || !IsHandleCreated) return; try { BeginInvoke(action); } catch (InvalidOperationException) { } }
    internal void RestoreFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); if (_tray != null) _tray.Visible = false; }
    private void CreateRecordingPanel() { _recordingPanel?.Dispose(); _recordingPanel = new RecordingPanelForm(_settings, () => _engine.Elapsed, StopRecordingAsync, () => _ = TakeScreenshotAsync(), TogglePauseAsync, () => _engine.IsPaused, () => _recordingTransition); }
    private void CloseRecordingWindows() { _recordingPanel?.Dispose(); _recordingPanel = null; _boundary?.Dispose(); _boundary = null; }
    private async Task TakeScreenshotAsync()
    {
        if (_screenshotBusy) return;
        _screenshotBusy = true;
        var snapshot = _settings.Clone();
        var area = new Rectangle((int)_x.Value, (int)_y.Value, (int)_width.Value, (int)_height.Value);
        var panel = _recordingPanel;
        bool restore = snapshot.HideMainForScreenshot && Visible; bool panelVisible = panel?.Visible == true;
        try {
            if (restore) Hide(); if (panelVisible && !snapshot.AllowPanelCapture) panel?.Hide(); await Task.Delay(180);
            if (!SystemInformation.VirtualScreen.Contains(area)) throw new ArgumentException("Screenshot area must be inside the desktop.");
            Directory.CreateDirectory(snapshot.ScreenshotFolder);
            using var bitmap = new Bitmap(area.Width, area.Height); using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(area.Location, Point.Empty, area.Size);
            string extension = snapshot.ScreenshotFormat.ToLowerInvariant(); string path = Path.Combine(snapshot.ScreenshotFolder, $"Insight_Screenshot_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.{extension}");
            bitmap.Save(path, extension == "jpg" ? System.Drawing.Imaging.ImageFormat.Jpeg : extension == "bmp" ? System.Drawing.Imaging.ImageFormat.Bmp : System.Drawing.Imaging.ImageFormat.Png);
            SetStatus("Screenshot saved • " + Path.GetFileName(path));
        } catch (Exception ex) { SetStatus("Screenshot failed • " + ex.Message, true); }
        finally { _screenshotBusy = false; if (restore && !IsDisposed) Show(); if (panelVisible && panel is { IsDisposed: false } && !IsDisposed) panel.Show(); }
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); _hotkeys = new HotkeyService(Handle); try { _hotkeys.Apply(_settings); SetShortcutWarning(""); } catch (Exception ex) { SetShortcutWarning(ex.Message); } }
    protected override void OnHandleDestroyed(EventArgs e) { _hotkeys?.Dispose(); _hotkeys = null; base.OnHandleDestroyed(e); }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x312) {
            switch (m.WParam.ToInt32()) {
                case 1: if (_engine.IsRecording) StopClicked(null, EventArgs.Empty); else if (!_starting) StartClicked(null, EventArgs.Empty); break;
                case 2: _ = TakeScreenshotAsync(); break;
                case 3: if (_engine.IsRecording) { if (_recordingPanel == null) CreateRecordingPanel(); if (_recordingPanel!.Visible) _recordingPanel.Hide(); else _recordingPanel.Show(); } break;
            }
            return;
        }
        base.WndProc(ref m);
    }
    protected override void OnFormClosed(FormClosedEventArgs e) { _clock.Dispose(); _deviceRefresh.Dispose(); _statusTip.Dispose(); _engine.Dispose(); CloseRecordingWindows(); _hotkeys?.Dispose(); _tray?.Dispose(); if (_studioPage?.Parent == null) _studioPage?.Dispose(); foreach (Control model in new Control[] { _fps, _crf, _outputSize, _x, _y, _width, _height }) if (model.Parent == null) model.Dispose(); base.OnFormClosed(e); }
}

internal sealed class RoundedPanel : Panel
{
    public RoundedPanel() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); BackColor = MainForm.Surface; }
    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; using var path = Shape.Round(new RectangleF(1, 1, Width - 3, Height - 3), 16); using var fill = new SolidBrush(MainForm.Surface); using var border = new Pen(Color.FromArgb(48, 70, 88)); e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); }
}
internal static class Shape
{
    public static GraphicsPath Round(RectangleF r, float radius) { var path = new GraphicsPath(); float d = radius * 2; path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90); path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path; }
}
internal sealed class StyledComboBox : ComboBox
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string? CompactCaption { get; set; }
    protected override void OnDropDown(EventArgs e)
    {
        int desired = Math.Max(Width, Items.Cast<object>().Select(item => TextRenderer.MeasureText(GetItemText(item), Font).Width + 32).DefaultIfEmpty(Width).Max());
        DropDownWidth = Math.Min(desired, Math.Max(Width, Screen.FromControl(this).WorkingArea.Width - 24)); base.OnDropDown(e);
    }
    public StyledComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        // Settings lays out these controls after handle creation. Repaint the
        // entire resized surface instead of retaining its first narrow paint.
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using var border = new Pen(Color.FromArgb(63, 89, 111)); e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        var text = new Rectangle(6, 0, Math.Max(1, Width - 28), Height);
        TextRenderer.DrawText(e.Graphics, CompactCaption ?? Text, Font, text, Enabled ? ForeColor : MainForm.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        using var arrow = new Pen(MainForm.Teal, 2); int x = Width - 16, y = Height / 2; e.Graphics.DrawLines(arrow, new[] { new Point(x - 4, y - 2), new Point(x, y + 2), new Point(x + 4, y - 2) });
    }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        using var brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(20, 83, 91) : BackColor); e.Graphics.FillRectangle(brush, e.Bounds);
        string? caption = (e.State & DrawItemState.ComboBoxEdit) != 0 ? CompactCaption ?? GetItemText(Items[e.Index]) : GetItemText(Items[e.Index]);
        TextRenderer.DrawText(e.Graphics, caption, Font, e.Bounds, ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}



