using SpiraModifier.Core.BinaryIO;
using SpiraModifier.Core.Text;

namespace SpiraModifier.Core.Models;

/// <summary>
/// Fichier monster1/2/3.bin = table centralisée de localisation pour une langue donnée.
/// Contient les noms + sensor + scan + leurs versions "simplified" pour tous les monstres
/// dans la plage [minIndex, maxIndex].
///
/// Format binaire (depuis DataFileReader.toList du parser de Karifean) :
///   0x00-0x07 : header opaque (8 octets)
///   0x08      : minIndex (2 bytes)
///   0x0A      : maxIndex (2 bytes)
///   0x0C      : individualLength (2 bytes) — taille d'une entrée (0x80)
///   0x0E      : totalLength (2 bytes) — taille de toutes les entrées combinées
///   0x10-0x13 : 4 octets skippés
///   0x14...   : entrées de stat (mêmes offsets que MonsterStat pour les textes)
///   ...       : pool de strings partagé
///
/// Dans chaque entrée, les offsets pertinents pour les textes (mêmes qu'en MonsterStat) :
///   0x00 : nameOffset
///   0x04 : sensorTextOffset
///   0x08 : simplifiedSensorTextOffset
///   0x0C : scanTextOffset
///   0x10 : simplifiedScanTextOffset
/// </summary>
public class MonsterLocalizationFile
{
    public int MinIndex { get; private set; }
    public int MaxIndex { get; private set; }
    public byte[] StringsPool { get; private set; } = Array.Empty<byte>();

    private readonly List<byte[]> _entries = new();

    public int EntryCount => _entries.Count;

