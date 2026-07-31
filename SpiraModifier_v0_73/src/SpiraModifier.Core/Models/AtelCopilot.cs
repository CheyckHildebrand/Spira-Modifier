using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SpiraModifier.Core.Models;

public sealed class AtelCopilotContext
{
    public string MonsterDisplayName { get; init; } = "";
    public string MonsterFileName { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public AtelDecompiledScript Script { get; init; } = null!;
    public byte[]? AiBytes { get; init; }
    public AtelAnalysisOptions AnalysisOptions { get; init; } = new();
    public string AnalysisText { get; init; } = "";
    public IReadOnlyList<AtelCommandCatalogEntry> CommandCatalog { get; init; } = Array.Empty<AtelCommandCatalogEntry>();
    public IReadOnlyList<string> CommandCatalogDiagnostics { get; init; } = Array.Empty<string>();
    public AtelGlobalIndex? GlobalIndex { get; init; }
}

public sealed record AtelCommandCatalogEntry(int Id, string Source, string Name);

public static partial class AtelCopilot
{
    private const int MaxListedLines = 18;

    public static string CreateWelcome(AtelCopilotContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Copilote ATEL pret pour {context.MonsterDisplayName}.");
        sb.AppendLine();
        sb.AppendLine("Je peux expliquer la structure, isoler les actions/counters, commenter une fonction comme w01::f03, ou proposer un plan de modification.");
        sb.AppendLine("Mode actuel : lecture et plan uniquement. Aucun octet n'est modifie.");
        sb.AppendLine();
        sb.AppendLine("Exemples :");
        sb.AppendLine("- Resume ce monstre.");
        sb.AppendLine("- Ou est le counter ?");
        sb.AppendLine("- Explique w01::f03.");
        sb.AppendLine("- Propose un plan pour ajouter une riposte avec Eau.");
        return sb.ToString();
    }

    public static string Answer(AtelCopilotContext context, string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return CreateWelcome(context);

        var normalized = Normalize(prompt);
        var functionMatch = FunctionLabelRegex().Match(prompt);
        if (functionMatch.Success)
            return ExplainFunction(context, functionMatch.Value);
        var workerMatch = WorkerLabelRegex().Match(prompt);
        if (workerMatch.Success)
            return ExplainWorker(context, workerMatch.Value);

        if (ContainsAny(normalized, "modif", "modifier", "modifie", "ajoute", "ajouter", "plan", "patch", "soin", "soigner", "riposte", "contre attaque", "contre-attaque"))
            return BuildModificationPlan(context, prompt, normalized);

        if (ContainsAny(normalized, "action", "attaque", "commande", "lance", "utilise", "sort"))
            return ExplainActions(context);

        if (ContainsAny(normalized, "counter", "contre", "reaction", "riposte", "degat", "dernier attaquant"))
            return ExplainReactions(context);

        if (AtelKnowledgeBase.IsPhasePrompt(normalized))
            return ExplainPhases(context);

        if (ContainsAny(normalized, "camera", "motion", "animation", "effet", "visuel", "scene"))
            return ExplainVisuals(context);

        if (ContainsAny(normalized, "dummy", "technique", "invisible", "ciblable", "ctb", "tour", "inutile", "nishida"))
            return ExplainTechnicalActorRisk(context);

        if (ContainsAny(normalized, "resume", "resumer", "explique", "structure", "que fait", "analyse"))
            return ExplainOverview(context);

        return BuildFallback(context, prompt);
    }

