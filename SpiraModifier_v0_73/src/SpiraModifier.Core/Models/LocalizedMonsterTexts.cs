namespace SpiraModifier.Core.Models;

/// <summary>
/// Conteneur des textes localisés d'un monstre dans une langue donnée.
/// Tous les champs peuvent être null/vides si non disponibles.
/// </summary>
public class LocalizedMonsterTexts
{
    public string? Name { get; set; }
    public string? SensorText { get; set; }
    public string? SimplifiedSensorText { get; set; }
    public string? ScanText { get; set; }
    public string? SimplifiedScanText { get; set; }

    /// <summary>True si au moins un champ a été décodé avec succès.</summary>
    public bool HasAnyContent =>
        !string.IsNullOrWhiteSpace(Name) ||
        !string.IsNullOrWhiteSpace(SensorText) ||
        !string.IsNullOrWhiteSpace(ScanText);
}
