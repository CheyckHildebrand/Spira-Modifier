using System.Text;
using SpiraModifier.Core.BinaryIO;

namespace SpiraModifier.Core.Models;

/// <summary>
/// Une formation référencée dans un groupe d'encounter (2 octets).
///
/// Layout :
///   0x00 : id     — index local de formation dans la zone
///   0x01 : weight — poids de tirage (0 si la formation est scriptée/vide)
///
/// Le nom du fichier de bataille correspondant est de la forme "{map}_{id:00}.bin"
/// (ex : "macal_03_07.bin").
/// </summary>
public class EncounterFormation
{
    public const int LENGTH = 0x02;

    public int Id { get; set; }
    public int Weight { get; set; }
    public int PayloadOffset { get; set; }

    /// <summary>Nom du fichier de bataille (ex : "macal_03_07").</summary>
    public string BattleFileName { get; set; } = "";

    public static EncounterFormation ReadFromBytes(byte[] bytes, int offset, string mapName)
    {
        var id = bytes[offset];
        return new EncounterFormation
        {
            Id = id,
            Weight = bytes[offset + 1],
            PayloadOffset = offset,
            BattleFileName = $"{mapName}_{id:00}",
        };
    }
}

/// <summary>
/// Un groupe de formations dans une zone (longueur variable : 0x05 + N × 0x02).
///
/// Layout :
///   0x00 : formationCount (N)
///   0x01-0x02 : battlefield
///   0x03 : danger
///   0x04 : totalWeight
///   0x05+ : N × EncounterFormation
///
/// Distinction RANDOM / SCRIPTED :
///   - totalWeight &gt; 0 → groupe ALÉATOIRE (rencontres de marche, pondérées)
///   - totalWeight == 0 → groupe SCRIPTÉ (combats forcés/boss déclenchés par event)
/// </summary>
public class EncounterGroup
{
    public int PayloadOffset { get; set; }

    /// <summary>Battlefield override (référence un identifiant de terrain de combat).</summary>
    public int Battlefield { get; set; }

    /// <summary>Niveau de danger (taux d'apparition relatif des combats dans cette zone).</summary>
    public int Danger { get; set; }

    /// <summary>Somme des poids de tirage. 0 = groupe scripté (combats déclenchés par event), &gt; 0 = pool aléatoire.</summary>
    public int TotalWeight { get; set; }

    /// <summary>Liste des formations du groupe.</summary>
    public List<EncounterFormation> Formations { get; } = new();

    /// <summary>Type lu à l'ouverture, conservé même si un mod met tous les poids à 0.</summary>
    public bool UsesRandomEncounterPool { get; set; }

    /// <summary>True si ce groupe est un pool aléatoire.</summary>
    public bool IsRandom => UsesRandomEncounterPool;

    /// <summary>True si ce groupe contient des combats scriptés (déclenchés par event).</summary>
    public bool IsScripted => !UsesRandomEncounterPool;

    /// <summary>Probabilité calculée depuis les poids de tirage (0..1), ou 0 si scripté.</summary>
    public double GetProbability(EncounterFormation f)
        => IsRandom && TotalWeight > 0 ? (double)f.Weight / TotalWeight : 0.0;

    public int LengthInBytes => 0x05 + Formations.Count * EncounterFormation.LENGTH;

    public static EncounterGroup ReadFromBytes(byte[] bytes, int offset, string mapName)
    {
        var formationCount = bytes[offset];
        var group = new EncounterGroup
        {
            PayloadOffset = offset,
            Battlefield = BytesHelper.Read2Bytes(bytes, offset + 0x01),
            Danger      = bytes[offset + 0x03],
            TotalWeight = bytes[offset + 0x04],
        };
        group.UsesRandomEncounterPool = group.TotalWeight > 0;
        for (int i = 0; i < formationCount; i++)
        {
            var off = offset + 0x05 + i * EncounterFormation.LENGTH;
            if (off + EncounterFormation.LENGTH > bytes.Length) break;
            group.Formations.Add(EncounterFormation.ReadFromBytes(bytes, off, mapName));
        }
        return group;
    }
}

/// <summary>
/// Une entrée de la table d'encounters = une zone (carte).
/// Header de 0x0E octets dans le chunk 0 de btl.bin :
///   0x00-0x01 : id          — ID interne de la table
///   0x02-0x03 : dataOffset  — offset des données du groupe dans le chunk 1
///   0x04-0x05 : formationOffset
///   0x06-0x0B : map         — nom de la carte (6 octets UTF-8, ex : "macal\0" ou "besaid")
///   0x0C-0x0D : unknown0C
///
/// Au dataOffset du chunk payload :
///   byte 0 : totalFormationCount (toutes formations confondues)
///   byte 1 : groupCount
///   bytes 2+ : groupes (de longueur variable)
/// </summary>
public class EncounterTableEntry
{
    public const int HEADER_LENGTH = 0x0E;

