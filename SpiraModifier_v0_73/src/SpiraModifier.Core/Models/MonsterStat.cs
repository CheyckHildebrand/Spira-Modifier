using SpiraModifier.Core.BinaryIO;

namespace SpiraModifier.Core.Models;

/// <summary>
/// Bloc de statistiques d'un monstre (0x80 octets fixes).
///
/// Ce bloc constitue le chunk "StatSheet" du fichier .bin monstre.
/// Il contient les stats de base (HP/MP/STR/DEF/...), les résistances aux
/// éléments et statuts, les auto-statuts, la liste des 16 commandes utilisables,
/// et les références aux textes (nom, Sensor, Scan).
///
/// Offsets reconstitués depuis MonsterStatDataObject.java (FFXDataParser de Karifean)
/// et confirmés par Monster_StatSheet.cs (FFXProjectEditor d'Osdanova).
/// </summary>
public class MonsterStat
{
    /// <summary>Taille fixe du bloc en octets.</summary>
    public const int LENGTH = 0x80;

    // ===== Références aux chaînes localisées (offsets 0x00-0x13) =====
    // Chaque chaîne a un offset 2-byte (vers le bloc texte) + une clé 2-byte de chiffrement.
    // Les chaînes elles-mêmes sont stockées dans le chunk texte qui suit le StatSheet.

    public int NameOffset;
    public int NameKey;
    public int SensorTextOffset;
    public int SensorTextKey;
    public int SimplifiedSensorTextOffset;
    public int SimplifiedSensorTextKey;
    public int ScanTextOffset;
    public int ScanTextKey;
    public int SimplifiedScanTextOffset;
    public int SimplifiedScanTextKey;

    // ===== Stats de base (0x14-0x27) =====
    public int Hp;                   // 0x14, 4 bytes
    public int Mp;                   // 0x18, 4 bytes
    public int OverkillThreshold;    // 0x1C, 4 bytes
    public int Str;                  // 0x20
    public int Def;                  // 0x21
    public int Mag;                  // 0x22
    public int Mdf;                  // 0x23
    public int Agi;                  // 0x24
    public int Lck;                  // 0x25
    public int Eva;                  // 0x26
    public int Acc;                  // 0x27

    // ===== Flags d'immunités spéciales (0x28-0x29) =====
    // Ces deux octets contiennent des bitfields ; on garde la valeur brute
    // ET on expose les flags décodés pour l'UI.

    public int MiscProperties28;     // 0x28
    public int MiscProperties29;     // 0x29
    public int PoisonDamage;         // 0x2A : pourcentage de dégâts du poison

    // Flags décodés depuis MiscProperties28 (au mappage)
    public bool Armored;
    public bool ImmunityFractionalDamage;
    public bool ImmunityLife;
    public bool ImmunitySensor;
    public bool ImmunityScanAgainOrWhat;
    public bool ImmunityPhysicalDamage;
    public bool ImmunityMagicalDamage;
    public bool ImmunityHpDamage;

    // Flags décodés depuis MiscProperties29
    public bool ImmunityCtbDamage;
    public bool ImmunitySlice;
    public bool ImmunityBribe;

    // ===== Affinités élémentaires (0x2B-0x2E) =====
    // Bitfields : bit 0=Feu, 1=Glace, 2=Foudre, 3=Eau, 4=Saint
    public int ElementAbsorb;        // 0x2B
    public int ElementImmune;        // 0x2C
    public int ElementResist;        // 0x2D
    public int ElementWeak;          // 0x2E

