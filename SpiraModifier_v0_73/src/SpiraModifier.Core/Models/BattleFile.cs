using SpiraModifier.Core.BinaryIO;

namespace SpiraModifier.Core.Models;

/// <summary>
/// Une position 3D dans une scène de combat (16 octets, 4 floats : x, y, z, w).
/// </summary>
public class BattlePosition
{
    public const int LENGTH = 0x10;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float W { get; set; }

    public bool IsZero => X == 0 && Y == 0 && Z == 0 && W == 0;

    public static BattlePosition ReadFromBytes(byte[] bytes, int offset)
        => new()
        {
            X = BytesHelper.ReadFloat(bytes, offset + 0x00),
            Y = BytesHelper.ReadFloat(bytes, offset + 0x04),
            Z = BytesHelper.ReadFloat(bytes, offset + 0x08),
            W = BytesHelper.ReadFloat(bytes, offset + 0x0C),
        };

    public void WriteInto(byte[] bytes, int offset)
    {
        BytesHelper.WriteFloat(bytes, offset + 0x00, X);
        BytesHelper.WriteFloat(bytes, offset + 0x04, Y);
        BytesHelper.WriteFloat(bytes, offset + 0x08, Z);
        BytesHelper.WriteFloat(bytes, offset + 0x0C, W);
    }

    public override string ToString()
        => W != 0
           ? $"({X:0.##}, {Y:0.##}, {Z:0.##}, w={W:0.##})"
           : $"({X:0.##}, {Y:0.##}, {Z:0.##})";
}

/// <summary>
/// Formation d'une scène de combat = les 8 slots de monstres + flags.
///
/// Layout (0x1C octets) — depuis FormationDataObject.java :
///   0x00 : commonVoiceLinesByte (lignes de voix communes activées si &gt; 0)
///   0x01 : unknownByte01 (bit 0x02 = inconnu)
///   0x02 : unknownByte02 (5 bits inconnus 0x01/0x02/0x04/0x08/0x10)
///   0x03 : inWaterByte    (combat sous l'eau si &gt; 0)
///   0x04-0x0B : 8 octets toujours 0
///   0x0C-0x1B : 8 × 2 octets = IDs des monstres (0xFFFF = slot vide)
///
/// Les IDs de monstres sont des indices internes au fichier de bataille — la
/// résolution vers le nom réel passe par chunk de strings localisées + Monster1/2/3.bin.
/// </summary>
public class BattleFormation
{
    public const int LENGTH = 0x1C;

    public byte CommonVoiceLinesByte { get; set; }
    public byte InWaterByte { get; set; }
    public bool CommonVoiceLinesEnabled
    {
        get => CommonVoiceLinesByte > 0;
        set => CommonVoiceLinesByte = (byte)(value ? (CommonVoiceLinesByte > 0 ? CommonVoiceLinesByte : 1) : 0);
    }
    public bool InWater
    {
        get => InWaterByte > 0;
        set => InWaterByte = (byte)(value ? (InWaterByte > 0 ? InWaterByte : 1) : 0);
    }
    public byte UnknownByte01 { get; set; }
    public byte UnknownByte02 { get; set; }

    /// <summary>IDs des monstres dans les 8 slots (0xFFFF = vide).</summary>
    public int[] MonsterIds { get; set; } = new int[8];

    /// <summary>Slots remplis (non-0xFFFF).</summary>
    public IEnumerable<int> NonEmptySlots
    {
        get
        {
            for (int i = 0; i < 8; i++)
                if (MonsterIds[i] != 0xFFFF) yield return i;
        }
    }

    public int MonsterCount => NonEmptySlots.Count();

