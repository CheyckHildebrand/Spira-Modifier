using SpiraModifier.Core.BinaryIO;

namespace SpiraModifier.Core.Models;

/// <summary>
/// Une "auto-ability" = une aptitude qu'on peut greffer sur un équipement
/// (Magic Booster, Strength +X%, Half MP Cost, Auto-Haste, Ribbon, etc.).
///
/// Ce sont les valeurs référencées par les 4 slots d'aptitudes dans GearData.
/// Stockées dans a_ability.bin (per-langue pour les noms et descriptions).
///
/// Layout d'une entrée (0x6C octets) :
///   0x00-0x0F : 16 octets de textes (nom + nom court + description + desc courte)
///               Mêmes offsets que NameDescriptionTextObject (cf. AttackData)
///   0x10      : SOS flag byte
///   0x11-0x15 : élément Strike/Absorb/Immune/Resist/Weak (1 byte chacun, bitfield)
///   0x16-0x2E : status inflict chances (25 statuts × 1 byte)
///   0x2F-0x3B : status durations (13 × 1 byte)
///   0x3C-0x54 : status resist chances (25 × 1 byte)
///   0x55      : statIncreaseAmount (montant du bonus de stat ex: +50 HP, +5%)
///   0x56-0x57 : statIncreaseFlags (2 bytes : sur quelle stat agit le bonus)
///   0x58-0x59 : autoStatusesPermanent (2 bytes — Auto-Protect, Auto-Shell, etc.)
///   0x5A-0x5B : autoStatusesTemporal (2 bytes)
///   0x5C-0x5D : autoStatusesExtra (2 bytes)
///   0x5E-0x5F : extraStatusInflict (2 bytes)
///   0x60-0x61 : extraStatusImmunities (2 bytes)
///   0x62-0x66 : abilityFlags62..66 (5 bytes de flags divers)
///   0x67      : unknownByte67
///   0x68      : icon
///   0x69      : groupIndex
///   0x6A      : groupLevel
///   0x6B      : internationalBonusIndex
///
/// Source : AutoAbilityDataObject.java du parser de Karifean.
/// </summary>
public class AutoAbilityData
{
    public const int LENGTH = 0x6C;

    // ===== Textes embarqués (offsets 0x00-0x0F) =====
    public int NameOffset;
    public int NameKey;
    public int SimplifiedNameOffset;
    public int SimplifiedNameKey;
    public int DescriptionOffset;
    public int DescriptionKey;
    public int SimplifiedDescriptionOffset;
    public int SimplifiedDescriptionKey;

    // ===== Flags & élements =====
    public int SosFlagByte;       // 0x10 — déclencheur en SOS (HP bas)
    public int ElementStrike;     // 0x11
    public int ElementAbsorb;     // 0x12
    public int ElementImmune;     // 0x13
    public int ElementResist;     // 0x14
    public int ElementWeak;       // 0x15

    // ===== Status inflict chances (0x16-0x2E) =====
    public int StatusInflictChanceDeath, StatusInflictChanceZombie, StatusInflictChancePetrify;
    public int StatusInflictChancePoison, StatusInflictChancePowerBreak, StatusInflictChanceMagicBreak;
    public int StatusInflictChanceArmorBreak, StatusInflictChanceMentalBreak;
    public int StatusInflictChanceConfuse, StatusInflictChanceBerserk;
    public int StatusInflictChanceProvoke, StatusInflictChanceThreaten;
    public int StatusInflictChanceSleep, StatusInflictChanceSilence, StatusInflictChanceDarkness;
    public int StatusInflictChanceShell, StatusInflictChanceProtect, StatusInflictChanceReflect;
    public int StatusInflictChanceNTide, StatusInflictChanceNBlaze;
    public int StatusInflictChanceNShock, StatusInflictChanceNFrost;
    public int StatusInflictChanceRegen, StatusInflictChanceHaste, StatusInflictChanceSlow;

    // ===== Status durations (0x2F-0x3B) =====
    public int StatusDurationSleep, StatusDurationSilence, StatusDurationDarkness;
    public int StatusDurationShell, StatusDurationProtect, StatusDurationReflect;
    public int StatusDurationNTide, StatusDurationNBlaze, StatusDurationNShock, StatusDurationNFrost;
    public int StatusDurationRegen, StatusDurationHaste, StatusDurationSlow;

