using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using ScreenRescue.Core;

namespace ScreenRescue;

internal sealed class WindowRescuer
{
    // Polling avoids injecting code into other apps. Never fight an active mouse drag.
    private readonly Dictionary<IntPtr, DateTime> attempts = new();
    private readonly Dictionary<IntPtr, DateTime> restoring = new();
    public string Sweep(Settings settings, IntPtr onlyWindow = default)
    {
        Screen? target = null;
        foreach (var screen in Screen.AllScreens)
            if (!screen.Primary && screen.DeviceName == settings.MonitorDevice) target = screen;
        if (target is null) return "Paused: selected secondary display is disconnected or is now primary.";
        if (!settings.Enabled) return "Paused — windows are free to move.";
        var display = From(target.Bounds);
        var area = settings.Bounds.Resolve(display, From(target.WorkingArea));
        if (area.Width < 64 || area.Height < 64) return "Paused: usable area is too small after excluding the taskbar.";
        if ((Native.GetAsyncKeyState(1) & 0x8000) != 0) return "Active — waiting for mouse drag to finish.";
        var alive = new HashSet<IntPtr>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (onlyWindow != IntPtr.Zero && hwnd != onlyWindow) return true;
            alive.Add(hwnd);
            if (!Eligible(hwnd) || Screen.FromHandle(hwnd).DeviceName != target.DeviceName) return true;
            if (!Native.GetWindowRect(hwnd, out var rect)) return true;
            var current = rect.Pixels;
            var now = DateTime.UtcNow;
            if (restoring.TryGetValue(hwnd, out var started))
            {
                if ((now - started).TotalSeconds > 3) restoring.Remove(hwnd);
                else if (!Native.IsZoomed(hwnd))
                {
                    Move(hwnd, area);
                    restoring.Remove(hwnd);
                }
                return true;
            }
            // Limit retries for apps enforcing minimum sizes or rejecting movement.
            if (attempts.TryGetValue(hwnd, out var last) && (now - last).TotalSeconds < 2) return true;
            if (Native.IsZoomed(hwnd))
            {
                attempts[hwnd] = now;
                restoring[hwnd] = now;
                Native.ShowWindowAsync(hwnd, 9); // SW_RESTORE; fit on a later sweep.
            }
            else
            {
                bool resizable = (Native.GetWindowLong(hwnd, -16) & 0x00040000) != 0;
                var desired = current.Fit(area, resizable);
                if (desired != current)
                {
                    attempts[hwnd] = now;
                    Move(hwnd, desired);
                }
            }
            return true;
        }, IntPtr.Zero);
        foreach (var hwnd in new List<IntPtr>(attempts.Keys))
            if (!alive.Contains(hwnd)) attempts.Remove(hwnd);
        foreach (var hwnd in new List<IntPtr>(restoring.Keys))
            if (!alive.Contains(hwnd)) restoring.Remove(hwnd);
        return $"Active — {area.Width} × {area.Height} usable pixels on {target.DeviceName}.";
    }

    public void Reset() { restoring.Clear(); attempts.Clear(); }
    private static void Move(IntPtr hwnd, PixelRect rect) => Native.SetWindowPos(hwnd, IntPtr.Zero,
        rect.X, rect.Y, rect.Width, rect.Height, 0x0004 | 0x0010 | 0x4000); // no Z order, no activation, async

    private static bool Eligible(IntPtr hwnd)
    {
        if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd) || Native.GetWindow(hwnd, 4) != IntPtr.Zero) return false;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return false;
        var style = Native.GetWindowLong(hwnd, -16);
        var ex = Native.GetWindowLong(hwnd, -20);
        if ((style & 0x00C00000) != 0x00C00000 || (style & 0x40000000) != 0 || (ex & 0x80) != 0) return false;
        if (Native.DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        var name = new StringBuilder(256);
        Native.GetClassName(hwnd, name, name.Capacity);
        return name.ToString() is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW");
    }
    private static PixelRect From(System.Drawing.Rectangle rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}
