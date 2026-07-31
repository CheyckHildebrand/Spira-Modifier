using SpiraModifier.Core.BinaryIO;

namespace SpiraModifier.Core.Models;

/// <summary>
/// Conteneur d'un fichier monstre complet (battle/mon/mXXX/mXXX.bin).
///
/// Structure du fichier (header de 0x30 octets + 7 chunks à pointeurs dynamiques) :
///
///   Header (0x30 octets) :
///     0x00 : Signature / nombre de chunks (toujours 8 dans Monster_Structs.cs)
///     0x04 : Pointeur vers chunk AI (bytecode ATEL)
///     0x08 : Pointeur vers Worker mapping
///     0x0C : Pointeur vers StatSheet (0x80 octets fixes + textes localisés)
///     0x10 : Pointeur vers chunk "Spoils" / inconnu
///     0x14 : Pointeur vers Loot (0x118 octets fixes)
///     0x18 : Pointeur vers Audio
///     0x1C : Pointeur vers Texte (localisations)
///     0x20 : Taille totale du fichier
///     0x24-0x2F : Padding (16 octets) pour aligner le header
///
/// IMPORTANT pour le mode overhaul : les chunks ne sont PAS à taille fixe.
/// Leur position est calculée dynamiquement depuis le pointeur précédent
/// jusqu'au pointeur suivant. Cela permet d'allonger le bytecode AI sans
/// limite (ce que DV a exploité pour Der Richter et autres boss).
///
/// Pour l'instant, on traite AiBytes et WorkerBytes comme des byte[] bruts
/// (comme le fait FFXProjectEditor). Le décodage/recompilation ATEL viendra
/// dans un module dédié, qui modifiera AiBytes avant la sérialisation.
/// </summary>
public class MonsterFile
{
    /// <summary>Taille du header avant le premier chunk.</summary>
    public const int HEADER_SIZE = 0x30;

    /// <summary>Signature standard (nombre de chunks attendus).</summary>
    public const int DEFAULT_SIGNATURE = 8;

    // ===== Chunks bruts =====
    // Chacun peut être null si le fichier original ne contient pas ce chunk.

    /// <summary>Bytecode ATEL (l'IA du monstre). Modifiable via AtelDecompiler/Recompiler.</summary>
    public byte[]? AiBytes { get; set; }

    /// <summary>Mapping des workers (slots de batailles, types de workers).</summary>
    public byte[]? WorkerBytes { get; set; }

    /// <summary>Bloc StatSheet décodé (stats, résistances, commandes).</summary>
    public MonsterStat? StatSheet { get; set; }

    /// <summary>Octets bruts du chunk StatSheet (textes localisés inclus, après les 0x80 octets de stats).</summary>
    public byte[]? StatSheetTextBytes { get; set; }

    /// <summary>Chunk "Spoils" — usage inconnu, on le préserve à l'identique.</summary>
    public byte[]? SpoilsBytes { get; set; }

    /// <summary>Bloc de loot (drops, steal, bribe, gear). À décoder ultérieurement.</summary>
    public byte[]? LootBytes { get; set; }

    /// <summary>Bloc loot décodé (récompenses, drops, vol, pots-de-vin).</summary>
    public MonsterLoot? Loot { get; set; }

    /// <summary>Références audio (cris, sons d'attaque). Préservé tel quel.</summary>
    public byte[]? AudioBytes { get; set; }

    /// <summary>Textes localisés (anglais, japonais, etc.).</summary>
    public byte[]? TextBytes { get; set; }

    /// <summary>Index global du monstre (si connu). Permet de retrouver son nom.</summary>
    public int? MonsterIndex { get; set; }

    /// <summary>Nom de fichier d'origine (par exemple "m037" pour Anima).</summary>
    public string? ScriptId { get; set; }

    /// <summary>
    /// True si le fichier a été modifié en mémoire depuis le chargement.
    /// Mis automatiquement par les méthodes d'édition (<see cref="RebuildTextChunk"/>).
    /// </summary>
    public bool IsDirty { get; set; }

    // =========================================================================
    // LECTURE
    // =========================================================================

