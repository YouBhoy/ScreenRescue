using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenRescue.Core;

namespace ScreenRescue;

internal interface IBlackoutMask : IDisposable
{
    void Show(PixelRect display, PixelRect usable);
    void Hide();
    void Maintain();
}

internal sealed class BlackoutMask : IBlackoutMask
{
    private readonly List<MaskWindow> windows = new();
    public void Show(PixelRect display, PixelRect usable)
    {
        Hide();
        try
        {
            foreach (var rect in BlackoutLayout.Outside(display, usable))
            {
                var window = new MaskWindow(rect);
                windows.Add(window);
                window.Show();
                window.Place();
            }
        }
        catch { Hide(); throw; }
    }
    public void Hide()
    {
        foreach (var window in windows) window.Close();
        windows.Clear();
    }
    public void Maintain() { foreach (var window in windows) window.Place(); }
    public void Dispose() => Hide();

    // Separate opaque windows leave NO window at all over the usable area. Normal
    // input there goes directly to the user's apps, without a mouse hook/remapping.
    private sealed class MaskWindow : Window
    {
        private readonly PixelRect rect;
        private IntPtr hwnd;
        internal MaskWindow(PixelRect rect)
        {
            this.rect = rect;
            Title = "ScreenRescue blackout";
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false; ShowActivated = false; Focusable = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = Brushes.Black; Topmost = true;
            // Avoid a normal-sized frame being shown before native pixel placement.
            Width = 1; Height = 1;
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            hwnd = new WindowInteropHelper(this).Handle;
            var style = Native.GetWindowLong(hwnd, -20);
            Native.SetWindowLong(hwnd, -20, style | 0x08000000 | 0x80); // NOACTIVATE | TOOLWINDOW
            HwndSource.FromHwnd(hwnd).AddHook(Message);
            Place();
        }
        internal void Place()
        {
            if (hwnd == IntPtr.Zero) return;
            // Do not activate or change focus. Use physical coordinates, including
            // negative origins and monitors with different DPI scaling.
            if (!Native.SetWindowPos(hwnd, new IntPtr(-1), rect.X, rect.Y, rect.Width, rect.Height, 0x0010 | 0x0040))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not position blackout mask.");
        }
        private IntPtr Message(IntPtr handle, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0021) { handled = true; return new IntPtr(4); } // MA_NOACTIVATEANDEAT
            return IntPtr.Zero;
        }
    }
}
