namespace InsightScreenRecorder;

internal sealed class MediaDeviceCatalog
{
    public IReadOnlyList<CameraDeviceInfo> Cameras { get; private set; } = Array.Empty<CameraDeviceInfo>();
    public IReadOnlyList<AudioDeviceInfo> Speakers { get; private set; } = Array.Empty<AudioDeviceInfo>();
    public IReadOnlyList<AudioDeviceInfo> Microphones { get; private set; } = Array.Empty<AudioDeviceInfo>();
    public string Error { get; private set; } = "";
    public void Refresh()
    {
        var errors = new List<string>();
        try { Cameras = MediaFoundationCamera.EnumerateDevices(); } catch (Exception ex) { Cameras = Array.Empty<CameraDeviceInfo>(); errors.Add("Camera: " + ex.Message); }
        try { Speakers = WindowsAudioCapture.EnumerateDevices(true); } catch (Exception ex) { Speakers = Array.Empty<AudioDeviceInfo>(); errors.Add("System sound: " + ex.Message); }
        try { Microphones = WindowsAudioCapture.EnumerateDevices(false); } catch (Exception ex) { Microphones = Array.Empty<AudioDeviceInfo>(); errors.Add("Microphone: " + ex.Message); }
        Error = string.Join("; ", errors);
    }
}
