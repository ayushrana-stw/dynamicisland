using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamicIsland.Core.Settings;

/// <summary>Loads and saves <see cref="IslandSettings"/> as JSON in the user's local app-data folder.</summary>
public sealed class SettingsService
{
    private readonly string _path;

    public SettingsService(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DynamicIsland",
            "settings.json");
    }

    public IslandSettings Current { get; private set; } = IslandSettings.Default;

    /// <summary>Raised on the calling thread after <see cref="Current"/> changes.</summary>
    public event EventHandler<IslandSettings>? Changed;

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                using var stream = File.OpenRead(_path);
                Current = JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.IslandSettings) ?? IslandSettings.Default;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable file should never stop the app from starting.
            Current = IslandSettings.Default;
        }
    }

    public void Update(IslandSettings settings)
    {
        if (settings == Current)
            return;

        Current = settings;
        Save();
        Changed?.Invoke(this, settings);
    }

    public void RestoreDefaults() => Update(IslandSettings.Default);

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            using (var stream = File.Create(temp))
                JsonSerializer.Serialize(stream, Current, SettingsJsonContext.Default.IslandSettings);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings stay in memory; the next successful save will persist them.
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(IslandSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
