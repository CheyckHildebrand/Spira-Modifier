using SpiraModifier.Core.Text;

namespace SpiraModifier.Core.Models;

public class LocalizationDatabase
{
    public string Language { get; }
    public FfxCharset? Charset { get; }

    private readonly List<MonsterLocalizationFile> _files = new();

    /// <summary>Chemins absolus des fichiers source (monster1.bin, monster2.bin, monster3.bin), indexés dans le même ordre que <see cref="Files"/>.</summary>
    private readonly List<string> _sourcePaths = new();

    public int FileCount => _files.Count;
    public int TotalEntryCount => _files.Sum(f => f.EntryCount);

    /// <summary>Tous les fichiers de localisation chargés, dans l'ordre.</summary>
    public IReadOnlyList<MonsterLocalizationFile> Files => _files;

    /// <summary>Couplage fichier ↔ chemin source pour la sauvegarde.</summary>
    public IEnumerable<(MonsterLocalizationFile File, string SourcePath)> FilesWithPaths
    {
        get
        {
            for (int i = 0; i < _files.Count; i++)
                yield return (_files[i], _sourcePaths[i]);
        }
    }

    /// <summary>True si au moins un fichier a été modifié.</summary>
    public bool HasUnsavedChanges => _files.Any(f => f.IsDirty);

    public LocalizationDatabase(string language, FfxCharset? charset)
    {
        Language = language;
        Charset = charset;
    }

    public void AddFile(MonsterLocalizationFile file, string sourcePath)
    {
        _files.Add(file);
        _sourcePaths.Add(sourcePath);
    }

    // Surcharge sans path pour compat
    public void AddFile(MonsterLocalizationFile file) => AddFile(file, "");

    /// <summary>
    /// Trouve le fichier qui contient un monstre donné et son chemin source.
    /// Retourne null si aucun fichier ne couvre cet index.
    /// </summary>
    public (MonsterLocalizationFile File, string SourcePath, int RelativeIndex)? FindFileForMonster(int globalMonsterIndex)
    {
        for (int i = 0; i < _files.Count; i++)
        {
            if (_files[i].ContainsMonsterIndex(globalMonsterIndex))
                return (_files[i], _sourcePaths[i], globalMonsterIndex - _files[i].MinIndex);
        }
        return null;
    }

    /// <summary>Lookup d'un nom par ID global de monstre.</summary>
    public string? GetNameForMonsterIndex(int globalMonsterIndex) =>
        GetTextsForMonsterIndex(globalMonsterIndex)?.Name;

    /// <summary>Lookup complet (Name + Sensor + Scan) par ID global de monstre.</summary>
    public LocalizedMonsterTexts? GetTextsForMonsterIndex(int globalMonsterIndex)
    {
        foreach (var file in _files)
        {
            if (file.ContainsMonsterIndex(globalMonsterIndex))
            {
                var relativeIndex = globalMonsterIndex - file.MinIndex;
                return file.GetTexts(relativeIndex, Charset);
            }
        }
        return null;
    }

    public static LocalizationDatabase BuildFromKernel(
        string language, FfxCharset? charset, string kernelDir)
    {
        var db = new LocalizationDatabase(language, charset);
        for (int i = 1; i <= 3; i++)
        {
            var path = Path.Combine(kernelDir, $"monster{i}.bin");
            if (!File.Exists(path)) continue;
            try { db.AddFile(MonsterLocalizationFile.ReadFromFile(path), path); }
            catch { }
        }
        return db;
    }
}
