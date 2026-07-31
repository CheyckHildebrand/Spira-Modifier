using System.Globalization;
using System.Text;
using SpiraModifier.Core.BinaryIO;

namespace SpiraModifier.Core.Models;

public static class AtelDecompiler
{
    private const int StaticHeaderLength = 0x38;
    private const int WorkerHeaderLength = 0x34;

    public static AtelDecompiledScript Decompile(byte[]? aiBytes)
    {
        var script = new AtelDecompiledScript
        {
            RawSize = aiBytes?.Length ?? 0,
        };

        if (aiBytes == null || aiBytes.Length == 0)
        {
            script.Warnings.Add("Aucun bytecode ATEL dans ce fichier monstre.");
            return script;
        }

        if (aiBytes.Length < StaticHeaderLength)
        {
            script.Warnings.Add($"Chunk ATEL trop court ({aiBytes.Length} octets, header attendu 0x38).");
            return script;
        }

        script.ScriptCodeLength = Read4(aiBytes, 0x00);
        script.MapStartOffset = Read4(aiBytes, 0x04);
        script.CreatorTagOffset = Read4(aiBytes, 0x08);
        script.ScriptIdOffset = Read4(aiBytes, 0x0C);
        script.JumpsEndOffset = Read4(aiBytes, 0x10);
        script.Type2Or3WorkerCount = Read2(aiBytes, 0x14);
        script.Type4WorkerCount = Read2(aiBytes, 0x16);
        script.MainWorkerIndex = Read2(aiBytes, 0x18);
        script.Unknown1A = Read2(aiBytes, 0x1A);
        script.Type5Or6WorkerCount = Read2(aiBytes, 0x1C);
        script.EventDataOffset = Read4(aiBytes, 0x20);
        script.UnknownTable24Offset = Read4(aiBytes, 0x24);
        script.AreaNameIndexesOffset = Read4(aiBytes, 0x28);
        script.ScriptMetaStructOffset = Read4(aiBytes, 0x2C);
        script.ScriptCodeStartOffset = Read4(aiBytes, 0x30);
        script.WorkerCount = Read2(aiBytes, 0x34);
        script.ActorCount = Read2(aiBytes, 0x36);
        script.CreatorTag = ReadUtf8Z(aiBytes, script.CreatorTagOffset);
        script.ScriptId = ReadUtf8Z(aiBytes, script.ScriptIdOffset);

        if (script.WorkerCount < 0 || StaticHeaderLength + script.WorkerCount * 4 > aiBytes.Length)
        {
            script.Warnings.Add($"Table de workers invalide : {script.WorkerCount} worker(s).");
            return script;
        }

        for (int i = 0; i < script.WorkerCount; i++)
        {
            var headerOffset = Read4(aiBytes, StaticHeaderLength + i * 4);
            if (!IsRange(aiBytes, headerOffset, WorkerHeaderLength))
            {
                script.Warnings.Add($"Worker w{i:X2} : header hors limites a 0x{headerOffset:X4}.");
                continue;
            }

            script.Workers.Add(ReadWorker(aiBytes, i, headerOffset, script.Warnings));
        }

        var codeStart = script.ScriptCodeStartOffset;
        var codeLength = script.ScriptCodeLength;
        if (!IsRange(aiBytes, codeStart, codeLength))
        {
            script.Warnings.Add(
                $"Code ATEL hors limites : start=0x{codeStart:X4}, len=0x{codeLength:X4}, chunk=0x{aiBytes.Length:X4}.");
            codeLength = Math.Max(0, Math.Min(aiBytes.Length - codeStart, codeLength));
        }

        if (codeStart >= 0 && codeStart < aiBytes.Length && codeLength > 0)
        {
            var code = new byte[codeLength];
            Array.Copy(aiBytes, codeStart, code, 0, code.Length);
            script.Instructions.AddRange(ReadInstructions(code, script.Warnings));
        }

        BuildLabels(script);
        Annotate(script);
        return script;
    }

    private static AtelWorker ReadWorker(byte[] bytes, int index, int offset, List<string> warnings)
    {
        var worker = new AtelWorker
        {
            Index = index,
            HeaderOffset = offset,
            EventWorkerType = Read2(bytes, offset + 0x00),
            VariableCount = Read2(bytes, offset + 0x02),
            RefIntCount = Read2(bytes, offset + 0x04),
            RefFloatCount = Read2(bytes, offset + 0x06),
            FunctionCount = Read2(bytes, offset + 0x08),
            JumpCount = Read2(bytes, offset + 0x0A),
            PrivateDataLength = Read4(bytes, offset + 0x10),
            VariableDeclarationsOffset = Read4(bytes, offset + 0x14),
            RefIntsOffset = Read4(bytes, offset + 0x18),
            RefFloatsOffset = Read4(bytes, offset + 0x1C),
            FunctionEntryPointsOffset = Read4(bytes, offset + 0x20),
            JumpsOffset = Read4(bytes, offset + 0x24),
            PrivateDataOffset = Read4(bytes, offset + 0x2C),
            SharedDataOffset = Read4(bytes, offset + 0x30),
        };

        ReadIntTable(bytes, worker.FunctionEntryPointsOffset, worker.FunctionCount, worker.Functions, $"w{index:X2} fonctions", warnings);
        ReadIntTable(bytes, worker.JumpsOffset, worker.JumpCount, worker.Jumps, $"w{index:X2} jumps", warnings);
        ReadIntTable(bytes, worker.RefIntsOffset, worker.RefIntCount, worker.RefInts, $"w{index:X2} refInts", warnings);
        ReadIntTable(bytes, worker.RefFloatsOffset, worker.RefFloatCount, worker.RefFloats, $"w{index:X2} refFloats", warnings);
        return worker;
    }

    private static List<AtelInstruction> ReadInstructions(byte[] code, List<string> warnings)
    {
        var result = new List<AtelInstruction>();
        var cursor = 0;
        while (cursor < code.Length)
        {
            var offset = cursor;
            var opcode = code[cursor++];
            var hasArgs = (opcode & 0x80) != 0;
            int? arg = null;
            var length = 1;

            if (hasArgs)
            {
                if (cursor + 1 >= code.Length)
                {
                    warnings.Add($"Instruction tronquee a 0x{offset:X4} : opcode 0x{opcode:X2} attend 2 octets d'argument.");
                    result.Add(new AtelInstruction(offset, opcode, null, code.Length - offset));
                    break;
                }

                arg = code[cursor] | (code[cursor + 1] << 8);
                cursor += 2;
                length = 3;
            }

            result.Add(new AtelInstruction(offset, opcode, arg, length));
        }

        return result;
    }

    private static void BuildLabels(AtelDecompiledScript script)
    {
        foreach (var worker in script.Workers)
        {
            for (int i = 0; i < worker.Functions.Count; i++)
                AddLabel(script.LabelsByOffset, worker.Functions[i], $"w{worker.Index:X2}::f{i:X2}");
            for (int i = 0; i < worker.Jumps.Count; i++)
                AddLabel(script.LabelsByOffset, worker.Jumps[i], $"w{worker.Index:X2}::j{i:X2}");
        }
    }

    private static void AddLabel(Dictionary<int, List<string>> labels, int offset, string label)
    {
        if (!labels.TryGetValue(offset, out var list))
        {
            list = new List<string>();
            labels[offset] = list;
        }
        if (!list.Contains(label))
            list.Add(label);
    }

    private static void Annotate(AtelDecompiledScript script)
    {
        var stack = new List<AtelStackValue>();
        var tempInts = new Dictionary<int, AtelStackValue>();
        var tempFloats = new Dictionary<int, AtelStackValue>();
        AtelWorker? currentWorker = null;

        foreach (var instruction in script.Instructions)
        {
            if (script.LabelsByOffset.TryGetValue(instruction.Offset, out var labels))
            {
                var workerLabel = labels.FirstOrDefault(l => l.Length >= 3 && l[0] == 'w');
                if (workerLabel != null
                    && int.TryParse(workerLabel.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var workerIndex))
                {
                    currentWorker = script.Workers.FirstOrDefault(w => w.Index == workerIndex);
                    stack.Clear();
                }
            }

            ApplyAnnotation(instruction, currentWorker, stack, tempInts, tempFloats);

            if (IsLineEnd(instruction.Opcode))
                stack.Clear();
        }
    }

