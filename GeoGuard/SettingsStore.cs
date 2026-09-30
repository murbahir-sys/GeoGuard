using System.Text.Json;

namespace GeoGuard;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DirectoryPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GeoGuard");

    public static string FilePath { get; } = Path.Combine(DirectoryPath, "settings.json");

    public static bool Exists => File.Exists(FilePath);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                using var stream = File.OpenRead(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(stream, Options);
                if (loaded is not null)
                    return loaded.Normalize();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Повреждённый или недоступный файл — работаем с настройками по умолчанию.
        }

        return new AppSettings().Normalize();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
