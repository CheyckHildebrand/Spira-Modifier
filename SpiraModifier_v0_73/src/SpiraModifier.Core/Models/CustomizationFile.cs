using SpiraModifier.Core.BinaryIO;

namespace SpiraModifier.Core.Models;

/// <summary>
/// Cible d'une recette de customisation (octet 0x00 d'une entrée kaizou.bin).
/// </summary>
public enum CustomizationTarget
{
    Unknown = 0x00,
    Weapon  = 0x01,
    Armor   = 0x02,
    Aeon    = 0x7F,
}

/// <summary>
/// Une entrée de kaizou.bin = recette pour customiser une aptitude sur un équipement.
///
/// Layout (8 octets) — d'après CustomizationDataObject.java du parser de Karifean :
///   0x00-0x01 : customizeTargetByte (0x01 = Arme, 0x02 = Armure, 0x7F = Chimère)
///   0x02-0x03 : customizedAbility   (ID de l'aptitude — référence a_ability.bin)
///   0x04-0x05 : requiredItemType    (ID de l'objet requis — plage 0x2000+ vers item.bin)
///   0x06      : requiredItemQuantity (quantité d'objets demandée)
///   0x07      : requiredItemQuantityBase (quantité de base pour les boosts de stat)
///
/// Le champ customizedAbility peut être :
///   - un ID d'aptitude (bit 0xF000 != 0)
///   - un index de stat 0..9 (HP/MP/Force/Défense/Magie/Mag.Déf/Vitesse/Chance/Esquive/Précis)
///     → dans ce cas la quantité représente l'amplitude du boost
/// </summary>
public class CustomizationEntry
{
    public const int LENGTH = 0x08;

    public CustomizationTarget Target { get; set; }
    public int RawTargetByte { get; set; }

    /// <summary>ID brut tel que stocké (peut être un ability ID ou un stat index).</summary>
    public int CustomizedAbility { get; set; }

    /// <summary>ID de l'objet requis (référence item.bin via la plage 0x2000+).</summary>
    public int RequiredItemId { get; set; }

    /// <summary>Quantité d'objets requise.</summary>
    public int Quantity { get; set; }

    /// <summary>Quantité "de base" (utilisée pour les boosts de stat dans le format vanilla).</summary>
    public int QuantityBase { get; set; }

    /// <summary>True si CustomizedAbility est un vrai ID d'aptitude (bit 0xF000 mis),
    /// false si c'est un index de stat 0..9.</summary>
    public bool IsAbilityCustomization => (CustomizedAbility & 0xF000) != 0;

    public static CustomizationEntry ReadFromBytes(byte[] bytes, int offset)
    {
        if (bytes.Length < offset + LENGTH)
            throw new ArgumentException(
                $"Buffer trop petit pour CustomizationEntry : {bytes.Length - offset} dispo, {LENGTH} requis.");

        var raw = BytesHelper.Read2Bytes(bytes, offset + 0x00);
        return new CustomizationEntry
        {
            RawTargetByte     = raw,
            Target            = raw switch
            {
                0x01 => CustomizationTarget.Weapon,
                0x02 => CustomizationTarget.Armor,
                0x7F => CustomizationTarget.Aeon,
                _    => CustomizationTarget.Unknown,
            },
            CustomizedAbility = BytesHelper.Read2Bytes(bytes, offset + 0x02),
            RequiredItemId    = BytesHelper.Read2Bytes(bytes, offset + 0x04),
            Quantity          = bytes[offset + 0x06],
            QuantityBase      = bytes[offset + 0x07],
        };
    }