    private static void ApplyAnnotation(
        AtelInstruction instruction,
        AtelWorker? worker,
        List<AtelStackValue> stack,
        Dictionary<int, AtelStackValue> tempInts,
        Dictionary<int, AtelStackValue> tempFloats)
    {
        switch (instruction.Opcode)
        {
            case 0x00:
                instruction.Annotation = "no operation";
                return;
            case 0x26:
                Push(stack, instruction, "LastCallResult");
                return;
            case 0x28:
                Push(stack, instruction, "test");
                return;
            case 0x29:
                Push(stack, instruction, "case");
                return;
            case 0x2B:
            {
                var value = Pop(stack);
                Push(stack, instruction, value.Text);
                instruction.Annotation = $"duplicate {value.Text}";
                return;
            }
            case 0xAD:
            {
                var value = GetRefInt(worker, instruction.Argument ?? 0);
                Push(stack, instruction, value.Text, value.Signed, value.Unsigned, value.RawType);
                return;
            }
            case 0xAE:
                Push(stack, instruction, AtelNames.FormatNumber(instruction.SignedArgument, instruction.Argument ?? 0),
                    instruction.SignedArgument, instruction.Argument ?? 0, "int");
                return;
            case 0xAF:
            {
                var value = GetRefFloat(worker, instruction.Argument ?? 0);
                Push(stack, instruction, value.Text, value.Signed, value.Unsigned, value.RawType);
                return;
            }
            case 0x9F:
                Push(stack, instruction, $"var{(instruction.Argument ?? 0):X2}");
                return;
            case 0xA2:
            {
                var index = Pop(stack);
                Push(stack, instruction, $"var{(instruction.Argument ?? 0):X2}[{index.Text}]");
                return;
            }
            case 0xA7:
            {
                var index = Pop(stack);
                Push(stack, instruction, $"&var{(instruction.Argument ?? 0):X2}[{index.Text}]");
                return;
            }
        }

        if (instruction.Opcode is >= 0x01 and <= 0x18)
        {
            AnnotateBinaryOperator(instruction, stack);
            return;
        }

        if (instruction.Opcode is 0x19 or 0x1A or 0x1C)
        {
            AnnotateUnaryOperator(instruction, stack);
            return;
        }

        if (instruction.Opcode is >= 0x59 and <= 0x5C)
        {
            var tempIndex = instruction.Opcode - 0x59;
            var value = Pop(stack);
            tempInts[tempIndex] = value;
            instruction.Annotation = $"tmpI{tempIndex} = {value.Text}";
            return;
        }

        if (instruction.Opcode is >= 0x5D and <= 0x66)
        {
            var tempIndex = instruction.Opcode - 0x5D;
            var value = Pop(stack);
            tempFloats[tempIndex] = value;
            instruction.Annotation = $"tmpF{tempIndex} = {value.Text}";
            return;
        }

        if (instruction.Opcode is >= 0x67 and <= 0x6A)
        {
            var tempIndex = instruction.Opcode - 0x67;
            Push(stack, instruction, tempInts.TryGetValue(tempIndex, out var value) ? $"tmpI{tempIndex}({value.Text})" : $"tmpI{tempIndex}");
            return;
        }

        if (instruction.Opcode is >= 0x6B and <= 0x74)
        {
            var tempIndex = instruction.Opcode - 0x6B;
            Push(stack, instruction, tempFloats.TryGetValue(tempIndex, out var value) ? $"tmpF{tempIndex}({value.Text})" : $"tmpF{tempIndex}");
            return;
        }

        switch (instruction.Opcode)
        {
            case 0x25:
            {
                var value = Pop(stack);
                instruction.Annotation = $"LastCallResult = {value.Text}";
                return;
            }
            case 0x2A:
            {
                var value = Pop(stack);
                instruction.Annotation = $"test = {value.Text}";
                return;
            }
            case 0x2C:
            {
                var value = Pop(stack);
                instruction.Annotation = $"case = {value.Text}";
                return;
            }
            case 0xA0:
            case 0xA1:
            {
                var value = Pop(stack);
                instruction.Annotation = $"var{(instruction.Argument ?? 0):X2} = {value.Text}";
                return;
            }
            case 0xA3:
            case 0xA4:
            {
                var value = Pop(stack);
                var index = Pop(stack);
                instruction.Annotation = $"var{(instruction.Argument ?? 0):X2}[{index.Text}] = {value.Text}";
                return;
            }
            case 0xB0:
            case 0xB1:
            case 0xB2:
            {
                instruction.Annotation = $"goto {ResolveJumpLabel(worker, instruction.Argument ?? 0)}";
                return;
            }
            case 0xB3:
                instruction.Annotation = $"subroutine {ResolveWorkerLabel(instruction.Argument ?? 0)}";
                return;
            case 0xB5:
            case 0xD8:
            {
                AnnotateCall(instruction, stack);
                return;
            }
            case 0x36:
            case 0x37:
            case 0x38:
            case 0x39:
            case 0x3A:
            case 0x3B:
            case 0x45:
            case 0x46:
            case 0x47:
            case 0x48:
            case 0x49:
            case 0x4A:
            case 0x4B:
            case 0x4C:
            case 0x4D:
            case 0x4E:
            case 0x4F:
            case 0x50:
            case 0x51:
            case 0x52:
            case 0x53:
                AnnotateRunWorker(instruction, stack);
                return;
            case 0x77:
            case 0x78:
            {
                var func = Pop(stack);
                var target = Pop(stack);
                instruction.Annotation = $"await {target.Text}::f{func.Text}";
                return;
            }
            case 0x79:
            {
                var newFunc = Pop(stack);
                var oldFunc = Pop(stack);
                var table = Pop(stack);
                instruction.Annotation = $"replace entry table {table.Text}: old={oldFunc.Text}, new={newFunc.Text}";
                return;
            }
            case 0xD5:
            case 0xD6:
            case 0xD7:
            {
                var condition = Pop(stack);
                var target = ResolveJumpLabel(worker, instruction.Argument ?? 0);
                instruction.Annotation = instruction.Opcode switch
                {
                    0xD5 => $"if {condition.Text} then goto {target}",
                    0xD6 => $"if {condition.Text} then goto {target}",
                    _ => $"if not ({condition.Text}) then goto {target}",
                };
                return;
            }
            case 0xF6:
                instruction.Annotation = $"system marker 0x{(instruction.Argument ?? 0):X4}";
                return;
            case 0x34:
                instruction.Annotation = "return from subroutine";
                return;
            case 0x3C:
                instruction.Annotation = "return";
                return;
            case 0x40:
                instruction.Annotation = "halt";
                return;
            case 0x54:
                instruction.Annotation = "direct return";
                return;
        }
    }

    private static void AnnotateCall(AtelInstruction instruction, List<AtelStackValue> stack)
    {
        var callId = instruction.Argument ?? 0;
        var info = AtelCallTargets.Lookup(callId);
        var paramCount = info?.ParamCount ?? stack.Count;
        var args = PopMany(stack, paramCount).ToList();
        var name = info?.Name ?? $"call_{callId:X4}";
        var callText = FormatCallText(callId, name, info, args);
        instruction.Annotation = info == null
            ? callText
            : $"{callText} [{info.InternalName ?? $"0x{callId:X4}"}]";

        if (instruction.Opcode == 0xB5)
            Push(stack, instruction, callText);
    }

    private static string FormatCallText(int callId, string name, AtelCallTargetInfo? info, IReadOnlyList<AtelStackValue> args)
    {
        var accessor = FormatAccessorCall(callId, args);
        if (accessor != null)
            return accessor;

        var formatted = new List<string>();
        for (int i = 0; i < args.Count; i++)
        {
            var parameter = info?.GetParameter(i);
            var value = AtelNames.FormatValue(args[i], parameter?.Type);
            formatted.Add(parameter is { } p ? $"{p.Name}={value}" : value);
        }

        return $"{name}({string.Join(", ", formatted)})";
    }

    private static string? FormatAccessorCall(int callId, IReadOnlyList<AtelStackValue> args)
    {
        return callId switch
        {
            0x700F when args.Count >= 2
                => $"{AtelNames.FormatValue(args[0], "btlChr")}.{AtelNames.FormatValue(args[1], "btlChrProperty")}",
            0x7018 when args.Count >= 3
                => $"Set {AtelNames.FormatValue(args[0], "btlChr")}.{AtelNames.FormatValue(args[1], "btlChrProperty")} = {args[2].Text}",
            0x701A when args.Count >= 2
                => $"{AtelNames.FormatValue(args[0], "command")}.{AtelNames.FormatValue(args[1], "commandProperty")}",
            0x7078 when args.Count >= 3
                => $"{AtelNames.FormatValue(args[0], "btlChr")}.{AtelNames.FormatValue(args[1], "command")}.{AtelNames.FormatValue(args[2], "commandProperty")}",
            0x70A8 when args.Count >= 3
                => $"Set {AtelNames.FormatValue(args[0], "btlChr")}.motion.{AtelNames.FormatValue(args[1], "motionProperty")} = {args[2].Text}",
            0x70AA when args.Count >= 1
                => $"Self.{AtelNames.FormatValue(args[0], "btlChrProperty")}",
            0x70AB when args.Count >= 2
                => $"Set Self.{AtelNames.FormatValue(args[0], "btlChrProperty")} = {args[1].Text}",
            0x70AC when args.Count >= 1
                => $"Self.motion.{AtelNames.FormatValue(args[0], "motionProperty")}",
            0x70B2 when args.Count >= 2
                => $"Set Self.motion.{AtelNames.FormatValue(args[0], "motionProperty")} = {args[1].Text}",
            _ => null,
        };
    }

    private static void AnnotateRunWorker(AtelInstruction instruction, List<AtelStackValue> stack)
    {
        var func = Pop(stack);
        var worker = Pop(stack);
        var level = Pop(stack);
        var targetPrefix = instruction.Opcode is >= 0x39 and <= 0x3B ? "party" : "worker";
        var flags = new List<string>();
        var opcode = instruction.Opcode;
        if (opcode is 0x37 or 0x3A || opcode is >= 0x4A and <= 0x4E) flags.Add("await start");
        if (opcode is 0x38 or 0x3B || opcode is >= 0x4F and <= 0x53) flags.Add("await end");
        if (opcode is 0x45 or 0x48 or 0x4A or 0x4D or 0x4F or 0x52) flags.Add("forced");
        if (opcode is 0x46 or 0x49 or 0x4B or 0x4E or 0x50 or 0x53) flags.Add("if not queued");
        if (opcode is >= 0x47 and <= 0x49 || opcode is >= 0x4C and <= 0x4E || opcode is >= 0x51 and <= 0x53) flags.Add("conditional");
        instruction.Annotation = $"run {targetPrefix} {worker.Text}::f{func.Text} at level {level.Text}" +
                                 (flags.Count > 0 ? $" ({string.Join(", ", flags)})" : "");
        Push(stack, instruction, "runResult");
    }

    private static void AnnotateBinaryOperator(AtelInstruction instruction, List<AtelStackValue> stack)
    {
        var b = Pop(stack);
        var a = Pop(stack);
        var op = instruction.Opcode switch
        {
            0x01 => "or",
            0x02 => "and",
            0x03 => "|",
            0x04 => "^",
            0x05 => "&",
            0x06 => "==",
            0x07 => "!=",
            0x08 => ">u",
            0x09 => "<u",
            0x0A => ">",
            0x0B => "<",
            0x0C => ">=u",
            0x0D => "<=u",
            0x0E => ">=",
            0x0F => "<=",
            0x10 => "B-ON",
            0x11 => "B-OFF",
            0x12 => "<<",
            0x13 => ">>",
            0x14 => "+",
            0x15 => "-",
            0x16 => "*",
            0x17 => "/",
            0x18 => "mod",
            _ => "?",
        };
        Push(stack, instruction, $"({a.Text} {op} {b.Text})");
    }

    private static void AnnotateUnaryOperator(AtelInstruction instruction, List<AtelStackValue> stack)
    {
        var value = Pop(stack);
        var op = instruction.Opcode switch
        {
            0x19 => "!",
            0x1A => "-",
            0x1C => "~",
            _ => "",
        };
        Push(stack, instruction, $"{op}{value.Text}");
    }

    private static void Push(
        List<AtelStackValue> stack,
        AtelInstruction instruction,
        string text,
        int? signed = null,
        int? unsigned = null,
        string? rawType = null)
    {
        stack.Add(new AtelStackValue(text, signed, unsigned, rawType));
        instruction.Annotation ??= $"push {text}";
        instruction.StackAfter = stack.Count;
    }

    private static AtelStackValue Pop(List<AtelStackValue> stack)
    {
        if (stack.Count == 0)
            return new AtelStackValue("<stack?>");
        var value = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return value;
    }

    private static IEnumerable<AtelStackValue> PopMany(List<AtelStackValue> stack, int count)
    {
        var values = new List<AtelStackValue>();
        for (int i = 0; i < count; i++)
            values.Add(Pop(stack));
        values.Reverse();
        return values;
    }

    private static AtelStackValue GetRefInt(AtelWorker? worker, int index)
    {
        if (worker == null || index < 0 || index >= worker.RefInts.Count)
            return new AtelStackValue($"refInt[{index:X2}]");
        var value = worker.RefInts[index];
        return new AtelStackValue(AtelNames.FormatNumber(value, value), value, value, "int32");
    }

