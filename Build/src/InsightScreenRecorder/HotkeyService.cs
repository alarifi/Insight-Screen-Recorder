using System.Runtime.InteropServices;

namespace InsightScreenRecorder;

internal sealed class HotkeyService(IntPtr window) : IDisposable
{
    private List<(int Id, uint Modifiers, uint Key)> _bindings = new();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    internal static (uint Modifiers, uint Key)? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new ArgumentException("Enter a shortcut key or leave the field blank.");
        uint modifiers = 0;
        foreach (string part in parts[..^1]) modifiers |= part.ToLowerInvariant() switch { "ctrl" or "control" => 2u, "alt" => 1u, "shift" => 4u, _ => throw new ArgumentException("Use Ctrl, Alt or Shift with a key.") };
        string last = parts[^1];
        if (last.Length == 1 && char.IsAsciiDigit(last[0])) last = "D" + last;
        if (!Enum.TryParse<Keys>(last, true, out var key)) throw new ArgumentException("Unknown shortcut key: " + last);
        key &= Keys.KeyCode;
        bool function = key >= Keys.F1 && key <= Keys.F24;
        bool valid = function || key >= Keys.A && key <= Keys.Z || key >= Keys.D0 && key <= Keys.D9 || key is Keys.Space or Keys.Insert or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown;
        if (!valid || modifiers == 0 && !function) throw new ArgumentException("A shortcut needs Ctrl, Alt or Shift (or a function key).");
        return (modifiers, (uint)key);
    }
    internal static List<(int Id, uint Modifiers, uint Key)> Bindings(RecorderSettings settings)
    {
        var result = new List<(int, uint, uint)>();
        var texts = new[] { settings.StartStopHotkey, settings.ScreenshotHotkey, settings.ToolbarHotkey };
        for (int i = 0; i < texts.Length; i++) if (Parse(texts[i]) is var binding && binding.HasValue) result.Add((i + 1, binding.Value.Modifiers, binding.Value.Key));
        if (result.Select(b => (b.Item2, b.Item3)).Distinct().Count() != result.Count) throw new ArgumentException("Each enabled shortcut must be different.");
        return result;
    }
    internal void Apply(RecorderSettings settings)
    {
        var requested = Bindings(settings); var previous = _bindings.ToList(); Clear();
        try {
            foreach (var binding in requested) {
                if (!RegisterHotKey(window, binding.Id, binding.Modifiers | 0x4000, binding.Key)) {
                    int error = Marshal.GetLastWin32Error();
                    string[] shortcuts = { settings.StartStopHotkey, settings.ScreenshotHotkey, settings.ToolbarHotkey };
                    string[] actions = { "Start / stop recording", "Screenshot", "Show / hide recording panel" };
                    string reason = error == 1409 ? "is already used by another application" : $"could not be registered (Windows error {error})";
                    throw new InvalidOperationException($"Shortcut {shortcuts[binding.Id - 1]} ({actions[binding.Id - 1]}) {reason}. Recording buttons remain usable. Change or disable this shortcut in Settings > Hotkeys.");
                }
                _bindings.Add(binding);
            }
        } catch {
            Clear(); foreach (var binding in previous) if (RegisterHotKey(window, binding.Id, binding.Modifiers | 0x4000, binding.Key)) _bindings.Add(binding);
            throw;
        }
    }
    private void Clear() { foreach (var binding in _bindings) UnregisterHotKey(window, binding.Id); _bindings.Clear(); }
    public void Dispose() => Clear();
}
