using SpiraModifier.Core.BinaryIO;
using SpiraModifier.Core.Text;

namespace SpiraModifier.Core.Models;

/// <summary>
/// Une entrée de w_name.bin = noms d'arme pour les 7 personnages humains.
///
/// FFX stocke les noms d'armes "par concept" : une entrée correspond à une "famille"
/// d'arme (par exemple un sabre standard) et fournit le nom adapté à chaque
/// personnage qui l'équipe (Brotherhood pour Tidus, Massamune pour Auron, etc.).
///
/// Layout d'une entrée (0x48 octets) :
///   0x00..0x1B : 7 × (offset 2B, key 2B)  → noms complets pour T/Y/A/K/W/L/R
///   0x1C..0x37 : 7 × (offset 2B, key 2B)  → noms simplifiés
///   0x38..0x45 : 7 × 2 bytes              → IDs de modèles 3D par personnage
///   0x46..0x47 : 2 bytes finalBytes        → drapeaux/inconnu
///
/// Les Chimères et Seymour (ids 7..15) n'ont pas d'entrées ici.
/// Source : WeaponNameDataObject.java du parser de Karifean.
/// </summary>
public class WeaponNameEntry
{
    public const int LENGTH = 0x48;
    public const int CHARACTER_COUNT = 7;  // T, Y, A, K, W, L, R

    public int[] NameOffsets { get; } = new int[CHARACTER_COUNT];
    public int[] NameKeys { get; } = new int[CHARACTER_COUNT];
    public int[] SimplifiedNameOffsets { get; } = new int[CHARACTER_COUNT];
    public int[] SimplifiedNameKeys { get; } = new int[CHARACTER_COUNT];
    public int[] ModelIds { get; } = new int[CHARACTER_COUNT];
    public int FinalBytes { get; set; }

    public static WeaponNameEntry ReadFromBytes(byte[] bytes, int offset)
    {
        if (bytes.Length < offset + LENGTH)
            throw new ArgumentException($"Buffer trop petit pour entrée w_name.");
        var entry = new WeaponNameEntry();
        for (int i = 0; i < CHARACTER_COUNT; i++)
        {
            entry.NameOffsets[i]           = BytesHelper.Read2Bytes(bytes, offset + i * 0x04);
            entry.NameKeys[i]              = BytesHelper.Read2Bytes(bytes, offset + i * 0x04 + 0x02);
            entry.SimplifiedNameOffsets[i] = BytesHelper.Read2Bytes(bytes, offset + 0x1C + i * 0x04);
            entry.SimplifiedNameKeys[i]    = BytesHelper.Read2Bytes(bytes, offset + 0x1E + i * 0x04);
            entry.ModelIds[i]              = BytesHelper.Read2Bytes(bytes, offset + 0x38 + i * 0x02);
        }
        entry.FinalBytes = BytesHelper.Read2Bytes(bytes, offset + 0x46);
        return entry;
    }

