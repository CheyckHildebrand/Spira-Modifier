namespace SpiraModifier.Core.Models;

/// <summary>
/// Définitions des bitfields de statuts et d'éléments, extraites de
/// FFXDataParser/src/main/resources/enums/bitfields.csv.
/// </summary>
public static class FfxStatusFlags
{
    /// <summary>Statuts permanents (offset 0x48 dans MonsterStat, 2 bytes).</summary>
    public static readonly (int Mask, string Label)[] Permanent =
    {
        (0x0001, "Mort"),
        (0x0002, "Zombie"),
        (0x0004, "Pétrification"),
        (0x0008, "Poison"),
        (0x0010, "Power Break"),
        (0x0020, "Magic Break"),
        (0x0040, "Armor Break"),
        (0x0080, "Mental Break"),
        (0x0100, "Confusion"),
        (0x0200, "Berserk"),
        (0x0400, "Provoque"),
        (0x0800, "Menace"),
    };

    /// <summary>Statuts temporels (offset 0x4A, 2 bytes).</summary>
    public static readonly (int Mask, string Label)[] Temporal =
    {
        (0x0001, "Sommeil"),
        (0x0002, "Silence"),
        (0x0004, "Obscurité"),
        (0x0008, "Carapace"),
        (0x0010, "Bouclier"),
        (0x0020, "Reflet"),
        (0x0040, "NulMaree"),
        (0x0080, "NulFlamme"),
        (0x0100, "NulChoc"),
        (0x0200, "NulFrimas"),
        (0x0400, "Régen"),
        (0x0800, "Hâte"),
        (0x1000, "Lenteur"),
    };

    /// <summary>Statuts extras (offset 0x4C, 2 bytes).</summary>
    public static readonly (int Mask, string Label)[] Extra =
    {
        (0x0001, "Scan"),
        (0x0002, "Distill Power"),
        (0x0004, "Distill Mana"),
        (0x0008, "Distill Speed"),
        (0x0010, "Inutilisé 1"),
        (0x0020, "Distill Ability"),
        (0x0040, "Shield"),
        (0x0080, "Boost"),
        (0x0100, "Eject"),
        (0x0200, "Auto-Vie"),
        (0x0400, "Curse"),
        (0x0800, "Defend"),
        (0x1000, "Guard"),
        (0x2000, "Sentinel"),
        (0x4000, "Doom"),
        (0x8000, "Inutilisé 2"),
    };

    /// <summary>Bitfield des affinités élémentaires (1 byte chacun pour Absorb/Immune/Resist/Weak).</summary>
    public static readonly (int Mask, string Label)[] Elements =
    {
        (0x01, "Feu"),
        (0x02, "Glace"),
        (0x04, "Foudre"),
        (0x08, "Eau"),
        (0x10, "Saint"),
    };

    /// <summary>Retourne la liste des labels actifs dans un bitfield donné.</summary>
    public static List<string> GetActiveLabels(int bitfield, (int Mask, string Label)[] definitions)
    {
        var result = new List<string>();
        foreach (var (mask, label) in definitions)
        {
            if ((bitfield & mask) != 0)
                result.Add(label);
        }
        return result;
    }

    /// <summary>Vérifie si un bit spécifique est actif dans un bitfield.</summary>
    public static bool IsSet(int bitfield, int mask) => (bitfield & mask) != 0;
}
