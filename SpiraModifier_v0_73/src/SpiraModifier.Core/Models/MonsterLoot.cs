using SpiraModifier.Core.BinaryIO;

namespace SpiraModifier.Core.Models;

/// <summary>
/// Chunk loot/récompenses d'un fichier monstre.
/// Layout basé sur MonsterLootDataObject du parser de Karifean.
/// </summary>
public class MonsterLoot
{
    public const int LENGTH = 0x118;
    public const int GearCharacterCount = 7;
    public const int GearAbilitySlotsPerCharacter = 8;

    private readonly byte[] _rawBytes;

    public int Gil { get; set; }
    public int ApNormal { get; set; }
    public int ApOverkill { get; set; }
    public int RonsoRage { get; set; }
    public int DropChancePrimary { get; set; }
    public int DropChanceSecondary { get; set; }
    public int StealChance { get; set; }
    public int DropChanceGear { get; set; }

    public int DropNormalPrimaryCommonItem { get; set; }
    public int DropNormalPrimaryRareItem { get; set; }
    public int DropNormalSecondaryCommonItem { get; set; }
    public int DropNormalSecondaryRareItem { get; set; }
    public int DropNormalPrimaryCommonQty { get; set; }
    public int DropNormalPrimaryRareQty { get; set; }
    public int DropNormalSecondaryCommonQty { get; set; }
    public int DropNormalSecondaryRareQty { get; set; }

    public int DropOverkillPrimaryCommonItem { get; set; }
    public int DropOverkillPrimaryRareItem { get; set; }
    public int DropOverkillSecondaryCommonItem { get; set; }
    public int DropOverkillSecondaryRareItem { get; set; }
    public int DropOverkillPrimaryCommonQty { get; set; }
    public int DropOverkillPrimaryRareQty { get; set; }
    public int DropOverkillSecondaryCommonQty { get; set; }
    public int DropOverkillSecondaryRareQty { get; set; }

    public int StealCommonItem { get; set; }
    public int StealRareItem { get; set; }
    public int StealCommonQty { get; set; }
    public int StealRareQty { get; set; }
    public int BribeItem { get; set; }
    public int BribeQty { get; set; }

    public int GearSlotCountByte { get; set; }
    public int GearDamageFormula { get; set; }
    public int GearCritBonus { get; set; }
    public int GearAttackPower { get; set; }
    public int GearAbilityCountByte { get; set; }
    public int[][] GearWeaponAbilitiesByCharacter { get; } = CreateGearAbilityMatrix();
    public int[][] GearArmorAbilitiesByCharacter { get; } = CreateGearAbilityMatrix();
    public int ZanmatoLevelByte { get; set; }
    public int GilStealByte { get; set; }
    public int MonsterArenaPrice { get; set; }

    private MonsterLoot(byte[] rawBytes)
    {
        _rawBytes = rawBytes;
    }

    public static MonsterLoot Read(byte[] bytes)
    {
        if (bytes.Length < LENGTH)
            throw new InvalidDataException($"Chunk loot trop petit ({bytes.Length} octets, {LENGTH} requis).");

        var raw = new byte[bytes.Length];
        Array.Copy(bytes, raw, bytes.Length);
        var loot = new MonsterLoot(raw);

        loot.Gil = BytesHelper.Read2Bytes(bytes, 0x00);
        loot.ApNormal = BytesHelper.Read2Bytes(bytes, 0x02);
        loot.ApOverkill = BytesHelper.Read2Bytes(bytes, 0x04);
        loot.RonsoRage = BytesHelper.Read2Bytes(bytes, 0x06);
        loot.DropChancePrimary = bytes[0x08];
        loot.DropChanceSecondary = bytes[0x09];
        loot.StealChance = bytes[0x0A];
        loot.DropChanceGear = bytes[0x0B];

        loot.DropNormalPrimaryCommonItem = BytesHelper.Read2Bytes(bytes, 0x0C);
        loot.DropNormalPrimaryRareItem = BytesHelper.Read2Bytes(bytes, 0x0E);
        loot.DropNormalSecondaryCommonItem = BytesHelper.Read2Bytes(bytes, 0x10);
        loot.DropNormalSecondaryRareItem = BytesHelper.Read2Bytes(bytes, 0x12);
        loot.DropNormalPrimaryCommonQty = bytes[0x14];
        loot.DropNormalPrimaryRareQty = bytes[0x15];
        loot.DropNormalSecondaryCommonQty = bytes[0x16];
        loot.DropNormalSecondaryRareQty = bytes[0x17];

        loot.DropOverkillPrimaryCommonItem = BytesHelper.Read2Bytes(bytes, 0x18);
        loot.DropOverkillPrimaryRareItem = BytesHelper.Read2Bytes(bytes, 0x1A);
        loot.DropOverkillSecondaryCommonItem = BytesHelper.Read2Bytes(bytes, 0x1C);
        loot.DropOverkillSecondaryRareItem = BytesHelper.Read2Bytes(bytes, 0x1E);
        loot.DropOverkillPrimaryCommonQty = bytes[0x20];
        loot.DropOverkillPrimaryRareQty = bytes[0x21];
        loot.DropOverkillSecondaryCommonQty = bytes[0x22];
        loot.DropOverkillSecondaryRareQty = bytes[0x23];

        loot.StealCommonItem = BytesHelper.Read2Bytes(bytes, 0x24);
        loot.StealRareItem = BytesHelper.Read2Bytes(bytes, 0x26);
        loot.StealCommonQty = bytes[0x28];
        loot.StealRareQty = bytes[0x29];
        loot.BribeItem = BytesHelper.Read2Bytes(bytes, 0x2A);
        loot.BribeQty = bytes[0x2C];

        loot.GearSlotCountByte = bytes[0x2D];
        loot.GearDamageFormula = bytes[0x2E];
        loot.GearCritBonus = bytes[0x2F];
        loot.GearAttackPower = bytes[0x30];
        loot.GearAbilityCountByte = bytes[0x31];
        for (int chr = 0; chr < GearCharacterCount; chr++)
        {
            var baseOffset = 0x32 + chr * 0x20;
            for (int i = 0; i < GearAbilitySlotsPerCharacter; i++)
            {
                loot.GearWeaponAbilitiesByCharacter[chr][i] = BytesHelper.Read2Bytes(bytes, baseOffset + i * 2);
                loot.GearArmorAbilitiesByCharacter[chr][i] = BytesHelper.Read2Bytes(bytes, baseOffset + 0x10 + i * 2);
            }
        }
        loot.ZanmatoLevelByte = bytes[0x112];
        loot.GilStealByte = bytes[0x113];
        loot.MonsterArenaPrice = BytesHelper.Read4BytesSigned(bytes, 0x114);

        return loot;
    }

