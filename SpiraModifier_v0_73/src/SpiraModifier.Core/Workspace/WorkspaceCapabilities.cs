namespace SpiraModifier.Core.Workspace;

/// <summary>
/// Drapeaux indiquant quels types de contenu ont été détectés dans le dossier ouvert.
/// Chaque module de l'UI peut s'activer ou se désactiver selon ces capacités.
/// </summary>
[Flags]
public enum WorkspaceCapabilities
{
    None           = 0,
    Monsters       = 1 << 0,  // battle/mon/m*.bin trouvés
    KernelCommands = 1 << 1,  // battle/kernel/command.bin
    KernelMonMagic = 1 << 2,  // battle/kernel/monmagic1.bin et/ou monmagic2.bin
    KernelItems    = 1 << 3,  // battle/kernel/item.bin
    LocalizedTexts = 1 << 4,  // dossiers new_XXpc/ avec leurs *_txt.bin
    PlayerStartData = 1 << 5, // battle/kernel/ply_save.bin
    MapTreasures = 1 << 6,    // battle/kernel/takara.bin
}
