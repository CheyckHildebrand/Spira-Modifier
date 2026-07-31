using SpiraModifier.Core.BinaryIO;
using SpiraModifier.Core.Text;

namespace SpiraModifier.Core.Models;

/// <summary>
/// Type de fichier d'attaque/objet — détermine la longueur de chaque entrée.
/// </summary>
public enum AttackFileKind
{
    /// <summary>command.bin — commandes joueur (0x60 par entrée).</summary>
    PlayerCommand,
    /// <summary>monmagic1.bin / monmagic2.bin — attaques de monstres (0x5C par entrée).</summary>
    MonsterAttack,
    /// <summary>item.bin — objets utilisables (0x5C par entrée).</summary>
    Item,
}

/// <summary>
/// Conteneur d'un fichier d'attaques/objets.
/// Format identique aux fichiers monsterN.bin (DataFileReader du parser) :
///   0x00-0x07 : header (8 octets opaques)
///   0x08      : minIndex (2 bytes)
///   0x0A      : maxIndex (2 bytes)
///   0x0C      : individualLength (2 bytes) — 0x5C ou 0x60
///   0x0E      : totalLength (2 bytes)
///   0x10-0x13 : 4 octets skippés
///   0x14...   : entrées (totalLength octets)
///   ...       : pool de strings
///
/// Contrairement aux fichiers monsterN.bin, les noms et descriptions sont **embarqués
/// dans chaque fichier**, donc chaque langue a sa propre version de monmagic1.bin etc.
/// </summary>
public class AttackFile
{
    public AttackFileKind Kind { get; }
    public int MinIndex { get; private set; }
    public int MaxIndex { get; private set; }
    public byte[] StringsPool { get; private set; } = Array.Empty<byte>();

    private readonly List<AttackData> _attacks = new();
    private readonly List<byte[]> _entryTailBytes = new();
    public IReadOnlyList<AttackData> Attacks => _attacks;

    public int Count => _attacks.Count;
    public bool IsDirty { get; private set; }

    private int _rawMinIndex;
    private int _rawMaxIndex;
    private int _individualLength;

    public AttackFile(AttackFileKind kind)
    {
        Kind = kind;
    }

    /// <summary>Taille d'une entrée pour ce type de fichier.</summary>
    public int EntryLength => Kind == AttackFileKind.PlayerCommand
        ? AttackData.LENGTH_PCCOM
        : AttackData.LENGTH_COM;

    public static AttackFile ReadFromFile(string path, AttackFileKind kind, int globalIdPrefix = 0)
    {
        var bytes = File.ReadAllBytes(path);
        return ReadFromBytes(bytes, kind, globalIdPrefix);
    }

