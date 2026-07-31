using System.Text;
using System.Globalization;

namespace SpiraModifier.Core.Text;

/// <summary>
/// Charset FFX : table de correspondance char ↔ valeur d'encodage Karifean.
///
/// Format du fichier <c>ffxsjistbl_<lang>.bin</c> : succession brute des caractères en UTF-8.
/// Karifean ne stocke pas la position brute <c>i</c> : il ajoute <c>0x30</c>
/// au chargement. Cette valeur est ensuite utilisée telle quelle par
/// <c>StringHelper.charToBytes</c> et <c>StringHelper.byteToChar</c>.
///
/// Source : <c>StringHelper.charToBytes</c> du parser de Karifean.
/// </summary>
public class FfxCharset
{
    /// <summary>Code de la charset (us, jp, ch, kr).</summary>
    public string Code { get; }

    /// <summary>Map valeur d'encodage Karifean → caractère lisible.</summary>
    private readonly Dictionary<int, char> _valueToChar = new();

    /// <summary>Map caractère lisible → valeur d'encodage Karifean.</summary>
    private readonly Dictionary<char, int> _charToValue = new();

    /// <summary>Caractères réellement présents dans ffxsjistbl_*.bin, sans les alias de saisie.</summary>
    private readonly HashSet<char> _tableChars = new();

    public FfxCharset(string code)
    {
        Code = code;
    }

    public bool IsCjk =>
        Code.Equals("jp", StringComparison.OrdinalIgnoreCase)
        || Code.Equals("ch", StringComparison.OrdinalIgnoreCase)
        || Code.Equals("kr", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Charge une charset depuis un fichier ffxsjistbl_*.bin (lu en UTF-8).
    /// Le caractère à la position <c>i</c> du fichier a la valeur FFX <c>i + 0x30</c>.
    /// </summary>
    public static FfxCharset LoadFromFile(string path, string code)
    {
        var bytes = File.ReadAllBytes(path);
        var content = Encoding.UTF8.GetString(bytes);
        var charset = new FfxCharset(code);

        for (int i = 0; i < content.Length; i++)
        {
            var c = content[i];
            var value = i + 0x30;
            charset._valueToChar[value] = c;
            charset._tableChars.Add(c);
            if (!charset._charToValue.ContainsKey(c))
                charset._charToValue[c] = value;
        }

        charset.AddInputAliases();
        return charset;
    }

    private void AddInputAliases()
    {
        if (!IsCjk)
            return;

        AddAliasRange('0', '０', 10);
        AddAliasRange('A', 'Ａ', 26);
        AddAliasRange('a', 'ａ', 26);

        AddAlias(' ', '　');
        AddAlias('!', '！');
        AddAlias('%', '％');
        AddAlias('&', '＆');
        AddAlias('(', '（');
        AddAlias(')', '）');
        AddAlias('*', '＊');
        AddAlias('+', '＋');
        AddAlias(',', '，');
        AddAlias('-', '－');
        AddAlias('.', '．');
        AddAlias('/', '／');
        AddAlias(':', '：');
        AddAlias(';', '；');
        AddAlias('<', '＜');
        AddAlias('=', '＝');
        AddAlias('>', '＞');
        AddAlias('?', '？');
        AddAlias('[', '【');
        AddAlias(']', '】');
        AddAlias('~', '～');
        AddAlias('･', '・');
        AddAlias('ｰ', 'ー');
        AddAlias('−', '－');
        AddAlias('―', Code.Equals("jp", StringComparison.OrdinalIgnoreCase) ? 'ー' : '－');
        AddAlias('\'', '’');
        AddAlias('"', '”');
    }

    private void AddAliasRange(char aliasStart, char canonicalStart, int count)
    {
        for (int i = 0; i < count; i++)
            AddAlias((char)(aliasStart + i), (char)(canonicalStart + i));
    }

    private void AddAlias(char alias, char canonical)
    {
        if (_charToValue.ContainsKey(alias)) return;
        if (_charToValue.TryGetValue(canonical, out var value))
            _charToValue[alias] = value;
    }

    public string? GetInputSuggestion(char c)
        => GetInputSuggestions(c).FirstOrDefault();

    /// <summary>
    /// Propose des équivalences encodables et réellement présentes dans la table FFX
    /// de la charset courante. Les alias ASCII restent acceptés à la saisie, mais les
    /// suggestions affichées au joueur utilisent les caractères natifs de la table.
    /// </summary>
    public IReadOnlyList<string> GetInputSuggestions(char c)
    {
        if (!IsCjk)
            return Array.Empty<string>();

        var candidates = new List<string>();

        AddCandidate(candidates, ToCjkTableText(GetCjkWidthSuggestion(c)));
        AddCandidate(candidates, ToCjkTableText(GetLatinFallback(c)));

        foreach (var punctuation in GetPunctuationFallbacks(c))
            AddCandidate(candidates, punctuation);

        foreach (var equivalent in GetScriptEquivalentFallbacks(c))
            AddCandidate(candidates, equivalent);

        return candidates
            .Where(IsNativeTableText)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static void AddCandidate(List<string> candidates, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            candidates.Add(value);
    }

    private static string? GetCjkWidthSuggestion(char c)
    {
        if (c >= '0' && c <= '9') return ((char)('０' + (c - '0'))).ToString();
        if (c >= 'A' && c <= 'Z') return ((char)('Ａ' + (c - 'A'))).ToString();
        if (c >= 'a' && c <= 'z') return ((char)('ａ' + (c - 'a'))).ToString();

        return c switch
        {
            ' ' => "　",
            '!' => "！",
            '%' => "％",
            '&' => "＆",
            '(' => "（",
            ')' => "）",
            '*' => "＊",
            '+' => "＋",
            ',' => "，",
            '-' => "－",
            '.' => "．",
            '/' => "／",
            ':' => "：",
            ';' => "；",
            '<' => "＜",
            '=' => "＝",
            '>' => "＞",
            '?' => "？",
            '[' => "【",
            ']' => "】",
            '~' => "～",
            '･' => "・",
            'ｰ' => "ー",
            '−' => "－",
            '―' => "－",
            '\'' => "’",
            '"' => "”",
            _ => null,
        };
    }

    private string? ToCjkTableText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (_tableChars.Contains(c))
            {
                builder.Append(c);
                continue;
            }

            var width = GetCjkWidthSuggestion(c);
            if (!string.IsNullOrWhiteSpace(width) && IsNativeTableText(width))
            {
                builder.Append(width);
                continue;
            }

            return null;
        }
        return builder.ToString();
    }

