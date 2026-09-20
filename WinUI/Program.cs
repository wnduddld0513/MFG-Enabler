using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
namespace MfgEnabler;
public static class Program
{
    static EventWaitHandle uiReady;
    internal static void NotifyUiReady() => uiReady?.Set();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
    [STAThread]
    static int Main(string[] args)
    {
        bool background = Array.Exists(args, x => x.Equals("--background-scan", StringComparison.OrdinalIgnoreCase));
        using var gate = new Mutex(false, AppPaths.InstanceName);
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, AppPaths.InstanceName + "-ready");
        bool owned;
        try { owned = gate.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
        while (!owned)
        {
            if (background) return 2;
            if (ready.WaitOne(0))
            {
                PostMessage((IntPtr)0xffff, RegisterWindowMessage("MFG-Enabler.Show"), IntPtr.Zero, IntPtr.Zero);
                return 0;
            }
            // A worker or updater owns the gate: wait until it finishes before opening the UI.
            try { owned = gate.WaitOne(100); } catch (AbandonedMutexException) { owned = true; }
        }
        ready.Reset(); uiReady = ready;
        try { if (background) return BackgroundApply.Run(); StartUi(); return 0; }
        finally { ready.Reset(); uiReady = null; gate.ReleaseMutex(); }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void StartUi()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }
}