    // ===== Status resist chances (0x3C-0x54) =====
    public int StatusResistChanceDeath, StatusResistChanceZombie, StatusResistChancePetrify;
    public int StatusResistChancePoison, StatusResistChancePowerBreak, StatusResistChanceMagicBreak;
    public int StatusResistChanceArmorBreak, StatusResistChanceMentalBreak;
    public int StatusResistChanceConfuse, StatusResistChanceBerserk;
    public int StatusResistChanceProvoke, StatusResistChanceThreaten;
    public int StatusResistChanceSleep, StatusResistChanceSilence, StatusResistChanceDarkness;
    public int StatusResistChanceShell, StatusResistChanceProtect, StatusResistChanceReflect;
    public int StatusResistChanceNTide, StatusResistChanceNBlaze;
    public int StatusResistChanceNShock, StatusResistChanceNFrost;
    public int StatusResistChanceRegen, StatusResistChanceHaste, StatusResistChanceSlow;

    // ===== Stat boosts =====
    public int StatIncreaseAmount;   // 0x55
    public int StatIncreaseFlags;    // 0x56-0x57 : bitfield indiquant quelle stat est boostée

    // ===== Auto-statuses (octroyés en début de combat) =====
    public int AutoStatusesPermanent; // 0x58-0x59
    public int AutoStatusesTemporal;  // 0x5A-0x5B
    public int AutoStatusesExtra;     // 0x5C-0x5D

    // ===== Status flags supplémentaires =====
    public int ExtraStatusInflict;    // 0x5E-0x5F
    public int ExtraStatusImmunities; // 0x60-0x61

    // ===== Flags d'aptitude divers =====
    public int AbilityFlags62, AbilityFlags63, AbilityFlags64, AbilityFlags65, AbilityFlags66;
    public int UnknownByte67;

    // ===== Métadonnées =====
    public int Icon;                     // 0x68
    public int GroupIndex;               // 0x69 — groupe de l'aptitude (Strength, Defense, etc.)
    public int GroupLevel;               // 0x6A — niveau dans le groupe (Strength +3% < +5% < +10%)
    public int InternationalBonusIndex;  // 0x6B — 1..4 pour Distill Power/Mana/Speed/Ability,
                                         //         255 pour Ribbon, 254 pour tout ce qui est remplacé par Ribbon

