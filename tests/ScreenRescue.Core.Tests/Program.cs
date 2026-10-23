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
Equal(new PixelRect(0, 0, 10, 0), new PixelRect(0, 0, 10, 10).Intersect(new(0, -20, 10, 10)), "Empty intersection");
var folder = Path.Combine(Path.GetTempPath(), "ScreenRescue-tests-" + Guid.NewGuid());
var path = Path.Combine(folder, "settings.json");
try
{
    Equal(new Settings(), SettingsStore.Load(path), "Missing config defaults paused");
    var settings = new Settings { MonitorDevice = @"\\.\DISPLAY2", Enabled = true, Bounds = new(5, 45, 95, 99) };
    SettingsStore.Save(path, settings);
    Equal(settings, SettingsStore.Load(path), "Config roundtrip");
    SettingsStore.Save(path, settings with { Enabled = false });
    Equal(false, SettingsStore.Load(path).Enabled, "Atomic overwrite");
    File.WriteAllText(path, "{\"Bounds\":null}");
    try { SettingsStore.Load(path); throw new Exception("Invalid config accepted"); }
    catch (InvalidDataException) { checks++; }
}
finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
Console.WriteLine($"PASS: {checks} checks");