    private static AtelStackValue GetRefFloat(AtelWorker? worker, int index)
    {
        if (worker == null || index < 0 || index >= worker.RefFloats.Count)
            return new AtelStackValue($"refFloat[{index:X2}]");
        var raw = worker.RefFloats[index];
        var value = BitConverter.Int32BitsToSingle(raw);
        return new AtelStackValue(value.ToString("0.###", CultureInfo.InvariantCulture), raw, raw, "float");
    }

    private static string ResolveJumpLabel(AtelWorker? worker, int jumpIndex)
        => worker != null && jumpIndex >= 0 && jumpIndex < worker.Jumps.Count
            ? $"{worker.Label}::j{jumpIndex:X2} (0x{worker.Jumps[jumpIndex]:X4})"
            : $"j{jumpIndex:X2}";

    private static string ResolveWorkerLabel(int workerIndex)
        => $"w{workerIndex:X2}";

    private static bool IsLineEnd(int opcode)
        => opcode is 0x25 or 0x2A or 0x2C or 0x34 or 0x36 or 0x37 or 0x38
            or 0x39 or 0x3A or 0x3B or 0x3C or 0x3D or 0x3E or 0x3F or 0x40
            or 0x45 or 0x46 or 0x47 or 0x48 or 0x49 or 0x4A or 0x4B or 0x4C
            or 0x4D or 0x4E or 0x4F or 0x50 or 0x51 or 0x52 or 0x53 or 0x54
            or 0x59 or 0x5A or 0x5B or 0x5C or 0x5D or 0x5E or 0x5F or 0x60
            or 0x61 or 0x62 or 0x63 or 0x64 or 0x65 or 0x66 or 0x77 or 0x78
            or 0x79 or 0xA0 or 0xA1 or 0xA3 or 0xA4 or 0xB0 or 0xB1 or 0xB2
            or 0xB3 or 0xD5 or 0xD6 or 0xD7 or 0xD8 or 0xF6;

    private static void ReadIntTable(
        byte[] bytes,
        int offset,
        int count,
        List<int> output,
        string label,
        List<string> warnings)
    {
        if (count <= 0) return;
        if (!IsRange(bytes, offset, count * 4))
        {
            warnings.Add($"{label} : table hors limites a 0x{offset:X4} ({count} entree(s)).");
            return;
        }

        for (int i = 0; i < count; i++)
            output.Add(Read4(bytes, offset + i * 4));
    }

    private static int Read2(byte[] bytes, int offset)
        => IsRange(bytes, offset, 2) ? BytesHelper.Read2Bytes(bytes, offset) : 0;

    private static int Read4(byte[] bytes, int offset)
        => IsRange(bytes, offset, 4) ? BytesHelper.Read4BytesSigned(bytes, offset) : 0;

    private static bool IsRange(byte[] bytes, int offset, int length)
        => offset >= 0 && length >= 0 && offset <= bytes.Length - length;

    private static string ReadUtf8Z(byte[] bytes, int offset)
    {
        if (offset <= 0 || offset >= bytes.Length) return "";
        var end = offset;
        while (end < bytes.Length && bytes[end] != 0) end++;
        return Encoding.UTF8.GetString(bytes, offset, end - offset);
    }

    internal static string Mnemonic(int opcode)
        => OpcodeNames.TryGetValue(opcode, out var name) ? name : $"OP_{opcode:X2}";

    private static readonly Dictionary<int, string> OpcodeNames = new()
    {
        [0x00] = "NOP", [0x01] = "OPLOR", [0x02] = "OPLAND", [0x03] = "OPOR",
        [0x04] = "OPEOR", [0x05] = "OPAND", [0x06] = "OPEQ", [0x07] = "OPNE",
        [0x08] = "OPGTU", [0x09] = "OPLSU", [0x0A] = "OPGT", [0x0B] = "OPLS",
        [0x0C] = "OPGTEU", [0x0D] = "OPLSEU", [0x0E] = "OPGTE", [0x0F] = "OPLSE",
        [0x10] = "OPBON", [0x11] = "OPBOFF", [0x12] = "OPSLL", [0x13] = "OPSRL",
        [0x14] = "OPADD", [0x15] = "OPSUB", [0x16] = "OPMUL", [0x17] = "OPDIV",
        [0x18] = "OPMOD", [0x19] = "OPNOT", [0x1A] = "OPUMINUS", [0x1B] = "OPFIXADRS",
        [0x1C] = "OPBNOT", [0x1D] = "LABEL", [0x1E] = "TAG", [0x25] = "POPA",
        [0x26] = "PUSHA", [0x28] = "PUSHX", [0x29] = "PUSHY", [0x2A] = "POPX",
        [0x2B] = "REPUSH", [0x2C] = "POPY", [0x34] = "RTS", [0x36] = "REQ",
        [0x37] = "REQSW", [0x38] = "REQEW", [0x39] = "PREQ", [0x3A] = "PREQSW",
        [0x3B] = "PREQEW", [0x3C] = "RET", [0x3D] = "RETN", [0x3E] = "RETT",
        [0x3F] = "RETTN", [0x40] = "HALT", [0x41] = "PUSHN", [0x42] = "PUSHT",
        [0x43] = "PUSHVP", [0x44] = "PUSHFIX", [0x45] = "FREQ", [0x46] = "TREQ",
        [0x47] = "BREQ", [0x48] = "BFREQ", [0x49] = "BTREQ", [0x4A] = "FREQSW",
        [0x4B] = "TREQSW", [0x4C] = "BREQSW", [0x4D] = "BFREQSW", [0x4E] = "BTREQSW",
        [0x4F] = "FREQEW", [0x50] = "TREQEW", [0x51] = "BREQEW", [0x52] = "BFREQEW",
        [0x53] = "BTREQEW", [0x54] = "DRET", [0x59] = "POPI0", [0x5A] = "POPI1",
        [0x5B] = "POPI2", [0x5C] = "POPI3", [0x5D] = "POPF0", [0x5E] = "POPF1",
        [0x5F] = "POPF2", [0x60] = "POPF3", [0x61] = "POPF4", [0x62] = "POPF5",
        [0x63] = "POPF6", [0x64] = "POPF7", [0x65] = "POPF8", [0x66] = "POPF9",
        [0x67] = "PUSHI0", [0x68] = "PUSHI1", [0x69] = "PUSHI2", [0x6A] = "PUSHI3",
        [0x6B] = "PUSHF0", [0x6C] = "PUSHF1", [0x6D] = "PUSHF2", [0x6E] = "PUSHF3",
        [0x6F] = "PUSHF4", [0x70] = "PUSHF5", [0x71] = "PUSHF6", [0x72] = "PUSHF7",
        [0x73] = "PUSHF8", [0x74] = "PUSHF9", [0x75] = "PUSHAINTER", [0x77] = "REQWAIT",
        [0x78] = "PREQWAIT", [0x79] = "REQCHG", [0x7A] = "ACTREQ", [0x9F] = "PUSHV",
        [0xA0] = "POPV", [0xA1] = "POPVL", [0xA2] = "PUSHAR", [0xA3] = "POPAR",
        [0xA4] = "POPARL", [0xA7] = "PUSHARP", [0xAD] = "PUSHI", [0xAE] = "PUSHII",
        [0xAF] = "PUSHF", [0xB0] = "JMP", [0xB1] = "CJMP", [0xB2] = "NCJMP",
        [0xB3] = "JSR", [0xB5] = "CALL", [0xD5] = "POPXJMP", [0xD6] = "POPXCJMP",
        [0xD7] = "POPXNCJMP", [0xD8] = "CALLPOPA", [0xF6] = "SYSTEM",
    };
}

public sealed class AtelDecompiledScript
{
    public int RawSize { get; init; }
    public int ScriptCodeLength { get; set; }
    public int MapStartOffset { get; set; }
    public int CreatorTagOffset { get; set; }
    public int ScriptIdOffset { get; set; }
    public int JumpsEndOffset { get; set; }
    public int Type2Or3WorkerCount { get; set; }
    public int Type4WorkerCount { get; set; }
    public int MainWorkerIndex { get; set; }
    public int Unknown1A { get; set; }
    public int Type5Or6WorkerCount { get; set; }
    public int EventDataOffset { get; set; }
    public int UnknownTable24Offset { get; set; }
    public int AreaNameIndexesOffset { get; set; }
    public int ScriptMetaStructOffset { get; set; }
    public int ScriptCodeStartOffset { get; set; }
    public int WorkerCount { get; set; }
    public int ActorCount { get; set; }
    public string CreatorTag { get; set; } = "";
    public string ScriptId { get; set; } = "";
    public List<AtelWorker> Workers { get; } = new();
    public List<AtelInstruction> Instructions { get; } = new();
    public Dictionary<int, List<string>> LabelsByOffset { get; } = new();
    public List<string> Warnings { get; } = new();

    public string ToListingText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# ATEL monster AI decompile (read-only)");
        sb.AppendLine($"chunk_size      = 0x{RawSize:X4} ({RawSize:N0} bytes)");
        sb.AppendLine($"script_id       = {Blank(ScriptId)}");
        sb.AppendLine($"creator         = {Blank(CreatorTag)}");
        sb.AppendLine($"script_code     = 0x{ScriptCodeStartOffset:X4} + 0x{ScriptCodeLength:X4}");
        sb.AppendLine($"workers         = {WorkerCount} (actors: {ActorCount}, main: w{MainWorkerIndex:X2})");
        sb.AppendLine();

        if (Warnings.Count > 0)
        {
            sb.AppendLine("# Warnings");
            foreach (var warning in Warnings)
                sb.AppendLine("# - " + warning);
            sb.AppendLine();
        }

        sb.AppendLine("# Workers");
        foreach (var worker in Workers)
            sb.AppendLine(worker.ToSummaryLine());
        sb.AppendLine();

        sb.AppendLine("# Linear code");
        foreach (var instruction in Instructions)
        {
            if (LabelsByOffset.TryGetValue(instruction.Offset, out var labels))
            {
                foreach (var label in labels)
                {
                    var comment = DescribeLabel(label);
                    if (comment != null)
                        sb.AppendLine();
                    if (comment != null)
                        sb.AppendLine("# " + comment);
                    sb.AppendLine(label + ":");
                }
            }
            sb.AppendLine("  " + instruction.ToListingLine());
        }

