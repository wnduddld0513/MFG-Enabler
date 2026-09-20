using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
namespace MfgEnabler;

public sealed partial class MainWindow
{
    FileSystemWatcher storageWatcher;
    DispatcherTimer storageDebounce;
    bool storageScanPending;
    WindowMessages windowMessages;
    void InitializeStorageWatcher()
    {
        windowMessages = new WindowMessages(hwnd, () => { AppWindow.Show(); if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p) p.Restore(); Activate(); });
        Program.NotifyUiReady();
        storageDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        storageDebounce.Tick += async (_, _) =>
        {
            storageDebounce.Stop();
            if (busy || dialogOpen) return;
            if (!storageScanPending || !settings.Global.Enabled) return;
            storageScanPending = false;
            await Scan();
        };
        Closed += (_, _) => { storageWatcher?.Dispose(); storageDebounce.Stop(); windowMessages.Dispose(); };
    }
    void ConfigureStorageWatcher()
    {
        storageWatcher?.Dispose(); storageWatcher = null;
        if (!settings.Global.Enabled) { storageScanPending = false; storageDebounce?.Stop(); return; }
        string directory = Path.GetDirectoryName(Discovery.DefaultStorage);
        while (!Directory.Exists(directory)) directory = Path.GetDirectoryName(directory);
        storageWatcher = new FileSystemWatcher(directory, "ApplicationStorage.json") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        storageWatcher.Changed += StorageChanged; storageWatcher.Created += StorageChanged; storageWatcher.Renamed += StorageChanged;
        storageWatcher.Error += (_, _) => QueueStorageScan();
        storageWatcher.EnableRaisingEvents = true;
    }
    void StorageChanged(object sender, FileSystemEventArgs e)
    {
        if (string.Equals(e.FullPath, Discovery.DefaultStorage, StringComparison.OrdinalIgnoreCase)) QueueStorageScan();
    }
    void QueueStorageScan() => DispatcherQueue.TryEnqueue(() => { storageScanPending = true; storageDebounce.Stop(); storageDebounce.Start(); });
    void ResumeStorageScan() { if (storageScanPending && !busy && !dialogOpen) storageDebounce?.Start(); }
}

internal sealed class WindowMessages : IDisposable
{
    readonly IntPtr window;
    readonly Procedure procedure;
    readonly Action show;
    readonly uint showMessage = RegisterWindowMessage("MFG-Enabler.Show");
    delegate IntPtr Procedure(IntPtr h, uint m, IntPtr w, IntPtr l, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr h, Procedure p, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr h, Procedure p, UIntPtr id);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    public WindowMessages(IntPtr handle, Action show)
    {
        window = handle; this.show = show; procedure = Handle;
        if (!SetWindowSubclass(window, procedure, (UIntPtr)73, UIntPtr.Zero)) throw new System.ComponentModel.Win32Exception();
    }
    IntPtr Handle(IntPtr h, uint m, IntPtr w, IntPtr l, UIntPtr id, UIntPtr data)
    {
        if (m == showMessage) { show(); return IntPtr.Zero; }
        return DefSubclassProc(h, m, w, l);
    }
    public void Dispose() => RemoveWindowSubclass(window, procedure, (UIntPtr)73);
}
