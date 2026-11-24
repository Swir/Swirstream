using System.Globalization;
using System.Text.Json;

namespace SwirStreamAI;

internal static class UiLanguage
{
    static readonly Lazy<Dictionary<string,string>> English = new(() =>
    {
        using var stream = typeof(UiLanguage).Assembly.GetManifestResourceStream("SwirStreamAI.interface.en.json")
            ?? throw new InvalidDataException("Missing English interface resource.");
        return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
    });
    internal static string Preference { get; private set; } = "system";
    internal static bool IsEnglish { get; private set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "pl";
    static string settingsFile = "";
    internal static void Load(string dataFolder)
    {
        settingsFile = Path.Combine(dataFolder,"ui-language.txt");
        string value = "system";
        try { if (File.Exists(settingsFile)) value = File.ReadAllText(settingsFile).Trim(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        Preference = value is "en" or "pl" ? value : "system";
        IsEnglish = Preference == "en" || Preference == "system" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "pl";
    }
    internal static void Save(string value)
    {
        if (value is not ("pl" or "en" or "system")) throw new ArgumentException(nameof(value));
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
        string temp = settingsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp,value); File.Move(temp,settingsFile,true); Preference=value; }
        finally { File.Delete(temp); }
    }
    internal static string T(string label) => IsEnglish ? English.Value.GetValueOrDefault(label,label) : label;
    internal static string[] InitialCountries(string region)
    {
        region = WorldMarkets.Canonical(region.ToUpperInvariant());
        return region.Length==2 && WorldMarkets.Name(region)!=region ? [region] : [];
    }
    internal static string[] InitialCountries()
    {
        try { return InitialCountries(RegionInfo.CurrentRegion.TwoLetterISORegionName); }
        catch(ArgumentException) { return []; }
    }
}
