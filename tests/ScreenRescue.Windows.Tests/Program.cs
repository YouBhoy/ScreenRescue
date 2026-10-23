using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using ScreenRescue;
using ScreenRescue.Core;
using Forms = System.Windows.Forms;

internal static class Program
{
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--fixture")
        {
            var display = Array.Find(Forms.Screen.AllScreens, s => !s.Primary)!;
            using var form = new Forms.Form { Text = "ScreenRescue test fixture", StartPosition = Forms.FormStartPosition.Manual,
                Bounds = new System.Drawing.Rectangle(display.Bounds.X + 20, display.Bounds.Y + 20, 400, 300) };
            form.Shown += (_, _) => { Console.WriteLine(form.Handle.ToInt64()); Console.Out.Flush(); };
            Forms.Application.Run(form);
            return 0;
        }
        var directory = Path.Combine(Path.GetTempPath(), "ScreenRescue-windows-tests-" + Guid.NewGuid());
        var path = Path.Combine(directory, "settings.json");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        MainWindow? window = null;
        try
        {
            window = new MainWindow(path);
            window.Show();
            Pump();
            var status = (TextBlock)window.FindName("StatusText");
            if (string.IsNullOrWhiteSpace(status.Text)) throw new Exception("Status did not initialize");
            var top = (TextBox)window.FindName("TopBox");
            if (top.Text != "40") throw new Exception("Default bounds did not initialize");
            // Send the registered message through the actual WPF HWND hook.
            PostMessage(new WindowInteropHelper(window).Handle, 0x0312, new IntPtr(1), IntPtr.Zero);
            Pump();
            if (!File.Exists(path) || SettingsStore.Load(path).Enabled) throw new Exception("Emergency pause was not persisted");
            var primary = Forms.Screen.PrimaryScreen!;
            var rescuer = new WindowRescuer();
            var result = rescuer.Sweep(new Settings { Enabled = true, MonitorDevice = primary.DeviceName });
            if (!result.StartsWith("Paused:")) throw new Exception("Primary display was accepted");
            result = rescuer.Sweep(new Settings { Enabled = true, MonitorDevice = "disconnected-test-display" });
            if (!result.StartsWith("Paused:")) throw new Exception("Missing display was accepted");
            var secondary = Array.Find(Forms.Screen.AllScreens, s => !s.Primary);
            if (secondary is not null)
            {
                using var fixture = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--fixture")
                    { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true })!;
                try
                {
                    var line = fixture.StandardOutput.ReadLineAsync();
                    if (!line.Wait(TimeSpan.FromSeconds(15))) throw new Exception("Fixture startup timed out");
                    var handle = new IntPtr(long.Parse(line.Result!));
                    var active = new Settings { Enabled = true, MonitorDevice = secondary.DeviceName };
                    rescuer.Sweep(active, handle); Pump();
                    var b = secondary.Bounds; var w = secondary.WorkingArea;
                    var area = active.Bounds.Resolve(new(b.X, b.Y, b.Width, b.Height), new(w.X, w.Y, w.Width, w.Height));
                    Native.GetWindowRect(handle, out var moved);
                    if (moved.Pixels.Fit(area) != moved.Pixels) throw new Exception("Fixture was not fitted to usable bounds");
                    Native.ShowWindowAsync(handle, 3); Pump();
                    rescuer.Reset();
                    rescuer.Sweep(active, handle); Pump();
                    rescuer.Sweep(active, handle); Pump();
                    Native.GetWindowRect(handle, out moved);
                    if (Native.IsZoomed(handle) || moved.Pixels != area) throw new Exception($"Maximize fitting failed: {moved.Pixels}, expected {area}");
                    Console.WriteLine("PASS: real secondary-display window repositioning and maximize fitting (isolated fixture only)");
                }
                finally { if (!fixture.HasExited) { fixture.Kill(); fixture.WaitForExit(); } }
            }
            else Console.WriteLine("SKIP: native movement tests require a secondary display");
            Console.WriteLine("PASS: WPF startup, default bounds, HWND emergency pause, saved pause, primary-display guard, disconnected-display guard");
            Console.WriteLine($"Displays available: {Forms.Screen.AllScreens.Length}; emergency shortcut: {((TextBlock)window.FindName("HotkeyText")).Text}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            // Invoke the real cleanup path, disposing tray, timer and shortcut.
            if (window is not null)
                typeof(MainWindow).GetMethod("Quit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null);
            else app.Shutdown();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(850) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