    public byte[] WriteToBytes()
    {
        var output = new byte[_rawBytes.Length];
        Array.Copy(_rawBytes, output, _rawBytes.Length);

        BytesHelper.Write2Bytes(output, 0x00, Gil);
        BytesHelper.Write2Bytes(output, 0x02, ApNormal);
        BytesHelper.Write2Bytes(output, 0x04, ApOverkill);
        BytesHelper.Write2Bytes(output, 0x06, RonsoRage);
        output[0x08] = (byte)DropChancePrimary;
        output[0x09] = (byte)DropChanceSecondary;
        output[0x0A] = (byte)StealChance;
        output[0x0B] = (byte)DropChanceGear;

        BytesHelper.Write2Bytes(output, 0x0C, DropNormalPrimaryCommonItem);
        BytesHelper.Write2Bytes(output, 0x0E, DropNormalPrimaryRareItem);
        BytesHelper.Write2Bytes(output, 0x10, DropNormalSecondaryCommonItem);
        BytesHelper.Write2Bytes(output, 0x12, DropNormalSecondaryRareItem);
        output[0x14] = (byte)DropNormalPrimaryCommonQty;
        output[0x15] = (byte)DropNormalPrimaryRareQty;
        output[0x16] = (byte)DropNormalSecondaryCommonQty;
        output[0x17] = (byte)DropNormalSecondaryRareQty;

        BytesHelper.Write2Bytes(output, 0x18, DropOverkillPrimaryCommonItem);
        BytesHelper.Write2Bytes(output, 0x1A, DropOverkillPrimaryRareItem);
        BytesHelper.Write2Bytes(output, 0x1C, DropOverkillSecondaryCommonItem);
        BytesHelper.Write2Bytes(output, 0x1E, DropOverkillSecondaryRareItem);
        output[0x20] = (byte)DropOverkillPrimaryCommonQty;
        output[0x21] = (byte)DropOverkillPrimaryRareQty;
        output[0x22] = (byte)DropOverkillSecondaryCommonQty;
        output[0x23] = (byte)DropOverkillSecondaryRareQty;

        BytesHelper.Write2Bytes(output, 0x24, StealCommonItem);
        BytesHelper.Write2Bytes(output, 0x26, StealRareItem);
        output[0x28] = (byte)StealCommonQty;
        output[0x29] = (byte)StealRareQty;
        BytesHelper.Write2Bytes(output, 0x2A, BribeItem);
        output[0x2C] = (byte)BribeQty;

        output[0x2D] = (byte)GearSlotCountByte;
        output[0x2E] = (byte)GearDamageFormula;
        output[0x2F] = (byte)GearCritBonus;
        output[0x30] = (byte)GearAttackPower;
        output[0x31] = (byte)GearAbilityCountByte;
        for (int chr = 0; chr < GearCharacterCount; chr++)
        {
            var baseOffset = 0x32 + chr * 0x20;
            for (int i = 0; i < GearAbilitySlotsPerCharacter; i++)
            {
                BytesHelper.Write2Bytes(output, baseOffset + i * 2, GearWeaponAbilitiesByCharacter[chr][i]);
                BytesHelper.Write2Bytes(output, baseOffset + 0x10 + i * 2, GearArmorAbilitiesByCharacter[chr][i]);
            }
        }
        output[0x112] = (byte)ZanmatoLevelByte;
        output[0x113] = (byte)GilStealByte;
        BytesHelper.Write4Bytes(output, 0x114, MonsterArenaPrice);

        return output;
    }

    private static int[][] CreateGearAbilityMatrix()
    {
        var matrix = new int[GearCharacterCount][];
        for (int i = 0; i < matrix.Length; i++)
            matrix[i] = new int[GearAbilitySlotsPerCharacter];
        return matrix;
    }
}
