using SpiraModifier.Core.BinaryIO;
using SpiraModifier.Core.Text;

namespace SpiraModifier.Core.Models;

/// <summary>
/// Données initiales des personnages dans ply_save.bin.
/// Format porté depuis PlayerCharStatDataObject du parser de Karifean.
/// </summary>
public class PlayerSaveData
{
    public const int LENGTH = 0x94;

    private readonly byte[] _rawBytes;

    public int NameOffset { get; private set; }
    public int NameKey { get; private set; }

    public int BaseHp { get; set; }
    public int BaseMp { get; set; }
    public int BaseStr { get; set; }
    public int BaseDef { get; set; }
    public int BaseMag { get; set; }
    public int BaseMdf { get; set; }
    public int BaseAgi { get; set; }
    public int BaseLck { get; set; }
    public int BaseEva { get; set; }
    public int BaseAcc { get; set; }

    public int CurrentAp { get; set; }
    public int CurrentHp { get; set; }
    public int CurrentMp { get; set; }
    public int MaxHp { get; set; }
    public int MaxMp { get; set; }

    public int MiscFlags { get; set; }
    public int EquippedWeaponIndex { get; set; }
    public int EquippedArmorIndex { get; set; }

    public int Str { get; set; }
    public int Def { get; set; }
    public int Mag { get; set; }
    public int Mdf { get; set; }
    public int Agi { get; set; }
    public int Lck { get; set; }
    public int Eva { get; set; }
    public int Acc { get; set; }

    public int PoisonDamage { get; set; }
    public int OverdriveMode { get; set; }
    public int OverdriveCurrent { get; set; }
    public int OverdriveMax { get; set; }
    public int SphereLevelsAvailable { get; set; }
    public int SphereLevelsUsed { get; set; }

    public int UnknownByte3D { get; set; }
    public int AbilityField3E { get; set; }
    public int AbilityField42 { get; set; }
    public int AbilityField46 { get; set; }

    public int EncounterCount { get; set; }
    public int KillCount { get; set; }
    public int UnknownInt58 { get; set; }
    public int UnknownInt5C { get; set; }
    public int[] OverdriveModeCounters { get; } = new int[20];
    public int OverdriveModeFlags { get; set; }
    public int UnknownInt8C { get; set; }
    public int UnknownInt90 { get; set; }

    private PlayerSaveData(byte[] rawBytes)
    {
        _rawBytes = rawBytes;
        ReadFields();
    }

    public static PlayerSaveData ReadFromBytes(byte[] bytes, int offset)
    {
        if (offset < 0 || offset + LENGTH > bytes.Length)
            throw new InvalidDataException($"Entrée ply_save.bin tronquée à l'offset 0x{offset:X}.");

        var raw = new byte[LENGTH];
        Array.Copy(bytes, offset, raw, 0, LENGTH);
        return new PlayerSaveData(raw);
    }