    private bool IsNativeTableText(string text)
        => text.Length > 0 && text.All(_tableChars.Contains);

    private static string? GetLatinFallback(char c)
    {
        var direct = c switch
        {
            'æ' => "ae",
            'Æ' => "AE",
            'œ' => "oe",
            'Œ' => "OE",
            'ß' => "ss",
            'ø' => "o",
            'Ø' => "O",
            'ð' => "d",
            'Ð' => "D",
            'þ' => "th",
            'Þ' => "TH",
            _ => null,
        };
        if (direct != null) return direct;

        var normalized = c.ToString().Normalize(NormalizationForm.FormD);
        var stripped = new string(normalized
            .Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            .ToArray())
            .Normalize(NormalizationForm.FormC);

        return stripped.Length > 0 && stripped[0] != c ? stripped : null;
    }

    private IEnumerable<string> GetPunctuationFallbacks(char c)
    {
        var candidates = c switch
        {
            '‘' or '’' or '`' or '´' or '\'' => new[] { "’", "＇", "「", "」" },
            '“' or '”' or '"' => new[] { "”", "「", "」", "『", "』" },
            '—' or '–' or '−' or '‐' or '‑' or '-' => new[] { "－", "ー" },
            '•' or '·' or '･' => new[] { "・" },
            '…' => new[] { "…" },
            ',' => new[] { "，", "、" },
            '，' => new[] { "，", "、" },
            '、' => new[] { "、", "，" },
            '.' => new[] { "．", "。" },
            '。' => new[] { "。", "．" },
            '[' => new[] { "【", "「", "『" },
            ']' => new[] { "】", "」", "』" },
            '{' => new[] { "【", "「", "『" },
            '}' => new[] { "】", "」", "』" },
            _ => Array.Empty<string>(),
        };

        return candidates.Where(IsNativeTableText);
    }

    private IEnumerable<string> GetScriptEquivalentFallbacks(char c)
    {
        var candidates = Code.ToLowerInvariant() switch
        {
            "jp" => GetJapaneseEquivalentFallbacks(c),
            "ch" => GetChineseEquivalentFallbacks(c),
            "kr" => GetKoreanEquivalentFallbacks(c),
            _ => Array.Empty<string>(),
        };

        return candidates.Where(IsNativeTableText);
    }

    private static IEnumerable<string> GetJapaneseEquivalentFallbacks(char c)
        => c switch
        {
            '福' => new[] { "ふく" },
            '恵' or '惠' => new[] { "めぐ", "けい" },
            '喜' => new[] { "よろこび", "き" },
            '祝' => new[] { "いわい", "しゅく" },
            '恩' => new[] { "おん" },
            '龍' or '竜' => new[] { "りゅう" },
            '剣' or '劍' => new[] { "けん" },
            '闇' => new[] { "やみ" },
            '光' => new[] { "ひかり" },
            '炎' => new[] { "ほのお" },
            '氷' => new[] { "こおり" },
            '雷' => new[] { "かみなり" },
            '水' => new[] { "みず" },
            '力' => new[] { "ちから" },
            '魔' => new[] { "ま" },
            '召' => new[] { "しょう" },
            '喚' => new[] { "かん" },
            _ => Array.Empty<string>(),
        };