    // ===== Résistances aux statuts (0x2F-0x47) =====
    // Chaque valeur : 0 = aucune résistance, 255 = immunité totale.
    public int StatusResistChanceDeath;        // 0x2F
    public int StatusResistChanceZombie;       // 0x30
    public int StatusResistChancePetrify;      // 0x31
    public int StatusResistChancePoison;       // 0x32
    public int StatusResistChancePowerBreak;   // 0x33
    public int StatusResistChanceMagicBreak;   // 0x34
    public int StatusResistChanceArmorBreak;   // 0x35
    public int StatusResistChanceMentalBreak;  // 0x36
    public int StatusResistChanceConfuse;      // 0x37
    public int StatusResistChanceBerserk;      // 0x38
    public int StatusResistChanceProvoke;      // 0x39
    public int StatusChanceThreaten;           // 0x3A : différent (chance d'être effrayé)
    public int StatusResistChanceSleep;        // 0x3B
    public int StatusResistChanceSilence;      // 0x3C
    public int StatusResistChanceDarkness;     // 0x3D
    public int StatusResistChanceShell;        // 0x3E
    public int StatusResistChanceProtect;      // 0x3F
    public int StatusResistChanceReflect;      // 0x40
    public int StatusResistChanceNTide;        // 0x41
    public int StatusResistChanceNBlaze;       // 0x42
    public int StatusResistChanceNShock;       // 0x43
    public int StatusResistChanceNFrost;       // 0x44
    public int StatusResistChanceRegen;        // 0x45
    public int StatusResistChanceHaste;        // 0x46
    public int StatusResistChanceSlow;         // 0x47

    // ===== Auto-statuts et immunités étendues (0x48-0x4F) =====
    public int AutoStatusesPermanent;          // 0x48 : 2 bytes bitfield
    public int AutoStatusesTemporal;           // 0x4A : 2 bytes bitfield
    public int AutoStatusesExtra;              // 0x4C : 2 bytes bitfield
    public int ExtraStatusImmunities;          // 0x4E : 2 bytes bitfield

    // ===== Liste des 16 commandes utilisables (0x50-0x6F) =====
    // Chaque slot = 2 bytes pointant vers un ID de commande dans command.bin/monmagic1/2.
    public int[] CommandList = new int[16];

    // ===== Métadonnées combat (0x70-0x7F) =====
    public int ForcedAction;          // 0x70 : action forcée si l'IA ne décide rien
    public int MonsterIdx;            // 0x72 : index du monstre dans la table globale
    public int ModelIdx;              // 0x74 : modèle 3D + animations
    public int CtbIconType;           // 0x76 : type d'icône CTB (1 byte)
    public int DoomCounter;           // 0x77 : compteur Doom (1 byte)
    public int MonsterArenaIdx;       // 0x78 : ID dans l'arène monstre (0xFF = non capturable)
    public int SoundBankRef;          // 0x7A : banque sonore
    public int AlwaysZero7C;          // 0x7C-0x7F : toujours zéro en vanilla
    public int AlwaysZero7D;
    public int AlwaysZero7E;
    public int AlwaysZero7F;

