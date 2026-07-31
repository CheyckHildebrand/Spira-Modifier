namespace SpiraModifier.Core.Settings;

/// <summary>
/// Paramètres persistants de l'application. Sauvegardés dans
/// %AppData%/SpiraModifier/settings.json entre les sessions.
/// </summary>
public class AppSettings
{
    /// <summary>Langue de l'interface de Spira Modifier ("fr" ou "en").</summary>
    public string InterfaceLanguage { get; set; } = "fr";

    /// <summary>
    /// Dossier ffx_encoding/ d'une copie vanilla du jeu, utilisé comme fallback
    /// quand le dossier ouvert ne contient pas ses propres charsets (cas typique
    /// d'un hardmod qui ne touche que certains fichiers).
    /// </summary>
    public string? ExternalEncodingFolder { get; set; }

    /// <summary>
    /// Dossier racine d'une copie vanilla complète, utilisé comme fallback global
    /// pour les fichiers manquants (charsets, monsterN.bin, etc.).
    /// Si défini, on cherche les fichiers manquants dans ce dossier en plus
    /// du dossier ouvert.
    /// </summary>
    public string? VanillaReferenceFolder { get; set; }

    /// <summary>
    /// Dossier de sortie où sont écrites les modifications. La sauvegarde
    /// reproduit l'arborescence du dossier ouvert (ex : workspace/battle/mon/m155.bin
    /// → outputFolder/battle/mon/m155.bin). Cela permet de copier ce dossier
    /// par-dessus l'install du jeu pour appliquer le mod, sans toucher aux originaux.
    ///
    /// Persisté entre sessions pour que Ctrl+S réutilise le même dossier.
    /// Si null, le premier Save demande à l'utilisateur de le choisir.
    /// </summary>
    public string? OutputFolder { get; set; }

    /// <summary>
    /// Active le mode LLM du copilote ATEL. La cle API n'est volontairement pas
    /// sauvegardee ici.
    /// </summary>
    public bool AtelCopilotUseLlm { get; set; }

    /// <summary>Endpoint compatible chat completions, local ou cloud.</summary>
    public string? AtelCopilotEndpoint { get; set; } = "http://localhost:1234/v1/chat/completions";

    /// <summary>Nom du modele a appeler via l'endpoint configure.</summary>
    public string? AtelCopilotModel { get; set; }
}