    public int Id { get; set; }
    public string MapName { get; set; } = "";
    public int FormationOffset { get; set; }
    public int Unknown0C { get; set; }
    public int DataOffset { get; set; }
    public int TotalFormationCount { get; set; }
    public List<EncounterGroup> Groups { get; } = new();

    /// <summary>Toutes les formations toutes catégories confondues (aplatissement).</summary>
    public IEnumerable<EncounterFormation> AllFormations
        => Groups.SelectMany(g => g.Formations);

    /// <summary>Groupes aléatoires uniquement.</summary>
    public IEnumerable<EncounterGroup> RandomGroups
        => Groups.Where(g => g.IsRandom);

    /// <summary>Groupes scriptés uniquement.</summary>
    public IEnumerable<EncounterGroup> ScriptedGroups
        => Groups.Where(g => g.IsScripted);

    public bool HasRandom   => Groups.Any(g => g.IsRandom);
    public bool HasScripted => Groups.Any(g => g.IsScripted);

    public int RandomFormationCount   => RandomGroups.Sum(g => g.Formations.Count);
    public int ScriptedFormationCount => ScriptedGroups.Sum(g => g.Formations.Count);

    public static EncounterTableEntry ReadFromBytes(byte[] headerBytes, int headerOffset, byte[] payloadBytes)
    {
        var entry = new EncounterTableEntry
        {
            Id              = BytesHelper.Read2Bytes(headerBytes, headerOffset + 0x00),
            FormationOffset = BytesHelper.Read2Bytes(headerBytes, headerOffset + 0x04),
            Unknown0C       = BytesHelper.Read2Bytes(headerBytes, headerOffset + 0x0C),
        };

        // Nom de la map : 6 octets UTF-8 potentiellement null-terminés
        var mapBytes = new byte[6];
        Array.Copy(headerBytes, headerOffset + 0x06, mapBytes, 0, 6);
        var nullIdx = Array.IndexOf(mapBytes, (byte)0);
        var rawName = Encoding.UTF8.GetString(mapBytes, 0, nullIdx >= 0 ? nullIdx : 6);
        entry.MapName = rawName.TrimEnd('\0', ' ');

        // Lecture des groupes dans le chunk payload
        var dataOffset = BytesHelper.Read2Bytes(headerBytes, headerOffset + 0x02);
        entry.DataOffset = dataOffset;
        if (dataOffset + 2 > payloadBytes.Length) return entry;

        entry.TotalFormationCount = payloadBytes[dataOffset];
        var groupCount            = payloadBytes[dataOffset + 0x01];

        var cursor = dataOffset + 0x02;
        for (int g = 0; g < groupCount; g++)
        {
            if (cursor + 0x05 > payloadBytes.Length) break;
            var group = EncounterGroup.ReadFromBytes(payloadBytes, cursor, entry.MapName);
            entry.Groups.Add(group);
            cursor += group.LengthInBytes;
        }

        return entry;
    }
}

/// <summary>
/// Fichier <c>btl.bin</c> = catalogue de toutes les zones (cartes) du jeu avec
/// leurs tables de rencontres (aléatoires + scriptées).
///
/// Format à chunks : les 4 premiers octets sont opaques (probable magic),
/// puis une table d'offsets de 4 octets à partir de l'offset 0x04 décrivant
/// 2 chunks :
///   - chunk 0 : tableau de headers de 0x0E octets, un par zone
///   - chunk 1 : payload contenant les données des groupes (référencées par dataOffset
///               de chaque header)
///
/// Le fichier n'est PAS localisé (données mécaniques pures, pas de texte).
///
/// Source : DataReadingManager.readEncounterTables + FieldEncounterTableDataObject
/// du parser de Karifean.
/// </summary>
public class EncounterTableFile
{
    private readonly List<EncounterTableEntry> _entries = new();
    private byte[]? _rawBytes;
    private int _payloadOffset;
    private int _payloadEndOffset;

    public IReadOnlyList<EncounterTableEntry> Entries => _entries;
    public int Count => _entries.Count;
    public bool IsDirty { get; private set; }

