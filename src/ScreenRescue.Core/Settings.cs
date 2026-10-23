using System.Text.Json;

namespace ScreenRescue.Core;

public sealed record Settings
{
    public string MonitorDevice { get; init; } = "";
    public UsableBounds Bounds { get; init; } = new();
    public bool Enabled { get; init; }
    public bool StartMinimized { get; init; }
    public bool LaunchAtLogin { get; init; }
    public bool BlackoutOutside { get; init; } = true;
}

public static class SettingsStore
{
    public static Settings Load(string path)
    {
        if (!File.Exists(path)) return new();
        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<Settings>(json)
            ?? throw new InvalidDataException("Settings file is empty.");
        if (settings.Bounds is null || !settings.Bounds.IsValid || settings.MonitorDevice is null)
            throw new InvalidDataException("Settings contain invalid monitor bounds.");
        // A removed desktop-view profile must never silently start window fitting.
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("DesktopMode", out var legacyMode)
            && legacyMode.ValueKind == JsonValueKind.True) settings = settings with { Enabled = false };
        return settings;
    }

    public static void Save(string path, Settings settings)
    {
        if (settings.Bounds is null || !settings.Bounds.IsValid) throw new ArgumentException("Invalid bounds.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
}