    public static BattleFormation ReadFromBytes(byte[] bytes, int offset)
    {
        var f = new BattleFormation
        {
            CommonVoiceLinesByte    = bytes[offset + 0x00],
            UnknownByte01           = bytes[offset + 0x01],
            UnknownByte02           = bytes[offset + 0x02],
            InWaterByte             = bytes[offset + 0x03],
        };
        for (int i = 0; i < 8; i++)
            f.MonsterIds[i] = BytesHelper.Read2Bytes(bytes, offset + 0x0C + i * 2);
        return f;
    }

    public void WriteInto(byte[] bytes, int offset)
    {
        bytes[offset + 0x00] = CommonVoiceLinesByte;
        bytes[offset + 0x01] = UnknownByte01;
        bytes[offset + 0x02] = UnknownByte02;
        bytes[offset + 0x03] = InWaterByte;
        for (int i = 0; i < 8; i++)
            BytesHelper.Write2Bytes(bytes, offset + 0x0C + i * 2, MonsterIds[i]);
    }
}

/// <summary>
/// Header d'une zone de combat (0x60 octets, dans le chunk battleAreasPositions).
///
/// Une scène peut avoir plusieurs zones (cas typique : combat avec changement de
/// scène à mi-combat — Sin-Spawn, Yunalesca, etc.).
/// </summary>
public class BattleAreaHeader
{
    public const int LENGTH = 0x60;

    public int AreaCount { get; set; }
    public int PartyPositionCount { get; set; }
    public int AeonPositionCount { get; set; }
    public int MonsterPositionCount { get; set; }
    public int UnknownSubstructCount { get; set; }

    public int OffsetPartyPositions { get; set; }
    public int OffsetPartySwitchPositions { get; set; }
    public int OffsetAeonPositions { get; set; }
    public int OffsetAeonSwitchPositions { get; set; }
    public int OffsetMonsterPositions { get; set; }
    public int OffsetMonsterSwitchPositions { get; set; }
    public int OffsetFinalLocation { get; set; }

    public static BattleAreaHeader ReadFromBytes(byte[] bytes, int offset)
        => new()
        {
            AreaCount                    = bytes[offset + 0x01],
            PartyPositionCount           = bytes[offset + 0x04],
            AeonPositionCount            = bytes[offset + 0x05],
            MonsterPositionCount         = bytes[offset + 0x06],
            UnknownSubstructCount        = bytes[offset + 0x08],
            OffsetPartyPositions         = (int)BytesHelper.Read4Bytes(bytes, offset + 0x10),
            OffsetPartySwitchPositions   = (int)BytesHelper.Read4Bytes(bytes, offset + 0x14),
            OffsetAeonPositions          = (int)BytesHelper.Read4Bytes(bytes, offset + 0x18),
            OffsetAeonSwitchPositions    = (int)BytesHelper.Read4Bytes(bytes, offset + 0x1C),
            OffsetMonsterPositions       = (int)BytesHelper.Read4Bytes(bytes, offset + 0x20),
            OffsetMonsterSwitchPositions = (int)BytesHelper.Read4Bytes(bytes, offset + 0x24),
            OffsetFinalLocation          = (int)BytesHelper.Read4Bytes(bytes, offset + 0x2C),
        };

    public void WriteMonsterPositionFieldsInto(byte[] bytes, int offset)
    {
        BytesHelper.Write1Byte(bytes, offset + 0x06, MonsterPositionCount);
        BytesHelper.Write4Bytes(bytes, offset + 0x20, OffsetMonsterPositions);
    }
}

/// <summary>
/// Toutes les zones (areas) d'une scène de combat, chacune avec ses positions XYZ
/// pour les personnages, Chimères et monstres.
/// </summary>
public class BattleArea
{
    public BattleAreaHeader Header { get; set; } = null!;
    public int HeaderOffset { get; set; }
    public List<BattlePosition> PartyPositions { get; } = new();
    public List<BattlePosition> AeonPositions { get; } = new();
    public List<BattlePosition> MonsterPositions { get; } = new();
    public BattlePosition? FinalPosition { get; set; }

