using SpiraModifier.Core.BinaryIO;

namespace SpiraModifier.Core.Models;

/// <summary>
/// Données d'un équipement (arme ou armure).
///
/// Tous les équipements de FFX (joueurs ET Chimères confondus) partagent la même
/// structure binaire. Le byte `Character` (offset 0x04) distingue qui peut l'équiper :
/// les valeurs 0x00-0x06 sont les 7 personnages joueurs, 0x08-0x0F sont les Chimères.
///
/// Les armes des Chimères ne sont pas dans un fichier séparé : elles sont mélangées
/// avec celles des persos dans buki_get.bin / shop_arms.bin.
///
/// Source : GearDataObject.java du parser de Karifean.
/// </summary>
public class GearData
{
    /// <summary>Taille standard d'une entrée d'équipement.</summary>
    public const int LENGTH_NORMAL = 0x16;
    /// <summary>Taille pour buki_get.bin (entrées plus courtes, sans header de noms).</summary>
    public const int LENGTH_BUKI_GET = 0x10;

    public bool IsBukiGet { get; set; }

    // ===== Header (uniquement pour le format normal, pas buki_get) =====
    public int NameIdMaybe1;          // 0x00
    public int NameIdMaybe2;          // 0x01
    public int ExistsByte;            // 0x02
    public int VariousFlags;          // 0x03 : bit 0x04=celestial, 0x08=brotherhood, 0x02=hidden

    // ===== Stats principales =====
    public int BukiPaddingByte;       // 0x03 (buki_get) : normalement 0, conservé tel quel
    public int Character;             // 0x04 (normal) / 0x01 (buki_get) : owner — voir PlayerCharacters
    public int ArmorByte;             // 0x05 / 0x02 : 0 = arme, !=0 = armure
    public int EquipperByte1;         // 0x06 / -
    public int EquipperByte2;         // 0x07 / -
    public int Formula;               // 0x08 / 0x04 : ID de formule de dégâts
    public int Power;                 // 0x09 / 0x05 : puissance
    public int Crit;                  // 0x0A / 0x06 : chance de critique en %
    public int Slots;                 // 0x0B / 0x07 : nombre d'aptitudes max (1-4)
    public int ModelIdx;              // 0x0C-0x0D : modèle 3D (uniquement format normal)
    public int Ability1;              // 0x0E / 0x08 (2 bytes)
    public int Ability2;              // 0x10 / 0x0A (2 bytes)
    public int Ability3;              // 0x12 / 0x0C (2 bytes)
    public int Ability4;              // 0x14 / 0x0E (2 bytes)

    // ===== Flags décodés depuis VariousFlags =====
    public bool Flag1;                // bit 0x01
    public bool HiddenInMenu;         // bit 0x02
    public bool Celestial;            // bit 0x04 — armes ultimes
    public bool Brotherhood;          // bit 0x08 — Brotherhood spécifiquement
    public bool UnknownFlagSet;       // bit 0x10+

    /// <summary>True si c'est une armure, false si c'est une arme.</summary>
    public bool IsArmor => ArmorByte != 0;

    /// <summary>Liste des 4 aptitudes (avec les zéros = slots vides).</summary>
    public int[] Abilities => new[] { Ability1, Ability2, Ability3, Ability4 };

    /// <summary>Aptitudes effectivement actives (slots non-vides).</summary>
    public IEnumerable<int> ActiveAbilities => Abilities.Where(a => a != 0);