    /// <summary>
    /// Lit un MonsterStat depuis un buffer de 0x80 octets.
    /// </summary>
    public static MonsterStat ReadFromBytes(byte[] bytes, int offset = 0)
    {
        if (bytes.Length < offset + LENGTH)
            throw new ArgumentException(
                $"Buffer trop petit pour un MonsterStat : {bytes.Length - offset} octets disponibles, {LENGTH} requis.");

        var s = new MonsterStat();

        // Références textes
        s.NameOffset                    = BytesHelper.Read2Bytes(bytes, offset + 0x00);
        s.NameKey                       = BytesHelper.Read2Bytes(bytes, offset + 0x02);
        s.SensorTextOffset              = BytesHelper.Read2Bytes(bytes, offset + 0x04);
        s.SensorTextKey                 = BytesHelper.Read2Bytes(bytes, offset + 0x06);
        s.SimplifiedSensorTextOffset    = BytesHelper.Read2Bytes(bytes, offset + 0x08);
        s.SimplifiedSensorTextKey       = BytesHelper.Read2Bytes(bytes, offset + 0x0A);
        s.ScanTextOffset                = BytesHelper.Read2Bytes(bytes, offset + 0x0C);
        s.ScanTextKey                   = BytesHelper.Read2Bytes(bytes, offset + 0x0E);
        s.SimplifiedScanTextOffset      = BytesHelper.Read2Bytes(bytes, offset + 0x10);
        s.SimplifiedScanTextKey         = BytesHelper.Read2Bytes(bytes, offset + 0x12);

        // Stats principales
        s.Hp                = BytesHelper.Read4BytesSigned(bytes, offset + 0x14);
        s.Mp                = BytesHelper.Read4BytesSigned(bytes, offset + 0x18);
        s.OverkillThreshold = BytesHelper.Read4BytesSigned(bytes, offset + 0x1C);
        s.Str               = bytes[offset + 0x20];
        s.Def               = bytes[offset + 0x21];
        s.Mag               = bytes[offset + 0x22];
        s.Mdf               = bytes[offset + 0x23];
        s.Agi               = bytes[offset + 0x24];
        s.Lck               = bytes[offset + 0x25];
        s.Eva               = bytes[offset + 0x26];
        s.Acc               = bytes[offset + 0x27];

        // Flags d'immunité
        s.MiscProperties28  = bytes[offset + 0x28];
        s.MiscProperties29  = bytes[offset + 0x29];
        s.PoisonDamage      = bytes[offset + 0x2A];

        // Affinités élémentaires
        s.ElementAbsorb     = bytes[offset + 0x2B];
        s.ElementImmune     = bytes[offset + 0x2C];
        s.ElementResist     = bytes[offset + 0x2D];
        s.ElementWeak       = bytes[offset + 0x2E];

        // Résistances aux statuts
        s.StatusResistChanceDeath        = bytes[offset + 0x2F];
        s.StatusResistChanceZombie       = bytes[offset + 0x30];
        s.StatusResistChancePetrify      = bytes[offset + 0x31];
        s.StatusResistChancePoison       = bytes[offset + 0x32];
        s.StatusResistChancePowerBreak   = bytes[offset + 0x33];
        s.StatusResistChanceMagicBreak   = bytes[offset + 0x34];
        s.StatusResistChanceArmorBreak   = bytes[offset + 0x35];
        s.StatusResistChanceMentalBreak  = bytes[offset + 0x36];
        s.StatusResistChanceConfuse      = bytes[offset + 0x37];
        s.StatusResistChanceBerserk      = bytes[offset + 0x38];
        s.StatusResistChanceProvoke      = bytes[offset + 0x39];
        s.StatusChanceThreaten           = bytes[offset + 0x3A];
        s.StatusResistChanceSleep        = bytes[offset + 0x3B];
        s.StatusResistChanceSilence      = bytes[offset + 0x3C];
        s.StatusResistChanceDarkness     = bytes[offset + 0x3D];
        s.StatusResistChanceShell        = bytes[offset + 0x3E];
        s.StatusResistChanceProtect      = bytes[offset + 0x3F];
        s.StatusResistChanceReflect      = bytes[offset + 0x40];
        s.StatusResistChanceNTide        = bytes[offset + 0x41];
        s.StatusResistChanceNBlaze       = bytes[offset + 0x42];
        s.StatusResistChanceNShock       = bytes[offset + 0x43];
        s.StatusResistChanceNFrost       = bytes[offset + 0x44];
        s.StatusResistChanceRegen        = bytes[offset + 0x45];
        s.StatusResistChanceHaste        = bytes[offset + 0x46];
        s.StatusResistChanceSlow         = bytes[offset + 0x47];

        // Auto-statuts
        s.AutoStatusesPermanent  = BytesHelper.Read2Bytes(bytes, offset + 0x48);
        s.AutoStatusesTemporal   = BytesHelper.Read2Bytes(bytes, offset + 0x4A);
        s.AutoStatusesExtra      = BytesHelper.Read2Bytes(bytes, offset + 0x4C);
        s.ExtraStatusImmunities  = BytesHelper.Read2Bytes(bytes, offset + 0x4E);

        // Liste des commandes (16 slots de 2 bytes)
        for (int i = 0; i < 16; i++)
            s.CommandList[i] = BytesHelper.Read2Bytes(bytes, offset + 0x50 + i * 2);

        // Métadonnées combat
        s.ForcedAction      = BytesHelper.Read2Bytes(bytes, offset + 0x70);
        s.MonsterIdx        = BytesHelper.Read2Bytes(bytes, offset + 0x72);
        s.ModelIdx          = BytesHelper.Read2Bytes(bytes, offset + 0x74);
        s.CtbIconType       = bytes[offset + 0x76];
        s.DoomCounter       = bytes[offset + 0x77];
        s.MonsterArenaIdx   = BytesHelper.Read2Bytes(bytes, offset + 0x78);
        s.SoundBankRef      = BytesHelper.Read2Bytes(bytes, offset + 0x7A);
        s.AlwaysZero7C      = bytes[offset + 0x7C];
        s.AlwaysZero7D      = bytes[offset + 0x7D];
        s.AlwaysZero7E      = bytes[offset + 0x7E];
        s.AlwaysZero7F      = bytes[offset + 0x7F];

        s.MapFlagsFromMiscProperties();
        return s;
    }