    private static string ExplainOverview(AtelCopilotContext context)
    {
        var functions = BuildFunctionAnalyses(context.Script);
        var sb = new StringBuilder();
        sb.AppendLine("Lecture du copilote");
        sb.AppendLine($"- {context.MonsterDisplayName} utilise {context.Script.Workers.Count} worker(s), {functions.Count} fonction(s) etiquetee(s), {context.Script.Instructions.Count} instruction(s).");
        sb.AppendLine($"- Script : {Blank(context.Script.ScriptId)} ; createur : {Blank(context.Script.CreatorTag)}.");

        var roles = functions
            .Where(f => f.Role != "vide / fin immediate")
            .GroupBy(f => f.Role)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Take(5)
            .Select(g => $"{g.Count()} x {g.Key}")
            .ToList();

        if (roles.Count > 0)
            sb.AppendLine("- Roles dominants : " + string.Join(", ", roles) + ".");
        else
            sb.AppendLine("- Le script semble surtout vide ou reserve.");

        AppendTechnicalActorNote(sb, context.Script);
        AppendWarnings(sb, context.Script);
        sb.AppendLine();
        sb.AppendLine("Pour aller plus loin, demande par exemple : \"explique w01::f03\", \"ou est le counter ?\", ou \"propose un plan pour ajouter une riposte\".");
        return sb.ToString();
    }

    private static string ExplainActions(AtelCopilotContext context)
    {
        var actions = InterestingInstructions(context.Script, IsActionAnnotation).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("Actions / commandes reperees");
        if (actions.Count == 0)
        {
            sb.AppendLine("- Je ne vois pas d'appel direct du type performCommand/addCommand/endBattle dans les annotations.");
            sb.AppendLine("- Cela peut vouloir dire que ce script initialise surtout des etats, ou que la vraie logique est dans le script de scene.");
            return sb.ToString();
        }

        foreach (var line in actions.Take(MaxListedLines))
            sb.AppendLine("- " + EnrichCommandLine(line, context));
        if (actions.Count > MaxListedLines)
            sb.AppendLine($"- ... {actions.Count - MaxListedLines} autre(s) action(s) dans le listing.");

        return sb.ToString();
    }

    private static string ExplainReactions(AtelCopilotContext context)
    {
        var reactions = InterestingInstructions(context.Script, IsReactionAnnotation).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("Reactions / counters");
        if (reactions.Count == 0)
        {
            sb.AppendLine("- Je ne vois pas de bloc de reaction evident : pas de LastDamageTakenHP, LastAttacker, usedCommand ou isCounterattackAllowed dans les annotations.");
            return sb.ToString();
        }

        sb.AppendLine("- Les indices principaux sont les lignes qui testent les degats recus, le dernier attaquant, la commande utilisee ou l'autorisation de contre.");
        foreach (var line in reactions.Take(MaxListedLines))
            sb.AppendLine("- " + EnrichCommandLine(line, context));
        if (reactions.Count > MaxListedLines)
            sb.AppendLine($"- ... {reactions.Count - MaxListedLines} autre(s) condition(s) dans le listing.");

        return sb.ToString();
    }

