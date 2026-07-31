using SpiraModifier.Core.Models;
using SpiraModifier.Core.Text;

namespace SpiraModifier.Core.Workspace;

public class SpiraWorkspace
{
    public WorkspaceScanResult Scan { get; }
    public FfxCharset? UsCharset { get; private set; }
    public FfxCharset? JpCharset { get; private set; }
    public Dictionary<string, FfxCharset> CharsetsByLanguage { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bases de localisation des noms de monstres (clé = code langue).</summary>
    public Dictionary<string, LocalizationDatabase> LocalizationDatabases { get; } = new();

    /// <summary>
    /// Fichiers d'attaques de monstres chargés par langue.
    /// Clé : code langue. Valeur : (monmagic1, monmagic2) — l'un peut être null.
    /// </summary>
    public Dictionary<string, (AttackFile? MonMagic1, AttackFile? MonMagic2)> MonsterAttacksByLanguage { get; } = new();

    /// <summary>Fichiers d'objets chargés par langue.</summary>
    public Dictionary<string, AttackFile> ItemsByLanguage { get; } = new();

    /// <summary>Fichiers de commandes joueur chargés par langue.</summary>
    public Dictionary<string, AttackFile> PlayerCommandsByLanguage { get; } = new();

    private readonly Dictionary<AttackFile, string> _attackSourcePaths =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<GearFile, string> _gearSourcePaths =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<WeaponNameFile, string> _weaponNameSourcePaths =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AutoAbilityFile, string> _abilitySourcePaths =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PlayerSaveFile, string> _playerSaveSourcePaths =
        new(ReferenceEqualityComparer.Instance);
    private string? _customizationSourcePath;
    private string? _treasureSourcePath;
    private IReadOnlyList<MapTreasureUsage>? _treasureUsages;

    /// <summary>Équipements obtenus en jeu (drops, coffres). Aucune notion de langue.</summary>
    public GearFile? BukiGetFile { get; private set; }

    /// <summary>Équipements achetables en boutique. Aucune notion de langue.</summary>
    public GearFile? ShopArmsFile { get; private set; }

    /// <summary>Noms d'armes par langue (w_name.bin) — 7 noms par entrée pour les 7 personnages humains.</summary>
    public Dictionary<string, WeaponNameFile> WeaponNamesByLanguage { get; } = new();

    /// <summary>Aptitudes (a_ability.bin) par langue — base des 4 slots d'aptitudes des équipements.</summary>
    public Dictionary<string, AutoAbilityFile> AbilitiesByLanguage { get; } = new();

    /// <summary>Objets clés (important.bin) par langue.</summary>
    public Dictionary<string, KeyItemFile> KeyItemsByLanguage { get; } = new();

    /// <summary>Recettes de customisation (kaizou.bin). Non localisé.
    /// Indique aussi quelles aptitudes sont applicables aux armes vs aux armures.</summary>
    public CustomizationFile? GearCustomizations { get; private set; }

    /// <summary>Données initiales des personnages (ply_save.bin).</summary>
    public Dictionary<string, PlayerSaveFile> PlayerSaveFilesByLanguage { get; } = new();

    /// <summary>Données initiales des personnages (ply_save.bin), langue préférée en compatibilité.</summary>
    public PlayerSaveFile? PlayerSaveFile { get; private set; }

    /// <summary>Table weapon.bin référencée par les index d'équipement de ply_save.bin.</summary>
    public GearFile? InitialEquipmentFile { get; private set; }

    /// <summary>Tables d'encounters par zone (btl.bin) — rencontres aléatoires + scriptées.
    /// Non localisé.</summary>
    public EncounterTableFile? EncounterTables { get; private set; }

    /// <summary>Contenus globaux des coffres/trésors (takara.bin). Non localisé.</summary>
    public TreasureFile? TreasureFile { get; private set; }

    /// <summary>
    /// Cache des BattleFile lus à la demande (clé : nom sans extension, ex : "cdsp00_00").
    /// Les fichiers sont volumineux (centaines de ko) et nombreux (~600) → lazy load.
    /// </summary>
    private readonly Dictionary<string, BattleFile?> _battleFileCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Cache des MonsterFile édités, indexés par chemin absolu source.
    /// Ne contient que les fichiers ayant été modifiés (ou en cours de modification).
    /// Le SaveService itère sur cette collection pour produire les fichiers de sortie.
    /// </summary>
    private readonly Dictionary<string, MonsterFile> _editedMonsters =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tous les MonsterFiles actuellement édités en mémoire (modifiés ou non).
    /// La distinction "modifié" se fait via <see cref="MonsterFile.IsDirty"/>.
    /// </summary>
    public IEnumerable<MonsterFile> EditedMonsters => _editedMonsters.Values;

    /// <summary>True si au moins un fichier a été modifié et n'est pas encore sauvegardé.</summary>
    public bool HasUnsavedChanges => _editedMonsters.Values.Any(m => m.IsDirty)
                                     || HasUnsavedAttackChanges
                                     || HasUnsavedGearChanges
                                     || HasUnsavedWeaponNameChanges
                                     || HasUnsavedAbilityChanges
                                     || HasUnsavedCustomizationChanges
                                     || HasUnsavedPlayerStartChanges
                                     || HasUnsavedTreasureChanges
                                     || HasUnsavedBattleSceneChanges
                                     || HasUnsavedEncounterTableChanges;

    public bool HasUnsavedAttackChanges => _attackSourcePaths.Keys.Any(f => f.IsDirty);
    public bool HasUnsavedGearChanges => _gearSourcePaths.Keys.Any(f => f.IsDirty);
    public bool HasUnsavedWeaponNameChanges => _weaponNameSourcePaths.Keys.Any(f => f.IsDirty);
    public bool HasUnsavedAbilityChanges => _abilitySourcePaths.Keys.Any(f => f.IsDirty);
    public bool HasUnsavedCustomizationChanges => GearCustomizations?.IsDirty == true;
    public bool HasUnsavedPlayerStartChanges => _playerSaveSourcePaths.Keys.Any(f => f.IsDirty);
    public bool HasUnsavedTreasureChanges => TreasureFile?.IsDirty == true;
    public bool HasUnsavedBattleSceneChanges => _battleFileCache.Values.Any(f => f?.IsDirty == true);
    public bool HasUnsavedEncounterTableChanges => EncounterTables?.IsDirty == true;

    public IEnumerable<(string SourcePath, AttackFile File)> DirtyAttackFilesWithPaths
    {
        get
        {
            foreach (var (file, sourcePath) in _attackSourcePaths)
                if (file.IsDirty)
                    yield return (sourcePath, file);
        }
    }

    public IEnumerable<(string SourcePath, GearFile File)> DirtyGearFilesWithPaths
    {
        get
        {
            foreach (var (file, sourcePath) in _gearSourcePaths)
                if (file.IsDirty)
                    yield return (sourcePath, file);
        }
    }

    public IEnumerable<(string SourcePath, WeaponNameFile File)> DirtyWeaponNameFilesWithPaths
    {
        get
        {
            foreach (var (file, sourcePath) in _weaponNameSourcePaths)
                if (file.IsDirty)
                    yield return (sourcePath, file);
        }
    }

    public IEnumerable<(string SourcePath, AutoAbilityFile File)> DirtyAbilityFilesWithPaths
    {
        get
        {
            foreach (var (file, sourcePath) in _abilitySourcePaths)
                if (file.IsDirty)
                    yield return (sourcePath, file);
        }
    }

    public IEnumerable<(string SourcePath, CustomizationFile File)> DirtyCustomizationFilesWithPaths
    {
        get
        {
            if (GearCustomizations?.IsDirty == true && !string.IsNullOrEmpty(_customizationSourcePath))
                yield return (_customizationSourcePath, GearCustomizations);
        }
    }

    public IEnumerable<(string SourcePath, PlayerSaveFile File)> DirtyPlayerSaveFilesWithPaths
    {
        get
        {
            foreach (var (file, sourcePath) in _playerSaveSourcePaths)
                if (file.IsDirty)
                    yield return (sourcePath, file);
        }
    }

    public IEnumerable<(string SourcePath, BattleFile File)> DirtyBattleSceneFilesWithPaths
    {
        get
        {
            foreach (var (fileName, file) in _battleFileCache)
            {
                if (file?.IsDirty != true) continue;
                if (Scan.BattleScenePaths.TryGetValue(fileName, out var sourcePath))
                    yield return (sourcePath, file);
            }
        }
    }

    public IEnumerable<(string SourcePath, EncounterTableFile File)> DirtyEncounterTableFilesWithPaths
    {
        get
        {
            if (EncounterTables?.IsDirty == true && !string.IsNullOrEmpty(Scan.BtlFilePath))
                yield return (Scan.BtlFilePath, EncounterTables);
        }
    }

    public IEnumerable<(string SourcePath, TreasureFile File)> DirtyTreasureFilesWithPaths
    {
        get
        {
            if (TreasureFile?.IsDirty == true && !string.IsNullOrEmpty(_treasureSourcePath))
                yield return (_treasureSourcePath, TreasureFile);
        }
    }

    /// <summary>
    /// Récupère le MonsterFile édité associé à un chemin source, ou null s'il
    /// n'a pas encore été enregistré pour édition.
    /// </summary>
    public MonsterFile? GetEditedMonster(string sourcePath)
        => _editedMonsters.TryGetValue(sourcePath, out var f) ? f : null;

    /// <summary>
    /// Enregistre un MonsterFile dans le cache d'édition (typiquement appelé
    /// avant la première modification pour pouvoir la persister à la sauvegarde).
    /// </summary>
    public void RegisterEditedMonster(string sourcePath, MonsterFile file)
    {
        _editedMonsters[sourcePath] = file;
    }

    /// <summary>
    /// Couple chaque MonsterFile édité avec son chemin source d'origine.
    /// Utilisé par le SaveService pour reconstruire l'arborescence de sortie.
    /// </summary>
    public IEnumerable<(string SourcePath, MonsterFile File)> EditedMonstersWithPaths
    {
        get
        {
            foreach (var kv in _editedMonsters)
                yield return (kv.Key, kv.Value);
        }
    }

    /// <summary>
    /// Charge (avec cache) un fichier de scène de combat par son nom.
    /// Retourne null si le fichier n'a pas été scanné, est introuvable, ou est corrompu.
    /// </summary>
    public BattleFile? LoadBattleFile(string fileName)
    {
        if (_battleFileCache.TryGetValue(fileName, out var cached))
            return cached;

        if (!Scan.BattleScenePaths.TryGetValue(fileName, out var path))
        {
            _battleFileCache[fileName] = null;
            return null;
        }

        try
        {
            var file = BattleFile.ReadFromFile(path);
            _battleFileCache[fileName] = file;
            return file;
        }
        catch
        {
            _battleFileCache[fileName] = null;
            return null;
        }
    }

    public string? PreferredDisplayLanguage { get; private set; }

    public SpiraWorkspace(WorkspaceScanResult scan)
    {
        Scan = scan;
        TryLoadCharsets();
        TryLoadLocalizationDatabases();
        TryLoadAttackFiles();
        TryLoadGearFiles();
        TryLoadWeaponNames();
        TryLoadAbilities();
        TryLoadKeyItems();
        TryLoadCustomizations();
        TryLoadInitialEquipment();
        TryLoadPlayerStartData();
        TryLoadEncounterTables();
        TryLoadTreasures();
        DeterminePreferredLanguage();
    }

    private void TryLoadCharsets()
    {
        if (Scan.EncodingFolderPath == null) return;
        TryLoadCharset("us", charset =>
        {
            UsCharset = charset;
            foreach (var lang in new[] { "fr", "en", "de", "es", "it" })
                CharsetsByLanguage[lang] = charset;
        });
        TryLoadCharset("jp", charset =>
        {
            JpCharset = charset;
            CharsetsByLanguage["jp"] = charset;
        });
        TryLoadCharset("ch", charset => CharsetsByLanguage["ch"] = charset);
        TryLoadCharset("kr", charset => CharsetsByLanguage["kr"] = charset);
    }

    private void TryLoadCharset(string code, Action<FfxCharset> onLoaded)
    {
        if (Scan.EncodingFolderPath == null) return;
        var path = Path.Combine(Scan.EncodingFolderPath, $"ffxsjistbl_{code}.bin");
        if (!File.Exists(path)) return;
        try { onLoaded(FfxCharset.LoadFromFile(path, code)); } catch { }
    }

    private void TryLoadLocalizationDatabases()
    {
        // Les dossiers new_XXpc peuvent contenir command.bin/item.bin/etc. sans
        // contenir monster1/2/3.bin. Dans ce cas, LocalizedKernelDirs pointe bien
        // vers une langue disponible, mais pas vers les textes bestiaire.
        // On charge donc depuis les chemins MonsterNPath déjà fusionnés par le
        // scanner avec le dossier vanilla éventuel.
        foreach (var (lang, kernel) in Scan.KernelByLanguage)
        {
            var charset = GetCharsetForLanguage(lang);
            if (charset == null) continue;

            var monsterPaths = new[]
            {
                kernel.Monster1Path,
                kernel.Monster2Path,
                kernel.Monster3Path,
            }
            .Where(path => !string.IsNullOrEmpty(path) && File.Exists(path))
            .Cast<string>()
            .ToList();

            if (monsterPaths.Count == 0) continue;

            try
            {
                var db = new LocalizationDatabase(lang, charset);
                foreach (var path in monsterPaths)
                    db.AddFile(MonsterLocalizationFile.ReadFromFile(path), path);

                if (db.TotalEntryCount > 0)
                    LocalizationDatabases[lang] = db;
            }
            catch { }
        }
    }

    private void TryLoadAttackFiles()
    {
        // Préfixes des IDs globaux selon la convention du parser de Karifean
        // (DataReadingManager.prepareCommandsFromFile, groupe * 0x1000) :
        //   item.bin → 0x2000, command.bin → 0x3000, monmagic1.bin → 0x4000, monmagic2.bin → 0x6000.
        // Sans ces préfixes, les références cross-file (ex : kaizou.bin référence 0x2035 = un objet)
        // ne pourraient pas être résolues.
        foreach (var (lang, kernel) in Scan.KernelByLanguage)
        {
            // monmagic1 + monmagic2 (deux fichiers différents, deux préfixes différents)
            var mm1 = TryReadAttack(kernel.MonMagic1Path, AttackFileKind.MonsterAttack, 0x4000);
            var mm2 = TryReadAttack(kernel.MonMagic2Path, AttackFileKind.MonsterAttack, 0x6000);
            TrackAttackSource(mm1, kernel.MonMagic1Path);
            TrackAttackSource(mm2, kernel.MonMagic2Path);
            if (mm1 != null || mm2 != null)
                MonsterAttacksByLanguage[lang] = (mm1, mm2);

            // item.bin
            var item = TryReadAttack(kernel.ItemPath, AttackFileKind.Item, 0x2000);
            TrackAttackSource(item, kernel.ItemPath);
            if (item != null)
                ItemsByLanguage[lang] = item;

            // command.bin
            var cmd = TryReadAttack(kernel.CommandPath, AttackFileKind.PlayerCommand, 0x3000);
            TrackAttackSource(cmd, kernel.CommandPath);
            if (cmd != null)
                PlayerCommandsByLanguage[lang] = cmd;
        }
    }

    private void TrackAttackSource(AttackFile? file, string? path)
    {
        if (file == null || string.IsNullOrEmpty(path)) return;
        _attackSourcePaths[file] = path;
    }

    private void TryLoadGearFiles()
    {
        if (Scan.BukiGetFilePath != null)
        {
            try
            {
                BukiGetFile = GearFile.ReadFromFile(Scan.BukiGetFilePath, GearFileKind.BukiGet);
                TrackGearSource(BukiGetFile, Scan.BukiGetFilePath);
            }
            catch { }
        }
        if (Scan.ShopArmsFilePath != null)
        {
            try
            {
                ShopArmsFile = GearFile.ReadFromFile(Scan.ShopArmsFilePath, GearFileKind.ShopArms);
                TrackGearSource(ShopArmsFile, Scan.ShopArmsFilePath);
            }
            catch { }
        }
    }

    private void TrackGearSource(GearFile? file, string? path)
    {
        if (file == null || string.IsNullOrEmpty(path)) return;
        _gearSourcePaths[file] = path;
    }

    private void TryLoadWeaponNames()
    {
        foreach (var (lang, kernel) in Scan.KernelByLanguage)
        {
            if (kernel.WeaponNamePath == null) continue;
            try
            {
                var file = WeaponNameFile.ReadFromFile(kernel.WeaponNamePath);
                if (file.Count > 0)
                {
                    WeaponNamesByLanguage[lang] = file;
                    TrackWeaponNameSource(file, kernel.WeaponNamePath);
                }
            }
            catch { }
        }
    }

    private void TrackWeaponNameSource(WeaponNameFile? file, string? path)
    {
        if (file == null || string.IsNullOrEmpty(path)) return;
        _weaponNameSourcePaths[file] = path;
    }

    private void TryLoadAbilities()
    {
        foreach (var (lang, kernel) in Scan.KernelByLanguage)
        {
            if (kernel.AbilityPath == null) continue;
            try
            {
                var file = AutoAbilityFile.ReadFromFile(kernel.AbilityPath);
                if (file.Count > 0)
                {
                    AbilitiesByLanguage[lang] = file;
                    TrackAbilitySource(file, kernel.AbilityPath);
                }
            }
            catch { }
        }
    }

    private void TrackAbilitySource(AutoAbilityFile? file, string? path)
    {
        if (file == null || string.IsNullOrEmpty(path)) return;
        _abilitySourcePaths[file] = path;
    }

    private void TryLoadKeyItems()
    {
        foreach (var (lang, kernel) in Scan.KernelByLanguage)
        {
            if (kernel.ImportantPath == null) continue;
            try
            {
                var file = KeyItemFile.ReadFromFile(kernel.ImportantPath);
                if (file.Count > 0)
                    KeyItemsByLanguage[lang] = file;
            }
            catch { }
        }
    }

    private void TryLoadCustomizations()
    {
        if (Scan.KaizouFilePath == null) return;
        try
        {
            var file = CustomizationFile.ReadFromFile(Scan.KaizouFilePath);
            if (file.Count > 0)
            {
                GearCustomizations = file;
                _customizationSourcePath = Scan.KaizouFilePath;
            }
        }
        catch { }
    }

    private void TryLoadPlayerStartData()
    {
        foreach (var (lang, kernel) in Scan.KernelByLanguage)
        {
            if (kernel.PlayerSavePath == null) continue;
            try
            {
                var file = PlayerSaveFile.ReadFromFile(kernel.PlayerSavePath);
                if (file.Count > 0)
                {
                    PlayerSaveFilesByLanguage[lang] = file;
                    TrackPlayerSaveSource(file, kernel.PlayerSavePath);
                }
            }
            catch { }
        }

        if (PlayerSaveFilesByLanguage.Count == 0 && Scan.PlayerSaveFilePath != null)
        {
            try
            {
                var file = PlayerSaveFile.ReadFromFile(Scan.PlayerSaveFilePath);
                if (file.Count > 0)
                {
                    PlayerSaveFile = file;
                    TrackPlayerSaveSource(file, Scan.PlayerSaveFilePath);
                }
            }
            catch { }
            return;
        }

        foreach (var lang in new[] { "fr", "en", "de", "es", "it", "ch", "kr", "jp" })
        {
            if (PlayerSaveFilesByLanguage.TryGetValue(lang, out var file))
            {
                PlayerSaveFile = file;
                return;
            }
        }
        PlayerSaveFile = PlayerSaveFilesByLanguage.Values.FirstOrDefault();
    }

    private void TrackPlayerSaveSource(PlayerSaveFile? file, string? path)
    {
        if (file == null || string.IsNullOrEmpty(path)) return;
        _playerSaveSourcePaths[file] = path;
    }

    private void TryLoadInitialEquipment()
    {
        if (Scan.WeaponFilePath == null) return;
        try
        {
            InitialEquipmentFile = GearFile.ReadFromFile(Scan.WeaponFilePath, GearFileKind.Weapon);
            TrackGearSource(InitialEquipmentFile, Scan.WeaponFilePath);
        }
        catch { }
    }

    private void TryLoadEncounterTables()
    {
        if (Scan.BtlFilePath == null) return;
        try
        {
            var file = EncounterTableFile.ReadFromFile(Scan.BtlFilePath);
            if (file.Count > 0) EncounterTables = file;
        }
        catch { }
    }

    private void TryLoadTreasures()
    {
        if (Scan.TakaraFilePath == null) return;
        try
        {
            var file = TreasureFile.ReadFromFile(Scan.TakaraFilePath);
            if (file.Count > 0)
            {
                TreasureFile = file;
                _treasureSourcePath = Scan.TakaraFilePath;
            }
        }
        catch { }
    }

    public IReadOnlyList<MapTreasureUsage> GetTreasureUsages()
    {
        _treasureUsages ??= MapTreasureUsageScanner.Scan(Scan.EventScriptPaths);
        return _treasureUsages;
    }

    /// <summary>
    /// Cherche le nom d'une aptitude (résout les bits de flag automatiquement).
    /// </summary>
    public string? LookupAbilityName(int abilityGlobalId, string language)
    {
        if (!AbilitiesByLanguage.TryGetValue(language, out var file)) return null;
        var charset = GetCharsetForLanguage(language);
        return file.GetNameByGlobalId(abilityGlobalId, charset);
    }

    /// <summary>
    /// Cherche le nom d'une arme dans w_name.bin pour la langue spécifiée.
    /// </summary>
    /// <param name="globalGearIndex">Index global de l'équipement (depuis buki_get/shop_arms).</param>
    /// <param name="characterId">ID du personnage humain (0..6 — Tidus à Rikku).
    /// Pour les Chimères et Seymour (id 7..15), retourne null.</param>
    /// <param name="language">Code de langue.</param>
    public string? LookupWeaponName(int globalGearIndex, int characterId, string language)
    {
        if (characterId < 0 || characterId >= WeaponNameEntry.CHARACTER_COUNT) return null;
        if (!WeaponNamesByLanguage.TryGetValue(language, out var file)) return null;
        var charset = GetCharsetForLanguage(language);
        return file.GetWeaponName(globalGearIndex, characterId, charset);
    }

    /// <summary>
    /// Calcule la distribution des entrées d'équipement par personnage.
    /// Utile pour diagnostiquer pourquoi une chimère ou Seymour n'a pas d'entrées :
    /// dans FFX vanilla, seuls les 7 persos jouables principaux ont du gear ici.
    /// </summary>
    /// <returns>Dictionnaire (CharacterId → (countBuki, countShop)).</returns>
    public Dictionary<int, (int Buki, int Shop)> GetGearDistributionByCharacter()
    {
        var dist = new Dictionary<int, (int Buki, int Shop)>();

        if (BukiGetFile != null)
        {
            foreach (var gear in BukiGetFile.Entries)
            {
                var (b, s) = dist.GetValueOrDefault(gear.Character);
                dist[gear.Character] = (b + 1, s);
            }
        }
        if (ShopArmsFile != null)
        {
            foreach (var gear in ShopArmsFile.Entries)
            {
                var (b, s) = dist.GetValueOrDefault(gear.Character);
                dist[gear.Character] = (b, s + 1);
            }
        }
        return dist;
    }

    private static AttackFile? TryReadAttack(string? path, AttackFileKind kind, int globalIdPrefix = 0)
    {
        if (path == null) return null;
        try { return AttackFile.ReadFromFile(path, kind, globalIdPrefix); }
        catch { return null; }
    }

    private void DeterminePreferredLanguage()
    {
        var order = new[] { "fr", "en", "de", "es", "it", "ch", "kr", "jp" };
        foreach (var lang in order)
        {
            if (LocalizationDatabases.ContainsKey(lang)
                || MonsterAttacksByLanguage.ContainsKey(lang)
                || PlayerCommandsByLanguage.ContainsKey(lang)
                || ItemsByLanguage.ContainsKey(lang)
                || WeaponNamesByLanguage.ContainsKey(lang)
                || AbilitiesByLanguage.ContainsKey(lang)
                || KeyItemsByLanguage.ContainsKey(lang)
                || PlayerSaveFilesByLanguage.ContainsKey(lang))
            {
                PreferredDisplayLanguage = lang;
                return;
            }
        }
    }

    public PlayerSaveFile? GetPlayerSaveFile(string? languageCode)
    {
        if (languageCode != null && PlayerSaveFilesByLanguage.TryGetValue(languageCode, out var file))
            return file;
        if (PreferredDisplayLanguage != null
            && PlayerSaveFilesByLanguage.TryGetValue(PreferredDisplayLanguage, out file))
            return file;
        return PlayerSaveFile ?? PlayerSaveFilesByLanguage.Values.FirstOrDefault();
    }

    public string? GetLanguageForPlayerSaveFile(PlayerSaveFile file)
    {
        foreach (var (lang, candidate) in PlayerSaveFilesByLanguage)
            if (ReferenceEquals(candidate, file))
                return lang;
        return null;
    }

    public string? LookupKeyItemName(int globalId, string language)
    {
        if (!KeyItemsByLanguage.TryGetValue(language, out var file))
            return null;
        var charset = GetCharsetForLanguage(language);
        return file.GetNameByGlobalId(globalId, charset);
    }

    public FfxCharset? GetCharsetForLanguage(string languageCode)
    {
        if (CharsetsByLanguage.TryGetValue(languageCode, out var charset))
            return charset;
        if (languageCode == "jp") return JpCharset ?? UsCharset;
        if (languageCode == "ch" || languageCode == "kr") return UsCharset ?? JpCharset;
        return UsCharset ?? JpCharset ?? CharsetsByLanguage.Values.FirstOrDefault();
    }

    public FfxCharset? DefaultCharset => UsCharset ?? JpCharset ?? CharsetsByLanguage.Values.FirstOrDefault();

    public string? GetMonsterName(int globalMonsterIndex)
    {
        if (PreferredDisplayLanguage == null) return null;
        return LocalizationDatabases.TryGetValue(PreferredDisplayLanguage, out var db)
            ? db.GetNameForMonsterIndex(globalMonsterIndex)
            : null;
    }

    public LocalizedMonsterTexts? GetMonsterTexts(int globalMonsterIndex, string language)
    {
        return LocalizationDatabases.TryGetValue(language, out var db)
            ? db.GetTextsForMonsterIndex(globalMonsterIndex)
            : null;
    }

    /// <summary>
    /// Cherche le nom d'une commande/attaque par son ID global.
    /// Les monstres référencent les attaques par ID global (par exemple 0x409E pour
    /// monmagic1, 0x6000+ pour monmagic2, 0x3000+ pour command, 0x2000+ pour item).
    /// On parcourt tous les fichiers chargés et on retourne le premier match.
    /// </summary>
    public string? LookupCommandName(int globalId, string language)
    {
        var charset = GetCharsetForLanguage(language);
        if (charset == null) return null;

        // Liste des fichiers à interroger (dans l'ordre le plus probable pour les monstres)
        var candidates = new List<AttackFile?>();
        if (MonsterAttacksByLanguage.TryGetValue(language, out var monPair))
        {
            candidates.Add(monPair.MonMagic1);
            candidates.Add(monPair.MonMagic2);
        }
        if (PlayerCommandsByLanguage.TryGetValue(language, out var cmd)) candidates.Add(cmd);
        if (ItemsByLanguage.TryGetValue(language, out var item))         candidates.Add(item);

        foreach (var file in candidates)
        {
            if (file == null) continue;
            if (globalId < file.MinIndex || globalId > file.MaxIndex) continue;
            var name = file.GetName(globalId - file.MinIndex, charset);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        return null;
    }

    /// <summary>
    /// Identifie l'origine d'une commande par son ID (utilité : afficher "monmagic1" / "item" / etc.).
    /// </summary>
    public string? LookupCommandSource(int globalId)
    {
        // On regarde n'importe quelle langue (toutes ont les mêmes ranges)
        var anyLang = AvailableLanguages.FirstOrDefault();
        if (anyLang == null) return null;

        if (MonsterAttacksByLanguage.TryGetValue(anyLang, out var monPair))
        {
            if (monPair.MonMagic1 != null && globalId >= monPair.MonMagic1.MinIndex && globalId <= monPair.MonMagic1.MaxIndex)
                return "monmagic1";
            if (monPair.MonMagic2 != null && globalId >= monPair.MonMagic2.MinIndex && globalId <= monPair.MonMagic2.MaxIndex)
                return "monmagic2";
        }
        if (PlayerCommandsByLanguage.TryGetValue(anyLang, out var cmd)
            && globalId >= cmd.MinIndex && globalId <= cmd.MaxIndex)
            return "command";
        if (ItemsByLanguage.TryGetValue(anyLang, out var item)
            && globalId >= item.MinIndex && globalId <= item.MaxIndex)
            return "item";
        return null;
    }

    /// <summary>
    /// Liste TOUTES les langues disponibles (qui ont au moins une donnée chargée :
    /// localisation de monstres, attaques, ou objets).
    /// </summary>
    public IEnumerable<string> AvailableLanguages
    {
        get
        {
            var order = new[] { "fr", "en", "de", "es", "it", "ch", "kr", "jp" };
            return order.Where(lang =>
                LocalizationDatabases.ContainsKey(lang)
                || MonsterAttacksByLanguage.ContainsKey(lang)
                || PlayerCommandsByLanguage.ContainsKey(lang)
                || ItemsByLanguage.ContainsKey(lang)
                || WeaponNamesByLanguage.ContainsKey(lang)
                || AbilitiesByLanguage.ContainsKey(lang)
                || KeyItemsByLanguage.ContainsKey(lang)
                || PlayerSaveFilesByLanguage.ContainsKey(lang));
        }
    }
}
