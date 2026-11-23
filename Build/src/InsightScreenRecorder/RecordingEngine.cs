using System.Diagnostics;
using System.Globalization;

namespace InsightScreenRecorder;

internal sealed class RecordingEngine : IDisposable
{
    private Process? _process;
    private Task? _completion;
    private MediaFoundationCamera? _camera;
    private readonly List<WindowsAudioCapture> _audio = new();
    private WindowsAudioCapture? _speakerLevelSource, _microphoneLevelSource;
    internal (float System, float Microphone) AudioLevels => (Volatile.Read(ref _speakerLevelSource)?.PeakLevel ?? 0, Volatile.Read(ref _microphoneLevelSource)?.PeakLevel ?? 0);
    private CancellationTokenSource? _monitorStop;
    private bool _starting;
    private readonly Stopwatch _captureClock = new();
    private long _writtenMicroseconds;
    internal TimeSpan Elapsed => ReportProgress ? TimeSpan.FromTicks(Math.Max(0, Interlocked.Read(ref _writtenMicroseconds)) * 10) : _captureClock.Elapsed;
    private TaskCompletionSource? _firstFrame;
    internal bool ReportProgress { get; init; }
    internal Task WaitForFirstFrameAsync() => _firstFrame?.Task ?? throw new InvalidOperationException("Capture progress was not enabled.");
    private string? ReleaseMedia()
    {
        Volatile.Write(ref _speakerLevelSource, null); Volatile.Write(ref _microphoneLevelSource, null);
        var errors = new List<string>();
        void Release(IDisposable? source) { try { source?.Dispose(); } catch(Exception ex) { errors.Add(ex.Message); } }
        try { _monitorStop?.Cancel(); } catch(ObjectDisposedException) { }
        Release(_camera); _camera = null;
        foreach(var source in _audio) Release(source);
        _audio.Clear();
        if(errors.Count == 0) return null;
        string error = "Media cleanup: " + string.Join("; ", errors);
        lock(_gate) _diagnostics.AppendLine(error);
        return error;
    }
    public async Task StartAsync(string ffmpeg, string output, int fps, int x, int y, int width, int height, int crf = 23, int outputWidth = 0, bool showMouseCursor = true, int keyframeSeconds = 2, bool constantFrameRate = false, RecorderSettings? mediaSettings = null)
    {
        lock (_gate) { if (_starting || _process is { HasExited: false } || _completion is { IsCompleted:false }) throw new InvalidOperationException("Recording already running or finalizing."); _starting = true; }
        try {
            var settings = mediaSettings?.Clone() ?? new RecorderSettings();
            settings.Validate();
            if(settings.CameraEnabled) {
                string id = settings.CameraDevice;
                if(string.IsNullOrEmpty(id)) id = MediaFoundationCamera.EnumerateDevices().FirstOrDefault()?.Id ?? throw new InvalidOperationException("No camera is available.");
                _camera = new MediaFoundationCamera(id, fps); await _camera.StartAsync().ConfigureAwait(false);
            }
            foreach(var request in new[]{(settings.SystemAudioEnabled, true, settings.SystemAudioDevice, settings.SystemAudioGain), (settings.MicrophoneEnabled, false, settings.MicrophoneDevice, settings.MicrophoneGain)}) {
                if(!request.Item1) continue;
                var devices = WindowsAudioCapture.EnumerateDevices(request.Item2);
                string id = (!string.IsNullOrEmpty(request.Item3) ? devices.FirstOrDefault(d => string.Equals(d.Id, request.Item3, StringComparison.OrdinalIgnoreCase))?.Id : null)
                    ?? devices.OrderByDescending(d => d.IsDefault).FirstOrDefault()?.Id
                    ?? throw new InvalidOperationException(request.Item2 ? "No speaker endpoint is available." : "No microphone endpoint is available.");
                var source = new WindowsAudioCapture(id, request.Item2, request.Item4 / 100f); _audio.Add(source);
                if (request.Item2) Volatile.Write(ref _speakerLevelSource, source); else Volatile.Write(ref _microphoneLevelSource, source);
                await source.StartAsync().ConfigureAwait(false);
            }
            StartCore(ffmpeg, output, fps, x, y, width, height, crf, outputWidth, showMouseCursor, keyframeSeconds, constantFrameRate, settings);
        }
        catch { ReleaseMedia(); throw; }
        finally { lock(_gate) _starting = false; }
    }
    private readonly object _gate = new();
    private readonly System.Text.StringBuilder _diagnostics = new();
    public string Diagnostics { get { lock (_gate) return _diagnostics.ToString(); } }
    public bool IsRecording { get { lock (_gate) return _process is { HasExited: false }; } }
    public event Action<string>? Message;
    public event Action<string, int>? Finished;

