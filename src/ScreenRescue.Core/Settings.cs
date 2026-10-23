using System.Text.Json;

namespace ScreenRescue.Core;

public sealed record Settings
{
    public string MonitorDevice { get; init; } = "";
    public UsableBounds Bounds { get; init; } = new();
    public bool Enabled { get; init; }
    public bool StartMinimized { get; init; }
    public bool LaunchAtLogin { get; init; }
}

public static class SettingsStore
{
    public static Settings Load(string path)
    {
        if (!File.Exists(path)) return new();
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Settings file is empty.");
        if (settings.Bounds is null || !settings.Bounds.IsValid || settings.MonitorDevice is null)
            throw new InvalidDataException("Settings contain invalid monitor bounds.");
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
