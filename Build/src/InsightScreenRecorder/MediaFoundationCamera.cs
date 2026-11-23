using System.Drawing.Imaging;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace InsightScreenRecorder;

internal sealed record CameraDeviceInfo(string Id, string Name);

/// <summary>Local Windows Media Foundation capture; BGRA frames are never generated or uploaded.</summary>
internal sealed class MediaFoundationCamera : IDisposable
{
    private readonly string deviceId;
    private readonly object gate = new();
    private readonly CancellationTokenSource stop = new();
    private readonly TaskCompletionSource firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly NamedPipeServerStream pipe;
    private Task? captureTask, pipeTask, monitorTask, shutdownTask;
    private IntPtr source, sourceReader;
    private byte[]? latestFrame;
    private bool disposed;
    private int started;
    private long frameCount;
    private long lastFrameTimestamp;
    private volatile string captureStage = "not started";
    internal TimeSpan NoFrameTimeout { get; set; } = TimeSpan.FromSeconds(3);
    internal TimeSpan PipeBackpressureTimeout { get; set; } = TimeSpan.FromMilliseconds(500);
    public string PipePath { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int FramesPerSecond { get; }
    public long FrameCount => Interlocked.Read(ref frameCount);
    public string? Error { get; private set; }

    public MediaFoundationCamera(string deviceId, int framesPerSecond = 15)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        this.deviceId = deviceId;
        FramesPerSecond = Math.Clamp(framesPerSecond, 1, 60);
        var name = "InsightCamera-" + Guid.NewGuid().ToString("N");
        PipePath = @"\\.\pipe\" + name;
        pipe = new NamedPipeServerStream(name, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 65536);
    }

