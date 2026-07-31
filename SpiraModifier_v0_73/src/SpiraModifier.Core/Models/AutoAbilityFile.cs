using SpiraModifier.Core.BinaryIO;
using SpiraModifier.Core.Text;

namespace SpiraModifier.Core.Models;

/// <summary>
/// Conteneur de a_ability.bin (un par langue).
/// Format DataFileReader standard : header 0x14 + entrées 0x6C + pool de strings.
///
/// Chaque entrée définit une auto-ability greffable sur les équipements.
/// Les IDs renvoyés correspondent aux valeurs stockées dans GearData.AbilityN.
/// </summary>
public class AutoAbilityFile
{
    public int MinIndex { get; private set; }
    public int MaxIndex { get; private set; }
    public byte[] StringsPool { get; private set; } = Array.Empty<byte>();

    private readonly List<AutoAbilityData> _entries = new();
    private readonly List<byte[]> _entryTailBytes = new();
    public IReadOnlyList<AutoAbilityData> Entries => _entries;
    public int Count => _entries.Count;
    public bool IsDirty { get; private set; }

    private byte[] _headerBytes = Array.Empty<byte>();
    private int _rawMinIndex;
    private int _rawMaxIndex;
    private int _individualLength;

    public static AutoAbilityFile ReadFromFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return ReadFromBytes(bytes);
    }

    public static AutoAbilityFile ReadFromBytes(byte[] bytes)
    {
        if (bytes.Length < 0x14)
            throw new InvalidDataException($"Fichier a_ability.bin trop petit ({bytes.Length} octets).");

        var file = new AutoAbilityFile();
        file._headerBytes = new byte[0x14];
        Array.Copy(bytes, 0, file._headerBytes, 0, 0x14);

        file.MinIndex   = BytesHelper.Read2Bytes(bytes, 0x08);
        file.MaxIndex   = BytesHelper.Read2Bytes(bytes, 0x0A);
        file._rawMinIndex = file.MinIndex;
        file._rawMaxIndex = file.MaxIndex;
        var indivLength = BytesHelper.Read2Bytes(bytes, 0x0C);
        var totalLength = BytesHelper.Read2Bytes(bytes, 0x0E);
        file._individualLength = indivLength > 0 ? indivLength : AutoAbilityData.LENGTH;

        var entryCount = file.MaxIndex - file.MinIndex + 1;
        if (entryCount * indivLength != totalLength && indivLength > 0)
            entryCount = totalLength / indivLength;

        var entriesStart = 0x14;
        for (int i = 0; i < entryCount && entriesStart + (i + 1) * file._individualLength <= bytes.Length; i++)
        {
            var entryStart = entriesStart + i * file._individualLength;
            try
            {
                file._entries.Add(AutoAbilityData.ReadFromBytes(bytes, entryStart));
                var tailLength = Math.Max(0, file._individualLength - AutoAbilityData.LENGTH);
                var tail = new byte[tailLength];
                if (tailLength > 0)
                    Array.Copy(bytes, entryStart + AutoAbilityData.LENGTH, tail, 0, tailLength);
                file._entryTailBytes.Add(tail);
            }
            catch { }
        }

        var stringsStart = entriesStart + totalLength;
        if (stringsStart < bytes.Length)
        {
            file.StringsPool = new byte[bytes.Length - stringsStart];
            Array.Copy(bytes, stringsStart, file.StringsPool, 0, file.StringsPool.Length);
        }
        return file;
    }

    /// <summary>
    /// Cherche une aptitude par son ID global (tel que stocké dans GearData.AbilityN).
    /// Note : les IDs d'aptitudes dans GearData ont souvent le bit 0x8000 mis (ex: 0x801E),
    /// il faut le masquer pour retrouver l'index réel.
    /// </summary>
    public AutoAbilityData? GetByGlobalId(int globalId)
    {
        // GearData stocke les abilities avec le bit 0x8000 mis (flag "active").
        // L'ID réel = globalId & 0x7FFF
        var realId = globalId & 0x7FFF;

        if (realId < MinIndex || realId > MaxIndex) return null;
        var rel = realId - MinIndex;
        return rel < _entries.Count ? _entries[rel] : null;
    }

    public string? GetName(AutoAbilityData ability, FfxCharset? charset) =>
        FfxStringDecoder.Decode(StringsPool, ability.NameOffset, charset);

    public string? GetDescription(AutoAbilityData ability, FfxCharset? charset) =>
        FfxStringDecoder.Decode(StringsPool, ability.DescriptionOffset, charset);

    public string? GetNameByGlobalId(int globalId, FfxCharset? charset)
    {
        var ab = GetByGlobalId(globalId);
        return ab != null ? GetName(ab, charset) : null;
    }

    public AutoAbilityTexts? GetTexts(int relativeIndex, FfxCharset? charset)
    {
        if (relativeIndex < 0 || relativeIndex >= _entries.Count || charset == null)
            return null;

        var ability = _entries[relativeIndex];
        return new AutoAbilityTexts
        {
            Name = FfxStringDecoder.Decode(StringsPool, ability.NameOffset, charset),
            SimplifiedName = FfxStringDecoder.Decode(StringsPool, ability.SimplifiedNameOffset, charset),
            Description = FfxStringDecoder.Decode(StringsPool, ability.DescriptionOffset, charset),
            SimplifiedDescription = FfxStringDecoder.Decode(StringsPool, ability.SimplifiedDescriptionOffset, charset),
        };
    }

    public bool SetTexts(int relativeIndex, AutoAbilityTexts newTexts, FfxCharset charset)
    {
        if (relativeIndex < 0 || relativeIndex >= _entries.Count) return false;

        var allTexts = new AutoAbilityTexts[_entries.Count];
        for (int i = 0; i < _entries.Count; i++)
            allTexts[i] = GetTexts(i, charset) ?? new AutoAbilityTexts();

        allTexts[relativeIndex] = new AutoAbilityTexts
        {
            Name = newTexts.Name ?? "",
            SimplifiedName = newTexts.SimplifiedName ?? "",
            Description = newTexts.Description ?? "",
            SimplifiedDescription = newTexts.SimplifiedDescription ?? "",
        };

        RebuildPoolKarifeanStyle(allTexts, charset);
        IsDirty = true;
        return true;
    }

    private void RebuildPoolKarifeanStyle(AutoAbilityTexts[] allTexts, FfxCharset charset)
    {
        var allStrings = new (int EntryIdx, int Field, string Text, byte[] Bytes)[_entries.Count * 4];
        for (int i = 0; i < _entries.Count; i++)
        {
            var t = allTexts[i];
            allStrings[i * 4 + 0] = (i, 0, t.Name ?? "", FfxStringEncoder.Encode(t.Name ?? "", charset));
            allStrings[i * 4 + 1] = (i, 1, t.SimplifiedName ?? "", FfxStringEncoder.Encode(t.SimplifiedName ?? "", charset));
            allStrings[i * 4 + 2] = (i, 2, t.Description ?? "", FfxStringEncoder.Encode(t.Description ?? "", charset));
            allStrings[i * 4 + 3] = (i, 3, t.SimplifiedDescription ?? "", FfxStringEncoder.Encode(t.SimplifiedDescription ?? "", charset));
        }

        var sorted = allStrings.OrderBy(s => s.Bytes.Length).ToArray();
        var uniqueByText = new Dictionary<string, (int Offset, int Key)>();
        var poolBuilder = new List<byte>();
        var assigned = new (int Offset, int Key)[_entries.Count, 4];

        foreach (var s in sorted)
        {
            if (uniqueByText.TryGetValue(s.Text, out var existing))
            {
                assigned[s.EntryIdx, s.Field] = existing;
            }
            else
            {
                var offset = poolBuilder.Count;
                var key = uniqueByText.Count;
                poolBuilder.AddRange(s.Bytes);
                uniqueByText[s.Text] = (offset, key);
                assigned[s.EntryIdx, s.Field] = (offset, key);
            }
        }

        for (int i = 0; i < _entries.Count; i++)
        {
            var ability = _entries[i];
            ability.NameOffset = assigned[i, 0].Offset;
            ability.NameKey = assigned[i, 0].Key;
            ability.SimplifiedNameOffset = assigned[i, 1].Offset;
            ability.SimplifiedNameKey = assigned[i, 1].Key;
            ability.DescriptionOffset = assigned[i, 2].Offset;
            ability.DescriptionKey = assigned[i, 2].Key;
            ability.SimplifiedDescriptionOffset = assigned[i, 3].Offset;
            ability.SimplifiedDescriptionKey = assigned[i, 3].Key;
        }

        StringsPool = poolBuilder.ToArray();
    }

    public byte[] WriteToBytes()
    {
        if (_individualLength == 0) _individualLength = AutoAbilityData.LENGTH;

        var totalLength = _entries.Count * _individualLength;
        var output = new byte[0x14 + totalLength + StringsPool.Length];

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
            var abilityBytes = _entries[i].WriteToBytes();
            Array.Copy(abilityBytes, 0, output, cursor, Math.Min(_individualLength, abilityBytes.Length));
            if (_individualLength > abilityBytes.Length
                && i < _entryTailBytes.Count
                && _entryTailBytes[i].Length > 0)
            {
                Array.Copy(_entryTailBytes[i], 0, output, cursor + abilityBytes.Length,
                    Math.Min(_entryTailBytes[i].Length, _individualLength - abilityBytes.Length));
            }
            cursor += _individualLength;
        }

        Array.Copy(StringsPool, 0, output, cursor, StringsPool.Length);
        return output;
    }

    public void MarkDirty() => IsDirty = true;
    public void MarkClean() => IsDirty = false;
}

public class AutoAbilityTexts
{
    public string Name { get; set; } = "";
    public string SimplifiedName { get; set; } = "";
    public string Description { get; set; } = "";
    public string SimplifiedDescription { get; set; } = "";
}