    public static BattleArea ReadFromBytes(byte[] bytes, int headerOffset)
    {
        var area = new BattleArea
        {
            Header = BattleAreaHeader.ReadFromBytes(bytes, headerOffset),
            HeaderOffset = headerOffset,
        };
        var h = area.Header;

        for (int i = 0; i < h.PartyPositionCount; i++)
            if (h.OffsetPartyPositions + (i + 1) * BattlePosition.LENGTH <= bytes.Length)
                area.PartyPositions.Add(BattlePosition.ReadFromBytes(bytes, h.OffsetPartyPositions + i * BattlePosition.LENGTH));
        for (int i = 0; i < h.AeonPositionCount; i++)
            if (h.OffsetAeonPositions + (i + 1) * BattlePosition.LENGTH <= bytes.Length)
                area.AeonPositions.Add(BattlePosition.ReadFromBytes(bytes, h.OffsetAeonPositions + i * BattlePosition.LENGTH));
        for (int i = 0; i < h.MonsterPositionCount; i++)
            if (h.OffsetMonsterPositions + (i + 1) * BattlePosition.LENGTH <= bytes.Length)
                area.MonsterPositions.Add(BattlePosition.ReadFromBytes(bytes, h.OffsetMonsterPositions + i * BattlePosition.LENGTH));

        if (h.OffsetFinalLocation + BattlePosition.LENGTH <= bytes.Length)
            area.FinalPosition = BattlePosition.ReadFromBytes(bytes, h.OffsetFinalLocation);

        return area;
    }

    public void WritePositionsInto(byte[] bytes, int baseOffset)
    {
        WritePositionList(bytes, baseOffset + Header.OffsetPartyPositions, PartyPositions, Header.PartyPositionCount);
        WritePositionList(bytes, baseOffset + Header.OffsetAeonPositions, AeonPositions, Header.AeonPositionCount);
        WritePositionList(bytes, baseOffset + Header.OffsetMonsterPositions, MonsterPositions, Header.MonsterPositionCount);
    }

    private static void WritePositionList(byte[] bytes, int offset, IReadOnlyList<BattlePosition> positions, int count)
    {
        var writableCount = Math.Min(count, positions.Count);
        for (int i = 0; i < writableCount; i++)
        {
            var posOffset = offset + i * BattlePosition.LENGTH;
            if (posOffset < 0 || posOffset + BattlePosition.LENGTH > bytes.Length) break;
            positions[i].WriteInto(bytes, posOffset);
        }
    }
}

/// <summary>
/// Représente le contenu lu d'un fichier <c>battle/btl/{map}/{map}_{NN}.bin</c>.
///
/// Format à chunks (depuis BattleFile.java) :
///   0x00 : chunkCount + 1 (4 octets)
///   0x04+: table d'offsets de chunks (4 octets chacun)
///
/// Chunks attendus :
///   chunk 0 : ATEL script (bytecode IA de la scène — pas parsé ici, gros morceau)
///   chunk 1 : worker mapping
///   chunk 2 : formation (BattleFormation.LENGTH octets)
///   chunk 3 : battle areas + positions
///   chunk 4 : strings JP (ou EN si inpc)
///   chunk 5 : ftcx (textures de fonts ?)
///   chunk 6 : strings EN (rare)
///
/// Le bytecode ATEL n'est PAS extrait ici — on garde juste les octets bruts pour
/// l'instant. Il sera traité dans une passe ultérieure dédiée.
/// </summary>
public class BattleFile
{
    public string FileName { get; private set; } = "";
    public BattleFormation? Formation { get; private set; }
    public List<BattleArea> Areas { get; } = new();
    public bool IsDirty { get; private set; }

    private byte[]? _rawBytes;
    private List<BattleChunk> _chunks = new();

    /// <summary>Octets bruts du script ATEL — pour future extraction.</summary>
    public byte[]? AtelScriptBytes { get; private set; }

