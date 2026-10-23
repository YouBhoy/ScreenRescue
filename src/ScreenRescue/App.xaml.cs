using System;
using System.Threading;
using System.Windows;

namespace ScreenRescue;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, "Local\\ScreenRescue.Desktop", out var created);
        if (!created)
        {
            MessageBox.Show("ScreenRescue is already running. Open its tray icon to change settings.");
            Shutdown();
            return;
        }
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (window.StartHidden && window.CanHide) window.Hide();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        instance?.Dispose();
        base.OnExit(e);
    }
}
