using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace InsightScreenRecorder;

/// <summary>A single user recording, composed of finalized capture segments. Pausing
/// closes all capture inputs; it never suspends FFmpeg or queues paused audio/video.</summary>
internal sealed class RecordingSession : IDisposable
{
    private enum SessionState { Idle, Recording, Paused, Finalizing, Faulted }
    private volatile SessionState _state;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly StringBuilder _diagnostics = new();
    private readonly List<string> _parts = new();
    private readonly object _timingGate = new();
    private long _completedTicks;
    private RecordingEngine? _part;
    private volatile bool _expectedStop;
    private int _partExitCode, _sequence;
    private bool _finished, _disposed;
    private string _ffmpeg = "", _output = "", _directory = "", _currentPart = "";
    private string? _signature;
    private CaptureOptions? _options;
    private RecordingPowerGuard? _power;
    private RecordingJournal? _journal;
    private sealed record CaptureOptions(int Fps, int X, int Y, int Width, int Height, int Crf, int OutputWidth, bool Cursor, int Keyframes, bool Cfr, RecorderSettings Settings);
    public bool IsRecording => _state is SessionState.Recording or SessionState.Paused or SessionState.Finalizing;
    public bool IsPaused => _state == SessionState.Paused;
    internal (float System, float Microphone) AudioLevels { get { lock (_timingGate) return _state == SessionState.Recording ? _part?.AudioLevels ?? (0f, 0f) : (0f, 0f); } }
    public TimeSpan Elapsed { get { lock (_timingGate) return TimeSpan.FromTicks(_completedTicks + (_part?.Elapsed.Ticks ?? 0)); } }
    public string Diagnostics { get { lock (_diagnostics) return _diagnostics.ToString(); } }
    public event Action<string>? Message;
    public event Action<string, int>? Finished;
    private void Log(string text) { lock (_diagnostics) { if (_diagnostics.Length > 200000) _diagnostics.Remove(0, 100000); _diagnostics.AppendLine(text); } _journal?.Write(text); Message?.Invoke(text); }