    public static EncounterTableFile ReadFromFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return ReadFromBytes(bytes);
    }

    public static EncounterTableFile ReadFromBytes(byte[] bytes)
    {
        if (bytes.Length < 0x10)
            throw new InvalidDataException(
                $"Fichier btl.bin trop petit ({bytes.Length} octets).");

        // Table d'offsets de chunks : 3 entrées de 4 octets à partir de l'offset 0x04
        // (2 chunks + un marqueur de fin)
        var off0 = (int)BytesHelper.Read4Bytes(bytes, 0x04);
        var off1 = (int)BytesHelper.Read4Bytes(bytes, 0x08);
        var off2 = (int)BytesHelper.Read4Bytes(bytes, 0x0C);

        // Validation basique
        if (off0 < 0 || off0 >= bytes.Length || off1 <= off0 || off1 > bytes.Length)
            throw new InvalidDataException(
                $"Table d'offsets btl.bin invalide : off0=0x{off0:X}, off1=0x{off1:X}, off2=0x{off2:X}.");

        // off2 peut être 0xFFFFFFFF (marqueur fin) → on prend la fin du fichier
        var off2Safe = (off2 == unchecked((int)0xFFFFFFFF) || off2 > bytes.Length || off2 < off1)
            ? bytes.Length : off2;

        // Extraction des deux chunks
        var headerBytes = new byte[off1 - off0];
        Array.Copy(bytes, off0, headerBytes, 0, headerBytes.Length);

        var payloadBytes = new byte[off2Safe - off1];
        Array.Copy(bytes, off1, payloadBytes, 0, payloadBytes.Length);

        var file = new EncounterTableFile
        {
            _rawBytes = (byte[])bytes.Clone(),
            _payloadOffset = off1,
            _payloadEndOffset = off2Safe,
        };
        var tableCount = headerBytes.Length / EncounterTableEntry.HEADER_LENGTH;
        for (int i = 0; i < tableCount; i++)
        {
            try
            {
                var entry = EncounterTableEntry.ReadFromBytes(
                    headerBytes, i * EncounterTableEntry.HEADER_LENGTH, payloadBytes);
                file._entries.Add(entry);
            }
            catch { /* on saute les entrées corrompues */ }
        }

        return file;
    }

    public void MarkDirty() => IsDirty = true;
    public void MarkClean() => IsDirty = false;

    public void SetRandomGroupChances(EncounterGroup group, IReadOnlyList<int> chances)
    {
        if (group.Formations.Count != chances.Count)
            throw new ArgumentException("Le nombre de chances ne correspond pas au nombre de formations.", nameof(chances));

        var total = chances.Sum();
        if (total is <= 0 or > 0xFF)
            throw new ArgumentOutOfRangeException(nameof(chances), "Le total des chances doit être compris entre 1 et 255.");

        foreach (var matchingGroup in _entries.SelectMany(e => e.Groups)
                     .Where(g => g.PayloadOffset == group.PayloadOffset
                                 && g.Formations.Count == chances.Count))
        {
            for (int i = 0; i < matchingGroup.Formations.Count; i++)
                matchingGroup.Formations[i].Weight = chances[i];
            matchingGroup.TotalWeight = total;
            matchingGroup.UsesRandomEncounterPool = true;
        }

        if (_rawBytes != null)
            PatchGroupIntoBytes(_rawBytes, group);
        IsDirty = true;
    }

    public byte[] WriteToBytes()
    {
        if (_rawBytes == null)
            throw new InvalidOperationException("Fichier btl.bin sans octets source.");

        return (byte[])_rawBytes.Clone();
    }

    private void PatchGroupIntoBytes(byte[] output, EncounterGroup group)
    {
        var groupOffset = _payloadOffset + group.PayloadOffset;
        if (groupOffset < _payloadOffset || groupOffset + 0x05 > _payloadEndOffset)
            return;

        BytesHelper.Write2Bytes(output, groupOffset + 0x01, group.Battlefield);
        BytesHelper.Write1Byte(output, groupOffset + 0x03, group.Danger);
        BytesHelper.Write1Byte(output, groupOffset + 0x04, group.TotalWeight);

        for (int i = 0; i < group.Formations.Count; i++)
        {
            var formation = group.Formations[i];
            var formationOffset = _payloadOffset + formation.PayloadOffset;
            if (formationOffset < _payloadOffset
                || formationOffset + EncounterFormation.LENGTH > _payloadEndOffset)
                continue;

            BytesHelper.Write1Byte(output, formationOffset + 0x00, formation.Id);
            BytesHelper.Write1Byte(output, formationOffset + 0x01, formation.Weight);
        }
    }
}