    /// <summary>
    /// Décode les bitfields MiscProperties28/29 vers les booléens.
    /// </summary>
    private void MapFlagsFromMiscProperties()
    {
        Armored                  = (MiscProperties28 & 0x01) > 0;
        ImmunityFractionalDamage = (MiscProperties28 & 0x02) > 0;
        ImmunityLife             = (MiscProperties28 & 0x04) > 0;
        ImmunitySensor           = (MiscProperties28 & 0x08) > 0;
        ImmunityScanAgainOrWhat  = (MiscProperties28 & 0x10) > 0;
        ImmunityPhysicalDamage   = (MiscProperties28 & 0x20) > 0;
        ImmunityMagicalDamage    = (MiscProperties28 & 0x40) > 0;
        ImmunityHpDamage         = (MiscProperties28 & 0x80) > 0;

        ImmunityCtbDamage        = (MiscProperties29 & 0x01) > 0;
        ImmunitySlice            = (MiscProperties29 & 0x02) > 0;
        ImmunityBribe            = (MiscProperties29 & 0x04) > 0;
    }

    /// <summary>
    /// Recompose les bitfields depuis les booléens (à appeler avant l'écriture).
    /// </summary>
    private void RebuildMiscProperties()
    {
        MiscProperties28 = 0;
        if (Armored)                  MiscProperties28 |= 0x01;
        if (ImmunityFractionalDamage) MiscProperties28 |= 0x02;
        if (ImmunityLife)             MiscProperties28 |= 0x04;
        if (ImmunitySensor)           MiscProperties28 |= 0x08;
        if (ImmunityScanAgainOrWhat)  MiscProperties28 |= 0x10;
        if (ImmunityPhysicalDamage)   MiscProperties28 |= 0x20;
        if (ImmunityMagicalDamage)    MiscProperties28 |= 0x40;
        if (ImmunityHpDamage)         MiscProperties28 |= 0x80;

        // Note : on préserve les bits inconnus (>= 0x08) de MiscProperties29
        // pour ne pas casser les monstres qui auraient des flags non-documentés.
        var preservedBits = MiscProperties29 & ~0x07;
        MiscProperties29 = preservedBits;
        if (ImmunityCtbDamage) MiscProperties29 |= 0x01;
        if (ImmunitySlice)     MiscProperties29 |= 0x02;
        if (ImmunityBribe)     MiscProperties29 |= 0x04;
    }