    /// <summary>
    /// Lit une entrée GearData depuis un buffer.
    /// </summary>
    public static GearData ReadFromBytes(byte[] bytes, int offset, bool isBukiGet)
    {
        var requiredLength = isBukiGet ? LENGTH_BUKI_GET : LENGTH_NORMAL;
        if (bytes.Length < offset + requiredLength)
            throw new ArgumentException(
                $"Buffer trop petit : {bytes.Length - offset} disponibles, {requiredLength} requis.");

        var g = new GearData { IsBukiGet = isBukiGet };

        if (isBukiGet)
        {
            g.VariousFlags  = bytes[offset + 0x00];
            g.Character     = bytes[offset + 0x01];
            g.ArmorByte     = bytes[offset + 0x02];
            g.BukiPaddingByte = bytes[offset + 0x03];
            g.Formula       = bytes[offset + 0x04];
            g.Power         = bytes[offset + 0x05];
            g.Crit          = bytes[offset + 0x06];
            g.Slots         = bytes[offset + 0x07];
            g.Ability1      = BytesHelper.Read2Bytes(bytes, offset + 0x08);
            g.Ability2      = BytesHelper.Read2Bytes(bytes, offset + 0x0A);
            g.Ability3      = BytesHelper.Read2Bytes(bytes, offset + 0x0C);
            g.Ability4      = BytesHelper.Read2Bytes(bytes, offset + 0x0E);
        }
        else
        {
            g.NameIdMaybe1  = bytes[offset + 0x00];
            g.NameIdMaybe2  = bytes[offset + 0x01];
            g.ExistsByte    = bytes[offset + 0x02];
            g.VariousFlags  = bytes[offset + 0x03];
            g.Character     = bytes[offset + 0x04];
            g.ArmorByte     = bytes[offset + 0x05];
            g.EquipperByte1 = bytes[offset + 0x06];
            g.EquipperByte2 = bytes[offset + 0x07];
            g.Formula       = bytes[offset + 0x08];
            g.Power         = bytes[offset + 0x09];
            g.Crit          = bytes[offset + 0x0A];
            g.Slots         = bytes[offset + 0x0B];
            g.ModelIdx      = BytesHelper.Read2Bytes(bytes, offset + 0x0C);
            g.Ability1      = BytesHelper.Read2Bytes(bytes, offset + 0x0E);
            g.Ability2      = BytesHelper.Read2Bytes(bytes, offset + 0x10);
            g.Ability3      = BytesHelper.Read2Bytes(bytes, offset + 0x12);
            g.Ability4      = BytesHelper.Read2Bytes(bytes, offset + 0x14);
        }

        // Décodage des flags
        g.Flag1          = (g.VariousFlags & 0x01) != 0;
        g.HiddenInMenu   = (g.VariousFlags & 0x02) != 0;
        g.Celestial      = (g.VariousFlags & 0x04) != 0;
        g.Brotherhood    = (g.VariousFlags & 0x08) != 0;
        g.UnknownFlagSet = (g.VariousFlags & 0xF0) != 0;

        return g;
    }

    /// <summary>Réencode l'entrée dans son layout original (0x10 buki_get ou 0x16 normal).</summary>
    public byte[] WriteToBytes()
    {
        var bytes = new byte[IsBukiGet ? LENGTH_BUKI_GET : LENGTH_NORMAL];

        if (IsBukiGet)
        {
            bytes[0x00] = (byte)(VariousFlags & 0xFF);
            bytes[0x01] = (byte)(Character & 0xFF);
            bytes[0x02] = (byte)(ArmorByte & 0xFF);
            bytes[0x03] = (byte)(BukiPaddingByte & 0xFF);
            bytes[0x04] = (byte)(Formula & 0xFF);
            bytes[0x05] = (byte)(Power & 0xFF);
            bytes[0x06] = (byte)(Crit & 0xFF);
            bytes[0x07] = (byte)(Slots & 0xFF);
            BytesHelper.Write2Bytes(bytes, 0x08, Ability1);
            BytesHelper.Write2Bytes(bytes, 0x0A, Ability2);
            BytesHelper.Write2Bytes(bytes, 0x0C, Ability3);
            BytesHelper.Write2Bytes(bytes, 0x0E, Ability4);
        }
        else
        {
            bytes[0x00] = (byte)(NameIdMaybe1 & 0xFF);
            bytes[0x01] = (byte)(NameIdMaybe2 & 0xFF);
            bytes[0x02] = (byte)(ExistsByte & 0xFF);
            bytes[0x03] = (byte)(VariousFlags & 0xFF);
            bytes[0x04] = (byte)(Character & 0xFF);
            bytes[0x05] = (byte)(ArmorByte & 0xFF);
            bytes[0x06] = (byte)(EquipperByte1 & 0xFF);
            bytes[0x07] = (byte)(EquipperByte2 & 0xFF);
            bytes[0x08] = (byte)(Formula & 0xFF);
            bytes[0x09] = (byte)(Power & 0xFF);
            bytes[0x0A] = (byte)(Crit & 0xFF);
            bytes[0x0B] = (byte)(Slots & 0xFF);
            BytesHelper.Write2Bytes(bytes, 0x0C, ModelIdx);
            BytesHelper.Write2Bytes(bytes, 0x0E, Ability1);
            BytesHelper.Write2Bytes(bytes, 0x10, Ability2);
            BytesHelper.Write2Bytes(bytes, 0x12, Ability3);
            BytesHelper.Write2Bytes(bytes, 0x14, Ability4);
        }

        RefreshFlagBooleans();
        return bytes;
    }

    public void SetAbility(int slot, int value)
    {
        switch (slot)
        {
            case 0: Ability1 = value; break;
            case 1: Ability2 = value; break;
            case 2: Ability3 = value; break;
            case 3: Ability4 = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(slot), "Le slot doit être entre 0 et 3.");
        }
    }

    public void RefreshFlagBooleans()
    {
        Flag1          = (VariousFlags & 0x01) != 0;
        HiddenInMenu   = (VariousFlags & 0x02) != 0;
        Celestial      = (VariousFlags & 0x04) != 0;
        Brotherhood    = (VariousFlags & 0x08) != 0;
        UnknownFlagSet = (VariousFlags & 0xF0) != 0;
    }
}
