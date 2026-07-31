namespace SpiraModifier.Core.Workspace;

public class MonsterFileEntry
{
    public string FullPath { get; init; } = "";
    public string Language { get; init; } = "jp";
    public string FileName => Path.GetFileNameWithoutExtension(FullPath);
    public Dictionary<string, string> AllLanguageVariants { get; } = new();
}

/// <summary>
/// Représente un dossier kernel (battle/kernel/) dans une langue donnée,
/// avec les fichiers présents.
/// </summary>
public class KernelFolderEntry
{
    public string Language { get; init; } = "jp";
    public string DirectoryPath { get; init; } = "";

    public string? CommandPath { get; set; }
    public string? MonMagic1Path { get; set; }
    public string? MonMagic2Path { get; set; }
    public string? ItemPath { get; set; }
    public string? Monster1Path { get; set; }
    public string? Monster2Path { get; set; }
    public string? Monster3Path { get; set; }
    public string? WeaponNamePath { get; set; }
    public string? AbilityPath { get; set; }
    public string? ImportantPath { get; set; }
    public string? PlayerSavePath { get; set; }
    public string? PlayerRomPath { get; set; }
    public string? WeaponPath { get; set; }
    public string? TakaraPath { get; set; }

    public bool HasAttackFiles => MonMagic1Path != null || MonMagic2Path != null;
    public bool HasItemFile => ItemPath != null;
    public bool HasMonsterLocalization => Monster1Path != null || Monster2Path != null || Monster3Path != null;
    public bool HasWeaponNames => WeaponNamePath != null;
    public bool HasAbilities => AbilityPath != null;
    public bool HasKeyItems => ImportantPath != null;
    public bool HasPlayerStartData => PlayerSavePath != null;
    public bool HasWeaponFile => WeaponPath != null;
    public bool HasTakaraFile => TakaraPath != null;
}

public class WorkspaceScanResult
{
    public string RootPath { get; init; } = "";
    public WorkspaceCapabilities Capabilities { get; set; } = WorkspaceCapabilities.None;
    public List<MonsterFileEntry> Monsters { get; } = new();
    public List<string> MonsterFilePaths => Monsters.Select(m => m.FullPath).ToList();

    /// <summary>Tous les dossiers kernel détectés, indexés par langue.</summary>
    public Dictionary<string, KernelFolderEntry> KernelByLanguage { get; } = new();

    /// <summary>Compatibilité : chemins du jppc (données mécaniques).</summary>
    public string? CommandFilePath { get; set; }
    public string? MonMagic1FilePath { get; set; }
    public string? MonMagic2FilePath { get; set; }
    public string? ItemFilePath { get; set; }

    public Dictionary<string, string> LocalizationFolders { get; } = new();

    /// <summary>Pour chaque langue détectée, le chemin du dossier battle/kernel/ (compat).</summary>
    public Dictionary<string, string> LocalizedKernelDirs { get; } = new();

    /// <summary>Chemin de buki_get.bin (équipements obtenus en jeu).</summary>
    public string? BukiGetFilePath { get; set; }

    /// <summary>Chemin de shop_arms.bin (équipements achetables en boutique).</summary>
    public string? ShopArmsFilePath { get; set; }

    /// <summary>Chemin de kaizou.bin (recettes de customisation des équipements joueurs).</summary>
    public string? KaizouFilePath { get; set; }

    /// <summary>Chemin de btl.bin (tables d'encounters par zone — rencontres aléatoires + scriptées).</summary>
    public string? BtlFilePath { get; set; }

    /// <summary>Chemin de ply_save.bin (stats, équipement et compétences de départ des personnages).</summary>
    public string? PlayerSaveFilePath { get; set; }

    /// <summary>Chemin de ply_rom.bin (croissance/texte ROM des personnages, réservé pour un module futur).</summary>
    public string? PlayerRomFilePath { get; set; }

    /// <summary>Chemin de weapon.bin (table d'équipements référencée par ply_save.bin).</summary>
    public string? WeaponFilePath { get; set; }

