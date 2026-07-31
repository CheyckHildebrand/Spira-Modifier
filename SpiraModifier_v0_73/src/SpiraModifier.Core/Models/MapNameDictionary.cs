namespace SpiraModifier.Core.Models;

/// <summary>
/// Dictionnaire des noms lisibles des zones (cartes) de FFX.
///
/// Les codes internes (ex : "cdsp00", "bsil03") correspondent à un préfixe de carte
/// + un index de sous-zone. Le préfixe est l'identifiant interne historique du jeu.
///
/// Source des codes : ScriptConstants.java du parser de Karifean (méthode putMaps).
/// Noms français : traduits par convention basée sur la localisation FFX HD.
/// </summary>
public static class MapNameDictionary
{
    /// <summary>
    /// Préfixe (4 lettres) → nom français lisible de la zone parente.
    /// Pour le nom complet d'une sous-zone, on ajoute « (zone N) » avec l'index extrait du code.
    /// </summary>
    private static readonly Dictionary<string, string> PrefixToName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["test"] = "Carte de test",
        ["znkd"] = "Zanarkand",
        ["bjyt"] = "Tour de Yu Yevon",                  // bjyt = Bevelle Yujyu Tower (intérieur fin)
        ["cdsp"] = "Salle des Épreuves",                // cdsp = Cloister of trials, Demi-SPhere
        ["bsil"] = "Île de Besaid",
        ["slik"] = "Île de Kilika",
        ["klyt"] = "Temple de Kilika",                  // klyt = Kilika Yujyu Temple
        ["lchb"] = "Luca",                              // lchb = Luca Champion Blitz
        ["mihn"] = "Route de Mi'ihen",
        ["kino"] = "Plaine Foudroyée",                  // kino = Kinokoiwa = Thunder Plains
        ["genk"] = "Désert de Bikanel",                 // genk = Genkaiya = sandstorm region
        ["kami"] = "Voyage de Guadosalam",              // kami = trail towards Farplane
        ["mcfr"] = "Bois de Macalania",                 // mcfr = MaCalania FoRest
        ["maca"] = "Macalania",                         // maca = Macalania Lake/Temple area
        ["mcyt"] = "Temple de Macalania",               // mcyt = MaCalania Yujyu Temple
        ["bika"] = "Bikanel",                           // bika = Bikanel Island town/Home
        ["azit"] = "Azito",                             // azit = Al Bhed Home (Azito = "base")
        ["hiku"] = "Aéronef Fahrenheit",                // hiku = Hikoutei = airship
        ["stbv"] = "Saint-Bevelle",
        ["bvyt"] = "Temple de Bevelle",                 // bvyt = BeVelle Yujyu Temple
        ["nagi"] = "Highbridge (Bevelle)",              // nagi = Nagigatake = pacified peak
        ["lmyt"] = "Temple de la Lune",                 // lmyt = LunarMonth Yujyu Temple
        ["mtgz"] = "Mont Gagazet",                      // mtgz = MounTainGAgaZet
        ["zkrn"] = "Zanarkand (Ruines)",                // zkrn = Zanarkand RuiNs
        ["dome"] = "Dôme de Zanarkand",                 // dome = ruin dome inside
        ["ssbt"] = "Sin (intérieur)",                   // ssbt = Sin extérieur / boat?
        ["sins"] = "Sin",                               // sins = Sin (intérieur final)
        ["omeg"] = "Donjon d'Oméga",                    // omeg = Omega Ruins
        ["zzzz"] = "Zone spéciale",                     // zzzz = placeholder/debug
        ["tori"] = "Arène de Monstres",                 // tori = Monster Arena
    };

    /// <summary>
    /// Convertit un code de carte (ex : "cdsp00", "bsil03") en nom lisible
    /// avec sous-zone (ex : "Salle des Épreuves – zone 0").
    /// Retourne le code brut si le préfixe est inconnu.
    /// </summary>
    public static string GetDisplayName(string mapCode)
    {
        if (string.IsNullOrWhiteSpace(mapCode)) return "(sans nom)";

        // Le code est de la forme {prefix4}{index:00} mais on tolère prefix3 / sans index
        var trimmed = mapCode.Trim();
        if (trimmed.Length < 4) return trimmed;

        var prefix = trimmed[..Math.Min(4, trimmed.Length)];
        if (!PrefixToName.TryGetValue(prefix, out var baseName))
            return mapCode;     // préfixe inconnu : on garde le code brut

        // Extrait l'index de sous-zone (les chiffres en suffixe)
        var suffix = trimmed.Length > 4 ? trimmed[4..] : "";
        if (int.TryParse(suffix, out var subIndex))
            return $"{baseName} – zone {subIndex}";

        return baseName;
    }

    /// <summary>
    /// Retourne le nom de la zone parente uniquement, sans index de sous-zone.
    /// Pratique pour regrouper visuellement les sous-zones d'une même région.
    /// </summary>
    public static string GetRegionName(string mapCode)
    {
        if (string.IsNullOrWhiteSpace(mapCode) || mapCode.Length < 4) return "(?)";
        var prefix = mapCode[..4];
        return PrefixToName.TryGetValue(prefix, out var name) ? name : prefix;
    }
}
