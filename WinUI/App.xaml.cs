using System;
using System.Threading;
using Microsoft.UI.Xaml;

namespace MfgEnabler;
public partial class App : Application
{
    private Window window;
    private Mutex instance;
    public App() { InitializeComponent(); }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        instance = new Mutex(true, @"Local\MFG-Enabler-1", out bool created);
        if (!created) { Exit(); return; }
        window = new MainWindow();
        window.Closed += (_, _) => { instance.ReleaseMutex(); instance.Dispose(); };
        window.Activate();
    }
}