    /// <summary>Chemin de takara.bin (contenus globaux des coffres/trésors de maps).</summary>
    public string? TakaraFilePath { get; set; }

    /// <summary>
    /// Chemins des fichiers de scène de combat individuels (battle/btl/{map}/{map}_{NN}.bin),
    /// indexés par leur nom sans extension (ex : "cdsp00_00").
    /// Une seule entrée par scène : si la scène existe dans plusieurs langues, on prend
    /// la plus prioritaire (les positions/formations ne sont pas localisées).
    /// </summary>
    public Dictionary<string, string> BattleScenePaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Scripts de maps/event .ebp, indexés par ID event (ex : bsil0100).
    /// Utilisés pour détecter quelles maps référencent chaque entrée takara.bin.
    /// </summary>
    public Dictionary<string, string> EventScriptPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? EncodingFolderPath { get; set; }
    public List<string> Warnings { get; } = new();

    public string GetSummary()
    {
        if (Capabilities == WorkspaceCapabilities.None)
            return "Aucun contenu reconnu dans ce dossier.";
        var parts = new List<string>();
        if (Monsters.Count > 0) parts.Add($"{Monsters.Count} monstres");
        if (BattleScenePaths.Count > 0) parts.Add($"{BattleScenePaths.Count} scènes de combat");
        if (MonMagic1FilePath != null || MonMagic2FilePath != null) parts.Add("attaques monstres");
        if (CommandFilePath != null) parts.Add("commandes joueur");
        if (ItemFilePath != null) parts.Add("objets");
        if (PlayerSaveFilePath != null) parts.Add("départ joueurs");
        if (TakaraFilePath != null) parts.Add("coffres/maps");
        if (KernelByLanguage.Count > 0)
            parts.Add($"kernels en {KernelByLanguage.Count} langues");
        if (EncodingFolderPath != null) parts.Add("charsets");
        return "Détecté : " + string.Join(" · ", parts);
    }
}

public static class WorkspaceScanner
{
    private static readonly Dictionary<string, string> LanguageCodeMap = new()
    {
        { "frpc", "fr" }, { "enpc", "en" }, { "uspc", "en" },
        { "depc", "de" }, { "espc", "es" }, { "sppc", "es" },
        { "itpc", "it" }, { "chpc", "ch" }, { "cnpc", "ch" },
        { "krpc", "kr" }, { "jppc", "jp" },
    };

    private static readonly string[] LanguagePreferenceOrder =
        { "fr", "en", "de", "es", "it", "ch", "kr", "jp" };

    /// <summary>
    /// Scanne un workspace.
    /// </summary>
    /// <param name="rootPath">Dossier principal (typiquement le hardmod).</param>
    /// <param name="vanillaReferenceFolder">Dossier vanilla optionnel (fallback pour fichiers manquants).</param>
    /// <param name="externalEncodingFolder">Dossier ffx_encoding/ externe (fallback prioritaire pour charsets).</param>
    public static WorkspaceScanResult Scan(
        string rootPath,
        string? vanillaReferenceFolder = null,
        string? externalEncodingFolder = null)
    {
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException($"Le dossier '{rootPath}' n'existe pas.");

        var result = ScanInternal(rootPath);

        // Fallback : dossier vanilla complet (si fourni et différent du dossier principal)
        if (vanillaReferenceFolder != null
            && Directory.Exists(vanillaReferenceFolder)
            && !PathsEqual(vanillaReferenceFolder, rootPath))
        {
            var vanillaResult = ScanInternal(vanillaReferenceFolder);
            MergeFromVanilla(result, vanillaResult);
        }

        // Override prioritaire pour le dossier ffx_encoding/ externe
        // (utile si l'utilisateur veut juste fournir les charsets sans tout le vanilla)
        if (externalEncodingFolder != null
            && Directory.Exists(externalEncodingFolder)
            && (result.EncodingFolderPath == null || !DirectoryHasCharsets(result.EncodingFolderPath)))
        {
            result.EncodingFolderPath = externalEncodingFolder;
        }

        ComputeWarnings(result);
        return result;
    }