    public static AutoAbilityData ReadFromBytes(byte[] bytes, int offset)
    {
        if (bytes.Length < offset + LENGTH)
            throw new ArgumentException(
                $"Buffer trop petit pour AutoAbility : {bytes.Length - offset} dispo, {LENGTH} requis.");

        var a = new AutoAbilityData();

        // Textes (0x00-0x0F, mêmes offsets que pour les attaques)
        a.NameOffset                  = BytesHelper.Read2Bytes(bytes, offset + 0x00);
        a.NameKey                     = BytesHelper.Read2Bytes(bytes, offset + 0x02);
        a.SimplifiedNameOffset        = BytesHelper.Read2Bytes(bytes, offset + 0x04);
        a.SimplifiedNameKey           = BytesHelper.Read2Bytes(bytes, offset + 0x06);
        a.DescriptionOffset           = BytesHelper.Read2Bytes(bytes, offset + 0x08);
        a.DescriptionKey              = BytesHelper.Read2Bytes(bytes, offset + 0x0A);
        a.SimplifiedDescriptionOffset = BytesHelper.Read2Bytes(bytes, offset + 0x0C);
        a.SimplifiedDescriptionKey    = BytesHelper.Read2Bytes(bytes, offset + 0x0E);

        a.SosFlagByte    = bytes[offset + 0x10];
        a.ElementStrike  = bytes[offset + 0x11];
        a.ElementAbsorb  = bytes[offset + 0x12];
        a.ElementImmune  = bytes[offset + 0x13];
        a.ElementResist  = bytes[offset + 0x14];
        a.ElementWeak    = bytes[offset + 0x15];

        // Status inflict (25 valeurs)
        a.StatusInflictChanceDeath        = bytes[offset + 0x16];
        a.StatusInflictChanceZombie       = bytes[offset + 0x17];
        a.StatusInflictChancePetrify      = bytes[offset + 0x18];
        a.StatusInflictChancePoison       = bytes[offset + 0x19];
        a.StatusInflictChancePowerBreak   = bytes[offset + 0x1A];
        a.StatusInflictChanceMagicBreak   = bytes[offset + 0x1B];
        a.StatusInflictChanceArmorBreak   = bytes[offset + 0x1C];
        a.StatusInflictChanceMentalBreak  = bytes[offset + 0x1D];
        a.StatusInflictChanceConfuse      = bytes[offset + 0x1E];
        a.StatusInflictChanceBerserk      = bytes[offset + 0x1F];
        a.StatusInflictChanceProvoke      = bytes[offset + 0x20];
        a.StatusInflictChanceThreaten     = bytes[offset + 0x21];
        a.StatusInflictChanceSleep        = bytes[offset + 0x22];
        a.StatusInflictChanceSilence      = bytes[offset + 0x23];
        a.StatusInflictChanceDarkness     = bytes[offset + 0x24];
        a.StatusInflictChanceShell        = bytes[offset + 0x25];
        a.StatusInflictChanceProtect      = bytes[offset + 0x26];
        a.StatusInflictChanceReflect      = bytes[offset + 0x27];
        a.StatusInflictChanceNTide        = bytes[offset + 0x28];
        a.StatusInflictChanceNBlaze       = bytes[offset + 0x29];
        a.StatusInflictChanceNShock       = bytes[offset + 0x2A];
        a.StatusInflictChanceNFrost       = bytes[offset + 0x2B];
        a.StatusInflictChanceRegen        = bytes[offset + 0x2C];
        a.StatusInflictChanceHaste        = bytes[offset + 0x2D];
        a.StatusInflictChanceSlow         = bytes[offset + 0x2E];

        // Status durations (13 valeurs)
        a.StatusDurationSleep    = bytes[offset + 0x2F];
        a.StatusDurationSilence  = bytes[offset + 0x30];
        a.StatusDurationDarkness = bytes[offset + 0x31];
        a.StatusDurationShell    = bytes[offset + 0x32];
        a.StatusDurationProtect  = bytes[offset + 0x33];
        a.StatusDurationReflect  = bytes[offset + 0x34];
        a.StatusDurationNTide    = bytes[offset + 0x35];
        a.StatusDurationNBlaze   = bytes[offset + 0x36];
        a.StatusDurationNShock   = bytes[offset + 0x37];
        a.StatusDurationNFrost   = bytes[offset + 0x38];
        a.StatusDurationRegen    = bytes[offset + 0x39];
        a.StatusDurationHaste    = bytes[offset + 0x3A];
        a.StatusDurationSlow     = bytes[offset + 0x3B];

        // Status resist (25 valeurs)
        a.StatusResistChanceDeath        = bytes[offset + 0x3C];
        a.StatusResistChanceZombie       = bytes[offset + 0x3D];
        a.StatusResistChancePetrify      = bytes[offset + 0x3E];
        a.StatusResistChancePoison       = bytes[offset + 0x3F];
        a.StatusResistChancePowerBreak   = bytes[offset + 0x40];
        a.StatusResistChanceMagicBreak   = bytes[offset + 0x41];
        a.StatusResistChanceArmorBreak   = bytes[offset + 0x42];
        a.StatusResistChanceMentalBreak  = bytes[offset + 0x43];
        a.StatusResistChanceConfuse      = bytes[offset + 0x44];
        a.StatusResistChanceBerserk      = bytes[offset + 0x45];
        a.StatusResistChanceProvoke      = bytes[offset + 0x46];
        a.StatusResistChanceThreaten     = bytes[offset + 0x47];
        a.StatusResistChanceSleep        = bytes[offset + 0x48];
        a.StatusResistChanceSilence      = bytes[offset + 0x49];
        a.StatusResistChanceDarkness     = bytes[offset + 0x4A];
        a.StatusResistChanceShell        = bytes[offset + 0x4B];
        a.StatusResistChanceProtect      = bytes[offset + 0x4C];
        a.StatusResistChanceReflect      = bytes[offset + 0x4D];
        a.StatusResistChanceNTide        = bytes[offset + 0x4E];
        a.StatusResistChanceNBlaze       = bytes[offset + 0x4F];
        a.StatusResistChanceNShock       = bytes[offset + 0x50];
        a.StatusResistChanceNFrost       = bytes[offset + 0x51];
        a.StatusResistChanceRegen        = bytes[offset + 0x52];
        a.StatusResistChanceHaste        = bytes[offset + 0x53];
        a.StatusResistChanceSlow         = bytes[offset + 0x54];

        // Stat boosts
        a.StatIncreaseAmount = bytes[offset + 0x55];
        a.StatIncreaseFlags  = BytesHelper.Read2Bytes(bytes, offset + 0x56);

        // Auto-statuses
        a.AutoStatusesPermanent = BytesHelper.Read2Bytes(bytes, offset + 0x58);
        a.AutoStatusesTemporal  = BytesHelper.Read2Bytes(bytes, offset + 0x5A);
        a.AutoStatusesExtra     = BytesHelper.Read2Bytes(bytes, offset + 0x5C);
        a.ExtraStatusInflict    = BytesHelper.Read2Bytes(bytes, offset + 0x5E);
        a.ExtraStatusImmunities = BytesHelper.Read2Bytes(bytes, offset + 0x60);

        // Flags & métadonnées
        a.AbilityFlags62 = bytes[offset + 0x62];
        a.AbilityFlags63 = bytes[offset + 0x63];
        a.AbilityFlags64 = bytes[offset + 0x64];
        a.AbilityFlags65 = bytes[offset + 0x65];
        a.AbilityFlags66 = bytes[offset + 0x66];
        a.UnknownByte67  = bytes[offset + 0x67];
        a.Icon                    = bytes[offset + 0x68];
        a.GroupIndex              = bytes[offset + 0x69];
        a.GroupLevel              = bytes[offset + 0x6A];
        a.InternationalBonusIndex = bytes[offset + 0x6B];

        return a;
    }