    /// <summary>
    /// Sérialise le bloc vers un nouveau buffer de 0x80 octets.
    /// Les références textes (offset/key) ne sont PAS écrites ici : elles seront
    /// recalculées par MonsterFile lors de la sérialisation complète, en fonction
    /// du contenu actuel des chaînes localisées.
    /// </summary>
    public byte[] WriteToBytes()
    {
        RebuildMiscProperties();

        var array = new byte[LENGTH];

        // Note : les offsets/keys textes restent à 0 ici. MonsterFile les patchera.
        BytesHelper.Write2Bytes(array, 0x00, NameOffset);
        BytesHelper.Write2Bytes(array, 0x02, NameKey);
        BytesHelper.Write2Bytes(array, 0x04, SensorTextOffset);
        BytesHelper.Write2Bytes(array, 0x06, SensorTextKey);
        BytesHelper.Write2Bytes(array, 0x08, SimplifiedSensorTextOffset);
        BytesHelper.Write2Bytes(array, 0x0A, SimplifiedSensorTextKey);
        BytesHelper.Write2Bytes(array, 0x0C, ScanTextOffset);
        BytesHelper.Write2Bytes(array, 0x0E, ScanTextKey);
        BytesHelper.Write2Bytes(array, 0x10, SimplifiedScanTextOffset);
        BytesHelper.Write2Bytes(array, 0x12, SimplifiedScanTextKey);

        BytesHelper.Write4Bytes(array, 0x14, Hp);
        BytesHelper.Write4Bytes(array, 0x18, Mp);
        BytesHelper.Write4Bytes(array, 0x1C, OverkillThreshold);
        array[0x20] = (byte)Str;
        array[0x21] = (byte)Def;
        array[0x22] = (byte)Mag;
        array[0x23] = (byte)Mdf;
        array[0x24] = (byte)Agi;
        array[0x25] = (byte)Lck;
        array[0x26] = (byte)Eva;
        array[0x27] = (byte)Acc;

        array[0x28] = (byte)MiscProperties28;
        array[0x29] = (byte)MiscProperties29;
        array[0x2A] = (byte)PoisonDamage;
        array[0x2B] = (byte)ElementAbsorb;
        array[0x2C] = (byte)ElementImmune;
        array[0x2D] = (byte)ElementResist;
        array[0x2E] = (byte)ElementWeak;

        array[0x2F] = (byte)StatusResistChanceDeath;
        array[0x30] = (byte)StatusResistChanceZombie;
        array[0x31] = (byte)StatusResistChancePetrify;
        array[0x32] = (byte)StatusResistChancePoison;
        array[0x33] = (byte)StatusResistChancePowerBreak;
        array[0x34] = (byte)StatusResistChanceMagicBreak;
        array[0x35] = (byte)StatusResistChanceArmorBreak;
        array[0x36] = (byte)StatusResistChanceMentalBreak;
        array[0x37] = (byte)StatusResistChanceConfuse;
        array[0x38] = (byte)StatusResistChanceBerserk;
        array[0x39] = (byte)StatusResistChanceProvoke;
        array[0x3A] = (byte)StatusChanceThreaten;
        array[0x3B] = (byte)StatusResistChanceSleep;
        array[0x3C] = (byte)StatusResistChanceSilence;
        array[0x3D] = (byte)StatusResistChanceDarkness;
        array[0x3E] = (byte)StatusResistChanceShell;
        array[0x3F] = (byte)StatusResistChanceProtect;
        array[0x40] = (byte)StatusResistChanceReflect;
        array[0x41] = (byte)StatusResistChanceNTide;
        array[0x42] = (byte)StatusResistChanceNBlaze;
        array[0x43] = (byte)StatusResistChanceNShock;
        array[0x44] = (byte)StatusResistChanceNFrost;
        array[0x45] = (byte)StatusResistChanceRegen;
        array[0x46] = (byte)StatusResistChanceHaste;
        array[0x47] = (byte)StatusResistChanceSlow;

        BytesHelper.Write2Bytes(array, 0x48, AutoStatusesPermanent);
        BytesHelper.Write2Bytes(array, 0x4A, AutoStatusesTemporal);
        BytesHelper.Write2Bytes(array, 0x4C, AutoStatusesExtra);
        BytesHelper.Write2Bytes(array, 0x4E, ExtraStatusImmunities);

        for (int i = 0; i < 16; i++)
            BytesHelper.Write2Bytes(array, 0x50 + i * 2, CommandList[i]);

        BytesHelper.Write2Bytes(array, 0x70, ForcedAction);
        BytesHelper.Write2Bytes(array, 0x72, MonsterIdx);
        BytesHelper.Write2Bytes(array, 0x74, ModelIdx);
        array[0x76] = (byte)CtbIconType;
        array[0x77] = (byte)DoomCounter;
        BytesHelper.Write2Bytes(array, 0x78, MonsterArenaIdx);
        BytesHelper.Write2Bytes(array, 0x7A, SoundBankRef);
        array[0x7C] = (byte)AlwaysZero7C;
        array[0x7D] = (byte)AlwaysZero7D;
        array[0x7E] = (byte)AlwaysZero7E;
        array[0x7F] = (byte)AlwaysZero7F;

        return array;
    }
}