    /// <summary>
    /// Scan d'un seul dossier sans fallback, sans warnings (étape interne).
    /// </summary>
    private static WorkspaceScanResult ScanInternal(string rootPath)
    {
        var result = new WorkspaceScanResult { RootPath = rootPath };

        // 1) Monstres .bin (battle/mon/)
        var byMonsterId = new Dictionary<string, MonsterFileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var monDir in FindDirectoriesByPattern(rootPath, "battle", "mon").OrderBy(KernelDirectoryPriority))
        {
            var lang = DetectLanguageFromPath(monDir);
            foreach (var path in Directory.EnumerateFiles(monDir, "m*.bin", SearchOption.AllDirectories))
            {
                if (!IsValidMonsterFileName(path)) continue;
                var name = Path.GetFileName(path);

                if (!byMonsterId.TryGetValue(name, out var existing))
                {
                    existing = new MonsterFileEntry { FullPath = path, Language = lang };
                    byMonsterId[name] = existing;
                }
                existing.AllLanguageVariants[lang] = path;

                if (LanguagePriority(lang) < LanguagePriority(existing.Language))
                {
                    var replacement = new MonsterFileEntry { FullPath = path, Language = lang };
                    foreach (var (k, v) in existing.AllLanguageVariants)
                        replacement.AllLanguageVariants[k] = v;
                    byMonsterId[name] = replacement;
                }
            }
        }
        result.Monsters.AddRange(byMonsterId.Values);
        if (result.Monsters.Count > 0)
            result.Capabilities |= WorkspaceCapabilities.Monsters;

        // 2) Dossiers kernel : on parcourt TOUS les battle/kernel/ et on construit
        //    un KernelFolderEntry par langue avec ses fichiers
        foreach (var kernelDir in FindDirectoriesByPattern(rootPath, "battle", "kernel").OrderBy(KernelDirectoryPriority))
        {
            var lang = DetectLanguageFromPath(kernelDir);

            if (!result.KernelByLanguage.TryGetValue(lang, out var entry))
            {
                entry = new KernelFolderEntry { Language = lang, DirectoryPath = kernelDir };
                result.KernelByLanguage[lang] = entry;
            }

            entry.CommandPath    ??= ExistingPath(kernelDir, "command.bin");
            entry.MonMagic1Path  ??= ExistingPath(kernelDir, "monmagic1.bin");
            entry.MonMagic2Path  ??= ExistingPath(kernelDir, "monmagic2.bin");
            entry.ItemPath       ??= ExistingPath(kernelDir, "item.bin");
            entry.Monster1Path   ??= ExistingPath(kernelDir, "monster1.bin");
            entry.Monster2Path   ??= ExistingPath(kernelDir, "monster2.bin");
            entry.Monster3Path   ??= ExistingPath(kernelDir, "monster3.bin");
            entry.WeaponNamePath ??= ExistingPath(kernelDir, "w_name.bin");
            entry.AbilityPath    ??= ExistingPath(kernelDir, "a_ability.bin");
            entry.ImportantPath  ??= ExistingPath(kernelDir, "important.bin");
            entry.PlayerSavePath ??= ExistingPath(kernelDir, "ply_save.bin");
            entry.PlayerRomPath  ??= ExistingPath(kernelDir, "ply_rom.bin");
            entry.WeaponPath     ??= ExistingPath(kernelDir, "weapon.bin");
            entry.TakaraPath     ??= ExistingPath(kernelDir, "takara.bin");

            // Important : on enregistre le kernel comme source localisée dès qu'il
            // contient AU MOINS UN fichier traduisible (pas seulement monsterN.bin).
            // Cas d'usage : un hardmod peut ne modifier que monmagic1.bin ou command.bin
            // sans toucher aux fichiers monsterN.bin — mais on veut quand même charger
            // les noms d'attaques modifiés.
            var hasAnyLocalizable =
                entry.HasMonsterLocalization ||
                entry.HasAttackFiles ||
                entry.HasItemFile ||
                entry.HasWeaponNames ||
                entry.HasAbilities ||
                entry.HasKeyItems ||
                entry.HasPlayerStartData ||
                entry.HasWeaponFile ||
                entry.CommandPath != null;
            if (hasAnyLocalizable && !result.LocalizedKernelDirs.ContainsKey(lang))
                result.LocalizedKernelDirs[lang] = kernelDir;
        }