    /// <summary>Taille (octets) du script ATEL — proxy d'« complexité IA » de la scène.</summary>
    public int AtelScriptSize => AtelScriptBytes?.Length ?? 0;

    public static BattleFile ReadFromFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var file = ReadFromBytes(bytes);
        file.FileName = Path.GetFileNameWithoutExtension(path);
        return file;
    }

    public static BattleFile ReadFromBytes(byte[] bytes)
    {
        if (bytes.Length < 0x10)
            throw new InvalidDataException($"Fichier battle trop petit ({bytes.Length} octets).");

        var file = new BattleFile
        {
            _rawBytes = (byte[])bytes.Clone(),
        };
        var declaredChunkCount = (int)BytesHelper.Read4Bytes(bytes, 0x00) - 1;
        if (declaredChunkCount <= 0 || declaredChunkCount > 32)
            throw new InvalidDataException($"chunkCount invalide ({declaredChunkCount}).");

        // Table d'offsets de chunks à partir de 0x04, chacun sur 4 octets
        var chunks = ReadChunks(bytes, declaredChunkCount, 0x04);
        file._chunks = chunks;

        // chunk 0 : ATEL script
        if (chunks.Count > 0 && chunks[0].Length > 0)
            file.AtelScriptBytes = chunks[0].Bytes;

        // chunk 2 : formation
        if (chunks.Count > 2 && chunks[2].Length >= BattleFormation.LENGTH)
            file.Formation = BattleFormation.ReadFromBytes(chunks[2].Bytes, 0);

        // chunk 3 : battle areas + positions
        if (chunks.Count > 3 && chunks[3].Length >= BattleAreaHeader.LENGTH)
        {
            var areasBytes = chunks[3].Bytes;
            var firstHeader = BattleAreaHeader.ReadFromBytes(areasBytes, 0);
            var areaCount = Math.Max(1, firstHeader.AreaCount);

            for (int a = 0; a < areaCount; a++)
            {
                var headerOff = a * BattleAreaHeader.LENGTH;
                if (headerOff + BattleAreaHeader.LENGTH > areasBytes.Length) break;
                try { file.Areas.Add(BattleArea.ReadFromBytes(areasBytes, headerOff)); }
                catch { /* on saute les zones corrompues */ }
            }
        }

        return file;
    }

    public void MarkDirty() => IsDirty = true;
    public void MarkClean() => IsDirty = false;

    public byte[] WriteToBytes()
    {
        if (_rawBytes == null)
            throw new InvalidOperationException("Fichier de scène sans octets source.");

        if (_chunks.Count == 0)
            return (byte[])_rawBytes.Clone();

        var chunkBytes = _chunks
            .Select(c => (byte[])c.Bytes.Clone())
            .ToList();

        if (Formation != null && chunkBytes.Count > 2 && chunkBytes[2].Length >= BattleFormation.LENGTH)
        {
            Formation.WriteInto(chunkBytes[2], 0);
        }

        if (Areas.Count > 0 && chunkBytes.Count > 3 && chunkBytes[3].Length >= BattleAreaHeader.LENGTH)
        {
            chunkBytes[3] = BuildAreasChunkBytes(chunkBytes[3]);
        }

        var sameLengths = chunkBytes.Count == _chunks.Count
                          && chunkBytes.Select((bytes, i) => bytes.Length == _chunks[i].Length).All(x => x);
        if (sameLengths)
        {
            var output = (byte[])_rawBytes.Clone();
            for (int i = 0; i < chunkBytes.Count; i++)
            {
                if (_chunks[i].Offset < 0 || chunkBytes[i].Length == 0) continue;
                Array.Copy(chunkBytes[i], 0, output, _chunks[i].Offset, chunkBytes[i].Length);
            }
            return output;
        }

        return RebuildFromChunks(chunkBytes);
    }

    private byte[] BuildAreasChunkBytes(byte[] originalAreasChunk)
    {
        var buffer = new List<byte>(originalAreasChunk);
        var chunk = buffer.ToArray();

        foreach (var area in Areas)
        {
            if (area.HeaderOffset < 0 || area.HeaderOffset + BattleAreaHeader.LENGTH > chunk.Length)
                continue;

            if (area.MonsterPositions.Count > area.Header.MonsterPositionCount)
            {
                var newOffset = buffer.Count;
                foreach (var position in area.MonsterPositions)
                {
                    var posBytes = new byte[BattlePosition.LENGTH];
                    position.WriteInto(posBytes, 0);
                    buffer.AddRange(posBytes);
                }

                area.Header.MonsterPositionCount = area.MonsterPositions.Count;
                area.Header.OffsetMonsterPositions = newOffset;
                chunk = buffer.ToArray();
            }

            area.Header.WriteMonsterPositionFieldsInto(chunk, area.HeaderOffset);
            area.WritePositionsInto(chunk, 0);
        }

        return chunk;
    }

    private static byte[] RebuildFromChunks(IReadOnlyList<byte[]> chunks)
    {
        var headerSize = 0x04 + (chunks.Count + 1) * 0x04;
        var offsets = new int[chunks.Count];
        var totalSize = headerSize;
        for (int i = 0; i < chunks.Count; i++)
        {
            if (chunks[i].Length == 0)
            {
                offsets[i] = 0;
                continue;
            }

            offsets[i] = totalSize;
            totalSize += chunks[i].Length;
        }

        var output = new byte[totalSize];
        BytesHelper.Write4Bytes(output, 0x00, chunks.Count + 1);
        for (int i = 0; i < chunks.Count; i++)
            BytesHelper.Write4Bytes(output, 0x04 + i * 0x04, offsets[i]);
        BytesHelper.Write4Bytes(output, 0x04 + chunks.Count * 0x04, unchecked((int)0xFFFFFFFF));

        for (int i = 0; i < chunks.Count; i++)
        {
            if (chunks[i].Length == 0 || offsets[i] == 0) continue;
            Array.Copy(chunks[i], 0, output, offsets[i], chunks[i].Length);
        }

        return output;
    }

    /// <summary>
    /// Lit la table de chunks à offsets variables.
    /// </summary>
    private static List<BattleChunk> ReadChunks(byte[] bytes, int chunkCount, int tableOffset)
    {
        var offsets = new int[chunkCount + 1];
        var effectiveCount = chunkCount;
        for (int i = 0; i <= chunkCount; i++)
        {
            var off = (int)BytesHelper.Read4Bytes(bytes, tableOffset + i * 4);
            if (off == unchecked((int)0xFFFFFFFF))
            {
                effectiveCount = i - 1;
                break;
            }
            offsets[i] = off;
        }

        var result = new List<BattleChunk>(effectiveCount);
        for (int i = 0; i < effectiveCount; i++)
        {
            var off = offsets[i];
            if (off == 0)
            {
                result.Add(new BattleChunk(Array.Empty<byte>(), -1, 0));
                continue;
            }

            // Trouve la fin : prochain offset valide ou fin du fichier
            var to = bytes.Length;
            for (int j = i + 1; j <= effectiveCount; j++)
            {
                if (offsets[j] >= off) { to = offsets[j]; break; }
            }

            var len = to - off;
            if (off < 0 || off >= bytes.Length || len <= 0)
            {
                result.Add(new BattleChunk(Array.Empty<byte>(), -1, 0));
                continue;
            }
            var data = new byte[len];
            Array.Copy(bytes, off, data, 0, len);
            result.Add(new BattleChunk(data, off, len));
        }
        return result;
    }

    private sealed class BattleChunk
    {
        public BattleChunk(byte[] bytes, int offset, int length)
        {
            Bytes = bytes;
            Offset = offset;
            Length = length;
        }

        public byte[] Bytes { get; }
        public int Offset { get; }
        public int Length { get; }
    }
}
