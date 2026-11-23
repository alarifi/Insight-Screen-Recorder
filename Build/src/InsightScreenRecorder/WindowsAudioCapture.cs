using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace InsightScreenRecorder;
internal sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);
internal sealed record AudioEndpointState(bool Muted, float Volume);

/// <summary>Shared-mode WASAPI capture. Pipe is headerless interleaved IEEE float32 little endian
/// at the endpoint mix sample rate/channel count. Gain is local to this source. A bounded queue
/// reports failure on sustained backpressure; idle render endpoints receive clock-paced silence.</summary>
internal sealed class WindowsAudioCapture : IDisposable
{
    private readonly string id;
    private readonly bool loopback;
    private readonly float gain;
    private readonly CancellationTokenSource stop = new();
    private readonly NamedPipeServerStream pipe;
    private readonly Channel<byte[]> queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(25) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = true });
    private Task? captureTask, pipeTask;
    private readonly ManualResetEventSlim connected = new();
    private long capturedFrames, capturedSamples, capturedPackets, lastPacketTimestamp;
    private float peakLevel, maxPeakLevel;
    private int disposed;
    /// <summary>Post-gain actual packet peak (0..1); zero after 200 ms without a packet. Synthetic idle silence never raises it.</summary>
    public float PeakLevel => Stopwatch.GetElapsedTime(Interlocked.Read(ref lastPacketTimestamp)).TotalMilliseconds > 200 ? 0 : Volatile.Read(ref peakLevel);
    public float MaxPeakLevel => Volatile.Read(ref maxPeakLevel);
    public long CapturedSamples => Interlocked.Read(ref capturedSamples);
    public long CapturedPackets => Interlocked.Read(ref capturedPackets);
    public string PipePath { get; }
    public int SampleRate { get; private set; }
    public int Channels { get; private set; }
    public string SampleFormat => "f32le";
    public long CapturedFrames => Interlocked.Read(ref capturedFrames);
    public string? Error { get; private set; }
    public WindowsAudioCapture(string deviceId, bool loopback, float gain = 1f)
    {
        if (!float.IsFinite(gain) || gain < 0) throw new ArgumentOutOfRangeException(nameof(gain));
        id = deviceId; this.loopback = loopback; this.gain = gain;
        var name = "InsightAudio-" + Guid.NewGuid().ToString("N");
        PipePath = @"\\.\pipe\" + name;
        pipe = new(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536);
    }
    public static IReadOnlyList<AudioDeviceInfo> EnumerateDevices(bool loopback)
    {
        var result = new List<AudioDeviceInfo>();
        IMMDeviceEnumerator? e = null; IMMDeviceCollection? collection = null; IMMDevice? def = null;
        try
        {
            e = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            string defaultId = "";
            if (e.GetDefaultAudioEndpoint(loopback ? 0 : 1, 1, out def) >= 0) Check(def.GetId(out defaultId));
            Check(e.EnumAudioEndpoints(loopback ? 0 : 1, 1, out collection)); Check(collection.GetCount(out var count));
            for (uint i = 0; i < count; i++)
            {
                IMMDevice? d = null; IPropertyStore? props = null;
                try
                {
                    Check(collection.Item(i, out d)); Check(d.GetId(out var deviceId));
                    Check(d.OpenPropertyStore(0, out props));
                    var key = new PropertyKey { fmtid = new("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
                    Check(props.GetValue(ref key, out var value));
                    try { result.Add(new(deviceId, Marshal.PtrToStringUni(value.pointer) ?? deviceId, deviceId == defaultId)); }
                    finally { PropVariantClear(ref value); }
                }
                finally { Release(props); Release(d); }
            }
        }
        finally { Release(def); Release(collection); Release(e); }
        return result.OrderByDescending(x => x.IsDefault).ToArray();
    }
    // Read endpoint state separately from PCM: a valid recording can be inaudible
    // during playback simply because Windows has muted the output endpoint.
    public static AudioEndpointState ReadEndpointState(bool loopback, string deviceId = "")
    {
        IMMDeviceEnumerator? enumerator = null; IMMDevice? device = null; IAudioEndpointVolume? volume = null;
        try {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (string.IsNullOrEmpty(deviceId)) Check(enumerator.GetDefaultAudioEndpoint(loopback ? 0 : 1, 1, out device));
            else Check(enumerator.GetDevice(deviceId, out device));
            var iid = typeof(IAudioEndpointVolume).GUID;
            Check(device.Activate(ref iid, 23, IntPtr.Zero, out var obj)); volume = (IAudioEndpointVolume)obj;
            Check(volume.GetMute(out bool muted)); Check(volume.GetMasterVolumeLevelScalar(out float level));
            return new(muted, level);
        } finally { Release(volume); Release(device); Release(enumerator); }
    }
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (captureTask != null) throw new InvalidOperationException("Already started");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        captureTask = Task.Run(() => Capture(ready));
        pipeTask = Task.Run(Pump);
        try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken); }
        catch { stop.Cancel(); throw; }
    }
    private async Task Pump()
    {
        try
        {
            await pipe.WaitForConnectionAsync(stop.Token); connected.Set();
            await foreach (var data in queue.Reader.ReadAllAsync(stop.Token)) await pipe.WriteAsync(data, stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex) { Error = "Audio pipe: " + ex.Message; stop.Cancel(); }
    }
    private void Capture(TaskCompletionSource ready)
    {
        IMMDeviceEnumerator? enumerator = null; IMMDevice? device = null; IAudioClient? client = null; IAudioCaptureClient? capture = null; IntPtr format = IntPtr.Zero;
        int com = CoInitializeEx(IntPtr.Zero, 0); bool started = false;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            Check(enumerator.GetDevice(id, out device));
            var iid = typeof(IAudioClient).GUID; Check(device.Activate(ref iid, 23, IntPtr.Zero, out var obj)); client = (IAudioClient)obj;
            Check(client.GetMixFormat(out format));
            ushort tag = (ushort)Marshal.ReadInt16(format), bits = (ushort)Marshal.ReadInt16(format, 14);
            Channels = (ushort)Marshal.ReadInt16(format, 2); SampleRate = Marshal.ReadInt32(format, 4);
            int block = (ushort)Marshal.ReadInt16(format, 12);
            bool floating = tag == 3 || (tag == 65534 && Marshal.PtrToStructure<Guid>(format + 24) == new Guid("00000003-0000-0010-8000-00aa00389b71"));
            if ((!floating && bits != 16 && bits != 24 && bits != 32) || (floating && bits != 32)) throw new NotSupportedException($"Unsupported endpoint PCM format {tag}/{bits}");
            Check(client.Initialize(0, loopback ? 0x20000u : 0u, 1000000, 0, format, IntPtr.Zero));
            iid = typeof(IAudioCaptureClient).GUID; Check(client.GetService(ref iid, out obj)); capture = (IAudioCaptureClient)obj;
            ready.TrySetResult();
            connected.Wait(stop.Token);
            Check(client.Start()); started = true;
            var clock = Stopwatch.StartNew(); long delivered = 0;
            while (!stop.IsCancellationRequested)
            {
                Check(capture.GetNextPacketSize(out var packet));
                while (packet > 0)
                {
                    Check(capture.GetBuffer(out var data, out var frames, out var flags, out _, out _));
                    try
                    {
                        var output = new byte[checked((int)frames * Channels * 4)];
                        float packetPeak = 0;
                        if ((flags & 2) == 0)
                        {
                            var raw = new byte[checked((int)frames * block)]; Marshal.Copy(data, raw, 0, raw.Length);
                            for (int j = 0; j < (int)frames * Channels; j++)
                            {
                                int offset = j * (bits / 8);
                                float sample = floating ? BitConverter.ToSingle(raw, offset) : bits == 16 ? BitConverter.ToInt16(raw, offset) / 32768f : bits == 32 ? BitConverter.ToInt32(raw, offset) / 2147483648f : ((raw[offset] | raw[offset + 1] << 8 | raw[offset + 2] << 16) << 8) / 2147483648f;
                                float scaled = Math.Clamp(sample * gain, -1f, 1f);
                                if (!float.IsFinite(scaled)) scaled = 0;
                                packetPeak = Math.Max(packetPeak, Math.Abs(scaled));
                                BitConverter.TryWriteBytes(output.AsSpan(j * 4, 4), scaled);
                            }
                        }
                        Volatile.Write(ref peakLevel, packetPeak);
                        Volatile.Write(ref maxPeakLevel, Math.Max(Volatile.Read(ref maxPeakLevel), packetPeak));
                        Interlocked.Exchange(ref lastPacketTimestamp, Stopwatch.GetTimestamp());
                        Interlocked.Increment(ref capturedPackets);
                        Interlocked.Add(ref capturedSamples, (long)frames * Channels);
                        Interlocked.Add(ref capturedFrames, frames);
                        if (!queue.Writer.TryWrite(output)) throw new IOException("Audio consumer fell behind the bounded capture buffer."); delivered += frames;
                    }
                    finally { Check(capture.ReleaseBuffer(frames)); }
                    Check(capture.GetNextPacketSize(out packet));
                }
                long target = (long)(clock.Elapsed.TotalSeconds * SampleRate);
                if (loopback && target - delivered >= SampleRate / 100)
                {
                    int frames = (int)Math.Min(target - delivered, SampleRate / 20);
                    if (!queue.Writer.TryWrite(new byte[frames * Channels * 4])) throw new IOException("Audio consumer fell behind the bounded silence buffer."); delivered += frames;
                    if (target - delivered > SampleRate / 2) throw new IOException("Audio capture clock fell behind by more than 500 ms.");
                }
                // GetNextPacketSize detects invalidation even on idle devices.
                stop.Token.WaitHandle.WaitOne(5);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { ready.TrySetCanceled(); }
        catch (Exception ex) { Error = "WASAPI: " + ex.Message; ready.TrySetException(ex); stop.Cancel(); }
        finally
        {
            if (started) client!.Stop();
            queue.Writer.TryComplete(); if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
            Release(capture); Release(client); Release(device); Release(enumerator); if (com >= 0) CoUninitialize();
        }
    }
    public async Task StopAsync()
    {
        if (Volatile.Read(ref disposed) == 2) return;
        stop.Cancel();
        if (captureTask != null) await captureTask.ConfigureAwait(false);
        if (pipeTask != null) await pipeTask.ConfigureAwait(false);
    }
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref disposed, 1, 0) != 0) return;
        try { StopAsync().GetAwaiter().GetResult(); }
        finally { pipe.Dispose(); connected.Dispose(); stop.Dispose(); Volatile.Write(ref disposed, 2); }
    }
    private static void Check(int hr) { Marshal.ThrowExceptionForHR(hr); }
    private static void Release(object? obj) { if (obj != null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj); }
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid fmtid; public uint pid; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr pointer; }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IMMDeviceCollection
    { [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index, out IMMDevice device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count); [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }
    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioClient
    {
        [PreserveSig] int Initialize(int mode, uint flags, long duration, long periodicity, IntPtr format, IntPtr session);
        [PreserveSig] int GetBufferSize(out uint frames); [PreserveSig] int GetStreamLatency(out long latency); [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int mode, IntPtr format, out IntPtr closest); [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long normal, out long minimum); [PreserveSig] int Start(); [PreserveSig] int Stop(); [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle); [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }
    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong position, out ulong qpc);
        [PreserveSig] int ReleaseBuffer(uint frames); [PreserveSig] int GetNextPacketSize(out uint frames);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}