        // Sélection des chemins "principaux" pour les données mécaniques.
        //
        // Ancienne version buggée : on prenait `KernelByLanguage["jp"]` directement,
        // mais si jppc/ existe en tant que dossier vide (cas typique d'un hardmod),
        // l'entrée est présente avec tous les chemins null → les capabilities ne se
        // déclenchent pas et les warnings affichent "monmagic introuvable" alors que
        // les fichiers sont bel et bien dans new_frpc/.
        //
        // Nouvelle version : on itère par ordre de préférence (jp d'abord, puis le
        // reste) et pour chaque type de fichier on prend le PREMIER chemin non-null
        // trouvé. Comme ça les capabilities reflètent ce qui est réellement chargeable.
        var orderedKernels = result.KernelByLanguage.Values
            .OrderByDescending(k => k.Language == "jp")
            .ThenBy(k => LanguagePriority(k.Language))
            .ToList();

        foreach (var k in orderedKernels)
        {
            result.CommandFilePath   ??= k.CommandPath;
            result.MonMagic1FilePath ??= k.MonMagic1Path;
            result.MonMagic2FilePath ??= k.MonMagic2Path;
            result.ItemFilePath      ??= k.ItemPath;
            result.PlayerSaveFilePath ??= k.PlayerSavePath;
            result.PlayerRomFilePath  ??= k.PlayerRomPath;
            result.WeaponFilePath     ??= k.WeaponPath;
            result.TakaraFilePath     ??= k.TakaraPath;
        }

        if (result.CommandFilePath != null)
            result.Capabilities |= WorkspaceCapabilities.KernelCommands;
        if (result.MonMagic1FilePath != null || result.MonMagic2FilePath != null)
            result.Capabilities |= WorkspaceCapabilities.KernelMonMagic;
        if (result.ItemFilePath != null)
            result.Capabilities |= WorkspaceCapabilities.KernelItems;
        if (result.PlayerSaveFilePath != null)
            result.Capabilities |= WorkspaceCapabilities.PlayerStartData;
        if (result.TakaraFilePath != null)
            result.Capabilities |= WorkspaceCapabilities.MapTreasures;

