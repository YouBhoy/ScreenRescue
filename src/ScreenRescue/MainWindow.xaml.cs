using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using ScreenRescue.Core;
using Forms = System.Windows.Forms;

namespace ScreenRescue;

public partial class MainWindow : Window
{
    private readonly string configPath;
    private readonly Action<bool> startupWriter;
    private readonly Func<Settings, string> sweep;
    private readonly IBlackoutMask blackout;
    private (PixelRect Display, PixelRect Usable)? maskLayout;
    private bool editingSelection;
    private readonly WindowRescuer rescuer = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private Settings settings = new();
    private Forms.NotifyIcon? tray;
    private HwndSource? source;
    private bool quitting;
    private bool hotkeyRegistered;
    private BoundsDrag? previewDrag;
    public bool StartHidden => settings.StartMinimized;
    public bool CanHide => tray?.Visible == true && hotkeyRegistered;
    private sealed record DisplayItem(string Device, string Label) { public override string ToString() => Label; }

    public MainWindow() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenRescue", "settings.json")) { }

    internal MainWindow(string path, Action<bool>? startupWriter = null, Func<Settings, string>? sweep = null,
        IBlackoutMask? blackout = null)
    {
        configPath = path;
        this.startupWriter = startupWriter ?? SetStartup;
        this.sweep = sweep ?? (s => rescuer.Sweep(s));
        this.blackout = blackout ?? new BlackoutMask();
        InitializeComponent();
        Title = "ScreenRescue " + typeof(MainWindow).Assembly.GetName().Version?.ToString(3);
        string? warning = null;
        try { settings = SettingsStore.Load(configPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        { warning = "Could not load settings; defaults are paused. " + ex.Message; }
        RefreshDisplays();
        LeftBox.Text = settings.Bounds.Left.ToString(CultureInfo.CurrentCulture);
        TopBox.Text = settings.Bounds.Top.ToString(CultureInfo.CurrentCulture);
        RightBox.Text = settings.Bounds.Right.ToString(CultureInfo.CurrentCulture);
        BottomBox.Text = settings.Bounds.Bottom.ToString(CultureInfo.CurrentCulture);
        StartupBox.IsChecked = settings.LaunchAtLogin;
        MinimizedBox.IsChecked = settings.StartMinimized;
        BlackoutBox.IsChecked = settings.BlackoutOutside;
        SourceInitialized += InitializeNative;
        Closing += OnClosing;
        timer.Tick += (_, _) =>
        {
            if (UpdateBlackout()) StatusText.Text = this.sweep(settings);
        };
        Loaded += (_, _) =>
        {
            if (warning is not null) MessageBox.Show(this, warning, "ScreenRescue settings");
            UpdateBlackout();
            timer.Start();
        };
    }

    private void InitializeNative(object? sender, EventArgs e)
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        source.AddHook(WindowMessage);
        hotkeyRegistered = Native.RegisterHotKey(source.Handle, 1, 0x4000 | 1 | 2 | 4, 0x7B);
        if (!hotkeyRegistered)
        {
            settings = settings with { Enabled = false };
            HotkeyText.Text = "Emergency shortcut unavailable (already registered). Close the conflicting app and restart ScreenRescue. Automatic fitting is disabled.";
        }
        try
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Open settings", null, (_, _) => Dispatcher.Invoke(OpenSettings));
            menu.Items.Add("Pause immediately", null, (_, _) => Dispatcher.Invoke(Pause));
            menu.Items.Add("Resume saved bounds", null, (_, _) => Dispatcher.Invoke(Resume));
            menu.Items.Add("Quit", null, (_, _) => Dispatcher.Invoke(Quit));
            tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "ScreenRescue", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenSettings);
        }
        catch (Exception ex)
        {
            tray?.Dispose(); tray = null;
            StatusText.Text = "Tray unavailable: " + ex.Message;
        }
    }

    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == 1) { Pause(); handled = true; }
        if (msg == 0x007E)
        {
            // Pause after topology changes rather than acting on stale monitor coordinates.
            ClearBlackout();
            settings = settings with { Enabled = false }; rescuer.Reset(); RefreshDisplays();
            ApplyNotice.Text = "Display layout changed. Window fitting paused; select a display and apply again.";
        }
        return IntPtr.Zero;
    }

    private void RefreshDisplays()
    {
        var selected = (MonitorBox.SelectedItem as DisplayItem)?.Device ?? settings.MonitorDevice;
        MonitorBox.Items.Clear();
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var item = new DisplayItem(screen.DeviceName, $"{screen.DeviceName} — {screen.Bounds.Width} × {screen.Bounds.Height} ({(screen.Primary ? "primary, " : "")}{(screen.Bounds.Height > screen.Bounds.Width ? "portrait" : "landscape")})");
            MonitorBox.Items.Add(item);
            if (item.Device == selected) MonitorBox.SelectedItem = item;
        }
        // First-run convenience only: never silently switch an existing saved target.
        if (string.IsNullOrEmpty(selected) && MonitorBox.Items.Count > 0)
        {
            var secondary = Array.Find(Forms.Screen.AllScreens, s => !s.Primary);
            foreach (DisplayItem item in MonitorBox.Items)
                if (item.Device == (secondary ?? Forms.Screen.PrimaryScreen)?.DeviceName) MonitorBox.SelectedItem = item;
        }
        UpdatePreview();
    }

    private bool TryBounds(out UsableBounds bounds)
    {
        bounds = new();
        if (!double.TryParse(LeftBox.Text, out var left) || !double.TryParse(TopBox.Text, out var top)
            || !double.TryParse(RightBox.Text, out var right) || !double.TryParse(BottomBox.Text, out var bottom)) return false;
        bounds = new(left, top, right, bottom);
        return bounds.IsValid;
    }
    private void UpdatePreview()
    {
        if (UsablePreview is null || LeftBox is null || TopBox is null || RightBox is null || BottomBox is null) return;
        if (!TryBounds(out var bounds)) { UsablePreview.Visibility = Visibility.Hidden; return; }
        var display = MonitorBox.SelectedItem as DisplayItem;
        var screen = Array.Find(Forms.Screen.AllScreens, s => s.DeviceName == display?.Device);
        var width = screen is null ? 240 : 140.0 * screen.Bounds.Width / screen.Bounds.Height;
        PreviewCanvas.Width = width;
        DisplayPreview.Width = width;
        UsablePreview.Visibility = Visibility.Visible;
        Canvas.SetLeft(UsablePreview, bounds.Left * width / 100);
        Canvas.SetTop(UsablePreview, bounds.Top * 1.4);
        UsablePreview.Width = (bounds.Right - bounds.Left) * width / 100;
        UsablePreview.Height = (bounds.Bottom - bounds.Top) * 1.4;
    }
    private void PreviewChanged(object sender, RoutedEventArgs e)
    {
        UpdatePreview();
    }
    private void SetBounds(UsableBounds bounds)
    {
        LeftBox.Text = bounds.Left.ToString("0.##", CultureInfo.CurrentCulture);
        TopBox.Text = bounds.Top.ToString("0.##", CultureInfo.CurrentCulture);
        RightBox.Text = bounds.Right.ToString("0.##", CultureInfo.CurrentCulture);
        BottomBox.Text = bounds.Bottom.ToString("0.##", CultureInfo.CurrentCulture);
    }
    private Point PreviewPercent(MouseEventArgs e)
    {
        var p = e.GetPosition(PreviewCanvas);
        return new(p.X / PreviewCanvas.Width * 100, p.Y / PreviewCanvas.Height * 100);
    }
    private void PreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (!TryBounds(out var bounds)) bounds = new();
        var p = PreviewPercent(e);
        previewDrag = new BoundsDrag(bounds, p.X, p.Y, 5);
        PreviewCanvas.CaptureMouse();
        e.Handled = true;
    }
    private void PreviewMove(object sender, MouseEventArgs e)
    {
        if (previewDrag is null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = PreviewPercent(e);
        SetBounds(previewDrag.Update(p.X, p.Y));
    }
    private void PreviewUp(object sender, MouseButtonEventArgs e)
    {
        if (previewDrag is not null)
        {
            var p = PreviewPercent(e);
            SetBounds(previewDrag.Update(p.X, p.Y));
        }
        previewDrag = null;
        PreviewCanvas.ReleaseMouseCapture();
    }
    private void PreviewCaptureLost(object sender, MouseEventArgs e) => previewDrag = null;
    private void SelectAreaClicked(object sender, RoutedEventArgs e)
    {
        var device = (MonitorBox.SelectedItem as DisplayItem)?.Device;
        var screen = Array.Find(Forms.Screen.AllScreens, s => s.DeviceName == device);
        if (screen is null) { MessageBox.Show(this, "Select a connected display first."); return; }
        var selector = new AreaSelectionWindow(screen) { Owner = this };
        editingSelection = true;
        ClearBlackout();
        timer.Stop();
        try
        {
            if (selector.ShowDialog() == true && selector.Selection is not null) SetBounds(selector.Selection);
        }
        finally { editingSelection = false; UpdateBlackout(); timer.Start(); }
    }
    private void RefreshClicked(object sender, RoutedEventArgs e) => RefreshDisplays();
    private void DefaultClicked(object sender, RoutedEventArgs e)
    { LeftBox.Text = "0"; TopBox.Text = "40"; RightBox.Text = "100"; BottomBox.Text = "100"; }

    private void ApplyClicked(object sender, RoutedEventArgs e)
    {
        if (MonitorBox.SelectedItem is not DisplayItem display || !TryBounds(out var bounds))
        { MessageBox.Show(this, "Select a connected display and enter bounds from 0–100, with at least 5% width and height."); return; }
        if (!hotkeyRegistered)
        { MessageBox.Show(this, "The emergency shortcut must be available before window fitting can start."); return; }
        var next = new Settings { MonitorDevice = display.Device, Bounds = bounds, Enabled = true,
            BlackoutOutside = BlackoutBox.IsChecked == true,
            LaunchAtLogin = StartupBox.IsChecked == true, StartMinimized = MinimizedBox.IsChecked == true };
        try
        {
            ClearBlackout();
            settings = settings with { Enabled = false };
            SettingsStore.Save(configPath, next with { Enabled = false });
            if (next.LaunchAtLogin != settings.LaunchAtLogin) startupWriter(next.LaunchAtLogin);
            SettingsStore.Save(configPath, next);
            settings = next;
            rescuer.Reset();
            if (!UpdateBlackout()) return;
            StatusText.Text = sweep(settings);
            ApplyNotice.Text = settings.BlackoutOutside ? "Saved — window fitting is on and the area outside your selection is blacked out."
                : "Saved — ordinary window fitting is on; blackout is off.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            ClearBlackout(); settings = settings with { Enabled = false };
            ApplyNotice.Text = "Could not apply settings. Window fitting is paused.";
            MessageBox.Show(this, "Settings could not be applied: " + ex.Message);
        }
    }
    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate executable.");
            if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Launch ScreenRescue.exe before enabling startup.");
            key.SetValue("ScreenRescue", "\"" + executable + "\"");
        }
        else key.DeleteValue("ScreenRescue", false);
    }

    private void Pause()
    {
        ClearBlackout();
        settings = settings with { Enabled = false };
        rescuer.Reset();
        ApplyNotice.Text = "Paused. Click Apply and save to start fitting again.";
        StatusText.Text = "Paused — blackout removed; windows are free to move.";
        try { SettingsStore.Save(configPath, settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { OpenSettings(); MessageBox.Show(this, "Paused for this session, but could not save the pause: " + ex.Message); }
    }
    private void Resume()
    {
        if (!hotkeyRegistered) { OpenSettings(); return; }
        settings = settings with { Enabled = true }; rescuer.Reset();
        if (!UpdateBlackout()) return;
        ApplyNotice.Text = "Window fitting resumed for this session.";
        StatusText.Text = sweep(settings);
        // Explicit Apply saves the enabled state for future launches.
    }
    private void PauseClicked(object sender, RoutedEventArgs e) => Pause();
    private void QuitClicked(object sender, RoutedEventArgs e) => Quit();
    private void HideClicked(object sender, RoutedEventArgs e)
    { if (CanHide) Hide(); else MessageBox.Show(this, "Keep settings open while the tray or emergency shortcut is unavailable."); }
    private void OpenSettings() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    { if (!quitting) { e.Cancel = true; if (CanHide) Hide(); } }
    private void Quit()
    {
        blackout.Dispose(); maskLayout = null;
        quitting = true; timer.Stop(); rescuer.Reset();
        if (source is not null) { Native.UnregisterHotKey(source.Handle, 1); source.RemoveHook(WindowMessage); }
        if (tray is not null) { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); }
        Close(); Application.Current.Shutdown();
    }
    private void ClearBlackout() { blackout.Hide(); maskLayout = null; }
    private bool UpdateBlackout()
    {
        try
        {
            var screen = Array.Find(Forms.Screen.AllScreens, s => s.DeviceName == settings.MonitorDevice);
            if (!settings.Enabled || !settings.BlackoutOutside || !hotkeyRegistered || editingSelection || screen is null)
            {
                if (maskLayout is not null) ClearBlackout();
                return true;
            }
            var b = screen.Bounds;
            var display = new PixelRect(b.X, b.Y, b.Width, b.Height);
            // Masks follow the full selected rectangle, not the taskbar-excluded work area.
            var usable = settings.Bounds.Resolve(display, display);
            var next = (display, usable);
            if (maskLayout != next) { blackout.Show(display, usable); maskLayout = next; }
            else blackout.Maintain();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
        {
            ClearBlackout(); settings = settings with { Enabled = false };
            try { SettingsStore.Save(configPath, settings); }
            catch (Exception saveError) when (saveError is IOException or UnauthorizedAccessException) { }
            ApplyNotice.Text = "Paused and removed blackout: " + ex.Message;
            StatusText.Text = "Blackout could not be shown. Nothing is masked.";
            return false;
        }
    }
}