    public byte[] WriteToBytes()
    {
        var bytes = new byte[LENGTH];

        BytesHelper.Write2Bytes(bytes, 0x00, NameOffset);
        BytesHelper.Write2Bytes(bytes, 0x02, NameKey);
        BytesHelper.Write2Bytes(bytes, 0x04, SimplifiedNameOffset);
        BytesHelper.Write2Bytes(bytes, 0x06, SimplifiedNameKey);
        BytesHelper.Write2Bytes(bytes, 0x08, DescriptionOffset);
        BytesHelper.Write2Bytes(bytes, 0x0A, DescriptionKey);
        BytesHelper.Write2Bytes(bytes, 0x0C, SimplifiedDescriptionOffset);
        BytesHelper.Write2Bytes(bytes, 0x0E, SimplifiedDescriptionKey);

        bytes[0x10] = (byte)(SosFlagByte & 0xFF);
        bytes[0x11] = (byte)(ElementStrike & 0xFF);
        bytes[0x12] = (byte)(ElementAbsorb & 0xFF);
        bytes[0x13] = (byte)(ElementImmune & 0xFF);
        bytes[0x14] = (byte)(ElementResist & 0xFF);
        bytes[0x15] = (byte)(ElementWeak & 0xFF);

        bytes[0x16] = (byte)(StatusInflictChanceDeath & 0xFF);
        bytes[0x17] = (byte)(StatusInflictChanceZombie & 0xFF);
        bytes[0x18] = (byte)(StatusInflictChancePetrify & 0xFF);
        bytes[0x19] = (byte)(StatusInflictChancePoison & 0xFF);
        bytes[0x1A] = (byte)(StatusInflictChancePowerBreak & 0xFF);
        bytes[0x1B] = (byte)(StatusInflictChanceMagicBreak & 0xFF);
        bytes[0x1C] = (byte)(StatusInflictChanceArmorBreak & 0xFF);
        bytes[0x1D] = (byte)(StatusInflictChanceMentalBreak & 0xFF);
        bytes[0x1E] = (byte)(StatusInflictChanceConfuse & 0xFF);
        bytes[0x1F] = (byte)(StatusInflictChanceBerserk & 0xFF);
        bytes[0x20] = (byte)(StatusInflictChanceProvoke & 0xFF);
        bytes[0x21] = (byte)(StatusInflictChanceThreaten & 0xFF);
        bytes[0x22] = (byte)(StatusInflictChanceSleep & 0xFF);
        bytes[0x23] = (byte)(StatusInflictChanceSilence & 0xFF);
        bytes[0x24] = (byte)(StatusInflictChanceDarkness & 0xFF);
        bytes[0x25] = (byte)(StatusInflictChanceShell & 0xFF);
        bytes[0x26] = (byte)(StatusInflictChanceProtect & 0xFF);
        bytes[0x27] = (byte)(StatusInflictChanceReflect & 0xFF);
        bytes[0x28] = (byte)(StatusInflictChanceNTide & 0xFF);
        bytes[0x29] = (byte)(StatusInflictChanceNBlaze & 0xFF);
        bytes[0x2A] = (byte)(StatusInflictChanceNShock & 0xFF);
        bytes[0x2B] = (byte)(StatusInflictChanceNFrost & 0xFF);
        bytes[0x2C] = (byte)(StatusInflictChanceRegen & 0xFF);
        bytes[0x2D] = (byte)(StatusInflictChanceHaste & 0xFF);
        bytes[0x2E] = (byte)(StatusInflictChanceSlow & 0xFF);

        bytes[0x2F] = (byte)(StatusDurationSleep & 0xFF);
        bytes[0x30] = (byte)(StatusDurationSilence & 0xFF);
        bytes[0x31] = (byte)(StatusDurationDarkness & 0xFF);
        bytes[0x32] = (byte)(StatusDurationShell & 0xFF);
        bytes[0x33] = (byte)(StatusDurationProtect & 0xFF);
        bytes[0x34] = (byte)(StatusDurationReflect & 0xFF);
        bytes[0x35] = (byte)(StatusDurationNTide & 0xFF);
        bytes[0x36] = (byte)(StatusDurationNBlaze & 0xFF);
        bytes[0x37] = (byte)(StatusDurationNShock & 0xFF);
        bytes[0x38] = (byte)(StatusDurationNFrost & 0xFF);
        bytes[0x39] = (byte)(StatusDurationRegen & 0xFF);
        bytes[0x3A] = (byte)(StatusDurationHaste & 0xFF);
        bytes[0x3B] = (byte)(StatusDurationSlow & 0xFF);

        bytes[0x3C] = (byte)(StatusResistChanceDeath & 0xFF);
        bytes[0x3D] = (byte)(StatusResistChanceZombie & 0xFF);
        bytes[0x3E] = (byte)(StatusResistChancePetrify & 0xFF);
        bytes[0x3F] = (byte)(StatusResistChancePoison & 0xFF);
        bytes[0x40] = (byte)(StatusResistChancePowerBreak & 0xFF);
        bytes[0x41] = (byte)(StatusResistChanceMagicBreak & 0xFF);
        bytes[0x42] = (byte)(StatusResistChanceArmorBreak & 0xFF);
        bytes[0x43] = (byte)(StatusResistChanceMentalBreak & 0xFF);
        bytes[0x44] = (byte)(StatusResistChanceConfuse & 0xFF);
        bytes[0x45] = (byte)(StatusResistChanceBerserk & 0xFF);
        bytes[0x46] = (byte)(StatusResistChanceProvoke & 0xFF);
        bytes[0x47] = (byte)(StatusResistChanceThreaten & 0xFF);
        bytes[0x48] = (byte)(StatusResistChanceSleep & 0xFF);
        bytes[0x49] = (byte)(StatusResistChanceSilence & 0xFF);
        bytes[0x4A] = (byte)(StatusResistChanceDarkness & 0xFF);
        bytes[0x4B] = (byte)(StatusResistChanceShell & 0xFF);
        bytes[0x4C] = (byte)(StatusResistChanceProtect & 0xFF);
        bytes[0x4D] = (byte)(StatusResistChanceReflect & 0xFF);
        bytes[0x4E] = (byte)(StatusResistChanceNTide & 0xFF);
        bytes[0x4F] = (byte)(StatusResistChanceNBlaze & 0xFF);
        bytes[0x50] = (byte)(StatusResistChanceNShock & 0xFF);
        bytes[0x51] = (byte)(StatusResistChanceNFrost & 0xFF);
        bytes[0x52] = (byte)(StatusResistChanceRegen & 0xFF);
        bytes[0x53] = (byte)(StatusResistChanceHaste & 0xFF);
        bytes[0x54] = (byte)(StatusResistChanceSlow & 0xFF);

        bytes[0x55] = (byte)(StatIncreaseAmount & 0xFF);
        BytesHelper.Write2Bytes(bytes, 0x56, StatIncreaseFlags);
        BytesHelper.Write2Bytes(bytes, 0x58, AutoStatusesPermanent);
        BytesHelper.Write2Bytes(bytes, 0x5A, AutoStatusesTemporal);
        BytesHelper.Write2Bytes(bytes, 0x5C, AutoStatusesExtra);
        BytesHelper.Write2Bytes(bytes, 0x5E, ExtraStatusInflict);
        BytesHelper.Write2Bytes(bytes, 0x60, ExtraStatusImmunities);

        bytes[0x62] = (byte)(AbilityFlags62 & 0xFF);
        bytes[0x63] = (byte)(AbilityFlags63 & 0xFF);
        bytes[0x64] = (byte)(AbilityFlags64 & 0xFF);
        bytes[0x65] = (byte)(AbilityFlags65 & 0xFF);
        bytes[0x66] = (byte)(AbilityFlags66 & 0xFF);
        bytes[0x67] = (byte)(UnknownByte67 & 0xFF);
        bytes[0x68] = (byte)(Icon & 0xFF);
        bytes[0x69] = (byte)(GroupIndex & 0xFF);
        bytes[0x6A] = (byte)(GroupLevel & 0xFF);
        bytes[0x6B] = (byte)(InternationalBonusIndex & 0xFF);

        return bytes;
    }

    /// <summary>
    /// True si l'aptitude semble "vide" (probablement un placeholder dans le fichier).
    /// </summary>
    public bool IsEmpty => NameOffset == 0 && DescriptionOffset == 0 && Icon == 0;
}
