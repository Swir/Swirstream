using System.Diagnostics;

namespace SwirStreamAI;

public enum ProviderModule { All, Playlists, Xtream, Mac, Drm }

public sealed partial class MainForm
{
    internal static string ModuleTitle(ProviderModule module)=>module switch {ProviderModule.Playlists=>"Playlisty URL",ProviderModule.Xtream=>"Xtream",ProviderModule.Mac=>"MAC Player",ProviderModule.Drm=>"DRM",_=>"Moje źródła"};
    internal static ProviderModule ModuleFor(PrivateSourceKind kind)=>kind switch {PrivateSourceKind.Xtream=>ProviderModule.Xtream,PrivateSourceKind.Portal=>ProviderModule.Mac,PrivateSourceKind.ClearKeyDash=>ProviderModule.Drm,_=>ProviderModule.Playlists};
    internal static bool ModuleContains(ProviderModule module,ProviderSource source)=>module==ProviderModule.All||ModuleFor(source.Kind)==module;
    static PrivateSourceKind ModuleKind(ProviderModule module)=>module switch {ProviderModule.Xtream=>PrivateSourceKind.Xtream,ProviderModule.Mac=>PrivateSourceKind.Portal,ProviderModule.Drm=>PrivateSourceKind.ClearKeyDash,_=>PrivateSourceKind.M3u};
    static string SourceKindName(PrivateSourceKind type)=>type switch{PrivateSourceKind.M3u=>"Playlista URL M3U",PrivateSourceKind.Xtream=>"Xtream • live + EPG",PrivateSourceKind.Portal=>"Portal MAC • podstawowy",PrivateSourceKind.ClearKeyDash=>"DASH • ClearKey",PrivateSourceKind.DirectUrl=>"Transmisja URL",PrivateSourceKind.M3uFile=>"Plik M3U",_=>"Przeniesione kanały"};
    static string ModuleDescription(ProviderModule module)=>module switch
    {
        ProviderModule.Playlists=>"Własne URL M3U/get.php, pliki i transmisje. AI znajduje publiczne playlisty; dodanie źródła wymaga Twojego zatwierdzenia.",
        ProviderModule.Xtream=>"Twoje konto operatora: serwer, login i hasło. Kanały live, kategorie i EPG. Bez VOD, seriali i archiwum.",
        ProviderModule.Mac=>"Twój portal Stalker/Ministra i przypisany MAC. Podstawowe kanały live; bez SN, dodatkowego parowania i EPG portalu.",
        ProviderModule.Drm=>"Windows: pojedynczy klucz DASH ClearKey CENC/H264. Widevine i PlayReady nie są obsługiwane. Wymagane własne dane licencji.",
        _=>"Zbiorczy widok wszystkich własnych źródeł bieżącego profilu. Oddzielne moduły są dostępne nad wyszukiwarką."
    };
    Control CreateProviderModuleBar()
    {
        var bar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty,Name="provider-modules"};
        var explorer=Button("Eksplorator źródeł",ShowSourceExplorer,width:174);explorer.Name="source-explorer-open";bar.Controls.Add(explorer);
        foreach(var module in new[]{ProviderModule.Playlists,ProviderModule.Xtream,ProviderModule.Mac,ProviderModule.Drm})
        {var selected=module;var button=Button(ModuleTitle(module),()=>ShowProviderModule(selected),width:150);button.Name="module-"+module;button.ForeColor=Cyan;bar.Controls.Add(button);}
        var all=Button("Wszystkie źródła",ShowPrivateSources,width:155);all.Name="module-All";bar.Controls.Add(all);
        var paste=Button("Wklej link",()=>SmartImport(),true,130);paste.Name="smart-import";bar.Controls.Add(paste);return bar;
    }
    Task ShowPrivateSources()=>ShowProviderModule(ProviderModule.All);

    internal async Task<bool> SaveConnectSource(ProviderSource source,CancellationToken ct,Action<string>? progress=null)
    {
        var repository=privateSources;var previous=repository.Sources.FirstOrDefault(s=>s.Source.Id==source.Id)?.Catalog;
        ct.ThrowIfCancellationRequested();progress?.Invoke("Sprawdzam źródło. Zmiana danych zostanie zapisana po poprawnym pobraniu…");
        ProviderState state;
        try{state=await repository.Connect(source,ct);ct.ThrowIfCancellationRequested();}
        catch(OperationCanceledException)when(closing||!ReferenceEquals(repository,privateSources)){return false;}
        if(closing||!ReferenceEquals(repository,privateSources))return false;
        status.Text=state.Status;progress?.Invoke(state.Status);
        if(state.Catalog==null||ReferenceEquals(state.Catalog,previous))
        {
            await LoadOwnChannels(source.Id);
            status.Text=SourceConnectionMessage(state);progress?.Invoke(status.Text);return false;
        }
        progress?.Invoke("Pobrano kanały. Otwieram listę w module "+ModuleTitle(ModuleFor(source.Kind))+"…");
        await LoadOwnChannels(source.Id);return true;
    }

    async Task ShowProviderModule(ProviderModule module)
    {
        var repository=privateSources;bool findPlaylists=false;
        using var dialog=new Form{Text=ModuleTitle(module)+" • SwirStream AI",Tag=module,Size=new(1040,770),MinimumSize=new(900,690),StartPosition=FormStartPosition.CenterParent,BackColor=Bg,ForeColor=Color.White,Font=Font};
        using var lifetime=new CancellationTokenSource();CancellationTokenSource? operation=null;dialog.FormClosing+=(_,_)=>{lifetime.Cancel();operation?.Cancel();};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new(18),RowCount=7,ColumnCount=1};
        foreach(var row in new[]{44f,55f,40f,-1f,150f,132f,48f})layout.RowStyles.Add(new(row<0?SizeType.Percent:SizeType.Absolute,row<0?100:row));
        layout.Controls.Add(Label(ModuleTitle(module)+"  •  "+profiles.Index.Profiles.Single(p=>p.Id==profiles.Index.ActiveId).Name,20,Cyan,true),0,0);
        layout.Controls.Add(Label(ModuleDescription(module),10,Muted),0,1);
        var filter=Input("Szukaj zapisanych źródeł w tym module");filter.Name="provider-source-search";filter.Dock=DockStyle.Fill;layout.Controls.Add(filter,0,2);
        var list=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,HideSelection=false,MultiSelect=false,BackColor=Card,ForeColor=Color.White,BorderStyle=BorderStyle.None,Name="provider-sources",Tag=module};
        list.Columns.Add("Nazwa",230);list.Columns.Add("Rodzaj",160);list.Columns.Add("Kanały",70);list.Columns.Add("Ostatni zapis",150);list.Columns.Add("Stan źródła",260);layout.Controls.Add(list,0,3);
        var details=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Card,ForeColor=Color.White,BorderStyle=BorderStyle.None,Name="provider-status"};layout.Controls.Add(details,0,4);
        ProviderState? SelectedSource()=>list.SelectedItems.Count>0?list.SelectedItems[0].Tag as ProviderState:null;
        void Describe()
        {
            var s=SelectedSource();details.Text=s==null?ModuleDescription(module)+"\r\nDodaj źródło: Zapisz i połącz od razu pobierze kanały oraz otworzy ich listę.\r\n"+repository.Notice:SourceConnectionMessage(s)+"\r\n"+s.Status+"\r\n"+(s.Catalog is {} c?"Ostatni zapis kanałów: "+c.DownloadedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm")+(c.ModifiedAt is {} modified?" • data dostawcy: "+modified.ToLocalTime().ToString("dd.MM.yyyy HH:mm"):" • dostawca nie podał daty aktualizacji")+(DateTimeOffset.UtcNow-c.DownloadedAt>TimeSpan.FromDays(7)?" • STARY ZAPIS, odśwież źródło":""):"Nie pobrano jeszcze kanałów.")+"\r\n"+(s.LastAttempt is {} tried?"Ostatnia próba: "+tried.ToLocalTime().ToString("dd.MM.yyyy HH:mm"):"")+(s.PlaybackFailure is {} play?"\r\nOstatnia próba kanału: "+ProviderFailures.Describe(play):"")+"\r\nAdres i dane dostępu są ukryte. Błąd zachowuje poprzednie kanały i datę."+(s.Source.Url.StartsWith("http://",StringComparison.OrdinalIgnoreCase)?"\r\nHTTP: połączenie z operatorem nie jest szyfrowane.":"");
        }
        void Fill(string? selected=null)
        {
            string? id=selected??SelectedSource()?.Source.Id;list.Items.Clear();string query=TextTools.Normalize(filter.Text.Trim());
            foreach(var source in repository.Sources.Where(s=>ModuleContains(module,s.Source)&&TextTools.Normalize(s.Source.Name).Contains(query)))
            {var item=new ListViewItem(source.Source.Name){Tag=source};item.SubItems.Add(SourceKindName(source.Source.Kind));item.SubItems.Add((source.Catalog?.Channels.Count??0).ToString());item.SubItems.Add(source.Catalog?.DownloadedAt.ToLocalTime().ToString("dd.MM HH:mm")??"—");item.SubItems.Add(source.PlaybackFailure!=null?"Błąd przygotowania kanału":SourceCatalogFailure(source)!=null?source.Catalog==null?"Brak dostępu / błąd źródła":"Zapisana lista • błąd odświeżenia":source.Catalog==null?"Nie pobrano kanałów":"Lista pobrana • sprawdź odtwarzanie");list.Items.Add(item);}
            if(list.Items.Count>0)(list.Items.Cast<ListViewItem>().FirstOrDefault(x=>((ProviderState)x.Tag!).Source.Id==id)??list.Items[0]).Selected=true;Describe();
        }
        list.SelectedIndexChanged+=(_,_)=>Describe();filter.TextChanged+=(_,_)=>Fill();
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};layout.Controls.Add(actions,0,5);
        var progressRow=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};var progress=new ProgressBar{Style=ProgressBarStyle.Marquee,Width=610,Height=24,Visible=false};
        var cancel=Button("Przerwij pobieranie",()=>{operation?.Cancel();return Task.CompletedTask;},width:175);cancel.Visible=false;progressRow.Controls.Add(progress);progressRow.Controls.Add(cancel);layout.Controls.Add(progressRow,0,6);
        async Task Connect(ProviderSource source)
        {
            if(!ReferenceEquals(repository,privateSources))return;
            if(operation!=null)return;using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(TimeSpan.FromMinutes(3));operation=timeout;
            actions.Enabled=filter.Enabled=list.Enabled=false;progress.Visible=cancel.Visible=true;
            try
            {
                bool loaded=await SaveConnectSource(source,timeout.Token,message=>{if(!dialog.IsDisposed)details.Text=message;});
                if(!dialog.IsDisposed){Fill(source.Id);if(loaded)dialog.Close();}
            }
            catch(OperationCanceledException){if(!dialog.IsDisposed){Fill(source.Id);details.AppendText("\r\nPrzerwano pobieranie. Poprzednia lista pozostała dostępna.");}}
            catch(Exception){if(!dialog.IsDisposed){Fill(source.Id);details.AppendText("\r\nNie udało się zapisać lub połączyć. Sprawdź ustawienia i dostęp do folderu danych.");}}
            finally{operation=null;if(!dialog.IsDisposed){actions.Enabled=filter.Enabled=list.Enabled=true;progress.Visible=cancel.Visible=false;}}
        }
        actions.Controls.Add(Button(module==ProviderModule.All?"Dodaj źródło":"Dodaj • "+ModuleTitle(module),async()=>
        {
            var source=EditOwnSource(null,module==ProviderModule.All?ChooseSourceKind():ModuleKind(module));if(source!=null)await Connect(source);
        },true,200));
        if(module is ProviderModule.Xtream or ProviderModule.All or ProviderModule.Playlists)actions.Controls.Add(Button(module==ProviderModule.Xtream?"Wklej link Xtream":"Wklej link",()=>{dialog.Close();return SmartImport(explicitXtream:module==ProviderModule.Xtream);},width:180));
        actions.Controls.Add(Button("Edytuj i połącz",async()=>{if(SelectedSource() is {} s&&s.Source.Kind!=PrivateSourceKind.SavedChannels){var source=EditOwnSource(s.Source,s.Source.Kind);if(source!=null)await Connect(source);}},width:155));
        actions.Controls.Add(Button("Odśwież i pokaż kanały",async()=>{if(SelectedSource() is {} s)await Connect(s.Source);},width:205));
        actions.Controls.Add(Button("Pokaż zapisane kanały",async()=>{if(SelectedSource() is {} s){await LoadOwnChannels(s.Source.Id);if(s.Catalog!=null)dialog.Close();}},width:200));
        actions.Controls.Add(Button("Usuń źródło",()=>
        {
            if(SelectedSource() is {} s&&MessageBox.Show(dialog,"Usunąć to źródło i jego wpisy w bibliotece tego profilu?","Usuń źródło",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)
            {if(current?.PrivateSourceId==s.Source.Id)Run(Stop);repository.Remove(s.Source.Id);store.Data.Playlist.RemoveAll(x=>x.PrivateSourceId==s.Source.Id);store.Data.Favorites.RemoveAll(x=>x.PrivateSourceId==s.Source.Id);store.Data.History.RemoveAll(x=>x.Station.PrivateSourceId==s.Source.Id);store.Save();localStations.RemoveAll(x=>x.PrivateSourceId==s.Source.Id);Fill();}
            return Task.CompletedTask;
        },width:135));
        if(module is ProviderModule.Playlists or ProviderModule.All)actions.Controls.Add(Button("AI: znajdź playlisty",()=>{findPlaylists=true;dialog.Close();return Task.CompletedTask;},width:190));
        if(module is ProviderModule.Playlists or ProviderModule.All)actions.Controls.Add(Button("Importuj plik M3U",()=>{dialog.Close();return ImportPlaylist();},width:180));
        if(module is ProviderModule.Mac or ProviderModule.All){var scanner=Button("Skaner portali TXT",()=>{dialog.Close();return ShowPortalScanner();},width:205);scanner.Name="portal-scanner-open";actions.Controls.Add(scanner);}
        actions.Controls.Add(Button("Streamtest • strona",()=>{Process.Start(new ProcessStartInfo("https://streamtest.co/"){UseShellExecute=true});return Task.CompletedTask;},width:180));
        actions.Controls.Add(Button("Streamtest • logi",()=>{Process.Start(new ProcessStartInfo("https://streamtest.in/logs"){UseShellExecute=true});return Task.CompletedTask;},width:170));
        dialog.Controls.Add(layout);Fill();dialog.Shown+=(_,_)=>BeginInvoke(()=>privateDialogPreview?.Invoke(dialog));dialog.ShowDialog(this);
        if(findPlaylists&&!closing)await FindProviderPlaylists();
    }

    PrivateSourceKind? ChooseSourceKind()
    {
        using var dialog=new Form{Text="Wybierz moduł",ClientSize=new(500,280),StartPosition=FormStartPosition.CenterParent,BackColor=Card,ForeColor=Color.White,Font=Font};
        var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new(20),FlowDirection=FlowDirection.TopDown,WrapContents=false};PrivateSourceKind? selected=null;
        foreach(var module in new[]{ProviderModule.Playlists,ProviderModule.Xtream,ProviderModule.Mac,ProviderModule.Drm}){var m=module;layout.Controls.Add(Button(ModuleTitle(m),()=>{selected=ModuleKind(m);dialog.Close();return Task.CompletedTask;},width:440));}
        dialog.Controls.Add(layout);dialog.ShowDialog(this);return selected;
    }
    ProviderSource? EditOwnSource(ProviderSource? old,PrivateSourceKind? fixedKind=null)
    {
        if(old==null&&fixedKind==null)return null;var type=fixedKind??old!.Kind;var module=ModuleFor(type);
        bool existing=old!=null&&privateSources.Sources.Any(s=>s.Source.Id==old.Id);
        using var form=new Form{Text=(existing?"Edytuj • ":"Dodaj • ")+SourceKindName(type),Tag=module,ClientSize=new(760,type==PrivateSourceKind.ClearKeyDash?580:type==PrivateSourceKind.Xtream?525:420),StartPosition=FormStartPosition.CenterParent,BackColor=Card,ForeColor=Color.White,Font=Font,MinimizeBox=false,MaximizeBox=false,FormBorderStyle=FormBorderStyle.FixedDialog};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Padding=new(20)};layout.ColumnStyles.Add(new(SizeType.Absolute,180));layout.ColumnStyles.Add(new(SizeType.Percent,100));int row=0;var inputs=new List<TextBox>();
        TextBox Field(string title,string value,string name,bool secret=false)
        {layout.RowStyles.Add(new(SizeType.Absolute,44));layout.Controls.Add(Label(title,10,Color.White),0,row);var text=Input(title);text.Name=name;text.Text=value;text.Dock=DockStyle.Fill;text.UseSystemPasswordChar=secret;layout.Controls.Add(text,1,row++);if(secret)inputs.Add(text);return text;}
        var name=Field("Nazwa źródła",old?.Name??"","source-name");
        var url=Field(type==PrivateSourceKind.Xtream?"Serwer Xtream":type==PrivateSourceKind.Portal?"Adres portalu":type==PrivateSourceKind.ClearKeyDash?"Manifest DASH":type==PrivateSourceKind.M3uFile?"Plik M3U":"Adres URL",old?.Url??"","source-url",true);
        TextBox? login=null,password=null,mac=null,license=null,kid=null,key=null;
        if(type==PrivateSourceKind.Xtream){login=Field("Login",old?.Username??"","source-username",true);password=Field("Hasło",old?.Password??"","source-password",true);}
        if(type==PrivateSourceKind.Portal)mac=Field("Twój przypisany MAC",old?.Mac??"","source-mac",true);
        if(type==PrivateSourceKind.ClearKeyDash){kid=Field("KID ClearKey",old?.KeyId??"","source-kid",true);license=Field("URL licencji",old?.LicenseUrl??"","source-license",true);key=Field("LUB klucz CENC",old?.KeyHex??"","source-key",true);}
        var note=Label(ModuleDescription(module)+"\nDane pozostają w chronionym magazynie tego profilu. Zapisz i połącz pobierze listę oraz pokaże kanały.",10,Muted);
        if(type==PrivateSourceKind.Xtream)
        {
            void FromLink(bool explicitReplace)
            {
                try
                {
                    var account=XtreamLink.Parse(url.Text,true);if(account==null)return;
                    if(!explicitReplace&&(login!.Text.Length>0&&login.Text!=account.Username||password!.Text.Length>0&&password.Text!=account.Password)){note.Text="Link zawiera inne dane niż pola. Kliknij Uzupełnij z linku, jeśli chcesz świadomie je zastąpić.";return;}
                    url.Text=account.Server;login!.Text=account.Username;password!.Text=account.Password;if(name.Text.Trim().Length==0)name.Text=new Uri(account.Server).Host;note.Text="Rozpoznano konto Xtream i uzupełniono pola. Zapisz i połącz sprawdzi dostęp przed zapisaniem zmiany.";
                }
                catch(Exception ex)when(ex is InvalidDataException or ArgumentException){note.Text=ex.Message;}
            }
            layout.RowStyles.Add(new(SizeType.Absolute,42));var parse=Button("Uzupełnij z linku w polu Serwer",()=>{FromLink(true);return Task.CompletedTask;},width:300);parse.Name="xtream-parse-link";layout.Controls.Add(parse,1,row++);url.Leave+=(_,_)=>FromLink(false);
        }
        layout.RowStyles.Add(new(SizeType.Absolute,36));var reveal=new CheckBox{Text="Pokaż dane na tym ekranie",AutoSize=true,ForeColor=Color.White};layout.Controls.Add(reveal,1,row++);reveal.CheckedChanged+=(_,_)=>{foreach(var input in inputs)input.UseSystemPasswordChar=!reveal.Checked;};
        if(type==PrivateSourceKind.M3uFile){layout.RowStyles.Add(new(SizeType.Absolute,42));layout.Controls.Add(Button("Wybierz plik M3U",()=>{using var pick=new OpenFileDialog{Filter="Playlisty M3U|*.m3u;*.m3u8|Wszystkie pliki|*.*"};if(pick.ShowDialog(form)==DialogResult.OK){url.Text=pick.FileName;if(name.Text.Length==0)name.Text=Path.GetFileNameWithoutExtension(pick.FileName);}return Task.CompletedTask;},width:190),1,row++);}
        layout.RowStyles.Add(new(SizeType.Percent,100));layout.Controls.Add(note,0,row);layout.SetColumnSpan(note,2);row++;
        ProviderSource? result=null;layout.RowStyles.Add(new(SizeType.Absolute,48));var save=Button("Zapisz i połącz",()=>
        {
            try{result=new(old?.Id??Guid.NewGuid().ToString("N"),name.Text.Trim(),type,url.Text.Trim(),login?.Text??"",password?.Text??"",mac?.Text.Trim()??"",license?.Text.Trim()??"",kid?.Text.Trim()??"",key?.Text.Trim()??"",old?.DiscoveredPublic??false);result=XtreamLink.Normalize(result);if(string.IsNullOrWhiteSpace(result.Name))result=result with{Name=new Uri(result.Url).Host};PrivateProviderClient.Validate(result);form.DialogResult=DialogResult.OK;form.Close();}
            catch(Exception ex)when(ex is InvalidDataException or ArgumentException){note.Text="Nie zapisano: "+ex.Message;result=null;}
            return Task.CompletedTask;
        },true,190);save.Name="source-save";layout.Controls.Add(save,1,row++);layout.RowCount=row;form.Controls.Add(layout);form.Shown+=(_,_)=>BeginInvoke(()=>privateDialogPreview?.Invoke(form));form.ShowDialog(this);return result;
    }
    internal async Task OpenProviderDraft(PrivateSourceKind type,string publicUrl,string suggestedName)
    {
        var repository=privateSources;
        var uri=PrivateProviderClient.Url(publicUrl);
        var existing=privateSources.Sources.FirstOrDefault(s=>s.Source.Kind==type&&Uri.TryCreate(s.Source.Url,UriKind.Absolute,out var saved)&&saved==uri)?.Source;
        if(existing is {DiscoveredPublic:false})
        {
            status.Text="Ten adres jest już zapisany jako Twoje własne źródło. Otwieram jego zapis bez zmiany ustawień ani nowego pobrania.";
            if(privateSources.Stations(existing.Id).Count>0)await LoadOwnChannels(existing.Id);else await ShowProviderModule(ProviderModule.Playlists);return;
        }
        var draft=existing??new ProviderSource(Guid.NewGuid().ToString("N"),suggestedName[..Math.Min(80,suggestedName.Length)],type,publicUrl,DiscoveredPublic:true);
        var approved=EditOwnSource(draft,type);if(approved==null||!ReferenceEquals(repository,privateSources))return;
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));await SaveConnectSource(approved,timeout.Token,message=>status.Text=message);
    }
    async Task FindProviderPlaylists()
    {
        using var dialog=new Form{Text="AI • znajdź publiczne playlisty",ClientSize=new(680,245),StartPosition=FormStartPosition.CenterParent,BackColor=Card,ForeColor=Color.White,Font=Font};
        var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new(20),FlowDirection=FlowDirection.TopDown,WrapContents=false};
        layout.Controls.Add(Label("Podaj nazwę kanału lub temat. "+CountryCaption()+".\nWyszukiwanie korzysta z publicznych katalogów i stron; nie znajduje kont, MAC ani kluczy.",10,Muted,width:630,height:60));
        var query=Input("Np. DW English albo muzyka");query.TextChanged+=(_,_)=>query.UseSystemPasswordChar=XtreamLink.PrivateInput(query.Text);query.Text=search.Text;query.Width=630;layout.Controls.Add(query);
        var go=new Button{Text="Znajdź playlisty",Width=190,Height=36,DialogResult=DialogResult.OK};layout.Controls.Add(go);dialog.AcceptButton=go;dialog.Controls.Add(layout);
        if(dialog.ShowDialog(this)!=DialogResult.OK||string.IsNullOrWhiteSpace(query.Text))return;
        if(await RedirectPrivateSearch(query.Text))return;
        changingSearch=true;search.Text=query.Text.Trim();changingSearch=false;
        await SmartSearch(true,deep:true,playlistSearch:true);if(!closing)await ShowFoundPlaylists();
    }
}
