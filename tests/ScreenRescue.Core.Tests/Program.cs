using ScreenRescue.Core;

int checks = 0;
void Equal<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{name}: expected {expected}, got {actual}");
    checks++;
}
var portrait = new PixelRect(-1080, -200, 1080, 1920);
var work = new PixelRect(-1080, -200, 1080, 1880);
var area = new UsableBounds().Resolve(portrait, work);
Equal(new PixelRect(-1080, 568, 1080, 1112), area, "Bottom 60%, negative monitor origin, taskbar");
Equal(new PixelRect(-1080, 568, 1080, 1112), new PixelRect(-1200, 0, 1600, 1300).Fit(area), "Oversized window");
Equal(new PixelRect(-400, 1280, 400, 400), new PixelRect(-100, 1500, 400, 400).Fit(area), "Right/bottom clamping");
Equal(new PixelRect(-1000, 600, 300, 200), new PixelRect(-1000, 600, 300, 200).Fit(area), "Inside area unchanged");
Equal(new PixelRect(-1080, 568, 1600, 1300), new PixelRect(-1200, 0, 1600, 1300).Fit(area, false), "Fixed-size window keeps size");
Equal(false, new UsableBounds(80, 40, 20, 100).IsValid, "Inverted bounds");
Equal(false, new UsableBounds(double.NaN).IsValid, "NaN rejected");
Equal(false, new UsableBounds(0, 99, 100, 100).IsValid, "Tiny area rejected");
Equal(false, new UsableBounds(0, 0, double.PositiveInfinity, 100).IsValid, "Infinity rejected");
Equal(new UsableBounds(10, 20, 80, 90), new BoundsDrag(new(), 80, 90).Update(10, 20), "Draw in reverse direction");
Equal(new UsableBounds(0, 0, 100, 100), new BoundsDrag(new(), 0, 0).Update(150, 120), "Drawing clamps to display");
Equal(new UsableBounds(95, 95, 100, 100), new BoundsDrag(new(), 100, 100).Update(100, 100), "Minimum selection at boundary");
Equal(new UsableBounds(60, 60, 100, 100), new BoundsDrag(new(10, 20, 50, 60), 30, 40, 3).Update(100, 100), "Moving clamps without shrinking");
Equal(new UsableBounds(0, 0, 40, 40), new BoundsDrag(new(10, 20, 50, 60), 30, 40, 3).Update(0, 0), "Moving to top left");
Equal(new UsableBounds(45, 55, 50, 60), new BoundsDrag(new(10, 20, 50, 60), 10, 20, 3).Update(80, 90), "Corner resize keeps minimum area");
Equal(new UsableBounds(10, 20, 90, 60), new BoundsDrag(new(10, 20, 50, 60), 50, 40, 3).Update(90, 70), "Right edge resize");
Equal(new PixelRect(0, 0, 10, 0), new PixelRect(0, 0, 10, 10).Intersect(new(0, -20, 10, 10)), "Empty intersection");
var physicalUsable = new UsableBounds().Resolve(portrait, portrait);
var defaultMasks = BlackoutLayout.Outside(portrait, physicalUsable);
Equal(1, defaultMasks.Count, "Bottom 60% requires one opaque top mask");
Equal(new PixelRect(-1080, -200, 1080, 768), defaultMasks[0], "Mask uses monitor origin and only covers top 40%");
Equal(0, BlackoutLayout.Outside(portrait, portrait).Count, "Full display selection creates no mask");
void CheckMasks(PixelRect display, PixelRect usable, string name)
{
    var masks = BlackoutLayout.Outside(display, usable);
    Equal((long)display.Width * display.Height - (long)usable.Width * usable.Height,
        masks.Sum(m => (long)m.Width * m.Height), name + " covers exactly unused pixels");
    for (var i = 0; i < masks.Count; i++)
    {
        Equal(masks[i], masks[i].Intersect(display), name + " stays on target monitor");
        var intersection = masks[i].Intersect(usable);
        Equal(0L, (long)intersection.Width * intersection.Height, name + " leaves usable pixels uncovered");
        for (var j = i + 1; j < masks.Count; j++)
        {
            intersection = masks[i].Intersect(masks[j]);
            Equal(0L, (long)intersection.Width * intersection.Height, name + " masks do not overlap");
        }
    }
}
CheckMasks(portrait, physicalUsable, "Portrait bottom area");
CheckMasks(new(-1920, -1080, 1920, 1080), new(-1800, -1000, 1700, 900), "All four margins with negative origin");
CheckMasks(new(1920, 0, 1080, 1920), new(1920, 0, 900, 1920), "Right margin only");
try { BlackoutLayout.Outside(portrait, new(10000, 10000, 100, 100)); throw new Exception("Empty usable area accepted"); }
catch (ArgumentException) { checks++; }
var folder = Path.Combine(Path.GetTempPath(), "ScreenRescue-tests-" + Guid.NewGuid());
var path = Path.Combine(folder, "settings.json");
try
{
    Equal(new Settings(), SettingsStore.Load(path), "Missing config defaults paused");
    var settings = new Settings { MonitorDevice = @"\\.\DISPLAY2", Enabled = true, Bounds = new(5, 45, 95, 99) };
    SettingsStore.Save(path, settings);
    Equal(settings, SettingsStore.Load(path), "Config roundtrip");
    SettingsStore.Save(path, settings with { BlackoutOutside = false });
    Equal(false, SettingsStore.Load(path).BlackoutOutside, "Blackout option persists off");
    SettingsStore.Save(path, settings with { Enabled = false });
    Equal(false, SettingsStore.Load(path).Enabled, "Atomic overwrite");
    File.WriteAllText(path, "{\"Enabled\":true,\"DesktopMode\":true,\"SourceMonitor\":\"old-source\"}");
    Equal(false, SettingsStore.Load(path).Enabled, "Removed desktop mode migrates paused");
    SettingsStore.Save(path, SettingsStore.Load(path));
    Equal(false, File.ReadAllText(path).Contains("DesktopMode"), "Removed desktop fields are not saved again");
    File.WriteAllText(path, "{\"Bounds\":null}");
    try { SettingsStore.Load(path); throw new Exception("Invalid config accepted"); }
    catch (InvalidDataException) { checks++; }
}
finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
Console.WriteLine($"PASS: {checks} checks");
