using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SpiraModifier.Core.Models;

public sealed class AtelGlobalIndex
{
    public AtelGlobalIndex(IEnumerable<AtelGlobalIndexEntry> entries, int errorCount = 0, DateTimeOffset? builtAt = null)
    {
        Entries = entries.ToList();
        ErrorCount = errorCount;
        BuiltAt = builtAt ?? DateTimeOffset.Now;
    }

    public IReadOnlyList<AtelGlobalIndexEntry> Entries { get; }
    public int ErrorCount { get; }
    public DateTimeOffset BuiltAt { get; }
    public bool IsEmpty => Entries.Count == 0;

    public IReadOnlyList<AtelGlobalIndexMatch> Search(
        string prompt,
        string? currentMonsterFileName = null,
        int maxEntries = 8,
        int maxLinesPerEntry = 10)
    {
        if (Entries.Count == 0 || string.IsNullOrWhiteSpace(prompt))
            return Array.Empty<AtelGlobalIndexMatch>();

        var terms = AtelKnowledgeBase.BuildSearchTerms(prompt).ToList();
        if (terms.Count == 0)
            return Array.Empty<AtelGlobalIndexMatch>();

        var normalizedCurrent = AtelKnowledgeBase.NormalizeForSearch(currentMonsterFileName ?? "");
        var results = new List<AtelGlobalIndexMatch>();

        foreach (var entry in Entries)
        {
            var score = ScoreEntry(entry, terms, normalizedCurrent);
            if (score <= 0)
                continue;

            var snippets = BuildSnippets(entry, terms, maxLinesPerEntry);
            results.Add(new AtelGlobalIndexMatch(entry, score, snippets));
        }

        return results
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.Entry.MonsterFileName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxEntries))
            .ToList();
    }

    private static int ScoreEntry(AtelGlobalIndexEntry entry, IReadOnlyList<string> terms, string normalizedCurrent)
    {
        var score = 0;
        var nameText = entry.NormalizedNameText;

        if (!string.IsNullOrWhiteSpace(normalizedCurrent)
            && string.Equals(entry.NormalizedFileName, normalizedCurrent, StringComparison.OrdinalIgnoreCase))
            score += 10;

        foreach (var term in terms)
        {
            if (term.Length < 2)
                continue;

            if (entry.NormalizedFileName.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 50;
            if (nameText.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 35;
            if (entry.NormalizedTags.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 25;

            var occurrences = CountOccurrences(entry.NormalizedSearchText, term);
            if (occurrences > 0)
                score += Math.Min(occurrences, 12) * 3;
        }

        if (terms.Any(t => t.Contains("overdrive", StringComparison.OrdinalIgnoreCase))
            && entry.Tags.Any(t => t.Contains("overdrive", StringComparison.OrdinalIgnoreCase)))
            score += 80;

        if (terms.Any(t => t is "phase" or "phases" or "transition" or "seuil" or "threshold")
            && entry.Tags.Any(t => t.Contains("phase", StringComparison.OrdinalIgnoreCase)
                                   || t.Contains("transition", StringComparison.OrdinalIgnoreCase)))
            score += 80;

        if (terms.Any(t => t is "jecht" or "braska" or "ultime" or "chimere")
            && (nameText.Contains("jecht", StringComparison.OrdinalIgnoreCase)
                || nameText.Contains("braska", StringComparison.OrdinalIgnoreCase)
                || nameText.Contains("ultime", StringComparison.OrdinalIgnoreCase)
                || nameText.Contains("chimere", StringComparison.OrdinalIgnoreCase)))
            score += 120;

        return score;
    }

    private static IReadOnlyList<string> BuildSnippets(
        AtelGlobalIndexEntry entry,
        IReadOnlyList<string> terms,
        int maxLines)
    {
        var matches = entry.Lines
            .Where(line =>
            {
                var normalized = AtelKnowledgeBase.NormalizeForSearch(line);
                return terms.Any(term => normalized.Contains(term, StringComparison.OrdinalIgnoreCase));
            })
            .Distinct(StringComparer.Ordinal)
            .Take(Math.Max(1, maxLines))
            .ToList();

        if (matches.Count > 0)
            return matches;

        return entry.HighlightLines.Take(Math.Max(1, maxLines)).ToList();
    }

    private static int CountOccurrences(string value, string term)
    {
        var count = 0;
        var index = 0;
        while (index < value.Length)
        {
            var found = value.IndexOf(term, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                break;
            count++;
            index = found + Math.Max(1, term.Length);
        }
        return count;
    }
}

public sealed class AtelGlobalIndexEntry
{
    public string MonsterFileName { get; init; } = "";
    public string MonsterDisplayName { get; init; } = "";
    public int? MonsterIndex { get; init; }
    public string SourcePath { get; init; } = "";
    public string ScriptId { get; init; } = "";
    public string CreatorTag { get; init; } = "";
    public int WorkerCount { get; init; }
    public int InstructionCount { get; init; }
    public int RawSize { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> HighlightLines { get; init; } = Array.Empty<string>();
    public string Summary { get; init; } = "";
    public string NormalizedFileName { get; init; } = "";
    public string NormalizedNameText { get; init; } = "";
    public string NormalizedTags { get; init; } = "";
    public string NormalizedSearchText { get; init; } = "";
}

public sealed record AtelGlobalIndexMatch(
    AtelGlobalIndexEntry Entry,
    int Score,
    IReadOnlyList<string> Snippets);

public static partial class AtelKnowledgeBase
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "avec", "pour", "dans", "une", "des", "les", "cet", "cette", "aux", "sur",
        "monstre", "ennemi", "peux", "peut", "veux", "faire", "faut", "comme",
        "ajoute", "ajouter", "modifier", "modifie", "question", "reponse",
        "chaque", "quand", "coup", "coups", "attaque", "attaques", "recu", "recus"
    };

    public static AtelGlobalIndexEntry CreateIndexEntry(
        string monsterFileName,
        string monsterDisplayName,
        int? monsterIndex,
        string sourcePath,
        AtelDecompiledScript script)
    {
        var lines = script.Instructions
            .Where(i => !string.IsNullOrWhiteSpace(i.Annotation))
            .Select(FormatInstructionLine)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var tags = BuildTags(script, lines);
        var highlights = lines
            .Where(IsImportantLine)
            .Take(40)
            .ToList();

        if (highlights.Count == 0)
            highlights = lines.Take(20).ToList();

        var display = string.IsNullOrWhiteSpace(monsterDisplayName)
            ? monsterFileName
            : monsterDisplayName;
        var summary = BuildSummary(monsterFileName, display, script, tags);
        var searchText = string.Join("\n", new[]
        {
            monsterFileName,
            display,
            script.ScriptId,
            script.CreatorTag,
            summary,
            string.Join(" ", tags),
            string.Join("\n", lines)
        });

        return new AtelGlobalIndexEntry
        {
            MonsterFileName = monsterFileName,
            MonsterDisplayName = display,
            MonsterIndex = monsterIndex,
            SourcePath = sourcePath,
            ScriptId = script.ScriptId,
            CreatorTag = script.CreatorTag,
            WorkerCount = script.Workers.Count,
            InstructionCount = script.Instructions.Count,
            RawSize = script.RawSize,
            Tags = tags,
            Lines = lines,
            HighlightLines = highlights,
            Summary = summary,
            NormalizedFileName = NormalizeForSearch(monsterFileName),
            NormalizedNameText = NormalizeForSearch($"{monsterFileName} {display} {script.ScriptId} {script.CreatorTag}"),
            NormalizedTags = NormalizeForSearch(string.Join(" ", tags)),
            NormalizedSearchText = NormalizeForSearch(searchText),
        };
    }

    public static IReadOnlyList<string> BuildNotes(string prompt)
    {
        var normalized = NormalizeForSearch(prompt);
        var notes = new List<string>();

        if (IsOverdrivePrompt(normalized))
        {
            notes.Add("Overdrive ennemi : ne pas repondre que c'est impossible par principe. Le decompilateur expose des proprietes btlChrProperty liees a l'Overdrive : OverdriveMode, OverdriveCurrent, OverdriveMax, showOverdriveBar, stat_limit_bar_pos et stat_limit_bar_flag_cam.");
            notes.Add("Pattern prudent pour une jauge 10/100 : initialiser OverdriveMax a 100, OverdriveCurrent a 0, activer showOverdriveBar, incrementer apres un vrai LastDamageTakenHP > 0, plafonner a OverdriveMax, puis declencher une commande/phase quand la jauge est pleine.");
            notes.Add("Les valeurs exactes de mode, position et camera doivent etre recoupees avec un exemple vanilla proche, typiquement chimere, Magus Sisters, Jecht/Braska's Final Aeon ou autre boss qui manipule une jauge Overdrive.");
            notes.Add("Appels ATEL connus utiles autour de cette famille : increaseMagusMotivationAndOverdrive et setMagusMotivationAndOverdriveChangeInPositiveOrNegativeCase.");
        }

        if (ContainsAny(normalized, "reaction", "contre", "counter", "riposte", "dernier attaquant", "lastattacker", "lastdamagetakenhp"))
        {
            notes.Add("Pour reagir a une action recue, chercher d'abord les blocs qui lisent LastDamageTakenHP, LastAttacker, usedCommand, chosenCommand ou isCounterattackAllowed.");
        }

        if (IsPhasePrompt(normalized))
        {
            notes.Add("Pour savoir si un boss a plusieurs phases, ne cherche pas le mot 'phase' litteralement : les indices sont plutot des seuils HP/Overdrive, des variables qui changent d'etat, des commandes ajoutees/retirees, des messages, des scenes, des changements de modele/motion ou des tests currentBattle().");
            notes.Add("Un bon diagnostic doit separer les indices forts (seuil HP, changement de commande, scene/message de transition) des indices faibles (beaucoup de variables ou de branches sans effet clair).");
            notes.Add("Si les indices sont ambigus, reponds franchement : 'je vois une logique de variation, mais pas encore une phase de combat certaine'.");
        }

        return notes;
    }

    public static IReadOnlyList<string> BuildSearchTerms(string prompt)
    {
        var normalized = NormalizeForSearch(prompt);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in WordRegex().Matches(normalized))
        {
            var word = match.Value;
            if (word.Length < 3 || StopWords.Contains(word))
                continue;
            result.Add(word);
        }

        if (IsOverdrivePrompt(normalized))
        {
            AddRange(result,
                "overdrive",
                "jauge",
                "limit",
                "showoverdrivebar",
                "overdrivecurrent",
                "overdrivemax",
                "overdrivemode",
                "stat_limit_bar",
                "btlsetuplimit",
                "magusmotivation");
        }

        if (ContainsAny(normalized, "jecht", "braska", "ultime", "chimere", "chimere ultime", "aeon"))
        {
            AddRange(result, "jecht", "braska", "ultime", "chimere", "aeon");
        }

        if (ContainsAny(normalized, "flambos", "flan"))
            AddRange(result, "flambos", "flan");

        if (ContainsAny(normalized, "eau", "water"))
            AddRange(result, "eau", "water");

        if (ContainsAny(normalized, "counter", "contre", "riposte", "reagir", "reaction"))
            AddRange(result, "counterattack", "lastdamagetakenhp", "lastattacker", "usedcommand", "chosencommand");

        if (IsPhasePrompt(normalized))
        {
            AddRange(result,
                "phase",
                "phases",
                "transition",
                "threshold",
                "seuil",
                "hp",
                "currentbattle",
                "runbtlscene",
                "displaybattlemessage",
                "setcommanddisabled",
                "addcommand",
                "removecommand",
                "clearowncommands",
                "btlsetmodelhide",
                "changeactorname",
                "overdrivecurrent");
        }

        return result.ToList();
    }

    public static bool IsOverdrivePrompt(string normalizedPrompt)
        => ContainsAny(normalizedPrompt, "overdrive", "jauge", "od ", "od.", "limite", "limit bar", "showoverdrivebar");

    public static bool IsPhasePrompt(string normalizedPrompt)
        => ContainsAny(normalizedPrompt,
            "phase",
            "phases",
            "plusieurs formes",
            "plusieurs forme",
            "forme de combat",
            "seconde forme",
            "deuxieme forme",
            "transition",
            "seuil hp",
            "seuil de hp",
            "seuil de vie",
            "change de comportement",
            "changement de comportement",
            "change ses attaques",
            "change d'attaque",
            "enrage",
            "rage",
            "a mi-vie",
            "mi vie",
            "moitie de vie",
            "boss a plusieurs");

    public static string NormalizeForSearch(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static IReadOnlyList<string> BuildTags(AtelDecompiledScript script, IReadOnlyList<string> lines)
    {
        var text = string.Join("\n", lines);
        var tags = new List<string>();

        if (ContainsAny(text, "Overdrive", "showOverdriveBar", "OverdriveCurrent", "OverdriveMax", "btlSetUpLimit"))
            tags.Add("overdrive / jauge");
        if (ContainsAny(text, "LastDamageTakenHP", "LastAttacker", "isCounterattackAllowed", "CounterAttack", "usedCommand("))
            tags.Add("reaction / counter");
        if (ContainsAny(text, "performCommand(", "forcePerformCommand(", "addCommand", "btlSetCommandBuffer"))
            tags.Add("commandes / actions");
        if (ContainsAny(text,
            ".HP",
            "HP [00h]",
            "currentBattle(",
            "setCommandDisabled(",
            "addCommand(",
            "addCommandToSelf(",
            "removeCommand(",
            "clearOwnCommands(",
            "runBtlScene(",
            "displayBattleMessage",
            "btlSetModelHide",
            "changeActorName",
            "OverdriveCurrent",
            "setBattleFlag"))
            tags.Add("phases / transitions possibles");
        if (ContainsAny(text, "currentBattle(", "runBtlScene(", "displayBattleMessage", "cam", "refMove", "refSet"))
            tags.Add("scene / camera");
        if (ContainsAny(text, "MustBeKilledForBattleEnd", "VisibleOnCTB", "GetsTurns", "Targetable"))
            tags.Add("acteur technique / visibilite");
        if (ContainsAny(text, "motion.", "btlSetScale", "btlSetBindEffect", "DeathAnimation"))
            tags.Add("animations / effets");
        if (script.Warnings.Count > 0)
            tags.Add("warnings decompilation");

        return tags.Count == 0 ? new[] { "general" } : tags;
    }

    private static bool IsImportantLine(string line)
        => ContainsAny(line,
            "Overdrive",
            "showOverdriveBar",
            "LastDamageTakenHP",
            "LastAttacker",
            "isCounterattackAllowed",
            "usedCommand(",
            "chosenCommand(",
            "performCommand(",
            "forcePerformCommand(",
            "addCommand",
            "removeCommand",
            "clearOwnCommands",
            "setCommandDisabled",
            "currentBattle(",
            "runBtlScene(",
            "displayBattleMessage",
            ".HP",
            "HP [00h]",
            "OverdriveCurrent",
            "setBattleFlag",
            "btlSetModelHide",
            "changeActorName",
            "MustBeKilledForBattleEnd",
            "VisibleOnCTB",
            "GetsTurns",
            "Targetable",
            "motion.",
            "cam");

    private static string BuildSummary(
        string fileName,
        string displayName,
        AtelDecompiledScript script,
        IReadOnlyList<string> tags)
    {
        var tagText = tags.Count == 0 ? "general" : string.Join(", ", tags.Take(5));
        return $"{fileName} - {displayName} : {script.Workers.Count} worker(s), {script.Instructions.Count} instruction(s), themes : {tagText}.";
    }

    private static string FormatInstructionLine(AtelInstruction instruction)
        => $"0x{instruction.Offset:X4}: {instruction.Annotation}";

    private static void AddRange(HashSet<string> output, params string[] terms)
    {
        foreach (var term in terms)
            output.Add(NormalizeForSearch(term));
    }

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(n => value.Contains(n, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex("[a-zA-Z0-9_\\-]+")]
    private static partial Regex WordRegex();
}