    private void ReadFields()
    {
        NameOffset = BytesHelper.Read2Bytes(_rawBytes, 0x00);
        NameKey = BytesHelper.Read2Bytes(_rawBytes, 0x02);

        BaseHp = BytesHelper.Read4BytesSigned(_rawBytes, 0x04);
        BaseMp = BytesHelper.Read4BytesSigned(_rawBytes, 0x08);
        BaseStr = _rawBytes[0x0C];
        BaseDef = _rawBytes[0x0D];
        BaseMag = _rawBytes[0x0E];
        BaseMdf = _rawBytes[0x0F];
        BaseAgi = _rawBytes[0x10];
        BaseLck = _rawBytes[0x11];
        BaseEva = _rawBytes[0x12];
        BaseAcc = _rawBytes[0x13];

        CurrentAp = BytesHelper.Read4BytesSigned(_rawBytes, 0x18);
        CurrentHp = BytesHelper.Read4BytesSigned(_rawBytes, 0x1C);
        CurrentMp = BytesHelper.Read4BytesSigned(_rawBytes, 0x20);
        MaxHp = BytesHelper.Read4BytesSigned(_rawBytes, 0x24);
        MaxMp = BytesHelper.Read4BytesSigned(_rawBytes, 0x28);

        MiscFlags = _rawBytes[0x2C];
        EquippedWeaponIndex = _rawBytes[0x2D];
        EquippedArmorIndex = _rawBytes[0x2E];
        Str = _rawBytes[0x2F];
        Def = _rawBytes[0x30];
        Mag = _rawBytes[0x31];
        Mdf = _rawBytes[0x32];
        Agi = _rawBytes[0x33];
        Lck = _rawBytes[0x34];
        Eva = _rawBytes[0x35];
        Acc = _rawBytes[0x36];

        PoisonDamage = _rawBytes[0x37];
        OverdriveMode = _rawBytes[0x38];
        OverdriveCurrent = _rawBytes[0x39];
        OverdriveMax = _rawBytes[0x3A];
        SphereLevelsAvailable = _rawBytes[0x3B];
        SphereLevelsUsed = _rawBytes[0x3C];

        UnknownByte3D = _rawBytes[0x3D];
        AbilityField3E = BytesHelper.Read4BytesSigned(_rawBytes, 0x3E);
        AbilityField42 = BytesHelper.Read4BytesSigned(_rawBytes, 0x42);
        AbilityField46 = BytesHelper.Read4BytesSigned(_rawBytes, 0x46);

        EncounterCount = BytesHelper.Read4BytesSigned(_rawBytes, 0x50);
        KillCount = BytesHelper.Read4BytesSigned(_rawBytes, 0x54);
        UnknownInt58 = BytesHelper.Read4BytesSigned(_rawBytes, 0x58);
        UnknownInt5C = BytesHelper.Read4BytesSigned(_rawBytes, 0x5C);
        for (var i = 0; i < OverdriveModeCounters.Length; i++)
            OverdriveModeCounters[i] = BytesHelper.Read2Bytes(_rawBytes, 0x60 + i * 2);

        OverdriveModeFlags = BytesHelper.Read4BytesSigned(_rawBytes, 0x88);
        UnknownInt8C = BytesHelper.Read4BytesSigned(_rawBytes, 0x8C);
        UnknownInt90 = BytesHelper.Read4BytesSigned(_rawBytes, 0x90);
    }

    public IReadOnlyList<int> GetLearnedCommandIds()
    {
        var ids = new List<int>();
        AddBitfield(ids, AbilityField3E, 0x3000);
        AddBitfield(ids, AbilityField42, 0x3020);
        AddBitfield(ids, AbilityField46, 0x3040);
        return ids;
    }

    public void SetLearnedCommandIds(IEnumerable<int> commandIds)
    {
        var field3E = 0;
        var field42 = 0;
        var field46 = 0;

        foreach (var id in commandIds.Distinct())
        {
            if (id >= 0x3000 && id <= 0x301F)
                field3E |= 1 << (id - 0x3000);
            else if (id >= 0x3020 && id <= 0x303F)
                field42 |= 1 << (id - 0x3020);
            else if (id >= 0x3040 && id <= 0x305F)
                field46 |= 1 << (id - 0x3040);
        }

        AbilityField3E = field3E;
        AbilityField42 = field42;
        AbilityField46 = field46;
    }