    private static string ExplainPhases(AtelCopilotContext context)
    {
        var phaseLines = InterestingInstructions(context.Script, IsPhaseAnnotation).ToList();
        var strongPhaseLines = InterestingInstructions(context.Script, IsStrongPhaseAnnotation).ToList();
        var displayedLines = strongPhaseLines.Count > 0 ? strongPhaseLines : phaseLines;
        var allText = string.Join("\n", context.Script.Instructions.Select(i => i.Annotation ?? ""));
        var strongSignals = CountNeedles(
            allText,
            ".HP",
            "HP [00h]",
            "OverdriveCurrent",
            "setCommandDisabled(",
            "addCommand(",
            "removeCommand(",
            "clearOwnCommands(",
            "runBtlScene(",
            "displayBattleMessage",
            "btlSetModelHide",
            "changeActorName");

        var sb = new StringBuilder();
        if (displayedLines.Count == 0)
        {
            sb.AppendLine("Je ne vois pas de vraie phase de combat evidente dans cet ATEL seul.");
            sb.AppendLine("Il peut quand meme y avoir une transition geree par le fichier de scene, mais dans le script monstre je ne retrouve pas les marqueurs habituels : seuil HP, changement de commandes, message, scene, modele ou variable de phase claire.");
            return sb.ToString();
        }

        if (strongSignals >= 4)
        {
            sb.AppendLine("Oui, il y a de bons indices d'une logique de phases ou au minimum d'un changement de comportement en combat.");
        }
        else
        {
            sb.AppendLine("Je vois des indices de variation, mais je resterais prudent : ce n'est pas encore une phase de boss certaine sans verifier le contexte du combat.");
        }

        sb.AppendLine("Les lignes les plus parlantes :");
        foreach (var line in displayedLines.Take(MaxListedLines))
            sb.AppendLine("- " + EnrichCommandLine(line, context));
        if (displayedLines.Count > MaxListedLines)
            sb.AppendLine($"- ... {displayedLines.Count - MaxListedLines} autre(s) indice(s) possible(s) dans le listing.");

        sb.AppendLine();
        sb.AppendLine("Comment je le lirais : un seuil HP/OD ou une variable qui bascule donne plutot une phase interne ; un message, une scene, un changement de modele ou de commandes donne plutot une transition visible. Si rien de tout ca n'est net, la phase peut etre pilotee par la scene de combat plutot que par cet ATEL monstre.");
        return sb.ToString();
    }

    private static string ExplainVisuals(AtelCopilotContext context)
    {
        var visuals = InterestingInstructions(context.Script, IsVisualAnnotation).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("Camera / animations / effets");
        if (visuals.Count == 0)
        {
            sb.AppendLine("- Aucun appel camera, motion ou effet visuel evident n'a ete repere.");
            return sb.ToString();
        }

        foreach (var line in visuals.Take(MaxListedLines))
            sb.AppendLine("- " + line);
        if (visuals.Count > MaxListedLines)
            sb.AppendLine($"- ... {visuals.Count - MaxListedLines} autre(s) ligne(s) visuelle(s).");

        return sb.ToString();
    }

    private static string ExplainFunction(AtelCopilotContext context, string label)
    {
        var function = BuildFunctionAnalyses(context.Script)
            .FirstOrDefault(f => string.Equals(f.Label, label, StringComparison.OrdinalIgnoreCase));

        var sb = new StringBuilder();
        if (function == null)
        {
            sb.AppendLine($"Je ne trouve pas la fonction {label} dans ce script.");
            sb.AppendLine("Verifie le label dans le listing brut, par exemple w00::f02.");
            return sb.ToString();
        }

        sb.AppendLine($"{function.Label}");
        sb.AppendLine($"- Role probable : {function.Role}.");
        sb.AppendLine($"- Offset : 0x{function.Offset:X4}.");
        sb.AppendLine($"- Instructions dans ce bloc lineaire : {function.Instructions.Count}.");

        var highlights = function.Instructions
            .Where(i => !string.IsNullOrWhiteSpace(i.Annotation))
            .Select(FormatInstructionLine)
            .Where(IsUsefulHighlight)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxListedLines)
            .ToList();

        if (highlights.Count == 0)
            sb.AppendLine("- Rien de vraiment parlant dans les annotations de cette fonction.");
        else
        {
            sb.AppendLine("- Lignes importantes :");
            foreach (var line in highlights)
                sb.AppendLine("  - " + EnrichCommandLine(line, context));
        }

        return sb.ToString();
    }

