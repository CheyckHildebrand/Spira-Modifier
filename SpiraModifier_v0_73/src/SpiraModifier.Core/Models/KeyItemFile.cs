using SpiraModifier.Core.BinaryIO;
using SpiraModifier.Core.Text;

namespace SpiraModifier.Core.Models;

public class KeyItemData
{
    public const int LENGTH = 0x14;

    public int NameOffset { get; set; }
    public int NameKey { get; set; }
    public int SimplifiedNameOffset { get; set; }
    public int SimplifiedNameKey { get; set; }
    public int DescriptionOffset { get; set; }
    public int DescriptionKey { get; set; }
    public int SimplifiedDescriptionOffset { get; set; }
    public int SimplifiedDescriptionKey { get; set; }
    public int IsAlBhedPrimer { get; set; }
    public int AlwaysZero { get; set; }
    public int Unknown12 { get; set; }
    public int Ordering { get; set; }

    public static KeyItemData ReadFromBytes(byte[] bytes, int offset)
    {
        if (offset + LENGTH > bytes.Length)
            throw new ArgumentException("Buffer trop petit pour une entrée important.bin.", nameof(bytes));

        return new KeyItemData
        {
            NameOffset = BytesHelper.Read2Bytes(bytes, offset + 0x00),
            NameKey = BytesHelper.Read2Bytes(bytes, offset + 0x02),
            SimplifiedNameOffset = BytesHelper.Read2Bytes(bytes, offset + 0x04),
            SimplifiedNameKey = BytesHelper.Read2Bytes(bytes, offset + 0x06),
            DescriptionOffset = BytesHelper.Read2Bytes(bytes, offset + 0x08),
            DescriptionKey = BytesHelper.Read2Bytes(bytes, offset + 0x0A),
            SimplifiedDescriptionOffset = BytesHelper.Read2Bytes(bytes, offset + 0x0C),
            SimplifiedDescriptionKey = BytesHelper.Read2Bytes(bytes, offset + 0x0E),
            IsAlBhedPrimer = bytes[offset + 0x10],
            AlwaysZero = bytes[offset + 0x11],
            Unknown12 = bytes[offset + 0x12],
            Ordering = bytes[offset + 0x13],
        };
    }
}

/// <summary>
/// Fichier localisé <c>battle/kernel/important.bin</c> : noms et descriptions
/// des objets clés. Les coffres de takara.bin référencent ces entrées par index local.
/// </summary>
public class KeyItemFile
{
    private readonly List<KeyItemData> _entries = new();
    public IReadOnlyList<KeyItemData> Entries => _entries;
    public byte[] StringsPool { get; private set; } = Array.Empty<byte>();
    public int MinIndex { get; private set; }
    public int MaxIndex { get; private set; }
    public int Count => _entries.Count;

    public static KeyItemFile ReadFromFile(string path)
        => ReadFromBytes(File.ReadAllBytes(path));

    public static KeyItemFile ReadFromBytes(byte[] bytes)
    {
        if (bytes.Length < 0x14)
            throw new InvalidDataException($"Fichier important.bin trop petit ({bytes.Length} octets).");

        var file = new KeyItemFile
        {
            MinIndex = BytesHelper.Read2Bytes(bytes, 0x08),
            MaxIndex = BytesHelper.Read2Bytes(bytes, 0x0A),
        };

        var individualLength = BytesHelper.Read2Bytes(bytes, 0x0C);
        var totalLength = BytesHelper.Read2Bytes(bytes, 0x0E);
        if (individualLength <= 0)
            individualLength = KeyItemData.LENGTH;

        var entryCount = file.MaxIndex - file.MinIndex + 1;
        if (entryCount <= 0 || entryCount * individualLength != totalLength)
            entryCount = totalLength / individualLength;

        var entriesStart = 0x14;
        for (int i = 0; i < entryCount; i++)
        {
            var offset = entriesStart + i * individualLength;
            if (offset + KeyItemData.LENGTH > bytes.Length) break;
            file._entries.Add(KeyItemData.ReadFromBytes(bytes, offset));
        }

        var stringsStart = entriesStart + totalLength;
        if (stringsStart < bytes.Length)
        {
            file.StringsPool = new byte[bytes.Length - stringsStart];
            Array.Copy(bytes, stringsStart, file.StringsPool, 0, file.StringsPool.Length);
        }

        return file;
    }

    public KeyItemData? GetByGlobalId(int globalId)
    {
        var local = globalId & 0x0FFF;
        var relative = local - MinIndex;
        return relative >= 0 && relative < _entries.Count ? _entries[relative] : null;
    }

    public string? GetNameByGlobalId(int globalId, FfxCharset? charset)
    {
        var entry = GetByGlobalId(globalId);
        return entry == null ? null : FfxStringDecoder.Decode(StringsPool, entry.NameOffset, charset);
    }
}
