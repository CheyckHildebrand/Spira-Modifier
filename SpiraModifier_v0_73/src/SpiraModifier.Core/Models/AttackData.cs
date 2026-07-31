using SpiraModifier.Core.BinaryIO;

namespace SpiraModifier.Core.Models;

/// <summary>
/// Représente une "commande" au sens FFX : c'est l'unité de base qui définit une attaque,
/// une magie, une compétence, ou un objet utilisable. Tous ces fichiers partagent le même format :
///
///   - command.bin   : commandes joueur (attaque, défense, fuite, magie, compétences spé)
///   - monmagic1.bin : attaques de monstres normaux
///   - monmagic2.bin : attaques de boss et monstres spéciaux
///   - item.bin      : objets utilisables
///
/// Une entrée fait 0x5C octets pour les attaques/objets, ou 0x60 pour les commandes joueur
/// (4 octets de plus pour les ordering/sphere grid).
///
/// Layout :
///   0x00-0x0F : textes embarqués (nom + nom simplifié + description + description simplifiée)
///   0x10-0x5B : données mécaniques (animations, puissance, formule, statuts, éléments, ...)
///   0x5C-0x5F : extension joueur uniquement
///
/// Source : CommandDataObject.java du parser de Karifean.
/// </summary>
public class AttackData
{
    /// <summary>Taille standard pour une attaque/objet (monmagic, item).</summary>
    public const int LENGTH_COM = 0x5C;
    /// <summary>Taille étendue pour une commande joueur (command.bin).</summary>
    public const int LENGTH_PCCOM = 0x60;

    /// <summary>Si true, l'entrée fait 0x60 octets (commande joueur) au lieu de 0x5C (attaque/objet).</summary>
    public bool IsCharacterAbility { get; set; }

    // ===== Textes embarqués (offsets 0x00-0x0F) =====
    // Mêmes pour command/monmagic/item : on note offsets+keys mais les chaînes
    // sont dans le pool de strings du fichier parent.
    public int NameOffset;
    public int NameKey;
    public int SimplifiedNameOffset;
    public int SimplifiedNameKey;
    public int DescriptionOffset;
    public int DescriptionKey;
    public int SimplifiedDescriptionOffset;
    public int SimplifiedDescriptionKey;

    // ===== Animations & propriétés (0x10-0x1F) =====
    public int Anim1;                    // 0x10 : 2 bytes
    public int Anim2;                    // 0x12 : 2 bytes
    public int Icon;                     // 0x14
    public int CasterAnimation;          // 0x15
    public int MenuProperties16;         // 0x16
    public int SubsubMenuCategorization; // 0x17
    public int SubMenuCategorization;    // 0x18
    public int CharacterUser;            // 0x19
    public int TargetingFlags;           // 0x1A : bitfield (target enemies / multi / self / etc.)
    public int TargetsAllowedApparently; // 0x1B
    public int MiscProperties1C;         // 0x1C
    public int MiscProperties1D;         // 0x1D
    public int MiscProperties1E;         // 0x1E
    public int AnimationProperties1F;    // 0x1F

    // ===== Coût & damage (0x20-0x2D) =====
    public int DamageProperties20;       // 0x20 : flags physique/magique/HP/MP/CTB
    public int StealGilByte;             // 0x21
    public int PartyPreviewByte;         // 0x22
    public int DamageClass;              // 0x23
    public int MoveRank;                 // 0x24
    public int CostMP;                   // 0x25
    public int CostOD;                   // 0x26 : coût Overdrive
    public int AttackCritBonus;          // 0x27
    public int DamageFormula;            // 0x28 : formule (0..N pour différentes formules vanilla)
    public int AttackAccuracy;           // 0x29 : précision
    public int AttackPower;              // 0x2A : puissance brute
    public int HitCount;                 // 0x2B : nombre de coups
    public int ShatterChance;            // 0x2C : chance de pétrification massive
    public int ElementFlags;             // 0x2D : bitfield éléments (Feu/Glace/Foudre/Eau/Saint)

