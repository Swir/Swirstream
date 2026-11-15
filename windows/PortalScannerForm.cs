using System.Text;

namespace SwirStreamAI;

public sealed partial class MainForm
{
    PortalScanner portalScanner=new();
    internal async Task OpenScannerEntry(PortalTxtEntry entry,PrivateSourceStore repository,bool sample)
    {
        if(closing||!ReferenceEquals(repository,privateSources))return;
        ProviderSource source=entry.Source();
        bool Same(ProviderState value){try{return value.Source.Kind==PrivateSourceKind.Portal&&PortalTxt.Identity(value.Source.Url,value.Source.Mac)==PortalTxt.Identity(entry.Url,entry.Mac);}catch(InvalidDataException){return false;}}
        var existing=repository.Sources.FirstOrDefault(Same);
        if(existing!=null)source=source with{Id=existing.Source.Id,Name=existing.Source.Name};
        if(!entry.HasMac)
        {
            var edited=EditOwnSource(source,PrivateSourceKind.Portal);
            if(edited==null||closing||!ReferenceEquals(repository,privateSources))return;source=edited;
        }
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            // Scanning is read-only. This separate, explicit Add/Play action uses
            // the normal protected, transactional store and a fresh catalogue.
            bool loaded=await SaveConnectSource(source,timeout.Token,message=>status.Text=message);
            if(!loaded||closing||!ReferenceEquals(repository,privateSources))return;
            if(sample)
            {
                var station=repository.Stations(source.Id).FirstOrDefault(s=>!s.Name.TrimStart().StartsWith('#')&&!s.Name.Contains("XXX",StringComparison.OrdinalIgnoreCase));
                if(station==null){status.Text="Pobrano listę. Wybierz kanał do próby odtwarzania.";return;}
                Play(station); // Existing player reports actual decoded A/V, not HTTP success.
            }
        }
        catch(OperationCanceledException){if(!closing&&ReferenceEquals(repository,privateSources))status.Text="Przerwano dodawanie źródła. Wcześniejsze wpisy pozostały.";}
        catch(Exception){if(!closing&&ReferenceEquals(repository,privateSources))status.Text="Nie zapisano źródła. Sprawdź dostęp i limit 12 własnych źródeł w profilu.";}
    }
    async Task ShowPortalScanner(PortalTxtImport? initial=null,Func<CancellationToken,Task<PortalTxtImport>>? importFixture=null)
    {
        var repository=privateSources;string profile=profiles.Index.ActiveId;var engine=portalScanner;
        bool Current()=>!closing&&ReferenceEquals(repository,privateSources)&&profiles.Index.ActiveId==profile;
        using var lifetime=new CancellationTokenSource();CancellationTokenSource? operation=null;Task? running=null;
        PortalTxtImport? imported=initial;var rows=initial?.Entries.Select(e=>new PortalScanRow(e,PortalScanStatus.NotChecked)).ToList()??[];
        PortalTxtEntry? chosen=null;bool sample=false,explore=false;
        using var dialog=new Form{Text="Skaner portali TXT • SwirStream AI",Name="portal-scanner",ClientSize=new(1060,710),MinimumSize=new(960,680),StartPosition=FormStartPosition.CenterParent,BackColor=Bg,ForeColor=Color.White,Font=Font};
        using var profileWatch=new System.Windows.Forms.Timer{Interval=200};
        profileWatch.Tick+=(_,_)=>{if(!Current()){operation?.Cancel();dialog.Close();}};
        dialog.FormClosing+=(_,_)=>{lifetime.Cancel();operation?.Cancel();profileWatch.Stop();};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new(18),ColumnCount=1,RowCount=6};
        foreach(float height in new[]{42f,92f,-1f,76f,108f,42f})layout.RowStyles.Add(new(height<0?SizeType.Percent:SizeType.Absolute,height<0?100:height));
        layout.Controls.Add(Label("SKANER PORTALI • TXT",20,Cyan,true),0,0);
        layout.Controls.Add(Label("TXT: adres URL, adres|MAC albo adres i własne MAC w następnych wierszach.\nDo 1 MiB / 200 wpisów; pojedyncza próba do 30 s, cały skan do 10 min. Konto nie jest zapisywane podczas skanu.\nPubliczny adres nie oznacza darmowego dostępu. Odtwarzanie sprawdzisz osobnym przyciskiem w Swir Playerze.",10,Muted),0,1);
        var list=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,MultiSelect=false,HideSelection=false,BackColor=Card,ForeColor=Color.White,BorderStyle=BorderStyle.None,Name="portal-scanner-results"};
        foreach(var column in new[]{("Pozycja",95),("Wiersz TXT",85),("Rodzaj",110),("Kanały",75),("Wynik",485)})list.Columns.Add(column.Item1,column.Item2);layout.Controls.Add(list,0,2);
        var detail=Label("Adresy i MAC pozostają ukryte. Raport zawiera tylko numery pozycji i wyniki.",10,Muted);detail.Name="portal-scanner-details";layout.Controls.Add(detail,0,3);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};layout.Controls.Add(actions,0,4);
        var message=Label("Importuj TXT, sprawdź podgląd i uruchom skan.",9,Muted);message.Name="portal-scanner-status";layout.Controls.Add(message,0,5);
        PortalScanRow? Selected()=>list.SelectedItems.Count==1?list.SelectedItems[0].Tag as PortalScanRow:null;
        Button? scan=null,add=null,play=null,load=null,web=null,export=null;
        var cancel=Button("Przerwij",()=>{operation?.Cancel();return Task.CompletedTask;},width:100);cancel.Name="portal-scanner-cancel";cancel.Enabled=false;
        void Describe()
        {
            var row=Selected();bool idle=operation==null&&Current();
            add!.Enabled=idle&&row!=null&&(!row.Entry.HasMac||row.Status==PortalScanStatus.Catalog);
            play!.Enabled=idle&&row?.Status==PortalScanStatus.Catalog;
            detail.Text=row==null?"Adresy i MAC pozostają ukryte. Raport zawiera tylko numery pozycji i wyniki.":row.Entry.Label+" • wiersz "+row.Entry.Line+" • "+row.Description+"\nOdtwarzanie: niesprawdzone w skanerze. Próbka otwiera się w głównym playerze, który sprawdza rzeczywisty obraz i dźwięk.";
        }
        void Fill()
        {
            int? selected=Selected()?.Entry.Number;list.BeginUpdate();list.Items.Clear();
            foreach(var row in rows){var item=new ListViewItem(row.Entry.Label){Tag=row};item.SubItems.AddRange([row.Entry.Line.ToString(),row.Entry.HasMac?"Własny MAC":"Publiczny URL",row.Channels==0?"—":row.Channels.ToString(),row.Description]);list.Items.Add(item);}
            if(list.Items.Count>0)(list.Items.Cast<ListViewItem>().FirstOrDefault(x=>((PortalScanRow)x.Tag!).Entry.Number==selected)??list.Items[0]).Selected=true;list.EndUpdate();Describe();
        }
        void ImportMessage()=>message.Text=imported==null?"Importuj TXT.":$"Podgląd: {imported.Entries.Count} wpisów • odrzucone wiersze: {imported.InvalidLines} • duplikaty: {imported.Duplicates}. Nie wykonano automatycznych połączeń.";
        async Task Scan()
        {
            if(operation!=null||imported==null||!Current())return;
            using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);operation=cancellation;
            load!.Enabled=scan!.Enabled=web!.Enabled=export!.Enabled=add!.Enabled=play!.Enabled=false;cancel.Enabled=true;
            int done=0;
            try
            {
                var progress=new Progress<PortalScanRow>(row=>
                {
                    if(dialog.IsDisposed||!Current()||lifetime.IsCancellationRequested||!ReferenceEquals(operation,cancellation))return;
                    int index=rows.FindIndex(r=>r.Entry.Number==row.Entry.Number);if(index<0)return;rows[index]=row;
                    if(row.Status!=PortalScanStatus.Checking)done++;Fill();message.Text=$"Sprawdzono {done}/{rows.Count} • {row.Entry.Label}: {row.Description}";
                });
                var result=await Task.Run(()=>engine.Scan(imported.Entries,cancellation.Token,((IProgress<PortalScanRow>)progress).Report),cancellation.Token);
                if(!Current()||dialog.IsDisposed)return;rows=result.Rows.ToList();Fill();
                message.Text=(result.Complete?"Zakończono skan.":"Przerwano lub osiągnięto limit 10 min. Pozostałe wpisy są niesprawdzone.")+" Katalog: "+rows.Count(r=>r.Status==PortalScanStatus.Catalog)+" • brak automatycznego potwierdzenia odtwarzania.";
            }
            catch(OperationCanceledException){if(!dialog.IsDisposed&&Current())message.Text="Przerwano. Dotychczasowe wyniki pozostały.";}
            catch(Exception){if(!dialog.IsDisposed&&Current())message.Text="Nie ukończono skanu. Dotychczasowe wyniki pozostały.";}
            finally{operation=null;if(!dialog.IsDisposed){load!.Enabled=web!.Enabled=Current();scan!.Enabled=export!.Enabled=Current()&&rows.Count>0;cancel.Enabled=false;Describe();}}
        }
        async Task Import()
        {
            if(operation!=null||!Current())return;
            string? path=null;
            if(importFixture==null){using var pick=new OpenFileDialog{Filter="Lista portali TXT|*.txt|Wszystkie pliki|*.*",CheckFileExists=true};if(pick.ShowDialog(dialog)!=DialogResult.OK)return;path=pick.FileName;}
            using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);operation=cancellation;
            load!.Enabled=scan!.Enabled=web!.Enabled=export!.Enabled=add!.Enabled=play!.Enabled=false;cancel.Enabled=true;
            try
            {
                PortalTxtImport parsed;
                if(importFixture!=null)parsed=await importFixture(cancellation.Token).WaitAsync(cancellation.Token);
                else {await using var input=new FileStream(path!,FileMode.Open,FileAccess.Read,FileShare.Read,8192,FileOptions.Asynchronous);parsed=await PortalTxt.Read(input,cancellation.Token);}
                if(cancellation.IsCancellationRequested||!Current()||dialog.IsDisposed||!ReferenceEquals(operation,cancellation))return;
                imported=parsed;rows=parsed.Entries.Select(e=>new PortalScanRow(e,PortalScanStatus.NotChecked)).ToList();Fill();ImportMessage();
            }
            catch(OperationCanceledException){if(!dialog.IsDisposed&&Current())message.Text="Przerwano import. Poprzedni podgląd pozostał.";}
            catch(InvalidDataException error){if(!dialog.IsDisposed)message.Text=error.Message;}
            catch(Exception){if(!dialog.IsDisposed)message.Text="Nie odczytano pliku TXT. Poprzedni podgląd pozostał.";}
            finally{operation=null;if(!dialog.IsDisposed){load!.Enabled=web!.Enabled=Current();scan!.Enabled=export!.Enabled=Current()&&rows.Count>0;cancel.Enabled=false;Describe();}}
        }
        load=Button("Importuj TXT",()=>{if(operation!=null)return Task.CompletedTask;running=Import();return running;},true,145);load.Name="portal-scanner-import";
        scan=Button("Sprawdź listę",()=>{running=Scan();return running;},width:145);scan.Name="portal-scanner-start";
        add=Button("Dodaj wybrany",()=>{if(Selected() is {} row){chosen=row.Entry;dialog.Close();}return Task.CompletedTask;},width:150);add.Name="portal-scanner-add";
        play=Button("Odtwórz próbkę",()=>{if(Selected() is {Status:PortalScanStatus.Catalog} row){chosen=row.Entry;sample=true;dialog.Close();}return Task.CompletedTask;},width:155);play.Name="portal-scanner-play";
        web=Button("Szukaj publicznych portali",()=>{explore=true;dialog.Close();return Task.CompletedTask;},width:245);web.Name="portal-scanner-web";
        export=Button("Eksportuj raport CSV",()=>
        {
            using var save=new SaveFileDialog{Filter="Raport CSV|*.csv",FileName="SwirStream_portale_raport.csv"};if(save.ShowDialog(dialog)==DialogResult.OK)
            {try{File.WriteAllText(save.FileName,PortalScanner.Export(rows),new UTF8Encoding(true));message.Text="Zapisano raport bez adresów, MAC i danych dostępu.";}catch{message.Text="Nie zapisano raportu. Sprawdź folder docelowy.";}}
            return Task.CompletedTask;
        },width:205);export.Name="portal-scanner-export";
        actions.Controls.AddRange([load,scan,cancel,add,play,web,export]);scan.Enabled=export.Enabled=rows.Count>0;
        list.SelectedIndexChanged+=(_,_)=>Describe();dialog.Controls.Add(layout);Fill();ImportMessage();
        dialog.Shown+=(_,_)=>{profileWatch.Start();BeginInvoke(()=>privateDialogPreview?.Invoke(dialog));};dialog.ShowDialog(this);
        if(running!=null)await running;
        if(!Current())return;
        if(explore)await ShowSourceExplorer("free public Stalker Ministra television portal",true);
        else if(chosen!=null)await OpenScannerEntry(chosen,repository,sample);
    }
}