    public void CopyMechanicsFrom(PlayerSaveData source)
    {
        ArgumentNullException.ThrowIfNull(source);

        BaseHp = source.BaseHp;
        BaseMp = source.BaseMp;
        BaseStr = source.BaseStr;
        BaseDef = source.BaseDef;
        BaseMag = source.BaseMag;
        BaseMdf = source.BaseMdf;
        BaseAgi = source.BaseAgi;
        BaseLck = source.BaseLck;
        BaseEva = source.BaseEva;
        BaseAcc = source.BaseAcc;

        CurrentAp = source.CurrentAp;
        CurrentHp = source.CurrentHp;
        CurrentMp = source.CurrentMp;
        MaxHp = source.MaxHp;
        MaxMp = source.MaxMp;

        MiscFlags = source.MiscFlags;
        EquippedWeaponIndex = source.EquippedWeaponIndex;
        EquippedArmorIndex = source.EquippedArmorIndex;

        Str = source.Str;
        Def = source.Def;
        Mag = source.Mag;
        Mdf = source.Mdf;
        Agi = source.Agi;
        Lck = source.Lck;
        Eva = source.Eva;
        Acc = source.Acc;

        PoisonDamage = source.PoisonDamage;
        OverdriveMode = source.OverdriveMode;
        OverdriveCurrent = source.OverdriveCurrent;
        OverdriveMax = source.OverdriveMax;
        SphereLevelsAvailable = source.SphereLevelsAvailable;
        SphereLevelsUsed = source.SphereLevelsUsed;

        UnknownByte3D = source.UnknownByte3D;
        AbilityField3E = source.AbilityField3E;
        AbilityField42 = source.AbilityField42;
        AbilityField46 = source.AbilityField46;

        EncounterCount = source.EncounterCount;
        KillCount = source.KillCount;
        UnknownInt58 = source.UnknownInt58;
        UnknownInt5C = source.UnknownInt5C;
        for (var i = 0; i < OverdriveModeCounters.Length; i++)
            OverdriveModeCounters[i] = source.OverdriveModeCounters[i];

        OverdriveModeFlags = source.OverdriveModeFlags;
        UnknownInt8C = source.UnknownInt8C;
        UnknownInt90 = source.UnknownInt90;
    }

    private static void AddBitfield(List<int> output, int bitfield, int baseId)
    {
        var value = unchecked((uint)bitfield);
        for (var i = 0; i < 32; i++)
        {
            if ((value & (1u << i)) != 0)
                output.Add(baseId + i);
        }
    }

    public byte[] WriteToBytes()
    {
        var output = new byte[LENGTH];
        Array.Copy(_rawBytes, output, LENGTH);

        BytesHelper.Write4Bytes(output, 0x04, BaseHp);
        BytesHelper.Write4Bytes(output, 0x08, BaseMp);
        output[0x0C] = (byte)BaseStr;
        output[0x0D] = (byte)BaseDef;
        output[0x0E] = (byte)BaseMag;
        output[0x0F] = (byte)BaseMdf;
        output[0x10] = (byte)BaseAgi;
        output[0x11] = (byte)BaseLck;
        output[0x12] = (byte)BaseEva;
        output[0x13] = (byte)BaseAcc;

        BytesHelper.Write4Bytes(output, 0x18, CurrentAp);
        BytesHelper.Write4Bytes(output, 0x1C, CurrentHp);
        BytesHelper.Write4Bytes(output, 0x20, CurrentMp);
        BytesHelper.Write4Bytes(output, 0x24, MaxHp);
        BytesHelper.Write4Bytes(output, 0x28, MaxMp);

        output[0x2C] = (byte)MiscFlags;
        output[0x2D] = (byte)EquippedWeaponIndex;
        output[0x2E] = (byte)EquippedArmorIndex;
        output[0x2F] = (byte)Str;
        output[0x30] = (byte)Def;
        output[0x31] = (byte)Mag;
        output[0x32] = (byte)Mdf;
        output[0x33] = (byte)Agi;
        output[0x34] = (byte)Lck;
        output[0x35] = (byte)Eva;
        output[0x36] = (byte)Acc;

        output[0x37] = (byte)PoisonDamage;
        output[0x38] = (byte)OverdriveMode;
        output[0x39] = (byte)OverdriveCurrent;
        output[0x3A] = (byte)OverdriveMax;
        output[0x3B] = (byte)SphereLevelsAvailable;
        output[0x3C] = (byte)SphereLevelsUsed;

        output[0x3D] = (byte)UnknownByte3D;
        BytesHelper.Write4Bytes(output, 0x3E, AbilityField3E);
        BytesHelper.Write4Bytes(output, 0x42, AbilityField42);
        BytesHelper.Write4Bytes(output, 0x46, AbilityField46);

        BytesHelper.Write4Bytes(output, 0x50, EncounterCount);
        BytesHelper.Write4Bytes(output, 0x54, KillCount);
        BytesHelper.Write4Bytes(output, 0x58, UnknownInt58);
        BytesHelper.Write4Bytes(output, 0x5C, UnknownInt5C);
        for (var i = 0; i < OverdriveModeCounters.Length; i++)
            BytesHelper.Write2Bytes(output, 0x60 + i * 2, OverdriveModeCounters[i]);

        BytesHelper.Write4Bytes(output, 0x88, OverdriveModeFlags);
        BytesHelper.Write4Bytes(output, 0x8C, UnknownInt8C);
        BytesHelper.Write4Bytes(output, 0x90, UnknownInt90);
        return output;
    }
}