        return sb.ToString();
    }

    private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? "(none)" : value;

    private string? DescribeLabel(string label)
    {
        if (label.Length < 7 || label[0] != 'w')
            return null;

        if (!int.TryParse(label.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var workerIndex))
            return null;

        var worker = Workers.FirstOrDefault(w => w.Index == workerIndex);
        if (worker == null)
            return null;

        if (label.Contains("::f", StringComparison.Ordinal))
        {
            var marker = label.IndexOf("::f", StringComparison.Ordinal);
            if (marker >= 0
                && int.TryParse(label.AsSpan(marker + 3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var functionIndex)
                && functionIndex >= 0
                && functionIndex < worker.Functions.Count)
            {
                return $"{worker.Label} {worker.KindLabel} - fonction f{functionIndex:X2}, entree 0x{worker.Functions[functionIndex]:X4}, vars={worker.VariableCount}, refI={worker.RefIntCount}, refF={worker.RefFloatCount}";
            }
        }

        if (label.Contains("::j", StringComparison.Ordinal))
        {
            var marker = label.IndexOf("::j", StringComparison.Ordinal);
            if (marker >= 0
                && int.TryParse(label.AsSpan(marker + 3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var jumpIndex)
                && jumpIndex >= 0
                && jumpIndex < worker.Jumps.Count)
            {
                return $"{worker.Label} {worker.KindLabel} - cible de saut j{jumpIndex:X2}, offset 0x{worker.Jumps[jumpIndex]:X4}";
            }
        }

        return null;
    }
}

public sealed class AtelWorker
{
    public int Index { get; init; }
    public int HeaderOffset { get; init; }
    public int EventWorkerType { get; init; }
    public int VariableCount { get; init; }
    public int RefIntCount { get; init; }
    public int RefFloatCount { get; init; }
    public int FunctionCount { get; init; }
    public int JumpCount { get; init; }
    public int PrivateDataLength { get; init; }
    public int VariableDeclarationsOffset { get; init; }
    public int RefIntsOffset { get; init; }
    public int RefFloatsOffset { get; init; }
    public int FunctionEntryPointsOffset { get; init; }
    public int JumpsOffset { get; init; }
    public int PrivateDataOffset { get; init; }
    public int SharedDataOffset { get; init; }
    public List<int> Functions { get; } = new();
    public List<int> Jumps { get; } = new();
    public List<int> RefInts { get; } = new();
    public List<int> RefFloats { get; } = new();

    public string Label => $"w{Index:X2}";
    public string KindLabel => AtelNames.FormatWorkerType(EventWorkerType);
    public string FunctionsLabel => Functions.Count == 0
        ? "-"
        : string.Join(" ", Functions.Select((addr, i) => $"f{i:X2}=0x{addr:X4}"));
    public string JumpsLabel => Jumps.Count == 0
        ? "-"
        : string.Join(" ", Jumps.Select((addr, i) => $"j{i:X2}=0x{addr:X4}"));

    public string ToSummaryLine()
        => $"{Label} {KindLabel,-12} funcs={FunctionCount,2} jumps={JumpCount,2} vars={VariableCount,2} " +
           $"refI={RefIntCount,2} refF={RefFloatCount,2} private=0x{PrivateDataLength:X}  {FunctionsLabel}";
}

public sealed class AtelInstruction
{
    public int Offset { get; }
    public int Opcode { get; }
    public int? Argument { get; }
    public int Length { get; }
    public string? Annotation { get; set; }
    public int StackAfter { get; set; }
    public string Mnemonic => AtelDecompiler.Mnemonic(Opcode);
    public bool HasArgument => Argument != null;
    public int SignedArgument => Argument is { } value && value >= 0x8000 ? value - 0x10000 : Argument ?? 0;

    public AtelInstruction(int offset, int opcode, int? argument, int length)
    {
        Offset = offset;
        Opcode = opcode;
        Argument = argument;
        Length = length;
    }

    public string ToListingLine()
    {
        var raw = HasArgument
            ? $"{Opcode:X2} {Argument!.Value & 0xFF:X2} {(Argument.Value >> 8) & 0xFF:X2}"
            : $"{Opcode:X2}";
        var line = $"{Offset:X4}  {raw,-8} {Mnemonic,-10}{FormatArgument()}";
        if (!string.IsNullOrWhiteSpace(Annotation))
            line += $"  // {Annotation}";
        return line;
    }

    private string FormatArgument()
    {
        if (Argument == null) return "";
        var arg = Argument.Value;
        return Opcode switch
        {
            0xAD => $" refInt[{arg:X2}]",
            0xAE => $" {SignedArgument}",
            0xAF => $" refFloat[{arg:X2}]",
            0x9F or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA7 => $" var[{arg:X2}]",
            0xB0 or 0xB1 or 0xB2 or 0xD5 or 0xD6 or 0xD7 => $" j{arg:X2}",
            0xB3 => $" w{arg:X2}",
            0xB5 or 0xD8 => $" call_{arg:X4}",
            0xF6 => $" system_{arg:X4}",
            _ => $" 0x{arg:X4} ({SignedArgument.ToString(CultureInfo.InvariantCulture)})",
        };
    }
}

internal readonly record struct AtelStackValue(string Text, int? Signed = null, int? Unsigned = null, string? RawType = null);

internal readonly record struct AtelCallParameter(string Name, string Type);

internal sealed class AtelCallTargetInfo
{
    public AtelCallTargetInfo(
        int id,
        string name,
        int paramCount,
        params AtelCallParameter[] parameters)
        : this(id, name, paramCount, null, parameters)
    {
    }

    public AtelCallTargetInfo(
        int id,
        string name,
        int paramCount,
        string? internalName = null,
        params AtelCallParameter[] parameters)
    {
        Id = id;
        Name = name;
        InternalName = internalName;
        Parameters = parameters ?? Array.Empty<AtelCallParameter>();
        ParamCount = Parameters.Count > 0 ? Parameters.Count : paramCount;
    }

    public int Id { get; }
    public string Name { get; }
    public int ParamCount { get; }
    public string? InternalName { get; }
    public IReadOnlyList<AtelCallParameter> Parameters { get; }

    public AtelCallParameter? GetParameter(int index)
        => index >= 0 && index < Parameters.Count ? Parameters[index] : null;
}

internal static class AtelNames
{
    public static string FormatWorkerType(int value)
        => FormatEnum("battleWorkerType", value, $"WorkerType {value}");

    public static string FormatNumber(int signed, int unsigned)
    {
        var width = (unsigned & 0xFF00) != 0 ? 4 : 2;
        var hex = (unsigned & 0xFFFF).ToString($"X{width}", CultureInfo.InvariantCulture);
        return $"{signed.ToString(CultureInfo.InvariantCulture)} [{hex}h]";
    }

    public static string FormatValue(AtelStackValue value, string? type)
    {
        if (string.IsNullOrWhiteSpace(type) || type == "unknown" || value.Signed == null)
            return value.Text;

        var signed = value.Signed.Value;
        var unsigned = value.Unsigned ?? signed;
        return type switch
        {
            "bool" => $"{(signed != 0 ? "true" : "false")} [{(unsigned & 0xFFFF):X2}h]",
            "float" when value.RawType == "float" => value.Text,
            "float" => BitConverter.Int32BitsToSingle(signed).ToString("0.###", CultureInfo.InvariantCulture),
            "btlChr" or "monster" => FormatBtlChr(signed, unsigned),
            "command" => FormatCommand(signed, unsigned),
            "treasure" => FormatTreasure(signed, unsigned),
            "battle" => FormatBattle(signed, unsigned),
            _ => FormatEnum(type, signed, value.Text),
        };
    }

    public static string FormatEnum(string type, int signed, string fallback)
        => EnumMaps.TryGetValue(type, out var map) && map.TryGetValue(signed, out var name)
            ? $"{name} [{FormatHexForSuffix(signed)}h]"
            : fallback;

    private static string FormatBtlChr(int signed, int unsigned)
    {
        if (EnumMaps.TryGetValue("btlChr", out var map) && map.TryGetValue(signed, out var name))
            return $"{name} [{FormatHexForSuffix(signed)}h]";

        if (signed >= 0x14 && signed <= 0x1B)
            return $"Monster#{signed - 0x14:X2} [{FormatHexForSuffix(signed)}h]";

        if (signed >= 0x1000 && signed < 0x2000)
            return $"MonsterType m{signed & 0x0FFF:000} [{FormatHexForSuffix(signed)}h]";

        return FormatNumber(signed, unsigned);
    }

    private static string FormatCommand(int signed, int unsigned)
    {
        if (signed == 0)
            return "Null Command [00h]";
        if (signed >= 0 && signed <= 0x13)
            return FormatEnum("playerChar", signed, FormatNumber(signed, unsigned));
        if (signed is >= 0x2000 and <= 0x206F)
            return $"item command 0x{signed:X4} [{signed - 0x2000}]";
        if (signed is >= 0x3000 and <= 0x313F)
            return $"player command 0x{signed:X4} [{signed - 0x3000}]";
        if (signed is >= 0x4000 and <= 0x412B)
            return $"monster command 0x{signed:X4} [{signed - 0x4000}]";
        if (signed is >= 0x6000 and <= 0x60F6)
            return $"monster special 0x{signed:X4} [{signed - 0x6000}]";
        return FormatNumber(signed, unsigned);
    }

    private static string FormatBattle(int signed, int unsigned)
    {
        var map = (unsigned >> 16) & 0xFFFF;
        var encounter = unsigned & 0xFFFF;
        return $"battle map=0x{map:X4}, encounter={encounter} [{unsigned:X8}h]";
    }

    private static string FormatTreasure(int signed, int unsigned)
        => $"Treasure #{signed} [{FormatHexForSuffix(signed)}h]";

    private static string FormatHexForSuffix(int signed)
    {
        var unsigned = signed & 0xFFFF;
        var width = (unsigned & 0xFF00) != 0 ? 4 : 2;
        return unsigned.ToString($"X{width}", CultureInfo.InvariantCulture);
    }

    private static readonly Dictionary<string, Dictionary<int, string>> EnumMaps = new()
    {
        ["battleWorkerType"] = new()
        {
            [0x00] = "CameraHandler",
            [0x01] = "MotionHandler",
            [0x02] = "CombatHandler",
            [0x03] = "BattleGruntHandler",
            [0x04] = "BattleScenes",
            [0x05] = "VoiceHandler",
            [0x06] = "StartEndHooks",
            [0x07] = "MagicCameraHandler-Command",
            [0x08] = "MagicCameraHandler-Item",
            [0x09] = "MagicCameraHandler-Monmagic1",
            [0x0A] = "MagicCameraHandler-Monmagic2",
        },
        ["playerChar"] = new()
        {
            [0x00] = "Tidus",
            [0x01] = "Yuna",
            [0x02] = "Auron",
            [0x03] = "Kimahri",
            [0x04] = "Wakka",
            [0x05] = "Lulu",
            [0x06] = "Rikku",
            [0x07] = "Seymour",
            [0x08] = "Valefor",
            [0x09] = "Ifrit",
            [0x0A] = "Ixion",
            [0x0B] = "Shiva",
            [0x0C] = "Bahamut",
            [0x0D] = "Anima",
            [0x0E] = "Yojimbo",
            [0x0F] = "Cindy",
            [0x10] = "Sandy",
            [0x11] = "Mindy",
            [-1] = "Empty",
        },
        ["btlChr"] = new()
        {
            [0x00] = "Tidus",
            [0x01] = "Yuna",
            [0x02] = "Auron",
            [0x03] = "Kimahri",
            [0x04] = "Wakka",
            [0x05] = "Lulu",
            [0x06] = "Rikku",
            [0x07] = "Seymour",
            [0x08] = "Valefor",
            [0x09] = "Ifrit",
            [0x0A] = "Ixion",
            [0x0B] = "Shiva",
            [0x0C] = "Bahamut",
            [0x0D] = "Anima",
            [0x0E] = "Yojimbo",
            [0x0F] = "Cindy",
            [0x10] = "Sandy",
            [0x11] = "Mindy",
            [0x14] = "Monster#00",
            [0x15] = "Monster#01",
            [0x16] = "Monster#02",
            [0x17] = "Monster#03",
            [0x18] = "Monster#04",
            [0x19] = "Monster#05",
            [0x1A] = "Monster#06",
            [0x1B] = "Monster#07",
            [0x00FF] = "Actor:None",
            [-26] = "CHR_OWN_TARGET0",
            [-25] = "CHR_ALL_PLY3",
            [-24] = "CHR_ALL_PLAYER2",
            [-23] = "AllCharsAndAeons",
            [-22] = "CHR_PARENT",
            [-21] = "AllChrs?",
            [-20] = "AllAeons",
            [-19] = "CHR_ALL_PLY2",
            [-18] = "CHR_INPUT",
            [-17] = "LastAttacker",
            [-16] = "MatchingGroup",
            [-15] = "AllMonsters",
            [-14] = "FrontlineChars",
            [-13] = "Self",
            [-12] = "CharacterReserve#4",
            [-11] = "CharacterReserve#3",
            [-10] = "CharacterReserve#2",
            [-9] = "CharacterReserve#1",
            [-8] = "Character#3",
            [-7] = "Character#2",
            [-6] = "Character#1",
            [-5] = "AllActors",
            [-4] = "?TargetChrsImmediate",
            [-3] = "TargetChrs",
            [-2] = "ActiveChrs",
            [-1] = "Actor:Null",
        },
        ["selector"] = new()
        {
            [0x00] = "Any/All",
            [0x01] = "Highest",
            [0x02] = "Lowest",
            [0x80] = "Not",
        },
        ["ambushState"] = new()
        {
            [0x00] = "Normal",
            [0x01] = "Preemptive",
            [0x02] = "Ambushed",
            [0x03] = "NormalRandomOff",
        },
        ["stdmotion"] = new()
        {
            [-1] = "MOT_ALL",
            [0x00] = "MOT_NONE",
            [0xFF] = "MOT_ALL",
        },
        ["magusTarget"] = new()
        {
            [0x00] = "AllMonsters",
            [0x01] = "FrontlineChars",
            [0x101] = "FrontlineChars except self",
        },
        ["battleEndType"] = new()
        {
            [0x01] = "Defeat",
            [0x02] = "Victory",
            [0x03] = "PlayerEscaped",
            [0x04] = "MonsterEscaped",
        },
        ["weakState"] = new()
        {
            [0x00] = "Normal",
            [0x01] = "Slightly Weak",
            [0x02] = "Very Weak",
            [0x03] = "Dead",
        },
        ["textAlignment"] = new()
        {
            [0x00] = "Top Left",
            [0x01] = "Bottom Left",
            [0x02] = "Top Right",
            [0x03] = "Bottom Right",
            [0x04] = "Center",
        },
        ["battleTransition"] = new()
        {
            [0x00] = "Screen Shatter",
            [0x01] = "Fade",
        },
        ["specialBattleSetting"] = new()
        {
            [0x01] = "Sin Fin",
            [0x02] = "Sin Arm",
            [0x03] = "Evrae",
        },
        ["battleDebugFlag"] = new()
        {
            [0x00] = "?FullItems",
            [0x01] = "?PlayersInvincible",
            [0x02] = "?AllInvincible",
            [0x03] = "ControlMonsters",
            [0x04] = "?NoMpCost",
            [0x05] = "?AlwaysOverdrive",
            [0x06] = "?NoDamageVariance",
            [0x07] = "NeverCrit",
            [0x08] = "?AttacksStatusesAlwaysHit",
            [0x09] = "?LogBattleInformation",
            [0x0A] = "?FullSpGil",
            [0x0B] = "?FullWeapons",
            [0x0C] = "?MonstersInvincible",
            [0x0D] = "?AlwaysCrit",
            [0x0E] = "?DmgAlways1",
            [0x0F] = "?DmgAlways9999",
            [0x10] = "?DmgAlways99999",
            [0x11] = "?AlwaysRareDrop",
            [0x12] = "?APx100",
            [0x13] = "?Gilx100",
            [0x14] = "?NoOverkills",
            [0x15] = "?FullCommands",
            [0x16] = "?FullSummons",
            [0x17] = "?CommandSkip",
            [0x18] = "?PermanentSensor",
            [0x19] = "?AlwaysPreemptive",
        },
        ["motionProperty"] = new()
        {
            [0x0000] = "motion_attack_start_dist",
            [0x0001] = "motion_attack_offset",
            [0x0002] = "motion_move_backjump_dist",
            [0x0003] = "motion_run_speed",
            [0x0004] = "motion_run_speed_return",
            [0x0005] = "motion_run_speed_v0",
            [0x0006] = "motion_run_speed_acc",
            [0x0007] = "motion_weight",
            [0x0008] = "motion_attack_height",
            [0x0009] = "motion_width",
        },
        ["commandProperty"] = new()
        {
            [0x0000] = "damageFormula",
            [0x0001] = "damageType",
            [0x0002] = "affectHP",
            [0x0003] = "affectMP",
            [0x0004] = "affectCTB",
            [0x0005] = "elementHoly",
            [0x0006] = "elementWater",
            [0x0007] = "elementThunder",
            [0x0008] = "elementIce",
            [0x0009] = "elementFire",
            [0x000A] = "targetType",
        },
        ["btlChrProperty"] = new()
        {
            [0x0000] = "HP",
            [0x0001] = "MP",
            [0x0002] = "maxHP",
            [0x0003] = "maxMP",
            [0x0004] = "isAlive",
            [0x0005] = "StatusPoison",
            [0x0006] = "StatusPetrify",
            [0x0007] = "StatusZombie",
            [0x0009] = "STR",
            [0x000A] = "DEF",
            [0x000B] = "MAG",
            [0x000C] = "MDF",
            [0x000D] = "AGI",
            [0x000E] = "LCK",
            [0x000F] = "EVA",
            [0x0010] = "ACC",
            [0x0011] = "PoisonDamage%",
            [0x0012] = "OverdriveMode",
            [0x0013] = "OverdriveCurrent",
            [0x0014] = "OverdriveMax",
            [0x0015] = "isOnFrontline",
            [0x001A] = "stat_fly",
            [0x001B] = "?willDieToAttack",
            [0x001C] = "Area",
            [0x001D] = "Position",
            [0x001E] = "BattleDistance",
            [0x001F] = "EnemyGroup",
            [0x0020] = "Armored",
            [0x0021] = "?ImmuneToFractionalDmg",
            [0x0025] = "StatusPowerBreak",
            [0x0026] = "StatusMagicBreak",
            [0x0027] = "StatusArmorBreak",
            [0x0028] = "StatusMentalBreak",
            [0x0029] = "StatusConfusion",
            [0x002A] = "StatusBerserk",
            [0x002B] = "StatusProvoke",
            [0x002C] = "StatusThreaten",
            [0x002D] = "StatusSleep",
            [0x002E] = "StatusSilence",
            [0x002F] = "StatusDarkness",
            [0x0030] = "StatusShell",
            [0x0031] = "StatusProtect",
            [0x0032] = "StatusReflect",
            [0x0033] = "StatusNulTide",
            [0x0034] = "StatusNulBlaze",
            [0x0035] = "StatusNulShock",
            [0x0036] = "StatusNulFrost",
            [0x0037] = "StatusRegen",
            [0x0038] = "StatusHaste",
            [0x0039] = "StatusSlow",
            [0x003B] = "FirstStrike",
            [0x003D] = "CounterAttack",
            [0x003E] = "EvadeAndCounter",
            [0x0048] = "ability_limitup",
            [0x004F] = "DeathAnimation",
            [0x0050] = "stat_event_chr",
            [0x0051] = "GetsTurns",
            [0x0052] = "Targetable",
            [0x0053] = "VisibleOnCTB",
            [0x0054] = "stat_visible",
            [0x0055] = "AreaToMoveTo",
            [0x0056] = "PositionToMoveTo",
            [0x0057] = "stat_efflv",
            [0x0059] = "?Host",
            [0x005B] = "AnimationsVariant",
            [0x0060] = "stat_height_on",
            [0x0061] = "stat_sleep_recover_flag",
            [0x0062] = "AbsorbFire",
            [0x0063] = "AbsorbIce",
            [0x0064] = "AbsorbThunder",
            [0x0065] = "AbsorbWater",
            [0x0066] = "AbsorbHoly",
            [0x0067] = "NullFire",
            [0x0068] = "NullIce",
            [0x0069] = "NullThunder",
            [0x006A] = "NullWater",
            [0x006C] = "ResistFire",
            [0x006D] = "ResistIce",
            [0x006E] = "ResistThunder",
            [0x006F] = "ResistWater",
            [0x0071] = "WeakFire",
            [0x0072] = "WeakIce",
            [0x0073] = "WeakThunder",
            [0x0074] = "WeakWater",
            [0x0075] = "WeakHoly",
            [0x0076] = "stat_adjust_pos_flag",
            [0x0077] = "DullHitReactionToPhys",
            [0x0078] = "DullHitReactionToMag",
            [0x0079] = "TimesStolenFrom",
            [0x007A] = "stat_wait_motion_flag",
            [0x007C] = "stat_attack_normal_frame",
            [0x007D] = "?Tough (No Delay recoil)",
            [0x007E] = "?Heavy (No lift off ground)",
            [0x007F] = "stat_bodyhit_flag",
            [0x0080] = "stat_effvar",
            [0x0081] = "StealItemCommonType",
            [0x0082] = "StealItemCommonAmount",
            [0x0083] = "StealItemRareType",
            [0x0084] = "StealItemRareAmount",
            [0x0086] = "BirthAnimation",
            [0x0087] = "stat_cursor_element",
            [0x0088] = "stat_limit_bar_flag_cam",
            [0x0089] = "showOverdriveBar",
            [0x008A] = "Item1DropChance",
            [0x008B] = "Item2DropChance",
            [0x008C] = "GearDropChance",
            [0x008D] = "StealChance",
            [0x008E] = "?MustBeKilledForBattleEnd",
            [0x0097] = "StatusEject",
            [0x0098] = "StatusAutoLife",
            [0x009A] = "StatusDefend",
            [0x009B] = "StatusGuard",
            [0x009C] = "StatusSentinel",
            [0x009E] = "stat_motion_type",
            [0x00A2] = "stat_direction_change_flag",
            [0x00A3] = "stat_direction_change_effect",
            [0x00A4] = "stat_direction_fix_flag",
            [0x00A5] = "stat_hit_terminate_flag",
            [0x00A6] = "LastDamageTakenHP",
            [0x00AA] = "stat_effect_hit_num",
            [0x00AB] = "stat_avoid_flag",
            [0x00AC] = "stat_blow_exist_flag",
            [0x00AE] = "?Visible",
            [0x00BB] = "StatusResistanceSleep",
            [0x00CF] = "StatusImmunityBoost",
            [0x00D1] = "StatusImmunityEject",
            [0x00D7] = "VisibleOnFrontlinePartyList",
            [0x00D8] = "stat_visible_cam",
            [0x00D9] = "stat_visible_out",
            [0x00DA] = "stat_round",
            [0x00DB] = "stat_round_return",
            [0x00DE] = "stat_fast_model_flag",
            [0x00DF] = "notDeadOrPetrified",
            [0x00E0] = "stat_command_type",
            [0x00E1] = "stat_effect_target_flag",
            [0x00E2] = "stat_magic_effect_ground",
            [0x00E4] = "stat_idle2_prob",
            [0x00E5] = "stat_attack_motion_type",
            [0x00E6] = "stat_attack_inc_speed",
            [0x00E7] = "stat_attack_dec_speed",
            [0x00E8] = "CurrentTurnDelay",
            [0x00EA] = "stat_motion_num",
            [0x00ED] = "stat_visible_eff",
            [0x00EE] = "stat_motion_dispose_flag",
            [0x00F1] = "stat_shadow",
            [0x00F2] = "stat_death",
            [0x00F7] = "stat_near_motion",
            [0x00F8] = "presentWithoutDthPtfSlpSil",
            [0x00FA] = "?ForceCloseRangeAttackAnim",
            [0x00FC] = "stat_motion_speed_normal_start",
            [0x0100] = "RetainsControlWhenProvoked",
            [0x0101] = "ProvokerActor",
            [0x0103] = "CTBIconNumber",
            [0x0104] = "stat_sound_hit_num",
            [0x0105] = "stat_damage_num_pos",
            [0x0108] = "NullMagic",
            [0x0109] = "NullPhysical",
            [0x010A] = "LearnableRonsoRage",
            [0x010C] = "OverkillThreshold",
            [0x010D] = "stat_return_motion_type",
            [0x010E] = "stat_cam_width",
            [0x010F] = "stat_cam_height",
            [0x0110] = "stat_height",
            [0x0116] = "stat_attack_near_frame",
            [0x0122] = "?BribeImmunity",
            [0x0123] = "stat_attack_motion_frame",
            [0x0124] = "stat_motion_type_reset",
            [0x0125] = "stat_motion_type_add",
            [0x0126] = "stat_death_status",
            [0x0127] = "stat_target_list",
            [0x0128] = "stat_limit_bar_pos",
            [0x012B] = "APRewardNormal",
            [0x012C] = "APRewardOverkill",
            [0x012D] = "GilReward",
            [0x0139] = "isDoublecasting",
            [0x014A] = "ReturnsBeforeDeathAnimation",
            [0x014B] = "stat_linear_move_reset",
            [0x014D] = "?recruited (Aeon)",
            [0x0151] = "stat_regen_damage_flag",
            [0x0153] = "?disableLowHealthSlump",
            [0x0156] = "wasCaptured",
            [0x0159] = "?onlyTargetableBy",
        },
    };
}

internal static class AtelCallTargets
{
    private static AtelCallParameter P(string name, string type) => new(name, type);

    private static readonly Dictionary<int, AtelCallTargetInfo> Targets = new()
    {
        [0x005F] = new(0x005F, "halt", 0, "halt"),
        [0x00A6] = new(0x00A6, "GetRandomInRange", 1),
        [0x00A9] = new(0x00A9, "GetRandomValue", 0),
        [0x015B] = new(0x015B, "obtainTreasure", 2, P("msgWindow", "int"), P("treasure", "treasure")),
        [0x01A7] = new(0x01A7, "obtainTreasureSilently", 1, P("treasure", "treasure")),
        [0x01B7] = new(0x01B7, "applyBrotherhoodPowerup", 1, P("treasure", "treasure")),
        [0x7000] = new(0x7000, "btlTerminateAction", 0),
        [0x7001] = new(0x7001, "btlSetRandPosFlag", 1),
        [0x7002] = new(0x7002, "launchBattle", 2, "btlExe", P("battle", "battle"), P("transition", "battleTransition")),
        [0x7003] = new(0x7003, "btlDirTarget", 2),
        [0x7004] = new(0x7004, "btlSetDirRate", 1),
        [0x7005] = new(0x7005, "isWater", 0, "btlGetWater"),
        [0x7006] = new(0x7006, "btlDirBasic", 2),
        [0x7007] = new(0x7007, "startMotion", 1, "btlSetMotion", P("motion", "stdmotion")),
        [0x7008] = new(0x7008, "awaitMotion", 0, "btlWaitMotion"),
        [0x7009] = new(0x7009, "setSelfGravity", 1, "btlSetGravity", P("affectedByGravity", "bool")),
        [0x700A] = new(0x700A, "setHeight", 2, "btlSetHeight", P("mode", "int"), P("height", "float")),
        [0x700B] = new(0x700B, "performCommand", 2, "btlSetDirectCommand", P("target", "btlChr"), P("command", "command")),
        [0x700C] = new(0x700C, "btlMove", 8),
        [0x700D] = new(0x700D, "btlDirPos", 2),
        [0x700E] = new(0x700E, "btlSetDamage", 1),
        [0x700F] = new(0x700F, "readBtlChrProperty", 2, "btlGetStat", P("subject", "btlChr"), P("property", "btlChrProperty")),
        [0x7010] = new(0x7010, "findMatchingChr", 4, "btlSearchChr", P("group", "btlChr"), P("property", "btlChrProperty"), P("unused", "unknown"), P("selector", "selector")),
        [0x7011] = new(0x7011, "btlCameraMode", 1),
        [0x7012] = new(0x7012, "btlTerminateEffect", 0),
        [0x7013] = new(0x7013, "btlChrSp", 1),
        [0x7014] = new(0x7014, "chosenCommand", 0, "btlGetComNum"),
        [0x7015] = new(0x7015, "print", 1, "btlPrint"),
        [0x7016] = new(0x7016, "stopMotion", 1, "btlTerminateMotion"),
        [0x7017] = new(0x7017, "btlSetNormalEffect", 2),
        [0x7018] = new(0x7018, "writeBtlChrProperty", 3, "btlSetStat", P("subject", "btlChr"), P("property", "btlChrProperty"), P("value", "unknown")),
        [0x7019] = new(0x7019, "usedCommand", 0, "btlGetReCom"),
        [0x701A] = new(0x701A, "readCommandProperty", 2, "btlGetComInfo", P("command", "command"), P("property", "commandProperty")),
        [0x701B] = new(0x701B, "overrideAttemptedCommand", 2, "btlChangeReCom", P("target", "btlChr"), P("command", "command")),
        [0x701C] = new(0x701C, "btlSetMotionLevel", 1),
        [0x701D] = new(0x701D, "btlGetMotionLevel", 0),
        [0x701E] = new(0x701E, "countChrOverlap", 2, "btlCountChr", P("group", "btlChr"), P("actor", "btlChr")),
        [0x701F] = new(0x701F, "btlChgWaitMotion", 1),
        [0x7020] = new(0x7020, "btlCheckStartEffect", 0),
        [0x7021] = new(0x7021, "dereferenceCharacter", 1, "btlGetChrNum", P("actor", "btlChr")),
        [0x7022] = new(0x7022, "setAmbushState", 1, "btlSetFirstAttack", P("state", "ambushState")),
        [0x7023] = new(0x7023, "btlDistTarget", 1),
        [0x7024] = new(0x7024, "currentBattle", 0, "btlGetBtlScene"),
        [0x7025] = new(0x7025, "findMatchingChrIncludingUntargetable", 4, "btlSearchChr2", P("group", "btlChr"), P("property", "btlChrProperty"), P("unknown", "unknown"), P("selector", "selector")),
        [0x7026] = new(0x7026, "btlSetWeak", 1, P("state", "weakState")),
        [0x7027] = new(0x7027, "btlGetWeak", 0),
        [0x7028] = new(0x7028, "scaleOwnSize", 3, "btlSetScale"),
        [0x7029] = new(0x7029, "setSelfFloating", 1, "btlSetFly"),
        [0x702A] = new(0x702A, "btlCheckBtlPos", 0),
        [0x702B] = new(0x702B, "btlCheckMotion", 0),
        [0x702C] = new(0x702C, "btlSetHoming", 9),
        [0x702D] = new(0x702D, "btlResetMove", 0),
        [0x702E] = new(0x702E, "btlMoveTargetDist", 1),
        [0x702F] = new(0x702F, "btlOut", 1),
        [0x7030] = new(0x7030, "btlGetMoveFlag", 0),
        [0x7031] = new(0x7031, "btlStartMotion", 0),
        [0x7032] = new(0x7032, "setActorFacingAngle", 2, "btlSetBtlPosDir", P("subject", "btlChr"), P("facingAngle", "float")),
        [0x7033] = new(0x7033, "btlSetEnMapID", 1),
        [0x7034] = new(0x7034, "endBattle", 1, "btlComplete", P("result", "battleEndType")),
        [0x7035] = new(0x7035, "battleEndType", 0, "btlGetCompInfo"),
        [0x7036] = new(0x7036, "btlSetTrans", 3),
        [0x7037] = new(0x7037, "addCommand", 2, "btlAddCom", P("actor", "btlChr"), P("command", "command")),
        [0x7038] = new(0x7038, "removeCommand", 2, "btlDelCom", P("actor", "btlChr"), P("command", "command")),
        [0x7039] = new(0x7039, "btlTerminateDeath", 0),
        [0x703A] = new(0x703A, "btlSetSpeed", 1),
        [0x703B] = new(0x703B, "setCommandDisabled", 3, "btlSetCommandUse", P("actor", "btlChr"), P("command", "command"), P("disabled", "bool")),
        [0x703C] = new(0x703C, "runBtlSceneA", 1, "btlOff", P("scene", "int")),
        [0x703D] = new(0x703D, "btlOn", 0),
        [0x703E] = new(0x703E, "btlWait", 0),
        [0x703F] = new(0x703F, "camReq", 2),
        [0x7040] = new(0x7040, "btlMagicStart", 1),
        [0x7041] = new(0x7041, "btlMagicEnd", 0),
        [0x7042] = new(0x7042, "displayBattleString", 5, "btlMes", P("msgWindow", "int"), P("string", "localString"), P("x", "int"), P("y", "int"), P("align", "textAlignment")),
        [0x7043] = new(0x7043, "closeTextOnConfirm", 1, "btlMesWait", P("msgWindow", "int")),
        [0x7044] = new(0x7044, "closeTextImmediately", 1, "btlMesClose", P("msgWindow", "int")),
        [0x7045] = new(0x7045, "btlDistTargetFrame", 1),
        [0x7046] = new(0x7046, "btlSplineStart", 1),
        [0x7047] = new(0x7047, "btlSplineRegist", 2),
        [0x7048] = new(0x7048, "btlSplineRegistPos", 4),
        [0x7049] = new(0x7049, "btlSplineMove", 4),
        [0x704A] = new(0x704A, "btlCheckMove", 1, P("actor", "btlChr")),
        [0x704B] = new(0x704B, "btlReqMotion", 3, P("actor", "btlChr"), P("motionIndex", "int"), P("await", "bool")),
        [0x704C] = new(0x704C, "btlWaitReqMotion", 1, P("actor", "btlChr")),
        [0x704D] = new(0x704D, "btlSetDeathLevel", 1),
        [0x704E] = new(0x704E, "btlSetDeathPattern", 1),
        [0x704F] = new(0x704F, "btlSetEventChrFlag", 2),
        [0x7050] = new(0x7050, "reviveOrReinitialize", 1, "btlResetParam", P("actor", "btlChr")),
        [0x7051] = new(0x7051, "btlWaitNormalEffect", 0),
        [0x7052] = new(0x7052, "attachActor", 3, "btlChrLink", P("actor", "btlChr"), P("host", "btlChr"), P("attachmentPoint", "int")),
        [0x7053] = new(0x7053, "btlMoveJump", 9),
        [0x7054] = new(0x7054, "btlSetChrPosElem", 3),
        [0x7055] = new(0x7055, "btlSetBodyHit", 1),
        [0x7056] = new(0x7056, "btlSetSpecialBattle", 1, P("setting", "specialBattleSetting")),
        [0x7057] = new(0x7057, "btlDirMove", 4),
        [0x7058] = new(0x7058, "btlCheckMotionNum", 2, P("actor", "btlChr"), P("motion", "stdmotion")),
        [0x7059] = new(0x7059, "btlMoveTargetDist2D", 1),
        [0x705A] = new(0x705A, "forcePerformCommand", 2, "btlSetAbsCommand", P("target", "btlChr"), P("command", "command")),
        [0x705B] = new(0x705B, "btlGetCamWidth", 1),
        [0x705C] = new(0x705C, "btlGetCamHeight", 1),
        [0x705D] = new(0x705D, "btlSetBindEffect", 2),
        [0x705E] = new(0x705E, "btlResetBindEffect", 0),
        [0x705F] = new(0x705F, "btlPrintF", 1),
        [0x7060] = new(0x7060, "btlSetStatEff", 0),
        [0x7061] = new(0x7061, "btlClearStatEff", 0),
        [0x7062] = new(0x7062, "btlSetHitEffect", 2),
        [0x7063] = new(0x7063, "btlWaitHitEffect", 0),
        [0x7064] = new(0x7064, "loadBattleVoiceLine", 1, "btlVoiceStandby", P("voiceFile", "voiceFile")),
        [0x7065] = new(0x7065, "btlVoiceStart", 0),
        [0x7066] = new(0x7066, "btlVoiceStop", 0),
        [0x7067] = new(0x7067, "btlGetVoiceStatus", 0),
        [0x7068] = new(0x7068, "btlVoiceSync", 0),
        [0x7069] = new(0x7069, "btlSearchChrCamera", 4),
        [0x706A] = new(0x706A, "btlCheckTargetOwn", 1),
        [0x706B] = new(0x706B, "btlSetModelHide", 3, P("actor", "btlChr"), P("part", "int"), P("show", "bool")),
        [0x706C] = new(0x706C, "btlSoundEffectNormal", 2),
        [0x706D] = new(0x706D, "btlSoundStreamNormal", 2),
        [0x706E] = new(0x706E, "btlReqVoice", 2),
        [0x706F] = new(0x706F, "btlSetMotion2", 1),
        [0x7070] = new(0x7070, "btlStatusOn", 0),
        [0x7071] = new(0x7071, "btlStatusOff", 0),
        [0x7072] = new(0x7072, "displayBattleDialogString", 2, "btlmes2", P("msgWindow", "int"), P("string", "localString")),
        [0x7073] = new(0x7073, "btlAttachWeapon", 1),
        [0x7074] = new(0x7074, "btlDetachWeapon", 1),
        [0x7075] = new(0x7075, "btlReqWeaponMotion", 3),
        [0x7076] = new(0x7076, "btlBallSplineMove", 3),
        [0x7077] = new(0x7077, "btlDistTargetFrameBall", 2),
        [0x7078] = new(0x7078, "readCommandPropertyForActor", 3, "btlGetComInfo2", P("actor", "btlChr"), P("command", "command"), P("property", "commandProperty")),
        [0x7079] = new(0x7079, "btlResetWeapon", 0),
        [0x707A] = new(0x707A, "btlGetCalcResult", 1),
        [0x707B] = new(0x707B, "playBattleSoundEffect", 2, "btlSoundEffect", P("actor", "btlChr"), P("sfx", "int")),
        [0x707C] = new(0x707C, "btlWaitSound", 0),
        [0x707D] = new(0x707D, "setDebugFlag", 2, "btlSetDebug", P("flag", "battleDebugFlag"), P("active", "bool")),
        [0x707E] = new(0x707E, "checkDebugFlagEnabled", 1, "btlGetDebug", P("flag", "battleDebugFlag")),
        [0x707F] = new(0x707F, "btlSetBtlPos", 1),
        [0x7080] = new(0x7080, "btlChangeAuron", 1),
        [0x7081] = new(0x7081, "btlWaitExe", 0),
        [0x7082] = new(0x7082, "btlSetFreeEffect", 2),
        [0x7083] = new(0x7083, "btlSetAfterImage", 2),
        [0x7084] = new(0x7084, "btlResetAfterImage", 0),
        [0x7085] = new(0x7085, "btlMoveAttack", 8),
        [0x7086] = new(0x7086, "btlUseChrMpLimit", 0),
        [0x7087] = new(0x7087, "btlSoundEffectFade", 3),
        [0x7088] = new(0x7088, "btlRegSoundEffect", 2),
        [0x7089] = new(0x7089, "btlRegSoundEffectFade", 3),
        [0x708A] = new(0x708A, "initializeEncounter", 1, "btlInitEncount", P("battle", "battle")),
        [0x708B] = new(0x708B, "btlGetEncount", 1, P("battle", "battle")),
        [0x708C] = new(0x708C, "setEncounterEnabled", 2, "btlSetEncount", P("battle", "battle"), P("active", "bool")),
        [0x708D] = new(0x708D, "btlGetLastActionChr", 0),
        [0x708E] = new(0x708E, "btlCheckBtlPos2", 0),
        [0x708F] = new(0x708F, "btlDirPosBasic", 1),
        [0x7090] = new(0x7090, "btlSetCriticalEffect", 1),
        [0x7091] = new(0x7091, "changeActorNameToCharName", 2, "btlChangeChrName", P("actor", "btlChr"), P("newName", "playerChar")),
        [0x7092] = new(0x7092, "btlGetGroundDist", 1),
        [0x7093] = new(0x7093, "btlCheckDirFlag", 0),
        [0x7094] = new(0x7094, "btlSetTransVisible", 3),
        [0x7095] = new(0x7095, "btlGetMoveFrameRest", 0),
        [0x7096] = new(0x7096, "btlGetReflect", 0),
        [0x7097] = new(0x7097, "runBtlSceneB", 1, "btlOff2", P("scene", "int")),
        [0x7098] = new(0x7098, "btlCheckDefenseMotion", 0),
        [0x7099] = new(0x7099, "btlSetCursorType", 1),
        [0x709A] = new(0x709A, "btlCheckPoison", 0),
        [0x709B] = new(0x709B, "btlGetChrPosY", 1),
        [0x709C] = new(0x709C, "btlGetTargetDir", 2),
        [0x709D] = new(0x709D, "btlWaitMotionAvoid", 0, "btlWaitMotion_avoid"),
        [0x709E] = new(0x709E, "btlSetMotionSignal", 3),
        [0x709F] = new(0x709F, "btlGetChrTargetDir", 1),
        [0x70A0] = new(0x70A0, "btlSetUpVectorFlag", 1),
        [0x70A1] = new(0x70A1, "dereferenceEnemy", 1, "btlGetChrNum2", P("actor", "btlChr")),
        [0x70A2] = new(0x70A2, "btlMotionRead", 1),
        [0x70A3] = new(0x70A3, "btlSetMotionAbs", 1),
        [0x70A4] = new(0x70A4, "btlMotionDispose", 0),
        [0x70A5] = new(0x70A5, "btlSetMapCenter", 3),
        [0x70A6] = new(0x70A6, "btlSetEscape", 1),
        [0x70A7] = new(0x70A7, "btlGetMotionData", 2),
        [0x70A8] = new(0x70A8, "setMotionValueForActor", 3, "btlSetMotionData", P("actor", "btlChr"), P("property", "motionProperty"), P("value", "float")),
        [0x70A9] = new(0x70A9, "btlmeswait_voice", 1),
        [0x70AA] = new(0x70AA, "readBtlChrProperty2", 1, "btlGetStat2", P("property", "btlChrProperty")),
        [0x70AB] = new(0x70AB, "writeBtlChrProperty2", 2, "btlSetStat2", P("property", "btlChrProperty"), P("value", "unknown")),
        [0x70AC] = new(0x70AC, "readMotionProperty2", 1, "btlGetMotionData2", P("property", "motionProperty")),
        [0x70AD] = new(0x70AD, "btlCheckWakkaWeapon", 0),
        [0x70AE] = new(0x70AE, "btlGetLastDeathChr", 0),
        [0x70AF] = new(0x70AF, "btlGetVoiceFlag", 0),
        [0x70B0] = new(0x70B0, "btlDistTargetFrame2", 1),
        [0x70B1] = new(0x70B1, "btlPrintSp", 1),
        [0x70B2] = new(0x70B2, "setMotionValue", 2, "btlSetMotionData2", P("property", "motionProperty"), P("value", "float")),
        [0x70B3] = new(0x70B3, "setCommandDialogVoiceLine", 1, "btlVoiceSet", P("voiceFile", "voiceFile")),
        [0x70B4] = new(0x70B4, "btlFadeOutWeapon", 0),
        [0x70B5] = new(0x70B5, "btlResetMotionSpeed", 0),
        [0x70B6] = new(0x70B6, "btlDistTargetFrameSpd", 1),
        [0x70B7] = new(0x70B7, "btlmesa", 2),
        [0x70B8] = new(0x70B8, "setDefendingEnabled", 1, "btlSetSkipMode", P("enabled", "bool")),
        [0x70B9] = new(0x70B9, "btlGetCamWidth2", 1),
        [0x70BA] = new(0x70BA, "btlGetCamHeight2", 1),
        [0x70BB] = new(0x70BB, "btlMoveLeave", 1),
        [0x70BC] = new(0x70BC, "btlWaitNomEff", 1),
        [0x70BD] = new(0x70BD, "btlWaitHitEff", 1),
        [0x70BE] = new(0x70BE, "btlGetChrDir", 1),
        [0x70BF] = new(0x70BF, "btlSetBindScale", 1),
        [0x70C0] = new(0x70C0, "btlGetHeight", 1),
        [0x70C1] = new(0x70C1, "btlDistTarget2", 2),
        [0x70C2] = new(0x70C2, "btlGetTargetDirH", 2),
        [0x70C3] = new(0x70C3, "btlGetChrTargetDir2", 1),
        [0x70C4] = new(0x70C4, "btlEquipWakkaWeapon", 1),
        [0x70C5] = new(0x70C5, "btlCheckRetBtlPos", 0),
        [0x70C6] = new(0x70C6, "btlGetCameraBuffer", 1),
        [0x70C7] = new(0x70C7, "btlGetCameraBufferFloat", 1),
        [0x70C8] = new(0x70C8, "btlSoundEffect2", 2),
        [0x70C9] = new(0x70C9, "btlSoundEffect3", 2),
        [0x70CA] = new(0x70CA, "btlRegSoundEffect2", 2),
        [0x70CB] = new(0x70CB, "btlRegSoundEffect3", 2),
        [0x70CC] = new(0x70CC, "initializeMatchingGroupTo", 1, "btlSetOwnTarget", P("actor", "btlChr")),
        [0x70CD] = new(0x70CD, "addToMatchingGroup", 1, "btlAddOwnTarget", P("actor", "btlChr")),
        [0x70CE] = new(0x70CE, "removeFromMatchingGroup", 1, "btlSubOwnTarget", P("actor", "btlChr")),
        [0x70CF] = new(0x70CF, "btlGetReverbe", 0),
        [0x70D0] = new(0x70D0, "btlSetCameraSelectMode", 1),
        [0x70D1] = new(0x70D1, "btlGetNomEff", 1),
        [0x70D2] = new(0x70D2, "btlGetHitEff", 1),
        [0x70D3] = new(0x70D3, "btlSetNomEff", 3),
        [0x70D4] = new(0x70D4, "btlSetHitEff", 3),
        [0x70D5] = new(0x70D5, "setSummoner", 1, "btlSetSummoner", P("summoner", "btlChr")),
        [0x70D6] = new(0x70D6, "calculateAverageDamage", 3, "btlGetAssumeDamage", P("user", "btlChr"), P("target", "btlChr"), P("command", "command")),
        [0x70D7] = new(0x70D7, "btlSetDamageMotion", 2),
        [0x70D8] = new(0x70D8, "btlSetAnimaChainOff", 1),
        [0x70D9] = new(0x70D9, "btlExeAnimaChainOff", 0),
        [0x70DA] = new(0x70DA, "btlGetFirstAttack", 0),
        [0x70DB] = new(0x70DB, "btlGetAnimaChainOff", 0),
        [0x70DC] = new(0x70DC, "changeChrName", 2, "btlChangeChrNameID", P("actor", "btlChr"), P("string", "localString")),
        [0x70DD] = new(0x70DD, "btlSetDebugCount", 1),
        [0x70DE] = new(0x70DE, "subtitlesEnabled", 0, "btlGetSubTitle"),
        [0x70DF] = new(0x70DF, "isBattleInField", 1, "btlCheckBtlScene", P("battle", "battle")),
        [0x70E0] = new(0x70E0, "isCounterattackAllowed", 0, "btlGetReaction"),
        [0x70E1] = new(0x70E1, "btlGetNormalAttack", 0),
        [0x70E2] = new(0x70E2, "btlSetTexAnime", 2),
        [0x70E3] = new(0x70E3, "btlGetEffectMemory", 1),
        [0x70E4] = new(0x70E4, "btlGetCalcResultLimit", 2),
        [0x70E5] = new(0x70E5, "btlSetNomEffReg", 3),
        [0x70E6] = new(0x70E6, "btlSetHitEffReg", 3),
        [0x70E7] = new(0x70E7, "btlSetRandomTarget", 1),
        [0x70E8] = new(0x70E8, "playerTotalGil", 0, "btlGetGold"),
        [0x70E9] = new(0x70E9, "yojimboHireAnswer", 0, "btlGetYoujinboType"),
        [0x70EA] = new(0x70EA, "setYojimboHireAnswer", 1, "btlSetYoujinboType"),
        [0x70EB] = new(0x70EB, "btlGetYoujinboRandom", 0),
        [0x70EC] = new(0x70EC, "btlGetItemNum", 1),
        [0x70ED] = new(0x70ED, "giveItem", 2, "btlGetItem", P("item", "command"), P("amount", "int")),
        [0x70EE] = new(0x70EE, "rollYojimboCommand", 2, "btlGetYoujinboCommand", P("motivation", "int"), P("unknown", "unknown")),
        [0x70EF] = new(0x70EF, "btlSetEffSignal", 2),
        [0x70F0] = new(0x70F0, "btlGetCameraCount", 0),
        [0x70F1] = new(0x70F1, "clearOwnCommands", 0, "btlCommandClear"),
        [0x70F2] = new(0x70F2, "addCommandToSelf", 1, "btlCommandSet", P("command", "command")),
        [0x70F3] = new(0x70F3, "rollMagusRandom", 2, "btlCheckMegasRandom", P("unknown", "unknown"), P("chance", "int")),
        [0x70F4] = new(0x70F4, "btlGetCommandTarget", 2),
        [0x70F5] = new(0x70F5, "btlCheckUseCommand", 2),
        [0x70F6] = new(0x70F6, "btlInitCommandBuffer", 0),
        [0x70F7] = new(0x70F7, "btlSetCommandBuffer", 1, P("command", "command")),
        [0x70F8] = new(0x70F8, "btlGetCommandBuffer", 0),
        [0x70F9] = new(0x70F9, "btlSearchChr3", 5),
        [0x70FA] = new(0x70FA, "btlSetMegasRandomCommand", 1),
        [0x70FB] = new(0x70FB, "searchCommandTarget", 5, "btlGetCommandTargetSearch", P("command", "command"), P("targeting", "magusTarget"), P("property", "btlChrProperty"), P("selector", "selector"), P("chance", "int")),
        [0x70FC] = new(0x70FC, "btlGetMegasRandomCommand", 0),
        [0x70FD] = new(0x70FD, "increaseMagusMotivationAndOverdrive", 2, "btlSetUpLimit", P("overdrive", "int"), P("motivation", "int")),
        [0x70FE] = new(0x70FE, "setMagusMotivationAndOverdriveChangeInPositiveOrNegativeCase", 4, "btlSetUpLimit2", P("overdrivePos", "int"), P("motivationPos", "int"), P("overdriveNeg", "int"), P("motivationNeg", "int")),
        [0x70FF] = new(0x70FF, "btlSetDeltaTarget", 0),
        [0x7100] = new(0x7100, "btlCheckReqMotion", 1),
        [0x7101] = new(0x7101, "isDebugBattleStart", 0, "btlGetFullCommand"),
        [0x7102] = new(0x7102, "makeChrHeadFaceChr", 2, "btlFaseTarget", P("actor", "btlChr"), P("target", "btlChr")),
        [0x7103] = new(0x7103, "makeChrHeadFacePoint", 4, "btlFaseTargetXYZ", P("actor", "btlChr"), P("x", "float"), P("y", "float"), P("z", "float")),
        [0x7104] = new(0x7104, "changeCommandAnimation", 3, "btlSetCommandEffect", P("command", "command"), P("anim1", "int"), P("anim2", "int")),
        [0x7105] = new(0x7105, "btlWaitStone", 0),
        [0x7106] = new(0x7106, "doesCharacterKnowCommand", 2, "btlCheckGetCommand", P("actor", "btlChr"), P("command", "command")),
        [0x7107] = new(0x7107, "btlDirPosBasic2", 1),
        [0x7108] = new(0x7108, "btlDirBasic2", 2),
        [0x7109] = new(0x7109, "btlSetAppear", 3),
        [0x710A] = new(0x710A, "btlSetSummonTiming", 0),
        [0x710B] = new(0x710B, "btlWaitSummonTiming", 0),
        [0x710C] = new(0x710C, "btlTerminateStone", 0),
        [0x710D] = new(0x710D, "btlDefensePosOff", 0),
        [0x710E] = new(0x710E, "btlGetWakkaLimitSkill", 0),
        [0x710F] = new(0x710F, "btlGetWakkaLimitNum", 0),
        [0x7110] = new(0x7110, "activateMouthMovement", 1, "btlMouseOn", P("actor", "btlChr")),
        [0x7111] = new(0x7111, "deactivateMouthMovement", 1, "btlMouseOff", P("actor", "btlChr")),
        [0x7112] = new(0x7112, "btlDirMove2", 4),
        [0x7113] = new(0x7113, "btlMonsterFarm", 0),
        [0x7114] = new(0x7114, "btlSphereMonitor", 0),
        [0x7115] = new(0x7115, "btlDirResetLeave", 0),
        [0x7116] = new(0x7116, "btlSetSummonDefenseEffect", 0),
        [0x7117] = new(0x7117, "overrideDeathAnimationWithCommand", 2, "btlSetDeathCommand", P("target", "btlChr"), P("command", "command")),
        [0x7118] = new(0x7118, "btlSetSummonGameOver", 1),
        [0x7119] = new(0x7119, "btlSetCounterFlag", 1),
        [0x711A] = new(0x711A, "btlSetWind", 4),
        [0x711B] = new(0x711B, "btlSetCameraStandard", 0),
        [0x711C] = new(0x711C, "btlSetGameOverEffNum", 1),
        [0x711D] = new(0x711D, "btlSetShadowHeight", 1),
        [0x7120] = new(0x7120, "displayBattleSystem01String", 2, null, P("msgWindow", "int"), P("string", "system01String")),
        [0x7123] = new(0x7123, "call_7123", 1),
        [0x7124] = new(0x7124, "setCommandDialogLineString", 1, null, P("string", "localString")),
        [0x7125] = new(0x7125, "call_7125", 0),
        [0x7126] = new(0x7126, "call_7126", 1),
        [0x7127] = new(0x7127, "call_7127", 2),
    };

    public static AtelCallTargetInfo? Lookup(int id)
        => Targets.TryGetValue(id, out var target) ? target : null;
}