    /// <summary>
    /// Lit un fichier d'attaques/objets/commandes.
    /// </summary>
    /// <param name="globalIdPrefix">
    /// Préfixe à ajouter à minIndex/maxIndex pour passer des indices locaux (du header)
    /// aux IDs globaux croisés (référencés depuis kaizou.bin, monstres, etc).
    /// Convention du parser de Karifean (DataReadingManager.prepareCommandsFromFile) :
    ///   - item.bin     : 0x2000
    ///   - command.bin  : 0x3000
    ///   - monmagic1.bin: 0x4000
    ///   - monmagic2.bin: 0x6000
    /// 0 = pas de préfixe (le header contient déjà des IDs globaux, ou on s'en moque).
    /// </param>
    public static AttackFile ReadFromBytes(byte[] bytes, AttackFileKind kind, int globalIdPrefix = 0)
    {
        if (bytes.Length < 0x14)
            throw new InvalidDataException(
                $"Fichier d'attaques trop petit ({bytes.Length} octets).");

        var file = new AttackFile(kind);
        var rawMin   = BytesHelper.Read2Bytes(bytes, 0x08);
        var rawMax   = BytesHelper.Read2Bytes(bytes, 0x0A);
        file._rawMinIndex = rawMin;
        file._rawMaxIndex = rawMax;

        // Garde défensive : si le header contient déjà des indices préfixés
        // (>= 0x1000), on ne ré-applique pas le préfixe pour éviter de doubler.
        // Cas normal : les fichiers vanilla ont des indices locaux (0..N).
        var effectivePrefix = (rawMin < 0x1000) ? globalIdPrefix : 0;
        file.MinIndex   = rawMin + effectivePrefix;
        file.MaxIndex   = rawMax + effectivePrefix;
        var indivLength = BytesHelper.Read2Bytes(bytes, 0x0C);
        var totalLength = BytesHelper.Read2Bytes(bytes, 0x0E);
        file._individualLength = indivLength;

        var expectedLength = file.EntryLength;
        if (indivLength != expectedLength)
        {
            // Un fichier mal interprété — on continue avec la valeur du header
            // mais on note l'incohérence pour l'UI.
        }

        var entryCount = file.MaxIndex - file.MinIndex + 1;
        if (entryCount * indivLength != totalLength && indivLength > 0)
            entryCount = totalLength / indivLength;

        var entriesStart = 0x14;
        var isPC = kind == AttackFileKind.PlayerCommand;
        var modeledLength = isPC ? AttackData.LENGTH_PCCOM : AttackData.LENGTH_COM;
        for (int i = 0; i < entryCount && entriesStart + (i + 1) * indivLength <= bytes.Length; i++)
        {
            var entryStart = entriesStart + i * indivLength;
            var attack = AttackData.ReadFromBytes(bytes, entryStart, isPC);
            file._attacks.Add(attack);

            var tailLength = Math.Max(0, indivLength - modeledLength);
            var tail = new byte[tailLength];
            if (tailLength > 0)
                Array.Copy(bytes, entryStart + modeledLength, tail, 0, tailLength);
            file._entryTailBytes.Add(tail);
        }

        var stringsStart = entriesStart + totalLength;
        if (stringsStart < bytes.Length)
        {
            file.StringsPool = new byte[bytes.Length - stringsStart];
            Array.Copy(bytes, stringsStart, file.StringsPool, 0, file.StringsPool.Length);
        }

        return file;
    }

    public AttackTexts? GetTexts(int relativeIndex, FfxCharset? charset)
    {
        if (relativeIndex < 0 || relativeIndex >= _attacks.Count || charset == null)
            return null;

        var attack = _attacks[relativeIndex];
        return new AttackTexts
        {
            Name = FfxStringDecoder.Decode(StringsPool, attack.NameOffset, charset),
            SimplifiedName = FfxStringDecoder.Decode(StringsPool, attack.SimplifiedNameOffset, charset),
            Description = FfxStringDecoder.Decode(StringsPool, attack.DescriptionOffset, charset),
            SimplifiedDescription = FfxStringDecoder.Decode(StringsPool, attack.SimplifiedDescriptionOffset, charset),
        };
    }

