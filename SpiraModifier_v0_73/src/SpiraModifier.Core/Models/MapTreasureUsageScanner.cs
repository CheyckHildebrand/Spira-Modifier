using System.Text.RegularExpressions;
using SpiraModifier.Core.BinaryIO;

namespace SpiraModifier.Core.Models;

public class MapTreasureUsage
{
    public int TreasureIndex { get; init; }
    public string EventId { get; init; } = "";
    public string MapCode { get; init; } = "";
    public string EventPath { get; init; } = "";
    public string CallName { get; init; } = "";
    public int InstructionOffset { get; init; }

    public string RegionName => MapNameDictionary.GetDisplayName(MapCode);
    public string DisplayName => $"{RegionName} ({EventId}) • {CallName} @ 0x{InstructionOffset:X4}";
}

public static partial class MapTreasureUsageScanner
{
    private static readonly Regex TreasureCallRegex = new(
        @"\b(?<call>obtainTreasure|obtainTreasureSilently|applyBrotherhoodPowerup)\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TreasureArgumentRegex = new(
        @"Treasure\s+#(?<idx>\d+)\s+\[[0-9A-Fa-f]+h\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<MapTreasureUsage> Scan(IReadOnlyDictionary<string, string> eventScriptPaths)
    {
        var usages = new List<MapTreasureUsage>();
        foreach (var (eventId, path) in eventScriptPaths.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                usages.AddRange(ScanEventFile(eventId, path));
            }
            catch
            {
                // Les events sont nombreux et certains peuvent être atypiques : un fichier
                // illisible ne doit pas bloquer l'ouverture du workspace.
            }
        }

        return usages
            .OrderBy(u => u.TreasureIndex)
            .ThenBy(u => u.EventId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(u => u.InstructionOffset)
            .ToList();
    }

    private static IEnumerable<MapTreasureUsage> ScanEventFile(string eventId, string path)
    {
        var bytes = File.ReadAllBytes(path);
        var chunks = FfxChunkedFile.ReadChunks(bytes, assumedChunkCount: 10, chunkOffset: 0x04);
        if (chunks.Count == 0 || chunks[0].Length == 0)
            yield break;

        var script = AtelDecompiler.Decompile(chunks[0]);
        foreach (var instruction in script.Instructions)
        {
            if (instruction.Opcode is not (0xB5 or 0xD8))
                continue;
            if (string.IsNullOrWhiteSpace(instruction.Annotation))
                continue;

            var callMatch = TreasureCallRegex.Match(instruction.Annotation);
            if (!callMatch.Success)
                continue;

            var treasureMatch = TreasureArgumentRegex.Match(instruction.Annotation);
            if (!treasureMatch.Success)
                continue;

            if (!int.TryParse(treasureMatch.Groups["idx"].Value, out var treasureIndex))
                continue;

            yield return new MapTreasureUsage
            {
                TreasureIndex = treasureIndex,
                EventId = eventId,
                MapCode = EventIdToMapCode(eventId),
                EventPath = path,
                CallName = callMatch.Groups["call"].Value,
                InstructionOffset = instruction.Offset,
            };
        }
    }

    private static string EventIdToMapCode(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return "";
        return eventId.Length >= 6 ? eventId[..6] : eventId;
    }
}

internal static class FfxChunkedFile
{
    public static IReadOnlyList<byte[]> ReadChunks(byte[] bytes, int assumedChunkCount, int chunkOffset)
    {
        if (bytes.Length < chunkOffset + 4)
            return Array.Empty<byte[]>();

        var offsets = new int[assumedChunkCount + 1];
        var chunkCount = assumedChunkCount;
        for (int i = 0; i <= assumedChunkCount; i++)
        {
            var tableOffset = chunkOffset + i * 4;
            if (tableOffset + 4 > bytes.Length)
            {
                chunkCount = Math.Max(0, i - 1);
                break;
            }

            var raw = BytesHelper.Read4Bytes(bytes, tableOffset);
            if (raw == 0xFFFFFFFF)
            {
                chunkCount = Math.Max(0, i - 1);
                break;
            }

            offsets[i] = unchecked((int)raw);
        }

        var chunks = new List<byte[]>(chunkCount);
        for (int i = 0; i < chunkCount; i++)
        {
            var offset = offsets[i];
            if (offset == 0 || offset >= bytes.Length)
            {
                chunks.Add(Array.Empty<byte>());
                continue;
            }

            var end = -1;
            for (int j = i + 1; j <= chunkCount; j++)
            {
                if (offsets[j] >= offset)
                {
                    end = offsets[j];
                    break;
                }
            }

            if (end < offset || end > bytes.Length)
                end = bytes.Length;

            var chunk = new byte[end - offset];
            Array.Copy(bytes, offset, chunk, 0, chunk.Length);
            chunks.Add(chunk);
        }

        return chunks;
    }
}
