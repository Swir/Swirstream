namespace SwirStreamAI;

internal static class WorldMarkets
{
    static readonly Lazy<Dictionary<string, string[]>> Markets = new(() =>
    {
        using var stream = typeof(WorldMarkets).Assembly.GetManifestResourceStream("SwirStreamAI.world-search.tsv")
            ?? throw new InvalidDataException("Brak indeksu języków wyszukiwania.");
        using var reader = new StreamReader(stream);
        var rows = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split('\t');
            rows.Add(fields[0], fields);
        }
        return rows;
    });

    internal static string Canonical(string code) => code == "UK" ? "GB" : code;
    internal static string TvCatalogCode(string code) => code == "GB" ? "uk" : code.ToLowerInvariant();
    internal static string Name(string code) => Markets.Value.GetValueOrDefault(Canonical(code))?[1] ?? code;
    internal static IEnumerable<string> Terms(string code, bool radio)
    {
        if (Markets.Value.TryGetValue(Canonical(code), out var fields))
            for (int i = radio ? 3 : 2; i < fields.Length; i += 2) yield return fields[i];
        else yield return radio ? "live radio" : "live TV";
    }
}
