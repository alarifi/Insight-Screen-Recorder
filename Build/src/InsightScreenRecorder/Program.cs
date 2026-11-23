using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace InsightScreenRecorder;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var instance = new SingleInstanceGuard();
        if (!instance.TryBecomePrimaryOrActivate()) return;
        ApplicationConfiguration.Initialize();
        using var form = new MainForm();
        _ = form.Handle;
        instance.ListenForActivation(() =>
        {
            if (form.IsDisposed || form.Disposing) return;
            try { form.BeginInvoke((Action)(() => { if (!form.IsDisposed) form.RestoreFromTray(); })); }
            catch (InvalidOperationException) { }
        });
        Application.Run(form);
    }
}

// Mutex ownership stays on the startup thread. No UI thread waits for another instance.
internal sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex mutex;
    private readonly EventWaitHandle activation;
    private RegisteredWaitHandle? listener;
    private bool ownsMutex;
    private int disposed;

    internal SingleInstanceGuard(string applicationScope = "InsightScreenRecorder")
    {
        string sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Windows user identity is unavailable.");
        string prefix = $@"Local\INSIGHT.{applicationScope}.{sid}.{Process.GetCurrentProcess().SessionId}";
        mutex = new Mutex(false, prefix + ".Owner");
        try { activation = new EventWaitHandle(false, EventResetMode.AutoReset, prefix + ".Activate"); }
        catch { mutex.Dispose(); throw; }
    }

    internal bool TryBecomePrimaryOrActivate(int closingHandoffMilliseconds = 750)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (ownsMutex) return true;
        bool Acquire(int timeout)
        {
            try { return mutex.WaitOne(timeout); }
            catch (AbandonedMutexException) { return true; }
        }
        if (Acquire(0)) return ownsMutex = true;
        activation.Set(); // Retained if the owner's window has not been constructed yet.
        if (Acquire(closingHandoffMilliseconds)) return ownsMutex = true;
        return false;
    }

    internal void ListenForActivation(Action activate)
    {
        ArgumentNullException.ThrowIfNull(activate);
        if (!ownsMutex) throw new InvalidOperationException("Only the primary instance can receive activation.");
        if (listener != null) throw new InvalidOperationException("Activation listener already started.");
        listener = ThreadPool.RegisterWaitForSingleObject(activation, (_, timedOut) =>
        {
            if (!timedOut && Volatile.Read(ref disposed) == 0) activate();
        }, null, Timeout.Infinite, false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        listener?.Unregister(null);
        activation.Dispose();
        if (ownsMutex) { mutex.ReleaseMutex(); ownsMutex = false; }
        mutex.Dispose();
    }
}
