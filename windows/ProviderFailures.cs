namespace SwirStreamAI;

// This diagnostic contains categories only. Never derive display text from an
// exception message, a server response, a source URL, or account credentials.
public record ProviderFailureInfo(string Stage,string Code);

public static class ProviderFailures
{
    const string StageKey="SwirStreamAI.ProviderFailure.Stage";
    static string Stage(string? value)=>value is "portal_handshake" or "portal_profile" or "portal_channels" or "portal_link" or "configuration"?value:"unknown";
    static string Code(string? value)=>value is "access_denied" or "invalid_response" or "network" or "configuration"?value:"unknown";
    public static Exception Tag(Exception exception,string stage)
    {
        if(exception is not OperationCanceledException&&Stage(exception.Data[StageKey] as string)=="unknown")
            exception.Data[StageKey]=Stage(stage);
        return exception;
    }
    public static ProviderFailureInfo FromException(Exception exception)
    {
        string stage=Stage(exception.Data[StageKey] as string);
        string code=stage=="configuration"?"configuration":exception switch
        {
            UnauthorizedAccessException=>"access_denied",
            InvalidDataException or System.Text.Json.JsonException or System.Xml.XmlException or FormatException=>"invalid_response",
            IOException or HttpRequestException or System.Net.Sockets.SocketException or TimeoutException or System.Security.Authentication.AuthenticationException=>"network",
            ArgumentException or InvalidOperationException=>"configuration",
            _=>"unknown"
        };
        return new(stage,code);
    }
    internal static ProviderFailureInfo? Normalize(ProviderFailureInfo? failure)=>failure==null?null:new(Stage(failure.Stage),Code(failure.Code));
    public static string Describe(ProviderFailureInfo failure)
    {
        string step=Stage(failure.Stage) switch
        {
            "portal_handshake"=>"Etap: połączenie z portalem. ",
            "portal_profile"=>"Etap: potwierdzenie profilu urządzenia. ",
            "portal_channels"=>"Etap: pobieranie listy kanałów. ",
            "portal_link"=>"Etap: uzyskanie adresu odtwarzania. ",
            "configuration"=>"Etap: sprawdzenie ustawień źródła. ",
            _=>""
        };
        string reason=(Stage(failure.Stage),Code(failure.Code)) switch
        {
            ("portal_profile","access_denied")=>"Portal nie potwierdził aktywnego profilu urządzenia. Sprawdź przypisanie i uprawnienia u operatora. Nie uruchomiono dalszych etapów tego połączenia.",
            (_,"access_denied")=>"Źródło odrzuciło dostęp. Sprawdź własne konto lub przypisanie urządzenia u operatora.",
            (_,"invalid_response")=>"Źródło zwróciło nieprawidłową lub nieobsługiwaną odpowiedź.",
            (_,"network")=>"Nie udało się połączyć ze źródłem. Sprawdź sieć, adres i certyfikat serwera lub spróbuj później.",
            (_,"configuration")=>"Sprawdź ustawienia własnego źródła i odśwież jego listę kanałów.",
            _=>"Nie udało się wykonać operacji na własnym źródle."
        };
        return step+reason;
    }
}
