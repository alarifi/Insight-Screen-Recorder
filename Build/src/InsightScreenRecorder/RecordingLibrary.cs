using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace InsightScreenRecorder;

internal sealed record RecordingItem(string Path, DateTime Created, long Bytes)
{
    internal string FileName => System.IO.Path.GetFileName(Path);
    internal string DisplayName => RecordingLibrary.DisplayName(FileName);
    internal double? Seconds { get; set; }
    internal string DurationError { get; set; } = "Reading duration…";
    internal string Duration => Seconds is double seconds ? (seconds >= 3600 ? TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss") : TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss")) : "—";
}

internal static class RecordingLibrary
{
    internal static string DisplayName(string name)
    {
        var match = Regex.Match(name, @"^Insight_(\d{8})_(\d{6})_\d{3}_[a-fA-F0-9]{8}\.(mp4|mkv)$", RegexOptions.IgnoreCase);
        return match.Success && DateTime.TryParseExact(match.Groups[1].Value + match.Groups[2].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? "Recording — " + date.ToString("yyyy-MM-dd HH:mm:ss") : name;
    }
    internal static List<RecordingItem> Scan(string directory)
    {
        if (!Directory.Exists(directory)) return new();
        var result = new List<RecordingItem>();
        foreach (string path in Directory.EnumerateFiles(directory)) {
            if (!new[] { ".mp4", ".mkv" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) continue;
            try { var file = new FileInfo(path); result.Add(new(file.FullName, file.CreationTime, file.Length)); }
            catch (FileNotFoundException) { } // A file may be removed during enumeration.
        }
        return result.OrderByDescending(item => item.Created).ToList();
    }
    internal static bool Matches(RecordingItem item, string query) => item.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) || item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase);
    internal static string Rename(string source, string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrEmpty(newName) || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(newName) != newName || newName.EndsWith('.') || newName.EndsWith(' ')) throw new ArgumentException("Enter a valid filename, without folders.");
        string extension = Path.GetExtension(source);
        if (Path.GetExtension(newName).Length == 0) newName += extension;
        if (!Path.GetExtension(newName).Equals(extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Keep the existing " + extension + " extension.");
        string stem = Path.GetFileNameWithoutExtension(newName).Split('.')[0];
        if (Regex.IsMatch(stem, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase)) throw new ArgumentException("That name is reserved by Windows.");
        string destination = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!, newName);
        if (string.Equals(source, destination, StringComparison.Ordinal)) return source;
        File.Move(source, destination, false); return destination;
    }
    internal static async Task<double> ReadDurationAsync(string probe, string path, CancellationToken token)
    {
        using var process = new Process { StartInfo = new(probe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (string argument in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", path }) process.StartInfo.ArgumentList.Add(argument);
        process.Start(); var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
        catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync().ConfigureAwait(false); throw; }
        string text = await output; string failure = await error;
        if (process.ExitCode != 0 || !double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) || !double.IsFinite(seconds) || seconds < 0 || seconds > TimeSpan.MaxValue.TotalSeconds) throw new IOException("Duration unavailable. The file may be incomplete or unreadable. " + failure.Trim());
        return seconds;
    }
}