    // ===== Statuts infligés (chances) (0x2E-0x46) =====
    public int StatusChanceDeath;
    public int StatusChanceZombie;
    public int StatusChancePetrify;
    public int StatusChancePoison;
    public int StatusChancePowerBreak;
    public int StatusChanceMagicBreak;
    public int StatusChanceArmorBreak;
    public int StatusChanceMentalBreak;
    public int StatusChanceConfuse;
    public int StatusChanceBerserk;
    public int StatusChanceProvoke;
    public int StatusChanceThreaten;
    public int StatusChanceSleep;
    public int StatusChanceSilence;
    public int StatusChanceDarkness;
    public int StatusChanceShell;
    public int StatusChanceProtect;
    public int StatusChanceReflect;
    public int StatusChanceNTide;
    public int StatusChanceNBlaze;
    public int StatusChanceNShock;
    public int StatusChanceNFrost;
    public int StatusChanceRegen;
    public int StatusChanceHaste;
    public int StatusChanceSlow;

    // ===== Durées des statuts (0x47-0x53) =====
    public int StatusDurationSleep;
    public int StatusDurationSilence;
    public int StatusDurationDarkness;
    public int StatusDurationShell;
    public int StatusDurationProtect;
    public int StatusDurationReflect;
    public int StatusDurationNTide;
    public int StatusDurationNBlaze;
    public int StatusDurationNShock;
    public int StatusDurationNFrost;
    public int StatusDurationRegen;
    public int StatusDurationHaste;
    public int StatusDurationSlow;

    // ===== Statuts extras + buffs (0x54-0x5B) =====
    public int ExtraStatusInflict;       // 0x54 : 2 bytes (Scan, Shield, Boost, Distill*)
    public int StatBuffFlags;            // 0x56 : 2 bytes
    public int OverdriveCategorizationByte; // 0x58
    public int StatBuffValue;            // 0x59
    public int SpecialBuffInflict;       // 0x5A : 2 bytes

    // ===== Extension joueur uniquement (0x5C-0x5F) =====
    public int OrderingIndexInMenu;
    public int SphereTypeForSphereGrid;
    public int AlwaysZero5E;
    public int AlwaysZero5F;