        // 3) Dossiers de langue
        foreach (var dir in Directory.EnumerateDirectories(rootPath, "new_*pc", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(dir).ToLowerInvariant();
            var suffix = name.StartsWith("new_") ? name[4..] : name;
            if (LanguageCodeMap.TryGetValue(suffix, out var langCode)
                && !result.LocalizationFolders.ContainsKey(langCode))
            {
                result.LocalizationFolders[langCode] = dir;
                result.Capabilities |= WorkspaceCapabilities.LocalizedTexts;
            }
        }

        // 4) ffx_encoding/
        foreach (var dir in Directory.EnumerateDirectories(rootPath, "ffx_encoding", SearchOption.AllDirectories))
        {
            result.EncodingFolderPath = dir;
            break;
        }

        // 4b) Équipements : buki_get.bin et shop_arms.bin
        // Ces fichiers ne contiennent pas de texte, donc ils sont dans originals/battle/kernel/
        // (pas dans les dossiers per-langue). On cherche aussi dans les autres kernels
        // au cas où ils s'y trouvent.
        var gearSearchDirs = new List<string>();
        foreach (var origKernel in Directory.EnumerateDirectories(rootPath, "kernel", SearchOption.AllDirectories))
        {
            var parent = Path.GetFileName(Path.GetDirectoryName(origKernel));
            if (string.Equals(parent, "battle", StringComparison.OrdinalIgnoreCase))
                gearSearchDirs.Add(origKernel);
        }
        foreach (var dir in gearSearchDirs)
        {
            result.BukiGetFilePath  ??= ExistingPath(dir, "buki_get.bin");
            result.ShopArmsFilePath ??= ExistingPath(dir, "shop_arms.bin");
            result.KaizouFilePath   ??= ExistingPath(dir, "kaizou.bin");
            result.BtlFilePath      ??= ExistingPath(dir, "btl.bin");
            result.TakaraFilePath   ??= ExistingPath(dir, "takara.bin");
        }

        if (result.TakaraFilePath != null)
            result.Capabilities |= WorkspaceCapabilities.MapTreasures;

        // 4c) Fichiers de scènes de combat : battle/btl/{map}/{map}_NN.bin
        // (un sous-dossier par préfixe de carte, ex : battle/btl/cdsp/, battle/btl/bsil/)
        foreach (var btlRoot in FindDirectoriesByPattern(rootPath, "battle", "btl"))
        {
            foreach (var path in Directory.EnumerateFiles(btlRoot, "*.bin", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                // Filtre : on garde les noms de la forme {map4}{NN}_{NN} ou {map4}_{NN}
                // (typiquement {prefix4}{subzone:00}_{formation:00} ex : "cdsp00_00")
                if (string.IsNullOrEmpty(name) || name.Length < 6) continue;

                // Évite d'écraser une entrée déjà présente (priorité au premier scan)
                if (!result.BattleScenePaths.ContainsKey(name))
                    result.BattleScenePaths[name] = path;
            }
        }

        // 4d) Scripts de maps/event : event/obj/{prefix}/{eventId}/{eventId}.ebp
        // Les coffres appellent takara.bin via obtainTreasure(...) dans ces scripts.
        foreach (var eventRoot in FindDirectoriesByPattern(rootPath, "event", "obj").OrderBy(EventDirectoryPriority))
        {
            foreach (var path in Directory.EnumerateFiles(eventRoot, "*.ebp", SearchOption.AllDirectories))
            {
                var eventId = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(eventId)) continue;
                if (!result.EventScriptPaths.ContainsKey(eventId))
                    result.EventScriptPaths[eventId] = path;
            }
        }

        // 5) Pas de warnings ici — ils sont calculés dans ComputeWarnings()
        //    après le merge avec le dossier vanilla éventuel.

        return result;
    }

    /// <summary>
    /// Calcule les warnings finaux à partir des données fusionnées.
    /// </summary>
    private static void ComputeWarnings(WorkspaceScanResult result)
    {
        if (result.Capabilities == WorkspaceCapabilities.None)
        {
            result.Warnings.Add("Aucun contenu FFX HD reconnu.");
            return;
        }

        if ((result.Capabilities & WorkspaceCapabilities.KernelMonMagic) == 0)
            result.Warnings.Add("monmagic1.bin / monmagic2.bin introuvables.");
        if ((result.Capabilities & WorkspaceCapabilities.KernelItems) == 0)
            result.Warnings.Add("item.bin introuvable.");
        if (result.EncodingFolderPath == null)
            result.Warnings.Add(
                "Dossier ffx_encoding/ introuvable. Sans charsets, les noms et descriptions ne peuvent pas être décodés. " +
                "Configure un dossier vanilla via Fichier > Configurer le dossier vanilla...");
        if (result.LocalizedKernelDirs.Count == 0)
            result.Warnings.Add("monster1/2/3.bin introuvables : noms de monstres non localisés.");
    }