    private static string ExplainWorker(AtelCopilotContext context, string label)
    {
        var worker = context.Script.Workers.FirstOrDefault(w =>
            string.Equals(w.Label, label, StringComparison.OrdinalIgnoreCase));

        if (worker == null)
            return $"Je ne trouve pas le worker {label}.";

        var functions = BuildFunctionAnalyses(context.Script)
            .Where(f => f.Label.StartsWith(worker.Label + "::", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"{worker.Label} - {worker.KindLabel}");
        sb.AppendLine($"- Fonctions : {worker.FunctionCount}, sauts : {worker.JumpCount}, variables : {worker.VariableCount}, refI={worker.RefIntCount}, refF={worker.RefFloatCount}.");
        if (functions.Count > 0)
        {
            sb.AppendLine("- Fonctions reconnues :");
            foreach (var f in functions)
                sb.AppendLine($"  - {f.Label} : {f.Role}");
        }

        return sb.ToString();
    }

    private static string ExplainTechnicalActorRisk(AtelCopilotContext context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Lecture acteur technique / dummy");
        AppendTechnicalActorNote(sb, context.Script);
        sb.AppendLine();
        sb.AppendLine("Garde-fous avant recyclage :");
        sb.AppendLine("- Verifier dans quelles scenes de combat ce fichier monstre est reference.");
        sb.AppendLine("- Verifier si le script de scene cible ce slot par Monster#00..Monster#07.");
        sb.AppendLine("- Eviter de remplacer un dummy reference par un vrai monstre actif sans modifier la formation ou les flags.");
        sb.AppendLine("- Conserver un backup du fichier original et tester le combat en jeu.");
        return sb.ToString();
    }

    private static string BuildModificationPlan(AtelCopilotContext context, string prompt, string normalizedPrompt)
    {
        var functions = BuildFunctionAnalyses(context.Script);
        var candidateCommands = FindCommandCandidates(context, prompt, normalizedPrompt).Take(8).ToList();
        var candidateFunction = PickCandidateFunction(functions, normalizedPrompt);
        var wantsGauge = AtelKnowledgeBase.IsOverdrivePrompt(normalizedPrompt)
                         || ContainsAny(normalizedPrompt, "compteur", "charge");
        var knowledgeNotes = AtelKnowledgeBase.BuildNotes(prompt);
        var globalMatches = context.GlobalIndex?
            .Search(prompt, context.MonsterFileName, maxEntries: 5, maxLinesPerEntry: 6)
            .ToList()
            ?? new List<AtelGlobalIndexMatch>();

        var sb = new StringBuilder();
        sb.AppendLine("Plan propose par le copilote");
        sb.AppendLine("Important : ceci est un plan de modification, pas un patch applique. Le logiciel ne modifie aucun octet a cette etape.");
        sb.AppendLine();

        if (wantsGauge)
        {
            sb.AppendLine("Oui, l'idee est possible a traiter serieusement. Tu as raison sur le point important : le moteur expose bien des proprietes liees a une jauge Overdrive, donc je ne dois pas partir du principe qu'une jauge UI ennemie est impossible.");
            sb.AppendLine("Je le traiterais comme une mecanique Overdrive visible si les champs vanilla se confirment sur un exemple proche, avec fallback en compteur interne si un combat precis refuse l'affichage.");
            sb.AppendLine();
        }

        if (knowledgeNotes.Count > 0)
        {
            sb.AppendLine("Connaissances ATEL a garder en tete");
            foreach (var note in knowledgeNotes)
                sb.AppendLine("- " + note);
            sb.AppendLine();
        }

        if (globalMatches.Count > 0)
        {
            sb.AppendLine("Exemples retrouves dans l'index global ATEL");
            foreach (var match in globalMatches)
            {
                sb.AppendLine($"- {match.Entry.Summary}");
                foreach (var line in match.Snippets.Take(4))
                    sb.AppendLine("  - " + EnrichCommandLine(line, context));
            }
            sb.AppendLine();
        }
        else if (context.GlobalIndex != null && !context.GlobalIndex.IsEmpty)
        {
            sb.AppendLine($"Index global ATEL charge ({context.GlobalIndex.Entries.Count} fichier(s)), mais aucun extrait tres proche n'a ete trouve pour cette question.");
            sb.AppendLine();
        }

        sb.AppendLine("1. Intention comprise");
        sb.AppendLine("- " + prompt.Trim());
        if (candidateCommands.Count > 0)
        {
            sb.AppendLine("- Commandes qui ressemblent a la demande :");
            foreach (var command in candidateCommands)
                sb.AppendLine($"  - 0x{command.Id:X4} {command.Source} - {command.Name}");
        }
        else
        {
            sb.AppendLine("- Aucune commande exacte n'a ete trouvee par nom. Il faudra choisir l'ID de commande manuellement ou depuis les onglets Attaques/Commandes.");
        }

        sb.AppendLine();
        sb.AppendLine("2. Zone ATEL a viser");
        if (candidateFunction != null)
        {
            sb.AppendLine($"- Candidat principal : {candidateFunction.Label} ({candidateFunction.Role}).");
            foreach (var line in candidateFunction.Highlights.Take(5))
                sb.AppendLine("  - " + EnrichCommandLine(line, context));
        }
        else
        {
            sb.AppendLine("- Aucun bloc evident. Je commencerais par identifier la fonction qui choisit la cible ou la reaction/counter dans le listing.");
        }

        sb.AppendLine();
        sb.AppendLine("3. Pseudo-logique prudente");
        if (wantsGauge)
        {
            sb.AppendLine("- Initialiser la mecanique au debut du combat : OverdriveMax = 100, OverdriveCurrent = 0, showOverdriveBar = 1 si le pattern vanilla confirme les bons flags/modes.");
            sb.AppendLine("- Si l'affichage UI n'est pas stable sur ce monstre, garder en reserve une variable ATEL libre comme compteur de jauge logique.");
            sb.AppendLine("- Dans le bloc de reaction aux degats, verifier LastDamageTakenHP > 0 pour confirmer qu'un coup a vraiment touche.");
            sb.AppendLine("- Ajouter 10 a OverdriveCurrent, puis plafonner a OverdriveMax pour eviter les depassements.");
            sb.AppendLine("- Quand OverdriveCurrent atteint OverdriveMax, soit lancer une commande speciale, soit activer une phase/commande Overdrive, puis remettre la jauge a 0 apres usage si c'est le comportement voulu.");
            sb.AppendLine("- Recopier les valeurs exactes d'OverdriveMode, position de barre et flags UI depuis un exemple vanilla avant toute phase de patch automatique.");
        }
        else
        if (ContainsAny(normalizedPrompt, "contre", "counter", "riposte", "attaque physique", "degat"))
        {
            sb.AppendLine("- Se brancher dans le bloc qui verifie LastDamageTakenHP / LastAttacker / isCounterattackAllowed.");
            sb.AppendLine("- Ajouter une condition stricte pour eviter une boucle de riposte.");
            sb.AppendLine("- Choisir une cible explicite : Self pour un soin, LastAttacker pour une riposte offensive.");
        }
        else
        {
            sb.AppendLine("- Ajouter la condition au plus pres du bloc existant qui choisit une commande.");
            sb.AppendLine("- Eviter de dupliquer une logique deja presente dans une autre fonction.");
        }

        if (ContainsAny(normalizedPrompt, "soin", "soigner", "heal", "recup", "gueri"))
            sb.AppendLine("- Pour un soin, viser performCommand(target=Self, command=<sort/commande choisie>) ou une commande equivalente deja supportee par le monstre.");
        if (ContainsAny(normalizedPrompt, "eau", "water"))
            sb.AppendLine("- Pour une logique element Eau, verifier que la commande choisie est bien elementaire Eau dans monmagic/command avant de patcher l'ATEL.");

        sb.AppendLine();
        sb.AppendLine("4. Garde-fous obligatoires avant application future");
        sb.AppendLine("- Decompiler avant/apres et comparer le resume humain.");
        sb.AppendLine("- Verifier les labels de saut et l'equilibre de pile ATEL.");
        sb.AppendLine("- Verifier le nombre de parametres de chaque call.");
        sb.AppendLine("- Verifier que le chunk ATEL reconstruit reste dans une taille supportee.");
        sb.AppendLine("- Pour les jauges Overdrive visibles, comparer avec au moins un exemple vanilla indexe avant de valider les valeurs de proprietes.");
        sb.AppendLine("- Tester en jeu le combat qui reference ce monstre.");

        AppendWarnings(sb, context.Script);
        return sb.ToString();
    }

    private static string BuildFallback(AtelCopilotContext context, string prompt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Je n'ai pas encore un vrai modele de langage embarque : je reponds avec des heuristiques locales sur le decompilateur.");
        sb.AppendLine($"Question recue : {prompt.Trim()}");
        sb.AppendLine();
        sb.AppendLine("Je peux deja traiter ces demandes :");
        sb.AppendLine("- resume / structure");
        sb.AppendLine("- actions / commandes");
        sb.AppendLine("- reactions / counters");
        sb.AppendLine("- phases / changements de comportement");
        sb.AppendLine("- camera / animations / effets");
        sb.AppendLine("- expliquer un label precis comme w01::f03");
        sb.AppendLine("- proposer un plan prudent de modification");
        sb.AppendLine();
        sb.AppendLine($"Contexte courant : {context.MonsterDisplayName}, {context.Script.Workers.Count} worker(s), {context.Script.Instructions.Count} instruction(s).");
        return sb.ToString();
    }

    private static AtelCopilotFunction? PickCandidateFunction(IReadOnlyList<AtelCopilotFunction> functions, string normalizedPrompt)
    {
        if (ContainsAny(normalizedPrompt, "contre", "counter", "riposte", "degat", "dernier attaquant", "coup", "jauge", "overdrive", "hit"))
        {
            var reaction = functions.FirstOrDefault(f => f.Role.Contains("reaction", StringComparison.OrdinalIgnoreCase));
            if (reaction != null) return reaction;
        }

        if (ContainsAny(normalizedPrompt, "camera", "scene", "message", "animation"))
        {
            var scene = functions.FirstOrDefault(f => f.Role.Contains("camera", StringComparison.OrdinalIgnoreCase)
                                                   || f.Role.Contains("visuelle", StringComparison.OrdinalIgnoreCase));
            if (scene != null) return scene;
        }

        if (AtelKnowledgeBase.IsPhasePrompt(normalizedPrompt))
        {
            var phase = functions.FirstOrDefault(f =>
                ContainsAny(
                    string.Join("\n", f.Instructions.Select(i => i.Annotation ?? i.Mnemonic)),
                    ".HP",
                    "HP [00h]",
                    "OverdriveCurrent",
                    "setCommandDisabled(",
                    "addCommand(",
                    "removeCommand(",
                    "runBtlScene(",
                    "displayBattleMessage",
                    "btlSetModelHide",
                    "changeActorName"));
            if (phase != null) return phase;
        }

        return functions.FirstOrDefault(f => f.Role.Contains("selection", StringComparison.OrdinalIgnoreCase))
               ?? functions.FirstOrDefault(f => f.Role != "vide / fin immediate");
    }

    private static IEnumerable<AtelCommandCatalogEntry> FindCommandCandidates(AtelCopilotContext context, string prompt, string normalizedPrompt)
    {
        var explicitIds = CommandIdRegex()
            .Matches(prompt)
            .Select(m => int.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) ? id : -1)
            .Where(id => id >= 0)
            .ToList();

        foreach (var id in explicitIds)
        {
            var match = context.CommandCatalog.FirstOrDefault(c => c.Id == id);
            yield return match ?? new AtelCommandCatalogEntry(id, context.AnalysisOptions.ResolveCommandSource?.Invoke(id) ?? "?", context.AnalysisOptions.ResolveCommandName?.Invoke(id) ?? "(nom non resolu)");
        }

        var words = SplitSearchWords(normalizedPrompt).ToList();
        if (words.Count == 0)
            yield break;

        foreach (var command in context.CommandCatalog)
        {
            var normalizedName = Normalize(command.Name);
            var score = words.Count(w => normalizedName.Contains(w, StringComparison.OrdinalIgnoreCase));
            if (score <= 0)
                continue;
            yield return command;
        }
    }

