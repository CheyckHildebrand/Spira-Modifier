namespace SpiraModifier.Core.BinaryIO;

/// <summary>
/// Utilitaires de lecture / écriture binaire en little-endian.
///
/// FFX HD Remaster stocke ses données binaires en little-endian. Cette classe
/// fournit les primitives de base utilisées par tous les readers/writers du Core.
///
/// Note : on travaille avec des byte[] (octets non signés 0-255), ce qui est plus
/// idiomatique en C# que les int[] utilisés dans le parser Java de Karifean.
/// </summary>
public static class BytesHelper
{
    // ===== Lecture =====

    /// <summary>Lit 1 octet (0-255).</summary>
    public static int Read1Byte(byte[] bytes, int offset)
        => bytes[offset];

    /// <summary>Lit 2 octets en little-endian (0-65535).</summary>
    public static int Read2Bytes(byte[] bytes, int offset)
        => bytes[offset] | (bytes[offset + 1] << 8);

    /// <summary>Lit 4 octets en little-endian, retourné comme uint pour éviter la confusion de signe.</summary>
    public static uint Read4Bytes(byte[] bytes, int offset)
        => (uint)(bytes[offset]
               | (bytes[offset + 1] << 8)
               | (bytes[offset + 2] << 16)
               | (bytes[offset + 3] << 24));

    /// <summary>Lit 4 octets comme int signé (utilisé pour HP, MP, gold, etc.).</summary>
    public static int Read4BytesSigned(byte[] bytes, int offset)
        => (int)Read4Bytes(bytes, offset);

    /// <summary>Lit un float IEEE-754 little-endian (utilisé par les scripts ATEL).</summary>
    public static float ReadFloat(byte[] bytes, int offset)
        => BitConverter.ToSingle(bytes, offset);

    // ===== Écriture =====

    /// <summary>Écrit 1 octet à l'offset donné.</summary>
    public static void Write1Byte(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value & 0xFF);
    }

    /// <summary>Écrit 2 octets en little-endian.</summary>
    public static void Write2Bytes(byte[] bytes, int offset, int value)
    {
        bytes[offset]     = (byte)(value & 0xFF);
        bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    /// <summary>Écrit 4 octets en little-endian.</summary>
    public static void Write4Bytes(byte[] bytes, int offset, int value)
    {
        bytes[offset]     = (byte)(value & 0xFF);
        bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
        bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
        bytes[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    /// <summary>Écrit 4 octets en little-endian (version uint).</summary>
    public static void Write4Bytes(byte[] bytes, int offset, uint value)
        => Write4Bytes(bytes, offset, (int)value);

    /// <summary>Écrit un float IEEE-754 little-endian.</summary>
    public static void WriteFloat(byte[] bytes, int offset, float value)
    {
        var floatBytes = BitConverter.GetBytes(value);
        Array.Copy(floatBytes, 0, bytes, offset, 4);
    }

    // ===== Helpers d'alignement =====

    /// <summary>
    /// Arrondit une longueur à un multiple de l'alignement (utilisé par les chunks
    /// du fichier monstre, généralement alignés sur 4 ou 8 octets avec padding 0xFF).
    /// </summary>
    public static int PadLengthTo(int length, int alignment)
    {
        var remainder = length % alignment;
        return remainder == 0 ? length : length + (alignment - remainder);
    }
}
