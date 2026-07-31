using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SpiraModifier.Core.Models;

public sealed class AtelAnalysisOptions
{
    public Func<int, string?>? ResolveCommandName { get; init; }
    public Func<int, string?>? ResolveCommandSource { get; init; }
    public Func<int, string?>? ResolveMonsterName { get; init; }
}

public static partial class AtelAnalyzer
{
    private const int MaxSectionLines = 28;

    public static string Analyze(AtelDecompiledScript script, AtelAnalysisOptions? options = null)
    {
        options ??= new AtelAnalysisOptions();

        var functions = BuildFunctionAnalyses(script);
        var sb = new StringBuilder();

        AppendHeader(sb, script);
        AppendHumanSummary(sb, script, functions, options);
        AppendStructure(sb, script);
        AppendActions(sb, script, options);
        AppendReactions(sb, script);
        AppendVisuals(sb, script);
        AppendConstants(sb, script, options);
        AppendFunctionRoles(sb, functions);
        AppendWarnings(sb, script);

        sb.AppendLine();
        sb.AppendLine("Notes");
        sb.AppendLine("- Analyse heuristique : les roles sont deduits des appels ATEL et des annotations du decompilateur.");
        sb.AppendLine("- Le listing brut reste la source de verite pour verifier un branchement ou une valeur exacte.");

        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, AtelDecompiledScript script)
    {
        sb.AppendLine("Resume ATEL");
        sb.AppendLine($"- Script : {Blank(script.ScriptId)}");
        sb.AppendLine($"- Createur : {Blank(script.CreatorTag)}");
        sb.AppendLine($"- Taille IA : 0x{script.RawSize:X4} ({script.RawSize.ToString("N0", CultureInfo.InvariantCulture)} octets)");
        sb.AppendLine($"- Code : 0x{script.ScriptCodeStartOffset:X4} + 0x{script.ScriptCodeLength:X4}");
        sb.AppendLine($"- Workers : {script.Workers.Count}  |  Instructions : {script.Instructions.Count}");
        sb.AppendLine($"- Main worker : w{script.MainWorkerIndex:X2}  |  Actors : {script.ActorCount}");
        sb.AppendLine();
    }

    private static void AppendHumanSummary(
        StringBuilder sb,
        AtelDecompiledScript script,
        IReadOnlyList<AtelFunctionAnalysis> functions,
        AtelAnalysisOptions options)
    {
        var annotations = script.Instructions
            .Select(i => i.Annotation)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(a => a!)
            .ToList();

        var allText = string.Join("\n", annotations);
        var actionDescriptions = annotations
            .Where(IsActionAnnotation)
            .Select(a => DescribeActionForHuman(a!, options))
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToList();

        var roleSummary = functions
            .Where(f => !string.IsNullOrWhiteSpace(f.Role) && f.Role != "vide / fin immediate")
            .GroupBy(f => f.Role)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(4)
            .Select(g => $"{g.Count()} x {g.Key}")
            .ToList();

        var emptyFunctionCount = functions.Count(f => f.Role == "vide / fin immediate");
        var disabledCombatFlags = CountNeedles(
            allText,
            "MustBeKilledForBattleEnd",
            "VisibleOnCTB",
            "GetsTurns",
            "Targetable");
        var looksLikeTechnicalActor = actionDescriptions.Count == 0
                                      && disabledCombatFlags >= 3
                                      && script.Instructions.Count <= 80;

        sb.AppendLine("Lecture humaine");
        sb.AppendLine(BuildOpeningSentence(script, functions, emptyFunctionCount));

        if (looksLikeTechnicalActor)
        {
            sb.AppendLine(
                "Ce script ressemble surtout a un acteur technique charge par la formation : il retire l'acteur du CTB, " +
                "l'empeche de prendre un tour, le rend non ciblable et evite qu'il compte pour la fin du combat.");
            sb.AppendLine(
                "En clair, l'ATEL du monstre ne semble pas porter une mecanique active ; il prepare plutot un slot silencieux que le script de scene peut garder en reserve.");
        }
        else if (actionDescriptions.Count > 0)
        {
            sb.AppendLine("Actions principales reperees : " + JoinSentence(actionDescriptions) + ".");
        }
        else
        {
            sb.AppendLine(
                "Aucune action directe n'a ete identifiee dans les annotations. Le script semble donc surtout preparer l'etat du combat, " +
                "attendre des evenements, ou deleguer la logique importante ailleurs.");
        }

        var reactionSentences = BuildReactionSentences(allText);
        if (reactionSentences.Count > 0)
            foreach (var sentence in reactionSentences)
                sb.AppendLine(sentence);

        var phaseSentences = BuildPhaseSentences(allText);
        if (phaseSentences.Count > 0)
            foreach (var sentence in phaseSentences)
                sb.AppendLine(sentence);

        var visualSentences = BuildVisualSentences(allText);
        if (visualSentences.Count > 0)
            foreach (var sentence in visualSentences)
                sb.AppendLine(sentence);

        if (roleSummary.Count > 0)
            sb.AppendLine("Repartition probable : " + string.Join(", ", roleSummary) + ".");

        if (script.Warnings.Count > 0)
            sb.AppendLine($"Attention : {script.Warnings.Count} avertissement(s) de decompilation sont presents ; le listing brut reste a verifier.");

        sb.AppendLine();
    }

