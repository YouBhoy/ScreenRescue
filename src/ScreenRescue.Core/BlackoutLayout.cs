namespace ScreenRescue.Core;

public static class BlackoutLayout
{
    /// <summary>Partitions only the area outside a visible usable rectangle into solid masks.</summary>
    public static IReadOnlyList<PixelRect> Outside(PixelRect display, PixelRect usable)
    {
        if (display.Width <= 0 || display.Height <= 0) throw new ArgumentException("Display must be nonempty.");
        usable = usable.Intersect(display);
        // Never accidentally black out an entire display because of invalid coordinates.
        if (usable.Width <= 0 || usable.Height <= 0) throw new ArgumentException("Usable area must intersect the display.");
        var masks = new List<PixelRect>();
        Add(new(display.X, display.Y, display.Width, usable.Y - display.Y));
        Add(new(display.X, usable.Bottom, display.Width, display.Bottom - usable.Bottom));
        Add(new(display.X, usable.Y, usable.X - display.X, usable.Height));
        Add(new(usable.Right, usable.Y, display.Right - usable.Right, usable.Height));
        return masks;
        void Add(PixelRect rect) { if (rect.Width > 0 && rect.Height > 0) masks.Add(rect); }
    }
}
