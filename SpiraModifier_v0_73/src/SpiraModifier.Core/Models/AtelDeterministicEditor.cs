using System.Buffers.Binary;

namespace SpiraModifier.Core.Models;

public static class AtelDeterministicEditor
{
    public static AtelPatchProposal BuildReplaceCommandProposal(
        AtelDecompiledScript script,
        int oldCommandId,
        int newCommandId,
        string? oldCommandName = null,
        string? newCommandName = null)
    {
        var operations = new List<AtelPatchOperation>();
        var instructions = script.Instructions;
        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            if (instruction.Opcode != 0xAE || instruction.Argument != oldCommandId)
                continue;

            if (!LooksLikeCommandConstant(instructions, i))
                continue;

            var bytes = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)newCommandId);
            operations.Add(new AtelPatchOperation
            {
                Kind = "replace",
                Offset = instruction.Offset + 1,
                Length = 2,
                Bytes = bytes,
                Note = $"Remplace l'argument PUSHII de 0x{oldCommandId:X4} vers 0x{newCommandId:X4} avant un appel de commande.",
            });
        }

        var oldLabel = FormatCommandLabel(oldCommandId, oldCommandName);
        var newLabel = FormatCommandLabel(newCommandId, newCommandName);
        return new AtelPatchProposal
        {
            Summary = operations.Count == 0
                ? $"Aucune occurrence ATEL sure de {oldLabel} n'a ete trouvee avant un appel de commande."
                : $"Remplacement deterministe de {oldLabel} par {newLabel} dans les appels de commande ATEL.",
            Risk = operations.Count == 0
                ? "Aucun patch applique : l'ID peut etre absent, indirect, stocke en refInt, ou gere par la scene."
                : "Risque modere : remplace les constantes immediates reperees avant des appels de commande, mais le comportement exact doit etre teste en combat.",
            RequiresSceneAtel = false,
            Mechanics = operations.Count == 0
                ? Array.Empty<string>()
                : new[]
                {
                    $"Les appels ATEL qui utilisaient {oldLabel} utiliseront {newLabel}.",
                    $"{operations.Count} occurrence(s) immediate(s) remplacee(s).",
                },
            Operations = operations,
        };
    }

    private static bool LooksLikeCommandConstant(IReadOnlyList<AtelInstruction> instructions, int index)
    {
        var max = Math.Min(instructions.Count - 1, index + 6);
        for (var i = index + 1; i <= max; i++)
        {
            var text = instructions[i].Annotation ?? "";
            if (ContainsAny(text,
                "performCommand(",
                "forcePerformCommand(",
                "addCommand(",
                "addCommandToSelf(",
                "removeCommand(",
                "setCommandDisabled(",
                "btlSetCommandBuffer(",
                "overrideAttemptedCommand(",
                "changeCommandAnimation(",
                "readCommandProperty("))
                return true;
        }

        return false;
    }

    private static string FormatCommandLabel(int id, string? name)
        => string.IsNullOrWhiteSpace(name)
            ? $"0x{id:X4}"
            : $"0x{id:X4} ({name})";

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));
}
