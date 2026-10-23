namespace ScreenRescue.Core;

/// <summary>Percentage-based drawing, moving and edge/corner resizing, independent of DPI.</summary>
public sealed class BoundsDrag
{
    private readonly UsableBounds initial;
    private readonly double startX, startY;
    private readonly bool left, right, top, bottom, move;
    public BoundsDrag(UsableBounds initial, double x, double y, double edgeTolerance = 0)
    {
        this.initial = initial;
        startX = Math.Clamp(x, 0, 100); startY = Math.Clamp(y, 0, 100);
        if (edgeTolerance <= 0 || x < initial.Left - edgeTolerance || x > initial.Right + edgeTolerance
            || y < initial.Top - edgeTolerance || y > initial.Bottom + edgeTolerance) return;
        left = Math.Abs(x - initial.Left) <= edgeTolerance;
        right = !left && Math.Abs(x - initial.Right) <= edgeTolerance;
        top = Math.Abs(y - initial.Top) <= edgeTolerance;
        bottom = !top && Math.Abs(y - initial.Bottom) <= edgeTolerance;
        move = !left && !right && !top && !bottom;
    }
    public UsableBounds Update(double x, double y)
    {
        x = Math.Clamp(x, 0, 100); y = Math.Clamp(y, 0, 100);
        if (move)
        {
            var dx = Math.Clamp(x - startX, -initial.Left, 100 - initial.Right);
            var dy = Math.Clamp(y - startY, -initial.Top, 100 - initial.Bottom);
            return new(initial.Left + dx, initial.Top + dy, initial.Right + dx, initial.Bottom + dy);
        }
        if (left || right || top || bottom)
            return new(left ? Math.Clamp(x, 0, initial.Right - 5) : initial.Left,
                top ? Math.Clamp(y, 0, initial.Bottom - 5) : initial.Top,
                right ? Math.Clamp(x, initial.Left + 5, 100) : initial.Right,
                bottom ? Math.Clamp(y, initial.Top + 5, 100) : initial.Bottom);
        var l = Math.Min(startX, x); var t = Math.Min(startY, y);
        var r = Math.Max(startX, x); var b = Math.Max(startY, y);
        l = Math.Min(l, 95); t = Math.Min(t, 95);
        return new(l, t, Math.Max(r, l + 5), Math.Max(b, t + 5));
    }
}