    public static MonsterLocalizationFile ReadFromFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return ReadFromBytes(bytes);
    }

    public static MonsterLocalizationFile ReadFromBytes(byte[] bytes)
    {
        if (bytes.Length < 0x14)
            throw new InvalidDataException(
                $"Fichier de localisation trop petit ({bytes.Length} octets, 0x14 minimum requis).");

        var file = new MonsterLocalizationFile();

        file.MinIndex   = BytesHelper.Read2Bytes(bytes, 0x08);
        file.MaxIndex   = BytesHelper.Read2Bytes(bytes, 0x0A);
        var indivLength = BytesHelper.Read2Bytes(bytes, 0x0C);
        var totalLength = BytesHelper.Read2Bytes(bytes, 0x0E);
        file._individualLength = indivLength;

        var entryCount = file.MaxIndex - file.MinIndex + 1;
        if (entryCount * indivLength != totalLength && indivLength > 0)
            entryCount = totalLength / indivLength;

        var entriesStart = 0x14;
        for (int i = 0; i < entryCount && entriesStart + (i + 1) * indivLength <= bytes.Length; i++)
        {
            var entryStart = entriesStart + i * indivLength;
            var entry = new byte[indivLength];
            Array.Copy(bytes, entryStart, entry, 0, indivLength);
            file._entries.Add(entry);
        }

        var stringsStart = entriesStart + totalLength;
        if (stringsStart < bytes.Length)
        {
            file.StringsPool = new byte[bytes.Length - stringsStart];
            Array.Copy(bytes, stringsStart, file.StringsPool, 0, file.StringsPool.Length);
        }

        return file;
    }

    public bool ContainsMonsterIndex(int globalIndex) =>
        globalIndex >= MinIndex && globalIndex <= MaxIndex;

    /// <summary>
    /// Retourne les textes localisés (Name + Sensor + Scan + simplifiés) pour un index donné.
    /// </summary>
    public LocalizedMonsterTexts? GetTexts(int relativeIndex, FfxCharset? charset)
    {
        if (relativeIndex < 0 || relativeIndex >= _entries.Count || charset == null)
            return null;

        var entry = _entries[relativeIndex];
        var nameOffset       = BytesHelper.Read2Bytes(entry, 0x00);
        var sensorOffset     = BytesHelper.Read2Bytes(entry, 0x04);
        var simpSensorOffset = BytesHelper.Read2Bytes(entry, 0x08);
        var scanOffset       = BytesHelper.Read2Bytes(entry, 0x0C);
        var simpScanOffset   = BytesHelper.Read2Bytes(entry, 0x10);

        return new LocalizedMonsterTexts
        {
            Name                 = FfxStringDecoder.Decode(StringsPool, nameOffset, charset),
            SensorText           = FfxStringDecoder.Decode(StringsPool, sensorOffset, charset),
            SimplifiedSensorText = FfxStringDecoder.Decode(StringsPool, simpSensorOffset, charset),
            ScanText             = FfxStringDecoder.Decode(StringsPool, scanOffset, charset),
            SimplifiedScanText   = FfxStringDecoder.Decode(StringsPool, simpScanOffset, charset),
        };
    }

    /// <summary>Lookup direct du nom seul (pour la sidebar).</summary>
    public string? GetName(int relativeIndex, FfxCharset? charset) =>
        GetTexts(relativeIndex, charset)?.Name;

    // ============================================================
    // ÉDITION
    // ============================================================

    /// <summary>
    /// True si le fichier a été modifié en mémoire depuis le chargement.
    /// </summary>
    public bool IsDirty { get; private set; }

    /// <summary>Taille d'une entrée (lue depuis le header, typiquement 0x80).</summary>
    private int _individualLength;

    /// <summary>
    /// Met à jour les 5 textes d'un monstre à l'index relatif donné, puis reconstruit
    /// intégralement le pool de strings et tous les offsets+keys selon l'algorithme
    /// exact du parser de Karifean (KeyedString.rebuildKeyedStrings) :
    ///
    ///   1) Toutes les chaînes (5 × N entrées) sont collectées et triées par longueur croissante
    ///   2) Une map de déduplication assigne à chaque chaîne unique :
    ///      - offset = position d'ajout dans le pool
    ///      - key    = index incrémental (0, 1, 2…) partagé entre doublons
    ///   3) Chaque entrée écrit DEUX champs par chaîne :
    ///      - offset (2 octets, à 0x00 / 0x04 / 0x08 / 0x0C / 0x10)
    ///      - key    (2 octets, à 0x02 / 0x06 / 0x0A / 0x0E / 0x12)
    ///
    /// Sans cet algorithme exact, les fichiers générés sont incompatibles avec le jeu
    /// (taille divergente, lookups internes incohérents).
    /// </summary>
    public bool SetTexts(int relativeIndex, LocalizedMonsterTexts newTexts, FfxCharset charset)
    {
        if (relativeIndex < 0 || relativeIndex >= _entries.Count) return false;

        // Récupère toutes les chaînes (décodées avec tokens)
        var allTexts = new LocalizedMonsterTexts[_entries.Count];
        for (int i = 0; i < _entries.Count; i++)
            allTexts[i] = GetTexts(i, charset) ?? new LocalizedMonsterTexts();

        // Remplace celle de l'utilisateur
        allTexts[relativeIndex] = new LocalizedMonsterTexts
        {
            Name                 = newTexts.Name                 ?? "",
            SensorText           = newTexts.SensorText           ?? "",
            SimplifiedSensorText = newTexts.SimplifiedSensorText ?? "",
            ScanText             = newTexts.ScanText             ?? "",
            SimplifiedScanText   = newTexts.SimplifiedScanText   ?? "",
        };

        RebuildPoolKarifeanStyle(allTexts, charset);
        IsDirty = true;
        return true;
    }

    /// <summary>
    /// Implémentation conforme à <c>KeyedString.rebuildKeyedStrings</c> de Karifean.
    /// Produit le pool de strings + assigne offsets et keys partagés entre doublons.
    /// </summary>
    private void RebuildPoolKarifeanStyle(LocalizedMonsterTexts[] allTexts, FfxCharset charset)
    {
        // 1) Encode chaque chaîne pour connaître sa taille
        //    On garde la string source pour la déduplication par texte (pas par bytes —
        //    deux chaînes textuellement identiques produisent les mêmes bytes).
        //    Total : N×5 chaînes (avec doublons probables).
        var allStrings = new (int EntryIdx, int Field, string Text, byte[] Bytes)[_entries.Count * 5];
        for (int i = 0; i < _entries.Count; i++)
        {
            var t = allTexts[i];
            allStrings[i * 5 + 0] = (i, 0, t.Name                 ?? "", FfxStringEncoder.Encode(t.Name                 ?? "", charset));
            allStrings[i * 5 + 1] = (i, 1, t.SensorText           ?? "", FfxStringEncoder.Encode(t.SensorText           ?? "", charset));
            allStrings[i * 5 + 2] = (i, 2, t.SimplifiedSensorText ?? "", FfxStringEncoder.Encode(t.SimplifiedSensorText ?? "", charset));
            allStrings[i * 5 + 3] = (i, 3, t.ScanText             ?? "", FfxStringEncoder.Encode(t.ScanText             ?? "", charset));
            allStrings[i * 5 + 4] = (i, 4, t.SimplifiedScanText   ?? "", FfxStringEncoder.Encode(t.SimplifiedScanText   ?? "", charset));
        }

        // 2) Trie par longueur croissante de bytes (ordre exact de Karifean)
        var sorted = allStrings.OrderBy(s => s.Bytes.Length).ToArray();

        // 3) Assigne offset + key avec déduplication par texte
        //    Map : texte source → (offset, key)
        var uniqueByText = new Dictionary<string, (int Offset, int Key)>();
        var poolBuilder = new List<byte>();

        // (offset, key) par (entryIdx, field)
        var assigned = new (int Offset, int Key)[_entries.Count, 5];

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

        // 4) Réécrit les 5 paires (offset, key) dans chaque entrée
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            BytesHelper.Write2Bytes(entry, 0x00, assigned[i, 0].Offset);
            BytesHelper.Write2Bytes(entry, 0x02, assigned[i, 0].Key);
            BytesHelper.Write2Bytes(entry, 0x04, assigned[i, 1].Offset);
            BytesHelper.Write2Bytes(entry, 0x06, assigned[i, 1].Key);
            BytesHelper.Write2Bytes(entry, 0x08, assigned[i, 2].Offset);
            BytesHelper.Write2Bytes(entry, 0x0A, assigned[i, 2].Key);
            BytesHelper.Write2Bytes(entry, 0x0C, assigned[i, 3].Offset);
            BytesHelper.Write2Bytes(entry, 0x0E, assigned[i, 3].Key);
            BytesHelper.Write2Bytes(entry, 0x10, assigned[i, 4].Offset);
            BytesHelper.Write2Bytes(entry, 0x12, assigned[i, 4].Key);
            // Le reste de l'entrée (stats à partir de 0x14) est préservé tel quel.
        }

        StringsPool = poolBuilder.ToArray();
    }

    /// <summary>
    /// Sérialise le fichier complet en octets, format conforme à Karifean
    /// (DataWritingManager.dataObjectsToBytes) :
    ///
    ///   0x00 : 0x01   (magic byte — toujours 1)
    ///   0x01-0x07 : 0x00 (padding)
    ///   0x08-0x09 : minIndex (2 bytes LE)
    ///   0x0A-0x0B : maxIndex (2 bytes LE)
    ///   0x0C-0x0D : individualLength (= 0x80 pour MonsterStat)
    ///   0x0E-0x0F : totalLength = entryCount × individualLength
    ///   0x10      : 0x14 (constante — offset de début des entrées)
    ///   0x11-0x13 : 0x00 (padding)
    ///   0x14+     : entrées
    ///   après     : pool de strings
    /// </summary>
    public byte[] WriteToBytes()
    {
        if (_individualLength == 0) _individualLength = 0x80;

        var totalLength = _entries.Count * _individualLength;
        var size = 0x14 + totalLength + StringsPool.Length;
        var output = new byte[size];

        // Header magic : 0x01 puis 7 zéros (algo exact de Karifean)
        output[0x00] = 0x01;
        // 0x01..0x07 restent à zéro (par défaut)

        BytesHelper.Write2Bytes(output, 0x08, MinIndex);
        BytesHelper.Write2Bytes(output, 0x0A, MaxIndex);
        BytesHelper.Write2Bytes(output, 0x0C, _individualLength);
        BytesHelper.Write2Bytes(output, 0x0E, totalLength);

        // 0x10 = 0x14 (constante), 0x11..0x13 = 0
        output[0x10] = 0x14;
        // 0x11..0x13 restent à zéro

        // Entrées
        var cursor = 0x14;
        foreach (var entry in _entries)
        {
            Array.Copy(entry, 0, output, cursor, _individualLength);
            cursor += _individualLength;
        }

        // Pool de strings
        Array.Copy(StringsPool, 0, output, cursor, StringsPool.Length);
        return output;
    }

    /// <summary>Marque le fichier comme sauvegardé (appelé après écriture sur disque).</summary>
    public void MarkClean() => IsDirty = false;
}