    public async Task StartAsync(string ffmpeg, string output, int fps, int x, int y, int width, int height, int crf = 23, int outputWidth = 0, bool showMouseCursor = true, int keyframeSeconds = 2, bool constantFrameRate = false, RecorderSettings? mediaSettings = null)
    {
        await _operations.WaitAsync().ConfigureAwait(false);
        try {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsRecording) throw new InvalidOperationException("Recording already running or finalizing.");
            output = Path.GetFullPath(output);
            if (File.Exists(output)) throw new IOException("The output file already exists. Choose a new filename.");
            if (!File.Exists(ffmpeg)) throw new FileNotFoundException("Recording component is missing.", ffmpeg);
            if (!File.Exists(Path.Combine(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe"))) throw new FileNotFoundException("Recording validation component is missing. Restore the complete tools folder.");
            var settings = mediaSettings?.Clone() ?? new RecorderSettings(); settings.Validate();
            // Resolve defaults once. Resume must not silently switch microphone, speaker or camera.
            if (settings.CameraEnabled && settings.CameraDevice.Length == 0) settings.CameraDevice = MediaFoundationCamera.EnumerateDevices().FirstOrDefault()?.Id ?? throw new IOException("No camera is available.");
            if (settings.SystemAudioEnabled && settings.SystemAudioDevice.Length == 0) settings.SystemAudioDevice = WindowsAudioCapture.EnumerateDevices(true).OrderByDescending(d => d.IsDefault).FirstOrDefault()?.Id ?? throw new IOException("No speaker endpoint is available.");
            if (settings.MicrophoneEnabled && settings.MicrophoneDevice.Length == 0) settings.MicrophoneDevice = WindowsAudioCapture.EnumerateDevices(false).OrderByDescending(d => d.IsDefault).FirstOrDefault()?.Id ?? throw new IOException("No microphone endpoint is available.");
            _ffmpeg = ffmpeg; _output = output; _parts.Clear(); _signature = null; _sequence = 0; _finished = false;
            lock (_timingGate) _completedTicks = 0;
            lock (_diagnostics) _diagnostics.Clear();
            _options = new(fps, x, y, width, height, crf, outputWidth, showMouseCursor, keyframeSeconds, constantFrameRate, settings);
            _directory = Path.Combine(Path.GetDirectoryName(output)!, ".insight-recording-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _journal = new RecordingJournal(Path.Combine(_directory, "recording-health.log"));
            Log($"START pid={Environment.ProcessId} {width}x{height} fps={fps} audio={settings.SystemAudioEnabled} mic={settings.MicrophoneEnabled} camera={settings.CameraEnabled}; encoder threads=2; video queues=8/4; below-normal priority");
            try { await StartPartAsync().ConfigureAwait(false); _state = SessionState.Recording; }
            catch (Exception ex) { Fail(ex); throw; }
        } finally { _operations.Release(); }
    }
    private async Task StartPartAsync()
    {
        var options = _options!;
        _currentPart = Path.Combine(_directory, $"part{++_sequence:D6}" + Path.GetExtension(_output));
        _partExitCode = int.MinValue;
        var capture = new RecordingEngine { ReportProgress = true }; lock (_timingGate) _part = capture;
        capture.Message += Log;
        capture.Finished += (path, code) => {
            _partExitCode = code;
            // Finished is raised on the native completion task. Dispose must not
            // synchronously wait for that same task inside its own callback.
            if (!_expectedStop) _ = Task.Run(() => HandleUnexpectedExitAsync(capture, code));
        };
        try {
            _power = new RecordingPowerGuard();
            await capture.StartAsync(_ffmpeg, _currentPart, options.Fps, options.X, options.Y, options.Width, options.Height, options.Crf, options.OutputWidth, options.Cursor, options.Keyframes, options.Cfr, options.Settings).ConfigureAwait(false);
            await capture.WaitForFirstFrameAsync().WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
        } catch { _power?.Dispose(); _power = null; capture.Dispose(); lock (_timingGate) _part = null; throw; }
    }
    private async Task HandleUnexpectedExitAsync(RecordingEngine capture, int code)
    {
        await _operations.WaitAsync().ConfigureAwait(false);
        try {
            if (!ReferenceEquals(_part, capture) || !IsRecording) return;
            _power?.Dispose(); _power = null; Log(capture.Diagnostics); capture.Dispose(); lock (_timingGate) { _completedTicks += capture.Elapsed.Ticks; _part = null; }
            Fail(new IOException($"Capture stopped unexpectedly (exit {code})."));
        } finally { _operations.Release(); }
    }
    public async Task PauseAsync()
    {
        await _operations.WaitAsync().ConfigureAwait(false);
        try {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_state != SessionState.Recording) return;
            try { await FinishPartAsync().ConfigureAwait(false); _state = SessionState.Paused; Log("Paused. All capture inputs released."); }
            catch (Exception ex) { Fail(ex); throw; }
        } finally { _operations.Release(); }
    }
    public async Task ResumeAsync()
    {
        await _operations.WaitAsync().ConfigureAwait(false);
        try {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_state != SessionState.Paused) return;
            // A failed device restart leaves prior segments available for Stop & Save.
            try { await StartPartAsync().ConfigureAwait(false); _state = SessionState.Recording; Log("Resumed."); }
            catch (Exception ex) { Log("Unable to resume: " + ex.Message); throw; }
        } finally { _operations.Release(); }
    }
    private async Task FinishPartAsync()
    {
        var capture = _part ?? throw new IOException("The capture segment is unavailable.");
        _expectedStop = true;
        try {
            await capture.StopAsync().ConfigureAwait(false); Log(capture.Diagnostics);
            if (_partExitCode != 0) throw new IOException($"Capture segment failed (exit {_partExitCode}).");
        } finally {
            try { capture.Dispose(); }
            finally { _power?.Dispose(); _power = null; lock (_timingGate) { _completedTicks += capture.Elapsed.Ticks; _part = null; } _expectedStop = false; }
        }
        string signature = await InspectPartAsync(_currentPart).ConfigureAwait(false);
        if (_signature != null && _signature != signature) throw new IOException("A capture format changed after resuming. Segments were preserved rather than combining incompatible tracks.");
        _signature = signature; _parts.Add(_currentPart);
    }
    private async Task<string> InspectPartAsync(string path)
    {
        string json = await RunAsync(Path.Combine(Path.GetDirectoryName(_ffmpeg)!, "ffprobe.exe"), new[] { "-v", "error", "-show_streams", "-show_format", "-of", "json", path }, TimeSpan.FromSeconds(20)).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var streams = doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        if (!streams.Any(s => s.GetProperty("codec_type").GetString() == "video")) throw new IOException("The segment contains no video frames. Recording parts were retained.");
        if ((_options!.Settings.SystemAudioEnabled || _options.Settings.MicrophoneEnabled) && !streams.Any(s => s.GetProperty("codec_type").GetString() == "audio")) throw new IOException("The selected audio track is missing from the segment.");
        string Field(JsonElement s, string name) => s.TryGetProperty(name, out var value) ? value.ToString() : "";
        return string.Join("|", streams.Select(s => string.Join("/", new[] { "codec_type", "codec_name", "width", "height", "pix_fmt", "sample_rate", "channels", "time_base" }.Select(n => Field(s, n)))));
    }
    public async Task StopAsync()
    {
        await _operations.WaitAsync().ConfigureAwait(false);
        try {
            if (_state is SessionState.Idle or SessionState.Faulted) return;
            bool paused = IsPaused; _state = SessionState.Finalizing;
            try {
                if (!paused) await FinishPartAsync().ConfigureAwait(false);
                if (_parts.Count == 0) throw new IOException("No complete recording segments are available.");
                if (_parts.Count == 1) File.Move(_parts[0], _output, false);
                else {
                    string list = Path.Combine(_directory, "parts.ffconcat");
                    await File.WriteAllLinesAsync(list, new[] { "ffconcat version 1.0" }.Concat(_parts.Select(p => "file '" + Path.GetFileName(p) + "'")), new UTF8Encoding(false)).ConfigureAwait(false);
                    string merged = Path.Combine(_directory, "joined" + Path.GetExtension(_output));
                    var args = new List<string> { "-hide_banner", "-loglevel", "warning", "-n", "-f", "concat", "-safe", "1", "-i", list, "-map", "0", "-c", "copy" };
                    if (Path.GetExtension(_output).Equals(".mp4", StringComparison.OrdinalIgnoreCase)) args.AddRange(new[] { "-movflags", "+faststart" });
                    args.Add(merged);
                    long bytes = _parts.Sum(p => new FileInfo(p).Length);
                    await RunAsync(_ffmpeg, args, TimeSpan.FromSeconds(Math.Clamp(120 + bytes / 20000000d, 120, 1800))).ConfigureAwait(false);
                    await InspectPartAsync(merged).ConfigureAwait(false);
                    File.Move(merged, _output, false);
                }
                _state = SessionState.Idle; Log("Saved: " + _output); CleanupPrivateParts(); NotifyFinished(0);
            } catch (Exception ex) { Fail(ex); throw; }
        } finally { _operations.Release(); }
    }
    private async Task<string> RunAsync(string exe, IEnumerable<string> args, TimeSpan timeout)
    {
        using var process = new Process { StartInfo = new(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.Start(); var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(timeout).ConfigureAwait(false); }
        catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync().ConfigureAwait(false); throw; }
        string error = await stderr; if (error.Length > 0) Log(error);
        string text = await stdout;
        if (process.ExitCode != 0) throw new IOException("Recording finalization failed: " + error);
        return text;
    }
    private void Fail(Exception error)
    {
        _state = SessionState.Faulted;
        string text = error.Message + Environment.NewLine + "Recoverable parts: " + _directory;
        Log(text);
        try { File.WriteAllText(Path.Combine(_directory, "recovery.txt"), "Intended output: " + _output + Environment.NewLine + Diagnostics); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        NotifyFinished(-1);
    }
    private void NotifyFinished(int code) { if (_finished) return; _finished = true; Finished?.Invoke(_output, code); }
    private void CleanupPrivateParts()
    {
        // Delete only this session's known transient files, never recursively delete.
        try {
            if (_journal != null && File.Exists(_journal.Path)) {
                string target = Path.Combine(Path.GetDirectoryName(RecorderSettings.DefaultPath)!, "last-recording-health.log");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(_journal.Path, target, true); File.Delete(_journal.Path); _journal = null;
            }
            foreach (string path in _parts.Append(Path.Combine(_directory, "parts.ffconcat"))) if (File.Exists(path)) File.Delete(path);
            Directory.Delete(_directory, false);
        } catch (IOException ex) { Log("Saved successfully; temporary files retained: " + ex.Message); }
        catch (UnauthorizedAccessException ex) { Log("Saved successfully; temporary files retained: " + ex.Message); }
    }
    public void Dispose()
    {
        _operations.Wait();
        try { if (_disposed) return; _disposed = true; _expectedStop = true; _power?.Dispose(); _power = null; _part?.Dispose(); lock (_timingGate) _part = null; }
        finally { _operations.Release(); }
    }
}
