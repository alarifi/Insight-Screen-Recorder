namespace InsightScreenRecorder;

internal sealed class PreferencesPage : UserControl
{
    internal static readonly string[] Categories = { "Output", "Sound", "Camera", "Recording Panel", "Mouse", "Hotkeys", "General", "Advanced" };
    private readonly MediaDeviceCatalog _devices;
    private readonly Action<string>? _preview;
    private readonly Func<bool, string, int, string?>? _audioTest;
    private RecorderSettings _draft;
    private RecorderSettings _original;
    private readonly Func<RecorderSettings, string?> _save;
    private readonly Action _cancel;
    private readonly Panel _editor = new() { Dock = DockStyle.Fill, BackColor = MainForm.Surface, Padding = new Padding(22, 12, 22, 12) };
    private readonly LibraryScroll _editorScroll = new() { Dock = DockStyle.Right, Width = 14 };
    private int _scrollOffset;
    private readonly Dictionary<string, ActionButton> _navigation = new();
    private readonly Label _notice = Label("Changes apply when you choose Save.", 9);
    private string _category = "Output";
    private TableLayoutPanel _fields = null!;
    internal RecorderSettings Draft => _draft;
    internal bool HasUnsavedChanges => System.Text.Json.JsonSerializer.Serialize(_draft) != System.Text.Json.JsonSerializer.Serialize(_original);
    private void Changed() { _notice.Text = HasUnsavedChanges ? "Unsaved changes · choose Save to apply" : "Changes apply when you choose Save."; _notice.ForeColor = HasUnsavedChanges ? Color.FromArgb(255, 199, 100) : MainForm.Muted; }
    internal bool CanLeave()
    {
        if (!HasUnsavedChanges) return true;
        return ResolveLeave(MessageBox.Show(FindForm(), "Save your settings changes before leaving?\n\nYes: Save    No: Discard    Cancel: Stay here", "Unsaved settings", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question));
    }
    internal bool ResolveLeave(DialogResult result)
    {
        if (result == DialogResult.Yes) return SaveDraft();
        if (result != DialogResult.No) return false;
        _draft = _original.Clone(); SelectCategory(_category); return true;
    }

