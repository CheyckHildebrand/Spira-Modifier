namespace SpiraModifier.Core.Models;

/// <summary>
/// Constantes mécaniques du moteur de combat FFX.
///
/// Ces valeurs sont des limites HARD du moteur — modifier la structure binaire
/// au-delà de ces seuils plante le jeu. Elles sont définies ici une fois pour
/// toutes pour servir de référence à l'UI d'édition et aux validateurs futurs.
/// </summary>
public static class BattleSceneConstants
{
    /// <summary>Nombre maximum de slots de monstres par formation (limite dure du moteur).</summary>
    public const int MaxMonstersPerFormation = 8;

    /// <summary>
    /// Nombre maximum de monstres simultanément actifs sur le terrain.
    /// Au-delà, ils sont en réserve et apparaissent à la mort d'un slot actif
    /// (système de "spawn replacement" géré par le script ATEL de la scène).
    /// </summary>
    public const int MaxActiveMonstersOnField = 4;

    /// <summary>Nombre maximum de personnages joueurs en équipe active.</summary>
    public const int MaxPartyMembers = 3;

    /// <summary>Nombre maximum de Chimères invocables par combat.</summary>
    public const int MaxAeons = 7;

    /// <summary>Valeur indiquant un slot vide dans une formation.</summary>
    public const int EmptySlotMarker = 0xFFFF;

    /// <summary>Masque pour extraire l'ID du fichier monstre d'un ID de slot.</summary>
    /// <remarks>
    /// Source : FormationDataObject.writeMonster du parser de Karifean.
    /// Les bits hauts (0x1000+) sont des flags (statut initial, visibilité, etc.)
    /// que le script ATEL de la scène applique au monstre lors du spawn.
    /// </remarks>
    public const int MonsterFileIdMask = 0x0FFF;

    /// <summary>
    /// Récupère le numéro de fichier monstre (m###.bin) à partir d'un ID de slot.
    /// Retourne null si le slot est vide.
    /// </summary>
    public static int? GetMonsterFileNumber(int slotId)
    {
        if (slotId == EmptySlotMarker) return null;
        return slotId & MonsterFileIdMask;
    }

    /// <summary>Récupère les flags hauts d'un ID de slot (status/visibility/etc).</summary>
    public static int GetSlotFlags(int slotId) => (slotId >> 12) & 0xF;

    /// <summary>Format standard d'un fichier monstre : "m###.bin".</summary>
    public static string FormatMonsterFileName(int monsterNumber) => $"m{monsterNumber:000}";
}
