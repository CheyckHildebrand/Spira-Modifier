using SpiraModifier.Core.Models;
using SpiraModifier.Core.Workspace;

namespace SpiraModifier.Core.Saving;

/// <summary>
/// Résultat d'une opération de sauvegarde.
/// </summary>
public class SaveReport
{
    /// <summary>Fichiers écrits avec succès, par chemin absolu de destination.</summary>
    public List<string> WrittenFiles { get; } = new();

    /// <summary>Erreurs rencontrées par fichier (chemin → message).</summary>
    public List<(string SourcePath, string Error)> Errors { get; } = new();

    public bool Success => Errors.Count == 0;
    public int WrittenCount => WrittenFiles.Count;
}

/// <summary>
/// Service de sauvegarde des modifications du workspace vers un dossier de sortie.
///
/// Principe : la sauvegarde reproduit l'arborescence relative du dossier source.
/// Si le workspace est ouvert depuis <c>D:\jeux\FFX\</c> et que l'output est
/// <c>D:\modded\</c>, alors un m155.bin modifié à <c>D:\jeux\FFX\battle\mon\m155.bin</c>
/// est écrit dans <c>D:\modded\battle\mon\m155.bin</c>. Le joueur peut ensuite
/// copier le contenu de <c>D:\modded\</c> par-dessus son install pour appliquer le mod.
///
/// On ne touche JAMAIS aux fichiers originaux : la sauvegarde est strictement
/// additive dans le dossier de sortie.
/// </summary>
public static class SaveService
{
    private static readonly HashSet<string> LanguageFolderSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "frpc", "enpc", "uspc", "depc", "espc", "itpc", "jppc"
    };

    /// <summary>
    /// Écrit toutes les modifications en attente : MonsterFiles individuels
    /// + fichiers de localisation monsterN.bin par langue.
    /// </summary>
    public static SaveReport SaveAll(SpiraWorkspace workspace, string outputFolder)
    {
        var report = new SaveReport();
        if (workspace == null) return report;

        Directory.CreateDirectory(outputFolder);

        SaveDirtyMonsters(workspace, outputFolder, report);
        SaveDirtyLocalizations(workspace, outputFolder, report);
        SaveDirtyAttackFiles(workspace, outputFolder, report);
        SaveDirtyGearFiles(workspace, outputFolder, report);
        SaveDirtyWeaponNameFiles(workspace, outputFolder, report);
        SaveDirtyAbilityFiles(workspace, outputFolder, report);
        SaveDirtyCustomizationFiles(workspace, outputFolder, report);
        SaveDirtyPlayerSaveFiles(workspace, outputFolder, report);
        SaveDirtyTreasureFiles(workspace, outputFolder, report);
        SaveDirtyEncounterTableFiles(workspace, outputFolder, report);
        SaveDirtyBattleSceneFiles(workspace, outputFolder, report);

        return report;
    }

    /// <summary>
    /// Écrit dans <paramref name="outputFolder"/> tous les <see cref="MonsterFile"/>
    /// du workspace dont <see cref="MonsterFile.IsDirty"/> est true.
    /// </summary>
    public static SaveReport SaveDirtyMonsters(SpiraWorkspace workspace, string outputFolder)
    {
        var report = new SaveReport();
        Directory.CreateDirectory(outputFolder);
        SaveDirtyMonsters(workspace, outputFolder, report);
        return report;
    }

    private static void SaveDirtyMonsters(SpiraWorkspace workspace, string outputFolder, SaveReport report)
    {
        foreach (var (sourcePath, monster) in workspace.EditedMonstersWithPaths)
        {
            if (!monster.IsDirty) continue;

            try
            {
                var destPath = ComputeOutputPath(workspace.Scan.RootPath, sourcePath, outputFolder);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                var bytes = monster.Write();
                File.WriteAllBytes(destPath, bytes);
                report.WrittenFiles.Add(destPath);
                monster.IsDirty = false;
            }
            catch (Exception ex)
            {
                report.Errors.Add((sourcePath, ex.Message));
            }
        }
    }

    /// <summary>
    /// Écrit tous les fichiers monsterN.bin modifiés (textes localisés des monstres).
    /// </summary>
    private static void SaveDirtyLocalizations(SpiraWorkspace workspace, string outputFolder, SaveReport report)
    {
        foreach (var db in workspace.LocalizationDatabases.Values)
        {
            foreach (var (file, sourcePath) in db.FilesWithPaths)
            {
                if (!file.IsDirty || string.IsNullOrEmpty(sourcePath)) continue;
                try
                {
                    var destPath = ComputeOutputPath(workspace.Scan.RootPath, sourcePath, outputFolder);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    var bytes = file.WriteToBytes();
                    File.WriteAllBytes(destPath, bytes);
                    report.WrittenFiles.Add(destPath);
                    file.MarkClean();
                }
                catch (Exception ex)
                {
                    report.Errors.Add((sourcePath, ex.Message));
                }
            }
        }
    }

    private static void SaveDirtyAttackFiles(SpiraWorkspace workspace, string outputFolder, SaveReport report)
    {
        foreach (var (sourcePath, file) in workspace.DirtyAttackFilesWithPaths)
        {
            if (string.IsNullOrEmpty(sourcePath)) continue;
            try
            {
                var destPath = ComputeOutputPath(workspace.Scan.RootPath, sourcePath, outputFolder);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                var bytes = file.WriteToBytes();
                File.WriteAllBytes(destPath, bytes);
                report.WrittenFiles.Add(destPath);
                file.MarkClean();
            }
            catch (Exception ex)
            {
                report.Errors.Add((sourcePath, ex.Message));
            }
        }
    }

    private static void SaveDirtyGearFiles(SpiraWorkspace workspace, string outputFolder, SaveReport report)
    {
        foreach (var (sourcePath, file) in workspace.DirtyGearFilesWithPaths)
        {
            if (string.IsNullOrEmpty(sourcePath)) continue;
            try
            {
                var destPath = ComputeOutputPath(workspace.Scan.RootPath, sourcePath, outputFolder);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                var bytes = file.WriteToBytes();
                File.WriteAllBytes(destPath, bytes);
                report.WrittenFiles.Add(destPath);
                file.MarkClean();
            }
            catch (Exception ex)
            {
                report.Errors.Add((sourcePath, ex.Message));
            }
        }
    }

    private static void SaveDirtyWeaponNameFiles(SpiraWorkspace workspace, string outputFolder, SaveReport report)
    {
        foreach (var (sourcePath, file) in workspace.DirtyWeaponNameFilesWithPaths)
        {
            if (string.IsNullOrEmpty(sourcePath)) continue;
            try
            {
                var destPath = ComputeOutputPath(workspace.Scan.RootPath, sourcePath, outputFolder);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                var bytes = file.WriteToBytes();
                File.WriteAllBytes(destPath, bytes);
                report.WrittenFiles.Add(destPath);
                file.MarkClean();
            }
            catch (Exception ex)
            {
                report.Errors.Add((sourcePath, ex.Message));
            }
        }
    }

    private static void SaveDirtyAbilityFiles(SpiraWorkspace workspace, string outputFolder, SaveReport report)
    {
        foreach (var (sourcePath, file) in workspace.DirtyAbilityFilesWithPaths)
        {
            if (string.IsNullOrEmpty(sourcePath)) continue;
            try
            {
                var destPath = ComputeOutputPath(workspace.Scan.RootPath, sourcePath, outputFolder);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                var bytes = file.WriteToBytes();
                File.WriteAllBytes(destPath, bytes);
                report.WrittenFiles.Add(destPath);
                file.MarkClean();
            }
            catch (Exception ex)
            {
                report.Errors.Add((sourcePath, ex.Message));
            }
        }
    }

    private static void SaveDirtyCustomizationFiles(SpiraWorkspace workspace, string outputFolder, SaveReport report)
    {
        foreach (var (sourcePath, file) in workspace.DirtyCustomizationFilesWithPaths)
        {
            if (string.IsNullOrEmpty(sourcePath)) continue;
            try
            {
                var destPath = ComputeOutputPath(workspace.Scan.RootPath, sourcePath, outputFolder);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                var bytes = file.WriteToBytes();
                File.WriteAllBytes(destPath, bytes);
                report.WrittenFiles.Add(destPath);
                file.MarkClean();
            }
            catch (Exception ex)
            {
                report.Errors.Add((sourcePath, ex.Message));
            }
        }
    }

    private static void SaveDirtyPlayerSaveFiles(SpiraWorkspace workspace, string outputFolder, SaveReport report)
    {
        foreach (var (sourcePath, file) in workspace.DirtyPlayerSaveFilesWithPaths)
        {
            if (string.IsNullOrEmpty(sourcePath)) continue;
            try
            {
                var destPath = ComputeOutputPath(workspace.Scan.RootPath, sourcePath, outputFolder);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                var bytes = file.WriteToBytes();
                File.WriteAllBytes(destPath, bytes);
                report.WrittenFiles.Add(destPath);
                file.MarkClean();
            }
            catch (Exception ex)
            {
                report.Errors.Add((sourcePath, ex.Message));
            }
        }
    }

    private static void SaveDirtyBattleSceneFiles(SpiraWorkspace workspace, string outputFolder, SaveReport report)
    {
        foreach (var (sourcePath, file) in workspace.DirtyBattleSceneFilesWithPaths)
        {
            if (string.IsNullOrEmpty(sourcePath)) continue;
            try
            {
                var destPath = ComputeOutputPath(workspace.Scan.RootPath, sourcePath, outputFolder);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                var bytes = file.WriteToBytes();
                File.WriteAllBytes(destPath, bytes);
                report.WrittenFiles.Add(destPath);
                file.MarkClean();
            }
            catch (Exception ex)
            {
                report.Errors.Add((sourcePath, ex.Message));
            }
        }
    }

    private static void SaveDirtyTreasureFiles(SpiraWorkspace workspace, string outputFolder, SaveReport report)
    {
        foreach (var (sourcePath, file) in workspace.DirtyTreasureFilesWithPaths)
        {
            if (string.IsNullOrEmpty(sourcePath)) continue;
            try
            {
                var destPath = ComputeOutputPath(workspace.Scan.RootPath, sourcePath, outputFolder);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                var bytes = file.WriteToBytes();
                File.WriteAllBytes(destPath, bytes);
                report.WrittenFiles.Add(destPath);
                file.MarkClean();
            }
            catch (Exception ex)
            {
                report.Errors.Add((sourcePath, ex.Message));
            }
        }
    }

    private static void SaveDirtyEncounterTableFiles(SpiraWorkspace workspace, string outputFolder, SaveReport report)
    {
        foreach (var (sourcePath, file) in workspace.DirtyEncounterTableFilesWithPaths)
        {
            if (string.IsNullOrEmpty(sourcePath)) continue;
            try
            {
                var destPath = ComputeOutputPath(workspace.Scan.RootPath, sourcePath, outputFolder);
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                var bytes = file.WriteToBytes();
                File.WriteAllBytes(destPath, bytes);
                report.WrittenFiles.Add(destPath);
                file.MarkClean();
            }
            catch (Exception ex)
            {
                report.Errors.Add((sourcePath, ex.Message));
            }
        }
    }

    /// <summary>
    /// Calcule le chemin de sortie d'un fichier source en miroirant son chemin
    /// relatif au workspace.
    /// </summary>
    private static string ComputeOutputPath(string workspaceRoot, string sourcePath, string outputFolder)
    {
        var fullRoot = Path.GetFullPath(workspaceRoot);
        var fullSource = Path.GetFullPath(sourcePath);

        var ffxMasterRelative = TryGetFfxMasterRelative(fullSource);
        if (ffxMasterRelative != null)
            return Path.Combine(outputFolder, AdjustFfxMasterRelativeForOutputRoot(outputFolder, ffxMasterRelative));

        var masterRelative = TryGetRelativeFromPathSegment(fullSource,
            segment => string.Equals(segment, "master", StringComparison.OrdinalIgnoreCase));
        if (masterRelative != null)
            return Path.Combine(outputFolder, AdjustMasterRelativeForOutputRoot(outputFolder, masterRelative));

        var relative = Path.GetRelativePath(fullRoot, fullSource);
        if (IsSafeRelativePath(relative))
        {
            var rootName = Path.GetFileName(fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (IsLanguageFolderName(rootName))
                relative = Path.Combine(rootName, relative);

            return Path.Combine(outputFolder, relative);
        }

        // Fichiers localisés venant d'un dossier frère ou du dossier vanilla :
        // on repart du dossier de langue pour produire, par exemple,
        // output\master\new_enpc\battle\kernel\command.bin au lieu de sortir du dossier output.
        relative = TryGetRelativeFromPathSegment(fullSource, IsLanguageFolderName);
        if (relative != null)
            return Path.Combine(outputFolder, relative);

        // Fallback pour les fichiers mécaniques non localisés chargés depuis une référence
        // vanilla (buki_get.bin, kaizou.bin, btl.bin, etc.).
        relative = TryGetRelativeFromPathSegment(fullSource,
            segment => string.Equals(segment, "battle", StringComparison.OrdinalIgnoreCase));
        if (relative != null)
            return Path.Combine(outputFolder, relative);

        return Path.Combine(outputFolder, Path.GetFileName(fullSource));
    }

    private static string? TryGetFfxMasterRelative(string fullPath)
    {
        var parts = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();

        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (string.Equals(parts[i], "ffx", StringComparison.OrdinalIgnoreCase)
                && string.Equals(parts[i + 1], "master", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(parts[i..]);
        }

        return null;
    }

    private static string AdjustFfxMasterRelativeForOutputRoot(string outputFolder, string relativePath)
    {
        var outputName = GetLastPathSegment(outputFolder);
        if (string.Equals(outputName, "master", StringComparison.OrdinalIgnoreCase))
            return DropLeadingPathSegments(relativePath, 2);
        if (string.Equals(outputName, "ffx", StringComparison.OrdinalIgnoreCase))
            return DropLeadingPathSegments(relativePath, 1);
        return relativePath;
    }

    private static string AdjustMasterRelativeForOutputRoot(string outputFolder, string relativePath)
    {
        var outputName = GetLastPathSegment(outputFolder);
        if (string.Equals(outputName, "master", StringComparison.OrdinalIgnoreCase))
            return DropLeadingPathSegments(relativePath, 1);
        return relativePath;
    }

    private static string GetLastPathSegment(string path)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(fullPath);
    }

    private static string DropLeadingPathSegments(string path, int count)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Skip(count)
            .ToArray();
        return parts.Length == 0 ? "" : Path.Combine(parts);
    }

    private static bool IsSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return true;
        if (Path.IsPathRooted(relativePath)) return false;

        var firstSegment = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .FirstOrDefault();
        return firstSegment != "..";
    }

    private static string? TryGetRelativeFromPathSegment(string fullPath, Func<string, bool> isAnchorSegment)
    {
        var parts = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToArray();

        for (int i = 0; i < parts.Length; i++)
        {
            if (!isAnchorSegment(parts[i])) continue;
            return Path.Combine(parts[i..]);
        }

        return null;
    }

    private static bool IsLanguageFolderName(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return false;

        var normalized = folderName;
        if (normalized.StartsWith("new_", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[4..];

        return LanguageFolderSuffixes.Contains(normalized);
    }
}