    public PreferencesPage(RecorderSettings settings, Func<RecorderSettings, string?> save, Action cancel, MediaDeviceCatalog? devices = null, Action<string>? preview = null, Func<bool, string, int, string?>? audioTest = null)
    {
        Dock = DockStyle.Fill; BackColor = MainForm.Background; ForeColor = MainForm.Ink;
        _devices = devices ?? new MediaDeviceCatalog(); _preview = preview; _audioTest = audioTest;
        _original = settings.Clone(); _draft = settings.Clone(); _save = save; _cancel = cancel;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = new Padding(0), BackColor = MainForm.Background };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58)); heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        heading.Controls.Add(Label("Settings", 18, true), 0, 0); _notice.TextAlign = ContentAlignment.MiddleRight; heading.Controls.Add(_notice, 1, 0); root.Controls.Add(heading, 0, 0);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 184)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var sidebar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = false, Padding = new Padding(0, 4, 12, 0), Margin = new Padding(0) };
        foreach (string category in Categories) {
            var button = new ActionButton(category) { Polished = true, Width = 170, Height = 32, Margin = new Padding(0, 0, 0, 4), Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            button.Click += (_, _) => SelectCategory(category); _navigation.Add(category, button); sidebar.Controls.Add(button);
        }
        var editorFrame = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = MainForm.Surface }; editorFrame.Controls.Add(_editor); editorFrame.Controls.Add(_editorScroll);
        _editorScroll.Changed += value => ScrollEditor(value); _editor.MouseWheel += (_, e) => ScrollEditor(_scrollOffset - Math.Sign(e.Delta) * 70);
        body.Controls.Add(sidebar, 0, 0); body.Controls.Add(editorFrame, 1, 0); root.Controls.Add(body, 0, 1);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, Padding = new Padding(0, 10, 0, 0), Margin = new Padding(0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var reset = new ActionButton("Reset all settings") { Polished = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0), Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        reset.Click += (_, _) => { if (MessageBox.Show(FindForm(), "Reset ALL settings to their defaults?\n\nRecordings and your license will be kept. Choose Save afterward to apply.", "Reset all settings", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return; _draft = new RecorderSettings(); SelectCategory(_category); Changed(); };
        var cancelButton = new ActionButton("Cancel") { Polished = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) }; cancelButton.Click += (_, _) => { _draft = _original.Clone(); _cancel(); };
        var saveButton = new ActionButton("Save", true) { Polished = true, Dock = DockStyle.Fill, Margin = new Padding(0) }; saveButton.Click += (_, _) => SaveDraft();
        footer.Controls.Add(reset, 0, 0); footer.Controls.Add(cancelButton, 2, 0); footer.Controls.Add(saveButton, 3, 0); root.Controls.Add(footer, 0, 2);
        Controls.Add(root); _editor.SizeChanged += (_, _) => FitFields(); SelectCategory("Output");
        Load += (_, _) => BeginInvoke((Action)FitFields);
    }
    private static Label Label(string text, float points = 10, bool bold = false) => new() { Text = text, Dock = DockStyle.Fill, ForeColor = MainForm.Ink, Font = new Font("Segoe UI", points, bold ? FontStyle.Bold : FontStyle.Regular), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 10, 0), BackColor = Color.Transparent };
    private void FitFields() { if (_fields != null) { _fields.Width = Math.Max(250, _editor.ClientSize.Width - _editor.Padding.Horizontal); _fields.PerformLayout(); var widths = _fields.GetColumnWidths(); if (widths.Length > 1) foreach (ComboBox combo in _fields.Controls.OfType<ComboBox>()) combo.Width = Math.Max(120, widths[1] - combo.Margin.Horizontal); ScrollEditor(_scrollOffset); } }
    private void ScrollEditor(int offset) { if (_fields == null) return; _editorScroll.Maximum = Math.Max(0, _fields.Height + _editor.Padding.Vertical - _editor.ClientSize.Height); _scrollOffset = Math.Clamp(offset, 0, _editorScroll.Maximum); _fields.Top = _editor.Padding.Top - _scrollOffset; _editorScroll.Value = _scrollOffset; _editorScroll.Invalidate(); }
    private void RevealField(Control input) { var top = _editor.PointToClient(input.PointToScreen(Point.Empty)).Y; if (top < 8) ScrollEditor(_scrollOffset + top - 8); else if (top + input.Height > _editor.Height - 8) ScrollEditor(_scrollOffset + top + input.Height - _editor.Height + 8); }
    internal void SelectCategory(string category)
    {
        if (!Categories.Contains(category)) throw new ArgumentException("Unknown preference category.");
        _category = category;
        foreach (var pair in _navigation) pair.Value.Selected = pair.Key == category;
        foreach (Control old in _editor.Controls.Cast<Control>().ToArray()) old.Dispose();
        _editor.Controls.Clear(); _scrollOffset = 0;
        _fields = new TableLayoutPanel { ColumnCount = 2, RowCount = 0, AutoSize = false, Location = new Point(22, 12), Margin = new Padding(0), BackColor = MainForm.Surface };
        _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 232)); _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _editor.Controls.Add(_fields); FitFields();
        _fields.MouseWheel += (_, e) => ScrollEditor(_scrollOffset - Math.Sign(e.Delta) * 70);
        switch (category) {
            case "Output": Output(); break;
            case "Sound": Sound(); break;
            case "Camera": Camera(); break;
            case "Recording Panel": RecordingPanel(); break;
            case "Mouse": MouseSettings(); break;
            case "Hotkeys": Hotkeys(); break;
            case "General": General(); break;
            case "Advanced": More(); break;
        }
        _fields.Height = (int)_fields.RowStyles.Cast<RowStyle>().Sum(row => row.Height);
        _fields.PerformLayout(); FitFields(); Changed(); if (IsHandleCreated) BeginInvoke((Action)FitFields);
    }
    private void AddRow(string caption, Control input, int height = 38)
    {
        int row = _fields.RowCount++; _fields.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        var label = Label(caption); label.MouseWheel += (_, e) => ScrollEditor(_scrollOffset - Math.Sign(e.Delta) * 70); _fields.Controls.Add(label, 0, row); input.Dock = DockStyle.Fill; input.Margin = new Padding(0, 5, 0, 5); _fields.Controls.Add(input, 1, row);
        input.Enter += (_, _) => RevealField(input);
    }
    private void Section(string title)
    {
        int row = _fields.RowCount++; _fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); var titleLabel = Label(title, 12, true); titleLabel.ForeColor = MainForm.Teal; _fields.Controls.Add(titleLabel, 0, row); _fields.SetColumnSpan(titleLabel, 2);
    }
    private void Note(string text, int height = 58)
    {
        int row = _fields.RowCount++; _fields.RowStyles.Add(new RowStyle(SizeType.Absolute, height)); var note = Label(text, 9); note.ForeColor = MainForm.Muted; _fields.Controls.Add(note, 0, row); _fields.SetColumnSpan(note, 2);
    }
    private void Choice(string caption, string[] choices, string selected, Action<string> changed, bool enabled = true)
    {
        var combo = new StyledComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(17, 29, 43), ForeColor = MainForm.Ink, Enabled = enabled, FlatStyle = FlatStyle.Flat };
        combo.Items.AddRange(choices); combo.SelectedIndex = Math.Max(0, Array.IndexOf(choices, selected)); combo.SelectedIndexChanged += (_, _) => { changed((string)combo.SelectedItem!); Changed(); }; AddRow(caption, combo);
    }
    private NumericUpDown Numeric(string caption, int minimum, int maximum, int value, Action<int> changed, int increment = 1)
    {
        var number = new NumericUpDown { Minimum = minimum, Maximum = maximum, Value = Math.Clamp(value, minimum, maximum), Increment = increment, BackColor = Color.FromArgb(17, 29, 43), ForeColor = MainForm.Ink, BorderStyle = BorderStyle.FixedSingle };
        number.ValueChanged += (_, _) => { changed((int)number.Value); Changed(); }; AddRow(caption, number);
        return number;
    }
    private void Check(string text, bool value, Action<bool> changed, bool enabled = true)
    {
        var box = new CheckBox { Text = text, Checked = value, Enabled = enabled, ForeColor = MainForm.Ink, BackColor = Color.Transparent, CheckAlign = ContentAlignment.MiddleLeft, AutoSize = false, FlatStyle = FlatStyle.Standard };
        box.CheckedChanged += (_, _) => { changed(box.Checked); Changed(); };
        int row = _fields.RowCount++; _fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); box.Dock = DockStyle.Fill; box.Margin = new Padding(0, 3, 0, 3); box.Enter += (_, _) => RevealField(box); _fields.Controls.Add(box, 0, row); _fields.SetColumnSpan(box, 2);
    }
    private void PathField(string caption, string value, Action<string> changed)
    {
        var line = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, Margin = new Padding(0) }; line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); line.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76)); line.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var text = new TextBox { Dock = DockStyle.Fill, Text = value, BackColor = Color.FromArgb(17, 29, 43), ForeColor = MainForm.Ink, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 2, 8, 0) }; text.TextChanged += (_, _) => { changed(text.Text); Changed(); };
        var browse = new ActionButton("Browse") { Polished = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9), Margin = new Padding(0) };
        browse.Click += (_, _) => { using var dialog = new FolderBrowserDialog { SelectedPath = Directory.Exists(text.Text) ? text.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) }; if (dialog.ShowDialog(FindForm()) == DialogResult.OK) text.Text = dialog.SelectedPath; };
        line.Controls.Add(text, 0, 0); line.Controls.Add(browse, 1, 0); AddRow(caption, line);
    }
    private void Output()
    {
        Section("Directory settings"); PathField("Recording files", _draft.OutputFolder, v => _draft.OutputFolder = v); PathField("Screenshot files", _draft.ScreenshotFolder, v => _draft.ScreenshotFolder = v);
        Section("Screenshot settings"); Choice("Screenshot format", new[] { "PNG", "JPG", "BMP" }, _draft.ScreenshotFormat, v => _draft.ScreenshotFormat = v);
        Section("Screen recording settings"); Choice("Video format", new[] { "MP4 — Recommended", "MKV" }, _draft.VideoFormat == "MP4" ? "MP4 — Recommended" : "MKV", v => _draft.VideoFormat = v.StartsWith("MP4") ? "MP4" : "MKV");
        Note("Video codec: H.264 · 8-bit", 34);
        Numeric("Frame rate (FPS)", 5, 60, _draft.Fps, v => _draft.Fps = v);
        Choice("Frame rate mode", new[] { "Variable frame rate", "Constant frame rate" }, _draft.ConstantFrameRate ? "Constant frame rate" : "Variable frame rate", v => _draft.ConstantFrameRate = v.StartsWith("Constant"));
        Numeric("Keyframe interval (seconds)", 1, 10, _draft.KeyframeSeconds, v => _draft.KeyframeSeconds = v);
        Numeric("Quality (CRF)", 0, 51, _draft.Crf, v => _draft.Crf = v);
        Note("CRF 18: high quality • 23: balanced • 28: smaller files. Lower values increase quality and file size.", 48);
        Choice("Output resolution", new[] { "Original size", "1920 px wide", "1280 px wide" }, _draft.OutputWidth == 1920 ? "1920 px wide" : _draft.OutputWidth == 1280 ? "1280 px wide" : "Original size", v => _draft.OutputWidth = v.StartsWith("1920") ? 1920 : v.StartsWith("1280") ? 1280 : 0);
        Section("Capture bounds");
        var boundsInputs = new List<Control>();
        Check("Use custom capture bounds", _draft.LastCustomMode, v => { _draft.LastCustomMode = v; foreach (var input in boundsInputs) input.Enabled = v; });
        boundsInputs.Add(Numeric("X position", -20000, 20000, _draft.LastX, v => _draft.LastX = v));
        boundsInputs.Add(Numeric("Y position", -20000, 20000, _draft.LastY, v => _draft.LastY = v));
        boundsInputs.Add(Numeric("Capture width (pixels)", 2, 20000, _draft.LastWidth, v => _draft.LastWidth = v, 2));
        boundsInputs.Add(Numeric("Capture height (pixels)", 2, 20000, _draft.LastHeight, v => _draft.LastHeight = v, 2));
        foreach (var input in boundsInputs) input.Enabled = _draft.LastCustomMode;
        Note("Capture bounds must stay inside the desktop. Width and height must be even. Turn off custom bounds to use the selected screen.", 62);
    }
    private void DeviceChoice(string caption, (string Id, string Name)[] devices, string selected, Action<string> changed)
    {
        if (devices.Length == 0) { Note("No " + caption.ToLowerInvariant() + " detected. Connect a device, then choose Refresh devices.", 54); return; }
        var combo = new StyledComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(17, 29, 43), ForeColor = MainForm.Ink, FlatStyle = FlatStyle.Flat };
        combo.Items.AddRange(devices.Select(d => d.Name).ToArray());
        int selectedIndex = Array.FindIndex(devices, d => d.Id == selected);
        if (selectedIndex < 0 && selected.Length > 0) { combo.Items.Add("Selected device unavailable"); combo.SelectedIndex = combo.Items.Count - 1; } else combo.SelectedIndex = Math.Max(0, selectedIndex);
        combo.SelectedIndexChanged += (_, _) => { if (combo.SelectedIndex >= devices.Length) return; changed(devices[combo.SelectedIndex].Id); Changed(); }; AddRow(caption, combo);
    }
    private void RefreshDevices()
    {
        var refresh = new ActionButton("Refresh devices");
        refresh.Click += async (_, _) => { refresh.Enabled = false; await Task.Run(_devices.Refresh); if (!IsDisposed) SelectCategory(_category); };
        AddRow("Connected devices", refresh);
        if (_devices.Error.Length > 0) Note(_devices.Error, 76);
    }
    private void Sound()
    {
        Section("System sound");
        DeviceChoice("Playback device", (new[] { ("", "Windows default (recommended)") }).Concat(_devices.Speakers.Select(d => (d.Id, d.Name))).ToArray(), _draft.SystemAudioDevice, v => _draft.SystemAudioDevice = v);
        Check("Record system sound", _draft.SystemAudioEnabled, v => _draft.SystemAudioEnabled = v, _devices.Speakers.Count > 0);
        Numeric("System sound volume (%)", 0, 200, _draft.SystemAudioGain, v => _draft.SystemAudioGain = v);
        AudioTestButton(true);
        Section("Microphone");
        DeviceChoice("Microphone", (new[] { ("", "Windows default (recommended)") }).Concat(_devices.Microphones.Select(d => (d.Id, d.Name))).ToArray(), _draft.MicrophoneDevice, v => _draft.MicrophoneDevice = v);
        Check("Record microphone", _draft.MicrophoneEnabled, v => _draft.MicrophoneEnabled = v, _devices.Microphones.Count > 0);
        Numeric("Microphone volume (%)", 0, 200, _draft.MicrophoneGain, v => _draft.MicrophoneGain = v);
        AudioTestButton(false);
        Section("Audio output · AAC");
        Choice("Sample rate", new[] { "44100", "48000" }, _draft.AudioSampleRate.ToString(), v => _draft.AudioSampleRate = int.Parse(v));
        Choice("Channels", new[] { "Mono", "Stereo" }, _draft.AudioChannels == 1 ? "Mono" : "Stereo", v => _draft.AudioChannels = v == "Mono" ? 1 : 2);
        Choice("Bitrate (kbps)", new[] { "96", "128", "192", "256", "320" }, _draft.AudioBitrate.ToString(), v => _draft.AudioBitrate = int.Parse(v));
        Note("Volumes affect the recording only. System sound and microphone are mixed when both are enabled.", 60); RefreshDevices();
    }
    private void AudioTestButton(bool loopback)
    {
        var test = new ActionButton(loopback ? "Test system sound" : "Test microphone") { Enabled = _audioTest != null && (loopback ? _devices.Speakers.Count : _devices.Microphones.Count) > 0 };
        test.Click += (_, _) => {
            string? selected = _audioTest?.Invoke(loopback, loopback ? _draft.SystemAudioDevice : _draft.MicrophoneDevice, loopback ? _draft.SystemAudioGain : _draft.MicrophoneGain);
            if (selected == null) return;
            if (loopback) { _draft.SystemAudioDevice = selected; _draft.SystemAudioEnabled = true; } else { _draft.MicrophoneDevice = selected; _draft.MicrophoneEnabled = true; }
            SelectCategory("Sound");
        };
        AddRow("Live input level", test);
    }
    private void Camera()
    {
        Section("Camera overlay");
        DeviceChoice("Camera device", _devices.Cameras.Select(d => (d.Id, d.Name)).ToArray(), _draft.CameraDevice, v => _draft.CameraDevice = v);
        Check("Enable camera overlay", _draft.CameraEnabled, v => _draft.CameraEnabled = v, _devices.Cameras.Count > 0);
        Choice("Overlay position", new[] { "Top left", "Top right", "Bottom left", "Bottom right" }, _draft.CameraPosition, v => _draft.CameraPosition = v);
        Numeric("Overlay width (%)", 10, 50, _draft.CameraWidthPercent, v => _draft.CameraWidthPercent = v);
        Check("Mirror camera horizontally", _draft.CameraMirror, v => _draft.CameraMirror = v);
        var preview = new ActionButton("Preview camera") { Enabled = _devices.Cameras.Count > 0 && _preview != null };
        preview.Click += (_, _) => _preview?.Invoke(_draft.CameraDevice.Length > 0 ? _draft.CameraDevice : _devices.Cameras.FirstOrDefault()?.Id ?? ""); AddRow("Test device access", preview);
        Note("Preview opens the actual device. Close preview before recording. Windows permission or driver errors are reported explicitly.", 70); RefreshDevices();
    }
    private void RecordingPanel()
    {
        Section("Recording panel"); Choice("Default style", new[] { "Full panel", "Floating ball" }, _draft.PanelStyle, v => _draft.PanelStyle = v);
        Note("Full panel: timer, screenshot and Stop & save. Floating ball: timer; double-click the ball to stop and save.", 64);
        Numeric("Opacity (%)", 40, 100, _draft.PanelOpacity, v => _draft.PanelOpacity = v);
        Check("Show the recording panel", _draft.ShowRecordingBar, v => _draft.ShowRecordingBar = v);
        Check("Hide floating panel while recording", _draft.HidePanelWhileRecording, v => _draft.HidePanelWhileRecording = v);
        Check("Allow floating panel to be captured", _draft.AllowPanelCapture, v => _draft.AllowPanelCapture = v);
        Note("Panel capture exclusion uses Windows capture protection where supported. The global toolbar shortcut can show or hide the panel.", 62);
    }
    private void MouseSettings()
    {
        Section("Mouse cursor"); Check("Show mouse cursor in the recording", _draft.ShowMouseCursor, v => _draft.ShowMouseCursor = v);
        Note("Cursor visibility is applied directly to the screen capture engine.", 48);
    }
    private void Hotkeys()
    {
        Section("Hotkeys"); Note("These shortcuts work while another application is active. Type a modifier and key, for example Ctrl+Alt+R. Leave blank to disable. Conflicts are reported when saving.", 76);
        void Shortcut(string label, string value, Action<string> changed) { var input = new TextBox { Text = value, BackColor = Color.FromArgb(17, 29, 43), ForeColor = MainForm.Ink, BorderStyle = BorderStyle.FixedSingle }; input.TextChanged += (_, _) => { changed(input.Text); Changed(); }; AddRow(label, input); }
        Shortcut("Start / stop recording", _draft.StartStopHotkey, v => _draft.StartStopHotkey = v);
        Shortcut("Screen capture", _draft.ScreenshotHotkey, v => _draft.ScreenshotHotkey = v);
        Shortcut("Toggle recording panel", _draft.ToolbarHotkey, v => _draft.ToolbarHotkey = v);
    }
    private void General()
    {
        Section("General"); Numeric("Countdown (seconds)", 0, 10, _draft.CountdownSeconds, v => _draft.CountdownSeconds = v);
        Check("Open recording folder after Stop and successful save", _draft.OpenFolderAfterSave, v => _draft.OpenFolderAfterSave = v);
        Check("Beep on start recording", _draft.BeepOnStart, v => _draft.BeepOnStart = v);
        Check("Show recording boundary", _draft.ShowRecordingBoundary, v => _draft.ShowRecordingBoundary = v);
        Check("Hide main interface when taking a screenshot", _draft.HideMainForScreenshot, v => _draft.HideMainForScreenshot = v);
        Check("Show the recording panel", _draft.ShowRecordingBar, v => _draft.ShowRecordingBar = v);
        Check("Keep the previous recording region", _draft.KeepRecordingMode, v => _draft.KeepRecordingMode = v);
        Choice("When closing the window", new[] { "Exit program", "Minimize to system tray" }, _draft.CloseToTray ? "Minimize to system tray" : "Exit program", v => _draft.CloseToTray = v.StartsWith("Minimize"));
    }
    private void More()
    {
        Section("Capture and encoding"); Note("Windows desktop capture (GDI) · H.264 software encoding. Camera uses Windows Media Foundation; sound uses Windows WASAPI.", 76);
        Note("The recording component is included and managed by the application. No component paths or downloads are required.", 64);
        Section("Troubleshooting");
        var logButton = new ActionButton("Open recording log");
        logButton.Click += (_, _) => { try { string path = Path.Combine(Path.GetDirectoryName(RecorderSettings.DefaultPath)!, "last-recording.log"); if (!File.Exists(path)) { MessageBox.Show(FindForm(), "No recording diagnostics have been saved yet.", "Recording log"); return; } System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Recording log"); } };
        AddRow("Recording log", logButton);
    }
    internal bool SaveDraft()
    {
        try { _draft.Validate(); string? error = _save(_draft.Clone()); if (error != null) throw new InvalidOperationException(error); _original = _draft.Clone(); _notice.Text = "Settings saved."; _notice.ForeColor = MainForm.Teal; return true; }
        catch (Exception ex) { _notice.Text = ex.Message; _notice.ForeColor = Color.FromArgb(255, 166, 135); return false; }
    }
}
