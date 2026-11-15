using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwirStreamAI;

internal enum PortalProtocolMode { DirectMac, FullProfile }
internal sealed record PortalConnection(string Endpoint,PortalProtocolMode Mode,string Token="");
internal enum PortalProbeKind { DirectMac, AuthenticationRequired, NotPortal, Denied }

internal static class PortalProtocol
{
    // A public client compatibility string, not an invented device identity.
    internal const string UserAgent="Mozilla/5.0 (QtEmbedded; U; Linux; C) AppleWebKit/533.3 (KHTML, like Gecko) MAG250";
    internal static PortalProbeKind Classify(ProviderDocument document)
    {
        if(document.Status is 401 or 403)return PortalProbeKind.AuthenticationRequired;
        if(document.Status==404)return PortalProbeKind.NotPortal;
        if(document.Status is <200 or >=300)return PortalProbeKind.NotPortal;
        string body=document.Text.Trim('\uFEFF',' ','\t','\r','\n');
        if(ExplicitBlock(body))return PortalProbeKind.Denied;
        if(AuthRequired(body))return PortalProbeKind.AuthenticationRequired;
        try
        {
            using var documentJson=JsonDocument.Parse(body);var root=documentJson.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)return PortalProbeKind.NotPortal;
            if(HasDenial(root))return PortalProbeKind.Denied;
            if(!root.TryGetProperty("js",out var js))return PortalProbeKind.NotPortal;
            if(js.ValueKind==JsonValueKind.String&&ExplicitBlock(js.GetString()!))return PortalProbeKind.Denied;
            if(js.ValueKind==JsonValueKind.Object&&HasDenial(js))return PortalProbeKind.Denied;
            if(AuthRequired(root)||AuthRequired(js))return PortalProbeKind.AuthenticationRequired;
            if(HasError(root))return PortalProbeKind.NotPortal;
            if(js.ValueKind==JsonValueKind.Object)
            {
                if(HasDenial(js))return PortalProbeKind.Denied;
                if(HasError(js))return PortalProbeKind.NotPortal;
                if(!js.TryGetProperty("data",out var data))return PortalProbeKind.NotPortal;
                js=data;
            }
            if(js.ValueKind!=JsonValueKind.Array||js.GetArrayLength()==0)return PortalProbeKind.NotPortal;
            foreach(var item in js.EnumerateArray())
            {
                if(item.ValueKind!=JsonValueKind.Object)return PortalProbeKind.NotPortal;
                if(HasDenial(item))return PortalProbeKind.Denied;
                if(HasError(item))return PortalProbeKind.NotPortal;
                if(!item.TryGetProperty("id",out var id)||id.ValueKind is not(JsonValueKind.String or JsonValueKind.Number)||string.IsNullOrWhiteSpace(id.ToString())||
                    !item.TryGetProperty("title",out var title)||title.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(title.GetString()))return PortalProbeKind.NotPortal;
            }
            return PortalProbeKind.DirectMac;
        }
        catch(JsonException){return PortalProbeKind.NotPortal;}
    }
    static bool HasDenial(JsonElement value)
    {
        foreach(string key in new[]{"blocked","status"})
            if(value.TryGetProperty(key,out var flag)&&!Inactive(flag))return true;
        if(value.TryGetProperty("auth",out var auth)&&!(auth.ValueKind==JsonValueKind.True||auth.ValueKind==JsonValueKind.Number&&auth.GetRawText()=="1"||auth.ValueKind==JsonValueKind.String&&auth.GetString()!.Trim().ToLowerInvariant() is "1" or "true"))return true;
        foreach(string key in new[]{"error","msg","message","block_msg"})
            if(value.TryGetProperty(key,out var message)&&message.ValueKind==JsonValueKind.String&&(ExplicitBlock(message.GetString()!)||Regex.IsMatch(message.GetString()!.Trim(),@"^access denied[.!]*$",RegexOptions.IgnoreCase)))return true;
        return false;
    }
    static bool ExplicitBlock(string value)=>Regex.IsMatch(value.Trim(),@"^(?:account (?:blocked|disabled|expired)|device blocked|blocked|banned)[.!]*$",RegexOptions.IgnoreCase);
    internal static bool CatalogAccessDeniedMessage(string value)=>ExplicitBlock(value)||AuthRequired(value);
    static bool AuthRequired(string value)=>Regex.IsMatch(value.Trim(),@"^(?:(?:authorization|authentication)\s+(?:failed|required)[.!]*(?:\s+[0-9]+)?|access denied[.!]*|not authorized[.!]*|unauthorized(?:\s+request)?[.!]*|invalid token[.!]*)$",RegexOptions.IgnoreCase);
    static bool AuthRequired(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.String)return AuthRequired(value.GetString()!);
        if(value.ValueKind!=JsonValueKind.Object)return false;
        return new[]{"error","msg","message"}.Any(key=>value.TryGetProperty(key,out var text)&&text.ValueKind==JsonValueKind.String&&AuthRequired(text.GetString()!));
    }
    static bool HasError(JsonElement value)=>new[]{"error","errors"}.Any(key=>value.TryGetProperty(key,out var flag)&&!Inactive(flag));
    static bool Inactive(JsonElement value)=>value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.False=>true,
        JsonValueKind.Number=>value.GetRawText()=="0",
        JsonValueKind.String=>value.GetString()!.Trim().ToLowerInvariant() is "" or "0" or "false" or "ok" or "success",
        JsonValueKind.Array=>value.GetArrayLength()==0,
        _=>false
    };
}