    public byte[] WriteToBytes()
    {
        var bytes = new byte[LENGTH];
        var rawTarget = Target switch
        {
            CustomizationTarget.Weapon => 0x01,
            CustomizationTarget.Armor  => 0x02,
            CustomizationTarget.Aeon   => 0x7F,
            _ => RawTargetByte,
        };
        RawTargetByte = rawTarget;

        BytesHelper.Write2Bytes(bytes, 0x00, rawTarget);
        BytesHelper.Write2Bytes(bytes, 0x02, CustomizedAbility);
        BytesHelper.Write2Bytes(bytes, 0x04, RequiredItemId);
        bytes[0x06] = (byte)(Quantity & 0xFF);
        bytes[0x07] = (byte)(QuantityBase & 0xFF);
        return bytes;
    }
}

/// <summary>
/// Conteneur du fichier kaizou.bin = toutes les recettes de customisation des
/// équipements joueurs (armes + armures). Le fichier équivalent pour les
/// Chimères s'appelle sum_grow.bin (même format, géré séparément si présent).
///
/// kaizou.bin n'est PAS localisé (données purement mécaniques, pas de texte).
/// Une seule copie dans originals/battle/kernel/.
///
/// Format DataFileReader standard :
///   0x00-0x07 : 8 octets de header opaque
///   0x08-0x09 : minIndex
///   0x0A-0x0B : maxIndex
///   0x0C-0x0D : individualLength (= 0x08)
///   0x0E-0x0F : totalLength
///   0x10-0x13 : 4 octets skippés
///   0x14+     : entrées
///
/// Source : CustomizationDataObject.java + DataFileReader.java du parser.
/// </summary>
public class CustomizationFile
{
    private readonly List<CustomizationEntry> _entries = new();
    public IReadOnlyList<CustomizationEntry> Entries => _entries;
    public int Count => _entries.Count;
    public bool IsDirty { get; private set; }

    private byte[] _headerBytes = Array.Empty<byte>();
    private int _rawMinIndex;
    private int _rawMaxIndex;
    private int _individualLength = CustomizationEntry.LENGTH;

    /// <summary>Index par AbilityId → recette pour ARMES (Target=Weapon).</summary>
    private readonly Dictionary<int, CustomizationEntry> _weaponRecipes = new();

    /// <summary>Index par AbilityId → recette pour ARMURES (Target=Armor).</summary>
    private readonly Dictionary<int, CustomizationEntry> _armorRecipes = new();

    /// <summary>Index par AbilityId → recette pour CHIMÈRES (Target=Aeon).</summary>
    private readonly Dictionary<int, CustomizationEntry> _aeonRecipes = new();

    public static CustomizationFile ReadFromFile(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return ReadFromBytes(bytes);
    }

    public static CustomizationFile ReadFromBytes(byte[] bytes)
    {
        if (bytes.Length < 0x14)
            throw new InvalidDataException(
                $"Fichier kaizou.bin trop petit ({bytes.Length} octets).");

        var file = new CustomizationFile();
        file._headerBytes = new byte[0x14];
        Array.Copy(bytes, 0, file._headerBytes, 0, 0x14);
        file._rawMinIndex = BytesHelper.Read2Bytes(bytes, 0x08);
        file._rawMaxIndex = BytesHelper.Read2Bytes(bytes, 0x0A);
        var indivLength = BytesHelper.Read2Bytes(bytes, 0x0C);
        var totalLength = BytesHelper.Read2Bytes(bytes, 0x0E);
        if (indivLength == 0) indivLength = CustomizationEntry.LENGTH;
        file._individualLength = indivLength;

        var entryCount = totalLength / indivLength;
        var entriesStart = 0x14;

        for (int i = 0; i < entryCount; i++)
        {
            var off = entriesStart + i * indivLength;
            if (off + indivLength > bytes.Length) break;
            try
            {
                var entry = CustomizationEntry.ReadFromBytes(bytes, off);
                file._entries.Add(entry);

                // On indexe uniquement les recettes liées à une aptitude (pas les boosts de stat),
                // c'est ce dont on a besoin pour l'onglet Aptitudes.
                if (!entry.IsAbilityCustomization) continue;

                // L'aptitude peut être stockée avec le bit 0x8000 mis (flag "active") ;
                // on masque pour avoir un ID stable comparable à AutoAbilityFile.GetByGlobalId.
                var key = entry.CustomizedAbility & 0x7FFF;

                switch (entry.Target)
                {
                    case CustomizationTarget.Weapon: file._weaponRecipes[key] = entry; break;
                    case CustomizationTarget.Armor:  file._armorRecipes[key]  = entry; break;
                    case CustomizationTarget.Aeon:   file._aeonRecipes[key]   = entry; break;
                }
            }
            catch { }
        }

        return file;
    }

