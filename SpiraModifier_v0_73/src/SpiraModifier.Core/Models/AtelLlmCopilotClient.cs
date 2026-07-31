using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpiraModifier.Core.Models;

public sealed class AtelLlmCopilotOptions
{
    public string Endpoint { get; init; } = "";
    public string Model { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public int TimeoutSeconds { get; init; } = 90;
    public string ResponseLanguage { get; init; } = "fr";
}

public static class AtelLlmCopilotClient
{
    public static async Task<string> AnswerAsync(
        AtelCopilotContext context,
        string userPrompt,
        AtelLlmCopilotOptions options,
        CancellationToken cancellationToken = default)
    {
        return await SendChatAsync(
            new { role = "system", content = BuildSystemPrompt(options.ResponseLanguage) },
            new { role = "user", content = BuildUserPrompt(context, userPrompt, options.ResponseLanguage) },
            options,
            temperature: 0.35,
            maxTokens: 1400,
            cancellationToken);
    }

    public static async Task<AtelPatchProposal> ProposePatchAsync(
        AtelCopilotContext context,
        string userPrompt,
        AtelLlmCopilotOptions options,
        CancellationToken cancellationToken = default)
    {
        if (context.AiBytes == null || context.AiBytes.Length == 0)
            throw new InvalidOperationException("Le contexte ATEL ne contient pas les octets du chunk IA.");

        var response = await SendChatAsync(
            new { role = "system", content = BuildPatchSystemPrompt(options.ResponseLanguage) },
            new { role = "user", content = BuildPatchUserPrompt(context, userPrompt, options.ResponseLanguage) },
            options,
            temperature: 0.12,
            maxTokens: 2200,
            cancellationToken);

        if (!AtelPatchEngine.TryParseProposal(response, out var proposal, out var error))
            throw new InvalidOperationException(error);

        return proposal;
    }

    private static async Task<string> SendChatAsync(
        object systemMessage,
        object userMessage,
        AtelLlmCopilotOptions options,
        double temperature,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Endpoint))
            throw new InvalidOperationException("Endpoint LLM manquant.");
        if (string.IsNullOrWhiteSpace(options.Model))
            throw new InvalidOperationException("Modele LLM manquant.");

