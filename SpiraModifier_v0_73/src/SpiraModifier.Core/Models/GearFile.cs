using SpiraModifier.Core.BinaryIO;

namespace SpiraModifier.Core.Models;

/// <summary>
/// Type de fichier d'équipements — détermine la longueur des entrées.
/// </summary>
public enum GearFileKind
{
    /// <summary>weapon.bin — table d'équipements équipables/référencés par ply_save.bin. Entrées 0x16 octets.</summary>
    Weapon,
    /// <summary>buki_get.bin — équipements obtenus en jeu (drops, coffres). Entrées 0x10 octets.</summary>
    BukiGet,
    /// <summary>shop_arms.bin — équipements achetables. Entrées 0x16 octets.</summary>
    ShopArms,
}

/// <summary>
/// Conteneur d'un fichier d'équipements (weapon.bin / buki_get.bin / shop_arms.bin).
///
/// Format binaire : c'est le DataFileReader standard
///   0x00-0x07 : header opaque (8 octets)
///   0x08      : minIndex (2 bytes)
///   0x0A      : maxIndex (2 bytes)
///   0x0C      : individualLength (2 bytes) — 0x10 ou 0x16
///   0x0E      : totalLength (2 bytes)
///   0x10-0x13 : 4 octets skippés
///   0x14...   : entrées (totalLength octets)
/// </summary>
public class GearFile
{
    public GearFileKind Kind { get; }
    public int MinIndex { get; private set; }
    public int MaxIndex { get; private set; }

    private readonly List<GearData> _entries = new();
    private readonly List<byte[]> _entryTailBytes = new();
    public IReadOnlyList<GearData> Entries => _entries;

    public int Count => _entries.Count;
    public bool IsDirty { get; private set; }

    private byte[] _headerBytes = Array.Empty<byte>();
    private int _rawMinIndex;
    private int _rawMaxIndex;
    private int _individualLength;

    public GearFile(GearFileKind kind) => Kind = kind;

    public int EntryLength => Kind == GearFileKind.BukiGet
        ? GearData.LENGTH_BUKI_GET
        : GearData.LENGTH_NORMAL;

    public static GearFile ReadFromFile(string path, GearFileKind kind)
    {
        var bytes = File.ReadAllBytes(path);
        return ReadFromBytes(bytes, kind);
    }

    public static GearFile ReadFromBytes(byte[] bytes, GearFileKind kind)
    {
        if (bytes.Length < 0x14)
            throw new InvalidDataException($"Fichier trop petit ({bytes.Length} octets).");

        var file = new GearFile(kind);
        file._headerBytes = new byte[0x14];
        Array.Copy(bytes, 0, file._headerBytes, 0, 0x14);

        file._rawMinIndex = BytesHelper.Read2Bytes(bytes, 0x08);
        file._rawMaxIndex = BytesHelper.Read2Bytes(bytes, 0x0A);
        file.MinIndex   = file._rawMinIndex;
        file.MaxIndex   = file._rawMaxIndex;
        var indivLength = BytesHelper.Read2Bytes(bytes, 0x0C);
        var totalLength = BytesHelper.Read2Bytes(bytes, 0x0E);
        file._individualLength = indivLength > 0 ? indivLength : file.EntryLength;

        var entryCount = file.MaxIndex - file.MinIndex + 1;
        if (entryCount * indivLength != totalLength && indivLength > 0)
            entryCount = totalLength / indivLength;

        var entriesStart = 0x14;
        var isBukiGet = kind == GearFileKind.BukiGet;
        var modeledLength = isBukiGet ? GearData.LENGTH_BUKI_GET : GearData.LENGTH_NORMAL;
        for (int i = 0; i < entryCount && entriesStart + (i + 1) * file._individualLength <= bytes.Length; i++)
        {
            var entryStart = entriesStart + i * file._individualLength;
            try
            {
                var gear = GearData.ReadFromBytes(bytes, entryStart, isBukiGet);
                file._entries.Add(gear);

                var tailLength = Math.Max(0, file._individualLength - modeledLength);
                var tail = new byte[tailLength];
                if (tailLength > 0)
                    Array.Copy(bytes, entryStart + modeledLength, tail, 0, tailLength);
                file._entryTailBytes.Add(tail);
            }
            catch
            {
                // Entrée corrompue — on saute et on continue
            }
        }

        return file;
    }

    public byte[] WriteToBytes()
    {
        if (_individualLength == 0) _individualLength = EntryLength;

        var totalLength = _entries.Count * _individualLength;
        var output = new byte[0x14 + totalLength];

        if (_headerBytes.Length >= 0x14)
            Array.Copy(_headerBytes, 0, output, 0, 0x14);
        else
            output[0x00] = 0x01;

        BytesHelper.Write2Bytes(output, 0x08, _rawMinIndex);
        BytesHelper.Write2Bytes(output, 0x0A, _rawMaxIndex);
        BytesHelper.Write2Bytes(output, 0x0C, _individualLength);
        BytesHelper.Write2Bytes(output, 0x0E, totalLength);
        output[0x10] = 0x14;

        var cursor = 0x14;
        for (int i = 0; i < _entries.Count; i++)
        {
            var gearBytes = _entries[i].WriteToBytes();
            Array.Copy(gearBytes, 0, output, cursor, Math.Min(_individualLength, gearBytes.Length));
            if (_individualLength > gearBytes.Length
                && i < _entryTailBytes.Count
                && _entryTailBytes[i].Length > 0)
            {
                Array.Copy(_entryTailBytes[i], 0, output, cursor + gearBytes.Length,
                    Math.Min(_entryTailBytes[i].Length, _individualLength - gearBytes.Length));
            }
            cursor += _individualLength;
        }

        return output;
    }

    public void MarkDirty() => IsDirty = true;
    public void MarkClean() => IsDirty = false;
}