    /// <summary>
    /// Fusionne un scan vanilla dans le résultat principal :
    /// remplit les chemins null avec leurs équivalents vanilla.
    /// Le dossier principal a toujours priorité — on ne remplace jamais ce qu'il a déjà.
    /// </summary>
    private static void MergeFromVanilla(WorkspaceScanResult main, WorkspaceScanResult vanilla)
    {
        // Encoding folder : on prend celui du vanilla si on n'en a pas
        main.EncodingFolderPath ??= vanilla.EncodingFolderPath;

        // Kernels par langue : pour chaque langue dans le vanilla, on complète
        // les chemins manquants dans le main. On crée l'entrée dans main si elle
        // n'existe pas.
        foreach (var (lang, vanillaEntry) in vanilla.KernelByLanguage)
        {
            if (!main.KernelByLanguage.TryGetValue(lang, out var mainEntry))
            {
                mainEntry = new KernelFolderEntry
                {
                    Language = lang,
                    DirectoryPath = vanillaEntry.DirectoryPath,
                };
                main.KernelByLanguage[lang] = mainEntry;
            }
            mainEntry.CommandPath    ??= vanillaEntry.CommandPath;
            mainEntry.MonMagic1Path  ??= vanillaEntry.MonMagic1Path;
            mainEntry.MonMagic2Path  ??= vanillaEntry.MonMagic2Path;
            mainEntry.ItemPath       ??= vanillaEntry.ItemPath;
            mainEntry.Monster1Path   ??= vanillaEntry.Monster1Path;
            mainEntry.Monster2Path   ??= vanillaEntry.Monster2Path;
            mainEntry.Monster3Path   ??= vanillaEntry.Monster3Path;
            mainEntry.WeaponNamePath ??= vanillaEntry.WeaponNamePath;
            mainEntry.AbilityPath    ??= vanillaEntry.AbilityPath;
            mainEntry.ImportantPath  ??= vanillaEntry.ImportantPath;
            mainEntry.PlayerSavePath ??= vanillaEntry.PlayerSavePath;
            mainEntry.PlayerRomPath  ??= vanillaEntry.PlayerRomPath;
            mainEntry.WeaponPath     ??= vanillaEntry.WeaponPath;
            mainEntry.TakaraPath     ??= vanillaEntry.TakaraPath;
        }

        // Chemins "principaux" (pour la mécanique) : on complète les nulls
        main.CommandFilePath   ??= vanilla.CommandFilePath;
        main.MonMagic1FilePath ??= vanilla.MonMagic1FilePath;
        main.MonMagic2FilePath ??= vanilla.MonMagic2FilePath;
        main.ItemFilePath      ??= vanilla.ItemFilePath;
        main.PlayerSaveFilePath ??= vanilla.PlayerSaveFilePath;
        main.PlayerRomFilePath  ??= vanilla.PlayerRomFilePath;
        main.WeaponFilePath     ??= vanilla.WeaponFilePath;
        main.TakaraFilePath     ??= vanilla.TakaraFilePath;

        // Équipements (pas de notion de langue, on prend du vanilla si manquant)
        main.BukiGetFilePath  ??= vanilla.BukiGetFilePath;
        main.ShopArmsFilePath ??= vanilla.ShopArmsFilePath;
        main.KaizouFilePath   ??= vanilla.KaizouFilePath;
        main.BtlFilePath      ??= vanilla.BtlFilePath;

        // Merge BattleScenePaths : le main est prioritaire, vanilla en complément
        foreach (var (name, path) in vanilla.BattleScenePaths)
            if (!main.BattleScenePaths.ContainsKey(name))
                main.BattleScenePaths[name] = path;

        // Merge EventScriptPaths : le main est prioritaire, vanilla en complément
        foreach (var (eventId, path) in vanilla.EventScriptPaths)
            if (!main.EventScriptPaths.ContainsKey(eventId))
                main.EventScriptPaths[eventId] = path;

        // Localisation des noms : on ajoute les langues vanilla manquantes
        foreach (var (lang, dir) in vanilla.LocalizedKernelDirs)
            if (!main.LocalizedKernelDirs.ContainsKey(lang))
                main.LocalizedKernelDirs[lang] = dir;

        // Monstres .bin : on complète ce que le main n'a pas. Pour chaque monstre
        // vanilla qui n'existe pas dans main, on l'ajoute.
        var mainMonsterNames = new HashSet<string>(
            main.Monsters.Select(m => Path.GetFileName(m.FullPath)),
            StringComparer.OrdinalIgnoreCase);
        foreach (var vmon in vanilla.Monsters)
        {
            var name = Path.GetFileName(vmon.FullPath);
            if (!mainMonsterNames.Contains(name))
            {
                main.Monsters.Add(vmon);
                mainMonsterNames.Add(name);
            }
        }

        // Capabilities : on combine
        main.Capabilities |= vanilla.Capabilities;

        // Si on avait des chemins principaux null avant, on a peut-être
        // gagné des capabilities — on recalcule
        if (main.CommandFilePath != null)
            main.Capabilities |= WorkspaceCapabilities.KernelCommands;
        if (main.MonMagic1FilePath != null || main.MonMagic2FilePath != null)
            main.Capabilities |= WorkspaceCapabilities.KernelMonMagic;
        if (main.ItemFilePath != null)
            main.Capabilities |= WorkspaceCapabilities.KernelItems;
        if (main.PlayerSaveFilePath != null)
            main.Capabilities |= WorkspaceCapabilities.PlayerStartData;
        if (main.TakaraFilePath != null)
            main.Capabilities |= WorkspaceCapabilities.MapTreasures;
        if (main.Monsters.Count > 0)
            main.Capabilities |= WorkspaceCapabilities.Monsters;
    }