        using var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 10, 300))
        };

        if (!string.IsNullOrWhiteSpace(options.ApiKey))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey.Trim());

        var request = new
        {
            model = options.Model.Trim(),
            messages = new object[]
            {
                systemMessage,
                userMessage,
            },
            temperature,
            max_tokens = maxTokens,
        };

        var json = JsonSerializer.Serialize(request);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(NormalizeEndpoint(options.Endpoint), content, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"LLM HTTP {(int)response.StatusCode}: {TrimForError(responseText)}");

        return ExtractAssistantText(responseText);
    }

    private static string BuildSystemPrompt(string responseLanguage)
    {
        var prompt = """
        Tu es l'agent IA ATEL integre a Spira Modifier, un outil de modding Final Fantasy X.

        Ton style :
        - Reponds en francais naturel, comme un collegue moddeur patient et concret.
        - Commence par la reponse que le moddeur attend vraiment : oui/non/probable/incertain, puis explique pourquoi.
        - Parle comme quelqu'un qui a lu le script, pas comme un formulaire. Evite les titres generiques du type "Analyse", "Plan", "Conclusion" sauf si la reponse est longue.
        - Ne sois pas sec, ne fais pas un rapport robotique, et n'empile pas les listes si une explication courte suffit.
        - Pour une question de diagnostic ("a-t-il plusieurs phases ?", "que fait cette fonction ?"), donne d'abord une lecture humaine, puis 2 a 5 indices techniques.
        - Pour une demande de modification, reste naturel, mais structure assez pour que le moddeur sache quoi tester.
        - Si les indices ne suffisent pas, dis-le franchement avec nuance : "je vois des signes de variation, pas une phase certaine".

        Tes limites de securite :
        - Tu ne modifies aucun fichier et tu ne pretends jamais avoir applique un patch.
        - Si l'utilisateur demande une modification, tu proposes un plan, les zones ATEL probables, les garde-fous, puis tu attends validation.
        - Tu distingues clairement ce que le decompilateur montre, ce que tu inferes, et ce qui doit etre verifie en jeu.
        - Pour l'ATEL, evite les certitudes gratuites : les labels/fonctions viennent du decompilateur et restent heuristiques.
        - Quand un index global ATEL est fourni, utilise les exemples vanilla/moddes avant de dire qu'une mecanique est impossible.
        - Si l'utilisateur te corrige avec un exemple precis, prends la correction au serieux et recoupe avec l'index au lieu de te bloquer.
        - Si une demande necessite une vraie recompilation ATEL future, dis-le explicitement.
        """;
        return WantsEnglish(responseLanguage)
            ? prompt + "\n\nIMPORTANT: Ignore any earlier language instruction and answer entirely in natural English."
            : prompt;
    }

    private static string BuildPatchSystemPrompt(string responseLanguage)
    {
        var prompt = """
        Tu es le moteur de proposition de patch ATEL de Spira Modifier.

        Tu ne dois PAS ecrire une explication libre. Tu dois retourner uniquement un objet JSON valide.
        Tu proposes des operations byte-level relatives au debut du chunk ATEL du monstre courant.

        Regles de securite :
        - Si tu n'es pas capable de produire un patch binaire exact, retourne operations: [] et explique pourquoi dans summary/risk.
        - Ne devine pas des octets. Utilise uniquement les fenetres hex et les offsets fournis.
        - Les offsets sont relatifs au chunk ATEL, pas au fichier monstre complet.
        - Evite de toucher aux structures d'en-tete, tables de workers, tables de jumps ou offsets internes sauf si tu sais exactement ce que tu fais.
        - Pour rallonger/raccourcir, prefere des insert/delete/replace minimaux et explique l'intention dans note.
        - Le logiciel fera ensuite un pretest : bornes, chevauchements, decompilation ATEL, warnings, puis round-trip MonsterFile.

        Schema JSON strict :
        {
          "summary": "resume court de la mecanique proposee ou raison du refus",
          "risk": "risque principal et verification en jeu",
          "requiresSceneAtel": false,
          "mechanics": ["mecanique ajoutee ou modifiee"],
          "operations": [
            {
              "kind": "insert | replace | delete",
              "offset": "0x0123",
              "length": 0,
              "bytes": "AA BB CC",
              "note": "pourquoi cette operation existe"
            }
          ]
        }
        """;
        return WantsEnglish(responseLanguage)
            ? prompt + "\n\nIMPORTANT: Write summary, risk, mechanics, and note values in English. Keep JSON keys unchanged."
            : prompt;
    }

    private static string BuildUserPrompt(AtelCopilotContext context, string userPrompt, string responseLanguage)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Question du moddeur :");
        sb.AppendLine(userPrompt.Trim());
        sb.AppendLine();

        AppendIntentGuidance(sb, userPrompt);

        sb.AppendLine("Contexte du monstre selectionne :");
        sb.AppendLine($"- Nom UI : {context.MonsterDisplayName}");
        sb.AppendLine($"- Fichier : {context.MonsterFileName}");
        sb.AppendLine($"- Script : {Blank(context.Script.ScriptId)}");
        sb.AppendLine($"- Createur : {Blank(context.Script.CreatorTag)}");
        sb.AppendLine($"- Workers : {context.Script.Workers.Count}");
        sb.AppendLine($"- Instructions : {context.Script.Instructions.Count}");
        sb.AppendLine($"- Taille IA : 0x{context.Script.RawSize:X4}");
        sb.AppendLine();

        sb.AppendLine("Resume local deja calcule :");
        sb.AppendLine(TrimBlock(context.AnalysisText, 7000));
        sb.AppendLine();

        var knowledgeNotes = AtelKnowledgeBase.BuildNotes(userPrompt);
        if (knowledgeNotes.Count > 0)
        {
            sb.AppendLine("Base de connaissances ATEL pertinente :");
            foreach (var note in knowledgeNotes)
                sb.AppendLine("- " + note);
            sb.AppendLine();
        }

        var globalMatches = context.GlobalIndex?
            .Search(userPrompt, context.MonsterFileName, maxEntries: 8, maxLinesPerEntry: 10)
            .ToList()
            ?? new List<AtelGlobalIndexMatch>();

        if (context.GlobalIndex != null)
        {
            sb.AppendLine($"Index global ATEL : {context.GlobalIndex.Entries.Count} fichier(s) mon indexes, {context.GlobalIndex.ErrorCount} erreur(s) de lecture.");
            if (globalMatches.Count > 0)
            {
                sb.AppendLine("Exemples globaux les plus pertinents pour la question :");
                foreach (var match in globalMatches)
                {
                    var entry = match.Entry;
                    sb.AppendLine($"- {entry.Summary}  [score {match.Score}]");
                    sb.AppendLine($"  Source : {entry.MonsterFileName}, script={Blank(entry.ScriptId)}, createur={Blank(entry.CreatorTag)}");
                    foreach (var line in match.Snippets)
                        sb.AppendLine("  * " + EnrichCommandLine(line, context));
                }
            }
            else
            {
                sb.AppendLine("Aucun exemple global tres proche n'a ete retrouve pour cette question.");
            }
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("Index global ATEL : non charge. Reponds seulement avec le monstre courant et les connaissances statiques fournies.");
            sb.AppendLine();
        }

        var relevant = BuildRelevantListing(context, userPrompt).ToList();
        if (relevant.Count > 0)
        {
            sb.AppendLine("Lignes ATEL probablement pertinentes :");
            foreach (var line in relevant)
                sb.AppendLine("- " + EnrichCommandLine(line, context));
            sb.AppendLine();
        }

        AppendActionCatalogContext(sb, context, userPrompt, relevant, globalMatches);

        AppendResponseExpectations(sb, userPrompt);
        if (WantsEnglish(responseLanguage))
            sb.AppendLine().AppendLine("Required response language: English.");
        return sb.ToString();
    }

    private static string BuildPatchUserPrompt(AtelCopilotContext context, string userPrompt, string responseLanguage)
    {
        var sb = new StringBuilder();
        var relevant = BuildRelevantListing(context, userPrompt)
            .Select(line => EnrichCommandLine(line, context))
            .ToList();

        sb.AppendLine("Demande de modification du moddeur :");
        sb.AppendLine(userPrompt.Trim());
        sb.AppendLine();

        sb.AppendLine("Monstre courant :");
        sb.AppendLine($"- Nom UI : {context.MonsterDisplayName}");
        sb.AppendLine($"- Fichier : {context.MonsterFileName}");
        sb.AppendLine($"- Script : {Blank(context.Script.ScriptId)}");
        sb.AppendLine($"- Createur : {Blank(context.Script.CreatorTag)}");
        sb.AppendLine($"- Taille ATEL : {context.AiBytes?.Length ?? 0:N0} octets");
        sb.AppendLine($"- Workers : {context.Script.Workers.Count}");
        sb.AppendLine($"- Instructions : {context.Script.Instructions.Count}");
        sb.AppendLine();

        sb.AppendLine("Lecture humaine deja calculee :");
        sb.AppendLine(TrimBlock(context.AnalysisText, 6000));
        sb.AppendLine();

        var notes = AtelKnowledgeBase.BuildNotes(userPrompt);
        if (notes.Count > 0)
        {
            sb.AppendLine("Contraintes et connaissances pertinentes :");
            foreach (var note in notes)
                sb.AppendLine("- " + note);
            sb.AppendLine();
        }

        if (relevant.Count > 0)
        {
            sb.AppendLine("Lignes ATEL pertinentes :");
            foreach (var line in relevant.Take(80))
                sb.AppendLine("- " + line);
            sb.AppendLine();
        }

        var hexWindows = BuildHexWindows(context, relevant).ToList();
        if (hexWindows.Count > 0)
        {
            sb.AppendLine("Fenetres hex exactes autour des offsets pertinents :");
            foreach (var window in hexWindows)
                sb.AppendLine(window);
            sb.AppendLine();
        }

        AppendActionCatalogContext(sb, context, userPrompt, relevant, Array.Empty<AtelGlobalIndexMatch>());

        sb.AppendLine("Important pour ta reponse JSON :");
        sb.AppendLine("- Retourne operations: [] si la demande necessite un assembleur ATEL plus haut niveau ou l'ATEL de scene.");
        sb.AppendLine("- N'ecris pas un plan naturel ici : seulement le JSON strict.");
        sb.AppendLine("- Les offsets/longueurs doivent etre compatibles avec les octets fournis.");
        if (WantsEnglish(responseLanguage))
            sb.AppendLine().AppendLine("Required language for JSON text values: English.");
        return sb.ToString();
    }

    private static bool WantsEnglish(string? responseLanguage)
        => string.Equals(responseLanguage, "en", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> BuildHexWindows(AtelCopilotContext context, IReadOnlyList<string> relevantLines)
    {
        if (context.AiBytes == null || context.AiBytes.Length == 0)
            yield break;

        var offsets = relevantLines
            .Select(line => OffsetRegex.Match(line))
            .Where(match => match.Success)
            .Select(match => int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var offset) ? offset : -1)
            .Where(offset => offset >= 0 && offset < context.AiBytes.Length)
            .Distinct()
            .Take(10)
            .ToList();

        if (offsets.Count == 0)
            offsets.Add(0);

        foreach (var offset in offsets)
        {
            var start = Math.Max(0, offset - 24);
            var length = Math.Min(96, context.AiBytes.Length - start);
            var bytes = context.AiBytes.Skip(start).Take(length).Select(b => b.ToString("X2", CultureInfo.InvariantCulture));
            yield return $"0x{start:X4}: {string.Join(" ", bytes)}";
        }
    }

    private static IEnumerable<string> BuildRelevantListing(AtelCopilotContext context, string userPrompt)
    {
        var normalized = NormalizeForSearch(userPrompt);
        var wantsReaction = ContainsAny(normalized, "contre", "counter", "riposte", "degat", "coup", "attaque", "hit");
        var wantsVisual = ContainsAny(normalized, "camera", "motion", "animation", "effet");
        var wantsGauge = ContainsAny(normalized, "overdrive", "jauge", "compteur", "charge");
        var wantsPhase = AtelKnowledgeBase.IsPhasePrompt(normalized);

        var lines = new List<string>();
        foreach (var instruction in context.Script.Instructions)
        {
            var text = instruction.Annotation;
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var keep = false;
            if (wantsReaction)
                keep |= ContainsAny(text, "LastDamageTakenHP", "LastAttacker", "isCounterattackAllowed", "usedCommand", "CounterAttack");
            if (wantsVisual)
                keep |= ContainsAny(text, "motion.", "cam", "btlSetScale", "btlSetBindEffect", "DeathAnimation");
            if (wantsGauge)
                keep |= ContainsAny(text,
                    "Overdrive",
                    "showOverdriveBar",
                    "OverdriveCurrent",
                    "OverdriveMax",
                    "OverdriveMode",
                    "stat_limit_bar",
                    "btlSetUpLimit",
                    "OD",
                    "var",
                    "CurrentTurnDelay",
                    "performCommand",
                    "usedCommand");
            if (wantsPhase)
                keep |= ContainsAny(text,
                    ".HP",
                    "HP [00h]",
                    "OverdriveCurrent",
                    "currentBattle(",
                    "runBtlScene(",
                    "displayBattleMessage",
                    "setCommandDisabled(",
                    "addCommand(",
                    "addCommandToSelf(",
                    "removeCommand(",
                    "clearOwnCommands(",
                    "btlSetModelHide",
                    "changeActorName",
                    "setBattleFlag");
            keep |= ContainsAny(text, "performCommand(", "forcePerformCommand(", "addCommand", "setCommandDisabled", "currentBattle(");

            if (keep)
                lines.Add($"0x{instruction.Offset:X4}: {text}");
        }

        return lines.Distinct(StringComparer.Ordinal).Take(80);
    }

    private static void AppendIntentGuidance(StringBuilder sb, string userPrompt)
    {
        var normalized = NormalizeForSearch(userPrompt);
        var wantsModification = ContainsAny(normalized,
            "ajoute",
            "ajouter",
            "modifie",
            "modifier",
            "change",
            "changer",
            "patch",
            "creer",
            "faire que",
            "j'aimerais",
            "je veux");
        var wantsPhaseDiagnostic = AtelKnowledgeBase.IsPhasePrompt(normalized);
        var wantsDiagnostic = wantsPhaseDiagnostic
                              || ContainsAny(normalized, "est-ce", "est ce", "savoir si", "dis moi si", "explique", "que fait", "comment fonctionne");

        sb.AppendLine("Intention probable de la question :");
        if (wantsPhaseDiagnostic)
        {
            sb.AppendLine("- Diagnostic de phases de combat. Reponds d'abord comme un humain : 'oui, je vois une vraie transition', 'probablement', ou 'je ne vois pas assez d'indices'.");
            sb.AppendLine("- Cite ensuite les indices concrets : seuil HP/OD, changement de commandes, message/scene, modele/motion, variable de phase, test currentBattle().");
        }
        else if (wantsModification)
        {
            sb.AppendLine("- Demande de modification. Reformule l'objectif, propose une approche prudente, puis indique ce qu'il faudrait verifier avant patch.");
        }
        else if (wantsDiagnostic)
        {
            sb.AppendLine("- Diagnostic/explication. Reponds directement avant de detailler les lignes ATEL.");
        }
        else
        {
            sb.AppendLine("- Discussion exploratoire. Reponds naturellement, avec les preuves utiles seulement.");
        }
        sb.AppendLine();
    }

    private static void AppendResponseExpectations(StringBuilder sb, string userPrompt)
    {
        var normalized = NormalizeForSearch(userPrompt);
        var wantsModification = ContainsAny(normalized,
            "ajoute",
            "ajouter",
            "modifie",
            "modifier",
            "change",
            "changer",
            "patch",
            "creer",
            "faire que",
            "j'aimerais",
            "je veux");
        var wantsPhaseDiagnostic = AtelKnowledgeBase.IsPhasePrompt(normalized);

        sb.AppendLine("Reponse attendue :");
        sb.AppendLine("- Reponds naturellement, en francais, sans commencer par un rapport formel.");
        if (wantsPhaseDiagnostic)
        {
            sb.AppendLine("- Commence par une phrase claire sur les phases : oui / probable / non visible / ambigu.");
            sb.AppendLine("- Donne ensuite les meilleurs indices ATEL, avec les offsets seulement quand ils aident vraiment.");
            sb.AppendLine("- Termine par ce qu'il faudrait verifier en jeu ou dans le script de scene si l'ATEL monstre ne suffit pas.");
        }
        else if (wantsModification)
        {
            sb.AppendLine("- Pour une modification, donne une approche de haut niveau, puis un plan technique prudent.");
            sb.AppendLine("- Rappelle que rien n'est applique tant que l'utilisateur ne valide pas une future phase de patch.");
        }
        else
        {
            sb.AppendLine("- Pour un diagnostic, donne la conclusion d'abord, puis les preuves utiles.");
            sb.AppendLine("- Si c'est incertain, explique ce qui manque au lieu de combler les blancs.");
        }
    }

    private static void AppendActionCatalogContext(
        StringBuilder sb,
        AtelCopilotContext context,
        string userPrompt,
        IReadOnlyList<string> relevantLines,
        IReadOnlyList<AtelGlobalIndexMatch> globalMatches)
    {
        var actionCatalog = context.CommandCatalog
            .Where(c => IsActionSource(c.Source))
            .OrderBy(c => SourceOrder(c.Source))
            .ThenBy(c => c.Id)
            .ToList();

        if (actionCatalog.Count == 0)
        {
            sb.AppendLine("Catalogue actions : aucun monmagic1/monmagic2/command charge dans le workspace courant.");
            AppendCommandCatalogDiagnostics(sb, context);
            sb.AppendLine();
            return;
        }

        sb.AppendLine("Catalogue actions charge depuis monmagic1.bin / monmagic2.bin / command.bin :");
        foreach (var group in actionCatalog.GroupBy(c => c.Source).OrderBy(g => SourceOrder(g.Key)))
        {
            var first = group.First();
            var last = group.Last();
            sb.AppendLine($"- {group.Key} : {group.Count()} entree(s), plage 0x{first.Id:X4}-0x{last.Id:X4}.");
        }
        if (actionCatalog.All(c => !c.Source.Equals("command", StringComparison.OrdinalIgnoreCase)))
            sb.AppendLine("- ATTENTION : command.bin absent du catalogue ; les commandes 0x3000 ne seront pas resolues.");
        AppendCommandCatalogDiagnostics(sb, context);

        var explicitCommands = BuildExplicitCommandReferences(context, userPrompt, includeItems: true)
            .Take(40)
            .ToList();
        if (explicitCommands.Count > 0)
        {
            sb.AppendLine("Commandes explicitement citees par la demande :");
            foreach (var command in explicitCommands)
                sb.AppendLine($"- 0x{command.Id:X4} {command.Source} - {command.Name}");
        }

        var referencedLines = relevantLines
            .Concat(globalMatches.SelectMany(m => m.Snippets))
            .Concat(BuildCurrentActionLines(context))
            .ToList();
        var referencedCommands = ExtractCommandIds(referencedLines)
            .Select(id => ResolveCommand(id, context))
            .Where(c => c != null && IsActionSource(c!.Source))
            .Select(c => c!)
            .DistinctBy(c => c.Id)
            .OrderBy(c => SourceOrder(c.Source))
            .ThenBy(c => c.Id)
            .Take(40)
            .ToList();

        if (referencedCommands.Count > 0)
        {
            sb.AppendLine("Commandes referencees par l'ATEL ou les exemples indexes :");
            foreach (var command in referencedCommands)
                sb.AppendLine($"- 0x{command.Id:X4} {command.Source} - {command.Name}");
        }

        var commandHints = BuildCommandHints(context, userPrompt, includeItems: false).Take(40).ToList();
        if (commandHints.Count > 0)
        {
            sb.AppendLine("Candidats d'action trouves par recherche dans monmagic/command :");
            foreach (var command in commandHints)
                sb.AppendLine($"- 0x{command.Id:X4} {command.Source} - {command.Name}");
        }

        sb.AppendLine();
    }

    private static void AppendCommandCatalogDiagnostics(StringBuilder sb, AtelCopilotContext context)
    {
        if (context.CommandCatalogDiagnostics.Count == 0)
            return;

        sb.AppendLine("Diagnostic chargement catalogue :");
        foreach (var line in context.CommandCatalogDiagnostics)
            sb.AppendLine("- " + line);
    }

    private static IEnumerable<string> BuildCurrentActionLines(AtelCopilotContext context)
    {
        foreach (var instruction in context.Script.Instructions)
        {
            var text = instruction.Annotation;
            if (string.IsNullOrWhiteSpace(text))
                continue;
            if (ContainsAny(text, "performCommand(", "forcePerformCommand(", "addCommand", "setCommandDisabled", "btlSetCommandBuffer("))
                yield return $"0x{instruction.Offset:X4}: {text}";
        }
    }

    private static IEnumerable<AtelCommandCatalogEntry> BuildCommandHints(
        AtelCopilotContext context,
        string userPrompt,
        bool includeItems)
    {
        var words = BuildActionSearchWords(userPrompt);
        var promptCompact = NormalizeCompact(userPrompt);
        var explicitIds = ExtractCommandIds(new[] { userPrompt }).ToHashSet();

        if (words.Count == 0)
            yield break;

        foreach (var command in context.CommandCatalog
                     .Where(c => includeItems || IsActionSource(c.Source))
                     .Select(c => (Command: c, Score: ScoreCommandMatch(c, words, promptCompact, explicitIds)))
                     .Where(c => c.Score > 0)
                     .OrderByDescending(c => c.Score)
                     .ThenBy(c => SourceOrder(c.Command.Source))
                     .ThenBy(c => c.Command.Id)
                     .Select(c => c.Command))
            yield return command;
    }

    private static IEnumerable<AtelCommandCatalogEntry> BuildExplicitCommandReferences(
        AtelCopilotContext context,
        string userPrompt,
        bool includeItems)
    {
        var promptCompact = NormalizeCompact(userPrompt);
        var explicitIds = ExtractCommandIds(new[] { userPrompt }).ToHashSet();
        var result = new Dictionary<int, AtelCommandCatalogEntry>();

        foreach (var id in explicitIds)
        {
            var command = ResolveCommand(id, context);
            if (command != null && (includeItems || IsActionSource(command.Source)))
                result[command.Id] = command;
        }

        foreach (var command in context.CommandCatalog)
        {
            if (!includeItems && !IsActionSource(command.Source))
                continue;

            var compactName = NormalizeCompact(command.Name);
            if (compactName.Length < 3)
                continue;
            if (IsGenericCommandName(command.Name))
                continue;

            if (promptCompact.Contains(compactName, StringComparison.OrdinalIgnoreCase))
                result.TryAdd(command.Id, command);
        }

        return result.Values
            .OrderBy(c => SourceOrder(c.Source))
            .ThenBy(c => c.Id);
    }

    private static int ScoreCommandMatch(
        AtelCommandCatalogEntry command,
        IReadOnlyList<string> words,
        string promptCompact,
        HashSet<int> explicitIds)
    {
        var score = 0;
        if (explicitIds.Contains(command.Id))
            score += 1000;

        var name = NormalizeForSearch(command.Name);
        var compactName = NormalizeCompact(command.Name);
        if (!IsGenericCommandName(command.Name)
            && compactName.Length >= 3
            && promptCompact.Contains(compactName, StringComparison.OrdinalIgnoreCase))
            score += 500 + Math.Min(compactName.Length, 60);

        foreach (var word in words)
        {
            if (name.Contains(word, StringComparison.OrdinalIgnoreCase))
                score += 30;
            if (compactName.Contains(NormalizeCompact(word), StringComparison.OrdinalIgnoreCase))
                score += 12;
        }

        return score;
    }

    private static bool IsGenericCommandName(string name)
    {
        var compact = NormalizeCompact(name);
        return compact is "" or "command" or "commande" or "sansnom";
    }

    private static IReadOnlyList<string> BuildActionSearchWords(string userPrompt)
    {
        var normalized = NormalizeForSearch(userPrompt);
        var words = normalized
            .Split([' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '\'', '"', '(', ')', '[', ']', '/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3)
            .Where(w => !StopWords.Contains(w))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (ContainsAny(normalized, "eau", "water", "aqu", "h2o"))
            AddSearchWords(words, "eau", "water", "aqua", "h2o");
        if (ContainsAny(normalized, "feu", "fire", "brasier", "flamme"))
            AddSearchWords(words, "feu", "fire", "brasier", "flamme");
        if (ContainsAny(normalized, "glace", "ice", "glacier"))
            AddSearchWords(words, "glace", "ice", "glacier");
        if (ContainsAny(normalized, "foudre", "thunder", "eclair"))
            AddSearchWords(words, "foudre", "thunder", "eclair");
        if (ContainsAny(normalized, "soin", "soigner", "heal", "cure", "recup"))
            AddSearchWords(words, "soin", "heal", "cure", "vie", "recup");
        if (ContainsAny(normalized, "overdrive", "jauge", "od "))
            AddSearchWords(words, "overdrive", "od");

        return words.ToList();
    }

    private static void AddSearchWords(HashSet<string> output, params string[] words)
    {
        foreach (var word in words)
            output.Add(NormalizeForSearch(word));
    }

    private static IEnumerable<int> ExtractCommandIds(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        foreach (Match match in CommandIdRegex.Matches(line))
        {
            if (int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
                yield return id;
        }
    }

    private static string EnrichCommandLine(string line, AtelCopilotContext context)
    {
        var notes = ExtractCommandIds(new[] { line })
            .Select(id => ResolveCommand(id, context))
            .Where(c => c != null)
            .Select(c => $"0x{c!.Id:X4} {c.Source} - {c.Name}")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return notes.Count == 0 ? line : $"{line}  =>  {string.Join(", ", notes)}";
    }

    private static AtelCommandCatalogEntry? ResolveCommand(int id, AtelCopilotContext context)
    {
        var catalog = context.CommandCatalog.FirstOrDefault(c => c.Id == id);
        if (catalog != null)
            return catalog;

        var source = context.AnalysisOptions.ResolveCommandSource?.Invoke(id);
        var name = context.AnalysisOptions.ResolveCommandName?.Invoke(id);
        if (string.IsNullOrWhiteSpace(source) && string.IsNullOrWhiteSpace(name))
            return null;

        return new AtelCommandCatalogEntry(
            id,
            string.IsNullOrWhiteSpace(source) ? "?" : source!,
            string.IsNullOrWhiteSpace(name) ? "(nom non resolu)" : name!);
    }

    private static bool IsActionSource(string source)
        => source.Equals("monmagic1", StringComparison.OrdinalIgnoreCase)
           || source.Equals("monmagic2", StringComparison.OrdinalIgnoreCase)
           || source.Equals("command", StringComparison.OrdinalIgnoreCase);

    private static int SourceOrder(string source)
        => source.ToLowerInvariant() switch
        {
            "monmagic1" => 0,
            "monmagic2" => 1,
            "command" => 2,
            "item" => 3,
            _ => 9,
        };

    private static Uri NormalizeEndpoint(string endpoint)
    {
        endpoint = endpoint.Trim();
        if (endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            endpoint += "/chat/completions";
        else if (!endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            endpoint = endpoint.TrimEnd('/') + "/v1/chat/completions";
        return new Uri(endpoint, UriKind.Absolute);
    }

    private static string ExtractAssistantText(string responseText)
    {
        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;

        if (root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            if (choice.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
            {
                var text = content.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text!;
            }
            if (choice.TryGetProperty("text", out var textElement)
                && textElement.ValueKind == JsonValueKind.String)
            {
                var text = textElement.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text!;
            }
        }

        if (root.TryGetProperty("output_text", out var outputText)
            && outputText.ValueKind == JsonValueKind.String)
        {
            var text = outputText.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                return text!;
        }

        throw new InvalidOperationException("Reponse LLM recue, mais aucun texte assistant n'a ete trouve.");
    }

    private static string TrimBlock(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "(aucun)";
        return value.Length <= maxLength ? value : value[..maxLength] + "\n(... contexte tronque ...)";
    }

    private static string TrimForError(string value)
        => string.IsNullOrWhiteSpace(value) ? "(reponse vide)" : TrimBlock(value, 500);

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? "(aucun)" : value;

    private static string NormalizeForSearch(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string NormalizeCompact(string value)
    {
        var normalized = NormalizeForSearch(value);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "avec", "pour", "dans", "une", "des", "les", "cet", "cette", "ennemi", "monstre",
        "ajoute", "ajouter", "est", "que", "peux", "peut", "chaque", "quand", "plus", "faire",
        "modifie", "modifier", "jauge", "attaque", "coup", "commande", "commandes", "utilise", "existe"
    };

    private static readonly Regex CommandIdRegex = new("0x([2364][0-9A-Fa-f]{3})", RegexOptions.Compiled);
    private static readonly Regex OffsetRegex = new("^[-* ]*0x([0-9A-Fa-f]{4,6})", RegexOptions.Compiled);
}
