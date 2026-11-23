using System.Text.Json;

namespace InsightScreenRecorder;

internal sealed class RecorderSettings
{
    public string OutputFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Insight Screen Recorder");
    [System.Text.Json.Serialization.JsonIgnore] public string FfmpegPath { get; set; } = ""; // Legacy field is no longer configured or persisted.
    public int Fps { get; set; } = 30;
    public int Crf { get; set; } = 23;
    public int OutputWidth { get; set; }
    public string ScreenshotFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Insight Screen Recorder");
    public string ScreenshotFormat { get; set; } = "PNG";
    public string VideoFormat { get; set; } = "MP4";
    public bool ConstantFrameRate { get; set; }
    public int KeyframeSeconds { get; set; } = 2;
    public bool ShowMouseCursor { get; set; } = true;
    public int CountdownSeconds { get; set; } = 3;
    public bool OpenFolderAfterSave { get; set; } = true;
    public bool BeepOnStart { get; set; }
    public bool ShowRecordingBoundary { get; set; }
    public bool HideMainForScreenshot { get; set; } = true;
    public bool ShowRecordingBar { get; set; } = true;
    public string PanelStyle { get; set; } = "Full panel";
    public int PanelOpacity { get; set; } = 100;
    public bool HidePanelWhileRecording { get; set; }
    public bool AllowPanelCapture { get; set; }
    public bool KeepRecordingMode { get; set; }
    public bool CloseToTray { get; set; }
    public string StartStopHotkey { get; set; } = "Ctrl+Alt+R";
    public string ScreenshotHotkey { get; set; } = "Ctrl+Alt+S";
    public string ToolbarHotkey { get; set; } = "Ctrl+Alt+H";
    public bool LastCustomMode { get; set; }
    public int LastX { get; set; }
    public int LastY { get; set; }
    public int LastWidth { get; set; } = 1280;
    public int LastHeight { get; set; } = 720;
    public string LastScreenDevice { get; set; } = "";
    public bool CameraEnabled { get; set; }
    public string CameraDevice { get; set; } = "";
    public string CameraPosition { get; set; } = "Bottom right";
    public int CameraWidthPercent { get; set; } = 25;
    public bool CameraMirror { get; set; }
    public bool SystemAudioEnabled { get; set; }
    public string SystemAudioDevice { get; set; } = "";
    public int SystemAudioGain { get; set; } = 100;
    public bool MicrophoneEnabled { get; set; }
    public string MicrophoneDevice { get; set; } = "";
    public int MicrophoneGain { get; set; } = 100;
    public int AudioSampleRate { get; set; } = 48000;
    public int AudioChannels { get; set; } = 2;
    public int AudioBitrate { get; set; } = 192;
    public RecorderSettings Clone() => JsonSerializer.Deserialize<RecorderSettings>(JsonSerializer.Serialize(this))!;
    public void Validate()
    {
        if (CameraWidthPercent < 10 || CameraWidthPercent > 50 || CameraPosition is not ("Top left" or "Top right" or "Bottom left" or "Bottom right")) throw new ArgumentException("Invalid camera overlay size or position.");
        if (SystemAudioGain < 0 || SystemAudioGain > 200 || MicrophoneGain < 0 || MicrophoneGain > 200 || AudioSampleRate is not (44100 or 48000) || AudioChannels is not (1 or 2) || AudioBitrate is not (96 or 128 or 192 or 256 or 320)) throw new ArgumentException("Invalid audio format or volume.");
        foreach (string folder in new[] { OutputFolder, ScreenshotFolder }) {
            if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)) throw new ArgumentException("Choose a full output and screenshot directory path.");
            if (File.Exists(Path.GetFullPath(folder))) throw new IOException("A selected directory is a file.");
        }
        if (Fps < 5 || Fps > 60 || Crf < 0 || Crf > 51 || KeyframeSeconds < 1 || KeyframeSeconds > 10 || CountdownSeconds < 0 || CountdownSeconds > 10 || PanelOpacity < 40 || PanelOpacity > 100) throw new ArgumentException("One or more recording settings are outside their supported range.");
        if (VideoFormat is not ("MP4" or "MKV") || ScreenshotFormat is not ("PNG" or "JPG" or "BMP")) throw new ArgumentException("Unsupported file format.");
        if (OutputWidth < 0 || OutputWidth > 7680 || OutputWidth % 2 != 0) throw new ArgumentException("Output width must be zero or an even value up to 7680.");
        if (PanelStyle is not ("Full panel" or "Floating ball")) throw new ArgumentException("Unsupported recording panel style.");
        if (LastCustomMode && (LastWidth < 2 || LastHeight < 2 || LastWidth > 20000 || LastHeight > 20000 || LastWidth % 2 != 0 || LastHeight % 2 != 0 || LastX < -20000 || LastX > 20000 || LastY < -20000 || LastY > 20000)) throw new ArgumentException("Custom capture width and height must be even numbers from 2 to 20000, with valid desktop coordinates.");
    }
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "INSIGHT Software", "Screen Recorder", "settings.json");
    public static RecorderSettings Load(string path)
    {
        try {
            var value = JsonSerializer.Deserialize<RecorderSettings>(File.ReadAllText(path)) ?? new();
            value.Fps = Math.Clamp(value.Fps, 5, 60); value.Crf = Math.Clamp(value.Crf, 0, 51);
            if (value.OutputWidth < 0 || value.OutputWidth > 7680 || value.OutputWidth % 2 != 0) value.OutputWidth = 0;
            if (string.IsNullOrWhiteSpace(value.OutputFolder)) value.OutputFolder = new RecorderSettings().OutputFolder;
            value.FfmpegPath ??= "";
            value.VideoFormat = value.VideoFormat == "MKV" ? "MKV" : "MP4";
            value.ScreenshotFormat = value.ScreenshotFormat is "JPG" or "BMP" ? value.ScreenshotFormat : "PNG";
            if (string.IsNullOrWhiteSpace(value.ScreenshotFolder)) value.ScreenshotFolder = new RecorderSettings().ScreenshotFolder;
            value.KeyframeSeconds = Math.Clamp(value.KeyframeSeconds, 1, 10); value.CountdownSeconds = Math.Clamp(value.CountdownSeconds, 0, 10); value.PanelOpacity = Math.Clamp(value.PanelOpacity, 40, 100);
            value.PanelStyle = value.PanelStyle == "Floating ball" ? "Floating ball" : "Full panel";
            value.StartStopHotkey ??= "Ctrl+Alt+R"; value.ScreenshotHotkey ??= "Ctrl+Alt+S"; value.ToolbarHotkey ??= "Ctrl+Alt+H";
            return value;
        } catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
    public static string NewRecordingPath(string directory, string format = "MP4") => Path.Combine(Path.GetFullPath(directory), $"Insight_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid().ToString("N")[..8]}.{(format == "MKV" ? "mkv" : "mp4")}");
}