    public static IReadOnlyList<CameraDeviceInfo> EnumerateDevices()
    {
        var com = Native.CoInitializeEx(IntPtr.Zero, 0);
        if (com < 0 && com != unchecked((int)0x80010106)) Native.Check(com, "CoInitializeEx");
        bool mf = false;
        IntPtr attributes = IntPtr.Zero, array = IntPtr.Zero;
        uint count = 0;
        try
        {
            Native.Check(Native.MFStartup(0x20070, 0), "MFStartup"); mf = true;
            attributes = Native.Attributes();
            Native.SetGuid(attributes, Native.SourceType, Native.VideoCapture);
            Native.Check(Native.MFEnumDeviceSources(attributes, out array, out count), "MFEnumDeviceSources");
            var result = new List<CameraDeviceInfo>();
            for (int i = 0; i < count; i++)
            {
                var activate = Marshal.ReadIntPtr(array, i * IntPtr.Size);
                try { result.Add(new CameraDeviceInfo(Native.GetString(activate, Native.SymbolicLink), Native.GetString(activate, Native.FriendlyName))); }
                finally { Native.Release(activate); Marshal.WriteIntPtr(array, i * IntPtr.Size, IntPtr.Zero); }
            }
            return result;
        }
        finally
        {
            if (array != IntPtr.Zero) { for (int i=0;i<count;i++) Native.Release(Marshal.ReadIntPtr(array,i*IntPtr.Size)); Marshal.FreeCoTaskMem(array); }
            Native.Release(attributes);
            if (mf) Native.MFShutdown();
            if (com >= 0) Native.CoUninitialize();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("Camera has already been started.");
        captureTask = Task.Factory.StartNew(Capture, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        pipeTask = PumpPipeAsync();
        monitorTask = MonitorFramesAsync();
        try { await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false); }
        catch
        {
            try { await StopAsync().ConfigureAwait(false); }
            catch (Exception cleanup) { Error ??= cleanup.Message; }
            throw;
        }
    }

    private void Capture()
    {
        int com = Native.CoInitializeEx(IntPtr.Zero, 0);
        bool mf = false;
        IntPtr attributes = IntPtr.Zero, readerAttributes = IntPtr.Zero, reader = IntPtr.Zero, mediaType = IntPtr.Zero, current = IntPtr.Zero;
        try
        {
            Native.Check(com, "CoInitializeEx(camera thread)");
            Native.Check(Native.MFStartup(0x20070, 0), "MFStartup"); mf = true;
            attributes = Native.Attributes();
            Native.SetGuid(attributes, Native.SourceType, Native.VideoCapture);
            Native.Check(Native.Call<Native.SetStringDelegate>(attributes, 25)(attributes, Native.SymbolicLink, deviceId), "Set camera symbolic link");
            IntPtr createdSource;
            Native.Check(Native.MFCreateDeviceSource(attributes, out createdSource), "MFCreateDeviceSource (camera binding)");
            lock (gate) { source = createdSource; }
            if (stop.IsCancellationRequested) return;
            readerAttributes = Native.Attributes();
            Native.Check(Native.Call<Native.SetUIntDelegate>(readerAttributes, 21)(readerAttributes, Native.EnableVideoProcessing, 1), "Enable camera video conversion");
            Native.Check(Native.MFCreateSourceReaderFromMediaSource(createdSource, readerAttributes, out reader), "MFCreateSourceReaderFromMediaSource");
            lock (gate) sourceReader = reader;
            Native.Check(Native.Call<Native.SelectStreamDelegate>(reader, 4)(reader, 0xfffffffe, false), "Deselect camera streams");
            Native.Check(Native.Call<Native.SelectStreamDelegate>(reader, 4)(reader, 0xfffffffc, true), "Select camera video stream");
            Native.Check(Native.MFCreateMediaType(out mediaType), "MFCreateMediaType");
            Native.SetGuid(mediaType, Native.MajorType, Native.Video);
            Native.SetGuid(mediaType, Native.Subtype, Native.Rgb32);
            Native.Check(Native.Call<Native.SetTypeDelegate>(reader, 7)(reader, 0xfffffffc, IntPtr.Zero, mediaType), "Set camera RGB32 output");
            Native.Check(Native.Call<Native.GetTypeDelegate>(reader, 6)(reader, 0xfffffffc, out current), "Get camera output format");
            Native.Check(Native.Call<Native.GetULongDelegate>(current, 8)(current, Native.FrameSize, out var size), "Get camera frame size");
            Width = checked((int)(size >> 32)); Height = checked((int)(size & 0xffffffff));
            if (Width <= 0 || Height <= 0 || (long)Width * Height > 33554432) throw new InvalidOperationException("Invalid camera frame dimensions.");
            // RGB DIB buffers default to bottom-up; IMF2DBuffer below supplies the authoritative scanline and stride when available.
            int stride = -checked(Width * 4);
            if (Native.Call<Native.GetUIntDelegate>(current, 7)(current, Native.DefaultStride, out var reportedStride) >= 0) stride = unchecked((int)reportedStride);
            while (!stop.IsCancellationRequested)
            {
                IntPtr sample = IntPtr.Zero;
                captureStage = "ReadSample";
                int result = Native.Call<Native.ReadSampleDelegate>(reader, 9)(reader, 0xfffffffc, 0, out _, out var flags, out _, out sample);
                captureStage = "sample received";
                try
                {
                    if (stop.IsCancellationRequested) break;
                    Native.Check(result, "Read camera sample");
                    if ((flags & 2) != 0) throw new IOException("Camera stream ended.");
                    if ((flags & 0x20) != 0) throw new IOException("Camera output format changed during capture.");
                    if (sample == IntPtr.Zero) continue;
                    var bytes = CopySample(sample, stride);
                    lock (gate) latestFrame = bytes;
                    Interlocked.Exchange(ref lastFrameTimestamp, Stopwatch.GetTimestamp());
                    Interlocked.Increment(ref frameCount);
                    firstFrame.TrySetResult();
                }
                finally { Native.Release(sample); }
            }
        }
        catch (Exception e)
        {
            if (!stop.IsCancellationRequested)
            {
                Error = e.Message; firstFrame.TrySetException(e);
                stop.Cancel(); pipe.Dispose();
            }
        }
        finally
        {
            captureStage = "capture finally";
            IntPtr oldSource;
            Task? pendingShutdown;
            lock (gate) { oldSource = source; source = IntPtr.Zero; sourceReader = IntPtr.Zero; pendingShutdown = shutdownTask; }
            captureStage = "final source shutdown";
            // ReadSample has returned. Let the explicit Flush/Shutdown finish before
            // releasing the reader or shutting down Media Foundation on this thread.
            if (pendingShutdown != null) pendingShutdown.GetAwaiter().GetResult();
            else if (oldSource != IntPtr.Zero) Native.Call<Native.SimpleDelegate>(oldSource, 12)(oldSource);
            captureStage = "release media types"; Native.Release(current); Native.Release(mediaType);
            captureStage = "release reader"; Native.Release(reader);
            captureStage = "release source"; Native.Release(oldSource);
            Native.Release(readerAttributes); Native.Release(attributes);
            captureStage = "MFShutdown";
            if (mf) Native.MFShutdown();
            if (com >= 0) Native.CoUninitialize();
            captureStage = "released";
            firstFrame.TrySetCanceled();
        }
    }

    private byte[] CopySample(IntPtr sample, int defaultStride)
    {
        IntPtr buffer = IntPtr.Zero, twoD = IntPtr.Zero;
        bool locked = false, locked2D = false;
        try
        {
            Native.Check(Native.Call<Native.OutPointerDelegate>(sample, 41)(sample, out buffer), "Get camera sample buffer");
            var iid = Native.Buffer2D;
            int query = Marshal.QueryInterface(buffer, in iid, out twoD);
            IntPtr scanline;
            int stride;
            if (query >= 0)
            {
                Native.Check(Native.Call<Native.Lock2DDelegate>(twoD, 3)(twoD, out scanline, out stride), "Lock camera 2D buffer"); locked2D = true;
            }
            else
            {
                Native.Check(Native.Call<Native.LockDelegate>(buffer, 3)(buffer, out scanline, out _, out var length), "Lock camera buffer"); locked = true;
                stride = defaultStride;
                if ((long)Math.Abs(stride) * Height > length) throw new IOException("Camera buffer is shorter than its declared dimensions.");
                if (stride < 0) scanline = IntPtr.Add(scanline, (Height - 1) * -stride);
            }
            var rowBytes = checked(Width * 4);
            if (Math.Abs(stride) < rowBytes) throw new IOException("Camera stride is shorter than a row.");
            var frame = new byte[checked(rowBytes * Height)];
            for (int row=0;row<Height;row++) Marshal.Copy(IntPtr.Add(scanline, row * stride), frame, row * rowBytes, rowBytes);
            // MFVideoFormat_RGB32 has an unused fourth byte. Normalize it for independent ARGB preview ownership.
            for (int i=3;i<frame.Length;i+=4) frame[i]=255;
            return frame;
        }
        finally
        {
            if (locked2D) Native.Call<Native.SimpleDelegate>(twoD,4)(twoD);
            if (locked) Native.Call<Native.SimpleDelegate>(buffer,4)(buffer);
            Native.Release(twoD); Native.Release(buffer);
        }
    }

    private async Task PumpPipeAsync()
    {
        try
        {
            await firstFrame.Task.WaitAsync(stop.Token).ConfigureAwait(false);
            await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
            using var clock = new PeriodicTimer(TimeSpan.FromSeconds(1d / FramesPerSecond));
            var outputStarted = Stopwatch.GetTimestamp();
            long sentFrames = 0;
            do
            {
                if (Stopwatch.GetElapsedTime(outputStarted) - TimeSpan.FromSeconds((double)sentFrames / FramesPerSecond) > PipeBackpressureTimeout)
                {
                    await FailCaptureAsync("The camera encoder fell behind real time by more than 500 ms.").ConfigureAwait(false);
                    return;
                }
                byte[]? frame;
                lock (gate) frame = latestFrame;
                if (frame != null)
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    deadline.CancelAfter(PipeBackpressureTimeout);
                    try { await pipe.WriteAsync(frame, deadline.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!stop.IsCancellationRequested)
                    {
                        await FailCaptureAsync("The camera encoder stopped accepting frames for more than 500 ms.").ConfigureAwait(false);
                        return;
                    }
                    sentFrames++;
                }
            } while (await clock.WaitForNextTickAsync(stop.Token).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) when (stop.IsCancellationRequested) { }
        catch (IOException) { /* A recorder can close its pipe before the camera preview stops. */ }
        catch (Exception e) { if (!stop.IsCancellationRequested) Error = e.Message; }
    }

    private async Task MonitorFramesAsync()
    {
        try
        {
            await firstFrame.Task.WaitAsync(stop.Token).ConfigureAwait(false);
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            while (await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false))
            {
                if (Stopwatch.GetElapsedTime(Interlocked.Read(ref lastFrameTimestamp)) > NoFrameTimeout)
                {
                    await FailCaptureAsync("The camera stopped delivering new frames for more than 3 seconds.").ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!stop.IsCancellationRequested) Error ??= e.Message; }
    }

    private async Task FailCaptureAsync(string message)
    {
        if (stop.IsCancellationRequested) return;
        Error = message;
        stop.Cancel();
        pipe.Dispose();
        try { await ShutdownSourceAsync().ConfigureAwait(false); }
        catch (Exception e) { Error += " Camera shutdown: " + e.Message; }
    }

    public Bitmap? GetPreviewFrame()
    {
        byte[]? bytes;
        lock (gate) bytes = latestFrame;
        if (bytes == null) return null;
        var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0,0,Width,Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { for (int y=0;y<Height;y++) Marshal.Copy(bytes,y*Width*4,IntPtr.Add(data.Scan0,y*data.Stride),Width*4); }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }

    public async Task StopAsync()
    {
        stop.Cancel();
        pipe.Dispose();
        async Task Wait(Task? task, string stage) {
            if (task == null) return;
            try { await task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
            catch (TimeoutException ex) { throw new IOException($"Camera {stage} timed out (stage={captureStage}, capture={captureTask?.Status}, pipe={pipeTask?.Status}, monitor={monitorTask?.Status}, shutdown={shutdownTask?.Status}).", ex); }
        }
        await Wait(ShutdownSourceAsync(), "source shutdown").ConfigureAwait(false);
        await Wait(captureTask, "capture release").ConfigureAwait(false);
        await Wait(pipeTask, "pipe release").ConfigureAwait(false);
        await Wait(monitorTask, "monitor release").ConfigureAwait(false);
        lock (gate) latestFrame = null;
    }

    private Task ShutdownSourceAsync()
    {
        // Flush cancels the Source Reader's pending request. Shutting down only
        // the underlying source can leave synchronous ReadSample blocked on reopen.
        lock (gate)
        {
            if (shutdownTask != null) return shutdownTask.WaitAsync(TimeSpan.FromSeconds(5));
            var active = source;
            if (active == IntPtr.Zero) return Task.CompletedTask;
            var activeReader = sourceReader;
            Marshal.AddRef(active);
            if (activeReader != IntPtr.Zero) Marshal.AddRef(activeReader);
            shutdownTask = Task.Run(() =>
            {
                int com = Native.CoInitializeEx(IntPtr.Zero,0);
                try {
                    if (activeReader != IntPtr.Zero) Native.Call<Native.FlushDelegate>(activeReader,10)(activeReader,0xfffffffe);
                    Native.Call<Native.SimpleDelegate>(active,12)(active);
                }
                finally { Native.Release(activeReader); Native.Release(active); if (com >= 0) Native.CoUninitialize(); }
            });
            return shutdownTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        StopAsync().GetAwaiter().GetResult();
        stop.Dispose();
    }

    private static class Native
    {
        internal static readonly Guid SourceType=new("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3"), VideoCapture=new("8ac3587a-4ae7-42d8-99e0-0a6013eef90f"), SymbolicLink=new("58f0aad8-22bf-4f8a-bb3d-d2c4978c6e2f"), FriendlyName=new("60d0e559-52f8-4fa2-bbce-acdb34a8ec01"), EnableVideoProcessing=new("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d"), MajorType=new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), Subtype=new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5"), Video=new("73646976-0000-0010-8000-00aa00389b71"), Rgb32=new("00000016-0000-0010-8000-00aa00389b71"), FrameSize=new("1652c33d-d6b2-4012-b834-72030849a37d"), DefaultStride=new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6"), Buffer2D=new("7dc9d5f9-9ed9-44ec-9bbf-0600bb589fbb");
        [DllImport("ole32.dll")] internal static extern int CoInitializeEx(IntPtr reserved,uint mode);
        [DllImport("ole32.dll")] internal static extern void CoUninitialize();
        [DllImport("mfplat.dll")] internal static extern int MFStartup(uint version,uint flags);
        [DllImport("mfplat.dll")] internal static extern int MFShutdown();
        [DllImport("mfplat.dll")] internal static extern int MFCreateAttributes(out IntPtr attributes,uint count);
        [DllImport("mfplat.dll")] internal static extern int MFCreateMediaType(out IntPtr mediaType);
        [DllImport("mf.dll")] internal static extern int MFEnumDeviceSources(IntPtr attributes,out IntPtr activateArray,out uint count);
        [DllImport("mf.dll")] internal static extern int MFCreateDeviceSource(IntPtr attributes,out IntPtr source);
        [DllImport("mfreadwrite.dll")] internal static extern int MFCreateSourceReaderFromMediaSource(IntPtr source,IntPtr attributes,out IntPtr reader);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SimpleDelegate(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int FlushDelegate(IntPtr self,uint stream);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetGuidDelegate(IntPtr self,[In,MarshalAs(UnmanagedType.LPStruct)] Guid key,[In,MarshalAs(UnmanagedType.LPStruct)] Guid value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetStringDelegate(IntPtr self,[In,MarshalAs(UnmanagedType.LPStruct)] Guid key,[MarshalAs(UnmanagedType.LPWStr)] string value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int GetStringDelegate(IntPtr self,[In,MarshalAs(UnmanagedType.LPStruct)] Guid key,out IntPtr value,out uint length);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int GetUIntDelegate(IntPtr self,[In,MarshalAs(UnmanagedType.LPStruct)] Guid key,out uint value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int GetULongDelegate(IntPtr self,[In,MarshalAs(UnmanagedType.LPStruct)] Guid key,out ulong value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetUIntDelegate(IntPtr self,[In,MarshalAs(UnmanagedType.LPStruct)] Guid key,uint value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SelectStreamDelegate(IntPtr self,uint stream,[MarshalAs(UnmanagedType.Bool)] bool selected);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int SetTypeDelegate(IntPtr self,uint stream,IntPtr reserved,IntPtr type);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int GetTypeDelegate(IntPtr self,uint stream,out IntPtr type);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int ReadSampleDelegate(IntPtr self,uint stream,uint controls,out uint actualStream,out uint flags,out long timestamp,out IntPtr sample);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int OutPointerDelegate(IntPtr self,out IntPtr pointer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int LockDelegate(IntPtr self,out IntPtr pointer,out uint maxLength,out uint currentLength);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] internal delegate int Lock2DDelegate(IntPtr self,out IntPtr scanline,out int stride);
        internal static T Call<T>(IntPtr obj,int slot) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj),slot*IntPtr.Size));
        internal static void Check(int hr,string operation) { if(hr < 0) throw new COMException($"{operation}: HRESULT 0x{hr:X8} ({Marshal.GetExceptionForHR(hr)?.Message})",hr); }
        internal static void Release(IntPtr p) { if(p!=IntPtr.Zero) Marshal.Release(p); }
        internal static IntPtr Attributes() {Check(MFCreateAttributes(out var p,4),"MFCreateAttributes");return p;}
        internal static void SetGuid(IntPtr p,Guid key,Guid value) => Check(Call<SetGuidDelegate>(p,24)(p,key,value),"Set Media Foundation GUID attribute");
        internal static string GetString(IntPtr p,Guid key)
        {
            Check(Call<GetStringDelegate>(p,13)(p,key,out var text,out _),"Read camera device name");
            try {return Marshal.PtrToStringUni(text) ?? "";} finally {Marshal.FreeCoTaskMem(text);}
        }
    }
}



