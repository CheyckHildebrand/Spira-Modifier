namespace SpiraModifier.Core.Models;

/// <summary>
/// IDs des personnages joueurs dans FFX, utilisés par CharacterUser (offset 0x19).
/// Source : enum playerChar du parser de Karifean.
/// </summary>
public static class PlayerCharacters
{
    public const int Tidus    = 0x00;
    public const int Yuna     = 0x01;
    public const int Auron    = 0x02;
    public const int Kimahri  = 0x03;
    public const int Wakka    = 0x04;
    public const int Lulu     = 0x05;
    public const int Rikku    = 0x06;
    public const int Seymour  = 0x07;
    public const int Valefor  = 0x08;
    public const int Ifrit    = 0x09;
    public const int Ixion    = 0x0A;
    public const int Shiva    = 0x0B;
    public const int Bahamut  = 0x0C;
    public const int Anima    = 0x0D;
    public const int Yojimbo  = 0x0E;
    public const int MagusCindy = 0x0F;
    public const int MagusSandy = 0x10;
    public const int MagusMindy = 0x11;
    public const int MagusFraternel = MagusCindy;
    public const int UsableAll = 0xFF;

    public static readonly int[] KnownCharacters =
    [
        Tidus, Yuna, Auron, Kimahri, Wakka, Lulu, Rikku, Seymour,
        Valefor, Ifrit, Ixion, Shiva, Bahamut, Anima, Yojimbo,
        MagusCindy, MagusSandy, MagusMindy,
    ];

    public static readonly int[] CommandOwners =
    [
        Tidus, Yuna, Auron, Kimahri, Wakka, Lulu, Rikku, Seymour,
        Valefor, Ifrit, Ixion, Shiva, Bahamut, Anima, Yojimbo,
        MagusCindy, MagusSandy, MagusMindy, UsableAll,
    ];

    /// <summary>Retourne le nom français du personnage (ou null si inconnu).</summary>
    public static string? GetName(int id) => id switch
    {
        Tidus => "Tidus",
        Yuna => "Yuna",
        Auron => "Auron",
        Kimahri => "Kimahri",
        Wakka => "Wakka",
        Lulu => "Lulu",
        Rikku => "Rikku",
        Seymour => "Seymour",
        Valefor => "Valefor",
        Ifrit => "Ifrit",
        Ixion => "Ixion",
        Shiva => "Shiva",
        Bahamut => "Bahamut",
        Anima => "Anima",
        Yojimbo => "Yojimbo",
        MagusCindy => "Cindy",
        MagusSandy => "Sandy",
        MagusMindy => "Mindy",
        UsableAll => "Tous",
        _ => null,
    };

    /// <summary>True si l'ID correspond à une Chimère (Aeon).</summary>
    public static bool IsAeon(int id) => id >= Valefor && id <= MagusMindy;

    /// <summary>True si l'ID correspond à un personnage joueur principal (pas Chimère).</summary>
    public static bool IsHumanCharacter(int id) => id >= Tidus && id <= Seymour;
}
