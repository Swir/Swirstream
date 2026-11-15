namespace SwirStreamAI;

public sealed partial class MainForm
{
    SourceExplorer sourceExplorer=new();
    internal static ProviderSource ExplorerDraft(SourceCandidate candidate)
    {
        if(!SourceExplorerRules.AllowedUrl(candidate.Url))throw new InvalidDataException("Ten adres nie nadaje się do publicznego importu.");
        var type=candidate.Kind switch
        {
            SourceCandidateKind.PortalCandidate=>PrivateSourceKind.Portal,
            SourceCandidateKind.PublicPlaylist=>PrivateSourceKind.M3u,
            SourceCandidateKind.DirectStream when !candidate.RequiresDrm=>PrivateSourceKind.DirectUrl,
            SourceCandidateKind.DirectStream when new Uri(candidate.Url).AbsolutePath.EndsWith(".mpd",StringComparison.OrdinalIgnoreCase)=>PrivateSourceKind.ClearKeyDash,
            _=>throw new InvalidDataException("Nie jest to adres obsługiwanej transmisji. Chroniony HLS wymaga systemu DRM, którego ta wersja Windows nie obsługuje.")
        };
        return new(Guid.NewGuid().ToString("N"),candidate.Title[..Math.Min(80,candidate.Title.Length)],type,candidate.Url,DiscoveredPublic:type==PrivateSourceKind.M3u);
    }
    async Task ImportExplorerCandidate(SourceCandidate candidate,PrivateSourceStore repository)
    {
        if(closing||!ReferenceEquals(repository,privateSources))return;
        if(candidate.Kind==SourceCandidateKind.ProviderPage)return;
        if(candidate.Kind==SourceCandidateKind.PublicPlaylist){await OpenProviderDraft(PrivateSourceKind.M3u,candidate.Url,candidate.Title);return;}
        ProviderSource draft;try{draft=ExplorerDraft(candidate);}catch(InvalidDataException ex){status.Text=ex.Message;return;}
        // Portal discovery deliberately supplies no MAC, token or existing account.
        var approved=EditOwnSource(draft,draft.Kind);
        if(approved==null||!ReferenceEquals(repository,privateSources))return;
        await ConnectDraft(approved,repository);
    }
    async Task ShowSourceExplorer()
    {
        var repository=privateSources;string profile=profiles.Index.ActiveId;var saved=new SourceExplorerStore(profiles.Folder(profile));
        string[] countries=store.Data.SelectedCountries.ToArray();var directory=knownChannels.ToArray();var engine=sourceExplorer;
        bool Current()=>!closing&&ReferenceEquals(repository,privateSources)&&profiles.Index.ActiveId==profile;
        using var lifetime=new CancellationTokenSource();CancellationTokenSource? operation=null;Task? running=null;SourceCandidate? import=null;
        using var dialog=new Form{Text="Eksplorator źródeł • SwirStream AI",ClientSize=new(1100,750),MinimumSize=new(960,710),StartPosition=FormStartPosition.CenterParent,BackColor=Bg,ForeColor=Color.White,Font=Font,Name="source-explorer"};
        dialog.FormClosing+=(_,_)=>{lifetime.Cancel();operation?.Cancel();};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new(18),ColumnCount=1,RowCount=8};foreach(float h in new[]{38f,65f,43f,42f,-1f,170f,43f,45f})layout.RowStyles.Add(new(h<0?SizeType.Percent:SizeType.Absolute,h<0?100:h));
        layout.Controls.Add(Label("EKSPLORATOR ŹRÓDEŁ",20,Cyan,true),0,0);
        layout.Controls.Add(Label("Publiczne strony, playlisty i transmisje. Portal nie oznacza dostępnego konta ani kanałów.\n"+CountryCaption()+" • kraje doprecyzują zapytanie; kraj wyniku tylko z metadanych. Maks. 20 stron / 90 s.",10,Muted),0,1);
        var query=Input("Temat lub nazwa, np. telewizja regionalna Polska • albo publiczny URL");query.Name="explorer-query";query.Dock=DockStyle.Fill;query.Text=XtreamLink.PrivateInput(search.Text)?"":search.Text;layout.Controls.Add(query,0,2);
        var filters=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};layout.Controls.Add(filters,0,3);
        var kindFilter=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=205,Name="explorer-type"};kindFilter.Items.AddRange(["Wszystkie typy","Portale MAC","Playlisty M3U","Transmisje","Strony źródłowe"]);kindFilter.SelectedIndex=0;filters.Controls.Add(kindFilter);
        var localFilter=Input("Filtruj zapisane wyniki po nazwie lub domenie");localFilter.Name="explorer-local-filter";localFilter.Width=420;filters.Controls.Add(localFilter);
        var list=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,MultiSelect=false,HideSelection=false,BackColor=Card,ForeColor=Color.White,BorderStyle=BorderStyle.None,Name="explorer-results"};
        foreach(var c in new[]{("Nazwa / domena",300),("Typ",145),("Kraj",65),("Kanały",65),("Odczytano",140),("Stan",250)})list.Columns.Add(c.Item1,c.Item2);layout.Controls.Add(list,0,4);
        var detail=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Card,ForeColor=Color.White,BorderStyle=BorderStyle.None,Name="explorer-details"};layout.Controls.Add(detail,0,5);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};layout.Controls.Add(actions,0,6);
        var message=Label(saved.Notice.Length>0?saved.Notice:"Wpisz temat i wybierz Szukaj źródeł albo wklej publiczny adres do analizy.",9,Muted);message.Name="explorer-status";layout.Controls.Add(message,0,7);
        SourceCandidate? Selected()=>list.SelectedItems.Count==1?list.SelectedItems[0].Tag as SourceCandidate:null;
        var handoff=Button("Dodaj w programie",()=>{if(Selected() is {} item&&Current()){import=item;dialog.Close();}return Task.CompletedTask;},true,190);handoff.Name="explorer-import";
        void Describe()
        {
            var item=Selected();handoff.Enabled=item!=null&&item.Kind!=SourceCandidateKind.ProviderPage&&Current();
            handoff.Text=item?.Kind==SourceCandidateKind.PortalCandidate?"Skonfiguruj mój MAC":item?.RequiresDrm==true?"Konfiguracja DRM":"Dodaj w programie";
            if(item==null){detail.Text="Wybierz wynik. Publiczny odczyt nie potwierdza odtwarzania. Zapis jest osobny dla tego profilu.";return;}
            detail.Text=item.Status+"\r\nAdres: "+item.Url+"\r\nZnaleziono na: "+item.EvidenceUrl+"\r\nOdczyt: "+item.CheckedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")+(item.Stale?" • starszy zapis, sprawdź ponownie":"")+"\r\nData dokumentu dostawcy: "+(item.ModifiedAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")??"nie podano")+"\r\nKanały: "+item.ChannelCount+" • dopasowane do krajów: "+item.MatchingChannels+" • kraj: "+(item.Country.Length==0?"nieustalony":item.Country);
        }
        void Fill()
        {
            string? id=Selected()?.Id;list.BeginUpdate();list.Items.Clear();string text=TextTools.Normalize(localFilter.Text);SourceCandidateKind? selected=kindFilter.SelectedIndex switch{1=>SourceCandidateKind.PortalCandidate,2=>SourceCandidateKind.PublicPlaylist,3=>SourceCandidateKind.DirectStream,4=>SourceCandidateKind.ProviderPage,_=>null};
            foreach(var item in saved.Items.Where(x=>(selected==null||x.Kind==selected)&&TextTools.Normalize(x.Title+" "+new Uri(x.Url).Host).Contains(text)).OrderBy(x=>x.Kind==SourceCandidateKind.ProviderPage?3:x.Kind==SourceCandidateKind.PortalCandidate?2:x.Kind==SourceCandidateKind.PublicPlaylist?0:1).ThenByDescending(x=>x.MatchingChannels).ThenByDescending(x=>x.CheckedAt))
            {var row=new ListViewItem(item.Title){Tag=item};row.SubItems.AddRange([item.KindLabel,item.Country.Length==0?"—":item.Country,item.ChannelCount==0?"—":item.ChannelCount.ToString(),item.CheckedAt.ToLocalTime().ToString("dd.MM HH:mm"),item.Stale?"Starszy zapis • sprawdź":item.RequiresDrm?"DRM • wymaga licencji":item.Kind==SourceCandidateKind.PortalCandidate?"Wymagany własny MAC":"Odtwarzanie niepotwierdzone"]);list.Items.Add(row);}
            if(list.Items.Count>0)(list.Items.Cast<ListViewItem>().FirstOrDefault(x=>((SourceCandidate)x.Tag!).Id==id)??list.Items[0]).Selected=true;list.EndUpdate();Describe();
        }
        Button? start=null,analyze=null;var cancel=Button("Przerwij",()=>{operation?.Cancel();return Task.CompletedTask;},width:105);cancel.Enabled=false;cancel.Name="explorer-cancel";
        async Task Scan(bool page,string? supplied=null)
        {
            if(operation!=null||!Current())return;string input=(supplied??query.Text).Trim();
            using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);operation=cancellation;start!.Enabled=analyze!.Enabled=handoff.Enabled=false;cancel.Enabled=true;
            try
            {
                var progress=new Progress<string>(text=>{if(!dialog.IsDisposed&&!lifetime.IsCancellationRequested&&Current())message.Text=text;});
                // Parsing/metadata work runs away from the UI thread, in a captured profile scope.
                var result=await Task.Run(()=>page?engine.Analyze(input,countries,directory,cancellation.Token,((IProgress<string>)progress).Report):engine.Search(input,countries,directory,cancellation.Token,((IProgress<string>)progress).Report),cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();if(!Current()||dialog.IsDisposed)return;
                saved.Merge(result);foreach(var playlist in result.Playlists)foundPlaylists.Save(playlist);Fill();
                message.Text="Znaleziono "+result.Candidates.Count+" wyników. "+result.Evidence.LastOrDefault()?.Detail;
                if(result.Candidates.Count==0)message.Text="Nie znaleziono nowych źródeł. Poprzednie wyniki i daty pozostały. "+result.Evidence.LastOrDefault()?.Detail;
            }
            catch(OperationCanceledException){if(!dialog.IsDisposed&&Current())message.Text="Przerwano. Poprzednie wyniki i daty pozostały.";}
            catch(InvalidDataException ex){if(!dialog.IsDisposed&&Current())message.Text=ex.Message;}
            catch(Exception){if(!dialog.IsDisposed&&Current())message.Text="Nie odczytano źródeł. Poprzednie wyniki pozostały; spróbuj ponownie.";}
            finally{operation=null;if(!dialog.IsDisposed){start!.Enabled=analyze!.Enabled=Current();cancel.Enabled=false;Describe();}}
        }
        Task Start(bool page,string? supplied=null){running=Scan(page,supplied);return running;}
        start=Button("Szukaj źródeł",()=>Start(false),width:155);start.Name="explorer-search";analyze=Button("Analizuj stronę / link",()=>Start(true),width:195);analyze.Name="explorer-analyze";
        var recheck=Button("Sprawdź ponownie",()=>Selected() is {} item?Start(true,item.Url):Task.CompletedTask,width:175);recheck.Name="explorer-recheck";
        actions.Controls.AddRange([start,analyze,cancel,handoff,recheck]);
        query.TextChanged+=(_,_)=>query.UseSystemPasswordChar=SourceExplorerRules.SensitiveText(query.Text)||(XtreamLink.PrivateInput(query.Text)&&!Uri.TryCreate(query.Text.Trim(),UriKind.Absolute,out _));
        kindFilter.SelectedIndexChanged+=(_,_)=>Fill();localFilter.TextChanged+=(_,_)=>Fill();list.SelectedIndexChanged+=(_,_)=>Describe();query.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){e.SuppressKeyPress=true;Run(()=>Start(false));}};
        dialog.Controls.Add(layout);Fill();dialog.Shown+=(_,_)=>BeginInvoke(()=>privateDialogPreview?.Invoke(dialog));dialog.ShowDialog(this);
        if(running!=null)await running;if(import!=null&&Current())await ImportExplorerCandidate(import,repository);
    }
}