    /// <summary>
    /// Parse un fichier monstre complet depuis ses octets bruts.
    /// Détermine la taille de chaque chunk depuis les pointeurs successifs du header.
    /// </summary>
    public static MonsterFile Read(byte[] fileBytes, int? monsterIndex = null, string? scriptId = null)
    {
        if (fileBytes.Length < HEADER_SIZE)
            throw new InvalidDataException(
                $"Fichier trop petit pour être un monster.bin valide ({fileBytes.Length} octets).");

        var file = new MonsterFile { MonsterIndex = monsterIndex, ScriptId = scriptId };

        // Lecture du header
        var aiPtr      = BytesHelper.Read4BytesSigned(fileBytes, 0x04);
        var workerPtr  = BytesHelper.Read4BytesSigned(fileBytes, 0x08);
        var statPtr    = BytesHelper.Read4BytesSigned(fileBytes, 0x0C);
        var spoilsPtr  = BytesHelper.Read4BytesSigned(fileBytes, 0x10);
        var lootPtr    = BytesHelper.Read4BytesSigned(fileBytes, 0x14);
        var audioPtr   = BytesHelper.Read4BytesSigned(fileBytes, 0x18);
        var textPtr    = BytesHelper.Read4BytesSigned(fileBytes, 0x1C);
        var fileSize   = BytesHelper.Read4BytesSigned(fileBytes, 0x20);

        // Sanity check sur la taille
        if (fileSize > fileBytes.Length)
            throw new InvalidDataException(
                $"Header annonce {fileSize} octets mais le fichier en contient {fileBytes.Length}.");

        // Stratégie de lecture : on lit les chunks de la fin vers le début,
        // car c'est ce qui permet de déduire la taille de chaque chunk
        // depuis le pointeur du chunk suivant (ou la taille totale).
        // Cette logique est calquée sur Monster_File.Read d'Osdanova.

        var currentEof = fileSize;

        if (textPtr > 0)
        {
            file.TextBytes = ExtractChunk(fileBytes, textPtr, currentEof);
            currentEof = textPtr;
        }

        if (audioPtr > 0)
        {
            file.AudioBytes = ExtractChunk(fileBytes, audioPtr, currentEof);
            currentEof = audioPtr;
        }

        if (lootPtr > 0)
        {
            file.LootBytes = ExtractChunk(fileBytes, lootPtr, currentEof);
            if (file.LootBytes.Length >= MonsterLoot.LENGTH)
                file.Loot = MonsterLoot.Read(file.LootBytes);
            currentEof = lootPtr;
        }

        if (spoilsPtr > 0)
        {
            file.SpoilsBytes = ExtractChunk(fileBytes, spoilsPtr, currentEof);
            currentEof = spoilsPtr;
        }

        if (statPtr > 0)
        {
            var statChunkBytes = ExtractChunk(fileBytes, statPtr, currentEof);
            // Le chunk StatSheet contient le bloc 0x80 puis les chaînes texte.
            file.StatSheet = MonsterStat.ReadFromBytes(statChunkBytes, 0);
            if (statChunkBytes.Length > MonsterStat.LENGTH)
            {
                file.StatSheetTextBytes = new byte[statChunkBytes.Length - MonsterStat.LENGTH];
                Array.Copy(statChunkBytes, MonsterStat.LENGTH,
                           file.StatSheetTextBytes, 0,
                           file.StatSheetTextBytes.Length);
            }
            currentEof = statPtr;
        }

        if (workerPtr > 0)
        {
            file.WorkerBytes = ExtractChunk(fileBytes, workerPtr, currentEof);
            currentEof = workerPtr;
        }

        if (aiPtr > 0)
        {
            file.AiBytes = ExtractChunk(fileBytes, aiPtr, currentEof);
        }

        return file;
    }

    /// <summary>
    /// Extrait un chunk depuis un offset jusqu'à un offset de fin (exclu).
    /// </summary>
    private static byte[] ExtractChunk(byte[] source, int start, int end)
    {
        var length = end - start;
        if (length <= 0)
            return Array.Empty<byte>();

        var chunk = new byte[length];
        Array.Copy(source, start, chunk, 0, length);
        return chunk;
    }

    // =========================================================================
    // ÉCRITURE
    // =========================================================================

    /// <summary>
    /// Réassemble le fichier monstre complet à partir des chunks actuels.
    /// Recalcule tous les pointeurs du header — c'est ce qui permet d'avoir
    /// des chunks de taille variable (notamment AiBytes étendu).
    /// </summary>
    public byte[] Write()
    {
        // 1) Sérialiser le StatSheet décodé en concaténant le bloc 0x80 + textes
        byte[]? statChunk = null;
        if (StatSheet != null)
        {
            var statBytes = StatSheet.WriteToBytes();
            var textPart = StatSheetTextBytes ?? Array.Empty<byte>();
            statChunk = new byte[statBytes.Length + textPart.Length];
            Array.Copy(statBytes, 0, statChunk, 0, statBytes.Length);
            Array.Copy(textPart, 0, statChunk, statBytes.Length, textPart.Length);
        }

        // 2) Calculer les pointeurs successifs en partant après le header
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        // On écrit d'abord un header vide qu'on patchera à la fin.
        stream.Position = HEADER_SIZE;

        var aiPtr     = WriteChunkAndGetPtr(stream, writer, AiBytes);
        var workerPtr = WriteChunkAndGetPtr(stream, writer, WorkerBytes);
        var statPtr   = WriteChunkAndGetPtr(stream, writer, statChunk);
        var spoilsPtr = WriteChunkAndGetPtr(stream, writer, SpoilsBytes);
        var lootPtr   = WriteChunkAndGetPtr(stream, writer, Loot?.WriteToBytes() ?? LootBytes);
        var audioPtr  = WriteChunkAndGetPtr(stream, writer, AudioBytes);
        var textPtr   = WriteChunkAndGetPtr(stream, writer, TextBytes);

        var fileSize = (int)stream.Length;

        // 3) Patcher le header avec les vrais pointeurs
        var fullBytes = stream.ToArray();
        BytesHelper.Write4Bytes(fullBytes, 0x00, DEFAULT_SIGNATURE);
        BytesHelper.Write4Bytes(fullBytes, 0x04, aiPtr);
        BytesHelper.Write4Bytes(fullBytes, 0x08, workerPtr);
        BytesHelper.Write4Bytes(fullBytes, 0x0C, statPtr);
        BytesHelper.Write4Bytes(fullBytes, 0x10, spoilsPtr);
        BytesHelper.Write4Bytes(fullBytes, 0x14, lootPtr);
        BytesHelper.Write4Bytes(fullBytes, 0x18, audioPtr);
        BytesHelper.Write4Bytes(fullBytes, 0x1C, textPtr);
        BytesHelper.Write4Bytes(fullBytes, 0x20, fileSize);
        // 0x24-0x2F restent à zéro (padding)

        return fullBytes;
    }