    private static void AppendStructure(StringBuilder sb, AtelDecompiledScript script)
    {
        sb.AppendLine("Structure");
        if (script.Workers.Count == 0)
        {
            sb.AppendLine("- Aucun worker detecte.");
            sb.AppendLine();
            return;
        }

        foreach (var worker in script.Workers)
        {
            var role = GuessWorkerRole(worker);
            sb.AppendLine(
                $"- {worker.Label} : {worker.KindLabel}, {role}, " +
                $"{worker.FunctionCount} fonction(s), {worker.JumpCount} saut(s), " +
                $"{worker.VariableCount} var, refI={worker.RefIntCount}, refF={worker.RefFloatCount}");
        }

        sb.AppendLine();
    }

    private static void AppendActions(StringBuilder sb, AtelDecompiledScript script, AtelAnalysisOptions options)
    {
        sb.AppendLine("Actions detectees");
        var lines = InterestingLines(script, IsActionAnnotation)
            .Select(line => EnrichCommandLine(line, options))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxSectionLines)
            .ToList();

        if (lines.Count == 0)
            sb.AppendLine("- Aucune action directe reperee dans les annotations.");
        else
            foreach (var line in lines)
                sb.AppendLine("- " + line);

        sb.AppendLine();
    }

    private static void AppendReactions(StringBuilder sb, AtelDecompiledScript script)
    {
        sb.AppendLine("Conditions / reactions");
        var lines = InterestingLines(script, IsReactionAnnotation)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxSectionLines)
            .ToList();

        if (lines.Count == 0)
            sb.AppendLine("- Pas de reaction/counter evidente reperee.");
        else
            foreach (var line in lines)
                sb.AppendLine("- " + line);

        sb.AppendLine();
    }

    private static void AppendVisuals(StringBuilder sb, AtelDecompiledScript script)
    {
        sb.AppendLine("Camera / effets / motions");
        var lines = InterestingLines(script, IsVisualAnnotation)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxSectionLines)
            .ToList();

        if (lines.Count == 0)
            sb.AppendLine("- Aucun appel camera/effet/motion evident repere.");
        else
            foreach (var line in lines)
                sb.AppendLine("- " + line);

        sb.AppendLine();
    }

    private static void AppendConstants(StringBuilder sb, AtelDecompiledScript script, AtelAnalysisOptions options)
    {
        var commands = ExtractCommandIds(script)
            .Select(id => FormatCommandReference(id, options))
            .Distinct(StringComparer.Ordinal)
            .Take(18)
            .ToList();

        var monsters = ExtractMonsterNumbers(script)
            .Select(id => FormatMonsterReference(id, options))
            .Distinct(StringComparer.Ordinal)
            .Take(18)
            .ToList();

        if (commands.Count == 0 && monsters.Count == 0)
            return;

        sb.AppendLine("References utiles");
        foreach (var line in commands)
            sb.AppendLine("- Commande : " + line);
        foreach (var line in monsters)
            sb.AppendLine("- Monstre : " + line);
        sb.AppendLine();
    }

    private static void AppendFunctionRoles(StringBuilder sb, IReadOnlyList<AtelFunctionAnalysis> functions)
    {
        sb.AppendLine("Roles probables par fonction");
        if (functions.Count == 0)
        {
            sb.AppendLine("- Aucune fonction etiquetee.");
            sb.AppendLine();
            return;
        }

        foreach (var function in functions.Take(48))
        {
            sb.AppendLine($"- {function.Label} : {function.Role}");
            foreach (var detail in function.Highlights.Take(4))
                sb.AppendLine($"  * {detail}");
        }

        if (functions.Count > 48)
            sb.AppendLine($"- ... {functions.Count - 48} fonction(s) supplementaire(s) dans le listing brut.");

        sb.AppendLine();
    }

    private static void AppendWarnings(StringBuilder sb, AtelDecompiledScript script)
    {
        if (script.Warnings.Count == 0)
            return;

        sb.AppendLine("Avertissements de decompilation");
        foreach (var warning in script.Warnings.Take(12))
            sb.AppendLine("- " + warning);
        if (script.Warnings.Count > 12)
            sb.AppendLine($"- ... {script.Warnings.Count - 12} avertissement(s) supplementaire(s).");
        sb.AppendLine();
    }

    private static IReadOnlyList<AtelFunctionAnalysis> BuildFunctionAnalyses(AtelDecompiledScript script)
    {
        var functionOffsets = new HashSet<int>(
            script.Workers.SelectMany(w => w.Functions),
            EqualityComparer<int>.Default);

        var result = new List<AtelFunctionAnalysis>();
        AtelFunctionAnalysis? current = null;

        foreach (var instruction in script.Instructions)
        {
            if (script.LabelsByOffset.TryGetValue(instruction.Offset, out var labels))
            {
                var functionLabel = labels.FirstOrDefault(l => l.Contains("::f", StringComparison.Ordinal));
                if (functionLabel != null && functionOffsets.Contains(instruction.Offset))
                {
                    current = new AtelFunctionAnalysis(functionLabel, instruction.Offset);
                    result.Add(current);
                }
            }

            current?.Instructions.Add(instruction);
        }

        foreach (var function in result)
        {
            function.Role = GuessFunctionRole(function.Instructions);
            function.Highlights.AddRange(
                function.Instructions
                    .Where(i => !string.IsNullOrWhiteSpace(i.Annotation))
                    .Select(i => FormatInstructionLine(i))
                    .Where(IsUsefulHighlight)
                    .Distinct(StringComparer.Ordinal)
                    .Take(8));
        }

        return result;
    }

    private static IEnumerable<string> InterestingLines(AtelDecompiledScript script, Func<string, bool> predicate)
    {
        return script.Instructions
            .Where(i => !string.IsNullOrWhiteSpace(i.Annotation) && predicate(i.Annotation!))
            .Select(FormatInstructionLine);
    }

    private static bool IsActionAnnotation(string text)
        => ContainsAny(text,
            "performCommand(",
            "forcePerformCommand(",
            "addCommand(",
            "addCommandToSelf(",
            "overrideAttemptedCommand(",
            "giveItem(",
            "endBattle(",
            "runBtlScene(",
            "setCommandDisabled(",
            "clearOwnCommands(",
            "removeCommand(",
            "btlSetCommandBuffer(",
            "setCommandDialogLine",
            "changeCommandAnimation(",
            "overrideDeathAnimationWithCommand(");

    private static bool IsReactionAnnotation(string text)
        => ContainsAny(text,
            "if ",
            "isCounterattackAllowed",
            "CounterAttack",
            "LastDamageTakenHP",
            "LastAttacker",
            "usedCommand(",
            "chosenCommand(",
            "readCommandProperty",
            "currentBattle(",
            "isTargetAlive(",
            "searchTarget(",
            "findMatchingChr",
            "willDieToAttack");

    private static bool IsVisualAnnotation(string text)
        => ContainsAny(text,
            "cam",
            "Cam",
            "motion.",
            "Motion",
            "refSet",
            "refMove",
            "btlSetScale",
            "btlSetBindEffect",
            "btlSound",
            "Voice",
            "displayBattleMessage",
            "setCommandDialogVoiceLine",
            "changeActorName",
            "DeathAnimation");

    private static bool IsUsefulHighlight(string line)
        => !ContainsAny(line,
            "push ",
            "duplicate ",
            "return",
            "direct return",
            "no operation")
           || ContainsAny(line, "if ", "performCommand", "forcePerformCommand", "addCommand", "removeCommand", "setCommandDisabled", "motion.", "cam", "LastDamageTakenHP", ".HP", "HP [00h]", "OverdriveCurrent", "displayBattleMessage", "runBtlScene");

    private static string GuessWorkerRole(AtelWorker worker)
    {
        return worker.EventWorkerType switch
        {
            0x00 => "camera principale",
            0x01 => "motions / animations",
            0x02 => "logique de combat",
            0x03 => "gestionnaire BattleGrunt",
            0x04 => "scenes de combat",
            0x05 => "voix",
            0x06 => "hooks debut/fin",
            >= 0x07 and <= 0x0A => "camera de magie/commande",
            _ => "role inconnu"
        };
    }

    private static string GuessFunctionRole(IReadOnlyList<AtelInstruction> instructions)
    {
        var text = string.Join("\n", instructions.Select(i => i.Annotation ?? i.Mnemonic));
        var interesting = instructions.Count(i => !string.IsNullOrWhiteSpace(i.Annotation)
                                                 && !IsUsefulHighlight(FormatInstructionLine(i)));

        if (instructions.Count <= 2 && ContainsAny(text, "return", "halt", "direct return"))
            return "vide / fin immediate";
        if (ContainsAny(text, "isCounterattackAllowed", "CounterAttack", "LastDamageTakenHP", "LastAttacker", "usedCommand("))
            return "reaction, contre ou verification de l'action recue";
        if (ContainsAny(text, ".HP", "HP [00h]", "OverdriveCurrent", "setCommandDisabled(", "removeCommand(", "clearOwnCommands(", "runBtlScene(", "displayBattleMessage", "btlSetModelHide", "changeActorName"))
            return "phase ou changement de comportement";
        if (ContainsAny(text, "MustBeKilledForBattleEnd", "VisibleOnCTB", "GetsTurns", "Targetable"))
            return "initialisation des proprietes combat / visibilite";
        if (ContainsAny(text, "performCommand(", "forcePerformCommand(", "addCommand", "searchCommandTarget", "btlSetCommandBuffer"))
            return "selection de cible / execution de commande";
        if (ContainsAny(text, "currentBattle(", "cam", "Cam", "refSet", "refMove", "displayBattleMessage"))
            return "camera, scene ou message de combat";
        if (ContainsAny(text, "motion.", "btlSetScale", "btlSetBindEffect", "DeathAnimation", "setHeight("))
            return "initialisation visuelle, motion ou effet";
        if (ContainsAny(text, "setAmbushState", "setBattleFlag", "clearOwnCommands", "addCommandToSelf"))
            return "initialisation des regles du combat";
        if (interesting == 0 && instructions.All(i => i.Mnemonic is "RET" or "DRET" or "HALT" or "NOP"))
            return "vide / reserve";

        return "logique ATEL generale";
    }

    private static IEnumerable<int> ExtractCommandIds(AtelDecompiledScript script)
    {
        foreach (var instruction in script.Instructions)
        {
            var text = instruction.Annotation ?? "";
            foreach (Match match in CommandIdRegex().Matches(text))
            {
                if (int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
                    yield return id;
            }
        }
    }

    private static IEnumerable<int> ExtractMonsterNumbers(AtelDecompiledScript script)
    {
        foreach (var instruction in script.Instructions)
        {
            var text = instruction.Annotation ?? "";
            foreach (Match match in MonsterTypeRegex().Matches(text))
            {
                if (int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    yield return id;
            }
        }
    }

    private static string BuildOpeningSentence(
        AtelDecompiledScript script,
        IReadOnlyList<AtelFunctionAnalysis> functions,
        int emptyFunctionCount)
    {
        var nonEmpty = Math.Max(0, functions.Count - emptyFunctionCount);
        var creator = string.IsNullOrWhiteSpace(script.CreatorTag)
            ? ""
            : $" cree par {script.CreatorTag}";

        return
            $"Cet ATEL{creator} contient {script.Workers.Count} worker(s), {functions.Count} fonction(s) etiquetee(s) " +
            $"dont {nonEmpty} avec une logique visible, et {script.Instructions.Count} instruction(s).";
    }

    private static List<string> BuildReactionSentences(string allText)
    {
        var sentences = new List<string>();

        if (ContainsAny(allText, "isCounterattackAllowed", "CounterAttack", "LastDamageTakenHP", "LastAttacker"))
        {
            sentences.Add(
                "Il surveille les degats recus, le dernier attaquant ou l'autorisation de contre-attaque : cette zone ressemble a une reaction/counter.");
        }

        if (ContainsAny(allText, "usedCommand(", "chosenCommand(", "readCommandProperty"))
        {
            sentences.Add(
                "Il lit aussi la derniere commande ou les proprietes d'une commande, ce qui sert souvent a reagir a une action precise du joueur ou d'un acteur.");
        }

        if (ContainsAny(allText, "currentBattle("))
        {
            sentences.Add(
                "Des branches dependent de l'ID de combat courant : le meme ATEL peut donc ajuster son comportement selon la scene ou la phase.");
        }

        return sentences;
    }

    private static List<string> BuildVisualSentences(string allText)
    {
        var sentences = new List<string>();

        if (ContainsAny(allText, "motion.", "stat_motion", "btlSetScale", "btlSetBindEffect", "DeathAnimation", "setHeight("))
        {
            sentences.Add(
                "Une partie du script initialise les animations, motions, tailles ou effets visuels du monstre.");
        }

        if (ContainsAny(allText, "camReq", "cam", "refSet", "refMove", "displayBattleMessage"))
        {
            sentences.Add(
                "On voit aussi des appels de camera ou de mise en scene, donc certaines fonctions servent probablement a presenter une phase du combat.");
        }

        return sentences;
    }

    private static List<string> BuildPhaseSentences(string allText)
    {
        var sentences = new List<string>();

        if (ContainsAny(allText, ".HP", "HP [00h]", "OverdriveCurrent"))
        {
            sentences.Add(
                "Le script lit des valeurs comme les HP ou l'Overdrive : cela peut servir de seuil pour changer de comportement ou declencher une transition.");
        }

        if (ContainsAny(allText, "setCommandDisabled(", "addCommand(", "removeCommand(", "clearOwnCommands("))
        {
            sentences.Add(
                "Il modifie aussi des commandes disponibles, ce qui est souvent un indice de phase, d'enrage ou de changement de pattern.");
        }

        if (ContainsAny(allText, "runBtlScene(", "displayBattleMessage", "btlSetModelHide", "changeActorName"))
        {
            sentences.Add(
                "On voit des marqueurs de transition visible, comme une scene, un message, un changement de modele ou de nom.");
        }

        return sentences;
    }

    private static string? DescribeActionForHuman(string annotation, AtelAnalysisOptions options)
    {
        var command = ResolveFirstCommandInText(annotation, options)
                      ?? CleanValue(ExtractCallArgument(annotation, "command"));
        var actor = CleanValue(ExtractCallArgument(annotation, "actor"));
        var target = CleanValue(ExtractCallArgument(annotation, "target"));

        if (annotation.Contains("performCommand(", StringComparison.OrdinalIgnoreCase))
            return $"execute {command ?? "une commande"} sur {target ?? "une cible"}";
        if (annotation.Contains("forcePerformCommand(", StringComparison.OrdinalIgnoreCase))
            return $"force {command ?? "une commande"} sur {target ?? "une cible"}";
        if (annotation.Contains("addCommandToSelf(", StringComparison.OrdinalIgnoreCase))
            return $"ajoute {command ?? "une commande"} au menu de l'acteur";
        if (annotation.Contains("addCommand(", StringComparison.OrdinalIgnoreCase))
            return $"ajoute {command ?? "une commande"} a {actor ?? "un acteur"}";
        if (annotation.Contains("removeCommand(", StringComparison.OrdinalIgnoreCase))
            return $"retire {command ?? "une commande"} a {actor ?? "un acteur"}";
        if (annotation.Contains("setCommandDisabled(", StringComparison.OrdinalIgnoreCase))
            return $"active/desactive {command ?? "une commande"} pour {actor ?? "un groupe"}";
        if (annotation.Contains("overrideAttemptedCommand(", StringComparison.OrdinalIgnoreCase))
            return $"remplace la commande tentee par {command ?? "une autre commande"}";
        if (annotation.Contains("changeCommandAnimation(", StringComparison.OrdinalIgnoreCase))
            return $"change l'animation de {command ?? "une commande"}";
        if (annotation.Contains("overrideDeathAnimationWithCommand(", StringComparison.OrdinalIgnoreCase))
            return $"remplace l'animation de mort par {command ?? "une commande"}";
        if (annotation.Contains("giveItem(", StringComparison.OrdinalIgnoreCase))
            return "donne un objet";
        if (annotation.Contains("endBattle(", StringComparison.OrdinalIgnoreCase))
            return "declenche une fin de combat";
        if (annotation.Contains("runBtlScene(", StringComparison.OrdinalIgnoreCase))
            return "lance une scene de combat";
        if (annotation.Contains("setCommandDialogLine", StringComparison.OrdinalIgnoreCase))
            return "prepare une ligne de dialogue de commande";

        return null;
    }

    private static string? ResolveFirstCommandInText(string text, AtelAnalysisOptions options)
    {
        foreach (Match match in CommandIdRegex().Matches(text))
        {
            if (int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
                return FormatCommandReference(id, options);
        }

        return null;
    }

    private static string? ExtractCallArgument(string text, string name)
    {
        var marker = name + "=";
        var start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;

        start += marker.Length;
        var comma = text.IndexOf(", ", start, StringComparison.Ordinal);
        var paren = text.IndexOf(')', start);
        var end = comma >= 0 && (paren < 0 || comma < paren) ? comma : paren;
        if (end < 0) end = text.Length;
        return text[start..end];
    }

    private static string? CleanValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();
        return value.Length > 90 ? value[..87] + "..." : value;
    }

    private static int CountNeedles(string value, params string[] needles)
        => needles.Count(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string JoinSentence(IReadOnlyList<string> parts)
    {
        if (parts.Count == 0) return "";
        if (parts.Count == 1) return parts[0];
        if (parts.Count == 2) return parts[0] + " ; " + parts[1];
        return string.Join(" ; ", parts.Take(parts.Count - 1)) + " ; " + parts[^1];
    }

    private static string EnrichCommandLine(string line, AtelAnalysisOptions options)
    {
        var commandNotes = CommandIdRegex()
            .Matches(line)
            .Select(m => int.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id)
                ? FormatCommandReference(id, options)
                : null)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return commandNotes.Count == 0
            ? line
            : $"{line}  =>  {string.Join(", ", commandNotes)}";
    }

    private static string FormatCommandReference(int id, AtelAnalysisOptions options)
    {
        var source = options.ResolveCommandSource?.Invoke(id);
        var name = options.ResolveCommandName?.Invoke(id);
        var label = $"0x{id:X4}";
        if (!string.IsNullOrWhiteSpace(source))
            label += $" {source}";
        if (!string.IsNullOrWhiteSpace(name))
            label += $" - {name}";
        return label;
    }

    private static string FormatMonsterReference(int monsterNumber, AtelAnalysisOptions options)
    {
        var name = options.ResolveMonsterName?.Invoke(monsterNumber);
        var label = $"m{monsterNumber:000}";
        if (!string.IsNullOrWhiteSpace(name))
            label += $" - {name}";
        return label;
    }

    private static string FormatInstructionLine(AtelInstruction instruction)
    {
        var annotation = string.IsNullOrWhiteSpace(instruction.Annotation)
            ? instruction.Mnemonic
            : instruction.Annotation!;
        return $"0x{instruction.Offset:X4}: {annotation}";
    }

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? "(aucun)" : value;

    [GeneratedRegex("0x([2364][0-9A-Fa-f]{3})")]
    private static partial Regex CommandIdRegex();

    [GeneratedRegex("MonsterType m([0-9]{3})")]
    private static partial Regex MonsterTypeRegex();

    private sealed class AtelFunctionAnalysis
    {
        public AtelFunctionAnalysis(string label, int offset)
        {
            Label = label;
            Offset = offset;
        }

        public string Label { get; }
        public int Offset { get; }
        public string Role { get; set; } = "";
        public List<AtelInstruction> Instructions { get; } = new();
        public List<string> Highlights { get; } = new();
    }
}
