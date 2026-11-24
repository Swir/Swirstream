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
    readonly TimeSpan gap,totalTimeout;
    public PortalScanner(PrivateProviderClient? client=null,Func<string,CancellationToken,Task<WebDocument>>? page=null,TimeSpan? entryTimeout=null,TimeSpan? gap=null,TimeSpan? totalTimeout=null)
    {
        this.gap=gap??TimeSpan.FromMilliseconds(800);this.totalTimeout=totalTimeout??TimeSpan.FromMinutes(10);
    }
    public Task<PortalScanRow> Check(PortalTxtEntry entry,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new PortalScanRow(entry,PortalScanStatus.NotChecked));
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
