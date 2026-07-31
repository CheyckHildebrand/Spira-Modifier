using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SpiraModifier.Core.Models;

public sealed class AtelPatchProposal
{
    public string Summary { get; init; } = "";
    public string Risk { get; init; } = "";
    public bool RequiresSceneAtel { get; init; }
    public IReadOnlyList<string> Mechanics { get; init; } = Array.Empty<string>();
    public IReadOnlyList<AtelPatchOperation> Operations { get; init; } = Array.Empty<AtelPatchOperation>();
}

public sealed class AtelPatchOperation
{
    public string Kind { get; init; } = "";
    public int Offset { get; init; }
    public int Length { get; init; }
    public byte[] Bytes { get; init; } = Array.Empty<byte>();
    public string Note { get; init; } = "";
}

public sealed class AtelPatchValidationResult
{
    public bool Success { get; init; }
    public byte[]? PatchedBytes { get; init; }
    public AtelDecompiledScript? BeforeScript { get; init; }
    public AtelDecompiledScript? AfterScript { get; init; }
    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();
    public int OriginalSize { get; init; }
    public int PatchedSize { get; init; }
    public int SizeDelta => PatchedSize - OriginalSize;
}

public static class AtelPatchEngine
{
    private const int MaxAiChunkBytes = 512 * 1024;

    public static bool TryParseProposal(string llmText, out AtelPatchProposal proposal, out string error)
    {
        proposal = new AtelPatchProposal();
        error = "";

        var json = ExtractJsonObject(llmText);
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Aucun objet JSON de patch n'a ete trouve dans la reponse LLM.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var operations = new List<AtelPatchOperation>();

            if (root.TryGetProperty("operations", out var opsElement)
                && opsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var opElement in opsElement.EnumerateArray())
                {
                    if (!TryReadOperation(opElement, out var operation, out error))
                        return false;
                    operations.Add(operation);
                }
            }

