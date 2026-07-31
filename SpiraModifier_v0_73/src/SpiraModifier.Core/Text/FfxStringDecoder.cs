using System.Text;

namespace SpiraModifier.Core.Text;

/// <summary>
/// Décodeur de chaînes FFX qui PRÉSERVE tous les codes de contrôle sous forme de tokens
/// échappés (convention Karifean) et gère correctement les caractères étendus (accents).
///
/// Format des octets décodés :
///   0x00       → fin de chaîne
///   0x01       → {PAUSE}
///   0x03       → {\n}
///   0x04       → drapeau "extra five sections" : ajoute 0x410 aux indices suivants
///                (pour les caractères accentués dans les charsets larges FR/DE/IT)
///   0x07 X     → {SPACE:NN}
///   0x09 X     → {TIME:NN}
///   0x0A X     → {CLR:COLOR}
///   0x0B X     → {CTRL:NN}
///   0x10 X     → {CHOICE:NN} ou {CHOICE-END} si X=0xFF
///   0x12 X     → {VAR:NN}
///   0x13 X     → {PC:NN} si X ≤ 0x43, sinon {MCR:s00lXX}
///   0x14..0x22 → {MCR:sNNlNN}
///   0x23 X     → {KEY:NN}
///   0x2B..0x2F X → caractère double-byte = (section * 0xD0 + X) → valeur charset
///   ≥ 0x30     → caractère direct, valeur charset = b
///   autres (0x27/0x28/0x2A et inconnus) → {CMD:XX:NN}
/// </summary>
public static class FfxStringDecoder
{
    public static string Decode(byte[] buffer, int offset, FfxCharset? charset)
    {
        if (charset == null || buffer.Length == 0 || offset < 0 || offset >= buffer.Length)
            return string.Empty;

        var sb = new StringBuilder();
        var i = offset;
        bool extraFiveSections = false; // flag activé par 0x04

        while (i < buffer.Length)
        {
            var b = buffer[i];
            if (b == 0x00) break;

            // Décalage actif si 0x04 a été vu juste avant
            var extraOffset = extraFiveSections ? 0x410 : 0;
            extraFiveSections = false;

            // Caractères 1-byte (valeur = b, plus éventuel offset 0x410)
            if (b >= 0x30)
            {
                var idx = b + extraOffset;
                var c = charset.IndexToChar(idx);
                if (c != null) sb.Append(c.Value);
                else sb.Append(extraOffset != 0
                    ? $"{{UNKDBLCHR:04:{b:X2}}}"
                    : $"{{UNKCHR:{b:X2}}}");
                i++;
                continue;
            }

            // Caractères double-byte (sections 0x2B..0x2F)
            if (b >= 0x2B && b <= 0x2F)
            {
                if (i + 1 >= buffer.Length) break;
                var lowByte = buffer[i + 1];
                var section = b - 0x2B;
                var idx = section * 0xD0 + lowByte + extraOffset;
                var c = charset.IndexToChar(idx);
                if (c != null) sb.Append(c.Value);
                else sb.Append(extraOffset != 0
                    ? $"{{UNKTPLCHR:04:{b:X2}:{lowByte:X2}}}"
                    : $"{{UNKDBLCHR:{b:X2}:{lowByte:X2}}}");
                i += 2;
                continue;
            }

            // Après 0x04, Karifean tente aussi de décoder les valeurs < 0x2B
            // comme caractères étendus avant de retomber sur les commandes.
            if (extraOffset != 0)
            {
                var c = charset.IndexToChar(b + extraOffset);
                if (c != null) sb.Append(c.Value);
                else sb.Append($"{{UNKDBLCHR:04:{b:X2}}}");
                i++;
                continue;
            }

            // 0x04 : drapeau pour la suite
            if (b == 0x04)
            {
                extraFiveSections = true;
                i++;
                continue;
            }

            // Codes de contrôle restants
            switch (b)
            {
                case 0x01:
                    sb.Append("{PAUSE}"); i++; break;

                case 0x03:
                    sb.Append("{\\n}"); i++; break;

                case 0x07:
                    if (i + 1 < buffer.Length)
                    {
                        sb.Append($"{{SPACE:{(buffer[i + 1] - 0x30):X2}}}");
                        i += 2;
                    }
                    else i++;
                    break;

                case 0x09:
                    if (i + 1 < buffer.Length)
                    {
                        sb.Append($"{{TIME:{(buffer[i + 1] - 0x30):X2}}}");
                        i += 2;
                    }
                    else i++;
                    break;

                case 0x0A:
                    if (i + 1 < buffer.Length)
                    {
                        sb.Append($"{{CLR:{FfxColors.ByteToName(buffer[i + 1])}}}");
                        i += 2;
                    }
                    else i++;
                    break;

                case 0x0B:
                    if (i + 1 < buffer.Length)
                    {
                        sb.Append($"{{CTRL:{buffer[i + 1]:X2}}}");
                        i += 2;
                    }
                    else i++;
                    break;

                case 0x10:
                    if (i + 1 < buffer.Length)
                    {
                        var raw = buffer[i + 1];
                        if (raw == 0xFF) sb.Append("{CHOICE-END}");
                        else sb.Append($"{{CHOICE:{(raw - 0x30):X2}}}");
                        i += 2;
                    }
                    else i++;
                    break;

                case 0x12:
                    if (i + 1 < buffer.Length)
                    {
                        sb.Append($"{{VAR:{(buffer[i + 1] - 0x30):X2}}}");
                        i += 2;
                    }
                    else i++;
                    break;

                case 0x13:
                    if (i + 1 < buffer.Length && buffer[i + 1] <= 0x43)
                    {
                        sb.Append($"{{PC:{(buffer[i + 1] - 0x30):X2}}}");
                        i += 2;
                    }
                    else if (i + 1 < buffer.Length)
                    {
                        sb.Append($"{{MCR:s00l{(buffer[i + 1] - 0x30):X2}}}");
                        i += 2;
                    }
                    else i++;
                    break;

                case 0x23:
                    if (i + 1 < buffer.Length)
                    {
                        sb.Append($"{{KEY:{buffer[i + 1]:X2}}}");
                        i += 2;
                    }
                    else i++;
                    break;

                default:
                    if (b >= 0x14 && b <= 0x22 && i + 1 < buffer.Length)
                    {
                        var section = b - 0x13;
                        var line = buffer[i + 1] - 0x30;
                        sb.Append($"{{MCR:s{section:X2}l{line:X2}}}");
                        i += 2;
                    }
                    else if (i + 1 < buffer.Length)
                    {
                        sb.Append($"{{CMD:{b:X2}:{(buffer[i + 1] - 0x30):X2}}}");
                        i += 2;
                    }
                    else i++;
                    break;
            }
        }

        return sb.ToString();
    }
}

/// <summary>Table des couleurs FFX.</summary>
public static class FfxColors
{
    public static string ByteToName(int b) => b switch
    {
        0x41 => "WHITE",
        0x43 => "YELLOW",
        0x52 => "GREY",
        0x88 => "BLUE",
        0x94 => "RED",
        0x97 => "PINK",
        0xA1 => "OL_PURPLE",
        0xB1 => "OL_CYAN",
        _    => $"{b:X2}",
    };

    public static int NameToByte(string name)
    {
        return name.ToUpperInvariant() switch
        {
            "WHITE"     => 0x41,
            "YELLOW"    => 0x43,
            "GREY"      => 0x52,
            "BLUE"      => 0x88,
            "RED"       => 0x94,
            "PINK"      => 0x97,
            "OL_PURPLE" => 0xA1,
            "OL_CYAN"   => 0xB1,
            _ => int.TryParse(name, System.Globalization.NumberStyles.HexNumber, null, out var v)
                 ? v : 0x41,
        };
    }

    public static readonly string[] NamedColors =
        { "WHITE", "YELLOW", "GREY", "BLUE", "RED", "PINK", "OL_PURPLE", "OL_CYAN" };
}