    /// <summary>True si le dossier contient au moins un ffxsjistbl_*.bin.</summary>
    private static bool DirectoryHasCharsets(string dir)
    {
        return File.Exists(Path.Combine(dir, "ffxsjistbl_us.bin"))
            || File.Exists(Path.Combine(dir, "ffxsjistbl_jp.bin"))
            || File.Exists(Path.Combine(dir, "ffxsjistbl_ch.bin"))
            || File.Exists(Path.Combine(dir, "ffxsjistbl_kr.bin"));
    }

    /// <summary>Comparaison de chemins normalisés pour tester l'égalité.</summary>
    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd('\\', '/'),
                Path.GetFullPath(b).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string? ExistingPath(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        return File.Exists(path) ? path : null;
    }

    private static string DetectLanguageFromPath(string path)
    {
        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        foreach (var (folderName, langCode) in LanguageCodeMap)
        {
            if (normalized.Contains($"/{folderName}/") || normalized.Contains($"/new_{folderName}/"))
                return langCode;
        }
        return "jp";
    }

    private static int LanguagePriority(string lang)
    {
        var idx = Array.IndexOf(LanguagePreferenceOrder, lang);
        return idx == -1 ? int.MaxValue : idx;
    }

    private static int KernelDirectoryPriority(string path)
    {
        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        if (normalized.Contains("/new_")) return 0;
        foreach (var folderName in LanguageCodeMap.Keys)
            if (normalized.Contains($"/{folderName}/"))
                return string.Equals(folderName, "jppc", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
        if (normalized.Contains("/inpc/")) return 3;
        return 4;
    }

    private static int EventDirectoryPriority(string path)
    {
        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        if (normalized.Contains("/new_")) return 0;
        if (normalized.Contains("/jppc/")) return 1;
        foreach (var folderName in LanguageCodeMap.Keys)
            if (normalized.Contains($"/{folderName}/"))
                return 2;
        if (normalized.Contains("/inpc/")) return 3;
        return 4;
    }

    private static IEnumerable<string> FindDirectoriesByPattern(string root, string parent, string child)
    {
        foreach (var dir in Directory.EnumerateDirectories(root, child, SearchOption.AllDirectories))
        {
            var parentDir = Path.GetFileName(Path.GetDirectoryName(dir));
            if (string.Equals(parentDir, parent, StringComparison.OrdinalIgnoreCase))
                yield return dir;
        }
    }

    private static bool IsValidMonsterFileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.Length < 4 || !name.StartsWith('m')) return false;
        for (int i = 1; i <= 3; i++)
            if (!char.IsLetterOrDigit(name[i])) return false;
        return true;
    }
}
