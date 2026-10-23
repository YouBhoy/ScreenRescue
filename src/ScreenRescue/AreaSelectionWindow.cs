using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using ScreenRescue.Core;
using Forms = System.Windows.Forms;

namespace ScreenRescue;

internal sealed class AreaSelectionWindow : Window
{
    private readonly Canvas surface = new() { Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)) };
    private readonly Rectangle rectangle = new() { Fill = new SolidColorBrush(Color.FromArgb(90, 30, 200, 140)), Stroke = Brushes.White, StrokeThickness = 2, IsHitTestVisible = false };
    private BoundsDrag? drag;
    public UsableBounds? Selection { get; private set; }

    internal AreaSelectionWindow(Forms.Screen screen)
    {
        Title = "ScreenRescue — select usable area";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; Cursor = Cursors.Cross;
        var grid = new Grid(); grid.Children.Add(surface); surface.Children.Add(rectangle);
        grid.Children.Add(new TextBlock { Text = "Drag to select the usable area • Esc to cancel", Foreground = Brushes.White,
            Background = Brushes.Black, Padding = new Thickness(12), Margin = new Thickness(12),
            VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center,
            IsHitTestVisible = false });
        Content = grid;
        Loaded += (_, _) =>
        {
            var b = screen.Bounds;
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), b.X, b.Y, b.Width, b.Height, 0x0040);
            Activate(); Focus();
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
        surface.MouseLeftButtonDown += (_, e) =>
        {
            var p = Percent(e); drag = new BoundsDrag(new(), p.X, p.Y);
            surface.CaptureMouse(); Update(e); e.Handled = true;
        };
        surface.MouseMove += (_, e) => { if (drag is not null && e.LeftButton == MouseButtonState.Pressed) Update(e); };
        surface.MouseLeftButtonUp += (_, e) =>
        {
            if (drag is null) return;
            Update(e); drag = null; surface.ReleaseMouseCapture(); DialogResult = true;
        };
        surface.LostMouseCapture += (_, _) => drag = null;
    }
    private Point Percent(MouseEventArgs e)
    {
        var p = e.GetPosition(surface);
        return new(p.X / Math.Max(1, surface.ActualWidth) * 100, p.Y / Math.Max(1, surface.ActualHeight) * 100);
    }
    private void Update(MouseEventArgs e)
    {
        var p = Percent(e); Selection = drag!.Update(p.X, p.Y);
        Canvas.SetLeft(rectangle, Selection.Left * surface.ActualWidth / 100);
        Canvas.SetTop(rectangle, Selection.Top * surface.ActualHeight / 100);
        rectangle.Width = (Selection.Right - Selection.Left) * surface.ActualWidth / 100;
        rectangle.Height = (Selection.Bottom - Selection.Top) * surface.ActualHeight / 100;
    }
}
