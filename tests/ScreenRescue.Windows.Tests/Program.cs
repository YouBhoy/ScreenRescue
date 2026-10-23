using System;
using System.IO;
using System.Linq;
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
            var display = Array.Find(Forms.Screen.AllScreens, s => s.DeviceName == args[1])!;
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
            Settings? lastApplied = null;
            var startupWrites = 0;
            var masks = new FakeBlackout();
            var headless = args.Length > 0 && args[0] == "--headless";
            window = headless
                ? new MainWindow(path, _ => startupWrites++, s => { lastApplied = s; return s.Enabled ? "Active" : "Paused"; }, masks)
                : new MainWindow(path);
            var monitorBox = (ComboBox)window.FindName("MonitorBox");
            if (monitorBox.Items.Count != Forms.Screen.AllScreens.Length) throw new Exception("Not all monitors are selectable");
            if (Array.Find(monitorBox.Items.Cast<object>().Select(i => i.ToString()!).ToArray(), label => label.Contains("primary")) is null)
                throw new Exception("Primary monitor is not labeled");
            if (headless)
            {
                var preview = (System.Windows.Shapes.Rectangle)window.FindName("UsablePreview");
                var topInput = (TextBox)window.FindName("TopBox");
                topInput.Text = "60";
                if (Math.Abs(preview.Height - 56) > 0.01) throw new Exception("Preview does not follow edited bounds");
                // The desktop is never touched in this mode. Model successful hotkey registration
                // and exercise the actual button handler with injected window/startup services.
                typeof(MainWindow).GetField("hotkeyRegistered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, true);
                ((Button)window.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var saved = SettingsStore.Load(path);
                if (!saved.Enabled || saved.Bounds.Top != 60 || lastApplied != saved)
                    throw new Exception("Apply did not save and enable the selected area");
                if (!saved.BlackoutOutside || !masks.Visible) throw new Exception("Apply did not start optional blackout");
                var display = Forms.Screen.AllScreens.Single(s => s.DeviceName == saved.MonitorDevice).Bounds;
                var fullDisplay = new PixelRect(display.X, display.Y, display.Width, display.Height);
                if (masks.Usable != saved.Bounds.Resolve(fullDisplay, fullDisplay))
                    throw new Exception("Blackout excluded the taskbar even when inside the selected area");
                if (startupWrites != 0) throw new Exception("Applying unchanged startup options wrote to the registry");
                if (!((TextBlock)window.FindName("ApplyNotice")).Text.StartsWith("Saved"))
                    throw new Exception("Apply did not confirm success");
                ((Button)window.FindName("PauseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (SettingsStore.Load(path).Enabled) throw new Exception("Pause did not persist disabled fitting");
                if (masks.Visible) throw new Exception("Pause did not immediately remove blackout masks");
                ((Button)window.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!SettingsStore.Load(path).Enabled) throw new Exception("Apply did not resume after pause");
                ((CheckBox)window.FindName("BlackoutBox")).IsChecked = false;
                ((Button)window.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (masks.Visible || SettingsStore.Load(path).BlackoutOutside || !SettingsStore.Load(path).Enabled)
                    throw new Exception("Disabling blackout did not preserve window fitting");
                ((CheckBox)window.FindName("BlackoutBox")).IsChecked = true;
                ((Button)window.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var message = new object[] { IntPtr.Zero, 0x0312, new IntPtr(1), IntPtr.Zero, false };
                typeof(MainWindow).GetMethod("WindowMessage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, message);
                if (masks.Visible || SettingsStore.Load(path).Enabled)
                    throw new Exception("Emergency pause did not remove masks and persist pause");
                ((Button)window.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                message = new object[] { IntPtr.Zero, 0x007E, IntPtr.Zero, IntPtr.Zero, false };
                typeof(MainWindow).GetMethod("WindowMessage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, message);
                if (masks.Visible) throw new Exception("Monitor layout change left stale blackout masks");
                masks.FailShow = true;
                ((Button)window.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (masks.Visible || SettingsStore.Load(path).Enabled)
                    throw new Exception("Failed blackout did not remove masks and save paused state");
                Console.WriteLine("PASS: blackout Apply, option persistence, full selected area, pause/emergency cleanup, and display-change cleanup (injected masks)");
                Console.WriteLine("PASS: WPF settings construction, every monitor selectable, primary label, editable bounds preview");
                Console.WriteLine("PASS: Apply button saves and enables fitting, confirms success, avoids unchanged startup writes, and resumes after pause");
                return 0;
            }
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
            var rescuer = new WindowRescuer();
            var result = rescuer.Sweep(new Settings { Enabled = true, MonitorDevice = "disconnected-test-display" });
            if (!result.StartsWith("Paused:")) throw new Exception("Missing display was accepted");
            foreach (var display in Forms.Screen.AllScreens)
            {
                var start = new ProcessStartInfo(Environment.ProcessPath!)
                    { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                start.ArgumentList.Add("--fixture"); start.ArgumentList.Add(display.DeviceName);
                using var fixture = Process.Start(start)!;
                try
                {
                    var line = fixture.StandardOutput.ReadLineAsync();
                    if (!line.Wait(TimeSpan.FromSeconds(15))) throw new Exception("Fixture startup timed out");
                    var handle = new IntPtr(long.Parse(line.Result!));
                    var active = new Settings { Enabled = true, MonitorDevice = display.DeviceName };
                    rescuer.Sweep(active, handle); Pump();
                    var b = display.Bounds; var w = display.WorkingArea;
                    var area = active.Bounds.Resolve(new(b.X, b.Y, b.Width, b.Height), new(w.X, w.Y, w.Width, w.Height));
                    Native.GetWindowRect(handle, out var moved);
                    if (moved.Pixels.Fit(area) != moved.Pixels) throw new Exception("Fixture was not fitted to usable bounds");
                    Native.ShowWindowAsync(handle, 3); Pump();
                    rescuer.Reset();
                    rescuer.Sweep(active, handle); Pump();
                    rescuer.Sweep(active, handle); Pump();
                    Native.GetWindowRect(handle, out moved);
                    if (Native.IsZoomed(handle) || moved.Pixels != area) throw new Exception($"Maximize fitting failed: {moved.Pixels}, expected {area}");
                    Console.WriteLine($"PASS: {display.DeviceName} (primary={display.Primary}) window repositioning and maximize fitting (isolated fixture only)");
                }
                finally { if (!fixture.HasExited) { fixture.Kill(); fixture.WaitForExit(); } }
            }
            Console.WriteLine("PASS: WPF startup, all monitors listed, default bounds, HWND emergency pause, saved pause, disconnected-display guard");
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
    private sealed class FakeBlackout : IBlackoutMask
    {
        public bool Visible;
        public bool FailShow;
        public PixelRect Usable;
        public void Show(PixelRect display, PixelRect usable)
        {
            if (FailShow) throw new InvalidOperationException("Simulated mask failure");
            Visible = true; Usable = usable;
        }
        public void Hide() => Visible = false;
        public void Maintain() { }
        public void Dispose() => Hide();
    }
}
