namespace ScreenRescue.Core;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public PixelRect Fit(PixelRect area, bool resize = true)
    {
        var width = resize ? Math.Min(Width, area.Width) : Width;
        var height = resize ? Math.Min(Height, area.Height) : Height;
        return new(Math.Clamp(X, area.X, Math.Max(area.X, area.Right - width)),
            Math.Clamp(Y, area.Y, Math.Max(area.Y, area.Bottom - height)), width, height);
    }
    public PixelRect Intersect(PixelRect other)
    {
        var x = Math.Max(X, other.X);
        var y = Math.Max(Y, other.Y);
        return new(x, y, Math.Max(0, Math.Min(Right, other.Right) - x), Math.Max(0, Math.Min(Bottom, other.Bottom) - y));
    }
}

public sealed record UsableBounds(double Left = 0, double Top = 40, double Right = 100, double Bottom = 100)
{
    public bool IsValid => double.IsFinite(Left) && double.IsFinite(Top) && double.IsFinite(Right) && double.IsFinite(Bottom)
        && Left >= 0 && Top >= 0 && Right <= 100 && Bottom <= 100 && Right - Left >= 5 && Bottom - Top >= 5;

    public PixelRect Resolve(PixelRect display, PixelRect workArea)
    {
        if (!IsValid) throw new ArgumentException("Bounds must be between 0 and 100 with at least 5% width and height.");
        var left = display.X + (int)Math.Round(display.Width * Left / 100);
        var top = display.Y + (int)Math.Round(display.Height * Top / 100);
        var right = display.X + (int)Math.Round(display.Width * Right / 100);
        var bottom = display.Y + (int)Math.Round(display.Height * Bottom / 100);
        return new PixelRect(left, top, right - left, bottom - top).Intersect(workArea);
    }
}
