using System.Text;

namespace SwirStreamAI;

public enum PortalScanStatus { NotChecked,Checking,PublicPage,RecognizedPortal,Catalog,Denied,Timeout,Failed,UnsupportedPublicCheck,Cancelled }
public record PortalScanRow(PortalTxtEntry Entry,PortalScanStatus Status,int Channels=0,bool Limited=false,DateTimeOffset? CheckedAt=null)
{
    public string Description=>Status switch
    {
        PortalScanStatus.Checking=>"Sprawdzanie…",
        PortalScanStatus.PublicPage=>"Strona odpowiada • portal niepotwierdzony",
        PortalScanStatus.RecognizedPortal=>"Rozpoznano portal • dostęp do kanałów niesprawdzony",
        PortalScanStatus.Catalog=>"Pobrano kanały • odtwarzanie niesprawdzone"+(Limited?" • limit listy":""),
        PortalScanStatus.Denied=>"Odmowa dostępu",
        PortalScanStatus.Timeout=>"Przekroczono czas oczekiwania",
        PortalScanStatus.UnsupportedPublicCheck=>"Adres API lub port poza 80/443 • użyj własnego MAC",
        PortalScanStatus.Failed=>"Nie udało się potwierdzić źródła",
        PortalScanStatus.Cancelled=>"Przerwano sprawdzanie",
        _=>"Jeszcze niesprawdzony"
    };
    public override string ToString()=>Entry.Label+": "+Description;
}
public record PortalScanResult(IReadOnlyList<PortalScanRow> Rows,bool Complete);

public sealed class PortalScanner
{
    readonly PrivateProviderClient client;
    readonly Func<string,CancellationToken,Task<WebDocument>> page;
    readonly TimeSpan entryTimeout,gap,totalTimeout;
    public PortalScanner(PrivateProviderClient? client=null,Func<string,CancellationToken,Task<WebDocument>>? page=null,TimeSpan? entryTimeout=null,TimeSpan? gap=null,TimeSpan? totalTimeout=null)
    {
        this.client=client??new();this.page=page??FetchPage;
        this.entryTimeout=entryTimeout??TimeSpan.FromSeconds(30);this.gap=gap??TimeSpan.FromMilliseconds(800);this.totalTimeout=totalTimeout??TimeSpan.FromMinutes(10);
    }
    static bool PublicPageUrl(string url){try{PortalTxt.Url(url);return PublicWeb.Allowed(url);}catch{return false;}}
    static Task<WebDocument> FetchPage(string url,CancellationToken ct)
    {
        var origin=new Uri(url);
        return PublicWeb.FetchChecked(url,ct,value=>PublicPageUrl(value)&&SourceExplorerRules.SafeManualPage(value)&&new Uri(value) is var next&&next.Host==origin.Host&&((next.Port==origin.Port&&next.Scheme==origin.Scheme)||(origin.Scheme=="http"&&origin.Port==80&&next.Scheme=="https"&&next.Port==443)));
    }
    public async Task<PortalScanRow> Check(PortalTxtEntry entry,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Validate again even for callers other than the TXT UI. No request can
        // acquire credentials from a page or infer another account identifier.
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(entryTimeout);
        try
        {
            string url=PortalTxt.Url(entry.Url);
            if(!entry.HasMac)
            {
                if(!PublicWeb.Allowed(url)||!SourceExplorerRules.SafeManualPage(url))return new(entry,PortalScanStatus.UnsupportedPublicCheck,CheckedAt:DateTimeOffset.UtcNow);
                var document=await page(url,deadline.Token).WaitAsync(deadline.Token);ct.ThrowIfCancellationRequested();
                return new(entry,SourceExplorerRules.PortalClient(document)?PortalScanStatus.RecognizedPortal:PortalScanStatus.PublicPage,CheckedAt:DateTimeOffset.UtcNow);
            }
            var source=entry.Source();PrivateProviderClient.Validate(source);
            var catalogue=await client.Refresh(source,deadline.Token).WaitAsync(deadline.Token);ct.ThrowIfCancellationRequested();
            // Only counts survive. Never accumulate every account's channel list.
            return new(entry,PortalScanStatus.Catalog,catalogue.Channels.Count,catalogue.Notice.Contains("Ograniczono",StringComparison.OrdinalIgnoreCase)||catalogue.Notice.Contains("limit",StringComparison.OrdinalIgnoreCase),DateTimeOffset.UtcNow);
        }
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){return new(entry,PortalScanStatus.Timeout,CheckedAt:DateTimeOffset.UtcNow);}
        catch(UnauthorizedAccessException){return new(entry,PortalScanStatus.Denied,CheckedAt:DateTimeOffset.UtcNow);}
        catch(Exception error)when(error is not OperationCanceledException){return new(entry,PortalScanStatus.Failed,CheckedAt:DateTimeOffset.UtcNow);}
    }
    public async Task<PortalScanResult> Scan(IReadOnlyList<PortalTxtEntry> entries,CancellationToken ct,Action<PortalScanRow>? progress=null)
    {
        if(entries.Count>PortalTxt.MaxEntries)throw new InvalidDataException("Lista przekracza 200 wpisów.");
        var rows=entries.Select(e=>new PortalScanRow(e,PortalScanStatus.NotChecked)).ToArray();
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct);lifetime.CancelAfter(totalTimeout);
        for(int i=0;i<entries.Count;i++)
        {
            bool started=false;
            try
            {
                lifetime.Token.ThrowIfCancellationRequested();if(i>0)await Task.Delay(gap,lifetime.Token);
                started=true;
                progress?.Invoke(new(entries[i],PortalScanStatus.Checking));
                var result=await Check(entries[i],lifetime.Token);lifetime.Token.ThrowIfCancellationRequested();rows[i]=result;progress?.Invoke(result);
            }
            catch(OperationCanceledException)
            {
                if(started){rows[i]=new(entries[i],PortalScanStatus.Cancelled);progress?.Invoke(rows[i]);}return new(rows,false);
            }
        }
        return new(rows,true);
    }
    public static string Export(IEnumerable<PortalScanRow> rows)
    {
        var text=new StringBuilder("Pozycja;Wiersz_TXT;Rodzaj;Stan;Kanaly;Limit_listy;Sprawdzono_UTC;Odtwarzanie\r\n");
        foreach(var row in rows)text.Append(row.Entry.Number).Append(';').Append(row.Entry.Line).Append(';').Append(row.Entry.HasMac?"Wlasny_MAC":"Publiczny_adres").Append(';').Append(row.Status).Append(';').Append(row.Channels).Append(';').Append(row.Limited?"tak":"nie").Append(';').Append(row.CheckedAt?.ToUniversalTime().ToString("O")??"").Append(";niesprawdzone\r\n");
        return text.ToString();
    }
}