    /// <summary>
    /// Écrit un chunk au stream actuel et retourne le pointeur (0 si chunk null/vide).
    /// </summary>
    private static int WriteChunkAndGetPtr(MemoryStream stream, BinaryWriter writer, byte[]? chunk)
    {
        if (chunk == null || chunk.Length == 0)
            return 0;

        var ptr = (int)stream.Position;
        writer.Write(chunk);
        return ptr;
    }
}

// ============================================================================
// Extensions pour le décodage des chaînes localisées
// ============================================================================

public static class MonsterFileTextExtensions
{
    /// <summary>
    /// Décode le nom du monstre depuis le chunk StatSheetTextBytes en utilisant la charset fournie.
    /// Retourne string.Empty si la charset n'est pas disponible ou si le décodage échoue.
    /// </summary>
    public static string DecodeName(this MonsterFile file, SpiraModifier.Core.Text.FfxCharset? charset)
    {
        if (file.StatSheet == null || file.StatSheetTextBytes == null || charset == null)
            return string.Empty;

        return SpiraModifier.Core.Text.FfxStringDecoder.Decode(
            file.StatSheetTextBytes,
            file.StatSheet.NameOffset,
            charset);
    }

    /// <summary>
    /// Reconstruit le chunk de textes du monstre (Name, Sensor, SensorCourt, Scan, ScanCourt)
    /// avec les nouvelles valeurs, met à jour les offsets dans le StatSheet, et marque
    /// le fichier comme modifié (<see cref="MonsterFile.IsDirty"/>).
    ///
    /// Chaque chaîne est encodée séquentiellement avec terminateur 0x00. Les offsets
    /// sont calculés à la volée pour pointer correctement dans le nouveau buffer.
    ///
    /// Si une chaîne est inchangée (null en entrée), on conserve la valeur décodée
    /// précédemment pour la réencoder à l'identique.
    /// </summary>
    /// <returns>True si le chunk a bien été reconstruit, false sinon (manque de données).</returns>
    public static bool RebuildTextChunk(
        this MonsterFile file,
        SpiraModifier.Core.Text.FfxCharset charset,
        string newName,
        string newSensor,
        string newSimplifiedSensor,
        string newScan,
        string newSimplifiedScan)
    {
        if (file.StatSheet == null || charset == null)
            return false;

        // Encode les 5 chaînes (chacune se termine par 0x00)
        var nameBytes        = SpiraModifier.Core.Text.FfxStringEncoder.Encode(newName, charset);
        var sensorBytes      = SpiraModifier.Core.Text.FfxStringEncoder.Encode(newSensor, charset);
        var sensorShortBytes = SpiraModifier.Core.Text.FfxStringEncoder.Encode(newSimplifiedSensor, charset);
        var scanBytes        = SpiraModifier.Core.Text.FfxStringEncoder.Encode(newScan, charset);
        var scanShortBytes   = SpiraModifier.Core.Text.FfxStringEncoder.Encode(newSimplifiedScan, charset);

        // Concatène le tout en gardant trace des offsets de début de chaque chaîne
        var total = new List<byte>(nameBytes.Length + sensorBytes.Length + sensorShortBytes.Length
                                   + scanBytes.Length + scanShortBytes.Length);

        var nameOff       = total.Count; total.AddRange(nameBytes);
        var sensorOff     = total.Count; total.AddRange(sensorBytes);
        var sensorShortOff= total.Count; total.AddRange(sensorShortBytes);
        var scanOff       = total.Count; total.AddRange(scanBytes);
        var scanShortOff  = total.Count; total.AddRange(scanShortBytes);

        // Met à jour les offsets dans le StatSheet
        file.StatSheet.NameOffset                 = nameOff;
        file.StatSheet.SensorTextOffset           = sensorOff;
        file.StatSheet.SimplifiedSensorTextOffset = sensorShortOff;
        file.StatSheet.ScanTextOffset             = scanOff;
        file.StatSheet.SimplifiedScanTextOffset   = scanShortOff;

        // Remplace le buffer texte (peut grossir ou rétrécir — pas de souci, on
        // sérialise dynamiquement la taille du chunk dans MonsterFile.Write())
        file.StatSheetTextBytes = total.ToArray();
        file.IsDirty = true;

        return true;
    }
}
