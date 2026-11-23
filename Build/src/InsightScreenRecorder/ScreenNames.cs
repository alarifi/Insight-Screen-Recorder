namespace InsightScreenRecorder;

internal static class ScreenNames
{
    internal static string SelectionLabel(int number, bool primary) => $"Screen {number}{(primary ? " — Main" : "")}";
    internal static string Label(int number, bool primary, int width, int height)
        => $"Screen {number}{(primary ? " — Main screen" : "")} · {width} × {height}";
}