    /// <summary>
    /// Lit une AttackData depuis un buffer. Si isCharacterAbility=true, lit 0x60 octets.
    /// </summary>
    public static AttackData ReadFromBytes(byte[] bytes, int offset, bool isCharacterAbility)
    {
        var requiredLength = isCharacterAbility ? LENGTH_PCCOM : LENGTH_COM;
        if (bytes.Length < offset + requiredLength)
            throw new ArgumentException(
                $"Buffer trop petit : {bytes.Length - offset} disponibles, {requiredLength} requis.");

        var a = new AttackData { IsCharacterAbility = isCharacterAbility };

        // Textes (mêmes offsets que NameDescriptionTextObject)
        a.NameOffset                  = BytesHelper.Read2Bytes(bytes, offset + 0x00);
        a.NameKey                     = BytesHelper.Read2Bytes(bytes, offset + 0x02);
        a.SimplifiedNameOffset        = BytesHelper.Read2Bytes(bytes, offset + 0x04);
        a.SimplifiedNameKey           = BytesHelper.Read2Bytes(bytes, offset + 0x06);
        a.DescriptionOffset           = BytesHelper.Read2Bytes(bytes, offset + 0x08);
        a.DescriptionKey              = BytesHelper.Read2Bytes(bytes, offset + 0x0A);
        a.SimplifiedDescriptionOffset = BytesHelper.Read2Bytes(bytes, offset + 0x0C);
        a.SimplifiedDescriptionKey    = BytesHelper.Read2Bytes(bytes, offset + 0x0E);

        // Animations & propriétés
        a.Anim1                    = BytesHelper.Read2Bytes(bytes, offset + 0x10);
        a.Anim2                    = BytesHelper.Read2Bytes(bytes, offset + 0x12);
        a.Icon                     = bytes[offset + 0x14];
        a.CasterAnimation          = bytes[offset + 0x15];
        a.MenuProperties16         = bytes[offset + 0x16];
        a.SubsubMenuCategorization = bytes[offset + 0x17];
        a.SubMenuCategorization    = bytes[offset + 0x18];
        a.CharacterUser            = bytes[offset + 0x19];
        a.TargetingFlags           = bytes[offset + 0x1A];
        a.TargetsAllowedApparently = bytes[offset + 0x1B];
        a.MiscProperties1C         = bytes[offset + 0x1C];
        a.MiscProperties1D         = bytes[offset + 0x1D];
        a.MiscProperties1E         = bytes[offset + 0x1E];
        a.AnimationProperties1F    = bytes[offset + 0x1F];

        // Damage
        a.DamageProperties20  = bytes[offset + 0x20];
        a.StealGilByte        = bytes[offset + 0x21];
        a.PartyPreviewByte    = bytes[offset + 0x22];
        a.DamageClass         = bytes[offset + 0x23];
        a.MoveRank            = bytes[offset + 0x24];
        a.CostMP              = bytes[offset + 0x25];
        a.CostOD              = bytes[offset + 0x26];
        a.AttackCritBonus     = bytes[offset + 0x27];
        a.DamageFormula       = bytes[offset + 0x28];
        a.AttackAccuracy      = bytes[offset + 0x29];
        a.AttackPower         = bytes[offset + 0x2A];
        a.HitCount            = bytes[offset + 0x2B];
        a.ShatterChance       = bytes[offset + 0x2C];
        a.ElementFlags        = bytes[offset + 0x2D];

        // Statuts (chances)
        a.StatusChanceDeath        = bytes[offset + 0x2E];
        a.StatusChanceZombie       = bytes[offset + 0x2F];
        a.StatusChancePetrify      = bytes[offset + 0x30];
        a.StatusChancePoison       = bytes[offset + 0x31];
        a.StatusChancePowerBreak   = bytes[offset + 0x32];
        a.StatusChanceMagicBreak   = bytes[offset + 0x33];
        a.StatusChanceArmorBreak   = bytes[offset + 0x34];
        a.StatusChanceMentalBreak  = bytes[offset + 0x35];
        a.StatusChanceConfuse      = bytes[offset + 0x36];
        a.StatusChanceBerserk      = bytes[offset + 0x37];
        a.StatusChanceProvoke      = bytes[offset + 0x38];
        a.StatusChanceThreaten     = bytes[offset + 0x39];
        a.StatusChanceSleep        = bytes[offset + 0x3A];
        a.StatusChanceSilence      = bytes[offset + 0x3B];
        a.StatusChanceDarkness     = bytes[offset + 0x3C];
        a.StatusChanceShell        = bytes[offset + 0x3D];
        a.StatusChanceProtect      = bytes[offset + 0x3E];
        a.StatusChanceReflect      = bytes[offset + 0x3F];
        a.StatusChanceNTide        = bytes[offset + 0x40];
        a.StatusChanceNBlaze       = bytes[offset + 0x41];
        a.StatusChanceNShock       = bytes[offset + 0x42];
        a.StatusChanceNFrost       = bytes[offset + 0x43];
        a.StatusChanceRegen        = bytes[offset + 0x44];
        a.StatusChanceHaste        = bytes[offset + 0x45];
        a.StatusChanceSlow         = bytes[offset + 0x46];

        // Durées (note : ordre légèrement différent dans le parser pour NTide/NBlaze/NShock/NFrost)
        a.StatusDurationSleep      = bytes[offset + 0x47];
        a.StatusDurationSilence    = bytes[offset + 0x48];
        a.StatusDurationDarkness   = bytes[offset + 0x49];
        a.StatusDurationShell      = bytes[offset + 0x4A];
        a.StatusDurationProtect    = bytes[offset + 0x4B];
        a.StatusDurationReflect    = bytes[offset + 0x4C];
        a.StatusDurationNTide      = bytes[offset + 0x4D];
        a.StatusDurationNBlaze     = bytes[offset + 0x4E];
        a.StatusDurationNShock     = bytes[offset + 0x4F];
        a.StatusDurationNFrost     = bytes[offset + 0x50];
        a.StatusDurationRegen      = bytes[offset + 0x51];
        a.StatusDurationHaste      = bytes[offset + 0x52];
        a.StatusDurationSlow       = bytes[offset + 0x53];

        a.ExtraStatusInflict           = BytesHelper.Read2Bytes(bytes, offset + 0x54);
        a.StatBuffFlags                = BytesHelper.Read2Bytes(bytes, offset + 0x56);
        a.OverdriveCategorizationByte  = bytes[offset + 0x58];
        a.StatBuffValue                = bytes[offset + 0x59];
        a.SpecialBuffInflict           = BytesHelper.Read2Bytes(bytes, offset + 0x5A);

        if (isCharacterAbility)
        {
            a.OrderingIndexInMenu       = bytes[offset + 0x5C];
            a.SphereTypeForSphereGrid   = bytes[offset + 0x5D];
            a.AlwaysZero5E              = bytes[offset + 0x5E];
            a.AlwaysZero5F              = bytes[offset + 0x5F];
        }

        return a;
    }