            proposal = new AtelPatchProposal
            {
                Summary = ReadString(root, "summary") ?? "",
                Risk = ReadString(root, "risk") ?? "",
                RequiresSceneAtel = ReadBool(root, "requiresSceneAtel"),
                Mechanics = ReadStringArray(root, "mechanics"),
                Operations = operations,
            };
            return true;
        }
        catch (Exception ex)
        {
            error = "JSON de patch invalide : " + ex.Message;
            return false;
        }
    }

    public static AtelPatchValidationResult Validate(MonsterFile monster, AtelPatchProposal proposal)
    {
        var messages = new List<string>();
        if (monster.AiBytes == null || monster.AiBytes.Length == 0)
            return Failure(messages, "Le monstre courant n'a pas de chunk ATEL exploitable.");

        if (proposal.Operations.Count == 0)
        {
            messages.Add("Le LLM n'a propose aucune operation byte-level. Aucune modification n'est appliquee.");
            if (proposal.RequiresSceneAtel)
                messages.Add("La proposition indique que la demande depend probablement aussi d'un ATEL de scene de combat.");
            return new AtelPatchValidationResult
            {
                Success = false,
                Messages = messages,
                OriginalSize = monster.AiBytes.Length,
                PatchedSize = monster.AiBytes.Length,
            };
        }

        AtelDecompiledScript beforeScript;
        try
        {
            beforeScript = AtelDecompiler.Decompile(monster.AiBytes);
        }
        catch (Exception ex)
        {
            return Failure(messages, "Impossible de decompiler l'ATEL original : " + ex.Message);
        }

        if (!TryApplyOperations(monster.AiBytes, proposal.Operations, out var patched, messages))
        {
            return new AtelPatchValidationResult
            {
                Success = false,
                BeforeScript = beforeScript,
                Messages = messages,
                OriginalSize = monster.AiBytes.Length,
                PatchedSize = monster.AiBytes.Length,
            };
        }

        if (patched.Length <= 0 || patched.Length > MaxAiChunkBytes)
        {
            messages.Add($"Taille ATEL candidate refusee : {patched.Length:N0} octets.");
            return new AtelPatchValidationResult
            {
                Success = false,
                BeforeScript = beforeScript,
                Messages = messages,
                OriginalSize = monster.AiBytes.Length,
                PatchedSize = patched.Length,
            };
        }

        AtelDecompiledScript afterScript;
        try
        {
            afterScript = AtelDecompiler.Decompile(patched);
        }
        catch (Exception ex)
        {
            messages.Add("La decompilation du patch candidat echoue : " + ex.Message);
            return new AtelPatchValidationResult
            {
                Success = false,
                BeforeScript = beforeScript,
                Messages = messages,
                OriginalSize = monster.AiBytes.Length,
                PatchedSize = patched.Length,
            };
        }

        var newWarnings = afterScript.Warnings
            .Where(w => !beforeScript.Warnings.Contains(w, StringComparer.Ordinal))
            .ToList();
        if (afterScript.Warnings.Count > beforeScript.Warnings.Count && newWarnings.Count > 0)
        {
            messages.Add("Le patch ajoute des avertissements de decompilation ATEL :");
            foreach (var warning in newWarnings.Take(8))
                messages.Add("- " + warning);
            return new AtelPatchValidationResult
            {
                Success = false,
                BeforeScript = beforeScript,
                AfterScript = afterScript,
                Messages = messages,
                OriginalSize = monster.AiBytes.Length,
                PatchedSize = patched.Length,
            };
        }

        if (!ValidateMonsterRoundtrip(monster, patched, messages))
        {
            return new AtelPatchValidationResult
            {
                Success = false,
                BeforeScript = beforeScript,
                AfterScript = afterScript,
                Messages = messages,
                OriginalSize = monster.AiBytes.Length,
                PatchedSize = patched.Length,
            };
        }

        messages.Add($"Pretest OK : ATEL {monster.AiBytes.Length:N0} -> {patched.Length:N0} octets ({patched.Length - monster.AiBytes.Length:+#;-#;0}).");
        messages.Add($"Decompilation candidate OK : {afterScript.Workers.Count} worker(s), {afterScript.Instructions.Count} instruction(s), {afterScript.Warnings.Count} avertissement(s).");
        messages.Add("Round-trip MonsterFile OK : les pointeurs de chunks sont recalcules et le chunk ATEL relu correspond au candidat.");

        return new AtelPatchValidationResult
        {
            Success = true,
            PatchedBytes = patched,
            BeforeScript = beforeScript,
            AfterScript = afterScript,
            Messages = messages,
            OriginalSize = monster.AiBytes.Length,
            PatchedSize = patched.Length,
        };
    }

    private static bool TryApplyOperations(
        byte[] original,
        IReadOnlyList<AtelPatchOperation> operations,
        out byte[] patched,
        List<string> messages)
    {
        patched = original.ToArray();
        var intervals = new List<(int Start, int End, string Kind)>();
        var insertOffsets = new HashSet<int>();

        foreach (var op in operations)
        {
            var kind = NormalizeKind(op.Kind);
            if (kind == null)
            {
                messages.Add($"Operation inconnue : '{op.Kind}'.");
                return false;
            }

            if (op.Offset < 0 || op.Offset > original.Length)
            {
                messages.Add($"Offset hors limites : 0x{op.Offset:X4}.");
                return false;
            }

            if (kind == "insert")
            {
                if (op.Bytes.Length == 0)
                {
                    messages.Add($"Insertion vide refusee a 0x{op.Offset:X4}.");
                    return false;
                }
                if (!insertOffsets.Add(op.Offset))
                {
                    messages.Add($"Plusieurs insertions au meme offset 0x{op.Offset:X4} : refuse pour eviter l'ordre ambigu.");
                    return false;
                }
                continue;
            }

            if (op.Length <= 0)
            {
                messages.Add($"{kind} a 0x{op.Offset:X4} doit avoir une longueur positive.");
                return false;
            }
            if (op.Offset + op.Length > original.Length)
            {
                messages.Add($"{kind} depasse la taille ATEL : 0x{op.Offset:X4} + {op.Length}.");
                return false;
            }
            if (kind == "replace" && op.Bytes.Length == 0)
            {
                messages.Add($"Remplacement vide refuse a 0x{op.Offset:X4}; utilise delete si c'est voulu.");
                return false;
            }

            intervals.Add((op.Offset, op.Offset + op.Length, kind));
        }

        for (var i = 0; i < intervals.Count; i++)
        {
            for (var j = i + 1; j < intervals.Count; j++)
            {
                if (intervals[i].Start < intervals[j].End && intervals[j].Start < intervals[i].End)
                {
                    messages.Add($"Operations chevauchantes refusees : 0x{intervals[i].Start:X4}-0x{intervals[i].End:X4} et 0x{intervals[j].Start:X4}-0x{intervals[j].End:X4}.");
                    return false;
                }
            }
        }

        var buffer = original.ToList();
        foreach (var op in operations
                     .Select((op, index) => (Operation: op, Index: index))
                     .OrderByDescending(x => x.Operation.Offset)
                     .ThenByDescending(x => x.Index))
        {
            var kind = NormalizeKind(op.Operation.Kind)!;
            var offset = op.Operation.Offset;
            if (kind == "insert")
            {
                buffer.InsertRange(offset, op.Operation.Bytes);
            }
            else if (kind == "replace")
            {
                buffer.RemoveRange(offset, op.Operation.Length);
                buffer.InsertRange(offset, op.Operation.Bytes);
            }
            else
            {
                buffer.RemoveRange(offset, op.Operation.Length);
            }
        }

        patched = buffer.ToArray();
        messages.Add($"Operations appliquees en memoire candidate : {operations.Count}.");
        return true;
    }

    private static bool ValidateMonsterRoundtrip(MonsterFile original, byte[] patchedAi, List<string> messages)
    {
        try
        {
            var clone = new MonsterFile
            {
                AiBytes = patchedAi.ToArray(),
                WorkerBytes = Copy(original.WorkerBytes),
                StatSheet = original.StatSheet,
                StatSheetTextBytes = Copy(original.StatSheetTextBytes),
                SpoilsBytes = Copy(original.SpoilsBytes),
                LootBytes = Copy(original.LootBytes),
                Loot = original.Loot,
                AudioBytes = Copy(original.AudioBytes),
                TextBytes = Copy(original.TextBytes),
                MonsterIndex = original.MonsterIndex,
                ScriptId = original.ScriptId,
            };

            var written = clone.Write();
            var reread = MonsterFile.Read(written, original.MonsterIndex, original.ScriptId);
            if (reread.AiBytes == null || !reread.AiBytes.SequenceEqual(patchedAi))
            {
                messages.Add("Round-trip refuse : le chunk ATEL relu ne correspond pas au candidat.");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            messages.Add("Round-trip MonsterFile refuse : " + ex.Message);
            return false;
        }
    }

    private static byte[]? Copy(byte[]? bytes) => bytes == null ? null : bytes.ToArray();

    private static AtelPatchValidationResult Failure(List<string> messages, string message)
    {
        messages.Add(message);
        return new AtelPatchValidationResult { Success = false, Messages = messages };
    }

    private static bool TryReadOperation(JsonElement element, out AtelPatchOperation operation, out string error)
    {
        operation = new AtelPatchOperation();
        error = "";

        var kind = ReadString(element, "kind") ?? ReadString(element, "type") ?? "";
        if (NormalizeKind(kind) == null)
        {
            error = $"Operation de patch sans type valide : '{kind}'.";
            return false;
        }

        if (!TryReadInt(element, "offset", out var offset) && !TryReadInt(element, "offsetHex", out offset))
        {
            error = "Operation de patch sans offset.";
            return false;
        }

        var length = 0;
        _ = TryReadInt(element, "length", out length) || TryReadInt(element, "lengthHex", out length);

        var bytes = Array.Empty<byte>();
        var bytesText = ReadString(element, "bytes") ?? ReadString(element, "bytesHex") ?? "";
        if (!string.IsNullOrWhiteSpace(bytesText) && !TryParseHexBytes(bytesText, out bytes, out error))
            return false;

        operation = new AtelPatchOperation
        {
            Kind = NormalizeKind(kind)!,
            Offset = offset,
            Length = length,
            Bytes = bytes,
            Note = ReadString(element, "note") ?? "",
        };
        return true;
    }

    private static string? NormalizeKind(string kind)
    {
        kind = kind.Trim().ToLowerInvariant();
        return kind switch
        {
            "insert" or "ins" => "insert",
            "replace" or "repl" or "patch" => "replace",
            "delete" or "del" or "remove" => "delete",
            _ => null,
        };
    }

    private static bool TryParseHexBytes(string text, out byte[] bytes, out string error)
    {
        bytes = Array.Empty<byte>();
        error = "";

        var cleaned = text
            .Replace("0x", "", StringComparison.OrdinalIgnoreCase)
            .Replace(",", " ")
            .Replace(";", " ")
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\t", " ")
            .Trim();

        var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && parts[0].Length > 2)
        {
            if (parts[0].Length % 2 != 0)
            {
                error = "Suite hex impaire dans bytes.";
                return false;
            }
            parts = Enumerable.Range(0, parts[0].Length / 2)
                .Select(i => parts[0].Substring(i * 2, 2))
                .ToArray();
        }

        var result = new byte[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!byte.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result[i]))
            {
                error = $"Octet hex invalide : '{parts[i]}'.";
                return false;
            }
        }

        bytes = result;
        return true;
    }

    private static bool TryReadInt(JsonElement element, string name, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(name, out var property))
            return false;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value))
            return true;

        if (property.ValueKind == JsonValueKind.String)
            return TryParseInt(property.GetString(), out value);

        return false;
    }

    private static bool TryParseInt(string? value, out int result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        value = value.Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool ReadBool(JsonElement element, string name)
        => element.TryGetProperty(name, out var property)
           && property.ValueKind == JsonValueKind.True;

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return property.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList();
    }

    private static string? ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        return text[start..(end + 1)];
    }
}