    public static string? FindFfmpeg(string appDir)
    {
        string[] local = { Path.Combine(appDir, "tools", "ffmpeg.exe"), Path.Combine(appDir, "ffmpeg.exe") };
        foreach (string p in local) if (File.Exists(p)) return p;
        return null;
    }

    public static async Task<string> InspectAsync(string executable)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("The recording component is missing. Restore the complete application package.", executable);
        async Task<string> Run(string argument)
        {
            using var p = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            p.StartInfo.ArgumentList.Add("-hide_banner"); p.StartInfo.ArgumentList.Add(argument);
            p.Start();
            var stdout = p.StandardOutput.ReadToEndAsync(); var stderr = p.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch { if (!p.HasExited) p.Kill(true); await p.WaitForExitAsync().ConfigureAwait(false); throw; }
            string text = await stdout + await stderr;
            if (p.ExitCode != 0) throw new InvalidOperationException("FFmpeg validation failed: " + text);
            return text;
        }
        string version = await Run("-version");
        if (!version.Contains("ffmpeg version", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The selected executable is not FFmpeg.");
        if (!(await Run("-encoders")).Contains("libx264")) throw new InvalidOperationException("This FFmpeg build does not provide libx264.");
        if (!(await Run("-devices")).Contains("gdigrab")) throw new InvalidOperationException("This FFmpeg build does not provide Windows gdigrab capture.");
        return version.Split('\n')[0].Trim();
    }

    public void Start(string ffmpeg, string output, int fps, int x, int y, int width, int height, int crf = 23, int outputWidth = 0, bool showMouseCursor = true, int keyframeSeconds = 2, bool constantFrameRate = false)
        => StartAsync(ffmpeg,output,fps,x,y,width,height,crf,outputWidth,showMouseCursor,keyframeSeconds,constantFrameRate).GetAwaiter().GetResult();
    private void StartCore(string ffmpeg, string output, int fps, int x, int y, int width, int height, int crf, int outputWidth, bool showMouseCursor, int keyframeSeconds, bool constantFrameRate, RecorderSettings settings)
    {
        lock (_gate)
        {
            if (_process is { HasExited: false }) throw new InvalidOperationException("Recording already running.");
            if (_completion is { IsCompleted: false }) throw new InvalidOperationException("The previous recording is still finalizing.");
            if (width < 2 || height < 2 || width % 2 != 0 || height % 2 != 0 || fps < 5 || fps > 60 || crf < 0 || crf > 51 || outputWidth < 0 || outputWidth % 2 != 0 || keyframeSeconds < 1 || keyframeSeconds > 10)
                throw new ArgumentException("Capture dimensions and output width must be even; FPS must be 5–60 and CRF 0–51.");
            if (!File.Exists(ffmpeg)) throw new FileNotFoundException("FFmpeg executable not found.", ffmpeg);
            output = Path.GetFullPath(output);
            if (File.Exists(output)) throw new IOException("The output file already exists. Choose a new filename.");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using (var check = new FileStream(output, FileMode.CreateNew, FileAccess.Write)) { }
            File.Delete(output);
            var psi = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = false
            };
            RecordingCommand.Populate(psi, output, fps, x, y, width, height, crf, outputWidth, showMouseCursor, keyframeSeconds, constantFrameRate, settings, _camera, _audio);
            if (ReportProgress) {
                psi.ArgumentList.Insert(0, "-progress"); psi.ArgumentList.Insert(1, "pipe:2");
                psi.ArgumentList.Insert(2, "-stats_period"); psi.ArgumentList.Insert(3, "0.1");
            }
            _process?.Dispose(); _diagnostics.Clear();
            var p = new Process { StartInfo = psi };
            var firstFrame = _firstFrame = ReportProgress ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : null;
            long encodedFrames = 0, writtenMicroseconds = 0;
            p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) {
                if (firstFrame != null) {
                    if (e.Data.StartsWith("frame=", StringComparison.Ordinal)) long.TryParse(e.Data.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out encodedFrames);
                    if (e.Data.StartsWith("out_time_us=", StringComparison.Ordinal)) { long.TryParse(e.Data.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out writtenMicroseconds); Interlocked.Exchange(ref _writtenMicroseconds, Math.Max(0, writtenMicroseconds)); }
                    if (encodedFrames > 0 && writtenMicroseconds >= 100000) firstFrame.TrySetResult();
                }
                lock (_gate) { if (_diagnostics.Length > 100000) _diagnostics.Remove(0, 50000); _diagnostics.AppendLine(e.Data); } Message?.Invoke(e.Data);
            } };
            try
            {
                if (!p.Start()) throw new InvalidOperationException("FFmpeg did not start.");
                try { p.PriorityClass = ProcessPriorityClass.BelowNormal; }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { _diagnostics.AppendLine("Encoder priority could not be lowered: " + ex.Message); }
                _captureClock.Restart();
                Interlocked.Exchange(ref _writtenMicroseconds, 0);
                _process = p;
                p.BeginErrorReadLine();
                _completion = Task.Run(async () => {
                    string? failure = null; int code = -1;
                    using var monitor = new CancellationTokenSource(); _monitorStop = monitor;
                    Task? watch = null;
                    try {
                        watch = Task.Run(async () => {
                            while (!monitor.IsCancellationRequested && !p.HasExited) {
                                failure = _camera?.Error ?? _audio.Select(a=>a.Error).FirstOrDefault(e=>e!=null);
                                if (failure != null) {
                                    lock(_gate) _diagnostics.AppendLine(failure);
                                    Message?.Invoke(failure);
                                    try { await p.StandardInput.WriteLineAsync("q").ConfigureAwait(false); await p.StandardInput.FlushAsync().ConfigureAwait(false); } catch(IOException) {} catch(InvalidOperationException) {}
                                    try { await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
                                    catch(TimeoutException) { if(!p.HasExited) p.Kill(true); }
                                    break;
                                }
                                await Task.Delay(100, monitor.Token).ConfigureAwait(false);
                            }
                        });
                        await p.WaitForExitAsync().ConfigureAwait(false); p.WaitForExit(); code = p.ExitCode;
                    }
                    catch(Exception ex) { failure ??= ex.Message; lock(_gate) _diagnostics.AppendLine(ex.Message); }
                    finally {
                        _captureClock.Stop();
                        monitor.Cancel();
                        if(watch != null) { try { await watch.ConfigureAwait(false); } catch(OperationCanceledException) {} catch(Exception ex) { failure ??= ex.Message; } }
                        var cleanupFailure = ReleaseMedia(); failure ??= cleanupFailure; _monitorStop = null;
                        firstFrame?.TrySetException(new IOException("The encoder stopped before producing a usable video frame. " + Diagnostics));
                        Finished?.Invoke(output, failure == null ? code : -1);
                    }
                });
            }
            catch { p.Dispose(); _process = null; throw; }
        }
    }

    public async Task StopAsync()
    {
        Process? p;
        lock (_gate) p = _process;
        if (p is null) return;
        if (p.HasExited) { if (_completion is not null) await _completion.ConfigureAwait(false); return; }
        try
        {
            try { await p.StandardInput.WriteLineAsync("q").ConfigureAwait(false); await p.StandardInput.FlushAsync().ConfigureAwait(false); }
            catch (IOException) { }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            try { await p.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { if (!p.HasExited) p.Kill(entireProcessTree: true); await p.WaitForExitAsync().ConfigureAwait(false); throw new IOException("FFmpeg did not finalize in time; the recording may be incomplete."); }
            p.WaitForExit();
            if (_completion is not null) await _completion.ConfigureAwait(false);
        }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        Process? p;
        lock (_gate) { p = _process; _process = null; }
        if (p is null) { ReleaseMedia(); return; }
        try { if (!p.HasExited) { p.Kill(entireProcessTree: true); p.WaitForExit(5000); } } catch (InvalidOperationException) { }
        if (_completion != null) { try { _completion.GetAwaiter().GetResult(); } catch { } }
        ReleaseMedia(); p.Dispose();
    }
}