    private static IEnumerable<string> SplitSearchWords(string normalizedPrompt)
    {
        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ajoute", "ajouter", "modifier", "modifie", "plan", "pour", "avec", "quand", "une", "des", "les", "dans",
            "monstre", "attaque", "sort", "commande", "faire", "fait", "fois", "si", "il", "elle", "self", "cible"
        };

        foreach (Match match in WordRegex().Matches(normalizedPrompt))
        {
            var word = match.Value;
            if (word.Length < 3 || stop.Contains(word))
                continue;
            yield return word;
        }
    }

    private static IReadOnlyList<AtelCopilotFunction> BuildFunctionAnalyses(AtelDecompiledScript script)
    {
        var functionOffsets = new HashSet<int>(script.Workers.SelectMany(w => w.Functions));
        var result = new List<AtelCopilotFunction>();
        AtelCopilotFunction? current = null;

        foreach (var instruction in script.Instructions)
        {
            if (script.LabelsByOffset.TryGetValue(instruction.Offset, out var labels))
            {
                var functionLabel = labels.FirstOrDefault(l => l.Contains("::f", StringComparison.Ordinal));
                if (functionLabel != null && functionOffsets.Contains(instruction.Offset))
                {
                    current = new AtelCopilotFunction(functionLabel, instruction.Offset);
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
                    .Select(FormatInstructionLine)
                    .Where(IsUsefulHighlight)
                    .Distinct(StringComparer.Ordinal)
                    .Take(8));
        }

        return result;
    }

    private static string GuessFunctionRole(IReadOnlyList<AtelInstruction> instructions)
    {
        var text = string.Join("\n", instructions.Select(i => i.Annotation ?? i.Mnemonic));
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
        if (ContainsAny(text, "currentBattle(", "cam", "refSet", "refMove", "displayBattleMessage"))
            return "camera, scene ou message de combat";
        if (ContainsAny(text, "motion.", "btlSetScale", "btlSetBindEffect", "DeathAnimation", "setHeight("))
            return "initialisation visuelle, motion ou effet";
        if (ContainsAny(text, "setAmbushState", "setBattleFlag", "clearOwnCommands", "addCommandToSelf"))
            return "initialisation des regles du combat";
        return "logique ATEL generale";
    }

    private static IEnumerable<string> InterestingInstructions(AtelDecompiledScript script, Func<string, bool> predicate)
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
            "findMatchingChr",
            "willDieToAttack");

    private static bool IsVisualAnnotation(string text)
        => ContainsAny(text,
            "cam",
            "motion.",
            "Motion",
            "refSet",
            "refMove",
            "btlSetScale",
            "btlSetBindEffect",
            "btlSound",
            "Voice",
            "displayBattle",
            "DeathAnimation");

    private static bool IsPhaseAnnotation(string text)
        => ContainsAny(text,
            ".HP",
            "HP [00h]",
            "OverdriveCurrent",
            "OverdriveMax",
            "currentBattle(",
            "setCommandDisabled(",
            "addCommand(",
            "addCommandToSelf(",
            "removeCommand(",
            "clearOwnCommands(",
            "runBtlScene(",
            "displayBattleMessage",
            "setBattleFlag",
            "btlSetModelHide",
            "changeActorName");

    private static bool IsStrongPhaseAnnotation(string text)
        => ContainsAny(text,
            ".HP",
            "HP [00h]",
            "OverdriveCurrent",
            "OverdriveMax",
            "setCommandDisabled(",
            "addCommand(",
            "addCommandToSelf(",
            "removeCommand(",
            "clearOwnCommands(",
            "runBtlScene(",
            "displayBattleMessage",
            "setBattleFlag",
            "btlSetModelHide",
            "changeActorName");

    private static bool IsUsefulHighlight(string line)
        => !ContainsAny(line, "push ", "duplicate ", "return", "direct return", "no operation")
           || ContainsAny(line, "if ", "performCommand", "forcePerformCommand", "addCommand", "removeCommand", "setCommandDisabled", "motion.", "cam", "LastDamageTakenHP", "usedCommand", ".HP", "HP [00h]", "OverdriveCurrent", "displayBattleMessage", "runBtlScene");

    private static void AppendTechnicalActorNote(StringBuilder sb, AtelDecompiledScript script)
    {
        var text = string.Join("\n", script.Instructions.Select(i => i.Annotation ?? ""));
        var disabledFlags = CountNeedles(text, "MustBeKilledForBattleEnd", "VisibleOnCTB", "GetsTurns", "Targetable");
        var hasActions = script.Instructions.Any(i => i.Annotation != null && IsActionAnnotation(i.Annotation));

        if (disabledFlags >= 3 && !hasActions && script.Instructions.Count <= 80)
        {
            sb.AppendLine("- Ce script ressemble a un acteur technique/dummy : il se rend silencieux ou non interactif au lieu de porter une IA active.");
        }
        else
        {
            sb.AppendLine("- Je ne vois pas le profil typique d'un dummy totalement neutralise.");
        }
    }

    private static void AppendWarnings(StringBuilder sb, AtelDecompiledScript script)
    {
        if (script.Warnings.Count == 0)
            return;
        sb.AppendLine();
        sb.AppendLine("Avertissements :");
        foreach (var warning in script.Warnings.Take(8))
            sb.AppendLine("- " + warning);
        if (script.Warnings.Count > 8)
            sb.AppendLine($"- ... {script.Warnings.Count - 8} avertissement(s) supplementaire(s).");
    }

    private static string EnrichCommandLine(string line, AtelCopilotContext context)
    {
        var notes = CommandIdRegex()
            .Matches(line)
            .Select(m => int.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id)
                ? FormatCommandReference(id, context)
                : null)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return notes.Count == 0 ? line : $"{line}  =>  {string.Join(", ", notes)}";
    }

    private static string FormatCommandReference(int id, AtelCopilotContext context)
    {
        var catalog = context.CommandCatalog.FirstOrDefault(c => c.Id == id);
        if (catalog != null)
            return $"0x{id:X4} {catalog.Source} - {catalog.Name}";

        var source = context.AnalysisOptions.ResolveCommandSource?.Invoke(id);
        var name = context.AnalysisOptions.ResolveCommandName?.Invoke(id);
        return $"0x{id:X4}" +
               (!string.IsNullOrWhiteSpace(source) ? $" {source}" : "") +
               (!string.IsNullOrWhiteSpace(name) ? $" - {name}" : "");
    }

    private static string FormatInstructionLine(AtelInstruction instruction)
        => $"0x{instruction.Offset:X4}: {(string.IsNullOrWhiteSpace(instruction.Annotation) ? instruction.Mnemonic : instruction.Annotation)}";

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static int CountNeedles(string value, params string[] needles)
        => needles.Count(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? "(aucun)" : value;

    private static string Normalize(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private sealed class AtelCopilotFunction
    {
        public AtelCopilotFunction(string label, int offset)
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

    [GeneratedRegex(@"w[0-9A-Fa-f]{2}::f[0-9A-Fa-f]{2}")]
    private static partial Regex FunctionLabelRegex();

    [GeneratedRegex(@"w[0-9A-Fa-f]{2}")]
    private static partial Regex WorkerLabelRegex();

    [GeneratedRegex("0x([2364][0-9A-Fa-f]{3})")]
    private static partial Regex CommandIdRegex();

    [GeneratedRegex("[a-zA-Z0-9_\\-]+")]
    private static partial Regex WordRegex();
}