    /// <summary>
    /// Récupère le nom de l'arme pour un personnage donné (0 = Tidus … 6 = Rikku).
    /// Retourne null si l'index de personnage est hors plage ou si la chaîne est vide.
    /// </summary>
    public string? GetName(int characterId, byte[] stringsPool, FfxCharset? charset)
    {
        if (characterId < 0 || characterId >= CHARACTER_COUNT) return null;
        var s = FfxStringDecoder.Decode(stringsPool, NameOffsets[characterId], charset);
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    public string? GetSimplifiedName(int characterId, byte[] stringsPool, FfxCharset? charset)
    {
        if (characterId < 0 || characterId >= CHARACTER_COUNT) return null;
        var s = FfxStringDecoder.Decode(stringsPool, SimplifiedNameOffsets[characterId], charset);
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    public byte[] WriteToBytes()
    {
        var bytes = new byte[LENGTH];
        for (int i = 0; i < CHARACTER_COUNT; i++)
        {
            BytesHelper.Write2Bytes(bytes, i * 0x04, NameOffsets[i]);
            BytesHelper.Write2Bytes(bytes, i * 0x04 + 0x02, NameKeys[i]);
            BytesHelper.Write2Bytes(bytes, 0x1C + i * 0x04, SimplifiedNameOffsets[i]);
            BytesHelper.Write2Bytes(bytes, 0x1E + i * 0x04, SimplifiedNameKeys[i]);
            BytesHelper.Write2Bytes(bytes, 0x38 + i * 0x02, ModelIds[i]);
        }
        BytesHelper.Write2Bytes(bytes, 0x46, FinalBytes);
        return bytes;
    }
}

/// <summary>
/// Conteneur d'un fichier w_name.bin (un par langue).
/// Format DataFileReader standard : header 0x14 + entrées + pool de strings.
/// </summary>
public class WeaponNameFile
{
    public int MinIndex { get; private set; }
    public int MaxIndex { get; private set; }
    public byte[] StringsPool { get; private set; } = Array.Empty<byte>();

    private readonly List<WeaponNameEntry> _entries = new();
    private readonly List<byte[]> _entryTailBytes = new();
    public IReadOnlyList<WeaponNameEntry> Entries => _entries;
    public int Count => _entries.Count;
    public bool IsDirty { get; private set; }

    private byte[] _headerBytes = Array.Empty<byte>();
    private int _rawMinIndex;
    private int _rawMaxIndex;
    private int _individualLength;

    public static WeaponNameFile ReadFromFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return ReadFromBytes(bytes);
    }

    public static WeaponNameFile ReadFromBytes(byte[] bytes)
    {
        if (bytes.Length < 0x14)
            throw new InvalidDataException($"Fichier w_name.bin trop petit ({bytes.Length} octets).");

        var file = new WeaponNameFile();
        file._headerBytes = new byte[0x14];
        Array.Copy(bytes, 0, file._headerBytes, 0, 0x14);

        file._rawMinIndex = BytesHelper.Read2Bytes(bytes, 0x08);
        file._rawMaxIndex = BytesHelper.Read2Bytes(bytes, 0x0A);
        file.MinIndex   = file._rawMinIndex;
        file.MaxIndex   = file._rawMaxIndex;
        var indivLength = BytesHelper.Read2Bytes(bytes, 0x0C);
        var totalLength = BytesHelper.Read2Bytes(bytes, 0x0E);
        file._individualLength = indivLength > 0 ? indivLength : WeaponNameEntry.LENGTH;

        var entryCount = file.MaxIndex - file.MinIndex + 1;
        if (entryCount * indivLength != totalLength && indivLength > 0)
            entryCount = totalLength / indivLength;

        var entriesStart = 0x14;
        for (int i = 0; i < entryCount && entriesStart + (i + 1) * file._individualLength <= bytes.Length; i++)
        {
            var entryStart = entriesStart + i * file._individualLength;
            try
            {
                file._entries.Add(WeaponNameEntry.ReadFromBytes(bytes, entryStart));

                var tailLength = Math.Max(0, file._individualLength - WeaponNameEntry.LENGTH);
                var tail = new byte[tailLength];
                if (tailLength > 0)
                    Array.Copy(bytes, entryStart + WeaponNameEntry.LENGTH, tail, 0, tailLength);
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
    /// Cherche un nom d'arme par index global (correspond à l'index dans buki_get/shop_arms)
    /// et par ID de personnage humain (0..6).
    /// </summary>
    public string? GetWeaponName(int globalIndex, int characterId, FfxCharset? charset)
    {
        var rel = globalIndex - MinIndex;
        if (rel < 0 || rel >= _entries.Count) return null;
        return _entries[rel].GetName(characterId, StringsPool, charset);
    }

    public WeaponNameTexts? GetTexts(int globalIndex, int characterId, FfxCharset? charset)
    {
        var rel = globalIndex - MinIndex;
        if (rel < 0 || rel >= _entries.Count || characterId < 0
            || characterId >= WeaponNameEntry.CHARACTER_COUNT || charset == null)
            return null;

        var entry = _entries[rel];
        return new WeaponNameTexts
        {
            Name = FfxStringDecoder.Decode(StringsPool, entry.NameOffsets[characterId], charset),
            SimplifiedName = FfxStringDecoder.Decode(StringsPool, entry.SimplifiedNameOffsets[characterId], charset),
        };
    }

    public bool SetTexts(int globalIndex, int characterId, WeaponNameTexts newTexts, FfxCharset charset)
    {
        var rel = globalIndex - MinIndex;
        if (rel < 0 || rel >= _entries.Count || characterId < 0
            || characterId >= WeaponNameEntry.CHARACTER_COUNT)
            return false;

        var names = new string[_entries.Count, WeaponNameEntry.CHARACTER_COUNT];
        var simplified = new string[_entries.Count, WeaponNameEntry.CHARACTER_COUNT];

        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            for (int c = 0; c < WeaponNameEntry.CHARACTER_COUNT; c++)
            {
                names[i, c] = FfxStringDecoder.Decode(StringsPool, entry.NameOffsets[c], charset);
                simplified[i, c] = FfxStringDecoder.Decode(StringsPool, entry.SimplifiedNameOffsets[c], charset);
            }
        }

        names[rel, characterId] = newTexts.Name ?? "";
        simplified[rel, characterId] = newTexts.SimplifiedName ?? "";
        RebuildPoolKarifeanStyle(names, simplified, charset);
        IsDirty = true;
        return true;
    }

    public bool SetModelId(int globalIndex, int characterId, int modelId)
    {
        var rel = globalIndex - MinIndex;
        if (rel < 0 || rel >= _entries.Count || characterId < 0
            || characterId >= WeaponNameEntry.CHARACTER_COUNT)
            return false;

        _entries[rel].ModelIds[characterId] = modelId & 0xFFFF;
        IsDirty = true;
        return true;
    }

    private void RebuildPoolKarifeanStyle(string[,] names, string[,] simplified, FfxCharset charset)
    {
        var allStrings = new List<(int EntryIdx, int CharacterIdx, bool Simple, string Text, byte[] Bytes)>(
            _entries.Count * WeaponNameEntry.CHARACTER_COUNT * 2);

        for (int i = 0; i < _entries.Count; i++)
        {
            for (int c = 0; c < WeaponNameEntry.CHARACTER_COUNT; c++)
            {
                var name = names[i, c] ?? "";
                var simple = simplified[i, c] ?? "";
                allStrings.Add((i, c, false, name, FfxStringEncoder.Encode(name, charset)));
                allStrings.Add((i, c, true, simple, FfxStringEncoder.Encode(simple, charset)));
            }
        }

        var sorted = allStrings.OrderBy(s => s.Bytes.Length).ToArray();
        var uniqueByText = new Dictionary<string, (int Offset, int Key)>(StringComparer.Ordinal);
        var poolBuilder = new List<byte>();
        var assignedNames = new (int Offset, int Key)[_entries.Count, WeaponNameEntry.CHARACTER_COUNT];
        var assignedSimple = new (int Offset, int Key)[_entries.Count, WeaponNameEntry.CHARACTER_COUNT];

        foreach (var s in sorted)
        {
            if (!uniqueByText.TryGetValue(s.Text, out var assigned))
            {
                assigned = (poolBuilder.Count, uniqueByText.Count);
                poolBuilder.AddRange(s.Bytes);
                uniqueByText[s.Text] = assigned;
            }

            if (s.Simple)
                assignedSimple[s.EntryIdx, s.CharacterIdx] = assigned;
            else
                assignedNames[s.EntryIdx, s.CharacterIdx] = assigned;
        }

        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            for (int c = 0; c < WeaponNameEntry.CHARACTER_COUNT; c++)
            {
                entry.NameOffsets[c] = assignedNames[i, c].Offset;
                entry.NameKeys[c] = assignedNames[i, c].Key;
                entry.SimplifiedNameOffsets[c] = assignedSimple[i, c].Offset;
                entry.SimplifiedNameKeys[c] = assignedSimple[i, c].Key;
            }
        }

        StringsPool = poolBuilder.ToArray();
    }

    public byte[] WriteToBytes()
    {
        if (_individualLength == 0) _individualLength = WeaponNameEntry.LENGTH;

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
            var entryBytes = _entries[i].WriteToBytes();
            Array.Copy(entryBytes, 0, output, cursor, Math.Min(_individualLength, entryBytes.Length));
            if (_individualLength > entryBytes.Length
                && i < _entryTailBytes.Count
                && _entryTailBytes[i].Length > 0)
            {
                Array.Copy(_entryTailBytes[i], 0, output, cursor + entryBytes.Length,
                    Math.Min(_entryTailBytes[i].Length, _individualLength - entryBytes.Length));
            }
            cursor += _individualLength;
        }

        Array.Copy(StringsPool, 0, output, cursor, StringsPool.Length);
        return output;
    }

    public void MarkDirty() => IsDirty = true;
    public void MarkClean() => IsDirty = false;
}

public class WeaponNameTexts
{
    public string Name { get; set; } = "";
    public string SimplifiedName { get; set; } = "";
}