    /// <summary>Sérialise l'entrée en préservant le layout exact de CommandDataObject.</summary>
    public byte[] WriteToBytes()
    {
        var bytes = new byte[IsCharacterAbility ? LENGTH_PCCOM : LENGTH_COM];

        BytesHelper.Write2Bytes(bytes, 0x00, NameOffset);
        BytesHelper.Write2Bytes(bytes, 0x02, NameKey);
        BytesHelper.Write2Bytes(bytes, 0x04, SimplifiedNameOffset);
        BytesHelper.Write2Bytes(bytes, 0x06, SimplifiedNameKey);
        BytesHelper.Write2Bytes(bytes, 0x08, DescriptionOffset);
        BytesHelper.Write2Bytes(bytes, 0x0A, DescriptionKey);
        BytesHelper.Write2Bytes(bytes, 0x0C, SimplifiedDescriptionOffset);
        BytesHelper.Write2Bytes(bytes, 0x0E, SimplifiedDescriptionKey);

        BytesHelper.Write2Bytes(bytes, 0x10, Anim1);
        BytesHelper.Write2Bytes(bytes, 0x12, Anim2);
        bytes[0x14] = (byte)Icon;
        bytes[0x15] = (byte)CasterAnimation;
        bytes[0x16] = (byte)MenuProperties16;
        bytes[0x17] = (byte)SubsubMenuCategorization;
        bytes[0x18] = (byte)SubMenuCategorization;
        bytes[0x19] = (byte)CharacterUser;
        bytes[0x1A] = (byte)TargetingFlags;
        bytes[0x1B] = (byte)TargetsAllowedApparently;
        bytes[0x1C] = (byte)MiscProperties1C;
        bytes[0x1D] = (byte)MiscProperties1D;
        bytes[0x1E] = (byte)MiscProperties1E;
        bytes[0x1F] = (byte)AnimationProperties1F;
        bytes[0x20] = (byte)DamageProperties20;
        bytes[0x21] = (byte)StealGilByte;
        bytes[0x22] = (byte)PartyPreviewByte;
        bytes[0x23] = (byte)DamageClass;
        bytes[0x24] = (byte)MoveRank;
        bytes[0x25] = (byte)CostMP;
        bytes[0x26] = (byte)CostOD;
        bytes[0x27] = (byte)AttackCritBonus;
        bytes[0x28] = (byte)DamageFormula;
        bytes[0x29] = (byte)AttackAccuracy;
        bytes[0x2A] = (byte)AttackPower;
        bytes[0x2B] = (byte)HitCount;
        bytes[0x2C] = (byte)ShatterChance;
        bytes[0x2D] = (byte)ElementFlags;

        bytes[0x2E] = (byte)StatusChanceDeath;
        bytes[0x2F] = (byte)StatusChanceZombie;
        bytes[0x30] = (byte)StatusChancePetrify;
        bytes[0x31] = (byte)StatusChancePoison;
        bytes[0x32] = (byte)StatusChancePowerBreak;
        bytes[0x33] = (byte)StatusChanceMagicBreak;
        bytes[0x34] = (byte)StatusChanceArmorBreak;
        bytes[0x35] = (byte)StatusChanceMentalBreak;
        bytes[0x36] = (byte)StatusChanceConfuse;
        bytes[0x37] = (byte)StatusChanceBerserk;
        bytes[0x38] = (byte)StatusChanceProvoke;
        bytes[0x39] = (byte)StatusChanceThreaten;
        bytes[0x3A] = (byte)StatusChanceSleep;
        bytes[0x3B] = (byte)StatusChanceSilence;
        bytes[0x3C] = (byte)StatusChanceDarkness;
        bytes[0x3D] = (byte)StatusChanceShell;
        bytes[0x3E] = (byte)StatusChanceProtect;
        bytes[0x3F] = (byte)StatusChanceReflect;
        bytes[0x40] = (byte)StatusChanceNTide;
        bytes[0x41] = (byte)StatusChanceNBlaze;
        bytes[0x42] = (byte)StatusChanceNShock;
        bytes[0x43] = (byte)StatusChanceNFrost;
        bytes[0x44] = (byte)StatusChanceRegen;
        bytes[0x45] = (byte)StatusChanceHaste;
        bytes[0x46] = (byte)StatusChanceSlow;

        bytes[0x47] = (byte)StatusDurationSleep;
        bytes[0x48] = (byte)StatusDurationSilence;
        bytes[0x49] = (byte)StatusDurationDarkness;
        bytes[0x4A] = (byte)StatusDurationShell;
        bytes[0x4B] = (byte)StatusDurationProtect;
        bytes[0x4C] = (byte)StatusDurationReflect;
        bytes[0x4D] = (byte)StatusDurationNTide;
        bytes[0x4E] = (byte)StatusDurationNBlaze;
        bytes[0x4F] = (byte)StatusDurationNShock;
        bytes[0x50] = (byte)StatusDurationNFrost;
        bytes[0x51] = (byte)StatusDurationRegen;
        bytes[0x52] = (byte)StatusDurationHaste;
        bytes[0x53] = (byte)StatusDurationSlow;

        BytesHelper.Write2Bytes(bytes, 0x54, ExtraStatusInflict);
        BytesHelper.Write2Bytes(bytes, 0x56, StatBuffFlags);
        bytes[0x58] = (byte)OverdriveCategorizationByte;
        bytes[0x59] = (byte)StatBuffValue;
        BytesHelper.Write2Bytes(bytes, 0x5A, SpecialBuffInflict);

        if (IsCharacterAbility)
        {
            bytes[0x5C] = (byte)OrderingIndexInMenu;
            bytes[0x5D] = (byte)SphereTypeForSphereGrid;
            bytes[0x5E] = (byte)AlwaysZero5E;
            bytes[0x5F] = (byte)AlwaysZero5F;
        }

        return bytes;
    }

    // =========================================================================
    // Helpers de catégorisation
    // =========================================================================

    /// <summary>Bit 0x40 du byte 0x1F : marqueur spécifique aux overdrives de Chimères.</summary>
    public bool IsAeonOverdrive => (AnimationProperties1F & 0x40) != 0;

    /// <summary>Catégorisation par possesseur de la commande.</summary>
    public AttackOwnership GetOwnership()
    {
        if (!IsCharacterAbility) return AttackOwnership.MonsterAttack;
        if (IsAeonOverdrive) return AttackOwnership.AeonOverdrive;
        if (CharacterUser == PlayerCharacters.UsableAll) return AttackOwnership.UsableByAll;
        if (PlayerCharacters.IsAeon(CharacterUser)) return AttackOwnership.AeonAbility;
        if (PlayerCharacters.IsHumanCharacter(CharacterUser)) return AttackOwnership.PlayerAbility;
        return AttackOwnership.Unknown;
    }
}

/// <summary>Catégorisation d'une commande par son possesseur principal.</summary>
public enum AttackOwnership
{
    Unknown,
    PlayerAbility,
    PlayerOverdrive,
    AeonAbility,
    AeonOverdrive,
    UsableByAll,
    MonsterAttack,
}
