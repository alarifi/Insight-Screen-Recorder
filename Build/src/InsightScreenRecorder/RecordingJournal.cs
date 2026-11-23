namespace InsightScreenRecorder;

internal sealed class RecordingJournal(string path)
{
    private readonly object _gate = new();
    private long _lastProgress;
    private static readonly HashSet<string> ProgressKeys = new(StringComparer.Ordinal) { "frame", "fps", "bitrate", "total_size", "out_time_ms", "out_time", "dup_frames", "drop_frames", "speed", "progress" };
    internal string Path { get; } = path;
    internal void Write(string message)
    {
        lock (_gate) {
            // Persist one progress sample per two seconds; retain non-stat diagnostics.
            if (message.StartsWith("out_time_us=", StringComparison.Ordinal)) {
                long now = Environment.TickCount64; if (_lastProgress != 0 && now - _lastProgress < 2000) return; _lastProgress = now;
            } else {
                int separator = message.IndexOf('=');
                if (separator > 0) { string key = message[..separator]; if (ProgressKeys.Contains(key) || key.StartsWith("stream_", StringComparison.Ordinal)) return; }
            }
            try { File.AppendAllText(Path, DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Logging must not stop capture. */ }
        }
    }
}
