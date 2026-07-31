using System.Text.Json;

namespace SpiraModifier.Core.Settings;

/// <summary>
/// Service de chargement/sauvegarde des AppSettings dans %AppData%.
/// </summary>
public static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string SettingsFolderPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SpiraModifier");

    public static string SettingsFilePath => Path.Combine(SettingsFolderPath, "settings.json");

    /// <summary>Charge les settings, ou retourne des settings par défaut si le fichier n'existe pas / est corrompu.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
                return new AppSettings();
            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsFolderPath);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch
        {
            // Erreur d'écriture (disque plein, permissions, etc.) — on ignore
        }
    }
}