    private static IEnumerable<string> GetChineseEquivalentFallbacks(char c)
        => c switch
        {
            '龍' or '龙' or '竜' => new[] { "龍", "龙" },
            '劍' or '剑' or '剣' => new[] { "劍", "剑" },
            '體' or '体' => new[] { "體", "体" },
            '國' or '国' => new[] { "國", "国" },
            '寶' or '宝' => new[] { "寶", "宝" },
            '術' or '术' => new[] { "術", "术" },
            '氣' or '气' => new[] { "氣", "气" },
            '戰' or '战' => new[] { "戰", "战" },
            '鬥' or '斗' => new[] { "鬥", "斗" },
            '萬' or '万' => new[] { "萬", "万" },
            '風' or '风' => new[] { "風", "风" },
            '雲' or '云' => new[] { "雲", "云" },
            '電' or '电' => new[] { "電", "电" },
            '廣' or '广' => new[] { "廣", "广" },
            '關' or '关' => new[] { "關", "关" },
            '開' or '开' => new[] { "開", "开" },
            '門' or '门' => new[] { "門", "门" },
            '無' or '无' => new[] { "無", "无" },
            '靈' or '灵' => new[] { "靈", "灵" },
            '聖' or '圣' => new[] { "聖", "圣" },
            '惡' or '恶' => new[] { "惡", "恶" },
            '樂' or '乐' => new[] { "樂", "乐" },
            '勃' => new[] { "博", "波", "布" },
            '拚' => new[] { "拼" },
            '劲' => new[] { "勁" },
            '惠' => new[] { "慧", "恵" },
            '恵' => new[] { "慧", "惠" },
            _ => Array.Empty<string>(),
        };

    private static IEnumerable<string> GetKoreanEquivalentFallbacks(char c)
        => c switch
        {
            '福' => new[] { "복" },
            '恵' or '惠' => new[] { "혜" },
            '喜' => new[] { "희" },
            '祝' => new[] { "축" },
            '恩' => new[] { "은" },
            '龍' or '竜' => new[] { "용" },
            '劍' or '剣' => new[] { "검" },
            '闇' => new[] { "암" },
            '光' => new[] { "빛", "광" },
            '炎' => new[] { "염" },
            '氷' => new[] { "빙" },
            '雷' => new[] { "뢰" },
            '水' => new[] { "수" },
            '力' => new[] { "힘", "력" },
            '魔' => new[] { "마" },
            '召' => new[] { "소" },
            '喚' => new[] { "환" },
            _ => Array.Empty<string>(),
        };

    /// <summary>Récupère le caractère correspondant à une valeur d'encodage FFX, ou null si inconnue.</summary>
    public char? IndexToChar(int indexValue)
    {
        return _valueToChar.TryGetValue(indexValue, out var c) ? c : null;
    }

    /// <summary>Récupère la valeur d'encodage FFX correspondant à un caractère, ou null si inconnue.</summary>
    public int? CharToIndex(char c)
    {
        return _charToValue.TryGetValue(c, out var value) ? value : null;
    }

    // === Compat avec l'ancienne API (utilisée par d'anciens points) ===
    // ByteToChar(b) renvoie le char pour la valeur Karifean b.
    // Le décodeur principal n'utilise plus ces méthodes, mais on les garde pour
    // un fallback éventuel des champs Name simples (WeaponName, etc.).

    /// <summary>
    /// Compat : lookup direct par octet (0x30+). Pour les caractères 1-byte uniquement.
    /// Si <paramref name="byteValue"/> &lt; 0x30, retourne null.
    /// </summary>
    public char? ByteToChar(int byteValue)
    {
        return IndexToChar(byteValue);
    }

    /// <summary>
    /// Compat : lookup direct par char retournant un byte 1-byte (0x30+).
    /// Si le caractère nécessite plusieurs octets (accent), retourne null.
    /// </summary>
    public int? CharToByte(char c)
    {
        var idx = CharToIndex(c);
        if (idx == null) return null;
        if (idx.Value < 0x100) return idx.Value;
        return null; // multi-byte, à gérer par l'encodeur
    }

    /// <summary>True si tous les caractères de la chaîne sont encodables.</summary>
    public bool CanEncode(string s) => FindUnsupportedChars(s).Count == 0;

    /// <summary>
    /// Retourne la liste des caractères de la chaîne non supportés.
    /// Les tokens {...} sont sautés (gérés par l'encodeur).
    /// Les retours à la ligne \n sont supportés (token implicite).
    /// </summary>
    public IReadOnlyList<char> FindUnsupportedChars(string s)
    {
        var bad = new List<char>();
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            // Retours à la ligne : supportés en interne
            if (c == '\n' || c == '\r') continue;
            // Tokens : gérés par l'encodeur
            if (c == '{')
            {
                var end = s.IndexOf('}', i + 1);
                if (end > i)
                {
                    i = end;
                    continue;
                }
            }
            if (!_charToValue.ContainsKey(c) && !bad.Contains(c))
                bad.Add(c);
        }
        return bad;
    }
}
