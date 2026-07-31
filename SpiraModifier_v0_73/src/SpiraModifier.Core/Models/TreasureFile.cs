using SpiraModifier.Core.BinaryIO;

namespace SpiraModifier.Core.Models;

public static class TreasureKinds
{
    public const int Gil = 0x00;
    public const int Item = 0x02;
    public const int Gear = 0x05;
    public const int KeyItem = 0x0A;

    public static string GetLabel(int kind) => kind switch
    {
        Gil => "Gils",
        Item => "Objet",
        Gear => "Équipement",
        KeyItem => "Objet clé",
        _ => $"Inconnu 0x{kind:X2}",
    };
}

/// <summary>
/// Entrée de <c>takara.bin</c> : contenu d'un coffre ou trésor obtenu par script.
///
/// Layout Karifean TreasureDataObject :
///   0x00 : kind     (0=gils, 2=item, 5=buki_get, 0x0A=objet clé)
///   0x01 : quantity (pour les gils, quantité * 100)
///   0x02 : type lo
///   0x03 : type hi
/// </summary>
public class TreasureEntry
{
    public const int LENGTH = 0x04;

    public int Index { get; init; }
    public int Kind { get; set; }
    public int Quantity { get; set; }
    public int Type { get; set; }
    public int PayloadOffset { get; init; }

    public int GilAmount => Kind == TreasureKinds.Gil ? Quantity * 100 : 0;

    public static TreasureEntry ReadFromBytes(byte[] bytes, int offset, int index)
    {
        if (offset + LENGTH > bytes.Length)
            throw new ArgumentException("Buffer trop petit pour une entrée takara.bin.", nameof(bytes));

        return new TreasureEntry
        {
            Index = index,
            PayloadOffset = offset,
            Kind = bytes[offset + 0x00],
            Quantity = bytes[offset + 0x01],
            Type = BytesHelper.Read2Bytes(bytes, offset + 0x02),
        };
    }

    public void WriteToBytes(byte[] bytes, int offset)
    {
        if (offset + LENGTH > bytes.Length)
            throw new ArgumentException("Buffer trop petit pour écrire une entrée takara.bin.", nameof(bytes));

        BytesHelper.Write1Byte(bytes, offset + 0x00, Kind);
        BytesHelper.Write1Byte(bytes, offset + 0x01, Quantity);
        BytesHelper.Write2Bytes(bytes, offset + 0x02, Type);
    }
}

/// <summary>
/// Fichier <c>battle/kernel/takara.bin</c>, table globale des contenus de coffres.
/// Les scripts de maps référencent ces entrées par index via obtainTreasure(...).
/// </summary>
public class TreasureFile
{
    private readonly List<TreasureEntry> _entries = new();
    private byte[] _headerBytes = Array.Empty<byte>();
    private int _rawMinIndex;
    private int _rawMaxIndex;
    private int _individualLength;
    private byte[] _tailBytes = Array.Empty<byte>();

    public IReadOnlyList<TreasureEntry> Entries => _entries;
    public int Count => _entries.Count;
    public int MinIndex { get; private set; }
    public int MaxIndex { get; private set; }
    public bool IsDirty { get; private set; }

    public static TreasureFile ReadFromFile(string path)
        => ReadFromBytes(File.ReadAllBytes(path));

    public static TreasureFile ReadFromBytes(byte[] bytes)
    {
        if (bytes.Length < 0x14)
            throw new InvalidDataException($"Fichier takara.bin trop petit ({bytes.Length} octets).");

        var file = new TreasureFile();
        file._headerBytes = new byte[0x14];
        Array.Copy(bytes, 0, file._headerBytes, 0, 0x14);

        file._rawMinIndex = BytesHelper.Read2Bytes(bytes, 0x08);
        file._rawMaxIndex = BytesHelper.Read2Bytes(bytes, 0x0A);
        file.MinIndex = file._rawMinIndex;
        file.MaxIndex = file._rawMaxIndex;

        var indivLength = BytesHelper.Read2Bytes(bytes, 0x0C);
        var totalLength = BytesHelper.Read2Bytes(bytes, 0x0E);
        file._individualLength = indivLength > 0 ? indivLength : TreasureEntry.LENGTH;

        var entryCount = file.MaxIndex - file.MinIndex + 1;
        if (entryCount <= 0 || entryCount * file._individualLength != totalLength)
            entryCount = file._individualLength > 0 ? totalLength / file._individualLength : 0;

        var entriesStart = 0x14;
        for (int i = 0; i < entryCount; i++)
        {
            var offset = entriesStart + i * file._individualLength;
            if (offset + TreasureEntry.LENGTH > bytes.Length) break;

            file._entries.Add(TreasureEntry.ReadFromBytes(bytes, offset, file.MinIndex + i));
        }

        var tailStart = entriesStart + totalLength;
        if (tailStart < bytes.Length)
        {
            file._tailBytes = new byte[bytes.Length - tailStart];
            Array.Copy(bytes, tailStart, file._tailBytes, 0, file._tailBytes.Length);
        }

        return file;
    }

    public TreasureEntry? GetEntry(int index)
    {
        var relative = index - MinIndex;
        return relative >= 0 && relative < _entries.Count ? _entries[relative] : null;
    }

    public void MarkDirty() => IsDirty = true;
    public void MarkClean() => IsDirty = false;

    public byte[] WriteToBytes()
    {
        if (_individualLength <= 0)
            _individualLength = TreasureEntry.LENGTH;

        var totalLength = _entries.Count * _individualLength;
        var output = new byte[0x14 + totalLength + _tailBytes.Length];

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
        foreach (var entry in _entries)
        {
            entry.WriteToBytes(output, cursor);
            cursor += _individualLength;
        }

        if (_tailBytes.Length > 0)
            Array.Copy(_tailBytes, 0, output, cursor, _tailBytes.Length);

        return output;
    }
}
