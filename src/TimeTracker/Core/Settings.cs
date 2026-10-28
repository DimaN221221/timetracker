using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace TimeTracker.Core;

/// <summary>settings.json. Key names match the Python version, so existing settings keep working.</summary>
internal sealed class Settings
{
    public static readonly string[] DefaultExclude = ["explorer.exe"];

    [JsonPropertyName("exclude")]
    public List<string> Exclude { get; set; } = [.. DefaultExclude];

    [JsonPropertyName("idle_minutes")]
    public int IdleMinutes { get; set; } = 2;

    [JsonPropertyName("tray_hint_shown")]
    public bool TrayHintShown { get; set; }

    /// <summary>"system", "light" or "dark".</summary>
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "system";

    /// <summary>Check GitHub for a new version every few hours and download it in the background.</summary>
    [JsonPropertyName("auto_update")]
    public bool AutoUpdate { get; set; } = true;

    /// <summary>Unix seconds of the last update check.</summary>
    [JsonPropertyName("last_update_check")]
    public double LastUpdateCheck { get; set; }

    private HashSet<string>? _excludeSet;

    /// <summary>Snapshot used by the tracker thread; replaced as a whole whenever the list changes.</summary>
    [JsonIgnore]
    public IReadOnlySet<string> ExcludeSet => _excludeSet ??= new(Exclude, StringComparer.OrdinalIgnoreCase);

    public void SetExclude(IEnumerable<string> exes)
    {
        Exclude = exes.Select(e => e.Trim().ToLowerInvariant()).Where(e => e.Length > 0).Distinct().ToList();
        _excludeSet = new(Exclude, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static Settings Load()
    {
        try
        {
            var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Paths.Settings), Json);
            if (loaded is not null)
            {
                loaded.SetExclude(loaded.Exclude ?? []);
                loaded.IdleMinutes = Math.Clamp(loaded.IdleMinutes, 1, 120);
                if (loaded.Theme is not ("light" or "dark")) loaded.Theme = "system";
                return loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new Settings();
    }

    public void Save()
    {
        Paths.EnsureDataDir();
        File.WriteAllText(Paths.Settings, JsonSerializer.Serialize(this, Json));
    }
}
