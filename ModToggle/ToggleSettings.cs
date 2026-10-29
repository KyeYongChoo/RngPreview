using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace ModToggle;

public sealed class ToggleConfig
{
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// The mod's on/off switch. Takes effect immediately; the mod's patches check <see cref="Enabled"/> each time.
/// Saved to the game's user folder, not under mods/ (the game reads every .json there as a mod manifest).
/// </summary>
public static class ToggleSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static ToggleConfig _current = new();

    private static string FilePath => Path.Combine(OS.GetUserDataDir(), ModInfo.Id + ".settings.json");

    public static bool Enabled
    {
        get => _current.Enabled;
        set
        {
            _current.Enabled = value;
            Save();
        }
    }

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                _current = JsonSerializer.Deserialize<ToggleConfig>(File.ReadAllText(FilePath), JsonOptions) ?? new ToggleConfig();
            }
        }
        catch (Exception e)
        {
            Log.Error($"{ModInfo.LogPrefix} Failed to read settings, using defaults: {e.Message}");
            _current = new ToggleConfig();
        }
    }

    private static void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_current, JsonOptions));
        }
        catch (Exception e)
        {
            Log.Error($"{ModInfo.LogPrefix} Failed to save settings: {e.Message}");
        }
    }
}
