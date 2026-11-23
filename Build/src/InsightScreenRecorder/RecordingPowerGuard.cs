using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace InsightScreenRecorder;

// Request lifetime is scoped to active capture, not a persistent power-plan change.
internal sealed class RecordingPowerGuard : IDisposable
{
    private SafeFileHandle? _handle;
    internal bool Active => _handle is { IsClosed: false, IsInvalid: false };
    internal RecordingPowerGuard()
    {
        IntPtr reason = Marshal.StringToHGlobalUni("Insight Screen Recorder is recording the screen");
        try {
            var context = new ReasonContext { Flags = 1, Reason = reason };
            _handle = PowerCreateRequest(ref context);
            if (_handle.IsInvalid) { _handle.Dispose(); _handle = null; throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to create recording power request."); }
            if (!PowerSetRequest(_handle, 1) || !PowerSetRequest(_handle, 0)) { int error = Marshal.GetLastWin32Error(); Dispose(); throw new Win32Exception(error, "Unable to keep the display/system awake during recording."); }
        } finally { Marshal.FreeHGlobal(reason); }
    }
    public void Dispose() { var handle = Interlocked.Exchange(ref _handle, null); if (handle == null) return; if (!handle.IsInvalid) { PowerClearRequest(handle, 0); PowerClearRequest(handle, 1); } handle.Dispose(); }
    [StructLayout(LayoutKind.Sequential)] private struct ReasonContext { internal uint Version, Flags; internal IntPtr Reason, Reserved1, Reserved2; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle PowerCreateRequest(ref ReasonContext context);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PowerSetRequest(SafeFileHandle handle, int type);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PowerClearRequest(SafeFileHandle handle, int type);
}
