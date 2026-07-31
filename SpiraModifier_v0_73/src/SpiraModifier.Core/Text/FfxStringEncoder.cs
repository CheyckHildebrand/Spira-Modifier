using System.Globalization;

namespace SpiraModifier.Core.Text;

/// <summary>
/// Encodeur de chaînes FFX. Reproduit exactement l'algorithme du parser de Karifean
/// (StringHelper.charToBytes et StringHelper.fillByteList).
///
/// Algorithme de codage d'un caractère :
///   1. Récupère la valeur FFX du caractère via la charset (position + 0x30).
///   2. Si valeur &lt; 0x100 : 1 octet = valeur.
///   3. Si valeur ≥ 0x100 : on calcule la "section".
///      section = 0x2B
///      Tant que index ≥ 0x100 : section++ ; index -= 0xD0
///      Si section ≥ 0x30 : émettre 0x04 (et si section ≥ 0x31, émettre section - 0x05)
///      Sinon : émettre section directement (sera dans 0x2B..0x2F)
///   4. Émettre le byte final (= index modifié).
///
/// Source : <c>StringHelper.charToBytes</c> du parser de Karifean.
/// </summary>
public static class FfxStringEncoder
{
    public record EncodeResult(byte[] Bytes, IReadOnlyList<char> UnsupportedChars);

    /// <summary>Encode une chaîne (tokens + caractères) en octets FFX terminés par 0x00.</summary>
    public static EncodeResult EncodeWithReport(string text, FfxCharset charset)
    {
        var output = new List<byte>(text.Length + 1);
        var bad = new List<char>();

        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];

            // Retour à la ligne physique → token implicite
            if (c == '\n')   { output.Add(0x03); continue; }
            if (c == '\r')   { continue; }

            // Détection des tokens { ... }
            if (c == '{')
            {
                var end = text.IndexOf('}', i + 1);
                if (end > i)
                {
                    var inner = text.Substring(i + 1, end - i - 1);
                    if (TryEncodeToken(inner, output))
                    {
                        i = end;
                        continue;
                    }
                    // Token non reconnu → fallback en char normal
                }
            }

            // Encode le caractère via la charset (1 à 3 octets)
            var charBytes = CharToBytes(c, charset);
            if (charBytes != null)
            {
                output.AddRange(charBytes);
            }
            else if (!bad.Contains(c))
            {
                bad.Add(c);
            }
        }

        output.Add(0x00); // terminateur
        return new EncodeResult(output.ToArray(), bad);
    }

    public static byte[] Encode(string text, FfxCharset charset)
        => EncodeWithReport(text, charset).Bytes;

    /// <summary>
    /// Encode un caractère unique en 1 à 3 octets selon l'algorithme Karifean.
    /// Retourne null si le caractère n'existe pas dans la charset.
    /// </summary>
    public static List<byte>? CharToBytes(char c, FfxCharset charset)
    {
        var indexNullable = charset.CharToIndex(c);
        if (indexNullable == null) return null;

        var index = indexNullable.Value;
        var bytes = new List<byte>(3);

        // Copie directe de l'algorithme Karifean : index contient déjà le +0x30
        // appliqué au chargement de la table charset.
        if (index >= 0x100)
        {
            var section = 0x2B;
            do
            {
                section++;
                index -= 0xD0;
            } while (index >= 0x100);

            if (section >= 0x30)
            {
                bytes.Add(0x04);
                if (section >= 0x31)
                {
                    bytes.Add((byte)(section - 0x05));
                }
            }
            else
            {
                bytes.Add((byte)section);
            }
        }

        bytes.Add((byte)index);
        return bytes;
    }

    /// <summary>Essaie d'encoder un token (sans accolades). Retourne true si reconnu.</summary>
    private static bool TryEncodeToken(string token, List<byte> output)
    {
        if (token.Length == 0) return false;

        if (token == "\\n")        { output.Add(0x03); return true; }
        if (token == "PAUSE")      { output.Add(0x01); return true; }
        if (token == "CHOICE-END") { output.Add(0x10); output.Add(0xFF); return true; }

        if (token.StartsWith("CLR:"))    { output.Add(0x0A); output.Add((byte)FfxColors.NameToByte(token[4..])); return true; }
        if (token.StartsWith("COLOR:"))  { output.Add(0x0A); output.Add((byte)FfxColors.NameToByte(token[6..])); return true; }
        if (token.StartsWith("SPACE:"))  return TryAdd2(token[6..], 0x07, 0x30, output);
        if (token.StartsWith("TIME:"))   return TryAdd2(token[5..], 0x09, 0x30, output);
        if (token.StartsWith("CTRL:"))   return TryAdd2(token[5..], 0x0B, 0x00, output);
        if (token.StartsWith("CHOICE:")) return TryAdd2(token[7..], 0x10, 0x30, output);
        if (token.StartsWith("VAR:"))    return TryAdd2(token[4..], 0x12, 0x30, output);
        if (token.StartsWith("PC:"))     return TryAdd2(token[3..], 0x13, 0x30, output);
        if (token.StartsWith("KEY:"))    return TryAdd2(token[4..], 0x23, 0x00, output);

        if (token.StartsWith("MCR:s") && token.Contains('l'))
        {
            var lIdx = token.IndexOf('l', 5);
            if (TryParseHex(token.AsSpan()[5..lIdx], out var section)
                && TryParseHex(token.AsSpan()[(lIdx + 1)..], out var line))
            {
                output.Add((byte)(section + 0x13));
                output.Add((byte)(line + 0x30));
                return true;
            }
            return false;
        }

        if (token.StartsWith("CMD:"))
        {
            var parts = token[4..].Split(':');
            if (parts.Length == 2
                && TryParseHex(parts[0], out var cmdByte)
                && TryParseHex(parts[1], out var arg))
            {
                output.Add((byte)cmdByte);
                output.Add((byte)(arg + 0x30));
                return true;
            }
            return false;
        }

        if (token.StartsWith("UNKCHR:"))
        {
            if (TryParseHex(token[7..], out var v)) { output.Add((byte)v); return true; }
            return false;
        }

        if (token.StartsWith("UNKDBLCHR:04:"))
        {
            // Format : {UNKDBLCHR:04:XX} = caractère étendu inconnu, après 0x04
            if (TryParseHex(token[13..], out var v))
            {
                output.Add(0x04);
                output.Add((byte)v);
                return true;
            }
            return false;
        }

        if (token.StartsWith("UNKDBLCHR:"))
        {
            var parts = token[10..].Split(':');
            if (parts.Length == 2
                && TryParseHex(parts[0], out var a)
                && TryParseHex(parts[1], out var b))
            {
                output.Add((byte)a);
                output.Add((byte)b);
                return true;
            }
            return false;
        }

        if (token.StartsWith("UNKTPLCHR:04:"))
        {
            // {UNKTPLCHR:04:XX:YY} = caractère étendu double-byte inconnu
            var parts = token[13..].Split(':');
            if (parts.Length == 2
                && TryParseHex(parts[0], out var a)
                && TryParseHex(parts[1], out var b))
            {
                output.Add(0x04);
                output.Add((byte)a);
                output.Add((byte)b);
                return true;
            }
            return false;
        }

        return false;
    }

    private static bool TryAdd2(string hex, byte prefix, int valueOffset, List<byte> output)
    {
        if (!TryParseHex(hex, out var v)) return false;
        output.Add(prefix);
        output.Add((byte)(v + valueOffset));
        return true;
    }

    private static bool TryParseHex(string s, out int value)
        => int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

    private static bool TryParseHex(ReadOnlySpan<char> s, out int value)
        => int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
}
