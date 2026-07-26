using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitHelper.App.Settings;

public sealed class JsonSettingsStore(string filePath) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GitHelper",
        "settings.json");

    /// <summary>Shape on disk. Nullable so a partially-written file still loads.</summary>
    private sealed class Dto
    {
        public List<string>? RecentRepositories { get; set; }
        public List<string>? SuppressedExplanations { get; set; }
        public AppTheme? Theme { get; set; }
    }

    public AppSettings Load()
    {
        if (!File.Exists(filePath)) return AppSettings.Default;

        Dto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(filePath), Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable settings file must never stop the app from starting.
            return AppSettings.Default;
        }

        if (dto is null) return AppSettings.Default;

        return new AppSettings(
            RecentRepositories: dto.RecentRepositories?.ToArray() ?? Array.Empty<string>(),
            SuppressedExplanations: new HashSet<string>(
                dto.SuppressedExplanations ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase),
            Theme: dto.Theme ?? AppTheme.System);
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var dto = new Dto
        {
            RecentRepositories = settings.RecentRepositories.ToList(),
            SuppressedExplanations = settings.SuppressedExplanations.ToList(),
            Theme = settings.Theme,
        };

        File.WriteAllText(filePath, JsonSerializer.Serialize(dto, Options));
    }
}
