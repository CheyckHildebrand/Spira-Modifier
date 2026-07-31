namespace SpiraModifier.Core.Models;

/// <summary>
/// Définitions de bitfields spécifiques aux attaques (CommandDataObject).
/// Complète FfxStatusFlags pour les champs particuliers aux attaques.
/// </summary>
public static class FfxAttackFlags
{
    /// <summary>Targeting flags (offset 0x1A) — décrit comment l'attaque cible.</summary>
    public static readonly (int Mask, string Label)[] Targeting =
    {
        (0x01, "Cible activable"),
        (0x02, "Cible ennemis"),
        (0x04, "Cible multiple"),
        (0x08, "Soi-même uniquement"),
        (0x10, "Choix dans équipe"),
        (0x20, "Soit équipe"),
        (0x40, "Cibles morts"),
        (0x80, "Longue portée"),
    };

    /// <summary>Damage properties (offset 0x20) — type de dégâts et flags.</summary>
    public static readonly (int Mask, string Label)[] DamageProperties =
    {
        (0x01, "Physique"),
        (0x02, "Magique"),
        (0x04, "Crit possible"),
        (0x08, "Bonus crit (gear)"),
        (0x10, "Soin"),
        (0x20, "Nettoie statuts"),
        (0x40, "Supprime BDL"),
        (0x80, "Casse limite (BDL)"),
    };

    /// <summary>Damage class (offset 0x23) — sur quoi tape l'attaque.</summary>
    public static readonly (int Mask, string Label)[] DamageClass =
    {
        (0x01, "HP"),
        (0x02, "MP"),
        (0x04, "CTB (délai)"),
        (0x08, "Inconnu"),
    };

    /// <summary>Misc properties 0x1C — disponibilité, affichage et calcul de hit.</summary>
    public static readonly (int Mask, string Label)[] MiscProperties1C =
    {
        (0x01, "Utilisable hors combat"),
        (0x02, "Utilisable en combat"),
        (0x04, "Affiche le nom"),
        (0x08, "Hit calc bit 0"),
        (0x10, "Hit calc bit 1"),
        (0x20, "Hit calc bit 2"),
        (0x40, "Affecté par Obscurité"),
        (0x80, "Réfléchissable"),
    };

    /// <summary>Misc properties 0x1D — menus, vol, absorption, délais CTB.</summary>
    public static readonly (int Mask, string Label)[] MiscProperties1D =
    {
        (0x01, "Absorbe dégâts"),
        (0x02, "Vole objet"),
        (0x04, "Menu Utiliser"),
        (0x08, "Menu droit"),
        (0x10, "Menu gauche"),
        (0x20, "Délai"),
        (0x40, "Délai +"),
        (0x80, "Cibles aléatoires"),
    };

    /// <summary>Misc properties 0x1E — contraintes d'exécution de la commande.</summary>
    public static readonly (int Mask, string Label)[] MiscProperties1E =
    {
        (0x01, "Perce armure"),
        (0x02, "Bloqué par Silence"),
        (0x04, "Utilise propriétés arme"),
        (0x08, "Commande trigger"),
        (0x10, "Anim cast palier 1"),
        (0x20, "Anim cast palier 3"),
        (0x40, "Détruit le lanceur"),
        (0x80, "Rate si vivant"),
    };

    /// <summary>Animation properties 0x1F — Overdrive, Copycat et flags d'animation.</summary>
    public static readonly (int Mask, string Label)[] AnimationProperties1F =
    {
        (0x01, "Charge OD Warrior/Healer"),
        (0x02, "Vide barre OD"),
        (0x04, "Aura de sort"),
        (0x08, "Sort de l'écran"),
        (0x10, "Copiable"),
        (0x20, "Inconnu 0x20"),
        (0x40, "Overdrive Chimère"),
        (0x80, "Pots-de-vin"),
    };

    /// <summary>Extra status (offset 0x54, 2 bytes) — Scan, Boost, Distill, Doom, etc.</summary>
    public static readonly (int Mask, string Label)[] ExtraStatus =
    {
        (0x0001, "Scan"),
        (0x0002, "Distill Power"),
        (0x0004, "Distill Mana"),
        (0x0008, "Distill Speed"),
        (0x0010, "Inutilisé"),
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

    /// <summary>Stat buffs (offset 0x56, 2 bytes).</summary>
    public static readonly (int Mask, string Label)[] StatBuffs =
    {
        (0x0001, "Encourager (Force + Défense)"),
        (0x0002, "Viser (Précision)"),
        (0x0004, "Concentration (Magie + Déf. mag.)"),
        (0x0008, "Réflexes (Esquive)"),
        (0x0010, "Chance (Chance +)"),
        (0x0020, "Malchance (Chance -)"),
        (0x0040, "Inutilisé 0x40"),
        (0x0080, "Inutilisé 0x80"),
        (0x0100, "Inutilisé 0x0100"),
        (0x0200, "Inutilisé 0x0200"),
        (0x0400, "Inutilisé 0x0400"),
        (0x0800, "Inutilisé 0x0800"),
        (0x1000, "Inutilisé 0x1000"),
        (0x2000, "Inutilisé 0x2000"),
        (0x4000, "Inutilisé 0x4000"),
        (0x8000, "Inutilisé 0x8000"),
    };

    /// <summary>Helper : retourne les labels actifs.</summary>
    public static List<string> GetActiveLabels(int bitfield, (int Mask, string Label)[] definitions)
    {
        var result = new List<string>();
        foreach (var (mask, label) in definitions)
            if ((bitfield & mask) != 0) result.Add(label);
        return result;
    }
}
