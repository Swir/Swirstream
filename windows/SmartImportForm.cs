namespace SwirStreamAI;

public sealed partial class MainForm
{
    bool smartImportBusy;
    static bool SameXtreamAccount(ProviderSource source,XtreamAccount account)
    {try{var normalized=XtreamLink.Normalize(source);return normalized.Url==account.Server&&normalized.Username==account.Username;}catch(InvalidDataException){return false;}}
    internal ProviderSource SmartDraft(string input,string mode)
    {
        string url=XtreamLink.Clean(input);var uri=PrivateProviderClient.Url(url);XtreamAccount? account=null;
        if(mode=="MAC Player"||mode=="Automatycznie"&&PortalAddress.ExplicitClient(url))
        {PortalAddress.Endpoint(url);return new(Guid.NewGuid().ToString("N"),uri.Host,PrivateSourceKind.Portal,url);}
        if(mode is "Automatycznie" or "Xtream")account=XtreamLink.Parse(url,mode=="Xtream");
        if(mode=="Xtream"&&account==null)throw new InvalidDataException("Ten link nie zawiera loginu i hasła. Użyj pełnego linku konta albo formularza Xtream.");
        if(account!=null)
        {
            var matches=privateSources.Sources.Where(s=>s.Source.Kind==PrivateSourceKind.Xtream&&SameXtreamAccount(s.Source,account)).ToArray();
            if(matches.Length>1)throw new InvalidDataException("Istnieje kilka źródeł tego samego konta. Wybierz konkretne źródło w module Xtream i użyj Edytuj.");
            var old=matches.FirstOrDefault()?.Source;
            return new(old?.Id??Guid.NewGuid().ToString("N"),old?.Name??uri.Host,PrivateSourceKind.Xtream,account.Server,account.Username,account.Password);
        }
        var kind=mode=="Jeden kanał"||mode=="Automatycznie"&&new[]{".mp3",".aac",".ogg",".mp4",".ts",".mpd"}.Contains(Path.GetExtension(uri.AbsolutePath).ToLowerInvariant())?PrivateSourceKind.DirectUrl:PrivateSourceKind.M3u;
        var same=privateSources.Sources.FirstOrDefault(s=>s.Source.Kind==kind&&!s.Source.DiscoveredPublic&&Uri.TryCreate(s.Source.Url,UriKind.Absolute,out var saved)&&saved==uri)?.Source;
        return same??new(Guid.NewGuid().ToString("N"),uri.Host,kind,url);
    }
    async Task SmartImport(string initial="",bool explicitXtream=false)
    {
        if(smartImportBusy)return;smartImportBusy=true;var repository=privateSources;
        try
        {
            using var dialog=new Form{Text="Wklej link • SwirStream AI",ClientSize=new(760,350),StartPosition=FormStartPosition.CenterParent,BackColor=Card,ForeColor=Color.White,Font=Font};
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new(20),ColumnCount=1,RowCount=6};foreach(var h in new[]{52f,44f,44f,38f,-1f,45f})layout.RowStyles.Add(new(h<0?SizeType.Percent:SizeType.Absolute,h<0?100:h));
            layout.Controls.Add(Label("Wklej cały link od operatora. Login i hasło rozpoznam lokalnie.\nNic nie jest wysyłane ani zapisywane przed zatwierdzeniem formularza.",10,Muted),0,0);
            var link=Input("Pełny adres HTTP/HTTPS");link.Name="smart-url";link.Dock=DockStyle.Fill;link.UseSystemPasswordChar=true;link.Text=initial;layout.Controls.Add(link,0,1);
            var options=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};var mode=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=205,Name="smart-kind"};mode.Items.AddRange(["Automatycznie","Xtream","Dokładny URL M3U","Jeden kanał","MAC Player"]);mode.SelectedIndex=explicitXtream?1:0;options.Controls.Add(mode);
            options.Controls.Add(Button("Wklej ze schowka",()=>{if(Clipboard.ContainsText())link.Text=Clipboard.GetText();return Task.CompletedTask;},width:185));
            var reveal=new CheckBox{Text="Pokaż link",AutoSize=true,ForeColor=Color.White};reveal.CheckedChanged+=(_,_)=>link.UseSystemPasswordChar=!reveal.Checked;options.Controls.Add(reveal);layout.Controls.Add(options,0,2);
            var heading=Label("Prywatny import • bieżący profil",10,Cyan,true);layout.Controls.Add(heading,0,3);
            var summary=Label("Wklej link, aby zobaczyć typ i serwer. Dane konta pozostają ukryte.",10,Muted);summary.Name="smart-summary";layout.Controls.Add(summary,0,4);
            ProviderSource? draft=null;
            void Preview()
            {
                draft=null;try{if(link.Text.Trim().Length==0)return;draft=SmartDraft(link.Text,mode.Text);bool existing=repository.Sources.Any(s=>s.Source.Id==draft.Id);summary.Text=SourceKindName(draft.Kind)+" • "+new Uri(draft.Url).Authority+"\n"+(existing?"Aktualizacja istniejącego źródła: zachowamy nazwę, ulubione i historię.":"Nowe źródło w tym profilu.")+(draft.Kind==PrivateSourceKind.Xtream?"\nSerwer, login i hasło zostaną uzupełnione. Zmiana danych wymaga udanego połączenia.":draft.Kind==PrivateSourceKind.Portal?"\nAdres przypomina klienta portalu. Wymagany Twój przypisany MAC; dostępu nie sprawdzono.":"\nDokładny adres zostaje zachowany, razem z własnymi danymi dostępu.");}catch(Exception ex)when(ex is InvalidDataException or ArgumentException){summary.Text=ex.Message;}
            }
            link.TextChanged+=(_,_)=>Preview();mode.SelectedIndexChanged+=(_,_)=>Preview();Preview();
            var next=Button("Uzupełnij formularz",()=>{Preview();if(draft!=null){dialog.DialogResult=DialogResult.OK;dialog.Close();}return Task.CompletedTask;},true,215);next.Name="smart-next";layout.Controls.Add(next,0,5);dialog.Controls.Add(layout);dialog.Shown+=(_,_)=>BeginInvoke(()=>privateDialogPreview?.Invoke(dialog));
            if(dialog.ShowDialog(this)!=DialogResult.OK||draft==null||!ReferenceEquals(repository,privateSources))return;
            var approved=EditOwnSource(draft,draft.Kind);if(approved==null||!ReferenceEquals(repository,privateSources))return;
            await ConnectDraft(approved,repository);
        }
        finally{smartImportBusy=false;}
    }
    async Task ConnectDraft(ProviderSource source,PrivateSourceStore repository)
    {
        if(!ReferenceEquals(repository,privateSources))return;
        using var dialog=new Form{Text="Łączenie ze źródłem",ClientSize=new(650,240),StartPosition=FormStartPosition.CenterParent,BackColor=Card,ForeColor=Color.White,Font=Font};
        using var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(3));dialog.FormClosing+=(_,_)=>cancel.Cancel();
        var label=Label("Pobieram kanały i EPG…",10,Color.White);label.SetBounds(24,20,600,110);dialog.Controls.Add(label);
        dialog.Controls.Add(new ProgressBar{Left=24,Top=135,Width=600,Style=ProgressBarStyle.Marquee});var stop=Button("Przerwij",()=>{cancel.Cancel();return Task.CompletedTask;},width:140);stop.SetBounds(24,180,140,35);dialog.Controls.Add(stop);
        dialog.Shown+=async(_,_)=>
        {
            try{bool ok=await SaveConnectSource(source,cancel.Token,message=>{if(!dialog.IsDisposed)label.Text=message;});if(ok&&!dialog.IsDisposed)dialog.Close();else if(!dialog.IsDisposed){label.Text=status.Text;stop.Text="Zamknij";stop.Click+=(_,_)=>dialog.Close();}}
            catch(OperationCanceledException){if(!dialog.IsDisposed){label.Text="Przerwano. Poprzednie źródło i kanały pozostają dostępne.";stop.Text="Zamknij";stop.Click+=(_,_)=>dialog.Close();}}
            catch(Exception){if(!dialog.IsDisposed){label.Text="Nie udało się połączyć. Poprzednie dane pozostały dostępne.";stop.Text="Zamknij";stop.Click+=(_,_)=>dialog.Close();}}
            privateDialogPreview?.Invoke(dialog);
        };
        dialog.ShowDialog(this);
    }
    async Task<bool> RedirectPrivateSearch(string value)
    {
        if(!XtreamLink.PrivateInput(value))return false;
        searchCancel?.Cancel();EndScan(searchGeneration);searchGeneration++;changingSearch=true;search.Clear();search.UseSystemPasswordChar=false;changingSearch=false;lastQuery="";
        status.Text="Link z danymi dostępu trafia do prywatnego importu, nie do wyszukiwarki internetowej.";
        await SmartImport(value);return true;
    }
    async Task SearchSubmit(){if(!await RedirectPrivateSearch(search.Text))await FilterCatalog();}
}
