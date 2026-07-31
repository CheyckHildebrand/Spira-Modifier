using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SpiraModifier.Core.Models;
using SpiraModifier.Core.Saving;
using SpiraModifier.Core.Settings;
using SpiraModifier.Core.Text;
using SpiraModifier.Core.Workspace;

namespace SpiraModifier.App;

public partial class MainWindow : Window
{
    private SpiraWorkspace? _workspace;
    private readonly ObservableCollection<MonsterListItem> _monsterListItems = new();
    private List<MonsterListItem> _allMonsterItems = new();

    /// <summary>Settings persistants (chargés au démarrage, sauvés à chaque modif).</summary>
    private AppSettings _settings = new();

    /// <summary>Langue actuellement sélectionnée pour l'affichage des noms et textes.</summary>
    private string? _currentLanguage;

    /// <summary>Évite que les SelectionChanged déclenchent un refresh pendant qu'on peuple le ComboBox.</summary>
    private bool _suppressLanguageEvents;
    private bool _suppressInterfaceLanguageEvents;
    private bool _applyingInterfaceLanguage;
    private bool _suppressMonsterAiSelectionEvents;
    private MonsterListItem? _currentMonsterAiItem;
    private MonsterFile? _currentMonsterAiFile;
    private AtelDecompiledScript? _currentMonsterAiScript;
    private string _currentMonsterAiAnalysisText = "";
    private bool _suppressAtelLlmSettingsEvents;
    private AtelGlobalIndex? _atelGlobalIndex;
    private bool _isBuildingAtelGlobalIndex;

    private sealed record AtelIndexProgress(int Current, int Total, string FileName);

    /// <summary>Dossier actuellement ouvert (pour pouvoir relancer le scan après config change).</summary>
    private string? _currentRootFolder;

    // ============================================================
    // ÉTAT D'ÉDITION DES TEXTES MONSTRES
    // ============================================================

    /// <summary>Item monstre actuellement sélectionné (pour les handlers d'édition).</summary>
    private MonsterListItem? _currentMonsterItem;

    /// <summary>MonsterFile du monstre courant — sa StatSheet.MonsterIdx donne l'index global.</summary>
    private MonsterFile? _currentMonsterFile;

    /// <summary>True quand on est en train de peupler les TextBoxes — empêche les TextChanged de marquer dirty.</summary>
    private bool _suppressMonsterTextEvents;

    /// <summary>True quand on peuple les contrôles de stats — empêche les TextChanged/Checked de marquer dirty.</summary>
    private bool _suppressMonsterStatEvents;

    /// <summary>Évite que l'affichage d'une attaque marque les TextBoxes comme modifiées.</summary>
    private bool _suppressAttackTextEvents;

    /// <summary>Évite que l'affichage d'une attaque marque les contrôles mécaniques comme modifiés.</summary>
    private bool _suppressAttackMechanicEvents;

    /// <summary>Évite que l'affichage d'une commande marque les TextBoxes comme modifiées.</summary>
    private bool _suppressCommandTextEvents;

    /// <summary>Évite que l'affichage d'une commande marque les contrôles mécaniques comme modifiés.</summary>
    private bool _suppressCommandMechanicEvents;

    /// <summary>Évite que l'affichage d'un objet marque les TextBoxes comme modifiées.</summary>
    private bool _suppressItemTextEvents;

    /// <summary>Évite que l'affichage d'un objet marque les contrôles mécaniques comme modifiés.</summary>
    private bool _suppressItemMechanicEvents;

    /// <summary>Évite que l'affichage d'une aptitude d'équipement marque le formulaire comme modifié.</summary>
    private bool _suppressAbilityEvents;

    /// <summary>Évite que l'affichage des données de départ marque le formulaire comme modifié.</summary>
    private bool _suppressPlayerStartEvents;

    private bool _lastAttackMechanicsApplySucceeded;
    private bool _lastCommandMechanicsApplySucceeded;
    private bool _lastItemMechanicsApplySucceeded;
    private bool _lastMonsterStatsApplySucceeded;
    private AttackData? _copiedAttackMechanics;
    private AttackData? _copiedCommandMechanics;
    private AttackData? _copiedItemMechanics;
    private MonsterMechanicsSnapshot? _copiedMonsterMechanics;

    private sealed record MonsterMechanicsSnapshot(MonsterStat Stat, MonsterLoot? Loot);

    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsService.Load();
        InitializeInterfaceLanguageSelector();
        InitializeAtelLlmSettingsUi();
        RefreshAtelIndexStatus();
        MonsterListBox.ItemsSource = _monsterListItems;
        MonsterListBox.DisplayMemberPath = nameof(MonsterListItem.DisplayName);
        MonsterAiMonsterSelector.ItemsSource = _monsterListItems;
        MonsterAiMonsterSelector.DisplayMemberPath = nameof(MonsterListItem.DisplayName);
        LayoutUpdated += OnWindowLayoutUpdated;
    }

    private void InitializeInterfaceLanguageSelector()
    {
        _suppressInterfaceLanguageEvents = true;
        try
        {
            var language = _settings.InterfaceLanguage == UiLocalization.English
                ? UiLocalization.English
                : UiLocalization.French;

            UiLocalization.SetLanguage(language);
            foreach (var item in InterfaceLanguageSelector.Items.OfType<ComboBoxItem>())
            {
                if (Equals(item.Tag, language))
                {
                    InterfaceLanguageSelector.SelectedItem = item;
                    break;
                }
            }

            ApplyInterfaceLanguage();
        }
        finally
        {
            _suppressInterfaceLanguageEvents = false;
        }
    }

    private void OnInterfaceLanguageSelector_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressInterfaceLanguageEvents ||
            InterfaceLanguageSelector.SelectedItem is not ComboBoxItem { Tag: string language })
            return;

        UiLocalization.SetLanguage(language);
        _settings.InterfaceLanguage = UiLocalization.CurrentLanguage;
        SettingsService.Save(_settings);
        ApplyInterfaceLanguage();
    }

    private void OnWindowLayoutUpdated(object? sender, EventArgs e)
    {
        if (UiLocalization.IsEnglish)
            ApplyInterfaceLanguage();
    }

    private void ApplyInterfaceLanguage()
    {
        if (_applyingInterfaceLanguage) return;

        try
        {
            _applyingInterfaceLanguage = true;
            UiLocalization.Apply(this);
            if (!string.IsNullOrEmpty(_currentMonsterAiAnalysisText))
                UiLocalization.SetLocalizedReadOnlyTextBox(MonsterAiAnalysisBox, _currentMonsterAiAnalysisText);
            else
                UiLocalization.ApplyReadOnlyTextBox(MonsterAiAnalysisBox);
            UiLocalization.ApplyReadOnlyTextBox(MonsterAiCopilotOutputBox);
        }
        finally
        {
            _applyingInterfaceLanguage = false;
        }
    }

    private void InitializeAtelLlmSettingsUi()
    {
        _suppressAtelLlmSettingsEvents = true;
        try
        {
            MonsterAiUseLlmCheckBox.IsChecked = _settings.AtelCopilotUseLlm;
            MonsterAiLlmEndpointBox.Text = string.IsNullOrWhiteSpace(_settings.AtelCopilotEndpoint)
                ? "http://localhost:1234/v1/chat/completions"
                : _settings.AtelCopilotEndpoint;
            MonsterAiLlmModelBox.Text = _settings.AtelCopilotModel ?? "";
            MonsterAiLlmApiKeyBox.Password = "";
            RefreshAtelLlmStatus();
        }
        finally
        {
            _suppressAtelLlmSettingsEvents = false;
        }
    }

    private void OnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = UiLocalization.Translate("Sélectionne le dossier racine de l'extraction VBF (généralement nommé ffx_ps2)")
        };
        if (dialog.ShowDialog() != true) return;
        OpenFolder(dialog.FolderName);
    }

    /// <summary>Charge un dossier en utilisant la config actuelle (vanilla / encoding).</summary>
    private void OpenFolder(string rootPath)
    {
        try
        {
            _currentRootFolder = rootPath;
            var scan = WorkspaceScanner.Scan(
                rootPath,
                vanillaReferenceFolder: _settings.VanillaReferenceFolder,
                externalEncodingFolder: _settings.ExternalEncodingFolder);
            _workspace = new SpiraWorkspace(scan);
            ApplyScanResult(_workspace);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Erreur lors du scan :\n\n{ex.Message}", "Spira Modifier",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnConfigureVanillaFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = UiLocalization.Translate("Sélectionne le dossier vanilla (typiquement ffx_ps2/ d'une copie non modifiée)")
        };
        if (dialog.ShowDialog() != true) return;

        _settings.VanillaReferenceFolder = dialog.FolderName;
        SettingsService.Save(_settings);

        MessageBox.Show(this,
            $"Dossier vanilla configuré :\n{dialog.FolderName}\n\n" +
            "Il sera utilisé en fallback pour tous les fichiers manquants.\n" +
            "Configuration sauvegardée — elle sera réutilisée à chaque ouverture.",
            "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Information);

        // Si un dossier est déjà ouvert, on le re-scanne avec la nouvelle config
        if (_currentRootFolder != null)
            OpenFolder(_currentRootFolder);
    }

    private void OnConfigureEncodingFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = UiLocalization.Translate("Sélectionne le dossier ffx_encoding/ (contient ffxsjistbl_us.bin et ffxsjistbl_jp.bin)")
        };
        if (dialog.ShowDialog() != true) return;

        // Vérification basique : on s'assure qu'au moins un charset est dedans
        var hasUs = File.Exists(Path.Combine(dialog.FolderName, "ffxsjistbl_us.bin"));
        var hasJp = File.Exists(Path.Combine(dialog.FolderName, "ffxsjistbl_jp.bin"));
        if (!hasUs && !hasJp)
        {
            var confirm = MessageBox.Show(this,
                "Ce dossier ne contient pas ffxsjistbl_us.bin ni ffxsjistbl_jp.bin.\n" +
                "Sauvegarder quand même ?",
                "Spira Modifier", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }

        _settings.ExternalEncodingFolder = dialog.FolderName;
        SettingsService.Save(_settings);

        if (_currentRootFolder != null)
            OpenFolder(_currentRootFolder);
    }

    private void OnClearReferenceConfig_Click(object sender, RoutedEventArgs e)
    {
        _settings.VanillaReferenceFolder = null;
        _settings.ExternalEncodingFolder = null;
        SettingsService.Save(_settings);
        MessageBox.Show(this, "Configuration de référence effacée.", "Spira Modifier",
            MessageBoxButton.OK, MessageBoxImage.Information);
        if (_currentRootFolder != null)
            OpenFolder(_currentRootFolder);
    }

    private void OnExit_Click(object sender, RoutedEventArgs e) => Close();

    // ============================================================
    // SAUVEGARDE (Ctrl+S / Ctrl+Maj+S / menu Fichier)
    // ============================================================

    private void OnSave_Click(object sender, RoutedEventArgs e) => DoSave(forceNewLocation: false);

    private void OnSaveAs_Click(object sender, RoutedEventArgs e) => DoSave(forceNewLocation: true);

    private void OnClearOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        _settings.OutputFolder = null;
        SettingsService.Save(_settings);
        MessageBox.Show(this,
            "Dossier de sortie effacé. La prochaine sauvegarde demandera un nouveau dossier.",
            "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// Force la sauvegarde de TOUS les fichiers monsterN.bin chargés, même sans modification.
    /// Utile pour tester le round-trip décode→réencode : si on sauvegarde le vanilla et que
    /// les fichiers générés sont identiques (ou ≥ taille vanilla et fonctionnels en jeu),
    /// alors la pipeline d'écriture est correcte.
    /// </summary>
    private void OnRoundTripSave_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null)
        {
            MessageBox.Show(this, "Aucun workspace ouvert.", "Spira Modifier",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Marque tous les fichiers de localisation comme dirty pour forcer leur écriture,
        // SANS appliquer la moindre modification de texte.
        int markedCount = 0;
        foreach (var db in _workspace.LocalizationDatabases.Values)
        {
            if (db.Charset == null) continue;
            foreach (var (file, _) in db.FilesWithPaths)
            {
                if (file.EntryCount == 0) continue;
                var current = file.GetTexts(0, db.Charset);
                if (current == null) continue;
                file.SetTexts(0, current, db.Charset);
                markedCount++;
            }
        }

        if (markedCount == 0)
        {
            MessageBox.Show(this, "Aucun fichier monsterN.bin chargé.",
                "Test round-trip", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StatusText.Text = $"Round-trip : {markedCount} fichier(s) marqué(s) — lance Ctrl+S pour sauvegarder.";
        MessageBox.Show(this,
            $"{markedCount} fichier(s) de localisation marqué(s) pour réécriture.\n\n" +
            "Utilise Ctrl+S (ou Fichier > Sauvegarder) pour produire les fichiers de sortie.\n" +
            "Compare leur taille avec les fichiers vanilla pour valider le round-trip.",
            "Test round-trip", MessageBoxButton.OK, MessageBoxImage.Information);
        UpdateSaveStatusUI();
    }

    /// <summary>
    /// Effectue la sauvegarde. Si <paramref name="forceNewLocation"/> est vrai ou
    /// si aucun dossier de sortie n'est encore configuré, ouvre un sélecteur de dossier.
    /// </summary>
    private void DoSave(bool forceNewLocation)
    {
        if (_workspace == null)
        {
            MessageBox.Show(this, "Aucun workspace ouvert.", "Spira Modifier",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Détermine le dossier de sortie
        var outputFolder = _settings.OutputFolder;
        if (forceNewLocation || string.IsNullOrWhiteSpace(outputFolder) || !Directory.Exists(outputFolder))
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = UiLocalization.Translate("Choisis le dossier de sortie (les fichiers édités y seront écrits en conservant l'arborescence)"),
            };
            if (dialog.ShowDialog() != true) return;
            outputFolder = dialog.FolderName;

            // Garde-fou : interdire d'écraser le workspace source
            if (string.Equals(Path.GetFullPath(outputFolder),
                              Path.GetFullPath(_workspace.Scan.RootPath),
                              StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this,
                    "Tu ne peux pas sauvegarder dans le dossier source lui-même.\n" +
                    "Choisis un dossier de sortie distinct pour préserver les originaux.",
                    "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _settings.OutputFolder = outputFolder;
            SettingsService.Save(_settings);
        }

        // Sauvegarde
        var report = SaveService.SaveAll(_workspace, outputFolder);
        UpdateSaveStatusUI();

        if (report.WrittenCount == 0 && report.Errors.Count == 0)
        {
            StatusText.Text = "Aucune modification à sauvegarder.";
            return;
        }

        var summary = new System.Text.StringBuilder();
        summary.AppendLine($"✓ {report.WrittenCount} fichier(s) écrit(s) dans :");
        summary.AppendLine(outputFolder);
        if (report.Errors.Count > 0)
        {
            summary.AppendLine();
            summary.AppendLine($"⚠ {report.Errors.Count} erreur(s) :");
            foreach (var (path, err) in report.Errors.Take(10))
                summary.AppendLine($"  • {Path.GetFileName(path)} : {err}");
            if (report.Errors.Count > 10)
                summary.AppendLine($"  ... et {report.Errors.Count - 10} autre(s).");
        }

        MessageBox.Show(this, summary.ToString(), "Sauvegarde",
            MessageBoxButton.OK,
            report.Errors.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);

        StatusText.Text = $"Sauvegardé : {report.WrittenCount} fichier(s) dans {outputFolder}";
    }

    /// <summary>Met à jour l'état visuel des items "Sauvegarder" selon HasUnsavedChanges.</summary>
    private void UpdateSaveStatusUI()
    {
        if (_workspace == null) return;
        var hasChanges = _workspace.HasUnsavedChanges ||
                         _workspace.LocalizationDatabases.Values.Any(db => db.HasUnsavedChanges);
        MenuSave.Header = hasChanges ? "_Sauvegarder ●" : "_Sauvegarder";
    }

    private void ApplyScanResult(SpiraWorkspace workspace)
    {
        _atelGlobalIndex = null;
        RefreshAtelIndexStatus();

        var scan = workspace.Scan;
        TabMonsters.IsEnabled    = (scan.Capabilities & WorkspaceCapabilities.Monsters) != 0;
        TabAttacks.IsEnabled     = workspace.MonsterAttacksByLanguage.Count > 0;
        TabMonsterAi.IsEnabled   = TabMonsters.IsEnabled; // même prérequis
        TabEncounters.IsEnabled  = workspace.EncounterTables != null;
        TabCommands.IsEnabled    = workspace.PlayerCommandsByLanguage.Count > 0;
        TabItems.IsEnabled       = workspace.ItemsByLanguage.Count > 0;
        TabEquipment.IsEnabled   = workspace.InitialEquipmentFile != null || workspace.BukiGetFile != null || workspace.ShopArmsFile != null;
        TabAbilities.IsEnabled   = workspace.AbilitiesByLanguage.Count > 0;
        TabPlayerStart.IsEnabled = workspace.PlayerSaveFilesByLanguage.Count > 0 || workspace.PlayerSaveFile != null;
        TabMaps.IsEnabled        = workspace.TreasureFile != null;
        TabSphereGrid.IsEnabled  = false; // pas encore implémenté

        StatusText.Text = scan.GetSummary();
        UpdateReport(scan);
        PopulateLanguageSelector(workspace);
        PopulateMonsterList(scan.Monsters, workspace);
        PopulateAttackTab(workspace);
        PopulateCommandTab(workspace);
        PopulateItemTab(workspace);
        PopulateGearTab(workspace);
        PopulateAbilityTab(workspace);
        PopulatePlayerStartTab(workspace);
        PopulateEncounterTab(workspace);
        PopulateMapChestTab(workspace);

        if (TabMonsters.IsEnabled && _monsterListItems.Count > 0)
            MainTabs.SelectedItem = TabMonsters;
    }

    /// <summary>
    /// Remplit le sélecteur de langue avec les langues effectivement chargées,
    /// et présélectionne la langue préférée.
    /// </summary>
    private void PopulateLanguageSelector(SpiraWorkspace workspace)
    {
        _suppressLanguageEvents = true;
        try
        {
            LanguageSelector.Items.Clear();

            var availableLanguages = workspace.AvailableLanguages.ToList();
            if (availableLanguages.Count == 0)
            {
                LanguageSelector.Items.Add(new LanguageOption("(aucune)", null));
                LanguageSelector.SelectedIndex = 0;
                LanguageSelector.IsEnabled = false;
                _currentLanguage = null;
                return;
            }

            LanguageSelector.IsEnabled = true;
            foreach (var lang in availableLanguages)
                LanguageSelector.Items.Add(new LanguageOption(LanguageDisplayName(lang), lang));

            // Présélection de la langue préférée
            _currentLanguage = workspace.PreferredDisplayLanguage ?? availableLanguages[0];
            for (int i = 0; i < LanguageSelector.Items.Count; i++)
            {
                if (LanguageSelector.Items[i] is LanguageOption opt && opt.Code == _currentLanguage)
                {
                    LanguageSelector.SelectedIndex = i;
                    break;
                }
            }
        }
        finally
        {
            _suppressLanguageEvents = false;
        }
    }

    private static string LanguageDisplayName(string code) => code switch
    {
        "fr" => "Français",
        "en" => "English",
        "de" => "Deutsch",
        "es" => "Español",
        "it" => "Italiano",
        "ch" => "中文",
        "kr" => "한국어",
        "jp" => "日本語",
        _    => code,
    };

    /// <summary>
    /// Quand l'utilisateur change la langue, on rafraîchit la sidebar (noms) et
    /// les textes du monstre actuellement sélectionné.
    /// </summary>
    private void OnLanguageSelector_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageEvents || _workspace == null) return;
        if (LanguageSelector.SelectedItem is not LanguageOption opt || opt.Code == null) return;

        _currentLanguage = opt.Code;
        _atelGlobalIndex = null;
        RefreshAtelIndexStatus();

        // Rebuild de la sidebar avec la nouvelle langue
        RefreshMonsterListNames();

        // Rafraîchit les textes du monstre actuellement sélectionné
        RefreshSelectedMonsterTexts();

        // Idem pour la liste d'attaques (les noms changent selon la langue)
        if (_workspace != null && _workspace.MonsterAttacksByLanguage.Count > 0)
        {
            var selectedAttack = AttackListBox.SelectedItem as AttackListItem;
            var selectedKey = selectedAttack != null ? (selectedAttack.SourceTag, selectedAttack.RelativeIndex) : default;

            RebuildAttackList();

            // Restaure la sélection
            if (selectedAttack != null)
            {
                foreach (var item in _attackListItems)
                {
                    if (item.SourceTag == selectedKey.SourceTag && item.RelativeIndex == selectedKey.RelativeIndex)
                    {
                        AttackListBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        // Et pour la liste des commandes joueur / Chimères
        if (_workspace != null && _workspace.PlayerCommandsByLanguage.Count > 0)
        {
            var selectedCommand = CommandListBox.SelectedItem as CommandListItem;
            var selectedRelIdx = selectedCommand?.RelativeIndex;

            RebuildCommandList();

            if (selectedRelIdx != null)
            {
                foreach (var item in _commandListItems)
                {
                    if (item.RelativeIndex == selectedRelIdx)
                    {
                        CommandListBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        // Et pour la liste des objets
        if (_workspace != null && _workspace.ItemsByLanguage.Count > 0)
        {
            var selectedItem = ItemListBox.SelectedItem as ItemListItem;
            var selectedRelIdx = selectedItem?.RelativeIndex;

            RebuildItemList();

            if (selectedRelIdx != null)
            {
                foreach (var item in _itemListItems)
                {
                    if (item.RelativeIndex == selectedRelIdx)
                    {
                        ItemListBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        // Et pour la liste des équipements (les noms d'armes changent par langue)
        if (_workspace != null && (_workspace.InitialEquipmentFile != null || _workspace.BukiGetFile != null || _workspace.ShopArmsFile != null))
        {
            var selectedGear = GearListBox.SelectedItem as GearListItem;
            var selKey = selectedGear != null ? (selectedGear.SourceTag, selectedGear.GlobalId) : default;

            RebuildGearList();

            if (selectedGear != null)
            {
                foreach (var item in _gearListItems)
                {
                    if (item.SourceTag == selKey.SourceTag && item.GlobalId == selKey.GlobalId)
                    {
                        GearListBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        // Et pour les aptitudes d'équipement
        if (_workspace != null && _workspace.AbilitiesByLanguage.Count > 0)
        {
            var selectedAbility = AbilityListBox.SelectedItem as AbilityListItem;
            var selectedRelIdx = selectedAbility?.RelativeIndex;

            RebuildAbilityList();

            if (selectedRelIdx != null)
            {
                foreach (var item in _abilityListItems)
                {
                    if (item.RelativeIndex == selectedRelIdx)
                    {
                        AbilityListBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        // Et pour les données de départ (noms des commandes dans la liste de compétences)
        if (_workspace != null && (_workspace.PlayerSaveFilesByLanguage.Count > 0 || _workspace.PlayerSaveFile != null))
        {
            var selectedPlayer = PlayerStartListBox.SelectedItem as PlayerStartListItem;
            var selectedRelIdx = selectedPlayer?.RelativeIndex;

            RebuildPlayerStartList();

            if (selectedRelIdx != null)
            {
                foreach (var item in _playerStartListItems)
                {
                    if (item.RelativeIndex == selectedRelIdx)
                    {
                        PlayerStartListBox.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        UpdateMonsterLootResolvedSummary();
    }

    private void RefreshMonsterListNames()
    {
        if (_workspace == null) return;

        // On regénère les noms décodés sans relire les fichiers (rapide).
        // Pour ça on a besoin de re-lookup les noms via la langue courante.
        var selectedItem = MonsterListBox.SelectedItem as MonsterListItem;
        var selectedPath = selectedItem?.Entry.FullPath;

        foreach (var item in _allMonsterItems)
        {
            item.DecodedName = LookupMonsterName(item.Entry, _workspace, _currentLanguage);
        }

        // Force le ListBox à rafraîchir l'affichage
        ApplyMonsterFilter();

        // Tente de restaurer la sélection
        if (selectedPath != null)
        {
            foreach (var item in _monsterListItems)
            {
                if (item.Entry.FullPath == selectedPath)
                {
                    MonsterListBox.SelectedItem = item;
                    break;
                }
            }
        }
    }

    private void RefreshSelectedMonsterTexts()
    {
        if (MonsterListBox.SelectedItem is MonsterListItem item)
            OnMonsterSelection_Changed(MonsterListBox, null!);
    }

    private void UpdateReport(WorkspaceScanResult scan)
    {
        var report = new StringBuilder();
        report.AppendLine($"Dossier scanné : {scan.RootPath}");

        // Affiche la config de référence si active
        if (!string.IsNullOrEmpty(_settings.VanillaReferenceFolder))
            report.AppendLine($"Dossier vanilla de référence : {_settings.VanillaReferenceFolder}");
        if (!string.IsNullOrEmpty(_settings.ExternalEncodingFolder))
            report.AppendLine($"Dossier ffx_encoding/ externe : {_settings.ExternalEncodingFolder}");

        report.AppendLine();
        report.AppendLine($"Résumé : {scan.GetSummary()}");
        report.AppendLine();

        if (scan.Monsters.Count > 0)
        {
            // Distribution par langue
            var byLang = scan.Monsters.GroupBy(m => m.Language)
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Key}={g.Count()}");
            report.AppendLine($"=== {scan.Monsters.Count} monstres détectés (langues : {string.Join(", ", byLang)}) ===");
            foreach (var entry in scan.Monsters.Take(10))
            {
                var langs = string.Join("/", entry.AllLanguageVariants.Keys);
                report.AppendLine($"  · {entry.FileName} [primary={entry.Language}, available={langs}]");
            }
            if (scan.Monsters.Count > 10)
                report.AppendLine($"  ... ({scan.Monsters.Count - 10} de plus)");
            report.AppendLine();
        }

        if (scan.CommandFilePath != null)   report.AppendLine($"command.bin    : {scan.CommandFilePath}");
        if (scan.MonMagic1FilePath != null) report.AppendLine($"monmagic1.bin  : {scan.MonMagic1FilePath}");
        if (scan.MonMagic2FilePath != null) report.AppendLine($"monmagic2.bin  : {scan.MonMagic2FilePath}");
        if (scan.ItemFilePath != null)      report.AppendLine($"item.bin       : {scan.ItemFilePath}");
        if (scan.EncodingFolderPath != null) report.AppendLine($"ffx_encoding/  : {scan.EncodingFolderPath}");

        // Détail des kernels par langue (essentiel pour debug du chargement des hardmods)
        if (scan.KernelByLanguage.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("=== Détail des kernels par langue ===");
            foreach (var (lang, kernel) in scan.KernelByLanguage.OrderBy(k => k.Key))
            {
                report.AppendLine($"  [{lang}] {kernel.DirectoryPath}");
                report.AppendLine($"      command.bin   : {(kernel.CommandPath   != null ? "✓" : "✗")}");
                report.AppendLine($"      monmagic1.bin : {(kernel.MonMagic1Path != null ? "✓" : "✗")}");
                report.AppendLine($"      monmagic2.bin : {(kernel.MonMagic2Path != null ? "✓" : "✗")}");
                report.AppendLine($"      item.bin      : {(kernel.ItemPath      != null ? "✓" : "✗")}");
                report.AppendLine($"      monster1.bin  : {(kernel.Monster1Path  != null ? "✓" : "✗")}");
                report.AppendLine($"      monster2.bin  : {(kernel.Monster2Path  != null ? "✓" : "✗")}");
                report.AppendLine($"      monster3.bin  : {(kernel.Monster3Path  != null ? "✓" : "✗")}");
            }
        }

        if (scan.LocalizedKernelDirs.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("=== Localisation des noms (monster1/2/3.bin) ===");
            foreach (var (lang, dir) in scan.LocalizedKernelDirs)
                report.AppendLine($"  [{lang}] {dir}");
            if (_workspace != null)
            {
                report.AppendLine();
                report.AppendLine($"Langue d'affichage choisie : {_workspace.PreferredDisplayLanguage ?? "(aucune)"}");

                if (_workspace.LocalizationDatabases.Count > 0)
                {
                    report.AppendLine("Bases de noms de monstres chargées :");
                    foreach (var (lang, db) in _workspace.LocalizationDatabases)
                        report.AppendLine($"  [{lang}] {db.FileCount} fichiers, {db.TotalEntryCount} entrées");
                }

                if (_workspace.MonsterAttacksByLanguage.Count > 0)
                {
                    report.AppendLine("Fichiers d'attaques chargés :");
                    foreach (var (lang, pair) in _workspace.MonsterAttacksByLanguage)
                    {
                        var mm1 = pair.MonMagic1 != null ? $"MM1: {pair.MonMagic1.Count} attaques (range 0x{pair.MonMagic1.MinIndex:X4}-0x{pair.MonMagic1.MaxIndex:X4})" : "MM1: ✗";
                        var mm2 = pair.MonMagic2 != null ? $"MM2: {pair.MonMagic2.Count} attaques (range 0x{pair.MonMagic2.MinIndex:X4}-0x{pair.MonMagic2.MaxIndex:X4})" : "MM2: ✗";
                        report.AppendLine($"  [{lang}] {mm1}, {mm2}");
                    }
                }

                if (_workspace.PlayerCommandsByLanguage.Count > 0)
                {
                    report.AppendLine("Fichiers de commandes joueur chargés :");
                    foreach (var (lang, file) in _workspace.PlayerCommandsByLanguage)
                        report.AppendLine($"  [{lang}] {file.Count} commandes (range 0x{file.MinIndex:X4}-0x{file.MaxIndex:X4})");
                }

                if (_workspace.ItemsByLanguage.Count > 0)
                {
                    report.AppendLine("Fichiers d'objets chargés :");
                    foreach (var (lang, file) in _workspace.ItemsByLanguage)
                        report.AppendLine($"  [{lang}] {file.Count} objets (range 0x{file.MinIndex:X4}-0x{file.MaxIndex:X4})");
                }
            }
        }

        // Distribution des équipements par personnage — utile pour diagnostiquer
        // les écarts entre weapon.bin (départ) et buki_get/shop_arms.
        if (_workspace != null && (_workspace.InitialEquipmentFile != null || _workspace.BukiGetFile != null || _workspace.ShopArmsFile != null))
        {
            report.AppendLine();
            report.AppendLine("=== Distribution des équipements par personnage ===");
            var dist = _workspace.GetGearDistributionByCharacter();
            var initialDist = CountGearByCharacter(_workspace.InitialEquipmentFile);
            var allChars = PlayerCharacters.KnownCharacters;
            foreach (var id in allChars)
            {
                var (b, s) = dist.GetValueOrDefault(id);
                var w = initialDist.GetValueOrDefault(id);
                var name = PlayerCharacters.GetName(id) ?? "?";
                var marker = (w + b + s == 0) ? "  ⚠" : "   ";
                report.AppendLine($"{marker} 0x{id:X2} {name,-15} : weapon={w,3}, buki_get={b,3}, shop_arms={s,3}");
            }
            // Autres valeurs trouvées (FF, valeurs hors range)
            var others = dist.Keys.Concat(initialDist.Keys).Distinct().Where(k => !allChars.Contains(k)).OrderBy(k => k).ToList();
            if (others.Count > 0)
            {
                report.AppendLine("  Autres valeurs (placeholders / inconnu) :");
                foreach (var k in others)
                {
                    var (b, s) = dist.GetValueOrDefault(k);
                    var w = initialDist.GetValueOrDefault(k);
                    report.AppendLine($"     0x{k:X2}              : weapon={w,3}, buki_get={b,3}, shop_arms={s,3}");
                }
            }
        }

        if (scan.LocalizationFolders.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("=== Dossiers de localisation détectés ===");
            foreach (var (langCode, path) in scan.LocalizationFolders)
                report.AppendLine($"  [{langCode}] {path}");
        }

        if (scan.Warnings.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("=== Avertissements ===");
            foreach (var w in scan.Warnings) report.AppendLine($"  ⚠ {w}");
        }

        ReportText.Text = report.ToString();
    }

    private void PopulateMonsterList(IEnumerable<MonsterFileEntry> entries, SpiraWorkspace workspace)
    {
        _allMonsterItems = entries
            .OrderBy(e => e.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(e => CreateListItem(e, workspace))
            .ToList();
        ApplyMonsterFilter();
    }

    private MonsterListItem CreateListItem(MonsterFileEntry entry, SpiraWorkspace workspace)
    {
        var item = new MonsterListItem(entry);
        item.DecodedName = LookupMonsterName(entry, workspace, _currentLanguage);
        return item;
    }

    /// <summary>
    /// Cherche le nom traduit pour un monstre. Stratégie :
    /// 1) Si une langue est sélectionnée, lookup dans sa base monster1/2/3.bin
    /// 2) Sinon fallback sur la langue préférée
    /// 3) Sinon fallback sur le nom embarqué dans le fichier .bin
    /// </summary>
    private static string? LookupMonsterName(MonsterFileEntry entry, SpiraWorkspace workspace, string? preferredLanguage)
    {
        try
        {
            var bytes = File.ReadAllBytes(entry.FullPath);
            var monster = MonsterFile.Read(bytes);
            if (monster.StatSheet == null) return null;

            // 1) Lookup via la langue sélectionnée
            if (preferredLanguage != null)
            {
                var texts = workspace.GetMonsterTexts(monster.StatSheet.MonsterIdx, preferredLanguage);
                if (!string.IsNullOrWhiteSpace(texts?.Name))
                    return texts!.Name;
            }

            // 2) Fallback : nom embarqué dans le fichier
            var charset = workspace.GetCharsetForLanguage(entry.Language);
            var embedded = monster.DecodeName(charset);
            if (!string.IsNullOrWhiteSpace(embedded))
                return embedded;
        }
        catch { }
        return null;
    }

    private void OnMonsterFilter_Changed(object sender, TextChangedEventArgs e)
    {
        FilterPlaceholder.Visibility = string.IsNullOrEmpty(MonsterFilterBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        ApplyMonsterFilter();
    }

    private void ApplyMonsterFilter()
    {
        var filter = MonsterFilterBox.Text.Trim();
        IEnumerable<MonsterListItem> filtered = _allMonsterItems;
        if (!string.IsNullOrEmpty(filter))
        {
            filtered = filtered.Where(item =>
                item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }
        _monsterListItems.Clear();
        foreach (var item in filtered) _monsterListItems.Add(item);

        var totalCount = _allMonsterItems.Count;
        var shownCount = _monsterListItems.Count;
        MonsterCountText.Text = shownCount == totalCount
            ? $"{totalCount} monstres"
            : $"{shownCount} / {totalCount} monstres";
    }

    private void OnMonsterSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MonsterListBox.SelectedItem is not MonsterListItem item)
        {
            ShowNoSelection();
            return;
        }
        try
        {
            var monsterFile = LoadMonsterFileForDisplay(item);
            DisplayMonster(item, monsterFile);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossible de lire le fichier monstre :\n\n{item.Entry.FullPath}\n\n{ex.Message}",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Error);
            ShowNoSelection();
        }
    }

    private void ShowNoSelection()
    {
        NoMonsterSelectedMessage.Visibility = Visibility.Visible;
        MonsterDetailsPanel.Visibility = Visibility.Collapsed;
        _currentMonsterItem = null;
        _currentMonsterFile = null;
        ClearMonsterAi();
    }

    private MonsterFile LoadMonsterFileForDisplay(MonsterListItem item)
    {
        var monsterFile = _workspace?.GetEditedMonster(item.Entry.FullPath);
        if (monsterFile != null)
            return monsterFile;

        var bytes = File.ReadAllBytes(item.Entry.FullPath);
        return MonsterFile.Read(bytes, scriptId: item.Entry.FileName);
    }

    private void DisplayMonster(MonsterListItem item, MonsterFile file)
    {
        NoMonsterSelectedMessage.Visibility = Visibility.Collapsed;
        MonsterDetailsPanel.Visibility = Visibility.Visible;
        _currentMonsterItem = item;
        _currentMonsterFile = file;

        MonsterHeaderText.Text = item.DisplayName;
        var stat = file.StatSheet;
        var langInfo = $"langue : {item.Entry.Language}";
        if (item.Entry.AllLanguageVariants.Count > 1)
            langInfo += $" (disponible aussi en : {string.Join(", ", item.Entry.AllLanguageVariants.Keys.Where(k => k != item.Entry.Language))})";
        MonsterFileInfoText.Text =
            $"Fichier : {item.Entry.FullPath}  •  {langInfo}" +
            (stat != null ? $"  •  ID monstre : 0x{stat.MonsterIdx:X4}" : "");

        if (stat == null)
        {
            ChunksInfoText.Text = BuildChunksReport(file);
            ClearAllFields();
            DisplayMonsterAi(item, file);
            return;
        }

        // Textes localisés (Sensor / Scan dans la langue sélectionnée)
        DisplayLocalizedTexts(stat.MonsterIdx);

        _suppressMonsterStatEvents = true;
        try
        {
            DisplayMonsterLoot(file.Loot);

            StatHpBox.Text       = stat.Hp.ToString();
            StatMpBox.Text       = stat.Mp.ToString();
            StatOverkillBox.Text = stat.OverkillThreshold.ToString();
            StatStrBox.Text      = stat.Str.ToString();
            StatDefBox.Text      = stat.Def.ToString();
            StatMagBox.Text      = stat.Mag.ToString();
            StatMdfBox.Text      = stat.Mdf.ToString();
            StatAgiBox.Text      = stat.Agi.ToString();
            StatLckBox.Text      = stat.Lck.ToString();
            StatEvaBox.Text      = stat.Eva.ToString();
            StatAccBox.Text      = stat.Acc.ToString();
            StatPoisonBox.Text   = stat.PoisonDamage.ToString();

            BuildElementsGrid(stat);
            BuildStatusResistGrid(stat);
            BuildAutoStatusPanels(stat);
            BuildExtraStatusImmunityPanel(stat);
            BuildImmunitiesPanel(stat);
            BuildCommandsList(stat);

            MetaForcedActionBox.Text = $"0x{stat.ForcedAction:X4}";
            MetaMonsterIdxBox.Text   = $"0x{stat.MonsterIdx:X4}";
            MetaModelIdxBox.Text     = $"0x{stat.ModelIdx:X4}";
            MetaDoomBox.Text         = stat.DoomCounter.ToString();
            MetaArenaBox.Text        = $"0x{stat.MonsterArenaIdx:X2}";
            MetaSoundBankBox.Text    = $"0x{stat.SoundBankRef:X4}";
            MetaCtbIconBox.Text      = stat.CtbIconType.ToString();

            UpdateMonsterLootResolvedSummary();

            ApplyMonsterStatsButton.IsEnabled = false;
            CopyMonsterMechanicsButton.IsEnabled = true;
            PasteMonsterMechanicsButton.IsEnabled = _copiedMonsterMechanics != null;
            RevertMonsterStatsButton.IsEnabled = false;
            MonsterStatsEditStatusText.Text = "";
        }
        finally
        {
            _suppressMonsterStatEvents = false;
        }

        ChunksInfoText.Text = BuildChunksReport(file);
        DisplayMonsterAi(item, file);
    }

    private void SyncMonsterAiSelector(MonsterListItem? item)
    {
        _suppressMonsterAiSelectionEvents = true;
        try
        {
            MonsterAiMonsterSelector.SelectedItem = item;
        }
        finally
        {
            _suppressMonsterAiSelectionEvents = false;
        }
    }

    private void ClearMonsterAi()
    {
        SyncMonsterAiSelector(null);
        MonsterAiHeaderText.Text = "Décompilation ATEL";
        MonsterAiSummaryText.Text = "Sélectionne un monstre pour afficher son bytecode IA.";
        MonsterAiWorkersList.ItemsSource = null;
        MonsterAiAnalysisBox.Text = "";
        MonsterAiCodeBox.Text = "";
        MonsterAiCopilotOutputBox.Text = "Sélectionne un monstre pour activer le copilote ATEL.";
        MonsterAiCopilotInputBox.Text = "";
        MonsterAiCopilotAskButton.IsEnabled = false;
        MonsterAiCopilotPatchButton.IsEnabled = false;
        MonsterAiReplaceCommandButton.IsEnabled = false;
        MonsterAiReplaceOldCommandBox.Text = "";
        MonsterAiReplaceNewCommandBox.Text = "";
        MonsterAiDeterministicEditStatusText.Text = "";
        _currentMonsterAiItem = null;
        _currentMonsterAiFile = null;
        _currentMonsterAiScript = null;
        _currentMonsterAiAnalysisText = "";
    }

    private void DisplayMonsterAi(MonsterListItem item, MonsterFile file)
    {
        SyncMonsterAiSelector(item);

        try
        {
            var decompiled = AtelDecompiler.Decompile(file.AiBytes);
            MonsterAiHeaderText.Text = $"{item.DisplayName} — IA ATEL";
            MonsterAiSummaryText.Text =
                $"Fichier : {item.Entry.FullPath}  •  " +
                $"AI : {ChunkSize(file.AiBytes)} octets  •  " +
                $"Workers : {decompiled.Workers.Count}  •  " +
                $"Instructions : {decompiled.Instructions.Count}" +
                (decompiled.Warnings.Count > 0 ? $"  •  Avertissements : {decompiled.Warnings.Count}" : "");
            MonsterAiWorkersList.ItemsSource = decompiled.Workers;
            _currentMonsterAiItem = item;
            _currentMonsterAiFile = file;
            _currentMonsterAiScript = decompiled;
            _currentMonsterAiAnalysisText = AtelAnalyzer.Analyze(decompiled, BuildAtelAnalysisOptions());
            UiLocalization.SetLocalizedReadOnlyTextBox(MonsterAiAnalysisBox, _currentMonsterAiAnalysisText);
            MonsterAiCodeBox.Text = decompiled.ToListingText();
            MonsterAiCopilotOutputBox.Text = AtelCopilot.CreateWelcome(BuildAtelCopilotContext());
            MonsterAiCopilotInputBox.Text = "";
            MonsterAiCopilotAskButton.IsEnabled = true;
            MonsterAiCopilotPatchButton.IsEnabled = true;
            MonsterAiReplaceCommandButton.IsEnabled = true;
            MonsterAiDeterministicEditStatusText.Text = "";
        }
        catch (Exception ex)
        {
            MonsterAiHeaderText.Text = $"{item.DisplayName} — IA ATEL";
            MonsterAiSummaryText.Text = "Décompilation impossible.";
            MonsterAiWorkersList.ItemsSource = null;
            _currentMonsterAiAnalysisText = "Analyse impossible.\n\n" + ex;
            UiLocalization.SetLocalizedReadOnlyTextBox(MonsterAiAnalysisBox, _currentMonsterAiAnalysisText);
            MonsterAiCodeBox.Text = ex.ToString();
            MonsterAiCopilotOutputBox.Text = "Copilote indisponible : la décompilation ATEL a échoué.";
            MonsterAiCopilotAskButton.IsEnabled = false;
            MonsterAiCopilotPatchButton.IsEnabled = false;
            MonsterAiReplaceCommandButton.IsEnabled = false;
            MonsterAiDeterministicEditStatusText.Text = "";
            _currentMonsterAiItem = null;
            _currentMonsterAiFile = null;
            _currentMonsterAiScript = null;
        }
    }

    private AtelCopilotContext BuildAtelCopilotContext()
    {
        var commandCatalog = BuildAtelCommandCatalog(out var commandCatalogDiagnostics);
        return new AtelCopilotContext
        {
            MonsterDisplayName = _currentMonsterAiItem?.DisplayName ?? "(monstre inconnu)",
            MonsterFileName = _currentMonsterAiItem?.Entry.FileName ?? "",
            SourcePath = _currentMonsterAiItem?.Entry.FullPath ?? "",
            Script = _currentMonsterAiScript ?? new AtelDecompiledScript(),
            AiBytes = _currentMonsterAiFile?.AiBytes,
            AnalysisOptions = BuildAtelAnalysisOptions(),
            AnalysisText = _currentMonsterAiAnalysisText,
            CommandCatalog = commandCatalog,
            CommandCatalogDiagnostics = commandCatalogDiagnostics,
            GlobalIndex = _atelGlobalIndex,
        };
    }

    private IReadOnlyList<AtelCommandCatalogEntry> BuildAtelCommandCatalog(out IReadOnlyList<string> diagnostics)
    {
        var diag = new List<string>();
        diagnostics = diag;
        if (_workspace == null)
            return Array.Empty<AtelCommandCatalogEntry>();

        var result = new Dictionary<int, AtelCommandCatalogEntry>();
        var languageCandidates = BuildAtelCatalogLanguageCandidates().ToList();
        if (languageCandidates.Count == 0)
        {
            diag.Add("Aucune langue de kernel disponible pour construire le catalogue actions.");
            return Array.Empty<AtelCommandCatalogEntry>();
        }

        var monsterAttackLang = languageCandidates.FirstOrDefault(_workspace.MonsterAttacksByLanguage.ContainsKey);
        if (monsterAttackLang != null && _workspace.MonsterAttacksByLanguage.TryGetValue(monsterAttackLang, out var monsterAttacks))
        {
            AddCommandsFromAttackFile(result, monsterAttacks.MonMagic1, "monmagic1", monsterAttackLang);
            AddCommandsFromAttackFile(result, monsterAttacks.MonMagic2, "monmagic2", monsterAttackLang);
            diag.Add($"monmagic1/2 : langue {monsterAttackLang}");
        }
        else
        {
            diag.Add("monmagic1/2 : aucun fichier chargé.");
        }

        var commandLang = languageCandidates.FirstOrDefault(_workspace.PlayerCommandsByLanguage.ContainsKey);
        if (commandLang != null && _workspace.PlayerCommandsByLanguage.TryGetValue(commandLang, out var commands))
        {
            AddCommandsFromAttackFile(result, commands, "command", commandLang);
            diag.Add($"command.bin : langue {commandLang}, {commands.Count} entrée(s)");
        }
        else
        {
            diag.Add("command.bin : aucun fichier chargé. Les commandes 0x3000 ne seront pas résolues.");
        }

        var itemLang = languageCandidates.FirstOrDefault(_workspace.ItemsByLanguage.ContainsKey);
        if (itemLang != null && _workspace.ItemsByLanguage.TryGetValue(itemLang, out var items))
        {
            AddCommandsFromAttackFile(result, items, "item", itemLang);
            diag.Add($"item.bin : langue {itemLang}");
        }
        else
        {
            diag.Add("item.bin : aucun fichier chargé.");
        }

        return result.Values.OrderBy(c => c.Id).ToList();
    }

    private IEnumerable<string> BuildAtelCatalogLanguageCandidates()
    {
        if (_workspace == null)
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? language)
        {
            if (!string.IsNullOrWhiteSpace(language))
                seen.Add(language);
        }

        Add(_currentLanguage);
        Add(_workspace.PreferredDisplayLanguage);
        foreach (var lang in _workspace.AvailableLanguages) Add(lang);
        foreach (var lang in _workspace.MonsterAttacksByLanguage.Keys) Add(lang);
        foreach (var lang in _workspace.PlayerCommandsByLanguage.Keys) Add(lang);
        foreach (var lang in _workspace.ItemsByLanguage.Keys) Add(lang);

        foreach (var preferred in new[] { _currentLanguage, _workspace.PreferredDisplayLanguage, "fr", "en", "de", "es", "it", "jp" })
        {
            if (!string.IsNullOrWhiteSpace(preferred) && seen.Remove(preferred))
                yield return preferred;
        }
        foreach (var lang in seen.OrderBy(l => l, StringComparer.OrdinalIgnoreCase))
            yield return lang;
    }

    private void AddCommandsFromAttackFile(
        Dictionary<int, AtelCommandCatalogEntry> output,
        AttackFile? file,
        string source,
        string language)
    {
        if (file == null || _workspace == null)
            return;

        var charset = _workspace.GetCharsetForLanguage(language);
        for (int i = 0; i < file.Count; i++)
        {
            var id = file.MinIndex + i;
            if (output.ContainsKey(id))
                continue;

            var name = file.GetName(i, charset);
            if (string.IsNullOrWhiteSpace(name))
                name = "(sans nom)";
            output[id] = new AtelCommandCatalogEntry(id, source, name);
        }
    }

    private async void OnMonsterAiBuildIndex_Click(object sender, RoutedEventArgs e)
    {
        await EnsureAtelGlobalIndexAsync(force: true);
    }

    private async Task EnsureAtelGlobalIndexAsync(bool force)
    {
        if (_workspace == null)
        {
            MonsterAiIndexStatusText.Text = "Index ATEL : aucun workspace";
            return;
        }

        if (_isBuildingAtelGlobalIndex)
            return;

        if (!force && _atelGlobalIndex != null)
            return;

        var workspace = _workspace;
        var language = _currentLanguage ?? workspace.PreferredDisplayLanguage;
        _isBuildingAtelGlobalIndex = true;
        MonsterAiBuildIndexButton.IsEnabled = false;
        MonsterAiIndexProgressBar.Visibility = Visibility.Visible;
        MonsterAiIndexProgressBar.IsIndeterminate = false;
        MonsterAiIndexProgressBar.Value = 0;
        MonsterAiIndexStatusText.Text = "Index ATEL : lecture des fichiers mon...";

        var progress = new Progress<AtelIndexProgress>(p =>
        {
            MonsterAiIndexProgressBar.Value = p.Total <= 0 ? 0 : Math.Clamp(p.Current * 100.0 / p.Total, 0, 100);
            MonsterAiIndexStatusText.Text = $"Index ATEL : {p.Current}/{p.Total} {p.FileName}";
        });

        try
        {
            _atelGlobalIndex = await Task.Run(() => BuildAtelGlobalIndex(workspace, language, progress));
            MonsterAiIndexStatusText.Text =
                $"Index ATEL : {_atelGlobalIndex.Entries.Count} fichier(s), {_atelGlobalIndex.ErrorCount} erreur(s)";
        }
        catch (Exception ex)
        {
            _atelGlobalIndex = null;
            MonsterAiIndexStatusText.Text = "Index ATEL : erreur";
            if (force)
            {
                MessageBox.Show(this,
                    $"Impossible de construire l'index global ATEL :\n\n{ex.Message}",
                    "Index ATEL", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            _isBuildingAtelGlobalIndex = false;
            MonsterAiIndexProgressBar.Visibility = Visibility.Collapsed;
            RefreshAtelIndexStatus();
        }
    }

    private AtelGlobalIndex BuildAtelGlobalIndex(
        SpiraWorkspace workspace,
        string? preferredLanguage,
        IProgress<AtelIndexProgress> progress)
    {
        var monsters = workspace.Scan.Monsters
            .OrderBy(m => m.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var entries = new List<AtelGlobalIndexEntry>();
        var errors = 0;

        for (int i = 0; i < monsters.Count; i++)
        {
            var entry = monsters[i];
            progress.Report(new AtelIndexProgress(i + 1, monsters.Count, entry.FileName));

            try
            {
                var monster = workspace.GetEditedMonster(entry.FullPath);
                if (monster == null)
                {
                    var bytes = File.ReadAllBytes(entry.FullPath);
                    monster = MonsterFile.Read(bytes, scriptId: entry.FileName);
                }

                var script = AtelDecompiler.Decompile(monster.AiBytes);
                var displayName = ResolveMonsterDisplayNameForIndex(workspace, entry, monster, preferredLanguage);
                var monsterIndex = monster.StatSheet?.MonsterIdx;
                entries.Add(AtelKnowledgeBase.CreateIndexEntry(
                    entry.FileName,
                    displayName,
                    monsterIndex,
                    entry.FullPath,
                    script));
            }
            catch
            {
                errors++;
            }
        }

        return new AtelGlobalIndex(entries, errors);
    }

    private static string ResolveMonsterDisplayNameForIndex(
        SpiraWorkspace workspace,
        MonsterFileEntry entry,
        MonsterFile monster,
        string? preferredLanguage)
    {
        var monsterIndex = monster.StatSheet?.MonsterIdx;
        if (monsterIndex != null)
        {
            var languages = new List<string>();
            if (!string.IsNullOrWhiteSpace(preferredLanguage))
                languages.Add(preferredLanguage);
            if (!string.IsNullOrWhiteSpace(workspace.PreferredDisplayLanguage))
                languages.Add(workspace.PreferredDisplayLanguage);
            languages.AddRange(workspace.AvailableLanguages);
            languages.Add(entry.Language);

            foreach (var lang in languages.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var texts = workspace.GetMonsterTexts(monsterIndex.Value, lang);
                if (!string.IsNullOrWhiteSpace(texts?.Name))
                    return texts!.Name;
            }
        }

        var charset = workspace.GetCharsetForLanguage(entry.Language) ?? workspace.DefaultCharset;
        var embedded = monster.DecodeName(charset);
        return string.IsNullOrWhiteSpace(embedded) ? entry.FileName : embedded;
    }

    private void RefreshAtelIndexStatus()
    {
        if (_isBuildingAtelGlobalIndex)
            return;

        MonsterAiBuildIndexButton.IsEnabled = _workspace != null;
        MonsterAiIndexProgressBar.Visibility = Visibility.Collapsed;

        if (_workspace == null)
        {
            MonsterAiIndexStatusText.Text = "Index ATEL : aucun workspace";
        }
        else if (_atelGlobalIndex == null)
        {
            MonsterAiIndexStatusText.Text = "Index ATEL : non charge";
        }
        else
        {
            MonsterAiIndexStatusText.Text =
                $"Index ATEL : {_atelGlobalIndex.Entries.Count} fichier(s), {_atelGlobalIndex.ErrorCount} erreur(s)";
        }
    }

    private void OnMonsterAiCopilotAsk_Click(object sender, RoutedEventArgs e)
    {
        SubmitMonsterAiCopilotQuestion(MonsterAiCopilotInputBox.Text);
    }

    private async void OnMonsterAiCopilotPatch_Click(object sender, RoutedEventArgs e)
    {
        var prompt = MonsterAiCopilotInputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            MessageBox.Show(this,
                "Décris d'abord la modification ATEL à prétester dans la zone de question.",
                "Patch ATEL", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_workspace == null || _currentMonsterAiItem == null || _currentMonsterAiFile == null)
        {
            MessageBox.Show(this, "Sélectionne un monstre avant de préparer un patch ATEL.",
                "Patch ATEL", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveAtelLlmSettingsFromUi(showStatus: false);
        if (!IsAtelLlmConfigured())
        {
            MonsterAiLlmStatusText.Text = "LLM requis pour prépatch";
            MessageBox.Show(this,
                "Le bouton « Prétester patch LLM » sert à demander au LLM de générer un patch ATEL JSON byte-level, puis à le valider avant application.\n\n" +
                "Il nécessite donc que l'option LLM soit activée en haut, avec un endpoint et un modèle renseignés.\n\n" +
                "Pour une analyse ou un plan sans patch, utilise plutôt le bouton « Demander ».",
                "Patch ATEL", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_atelGlobalIndex == null)
            await EnsureAtelGlobalIndexAsync(force: false);

        MonsterAiCopilotAskButton.IsEnabled = false;
        MonsterAiCopilotPatchButton.IsEnabled = false;
        MonsterAiLlmStatusText.Text = "Prépatch LLM...";

        AtelPatchProposal proposal;
        AtelPatchValidationResult validation;
        try
        {
            var context = BuildAtelCopilotContext();
            proposal = await AtelLlmCopilotClient.ProposePatchAsync(context, prompt, BuildAtelLlmOptions());
            validation = AtelPatchEngine.Validate(_currentMonsterAiFile, proposal);
        }
        catch (Exception ex)
        {
            MonsterAiLlmStatusText.Text = "Prépatch refusé";
            AppendMonsterAiCopilotBlock(
                "Prépatch ATEL",
                prompt,
                "Le LLM n'a pas produit de patch exploitable.\n\nDétail : " + ex.Message);
            MonsterAiCopilotAskButton.IsEnabled = true;
            MonsterAiCopilotPatchButton.IsEnabled = true;
            return;
        }
        finally
        {
            if (_currentMonsterAiScript != null)
            {
                MonsterAiCopilotAskButton.IsEnabled = true;
                MonsterAiCopilotPatchButton.IsEnabled = true;
            }
        }

        var report = BuildAtelPatchReport(proposal, validation);
        AppendMonsterAiCopilotBlock("Prépatch ATEL", prompt, report);

        if (!validation.Success || validation.PatchedBytes == null)
        {
            MonsterAiLlmStatusText.Text = "Prépatch non appliqué";
            return;
        }

        var confirm = MessageBox.Show(this,
            "Prétest ATEL OK.\n\n" +
            $"Taille : {validation.OriginalSize:N0} -> {validation.PatchedSize:N0} octets ({validation.SizeDelta:+#;-#;0}).\n\n" +
            "Appliquer ce patch en mémoire ?\n" +
            "Il faudra ensuite sauvegarder avec Ctrl+S pour écrire le fichier de sortie.",
            "Appliquer patch ATEL",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            MonsterAiLlmStatusText.Text = "Prépatch validé, non appliqué";
            return;
        }

        ApplyAtelPatch(validation.PatchedBytes, proposal, validation);
    }

    private void OnMonsterAiReplaceCommand_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || _currentMonsterAiItem == null || _currentMonsterAiFile == null || _currentMonsterAiScript == null)
        {
            MessageBox.Show(this, "Sélectionne un monstre avec un ATEL décompilé avant d'éditer une commande.",
                "Édition ATEL", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TryParseIntText(MonsterAiReplaceOldCommandBox.Text, out var oldId)
            || oldId < 0 || oldId > 0xFFFF)
        {
            MessageBox.Show(this, "L'ID source doit être un entier ou un hexadécimal valide, par exemple 0x3044.",
                "Édition ATEL", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryParseIntText(MonsterAiReplaceNewCommandBox.Text, out var newId)
            || newId < 0 || newId > 0xFFFF)
        {
            MessageBox.Show(this, "L'ID destination doit être un entier ou un hexadécimal valide, par exemple 0x3048.",
                "Édition ATEL", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (oldId == newId)
        {
            MessageBox.Show(this, "Les deux IDs sont identiques : aucune modification à prétester.",
                "Édition ATEL", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var oldName = LookupCommandNameInAnyLanguage(oldId);
        var newName = LookupCommandNameInAnyLanguage(newId);
        var prompt = $"Remplacer 0x{oldId:X4}" +
                     (!string.IsNullOrWhiteSpace(oldName) ? $" ({oldName})" : "") +
                     $" par 0x{newId:X4}" +
                     (!string.IsNullOrWhiteSpace(newName) ? $" ({newName})" : "") +
                     " dans les appels de commande ATEL.";

        var proposal = AtelDeterministicEditor.BuildReplaceCommandProposal(
            _currentMonsterAiScript,
            oldId,
            newId,
            oldName,
            newName);
        var validation = AtelPatchEngine.Validate(_currentMonsterAiFile, proposal);
        var report = BuildAtelPatchReport(proposal, validation);
        AppendMonsterAiCopilotBlock("Édition ATEL déterministe", prompt, report);

        if (!validation.Success || validation.PatchedBytes == null)
        {
            MonsterAiDeterministicEditStatusText.Text = "Prétest refusé";
            return;
        }

        var confirm = MessageBox.Show(this,
            "Prétest ATEL OK.\n\n" +
            $"{proposal.Operations.Count} occurrence(s) vont être remplacée(s).\n" +
            $"Taille : {validation.OriginalSize:N0} -> {validation.PatchedSize:N0} octets ({validation.SizeDelta:+#;-#;0}).\n\n" +
            "Appliquer ce remplacement en mémoire ?",
            "Remplacer commande ATEL",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            MonsterAiDeterministicEditStatusText.Text = "Prétest OK, non appliqué";
            return;
        }

        ApplyAtelPatch(validation.PatchedBytes, proposal, validation);
        MonsterAiDeterministicEditStatusText.Text =
            $"Remplacement appliqué : 0x{oldId:X4} -> 0x{newId:X4}";
    }

    private string BuildAtelPatchReport(AtelPatchProposal proposal, AtelPatchValidationResult validation)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Proposition LLM");
        sb.AppendLine("- " + (string.IsNullOrWhiteSpace(proposal.Summary) ? "(aucun résumé)" : proposal.Summary));
        if (proposal.Mechanics.Count > 0)
        {
            sb.AppendLine("- Mécaniques prévues :");
            foreach (var mechanic in proposal.Mechanics.Take(8))
                sb.AppendLine("  - " + mechanic);
        }
        if (!string.IsNullOrWhiteSpace(proposal.Risk))
            sb.AppendLine("- Risque annoncé : " + proposal.Risk);
        if (proposal.RequiresSceneAtel)
            sb.AppendLine("- Le LLM indique que l'ATEL de scène pourrait aussi être nécessaire.");

        sb.AppendLine();
        sb.AppendLine("Opérations");
        if (proposal.Operations.Count == 0)
        {
            sb.AppendLine("- Aucune opération byte-level proposée.");
        }
        else
        {
            foreach (var op in proposal.Operations.Take(24))
            {
                var bytesLabel = op.Bytes.Length == 0 ? "" : $" ; bytes={op.Bytes.Length}";
                sb.AppendLine($"- {op.Kind} 0x{op.Offset:X4} len={op.Length}{bytesLabel}" +
                              (string.IsNullOrWhiteSpace(op.Note) ? "" : $" — {op.Note}"));
            }
            if (proposal.Operations.Count > 24)
                sb.AppendLine($"- ... {proposal.Operations.Count - 24} opération(s) supplémentaire(s).");
        }

        sb.AppendLine();
        sb.AppendLine(validation.Success ? "Prétest interne OK" : "Prétest interne refusé");
        foreach (var message in validation.Messages)
            sb.AppendLine("- " + message);

        return sb.ToString();
    }

    private void ApplyAtelPatch(
        byte[] patchedBytes,
        AtelPatchProposal proposal,
        AtelPatchValidationResult validation)
    {
        if (_workspace == null || _currentMonsterAiItem == null || _currentMonsterAiFile == null)
            return;

        _currentMonsterAiFile.AiBytes = patchedBytes.ToArray();
        _currentMonsterAiFile.IsDirty = true;
        _workspace.RegisterEditedMonster(_currentMonsterAiItem.Entry.FullPath, _currentMonsterAiFile);

        _atelGlobalIndex = null;
        RefreshAtelIndexStatus();

        DisplayMonsterAi(_currentMonsterAiItem, _currentMonsterAiFile);
        if (_currentMonsterItem != null
            && string.Equals(_currentMonsterItem.Entry.FullPath, _currentMonsterAiItem.Entry.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            _currentMonsterFile = _currentMonsterAiFile;
            ChunksInfoText.Text = BuildChunksReport(_currentMonsterAiFile);
        }

        UpdateSaveStatusUI();
        StatusText.Text = $"Patch ATEL appliqué en mémoire : {validation.OriginalSize:N0} -> {validation.PatchedSize:N0} octets. Sauvegarde avec Ctrl+S.";

        var sb = new StringBuilder();
        sb.AppendLine("Patch ATEL appliqué en mémoire.");
        sb.AppendLine("Nouveaux ajouts / modifications :");
        if (proposal.Mechanics.Count == 0)
            sb.AppendLine("- " + (string.IsNullOrWhiteSpace(proposal.Summary) ? "Modification byte-level validée." : proposal.Summary));
        else
            foreach (var mechanic in proposal.Mechanics.Take(8))
                sb.AppendLine("- " + mechanic);
        sb.AppendLine();
        sb.AppendLine("Le fichier source n'est pas écrasé. Utilise Ctrl+S pour écrire le fichier modifié dans le dossier de sortie.");
        MonsterAiCopilotOutputBox.Text = sb.ToString();
        MonsterAiCopilotOutputBox.ScrollToEnd();
        MonsterAiLlmStatusText.Text = "Patch appliqué en mémoire";
    }

    private void AppendMonsterAiCopilotBlock(string header, string prompt, string answer)
    {
        var sb = new StringBuilder(MonsterAiCopilotOutputBox.Text);
        if (sb.Length > 0)
            sb.AppendLine().AppendLine();
        sb.AppendLine("-----");
        sb.AppendLine("Question");
        foreach (var line in prompt.Replace("\r\n", "\n").Split('\n'))
            sb.AppendLine("> " + line);
        sb.AppendLine();
        sb.AppendLine(header);
        sb.AppendLine(answer);

        MonsterAiCopilotOutputBox.Text = sb.ToString();
        MonsterAiCopilotOutputBox.ScrollToEnd();
    }

    private void OnMonsterAiCopilotQuickAsk_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string prompt })
        {
            MonsterAiCopilotInputBox.Text = UiLocalization.Translate(prompt);
            SubmitMonsterAiCopilotQuestion(prompt);
        }
    }

    private void OnMonsterAiCopilotInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            SubmitMonsterAiCopilotQuestion(MonsterAiCopilotInputBox.Text);
        }
    }

    private async void SubmitMonsterAiCopilotQuestion(string prompt)
    {
        prompt = prompt.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
            return;

        if (_currentMonsterAiScript == null || _currentMonsterAiItem == null)
        {
            MonsterAiCopilotOutputBox.Text = "Sélectionne un monstre pour activer le copilote ATEL.";
            MonsterAiCopilotAskButton.IsEnabled = false;
            return;
        }

        SaveAtelLlmSettingsFromUi(showStatus: false);
        var usingLlm = IsAtelLlmConfigured();
        if (usingLlm && _atelGlobalIndex == null)
            await EnsureAtelGlobalIndexAsync(force: false);

        var context = BuildAtelCopilotContext();
        var answerHeader = usingLlm ? "Réponse LLM" : "Réponse locale";
        string answer;

        MonsterAiCopilotAskButton.IsEnabled = false;
        MonsterAiLlmStatusText.Text = usingLlm ? "LLM en cours..." : "Mode local";
        try
        {
            if (usingLlm)
            {
                try
                {
                    answer = await AtelLlmCopilotClient.AnswerAsync(context, prompt, BuildAtelLlmOptions());
                    MonsterAiLlmStatusText.Text = "LLM OK";
                }
                catch (Exception ex)
                {
                    var fallback = AtelCopilot.Answer(context, prompt);
                    answerHeader = "Réponse locale (fallback)";
                    answer =
                        "Je n'ai pas réussi à joindre le LLM configuré, donc je repasse sur le copilote local pour ne pas te laisser sans réponse.\n\n" +
                        $"Détail technique : {ex.Message}\n\n" +
                        fallback;
                    MonsterAiLlmStatusText.Text = "LLM indisponible";
                }
            }
            else
            {
                answer = AtelCopilot.Answer(context, prompt);
            }
        }
        finally
        {
            MonsterAiCopilotAskButton.IsEnabled = true;
        }

        var sb = new StringBuilder(MonsterAiCopilotOutputBox.Text);
        if (sb.Length > 0)
            sb.AppendLine().AppendLine();
        sb.AppendLine("-----");
        sb.AppendLine("Question");
        var displayPrompt = UiLocalization.Translate(prompt);
        foreach (var line in displayPrompt.Replace("\r\n", "\n").Split('\n'))
            sb.AppendLine("> " + line);
        sb.AppendLine();
        sb.AppendLine(answerHeader);
        sb.AppendLine(answer);

        MonsterAiCopilotOutputBox.Text = sb.ToString();
        MonsterAiCopilotInputBox.Clear();
        MonsterAiCopilotOutputBox.ScrollToEnd();
    }

    private void OnMonsterAiLlmSaveSettings_Click(object sender, RoutedEventArgs e)
        => SaveAtelLlmSettingsFromUi(showStatus: true);

    private async void OnMonsterAiLlmTest_Click(object sender, RoutedEventArgs e)
    {
        SaveAtelLlmSettingsFromUi(showStatus: false);
        if (!IsAtelLlmConfigured())
        {
            MonsterAiLlmStatusText.Text = "LLM non configuré";
            MessageBox.Show(this,
                "Active LLM, renseigne un endpoint et un modèle avant le test.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MonsterAiLlmStatusText.Text = "Test LLM...";
        try
        {
            var context = _currentMonsterAiScript != null && _currentMonsterAiItem != null
                ? BuildAtelCopilotContext()
                : new AtelCopilotContext
                {
                    MonsterDisplayName = "Test",
                    MonsterFileName = "test",
                    Script = new AtelDecompiledScript(),
                    AnalysisText = "Test de connexion LLM sans monstre chargé.",
                };

            var answer = await AtelLlmCopilotClient.AnswerAsync(
                context,
                "Réponds en une seule phrase naturelle pour confirmer que le copilote LLM est disponible.",
                BuildAtelLlmOptions());

            MonsterAiLlmStatusText.Text = "LLM OK";
            MessageBox.Show(this, answer, "Test LLM", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MonsterAiLlmStatusText.Text = "Erreur LLM";
            MessageBox.Show(this, ex.Message, "Test LLM", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool IsAtelLlmConfigured()
    {
        return MonsterAiUseLlmCheckBox.IsChecked == true
               && !string.IsNullOrWhiteSpace(MonsterAiLlmEndpointBox.Text)
               && !string.IsNullOrWhiteSpace(MonsterAiLlmModelBox.Text);
    }

    private AtelLlmCopilotOptions BuildAtelLlmOptions()
    {
        return new AtelLlmCopilotOptions
        {
            Endpoint = MonsterAiLlmEndpointBox.Text.Trim(),
            Model = MonsterAiLlmModelBox.Text.Trim(),
            ApiKey = MonsterAiLlmApiKeyBox.Password.Trim(),
            ResponseLanguage = UiLocalization.IsEnglish ? "en" : "fr",
        };
    }

    private void SaveAtelLlmSettingsFromUi(bool showStatus)
    {
        if (_suppressAtelLlmSettingsEvents)
            return;

        _settings.AtelCopilotUseLlm = MonsterAiUseLlmCheckBox.IsChecked == true;
        _settings.AtelCopilotEndpoint = string.IsNullOrWhiteSpace(MonsterAiLlmEndpointBox.Text)
            ? null
            : MonsterAiLlmEndpointBox.Text.Trim();
        _settings.AtelCopilotModel = string.IsNullOrWhiteSpace(MonsterAiLlmModelBox.Text)
            ? null
            : MonsterAiLlmModelBox.Text.Trim();
        SettingsService.Save(_settings);

        if (showStatus)
            RefreshAtelLlmStatus(saved: true);
    }

    private void RefreshAtelLlmStatus(bool saved = false)
    {
        if (MonsterAiUseLlmCheckBox.IsChecked == true)
        {
            MonsterAiLlmStatusText.Text = saved
                ? "Config sauvegardée (clé non sauvegardée)"
                : "LLM actif";
        }
        else
        {
            MonsterAiLlmStatusText.Text = saved ? "Config sauvegardée" : "Mode local";
        }
    }

    private AtelAnalysisOptions BuildAtelAnalysisOptions()
    {
        return new AtelAnalysisOptions
        {
            ResolveCommandName = LookupCommandNameInAnyLanguage,
            ResolveCommandSource = id => _workspace?.LookupCommandSource(id),
            ResolveMonsterName = LookupMonsterFileNameForAtel,
        };
    }

    private string? LookupMonsterFileNameForAtel(int monsterNumber)
    {
        if (_workspace == null) return null;

        var fileName = $"m{monsterNumber:000}";
        var entry = _workspace.Scan.Monsters.FirstOrDefault(m =>
            string.Equals(Path.GetFileNameWithoutExtension(m.FullPath), fileName, StringComparison.OrdinalIgnoreCase));
        if (entry == null) return null;

        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage ?? entry.Language;
        return LookupMonsterName(entry, _workspace, lang);
    }

    private void OnMonsterAiMonster_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressMonsterAiSelectionEvents)
            return;

        if (MonsterAiMonsterSelector.SelectedItem is not MonsterListItem item)
        {
            ClearMonsterAi();
            return;
        }

        if (!ReferenceEquals(MonsterListBox.SelectedItem, item))
        {
            MonsterListBox.SelectedItem = item;
            MonsterListBox.ScrollIntoView(item);
            return;
        }

        try
        {
            var monsterFile = LoadMonsterFileForDisplay(item);
            DisplayMonsterAi(item, monsterFile);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossible de lire le fichier monstre :\n\n{item.Entry.FullPath}\n\n{ex.Message}",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Error);
            ClearMonsterAi();
        }
    }

    private void OnRefreshMonsterAi_Click(object sender, RoutedEventArgs e)
    {
        if (MonsterAiMonsterSelector.SelectedItem is not MonsterListItem item)
        {
            ClearMonsterAi();
            return;
        }

        try
        {
            var monsterFile = LoadMonsterFileForDisplay(item);
            DisplayMonsterAi(item, monsterFile);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossible de rafraîchir l'IA du monstre :\n\n{item.Entry.FullPath}\n\n{ex.Message}",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DisplayMonsterLoot(MonsterLoot? loot)
    {
        if (loot == null)
        {
            foreach (var box in MonsterLootBoxes())
                box.Text = "—";
            foreach (var combo in MonsterLootCombos())
            {
                combo.ItemsSource = null;
                combo.SelectedIndex = -1;
            }
            LootResolvedSummaryText.Text = "";
            return;
        }

        LootGilBox.Text = loot.Gil.ToString();
        LootApNormalBox.Text = loot.ApNormal.ToString();
        LootApOverkillBox.Text = loot.ApOverkill.ToString();
        SetLootCombo(LootRonsoRageCombo, BuildRonsoRageOptions(loot.RonsoRage), loot.RonsoRage);
        LootDropChancePrimaryBox.Text = loot.DropChancePrimary.ToString();
        LootDropChanceSecondaryBox.Text = loot.DropChanceSecondary.ToString();
        LootStealChanceBox.Text = loot.StealChance.ToString();
        LootDropChanceGearBox.Text = loot.DropChanceGear.ToString();
        LootArenaPriceBox.Text = loot.MonsterArenaPrice.ToString();

        SetLootItemCombo(LootPrimaryNormalCommonItemCombo, loot.DropNormalPrimaryCommonItem);
        SetLootItemCombo(LootPrimaryNormalRareItemCombo, loot.DropNormalPrimaryRareItem);
        SetLootItemCombo(LootSecondaryNormalCommonItemCombo, loot.DropNormalSecondaryCommonItem);
        SetLootItemCombo(LootSecondaryNormalRareItemCombo, loot.DropNormalSecondaryRareItem);
        LootPrimaryNormalCommonQtyBox.Text = loot.DropNormalPrimaryCommonQty.ToString();
        LootPrimaryNormalRareQtyBox.Text = loot.DropNormalPrimaryRareQty.ToString();
        LootSecondaryNormalCommonQtyBox.Text = loot.DropNormalSecondaryCommonQty.ToString();
        LootSecondaryNormalRareQtyBox.Text = loot.DropNormalSecondaryRareQty.ToString();

        SetLootItemCombo(LootPrimaryOkCommonItemCombo, loot.DropOverkillPrimaryCommonItem);
        SetLootItemCombo(LootPrimaryOkRareItemCombo, loot.DropOverkillPrimaryRareItem);
        SetLootItemCombo(LootSecondaryOkCommonItemCombo, loot.DropOverkillSecondaryCommonItem);
        SetLootItemCombo(LootSecondaryOkRareItemCombo, loot.DropOverkillSecondaryRareItem);
        LootPrimaryOkCommonQtyBox.Text = loot.DropOverkillPrimaryCommonQty.ToString();
        LootPrimaryOkRareQtyBox.Text = loot.DropOverkillPrimaryRareQty.ToString();
        LootSecondaryOkCommonQtyBox.Text = loot.DropOverkillSecondaryCommonQty.ToString();
        LootSecondaryOkRareQtyBox.Text = loot.DropOverkillSecondaryRareQty.ToString();

        SetLootItemCombo(LootStealCommonItemCombo, loot.StealCommonItem);
        SetLootItemCombo(LootStealRareItemCombo, loot.StealRareItem);
        LootStealCommonQtyBox.Text = loot.StealCommonQty.ToString();
        LootStealRareQtyBox.Text = loot.StealRareQty.ToString();
        SetLootItemCombo(LootBribeItemCombo, loot.BribeItem);
        LootBribeQtyBox.Text = loot.BribeQty.ToString();
        LootGilStealBox.Text = loot.GilStealByte.ToString();
        SetLootCombo(LootGearSlotsCombo, BuildGearRollOptions(loot.GearSlotCountByte, isSlotRoll: true), loot.GearSlotCountByte);
        SetLootCombo(LootGearAbilityCountCombo, BuildGearRollOptions(loot.GearAbilityCountByte, isSlotRoll: false), loot.GearAbilityCountByte);

        var weaponForced = GetUniformForcedGearAbility(loot.GearWeaponAbilitiesByCharacter);
        var armorForced = GetUniformForcedGearAbility(loot.GearArmorAbilitiesByCharacter);
        SetLootCombo(
            LootWeaponForcedAbilityCombo,
            BuildGearAbilityOptions(weaponForced ?? -1, GearAbilityKind.Weapon, includeKeepMixed: weaponForced == null),
            weaponForced ?? -1);
        SetLootCombo(
            LootArmorForcedAbilityCombo,
            BuildGearAbilityOptions(armorForced ?? -1, GearAbilityKind.Armor, includeKeepMixed: armorForced == null),
            armorForced ?? -1);
    }

    private TextBox[] MonsterLootBoxes() =>
    [
        LootGilBox, LootApNormalBox, LootApOverkillBox,
        LootDropChancePrimaryBox, LootDropChanceSecondaryBox, LootStealChanceBox,
        LootDropChanceGearBox, LootArenaPriceBox,
        LootPrimaryNormalCommonQtyBox, LootPrimaryNormalRareQtyBox,
        LootPrimaryOkCommonQtyBox, LootPrimaryOkRareQtyBox,
        LootSecondaryNormalCommonQtyBox, LootSecondaryNormalRareQtyBox,
        LootSecondaryOkCommonQtyBox, LootSecondaryOkRareQtyBox,
        LootStealCommonQtyBox, LootStealRareQtyBox,
        LootBribeQtyBox, LootGilStealBox,
    ];

    private ComboBox[] MonsterLootCombos() =>
    [
        LootRonsoRageCombo,
        LootPrimaryNormalCommonItemCombo, LootPrimaryNormalRareItemCombo,
        LootPrimaryOkCommonItemCombo, LootPrimaryOkRareItemCombo,
        LootSecondaryNormalCommonItemCombo, LootSecondaryNormalRareItemCombo,
        LootSecondaryOkCommonItemCombo, LootSecondaryOkRareItemCombo,
        LootStealCommonItemCombo, LootStealRareItemCombo, LootBribeItemCombo,
        LootGearSlotsCombo, LootGearAbilityCountCombo,
        LootWeaponForcedAbilityCombo, LootArmorForcedAbilityCombo,
    ];

    private void UpdateMonsterLootResolvedSummary()
    {
        if (_workspace == null || _currentMonsterFile?.Loot == null)
        {
            LootResolvedSummaryText.Text = "";
            return;
        }

        var lines = new List<string>
        {
            $"Émulattaque Kimahri : {FormatLootComboReference(LootRonsoRageCombo, null)}",
            $"Drop principal normal : commun {FormatLootComboReference(LootPrimaryNormalCommonItemCombo, LootPrimaryNormalCommonQtyBox)} ; rare {FormatLootComboReference(LootPrimaryNormalRareItemCombo, LootPrimaryNormalRareQtyBox)}",
            $"Drop principal overkill : commun {FormatLootComboReference(LootPrimaryOkCommonItemCombo, LootPrimaryOkCommonQtyBox)} ; rare {FormatLootComboReference(LootPrimaryOkRareItemCombo, LootPrimaryOkRareQtyBox)}",
            $"Drop secondaire normal : commun {FormatLootComboReference(LootSecondaryNormalCommonItemCombo, LootSecondaryNormalCommonQtyBox)} ; rare {FormatLootComboReference(LootSecondaryNormalRareItemCombo, LootSecondaryNormalRareQtyBox)}",
            $"Drop secondaire overkill : commun {FormatLootComboReference(LootSecondaryOkCommonItemCombo, LootSecondaryOkCommonQtyBox)} ; rare {FormatLootComboReference(LootSecondaryOkRareItemCombo, LootSecondaryOkRareQtyBox)}",
            $"Vol : commun {FormatLootComboReference(LootStealCommonItemCombo, LootStealCommonQtyBox)} ; rare {FormatLootComboReference(LootStealRareItemCombo, LootStealRareQtyBox)}",
            $"Pots-de-vin : {FormatLootComboReference(LootBribeItemCombo, LootBribeQtyBox)}",
        };

        if (TryParseIntText(LootStealChanceBox.Text, out var stealChance))
        {
            lines.Add($"Chance de vol : {stealChance}/255");
            if (stealChance <= 0 && HasDisplayedStealItem())
                lines.Add("Attention : vol inactif tant que la chance de vol reste à 0/255.");
        }

        if (TryParseIntText(LootGilStealBox.Text, out var gilStealByte))
            lines.Add($"Gils volés max : {FormatGil((long)gilStealByte * 100)}");

        if (TryGetSelectedLootId(LootGearSlotsCombo, out var slotByte))
            lines.Add($"Équipement : slots {DescribeGearRollByte(slotByte, isSlotRoll: true)} ; byte 0x{slotByte:X2}");
        if (TryGetSelectedLootId(LootGearAbilityCountCombo, out var abilityByte))
            lines.Add($"Équipement : aptitudes tirées {DescribeGearRollByte(abilityByte, isSlotRoll: false)} ; byte 0x{abilityByte:X2}");
        lines.Add($"Aptitude arme forcée : {FormatGearAbilityComboReference(LootWeaponForcedAbilityCombo)}");
        lines.Add($"Aptitude protection forcée : {FormatGearAbilityComboReference(LootArmorForcedAbilityCombo)}");

        var hp = ReadDisplayedMonsterHp();
        if (hp > 0)
        {
            lines.Add($"Coût pot-de-vin calculé : 20x HP = {FormatGil(hp * 20)} (guide) ; 25x HP = {FormatGil(hp * 25)} (garanti)");
            lines.Add("Coût non stocké dans le chunk loot : le fichier conserve seulement l'objet et la quantité.");
        }

        LootResolvedSummaryText.Text = string.Join(Environment.NewLine, lines);
    }

    private string FormatLootComboReference(ComboBox itemCombo, TextBox? quantityBox)
    {
        var qtyPrefix = "";
        if (quantityBox != null)
        {
            qtyPrefix = TryParseIntText(quantityBox.Text, out var qty)
                ? $"{qty}x "
                : "?x ";
        }

        if (itemCombo.SelectedItem is LootOption option)
            return option.Id == 0
                ? $"{qtyPrefix}aucun"
                : $"{qtyPrefix}{option.DisplayName}";

        return $"{qtyPrefix}(non sélectionné)";
    }

    private string FormatGearAbilityComboReference(ComboBox abilityCombo)
    {
        if (abilityCombo.SelectedItem is not LootOption option)
            return "(non sélectionné)";
        return option.Id switch
        {
            -1 => option.DisplayName,
            0 => "aucune",
            _ => option.DisplayName,
        };
    }

    private string FormatLootTextReference(TextBox itemBox, TextBox? quantityBox)
    {
        var qtyPrefix = "";
        if (quantityBox != null)
        {
            qtyPrefix = TryParseIntText(quantityBox.Text, out var qty)
                ? $"{qty}x "
                : "?x ";
        }

        if (!TryParseIntText(itemBox.Text, out var id))
            return $"{qtyPrefix}{itemBox.Text.Trim()} (ID invalide)";

        if (id == 0)
            return $"{qtyPrefix}aucun";

        var source = _workspace?.LookupCommandSource(id);
        var name = LookupCommandNameInAnyLanguage(id);
        if (!string.IsNullOrWhiteSpace(name))
            return $"{qtyPrefix}{name} ({source ?? "?"} 0x{id:X4})";

        return $"{qtyPrefix}0x{id:X4} (non trouvé)";
    }

    private void SetLootItemCombo(ComboBox combo, int id)
    {
        SetLootCombo(combo, BuildLootItemOptions(id), id);
    }

    private void SetLootCombo(ComboBox combo, List<LootOption> options, int id)
    {
        combo.ItemsSource = options;
        combo.SelectedValue = id;
        if (combo.SelectedIndex < 0 && options.Count > 0)
            combo.SelectedIndex = 0;
    }

    private List<LootOption> BuildLootItemOptions(int currentId)
    {
        var options = new List<LootOption> { new(0, "[0x0000] Aucun") };
        if (_workspace != null)
        {
            var lang = GetEffectiveItemLanguage() ?? _currentLanguage ?? _workspace.PreferredDisplayLanguage;
            if (lang != null && _workspace.ItemsByLanguage.TryGetValue(lang, out var file))
            {
                var charset = _workspace.GetCharsetForLanguage(lang);
                for (int i = 0; i < file.Count; i++)
                {
                    var id = file.MinIndex + i;
                    var name = file.GetName(i, charset);
                    if (string.IsNullOrWhiteSpace(name)) name = "(sans nom)";
                    options.Add(new LootOption(id, $"[0x{id:X4}] {name}"));
                }
            }
        }
        EnsureOptionExists(options, currentId, $"[0x{currentId:X4}] ID inconnu/non chargé");
        return options;
    }

    private List<LootOption> BuildRonsoRageOptions(int currentId)
    {
        var options = new List<LootOption> { new(0, "[0x0000] Aucune") };
        if (_workspace != null)
        {
            var lang = GetEffectiveCommandLanguage() ?? _currentLanguage ?? _workspace.PreferredDisplayLanguage;
            if (lang != null && _workspace.PlayerCommandsByLanguage.TryGetValue(lang, out var file))
            {
                var charset = _workspace.GetCharsetForLanguage(lang);
                for (int i = 0; i < file.Count; i++)
                {
                    var command = file.Attacks[i];
                    if (command.CharacterUser != PlayerCharacters.Kimahri || command.CostOD <= 0)
                        continue;

                    var id = file.MinIndex + i;
                    var name = file.GetName(i, charset);
                    if (string.IsNullOrWhiteSpace(name)) name = "(sans nom)";
                    options.Add(new LootOption(id, $"[0x{id:X4}] {name}"));
                }
            }
        }

        if (currentId > 0)
            EnsureOptionExists(options, currentId, $"[0x{currentId:X4}] Emulattaque inconnue/non chargée");
        return options;
    }

    private List<LootOption> BuildGearRollOptions(int currentByte, bool isSlotRoll)
    {
        var rawValues = isSlotRoll
            ? new[] { 0x04, 0x08, 0x0C, 0x10, 0x14 }
            : new[] { 0x04, 0x0C, 0x14, 0x1C, 0x24 };

        var options = rawValues
            .Select(v => new LootOption(v, $"[0x{v:X2}] {DescribeGearRollByte(v, isSlotRoll)}"))
            .ToList();
        EnsureOptionExists(options, currentByte, $"[0x{currentByte:X2}] {DescribeGearRollByte(currentByte, isSlotRoll)} (actuel)");
        return options;
    }

    private List<LootOption> BuildGearAbilityOptions(int currentId, GearAbilityKind kind, bool includeKeepMixed)
    {
        var options = new List<LootOption>();
        if (includeKeepMixed)
            options.Add(new LootOption(-1, "Conserver valeurs par personnage (mixte)"));
        options.Add(new LootOption(0, "[0x0000] Aucune aptitude forcée"));

        if (_workspace != null)
        {
            var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage;
            if (lang == null || !_workspace.AbilitiesByLanguage.ContainsKey(lang))
                lang = _workspace.AbilitiesByLanguage.Keys.FirstOrDefault();

            if (lang != null && _workspace.AbilitiesByLanguage.TryGetValue(lang, out var file))
            {
                var charset = _workspace.GetCharsetForLanguage(lang);
                var custos = _workspace.GearCustomizations;
                for (int i = 0; i < file.Count; i++)
                {
                    var ability = file.Entries[i];
                    if (ability.IsEmpty) continue;

                    var id = file.MinIndex + i;
                    if (custos != null)
                    {
                        var allowed = kind == GearAbilityKind.Weapon
                            ? custos.IsWeaponAbility(id)
                            : custos.IsArmorAbility(id);
                        if (!allowed) continue;
                    }

                    var name = file.GetName(ability, charset);
                    if (string.IsNullOrWhiteSpace(name)) name = "(sans nom)";
                    options.Add(new LootOption(id, $"[0x{id:X4}] {name}"));
                }
            }
        }

        if (currentId > 0)
            EnsureOptionExists(options, currentId, $"[0x{currentId:X4}] Aptitude inconnue/non chargée");
        return options;
    }

    private static void EnsureOptionExists(List<LootOption> options, int id, string displayName)
    {
        if (options.All(o => o.Id != id))
            options.Insert(Math.Min(1, options.Count), new LootOption(id, displayName));
    }

    private static string DescribeGearRollByte(int countByte, bool isSlotRoll)
    {
        var multiplier = isSlotRoll ? 0.25 : 0.125;
        var min = (countByte - 4) * multiplier;
        var max = (countByte + 3) * multiplier;
        if (isSlotRoll && min >= 4)
        {
            min = max = 4;
        }
        else if (max < 2)
        {
            if (isSlotRoll)
            {
                min = max = 1;
            }
            else if (max < 1)
            {
                min = max = 0;
            }
        }

        var label = isSlotRoll ? "slot" : "aptitude";
        var minInt = (int)min;
        var maxInt = (int)max;
        if (minInt == maxInt)
            return $"{minInt} {label}{(minInt > 1 ? "s" : "")}";
        return $"{minInt}-{maxInt} {label}s";
    }

    private static int? GetUniformForcedGearAbility(int[][] matrix)
    {
        if (matrix.Length == 0 || matrix[0].Length == 0) return 0;
        var value = matrix[0][0];
        foreach (var row in matrix)
        {
            if (row.Length == 0 || row[0] != value)
                return null;
        }
        return value;
    }

    private static void SetForcedGearAbilityForAllCharacters(int[][] matrix, int abilityId)
    {
        foreach (var row in matrix)
        {
            if (row.Length > 0)
                row[0] = abilityId;
        }
    }

    private bool TryReadLootCombo(ComboBox combo, string label, out int value)
    {
        if (TryGetSelectedLootId(combo, out value))
            return true;

        MessageBox.Show(this,
            $"{label} doit être sélectionné dans la liste.",
            "Valeur invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
        combo.Focus();
        return false;
    }

    private static bool TryGetSelectedLootId(ComboBox combo, out int value)
    {
        if (combo.SelectedValue is int selectedValue)
        {
            value = selectedValue;
            return true;
        }

        if (combo.SelectedItem is LootOption option)
        {
            value = option.Id;
            return true;
        }

        value = 0;
        return false;
    }

    private void OnMonsterLootCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        NormalizeDisplayedStealFields(sender);
        OnMonsterStat_Changed(sender, e);
    }

    private void NormalizeDisplayedStealFields(object sender)
    {
        if (_suppressMonsterStatEvents) return;
        if (!ReferenceEquals(sender, LootStealCommonItemCombo)
            && !ReferenceEquals(sender, LootStealRareItemCombo))
            return;

        if (TryGetSelectedLootId(LootStealCommonItemCombo, out var commonItem)
            && commonItem > 0
            && (!TryParseIntText(LootStealCommonQtyBox.Text, out var commonQty) || commonQty <= 0))
        {
            LootStealCommonQtyBox.Text = "1";
        }

        if (TryGetSelectedLootId(LootStealRareItemCombo, out var rareItem)
            && rareItem > 0
            && (!TryParseIntText(LootStealRareQtyBox.Text, out var rareQty) || rareQty <= 0))
        {
            LootStealRareQtyBox.Text = "1";
        }

        if (HasDisplayedStealItem()
            && (!TryParseIntText(LootStealChanceBox.Text, out var stealChance) || stealChance <= 0))
        {
            LootStealChanceBox.Text = "255";
        }
    }

    private bool HasDisplayedStealItem()
    {
        return TryGetSelectedLootId(LootStealCommonItemCombo, out var commonItem) && commonItem > 0
            || TryGetSelectedLootId(LootStealRareItemCombo, out var rareItem) && rareItem > 0;
    }

    private enum GearAbilityKind
    {
        Weapon,
        Armor,
    }

    private sealed class LootOption
    {
        public int Id { get; }
        public string DisplayName { get; }
        public string SearchText => Id >= 0 ? $"{DisplayName} 0x{Id:X4}" : DisplayName;

        public LootOption(int id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }
    }

    private string? LookupCommandNameInAnyLanguage(int globalId)
    {
        if (_workspace == null) return null;

        var languages = new List<string>();
        if (_currentLanguage != null) languages.Add(_currentLanguage);
        if (_workspace.PreferredDisplayLanguage != null) languages.Add(_workspace.PreferredDisplayLanguage);
        languages.AddRange(_workspace.AvailableLanguages);

        foreach (var lang in languages.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var name = _workspace.LookupCommandName(globalId, lang);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        return null;
    }

    private long ReadDisplayedMonsterHp()
    {
        if (TryParseIntText(StatHpBox.Text, out var displayedHp) && displayedHp > 0)
            return displayedHp;
        return _currentMonsterFile?.StatSheet?.Hp > 0 ? _currentMonsterFile.StatSheet.Hp : 0;
    }

    private static bool TryParseIntText(string rawText, out int value)
    {
        var raw = rawText.Trim().Replace("%", "").Trim();
        var isHex = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        var numberText = isHex ? raw[2..] : raw;
        var style = isHex
            ? System.Globalization.NumberStyles.HexNumber
            : System.Globalization.NumberStyles.Integer;

        return int.TryParse(numberText, style, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private static string FormatGil(long value) => $"{value:N0} gils";

    /// <summary>
    /// Remplit les champs Sensor/Scan/etc. depuis la base de localisation
    /// dans la langue actuellement sélectionnée. Désactive les TextChanged events
    /// le temps du remplissage pour ne pas marquer le monstre comme modifié.
    /// </summary>
    private void DisplayLocalizedTexts(int monsterIdx)
    {
        _suppressMonsterTextEvents = true;
        try
        {
            if (_workspace == null || _currentLanguage == null)
            {
                LocalizedTextsGroup.Header = "Textes du Bestiaire (aucune langue chargée)";
                LocalizedNameBox.Text = "—";
                LocalizedSensorBox.Text = "—";
                LocalizedSimplifiedSensorBox.Text = "—";
                LocalizedScanBox.Text = "—";
                LocalizedSimplifiedScanBox.Text = "—";
                SetMonsterEditControlsEnabled(false, "Aucune langue chargée — édition désactivée");
                return;
            }

            var langName = LanguageDisplayName(_currentLanguage);
            LocalizedTextsGroup.Header = $"Textes du Bestiaire — {langName}";

            var texts = _workspace.GetMonsterTexts(monsterIdx, _currentLanguage);
            LocalizedNameBox.Text             = texts?.Name                 ?? "";
            LocalizedSensorBox.Text           = texts?.SensorText           ?? "";
            LocalizedSimplifiedSensorBox.Text = texts?.SimplifiedSensorText ?? "";
            LocalizedScanBox.Text             = texts?.ScanText             ?? "";
            LocalizedSimplifiedScanBox.Text   = texts?.SimplifiedScanText   ?? "";

            // Active l'édition uniquement si on a une vraie base de localisation
            var canEdit = _workspace.LocalizationDatabases.ContainsKey(_currentLanguage)
                          && texts != null;
            SetMonsterEditControlsEnabled(canEdit,
                canEdit ? "" : "Aucune donnée de localisation trouvée pour cette langue.");
            ApplyMonsterTextsButton.IsEnabled = false;
            RevertMonsterTextsButton.IsEnabled = false;
        }
        finally
        {
            _suppressMonsterTextEvents = false;
        }
    }

    /// <summary>Active/désactive les TextBoxes d'édition + affiche un statut.</summary>
    private void SetMonsterEditControlsEnabled(bool enabled, string statusMessage)
    {
        LocalizedNameBox.IsReadOnly             = !enabled;
        LocalizedSensorBox.IsReadOnly           = !enabled;
        LocalizedSimplifiedSensorBox.IsReadOnly = !enabled;
        LocalizedScanBox.IsReadOnly             = !enabled;
        LocalizedSimplifiedScanBox.IsReadOnly   = !enabled;

        var bg = enabled ? System.Windows.Media.Brushes.White
                         : new System.Windows.Media.SolidColorBrush(
                               System.Windows.Media.Color.FromRgb(0xFA, 0xFA, 0xFA));
        LocalizedNameBox.Background             = bg;
        LocalizedSensorBox.Background           = bg;
        LocalizedSimplifiedSensorBox.Background = bg;
        LocalizedScanBox.Background             = bg;
        LocalizedSimplifiedScanBox.Background   = bg;

        MonsterEditStatusText.Text = statusMessage;
    }

    // ============================================================
    // HANDLERS D'ÉDITION DES TEXTES MONSTRES
    // ============================================================

    private void OnMonsterText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressMonsterTextEvents) return;
        ApplyMonsterTextsButton.IsEnabled = true;
        RevertMonsterTextsButton.IsEnabled = true;
        MonsterEditStatusText.Text = "● Modifications non appliquées";
    }

    /// <summary>
    /// Intercepte Entrée dans les zones multi-lignes pour insérer "{\n}" plutôt
    /// qu'un retour à la ligne physique (qui n'a pas de sens dans le format FFX).
    /// </summary>
    private void OnMonsterTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        // Shift+Entrée laisse passer un vrai retour à la ligne (utile pour la lisibilité
        // visuelle de l'utilisateur sans impact sur les bytes finaux — on collapsera).
        if (e.Key == Key.Enter &&
            (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            InsertAtCaret(tb, "{\\n}");
            e.Handled = true;
        }
    }

    private void OnInsertToken_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string token) return;
        InsertIntoActiveMonsterTextBox(token);
    }

    private void OnInsertColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InsertColorBox.SelectedItem is not ComboBoxItem item) return;
        var color = item.Tag as string;
        if (string.IsNullOrEmpty(color)) return;

        InsertIntoActiveMonsterTextBox($"{{CLR:{color}}}");
        // Réinitialiser le ComboBox sur la 1ère entrée
        InsertColorBox.SelectedIndex = 0;
    }

    /// <summary>Insère le token donné dans la TextBox actuellement focused, ou Sensor par défaut.</summary>
    private void InsertIntoActiveMonsterTextBox(string token)
    {
        var target = Keyboard.FocusedElement as TextBox;
        // Vérifier que c'est bien une de nos TextBoxes éditables
        if (target != LocalizedNameBox && target != LocalizedSensorBox
            && target != LocalizedSimplifiedSensorBox && target != LocalizedScanBox
            && target != LocalizedSimplifiedScanBox)
        {
            // Fallback : Scan (le plus courant pour les codes de contrôle)
            target = LocalizedScanBox;
            target.Focus();
        }
        InsertAtCaret(target, token);
    }

    /// <summary>Insère un texte à la position du curseur dans une TextBox.</summary>
    private static void InsertAtCaret(TextBox tb, string text)
    {
        var pos = tb.CaretIndex;
        tb.Text = tb.Text.Insert(pos, text);
        tb.CaretIndex = pos + text.Length;
        tb.Focus();
    }

    private void OnRevertMonsterTexts_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMonsterFile?.StatSheet == null) return;
        DisplayLocalizedTexts(_currentMonsterFile.StatSheet.MonsterIdx);
    }

    private void OnApplyMonsterTexts_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || _currentLanguage == null
            || _currentMonsterItem == null || _currentMonsterFile?.StatSheet == null) return;
        var globalIdx = _currentMonsterFile.StatSheet.MonsterIdx;

        if (!_workspace.LocalizationDatabases.TryGetValue(_currentLanguage, out var db))
        {
            MessageBox.Show(this, $"Pas de base de localisation chargée pour {_currentLanguage}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var charset = db.Charset;
        if (charset == null)
        {
            MessageBox.Show(this,
                "La charset de cette langue n'est pas chargée — impossible de réencoder les textes.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryResolveUnsupportedChars(charset, _currentLanguage ?? "?",
                LocalizedNameBox, LocalizedSensorBox, LocalizedSimplifiedSensorBox,
                LocalizedScanBox, LocalizedSimplifiedScanBox))
            return;

        var newTexts = new LocalizedMonsterTexts
        {
            Name                 = LocalizedNameBox.Text,
            SensorText           = LocalizedSensorBox.Text,
            SimplifiedSensorText = LocalizedSimplifiedSensorBox.Text,
            ScanText             = LocalizedScanBox.Text,
            SimplifiedScanText   = LocalizedSimplifiedScanBox.Text,
        };

        // Recherche le fichier monsterN.bin qui contient ce monstre
        var found = db.FindFileForMonster(globalIdx);
        if (found == null)
        {
            MessageBox.Show(this,
                $"Aucun fichier monsterN.bin ne couvre l'index {globalIdx:X4} dans la langue {_currentLanguage}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var (file, _, relIdx) = found.Value;
        if (!file.SetTexts(relIdx, newTexts, charset))
        {
            MessageBox.Show(this, "Échec de l'écriture en mémoire (index invalide).",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Met à jour la sidebar (le nom peut avoir changé)
        _currentMonsterItem.DecodedName = newTexts.Name;
        var idx = _monsterListItems.IndexOf(_currentMonsterItem);
        if (idx >= 0)
        {
            _monsterListItems.RemoveAt(idx);
            _monsterListItems.Insert(idx, _currentMonsterItem);
            MonsterListBox.SelectedItem = _currentMonsterItem;
        }

        ApplyMonsterTextsButton.IsEnabled = false;
        RevertMonsterTextsButton.IsEnabled = false;
        MonsterEditStatusText.Text = "✓ Modifications appliquées (sauvegarde avec Ctrl+S)";
        UpdateSaveStatusUI();
    }

    // ============================================================
    // HANDLERS D'ÉDITION DES STATS MONSTRES
    // ============================================================

    private void OnMonsterStat_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressMonsterStatEvents) return;
        UpdateMonsterLootResolvedSummary();
        ApplyMonsterStatsButton.IsEnabled = true;
        RevertMonsterStatsButton.IsEnabled = true;
        MonsterStatsEditStatusText.Text = "● Stats/drops non appliqués";
    }

    private void OnRevertMonsterStats_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMonsterItem != null && _currentMonsterFile != null)
            DisplayMonster(_currentMonsterItem, _currentMonsterFile);
    }

    private void OnApplyMonsterStats_Click(object sender, RoutedEventArgs e)
    {
        _lastMonsterStatsApplySucceeded = false;
        if (_workspace == null || _currentMonsterItem == null
            || _currentMonsterFile?.StatSheet == null) return;

        var stat = _currentMonsterFile.StatSheet;

        if (!TryReadIntBox(StatHpBox, "HP max", 0, int.MaxValue, out var hp)) return;
        if (!TryReadIntBox(StatMpBox, "MP max", 0, int.MaxValue, out var mp)) return;
        if (!TryReadIntBox(StatOverkillBox, "Overkill", 0, int.MaxValue, out var overkill)) return;
        if (!TryReadIntBox(StatStrBox, "Force", 0, 255, out var str)) return;
        if (!TryReadIntBox(StatDefBox, "Défense", 0, 255, out var def)) return;
        if (!TryReadIntBox(StatMagBox, "Magie", 0, 255, out var mag)) return;
        if (!TryReadIntBox(StatMdfBox, "Défense magique", 0, 255, out var mdf)) return;
        if (!TryReadIntBox(StatAgiBox, "Agilité", 0, 255, out var agi)) return;
        if (!TryReadIntBox(StatLckBox, "Chance", 0, 255, out var lck)) return;
        if (!TryReadIntBox(StatEvaBox, "Esquive", 0, 255, out var eva)) return;
        if (!TryReadIntBox(StatAccBox, "Précision", 0, 255, out var acc)) return;
        if (!TryReadIntBox(StatPoisonBox, "Poison %", 0, 255, out var poison)) return;

        if (!TryReadIntBox(MetaForcedActionBox, "Action forcée", 0, 0xFFFF, out var forcedAction)) return;
        if (!TryReadIntBox(MetaMonsterIdxBox, "ID monstre", 0, 0xFFFF, out var monsterIdx)) return;
        if (!TryReadIntBox(MetaModelIdxBox, "ID modèle", 0, 0xFFFF, out var modelIdx)) return;
        if (!TryReadIntBox(MetaDoomBox, "Compteur Doom", 0, 255, out var doom)) return;
        if (!TryReadIntBox(MetaArenaBox, "ID arène", 0, 0xFFFF, out var arena)) return;
        if (!TryReadIntBox(MetaSoundBankBox, "Banque audio", 0, 0xFFFF, out var soundBank)) return;
        if (!TryReadIntBox(MetaCtbIconBox, "Icône CTB", 0, 255, out var ctbIcon)) return;

        if (!TryReadStatus("Death", out var rstDeath)) return;
        if (!TryReadStatus("Zombie", out var rstZombie)) return;
        if (!TryReadStatus("Petrify", out var rstPetrify)) return;
        if (!TryReadStatus("Poison", out var rstPoison)) return;
        if (!TryReadStatus("PowerBreak", out var rstPowerBreak)) return;
        if (!TryReadStatus("MagicBreak", out var rstMagicBreak)) return;
        if (!TryReadStatus("ArmorBreak", out var rstArmorBreak)) return;
        if (!TryReadStatus("MentalBreak", out var rstMentalBreak)) return;
        if (!TryReadStatus("Confuse", out var rstConfuse)) return;
        if (!TryReadStatus("Berserk", out var rstBerserk)) return;
        if (!TryReadStatus("Provoke", out var rstProvoke)) return;
        if (!TryReadStatus("Threaten", out var rstThreaten)) return;
        if (!TryReadStatus("Sleep", out var rstSleep)) return;
        if (!TryReadStatus("Silence", out var rstSilence)) return;
        if (!TryReadStatus("Darkness", out var rstDarkness)) return;
        if (!TryReadStatus("Shell", out var rstShell)) return;
        if (!TryReadStatus("Protect", out var rstProtect)) return;
        if (!TryReadStatus("Reflect", out var rstReflect)) return;
        if (!TryReadStatus("NTide", out var rstNTide)) return;
        if (!TryReadStatus("NBlaze", out var rstNBlaze)) return;
        if (!TryReadStatus("NShock", out var rstNShock)) return;
        if (!TryReadStatus("NFrost", out var rstNFrost)) return;
        if (!TryReadStatus("Regen", out var rstRegen)) return;
        if (!TryReadStatus("Haste", out var rstHaste)) return;
        if (!TryReadStatus("Slow", out var rstSlow)) return;
        if (_currentMonsterFile.Loot != null && !TryApplyMonsterLoot(_currentMonsterFile.Loot)) return;

        stat.Hp = hp;
        stat.Mp = mp;
        stat.OverkillThreshold = overkill;
        stat.Str = str;
        stat.Def = def;
        stat.Mag = mag;
        stat.Mdf = mdf;
        stat.Agi = agi;
        stat.Lck = lck;
        stat.Eva = eva;
        stat.Acc = acc;
        stat.PoisonDamage = poison;

        stat.ElementAbsorb = ReadBitfieldFromChecks(ElementsGrid, "ElementAbsorb");
        stat.ElementImmune = ReadBitfieldFromChecks(ElementsGrid, "ElementImmune");
        stat.ElementResist = ReadBitfieldFromChecks(ElementsGrid, "ElementResist");
        stat.ElementWeak = ReadBitfieldFromChecks(ElementsGrid, "ElementWeak");

        stat.StatusResistChanceDeath = rstDeath;
        stat.StatusResistChanceZombie = rstZombie;
        stat.StatusResistChancePetrify = rstPetrify;
        stat.StatusResistChancePoison = rstPoison;
        stat.StatusResistChancePowerBreak = rstPowerBreak;
        stat.StatusResistChanceMagicBreak = rstMagicBreak;
        stat.StatusResistChanceArmorBreak = rstArmorBreak;
        stat.StatusResistChanceMentalBreak = rstMentalBreak;
        stat.StatusResistChanceConfuse = rstConfuse;
        stat.StatusResistChanceBerserk = rstBerserk;
        stat.StatusResistChanceProvoke = rstProvoke;
        stat.StatusChanceThreaten = rstThreaten;
        stat.StatusResistChanceSleep = rstSleep;
        stat.StatusResistChanceSilence = rstSilence;
        stat.StatusResistChanceDarkness = rstDarkness;
        stat.StatusResistChanceShell = rstShell;
        stat.StatusResistChanceProtect = rstProtect;
        stat.StatusResistChanceReflect = rstReflect;
        stat.StatusResistChanceNTide = rstNTide;
        stat.StatusResistChanceNBlaze = rstNBlaze;
        stat.StatusResistChanceNShock = rstNShock;
        stat.StatusResistChanceNFrost = rstNFrost;
        stat.StatusResistChanceRegen = rstRegen;
        stat.StatusResistChanceHaste = rstHaste;
        stat.StatusResistChanceSlow = rstSlow;

        stat.AutoStatusesPermanent = ReadBitfieldFromChecks(AutoStatusPermanentPanel, "AutoPermanent");
        stat.AutoStatusesTemporal = ReadBitfieldFromChecks(AutoStatusTemporalPanel, "AutoTemporal");
        stat.AutoStatusesExtra = ReadBitfieldFromChecks(AutoStatusExtraPanel, "AutoExtra");
        stat.ExtraStatusImmunities = ReadBitfieldFromChecks(ExtraStatusImmunityPanel, "ExtraImmunity");

        stat.Armored = IsChecked("Immunity:Armored");
        stat.ImmunityFractionalDamage = IsChecked("Immunity:Fractional");
        stat.ImmunityLife = IsChecked("Immunity:Life");
        stat.ImmunitySensor = IsChecked("Immunity:Sensor");
        stat.ImmunityScanAgainOrWhat = IsChecked("Immunity:ScanAgain");
        stat.ImmunityPhysicalDamage = IsChecked("Immunity:Physical");
        stat.ImmunityMagicalDamage = IsChecked("Immunity:Magical");
        stat.ImmunityHpDamage = IsChecked("Immunity:Hp");
        stat.ImmunityCtbDamage = IsChecked("Immunity:Ctb");
        stat.ImmunitySlice = IsChecked("Immunity:Slice");
        stat.ImmunityBribe = IsChecked("Immunity:Bribe");

        stat.ForcedAction = forcedAction;
        stat.MonsterIdx = monsterIdx;
        stat.ModelIdx = modelIdx;
        stat.DoomCounter = doom;
        stat.MonsterArenaIdx = arena;
        stat.SoundBankRef = soundBank;
        stat.CtbIconType = ctbIcon;

        if (_currentMonsterFile.Loot != null)
            _currentMonsterFile.LootBytes = _currentMonsterFile.Loot.WriteToBytes();

        _currentMonsterFile.IsDirty = true;
        _workspace.RegisterEditedMonster(_currentMonsterItem.Entry.FullPath, _currentMonsterFile);

        ApplyMonsterStatsButton.IsEnabled = false;
        RevertMonsterStatsButton.IsEnabled = false;
        MonsterStatsEditStatusText.Text = "✓ Stats/drops appliqués (sauvegarde avec Ctrl+S)";
        ChunksInfoText.Text = BuildChunksReport(_currentMonsterFile);
        UpdateSaveStatusUI();
        _lastMonsterStatsApplySucceeded = true;
    }

    private void OnCopyMonsterMechanics_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMonsterFile?.StatSheet == null)
            return;

        if (ApplyMonsterStatsButton.IsEnabled)
        {
            OnApplyMonsterStats_Click(sender, e);
            if (!_lastMonsterStatsApplySucceeded) return;
        }

        _copiedMonsterMechanics = CreateMonsterMechanicsSnapshot(_currentMonsterFile);
        PasteMonsterMechanicsButton.IsEnabled = true;
        MonsterStatsEditStatusText.Text = "✓ Mécaniques du monstre copiées";
    }

    private void OnPasteMonsterMechanics_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || _currentMonsterItem == null
            || _currentMonsterFile?.StatSheet == null || _copiedMonsterMechanics == null)
            return;

        _currentMonsterFile.StatSheet =
            BuildMonsterStatWithCopiedMechanics(_copiedMonsterMechanics.Stat, _currentMonsterFile.StatSheet);

        if (_copiedMonsterMechanics.Loot != null)
        {
            _currentMonsterFile.Loot = CloneMonsterLoot(_copiedMonsterMechanics.Loot);
            _currentMonsterFile.LootBytes = _currentMonsterFile.Loot.WriteToBytes();
        }

        _currentMonsterFile.IsDirty = true;
        _workspace.RegisterEditedMonster(_currentMonsterItem.Entry.FullPath, _currentMonsterFile);

        DisplayMonster(_currentMonsterItem, _currentMonsterFile);
        CopyMonsterMechanicsButton.IsEnabled = true;
        PasteMonsterMechanicsButton.IsEnabled = true;
        MonsterStatsEditStatusText.Text = "✓ Mécaniques collées (textes et ID localisé préservés, sauvegarde avec Ctrl+S)";
        ChunksInfoText.Text = BuildChunksReport(_currentMonsterFile);
        UpdateSaveStatusUI();
    }

    private bool TryApplyMonsterLoot(MonsterLoot loot)
    {
        if (!TryReadIntBox(LootGilBox, "Gils", 0, 0xFFFF, out var gil)) return false;
        if (!TryReadIntBox(LootApNormalBox, "EXP normal", 0, 0xFFFF, out var apNormal)) return false;
        if (!TryReadIntBox(LootApOverkillBox, "EXP overkill", 0, 0xFFFF, out var apOverkill)) return false;
        if (!TryReadLootCombo(LootRonsoRageCombo, "Émulattaque Kimahri", out var ronsoRage)) return false;
        if (!TryReadIntBox(LootDropChancePrimaryBox, "Drop principal", 0, 255, out var dropChancePrimary)) return false;
        if (!TryReadIntBox(LootDropChanceSecondaryBox, "Drop secondaire", 0, 255, out var dropChanceSecondary)) return false;
        if (!TryReadIntBox(LootStealChanceBox, "Vol chance", 0, 255, out var stealChance)) return false;
        if (!TryReadIntBox(LootDropChanceGearBox, "Drop équipement", 0, 255, out var dropChanceGear)) return false;
        if (!TryReadIntBox(LootArenaPriceBox, "Prix arène", 0, int.MaxValue, out var arenaPrice)) return false;

        if (!TryReadLootCombo(LootPrimaryNormalCommonItemCombo, "Drop principal normal commun", out var primaryNormalCommonItem)) return false;
        if (!TryReadIntBox(LootPrimaryNormalCommonQtyBox, "Quantité drop principal normal commun", 0, 255, out var primaryNormalCommonQty)) return false;
        if (!TryReadLootCombo(LootPrimaryNormalRareItemCombo, "Drop principal normal rare", out var primaryNormalRareItem)) return false;
        if (!TryReadIntBox(LootPrimaryNormalRareQtyBox, "Quantité drop principal normal rare", 0, 255, out var primaryNormalRareQty)) return false;
        if (!TryReadLootCombo(LootPrimaryOkCommonItemCombo, "Drop principal overkill commun", out var primaryOkCommonItem)) return false;
        if (!TryReadIntBox(LootPrimaryOkCommonQtyBox, "Quantité drop principal overkill commun", 0, 255, out var primaryOkCommonQty)) return false;
        if (!TryReadLootCombo(LootPrimaryOkRareItemCombo, "Drop principal overkill rare", out var primaryOkRareItem)) return false;
        if (!TryReadIntBox(LootPrimaryOkRareQtyBox, "Quantité drop principal overkill rare", 0, 255, out var primaryOkRareQty)) return false;

        if (!TryReadLootCombo(LootSecondaryNormalCommonItemCombo, "Drop secondaire normal commun", out var secondaryNormalCommonItem)) return false;
        if (!TryReadIntBox(LootSecondaryNormalCommonQtyBox, "Quantité drop secondaire normal commun", 0, 255, out var secondaryNormalCommonQty)) return false;
        if (!TryReadLootCombo(LootSecondaryNormalRareItemCombo, "Drop secondaire normal rare", out var secondaryNormalRareItem)) return false;
        if (!TryReadIntBox(LootSecondaryNormalRareQtyBox, "Quantité drop secondaire normal rare", 0, 255, out var secondaryNormalRareQty)) return false;
        if (!TryReadLootCombo(LootSecondaryOkCommonItemCombo, "Drop secondaire overkill commun", out var secondaryOkCommonItem)) return false;
        if (!TryReadIntBox(LootSecondaryOkCommonQtyBox, "Quantité drop secondaire overkill commun", 0, 255, out var secondaryOkCommonQty)) return false;
        if (!TryReadLootCombo(LootSecondaryOkRareItemCombo, "Drop secondaire overkill rare", out var secondaryOkRareItem)) return false;
        if (!TryReadIntBox(LootSecondaryOkRareQtyBox, "Quantité drop secondaire overkill rare", 0, 255, out var secondaryOkRareQty)) return false;

        if (!TryReadLootCombo(LootStealCommonItemCombo, "Vol commun", out var stealCommonItem)) return false;
        if (!TryReadIntBox(LootStealCommonQtyBox, "Quantité vol commun", 0, 255, out var stealCommonQty)) return false;
        if (!TryReadLootCombo(LootStealRareItemCombo, "Vol rare", out var stealRareItem)) return false;
        if (!TryReadIntBox(LootStealRareQtyBox, "Quantité vol rare", 0, 255, out var stealRareQty)) return false;
        if (!TryReadLootCombo(LootBribeItemCombo, "Pots-de-vin item", out var bribeItem)) return false;
        if (!TryReadIntBox(LootBribeQtyBox, "Quantité pots-de-vin", 0, 255, out var bribeQty)) return false;
        if (!TryReadIntBox(LootGilStealBox, "Gil steal", 0, 255, out var gilSteal)) return false;
        if (!TryReadLootCombo(LootGearSlotsCombo, "Slots équipement", out var gearSlots)) return false;
        if (!TryReadLootCombo(LootGearAbilityCountCombo, "Aptitudes équipement", out var gearAbilities)) return false;
        if (!TryReadLootCombo(LootWeaponForcedAbilityCombo, "Aptitude arme forcée", out var forcedWeaponAbility)) return false;
        if (!TryReadLootCombo(LootArmorForcedAbilityCombo, "Aptitude protection forcée", out var forcedArmorAbility)) return false;

        if (NormalizeStealApplyValues(
                stealCommonItem,
                stealRareItem,
                ref stealChance,
                ref stealCommonQty,
                ref stealRareQty))
        {
            LootStealChanceBox.Text = stealChance.ToString();
            LootStealCommonQtyBox.Text = stealCommonQty.ToString();
            LootStealRareQtyBox.Text = stealRareQty.ToString();
            UpdateMonsterLootResolvedSummary();
        }

        loot.Gil = gil;
        loot.ApNormal = apNormal;
        loot.ApOverkill = apOverkill;
        loot.RonsoRage = ronsoRage;
        loot.DropChancePrimary = dropChancePrimary;
        loot.DropChanceSecondary = dropChanceSecondary;
        loot.StealChance = stealChance;
        loot.DropChanceGear = dropChanceGear;
        loot.MonsterArenaPrice = arenaPrice;

        loot.DropNormalPrimaryCommonItem = primaryNormalCommonItem;
        loot.DropNormalPrimaryCommonQty = primaryNormalCommonQty;
        loot.DropNormalPrimaryRareItem = primaryNormalRareItem;
        loot.DropNormalPrimaryRareQty = primaryNormalRareQty;
        loot.DropOverkillPrimaryCommonItem = primaryOkCommonItem;
        loot.DropOverkillPrimaryCommonQty = primaryOkCommonQty;
        loot.DropOverkillPrimaryRareItem = primaryOkRareItem;
        loot.DropOverkillPrimaryRareQty = primaryOkRareQty;

        loot.DropNormalSecondaryCommonItem = secondaryNormalCommonItem;
        loot.DropNormalSecondaryCommonQty = secondaryNormalCommonQty;
        loot.DropNormalSecondaryRareItem = secondaryNormalRareItem;
        loot.DropNormalSecondaryRareQty = secondaryNormalRareQty;
        loot.DropOverkillSecondaryCommonItem = secondaryOkCommonItem;
        loot.DropOverkillSecondaryCommonQty = secondaryOkCommonQty;
        loot.DropOverkillSecondaryRareItem = secondaryOkRareItem;
        loot.DropOverkillSecondaryRareQty = secondaryOkRareQty;

        loot.StealCommonItem = stealCommonItem;
        loot.StealCommonQty = stealCommonQty;
        loot.StealRareItem = stealRareItem;
        loot.StealRareQty = stealRareQty;
        loot.BribeItem = bribeItem;
        loot.BribeQty = bribeQty;
        loot.GilStealByte = gilSteal;
        loot.GearSlotCountByte = gearSlots;
        loot.GearAbilityCountByte = gearAbilities;
        if (forcedWeaponAbility >= 0)
            SetForcedGearAbilityForAllCharacters(loot.GearWeaponAbilitiesByCharacter, forcedWeaponAbility);
        if (forcedArmorAbility >= 0)
            SetForcedGearAbilityForAllCharacters(loot.GearArmorAbilitiesByCharacter, forcedArmorAbility);

        return true;
    }

    private static bool NormalizeStealApplyValues(
        int stealCommonItem,
        int stealRareItem,
        ref int stealChance,
        ref int stealCommonQty,
        ref int stealRareQty)
    {
        var changed = false;
        if (stealCommonItem > 0 && stealCommonQty <= 0)
        {
            stealCommonQty = 1;
            changed = true;
        }
        if (stealRareItem > 0 && stealRareQty <= 0)
        {
            stealRareQty = 1;
            changed = true;
        }
        if ((stealCommonItem > 0 || stealRareItem > 0) && stealChance <= 0)
        {
            stealChance = 255;
            changed = true;
        }
        return changed;
    }

    private bool TryReadStatus(string key, out int value)
    {
        var box = FindTaggedTextBox(StatusResistGrid, $"StatusResist:{key}");
        if (box == null)
        {
            value = 0;
            MessageBox.Show(this, $"Champ de résistance introuvable : {key}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        return TryReadIntBox(box, key, 0, 255, out value);
    }

    private bool TryReadIntBox(TextBox box, string label, int min, int max, out int value)
    {
        var raw = box.Text.Trim().Replace("%", "").Trim();
        var isHex = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        var numberText = isHex ? raw[2..] : raw;
        var style = isHex
            ? System.Globalization.NumberStyles.HexNumber
            : System.Globalization.NumberStyles.Integer;

        if (!int.TryParse(numberText, style, System.Globalization.CultureInfo.InvariantCulture, out value)
            || value < min || value > max)
        {
            MessageBox.Show(this,
                $"{label} doit être une valeur entre {min} et {max}.\nValeur reçue : {box.Text}",
                "Valeur invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
            box.Focus();
            box.SelectAll();
            return false;
        }
        return true;
    }

    private bool TryResolveUnsupportedChars(
        FfxCharset charset,
        string language,
        params TextBox[] boxes)
    {
        var allText = string.Concat(boxes.Select(box => box.Text + " "));
        var bad = charset.FindUnsupportedChars(allText);
        return TryResolveUnsupportedChars(charset, language, bad, boxes);
    }

    private bool TryResolveUnsupportedChars(
        FfxCharset charset,
        string language,
        IReadOnlyList<char> bad,
        params TextBox[] boxes)
    {
        if (bad.Count == 0) return true;

        var replacements = new List<(char Character, IReadOnlyList<string> Suggestions)>();
        foreach (var c in bad)
        {
            var suggestions = charset.GetInputSuggestions(c);
            if (suggestions.Count > 0)
                replacements.Add((c, suggestions));
        }

        if (replacements.Count > 0
            && ShowApplyCharsetSuggestionsDialog(charset, language, bad, replacements))
        {
            foreach (var box in boxes)
                box.Text = ApplyInputSuggestions(charset, box.Text);

            var remainingText = string.Concat(boxes.Select(box => box.Text + " "));
            var remainingBad = charset.FindUnsupportedChars(remainingText);
            if (remainingBad.Count == 0) return true;

            ShowUnsupportedCharsWarning(charset, language, remainingBad);
            return false;
        }

        ShowUnsupportedCharsWarning(charset, language, bad);
        return false;
    }

    private bool ShowApplyCharsetSuggestionsDialog(
        FfxCharset charset,
        string language,
        IReadOnlyList<char> bad,
        IReadOnlyList<(char Character, IReadOnlyList<string> Suggestions)> replacements)
    {
        var badList = string.Join(" ", bad.Select(x => $"'{x}'"));
        var suggestionList = string.Join(Environment.NewLine,
            replacements.Select(x => FormatCharsetSuggestionLine(x.Character, x.Suggestions)));
        var unresolved = bad
            .Where(c => replacements.All(x => x.Character != c))
            .Distinct()
            .ToList();
        var hasUnresolved = unresolved.Count > 0;

        var dialog = new Window
        {
            Title = "Caractères non supportés",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Height,
            Width = 560,
            ShowInTaskbar = false
        };

        var root = new DockPanel
        {
            Margin = new Thickness(16),
            LastChildFill = true
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var applyButton = new Button
        {
            Content = "Appliquer",
            IsDefault = true,
            MinWidth = 96,
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var cancelButton = new Button
        {
            Content = "Annuler",
            IsCancel = true,
            MinWidth = 96,
            Padding = new Thickness(12, 5, 12, 5)
        };
        applyButton.Click += (_, _) => dialog.DialogResult = true;
        cancelButton.Click += (_, _) => dialog.DialogResult = false;
        buttons.Children.Add(applyButton);
        buttons.Children.Add(cancelButton);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = $"⚠ Les caractères suivants ne sont pas dans la charset de la langue {language} :",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = badList,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = hasUnresolved
                ? $"Des équivalences présentes dans la table des caractères de Final Fantasy X ({charset.Code}) existent pour une partie des caractères. Spira Modifier peut appliquer automatiquement ces choix, puis il restera à corriger les caractères sans équivalence."
                : $"Des équivalences présentes dans la table des caractères de Final Fantasy X ({charset.Code}) existent. Spira Modifier peut appliquer automatiquement le premier choix compatible dans les champs texte, puis continuer l'écriture.",
            Margin = new Thickness(0, 12, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(new TextBlock
        {
            Text = "Équivalences proposées :",
            Margin = new Thickness(0, 12, 0, 0),
            FontWeight = FontWeights.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = suggestionList,
            Margin = new Thickness(0, 4, 0, 0),
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.Wrap
        });
        if (hasUnresolved)
        {
            content.Children.Add(new TextBlock
            {
                Text = "Sans équivalence automatique :",
                Margin = new Thickness(0, 12, 0, 0),
                FontWeight = FontWeights.SemiBold
            });
            content.Children.Add(new TextBlock
            {
                Text = string.Join(" ", unresolved.Select(c => $"'{c}'")),
                Margin = new Thickness(0, 4, 0, 0),
                FontFamily = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.Wrap
            });
        }

        root.Children.Add(content);
        dialog.Content = root;
        UiLocalization.Apply(dialog);
        return dialog.ShowDialog() == true;
    }

    private static string FormatCharsetSuggestionLine(char character, IReadOnlyList<string> suggestions)
    {
        if (suggestions.Count == 0)
            return $"  '{character}' -> ?";

        var line = $"  '{character}' -> {suggestions[0]}";
        if (suggestions.Count > 1)
            line += $"    autres disponibles : {string.Join(", ", suggestions.Skip(1))}";
        return line;
    }

    private static string FormatInlineCharsetSuggestion(char character, IReadOnlyList<string> suggestions)
    {
        if (suggestions.Count == 0)
            return $"'{character}'";

        var text = $"'{character}'->{suggestions[0]}";
        if (suggestions.Count > 1)
            text += $" (autres : {string.Join(", ", suggestions.Skip(1))})";
        return text;
    }

    private static string ApplyInputSuggestions(FfxCharset charset, string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '{')
            {
                var end = text.IndexOf('}', i + 1);
                if (end > i)
                {
                    builder.Append(text.Substring(i, end - i + 1));
                    i = end;
                    continue;
                }
            }

            var suggestion = charset.GetInputSuggestions(c).FirstOrDefault();
            builder.Append(suggestion ?? c.ToString());
        }

        return builder.ToString();
    }

    private bool ShowUnsupportedCharsWarning(FfxCharset charset, string language, IReadOnlyList<char> bad)
    {
        if (bad.Count == 0) return false;

        var badList = string.Join(" ", bad.Select(c => $"'{c}'"));
        var suggestions = bad
            .Select(c => (Character: c, Suggestions: charset.GetInputSuggestions(c)))
            .Where(x => x.Suggestions.Count > 0)
            .Select(x => FormatCharsetSuggestionLine(x.Character, x.Suggestions))
            .Distinct()
            .ToList();

        var message = new StringBuilder();
        message.AppendLine($"⚠ Les caractères suivants ne sont pas dans la charset de la langue {language} :");
        message.AppendLine();
        message.AppendLine(badList);

        if (charset.Code.Equals("jp", StringComparison.OrdinalIgnoreCase))
        {
            message.AppendLine();
            message.AppendLine("La version JP de FFX n'inclut qu'une liste limitée de kanji dans ffxsjistbl_jp.bin.");
            message.AppendLine("Si un kanji manque, il faut l'écrire en kana ou choisir un autre caractère présent dans la table.");
        }
        else if (charset.IsCjk)
        {
            message.AppendLine();
            message.AppendLine("Les charsets chinois/coréen de FFX ont aussi une table limitée. Certains signes occidentaux doivent parfois être remplacés par leur équivalent pleine chasse.");
        }

        if (suggestions.Count > 0)
        {
            message.AppendLine();
            message.AppendLine("Suggestions compatibles :");
            foreach (var suggestion in suggestions)
                message.AppendLine(suggestion);
        }

        message.AppendLine();
        message.Append("Aucune modification appliquée.");

        MessageBox.Show(this, message.ToString(),
            "Caractères non supportés", MessageBoxButton.OK, MessageBoxImage.Warning);
        return true;
    }

    private int ReadBitfieldFromChecks(DependencyObject root, string group)
    {
        var value = 0;
        foreach (var cb in FindCheckBoxes(root))
        {
            if (cb.Tag is not string tag || !tag.StartsWith(group + ":", StringComparison.Ordinal)) continue;
            if (cb.IsChecked != true) continue;
            if (int.TryParse(tag[(group.Length + 1)..], out var mask))
                value |= mask;
        }
        return value;
    }

    private bool IsChecked(string tag) =>
        FindCheckBoxes(ImmunitiesPanel).Any(cb => string.Equals(cb.Tag as string, tag, StringComparison.Ordinal)
                                                  && cb.IsChecked == true);

    private static IEnumerable<CheckBox> FindCheckBoxes(DependencyObject root)
    {
        if (root is CheckBox cb) yield return cb;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            foreach (var child in FindCheckBoxes(VisualTreeHelper.GetChild(root, i)))
                yield return child;
        }
    }

    private static TextBox? FindTaggedTextBox(DependencyObject root, string tag)
    {
        if (root is TextBox tb && string.Equals(tb.Tag as string, tag, StringComparison.Ordinal))
            return tb;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var found = FindTaggedTextBox(VisualTreeHelper.GetChild(root, i), tag);
            if (found != null) return found;
        }
        return null;
    }

    private void BuildElementsGrid(MonsterStat stat)
    {
        var toRemove = ElementsGrid.Children.OfType<UIElement>()
            .Where(c => Grid.GetRow(c) > 0).ToList();
        foreach (var c in toRemove) ElementsGrid.Children.Remove(c);

        for (int i = 0; i < FfxStatusFlags.Elements.Length; i++)
        {
            var (mask, label) = FfxStatusFlags.Elements[i];
            var row = i + 1;
            var labelText = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
            Grid.SetRow(labelText, row); Grid.SetColumn(labelText, 0);
            ElementsGrid.Children.Add(labelText);
            AddElementCheck(row, 1, "ElementAbsorb", mask, FfxStatusFlags.IsSet(stat.ElementAbsorb, mask));
            AddElementCheck(row, 2, "ElementImmune", mask, FfxStatusFlags.IsSet(stat.ElementImmune, mask));
            AddElementCheck(row, 3, "ElementResist", mask, FfxStatusFlags.IsSet(stat.ElementResist, mask));
            AddElementCheck(row, 4, "ElementWeak", mask, FfxStatusFlags.IsSet(stat.ElementWeak, mask));
        }
    }

    private void AddElementCheck(int row, int column, string group, int mask, bool isChecked)
    {
        var cb = new CheckBox
        {
            IsChecked = isChecked,
            Tag = $"{group}:{mask}",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
        cb.Checked += OnMonsterStat_Changed;
        cb.Unchecked += OnMonsterStat_Changed;
        Grid.SetRow(cb, row); Grid.SetColumn(cb, column);
        ElementsGrid.Children.Add(cb);
    }

    private void BuildStatusResistGrid(MonsterStat stat)
    {
        StatusResistGrid.Children.Clear();
        StatusResistGrid.RowDefinitions.Clear();
        StatusResistGrid.ColumnDefinitions.Clear();

        StatusResistGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        StatusResistGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        StatusResistGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        StatusResistGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        StatusResistGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        var resistList = new (string Key, string Label, int Value)[]
        {
            ("Death", "Mort", stat.StatusResistChanceDeath),
            ("Zombie", "Zombie", stat.StatusResistChanceZombie),
            ("Petrify", "Pétrification", stat.StatusResistChancePetrify),
            ("Poison", "Poison", stat.StatusResistChancePoison),
            ("PowerBreak", "Power Break", stat.StatusResistChancePowerBreak),
            ("MagicBreak", "Magic Break", stat.StatusResistChanceMagicBreak),
            ("ArmorBreak", "Armor Break", stat.StatusResistChanceArmorBreak),
            ("MentalBreak", "Mental Break", stat.StatusResistChanceMentalBreak),
            ("Confuse", "Confusion", stat.StatusResistChanceConfuse),
            ("Berserk", "Berserk", stat.StatusResistChanceBerserk),
            ("Provoke", "Provoque", stat.StatusResistChanceProvoke),
            ("Threaten", "Menace (chance)", stat.StatusChanceThreaten),
            ("Sleep", "Sommeil", stat.StatusResistChanceSleep),
            ("Silence", "Silence", stat.StatusResistChanceSilence),
            ("Darkness", "Obscurité", stat.StatusResistChanceDarkness),
            ("Shell", "Carapace", stat.StatusResistChanceShell),
            ("Protect", "Bouclier", stat.StatusResistChanceProtect),
            ("Reflect", "Reflet", stat.StatusResistChanceReflect),
            ("NTide", "NulMaree", stat.StatusResistChanceNTide),
            ("NBlaze", "NulFlamme", stat.StatusResistChanceNBlaze),
            ("NShock", "NulChoc", stat.StatusResistChanceNShock),
            ("NFrost", "NulFrimas", stat.StatusResistChanceNFrost),
            ("Regen", "Régen", stat.StatusResistChanceRegen),
            ("Haste", "Hâte", stat.StatusResistChanceHaste),
            ("Slow", "Lenteur", stat.StatusResistChanceSlow),
        };

        var rows = (resistList.Length + 1) / 2;
        for (int i = 0; i < rows; i++)
            StatusResistGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < resistList.Length; i++)
        {
            var (key, label, value) = resistList[i];
            var row = i / 2;
            var colOffset = (i % 2) * 3;

            var labelText = new TextBlock { Text = label + " :", Margin = new Thickness(0, 3, 4, 3), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(labelText, row); Grid.SetColumn(labelText, colOffset);
            StatusResistGrid.Children.Add(labelText);

            var valueText = new TextBox
            {
                Text = value.ToString(),
                Tag = $"StatusResist:{key}",
                Margin = new Thickness(2), Padding = new Thickness(3, 2, 3, 2),
                Background = value == 0xFF ? Brushes.LightGreen : (value == 0 ? Brushes.MistyRose : Brushes.White)
            };
            valueText.TextChanged += OnMonsterStat_Changed;
            Grid.SetRow(valueText, row); Grid.SetColumn(valueText, colOffset + 1);
            StatusResistGrid.Children.Add(valueText);
        }
    }

    private void BuildAutoStatusPanels(MonsterStat stat)
    {
        FillEditableStatusPanel(AutoStatusPermanentPanel, "AutoPermanent", stat.AutoStatusesPermanent, FfxStatusFlags.Permanent);
        FillEditableStatusPanel(AutoStatusTemporalPanel, "AutoTemporal", stat.AutoStatusesTemporal, FfxStatusFlags.Temporal);
        FillEditableStatusPanel(AutoStatusExtraPanel, "AutoExtra", stat.AutoStatusesExtra, FfxStatusFlags.Extra);
    }

    private void BuildExtraStatusImmunityPanel(MonsterStat stat)
    {
        FillEditableStatusPanel(ExtraStatusImmunityPanel, "ExtraImmunity", stat.ExtraStatusImmunities, FfxStatusFlags.Extra);
    }

    private void FillEditableStatusPanel(WrapPanel panel, string group, int bitfield, (int Mask, string Label)[] definitions)
    {
        panel.Children.Clear();
        foreach (var (mask, label) in definitions)
            panel.Children.Add(MakeEditableFlagCheck(label, group, mask, (bitfield & mask) != 0));
    }

    private void FillStatusPanel(WrapPanel panel, int bitfield, (int Mask, string Label)[] definitions)
    {
        panel.Children.Clear();
        var activeLabels = FfxStatusFlags.GetActiveLabels(bitfield, definitions);
        if (activeLabels.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "(aucun)", Foreground = Brushes.Gray, FontStyle = FontStyles.Italic });
            return;
        }
        foreach (var label in activeLabels)
            panel.Children.Add(MakeChip(label, Brushes.SteelBlue));
    }

    private void BuildImmunitiesPanel(MonsterStat stat)
    {
        ImmunitiesPanel.Children.Clear();
        ImmunitiesPanel.Children.Add(MakeEditableBoolCheck("Armuré", "Immunity:Armored", stat.Armored));
        ImmunitiesPanel.Children.Add(MakeEditableBoolCheck("Dégâts %", "Immunity:Fractional", stat.ImmunityFractionalDamage));
        ImmunitiesPanel.Children.Add(MakeEditableBoolCheck("Life", "Immunity:Life", stat.ImmunityLife));
        ImmunitiesPanel.Children.Add(MakeEditableBoolCheck("Sensor", "Immunity:Sensor", stat.ImmunitySensor));
        ImmunitiesPanel.Children.Add(MakeEditableBoolCheck("Scan?", "Immunity:ScanAgain", stat.ImmunityScanAgainOrWhat));
        ImmunitiesPanel.Children.Add(MakeEditableBoolCheck("Dégâts phys.", "Immunity:Physical", stat.ImmunityPhysicalDamage));
        ImmunitiesPanel.Children.Add(MakeEditableBoolCheck("Dégâts magiques", "Immunity:Magical", stat.ImmunityMagicalDamage));
        ImmunitiesPanel.Children.Add(MakeEditableBoolCheck("Tous dégâts HP", "Immunity:Hp", stat.ImmunityHpDamage));
        ImmunitiesPanel.Children.Add(MakeEditableBoolCheck("Délai CTB", "Immunity:Ctb", stat.ImmunityCtbDamage));
        ImmunitiesPanel.Children.Add(MakeEditableBoolCheck("Slice", "Immunity:Slice", stat.ImmunitySlice));
        ImmunitiesPanel.Children.Add(MakeEditableBoolCheck("Bribe", "Immunity:Bribe", stat.ImmunityBribe));
    }

    private void AddChipIfActive(bool active, string label)
    {
        if (active) ImmunitiesPanel.Children.Add(MakeChip(label, Brushes.LightCoral));
    }

    private CheckBox MakeEditableFlagCheck(string label, string group, int mask, bool isChecked) =>
        MakeEditableBoolCheck(label, $"{group}:{mask}", isChecked);

    private CheckBox MakeEditableBoolCheck(string label, string tag, bool isChecked)
    {
        var cb = new CheckBox
        {
            Content = label,
            Tag = tag,
            IsChecked = isChecked,
            Margin = new Thickness(4, 2, 10, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        cb.Checked += OnMonsterStat_Changed;
        cb.Unchecked += OnMonsterStat_Changed;
        return cb;
    }

    private static Border MakeChip(string text, Brush background) =>
        new() {
            Background = background,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(3),
            Child = new TextBlock { Text = text, Foreground = Brushes.White }
        };

    private void BuildCommandsList(MonsterStat stat)
    {
        var commands = new List<CommandSlotItem>();
        var lang = _currentLanguage ?? _workspace?.PreferredDisplayLanguage;

        for (int i = 0; i < stat.CommandList.Length; i++)
        {
            var cmd = stat.CommandList[i];
            var item = new CommandSlotItem
            {
                Slot = i,
                HexId = $"0x{cmd:X4}",
                DecimalId = cmd,
                Status = cmd == 0 ? "(vide)" : cmd == 0xFFFF ? "(inutilisé)" : "actif",
            };

            // Résolution du nom et de la source si la commande est active
            if (cmd != 0 && cmd != 0xFFFF && _workspace != null && lang != null)
            {
                item.Name = _workspace.LookupCommandName(cmd, lang) ?? "(non trouvé)";
                item.SourceLabel = _workspace.LookupCommandSource(cmd) ?? "?";
            }

            commands.Add(item);
        }
        CommandsListView.ItemsSource = commands;
    }

    private static string BuildChunksReport(MonsterFile file)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"AI script (ATEL bytecode)  : {ChunkSize(file.AiBytes)} octets");
        sb.AppendLine($"Worker mapping             : {ChunkSize(file.WorkerBytes)} octets");
        var statBlockSize = file.StatSheet != null ? MonsterStat.LENGTH : 0;
        var statTextSize  = file.StatSheetTextBytes?.Length ?? 0;
        sb.AppendLine($"StatSheet (stats + textes) : {statBlockSize + statTextSize} octets ({statBlockSize} stats + {statTextSize} textes)");
        sb.AppendLine($"Spoils (chunk inconnu)     : {ChunkSize(file.SpoilsBytes)} octets");
        sb.AppendLine($"Loot                       : {ChunkSize(file.LootBytes)} octets");
        sb.AppendLine($"Audio                      : {ChunkSize(file.AudioBytes)} octets");
        sb.Append   ($"Texte (localisations)      : {ChunkSize(file.TextBytes)} octets");
        return sb.ToString();
    }

    private static string ChunkSize(byte[]? chunk) => chunk == null ? "0" : chunk.Length.ToString("N0");

    private void ClearAllFields()
    {
        _suppressMonsterTextEvents = true;
        _suppressMonsterStatEvents = true;
        try
        {
            foreach (var box in new[] {
                StatHpBox, StatMpBox, StatOverkillBox, StatStrBox, StatDefBox, StatMagBox,
                StatMdfBox, StatAgiBox, StatLckBox, StatEvaBox, StatAccBox, StatPoisonBox,
                MetaForcedActionBox, MetaMonsterIdxBox, MetaModelIdxBox,
                MetaDoomBox, MetaArenaBox, MetaSoundBankBox, MetaCtbIconBox,
                LocalizedNameBox, LocalizedSensorBox, LocalizedSimplifiedSensorBox,
                LocalizedScanBox, LocalizedSimplifiedScanBox })
            {
                box.Text = "—";
            }
            foreach (var box in MonsterLootBoxes())
            {
                box.Text = "—";
            }
            foreach (var combo in MonsterLootCombos())
            {
                combo.ItemsSource = null;
                combo.SelectedIndex = -1;
            }
            ImmunitiesPanel.Children.Clear();
            ExtraStatusImmunityPanel.Children.Clear();
            AutoStatusPermanentPanel.Children.Clear();
            AutoStatusTemporalPanel.Children.Clear();
            AutoStatusExtraPanel.Children.Clear();
            StatusResistGrid.Children.Clear();
            var toRemove = ElementsGrid.Children.OfType<UIElement>().Where(c => Grid.GetRow(c) > 0).ToList();
            foreach (var c in toRemove) ElementsGrid.Children.Remove(c);
            CommandsListView.ItemsSource = null;
            ApplyMonsterStatsButton.IsEnabled = false;
            CopyMonsterMechanicsButton.IsEnabled = false;
            PasteMonsterMechanicsButton.IsEnabled = false;
            RevertMonsterStatsButton.IsEnabled = false;
            MonsterStatsEditStatusText.Text = "";
            ApplyMonsterTextsButton.IsEnabled = false;
            RevertMonsterTextsButton.IsEnabled = false;
            MonsterEditStatusText.Text = "";
            LootResolvedSummaryText.Text = "";
        }
        finally
        {
            _suppressMonsterTextEvents = false;
            _suppressMonsterStatEvents = false;
        }
    }

    // =========================================================================
    // ONGLET ATTAQUES
    // =========================================================================

    private readonly ObservableCollection<AttackListItem> _attackListItems = new();
    private List<AttackListItem> _allAttackItems = new();
    private AttackSource _currentAttackSource = AttackSource.MonMagic1;

    /// <summary>
    /// Initialise l'onglet Attaques au chargement du workspace : remplit le
    /// sélecteur de source (MM1/MM2/Both) et bind la liste.
    /// </summary>
    private void PopulateAttackTab(SpiraWorkspace workspace)
    {
        AttackListBox.ItemsSource = _attackListItems;
        AttackListBox.DisplayMemberPath = nameof(AttackListItem.DisplayName);

        _suppressLanguageEvents = true;
        try
        {
            AttackSourceSelector.Items.Clear();

            var hasMM1 = workspace.MonsterAttacksByLanguage.Values.Any(t => t.MonMagic1 != null);
            var hasMM2 = workspace.MonsterAttacksByLanguage.Values.Any(t => t.MonMagic2 != null);

            if (hasMM1 && hasMM2)
                AttackSourceSelector.Items.Add(new AttackSourceOption("Toutes les attaques (monmagic1+2)", AttackSource.Both));
            if (hasMM1)
                AttackSourceSelector.Items.Add(new AttackSourceOption("monmagic1.bin", AttackSource.MonMagic1));
            if (hasMM2)
                AttackSourceSelector.Items.Add(new AttackSourceOption("monmagic2.bin", AttackSource.MonMagic2));

            if (AttackSourceSelector.Items.Count > 0)
            {
                AttackSourceSelector.SelectedIndex = 0;
                _currentAttackSource = ((AttackSourceOption)AttackSourceSelector.Items[0]).Source;
            }

            AddMonMagic1EntryButton.IsEnabled = hasMM1;
            AddMonMagic2EntryButton.IsEnabled = hasMM2;
        }
        finally
        {
            _suppressLanguageEvents = false;
        }

        RebuildAttackList();
    }

    private void OnAttackSource_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageEvents || _workspace == null) return;
        if (AttackSourceSelector.SelectedItem is not AttackSourceOption opt) return;
        _currentAttackSource = opt.Source;
        RebuildAttackList();
    }

    private void OnAddMonMagic1Entry_Click(object sender, RoutedEventArgs e)
    {
        AddMonsterAttackEntry(AttackSource.MonMagic1, "MM1", "monmagic1.bin", "0x4000");
    }

    private void OnAddMonMagic2Entry_Click(object sender, RoutedEventArgs e)
    {
        AddMonsterAttackEntry(AttackSource.MonMagic2, "MM2", "monmagic2.bin", "0x6000");
    }

    private void AddMonsterAttackEntry(AttackSource source, string sourceTag, string fileName, string cloneSourceId)
    {
        if (_workspace == null) return;

        var files = _workspace.MonsterAttacksByLanguage
            .Select(pair => source == AttackSource.MonMagic1 ? pair.Value.MonMagic1 : pair.Value.MonMagic2)
            .Where(file => file != null)
            .Cast<AttackFile>()
            .ToList();

        if (files.Count == 0)
        {
            MessageBox.Show(this,
                $"Aucun {fileName} n'est chargé dans ce workspace.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newIds = new List<int>();
        foreach (var file in files)
            newIds.Add(file.AppendCloneOf(0));

        var selectedGlobalId = newIds[0];
        SelectAttackSource(source);
        AttackFilterBox.Clear();
        RebuildAttackList();
        SelectAttackById(sourceTag, selectedGlobalId);

        AttackEditStatusText.Text =
            $"Nouvelle entrée {fileName} 0x{selectedGlobalId:X4} créée depuis {cloneSourceId} (sauvegarde avec Ctrl+S)";
        UpdateSaveStatusUI();
    }

    private void SelectAttackSource(AttackSource source)
    {
        foreach (var item in AttackSourceSelector.Items)
        {
            if (item is AttackSourceOption option && option.Source == source)
            {
                AttackSourceSelector.SelectedItem = item;
                _currentAttackSource = source;
                return;
            }
        }

        _currentAttackSource = source;
    }

    private void SelectAttackById(string sourceTag, int globalId)
    {
        foreach (var item in _attackListItems)
        {
            if (item.SourceTag == sourceTag && item.GlobalId == globalId)
            {
                AttackListBox.SelectedItem = item;
                AttackListBox.ScrollIntoView(item);
                break;
            }
        }
    }

    /// <summary>
    /// Reconstruit la liste d'attaques selon la source sélectionnée et la langue actuelle.
    /// On utilise la langue choisie dans l'onglet Monstres pour les noms.
    /// </summary>
    private void RebuildAttackList()
    {
        if (_workspace == null) return;

        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage;
        // Fallback : prendre la première langue qui a des attaques
        if (lang == null || !_workspace.MonsterAttacksByLanguage.ContainsKey(lang))
            lang = _workspace.MonsterAttacksByLanguage.Keys.FirstOrDefault();

        if (lang == null)
        {
            _allAttackItems.Clear();
            ApplyAttackFilter();
            return;
        }

        var pair = _workspace.MonsterAttacksByLanguage[lang];
        var charset = _workspace.GetCharsetForLanguage(lang);
        var items = new List<AttackListItem>();

        if ((_currentAttackSource == AttackSource.Both || _currentAttackSource == AttackSource.MonMagic1)
            && pair.MonMagic1 != null)
            BuildItemsFromFile(pair.MonMagic1, "MM1", charset, items);

        if ((_currentAttackSource == AttackSource.Both || _currentAttackSource == AttackSource.MonMagic2)
            && pair.MonMagic2 != null)
            BuildItemsFromFile(pair.MonMagic2, "MM2", charset, items);

        _allAttackItems = items;
        ApplyAttackFilter();
    }

    private static void BuildItemsFromFile(AttackFile file, string sourceTag,
        SpiraModifier.Core.Text.FfxCharset? charset, List<AttackListItem> output)
    {
        for (int i = 0; i < file.Count; i++)
        {
            var attack = file.Attacks[i];
            var name = file.GetName(i, charset);
            var globalId = file.MinIndex + i;
            output.Add(new AttackListItem
            {
                File = file,
                Attack = attack,
                RelativeIndex = i,
                GlobalId = globalId,
                SourceTag = sourceTag,
                Name = string.IsNullOrWhiteSpace(name) ? "(sans nom)" : name,
            });
        }
    }

    private void OnAttackFilter_Changed(object sender, TextChangedEventArgs e)
    {
        AttackFilterPlaceholder.Visibility = string.IsNullOrEmpty(AttackFilterBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        ApplyAttackFilter();
    }

    private void ApplyAttackFilter()
    {
        var filter = AttackFilterBox.Text.Trim();
        IEnumerable<AttackListItem> filtered = _allAttackItems;
        if (!string.IsNullOrEmpty(filter))
        {
            filtered = filtered.Where(item =>
                item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }
        _attackListItems.Clear();
        foreach (var item in filtered) _attackListItems.Add(item);

        AttackCountText.Text = _attackListItems.Count == _allAttackItems.Count
            ? $"{_allAttackItems.Count} attaques"
            : $"{_attackListItems.Count} / {_allAttackItems.Count} attaques";
    }

    private void OnAttackSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (AttackListBox.SelectedItem is not AttackListItem item)
        {
            NoAttackSelectedMessage.Visibility = Visibility.Visible;
            AttackDetailsPanel.Visibility = Visibility.Collapsed;
            return;
        }
        DisplayAttack(item);
    }

    private void DisplayAttack(AttackListItem item)
    {
        if (_workspace == null) return;

        NoAttackSelectedMessage.Visibility = Visibility.Collapsed;
        AttackDetailsPanel.Visibility = Visibility.Visible;

        AttackHeaderText.Text = item.DisplayName;

        // Pour les textes, on relit le même index dans le fichier de la langue actuelle
        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage ?? "jp";
        AttackTextsGroup.Header = $"Texte — {LanguageDisplayName(lang)}";

        var charset = _workspace.GetCharsetForLanguage(lang);
        AttackFile? fileInLang = null;
        if (_workspace.MonsterAttacksByLanguage.TryGetValue(lang, out var pair))
            fileInLang = item.SourceTag == "MM1" ? pair.MonMagic1 : pair.MonMagic2;

        _suppressAttackTextEvents = true;
        if (fileInLang != null && item.RelativeIndex < fileInLang.Count)
        {
            var atkInLang = fileInLang.Attacks[item.RelativeIndex];
            AttackNameBox.Text       = SpiraModifier.Core.Text.FfxStringDecoder.Decode(fileInLang.StringsPool, atkInLang.NameOffset, charset);
            AttackSimpleNameBox.Text = SpiraModifier.Core.Text.FfxStringDecoder.Decode(fileInLang.StringsPool, atkInLang.SimplifiedNameOffset, charset);
            AttackDescBox.Text       = SpiraModifier.Core.Text.FfxStringDecoder.Decode(fileInLang.StringsPool, atkInLang.DescriptionOffset, charset);
            AttackSimpleDescBox.Text = SpiraModifier.Core.Text.FfxStringDecoder.Decode(fileInLang.StringsPool, atkInLang.SimplifiedDescriptionOffset, charset);
        }
        else
        {
            AttackNameBox.Text = "(non disponible)";
            AttackSimpleNameBox.Text = "(non disponible)";
            AttackDescBox.Text = "(non disponible)";
            AttackSimpleDescBox.Text = "(non disponible)";
        }
        ApplyAttackTextsButton.IsEnabled = false;
        RevertAttackTextsButton.IsEnabled = false;
        AttackEditStatusText.Text = "";
        _suppressAttackTextEvents = false;

        AttackInfoText.Text =
            $"Source : {(item.SourceTag == "MM1" ? "monmagic1.bin" : "monmagic2.bin")}  •  " +
            $"ID global : 0x{item.GlobalId:X4} ({item.GlobalId})  •  " +
            $"Index relatif : {item.RelativeIndex}";

        var atk = item.Attack;
        _suppressAttackMechanicEvents = true;
        try
        {
            AtkPowerBox.Text     = atk.AttackPower.ToString();
            AtkAccuracyBox.Text  = atk.AttackAccuracy.ToString();
            AtkHitCountBox.Text  = atk.HitCount.ToString();
            AtkFormulaBox.Text   = atk.DamageFormula.ToString();
            AtkCostMpBox.Text    = atk.CostMP.ToString();
            AtkCostOdBox.Text    = atk.CostOD.ToString();
            AtkCritBox.Text      = atk.AttackCritBonus.ToString();
            AtkShatterBox.Text   = atk.ShatterChance.ToString();
            AtkMoveRankBox.Text  = atk.MoveRank.ToString();

            AtkAnim1Box.Text = $"0x{atk.Anim1:X4}";
            AtkAnim2Box.Text = $"0x{atk.Anim2:X4}";
            AtkIconBox.Text = atk.Icon.ToString();
            AtkCasterAnimationBox.Text = atk.CasterAnimation.ToString();
            AtkMenuPropsBox.Text = $"0x{atk.MenuProperties16:X2}";
            AtkSubsubMenuBox.Text = $"0x{atk.SubsubMenuCategorization:X2}";
            AtkSubMenuBox.Text = $"0x{atk.SubMenuCategorization:X2}";
            AtkCharacterUserBox.Text = $"0x{atk.CharacterUser:X2}";
            AtkTargetsAllowedBox.Text = $"0x{atk.TargetsAllowedApparently:X2}";
            AtkMisc1CBox.Text = $"0x{atk.MiscProperties1C:X2}";
            AtkMisc1DBox.Text = $"0x{atk.MiscProperties1D:X2}";
            AtkMisc1EBox.Text = $"0x{atk.MiscProperties1E:X2}";
            AtkAnimPropsBox.Text = $"0x{atk.AnimationProperties1F:X2}";
            AtkStealGilBox.Text = $"0x{atk.StealGilByte:X2}";
            AtkPartyPreviewBox.Text = $"0x{atk.PartyPreviewByte:X2}";
            AtkStatBuffValueBox.Text = atk.StatBuffValue.ToString();
            AtkOverdriveCategoryBox.Text = $"0x{atk.OverdriveCategorizationByte:X2}";
            AtkSpecialBuffBox.Text = $"0x{atk.SpecialBuffInflict:X4}";

            FillEditableAttackFlagsPanel(AtkMisc1CFlagsPanel, "AtkMisc1C", atk.MiscProperties1C, FfxAttackFlags.MiscProperties1C);
            FillEditableAttackFlagsPanel(AtkMisc1DFlagsPanel, "AtkMisc1D", atk.MiscProperties1D, FfxAttackFlags.MiscProperties1D);
            FillEditableAttackFlagsPanel(AtkMisc1EFlagsPanel, "AtkMisc1E", atk.MiscProperties1E, FfxAttackFlags.MiscProperties1E);
            FillEditableAttackFlagsPanel(AtkAnimFlagsPanel, "AtkAnimFlags", atk.AnimationProperties1F, FfxAttackFlags.AnimationProperties1F);
            FillEditableAttackFlagsPanel(AtkDamagePropsPanel, "AtkDamageProps", atk.DamageProperties20, FfxAttackFlags.DamageProperties);
            FillEditableAttackFlagsPanel(AtkDamageClassPanel, "AtkDamageClass", atk.DamageClass, FfxAttackFlags.DamageClass);
            FillEditableAttackFlagsPanel(AtkTargetingPanel, "AtkTargeting", atk.TargetingFlags, FfxAttackFlags.Targeting);
            FillEditableAttackFlagsPanel(AtkElementsPanel, "AtkElement", atk.ElementFlags, FfxStatusFlags.Elements);
            FillEditableAttackFlagsPanel(AtkExtraStatusPanel, "AtkExtraStatus", atk.ExtraStatusInflict, FfxAttackFlags.ExtraStatus);
            FillEditableAttackFlagsPanel(AtkStatBuffPanel, "AtkStatBuff", atk.StatBuffFlags, FfxAttackFlags.StatBuffs);

            BuildAttackStatusGrid(atk);
            ApplyAttackMechanicsButton.IsEnabled = false;
            CopyAttackMechanicsButton.IsEnabled = true;
            PasteAttackMechanicsButton.IsEnabled = _copiedAttackMechanics != null;
            ApplyAttackMechanicsAllLanguagesButton.IsEnabled = true;
            ApplyAllAttackMechanicsAllLanguagesButton.IsEnabled = true;
            RevertAttackMechanicsButton.IsEnabled = false;
            AttackMechanicsStatusText.Text = "";
        }
        finally
        {
            _suppressAttackMechanicEvents = false;
        }
    }

    private void OnAttackText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressAttackTextEvents) return;
        ApplyAttackTextsButton.IsEnabled = true;
        RevertAttackTextsButton.IsEnabled = true;
        AttackEditStatusText.Text = "● Modifications non appliquées";
    }

    private void OnRevertAttackTexts_Click(object sender, RoutedEventArgs e)
    {
        if (AttackListBox.SelectedItem is AttackListItem item)
            DisplayAttack(item);
    }

    private void OnApplyAttackTexts_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || AttackListBox.SelectedItem is not AttackListItem item)
            return;

        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage ?? "jp";
        var charset = _workspace.GetCharsetForLanguage(lang);
        if (charset == null)
        {
            MessageBox.Show(this,
                "La charset de cette langue n'est pas chargée — impossible de réencoder les textes.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var file = GetAttackFileForItem(item, lang);
        if (file == null || item.RelativeIndex < 0 || item.RelativeIndex >= file.Count)
        {
            MessageBox.Show(this,
                $"Aucun fichier monmagic ne couvre cette attaque dans la langue {lang}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryResolveUnsupportedChars(charset, lang,
                AttackNameBox, AttackSimpleNameBox, AttackDescBox, AttackSimpleDescBox))
            return;

        var newTexts = new AttackTexts
        {
            Name = AttackNameBox.Text,
            SimplifiedName = AttackSimpleNameBox.Text,
            Description = AttackDescBox.Text,
            SimplifiedDescription = AttackSimpleDescBox.Text,
        };

        if (!file.SetTexts(item.RelativeIndex, newTexts, charset))
        {
            MessageBox.Show(this, "Échec de l'écriture en mémoire (index invalide).",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        item.File = file;
        item.Attack = file.Attacks[item.RelativeIndex];
        item.Name = newTexts.Name;
        AttackHeaderText.Text = item.DisplayName;

        var selectedKey = (item.SourceTag, item.RelativeIndex);
        ApplyAttackFilter();
        foreach (var candidate in _attackListItems)
        {
            if (candidate.SourceTag == selectedKey.SourceTag
                && candidate.RelativeIndex == selectedKey.RelativeIndex)
            {
                AttackListBox.SelectedItem = candidate;
                break;
            }
        }

        ApplyAttackTextsButton.IsEnabled = false;
        RevertAttackTextsButton.IsEnabled = false;
        AttackEditStatusText.Text = "✓ Modifications appliquées (sauvegarde avec Ctrl+S)";
        UpdateSaveStatusUI();
    }

    private AttackFile? GetAttackFileForItem(AttackListItem item, string language)
    {
        if (_workspace == null) return null;
        if (!_workspace.MonsterAttacksByLanguage.TryGetValue(language, out var pair))
            return null;
        return item.SourceTag == "MM1" ? pair.MonMagic1 : pair.MonMagic2;
    }

    private string? GetEffectiveAttackLanguage()
    {
        if (_workspace == null) return null;

        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage;
        if (lang != null && _workspace.MonsterAttacksByLanguage.ContainsKey(lang))
            return lang;

        return _workspace.MonsterAttacksByLanguage.Keys.FirstOrDefault();
    }

    private void OnAttackMechanic_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressAttackMechanicEvents) return;
        ApplyAttackMechanicsButton.IsEnabled = true;
        RevertAttackMechanicsButton.IsEnabled = true;
        AttackMechanicsStatusText.Text = "● Mécaniques non appliquées";
    }

    private void OnRevertAttackMechanics_Click(object sender, RoutedEventArgs e)
    {
        if (AttackListBox.SelectedItem is AttackListItem item)
            DisplayAttack(item);
    }

    private void OnApplyAttackMechanics_Click(object sender, RoutedEventArgs e)
    {
        _lastAttackMechanicsApplySucceeded = false;
        if (_workspace == null || AttackListBox.SelectedItem is not AttackListItem item)
            return;

        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage ?? "jp";
        var file = GetAttackFileForItem(item, lang) ?? item.File;
        if (file == null || item.RelativeIndex < 0 || item.RelativeIndex >= file.Count)
        {
            MessageBox.Show(this,
                $"Aucun fichier monmagic ne couvre cette attaque dans la langue {lang}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryReadIntBox(AtkPowerBox, "Puissance", 0, 255, out var power)) return;
        if (!TryReadIntBox(AtkAccuracyBox, "Précision", 0, 255, out var accuracy)) return;
        if (!TryReadIntBox(AtkHitCountBox, "Nombre de coups", 0, 255, out var hitCount)) return;
        if (!TryReadIntBox(AtkFormulaBox, "Formule", 0, 255, out var formula)) return;
        if (!TryReadIntBox(AtkCostMpBox, "Coût MP", 0, 255, out var costMp)) return;
        if (!TryReadIntBox(AtkCostOdBox, "Coût OD", 0, 255, out var costOd)) return;
        if (!TryReadIntBox(AtkCritBox, "Bonus critique", 0, 255, out var crit)) return;
        if (!TryReadIntBox(AtkShatterBox, "Chance pétrification massive", 0, 255, out var shatter)) return;
        if (!TryReadIntBox(AtkMoveRankBox, "Move rank", 0, 255, out var moveRank)) return;

        if (!TryReadIntBox(AtkAnim1Box, "Animation 1", 0, 0xFFFF, out var anim1)) return;
        if (!TryReadIntBox(AtkAnim2Box, "Animation 2", 0, 0xFFFF, out var anim2)) return;
        if (!TryReadIntBox(AtkIconBox, "Icône", 0, 255, out var icon)) return;
        if (!TryReadIntBox(AtkCasterAnimationBox, "Animation caster", 0, 255, out var casterAnim)) return;
        if (!TryReadIntBox(AtkMenuPropsBox, "Menu 0x16", 0, 255, out var menuProps)) return;
        if (!TryReadIntBox(AtkSubsubMenuBox, "Sous-menu A", 0, 255, out var subsubMenu)) return;
        if (!TryReadIntBox(AtkSubMenuBox, "Sous-menu B", 0, 255, out var subMenu)) return;
        if (!TryReadIntBox(AtkCharacterUserBox, "User char", 0, 255, out var characterUser)) return;
        if (!TryReadIntBox(AtkTargetsAllowedBox, "Cibles permises", 0, 255, out var targetsAllowed)) return;
        if (!TryReadIntBox(AtkMisc1CBox, "Misc 0x1C", 0, 255, out var misc1C)) return;
        if (!TryReadIntBox(AtkMisc1DBox, "Misc 0x1D", 0, 255, out var misc1D)) return;
        if (!TryReadIntBox(AtkMisc1EBox, "Misc 0x1E", 0, 255, out var misc1E)) return;
        if (!TryReadIntBox(AtkAnimPropsBox, "Animation props", 0, 255, out var animProps)) return;
        if (!TryReadIntBox(AtkStealGilBox, "Steal/Gil", 0, 255, out var stealGil)) return;
        if (!TryReadIntBox(AtkPartyPreviewBox, "Preview", 0, 255, out var partyPreview)) return;
        if (!TryReadIntBox(AtkStatBuffValueBox, "Valeur buff", 0, 255, out var statBuffValue)) return;
        if (!TryReadIntBox(AtkOverdriveCategoryBox, "OD catégorie", 0, 255, out var odCategory)) return;
        if (!TryReadIntBox(AtkSpecialBuffBox, "Buff spécial", 0, 0xFFFF, out var specialBuff)) return;

        if (!TryReadAttackStatusChance("Death", "Mort", out var stDeath)) return;
        if (!TryReadAttackStatusChance("Zombie", "Zombie", out var stZombie)) return;
        if (!TryReadAttackStatusChance("Petrify", "Pétrification", out var stPetrify)) return;
        if (!TryReadAttackStatusChance("Poison", "Poison", out var stPoison)) return;
        if (!TryReadAttackStatusChance("PowerBreak", "Power Break", out var stPowerBreak)) return;
        if (!TryReadAttackStatusChance("MagicBreak", "Magic Break", out var stMagicBreak)) return;
        if (!TryReadAttackStatusChance("ArmorBreak", "Armor Break", out var stArmorBreak)) return;
        if (!TryReadAttackStatusChance("MentalBreak", "Mental Break", out var stMentalBreak)) return;
        if (!TryReadAttackStatusChance("Confuse", "Confusion", out var stConfuse)) return;
        if (!TryReadAttackStatusChance("Berserk", "Berserk", out var stBerserk)) return;
        if (!TryReadAttackStatusChance("Provoke", "Provoque", out var stProvoke)) return;
        if (!TryReadAttackStatusChance("Threaten", "Menace", out var stThreaten)) return;
        if (!TryReadAttackStatusChance("Sleep", "Sommeil", out var stSleep)) return;
        if (!TryReadAttackStatusChance("Silence", "Silence", out var stSilence)) return;
        if (!TryReadAttackStatusChance("Darkness", "Obscurité", out var stDarkness)) return;
        if (!TryReadAttackStatusChance("Shell", "Carapace", out var stShell)) return;
        if (!TryReadAttackStatusChance("Protect", "Bouclier", out var stProtect)) return;
        if (!TryReadAttackStatusChance("Reflect", "Reflet", out var stReflect)) return;
        if (!TryReadAttackStatusChance("NTide", "NulMaree", out var stNTide)) return;
        if (!TryReadAttackStatusChance("NBlaze", "NulFlamme", out var stNBlaze)) return;
        if (!TryReadAttackStatusChance("NShock", "NulChoc", out var stNShock)) return;
        if (!TryReadAttackStatusChance("NFrost", "NulFrimas", out var stNFrost)) return;
        if (!TryReadAttackStatusChance("Regen", "Régen", out var stRegen)) return;
        if (!TryReadAttackStatusChance("Haste", "Hâte", out var stHaste)) return;
        if (!TryReadAttackStatusChance("Slow", "Lenteur", out var stSlow)) return;

        if (!TryReadAttackStatusDuration("Sleep", "Sommeil", out var durSleep)) return;
        if (!TryReadAttackStatusDuration("Silence", "Silence", out var durSilence)) return;
        if (!TryReadAttackStatusDuration("Darkness", "Obscurité", out var durDarkness)) return;
        if (!TryReadAttackStatusDuration("Shell", "Carapace", out var durShell)) return;
        if (!TryReadAttackStatusDuration("Protect", "Bouclier", out var durProtect)) return;
        if (!TryReadAttackStatusDuration("Reflect", "Reflet", out var durReflect)) return;
        if (!TryReadAttackStatusDuration("NTide", "NulMaree", out var durNTide)) return;
        if (!TryReadAttackStatusDuration("NBlaze", "NulFlamme", out var durNBlaze)) return;
        if (!TryReadAttackStatusDuration("NShock", "NulChoc", out var durNShock)) return;
        if (!TryReadAttackStatusDuration("NFrost", "NulFrimas", out var durNFrost)) return;
        if (!TryReadAttackStatusDuration("Regen", "Régen", out var durRegen)) return;
        if (!TryReadAttackStatusDuration("Haste", "Hâte", out var durHaste)) return;
        if (!TryReadAttackStatusDuration("Slow", "Lenteur", out var durSlow)) return;

        var atk = file.Attacks[item.RelativeIndex];
        atk.AttackPower = power;
        atk.AttackAccuracy = accuracy;
        atk.HitCount = hitCount;
        atk.DamageFormula = formula;
        atk.CostMP = costMp;
        atk.CostOD = costOd;
        atk.AttackCritBonus = crit;
        atk.ShatterChance = shatter;
        atk.MoveRank = moveRank;

        atk.Anim1 = anim1;
        atk.Anim2 = anim2;
        atk.Icon = icon;
        atk.CasterAnimation = casterAnim;
        atk.MenuProperties16 = menuProps;
        atk.SubsubMenuCategorization = subsubMenu;
        atk.SubMenuCategorization = subMenu;
        atk.CharacterUser = characterUser;
        atk.TargetsAllowedApparently = targetsAllowed;
        atk.MiscProperties1C = MergeKnownBitfield(misc1C,
            ReadBitfieldFromChecks(AtkMisc1CFlagsPanel, "AtkMisc1C"), FfxAttackFlags.MiscProperties1C);
        atk.MiscProperties1D = MergeKnownBitfield(misc1D,
            ReadBitfieldFromChecks(AtkMisc1DFlagsPanel, "AtkMisc1D"), FfxAttackFlags.MiscProperties1D);
        atk.MiscProperties1E = MergeKnownBitfield(misc1E,
            ReadBitfieldFromChecks(AtkMisc1EFlagsPanel, "AtkMisc1E"), FfxAttackFlags.MiscProperties1E);
        atk.AnimationProperties1F = MergeKnownBitfield(animProps,
            ReadBitfieldFromChecks(AtkAnimFlagsPanel, "AtkAnimFlags"), FfxAttackFlags.AnimationProperties1F);
        atk.StealGilByte = stealGil;
        atk.PartyPreviewByte = partyPreview;

        atk.DamageProperties20 = MergeKnownBitfield(atk.DamageProperties20,
            ReadBitfieldFromChecks(AtkDamagePropsPanel, "AtkDamageProps"), FfxAttackFlags.DamageProperties);
        atk.DamageClass = MergeKnownBitfield(atk.DamageClass,
            ReadBitfieldFromChecks(AtkDamageClassPanel, "AtkDamageClass"), FfxAttackFlags.DamageClass);
        atk.TargetingFlags = MergeKnownBitfield(atk.TargetingFlags,
            ReadBitfieldFromChecks(AtkTargetingPanel, "AtkTargeting"), FfxAttackFlags.Targeting);
        atk.ElementFlags = MergeKnownBitfield(atk.ElementFlags,
            ReadBitfieldFromChecks(AtkElementsPanel, "AtkElement"), FfxStatusFlags.Elements);
        atk.ExtraStatusInflict = MergeKnownBitfield(atk.ExtraStatusInflict,
            ReadBitfieldFromChecks(AtkExtraStatusPanel, "AtkExtraStatus"), FfxAttackFlags.ExtraStatus);
        atk.StatBuffFlags = MergeKnownBitfield(atk.StatBuffFlags,
            ReadBitfieldFromChecks(AtkStatBuffPanel, "AtkStatBuff"), FfxAttackFlags.StatBuffs);
        atk.StatBuffValue = statBuffValue;
        atk.OverdriveCategorizationByte = odCategory;
        atk.SpecialBuffInflict = specialBuff;

        atk.StatusChanceDeath = stDeath;
        atk.StatusChanceZombie = stZombie;
        atk.StatusChancePetrify = stPetrify;
        atk.StatusChancePoison = stPoison;
        atk.StatusChancePowerBreak = stPowerBreak;
        atk.StatusChanceMagicBreak = stMagicBreak;
        atk.StatusChanceArmorBreak = stArmorBreak;
        atk.StatusChanceMentalBreak = stMentalBreak;
        atk.StatusChanceConfuse = stConfuse;
        atk.StatusChanceBerserk = stBerserk;
        atk.StatusChanceProvoke = stProvoke;
        atk.StatusChanceThreaten = stThreaten;
        atk.StatusChanceSleep = stSleep;
        atk.StatusChanceSilence = stSilence;
        atk.StatusChanceDarkness = stDarkness;
        atk.StatusChanceShell = stShell;
        atk.StatusChanceProtect = stProtect;
        atk.StatusChanceReflect = stReflect;
        atk.StatusChanceNTide = stNTide;
        atk.StatusChanceNBlaze = stNBlaze;
        atk.StatusChanceNShock = stNShock;
        atk.StatusChanceNFrost = stNFrost;
        atk.StatusChanceRegen = stRegen;
        atk.StatusChanceHaste = stHaste;
        atk.StatusChanceSlow = stSlow;

        atk.StatusDurationSleep = durSleep;
        atk.StatusDurationSilence = durSilence;
        atk.StatusDurationDarkness = durDarkness;
        atk.StatusDurationShell = durShell;
        atk.StatusDurationProtect = durProtect;
        atk.StatusDurationReflect = durReflect;
        atk.StatusDurationNTide = durNTide;
        atk.StatusDurationNBlaze = durNBlaze;
        atk.StatusDurationNShock = durNShock;
        atk.StatusDurationNFrost = durNFrost;
        atk.StatusDurationRegen = durRegen;
        atk.StatusDurationHaste = durHaste;
        atk.StatusDurationSlow = durSlow;

        file.MarkDirty();
        item.File = file;
        item.Attack = atk;

        ApplyAttackMechanicsButton.IsEnabled = false;
        RevertAttackMechanicsButton.IsEnabled = false;
        AttackMechanicsStatusText.Text = "✓ Mécaniques appliquées (sauvegarde avec Ctrl+S)";
        UpdateSaveStatusUI();
        _lastAttackMechanicsApplySucceeded = true;
    }

    private void OnApplyAttackMechanicsAllLanguages_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || AttackListBox.SelectedItem is not AttackListItem item)
            return;

        OnApplyAttackMechanics_Click(sender, e);
        if (!_lastAttackMechanicsApplySucceeded) return;

        var source = item.Attack;
        var touched = 0;
        foreach (var (_, pair) in _workspace.MonsterAttacksByLanguage)
        {
            var file = item.SourceTag == "MM1" ? pair.MonMagic1 : pair.MonMagic2;
            if (file == null || item.RelativeIndex < 0 || item.RelativeIndex >= file.Count) continue;

            CopyAttackMechanics(source, file.Attacks[item.RelativeIndex]);
            file.MarkDirty();
            touched++;
        }

        AttackMechanicsStatusText.Text =
            $"✓ Mécaniques appliquées à {touched} langue(s) (sauvegarde avec Ctrl+S)";
        UpdateSaveStatusUI();
    }

    private void OnCopyAttackMechanics_Click(object sender, RoutedEventArgs e)
    {
        if (AttackListBox.SelectedItem is not AttackListItem item)
            return;

        if (ApplyAttackMechanicsButton.IsEnabled)
        {
            OnApplyAttackMechanics_Click(sender, e);
            if (!_lastAttackMechanicsApplySucceeded) return;
        }

        _copiedAttackMechanics = CloneAttackData(item.Attack);
        PasteAttackMechanicsButton.IsEnabled = true;
        AttackMechanicsStatusText.Text = "✓ Mécaniques copiées";
    }

    private void OnPasteAttackMechanics_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || _copiedAttackMechanics == null
            || AttackListBox.SelectedItem is not AttackListItem item)
            return;

        var lang = GetEffectiveAttackLanguage() ?? _currentLanguage ?? _workspace.PreferredDisplayLanguage ?? "jp";
        var file = GetAttackFileForItem(item, lang) ?? item.File;
        if (file == null || item.RelativeIndex < 0 || item.RelativeIndex >= file.Count)
        {
            MessageBox.Show(this,
                $"Aucun fichier monmagic ne couvre cette attaque dans la langue {lang}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CopyAttackMechanics(_copiedAttackMechanics, file.Attacks[item.RelativeIndex]);
        file.MarkDirty();
        item.File = file;
        item.Attack = file.Attacks[item.RelativeIndex];

        DisplayAttack(item);
        PasteAttackMechanicsButton.IsEnabled = true;
        AttackMechanicsStatusText.Text = "✓ Mécaniques collées (textes préservés, sauvegarde avec Ctrl+S)";
        UpdateSaveStatusUI();
    }

    private void OnApplyAllAttackMechanicsAllLanguages_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null)
            return;

        var sourceLang = GetEffectiveAttackLanguage();
        if (sourceLang == null || !_workspace.MonsterAttacksByLanguage.TryGetValue(sourceLang, out var sourcePair))
        {
            MessageBox.Show(this,
                "Aucun fichier monmagic source n'est chargé.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (AttackListBox.SelectedItem is AttackListItem && ApplyAttackMechanicsButton.IsEnabled)
        {
            OnApplyAttackMechanics_Click(sender, e);
            if (!_lastAttackMechanicsApplySucceeded) return;
            sourcePair = _workspace.MonsterAttacksByLanguage[sourceLang];
        }

        var targetLanguageCount = _workspace.MonsterAttacksByLanguage.Keys
            .Count(lang => !string.Equals(lang, sourceLang, StringComparison.OrdinalIgnoreCase));
        if (!ConfirmBulkMechanicsCopy("attaques", sourceLang, targetLanguageCount))
            return;

        var touchedFiles = 0;
        var copiedEntries = 0;
        foreach (var (lang, pair) in _workspace.MonsterAttacksByLanguage)
        {
            if (string.Equals(lang, sourceLang, StringComparison.OrdinalIgnoreCase))
                continue;

            copiedEntries += CopyAllAttackMechanics(sourcePair.MonMagic1, pair.MonMagic1, ref touchedFiles);
            copiedEntries += CopyAllAttackMechanics(sourcePair.MonMagic2, pair.MonMagic2, ref touchedFiles);
        }

        AttackMechanicsStatusText.Text =
            $"✓ {copiedEntries} bloc(s) mécaniques copiés vers {touchedFiles} fichier(s) localisé(s) (sauvegarde avec Ctrl+S)";
        UpdateSaveStatusUI();
    }

    private static int MergeKnownBitfield(int original, int editedKnownBits, (int Mask, string Label)[] defs)
    {
        var knownMask = 0;
        foreach (var (mask, _) in defs)
            knownMask |= mask;
        return (original & ~knownMask) | (editedKnownBits & knownMask);
    }

    private bool ConfirmBulkMechanicsCopy(string entryKind, string sourceLanguage, int targetLanguageCount)
    {
        if (targetLanguageCount <= 0)
        {
            MessageBox.Show(this,
                "Aucune autre langue chargée pour recevoir les mécaniques.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var answer = MessageBox.Show(this,
            $"Copier les mécaniques de toutes les entrées ({entryKind}) depuis {LanguageDisplayName(sourceLanguage)} vers {targetLanguageCount} autre(s) langue(s) chargée(s) ?\n\n" +
            "Les noms, noms courts, descriptions et descriptions courtes ne seront pas modifiés.",
            "Copie globale des mécaniques", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        return answer == MessageBoxResult.OK;
    }

    private static int CopyAllAttackMechanics(AttackFile? sourceFile, AttackFile? targetFile, ref int touchedFiles)
    {
        if (sourceFile == null || targetFile == null || ReferenceEquals(sourceFile, targetFile))
            return 0;

        var count = Math.Min(sourceFile.Count, targetFile.Count);
        for (var i = 0; i < count; i++)
            CopyAttackMechanics(sourceFile.Attacks[i], targetFile.Attacks[i]);

        if (count > 0)
        {
            targetFile.MarkDirty();
            touchedFiles++;
        }

        return count;
    }

    private static AttackData CloneAttackData(AttackData source)
        => AttackData.ReadFromBytes(source.WriteToBytes(), 0, source.IsCharacterAbility);

    private static MonsterMechanicsSnapshot CreateMonsterMechanicsSnapshot(MonsterFile source)
    {
        if (source.StatSheet == null)
            throw new InvalidOperationException("MonsterFile sans StatSheet.");

        var stat = MonsterStat.ReadFromBytes(source.StatSheet.WriteToBytes(), 0);
        var loot = source.Loot == null ? null : CloneMonsterLoot(source.Loot);
        return new MonsterMechanicsSnapshot(stat, loot);
    }

    private static MonsterLoot CloneMonsterLoot(MonsterLoot source)
        => MonsterLoot.Read(source.WriteToBytes());

    private static MonsterStat BuildMonsterStatWithCopiedMechanics(MonsterStat source, MonsterStat target)
    {
        var sourceBytes = source.WriteToBytes();
        var targetBytes = target.WriteToBytes();

        Array.Copy(sourceBytes, 0x14, targetBytes, 0x14, 0x72 - 0x14);
        Array.Copy(sourceBytes, 0x74, targetBytes, 0x74, MonsterStat.LENGTH - 0x74);

        return MonsterStat.ReadFromBytes(targetBytes, 0);
    }

    private static void CopyAttackMechanics(AttackData source, AttackData target)
    {
        target.Anim1 = source.Anim1;
        target.Anim2 = source.Anim2;
        target.Icon = source.Icon;
        target.CasterAnimation = source.CasterAnimation;
        target.MenuProperties16 = source.MenuProperties16;
        target.SubsubMenuCategorization = source.SubsubMenuCategorization;
        target.SubMenuCategorization = source.SubMenuCategorization;
        target.CharacterUser = source.CharacterUser;
        target.TargetingFlags = source.TargetingFlags;
        target.TargetsAllowedApparently = source.TargetsAllowedApparently;
        target.MiscProperties1C = source.MiscProperties1C;
        target.MiscProperties1D = source.MiscProperties1D;
        target.MiscProperties1E = source.MiscProperties1E;
        target.AnimationProperties1F = source.AnimationProperties1F;
        target.DamageProperties20 = source.DamageProperties20;
        target.StealGilByte = source.StealGilByte;
        target.PartyPreviewByte = source.PartyPreviewByte;
        target.DamageClass = source.DamageClass;
        target.MoveRank = source.MoveRank;
        target.CostMP = source.CostMP;
        target.CostOD = source.CostOD;
        target.AttackCritBonus = source.AttackCritBonus;
        target.DamageFormula = source.DamageFormula;
        target.AttackAccuracy = source.AttackAccuracy;
        target.AttackPower = source.AttackPower;
        target.HitCount = source.HitCount;
        target.ShatterChance = source.ShatterChance;
        target.ElementFlags = source.ElementFlags;

        target.StatusChanceDeath = source.StatusChanceDeath;
        target.StatusChanceZombie = source.StatusChanceZombie;
        target.StatusChancePetrify = source.StatusChancePetrify;
        target.StatusChancePoison = source.StatusChancePoison;
        target.StatusChancePowerBreak = source.StatusChancePowerBreak;
        target.StatusChanceMagicBreak = source.StatusChanceMagicBreak;
        target.StatusChanceArmorBreak = source.StatusChanceArmorBreak;
        target.StatusChanceMentalBreak = source.StatusChanceMentalBreak;
        target.StatusChanceConfuse = source.StatusChanceConfuse;
        target.StatusChanceBerserk = source.StatusChanceBerserk;
        target.StatusChanceProvoke = source.StatusChanceProvoke;
        target.StatusChanceThreaten = source.StatusChanceThreaten;
        target.StatusChanceSleep = source.StatusChanceSleep;
        target.StatusChanceSilence = source.StatusChanceSilence;
        target.StatusChanceDarkness = source.StatusChanceDarkness;
        target.StatusChanceShell = source.StatusChanceShell;
        target.StatusChanceProtect = source.StatusChanceProtect;
        target.StatusChanceReflect = source.StatusChanceReflect;
        target.StatusChanceNTide = source.StatusChanceNTide;
        target.StatusChanceNBlaze = source.StatusChanceNBlaze;
        target.StatusChanceNShock = source.StatusChanceNShock;
        target.StatusChanceNFrost = source.StatusChanceNFrost;
        target.StatusChanceRegen = source.StatusChanceRegen;
        target.StatusChanceHaste = source.StatusChanceHaste;
        target.StatusChanceSlow = source.StatusChanceSlow;

        target.StatusDurationSleep = source.StatusDurationSleep;
        target.StatusDurationSilence = source.StatusDurationSilence;
        target.StatusDurationDarkness = source.StatusDurationDarkness;
        target.StatusDurationShell = source.StatusDurationShell;
        target.StatusDurationProtect = source.StatusDurationProtect;
        target.StatusDurationReflect = source.StatusDurationReflect;
        target.StatusDurationNTide = source.StatusDurationNTide;
        target.StatusDurationNBlaze = source.StatusDurationNBlaze;
        target.StatusDurationNShock = source.StatusDurationNShock;
        target.StatusDurationNFrost = source.StatusDurationNFrost;
        target.StatusDurationRegen = source.StatusDurationRegen;
        target.StatusDurationHaste = source.StatusDurationHaste;
        target.StatusDurationSlow = source.StatusDurationSlow;

        target.ExtraStatusInflict = source.ExtraStatusInflict;
        target.StatBuffFlags = source.StatBuffFlags;
        target.OverdriveCategorizationByte = source.OverdriveCategorizationByte;
        target.StatBuffValue = source.StatBuffValue;
        target.SpecialBuffInflict = source.SpecialBuffInflict;

        if (target.IsCharacterAbility && source.IsCharacterAbility)
        {
            target.OrderingIndexInMenu = source.OrderingIndexInMenu;
            target.SphereTypeForSphereGrid = source.SphereTypeForSphereGrid;
            target.AlwaysZero5E = source.AlwaysZero5E;
            target.AlwaysZero5F = source.AlwaysZero5F;
        }
    }

    private bool TryReadAttackStatusChance(string key, string label, out int value)
    {
        var box = FindTaggedTextBox(AtkStatusGrid, $"AtkStatusChance:{key}");
        if (box == null)
        {
            value = 0;
            MessageBox.Show(this, $"Champ de chance introuvable : {label}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        return TryReadIntBox(box, $"{label} chance", 0, 255, out value);
    }

    private bool TryReadAttackStatusDuration(string key, string label, out int value)
    {
        var box = FindTaggedTextBox(AtkStatusGrid, $"AtkStatusDuration:{key}");
        if (box == null)
        {
            value = 0;
            MessageBox.Show(this, $"Champ de durée introuvable : {label}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        return TryReadIntBox(box, $"{label} durée", 0, 255, out value);
    }

    private void FillEditableAttackFlagsPanel(WrapPanel panel, string group, int bitfield, (int Mask, string Label)[] defs)
    {
        panel.Children.Clear();
        foreach (var (mask, label) in defs)
            panel.Children.Add(MakeAttackFlagCheck(label, group, mask, (bitfield & mask) != 0));
    }

    private CheckBox MakeAttackFlagCheck(string label, string group, int mask, bool isChecked)
    {
        var cb = new CheckBox
        {
            Content = label,
            Tag = $"{group}:{mask}",
            IsChecked = isChecked,
            Margin = new Thickness(4, 2, 10, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        cb.Checked += OnAttackMechanic_Changed;
        cb.Unchecked += OnAttackMechanic_Changed;
        return cb;
    }

    private void FillEditableCommandFlagsPanel(WrapPanel panel, string group, int bitfield, (int Mask, string Label)[] defs)
    {
        panel.Children.Clear();
        foreach (var (mask, label) in defs)
            panel.Children.Add(MakeCommandFlagCheck(label, group, mask, (bitfield & mask) != 0));
    }

    private CheckBox MakeCommandFlagCheck(string label, string group, int mask, bool isChecked)
    {
        var cb = new CheckBox
        {
            Content = label,
            Tag = $"{group}:{mask}",
            IsChecked = isChecked,
            Margin = new Thickness(4, 2, 10, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        cb.Checked += OnCommandMechanic_Changed;
        cb.Unchecked += OnCommandMechanic_Changed;
        return cb;
    }

    private void FillEditableItemFlagsPanel(WrapPanel panel, string group, int bitfield, (int Mask, string Label)[] defs)
    {
        panel.Children.Clear();
        foreach (var (mask, label) in defs)
            panel.Children.Add(MakeItemFlagCheck(label, group, mask, (bitfield & mask) != 0));
    }

    private CheckBox MakeItemFlagCheck(string label, string group, int mask, bool isChecked)
    {
        var cb = new CheckBox
        {
            Content = label,
            Tag = $"{group}:{mask}",
            IsChecked = isChecked,
            Margin = new Thickness(4, 2, 10, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        cb.Checked += OnItemMechanic_Changed;
        cb.Unchecked += OnItemMechanic_Changed;
        return cb;
    }

    private void FillFlagsPanel(WrapPanel panel, int bitfield, (int Mask, string Label)[] defs, Brush activeColor)
    {
        panel.Children.Clear();
        var labels = FfxAttackFlags.GetActiveLabels(bitfield, defs);
        if (labels.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = "(aucun)", Foreground = Brushes.Gray, FontStyle = FontStyles.Italic });
            return;
        }
        foreach (var label in labels)
            panel.Children.Add(MakeChip(label, activeColor));
    }

    private void BuildAttackStatusGrid(AttackData atk)
    {
        AtkStatusGrid.Children.Clear();
        AtkStatusGrid.RowDefinitions.Clear();
        AtkStatusGrid.ColumnDefinitions.Clear();

        AtkStatusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        AtkStatusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        AtkStatusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        var rows = GetAttackStatusRows(atk);

        AtkStatusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddCell(AtkStatusGrid, 0, 0, "Statut", FontWeights.Bold);
        AddCell(AtkStatusGrid, 0, 1, "Chance %", FontWeights.Bold);
        AddCell(AtkStatusGrid, 0, 2, "Durée (tours)", FontWeights.Bold);

        for (int i = 0; i < rows.Length; i++)
        {
            var row = i + 1;
            AtkStatusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddCell(AtkStatusGrid, row, 0, rows[i].Label);
            AddAttackStatusBox(AtkStatusGrid, row, 1, $"AtkStatusChance:{rows[i].Key}", rows[i].Chance);
            if (rows[i].Duration is int duration)
                AddAttackStatusBox(AtkStatusGrid, row, 2, $"AtkStatusDuration:{rows[i].Key}", duration);
            else
                AddCell(AtkStatusGrid, row, 2, "—");
        }
    }

    private static (string Key, string Label, int Chance, int? Duration)[] GetAttackStatusRows(AttackData atk) =>
    [
        ("Death",      "Mort",          atk.StatusChanceDeath,        null),
        ("Zombie",     "Zombie",        atk.StatusChanceZombie,       null),
        ("Petrify",    "Pétrification", atk.StatusChancePetrify,      null),
        ("Poison",     "Poison",        atk.StatusChancePoison,       null),
        ("PowerBreak", "Power Break",   atk.StatusChancePowerBreak,   null),
        ("MagicBreak", "Magic Break",   atk.StatusChanceMagicBreak,   null),
        ("ArmorBreak", "Armor Break",   atk.StatusChanceArmorBreak,   null),
        ("MentalBreak","Mental Break",  atk.StatusChanceMentalBreak,  null),
        ("Confuse",    "Confusion",     atk.StatusChanceConfuse,      null),
        ("Berserk",    "Berserk",       atk.StatusChanceBerserk,      null),
        ("Provoke",    "Provoque",      atk.StatusChanceProvoke,      null),
        ("Threaten",   "Menace",        atk.StatusChanceThreaten,     null),
        ("Sleep",      "Sommeil",       atk.StatusChanceSleep,        atk.StatusDurationSleep),
        ("Silence",    "Silence",       atk.StatusChanceSilence,      atk.StatusDurationSilence),
        ("Darkness",   "Obscurité",     atk.StatusChanceDarkness,     atk.StatusDurationDarkness),
        ("Shell",      "Carapace",      atk.StatusChanceShell,        atk.StatusDurationShell),
        ("Protect",    "Bouclier",      atk.StatusChanceProtect,      atk.StatusDurationProtect),
        ("Reflect",    "Reflet",        atk.StatusChanceReflect,      atk.StatusDurationReflect),
        ("NTide",      "NulMaree",      atk.StatusChanceNTide,        atk.StatusDurationNTide),
        ("NBlaze",     "NulFlamme",     atk.StatusChanceNBlaze,       atk.StatusDurationNBlaze),
        ("NShock",     "NulChoc",       atk.StatusChanceNShock,       atk.StatusDurationNShock),
        ("NFrost",     "NulFrimas",     atk.StatusChanceNFrost,       atk.StatusDurationNFrost),
        ("Regen",      "Régen",         atk.StatusChanceRegen,        atk.StatusDurationRegen),
        ("Haste",      "Hâte",          atk.StatusChanceHaste,        atk.StatusDurationHaste),
        ("Slow",       "Lenteur",       atk.StatusChanceSlow,         atk.StatusDurationSlow),
    ];

    private void AddAttackStatusBox(Grid grid, int row, int column, string tag, int value)
        => AddStatusBox(grid, row, column, tag, value, OnAttackMechanic_Changed);

    private void AddCommandStatusBox(Grid grid, int row, int column, string tag, int value)
        => AddStatusBox(grid, row, column, tag, value, OnCommandMechanic_Changed);

    private void AddItemStatusBox(Grid grid, int row, int column, string tag, int value)
        => AddStatusBox(grid, row, column, tag, value, OnItemMechanic_Changed);

    private static void AddStatusBox(Grid grid, int row, int column, string tag, int value, TextChangedEventHandler changedHandler)
    {
        var tb = new TextBox
        {
            Text = value.ToString(),
            Tag = tag,
            Margin = new Thickness(2),
            Padding = new Thickness(3, 2, 3, 2),
            Background = value > 0 ? Brushes.LemonChiffon : Brushes.White,
        };
        tb.TextChanged += changedHandler;
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, column);
        grid.Children.Add(tb);
    }

    private static void AddCell(Grid grid, int row, int column, string text, FontWeight? bold = null)
    {
        var tb = new TextBlock
        {
            Text = text,
            Margin = new Thickness(2, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (bold != null) tb.FontWeight = bold.Value;
        Grid.SetRow(tb, row); Grid.SetColumn(tb, column);
        grid.Children.Add(tb);
    }

    private enum AttackSource { Both, MonMagic1, MonMagic2 }

    private class AttackSourceOption
    {
        public string DisplayName { get; }
        public AttackSource Source { get; }
        public AttackSourceOption(string displayName, AttackSource source) { DisplayName = displayName; Source = source; }
        public override string ToString() => DisplayName;
    }

    // =========================================================================
    // ONGLET COMMANDES JOUEURS / CHIMÈRES
    // =========================================================================

    private readonly ObservableCollection<CommandListItem> _commandListItems = new();
    private List<CommandListItem> _allCommandItems = new();
    private CommandCategory _currentCommandCategory = CommandCategory.All;
    private int? _currentCommandCharacterFilter; // null = tous personnages

    /// <summary>
    /// Initialise l'onglet Commandes : remplit le sélecteur de catégorie + de personnage,
    /// puis construit la liste initiale.
    /// </summary>
    private void PopulateCommandTab(SpiraWorkspace workspace)
    {
        CommandListBox.ItemsSource = _commandListItems;
        CommandListBox.DisplayMemberPath = nameof(CommandListItem.DisplayName);

        _suppressLanguageEvents = true;
        try
        {
            // Catégories
            CommandCategorySelector.Items.Clear();
            CommandCategorySelector.Items.Add(new CommandCategoryOption("Toutes les commandes", CommandCategory.All));
            CommandCategorySelector.Items.Add(new CommandCategoryOption("Capacités personnages", CommandCategory.PlayerAbility));
            CommandCategorySelector.Items.Add(new CommandCategoryOption("Overdrives personnages", CommandCategory.PlayerOverdrive));
            CommandCategorySelector.Items.Add(new CommandCategoryOption("Capacités Chimères", CommandCategory.AeonAbility));
            CommandCategorySelector.Items.Add(new CommandCategoryOption("Overdrives Chimères", CommandCategory.AeonOverdrive));
            CommandCategorySelector.Items.Add(new CommandCategoryOption("Utilisables par tous", CommandCategory.UsableByAll));
            CommandCategorySelector.SelectedIndex = 0;
            _currentCommandCategory = CommandCategory.All;

            // Personnages : "Tous" + tous les noms FFX
            CommandCharacterSelector.Items.Clear();
            CommandCharacterSelector.Items.Add(new CharacterOption("Tous personnages", null));
            foreach (var id in PlayerCharacters.KnownCharacters)
            {
                CommandCharacterSelector.Items.Add(new CharacterOption(PlayerCharacters.GetName(id) ?? $"#{id:X2}", id));
            }
            CommandCharacterSelector.SelectedIndex = 0;
            _currentCommandCharacterFilter = null;

            AddCommandEntryButton.IsEnabled = workspace.PlayerCommandsByLanguage.Count > 0;
        }
        finally
        {
            _suppressLanguageEvents = false;
        }

        RebuildCommandList();
    }

    private void OnCommandCategory_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageEvents || _workspace == null) return;
        if (CommandCategorySelector.SelectedItem is not CommandCategoryOption opt) return;
        _currentCommandCategory = opt.Category;
        RebuildCommandList();
    }

    private void OnCommandCharacter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageEvents || _workspace == null) return;
        if (CommandCharacterSelector.SelectedItem is not CharacterOption opt) return;
        _currentCommandCharacterFilter = opt.CharacterId;
        RebuildCommandList();
    }

    private void OnAddCommandEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null) return;

        var files = _workspace.PlayerCommandsByLanguage.Values.ToList();
        if (files.Count == 0)
        {
            MessageBox.Show(this,
                "Aucun command.bin n'est chargé dans ce workspace.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newIds = new List<int>();
        foreach (var file in files)
            newIds.Add(file.AppendCloneOf(0));

        var selectedGlobalId = newIds[0];
        SelectCommandCategory(CommandCategory.All);
        SelectCommandCharacter(null);
        CommandFilterBox.Clear();
        RebuildCommandList();
        SelectCommandById(selectedGlobalId);

        CommandEditStatusText.Text =
            $"Nouvelle entrée command.bin 0x{selectedGlobalId:X4} créée depuis 0x3000 (sauvegarde avec Ctrl+S)";
        UpdateSaveStatusUI();
    }

    private void SelectCommandCategory(CommandCategory category)
    {
        foreach (var item in CommandCategorySelector.Items)
        {
            if (item is CommandCategoryOption option && option.Category == category)
            {
                CommandCategorySelector.SelectedItem = item;
                _currentCommandCategory = category;
                return;
            }
        }

        _currentCommandCategory = category;
    }

    private void SelectCommandCharacter(int? characterId)
    {
        foreach (var item in CommandCharacterSelector.Items)
        {
            if (item is CharacterOption option && option.CharacterId == characterId)
            {
                CommandCharacterSelector.SelectedItem = item;
                _currentCommandCharacterFilter = characterId;
                return;
            }
        }

        _currentCommandCharacterFilter = characterId;
    }

    private void SelectCommandById(int globalId)
    {
        foreach (var item in _commandListItems)
        {
            if (item.GlobalId == globalId)
            {
                CommandListBox.SelectedItem = item;
                CommandListBox.ScrollIntoView(item);
                break;
            }
        }
    }

    private void OnCommandFilter_Changed(object sender, TextChangedEventArgs e)
    {
        CommandFilterPlaceholder.Visibility = string.IsNullOrEmpty(CommandFilterBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        ApplyCommandFilter();
    }

    /// <summary>Reconstruit la liste de commandes selon la catégorie + personnage + langue.</summary>
    private void RebuildCommandList()
    {
        if (_workspace == null) return;

        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage;
        if (lang == null || !_workspace.PlayerCommandsByLanguage.ContainsKey(lang))
            lang = _workspace.PlayerCommandsByLanguage.Keys.FirstOrDefault();

        if (lang == null)
        {
            _allCommandItems.Clear();
            ApplyCommandFilter();
            return;
        }

        var file = _workspace.PlayerCommandsByLanguage[lang];
        var charset = _workspace.GetCharsetForLanguage(lang);
        var items = new List<CommandListItem>();

        for (int i = 0; i < file.Count; i++)
        {
            var attack = file.Attacks[i];

            // Filtre par catégorie
            if (!MatchesCategory(attack, _currentCommandCategory))
                continue;

            // Filtre par personnage
            if (_currentCommandCharacterFilter != null
                && attack.CharacterUser != _currentCommandCharacterFilter
                && attack.CharacterUser != PlayerCharacters.UsableAll)
                continue;

            var name = file.GetName(i, charset);
            var globalId = file.MinIndex + i;
            items.Add(new CommandListItem
            {
                File = file,
                Attack = attack,
                RelativeIndex = i,
                GlobalId = globalId,
                Name = string.IsNullOrWhiteSpace(name) ? "(sans nom)" : name,
                OwnerName = PlayerCharacters.GetName(attack.CharacterUser) ?? "?",
                Ownership = attack.GetOwnership(),
            });
        }

        _allCommandItems = items;
        ApplyCommandFilter();
    }

    private static bool MatchesCategory(AttackData attack, CommandCategory category)
    {
        var ownership = attack.GetOwnership();
        return category switch
        {
            CommandCategory.All             => true,
            CommandCategory.PlayerAbility   => ownership == AttackOwnership.PlayerAbility,
            // Note : pour identifier un overdrive, on regarde si la commande appartient à un perso humain
            // ET que le flag emptiesOverdriveBar serait actif. Comme ce flag exact n'est pas exposé,
            // on heuristique : un overdrive joueur, c'est typiquement un perso humain qui a un coût OD > 0
            // ou qui appartient au sous-menu overdrive (mais on n'a pas la table des sub-menus en C#).
            // Pour l'instant on les regroupe avec les capacités joueurs et on les distinguera par le coût OD.
            CommandCategory.PlayerOverdrive => ownership == AttackOwnership.PlayerAbility && attack.CostOD > 0,
            CommandCategory.AeonAbility     => ownership == AttackOwnership.AeonAbility,
            CommandCategory.AeonOverdrive   => ownership == AttackOwnership.AeonOverdrive,
            CommandCategory.UsableByAll     => ownership == AttackOwnership.UsableByAll,
            _ => true,
        };
    }

    private void ApplyCommandFilter()
    {
        var filter = CommandFilterBox.Text.Trim();
        IEnumerable<CommandListItem> filtered = _allCommandItems;
        if (!string.IsNullOrEmpty(filter))
        {
            filtered = filtered.Where(item =>
                item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }
        _commandListItems.Clear();
        foreach (var item in filtered) _commandListItems.Add(item);

        CommandCountText.Text = _commandListItems.Count == _allCommandItems.Count
            ? $"{_allCommandItems.Count} commandes"
            : $"{_commandListItems.Count} / {_allCommandItems.Count} commandes";
    }

    private void OnCommandSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CommandListBox.SelectedItem is not CommandListItem item)
        {
            NoCommandSelectedMessage.Visibility = Visibility.Visible;
            CommandDetailsPanel.Visibility = Visibility.Collapsed;
            return;
        }
        DisplayCommand(item);
    }

    private string? GetEffectiveCommandLanguage()
    {
        if (_workspace == null) return null;

        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage;
        if (lang != null && _workspace.PlayerCommandsByLanguage.ContainsKey(lang))
            return lang;

        return _workspace.PlayerCommandsByLanguage.Keys.FirstOrDefault();
    }

    private void DisplayCommand(CommandListItem item)
    {
        if (_workspace == null) return;

        NoCommandSelectedMessage.Visibility = Visibility.Collapsed;
        CommandDetailsPanel.Visibility = Visibility.Visible;

        CommandHeaderText.Text = item.DisplayName;

        var lang = GetEffectiveCommandLanguage() ?? _currentLanguage ?? _workspace.PreferredDisplayLanguage ?? "jp";
        CommandTextsGroup.Header = $"Texte — {LanguageDisplayName(lang)}";

        var charset = _workspace.GetCharsetForLanguage(lang);
        AttackFile? fileInLang = _workspace.PlayerCommandsByLanguage.GetValueOrDefault(lang);

        _suppressCommandTextEvents = true;
        try
        {
            if (fileInLang != null && item.RelativeIndex < fileInLang.Count)
            {
                var texts = fileInLang.GetTexts(item.RelativeIndex, charset);
                CmdNameBox.Text = texts?.Name ?? "";
                CmdSimpleNameBox.Text = texts?.SimplifiedName ?? "";
                CmdDescBox.Text = texts?.Description ?? "";
                CmdSimpleDescBox.Text = texts?.SimplifiedDescription ?? "";
            }
            else
            {
                CmdNameBox.Text = CmdSimpleNameBox.Text = CmdDescBox.Text = CmdSimpleDescBox.Text = "(non disponible)";
            }

            ApplyCommandTextsButton.IsEnabled = false;
            RevertCommandTextsButton.IsEnabled = false;
            CommandEditStatusText.Text = "";
        }
        finally
        {
            _suppressCommandTextEvents = false;
        }

        CommandInfoText.Text =
            $"Possesseur : {item.OwnerName}  •  Catégorie : {OwnershipLabel(item.Ownership)}  •  " +
            $"ID global : 0x{item.GlobalId:X4} ({item.GlobalId})  •  Index : {item.RelativeIndex}";

        var atk = item.Attack;
        _suppressCommandMechanicEvents = true;
        try
        {
            CmdPowerBox.Text = atk.AttackPower.ToString();
            CmdAccuracyBox.Text = atk.AttackAccuracy.ToString();
            CmdHitCountBox.Text = atk.HitCount.ToString();
            CmdFormulaBox.Text = atk.DamageFormula.ToString();
            CmdCostMpBox.Text = atk.CostMP.ToString();
            CmdCostOdBox.Text = atk.CostOD.ToString();
            CmdCritBox.Text = atk.AttackCritBonus.ToString();
            CmdShatterBox.Text = atk.ShatterChance.ToString();
            CmdMoveRankBox.Text = atk.MoveRank.ToString();

            CmdAnim1Box.Text = $"0x{atk.Anim1:X4}";
            CmdAnim2Box.Text = $"0x{atk.Anim2:X4}";
            CmdIconBox.Text = atk.Icon.ToString();
            CmdCasterAnimationBox.Text = atk.CasterAnimation.ToString();
            CmdMenuPropsBox.Text = $"0x{atk.MenuProperties16:X2}";
            CmdSubsubMenuBox.Text = $"0x{atk.SubsubMenuCategorization:X2}";
            CmdSubMenuBox.Text = $"0x{atk.SubMenuCategorization:X2}";
            SelectCommandOwner(atk.CharacterUser);
            CmdTargetsAllowedBox.Text = $"0x{atk.TargetsAllowedApparently:X2}";
            CmdMisc1CBox.Text = $"0x{atk.MiscProperties1C:X2}";
            CmdMisc1DBox.Text = $"0x{atk.MiscProperties1D:X2}";
            CmdMisc1EBox.Text = $"0x{atk.MiscProperties1E:X2}";
            CmdAnimPropsBox.Text = $"0x{atk.AnimationProperties1F:X2}";
            CmdStealGilBox.Text = $"0x{atk.StealGilByte:X2}";
            CmdPartyPreviewBox.Text = $"0x{atk.PartyPreviewByte:X2}";
            CmdOrderingIndexBox.Text = atk.OrderingIndexInMenu.ToString();
            CmdSphereTypeBox.Text = $"0x{atk.SphereTypeForSphereGrid:X2}";
            CmdAlwaysZero5EBox.Text = $"0x{atk.AlwaysZero5E:X2}";
            CmdAlwaysZero5FBox.Text = $"0x{atk.AlwaysZero5F:X2}";
            CmdStatBuffValueBox.Text = atk.StatBuffValue.ToString();
            CmdOverdriveCategoryBox.Text = $"0x{atk.OverdriveCategorizationByte:X2}";
            CmdSpecialBuffBox.Text = $"0x{atk.SpecialBuffInflict:X4}";

            FillEditableCommandFlagsPanel(CmdMisc1CFlagsPanel, "CmdMisc1C", atk.MiscProperties1C, FfxAttackFlags.MiscProperties1C);
            FillEditableCommandFlagsPanel(CmdMisc1DFlagsPanel, "CmdMisc1D", atk.MiscProperties1D, FfxAttackFlags.MiscProperties1D);
            FillEditableCommandFlagsPanel(CmdMisc1EFlagsPanel, "CmdMisc1E", atk.MiscProperties1E, FfxAttackFlags.MiscProperties1E);
            FillEditableCommandFlagsPanel(CmdAnimFlagsPanel, "CmdAnimFlags", atk.AnimationProperties1F, FfxAttackFlags.AnimationProperties1F);
            FillEditableCommandFlagsPanel(CmdDamagePropsPanel, "CmdDamageProps", atk.DamageProperties20, FfxAttackFlags.DamageProperties);
            FillEditableCommandFlagsPanel(CmdDamageClassPanel, "CmdDamageClass", atk.DamageClass, FfxAttackFlags.DamageClass);
            FillEditableCommandFlagsPanel(CmdTargetingPanel, "CmdTargeting", atk.TargetingFlags, FfxAttackFlags.Targeting);
            FillEditableCommandFlagsPanel(CmdElementsPanel, "CmdElement", atk.ElementFlags, FfxStatusFlags.Elements);
            FillEditableCommandFlagsPanel(CmdExtraStatusPanel, "CmdExtraStatus", atk.ExtraStatusInflict, FfxAttackFlags.ExtraStatus);
            FillEditableCommandFlagsPanel(CmdStatBuffPanel, "CmdStatBuff", atk.StatBuffFlags, FfxAttackFlags.StatBuffs);

            BuildCommandStatusGrid(atk);
            ApplyCommandMechanicsButton.IsEnabled = false;
            CopyCommandMechanicsButton.IsEnabled = true;
            PasteCommandMechanicsButton.IsEnabled = _copiedCommandMechanics != null;
            ApplyCommandMechanicsAllLanguagesButton.IsEnabled = true;
            ApplyAllCommandMechanicsAllLanguagesButton.IsEnabled = true;
            RevertCommandMechanicsButton.IsEnabled = false;
            CommandMechanicsStatusText.Text = "";
        }
        finally
        {
            _suppressCommandMechanicEvents = false;
        }
    }

    private void OnCommandText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressCommandTextEvents) return;
        ApplyCommandTextsButton.IsEnabled = true;
        RevertCommandTextsButton.IsEnabled = true;
        CommandEditStatusText.Text = "● Modifications non appliquées";
    }

    private void OnRevertCommandTexts_Click(object sender, RoutedEventArgs e)
    {
        if (CommandListBox.SelectedItem is CommandListItem item)
            DisplayCommand(item);
    }

    private void OnApplyCommandTexts_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || CommandListBox.SelectedItem is not CommandListItem item)
            return;

        var lang = GetEffectiveCommandLanguage() ?? _currentLanguage ?? _workspace.PreferredDisplayLanguage ?? "jp";
        var charset = _workspace.GetCharsetForLanguage(lang);
        if (charset == null)
        {
            MessageBox.Show(this,
                "La charset de cette langue n'est pas chargée — impossible de réencoder les textes.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var file = GetCommandFileForItem(item, lang);
        if (file == null || item.RelativeIndex < 0 || item.RelativeIndex >= file.Count)
        {
            MessageBox.Show(this,
                $"Aucun fichier command.bin ne couvre cette commande dans la langue {lang}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryResolveUnsupportedChars(charset, lang,
                CmdNameBox, CmdSimpleNameBox, CmdDescBox, CmdSimpleDescBox))
            return;

        var newTexts = new AttackTexts
        {
            Name = CmdNameBox.Text,
            SimplifiedName = CmdSimpleNameBox.Text,
            Description = CmdDescBox.Text,
            SimplifiedDescription = CmdSimpleDescBox.Text,
        };

        if (!file.SetTexts(item.RelativeIndex, newTexts, charset))
        {
            MessageBox.Show(this, "Échec de l'écriture en mémoire (index invalide).",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        item.File = file;
        item.Attack = file.Attacks[item.RelativeIndex];
        item.Name = string.IsNullOrWhiteSpace(newTexts.Name) ? "(sans nom)" : newTexts.Name;
        CommandHeaderText.Text = item.DisplayName;

        var selectedIndex = item.RelativeIndex;
        ApplyCommandFilter();
        foreach (var candidate in _commandListItems)
        {
            if (candidate.RelativeIndex == selectedIndex)
            {
                CommandListBox.SelectedItem = candidate;
                break;
            }
        }

        ApplyCommandTextsButton.IsEnabled = false;
        RevertCommandTextsButton.IsEnabled = false;
        CommandEditStatusText.Text = "✓ Modifications appliquées (sauvegarde avec Ctrl+S)";
        UpdateSaveStatusUI();
    }

    private AttackFile? GetCommandFileForItem(CommandListItem item, string language)
    {
        if (_workspace == null) return null;
        return _workspace.PlayerCommandsByLanguage.TryGetValue(language, out var file)
            ? file
            : item.File;
    }

    private void PopulateCommandOwnerSelector()
    {
        if (CmdOwnerSelector.Items.Count > 0) return;

        foreach (var id in PlayerCharacters.CommandOwners)
        {
            var name = PlayerCharacters.GetName(id) ?? $"0x{id:X2}";
            CmdOwnerSelector.Items.Add(new CharacterOption($"{name} (0x{id:X2})", id));
        }
    }

    private void SelectCommandOwner(int characterUser)
    {
        PopulateCommandOwnerSelector();

        foreach (var option in CmdOwnerSelector.Items.OfType<CharacterOption>())
        {
            if (option.CharacterId == characterUser)
            {
                CmdOwnerSelector.SelectedItem = option;
                return;
            }
        }

        var unknown = new CharacterOption($"Inconnu 0x{characterUser:X2}", characterUser);
        CmdOwnerSelector.Items.Add(unknown);
        CmdOwnerSelector.SelectedItem = unknown;
    }

    private bool TryReadSelectedCommandOwner(out int value)
    {
        if (CmdOwnerSelector.SelectedItem is CharacterOption { CharacterId: int id })
        {
            value = id;
            return true;
        }

        value = 0;
        MessageBox.Show(this, "Sélectionne un possesseur pour cette commande.",
            "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private void OnCommandMechanic_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressCommandMechanicEvents) return;
        ApplyCommandMechanicsButton.IsEnabled = true;
        RevertCommandMechanicsButton.IsEnabled = true;
        CommandMechanicsStatusText.Text = "● Mécaniques non appliquées";
    }

    private void OnRevertCommandMechanics_Click(object sender, RoutedEventArgs e)
    {
        if (CommandListBox.SelectedItem is CommandListItem item)
            DisplayCommand(item);
    }

    private void OnApplyCommandMechanics_Click(object sender, RoutedEventArgs e)
    {
        _lastCommandMechanicsApplySucceeded = false;
        if (_workspace == null || CommandListBox.SelectedItem is not CommandListItem item)
            return;

        var lang = GetEffectiveCommandLanguage() ?? _currentLanguage ?? _workspace.PreferredDisplayLanguage ?? "jp";
        var file = GetCommandFileForItem(item, lang);
        if (file == null || item.RelativeIndex < 0 || item.RelativeIndex >= file.Count)
        {
            MessageBox.Show(this,
                $"Aucun fichier command.bin ne couvre cette commande dans la langue {lang}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryReadIntBox(CmdPowerBox, "Puissance", 0, 255, out var power)) return;
        if (!TryReadIntBox(CmdAccuracyBox, "Précision", 0, 255, out var accuracy)) return;
        if (!TryReadIntBox(CmdHitCountBox, "Nombre de coups", 0, 255, out var hitCount)) return;
        if (!TryReadIntBox(CmdFormulaBox, "Formule", 0, 255, out var formula)) return;
        if (!TryReadIntBox(CmdCostMpBox, "Coût MP", 0, 255, out var costMp)) return;
        if (!TryReadIntBox(CmdCostOdBox, "Coût OD", 0, 255, out var costOd)) return;
        if (!TryReadIntBox(CmdCritBox, "Bonus critique", 0, 255, out var crit)) return;
        if (!TryReadIntBox(CmdShatterBox, "Chance pétrification massive", 0, 255, out var shatter)) return;
        if (!TryReadIntBox(CmdMoveRankBox, "Move rank", 0, 255, out var moveRank)) return;

        if (!TryReadIntBox(CmdAnim1Box, "Animation 1", 0, 0xFFFF, out var anim1)) return;
        if (!TryReadIntBox(CmdAnim2Box, "Animation 2", 0, 0xFFFF, out var anim2)) return;
        if (!TryReadIntBox(CmdIconBox, "Icône", 0, 255, out var icon)) return;
        if (!TryReadIntBox(CmdCasterAnimationBox, "Animation caster", 0, 255, out var casterAnim)) return;
        if (!TryReadIntBox(CmdMenuPropsBox, "Menu 0x16", 0, 255, out var menuProps)) return;
        if (!TryReadIntBox(CmdSubsubMenuBox, "Sous-menu A", 0, 255, out var subsubMenu)) return;
        if (!TryReadIntBox(CmdSubMenuBox, "Sous-menu B", 0, 255, out var subMenu)) return;
        if (!TryReadSelectedCommandOwner(out var characterUser)) return;
        if (!TryReadIntBox(CmdTargetsAllowedBox, "Cibles permises", 0, 255, out var targetsAllowed)) return;
        if (!TryReadIntBox(CmdMisc1CBox, "Misc 0x1C", 0, 255, out var misc1C)) return;
        if (!TryReadIntBox(CmdMisc1DBox, "Misc 0x1D", 0, 255, out var misc1D)) return;
        if (!TryReadIntBox(CmdMisc1EBox, "Misc 0x1E", 0, 255, out var misc1E)) return;
        if (!TryReadIntBox(CmdAnimPropsBox, "Animation props", 0, 255, out var animProps)) return;
        if (!TryReadIntBox(CmdStealGilBox, "Steal/Gil", 0, 255, out var stealGil)) return;
        if (!TryReadIntBox(CmdPartyPreviewBox, "Preview", 0, 255, out var partyPreview)) return;
        if (!TryReadIntBox(CmdOrderingIndexBox, "Ordre menu", 0, 255, out var orderingIndex)) return;
        if (!TryReadIntBox(CmdSphereTypeBox, "Type sphérier", 0, 255, out var sphereType)) return;
        if (!TryReadIntBox(CmdAlwaysZero5EBox, "Byte 0x5E", 0, 255, out var alwaysZero5E)) return;
        if (!TryReadIntBox(CmdAlwaysZero5FBox, "Byte 0x5F", 0, 255, out var alwaysZero5F)) return;
        if (!TryReadIntBox(CmdStatBuffValueBox, "Valeur buff", 0, 255, out var statBuffValue)) return;
        if (!TryReadIntBox(CmdOverdriveCategoryBox, "OD catégorie", 0, 255, out var odCategory)) return;
        if (!TryReadIntBox(CmdSpecialBuffBox, "Buff spécial", 0, 0xFFFF, out var specialBuff)) return;

        if (!TryReadCommandStatusChance("Death", "Mort", out var stDeath)) return;
        if (!TryReadCommandStatusChance("Zombie", "Zombie", out var stZombie)) return;
        if (!TryReadCommandStatusChance("Petrify", "Pétrification", out var stPetrify)) return;
        if (!TryReadCommandStatusChance("Poison", "Poison", out var stPoison)) return;
        if (!TryReadCommandStatusChance("PowerBreak", "Power Break", out var stPowerBreak)) return;
        if (!TryReadCommandStatusChance("MagicBreak", "Magic Break", out var stMagicBreak)) return;
        if (!TryReadCommandStatusChance("ArmorBreak", "Armor Break", out var stArmorBreak)) return;
        if (!TryReadCommandStatusChance("MentalBreak", "Mental Break", out var stMentalBreak)) return;
        if (!TryReadCommandStatusChance("Confuse", "Confusion", out var stConfuse)) return;
        if (!TryReadCommandStatusChance("Berserk", "Berserk", out var stBerserk)) return;
        if (!TryReadCommandStatusChance("Provoke", "Provoque", out var stProvoke)) return;
        if (!TryReadCommandStatusChance("Threaten", "Menace", out var stThreaten)) return;
        if (!TryReadCommandStatusChance("Sleep", "Sommeil", out var stSleep)) return;
        if (!TryReadCommandStatusChance("Silence", "Silence", out var stSilence)) return;
        if (!TryReadCommandStatusChance("Darkness", "Obscurité", out var stDarkness)) return;
        if (!TryReadCommandStatusChance("Shell", "Carapace", out var stShell)) return;
        if (!TryReadCommandStatusChance("Protect", "Bouclier", out var stProtect)) return;
        if (!TryReadCommandStatusChance("Reflect", "Reflet", out var stReflect)) return;
        if (!TryReadCommandStatusChance("NTide", "NulMaree", out var stNTide)) return;
        if (!TryReadCommandStatusChance("NBlaze", "NulFlamme", out var stNBlaze)) return;
        if (!TryReadCommandStatusChance("NShock", "NulChoc", out var stNShock)) return;
        if (!TryReadCommandStatusChance("NFrost", "NulFrimas", out var stNFrost)) return;
        if (!TryReadCommandStatusChance("Regen", "Régen", out var stRegen)) return;
        if (!TryReadCommandStatusChance("Haste", "Hâte", out var stHaste)) return;
        if (!TryReadCommandStatusChance("Slow", "Lenteur", out var stSlow)) return;

        if (!TryReadCommandStatusDuration("Sleep", "Sommeil", out var durSleep)) return;
        if (!TryReadCommandStatusDuration("Silence", "Silence", out var durSilence)) return;
        if (!TryReadCommandStatusDuration("Darkness", "Obscurité", out var durDarkness)) return;
        if (!TryReadCommandStatusDuration("Shell", "Carapace", out var durShell)) return;
        if (!TryReadCommandStatusDuration("Protect", "Bouclier", out var durProtect)) return;
        if (!TryReadCommandStatusDuration("Reflect", "Reflet", out var durReflect)) return;
        if (!TryReadCommandStatusDuration("NTide", "NulMaree", out var durNTide)) return;
        if (!TryReadCommandStatusDuration("NBlaze", "NulFlamme", out var durNBlaze)) return;
        if (!TryReadCommandStatusDuration("NShock", "NulChoc", out var durNShock)) return;
        if (!TryReadCommandStatusDuration("NFrost", "NulFrimas", out var durNFrost)) return;
        if (!TryReadCommandStatusDuration("Regen", "Régen", out var durRegen)) return;
        if (!TryReadCommandStatusDuration("Haste", "Hâte", out var durHaste)) return;
        if (!TryReadCommandStatusDuration("Slow", "Lenteur", out var durSlow)) return;

        var atk = file.Attacks[item.RelativeIndex];
        atk.AttackPower = power;
        atk.AttackAccuracy = accuracy;
        atk.HitCount = hitCount;
        atk.DamageFormula = formula;
        atk.CostMP = costMp;
        atk.CostOD = costOd;
        atk.AttackCritBonus = crit;
        atk.ShatterChance = shatter;
        atk.MoveRank = moveRank;

        atk.Anim1 = anim1;
        atk.Anim2 = anim2;
        atk.Icon = icon;
        atk.CasterAnimation = casterAnim;
        atk.MenuProperties16 = menuProps;
        atk.SubsubMenuCategorization = subsubMenu;
        atk.SubMenuCategorization = subMenu;
        atk.CharacterUser = characterUser;
        atk.TargetsAllowedApparently = targetsAllowed;
        atk.MiscProperties1C = MergeKnownBitfield(misc1C,
            ReadBitfieldFromChecks(CmdMisc1CFlagsPanel, "CmdMisc1C"), FfxAttackFlags.MiscProperties1C);
        atk.MiscProperties1D = MergeKnownBitfield(misc1D,
            ReadBitfieldFromChecks(CmdMisc1DFlagsPanel, "CmdMisc1D"), FfxAttackFlags.MiscProperties1D);
        atk.MiscProperties1E = MergeKnownBitfield(misc1E,
            ReadBitfieldFromChecks(CmdMisc1EFlagsPanel, "CmdMisc1E"), FfxAttackFlags.MiscProperties1E);
        atk.AnimationProperties1F = MergeKnownBitfield(animProps,
            ReadBitfieldFromChecks(CmdAnimFlagsPanel, "CmdAnimFlags"), FfxAttackFlags.AnimationProperties1F);
        atk.StealGilByte = stealGil;
        atk.PartyPreviewByte = partyPreview;
        atk.OrderingIndexInMenu = orderingIndex;
        atk.SphereTypeForSphereGrid = sphereType;
        atk.AlwaysZero5E = alwaysZero5E;
        atk.AlwaysZero5F = alwaysZero5F;

        atk.DamageProperties20 = MergeKnownBitfield(atk.DamageProperties20,
            ReadBitfieldFromChecks(CmdDamagePropsPanel, "CmdDamageProps"), FfxAttackFlags.DamageProperties);
        atk.DamageClass = MergeKnownBitfield(atk.DamageClass,
            ReadBitfieldFromChecks(CmdDamageClassPanel, "CmdDamageClass"), FfxAttackFlags.DamageClass);
        atk.TargetingFlags = MergeKnownBitfield(atk.TargetingFlags,
            ReadBitfieldFromChecks(CmdTargetingPanel, "CmdTargeting"), FfxAttackFlags.Targeting);
        atk.ElementFlags = MergeKnownBitfield(atk.ElementFlags,
            ReadBitfieldFromChecks(CmdElementsPanel, "CmdElement"), FfxStatusFlags.Elements);
        atk.ExtraStatusInflict = MergeKnownBitfield(atk.ExtraStatusInflict,
            ReadBitfieldFromChecks(CmdExtraStatusPanel, "CmdExtraStatus"), FfxAttackFlags.ExtraStatus);
        atk.StatBuffFlags = MergeKnownBitfield(atk.StatBuffFlags,
            ReadBitfieldFromChecks(CmdStatBuffPanel, "CmdStatBuff"), FfxAttackFlags.StatBuffs);
        atk.StatBuffValue = statBuffValue;
        atk.OverdriveCategorizationByte = odCategory;
        atk.SpecialBuffInflict = specialBuff;

        atk.StatusChanceDeath = stDeath;
        atk.StatusChanceZombie = stZombie;
        atk.StatusChancePetrify = stPetrify;
        atk.StatusChancePoison = stPoison;
        atk.StatusChancePowerBreak = stPowerBreak;
        atk.StatusChanceMagicBreak = stMagicBreak;
        atk.StatusChanceArmorBreak = stArmorBreak;
        atk.StatusChanceMentalBreak = stMentalBreak;
        atk.StatusChanceConfuse = stConfuse;
        atk.StatusChanceBerserk = stBerserk;
        atk.StatusChanceProvoke = stProvoke;
        atk.StatusChanceThreaten = stThreaten;
        atk.StatusChanceSleep = stSleep;
        atk.StatusChanceSilence = stSilence;
        atk.StatusChanceDarkness = stDarkness;
        atk.StatusChanceShell = stShell;
        atk.StatusChanceProtect = stProtect;
        atk.StatusChanceReflect = stReflect;
        atk.StatusChanceNTide = stNTide;
        atk.StatusChanceNBlaze = stNBlaze;
        atk.StatusChanceNShock = stNShock;
        atk.StatusChanceNFrost = stNFrost;
        atk.StatusChanceRegen = stRegen;
        atk.StatusChanceHaste = stHaste;
        atk.StatusChanceSlow = stSlow;

        atk.StatusDurationSleep = durSleep;
        atk.StatusDurationSilence = durSilence;
        atk.StatusDurationDarkness = durDarkness;
        atk.StatusDurationShell = durShell;
        atk.StatusDurationProtect = durProtect;
        atk.StatusDurationReflect = durReflect;
        atk.StatusDurationNTide = durNTide;
        atk.StatusDurationNBlaze = durNBlaze;
        atk.StatusDurationNShock = durNShock;
        atk.StatusDurationNFrost = durNFrost;
        atk.StatusDurationRegen = durRegen;
        atk.StatusDurationHaste = durHaste;
        atk.StatusDurationSlow = durSlow;

        file.MarkDirty();
        item.File = file;
        item.Attack = atk;
        item.OwnerName = PlayerCharacters.GetName(atk.CharacterUser) ?? "?";
        item.Ownership = atk.GetOwnership();
        CommandHeaderText.Text = item.DisplayName;
        CommandInfoText.Text =
            $"Possesseur : {item.OwnerName}  •  Catégorie : {OwnershipLabel(item.Ownership)}  •  " +
            $"ID global : 0x{item.GlobalId:X4} ({item.GlobalId})  •  Index : {item.RelativeIndex}";

        ApplyCommandMechanicsButton.IsEnabled = false;
        RevertCommandMechanicsButton.IsEnabled = false;
        CommandMechanicsStatusText.Text = "✓ Mécaniques appliquées (sauvegarde avec Ctrl+S)";
        UpdateSaveStatusUI();
        _lastCommandMechanicsApplySucceeded = true;
    }

    private void OnApplyCommandMechanicsAllLanguages_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || CommandListBox.SelectedItem is not CommandListItem item)
            return;

        OnApplyCommandMechanics_Click(sender, e);
        if (!_lastCommandMechanicsApplySucceeded) return;

        var source = item.Attack;
        var touched = 0;
        foreach (var file in _workspace.PlayerCommandsByLanguage.Values)
        {
            if (item.RelativeIndex < 0 || item.RelativeIndex >= file.Count) continue;

            CopyAttackMechanics(source, file.Attacks[item.RelativeIndex]);
            file.MarkDirty();
            touched++;
        }

        CommandMechanicsStatusText.Text =
            $"✓ Mécaniques appliquées à {touched} langue(s) (sauvegarde avec Ctrl+S)";
        UpdateSaveStatusUI();
    }

    private void OnCopyCommandMechanics_Click(object sender, RoutedEventArgs e)
    {
        if (CommandListBox.SelectedItem is not CommandListItem item)
            return;

        if (ApplyCommandMechanicsButton.IsEnabled)
        {
            OnApplyCommandMechanics_Click(sender, e);
            if (!_lastCommandMechanicsApplySucceeded) return;
        }

        _copiedCommandMechanics = CloneAttackData(item.Attack);
        PasteCommandMechanicsButton.IsEnabled = true;
        CommandMechanicsStatusText.Text = "✓ Mécaniques copiées";
    }

    private void OnPasteCommandMechanics_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || _copiedCommandMechanics == null
            || CommandListBox.SelectedItem is not CommandListItem item)
            return;

        var lang = GetEffectiveCommandLanguage() ?? _currentLanguage ?? _workspace.PreferredDisplayLanguage ?? "jp";
        var file = GetCommandFileForItem(item, lang);
        if (file == null || item.RelativeIndex < 0 || item.RelativeIndex >= file.Count)
        {
            MessageBox.Show(this,
                $"Aucun fichier command.bin ne couvre cette commande dans la langue {lang}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CopyAttackMechanics(_copiedCommandMechanics, file.Attacks[item.RelativeIndex]);
        file.MarkDirty();
        item.File = file;
        item.Attack = file.Attacks[item.RelativeIndex];
        item.OwnerName = PlayerCharacters.GetName(item.Attack.CharacterUser) ?? "?";
        item.Ownership = item.Attack.GetOwnership();

        DisplayCommand(item);
        PasteCommandMechanicsButton.IsEnabled = true;
        CommandMechanicsStatusText.Text = "✓ Mécaniques collées (textes préservés, sauvegarde avec Ctrl+S)";
        UpdateSaveStatusUI();
    }

    private void OnApplyAllCommandMechanicsAllLanguages_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null)
            return;

        var sourceLang = GetEffectiveCommandLanguage() ?? _currentLanguage ?? _workspace.PreferredDisplayLanguage;
        if (sourceLang == null || !_workspace.PlayerCommandsByLanguage.TryGetValue(sourceLang, out var sourceFile))
        {
            MessageBox.Show(this,
                "Aucun fichier command.bin source n'est chargé.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (CommandListBox.SelectedItem is CommandListItem && ApplyCommandMechanicsButton.IsEnabled)
        {
            OnApplyCommandMechanics_Click(sender, e);
            if (!_lastCommandMechanicsApplySucceeded) return;
            sourceFile = _workspace.PlayerCommandsByLanguage[sourceLang];
        }

        var targetLanguageCount = _workspace.PlayerCommandsByLanguage.Keys
            .Count(lang => !string.Equals(lang, sourceLang, StringComparison.OrdinalIgnoreCase));
        if (!ConfirmBulkMechanicsCopy("commandes", sourceLang, targetLanguageCount))
            return;

        var touchedFiles = 0;
        var copiedEntries = 0;
        foreach (var (lang, file) in _workspace.PlayerCommandsByLanguage)
        {
            if (string.Equals(lang, sourceLang, StringComparison.OrdinalIgnoreCase))
                continue;

            copiedEntries += CopyAllAttackMechanics(sourceFile, file, ref touchedFiles);
        }

        CommandMechanicsStatusText.Text =
            $"✓ {copiedEntries} bloc(s) mécaniques copiés vers {touchedFiles} fichier(s) localisé(s) (sauvegarde avec Ctrl+S)";
        UpdateSaveStatusUI();
    }

    private bool TryReadCommandStatusChance(string key, string label, out int value)
    {
        var box = FindTaggedTextBox(CmdStatusGrid, $"CmdStatusChance:{key}");
        if (box == null)
        {
            value = 0;
            MessageBox.Show(this, $"Champ de chance introuvable : {label}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        return TryReadIntBox(box, $"{label} chance", 0, 255, out value);
    }

    private bool TryReadCommandStatusDuration(string key, string label, out int value)
    {
        var box = FindTaggedTextBox(CmdStatusGrid, $"CmdStatusDuration:{key}");
        if (box == null)
        {
            value = 0;
            MessageBox.Show(this, $"Champ de durée introuvable : {label}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        return TryReadIntBox(box, $"{label} durée", 0, 255, out value);
    }

    private void BuildCommandStatusGrid(AttackData atk)
    {
        CmdStatusGrid.Children.Clear();
        CmdStatusGrid.RowDefinitions.Clear();
        CmdStatusGrid.ColumnDefinitions.Clear();

        CmdStatusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        CmdStatusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        CmdStatusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        var rows = GetAttackStatusRows(atk);

        CmdStatusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddCell(CmdStatusGrid, 0, 0, "Statut", FontWeights.Bold);
        AddCell(CmdStatusGrid, 0, 1, "Chance %", FontWeights.Bold);
        AddCell(CmdStatusGrid, 0, 2, "Durée (tours)", FontWeights.Bold);

        for (int i = 0; i < rows.Length; i++)
        {
            var row = i + 1;
            CmdStatusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddCell(CmdStatusGrid, row, 0, rows[i].Label);
            AddCommandStatusBox(CmdStatusGrid, row, 1, $"CmdStatusChance:{rows[i].Key}", rows[i].Chance);
            if (rows[i].Duration is int duration)
                AddCommandStatusBox(CmdStatusGrid, row, 2, $"CmdStatusDuration:{rows[i].Key}", duration);
            else
                AddCell(CmdStatusGrid, row, 2, "—");
        }
    }

    private static string OwnershipLabel(AttackOwnership o) => o switch
    {
        AttackOwnership.PlayerAbility   => "Capacité joueur",
        AttackOwnership.PlayerOverdrive => "Overdrive joueur",
        AttackOwnership.AeonAbility     => "Capacité Chimère",
        AttackOwnership.AeonOverdrive   => "Overdrive Chimère",
        AttackOwnership.UsableByAll     => "Utilisable par tous",
        AttackOwnership.MonsterAttack   => "Attaque ennemie",
        _ => "?",
    };

    /// <summary>
    /// Variante générique de BuildAttackStatusGrid qui prend le grid en paramètre,
    /// pour pouvoir être appelée depuis l'onglet Attaques ou Commandes.
    /// </summary>
    private void BuildStatusGridForAttack(Grid targetGrid, AttackData atk)
    {
        targetGrid.Children.Clear();
        targetGrid.RowDefinitions.Clear();
        targetGrid.ColumnDefinitions.Clear();
        targetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        targetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        targetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

        var rows = new (string Label, int Chance, int? Duration)[]
        {
            ("Mort",          atk.StatusChanceDeath,        null),
            ("Zombie",        atk.StatusChanceZombie,       null),
            ("Pétrification", atk.StatusChancePetrify,      null),
            ("Poison",        atk.StatusChancePoison,       null),
            ("Power Break",   atk.StatusChancePowerBreak,   null),
            ("Magic Break",   atk.StatusChanceMagicBreak,   null),
            ("Armor Break",   atk.StatusChanceArmorBreak,   null),
            ("Mental Break",  atk.StatusChanceMentalBreak,  null),
            ("Confusion",     atk.StatusChanceConfuse,      null),
            ("Berserk",       atk.StatusChanceBerserk,      null),
            ("Provoque",      atk.StatusChanceProvoke,      null),
            ("Menace",        atk.StatusChanceThreaten,     null),
            ("Sommeil",       atk.StatusChanceSleep,        atk.StatusDurationSleep),
            ("Silence",       atk.StatusChanceSilence,      atk.StatusDurationSilence),
            ("Obscurité",     atk.StatusChanceDarkness,     atk.StatusDurationDarkness),
            ("Carapace",      atk.StatusChanceShell,        atk.StatusDurationShell),
            ("Bouclier",      atk.StatusChanceProtect,      atk.StatusDurationProtect),
            ("Reflet",        atk.StatusChanceReflect,      atk.StatusDurationReflect),
            ("NulMaree",      atk.StatusChanceNTide,        atk.StatusDurationNTide),
            ("NulFlamme",     atk.StatusChanceNBlaze,       atk.StatusDurationNBlaze),
            ("NulChoc",       atk.StatusChanceNShock,       atk.StatusDurationNShock),
            ("NulFrimas",     atk.StatusChanceNFrost,       atk.StatusDurationNFrost),
            ("Régen",         atk.StatusChanceRegen,        atk.StatusDurationRegen),
            ("Hâte",          atk.StatusChanceHaste,        atk.StatusDurationHaste),
            ("Lenteur",       atk.StatusChanceSlow,         atk.StatusDurationSlow),
        };

        var activeRows = rows.Where(r => r.Chance > 0).ToList();
        if (activeRows.Count == 0)
        {
            targetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var msg = new TextBlock { Text = "(aucun statut infligé)", Foreground = Brushes.Gray, FontStyle = FontStyles.Italic, Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetRow(msg, 0); Grid.SetColumn(msg, 0); Grid.SetColumnSpan(msg, 3);
            targetGrid.Children.Add(msg);
            return;
        }

        targetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddCell(targetGrid, 0, 0, "Statut", FontWeights.Bold);
        AddCell(targetGrid, 0, 1, "Chance %", FontWeights.Bold);
        AddCell(targetGrid, 0, 2, "Durée (tours)", FontWeights.Bold);

        for (int i = 0; i < activeRows.Count; i++)
        {
            var row = i + 1;
            targetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddCell(targetGrid, row, 0, activeRows[i].Label);
            AddCell(targetGrid, row, 1, activeRows[i].Chance.ToString());
            AddCell(targetGrid, row, 2, activeRows[i].Duration?.ToString() ?? "—");
        }
    }

    private enum CommandCategory
    {
        All,
        PlayerAbility,
        PlayerOverdrive,
        AeonAbility,
        AeonOverdrive,
        UsableByAll,
    }

    private class CommandCategoryOption
    {
        public string DisplayName { get; }
        public CommandCategory Category { get; }
        public CommandCategoryOption(string n, CommandCategory c) { DisplayName = n; Category = c; }
        public override string ToString() => DisplayName;
    }

    private class CharacterOption
    {
        public string DisplayName { get; }
        public int? CharacterId { get; }
        public CharacterOption(string n, int? id) { DisplayName = n; CharacterId = id; }
        public override string ToString() => DisplayName;
    }

    // =========================================================================
    // ONGLET OBJETS (item.bin)
    // =========================================================================

    private readonly ObservableCollection<ItemListItem> _itemListItems = new();
    private List<ItemListItem> _allItemItems = new();

    private void PopulateItemTab(SpiraWorkspace workspace)
    {
        ItemListBox.ItemsSource = _itemListItems;
        ItemListBox.DisplayMemberPath = nameof(ItemListItem.DisplayName);
        RebuildItemList();
    }

    private void OnItemFilter_Changed(object sender, TextChangedEventArgs e)
    {
        ItemFilterPlaceholder.Visibility = string.IsNullOrEmpty(ItemFilterBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        ApplyItemFilter();
    }

    private void RebuildItemList()
    {
        if (_workspace == null) return;

        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage;
        if (lang == null || !_workspace.ItemsByLanguage.ContainsKey(lang))
            lang = _workspace.ItemsByLanguage.Keys.FirstOrDefault();

        if (lang == null)
        {
            _allItemItems.Clear();
            ApplyItemFilter();
            return;
        }

        var file = _workspace.ItemsByLanguage[lang];
        var charset = _workspace.GetCharsetForLanguage(lang);
        var items = new List<ItemListItem>();

        for (int i = 0; i < file.Count; i++)
        {
            var attack = file.Attacks[i];
            var name = file.GetName(i, charset);
            var globalId = file.MinIndex + i;
            items.Add(new ItemListItem
            {
                File = file,
                Attack = attack,
                RelativeIndex = i,
                GlobalId = globalId,
                Name = string.IsNullOrWhiteSpace(name) ? "(sans nom)" : name,
            });
        }
        _allItemItems = items;
        ApplyItemFilter();
    }

    private void ApplyItemFilter()
    {
        var filter = ItemFilterBox.Text.Trim();
        IEnumerable<ItemListItem> filtered = _allItemItems;
        if (!string.IsNullOrEmpty(filter))
        {
            filtered = filtered.Where(item =>
                item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }
        _itemListItems.Clear();
        foreach (var item in filtered) _itemListItems.Add(item);

        ItemCountText.Text = _itemListItems.Count == _allItemItems.Count
            ? $"{_allItemItems.Count} objets"
            : $"{_itemListItems.Count} / {_allItemItems.Count} objets";
    }

    private void OnItemSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ItemListBox.SelectedItem is not ItemListItem item)
        {
            NoItemSelectedMessage.Visibility = Visibility.Visible;
            ItemDetailsPanel.Visibility = Visibility.Collapsed;
            return;
        }
        DisplayItem(item);
    }

    private void DisplayItem(ItemListItem item)
    {
        if (_workspace == null) return;

        NoItemSelectedMessage.Visibility = Visibility.Collapsed;
        ItemDetailsPanel.Visibility = Visibility.Visible;

        ItemHeaderText.Text = item.DisplayName;

        var lang = GetEffectiveItemLanguage() ?? _currentLanguage ?? _workspace.PreferredDisplayLanguage ?? "jp";
        ItemTextsGroup.Header = $"Texte — {LanguageDisplayName(lang)}";

        var charset = _workspace.GetCharsetForLanguage(lang);
        AttackFile? fileInLang = _workspace.ItemsByLanguage.GetValueOrDefault(lang);

        _suppressItemTextEvents = true;
        try
        {
            if (fileInLang != null && item.RelativeIndex < fileInLang.Count)
            {
                var texts = fileInLang.GetTexts(item.RelativeIndex, charset);
                ItemNameBox.Text = texts?.Name ?? "";
                ItemSimpleNameBox.Text = texts?.SimplifiedName ?? "";
                ItemDescBox.Text = texts?.Description ?? "";
                ItemSimpleDescBox.Text = texts?.SimplifiedDescription ?? "";
            }
            else
            {
                ItemNameBox.Text = ItemSimpleNameBox.Text = ItemDescBox.Text = ItemSimpleDescBox.Text = "(non disponible)";
            }

            ApplyItemTextsButton.IsEnabled = false;
            RevertItemTextsButton.IsEnabled = false;
            ItemEditStatusText.Text = "";
        }
        finally
        {
            _suppressItemTextEvents = false;
        }

        ItemInfoText.Text =
            $"ID global : 0x{item.GlobalId:X4} ({item.GlobalId})  •  Index : {item.RelativeIndex}";

        var atk = item.Attack;
        _suppressItemMechanicEvents = true;
        try
        {
            ItemPowerBox.Text = atk.AttackPower.ToString();
            ItemAccuracyBox.Text = atk.AttackAccuracy.ToString();
            ItemHitCountBox.Text = atk.HitCount.ToString();
            ItemFormulaBox.Text = atk.DamageFormula.ToString();
            ItemCostMpBox.Text = atk.CostMP.ToString();
            ItemCostOdBox.Text = atk.CostOD.ToString();
            ItemCritBox.Text = atk.AttackCritBonus.ToString();
            ItemShatterBox.Text = atk.ShatterChance.ToString();
            ItemMoveRankBox.Text = atk.MoveRank.ToString();

            ItemAnim1Box.Text = $"0x{atk.Anim1:X4}";
            ItemAnim2Box.Text = $"0x{atk.Anim2:X4}";
            ItemIconBox.Text = atk.Icon.ToString();
            ItemCasterAnimationBox.Text = atk.CasterAnimation.ToString();
            ItemMenuPropsBox.Text = $"0x{atk.MenuProperties16:X2}";
            ItemSubsubMenuBox.Text = $"0x{atk.SubsubMenuCategorization:X2}";
            ItemSubMenuBox.Text = $"0x{atk.SubMenuCategorization:X2}";
            ItemCharacterUserBox.Text = $"0x{atk.CharacterUser:X2}";
            ItemTargetsAllowedBox.Text = $"0x{atk.TargetsAllowedApparently:X2}";
            ItemMisc1CBox.Text = $"0x{atk.MiscProperties1C:X2}";
            ItemMisc1DBox.Text = $"0x{atk.MiscProperties1D:X2}";
            ItemMisc1EBox.Text = $"0x{atk.MiscProperties1E:X2}";
            ItemAnimPropsBox.Text = $"0x{atk.AnimationProperties1F:X2}";
            ItemStealGilBox.Text = $"0x{atk.StealGilByte:X2}";
            ItemPartyPreviewBox.Text = $"0x{atk.PartyPreviewByte:X2}";
            ItemStatBuffValueBox.Text = atk.StatBuffValue.ToString();
            ItemOverdriveCategoryBox.Text = $"0x{atk.OverdriveCategorizationByte:X2}";
            ItemSpecialBuffBox.Text = $"0x{atk.SpecialBuffInflict:X4}";

            FillEditableItemFlagsPanel(ItemMisc1CFlagsPanel, "ItemMisc1C", atk.MiscProperties1C, FfxAttackFlags.MiscProperties1C);
            FillEditableItemFlagsPanel(ItemMisc1DFlagsPanel, "ItemMisc1D", atk.MiscProperties1D, FfxAttackFlags.MiscProperties1D);
            FillEditableItemFlagsPanel(ItemMisc1EFlagsPanel, "ItemMisc1E", atk.MiscProperties1E, FfxAttackFlags.MiscProperties1E);
            FillEditableItemFlagsPanel(ItemAnimFlagsPanel, "ItemAnimFlags", atk.AnimationProperties1F, FfxAttackFlags.AnimationProperties1F);
            FillEditableItemFlagsPanel(ItemDamagePropsPanel, "ItemDamageProps", atk.DamageProperties20, FfxAttackFlags.DamageProperties);
            FillEditableItemFlagsPanel(ItemDamageClassPanel, "ItemDamageClass", atk.DamageClass, FfxAttackFlags.DamageClass);
            FillEditableItemFlagsPanel(ItemTargetingPanel, "ItemTargeting", atk.TargetingFlags, FfxAttackFlags.Targeting);
            FillEditableItemFlagsPanel(ItemElementsPanel, "ItemElement", atk.ElementFlags, FfxStatusFlags.Elements);
            FillEditableItemFlagsPanel(ItemExtraStatusPanel, "ItemExtraStatus", atk.ExtraStatusInflict, FfxAttackFlags.ExtraStatus);
            FillEditableItemFlagsPanel(ItemStatBuffPanel, "ItemStatBuff", atk.StatBuffFlags, FfxAttackFlags.StatBuffs);

            BuildItemStatusGrid(atk);
            ApplyItemMechanicsButton.IsEnabled = false;
            CopyItemMechanicsButton.IsEnabled = true;
            PasteItemMechanicsButton.IsEnabled = _copiedItemMechanics != null;
            ApplyItemMechanicsAllLanguagesButton.IsEnabled = true;
            ApplyAllItemMechanicsAllLanguagesButton.IsEnabled = true;
            RevertItemMechanicsButton.IsEnabled = false;
            ItemMechanicsStatusText.Text = "";
        }
        finally
        {
            _suppressItemMechanicEvents = false;
        }
    }

    private string? GetEffectiveItemLanguage()
    {
        if (_workspace == null) return null;

        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage;
        if (lang != null && _workspace.ItemsByLanguage.ContainsKey(lang))
            return lang;

        return _workspace.ItemsByLanguage.Keys.FirstOrDefault();
    }

    private AttackFile? GetItemFileForItem(ItemListItem item, string language)
    {
        if (_workspace == null) return null;
        return _workspace.ItemsByLanguage.TryGetValue(language, out var file)
            ? file
            : item.File;
    }

    private void OnItemText_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppressItemTextEvents) return;
        ApplyItemTextsButton.IsEnabled = true;
        RevertItemTextsButton.IsEnabled = true;
        ItemEditStatusText.Text = "● Modifications non appliquées";
    }

    private void OnRevertItemTexts_Click(object sender, RoutedEventArgs e)
    {
        if (ItemListBox.SelectedItem is ItemListItem item)
            DisplayItem(item);
    }

    private void OnApplyItemTexts_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || ItemListBox.SelectedItem is not ItemListItem item)
            return;

        var lang = GetEffectiveItemLanguage() ?? _currentLanguage ?? _workspace.PreferredDisplayLanguage ?? "jp";
        var charset = _workspace.GetCharsetForLanguage(lang);
        if (charset == null)
        {
            MessageBox.Show(this,
                "La charset de cette langue n'est pas chargée — impossible de réencoder les textes.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var file = GetItemFileForItem(item, lang);
        if (file == null || item.RelativeIndex < 0 || item.RelativeIndex >= file.Count)
        {
            MessageBox.Show(this,
                $"Aucun fichier item.bin ne couvre cet objet dans la langue {lang}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryResolveUnsupportedChars(charset, lang,
                ItemNameBox, ItemSimpleNameBox, ItemDescBox, ItemSimpleDescBox))
            return;

        var newTexts = new AttackTexts
        {
            Name = ItemNameBox.Text,
            SimplifiedName = ItemSimpleNameBox.Text,
            Description = ItemDescBox.Text,
            SimplifiedDescription = ItemSimpleDescBox.Text,
        };

        if (!file.SetTexts(item.RelativeIndex, newTexts, charset))
        {
            MessageBox.Show(this, "Échec de l'écriture en mémoire (index invalide).",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        item.File = file;
        item.Attack = file.Attacks[item.RelativeIndex];
        item.Name = string.IsNullOrWhiteSpace(newTexts.Name) ? "(sans nom)" : newTexts.Name;
        ItemHeaderText.Text = item.DisplayName;

        var selectedIndex = item.RelativeIndex;
        ApplyItemFilter();
        foreach (var candidate in _itemListItems)
        {
            if (candidate.RelativeIndex == selectedIndex)
            {
                ItemListBox.SelectedItem = candidate;
                break;
            }
        }

        ApplyItemTextsButton.IsEnabled = false;
        RevertItemTextsButton.IsEnabled = false;
        ItemEditStatusText.Text = "✓ Modifications appliquées (sauvegarde avec Ctrl+S)";
        UpdateMonsterLootResolvedSummary();
        UpdateSaveStatusUI();
    }

    private void OnItemMechanic_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressItemMechanicEvents) return;
        ApplyItemMechanicsButton.IsEnabled = true;
        RevertItemMechanicsButton.IsEnabled = true;
        ItemMechanicsStatusText.Text = "● Mécaniques non appliquées";
    }

    private void OnRevertItemMechanics_Click(object sender, RoutedEventArgs e)
    {
        if (ItemListBox.SelectedItem is ItemListItem item)
            DisplayItem(item);
    }

    private void OnApplyItemMechanics_Click(object sender, RoutedEventArgs e)
    {
        _lastItemMechanicsApplySucceeded = false;
        if (_workspace == null || ItemListBox.SelectedItem is not ItemListItem item)
            return;

        var lang = GetEffectiveItemLanguage() ?? _currentLanguage ?? _workspace.PreferredDisplayLanguage ?? "jp";
        var file = GetItemFileForItem(item, lang);
        if (file == null || item.RelativeIndex < 0 || item.RelativeIndex >= file.Count)
        {
            MessageBox.Show(this,
                $"Aucun fichier item.bin ne couvre cet objet dans la langue {lang}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryReadIntBox(ItemPowerBox, "Puissance", 0, 255, out var power)) return;
        if (!TryReadIntBox(ItemAccuracyBox, "Précision", 0, 255, out var accuracy)) return;
        if (!TryReadIntBox(ItemHitCountBox, "Nombre de cibles touchées", 0, 255, out var hitCount)) return;
        if (!TryReadIntBox(ItemFormulaBox, "Formule", 0, 255, out var formula)) return;
        if (!TryReadIntBox(ItemCostMpBox, "Coût MP", 0, 255, out var costMp)) return;
        if (!TryReadIntBox(ItemCostOdBox, "Coût OD", 0, 255, out var costOd)) return;
        if (!TryReadIntBox(ItemCritBox, "Bonus critique", 0, 255, out var crit)) return;
        if (!TryReadIntBox(ItemShatterBox, "Chance pétrification massive", 0, 255, out var shatter)) return;
        if (!TryReadIntBox(ItemMoveRankBox, "Move rank", 0, 255, out var moveRank)) return;

        if (!TryReadIntBox(ItemAnim1Box, "Animation 1", 0, 0xFFFF, out var anim1)) return;
        if (!TryReadIntBox(ItemAnim2Box, "Animation 2", 0, 0xFFFF, out var anim2)) return;
        if (!TryReadIntBox(ItemIconBox, "Icône", 0, 255, out var icon)) return;
        if (!TryReadIntBox(ItemCasterAnimationBox, "Animation caster", 0, 255, out var casterAnim)) return;
        if (!TryReadIntBox(ItemMenuPropsBox, "Menu 0x16", 0, 255, out var menuProps)) return;
        if (!TryReadIntBox(ItemSubsubMenuBox, "Sous-menu A", 0, 255, out var subsubMenu)) return;
        if (!TryReadIntBox(ItemSubMenuBox, "Sous-menu B", 0, 255, out var subMenu)) return;
        if (!TryReadIntBox(ItemCharacterUserBox, "User char", 0, 255, out var characterUser)) return;
        if (!TryReadIntBox(ItemTargetsAllowedBox, "Cibles permises", 0, 255, out var targetsAllowed)) return;
        if (!TryReadIntBox(ItemMisc1CBox, "Misc 0x1C", 0, 255, out var misc1C)) return;
        if (!TryReadIntBox(ItemMisc1DBox, "Misc 0x1D", 0, 255, out var misc1D)) return;
        if (!TryReadIntBox(ItemMisc1EBox, "Misc 0x1E", 0, 255, out var misc1E)) return;
        if (!TryReadIntBox(ItemAnimPropsBox, "Animation props", 0, 255, out var animProps)) return;
        if (!TryReadIntBox(ItemStealGilBox, "Steal/Gil", 0, 255, out var stealGil)) return;
        if (!TryReadIntBox(ItemPartyPreviewBox, "Preview", 0, 255, out var partyPreview)) return;
        if (!TryReadIntBox(ItemStatBuffValueBox, "Valeur buff", 0, 255, out var statBuffValue)) return;
        if (!TryReadIntBox(ItemOverdriveCategoryBox, "OD catégorie", 0, 255, out var odCategory)) return;
        if (!TryReadIntBox(ItemSpecialBuffBox, "Buff spécial", 0, 0xFFFF, out var specialBuff)) return;

        if (!TryReadItemStatusChance("Death", "Mort", out var stDeath)) return;
        if (!TryReadItemStatusChance("Zombie", "Zombie", out var stZombie)) return;
        if (!TryReadItemStatusChance("Petrify", "Pétrification", out var stPetrify)) return;
        if (!TryReadItemStatusChance("Poison", "Poison", out var stPoison)) return;
        if (!TryReadItemStatusChance("PowerBreak", "Power Break", out var stPowerBreak)) return;
        if (!TryReadItemStatusChance("MagicBreak", "Magic Break", out var stMagicBreak)) return;
        if (!TryReadItemStatusChance("ArmorBreak", "Armor Break", out var stArmorBreak)) return;
        if (!TryReadItemStatusChance("MentalBreak", "Mental Break", out var stMentalBreak)) return;
        if (!TryReadItemStatusChance("Confuse", "Confusion", out var stConfuse)) return;
        if (!TryReadItemStatusChance("Berserk", "Berserk", out var stBerserk)) return;
        if (!TryReadItemStatusChance("Provoke", "Provoque", out var stProvoke)) return;
        if (!TryReadItemStatusChance("Threaten", "Menace", out var stThreaten)) return;
        if (!TryReadItemStatusChance("Sleep", "Sommeil", out var stSleep)) return;
        if (!TryReadItemStatusChance("Silence", "Silence", out var stSilence)) return;
        if (!TryReadItemStatusChance("Darkness", "Obscurité", out var stDarkness)) return;
        if (!TryReadItemStatusChance("Shell", "Carapace", out var stShell)) return;
        if (!TryReadItemStatusChance("Protect", "Bouclier", out var stProtect)) return;
        if (!TryReadItemStatusChance("Reflect", "Reflet", out var stReflect)) return;
        if (!TryReadItemStatusChance("NTide", "NulMaree", out var stNTide)) return;
        if (!TryReadItemStatusChance("NBlaze", "NulFlamme", out var stNBlaze)) return;
        if (!TryReadItemStatusChance("NShock", "NulChoc", out var stNShock)) return;
        if (!TryReadItemStatusChance("NFrost", "NulFrimas", out var stNFrost)) return;
        if (!TryReadItemStatusChance("Regen", "Régen", out var stRegen)) return;
        if (!TryReadItemStatusChance("Haste", "Hâte", out var stHaste)) return;
        if (!TryReadItemStatusChance("Slow", "Lenteur", out var stSlow)) return;

        if (!TryReadItemStatusDuration("Sleep", "Sommeil", out var durSleep)) return;
        if (!TryReadItemStatusDuration("Silence", "Silence", out var durSilence)) return;
        if (!TryReadItemStatusDuration("Darkness", "Obscurité", out var durDarkness)) return;
        if (!TryReadItemStatusDuration("Shell", "Carapace", out var durShell)) return;
        if (!TryReadItemStatusDuration("Protect", "Bouclier", out var durProtect)) return;
        if (!TryReadItemStatusDuration("Reflect", "Reflet", out var durReflect)) return;
        if (!TryReadItemStatusDuration("NTide", "NulMaree", out var durNTide)) return;
        if (!TryReadItemStatusDuration("NBlaze", "NulFlamme", out var durNBlaze)) return;
        if (!TryReadItemStatusDuration("NShock", "NulChoc", out var durNShock)) return;
        if (!TryReadItemStatusDuration("NFrost", "NulFrimas", out var durNFrost)) return;
        if (!TryReadItemStatusDuration("Regen", "Régen", out var durRegen)) return;
        if (!TryReadItemStatusDuration("Haste", "Hâte", out var durHaste)) return;
        if (!TryReadItemStatusDuration("Slow", "Lenteur", out var durSlow)) return;

        var atk = file.Attacks[item.RelativeIndex];
        atk.AttackPower = power;
        atk.AttackAccuracy = accuracy;
        atk.HitCount = hitCount;
        atk.DamageFormula = formula;
        atk.CostMP = costMp;
        atk.CostOD = costOd;
        atk.AttackCritBonus = crit;
        atk.ShatterChance = shatter;
        atk.MoveRank = moveRank;

        atk.Anim1 = anim1;
        atk.Anim2 = anim2;
        atk.Icon = icon;
        atk.CasterAnimation = casterAnim;
        atk.MenuProperties16 = menuProps;
        atk.SubsubMenuCategorization = subsubMenu;
        atk.SubMenuCategorization = subMenu;
        atk.CharacterUser = characterUser;
        atk.TargetsAllowedApparently = targetsAllowed;
        atk.MiscProperties1C = MergeKnownBitfield(misc1C,
            ReadBitfieldFromChecks(ItemMisc1CFlagsPanel, "ItemMisc1C"), FfxAttackFlags.MiscProperties1C);
        atk.MiscProperties1D = MergeKnownBitfield(misc1D,
            ReadBitfieldFromChecks(ItemMisc1DFlagsPanel, "ItemMisc1D"), FfxAttackFlags.MiscProperties1D);
        atk.MiscProperties1E = MergeKnownBitfield(misc1E,
            ReadBitfieldFromChecks(ItemMisc1EFlagsPanel, "ItemMisc1E"), FfxAttackFlags.MiscProperties1E);
        atk.AnimationProperties1F = MergeKnownBitfield(animProps,
            ReadBitfieldFromChecks(ItemAnimFlagsPanel, "ItemAnimFlags"), FfxAttackFlags.AnimationProperties1F);
        atk.StealGilByte = stealGil;
        atk.PartyPreviewByte = partyPreview;

        atk.DamageProperties20 = MergeKnownBitfield(atk.DamageProperties20,
            ReadBitfieldFromChecks(ItemDamagePropsPanel, "ItemDamageProps"), FfxAttackFlags.DamageProperties);
        atk.DamageClass = MergeKnownBitfield(atk.DamageClass,
            ReadBitfieldFromChecks(ItemDamageClassPanel, "ItemDamageClass"), FfxAttackFlags.DamageClass);
        atk.TargetingFlags = MergeKnownBitfield(atk.TargetingFlags,
            ReadBitfieldFromChecks(ItemTargetingPanel, "ItemTargeting"), FfxAttackFlags.Targeting);
        atk.ElementFlags = MergeKnownBitfield(atk.ElementFlags,
            ReadBitfieldFromChecks(ItemElementsPanel, "ItemElement"), FfxStatusFlags.Elements);
        atk.ExtraStatusInflict = MergeKnownBitfield(atk.ExtraStatusInflict,
            ReadBitfieldFromChecks(ItemExtraStatusPanel, "ItemExtraStatus"), FfxAttackFlags.ExtraStatus);
        atk.StatBuffFlags = MergeKnownBitfield(atk.StatBuffFlags,
            ReadBitfieldFromChecks(ItemStatBuffPanel, "ItemStatBuff"), FfxAttackFlags.StatBuffs);
        atk.StatBuffValue = statBuffValue;
        atk.OverdriveCategorizationByte = odCategory;
        atk.SpecialBuffInflict = specialBuff;

        atk.StatusChanceDeath = stDeath;
        atk.StatusChanceZombie = stZombie;
        atk.StatusChancePetrify = stPetrify;
        atk.StatusChancePoison = stPoison;
        atk.StatusChancePowerBreak = stPowerBreak;
        atk.StatusChanceMagicBreak = stMagicBreak;
        atk.StatusChanceArmorBreak = stArmorBreak;
        atk.StatusChanceMentalBreak = stMentalBreak;
        atk.StatusChanceConfuse = stConfuse;
        atk.StatusChanceBerserk = stBerserk;
        atk.StatusChanceProvoke = stProvoke;
        atk.StatusChanceThreaten = stThreaten;
        atk.StatusChanceSleep = stSleep;
        atk.StatusChanceSilence = stSilence;
        atk.StatusChanceDarkness = stDarkness;
        atk.StatusChanceShell = stShell;
        atk.StatusChanceProtect = stProtect;
        atk.StatusChanceReflect = stReflect;
        atk.StatusChanceNTide = stNTide;
        atk.StatusChanceNBlaze = stNBlaze;
        atk.StatusChanceNShock = stNShock;
        atk.StatusChanceNFrost = stNFrost;
        atk.StatusChanceRegen = stRegen;
        atk.StatusChanceHaste = stHaste;
        atk.StatusChanceSlow = stSlow;

        atk.StatusDurationSleep = durSleep;
        atk.StatusDurationSilence = durSilence;
        atk.StatusDurationDarkness = durDarkness;
        atk.StatusDurationShell = durShell;
        atk.StatusDurationProtect = durProtect;
        atk.StatusDurationReflect = durReflect;
        atk.StatusDurationNTide = durNTide;
        atk.StatusDurationNBlaze = durNBlaze;
        atk.StatusDurationNShock = durNShock;
        atk.StatusDurationNFrost = durNFrost;
        atk.StatusDurationRegen = durRegen;
        atk.StatusDurationHaste = durHaste;
        atk.StatusDurationSlow = durSlow;

        file.MarkDirty();
        item.File = file;
        item.Attack = atk;

        ApplyItemMechanicsButton.IsEnabled = false;
        RevertItemMechanicsButton.IsEnabled = false;
        ItemMechanicsStatusText.Text = "✓ Mécaniques appliquées (sauvegarde avec Ctrl+S)";
        UpdateSaveStatusUI();
        _lastItemMechanicsApplySucceeded = true;
    }

    private void OnApplyItemMechanicsAllLanguages_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || ItemListBox.SelectedItem is not ItemListItem item)
            return;

        OnApplyItemMechanics_Click(sender, e);
        if (!_lastItemMechanicsApplySucceeded) return;

        var source = item.Attack;
        var touched = 0;
        foreach (var file in _workspace.ItemsByLanguage.Values)
        {
            if (item.RelativeIndex < 0 || item.RelativeIndex >= file.Count) continue;

            CopyAttackMechanics(source, file.Attacks[item.RelativeIndex]);
            file.MarkDirty();
            touched++;
        }

        ItemMechanicsStatusText.Text =
            $"✓ Mécaniques appliquées à {touched} langue(s) (sauvegarde avec Ctrl+S)";
        UpdateMonsterLootResolvedSummary();
        UpdateSaveStatusUI();
    }

    private void OnCopyItemMechanics_Click(object sender, RoutedEventArgs e)
    {
        if (ItemListBox.SelectedItem is not ItemListItem item)
            return;

        if (ApplyItemMechanicsButton.IsEnabled)
        {
            OnApplyItemMechanics_Click(sender, e);
            if (!_lastItemMechanicsApplySucceeded) return;
        }

        _copiedItemMechanics = CloneAttackData(item.Attack);
        PasteItemMechanicsButton.IsEnabled = true;
        ItemMechanicsStatusText.Text = "✓ Mécaniques copiées";
    }

    private void OnPasteItemMechanics_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || _copiedItemMechanics == null
            || ItemListBox.SelectedItem is not ItemListItem item)
            return;

        var lang = GetEffectiveItemLanguage() ?? _currentLanguage ?? _workspace.PreferredDisplayLanguage ?? "jp";
        var file = GetItemFileForItem(item, lang);
        if (file == null || item.RelativeIndex < 0 || item.RelativeIndex >= file.Count)
        {
            MessageBox.Show(this,
                $"Aucun fichier item.bin ne couvre cet objet dans la langue {lang}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CopyAttackMechanics(_copiedItemMechanics, file.Attacks[item.RelativeIndex]);
        file.MarkDirty();
        item.File = file;
        item.Attack = file.Attacks[item.RelativeIndex];

        DisplayItem(item);
        PasteItemMechanicsButton.IsEnabled = true;
        ItemMechanicsStatusText.Text = "✓ Mécaniques collées (textes préservés, sauvegarde avec Ctrl+S)";
        UpdateMonsterLootResolvedSummary();
        UpdateSaveStatusUI();
    }

    private void OnApplyAllItemMechanicsAllLanguages_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null)
            return;

        var sourceLang = GetEffectiveItemLanguage() ?? _currentLanguage ?? _workspace.PreferredDisplayLanguage;
        if (sourceLang == null || !_workspace.ItemsByLanguage.TryGetValue(sourceLang, out var sourceFile))
        {
            MessageBox.Show(this,
                "Aucun fichier item.bin source n'est chargé.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (ItemListBox.SelectedItem is ItemListItem && ApplyItemMechanicsButton.IsEnabled)
        {
            OnApplyItemMechanics_Click(sender, e);
            if (!_lastItemMechanicsApplySucceeded) return;
            sourceFile = _workspace.ItemsByLanguage[sourceLang];
        }

        var targetLanguageCount = _workspace.ItemsByLanguage.Keys
            .Count(lang => !string.Equals(lang, sourceLang, StringComparison.OrdinalIgnoreCase));
        if (!ConfirmBulkMechanicsCopy("objets", sourceLang, targetLanguageCount))
            return;

        var touchedFiles = 0;
        var copiedEntries = 0;
        foreach (var (lang, file) in _workspace.ItemsByLanguage)
        {
            if (string.Equals(lang, sourceLang, StringComparison.OrdinalIgnoreCase))
                continue;

            copiedEntries += CopyAllAttackMechanics(sourceFile, file, ref touchedFiles);
        }

        ItemMechanicsStatusText.Text =
            $"✓ {copiedEntries} bloc(s) mécaniques copiés vers {touchedFiles} fichier(s) localisé(s) (sauvegarde avec Ctrl+S)";
        UpdateMonsterLootResolvedSummary();
        UpdateSaveStatusUI();
    }

    private bool TryReadItemStatusChance(string key, string label, out int value)
    {
        var box = FindTaggedTextBox(ItemStatusGrid, $"ItemStatusChance:{key}");
        if (box == null)
        {
            value = 0;
            MessageBox.Show(this, $"Champ de chance introuvable : {label}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        return TryReadIntBox(box, $"{label} chance", 0, 255, out value);
    }

    private bool TryReadItemStatusDuration(string key, string label, out int value)
    {
        var box = FindTaggedTextBox(ItemStatusGrid, $"ItemStatusDuration:{key}");
        if (box == null)
        {
            value = 0;
            MessageBox.Show(this, $"Champ de durée introuvable : {label}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        return TryReadIntBox(box, $"{label} durée", 0, 255, out value);
    }

    private void BuildItemStatusGrid(AttackData atk)
    {
        ItemStatusGrid.Children.Clear();
        ItemStatusGrid.RowDefinitions.Clear();
        ItemStatusGrid.ColumnDefinitions.Clear();

        ItemStatusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        ItemStatusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        ItemStatusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        var rows = GetAttackStatusRows(atk);

        ItemStatusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddCell(ItemStatusGrid, 0, 0, "Statut", FontWeights.Bold);
        AddCell(ItemStatusGrid, 0, 1, "Chance %", FontWeights.Bold);
        AddCell(ItemStatusGrid, 0, 2, "Durée (tours)", FontWeights.Bold);

        for (int i = 0; i < rows.Length; i++)
        {
            var row = i + 1;
            ItemStatusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddCell(ItemStatusGrid, row, 0, rows[i].Label);
            AddItemStatusBox(ItemStatusGrid, row, 1, $"ItemStatusChance:{rows[i].Key}", rows[i].Chance);
            if (rows[i].Duration is int duration)
                AddItemStatusBox(ItemStatusGrid, row, 2, $"ItemStatusDuration:{rows[i].Key}", duration);
            else
                AddCell(ItemStatusGrid, row, 2, "—");
        }
    }

    // =========================================================================
    // ONGLET DÉPART JOUEURS
    // =========================================================================

    private readonly ObservableCollection<PlayerStartListItem> _playerStartListItems = new();

    private void PopulatePlayerStartTab(SpiraWorkspace workspace)
    {
        PlayerStartListBox.ItemsSource = _playerStartListItems;
        PlayerStartListBox.DisplayMemberPath = nameof(PlayerStartListItem.DisplayName);
        RebuildPlayerStartList();
    }

    private void RebuildPlayerStartList()
    {
        _playerStartListItems.Clear();

        if (_workspace == null)
        {
            PlayerStartCountText.Text = "ply_save.bin non chargé";
            NoPlayerStartSelectedMessage.Visibility = Visibility.Visible;
            PlayerStartDetailsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var requestedLang = _currentLanguage ?? _workspace.PreferredDisplayLanguage ?? "jp";
        var file = _workspace.GetPlayerSaveFile(requestedLang);
        if (file == null)
        {
            PlayerStartCountText.Text = "ply_save.bin non chargé";
            NoPlayerStartSelectedMessage.Visibility = Visibility.Visible;
            PlayerStartDetailsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var fileLang = _workspace.GetLanguageForPlayerSaveFile(file) ?? requestedLang;
        var charset = _workspace.GetCharsetForLanguage(fileLang);

        for (var i = 0; i < file.Count; i++)
        {
            var globalId = file.MinIndex + i;
            var decoded = file.GetName(i, charset);
            var fallback = PlayerCharacters.GetName(globalId) ?? $"Entrée {globalId}";
            _playerStartListItems.Add(new PlayerStartListItem
            {
                File = file,
                Data = file.Entries[i],
                RelativeIndex = i,
                GlobalId = globalId,
                Language = fileLang,
                Name = string.IsNullOrWhiteSpace(decoded) ? fallback : decoded!,
            });
        }

        var langLabel = LanguageDisplayName(fileLang);
        var totalLangs = _workspace.PlayerSaveFilesByLanguage.Count;
        PlayerStartCountText.Text = totalLangs > 1
            ? $"{_playerStartListItems.Count} entrées ply_save.bin ({langLabel}, {totalLangs} langues)"
            : $"{_playerStartListItems.Count} entrées ply_save.bin ({langLabel})";
    }

    private void OnPlayerStartSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PlayerStartListBox.SelectedItem is not PlayerStartListItem item)
        {
            NoPlayerStartSelectedMessage.Visibility = Visibility.Visible;
            PlayerStartDetailsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        DisplayPlayerStart(item);
    }

    private void DisplayPlayerStart(PlayerStartListItem item)
    {
        NoPlayerStartSelectedMessage.Visibility = Visibility.Collapsed;
        PlayerStartDetailsPanel.Visibility = Visibility.Visible;

        _suppressPlayerStartEvents = true;
        try
        {
            var p = item.Data;
            PlayerStartHeaderText.Text = item.Name;
            PlayerStartInfoText.Text =
                $"Source : ply_save.bin [{LanguageDisplayName(item.Language)}]  •  Index : {item.RelativeIndex}  •  ID : 0x{item.GlobalId:X2}  •  " +
                $"Nom @0x{p.NameOffset:X4} / clé {p.NameKey}";

            PlayerBaseHpBox.Text = p.BaseHp.ToString();
            PlayerBaseMpBox.Text = p.BaseMp.ToString();
            PlayerBaseStrBox.Text = p.BaseStr.ToString();
            PlayerBaseDefBox.Text = p.BaseDef.ToString();
            PlayerBaseMagBox.Text = p.BaseMag.ToString();
            PlayerBaseMdfBox.Text = p.BaseMdf.ToString();
            PlayerBaseAgiBox.Text = p.BaseAgi.ToString();
            PlayerBaseLckBox.Text = p.BaseLck.ToString();
            PlayerBaseEvaBox.Text = p.BaseEva.ToString();
            PlayerBaseAccBox.Text = p.BaseAcc.ToString();

            PlayerCurrentApBox.Text = p.CurrentAp.ToString();
            PlayerCurrentHpBox.Text = p.CurrentHp.ToString();
            PlayerCurrentMpBox.Text = p.CurrentMp.ToString();
            PlayerMaxHpBox.Text = p.MaxHp.ToString();
            PlayerMaxMpBox.Text = p.MaxMp.ToString();

            PlayerCurrentStrBox.Text = p.Str.ToString();
            PlayerCurrentDefBox.Text = p.Def.ToString();
            PlayerCurrentMagBox.Text = p.Mag.ToString();
            PlayerCurrentMdfBox.Text = p.Mdf.ToString();
            PlayerCurrentAgiBox.Text = p.Agi.ToString();
            PlayerCurrentLckBox.Text = p.Lck.ToString();
            PlayerCurrentEvaBox.Text = p.Eva.ToString();
            PlayerCurrentAccBox.Text = p.Acc.ToString();

            PlayerPoisonBox.Text = p.PoisonDamage.ToString();
            PlayerOverdriveModeBox.Text = p.OverdriveMode.ToString();
            PlayerOverdriveCurrentBox.Text = p.OverdriveCurrent.ToString();
            PlayerOverdriveMaxBox.Text = p.OverdriveMax.ToString();

            PlayerWeaponCombo.ItemsSource = BuildPlayerStartGearOptions(item.GlobalId, isArmor: false, p.EquippedWeaponIndex);
            PlayerWeaponCombo.SelectedValue = p.EquippedWeaponIndex;
            PlayerArmorCombo.ItemsSource = BuildPlayerStartGearOptions(item.GlobalId, isArmor: true, p.EquippedArmorIndex);
            PlayerArmorCombo.SelectedValue = p.EquippedArmorIndex;
            RefreshPlayerStartGearIndexText();
            PlayerSphereLevelAvailableBox.Text = p.SphereLevelsAvailable.ToString();
            PlayerSphereLevelUsedBox.Text = p.SphereLevelsUsed.ToString();
            PlayerMiscFlagsBox.Text = $"0x{p.MiscFlags:X2}";

            BuildPlayerStartSkillsPanel(p);

            ApplyPlayerStartButton.IsEnabled = false;
            ApplyPlayerStartAllLanguagesButton.IsEnabled = false;
            RevertPlayerStartButton.IsEnabled = false;
            PlayerStartEditStatusText.Text = "Prêt";
        }
        finally
        {
            _suppressPlayerStartEvents = false;
        }
    }

    private List<PlayerStartGearOption> BuildPlayerStartGearOptions(int characterId, bool isArmor, int currentRawIndex)
    {
        var options = new List<PlayerStartGearOption>();

        if (currentRawIndex == 0xFF)
            options.Add(new PlayerStartGearOption(0xFF, "[0xFF] Aucun / non équipé"));

        if (_workspace?.InitialEquipmentFile != null)
        {
            var file = _workspace.InitialEquipmentFile;
            for (var i = 0; i < file.Count; i++)
            {
                var gear = file.Entries[i];
                if (gear.Character != characterId || gear.IsArmor != isArmor) continue;

                var id = file.MinIndex + i;
                options.Add(new PlayerStartGearOption(id, FormatPlayerStartGearOption("weapon.bin", id, gear)));
            }

            if (currentRawIndex != 0xFF && options.All(o => o.Id != currentRawIndex))
            {
                var rel = currentRawIndex - file.MinIndex;
                if (rel >= 0 && rel < file.Count)
                {
                    var gear = file.Entries[rel];
                    var owner = PlayerCharacters.GetName(gear.Character) ?? $"#{gear.Character:X2}";
                    options.Insert(0, new PlayerStartGearOption(currentRawIndex,
                        $"{FormatPlayerStartGearOption("weapon.bin", currentRawIndex, gear)}  — hors série {owner}"));
                }
            }
        }

        if (options.All(o => o.Id != currentRawIndex))
            options.Insert(0, new PlayerStartGearOption(currentRawIndex, $"[weapon 0x{currentRawIndex:X4}] Valeur brute non résolue"));

        return options;
    }

    private string FormatPlayerStartGearOption(string source, int id, GearData gear)
    {
        var owner = PlayerCharacters.GetName(gear.Character) ?? $"#{gear.Character:X2}";
        var type = gear.IsArmor ? "Protection" : "Arme";
        var abilityText = FormatGearAbilitySummary(gear);
        var nameText = source == "weapon.bin" ? "" : ResolvePlayerStartGearName(gear, id);
        if (!string.IsNullOrWhiteSpace(nameText))
            nameText = $" · {nameText}";

        var prefix = source == "weapon.bin"
            ? $"[weapon 0x{id:X4}]"
            : $"[0x{id:X4}]";
        return $"{prefix} {owner} · {type} · P{gear.Power} S{gear.Slots}{nameText}{abilityText}";
    }

    private string ResolvePlayerStartGearName(GearData gear, int globalId)
    {
        if (_workspace == null || gear.IsArmor || gear.Character < 0 || gear.Character >= WeaponNameEntry.CHARACTER_COUNT)
            return "";

        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage;
        if (lang == null) return "";

        var nameIndex = gear.IsBukiGet ? globalId : gear.NameIdMaybe1;
        return _workspace.LookupWeaponName(nameIndex, gear.Character, lang) ?? "";
    }

    private string FormatGearAbilitySummary(GearData gear)
    {
        var abilities = gear.Abilities
            .Take(Math.Clamp(gear.Slots, 0, gear.Abilities.Length))
            .Where(id => id != 0 && id != 0x00FF && id != 0xFFFF)
            .Select(ResolveGearAbilityName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(3)
            .ToList();

        return abilities.Count == 0 ? "" : $" · {string.Join(", ", abilities)}";
    }

    private void RefreshPlayerStartGearIndexText()
    {
        PlayerWeaponIndexText.Text = PlayerWeaponCombo.SelectedValue is int weapon
            ? $"weapon 0x{weapon:X4}"
            : "";
        PlayerArmorIndexText.Text = PlayerArmorCombo.SelectedValue is int armor
            ? $"weapon 0x{armor:X4}"
            : "";
    }

    private void BuildPlayerStartSkillsPanel(PlayerSaveData data)
    {
        PlayerStartSkillsPanel.Children.Clear();
        var learned = data.GetLearnedCommandIds().ToHashSet();

        for (var id = 0x3000; id <= 0x305F; id++)
        {
            var name = ResolvePlayerCommandName(id);
            var cb = new CheckBox
            {
                Content = $"[0x{id:X4}] {name}",
                Tag = id,
                IsChecked = learned.Contains(id),
                Width = 260,
                Margin = new Thickness(2, 1, 8, 1),
            };
            cb.Checked += OnPlayerStartSkill_Changed;
            cb.Unchecked += OnPlayerStartSkill_Changed;
            PlayerStartSkillsPanel.Children.Add(cb);
        }
    }

    private string ResolvePlayerCommandName(int commandId)
    {
        if (_workspace == null) return $"Commande 0x{commandId:X4}";

        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage;
        if (lang == null || !_workspace.PlayerCommandsByLanguage.ContainsKey(lang))
            lang = _workspace.PlayerCommandsByLanguage.Keys.FirstOrDefault();

        if (lang != null)
        {
            var name = _workspace.LookupCommandName(commandId, lang);
            if (!string.IsNullOrWhiteSpace(name))
                return name!;
        }

        return $"Commande 0x{commandId:X4}";
    }

    private void OnPlayerStart_Changed(object sender, TextChangedEventArgs e)
    {
        MarkPlayerStartDirtyUi();
    }

    private void OnPlayerStartSkill_Changed(object sender, RoutedEventArgs e)
    {
        MarkPlayerStartDirtyUi();
    }

    private void OnPlayerStartGear_Changed(object sender, SelectionChangedEventArgs e)
    {
        RefreshPlayerStartGearIndexText();
        MarkPlayerStartDirtyUi();
    }

    private void MarkPlayerStartDirtyUi()
    {
        if (_suppressPlayerStartEvents) return;
        ApplyPlayerStartButton.IsEnabled = true;
        ApplyPlayerStartAllLanguagesButton.IsEnabled = _workspace?.PlayerSaveFilesByLanguage.Count > 1;
        RevertPlayerStartButton.IsEnabled = true;
        PlayerStartEditStatusText.Text = "● Modifications non appliquées";
    }

    private void OnRevertPlayerStart_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerStartListBox.SelectedItem is PlayerStartListItem item)
            DisplayPlayerStart(item);
    }

    private void OnApplyPlayerStart_Click(object sender, RoutedEventArgs e)
    {
        if (PlayerStartListBox.SelectedItem is not PlayerStartListItem item)
            return;

        if (!TryApplyPlayerStartFormTo(item))
            return;

        ApplyPlayerStartButton.IsEnabled = false;
        ApplyPlayerStartAllLanguagesButton.IsEnabled = false;
        RevertPlayerStartButton.IsEnabled = false;
        PlayerStartEditStatusText.Text = "✓ Personnage appliqué (sauvegarde avec Ctrl+S)";
        UpdateSaveStatusUI();
    }

    private void OnApplyPlayerStartAllLanguages_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || PlayerStartListBox.SelectedItem is not PlayerStartListItem item)
            return;

        if (!TryApplyPlayerStartFormTo(item))
            return;

        var source = item.Data;
        var touched = 0;
        var skipped = 0;
        foreach (var (_, file) in _workspace.PlayerSaveFilesByLanguage)
        {
            if (item.RelativeIndex < 0 || item.RelativeIndex >= file.Count)
            {
                skipped++;
                continue;
            }

            file.Entries[item.RelativeIndex].CopyMechanicsFrom(source);
            file.MarkDirty();
            touched++;
        }

        if (touched == 0)
        {
            item.File.MarkDirty();
            touched = 1;
        }

        ApplyPlayerStartButton.IsEnabled = false;
        ApplyPlayerStartAllLanguagesButton.IsEnabled = false;
        RevertPlayerStartButton.IsEnabled = false;
        PlayerStartEditStatusText.Text = skipped == 0
            ? $"✓ Personnage appliqué à {touched} langue(s), noms conservés (sauvegarde avec Ctrl+S)"
            : $"✓ Personnage appliqué à {touched} langue(s), {skipped} ignorée(s), noms conservés";
        UpdateSaveStatusUI();
    }

    private bool TryApplyPlayerStartFormTo(PlayerStartListItem item)
    {
        if (_workspace == null)
            return false;

        var p = item.Data;
        if (!TryReadIntBox(PlayerBaseHpBox, "HP de base", 0, int.MaxValue, out var baseHp)) return false;
        if (!TryReadIntBox(PlayerBaseMpBox, "MP de base", 0, int.MaxValue, out var baseMp)) return false;
        if (!TryReadIntBox(PlayerBaseStrBox, "Force de base", 0, 255, out var baseStr)) return false;
        if (!TryReadIntBox(PlayerBaseDefBox, "Défense de base", 0, 255, out var baseDef)) return false;
        if (!TryReadIntBox(PlayerBaseMagBox, "Magie de base", 0, 255, out var baseMag)) return false;
        if (!TryReadIntBox(PlayerBaseMdfBox, "Esprit de base", 0, 255, out var baseMdf)) return false;
        if (!TryReadIntBox(PlayerBaseAgiBox, "Agilité de base", 0, 255, out var baseAgi)) return false;
        if (!TryReadIntBox(PlayerBaseLckBox, "Chance de base", 0, 255, out var baseLck)) return false;
        if (!TryReadIntBox(PlayerBaseEvaBox, "Esquive de base", 0, 255, out var baseEva)) return false;
        if (!TryReadIntBox(PlayerBaseAccBox, "Précision de base", 0, 255, out var baseAcc)) return false;

        if (!TryReadIntBox(PlayerCurrentApBox, "AP de départ", 0, int.MaxValue, out var currentAp)) return false;
        if (!TryReadIntBox(PlayerCurrentHpBox, "HP actuels", 0, int.MaxValue, out var currentHp)) return false;
        if (!TryReadIntBox(PlayerCurrentMpBox, "MP actuels", 0, int.MaxValue, out var currentMp)) return false;
        if (!TryReadIntBox(PlayerMaxHpBox, "HP max", 0, int.MaxValue, out var maxHp)) return false;
        if (!TryReadIntBox(PlayerMaxMpBox, "MP max", 0, int.MaxValue, out var maxMp)) return false;

        if (!TryReadIntBox(PlayerCurrentStrBox, "Force actuelle", 0, 255, out var str)) return false;
        if (!TryReadIntBox(PlayerCurrentDefBox, "Défense actuelle", 0, 255, out var def)) return false;
        if (!TryReadIntBox(PlayerCurrentMagBox, "Magie actuelle", 0, 255, out var mag)) return false;
        if (!TryReadIntBox(PlayerCurrentMdfBox, "Esprit actuel", 0, 255, out var mdf)) return false;
        if (!TryReadIntBox(PlayerCurrentAgiBox, "Agilité actuelle", 0, 255, out var agi)) return false;
        if (!TryReadIntBox(PlayerCurrentLckBox, "Chance actuelle", 0, 255, out var lck)) return false;
        if (!TryReadIntBox(PlayerCurrentEvaBox, "Esquive actuelle", 0, 255, out var eva)) return false;
        if (!TryReadIntBox(PlayerCurrentAccBox, "Précision actuelle", 0, 255, out var acc)) return false;

        if (!TryReadIntBox(PlayerPoisonBox, "Poison %", 0, 255, out var poison)) return false;
        if (!TryReadIntBox(PlayerOverdriveModeBox, "Mode Overdrive", 0, 255, out var odMode)) return false;
        if (!TryReadIntBox(PlayerOverdriveCurrentBox, "Overdrive actuelle", 0, 255, out var odCurrent)) return false;
        if (!TryReadIntBox(PlayerOverdriveMaxBox, "Overdrive max", 0, 255, out var odMax)) return false;

        if (!TryReadPlayerStartGearSelection(PlayerWeaponCombo, "Arme équipée", out var weaponIndex)) return false;
        if (!TryReadPlayerStartGearSelection(PlayerArmorCombo, "Armure équipée", out var armorIndex)) return false;
        if (!TryReadIntBox(PlayerSphereLevelAvailableBox, "Sphères disponibles", 0, 255, out var slvAvailable)) return false;
        if (!TryReadIntBox(PlayerSphereLevelUsedBox, "Sphères utilisées", 0, 255, out var slvUsed)) return false;
        if (!TryReadIntBox(PlayerMiscFlagsBox, "Flags bruts", 0, 255, out var miscFlags)) return false;

        p.BaseHp = baseHp;
        p.BaseMp = baseMp;
        p.BaseStr = baseStr;
        p.BaseDef = baseDef;
        p.BaseMag = baseMag;
        p.BaseMdf = baseMdf;
        p.BaseAgi = baseAgi;
        p.BaseLck = baseLck;
        p.BaseEva = baseEva;
        p.BaseAcc = baseAcc;

        p.CurrentAp = currentAp;
        p.CurrentHp = currentHp;
        p.CurrentMp = currentMp;
        p.MaxHp = maxHp;
        p.MaxMp = maxMp;

        p.Str = str;
        p.Def = def;
        p.Mag = mag;
        p.Mdf = mdf;
        p.Agi = agi;
        p.Lck = lck;
        p.Eva = eva;
        p.Acc = acc;

        p.PoisonDamage = poison;
        p.OverdriveMode = odMode;
        p.OverdriveCurrent = odCurrent;
        p.OverdriveMax = odMax;

        p.EquippedWeaponIndex = weaponIndex;
        p.EquippedArmorIndex = armorIndex;
        p.SphereLevelsAvailable = slvAvailable;
        p.SphereLevelsUsed = slvUsed;
        p.MiscFlags = miscFlags;
        p.SetLearnedCommandIds(ReadSelectedPlayerStartSkillIds());

        item.File.MarkDirty();

        RefreshPlayerStartGearIndexText();
        return true;
    }

    private bool TryReadPlayerStartGearSelection(ComboBox combo, string label, out int value)
    {
        if (combo.SelectedValue is int selected)
        {
            value = selected;
            return true;
        }

        value = 0;
        MessageBox.Show(this,
            $"{label} doit être choisi dans la liste.",
            "Valeur invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
        combo.Focus();
        return false;
    }

    private IReadOnlyList<int> ReadSelectedPlayerStartSkillIds()
    {
        var ids = new List<int>();
        foreach (var cb in FindCheckBoxes(PlayerStartSkillsPanel))
        {
            if (cb.IsChecked == true && cb.Tag is int id)
                ids.Add(id);
        }
        return ids;
    }

    // =========================================================================
    // ONGLET ÉQUIPEMENTS
    // =========================================================================

    private readonly ObservableCollection<GearListItem> _gearListItems = new();
    private List<GearListItem> _allGearItems = new();
    private GearSource _currentGearSource = GearSource.Both;
    private GearTypeFilter _currentGearType = GearTypeFilter.Both;
    private int? _currentGearCharacterFilter;
    private bool _suppressGearEvents;

    private void PopulateGearTab(SpiraWorkspace workspace)
    {
        GearListBox.ItemsSource = _gearListItems;
        GearListBox.DisplayMemberPath = nameof(GearListItem.DisplayName);
        GearAbilitiesItemsControl.ItemsSource = null;

        _suppressLanguageEvents = true;
        try
        {
            // Source : weapon.bin vs buki_get vs shop_arms
            GearSourceSelector.Items.Clear();
            var hasInitial = workspace.InitialEquipmentFile != null;
            var hasBuki = workspace.BukiGetFile != null;
            var hasShop = workspace.ShopArmsFile != null;

            if ((hasInitial ? 1 : 0) + (hasBuki ? 1 : 0) + (hasShop ? 1 : 0) > 1)
            {
                var allLabel = hasInitial
                    ? "Tous (weapon + buki_get + shop_arms)"
                    : "Tous (buki_get + shop_arms)";
                GearSourceSelector.Items.Add(new GearSourceOption(allLabel, GearSource.Both));
            }
            if (hasInitial)
                GearSourceSelector.Items.Add(new GearSourceOption("weapon.bin (départ joueurs)", GearSource.PlayerStart));
            if (hasBuki)
                GearSourceSelector.Items.Add(new GearSourceOption("buki_get.bin (drops/coffres)", GearSource.BukiGet));
            if (hasShop)
                GearSourceSelector.Items.Add(new GearSourceOption("shop_arms.bin (boutique)", GearSource.ShopArms));

            if (GearSourceSelector.Items.Count > 0)
            {
                GearSourceSelector.SelectedIndex = 0;
                _currentGearSource = ((GearSourceOption)GearSourceSelector.Items[0]).Source;
            }

            // Type : armes / armures / les deux
            GearTypeSelector.Items.Clear();
            GearTypeSelector.Items.Add(new GearTypeOption("Tous", GearTypeFilter.Both));
            GearTypeSelector.Items.Add(new GearTypeOption("Armes uniquement", GearTypeFilter.WeaponsOnly));
            GearTypeSelector.Items.Add(new GearTypeOption("Armures uniquement", GearTypeFilter.ArmorsOnly));
            GearTypeSelector.SelectedIndex = 0;
            _currentGearType = GearTypeFilter.Both;

            // Personnage : tous + chaque persoot/Chimère, avec counts pour chaque
            var dist = workspace.GetGearDistributionByCharacter();
            var initialDist = CountGearByCharacter(workspace.InitialEquipmentFile);
            var totalCount = dist.Values.Sum(v => v.Buki + v.Shop) + initialDist.Values.Sum();

            GearCharacterSelector.Items.Clear();
            GearCharacterSelector.Items.Add(new CharacterOption($"Tous personnages ({totalCount})", null));
            foreach (var id in PlayerCharacters.KnownCharacters)
            {
                var name = PlayerCharacters.GetName(id) ?? $"#{id:X2}";
                var (b, s) = dist.GetValueOrDefault(id);
                var initial = initialDist.GetValueOrDefault(id);
                var label = $"{name} ({initial + b + s})";
                GearCharacterSelector.Items.Add(new CharacterOption(label, id));
            }
            GearCharacterSelector.SelectedIndex = 0;
            _currentGearCharacterFilter = null;
        }
        finally
        {
            _suppressLanguageEvents = false;
        }

        RebuildGearList();
    }

    private void OnGearSource_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageEvents || _workspace == null) return;
        if (GearSourceSelector.SelectedItem is not GearSourceOption opt) return;
        _currentGearSource = opt.Source;
        RebuildGearList();
    }

    private void OnGearType_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageEvents || _workspace == null) return;
        if (GearTypeSelector.SelectedItem is not GearTypeOption opt) return;
        _currentGearType = opt.Type;
        RebuildGearList();
    }

    private void OnGearCharacter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageEvents || _workspace == null) return;
        if (GearCharacterSelector.SelectedItem is not CharacterOption opt) return;
        _currentGearCharacterFilter = opt.CharacterId;
        RebuildGearList();
    }

    private void OnGearFilter_Changed(object sender, TextChangedEventArgs e)
    {
        GearFilterPlaceholder.Visibility = string.IsNullOrEmpty(GearFilterBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        ApplyGearFilter();
    }

    private void RebuildGearList()
    {
        if (_workspace == null) return;
        var items = new List<GearListItem>();

        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage;

        if ((_currentGearSource == GearSource.Both || _currentGearSource == GearSource.PlayerStart)
            && _workspace.InitialEquipmentFile != null)
            BuildItemsFromGearFile(_workspace.InitialEquipmentFile, "weapon", _workspace, lang, items);

        if ((_currentGearSource == GearSource.Both || _currentGearSource == GearSource.BukiGet)
            && _workspace.BukiGetFile != null)
            BuildItemsFromGearFile(_workspace.BukiGetFile, "buki", _workspace, lang, items);

        if ((_currentGearSource == GearSource.Both || _currentGearSource == GearSource.ShopArms)
            && _workspace.ShopArmsFile != null)
            BuildItemsFromGearFile(_workspace.ShopArmsFile, "shop", _workspace, lang, items);

        // Filtres type + personnage
        items = items.Where(i => MatchesGearType(i.Gear, _currentGearType)).ToList();
        if (_currentGearCharacterFilter != null)
            items = items.Where(i => i.Gear.Character == _currentGearCharacterFilter).ToList();

        _allGearItems = items;
        ApplyGearFilter();
    }

    private void BuildItemsFromGearFile(GearFile file, string sourceTag,
        SpiraWorkspace workspace, string? language, List<GearListItem> output)
    {
        var showNames = GearShowNamesCheckbox?.IsChecked ?? true;
        for (int i = 0; i < file.Count; i++)
        {
            var gear = file.Entries[i];
            var globalId = file.MinIndex + i;

            // Résolution du nom d'arme via w_name.bin :
            //   - uniquement pour les armes (pas armures — w_name.bin ne couvre pas)
            //   - uniquement pour les personnages humains 0..6 (Tidus..Rikku)
            //   - les Chimères/Seymour/aucun ID humain valide → null
            string? resolvedName = null;
            if (sourceTag != "weapon"
                && !gear.IsArmor && language != null
                && gear.Character >= 0 && gear.Character < WeaponNameEntry.CHARACTER_COUNT)
            {
                resolvedName = workspace.LookupWeaponName(GetWeaponNameIndex(gear, globalId), gear.Character, language);
            }

            output.Add(new GearListItem
            {
                File = file,
                Gear = gear,
                RelativeIndex = i,
                GlobalId = globalId,
                SourceTag = sourceTag,
                OwnerName = PlayerCharacters.GetName(gear.Character) ?? $"#{gear.Character:X2}",
                ResolvedName = resolvedName,
                ShowName = showNames,
            });
        }
    }

    private static Dictionary<int, int> CountGearByCharacter(GearFile? file)
    {
        var counts = new Dictionary<int, int>();
        if (file == null) return counts;

        foreach (var gear in file.Entries)
        {
            counts.TryGetValue(gear.Character, out var count);
            counts[gear.Character] = count + 1;
        }

        return counts;
    }

    private void OnGearShowNames_Changed(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || _allGearItems.Count == 0) return;
        var show = GearShowNamesCheckbox?.IsChecked ?? true;

        // On met juste à jour le flag dans les items existants — pas besoin de relire les fichiers
        foreach (var item in _allGearItems) item.ShowName = show;

        // Sauvegarde la sélection courante avant rafraîchissement
        var selected = GearListBox.SelectedItem as GearListItem;

        ApplyGearFilter();

        if (selected != null)
        {
            foreach (var item in _gearListItems)
            {
                if (item.GlobalId == selected.GlobalId && item.SourceTag == selected.SourceTag)
                {
                    GearListBox.SelectedItem = item;
                    break;
                }
            }
        }
    }

    private static bool MatchesGearType(GearData gear, GearTypeFilter filter) => filter switch
    {
        GearTypeFilter.Both        => true,
        GearTypeFilter.WeaponsOnly => !gear.IsArmor,
        GearTypeFilter.ArmorsOnly  => gear.IsArmor,
        _ => true,
    };

    private void ApplyGearFilter()
    {
        var filter = GearFilterBox.Text.Trim();
        IEnumerable<GearListItem> filtered = _allGearItems;
        if (!string.IsNullOrEmpty(filter))
        {
            filtered = filtered.Where(item =>
                item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }
        _gearListItems.Clear();
        foreach (var item in filtered) _gearListItems.Add(item);

        // Texte de statut + message contextuel pour les Chimères
        if (_gearListItems.Count == 0
            && _currentGearCharacterFilter != null
            && PlayerCharacters.IsAeon(_currentGearCharacterFilter.Value))
        {
            var charName = PlayerCharacters.GetName(_currentGearCharacterFilter.Value) ?? "?";
            GearCountText.Text = $"Aucun équipement pour {charName} dans buki_get/shop_arms (voir détails)";
            ShowAeonGearExplanation(charName);
        }
        else if (_gearListItems.Count == 0
            && _currentGearCharacterFilter == PlayerCharacters.Seymour)
        {
            GearCountText.Text = "Aucun équipement pour Seymour dans buki_get/shop_arms";
            ShowSeymourGearExplanation();
        }
        else
        {
            GearCountText.Text = _gearListItems.Count == _allGearItems.Count
                ? $"{_allGearItems.Count} équipements"
                : $"{_gearListItems.Count} / {_allGearItems.Count} équipements";

            // Si on avait montré le message d'explication, on le cache (sauf si l'utilisateur a sélectionné un item)
            if (GearListBox.SelectedItem == null)
            {
                NoGearSelectedMessage.Visibility = Visibility.Visible;
                NoGearSelectedMessage.Text = "← Sélectionne un équipement dans la liste pour voir ses propriétés.";
                GearDetailsPanel.Visibility = Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// Affiche dans la zone de détail un message expliquant pourquoi cette
    /// Chimère n'a pas d'équipement éditable dans buki_get/shop_arms.
    /// </summary>
    private void ShowAeonGearExplanation(string aeonName)
    {
        GearDetailsPanel.Visibility = Visibility.Collapsed;
        NoGearSelectedMessage.Visibility = Visibility.Visible;
        NoGearSelectedMessage.Text =
            $"⚠ {aeonName} n'a pas d'entrées dans buki_get.bin / shop_arms.bin.\n\n" +
            "Dans FFX, les Chimères n'ont pas d'équipement éditable au sens classique :\n\n" +
            "  • Leurs stats de base sont dans leur fichier monstre (ex: m###.bin)\n" +
            "  • Leurs aptitudes apprises sont gérées via sum_grow.bin\n" +
            "    (recettes de customisation des Chimères — pas encore supporté ici)\n" +
            "  • Leurs attaques sont dans command.bin\n\n" +
            "Pour modifier les capacités des Chimères, va plutôt dans :\n" +
            "  • Onglet Monstres : pour les stats brutes\n" +
            "  • Onglet Commandes joueurs & Chimères : pour les attaques et overdrives";
    }

    private void ShowSeymourGearExplanation()
    {
        GearDetailsPanel.Visibility = Visibility.Collapsed;
        NoGearSelectedMessage.Visibility = Visibility.Visible;
        NoGearSelectedMessage.Text =
            "⚠ Seymour n'a pas d'entrées dans buki_get.bin / shop_arms.bin.\n\n" +
            "Seymour étant un personnage invité (pas jouable de façon permanente),\n" +
            "son équipement est fixe et baked-in à son fichier de personnage.\n\n" +
            "Tu pourras éventuellement modifier ses stats de base via ply_save.bin\n" +
            "ou ply_rom.bin dans une version future de Spira Modifier.";
    }

    private void OnGearSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (GearListBox.SelectedItem is not GearListItem item)
        {
            NoGearSelectedMessage.Visibility = Visibility.Visible;
            GearDetailsPanel.Visibility = Visibility.Collapsed;
            return;
        }
        DisplayGear(item);
    }

    private void DisplayGear(GearListItem item)
    {
        NoGearSelectedMessage.Visibility = Visibility.Collapsed;
        GearDetailsPanel.Visibility = Visibility.Visible;

        _suppressGearEvents = true;
        try
        {
            var g = item.Gear;
            var lang = _currentLanguage ?? _workspace?.PreferredDisplayLanguage ?? "jp";
            var charset = _workspace?.GetCharsetForLanguage(lang);

            GearHeaderText.Text = !string.IsNullOrWhiteSpace(item.ResolvedName)
                ? item.ResolvedName
                : item.DisplayName;

            GearInfoText.Text =
                $"Source : {GetGearSourceFileName(item.SourceTag)}  •  " +
                $"ID global : 0x{item.GlobalId:X4} ({item.GlobalId})  •  " +
                $"Index : {item.RelativeIndex}";

            var weaponNameIndex = GetWeaponNameIndex(g, item.GlobalId);
            var canResolveName = item.SourceTag != "weapon"
                && !g.IsArmor && _workspace != null
                && g.Character >= 0 && g.Character < WeaponNameEntry.CHARACTER_COUNT;
            var canEditName = false;
            int? weaponNameModel = null;
            GearNameGroup.Visibility = canResolveName ? Visibility.Visible : Visibility.Collapsed;
            GearNameGroup.Header = $"Nom — {LanguageDisplayName(lang)}";

            if (canResolveName && _workspace != null
                && _workspace.WeaponNamesByLanguage.TryGetValue(lang, out var wnFile))
            {
                var texts = wnFile.GetTexts(weaponNameIndex, g.Character, charset);
                if (texts != null)
                {
                    GearNameBox.Text = texts.Name;
                    GearSimpleNameBox.Text = texts.SimplifiedName;
                    var rel = weaponNameIndex - wnFile.MinIndex;
                    GearWeaponNameModelBox.Text = rel >= 0 && rel < wnFile.Count
                        ? $"0x{wnFile.Entries[rel].ModelIds[g.Character]:X4}"
                        : "—";
                    if (rel >= 0 && rel < wnFile.Count)
                        weaponNameModel = wnFile.Entries[rel].ModelIds[g.Character];
                    canEditName = true;
                }
                else
                {
                    GearNameBox.Text = $"(index 0x{weaponNameIndex:X4} hors plage w_name.bin)";
                    GearSimpleNameBox.Text = "—";
                    GearWeaponNameModelBox.Text = "—";
                }
            }
            else if (canResolveName)
            {
                GearNameBox.Text = "(w_name.bin non chargé pour cette langue)";
                GearSimpleNameBox.Text = "—";
                GearWeaponNameModelBox.Text = "—";
            }
            GearNameBox.IsEnabled = canEditName;
            GearSimpleNameBox.IsEnabled = canEditName;
            GearWeaponNameModelBox.IsEnabled = canEditName && !g.IsBukiGet;
            ApplyWeaponNameAllLanguagesButton.IsEnabled = false;

            GearOwnerBox.Text     = item.OwnerName;
            GearTypeBox.Text      = g.IsArmor ? "Protection" : "Arme";
            GearSlotsBox.Text     = g.Slots.ToString();
            GearFormulaBox.Text   = g.Formula.ToString();
            GearPowerBox.Text     = g.Power.ToString();
            GearCritBox.Text      = g.Crit.ToString();
            GearModelBox.Text     = g.IsBukiGet
                ? (weaponNameModel is int model ? $"0x{model:X4}" : "—")
                : $"0x{g.ModelIdx:X4}";
            GearModelBox.IsEnabled = !g.IsBukiGet || weaponNameModel != null;
            GearArmorByteBox.Text = $"0x{g.ArmorByte:X2}";
            GearFlagsBox.Text     = $"0x{g.VariousFlags:X2}";
            GearNameId1Box.Text   = g.IsBukiGet ? $"0x{weaponNameIndex:X2}" : $"0x{g.NameIdMaybe1:X2}";
            GearNameId2Box.Text   = g.IsBukiGet ? "—" : $"0x{g.NameIdMaybe2:X2}";
            GearNameId1Box.IsEnabled = !g.IsBukiGet && item.SourceTag != "weapon";
            GearNameId2Box.IsEnabled = !g.IsBukiGet && item.SourceTag != "weapon";

            g.RefreshFlagBooleans();
            FillEditableGearFlagsPanel(g.VariousFlags);

            GearAbilitiesItemsControl.ItemsSource = BuildGearAbilitySlotItems(g);

            ApplyGearButton.IsEnabled = false;
            RevertGearButton.IsEnabled = false;
            GearEditStatusText.Text = "Prêt";
        }
        finally
        {
            _suppressGearEvents = false;
        }
    }

    private static int GetWeaponNameIndex(GearData gear, int fallbackGlobalId)
        => gear.IsBukiGet ? fallbackGlobalId : gear.NameIdMaybe1;

    private static readonly (int Mask, string Label)[] GearFlagDefs =
    {
        (0x01, "Flag 0x01"),
        (0x02, "Caché menu"),
        (0x04, "Céleste"),
        (0x08, "Fraternité"),
    };

    private static readonly Dictionary<int, string> GearAbilityFallbackNames = new()
    {
        // Entrée interne présente sur certaines armes, mais souvent sans texte chargé
        // dans a_ability.bin. En FR, "No AP" est affiché comme PC0.
        [0x0014] = "PC0",
    };

    private List<GearAbilitySlotItem> BuildGearAbilitySlotItems(GearData gear)
    {
        var rows = new List<GearAbilitySlotItem>();
        var abilities = gear.Abilities;
        for (int i = 0; i < abilities.Length; i++)
        {
            var currentId = abilities[i];
            var data = GetGearAbilityData(currentId, out var category);
            rows.Add(new GearAbilitySlotItem
            {
                Slot = i + 1,
                HexId = $"0x{currentId:X4}",
                DecimalId = currentId,
                SelectedId = currentId,
                Status = DescribeGearAbilityStatus(currentId),
                ResolvedName = ResolveGearAbilityName(currentId),
                CategoryLabel = category,
                Options = BuildEquipmentAbilityOptions(currentId, gear.IsArmor),
            });
        }
        return rows;
    }

    private AutoAbilityData? GetGearAbilityData(int abilityId, out string category)
    {
        category = "";
        if (abilityId == 0 || abilityId == 0x00FF || abilityId == 0xFFFF)
            return null;

        if (_workspace == null) return null;
        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage;
        if (lang == null || !_workspace.AbilitiesByLanguage.TryGetValue(lang, out var file))
        {
            lang = _workspace.AbilitiesByLanguage.Keys.FirstOrDefault();
            if (lang == null || !_workspace.AbilitiesByLanguage.TryGetValue(lang, out file))
                return null;
        }

        var data = file.GetByGlobalId(abilityId);
        if (data != null)
            category = AbilityCategoryLabel(data);
        return data;
    }

    private string ResolveGearAbilityName(int abilityId)
    {
        if (abilityId == 0x00FF) return "(slot vide)";
        if (abilityId == 0x0000) return "(valeur 0)";
        if (abilityId == 0xFFFF) return "(inutilisé)";

        if (_workspace == null) return $"ID 0x{abilityId:X4}";
        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage;
        if (lang == null || !_workspace.AbilitiesByLanguage.TryGetValue(lang, out var file))
        {
            lang = _workspace.AbilitiesByLanguage.Keys.FirstOrDefault();
            if (lang == null || !_workspace.AbilitiesByLanguage.TryGetValue(lang, out file))
                return $"ID 0x{abilityId:X4}";
        }

        var charset = _workspace.GetCharsetForLanguage(lang);
        var name = file.GetNameByGlobalId(abilityId, charset);
        if (!string.IsNullOrWhiteSpace(name)) return name;

        var fallback = GetGearAbilityFallbackName(abilityId);
        return fallback ?? $"ID 0x{abilityId & 0x7FFF:X4}";
    }

    private static string? GetGearAbilityFallbackName(int abilityId)
        => GearAbilityFallbackNames.TryGetValue(abilityId & 0x7FFF, out var name) ? name : null;

    private static string DescribeGearAbilityStatus(int abilityId) => abilityId switch
    {
        0x00FF => "(vide)",
        0x0000 => "(zéro)",
        0xFFFF => "(inutilisé)",
        _ => "actif",
    };

    private List<GearAbilityOption> BuildEquipmentAbilityOptions(int currentId, bool isArmor)
    {
        var options = new List<GearAbilityOption>
        {
            new(0x00FF, "[0x00FF] Slot vide"),
            new(0x0000, "[0x0000] Valeur 0 / aucun"),
        };

        if (_workspace != null)
        {
            var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage;
            if (lang == null || !_workspace.AbilitiesByLanguage.ContainsKey(lang))
                lang = _workspace.AbilitiesByLanguage.Keys.FirstOrDefault();

            if (lang != null && _workspace.AbilitiesByLanguage.TryGetValue(lang, out var file))
            {
                var charset = _workspace.GetCharsetForLanguage(lang);
                var custos = _workspace.GearCustomizations;
                for (int i = 0; i < file.Count; i++)
                {
                    var ability = file.Entries[i];
                    var id = file.MinIndex + i;
                    var fallbackName = GetGearAbilityFallbackName(id);
                    if (ability.IsEmpty && fallbackName == null) continue;

                    if (custos != null)
                    {
                        var allowed = isArmor
                            ? custos.IsArmorAbility(id)
                            : custos.IsWeaponAbility(id);
                        if (!allowed && fallbackName != null && !isArmor)
                            allowed = true;
                        if (!allowed) continue;
                    }

                    var name = file.GetName(ability, charset);
                    if (string.IsNullOrWhiteSpace(name)) name = fallbackName ?? "(sans nom)";
                    var storedId = id | 0x8000;
                    options.Add(new GearAbilityOption(storedId, $"[0x{storedId:X4}] {name}"));
                }
            }
        }

        if (currentId > 0 && currentId != 0x00FF)
            EnsureGearAbilityOptionExists(options, currentId, $"[0x{currentId:X4}] {ResolveGearAbilityName(currentId)}");
        return options;
    }

    private static void EnsureGearAbilityOptionExists(List<GearAbilityOption> options, int id, string displayName)
    {
        if (options.All(o => o.Id != id))
            options.Insert(Math.Min(1, options.Count), new GearAbilityOption(id, displayName));
    }

    private void OnGearMechanic_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressGearEvents) return;
        ApplyGearButton.IsEnabled = true;
        ApplyWeaponNameAllLanguagesButton.IsEnabled =
            GearNameGroup.Visibility == Visibility.Visible
            && GearNameBox.IsEnabled
            && _workspace?.WeaponNamesByLanguage.Count > 1;
        RevertGearButton.IsEnabled = true;
        GearEditStatusText.Text = "● Modifications non appliquées";
    }

    private void OnRevertGear_Click(object sender, RoutedEventArgs e)
    {
        if (GearListBox.SelectedItem is GearListItem item)
            DisplayGear(item);
    }

    private void OnApplyGear_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || GearListBox.SelectedItem is not GearListItem item)
            return;

        var gear = item.Gear;
        if (!TryReadIntBox(GearSlotsBox, "Slots", 0, 4, out var slots)) return;
        if (!TryReadIntBox(GearFormulaBox, "Formule", 0, 255, out var formula)) return;
        if (!TryReadIntBox(GearPowerBox, "Puissance", 0, 255, out var power)) return;
        if (!TryReadIntBox(GearCritBox, "Critique", 0, 255, out var crit)) return;
        if (!TryReadIntBox(GearArmorByteBox, "Armure byte", 0, 255, out var armorByte)) return;
        if (!TryReadIntBox(GearFlagsBox, "Flags bruts", 0, 255, out var flags)) return;
        flags = MergeKnownBitfield(flags,
            ReadBitfieldFromChecks(GearFlagsPanel, "GearFlags"), GearFlagDefs);

        var model = gear.ModelIdx;
        if (GearModelBox.IsEnabled && !TryReadIntBox(GearModelBox, "Modèle", 0, 0xFFFF, out model)) return;

        var nameId1 = gear.NameIdMaybe1;
        var nameId2 = gear.NameIdMaybe2;
        if (!gear.IsBukiGet && item.SourceTag != "weapon")
        {
            if (!TryReadIntBox(GearNameId1Box, "Index nom 1", 0, 255, out nameId1)) return;
            if (!TryReadIntBox(GearNameId2Box, "Index nom 2", 0, 255, out nameId2)) return;
        }

        if (!TryReadGearAbilitySelections(out var selectedAbilities)) return;

        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage ?? "jp";
        var charset = _workspace.GetCharsetForLanguage(lang);
        WeaponNameFile? weaponNameFile = null;
        var canEditName = GearNameGroup.Visibility == Visibility.Visible
            && GearNameBox.IsEnabled
            && !gear.IsArmor
            && gear.Character >= 0
            && gear.Character < WeaponNameEntry.CHARACTER_COUNT
            && _workspace.WeaponNamesByLanguage.TryGetValue(lang, out weaponNameFile)
            && charset != null;

        if (canEditName)
        {
            var weaponNameModel = model;
            if (!gear.IsBukiGet
                && !TryReadIntBox(GearWeaponNameModelBox, "Modèle w_name", 0, 0xFFFF, out weaponNameModel))
                return;

            if (!TryResolveUnsupportedChars(charset!, lang, GearNameBox, GearSimpleNameBox))
                return;

            var weaponNameIndex = gear.IsBukiGet ? item.GlobalId : nameId1;
            var ok = weaponNameFile!.SetTexts(weaponNameIndex, gear.Character, new WeaponNameTexts
            {
                Name = GearNameBox.Text,
                SimplifiedName = GearSimpleNameBox.Text,
            }, charset!);

            if (!ok)
            {
                MessageBox.Show(this,
                    $"Impossible d'écrire le nom : index 0x{weaponNameIndex:X4} hors plage dans w_name.bin.",
                    "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            weaponNameFile!.SetModelId(weaponNameIndex, gear.Character, weaponNameModel);
        }

        gear.Slots = slots;
        gear.Formula = formula;
        gear.Power = power;
        gear.Crit = crit;
        gear.ArmorByte = armorByte;
        gear.VariousFlags = flags;
        gear.ModelIdx = model;
        if (!gear.IsBukiGet && item.SourceTag != "weapon")
        {
            gear.NameIdMaybe1 = nameId1;
            gear.NameIdMaybe2 = nameId2;
        }
        for (int i = 0; i < selectedAbilities.Length; i++)
            gear.SetAbility(i, selectedAbilities[i]);
        gear.RefreshFlagBooleans();

        item.File.MarkDirty();
        item.Gear = gear;

        var selectedKey = (item.SourceTag, item.RelativeIndex);
        RebuildGearList();
        foreach (var candidate in _gearListItems)
        {
            if (candidate.SourceTag == selectedKey.SourceTag
                && candidate.RelativeIndex == selectedKey.RelativeIndex)
            {
                GearListBox.SelectedItem = candidate;
                break;
            }
        }

        ApplyGearButton.IsEnabled = false;
        ApplyWeaponNameAllLanguagesButton.IsEnabled = false;
        RevertGearButton.IsEnabled = false;
        GearEditStatusText.Text = "✓ Équipement appliqué (sauvegarde avec Ctrl+S)";
        UpdateSaveStatusUI();
    }

    private void OnApplyWeaponNameAllLanguages_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || GearListBox.SelectedItem is not GearListItem item)
            return;

        var gear = item.Gear;
        if (gear.IsArmor
            || gear.Character < 0
            || gear.Character >= WeaponNameEntry.CHARACTER_COUNT
            || GearNameGroup.Visibility != Visibility.Visible
            || !GearNameBox.IsEnabled)
            return;

        var weaponNameIndex = item.GlobalId;
        if (!gear.IsBukiGet)
        {
            if (!TryReadIntBox(GearNameId1Box, "Index nom 1", 0, 255, out weaponNameIndex))
                return;
        }

        if (!TryReadWeaponNameModelForCurrentGear(gear, out var weaponNameModel))
            return;

        var originalTexts = new WeaponNameTexts
        {
            Name = GearNameBox.Text,
            SimplifiedName = GearSimpleNameBox.Text,
        };
        var textsByLanguage = new Dictionary<string, WeaponNameTexts>(StringComparer.OrdinalIgnoreCase);
        var unsupported = new List<string>();
        var suggested = new List<string>();
        foreach (var (lang, _) in _workspace.WeaponNamesByLanguage)
        {
            var charset = _workspace.GetCharsetForLanguage(lang);
            if (charset == null) continue;

            var allText = $"{originalTexts.Name} {originalTexts.SimplifiedName}";
            var bad = charset.FindUnsupportedChars(allText);
            if (bad.Count == 0)
            {
                textsByLanguage[lang] = originalTexts;
                continue;
            }

            var suggestionMap = bad
                .Select(c => (Character: c, Suggestions: charset.GetInputSuggestions(c)))
                .ToList();

            if (suggestionMap.All(x => x.Suggestions.Count > 0))
            {
                var variant = new WeaponNameTexts
                {
                    Name = ApplyInputSuggestions(charset, originalTexts.Name ?? ""),
                    SimplifiedName = ApplyInputSuggestions(charset, originalTexts.SimplifiedName ?? ""),
                };
                var remaining = charset.FindUnsupportedChars($"{variant.Name} {variant.SimplifiedName}");
                if (remaining.Count == 0)
                {
                    textsByLanguage[lang] = variant;
                    suggested.Add($"{LanguageDisplayName(lang)} : " +
                                  string.Join(" ", suggestionMap.Select(x => FormatInlineCharsetSuggestion(x.Character, x.Suggestions))));
                    continue;
                }
            }

            if (bad.Count > 0)
                unsupported.Add($"{LanguageDisplayName(lang)} : {string.Join(" ", bad.Select(c => $"'{c}'"))}");
        }

        if (unsupported.Count > 0)
        {
            MessageBox.Show(this,
                "Le nom actuel ne peut pas être encodé dans toutes les langues chargées :\n\n" +
                string.Join(Environment.NewLine, unsupported),
                "Caractères non supportés", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (suggested.Count > 0)
        {
            var answer = MessageBox.Show(this,
                "Certaines langues ne peuvent pas encoder le nom tel quel, mais des équivalences compatibles existent.\n\n" +
                string.Join(Environment.NewLine, suggested) +
                "\n\nAppliquer ces équivalences uniquement aux fichiers des langues concernées ?",
                "Caractères non supportés", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK)
                return;
        }

        var touched = 0;
        var skipped = 0;
        foreach (var (lang, file) in _workspace.WeaponNamesByLanguage)
        {
            var charset = _workspace.GetCharsetForLanguage(lang);
            if (charset == null)
            {
                skipped++;
                continue;
            }

            if (!textsByLanguage.TryGetValue(lang, out var texts))
                texts = originalTexts;

            if (!file.SetTexts(weaponNameIndex, gear.Character, texts, charset))
            {
                skipped++;
                continue;
            }

            file.SetModelId(weaponNameIndex, gear.Character, weaponNameModel);
            touched++;
        }

        if (touched == 0)
        {
            MessageBox.Show(this,
                $"Impossible d'écrire le nom : index 0x{weaponNameIndex:X4} hors plage dans les w_name.bin chargés.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ApplyWeaponNameAllLanguagesButton.IsEnabled = false;
        GearEditStatusText.Text = skipped == 0
            ? $"✓ Nom d'arme appliqué à {touched} langue(s) (sauvegarde avec Ctrl+S)"
            : $"✓ Nom d'arme appliqué à {touched} langue(s), {skipped} ignorée(s)";
        UpdateSaveStatusUI();
    }

    private bool TryReadWeaponNameModelForCurrentGear(GearData gear, out int model)
    {
        if (gear.IsBukiGet)
            return TryReadIntBox(GearModelBox, "Modèle w_name", 0, 0xFFFF, out model);

        return TryReadIntBox(GearWeaponNameModelBox, "Modèle w_name", 0, 0xFFFF, out model);
    }

    private bool TryReadGearAbilitySelections(out int[] values)
    {
        values = new int[4];
        if (GearAbilitiesItemsControl.ItemsSource is not IEnumerable<GearAbilitySlotItem> rows)
        {
            MessageBox.Show(this, "La liste des aptitudes d'équipement n'est pas chargée.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        foreach (var row in rows)
        {
            if (row.Slot < 1 || row.Slot > 4) continue;
            if (row.SelectedId < 0 || row.SelectedId > 0xFFFF)
            {
                MessageBox.Show(this,
                    $"Le slot {row.Slot} doit contenir une aptitude valide.",
                    "Valeur invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            values[row.Slot - 1] = row.SelectedId;
        }
        return true;
    }

    /// <summary>Heuristique de catégorisation à partir du groupIndex / flags.</summary>
    private static string AbilityCategoryLabel(AutoAbilityData a)
    {
        // groupIndex semble identifier le type d'aptitude :
        // 0 = stat boost, 1 = elemental, 2 = status, etc. — pas documenté précisément.
        // En attendant on affiche juste le groupe brut sous une forme lisible.
        if (a.StatIncreaseAmount > 0 && a.StatIncreaseFlags != 0) return "Stat boost";
        if (a.ElementStrike != 0 || a.ElementAbsorb != 0 || a.ElementWeak != 0
            || a.ElementResist != 0 || a.ElementImmune != 0) return "Élément";
        if (a.AutoStatusesPermanent != 0 || a.AutoStatusesTemporal != 0
            || a.AutoStatusesExtra != 0) return "Auto-statut";
        if (a.ExtraStatusInflict != 0 || HasAnyStatusInflict(a)) return "Inflige statut";
        if (HasAnyStatusResist(a)) return "Résiste statut";
        return $"Groupe {a.GroupIndex}";
    }

    private static bool HasAnyStatusInflict(AutoAbilityData a) =>
        a.StatusInflictChanceDeath != 0 || a.StatusInflictChancePoison != 0
        || a.StatusInflictChanceConfuse != 0 || a.StatusInflictChanceSleep != 0
        || a.StatusInflictChanceSilence != 0 || a.StatusInflictChanceDarkness != 0;

    private static bool HasAnyStatusResist(AutoAbilityData a) =>
        a.StatusResistChanceDeath != 0 || a.StatusResistChancePoison != 0
        || a.StatusResistChanceConfuse != 0 || a.StatusResistChanceSleep != 0
        || a.StatusResistChanceSilence != 0 || a.StatusResistChanceDarkness != 0;

    private void AddGearFlagChip(bool active, string label, Brush color)
    {
        if (active) GearFlagsPanel.Children.Add(MakeChip(label, color));
    }

    private void FillEditableGearFlagsPanel(int flags)
    {
        GearFlagsPanel.Children.Clear();
        foreach (var (mask, label) in GearFlagDefs)
            GearFlagsPanel.Children.Add(MakeGearFlagCheck(label, mask, (flags & mask) != 0));

        var knownMask = GearFlagDefs.Aggregate(0, (acc, def) => acc | def.Mask);
        var unknownBits = flags & ~knownMask;
        if (unknownBits != 0)
        {
            GearFlagsPanel.Children.Add(new TextBlock
            {
                Text = $"Bits inconnus : 0x{unknownBits:X2}",
                Foreground = Brushes.IndianRed,
                FontStyle = FontStyles.Italic,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 2, 0, 2),
            });
        }
    }

    private CheckBox MakeGearFlagCheck(string label, int mask, bool isChecked)
    {
        var cb = new CheckBox
        {
            Content = label,
            Tag = $"GearFlags:{mask}",
            IsChecked = isChecked,
            Margin = new Thickness(4, 2, 12, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        cb.Checked += OnGearMechanic_Changed;
        cb.Unchecked += OnGearMechanic_Changed;
        return cb;
    }

    private enum GearSource { Both, PlayerStart, BukiGet, ShopArms }

    private static string GetGearSourceFileName(string sourceTag) => sourceTag switch
    {
        "weapon" => "weapon.bin (départ joueurs)",
        "buki" => "buki_get.bin",
        "shop" => "shop_arms.bin",
        _ => sourceTag,
    };

    private class GearSourceOption
    {
        public string DisplayName { get; }
        public GearSource Source { get; }
        public GearSourceOption(string n, GearSource s) { DisplayName = n; Source = s; }
        public override string ToString() => DisplayName;
    }

    private enum GearTypeFilter { Both, WeaponsOnly, ArmorsOnly }

    private class GearTypeOption
    {
        public string DisplayName { get; }
        public GearTypeFilter Type { get; }
        public GearTypeOption(string n, GearTypeFilter t) { DisplayName = n; Type = t; }
        public override string ToString() => DisplayName;
    }

    // =========================================================================
    // ONGLET APTITUDES D'ÉQUIPEMENT (a_ability.bin)
    // =========================================================================

    private readonly ObservableCollection<AbilityListItem> _abilityListItems = new();
    private List<AbilityListItem> _allAbilityItems = new();

    private void PopulateAbilityTab(SpiraWorkspace workspace)
    {
        AbilityListBox.ItemsSource = _abilityListItems;
        AbilityListBox.DisplayMemberPath = nameof(AbilityListItem.DisplayName);
        RebuildAbilityList();
    }

    private void OnAbilityFilter_Changed(object sender, TextChangedEventArgs e)
    {
        AbilityFilterPlaceholder.Visibility = string.IsNullOrEmpty(AbilityFilterBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        ApplyAbilityFilter();
    }

    private void OnAbilityCategoryFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        // ApplyAbilityFilter peut être appelé avant que la liste soit construite
        // (au boot de WPF, l'event arrive AVANT que _workspace soit assigné).
        if (_workspace == null) return;
        ApplyAbilityFilter();
    }

    private void RebuildAbilityList()
    {
        if (_workspace == null) return;

        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage;
        if (lang == null || !_workspace.AbilitiesByLanguage.ContainsKey(lang))
            lang = _workspace.AbilitiesByLanguage.Keys.FirstOrDefault();

        if (lang == null)
        {
            _allAbilityItems.Clear();
            ApplyAbilityFilter();
            return;
        }

        var file = _workspace.AbilitiesByLanguage[lang];
        var charset = _workspace.GetCharsetForLanguage(lang);
        var items = new List<AbilityListItem>();

        for (int i = 0; i < file.Count; i++)
        {
            var ability = file.Entries[i];
            var globalId = file.MinIndex + i;
            var name = file.GetName(ability, charset);
            items.Add(new AbilityListItem
            {
                File = file,
                Ability = ability,
                RelativeIndex = i,
                GlobalId = globalId,
                Name = string.IsNullOrWhiteSpace(name) ? "(sans nom)" : name,
                IsEmpty = ability.IsEmpty,
            });
        }
        _allAbilityItems = items;
        ApplyAbilityFilter();
    }

    private void ApplyAbilityFilter()
    {
        var filter = AbilityFilterBox.Text.Trim();
        IEnumerable<AbilityListItem> filtered = _allAbilityItems;

        // Filtre par catégorie (Arme / Armure / Both / None / All) basé sur kaizou.bin
        var category = (AbilityCategoryFilter?.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        var custos = _workspace?.GearCustomizations;
        if (custos != null && category != "all")
        {
            filtered = filtered.Where(item =>
            {
                var isW = custos.IsWeaponAbility(item.GlobalId);
                var isA = custos.IsArmorAbility(item.GlobalId);
                return category switch
                {
                    "weapon" => isW,
                    "armor"  => isA,
                    "both"   => isW && isA,
                    "none"   => !isW && !isA,
                    _        => true,
                };
            });
        }

        // Filtre texte
        if (!string.IsNullOrEmpty(filter))
        {
            filtered = filtered.Where(item =>
                item.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        _abilityListItems.Clear();
        foreach (var item in filtered) _abilityListItems.Add(item);

        AbilityCountText.Text = _abilityListItems.Count == _allAbilityItems.Count
            ? $"{_allAbilityItems.Count} aptitudes"
            : $"{_abilityListItems.Count} / {_allAbilityItems.Count} aptitudes";
    }

    private void OnAbilitySelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (AbilityListBox.SelectedItem is not AbilityListItem item)
        {
            NoAbilitySelectedMessage.Visibility = Visibility.Visible;
            AbilityDetailsPanel.Visibility = Visibility.Collapsed;
            return;
        }
        DisplayAbility(item);
    }

    private void DisplayAbility(AbilityListItem item)
    {
        if (_workspace == null) return;

        NoAbilitySelectedMessage.Visibility = Visibility.Collapsed;
        AbilityDetailsPanel.Visibility = Visibility.Visible;

        AbilityHeaderText.Text = item.DisplayName;

        var lang = GetEffectiveAbilityLanguage() ?? _currentLanguage ?? _workspace.PreferredDisplayLanguage ?? "jp";
        AbilityTextsGroup.Header = $"Texte — {LanguageDisplayName(lang)}";

        var charset = _workspace.GetCharsetForLanguage(lang);
        AutoAbilityFile? fileInLang = GetAbilityFileForItem(item, lang);
        var a = fileInLang != null && item.RelativeIndex < fileInLang.Count
            ? fileInLang.Entries[item.RelativeIndex]
            : item.Ability;

        _suppressAbilityEvents = true;
        try
        {
            if (fileInLang != null && item.RelativeIndex < fileInLang.Count)
            {
                var texts = fileInLang.GetTexts(item.RelativeIndex, charset);
                AbilityNameBox.Text = texts?.Name ?? "";
                AbilitySimpleNameBox.Text = texts?.SimplifiedName ?? "";
                AbilityDescBox.Text = texts?.Description ?? "";
                AbilitySimpleDescBox.Text = texts?.SimplifiedDescription ?? "";
            }
            else
            {
                AbilityNameBox.Text = AbilitySimpleNameBox.Text = AbilityDescBox.Text = AbilitySimpleDescBox.Text = "(non disponible)";
            }

            AbilityInfoText.Text =
                $"ID global : 0x{item.GlobalId:X4} ({item.GlobalId})  •  " +
                $"Index : {item.RelativeIndex}  •  " +
                $"Catégorie : {AbilityCategoryLabel(a)}";

            AbilityIconBox.Text         = a.Icon.ToString();
            AbilityGroupBox.Text        = a.GroupIndex.ToString();
            AbilityGroupLevelBox.Text   = a.GroupLevel.ToString();
            AbilityStatAmountBox.Text   = a.StatIncreaseAmount.ToString();
            AbilityStatFlagsBox.Text    = $"0x{a.StatIncreaseFlags:X4}";
            AbilityIntlBonusBox.Text    = a.InternationalBonusIndex.ToString();

            BuildAbilityRecipeView(item, lang);
            BuildAbilityElementsView(a);

            FillEditableAbilityFlagsPanel(AbilityAutoPermanentPanel, "AbilityAutoPermanent", a.AutoStatusesPermanent, FfxStatusFlags.Permanent);
            FillEditableAbilityFlagsPanel(AbilityAutoTemporalPanel,  "AbilityAutoTemporal",  a.AutoStatusesTemporal,  FfxStatusFlags.Temporal);
            FillEditableAbilityFlagsPanel(AbilityAutoExtraPanel,     "AbilityAutoExtra",     a.AutoStatusesExtra,     FfxStatusFlags.Extra);

            BuildAbilityStatusInflictGrid(a);
            BuildAbilityStatusResistGrid(a);

            AbilitySosFlagBox.Text = $"0x{a.SosFlagByte:X2}";
            AbilityFlag62Box.Text = $"0x{a.AbilityFlags62:X2}";
            AbilityFlag63Box.Text = $"0x{a.AbilityFlags63:X2}";
            AbilityFlag64Box.Text = $"0x{a.AbilityFlags64:X2}";
            AbilityFlag65Box.Text = $"0x{a.AbilityFlags65:X2}";
            AbilityFlag66Box.Text = $"0x{a.AbilityFlags66:X2}";
            AbilityUnknown67Box.Text = $"0x{a.UnknownByte67:X2}";
            AbilityExtraInflictBox.Text = $"0x{a.ExtraStatusInflict:X4}";
            AbilityExtraImmunityBox.Text = $"0x{a.ExtraStatusImmunities:X4}";

            ApplyAbilityButton.IsEnabled = false;
            ApplyAbilityMechanicsAllLanguagesButton.IsEnabled = true;
            RevertAbilityButton.IsEnabled = false;
            AbilityEditStatusText.Text = "Prêt";
        }
        finally
        {
            _suppressAbilityEvents = false;
        }
    }

    private string? GetEffectiveAbilityLanguage()
    {
        if (_workspace == null) return null;

        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage;
        if (lang != null && _workspace.AbilitiesByLanguage.ContainsKey(lang))
            return lang;

        return _workspace.AbilitiesByLanguage.Keys.FirstOrDefault();
    }

    private AutoAbilityFile? GetAbilityFileForItem(AbilityListItem item, string language)
    {
        if (_workspace == null) return null;
        return _workspace.AbilitiesByLanguage.TryGetValue(language, out var file)
            ? file
            : item.File;
    }

    /// <summary>
    /// Affiche la recette de customisation (arme et armure) pour l'aptitude sélectionnée.
    /// Les données viennent de kaizou.bin (workspace.GearCustomizations), résolues
    /// en noms d'objets via item.bin (LookupCommandName sur la plage 0x2000+).
    /// </summary>
    private void BuildAbilityRecipeView(AbilityListItem item, string lang)
    {
        if (_workspace == null) return;

        var custos = _workspace.GearCustomizations;
        if (custos == null)
        {
            AbilityRecipeStatusText.Text =
                "(kaizou.bin non trouvé — pas de recettes de craft disponibles)";
            ClearRecipeRow(AbilityWeaponRecipeItemCombo, AbilityWeaponRecipeQtyBox, AbilityWeaponRecipeItemIdBox);
            ClearRecipeRow(AbilityArmorRecipeItemCombo,  AbilityArmorRecipeQtyBox,  AbilityArmorRecipeItemIdBox);
            AbilityWeaponRecipeGrid.IsEnabled = false;
            AbilityArmorRecipeGrid.IsEnabled  = false;
            return;
        }

        var itemOptions = BuildRecipeItemOptions(lang);
        AbilityWeaponRecipeItemCombo.ItemsSource = itemOptions;
        AbilityArmorRecipeItemCombo.ItemsSource = itemOptions.ToList();

        var weaponRecipe = custos.GetWeaponRecipe(item.GlobalId);
        var armorRecipe  = custos.GetArmorRecipe(item.GlobalId);

        FillRecipeRow(weaponRecipe, lang,
            AbilityWeaponRecipeItemCombo, AbilityWeaponRecipeQtyBox, AbilityWeaponRecipeItemIdBox);
        FillRecipeRow(armorRecipe, lang,
            AbilityArmorRecipeItemCombo, AbilityArmorRecipeQtyBox, AbilityArmorRecipeItemIdBox);

        AbilityWeaponRecipeGrid.IsEnabled = weaponRecipe != null;
        AbilityArmorRecipeGrid.IsEnabled  = armorRecipe != null;

        // Statut récapitulatif
        var hasW = weaponRecipe != null;
        var hasA = armorRecipe  != null;
        AbilityRecipeStatusText.Text = (hasW, hasA) switch
        {
            (true,  true)  => "Cette aptitude peut être customisée sur les armes ET les armures.",
            (true,  false) => "Cette aptitude est exclusive aux ARMES.",
            (false, true)  => "Cette aptitude est exclusive aux ARMURES.",
            (false, false) => "Cette aptitude n'a pas de recette de customisation dans kaizou.bin " +
                              "(probablement obtenue uniquement via Drop / présente d'origine sur un équipement / réservée aux Chimères).",
        };
    }

    private List<RecipeItemOption> BuildRecipeItemOptions(string lang)
    {
        var options = new List<RecipeItemOption>();
        if (_workspace == null) return options;

        if (!_workspace.ItemsByLanguage.TryGetValue(lang, out var itemFile))
        {
            lang = _workspace.ItemsByLanguage.Keys.FirstOrDefault() ?? lang;
            if (!_workspace.ItemsByLanguage.TryGetValue(lang, out itemFile))
                return options;
        }

        var charset = _workspace.GetCharsetForLanguage(lang);
        for (int i = 0; i < itemFile.Count; i++)
        {
            var id = itemFile.MinIndex + i;
            var name = itemFile.GetName(i, charset);
            if (string.IsNullOrWhiteSpace(name)) name = "(sans nom)";
            options.Add(new RecipeItemOption(id, $"[0x{id:X4}] {name}"));
        }

        return options;
    }

    private void FillRecipeRow(CustomizationEntry? recipe, string lang,
        ComboBox itemCombo, TextBox qtyBox, TextBox itemIdBox)
    {
        if (recipe == null)
        {
            ClearRecipeRow(itemCombo, qtyBox, itemIdBox);
            return;
        }

        EnsureRecipeOptionExists(itemCombo, recipe.RequiredItemId, lang);
        itemCombo.SelectedValue = recipe.RequiredItemId;
        qtyBox.Text    = recipe.Quantity.ToString();
        itemIdBox.Text = $"0x{recipe.RequiredItemId:X4}";
    }

    private void EnsureRecipeOptionExists(ComboBox combo, int itemId, string lang)
    {
        if (combo.ItemsSource is not List<RecipeItemOption> options) return;
        if (options.Any(o => o.Id == itemId)) return;

        var itemName = _workspace?.LookupCommandName(itemId, lang) ?? "(nom non résolu)";
        options.Insert(0, new RecipeItemOption(itemId, $"[0x{itemId:X4}] {itemName}"));
        combo.ItemsSource = null;
        combo.ItemsSource = options;
    }

    private static void ClearRecipeRow(ComboBox itemCombo, TextBox qtyBox, TextBox itemIdBox)
    {
        itemCombo.SelectedIndex = -1;
        qtyBox.Text    = "";
        itemIdBox.Text = "";
    }

    /// <summary>
    /// Affiche les 5 effets élémentaires (Strike/Absorb/Immune/Resist/Weak) sous forme
    /// de cases éditables. Chaque effet est un bitfield des 8 éléments FFX.
    /// </summary>
    private void BuildAbilityElementsView(AutoAbilityData a)
    {
        AbilityElementsPanel.Children.Clear();

        var rows = new (string Label, string Group, int Bitfield)[]
        {
            ("Frappe",  "AbilityElementStrike", a.ElementStrike),
            ("Absorbe", "AbilityElementAbsorb", a.ElementAbsorb),
            ("Immun",   "AbilityElementImmune", a.ElementImmune),
            ("Résiste", "AbilityElementResist", a.ElementResist),
            ("Faible",  "AbilityElementWeak", a.ElementWeak),
        };

        foreach (var (label, group, bitfield) in rows)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            line.Children.Add(new TextBlock {
                Text = label + " :",
                Width = 80,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
            });

            var panel = new WrapPanel();
            FillEditableAbilityFlagsPanel(panel, group, bitfield, FfxStatusFlags.Elements);
            line.Children.Add(panel);

            AbilityElementsPanel.Children.Add(line);
        }
    }

    private void BuildAbilityStatusInflictGrid(AutoAbilityData a)
    {
        var rows = new (string Key, string Label, int Chance, int? Duration)[]
        {
            ("Death", "Mort", a.StatusInflictChanceDeath, null),
            ("Zombie", "Zombie", a.StatusInflictChanceZombie, null),
            ("Petrify", "Pétrification", a.StatusInflictChancePetrify, null),
            ("Poison", "Poison", a.StatusInflictChancePoison, null),
            ("PowerBreak", "Power Break", a.StatusInflictChancePowerBreak, null),
            ("MagicBreak", "Magic Break", a.StatusInflictChanceMagicBreak, null),
            ("ArmorBreak", "Armor Break", a.StatusInflictChanceArmorBreak, null),
            ("MentalBreak", "Mental Break", a.StatusInflictChanceMentalBreak, null),
            ("Confuse", "Confusion", a.StatusInflictChanceConfuse, null),
            ("Berserk", "Berserk", a.StatusInflictChanceBerserk, null),
            ("Provoke", "Provoque", a.StatusInflictChanceProvoke, null),
            ("Threaten", "Menace", a.StatusInflictChanceThreaten, null),
            ("Sleep", "Sommeil", a.StatusInflictChanceSleep, a.StatusDurationSleep),
            ("Silence", "Silence", a.StatusInflictChanceSilence, a.StatusDurationSilence),
            ("Darkness", "Obscurité", a.StatusInflictChanceDarkness, a.StatusDurationDarkness),
            ("Shell", "Carapace", a.StatusInflictChanceShell, a.StatusDurationShell),
            ("Protect", "Bouclier", a.StatusInflictChanceProtect, a.StatusDurationProtect),
            ("Reflect", "Reflet", a.StatusInflictChanceReflect, a.StatusDurationReflect),
            ("NTide", "NulMaree", a.StatusInflictChanceNTide, a.StatusDurationNTide),
            ("NBlaze", "NulFlamme", a.StatusInflictChanceNBlaze, a.StatusDurationNBlaze),
            ("NShock", "NulChoc", a.StatusInflictChanceNShock, a.StatusDurationNShock),
            ("NFrost", "NulFrimas", a.StatusInflictChanceNFrost, a.StatusDurationNFrost),
            ("Regen", "Régen", a.StatusInflictChanceRegen, a.StatusDurationRegen),
            ("Haste", "Hâte", a.StatusInflictChanceHaste, a.StatusDurationHaste),
            ("Slow", "Lenteur", a.StatusInflictChanceSlow, a.StatusDurationSlow),
        };
        FillEditableAbilityStatusGrid(AbilityInflictGrid, "AbilityInflict", rows);
    }

    private void BuildAbilityStatusResistGrid(AutoAbilityData a)
    {
        var rows = new (string Key, string Label, int Chance, int? Duration)[]
        {
            ("Death", "Mort", a.StatusResistChanceDeath, null),
            ("Zombie", "Zombie", a.StatusResistChanceZombie, null),
            ("Petrify", "Pétrification", a.StatusResistChancePetrify, null),
            ("Poison", "Poison", a.StatusResistChancePoison, null),
            ("PowerBreak", "Power Break", a.StatusResistChancePowerBreak, null),
            ("MagicBreak", "Magic Break", a.StatusResistChanceMagicBreak, null),
            ("ArmorBreak", "Armor Break", a.StatusResistChanceArmorBreak, null),
            ("MentalBreak", "Mental Break", a.StatusResistChanceMentalBreak, null),
            ("Confuse", "Confusion", a.StatusResistChanceConfuse, null),
            ("Berserk", "Berserk", a.StatusResistChanceBerserk, null),
            ("Provoke", "Provoque", a.StatusResistChanceProvoke, null),
            ("Threaten", "Menace", a.StatusResistChanceThreaten, null),
            ("Sleep", "Sommeil", a.StatusResistChanceSleep, null),
            ("Silence", "Silence", a.StatusResistChanceSilence, null),
            ("Darkness", "Obscurité", a.StatusResistChanceDarkness, null),
            ("Shell", "Carapace", a.StatusResistChanceShell, null),
            ("Protect", "Bouclier", a.StatusResistChanceProtect, null),
            ("Reflect", "Reflet", a.StatusResistChanceReflect, null),
            ("NTide", "NulMaree", a.StatusResistChanceNTide, null),
            ("NBlaze", "NulFlamme", a.StatusResistChanceNBlaze, null),
            ("NShock", "NulChoc", a.StatusResistChanceNShock, null),
            ("NFrost", "NulFrimas", a.StatusResistChanceNFrost, null),
            ("Regen", "Régen", a.StatusResistChanceRegen, null),
            ("Haste", "Hâte", a.StatusResistChanceHaste, null),
            ("Slow", "Lenteur", a.StatusResistChanceSlow, null),
        };
        FillEditableAbilityStatusGrid(AbilityResistGrid, "AbilityResist", rows);
    }

    /// <summary>Helper générique : construit une grille éditable pour les chances/durées de statuts.</summary>
    private void FillEditableAbilityStatusGrid(Grid grid, string group, (string Key, string Label, int Chance, int? Duration)[] rows)
    {
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddCell(grid, 0, 0, "Statut", FontWeights.Bold);
        AddCell(grid, 0, 1, "Chance %", FontWeights.Bold);
        AddCell(grid, 0, 2, "Durée (tours)", FontWeights.Bold);

        for (int i = 0; i < rows.Length; i++)
        {
            var row = i + 1;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddCell(grid, row, 0, rows[i].Label);
            AddAbilityStatusBox(grid, row, 1, $"{group}Chance:{rows[i].Key}", rows[i].Chance);
            if (rows[i].Duration is int duration)
                AddAbilityStatusBox(grid, row, 2, $"{group}Duration:{rows[i].Key}", duration);
            else
                AddCell(grid, row, 2, "—");
        }
    }

    private void AddAbilityStatusBox(Grid grid, int row, int col, string tag, int value)
    {
        var box = new TextBox
        {
            Text = value.ToString(),
            Tag = tag,
            Width = 70,
            Margin = new Thickness(4, 2, 4, 2),
            TextAlignment = TextAlignment.Center,
        };
        box.TextChanged += OnAbilityEdit_TextChanged;
        Grid.SetRow(box, row);
        Grid.SetColumn(box, col);
        grid.Children.Add(box);
    }

    private void FillEditableAbilityFlagsPanel(WrapPanel panel, string group, int bitfield, (int Mask, string Label)[] defs)
    {
        panel.Children.Clear();
        foreach (var (mask, label) in defs)
            panel.Children.Add(MakeAbilityFlagCheck(label, group, mask, (bitfield & mask) != 0));
    }

    private CheckBox MakeAbilityFlagCheck(string label, string group, int mask, bool isChecked)
    {
        var cb = new CheckBox
        {
            Content = label,
            Tag = $"{group}:{mask}",
            IsChecked = isChecked,
            Margin = new Thickness(4, 2, 10, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        cb.Checked += OnAbilityEdit_Changed;
        cb.Unchecked += OnAbilityEdit_Changed;
        return cb;
    }

    private void OnAbilityEdit_TextChanged(object sender, TextChangedEventArgs e)
    {
        MarkAbilityFormDirty();
    }

    private void OnAbilityEdit_Changed(object sender, RoutedEventArgs e)
    {
        MarkAbilityFormDirty();
    }

    private void OnAbilityRecipeItem_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender == AbilityWeaponRecipeItemCombo)
            UpdateRecipeItemIdBox(AbilityWeaponRecipeItemCombo, AbilityWeaponRecipeItemIdBox);
        else if (sender == AbilityArmorRecipeItemCombo)
            UpdateRecipeItemIdBox(AbilityArmorRecipeItemCombo, AbilityArmorRecipeItemIdBox);

        MarkAbilityFormDirty();
    }

    private void MarkAbilityFormDirty()
    {
        if (_suppressAbilityEvents) return;
        ApplyAbilityButton.IsEnabled = true;
        RevertAbilityButton.IsEnabled = true;
        AbilityEditStatusText.Text = "● Modifications non appliquées";
    }

    private static void UpdateRecipeItemIdBox(ComboBox combo, TextBox itemIdBox)
    {
        if (combo.SelectedValue is int itemId)
            itemIdBox.Text = $"0x{itemId:X4}";
        else if (combo.SelectedItem is RecipeItemOption opt)
            itemIdBox.Text = $"0x{opt.Id:X4}";
        else
            itemIdBox.Text = "";
    }

    private void OnRevertAbility_Click(object sender, RoutedEventArgs e)
    {
        if (AbilityListBox.SelectedItem is AbilityListItem item)
            DisplayAbility(item);
    }

    private void OnApplyAbility_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || AbilityListBox.SelectedItem is not AbilityListItem item)
            return;

        var lang = GetEffectiveAbilityLanguage() ?? _currentLanguage ?? _workspace.PreferredDisplayLanguage ?? "jp";
        var charset = _workspace.GetCharsetForLanguage(lang);
        if (charset == null)
        {
            MessageBox.Show(this,
                "La charset de cette langue n'est pas chargée — impossible de réencoder les textes.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var file = GetAbilityFileForItem(item, lang);
        if (file == null || item.RelativeIndex < 0 || item.RelativeIndex >= file.Count)
        {
            MessageBox.Show(this,
                $"Aucun fichier a_ability.bin ne couvre cette aptitude dans la langue {lang}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryReadIntBox(AbilityIconBox, "Icône", 0, 255, out var icon)) return;
        if (!TryReadIntBox(AbilityGroupBox, "Groupe", 0, 255, out var group)) return;
        if (!TryReadIntBox(AbilityGroupLevelBox, "Niveau", 0, 255, out var groupLevel)) return;
        if (!TryReadIntBox(AbilityStatAmountBox, "Bonus stat", 0, 255, out var statAmount)) return;
        if (!TryReadIntBox(AbilityStatFlagsBox, "Cible bonus", 0, 0xFFFF, out var statFlags)) return;
        if (!TryReadIntBox(AbilityIntlBonusBox, "Spécial international", 0, 255, out var intlBonus)) return;

        if (!TryReadIntBox(AbilitySosFlagBox, "SOS flag", 0, 255, out var sosFlag)) return;
        if (!TryReadIntBox(AbilityFlag62Box, "Flag 0x62", 0, 255, out var flag62)) return;
        if (!TryReadIntBox(AbilityFlag63Box, "Flag 0x63", 0, 255, out var flag63)) return;
        if (!TryReadIntBox(AbilityFlag64Box, "Flag 0x64", 0, 255, out var flag64)) return;
        if (!TryReadIntBox(AbilityFlag65Box, "Flag 0x65", 0, 255, out var flag65)) return;
        if (!TryReadIntBox(AbilityFlag66Box, "Flag 0x66", 0, 255, out var flag66)) return;
        if (!TryReadIntBox(AbilityUnknown67Box, "Unknown 0x67", 0, 255, out var unknown67)) return;
        if (!TryReadIntBox(AbilityExtraInflictBox, "Extra inflige", 0, 0xFFFF, out var extraInflict)) return;
        if (!TryReadIntBox(AbilityExtraImmunityBox, "Extra immunités", 0, 0xFFFF, out var extraImmunity)) return;

        var weaponRecipe = _workspace.GearCustomizations?.GetWeaponRecipe(item.GlobalId);
        var armorRecipe = _workspace.GearCustomizations?.GetArmorRecipe(item.GlobalId);
        int weaponRecipeItem = 0, weaponRecipeQty = 0;
        int armorRecipeItem = 0, armorRecipeQty = 0;
        if (weaponRecipe != null
            && !TryReadAbilityRecipeRow(AbilityWeaponRecipeItemCombo, AbilityWeaponRecipeQtyBox,
                "recette arme", out weaponRecipeItem, out weaponRecipeQty))
            return;
        if (armorRecipe != null
            && !TryReadAbilityRecipeRow(AbilityArmorRecipeItemCombo, AbilityArmorRecipeQtyBox,
                "recette armure", out armorRecipeItem, out armorRecipeQty))
            return;

        var statusValues = new AutoAbilityData();
        if (!TryApplyAbilityStatusFields(statusValues)) return;

        if (!TryResolveUnsupportedChars(charset, lang,
                AbilityNameBox, AbilitySimpleNameBox, AbilityDescBox, AbilitySimpleDescBox))
            return;

        var newTexts = new AutoAbilityTexts
        {
            Name = AbilityNameBox.Text,
            SimplifiedName = AbilitySimpleNameBox.Text,
            Description = AbilityDescBox.Text,
            SimplifiedDescription = AbilitySimpleDescBox.Text,
        };
        if (!file.SetTexts(item.RelativeIndex, newTexts, charset))
        {
            MessageBox.Show(this, "Échec de l'écriture des textes en mémoire (index invalide).",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var ability = file.Entries[item.RelativeIndex];
        ability.Icon = icon;
        ability.GroupIndex = group;
        ability.GroupLevel = groupLevel;
        ability.StatIncreaseAmount = statAmount;
        ability.StatIncreaseFlags = statFlags;
        ability.InternationalBonusIndex = intlBonus;
        ability.SosFlagByte = sosFlag;
        ability.AbilityFlags62 = flag62;
        ability.AbilityFlags63 = flag63;
        ability.AbilityFlags64 = flag64;
        ability.AbilityFlags65 = flag65;
        ability.AbilityFlags66 = flag66;
        ability.UnknownByte67 = unknown67;
        ability.ExtraStatusInflict = extraInflict;
        ability.ExtraStatusImmunities = extraImmunity;

        ability.ElementStrike = MergeKnownBitfield(ability.ElementStrike,
            ReadBitfieldFromChecks(AbilityElementsPanel, "AbilityElementStrike"), FfxStatusFlags.Elements);
        ability.ElementAbsorb = MergeKnownBitfield(ability.ElementAbsorb,
            ReadBitfieldFromChecks(AbilityElementsPanel, "AbilityElementAbsorb"), FfxStatusFlags.Elements);
        ability.ElementImmune = MergeKnownBitfield(ability.ElementImmune,
            ReadBitfieldFromChecks(AbilityElementsPanel, "AbilityElementImmune"), FfxStatusFlags.Elements);
        ability.ElementResist = MergeKnownBitfield(ability.ElementResist,
            ReadBitfieldFromChecks(AbilityElementsPanel, "AbilityElementResist"), FfxStatusFlags.Elements);
        ability.ElementWeak = MergeKnownBitfield(ability.ElementWeak,
            ReadBitfieldFromChecks(AbilityElementsPanel, "AbilityElementWeak"), FfxStatusFlags.Elements);

        ability.AutoStatusesPermanent = MergeKnownBitfield(ability.AutoStatusesPermanent,
            ReadBitfieldFromChecks(AbilityAutoPermanentPanel, "AbilityAutoPermanent"), FfxStatusFlags.Permanent);
        ability.AutoStatusesTemporal = MergeKnownBitfield(ability.AutoStatusesTemporal,
            ReadBitfieldFromChecks(AbilityAutoTemporalPanel, "AbilityAutoTemporal"), FfxStatusFlags.Temporal);
        ability.AutoStatusesExtra = MergeKnownBitfield(ability.AutoStatusesExtra,
            ReadBitfieldFromChecks(AbilityAutoExtraPanel, "AbilityAutoExtra"), FfxStatusFlags.Extra);

        CopyAbilityStatusFields(statusValues, ability);

        file.MarkDirty();
        if (weaponRecipe != null)
            _workspace.GearCustomizations?.UpdateRecipe(CustomizationTarget.Weapon, item.GlobalId, weaponRecipeItem, weaponRecipeQty);
        if (armorRecipe != null)
            _workspace.GearCustomizations?.UpdateRecipe(CustomizationTarget.Armor, item.GlobalId, armorRecipeItem, armorRecipeQty);

        item.File = file;
        item.Ability = ability;
        item.Name = string.IsNullOrWhiteSpace(newTexts.Name) ? "(sans nom)" : newTexts.Name;
        item.IsEmpty = ability.IsEmpty;

        var selectedRelIdx = item.RelativeIndex;
        ApplyAbilityFilter();
        foreach (var candidate in _abilityListItems)
        {
            if (candidate.RelativeIndex == selectedRelIdx)
            {
                AbilityListBox.SelectedItem = candidate;
                break;
            }
        }

        ApplyAbilityButton.IsEnabled = false;
        RevertAbilityButton.IsEnabled = false;
        AbilityEditStatusText.Text = "✓ Aptitude appliquée (sauvegarde avec Ctrl+S)";
        UpdateMonsterLootResolvedSummary();
        UpdateSaveStatusUI();
    }

    private void OnApplyAbilityMechanicsAllLanguages_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace == null || AbilityListBox.SelectedItem is not AbilityListItem item)
            return;

        var lang = GetEffectiveAbilityLanguage() ?? _currentLanguage ?? _workspace.PreferredDisplayLanguage ?? "jp";
        var file = GetAbilityFileForItem(item, lang);
        if (file == null || item.RelativeIndex < 0 || item.RelativeIndex >= file.Count)
        {
            MessageBox.Show(this,
                $"Aucun fichier a_ability.bin ne couvre cette aptitude dans la langue {lang}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var sourceBase = file.Entries[item.RelativeIndex];
        if (!TryBuildAbilityMechanicsFromForm(sourceBase, out var mechanics))
            return;

        var weaponRecipe = _workspace.GearCustomizations?.GetWeaponRecipe(item.GlobalId);
        var armorRecipe = _workspace.GearCustomizations?.GetArmorRecipe(item.GlobalId);
        int weaponRecipeItem = 0, weaponRecipeQty = 0;
        int armorRecipeItem = 0, armorRecipeQty = 0;
        if (weaponRecipe != null
            && !TryReadAbilityRecipeRow(AbilityWeaponRecipeItemCombo, AbilityWeaponRecipeQtyBox,
                "recette arme", out weaponRecipeItem, out weaponRecipeQty))
            return;
        if (armorRecipe != null
            && !TryReadAbilityRecipeRow(AbilityArmorRecipeItemCombo, AbilityArmorRecipeQtyBox,
                "recette armure", out armorRecipeItem, out armorRecipeQty))
            return;

        var touched = 0;
        foreach (var targetFile in _workspace.AbilitiesByLanguage.Values)
        {
            if (item.RelativeIndex < 0 || item.RelativeIndex >= targetFile.Count) continue;

            CopyAutoAbilityMechanics(mechanics, targetFile.Entries[item.RelativeIndex]);
            targetFile.MarkDirty();
            touched++;
        }

        if (weaponRecipe != null)
            _workspace.GearCustomizations?.UpdateRecipe(CustomizationTarget.Weapon, item.GlobalId, weaponRecipeItem, weaponRecipeQty);
        if (armorRecipe != null)
            _workspace.GearCustomizations?.UpdateRecipe(CustomizationTarget.Armor, item.GlobalId, armorRecipeItem, armorRecipeQty);

        item.File = file;
        item.Ability = file.Entries[item.RelativeIndex];
        item.IsEmpty = item.Ability.IsEmpty;
        AbilityInfoText.Text =
            $"ID global : 0x{item.GlobalId:X4} ({item.GlobalId})  •  " +
            $"Index : {item.RelativeIndex}  •  " +
            $"Catégorie : {AbilityCategoryLabel(item.Ability)}";
        AbilityEditStatusText.Text =
            $"✓ Mécaniques appliquées à {touched} langue(s) (sauvegarde avec Ctrl+S)";
        UpdateSaveStatusUI();
    }

    private bool TryBuildAbilityMechanicsFromForm(AutoAbilityData baseAbility, out AutoAbilityData mechanics)
    {
        mechanics = new AutoAbilityData();
        CopyAutoAbilityMechanics(baseAbility, mechanics);

        if (!TryReadIntBox(AbilityIconBox, "Icône", 0, 255, out var icon)) return false;
        if (!TryReadIntBox(AbilityGroupBox, "Groupe", 0, 255, out var group)) return false;
        if (!TryReadIntBox(AbilityGroupLevelBox, "Niveau", 0, 255, out var groupLevel)) return false;
        if (!TryReadIntBox(AbilityStatAmountBox, "Bonus stat", 0, 255, out var statAmount)) return false;
        if (!TryReadIntBox(AbilityStatFlagsBox, "Cible bonus", 0, 0xFFFF, out var statFlags)) return false;
        if (!TryReadIntBox(AbilityIntlBonusBox, "Spécial international", 0, 255, out var intlBonus)) return false;

        if (!TryReadIntBox(AbilitySosFlagBox, "SOS flag", 0, 255, out var sosFlag)) return false;
        if (!TryReadIntBox(AbilityFlag62Box, "Flag 0x62", 0, 255, out var flag62)) return false;
        if (!TryReadIntBox(AbilityFlag63Box, "Flag 0x63", 0, 255, out var flag63)) return false;
        if (!TryReadIntBox(AbilityFlag64Box, "Flag 0x64", 0, 255, out var flag64)) return false;
        if (!TryReadIntBox(AbilityFlag65Box, "Flag 0x65", 0, 255, out var flag65)) return false;
        if (!TryReadIntBox(AbilityFlag66Box, "Flag 0x66", 0, 255, out var flag66)) return false;
        if (!TryReadIntBox(AbilityUnknown67Box, "Unknown 0x67", 0, 255, out var unknown67)) return false;
        if (!TryReadIntBox(AbilityExtraInflictBox, "Extra inflige", 0, 0xFFFF, out var extraInflict)) return false;
        if (!TryReadIntBox(AbilityExtraImmunityBox, "Extra immunités", 0, 0xFFFF, out var extraImmunity)) return false;

        var statusValues = new AutoAbilityData();
        if (!TryApplyAbilityStatusFields(statusValues)) return false;

        mechanics.Icon = icon;
        mechanics.GroupIndex = group;
        mechanics.GroupLevel = groupLevel;
        mechanics.StatIncreaseAmount = statAmount;
        mechanics.StatIncreaseFlags = statFlags;
        mechanics.InternationalBonusIndex = intlBonus;
        mechanics.SosFlagByte = sosFlag;
        mechanics.AbilityFlags62 = flag62;
        mechanics.AbilityFlags63 = flag63;
        mechanics.AbilityFlags64 = flag64;
        mechanics.AbilityFlags65 = flag65;
        mechanics.AbilityFlags66 = flag66;
        mechanics.UnknownByte67 = unknown67;
        mechanics.ExtraStatusInflict = extraInflict;
        mechanics.ExtraStatusImmunities = extraImmunity;

        mechanics.ElementStrike = MergeKnownBitfield(baseAbility.ElementStrike,
            ReadBitfieldFromChecks(AbilityElementsPanel, "AbilityElementStrike"), FfxStatusFlags.Elements);
        mechanics.ElementAbsorb = MergeKnownBitfield(baseAbility.ElementAbsorb,
            ReadBitfieldFromChecks(AbilityElementsPanel, "AbilityElementAbsorb"), FfxStatusFlags.Elements);
        mechanics.ElementImmune = MergeKnownBitfield(baseAbility.ElementImmune,
            ReadBitfieldFromChecks(AbilityElementsPanel, "AbilityElementImmune"), FfxStatusFlags.Elements);
        mechanics.ElementResist = MergeKnownBitfield(baseAbility.ElementResist,
            ReadBitfieldFromChecks(AbilityElementsPanel, "AbilityElementResist"), FfxStatusFlags.Elements);
        mechanics.ElementWeak = MergeKnownBitfield(baseAbility.ElementWeak,
            ReadBitfieldFromChecks(AbilityElementsPanel, "AbilityElementWeak"), FfxStatusFlags.Elements);

        mechanics.AutoStatusesPermanent = MergeKnownBitfield(baseAbility.AutoStatusesPermanent,
            ReadBitfieldFromChecks(AbilityAutoPermanentPanel, "AbilityAutoPermanent"), FfxStatusFlags.Permanent);
        mechanics.AutoStatusesTemporal = MergeKnownBitfield(baseAbility.AutoStatusesTemporal,
            ReadBitfieldFromChecks(AbilityAutoTemporalPanel, "AbilityAutoTemporal"), FfxStatusFlags.Temporal);
        mechanics.AutoStatusesExtra = MergeKnownBitfield(baseAbility.AutoStatusesExtra,
            ReadBitfieldFromChecks(AbilityAutoExtraPanel, "AbilityAutoExtra"), FfxStatusFlags.Extra);

        CopyAbilityStatusFields(statusValues, mechanics);
        return true;
    }

    private static void CopyAutoAbilityMechanics(AutoAbilityData source, AutoAbilityData target)
    {
        target.SosFlagByte = source.SosFlagByte;
        target.ElementStrike = source.ElementStrike;
        target.ElementAbsorb = source.ElementAbsorb;
        target.ElementImmune = source.ElementImmune;
        target.ElementResist = source.ElementResist;
        target.ElementWeak = source.ElementWeak;

        CopyAbilityStatusFields(source, target);

        target.StatIncreaseAmount = source.StatIncreaseAmount;
        target.StatIncreaseFlags = source.StatIncreaseFlags;
        target.AutoStatusesPermanent = source.AutoStatusesPermanent;
        target.AutoStatusesTemporal = source.AutoStatusesTemporal;
        target.AutoStatusesExtra = source.AutoStatusesExtra;
        target.ExtraStatusInflict = source.ExtraStatusInflict;
        target.ExtraStatusImmunities = source.ExtraStatusImmunities;
        target.AbilityFlags62 = source.AbilityFlags62;
        target.AbilityFlags63 = source.AbilityFlags63;
        target.AbilityFlags64 = source.AbilityFlags64;
        target.AbilityFlags65 = source.AbilityFlags65;
        target.AbilityFlags66 = source.AbilityFlags66;
        target.UnknownByte67 = source.UnknownByte67;
        target.Icon = source.Icon;
        target.GroupIndex = source.GroupIndex;
        target.GroupLevel = source.GroupLevel;
        target.InternationalBonusIndex = source.InternationalBonusIndex;
    }

    private bool TryReadAbilityRecipeRow(ComboBox itemCombo, TextBox qtyBox, string label,
        out int itemId, out int quantity)
    {
        itemId = 0;
        quantity = 0;

        if (itemCombo.SelectedValue is int selectedId)
            itemId = selectedId;
        else if (itemCombo.SelectedItem is RecipeItemOption opt)
            itemId = opt.Id;
        else
        {
            MessageBox.Show(this,
                $"Sélectionne un objet pour la {label}.",
                "Valeur invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
            itemCombo.Focus();
            return false;
        }

        return TryReadIntBox(qtyBox, $"Quantité {label}", 0, 255, out quantity);
    }

    private bool TryApplyAbilityStatusFields(AutoAbilityData a)
    {
        bool ReadChance(Grid grid, string group, string key, string label, Action<int> assign)
        {
            if (!TryReadAbilityStatusChance(grid, group, key, label, out var value)) return false;
            assign(value);
            return true;
        }

        bool ReadDuration(string key, string label, Action<int> assign)
        {
            if (!TryReadAbilityStatusDuration(key, label, out var value)) return false;
            assign(value);
            return true;
        }

        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "Death", "Mort", v => a.StatusInflictChanceDeath = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "Zombie", "Zombie", v => a.StatusInflictChanceZombie = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "Petrify", "Pétrification", v => a.StatusInflictChancePetrify = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "Poison", "Poison", v => a.StatusInflictChancePoison = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "PowerBreak", "Power Break", v => a.StatusInflictChancePowerBreak = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "MagicBreak", "Magic Break", v => a.StatusInflictChanceMagicBreak = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "ArmorBreak", "Armor Break", v => a.StatusInflictChanceArmorBreak = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "MentalBreak", "Mental Break", v => a.StatusInflictChanceMentalBreak = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "Confuse", "Confusion", v => a.StatusInflictChanceConfuse = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "Berserk", "Berserk", v => a.StatusInflictChanceBerserk = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "Provoke", "Provoque", v => a.StatusInflictChanceProvoke = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "Threaten", "Menace", v => a.StatusInflictChanceThreaten = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "Sleep", "Sommeil", v => a.StatusInflictChanceSleep = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "Silence", "Silence", v => a.StatusInflictChanceSilence = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "Darkness", "Obscurité", v => a.StatusInflictChanceDarkness = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "Shell", "Carapace", v => a.StatusInflictChanceShell = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "Protect", "Bouclier", v => a.StatusInflictChanceProtect = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "Reflect", "Reflet", v => a.StatusInflictChanceReflect = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "NTide", "NulMaree", v => a.StatusInflictChanceNTide = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "NBlaze", "NulFlamme", v => a.StatusInflictChanceNBlaze = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "NShock", "NulChoc", v => a.StatusInflictChanceNShock = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "NFrost", "NulFrimas", v => a.StatusInflictChanceNFrost = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "Regen", "Régen", v => a.StatusInflictChanceRegen = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "Haste", "Hâte", v => a.StatusInflictChanceHaste = v)) return false;
        if (!ReadChance(AbilityInflictGrid, "AbilityInflict", "Slow", "Lenteur", v => a.StatusInflictChanceSlow = v)) return false;

        if (!ReadDuration("Sleep", "Sommeil", v => a.StatusDurationSleep = v)) return false;
        if (!ReadDuration("Silence", "Silence", v => a.StatusDurationSilence = v)) return false;
        if (!ReadDuration("Darkness", "Obscurité", v => a.StatusDurationDarkness = v)) return false;
        if (!ReadDuration("Shell", "Carapace", v => a.StatusDurationShell = v)) return false;
        if (!ReadDuration("Protect", "Bouclier", v => a.StatusDurationProtect = v)) return false;
        if (!ReadDuration("Reflect", "Reflet", v => a.StatusDurationReflect = v)) return false;
        if (!ReadDuration("NTide", "NulMaree", v => a.StatusDurationNTide = v)) return false;
        if (!ReadDuration("NBlaze", "NulFlamme", v => a.StatusDurationNBlaze = v)) return false;
        if (!ReadDuration("NShock", "NulChoc", v => a.StatusDurationNShock = v)) return false;
        if (!ReadDuration("NFrost", "NulFrimas", v => a.StatusDurationNFrost = v)) return false;
        if (!ReadDuration("Regen", "Régen", v => a.StatusDurationRegen = v)) return false;
        if (!ReadDuration("Haste", "Hâte", v => a.StatusDurationHaste = v)) return false;
        if (!ReadDuration("Slow", "Lenteur", v => a.StatusDurationSlow = v)) return false;

        if (!ReadChance(AbilityResistGrid, "AbilityResist", "Death", "Mort", v => a.StatusResistChanceDeath = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "Zombie", "Zombie", v => a.StatusResistChanceZombie = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "Petrify", "Pétrification", v => a.StatusResistChancePetrify = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "Poison", "Poison", v => a.StatusResistChancePoison = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "PowerBreak", "Power Break", v => a.StatusResistChancePowerBreak = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "MagicBreak", "Magic Break", v => a.StatusResistChanceMagicBreak = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "ArmorBreak", "Armor Break", v => a.StatusResistChanceArmorBreak = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "MentalBreak", "Mental Break", v => a.StatusResistChanceMentalBreak = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "Confuse", "Confusion", v => a.StatusResistChanceConfuse = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "Berserk", "Berserk", v => a.StatusResistChanceBerserk = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "Provoke", "Provoque", v => a.StatusResistChanceProvoke = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "Threaten", "Menace", v => a.StatusResistChanceThreaten = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "Sleep", "Sommeil", v => a.StatusResistChanceSleep = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "Silence", "Silence", v => a.StatusResistChanceSilence = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "Darkness", "Obscurité", v => a.StatusResistChanceDarkness = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "Shell", "Carapace", v => a.StatusResistChanceShell = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "Protect", "Bouclier", v => a.StatusResistChanceProtect = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "Reflect", "Reflet", v => a.StatusResistChanceReflect = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "NTide", "NulMaree", v => a.StatusResistChanceNTide = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "NBlaze", "NulFlamme", v => a.StatusResistChanceNBlaze = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "NShock", "NulChoc", v => a.StatusResistChanceNShock = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "NFrost", "NulFrimas", v => a.StatusResistChanceNFrost = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "Regen", "Régen", v => a.StatusResistChanceRegen = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "Haste", "Hâte", v => a.StatusResistChanceHaste = v)) return false;
        if (!ReadChance(AbilityResistGrid, "AbilityResist", "Slow", "Lenteur", v => a.StatusResistChanceSlow = v)) return false;

        return true;
    }

    private static void CopyAbilityStatusFields(AutoAbilityData source, AutoAbilityData target)
    {
        target.StatusInflictChanceDeath = source.StatusInflictChanceDeath;
        target.StatusInflictChanceZombie = source.StatusInflictChanceZombie;
        target.StatusInflictChancePetrify = source.StatusInflictChancePetrify;
        target.StatusInflictChancePoison = source.StatusInflictChancePoison;
        target.StatusInflictChancePowerBreak = source.StatusInflictChancePowerBreak;
        target.StatusInflictChanceMagicBreak = source.StatusInflictChanceMagicBreak;
        target.StatusInflictChanceArmorBreak = source.StatusInflictChanceArmorBreak;
        target.StatusInflictChanceMentalBreak = source.StatusInflictChanceMentalBreak;
        target.StatusInflictChanceConfuse = source.StatusInflictChanceConfuse;
        target.StatusInflictChanceBerserk = source.StatusInflictChanceBerserk;
        target.StatusInflictChanceProvoke = source.StatusInflictChanceProvoke;
        target.StatusInflictChanceThreaten = source.StatusInflictChanceThreaten;
        target.StatusInflictChanceSleep = source.StatusInflictChanceSleep;
        target.StatusInflictChanceSilence = source.StatusInflictChanceSilence;
        target.StatusInflictChanceDarkness = source.StatusInflictChanceDarkness;
        target.StatusInflictChanceShell = source.StatusInflictChanceShell;
        target.StatusInflictChanceProtect = source.StatusInflictChanceProtect;
        target.StatusInflictChanceReflect = source.StatusInflictChanceReflect;
        target.StatusInflictChanceNTide = source.StatusInflictChanceNTide;
        target.StatusInflictChanceNBlaze = source.StatusInflictChanceNBlaze;
        target.StatusInflictChanceNShock = source.StatusInflictChanceNShock;
        target.StatusInflictChanceNFrost = source.StatusInflictChanceNFrost;
        target.StatusInflictChanceRegen = source.StatusInflictChanceRegen;
        target.StatusInflictChanceHaste = source.StatusInflictChanceHaste;
        target.StatusInflictChanceSlow = source.StatusInflictChanceSlow;

        target.StatusDurationSleep = source.StatusDurationSleep;
        target.StatusDurationSilence = source.StatusDurationSilence;
        target.StatusDurationDarkness = source.StatusDurationDarkness;
        target.StatusDurationShell = source.StatusDurationShell;
        target.StatusDurationProtect = source.StatusDurationProtect;
        target.StatusDurationReflect = source.StatusDurationReflect;
        target.StatusDurationNTide = source.StatusDurationNTide;
        target.StatusDurationNBlaze = source.StatusDurationNBlaze;
        target.StatusDurationNShock = source.StatusDurationNShock;
        target.StatusDurationNFrost = source.StatusDurationNFrost;
        target.StatusDurationRegen = source.StatusDurationRegen;
        target.StatusDurationHaste = source.StatusDurationHaste;
        target.StatusDurationSlow = source.StatusDurationSlow;

        target.StatusResistChanceDeath = source.StatusResistChanceDeath;
        target.StatusResistChanceZombie = source.StatusResistChanceZombie;
        target.StatusResistChancePetrify = source.StatusResistChancePetrify;
        target.StatusResistChancePoison = source.StatusResistChancePoison;
        target.StatusResistChancePowerBreak = source.StatusResistChancePowerBreak;
        target.StatusResistChanceMagicBreak = source.StatusResistChanceMagicBreak;
        target.StatusResistChanceArmorBreak = source.StatusResistChanceArmorBreak;
        target.StatusResistChanceMentalBreak = source.StatusResistChanceMentalBreak;
        target.StatusResistChanceConfuse = source.StatusResistChanceConfuse;
        target.StatusResistChanceBerserk = source.StatusResistChanceBerserk;
        target.StatusResistChanceProvoke = source.StatusResistChanceProvoke;
        target.StatusResistChanceThreaten = source.StatusResistChanceThreaten;
        target.StatusResistChanceSleep = source.StatusResistChanceSleep;
        target.StatusResistChanceSilence = source.StatusResistChanceSilence;
        target.StatusResistChanceDarkness = source.StatusResistChanceDarkness;
        target.StatusResistChanceShell = source.StatusResistChanceShell;
        target.StatusResistChanceProtect = source.StatusResistChanceProtect;
        target.StatusResistChanceReflect = source.StatusResistChanceReflect;
        target.StatusResistChanceNTide = source.StatusResistChanceNTide;
        target.StatusResistChanceNBlaze = source.StatusResistChanceNBlaze;
        target.StatusResistChanceNShock = source.StatusResistChanceNShock;
        target.StatusResistChanceNFrost = source.StatusResistChanceNFrost;
        target.StatusResistChanceRegen = source.StatusResistChanceRegen;
        target.StatusResistChanceHaste = source.StatusResistChanceHaste;
        target.StatusResistChanceSlow = source.StatusResistChanceSlow;
    }

    private bool TryReadAbilityStatusChance(Grid grid, string group, string key, string label, out int value)
    {
        var box = FindTaggedTextBox(grid, $"{group}Chance:{key}");
        if (box == null)
        {
            value = 0;
            MessageBox.Show(this, $"Champ de chance introuvable : {label}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        return TryReadIntBox(box, $"{label} chance", 0, 255, out value);
    }

    private bool TryReadAbilityStatusDuration(string key, string label, out int value)
    {
        var box = FindTaggedTextBox(AbilityInflictGrid, $"AbilityInflictDuration:{key}");
        if (box == null)
        {
            value = 0;
            MessageBox.Show(this, $"Champ de durée introuvable : {label}.",
                "Spira Modifier", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        return TryReadIntBox(box, $"{label} durée", 0, 255, out value);
    }

    // =========================================================================
    // ONGLET MAPS / CARTES — COFFRES (takara.bin)
    // =========================================================================

    private readonly ObservableCollection<TreasureListItem> _mapChestListItems = new();
    private List<TreasureListItem> _allMapChestItems = new();
    private IReadOnlyList<MapTreasureUsage> _mapChestUsages = Array.Empty<MapTreasureUsage>();
    private Dictionary<int, List<MapTreasureUsage>> _mapChestUsagesByTreasure = new();
    private TreasureListItem? _currentTreasureItem;
    private bool _suppressTreasureEvents;

    private void PopulateMapChestTab(SpiraWorkspace workspace)
    {
        MapChestListBox.ItemsSource = _mapChestListItems;
        MapChestListBox.DisplayMemberPath = nameof(TreasureListItem.DisplayName);
        RebuildMapChestList();
    }

    private void RebuildMapChestList()
    {
        _allMapChestItems.Clear();
        _mapChestUsages = Array.Empty<MapTreasureUsage>();
        _mapChestUsagesByTreasure.Clear();

        if (_workspace?.TreasureFile == null)
        {
            ApplyMapChestFilter();
            return;
        }

        _mapChestUsages = _workspace.GetTreasureUsages();
        _mapChestUsagesByTreasure = _mapChestUsages
            .GroupBy(u => u.TreasureIndex)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var entry in _workspace.TreasureFile.Entries)
        {
            _mapChestUsagesByTreasure.TryGetValue(entry.Index, out var usages);
            var usageList = usages ?? new List<MapTreasureUsage>();
            if (usageList.Count == 0)
            {
                _allMapChestItems.Add(new TreasureListItem
                {
                    Entry = entry,
                    ContentSummary = FormatTreasureEntry(entry),
                    UsageCount = 0,
                });
                continue;
            }

            foreach (var usage in usageList
                         .OrderBy(u => u.RegionName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(u => u.EventId, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(u => u.InstructionOffset))
            {
                _allMapChestItems.Add(new TreasureListItem
                {
                    Entry = entry,
                    Usage = usage,
                    ContentSummary = FormatTreasureEntry(entry),
                    UsageCount = usageList.Count,
                });
            }
        }

        _allMapChestItems = _allMapChestItems
            .OrderBy(i => i.Usage == null ? 1 : 0)
            .ThenBy(i => i.MapSortKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.Entry.Index)
            .ToList();

        PopulateMapChestMapFilter();
        ApplyMapChestFilter();
    }

    private void PopulateMapChestMapFilter()
    {
        if (MapChestMapFilter == null) return;

        var current = MapChestMapFilter.SelectedValue as string ?? "";
        var options = new List<MapChestMapOption>
        {
            new("", "Toutes les maps"),
        };

        options.AddRange(_mapChestUsages
            .GroupBy(u => u.MapCode, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var mapCode = g.Key;
                var label = $"{MapNameDictionary.GetDisplayName(mapCode)} ({mapCode}) • {g.Count()} coffre(s)";
                return new MapChestMapOption(mapCode, label);
            })
            .OrderBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase));

        MapChestMapFilter.ItemsSource = options;
        var selected = options.FirstOrDefault(o => string.Equals(o.MapCode, current, StringComparison.OrdinalIgnoreCase))
                       ?? options[0];
        MapChestMapFilter.SelectedItem = selected;
    }

    private void OnMapChestFilter_Changed(object sender, TextChangedEventArgs e)
    {
        MapChestFilterPlaceholder.Visibility = string.IsNullOrEmpty(MapChestFilterBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        ApplyMapChestFilter();
    }

    private void OnMapChestCategoryFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_workspace == null) return;
        ApplyMapChestFilter();
    }

    private void OnMapChestMapFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_workspace == null) return;
        ApplyMapChestFilter();
    }

    private void ApplyMapChestFilter()
    {
        var filter = MapChestFilterBox?.Text.Trim() ?? "";
        var category = (MapChestCategoryFilter?.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        var mapFilter = MapChestMapFilter?.SelectedValue as string ?? "";

        IEnumerable<TreasureListItem> filtered = _allMapChestItems;
        filtered = category switch
        {
            "gil" => filtered.Where(i => i.Entry.Kind == TreasureKinds.Gil),
            "item" => filtered.Where(i => i.Entry.Kind == TreasureKinds.Item),
            "gear" => filtered.Where(i => i.Entry.Kind == TreasureKinds.Gear),
            "key" => filtered.Where(i => i.Entry.Kind == TreasureKinds.KeyItem),
            "used" => filtered.Where(i => i.Usage != null),
            "unused" => filtered.Where(i => i.Usage == null),
            _ => filtered,
        };

        if (!string.IsNullOrWhiteSpace(mapFilter))
            filtered = filtered.Where(i => i.Usage != null
                                           && string.Equals(i.Usage.MapCode, mapFilter, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(filter))
        {
            filtered = filtered.Where(i =>
                i.Entry.Index.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase)
                || $"0x{i.Entry.Index:X3}".Contains(filter, StringComparison.OrdinalIgnoreCase)
                || i.ContentSummary.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (i.Usage?.EventId.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                || (i.Usage?.RegionName.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                || GetTreasureUsages(i.Entry.Index).Any(u =>
                    u.EventId.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || u.RegionName.Contains(filter, StringComparison.OrdinalIgnoreCase)));
        }

        _mapChestListItems.Clear();
        foreach (var item in filtered) _mapChestListItems.Add(item);

        MapChestCountText.Text = _mapChestListItems.Count == _allMapChestItems.Count
            ? $"{_allMapChestItems.Count} lignes map/coffre"
            : $"{_mapChestListItems.Count} / {_allMapChestItems.Count} lignes map/coffre";
    }

    private void OnMapChestSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MapChestListBox.SelectedItem is not TreasureListItem item)
        {
            _currentTreasureItem = null;
            NoMapChestSelectedMessage.Visibility = Visibility.Visible;
            MapChestDetailsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        DisplayMapChest(item);
    }

    private void DisplayMapChest(TreasureListItem item)
    {
        _currentTreasureItem = item;
        NoMapChestSelectedMessage.Visibility = Visibility.Collapsed;
        MapChestDetailsPanel.Visibility = Visibility.Visible;

        var entry = item.Entry;
        var usages = GetTreasureUsages(entry.Index).ToList();

        _suppressTreasureEvents = true;
        try
        {
            MapChestHeaderText.Text = item.Usage == null
                ? $"Entrée takara 0x{entry.Index:X3}"
                : $"{item.Usage.RegionName} — coffre 0x{entry.Index:X3}";
            MapChestInfoText.Text =
                $"Index : {entry.Index} (0x{entry.Index:X3})  •  " +
                $"Kind : 0x{entry.Kind:X2}  •  " +
                $"Références maps : {usages.Count}" +
                (item.Usage != null
                    ? $"  •  Sélection : {item.Usage.EventId} @ 0x{item.Usage.InstructionOffset:X4}"
                    : "");

            TreasureKindCombo.ItemsSource = BuildTreasureKindOptions(entry.Kind);
            TreasureKindCombo.SelectedValue = entry.Kind;
            TreasureRawKindBox.Text = $"0x{entry.Kind:X2}";
            TreasureQuantityBox.Text = entry.Quantity.ToString();
            TreasureTypeBox.Text = $"0x{entry.Type:X4}";
            RefreshTreasureTypeOptions(entry.Kind, entry.Type);
            TreasureAmountText.Text = FormatTreasureAmount(entry.Kind, entry.Quantity);
            TreasureResolvedContentText.Text = FormatTreasureEntry(entry);
            TreasureUsageListBox.ItemsSource = usages;

            ApplyTreasureButton.IsEnabled = false;
            RevertTreasureButton.IsEnabled = false;
            TreasureEditStatusText.Text = _workspace?.TreasureFile?.IsDirty == true
                ? "● takara.bin modifié en mémoire (sauvegarde avec Ctrl+S)"
                : "Prêt";
        }
        finally
        {
            _suppressTreasureEvents = false;
        }
    }

    private List<TreasureKindOption> BuildTreasureKindOptions(int currentKind)
    {
        var options = new List<TreasureKindOption>
        {
            new(TreasureKinds.Gil, "Gils"),
            new(TreasureKinds.Item, "Objet"),
            new(TreasureKinds.Gear, "Équipement buki_get.bin"),
            new(TreasureKinds.KeyItem, "Objet clé"),
        };

        if (options.All(o => o.Kind != currentKind))
            options.Add(new TreasureKindOption(currentKind, $"Inconnu 0x{currentKind:X2}"));

        return options;
    }

    private void RefreshTreasureTypeOptions(int kind, int currentType)
    {
        TreasureTypeCombo.ItemsSource = BuildTreasureTypeOptions(kind, currentType);
        TreasureTypeCombo.SelectedValue = currentType;
        TreasureTypeHintText.Text = kind switch
        {
            TreasureKinds.Gil => "Pour les gils, la quantité vaut montant / 100. Le type brut est conservé comme valeur auxiliaire.",
            TreasureKinds.Item => "ID global item.bin, typiquement 0x2000+.",
            TreasureKinds.Gear => "Index dans buki_get.bin, utilisé pour les armes/protections obtenues en jeu.",
            TreasureKinds.KeyItem => "Index local important.bin de l'objet clé.",
            _ => "Type inconnu : édition brute conservée.",
        };
    }

    private List<TreasureValueOption> BuildTreasureTypeOptions(int kind, int currentType)
    {
        var options = new List<TreasureValueOption>();

        switch (kind)
        {
            case TreasureKinds.Item:
                options.AddRange(BuildTreasureItemOptions());
                break;
            case TreasureKinds.Gear:
                options.AddRange(BuildTreasureGearOptions());
                break;
            case TreasureKinds.KeyItem:
                options.AddRange(BuildTreasureKeyItemOptions());
                break;
            case TreasureKinds.Gil:
                options.Add(new TreasureValueOption(currentType, $"[0x{currentType:X4}] Valeur auxiliaire"));
                break;
        }

        EnsureTreasureTypeOption(options, currentType, FormatTreasureTypeFallback(kind, currentType));
        return options.OrderBy(o => o.Id).ToList();
    }

    private IEnumerable<TreasureValueOption> BuildTreasureItemOptions()
    {
        if (_workspace == null) yield break;
        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage
                   ?? _workspace.ItemsByLanguage.Keys.FirstOrDefault();
        if (lang == null || !_workspace.ItemsByLanguage.TryGetValue(lang, out var file))
            yield break;

        var charset = _workspace.GetCharsetForLanguage(lang);
        for (int i = 0; i < file.Count; i++)
        {
            var id = file.MinIndex + i;
            var name = file.GetName(i, charset);
            if (string.IsNullOrWhiteSpace(name)) name = $"Objet 0x{id:X4}";
            yield return new TreasureValueOption(id, $"[0x{id:X4}] {name}");
        }
    }

    private IEnumerable<TreasureValueOption> BuildTreasureGearOptions()
    {
        if (_workspace?.BukiGetFile == null) yield break;

        var file = _workspace.BukiGetFile;
        for (int i = 0; i < file.Count; i++)
        {
            var id = file.MinIndex + i;
            yield return new TreasureValueOption(id, FormatTreasureGearOption(id, file.Entries[i]));
        }
    }

    private IEnumerable<TreasureValueOption> BuildTreasureKeyItemOptions()
    {
        if (_workspace == null) yield break;
        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage
                   ?? _workspace.KeyItemsByLanguage.Keys.FirstOrDefault();
        if (lang == null || !_workspace.KeyItemsByLanguage.TryGetValue(lang, out var file))
            yield break;

        var charset = _workspace.GetCharsetForLanguage(lang);
        for (int i = 0; i < file.Count; i++)
        {
            var id = file.MinIndex + i;
            var name = file.GetNameByGlobalId(id, charset);
            if (string.IsNullOrWhiteSpace(name)) name = $"Objet clé 0x{id:X4}";
            yield return new TreasureValueOption(id, $"[0x{id:X4}] {name}");
        }
    }

    private static void EnsureTreasureTypeOption(List<TreasureValueOption> options, int id, string label)
    {
        if (options.All(o => o.Id != id))
            options.Insert(0, new TreasureValueOption(id, label));
    }

    private string FormatTreasureTypeFallback(int kind, int type) => kind switch
    {
        TreasureKinds.Item => $"[0x{type:X4}] Objet non résolu",
        TreasureKinds.Gear => $"[0x{type:X4}] Équipement buki_get non résolu",
        TreasureKinds.KeyItem => $"[0x{type:X4}] {ResolveTreasureKeyItemName(type)}",
        TreasureKinds.Gil => $"[0x{type:X4}] Valeur auxiliaire",
        _ => $"[0x{type:X4}] Valeur brute",
    };

    private string FormatTreasureGearOption(int id, GearData gear)
    {
        var owner = PlayerCharacters.GetName(gear.Character) ?? $"#{gear.Character:X2}";
        var type = gear.IsArmor ? "Protection" : "Arme";
        var name = "";
        if (_workspace != null && !gear.IsArmor && gear.Character is >= 0 and < WeaponNameEntry.CHARACTER_COUNT)
        {
            var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage;
            if (lang != null)
                name = _workspace.LookupWeaponName(GetWeaponNameIndex(gear, id), gear.Character, lang) ?? "";
        }

        var namePart = string.IsNullOrWhiteSpace(name) ? "" : $" · {name}";
        return $"[0x{id:X4}] {owner} · {type} · P{gear.Power} S{gear.Slots}{namePart}{FormatGearAbilitySummary(gear)}";
    }

    private void OnTreasureKind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTreasureEvents) return;

        if (TreasureKindCombo.SelectedValue is int kind)
        {
            _suppressTreasureEvents = true;
            try
            {
                TreasureRawKindBox.Text = $"0x{kind:X2}";
                var currentType = TryParseIntText(TreasureTypeBox.Text, out var parsedType) ? parsedType : 0;
                RefreshTreasureTypeOptions(kind, currentType);
            }
            finally
            {
                _suppressTreasureEvents = false;
            }
        }

        MarkTreasureEditDirty();
    }

    private void OnMapChestEdit_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressTreasureEvents) return;

        if (ReferenceEquals(sender, TreasureTypeCombo) && TreasureTypeCombo.SelectedValue is int selectedType)
        {
            _suppressTreasureEvents = true;
            try { TreasureTypeBox.Text = $"0x{selectedType:X4}"; }
            finally { _suppressTreasureEvents = false; }
        }

        UpdateTreasurePreviewFromEditor();
        MarkTreasureEditDirty();
    }

    private void UpdateTreasurePreviewFromEditor()
    {
        if (!TryParseIntText(TreasureRawKindBox.Text, out var kind)
            || !TryParseIntText(TreasureQuantityBox.Text, out var quantity)
            || !TryParseIntText(TreasureTypeBox.Text, out var type))
            return;

        TreasureAmountText.Text = FormatTreasureAmount(kind, quantity);
        TreasureResolvedContentText.Text = FormatTreasureEntry(kind, quantity, type);
    }

    private void MarkTreasureEditDirty()
    {
        if (_suppressTreasureEvents || _currentTreasureItem == null) return;
        ApplyTreasureButton.IsEnabled = true;
        RevertTreasureButton.IsEnabled = true;
        TreasureEditStatusText.Text = "Modifications coffre non appliquées.";
    }

    private void OnApplyTreasure_Click(object sender, RoutedEventArgs e)
    {
        if (_workspace?.TreasureFile == null || _currentTreasureItem == null) return;

        if (!TryReadIntBox(TreasureRawKindBox, "Kind coffre", 0, 0xFF, out var kind)) return;
        if (!TryReadIntBox(TreasureQuantityBox, "Quantité coffre", 0, 0xFF, out var quantity)) return;
        if (!TryReadIntBox(TreasureTypeBox, "Type coffre", 0, 0xFFFF, out var type)) return;

        var entry = _currentTreasureItem.Entry;
        entry.Kind = kind;
        entry.Quantity = quantity;
        entry.Type = type;
        _workspace.TreasureFile.MarkDirty();

        var selectedIndex = entry.Index;
        var selectedEvent = _currentTreasureItem.Usage?.EventId;
        var selectedOffset = _currentTreasureItem.Usage?.InstructionOffset;
        RebuildMapChestList();
        var selected = _mapChestListItems.FirstOrDefault(i =>
            i.Entry.Index == selectedIndex
            && i.Usage?.EventId == selectedEvent
            && i.Usage?.InstructionOffset == selectedOffset)
            ?? _mapChestListItems.FirstOrDefault(i => i.Entry.Index == selectedIndex);
        if (selected != null)
            MapChestListBox.SelectedItem = selected;

        UpdateSaveStatusUI();
        StatusText.Text = "✓ Coffre appliqué (sauvegarde avec Ctrl+S)";
    }

    private void OnRevertTreasure_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTreasureItem != null)
            DisplayMapChest(_currentTreasureItem);
    }

    private string FormatTreasureEntry(TreasureEntry entry)
        => FormatTreasureEntry(entry.Kind, entry.Quantity, entry.Type);

    private string FormatTreasureEntry(int kind, int quantity, int type)
    {
        return kind switch
        {
            TreasureKinds.Gil => $"Gils : {quantity * 100}" + (type != 0 ? $" (type 0x{type:X4})" : ""),
            TreasureKinds.Item => $"Objet : {quantity}x {ResolveTreasureItemName(type)}",
            TreasureKinds.Gear => $"Équipement : {ResolveTreasureGearName(type)}" + (quantity != 1 ? $" (Q={quantity})" : ""),
            TreasureKinds.KeyItem => $"Objet clé : {ResolveTreasureKeyItemName(type)} [0x{type:X4}]",
            _ => $"Inconnu K=0x{kind:X2}, Q={quantity}, T=0x{type:X4}",
        };
    }

    private static string FormatTreasureAmount(int kind, int quantity)
        => kind == TreasureKinds.Gil ? $"{quantity * 100} gils" : "—";

    private string ResolveTreasureItemName(int globalId)
    {
        if (_workspace == null) return $"0x{globalId:X4}";
        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage;
        if (lang != null)
        {
            var name = _workspace.LookupCommandName(globalId, lang);
            if (!string.IsNullOrWhiteSpace(name))
                return $"{name} [0x{globalId:X4}]";
        }

        return $"Objet 0x{globalId:X4}";
    }

    private string ResolveTreasureKeyItemName(int globalId)
    {
        if (_workspace == null) return $"Objet clé 0x{globalId:X4}";
        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage;
        if (lang != null)
        {
            var name = _workspace.LookupKeyItemName(globalId, lang);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        foreach (var candidateLang in _workspace.KeyItemsByLanguage.Keys)
        {
            var name = _workspace.LookupKeyItemName(globalId, candidateLang);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        return $"Objet clé 0x{globalId:X4}";
    }

    private string ResolveTreasureGearName(int type)
    {
        if (_workspace?.BukiGetFile == null)
            return $"buki_get 0x{type:X4}";

        var relative = type - _workspace.BukiGetFile.MinIndex;
        if (relative < 0 || relative >= _workspace.BukiGetFile.Count)
            return $"buki_get 0x{type:X4}";

        return FormatTreasureGearOption(type, _workspace.BukiGetFile.Entries[relative]);
    }

    private IReadOnlyList<MapTreasureUsage> GetTreasureUsages(int treasureIndex)
        => _mapChestUsagesByTreasure.TryGetValue(treasureIndex, out var usages)
            ? usages
            : Array.Empty<MapTreasureUsage>();

    // =========================================================================
    // ONGLET SCÈNES DE COMBAT (btl.bin)
    // =========================================================================

    private readonly ObservableCollection<EncounterListItem> _encounterListItems = new();
    private List<EncounterListItem> _allEncounterItems = new();
    private BattleFile? _currentBattleSceneFile;
    private string? _currentBattleSceneName;
    private bool _suppressBattleSceneEditEvents;
    private readonly List<BattleSceneSlotControls> _battleSceneSlotControls = new();
    private readonly List<BattleScenePositionControls> _battleSceneMonsterPositionControls = new();

    private void PopulateEncounterTab(SpiraWorkspace workspace)
    {
        EncounterListBox.ItemsSource = _encounterListItems;
        EncounterListBox.DisplayMemberPath = nameof(EncounterListItem.DisplayName);
        RebuildEncounterList();
    }

    private void RebuildEncounterList()
    {
        _allEncounterItems.Clear();
        if (_workspace?.EncounterTables == null)
        {
            ApplyEncounterFilter();
            return;
        }

        foreach (var entry in _workspace.EncounterTables.Entries)
        {
            _allEncounterItems.Add(new EncounterListItem { Entry = entry });
        }
        ApplyEncounterFilter();
    }

    private void OnEncounterFilter_Changed(object sender, TextChangedEventArgs e)
    {
        EncounterFilterPlaceholder.Visibility = string.IsNullOrEmpty(EncounterFilterBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        ApplyEncounterFilter();
    }

    private void OnEncounterCategoryFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_workspace == null) return;
        ApplyEncounterFilter();
    }

    private void ApplyEncounterFilter()
    {
        var nameFilter = EncounterFilterBox?.Text.Trim() ?? "";
        var category = (EncounterCategoryFilter?.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";

        IEnumerable<EncounterListItem> filtered = _allEncounterItems;

        // Filtre catégorie
        filtered = category switch
        {
            "random"   => filtered.Where(i => i.Entry.HasRandom),
            "scripted" => filtered.Where(i => i.Entry.HasScripted),
            "mixed"    => filtered.Where(i => i.Entry.HasRandom && i.Entry.HasScripted),
            _          => filtered,
        };

        // Filtre texte : sur nom interne ET nom humain
        if (!string.IsNullOrEmpty(nameFilter))
        {
            filtered = filtered.Where(i =>
                i.Entry.MapName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)
                || MapNameDictionary.GetDisplayName(i.Entry.MapName)
                    .Contains(nameFilter, StringComparison.OrdinalIgnoreCase)
                || UiLocalization.Translate(MapNameDictionary.GetDisplayName(i.Entry.MapName))
                    .Contains(nameFilter, StringComparison.OrdinalIgnoreCase)
                || i.Entry.Id.ToString().Contains(nameFilter));
        }

        _encounterListItems.Clear();
        foreach (var item in filtered) _encounterListItems.Add(item);

        EncounterCountText.Text = _encounterListItems.Count == _allEncounterItems.Count
            ? $"{_allEncounterItems.Count} zones"
            : $"{_encounterListItems.Count} / {_allEncounterItems.Count} zones";
    }

    private void OnEncounterSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (EncounterListBox.SelectedItem is not EncounterListItem item)
        {
            NoEncounterSelectedMessage.Visibility = Visibility.Visible;
            EncounterDetailsPanel.Visibility = Visibility.Collapsed;
            return;
        }
        DisplayEncounter(item);
    }

    private void DisplayEncounter(EncounterListItem item)
    {
        NoEncounterSelectedMessage.Visibility = Visibility.Collapsed;
        EncounterDetailsPanel.Visibility = Visibility.Visible;

        var e = item.Entry;
        var humanName = MapNameDictionary.GetDisplayName(e.MapName);
        var internalCode = string.IsNullOrWhiteSpace(e.MapName) ? "(sans nom)" : e.MapName;

        EncounterHeaderText.Text = humanName;

        var typeSummary = (e.HasRandom, e.HasScripted) switch
        {
            (true,  true)  => "🎲 Aléatoires + 📜 Scriptées",
            (true,  false) => "🎲 Aléatoires uniquement",
            (false, true)  => "📜 Scriptées uniquement",
            _              => "(aucune formation)",
        };
        EncounterInfoText.Text =
            $"Code interne : {internalCode}  •  " +
            $"ID table : {e.Id} (0x{e.Id:X4})  •  " +
            $"Formations totales : {e.TotalFormationCount}  •  " +
            $"Groupes : {e.Groups.Count}  •  " +
            $"{typeSummary}";

        BuildRandomGroupsPanel(e);
        BuildScriptedGroupsPanel(e);

        // Cache le panneau de détail tant qu'aucun fichier n'est cliqué
        BattleSceneDetailsGroup.Visibility = Visibility.Collapsed;
    }

    private void RefreshSelectedEncounterGroupsPanel()
    {
        if (EncounterListBox.SelectedItem is not EncounterListItem selected)
            return;

        BuildRandomGroupsPanel(selected.Entry);
        BuildScriptedGroupsPanel(selected.Entry);
    }

    private void BuildRandomGroupsPanel(EncounterTableEntry entry)
    {
        EncounterRandomGroupsPanel.Children.Clear();

        var randomGroups = entry.RandomGroups.ToList();
        EncounterRandomGroup.Visibility = randomGroups.Any() ? Visibility.Visible : Visibility.Collapsed;
        if (!randomGroups.Any()) return;

        for (int gi = 0; gi < randomGroups.Count; gi++)
        {
            var group = randomGroups[gi];
            var groupBox = new GroupBox
            {
                Header = $"Groupe aléatoire #{gi + 1}  •  Danger : {group.Danger}  •  " +
                         $"Battlefield : {group.Battlefield}  •  Total tirage : {group.TotalWeight}",
                Margin = new Thickness(0, 0, 0, 8),
            };

            var groupControls = new List<EncounterChanceControl>();
            var stack = new StackPanel();
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddCell(grid, 0, 0, "Fichier de bataille", FontWeights.Bold);
            AddCell(grid, 0, 1, "Chance %", FontWeights.Bold);
            AddCell(grid, 0, 2, "Monstres", FontWeights.Bold);
            AddCell(grid, 0, 3, "Tirage",   FontWeights.Bold);
            AddCell(grid, 0, 4, "ID",       FontWeights.Bold);

            var editablePercents = BuildEditableEncounterPercents(group);
            for (int fi = 0; fi < group.Formations.Count; fi++)
            {
                var f = group.Formations[fi];
                var row = fi + 1;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                AddBattleFileLink(grid, row, 0, f.BattleFileName);
                var chanceBox = new TextBox
                {
                    Text = editablePercents.TryGetValue(f, out var percent) ? percent.ToString() : "0",
                    Width = 58,
                    Margin = new Thickness(4, 2, 4, 2),
                    Padding = new Thickness(3, 2, 3, 2),
                    ToolTip = "Pourcentage entier 0..100. La somme du groupe doit être exactement 100."
                };
                Grid.SetRow(chanceBox, row);
                Grid.SetColumn(chanceBox, 1);
                grid.Children.Add(chanceBox);
                groupControls.Add(new EncounterChanceControl(f, chanceBox));
                AddCell(grid, row, 2, ResolveBattleSceneMonsterCountText(f.BattleFileName));
                AddCell(grid, row, 3, f.Weight.ToString());
                AddCell(grid, row, 4, f.Id.ToString());
            }

            stack.Children.Add(grid);
            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };
            var applyButton = new Button
            {
                Content = "✓ Appliquer chances",
                Margin = new Thickness(0, 0, 10, 0)
            };
            applyButton.Click += (_, _) => OnApplyRandomEncounterChances(group, groupControls);
            footer.Children.Add(applyButton);
            footer.Children.Add(new TextBlock
            {
                Text = "Les chances modifient le poids de tirage du btl.bin. La colonne Monstres vient du fichier de bataille et ne change pas ici.",
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(footer);

            groupBox.Content = stack;
            EncounterRandomGroupsPanel.Children.Add(groupBox);
        }
    }

    private static Dictionary<EncounterFormation, int> BuildEditableEncounterPercents(EncounterGroup group)
    {
        var result = group.Formations.ToDictionary(f => f, _ => 0);
        if (group.TotalWeight <= 0) return result;

        var active = group.Formations
            .Where(f => f.Weight > 0)
            .Select(f =>
            {
                var exact = f.Weight * 100.0 / group.TotalWeight;
                var floor = (int)Math.Floor(exact);
                return new { Formation = f, Floor = floor, Fraction = exact - floor };
            })
            .ToList();
        if (active.Count == 0) return result;

        foreach (var item in active)
            result[item.Formation] = item.Floor;

        var remainder = 100 - active.Sum(item => item.Floor);
        foreach (var item in active.OrderByDescending(item => item.Fraction).Take(remainder))
            result[item.Formation]++;

        return result;
    }

    private string ResolveBattleSceneMonsterCountText(string battleFileName)
    {
        var count = _workspace?.LoadBattleFile(battleFileName)?.Formation?.MonsterCount;
        return count?.ToString() ?? "?";
    }

    private void OnApplyRandomEncounterChances(
        EncounterGroup group,
        IReadOnlyList<EncounterChanceControl> controls)
    {
        if (_workspace?.EncounterTables == null) return;

        var values = new List<(EncounterFormation Formation, int Chance)>();
        foreach (var control in controls)
        {
            if (!TryReadIntBox(control.ChanceBox, $"{control.Formation.BattleFileName} chance", 0, 100, out var chance))
                return;
            values.Add((control.Formation, chance));
        }

        var total = values.Sum(v => v.Chance);
        if (total != 100)
        {
            MessageBox.Show(this,
                $"La somme des chances du groupe doit être exactement 100 %.\nSomme actuelle : {total} %",
                "Chances invalides", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (values.All(v => v.Chance == 0))
        {
            MessageBox.Show(this,
                "Au moins une formation doit avoir une chance supérieure à 0 %.",
                "Chances invalides", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _workspace.EncounterTables.SetRandomGroupChances(group, values.Select(v => v.Chance).ToList());

        UpdateSaveStatusUI();
        StatusText.Text = "✓ Chances de rencontres appliquées (sauvegarde avec Ctrl+S)";

        if (EncounterListBox.SelectedItem is EncounterListItem selected)
            DisplayEncounter(selected);
    }

    private void BuildScriptedGroupsPanel(EncounterTableEntry entry)
    {
        EncounterScriptedGroupsPanel.Children.Clear();

        var scriptedGroups = entry.ScriptedGroups.ToList();
        EncounterScriptedGroup.Visibility = scriptedGroups.Any() ? Visibility.Visible : Visibility.Collapsed;
        if (!scriptedGroups.Any()) return;

        for (int gi = 0; gi < scriptedGroups.Count; gi++)
        {
            var group = scriptedGroups[gi];
            var groupBox = new GroupBox
            {
                Header = $"Groupe scripté #{gi + 1}  •  Danger : {group.Danger}  •  " +
                         $"Battlefield : {group.Battlefield}  •  {group.Formations.Count} formation(s)",
                Margin = new Thickness(0, 0, 0, 8),
            };

            var stack = new StackPanel();
            foreach (var f in group.Formations)
            {
                var link = new Hyperlink(new Run($"  • {f.BattleFileName}.bin   (ID {f.Id})"));
                link.Click += (_, _) => DisplayBattleScene(f.BattleFileName);
                var line = new TextBlock
                {
                    Margin = new Thickness(0, 2, 0, 2),
                    FontFamily = FontFamilies.Mono,
                };
                line.Inlines.Add(link);
                stack.Children.Add(line);
            }

            groupBox.Content = stack;
            EncounterScriptedGroupsPanel.Children.Add(groupBox);
        }
    }

    /// <summary>
    /// Ajoute un nom de fichier cliquable dans une grille — clic = ouverture du détail
    /// de la scène de combat correspondante.
    /// </summary>
    private void AddBattleFileLink(Grid grid, int row, int col, string battleFileName)
    {
        var link = new Hyperlink(new Run(battleFileName + ".bin"));
        link.Click += (_, _) => DisplayBattleScene(battleFileName);
        var tb = new TextBlock
        {
            Margin = new Thickness(4, 2, 4, 2),
            FontFamily = FontFamilies.Mono,
        };
        tb.Inlines.Add(link);
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }

    /// <summary>
    /// Affiche le détail d'un fichier de scène de combat dans le panneau dédié.
    /// </summary>
    private void DisplayBattleScene(string fileName)
    {
        if (_workspace == null) return;

        var file = _workspace.LoadBattleFile(fileName);
        if (file == null)
        {
            ClearBattleSceneEditor();
            BattleSceneDetailsGroup.Visibility = Visibility.Visible;
            BattleSceneHeaderText.Text = fileName + ".bin";
            BattleSceneSummaryText.Text = "⚠ Fichier introuvable ou illisible (le scan ne l'a peut-être pas détecté).";
            BattleSceneCapacityText.Text = "";
            BattleSceneMonstersList.ItemsSource = null;
            BattleScenePartyList.ItemsSource = null;
            BattleSceneAeonList.ItemsSource = null;
            BattleSceneInWaterText.Text = "—";
            BattleSceneVoicesText.Text = "—";
            BattleSceneAreasText.Text = "—";
            BattleSceneAtelSizeText.Text = "—";
            return;
        }

        BattleSceneDetailsGroup.Visibility = Visibility.Visible;
        BattleSceneHeaderText.Text = fileName + ".bin";
        _currentBattleSceneFile = file;
        _currentBattleSceneName = fileName;

        var lang = _currentLanguage ?? _workspace.PreferredDisplayLanguage ?? "fr";
        var firstArea = file.Areas.Count > 0 ? file.Areas[0] : null;
        BuildBattleSceneEditor(file, lang, firstArea);

        // --- Monstres ---
        var monsterRows = new List<BattleSceneMonsterRow>();
        if (file.Formation != null)
        {
            foreach (var slotIdx in file.Formation.NonEmptySlots)
            {
                var slotId = file.Formation.MonsterIds[slotIdx];
                string posStr = "—";
                if (firstArea != null && slotIdx < firstArea.MonsterPositions.Count)
                    posStr = firstArea.MonsterPositions[slotIdx].ToString();

                monsterRows.Add(new BattleSceneMonsterRow
                {
                    SlotLabel   = $"#{slotIdx}",
                    HexId       = $"0x{slotId:X4}",
                    MonsterName = ResolveMonsterNameForBattleScene(slotId, lang),
                    Position    = posStr,
                });
            }
        }
        BattleSceneMonstersList.ItemsSource = monsterRows;

        // --- Capacité ---
        var slotsUsed = monsterRows.Count;
        var slotsMax = BattleSceneConstants.MaxMonstersPerFormation;
        var capacityNote = slotsUsed > BattleSceneConstants.MaxActiveMonstersOnField
            ? $" — {BattleSceneConstants.MaxActiveMonstersOnField} actifs au max sur le terrain, " +
              $"{slotsUsed - BattleSceneConstants.MaxActiveMonstersOnField} en réserve (spawn auto)"
            : "";
        BattleSceneCapacityText.Text =
            $"📊 Slots monstres : {slotsUsed} / {slotsMax} utilisés{capacityNote}\n" +
            $"⚙ Limites moteur : max {BattleSceneConstants.MaxMonstersPerFormation} monstres par formation, " +
            $"mais le moteur ne gère que {BattleSceneConstants.MaxActiveMonstersOnField} monstres maximum par fichier de bataille, " +
            $"{BattleSceneConstants.MaxPartyMembers} joueurs + {BattleSceneConstants.MaxAeons} Chimères max.";

        BattleSceneSummaryText.Text = $"{slotsUsed} monstre(s) actif(s) dans la formation principale.";

        // --- Positions des personnages joueurs ---
        var partyRows = new List<BattleScenePositionRow>();
        if (firstArea != null)
        {
            for (int i = 0; i < firstArea.PartyPositions.Count; i++)
            {
                var p = firstArea.PartyPositions[i];
                if (p.IsZero) continue;
                partyRows.Add(new BattleScenePositionRow
                {
                    SlotLabel = $"P{i}",
                    Position = p.ToString(),
                    SwitchPosition = "",
                });
            }
        }
        BattleScenePartyList.ItemsSource = partyRows;
        BattleScenePartyExpander.Header =
            $"👤 Positions des personnages joueurs ({partyRows.Count} occupée(s))";
        BattleScenePartyExpander.Visibility = partyRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // --- Positions des Chimères ---
        var aeonRows = new List<BattleScenePositionRow>();
        if (firstArea != null)
        {
            for (int i = 0; i < firstArea.AeonPositions.Count; i++)
            {
                var p = firstArea.AeonPositions[i];
                if (p.IsZero) continue;
                aeonRows.Add(new BattleScenePositionRow
                {
                    SlotLabel = $"A{i}",
                    Position = p.ToString(),
                    SwitchPosition = "",
                });
            }
        }
        BattleSceneAeonList.ItemsSource = aeonRows;
        BattleSceneAeonExpander.Header =
            $"✨ Positions des Chimères ({aeonRows.Count} occupée(s))";
        BattleSceneAeonExpander.Visibility = aeonRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // --- Infos générales ---
        var f = file.Formation;
        BattleSceneInWaterText.Text = f == null ? "—" : (f.InWater ? "Oui (combat sous-marin)" : "Non");
        BattleSceneVoicesText.Text  = f == null ? "—" : (f.CommonVoiceLinesEnabled ? "Activées" : "Désactivées");
        BattleSceneAreasText.Text   = $"{file.Areas.Count} zone(s)";
        BattleSceneAtelSizeText.Text = file.AtelScriptSize > 0
            ? $"{file.AtelScriptSize:N0} octets (bytecode IA — décodage à venir)"
            : "(aucun script ATEL)";
    }

    private void ClearBattleSceneEditor()
    {
        _currentBattleSceneFile = null;
        _currentBattleSceneName = null;
        _battleSceneSlotControls.Clear();
        _battleSceneMonsterPositionControls.Clear();
        if (BattleSceneFormationEditGrid != null) BattleSceneFormationEditGrid.Children.Clear();
        if (BattleSceneMonsterPositionEditGrid != null) BattleSceneMonsterPositionEditGrid.Children.Clear();
        if (BattleSceneEditGroup != null) BattleSceneEditGroup.Visibility = Visibility.Collapsed;
        if (ApplyBattleSceneButton != null) ApplyBattleSceneButton.IsEnabled = false;
        if (RevertBattleSceneButton != null) RevertBattleSceneButton.IsEnabled = false;
        if (BattleSceneEditStatusText != null) BattleSceneEditStatusText.Text = "";
    }

    private void BuildBattleSceneEditor(BattleFile file, string lang, BattleArea? firstArea)
    {
        _suppressBattleSceneEditEvents = true;
        try
        {
            _battleSceneSlotControls.Clear();
            _battleSceneMonsterPositionControls.Clear();
            BattleSceneFormationEditGrid.Children.Clear();
            BattleSceneFormationEditGrid.RowDefinitions.Clear();
            BattleSceneFormationEditGrid.ColumnDefinitions.Clear();
            BattleSceneMonsterPositionEditGrid.Children.Clear();
            BattleSceneMonsterPositionEditGrid.RowDefinitions.Clear();
            BattleSceneMonsterPositionEditGrid.ColumnDefinitions.Clear();

            var formation = file.Formation;
            if (formation == null)
            {
                BattleSceneEditGroup.Visibility = Visibility.Collapsed;
                return;
            }

            BattleSceneEditGroup.Visibility = Visibility.Visible;
            BattleSceneInWaterCheck.IsChecked = formation.InWater;
            BattleSceneVoicesCheck.IsChecked = formation.CommonVoiceLinesEnabled;

            var monsterOptions = BuildBattleSceneMonsterOptions(lang, formation);
            BuildBattleSceneFormationGrid(formation, monsterOptions, firstArea);
            BuildBattleSceneMonsterPositionGrid(firstArea);

            ApplyBattleSceneButton.IsEnabled = false;
            RevertBattleSceneButton.IsEnabled = false;
            BattleSceneEditStatusText.Text = file.IsDirty
                ? "● Scène modifiée en mémoire (sauvegarde avec Ctrl+S)"
                : "";
            UpdateBattleSceneEditCountText();
        }
        finally
        {
            _suppressBattleSceneEditEvents = false;
        }
    }

    private List<BattleSceneMonsterOption> BuildBattleSceneMonsterOptions(string lang, BattleFormation formation)
    {
        var options = new List<BattleSceneMonsterOption>
        {
            new("— Slot vide", null)
        };

        if (_workspace != null)
        {
            foreach (var entry in _workspace.Scan.Monsters
                         .Select(e => (Entry: e, Number: TryGetMonsterNumberFromEntry(e)))
                         .Where(x => x.Number != null)
                         .OrderBy(x => x.Number!.Value))
            {
                var fileName = Path.GetFileNameWithoutExtension(entry.Entry.FullPath);
                var name = LookupMonsterName(entry.Entry, _workspace, lang);
                var label = string.IsNullOrWhiteSpace(name)
                    ? fileName
                    : $"{fileName} — {name}";
                options.Add(new BattleSceneMonsterOption(label, entry.Number));
            }
        }

        foreach (var slotId in formation.MonsterIds)
        {
            var monsterNumber = BattleSceneConstants.GetMonsterFileNumber(slotId);
            if (monsterNumber == null) continue;
            if (options.Any(o => o.MonsterNumber == monsterNumber)) continue;
            var fileName = BattleSceneConstants.FormatMonsterFileName(monsterNumber.Value);
            options.Add(new BattleSceneMonsterOption($"{fileName} — introuvable dans le scan", monsterNumber));
        }

        return options;
    }

    private void BuildBattleSceneFormationGrid(
        BattleFormation formation,
        List<BattleSceneMonsterOption> monsterOptions,
        BattleArea? firstArea)
    {
        BattleSceneFormationEditGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        BattleSceneFormationEditGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        BattleSceneFormationEditGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        BattleSceneFormationEditGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
        BattleSceneFormationEditGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        BattleSceneFormationEditGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddCell(BattleSceneFormationEditGrid, 0, 0, "Slot", FontWeights.Bold);
        AddCell(BattleSceneFormationEditGrid, 0, 1, "Monstre", FontWeights.Bold);
        AddCell(BattleSceneFormationEditGrid, 0, 2, "Flags", FontWeights.Bold);
        AddCell(BattleSceneFormationEditGrid, 0, 3, "ID brut", FontWeights.Bold);
        AddCell(BattleSceneFormationEditGrid, 0, 4, "Position", FontWeights.Bold);

        for (int slot = 0; slot < BattleSceneConstants.MaxMonstersPerFormation; slot++)
        {
            var row = slot + 1;
            BattleSceneFormationEditGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var slotId = formation.MonsterIds[slot];
            var monsterNumber = BattleSceneConstants.GetMonsterFileNumber(slotId);
            var flags = slotId == BattleSceneConstants.EmptySlotMarker
                ? 0
                : BattleSceneConstants.GetSlotFlags(slotId);

            AddCell(BattleSceneFormationEditGrid, row, 0, $"#{slot}");

            var combo = new ComboBox
            {
                ItemsSource = monsterOptions,
                DisplayMemberPath = nameof(BattleSceneMonsterOption.DisplayName),
                MinWidth = 330,
                Margin = new Thickness(2),
                IsTextSearchEnabled = true,
            };
            combo.SelectedItem = monsterOptions.FirstOrDefault(o => o.MonsterNumber == monsterNumber) ?? monsterOptions[0];
            combo.SelectionChanged += OnBattleSceneCombo_Changed;
            Grid.SetRow(combo, row);
            Grid.SetColumn(combo, 1);
            BattleSceneFormationEditGrid.Children.Add(combo);

            var flagsBox = new TextBox
            {
                Text = flags.ToString(),
                Width = 42,
                Margin = new Thickness(2),
                Padding = new Thickness(3, 2, 3, 2),
                ToolTip = "Bits hauts du slot (0..15). Laisse 0 pour un monstre ajouté normalement."
            };
            flagsBox.TextChanged += OnBattleSceneText_Changed;
            Grid.SetRow(flagsBox, row);
            Grid.SetColumn(flagsBox, 2);
            BattleSceneFormationEditGrid.Children.Add(flagsBox);

            var rawText = new TextBlock
            {
                Text = slotId == BattleSceneConstants.EmptySlotMarker ? "0xFFFF" : $"0x{slotId:X4}",
                Margin = new Thickness(4, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = FontFamilies.Mono,
                Foreground = Brushes.DimGray
            };
            Grid.SetRow(rawText, row);
            Grid.SetColumn(rawText, 3);
            BattleSceneFormationEditGrid.Children.Add(rawText);

            var positionText = slot < (firstArea?.MonsterPositions.Count ?? 0)
                ? $"Position #{slot} éditable"
                : $"Position #{slot} ajoutée si le slot est rempli";
            AddCell(BattleSceneFormationEditGrid, row, 4, positionText);

            _battleSceneSlotControls.Add(new BattleSceneSlotControls(slot, combo, flagsBox, rawText));
        }
    }

    private void BuildBattleSceneMonsterPositionGrid(BattleArea? firstArea)
    {
        BattleSceneMonsterPositionEditGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        BattleSceneMonsterPositionEditGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        BattleSceneMonsterPositionEditGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        BattleSceneMonsterPositionEditGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        BattleSceneMonsterPositionEditGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        BattleSceneMonsterPositionEditGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddCell(BattleSceneMonsterPositionEditGrid, 0, 0, "Pos", FontWeights.Bold);
        AddCell(BattleSceneMonsterPositionEditGrid, 0, 1, "X", FontWeights.Bold);
        AddCell(BattleSceneMonsterPositionEditGrid, 0, 2, "Y", FontWeights.Bold);
        AddCell(BattleSceneMonsterPositionEditGrid, 0, 3, "Z", FontWeights.Bold);
        AddCell(BattleSceneMonsterPositionEditGrid, 0, 4, "W", FontWeights.Bold);

        if (firstArea == null)
        {
            BattleSceneMonsterPositionEditGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddCell(BattleSceneMonsterPositionEditGrid, 1, 0, "Aucune position monstre éditable dans cette scène.");
            Grid.SetColumnSpan(BattleSceneMonsterPositionEditGrid.Children[^1], 5);
            return;
        }

        for (int i = 0; i < BattleSceneConstants.MaxMonstersPerFormation; i++)
        {
            var pos = i < firstArea.MonsterPositions.Count
                ? firstArea.MonsterPositions[i]
                : new BattlePosition();
            var row = i + 1;
            BattleSceneMonsterPositionEditGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddCell(BattleSceneMonsterPositionEditGrid, row, 0,
                i < firstArea.MonsterPositions.Count ? $"#{i}" : $"#{i} +");

            var xBox = AddBattleSceneFloatBox(BattleSceneMonsterPositionEditGrid, row, 1, pos.X);
            var yBox = AddBattleSceneFloatBox(BattleSceneMonsterPositionEditGrid, row, 2, pos.Y);
            var zBox = AddBattleSceneFloatBox(BattleSceneMonsterPositionEditGrid, row, 3, pos.Z);
            var wBox = AddBattleSceneFloatBox(BattleSceneMonsterPositionEditGrid, row, 4, pos.W);
            _battleSceneMonsterPositionControls.Add(new BattleScenePositionControls(i, xBox, yBox, zBox, wBox));
        }
    }

    private TextBox AddBattleSceneFloatBox(Grid grid, int row, int column, float value)
    {
        var box = new TextBox
        {
            Text = FormatBattleFloat(value),
            Width = 82,
            Margin = new Thickness(2),
            Padding = new Thickness(3, 2, 3, 2),
            FontFamily = FontFamilies.Mono,
        };
        box.TextChanged += OnBattleSceneText_Changed;
        Grid.SetRow(box, row);
        Grid.SetColumn(box, column);
        grid.Children.Add(box);
        return box;
    }

    private void OnBattleSceneEdit_Changed(object sender, RoutedEventArgs e) => MarkBattleSceneEditDirty();
    private void OnBattleSceneText_Changed(object sender, TextChangedEventArgs e) => MarkBattleSceneEditDirty();
    private void OnBattleSceneCombo_Changed(object sender, SelectionChangedEventArgs e) => MarkBattleSceneEditDirty();

    private void MarkBattleSceneEditDirty()
    {
        if (_suppressBattleSceneEditEvents || _currentBattleSceneFile == null) return;
        ApplyBattleSceneButton.IsEnabled = true;
        RevertBattleSceneButton.IsEnabled = true;
        BattleSceneEditStatusText.Text = "Modifications scène non appliquées.";
        UpdateBattleSceneEditCountText();
    }

    private void UpdateBattleSceneEditCountText()
    {
        if (BattleSceneEditCountText == null) return;
        var selectedCount = _battleSceneSlotControls.Count(c =>
            (c.MonsterCombo.SelectedItem as BattleSceneMonsterOption)?.MonsterNumber != null);
        var positionCount = _battleSceneMonsterPositionControls.Count;
        var reserveText = selectedCount > BattleSceneConstants.MaxActiveMonstersOnField
            ? $" Au-delà de {BattleSceneConstants.MaxActiveMonstersOnField}, le comportement dépend du script ATEL de la scène."
            : "";
        BattleSceneEditCountText.Text =
            $"Monstres sélectionnés : {selectedCount} / {BattleSceneConstants.MaxMonstersPerFormation}. " +
            $"Positions monstre affichées : {positionCount} (les lignes + seront ajoutées si leur slot est rempli).{reserveText}";
    }

    private void OnApplyBattleScene_Click(object sender, RoutedEventArgs e)
    {
        if (_currentBattleSceneFile?.Formation == null) return;

        var file = _currentBattleSceneFile;
        var formation = file.Formation;
        var firstArea = file.Areas.Count > 0 ? file.Areas[0] : null;
        foreach (var controls in _battleSceneSlotControls)
        {
            var option = controls.MonsterCombo.SelectedItem as BattleSceneMonsterOption;
            if (!TryReadIntBox(controls.FlagsBox, $"Flags slot #{controls.SlotIndex}", 0, 0xF, out var flags))
                return;

            if (option?.MonsterNumber == null)
            {
                formation.MonsterIds[controls.SlotIndex] = BattleSceneConstants.EmptySlotMarker;
                continue;
            }

            formation.MonsterIds[controls.SlotIndex] =
                ((flags & 0xF) << 12) | (option.MonsterNumber.Value & BattleSceneConstants.MonsterFileIdMask);
        }

        var occupiedSlots = formation.NonEmptySlots.ToHashSet();
        if (firstArea == null && occupiedSlots.Count > 0)
        {
            var answer = MessageBox.Show(this,
                "Cette scène ne contient pas de bloc de positions monstre éditable.\n\n" +
                "Les slots seront modifiés, mais les positions ne pourront pas être ajoutées automatiquement. Continuer ?",
                "Positions manquantes", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK) return;
        }

        if (firstArea != null)
        {
            foreach (var controls in _battleSceneMonsterPositionControls)
            {
                if (controls.Index < 0 || controls.Index >= BattleSceneConstants.MaxMonstersPerFormation) continue;
                if (controls.Index >= firstArea.MonsterPositions.Count
                    && !occupiedSlots.Contains(controls.Index))
                    continue;

                if (!TryReadFloatBox(controls.XBox, $"Position #{controls.Index} X", out var x)) return;
                if (!TryReadFloatBox(controls.YBox, $"Position #{controls.Index} Y", out var y)) return;
                if (!TryReadFloatBox(controls.ZBox, $"Position #{controls.Index} Z", out var z)) return;
                if (!TryReadFloatBox(controls.WBox, $"Position #{controls.Index} W", out var w)) return;

                while (controls.Index >= firstArea.MonsterPositions.Count)
                    firstArea.MonsterPositions.Add(new BattlePosition());

                var pos = firstArea.MonsterPositions[controls.Index];
                pos.X = x;
                pos.Y = y;
                pos.Z = z;
                pos.W = w;
            }
        }

        formation.InWater = BattleSceneInWaterCheck.IsChecked == true;
        formation.CommonVoiceLinesEnabled = BattleSceneVoicesCheck.IsChecked == true;

        file.MarkDirty();
        ApplyBattleSceneButton.IsEnabled = false;
        RevertBattleSceneButton.IsEnabled = false;
        BattleSceneEditStatusText.Text = "✓ Scène appliquée (sauvegarde avec Ctrl+S)";
        UpdateSaveStatusUI();

        var sceneName = _currentBattleSceneName;
        if (!string.IsNullOrWhiteSpace(sceneName))
        {
            DisplayBattleScene(sceneName);
            RefreshSelectedEncounterGroupsPanel();
        }
        BattleSceneEditStatusText.Text = "✓ Scène appliquée (sauvegarde avec Ctrl+S)";
    }

    private void OnRevertBattleScene_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_currentBattleSceneName))
            DisplayBattleScene(_currentBattleSceneName);
    }

    private bool TryReadFloatBox(TextBox box, string label, out float value)
    {
        var raw = box.Text.Trim().Replace(',', '.');
        if (!float.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value)
            || float.IsNaN(value)
            || float.IsInfinity(value))
        {
            MessageBox.Show(this,
                $"{label} doit être un nombre valide.\nValeur reçue : {box.Text}",
                "Valeur invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
            box.Focus();
            box.SelectAll();
            return false;
        }

        return true;
    }

    private static string FormatBattleFloat(float value)
        => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static int? TryGetMonsterNumberFromEntry(MonsterFileEntry entry)
    {
        var name = Path.GetFileNameWithoutExtension(entry.FullPath);
        if (name.Length < 2 || !name.StartsWith('m')) return null;
        return int.TryParse(name[1..], out var number) ? number : null;
    }

    /// <summary>
    /// Résout le nom d'un monstre par son ID de slot dans une formation.
    ///
    /// L'ID stocké dans la formation contient :
    ///   - bits 0-11 (masque 0x0FFF) : numéro du fichier m###.bin
    ///   - bits 12-15 : flags (statuts initiaux, visibilité, etc.)
    /// </summary>
    private string ResolveMonsterNameForBattleScene(int slotId, string lang)
    {
        if (_workspace == null) return "(workspace absent)";

        var monsterNumber = BattleSceneConstants.GetMonsterFileNumber(slotId);
        if (monsterNumber == null) return "(slot vide)";

        var candidate = BattleSceneConstants.FormatMonsterFileName(monsterNumber.Value);
        var monster = _workspace.Scan.Monsters.FirstOrDefault(m =>
            string.Equals(Path.GetFileNameWithoutExtension(m.FullPath), candidate,
                StringComparison.OrdinalIgnoreCase));

        if (monster == null) return $"({candidate} introuvable)";

        var name = LookupMonsterName(monster, _workspace, lang);
        return string.IsNullOrWhiteSpace(name) ? candidate : name!;
    }

    /// <summary>Helper pour ajouter une cellule avec une police monospace.</summary>
    private static void AddMonoCell(Grid grid, int row, int col, string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            Margin = new Thickness(4, 2, 4, 2),
            FontFamily = FontFamilies.Mono,
        };
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, col);
        grid.Children.Add(tb);
    }
}

/// <summary>Conteneur statique pour les FontFamily réutilisés.</summary>
internal static class FontFamilies
{
    public static readonly FontFamily Mono = new("Consolas");
}

/// <summary>Entrée de la sidebar Coffres / takara.bin.</summary>
public class TreasureListItem
{
    public TreasureEntry Entry { get; set; } = null!;
    public MapTreasureUsage? Usage { get; set; }
    public string ContentSummary { get; set; } = "";
    public int UsageCount { get; set; }
    public string MapSortKey => Usage == null ? "zzzz" : $"{Usage.RegionName}|{Usage.EventId}|{Usage.InstructionOffset:X4}";

    public string DisplayName
    {
        get
        {
            if (Usage == null)
                return $"[non référencé] [0x{Entry.Index:X3}] {ContentSummary}";

            var multi = UsageCount > 1 ? $" • partagé x{UsageCount}" : "";
            return $"{Usage.RegionName} • {Usage.EventId} • [0x{Entry.Index:X3}] {ContentSummary}{multi}";
        }
    }
}

public class MapChestMapOption
{
    public string MapCode { get; }
    public string DisplayName { get; }

    public MapChestMapOption(string mapCode, string displayName)
    {
        MapCode = mapCode;
        DisplayName = displayName;
    }
}

public class TreasureKindOption
{
    public int Kind { get; }
    public string DisplayName { get; }

    public TreasureKindOption(int kind, string displayName)
    {
        Kind = kind;
        DisplayName = displayName;
    }
}

public class TreasureValueOption
{
    public int Id { get; }
    public string DisplayName { get; }

    public TreasureValueOption(int id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }
}

/// <summary>Entrée de la sidebar Scènes de combat.</summary>
public class EncounterListItem
{
    public EncounterTableEntry Entry { get; set; } = null!;

    public string DisplayName
    {
        get
        {
            // Nom lisible (Mi'ihen Highroad – zone 2) au lieu de "mihn02"
            var humanName = MapNameDictionary.GetDisplayName(Entry.MapName);
            var marks = "";
            if (Entry.HasRandom)   marks += "🎲";
            if (Entry.HasScripted) marks += "📜";
            if (string.IsNullOrEmpty(marks)) marks = "·";

            return $"{humanName}  {marks}";
        }
    }

    /// <summary>Code interne d'origine (affiché en tooltip et dans le détail).</summary>
    public string InternalCode => Entry.MapName;
}

/// <summary>Ligne du tableau de monstres dans le détail d'une scène de combat.</summary>
public class BattleSceneMonsterRow
{
    public string SlotLabel { get; set; } = "";
    public string HexId { get; set; } = "";
    public string MonsterName { get; set; } = "";
    public string Position { get; set; } = "";
}

public class EncounterChanceControl
{
    public EncounterFormation Formation { get; }
    public TextBox ChanceBox { get; }

    public EncounterChanceControl(EncounterFormation formation, TextBox chanceBox)
    {
        Formation = formation;
        ChanceBox = chanceBox;
    }
}

/// <summary>Ligne pour positions joueurs / Chimères dans le détail d'une scène.</summary>
public class BattleScenePositionRow
{
    public string SlotLabel { get; set; } = "";
    public string Position { get; set; } = "";
    public string SwitchPosition { get; set; } = "";
}

public class BattleSceneMonsterOption
{
    public string DisplayName { get; }
    public int? MonsterNumber { get; }

    public BattleSceneMonsterOption(string displayName, int? monsterNumber)
    {
        DisplayName = displayName;
        MonsterNumber = monsterNumber;
    }

    public override string ToString() => DisplayName;
}

public class BattleSceneSlotControls
{
    public int SlotIndex { get; }
    public ComboBox MonsterCombo { get; }
    public TextBox FlagsBox { get; }
    public TextBlock RawIdText { get; }

    public BattleSceneSlotControls(int slotIndex, ComboBox monsterCombo, TextBox flagsBox, TextBlock rawIdText)
    {
        SlotIndex = slotIndex;
        MonsterCombo = monsterCombo;
        FlagsBox = flagsBox;
        RawIdText = rawIdText;
    }
}

public class BattleScenePositionControls
{
    public int Index { get; }
    public TextBox XBox { get; }
    public TextBox YBox { get; }
    public TextBox ZBox { get; }
    public TextBox WBox { get; }

    public BattleScenePositionControls(int index, TextBox xBox, TextBox yBox, TextBox zBox, TextBox wBox)
    {
        Index = index;
        XBox = xBox;
        YBox = yBox;
        ZBox = zBox;
        WBox = wBox;
    }
}

/// <summary>Entrée de la sidebar Aptitudes d'équipement.</summary>
public class AbilityListItem
{
    public AutoAbilityFile File { get; set; } = null!;
    public AutoAbilityData Ability { get; set; } = null!;
    public int RelativeIndex { get; set; }
    public int GlobalId { get; set; }
    public string Name { get; set; } = "";
    public bool IsEmpty { get; set; }

    public string DisplayName
    {
        get
        {
            var prefix = IsEmpty ? "(vide) " : "";
            return $"[0x{GlobalId:X4}] {prefix}{Name}";
        }
    }
}

/// <summary>Entrée de la sidebar Départ joueurs.</summary>
public class PlayerStartListItem
{
    public PlayerSaveFile File { get; set; } = null!;
    public PlayerSaveData Data { get; set; } = null!;
    public int RelativeIndex { get; set; }
    public int GlobalId { get; set; }
    public string Language { get; set; } = "jp";
    public string Name { get; set; } = "";
    public string DisplayName => $"[0x{GlobalId:X2}] {Name}";
}

public class PlayerStartGearOption
{
    public int Id { get; }
    public string DisplayName { get; }

    public PlayerStartGearOption(int id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }
}

/// <summary>Entrée de la sidebar Équipements.</summary>
public class GearListItem
{
    public GearFile File { get; set; } = null!;
    public GearData Gear { get; set; } = null!;
    public int RelativeIndex { get; set; }
    public int GlobalId { get; set; }
    public string SourceTag { get; set; } = "";
    public string OwnerName { get; set; } = "";

    /// <summary>
    /// Nom résolu de l'arme dans la langue actuelle (depuis w_name.bin).
    /// Null pour les Chimères, Seymour, ou les armures (pas de noms dans w_name.bin).
    /// </summary>
    public string? ResolvedName { get; set; }

    /// <summary>Si true, le nom résolu est intégré dans DisplayName (sinon affichage compact).</summary>
    public bool ShowName { get; set; } = true;

    public string DisplayName
    {
        get
        {
            var ownerTag = OwnerName.Length > 0 ? OwnerName[..Math.Min(3, OwnerName.Length)] : "?";
            var typeTag = Gear.IsArmor ? "Arm" : "Wpn";
            var srcTag = SourceTag.Substring(0, Math.Min(4, SourceTag.Length));
            var stats = $"P{Gear.Power} S{Gear.Slots}";

            if (ShowName && !string.IsNullOrWhiteSpace(ResolvedName))
                return $"[{srcTag} 0x{GlobalId:X4}] {ResolvedName} ({ownerTag} · {typeTag} · {stats})";

            return $"[{srcTag} 0x{GlobalId:X4}] {ownerTag} · {typeTag} · {stats}";
        }
    }
}

public class GearAbilitySlotItem
{
    public int Slot { get; set; }
    public string HexId { get; set; } = "";
    public int DecimalId { get; set; }
    public int SelectedId { get; set; }
    public string Status { get; set; } = "";
    public string ResolvedName { get; set; } = "";
    public string CategoryLabel { get; set; } = "";
    public List<GearAbilityOption> Options { get; set; } = new();
}

public class GearAbilityOption
{
    public int Id { get; }
    public string DisplayName { get; }

    public GearAbilityOption(int id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }
}

public class RecipeItemOption
{
    public int Id { get; }
    public string DisplayName { get; }

    public RecipeItemOption(int id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }
}

/// <summary>Entrée de la sidebar Objets.</summary>
public class ItemListItem
{
    public AttackFile File { get; set; } = null!;
    public AttackData Attack { get; set; } = null!;
    public int RelativeIndex { get; set; }
    public int GlobalId { get; set; }
    public string Name { get; set; } = "";
    public string DisplayName => $"[0x{GlobalId:X4}] {Name}";
}

public class AttackListItem
{
    public AttackFile File { get; set; } = null!;
    public AttackData Attack { get; set; } = null!;
    public int RelativeIndex { get; set; }
    public int GlobalId { get; set; }
    public string SourceTag { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayName => $"[{SourceTag} 0x{GlobalId:X4}] {Name}";
}

/// <summary>Entrée de la sidebar Commandes joueurs / Chimères.</summary>
public class CommandListItem
{
    public AttackFile File { get; set; } = null!;
    public AttackData Attack { get; set; } = null!;
    public int RelativeIndex { get; set; }
    public int GlobalId { get; set; }
    public string Name { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public AttackOwnership Ownership { get; set; }

    /// <summary>Tag visuel devant le nom : T pour Tidus, Y pour Yuna, etc.</summary>
    public string OwnerTag => OwnerName.Length > 0 ? OwnerName[..Math.Min(3, OwnerName.Length)] : "?";

    public string DisplayName => $"[{OwnerTag}] {Name}";
}

public class MonsterListItem
{
    public MonsterFileEntry Entry { get; }
    public string FileName => Entry.FileName;
    public string? DecodedName { get; set; }
    public string DisplayName => DecodedName != null ? $"{FileName} — {DecodedName}" : FileName;

    public MonsterListItem(MonsterFileEntry entry) => Entry = entry;
}

public class CommandSlotItem
{
    public int Slot { get; set; }
    public string HexId { get; set; } = "";
    public int DecimalId { get; set; }
    public string Status { get; set; } = "";
    public string Name { get; set; } = "";
    public string SourceLabel { get; set; } = "";
}

/// <summary>Entrée du sélecteur de langue.</summary>
public class LanguageOption
{
    public string DisplayName { get; }
    public string? Code { get; }
    public LanguageOption(string displayName, string? code)
    {
        DisplayName = displayName;
        Code = code;
    }
    public override string ToString() => DisplayName;
}