    /// <summary>Recette pour greffer cette aptitude sur une ARME (null si inexistante).</summary>
    public CustomizationEntry? GetWeaponRecipe(int abilityGlobalId)
        => _weaponRecipes.TryGetValue(abilityGlobalId & 0x7FFF, out var e) ? e : null;

    /// <summary>Recette pour greffer cette aptitude sur une ARMURE (null si inexistante).</summary>
    public CustomizationEntry? GetArmorRecipe(int abilityGlobalId)
        => _armorRecipes.TryGetValue(abilityGlobalId & 0x7FFF, out var e) ? e : null;

    /// <summary>Recette pour greffer cette aptitude sur une CHIMÈRE (null si inexistante).</summary>
    public CustomizationEntry? GetAeonRecipe(int abilityGlobalId)
        => _aeonRecipes.TryGetValue(abilityGlobalId & 0x7FFF, out var e) ? e : null;

    /// <summary>True si cette aptitude est customisable sur au moins une arme dans le jeu.</summary>
    public bool IsWeaponAbility(int abilityGlobalId)
        => _weaponRecipes.ContainsKey(abilityGlobalId & 0x7FFF);

    /// <summary>True si cette aptitude est customisable sur au moins une armure dans le jeu.</summary>
    public bool IsArmorAbility(int abilityGlobalId)
        => _armorRecipes.ContainsKey(abilityGlobalId & 0x7FFF);

    public bool UpdateRecipe(CustomizationTarget target, int abilityGlobalId, int requiredItemId, int quantity)
    {
        var recipe = target switch
        {
            CustomizationTarget.Weapon => GetWeaponRecipe(abilityGlobalId),
            CustomizationTarget.Armor => GetArmorRecipe(abilityGlobalId),
            CustomizationTarget.Aeon => GetAeonRecipe(abilityGlobalId),
            _ => null,
        };
        if (recipe == null) return false;

        recipe.RequiredItemId = requiredItemId;
        recipe.Quantity = quantity;
        if (recipe.QuantityBase == 0)
            recipe.QuantityBase = quantity;

        IsDirty = true;
        return true;
    }

    public byte[] WriteToBytes()
    {
        if (_individualLength == 0) _individualLength = CustomizationEntry.LENGTH;

        var totalLength = _entries.Count * _individualLength;
        var output = new byte[0x14 + totalLength];

        if (_headerBytes.Length >= 0x14)
            Array.Copy(_headerBytes, 0, output, 0, 0x14);
        else
            output[0x00] = 0x01;

        BytesHelper.Write2Bytes(output, 0x08, _rawMinIndex);
        BytesHelper.Write2Bytes(output, 0x0A, _rawMaxIndex);
        BytesHelper.Write2Bytes(output, 0x0C, _individualLength);
        BytesHelper.Write2Bytes(output, 0x0E, totalLength);
        output[0x10] = 0x14;

        var cursor = 0x14;
        foreach (var entry in _entries)
        {
            var entryBytes = entry.WriteToBytes();
            Array.Copy(entryBytes, 0, output, cursor, Math.Min(_individualLength, entryBytes.Length));
            cursor += _individualLength;
        }

        return output;
    }

    public void MarkDirty() => IsDirty = true;
    public void MarkClean() => IsDirty = false;
}