    public bool SetTexts(int relativeIndex, AttackTexts newTexts, FfxCharset charset)
    {
        if (relativeIndex < 0 || relativeIndex >= _attacks.Count) return false;

        var allTexts = new AttackTexts[_attacks.Count];
        for (int i = 0; i < _attacks.Count; i++)
            allTexts[i] = GetTexts(i, charset) ?? new AttackTexts();

        allTexts[relativeIndex] = new AttackTexts
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

    private void RebuildPoolKarifeanStyle(AttackTexts[] allTexts, FfxCharset charset)
    {
        var allStrings = new (int EntryIdx, int Field, string Text, byte[] Bytes)[_attacks.Count * 4];
        for (int i = 0; i < _attacks.Count; i++)
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
        var assigned = new (int Offset, int Key)[_attacks.Count, 4];

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

        for (int i = 0; i < _attacks.Count; i++)
        {
            var attack = _attacks[i];
            attack.NameOffset = assigned[i, 0].Offset;
            attack.NameKey = assigned[i, 0].Key;
            attack.SimplifiedNameOffset = assigned[i, 1].Offset;
            attack.SimplifiedNameKey = assigned[i, 1].Key;
            attack.DescriptionOffset = assigned[i, 2].Offset;
            attack.DescriptionKey = assigned[i, 2].Key;
            attack.SimplifiedDescriptionOffset = assigned[i, 3].Offset;
            attack.SimplifiedDescriptionKey = assigned[i, 3].Key;
        }

        StringsPool = poolBuilder.ToArray();
    }

    /// <summary>Récupère le nom d'une entrée à un index relatif (0..Count-1).</summary>
    public string? GetName(int relativeIndex, FfxCharset? charset)
    {
        if (relativeIndex < 0 || relativeIndex >= _attacks.Count) return null;
        return FfxStringDecoder.Decode(StringsPool, _attacks[relativeIndex].NameOffset, charset);
    }

    /// <summary>Récupère la description d'une entrée à un index relatif.</summary>
    public string? GetDescription(int relativeIndex, FfxCharset? charset)
    {
        if (relativeIndex < 0 || relativeIndex >= _attacks.Count) return null;
        return FfxStringDecoder.Decode(StringsPool, _attacks[relativeIndex].DescriptionOffset, charset);
    }

    /// <summary>Conversion d'un ID global vers un index relatif (ou -1 si hors plage).</summary>
    public int GetRelativeIndex(int globalIndex)
    {
        if (globalIndex < MinIndex || globalIndex > MaxIndex) return -1;
        return globalIndex - MinIndex;
    }

    /// <summary>
    /// Ajoute une nouvelle entrée en clonant une entrée existante du même fichier.
    /// Retourne l'ID global de la nouvelle entrée.
    /// </summary>
    public int AppendCloneOf(int sourceRelativeIndex)
    {
        if (sourceRelativeIndex < 0 || sourceRelativeIndex >= _attacks.Count)
            throw new ArgumentOutOfRangeException(nameof(sourceRelativeIndex));

        if (_individualLength == 0) _individualLength = EntryLength;

        var source = _attacks[sourceRelativeIndex];
        var sourceBytes = source.WriteToBytes();
        var clone = AttackData.ReadFromBytes(sourceBytes, 0, Kind == AttackFileKind.PlayerCommand);
        _attacks.Add(clone);

        if (sourceRelativeIndex < _entryTailBytes.Count && _entryTailBytes[sourceRelativeIndex].Length > 0)
            _entryTailBytes.Add((byte[])_entryTailBytes[sourceRelativeIndex].Clone());
        else
            _entryTailBytes.Add(Array.Empty<byte>());

        _rawMaxIndex = _rawMinIndex + _attacks.Count - 1;
        MaxIndex = MinIndex + _attacks.Count - 1;
        IsDirty = true;
        return MaxIndex;
    }

    public byte[] WriteToBytes()
    {
        if (_individualLength == 0) _individualLength = EntryLength;

        var totalLength = _attacks.Count * _individualLength;
        var size = 0x14 + totalLength + StringsPool.Length;
        var output = new byte[size];

        output[0x00] = 0x01;
        BytesHelper.Write2Bytes(output, 0x08, _rawMinIndex);
        BytesHelper.Write2Bytes(output, 0x0A, _rawMaxIndex);
        BytesHelper.Write2Bytes(output, 0x0C, _individualLength);
        BytesHelper.Write2Bytes(output, 0x0E, totalLength);
        output[0x10] = 0x14;

        var cursor = 0x14;
        for (int i = 0; i < _attacks.Count; i++)
        {
            var attack = _attacks[i];
            var attackBytes = attack.WriteToBytes();
            Array.Copy(attackBytes, 0, output, cursor, Math.Min(_individualLength, attackBytes.Length));
            if (_individualLength > attackBytes.Length
                && i < _entryTailBytes.Count
                && _entryTailBytes[i].Length > 0)
            {
                Array.Copy(_entryTailBytes[i], 0, output, cursor + attackBytes.Length,
                    Math.Min(_entryTailBytes[i].Length, _individualLength - attackBytes.Length));
            }
            cursor += _individualLength;
        }

        Array.Copy(StringsPool, 0, output, cursor, StringsPool.Length);
        return output;
    }

    public void MarkDirty() => IsDirty = true;

    public void MarkClean() => IsDirty = false;
}

public class AttackTexts
{
    public string Name { get; set; } = "";
    public string SimplifiedName { get; set; } = "";
    public string Description { get; set; } = "";
    public string SimplifiedDescription { get; set; } = "";
}