/// <summary>
/// Conteneur de ply_save.bin : header DataFileReader + entrées + pool de chaînes.
/// </summary>
public class PlayerSaveFile
{
    private readonly List<PlayerSaveData> _entries = new();

    private byte[] _headerBytes = Array.Empty<byte>();
    private byte[] _stringsPool = Array.Empty<byte>();
    private int _rawMinIndex;
    private int _rawMaxIndex;
    private int _individualLength;

    public IReadOnlyList<PlayerSaveData> Entries => _entries;
    public int Count => _entries.Count;
    public int MinIndex { get; private set; }
    public int MaxIndex { get; private set; }
    public bool IsDirty { get; private set; }

    public static PlayerSaveFile ReadFromFile(string path)
        => ReadFromBytes(File.ReadAllBytes(path));

    public static PlayerSaveFile ReadFromBytes(byte[] bytes)
    {
        if (bytes.Length < 0x14)
            throw new InvalidDataException($"ply_save.bin trop petit ({bytes.Length} octets).");

        var file = new PlayerSaveFile
        {
            _headerBytes = bytes[..0x14],
            _rawMinIndex = BytesHelper.Read2Bytes(bytes, 0x08),
            _rawMaxIndex = BytesHelper.Read2Bytes(bytes, 0x0A),
            _individualLength = BytesHelper.Read2Bytes(bytes, 0x0C),
        };

        file.MinIndex = file._rawMinIndex;
        file.MaxIndex = file._rawMaxIndex;

        if (file._individualLength <= 0)
            file._individualLength = PlayerSaveData.LENGTH;

        var totalLength = BytesHelper.Read2Bytes(bytes, 0x0E);
        var entryCount = file.MaxIndex - file.MinIndex + 1;
        if (entryCount <= 0 || entryCount * file._individualLength != totalLength)
            entryCount = totalLength / file._individualLength;

        const int entriesStart = 0x14;
        for (var i = 0; i < entryCount && entriesStart + (i + 1) * file._individualLength <= bytes.Length; i++)
            file._entries.Add(PlayerSaveData.ReadFromBytes(bytes, entriesStart + i * file._individualLength));

        var stringsStart = entriesStart + totalLength;
        if (stringsStart < bytes.Length)
            file._stringsPool = bytes[stringsStart..];

        return file;
    }

    public string? GetName(int relativeIndex, FfxCharset? charset)
    {
        if (relativeIndex < 0 || relativeIndex >= _entries.Count || charset == null)
            return null;

        return FfxStringDecoder.Decode(_stringsPool, _entries[relativeIndex].NameOffset, charset);
    }

    public byte[] WriteToBytes()
    {
        if (_individualLength <= 0)
            _individualLength = PlayerSaveData.LENGTH;

        var totalLength = _entries.Count * _individualLength;
        var output = new byte[0x14 + totalLength + _stringsPool.Length];

        if (_headerBytes.Length >= 0x14)
            Array.Copy(_headerBytes, output, 0x14);
        else
            output[0x00] = 0x01;

        BytesHelper.Write2Bytes(output, 0x08, _rawMinIndex);
        BytesHelper.Write2Bytes(output, 0x0A, _rawMaxIndex);
        BytesHelper.Write2Bytes(output, 0x0C, _individualLength);
        BytesHelper.Write2Bytes(output, 0x0E, totalLength);
        output[0x10] = 0x14;

        var cursor = 0x14;
        foreach (var entry in _entries)
        {
            var entryBytes = entry.WriteToBytes();
            Array.Copy(entryBytes, 0, output, cursor, Math.Min(_individualLength, entryBytes.Length));
            cursor += _individualLength;
        }

        Array.Copy(_stringsPool, 0, output, cursor, _stringsPool.Length);
        return output;
    }

    public void MarkDirty() => IsDirty = true;
    public void MarkClean() => IsDirty = false;
}
