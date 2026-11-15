package com.swir.stream;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.content.ActivityNotFoundException;
import android.content.SharedPreferences;
import android.content.res.ColorStateList;
import android.content.res.Configuration;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.StateListDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.Editable;
import android.text.TextWatcher;
import android.text.TextUtils;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowInsets;
import android.view.WindowManager;
import android.widget.*;
import androidx.media3.common.util.UnstableApi;
import androidx.media3.ui.PlayerView;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.text.Normalizer;
import java.util.*;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

@UnstableApi
public final class MainActivity extends Activity {
    private static final int BG=0xff080d19,PANEL=0xff101a2c,TEXT=0xffeef5ff,MUTED=0xffa1afc8,CYAN=0xff40e8e8,PURPLE=0xffa589ff;
    private static final int IMPORT=71,EXPORT=72,IMPORT_PORTALS=73,EXPORT_PORTALS=74;
    private final Handler main=new Handler(Looper.getMainLooper());
    private final ExecutorService work=Executors.newSingleThreadExecutor();
    private final ExecutorService epgWork=Executors.newSingleThreadExecutor();
    private final ExecutorService explorerWork=Executors.newSingleThreadExecutor();
    private SourceExplorerDialog explorerPage;private SourceExplorer explorerEngine;
    private final ExecutorService scannerWork=Executors.newSingleThreadExecutor();
    private PortalScannerDialog scannerPage,pendingScannerImport,pendingScannerExport;private PortalScanner scannerEngine;private String pendingScannerCsv="";
    private final ExecutorService providerWork=Executors.newSingleThreadExecutor();
    private final ExecutorService playbackWork=Executors.newSingleThreadExecutor();
    private AtomicBoolean playbackCancellation=new AtomicBoolean();
    private ProviderRepository providers;
    private List<Station> providerLibrary=new ArrayList<>();
    private AtomicBoolean cancellation=new AtomicBoolean();
    private AtomicBoolean epgCancellation=new AtomicBoolean();
    private SharedPreferences settings;
    private CatalogRepository catalog;
    private EpgRepository epg;
    private ProfileStore profiles;
    private PersonalAi ai;
    private Set<String> countries=new LinkedHashSet<>();
    private final List<String> countryCodes=new ArrayList<>(),countryNames=new ArrayList<>();
    private List<Station> library=new ArrayList<>();
    private List<Channel> channels=new ArrayList<>(),visible=new ArrayList<>();
    private final Set<String> attempted=new HashSet<>();
    private LinearLayout shell,header,toolbar,providerTiles,body,playerPanel,listPanel,channelTabs;
    private LinearLayout brandBox;private ImageView brandIcon;private TextView brandTitle,brandSignature;private Button headerMenu;
    private ProviderManagerDialog providerPage;private AlertDialog discoveryPage;
    private String providerScopeId="",providerScopeName="";private Button providerScopeButton;
    private TextView gateStatus,status,count,playingTitle,playingEpg,playStatus,emptyMessage;
    private EditText search;
    private ListView channelList;
    private Button favoriteButton,refreshButton,countriesButton,profileButton,showAllDownloaded;
    private PlayerView playerView;
    private Spinner quality;
    private ChannelAdapter adapter;
    private SwirPlayer player;
    private Channel activeChannel;
    private Station activeSource;
    private volatile boolean destroyed,busy;
    private boolean mainVisible,wide,fullScreen;
    private boolean foreground;
    private volatile long epgRefreshRequestedAt;
    private Object fullscreenBackCallback;
    private int tab,playGeneration,hiddenDownloadedMatches;
    private String query="",lastNotice="",qualityMode="Stabilność";
    private final Runnable deferredFilter=this::filter;
    private final Runnable epgClock=new Runnable(){public void run(){if(destroyed||!foreground)return;if(mainVisible){if(adapter!=null)adapter.notifyDataSetChanged();updateEpg();if(!busy&&!library.isEmpty()&&android.os.SystemClock.elapsedRealtime()-epgRefreshRequestedAt>=6*60*60*1000L)refreshEpg(new ArrayList<>(library));}main.postDelayed(this,60000);}};
    static final class Channel {final List<Station> sources=new ArrayList<>();final List<LocalChannelSearch.Entry> searchEntries=new ArrayList<>();String searchable="",category="";Station head(){return sources.get(0);}}
    private static final class ScanRequest {
        final boolean web,initial;final String query;final Set<String> countries;final AtomicBoolean cancelled;
        ScanRequest(boolean web,boolean initial,String query,Set<String> countries,AtomicBoolean cancelled){this.web=web;this.initial=initial;this.query=query;this.countries=new LinkedHashSet<>(countries);this.cancelled=cancelled;}
    }

    @Override public void onCreate(Bundle saved){
        super.onCreate(saved);requestWindowFeature(Window.FEATURE_NO_TITLE);
        settings=getSharedPreferences("interface",MODE_PRIVATE);countries=new LinkedHashSet<>(settings.getStringSet("countries",new HashSet<>(Collections.singletonList("PL"))));
        qualityMode=settings.getString("quality","Stabilność");if(saved!=null){query=saved.getString("query","");if(SourceLinkParser.importCandidate(query))query="";tab=saved.getInt("tab",0);}
        showGate();catalog=new CatalogRepository(this);epg=new EpgRepository(this);profiles=new ProfileStore(this);ai=new PersonalAi(this,profiles.currentId());catalog.setDiscoveryRanking(profiles.currentId(),ai::reorder);
        providers=new ProviderRepository(this,profiles.currentId());reloadProviders(false);
        ScanRequest initialRequest=new ScanRequest(false,true,query,countries,cancellation);work.execute(()->{
            try{loadCountries();List<Station> cached=catalog.load();library=new ArrayList<>(cached);if(!cached.isEmpty()&&catalog.matchesScope(initialRequest.countries))publish(cached,"Zapisany katalog • "+age(catalog.dataAt())+". Odśwież, aby sprawdzić nowe adresy.");else scanInWorker(initialRequest);}
            catch(Exception error){post(()->{showMain();notice("Nie udało się wczytać katalogu: "+message(error));});}
        });
    }
    private void showGate(){
        shell=column();shell.setGravity(Gravity.CENTER);shell.setPadding(dp(28),dp(28),dp(28),dp(28));shell.setBackgroundColor(BG);
        ImageView logo=new ImageView(this);logo.setImageResource(getApplicationInfo().icon);shell.addView(logo,new LinearLayout.LayoutParams(dp(112),dp(112)));
        TextView title=text("SWIRSTREAM AI",27,CYAN);title.setTypeface(null,Typeface.BOLD);title.setGravity(Gravity.CENTER);shell.addView(title,fillWrap());
        TextView by=text("by Swir",15,PURPLE);by.setGravity(Gravity.CENTER);by.setPadding(0,dp(8),0,dp(26));shell.addView(by,fillWrap());
        ProgressBar progress=new ProgressBar(this);progress.setIndeterminateTintList(ColorStateList.valueOf(CYAN));shell.addView(progress,new LinearLayout.LayoutParams(dp(44),dp(44)));
        gateStatus=text("Proszę czekać…\nTrwa pierwsze wyszukiwanie kanałów.",17,TEXT);gateStatus.setGravity(Gravity.CENTER);gateStatus.setPadding(0,dp(20),0,dp(18));shell.addView(gateStatus,fillWrap());
        TextView detail=text("Publiczne katalogi i źródła • "+countryCaption()+"\nObraz i dźwięk sprawdzamy podczas odtwarzania.",13,MUTED);detail.setGravity(Gravity.CENTER);shell.addView(detail,fillWrap());
        Button cancel=button("Przerwij wyszukiwanie",v->{cancellation.set(true);notice("Kończenie wyszukiwania…");});cancel.setTag("startup_cancel");shell.addView(cancel,wrap());
        setContentView(shell);insets(shell,20);
    }
    private void showMain(){
        if(mainVisible)return;mainVisible=true;
        wide=getResources().getConfiguration().screenWidthDp>=700||getResources().getConfiguration().screenWidthDp>getResources().getConfiguration().screenHeightDp;
        shell=column();shell.setBackgroundColor(BG);header=row();header.setGravity(Gravity.CENTER_VERTICAL);
        ImageView logo=new ImageView(this);brandIcon=logo;logo.setImageResource(getApplicationInfo().icon);header.addView(logo,new LinearLayout.LayoutParams(dp(39),dp(39)));
        LinearLayout brand=column();brandBox=brand;brand.setGravity(Gravity.CENTER_VERTICAL);brand.setMinimumHeight(dp(51));TextView title=text("SWIRSTREAM AI",16,CYAN);brandTitle=title;title.setTypeface(null,Typeface.BOLD);title.setSingleLine(true);title.setEllipsize(TextUtils.TruncateAt.END);brand.addView(title);TextView signature=text("by Swir  ·  TV i radio bez granic",11,MUTED);brandSignature=signature;signature.setSingleLine(true);signature.setEllipsize(TextUtils.TruncateAt.END);brand.addView(signature);header.addView(brand,new LinearLayout.LayoutParams(0,-2,1));
        profileButton=button("Profil",v->profileDialog());profileButton.setTag("profiles");header.addView(profileButton,wrap());Button menuButton=button("☰",v->menu(v));headerMenu=menuButton;menuButton.setMinWidth(0);menuButton.setMinimumWidth(0);menuButton.setPadding(0,0,0,0);menuButton.setContentDescription("Menu");header.addView(menuButton,new LinearLayout.LayoutParams(dp(48),dp(48)));wireRowFocus(header);shell.addView(header,fillWrap());
        toolbar=row();Button paste=button("Wklej link",v->pasteProviderLink(""));paste.setTag("paste_source_link");paste.setTextColor(CYAN);toolbar.addView(paste,wrap());Button explorer=button("Eksplorator WWW",v->explorerDialog());explorer.setTag("source_explorer_open");toolbar.addView(explorer,wrap());countriesButton=button(countryCaption(),v->countryDialog());countriesButton.setTag("countries");toolbar.addView(countriesButton,wrap());
        refreshButton=button("Odśwież",v->startScan(false));refreshButton.setTag("refresh");toolbar.addView(refreshButton,wrap());Button deep=button("Deep Search",v->startScan(true));deep.setTag("deep_search");deep.setContentDescription("Głębokie wyszukiwanie kanału w internecie");toolbar.addView(deep,wrap());Button own=button("Moje źródła",v->providerDialog());own.setTag("my_sources");toolbar.addView(own,wrap());toolbar.addView(button("Playlisty",v->playlistDialog()),wrap());wireRowFocus(toolbar);shell.addView(scrollHorizontal(toolbar),fillWrap());
        providerTiles=row();providerTiles.setTag("provider_tiles");String[] tileNames={"Playlisty\nURL","Xtream\nkonta","MAC\nPlayer","DRM\nlicencje"};String[] tileTags={"m3u","xtream","portal","drm"};
        for(int i=0;i<tileNames.length;i++){final int section=i;Button tile=button(tileNames[i],v->providerSection(section));tile.setTag("provider_tile_"+tileTags[i]);tile.setTextSize(12);tile.setSingleLine(false);tile.setMaxLines(2);tile.setGravity(Gravity.CENTER);tile.setPadding(dp(2),0,dp(2),0);tile.setMinWidth(0);tile.setMinimumWidth(0);tile.setContentDescription("Otwórz "+new String[]{"Playlisty URL","Xtream","MAC Player","DRM"}[i]);providerTiles.addView(tile,new LinearLayout.LayoutParams(0,dp(50),1));}wireRowFocus(providerTiles);shell.addView(providerTiles,fillWrap());
        body=new LinearLayout(this);body.setOrientation(wide?LinearLayout.HORIZONTAL:LinearLayout.VERTICAL);body.addOnLayoutChangeListener((v,l,t,r,b,oldL,oldT,oldR,oldB)->{if(playerView!=null)playerView.post(this::fitPortraitPreview);});shell.addView(body,new LinearLayout.LayoutParams(-1,0,1));
        playerPanel=column();playerPanel.setBackground(rounded(PANEL,0));playerPanel.setPadding(dp(8),dp(8),dp(8),dp(8));
        playerView=new PlayerView(this);playerView.setTag("swir_player");playerView.setBackgroundColor(Color.BLACK);playerView.setShowBuffering(PlayerView.SHOW_BUFFERING_WHEN_PLAYING);
        playerPanel.addView(playerView,new LinearLayout.LayoutParams(-1,wide?0:dp(videoHeight()),wide?1:0));
        playingTitle=text("Twój kanał. Twój wybór.",16,TEXT);playingTitle.setTypeface(null,Typeface.BOLD);playingTitle.setSingleLine(true);playingTitle.setEllipsize(TextUtils.TruncateAt.END);playingTitle.setPadding(0,dp(7),0,0);playerPanel.addView(playingTitle,fillWrap());
        playStatus=text("Wybierz kanał z listy. Odtwarzanie pozostaje w aplikacji.",12,CYAN);playStatus.setSingleLine(true);playStatus.setEllipsize(TextUtils.TruncateAt.END);playerPanel.addView(playStatus,fillWrap());
        playingEpg=text("",12,MUTED);playingEpg.setMaxLines(2);playerPanel.addView(playingEpg,fillWrap());compactPlayerDetails();
        LinearLayout controls=row();controls.addView(button("▶ / Ⅱ",v->{if(player!=null)player.toggle();else if(activeChannel!=null)playChannel(activeChannel,null);}),wrap());
        Button full=button("Pełny ekran",v->toggleFullscreen());full.setTag("fullscreen");controls.addView(full,wrap());
        Button sources=button("Warianty",v->sourceDialog());sources.setTag("sources");controls.addView(sources,wrap());favoriteButton=button("☆",v->{if(activeSource!=null)favorite(activeSource);});favoriteButton.setContentDescription("Dodaj lub usuń ulubiony kanał");controls.addView(favoriteButton,wrap());
        controls.addView(button("Stop",v->stopPlayback()),wrap());controls.addView(button("Dźwięk",v->{if(player!=null)player.mute();}),wrap());
        quality=new Spinner(this);quality.setContentDescription("Jakość obrazu");String[] modes={"Stabilność","Najwyższa jakość","Oszczędzaj transfer"};ArrayAdapter<String> choices=new ArrayAdapter<String>(this,android.R.layout.simple_spinner_item,modes){@Override public View getView(int p,View v,ViewGroup parent){TextView t=(TextView)super.getView(p,v,parent);t.setTextColor(TEXT);t.setTextSize(13);return t;}};
        choices.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);quality.setAdapter(choices);quality.setSelection(Arrays.asList(modes).indexOf(qualityMode)<0?0:Arrays.asList(modes).indexOf(qualityMode));quality.setFocusable(true);quality.setBackground(outlineFocus());controls.addView(quality,new LinearLayout.LayoutParams(dp(188),dp(44)));
        quality.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onNothingSelected(AdapterView<?> parent){}public void onItemSelected(AdapterView<?> p,View v,int position,long id){qualityMode=modes[position];settings.edit().putString("quality",qualityMode).apply();if(player!=null)player.setQuality(qualityMode);}});
        wireRowFocus(controls);playerPanel.addView(scrollHorizontal(controls),fillWrap());
        listPanel=column();listPanel.setPadding(wide?dp(10):0,dp(8),0,0);search=new EditText(this);search.setTag("channel_search");search.setTextColor(TEXT);search.setHintTextColor(MUTED);search.setHint("Szukaj kanału lub typu, np. sport");search.setSingleLine(true);search.setTextSize(15);search.setPadding(dp(12),0,dp(12),0);search.setBackground(outlineFocus());search.setText(query);listPanel.addView(search,new LinearLayout.LayoutParams(-1,dp(45)));
        search.setSaveEnabled(false);if(android.os.Build.VERSION.SDK_INT>=26)search.setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_NO_EXCLUDE_DESCENDANTS);
        search.addTextChangedListener(new TextWatcher(){boolean pasted;public void beforeTextChanged(CharSequence s,int a,int c,int d){}public void onTextChanged(CharSequence s,int a,int b,int c){pasted=c>1&&SourceLinkParser.importCandidate(s.toString());query=SourceLinkParser.importCandidate(s.toString())?"":s.toString();main.removeCallbacks(deferredFilter);main.postDelayed(deferredFilter,180);}public void afterTextChanged(Editable s){if(pasted&&SourceLinkParser.importCandidate(s.toString())){pasted=false;String link=s.toString();s.clear();main.post(()->pasteProviderLink(link));}}});
        search.setHint("Szukaj w pobranych kanałach");search.setImeOptions(android.view.inputmethod.EditorInfo.IME_ACTION_SEARCH);search.setOnEditorActionListener((view,action,event)->{if(action==android.view.inputmethod.EditorInfo.IME_ACTION_SEARCH||event!=null&&event.getKeyCode()==android.view.KeyEvent.KEYCODE_ENTER&&event.getAction()==android.view.KeyEvent.ACTION_UP){main.removeCallbacks(deferredFilter);if(!routePrivateSearch())filter();android.view.inputmethod.InputMethodManager keyboard=(android.view.inputmethod.InputMethodManager)getSystemService(INPUT_METHOD_SERVICE);if(keyboard!=null)keyboard.hideSoftInputFromWindow(search.getWindowToken(),0);return true;}return false;});
        LinearLayout tabs=row();channelTabs=tabs;String[] names={"TV","Radio","Ulubione","Historia","Wszystkie"};for(int i=0;i<names.length;i++){final int position=i;Button b=button(names[i],v->{tab=position;filter();});b.setTag(i==4?"tab_all":"tab_"+i);b.setTextSize(12);b.setPadding(dp(3),0,dp(3),0);b.setEllipsize(TextUtils.TruncateAt.END);tabs.addView(b,new LinearLayout.LayoutParams(0,dp(44),1));}listPanel.addView(tabs,fillWrap());
        providerScopeButton=button("",v->{providerScopeId=providerScopeName="";filter();});providerScopeButton.setTag("provider_scope_clear");providerScopeButton.setTextSize(12);providerScopeButton.setEllipsize(TextUtils.TruncateAt.END);providerScopeButton.setContentDescription("Usuń filtr konta i pokaż całą bibliotekę");providerScopeButton.setVisibility(View.GONE);listPanel.addView(providerScopeButton,new LinearLayout.LayoutParams(-1,dp(40)));
        count=text("Przygotowanie listy…",12,MUTED);listPanel.addView(count,fillWrap());
        FrameLayout listFrame=new FrameLayout(this);channelList=new ListView(this);channelList.setTag("channel_list");channelList.setDivider(null);channelList.setCacheColorHint(Color.TRANSPARENT);channelList.setDrawSelectorOnTop(true);channelList.setSelector(rounded(Color.TRANSPARENT,CYAN));channelList.setFastScrollEnabled(true);
        LinearLayout empty=column();empty.setGravity(Gravity.CENTER);empty.setPadding(dp(12),dp(8),dp(12),dp(8));emptyMessage=text("Brak kanałów dla tego filtra.",14,MUTED);emptyMessage.setGravity(Gravity.CENTER);empty.addView(emptyMessage,fillWrap());showAllDownloaded=button("Pokaż wszystkie pobrane",v->{tab=4;providerScopeId=providerScopeName="";if(hiddenDownloadedMatches==0){query="";search.setText("");}filter();});showAllDownloaded.setTag("show_all_downloaded");empty.addView(showAllDownloaded,wrap());listFrame.addView(channelList,new FrameLayout.LayoutParams(-1,-1));listFrame.addView(empty,new FrameLayout.LayoutParams(-1,-1));channelList.setEmptyView(empty);
        adapter=new ChannelAdapter();channelList.setAdapter(adapter);channelList.setOnItemClickListener((p,v,position,id)->playChannel(visible.get(position),null));channelList.setOnItemLongClickListener((p,v,position,id)->{channelActions(visible.get(position));return true;});listPanel.addView(listFrame,new LinearLayout.LayoutParams(-1,0,1));
        if(wide){body.addView(listPanel,new LinearLayout.LayoutParams(0,-1,1));body.addView(playerPanel,new LinearLayout.LayoutParams(0,-1,1.3f));}else{body.addView(playerPanel,fillWrap());body.addView(listPanel,new LinearLayout.LayoutParams(-1,0,1));}
        status=text(lastNotice,12,MUTED);status.setSingleLine(true);status.setEllipsize(TextUtils.TruncateAt.END);status.setPadding(0,dp(6),0,0);shell.addView(status,fillWrap());setContentView(shell);insets(shell,8);compactSourceControls();filter();
    }
    private boolean routePrivateSearch(){String value=search==null?query:search.getText().toString();if(!SourceLinkParser.importCandidate(value))return false;query="";if(search!=null)search.setText("");pasteProviderLink(value);return true;}
    private void startScan(boolean web){
        if(routePrivateSearch())return;
        if(SourceLinkParser.importCandidate(query)){String link=query;query="";if(search!=null)search.setText("");pasteProviderLink(link);return;}
        if(busy){new AlertDialog.Builder(this).setMessage("Wyszukiwanie już trwa. Możesz je przerwać; dotychczasowa lista zostanie zachowana.").setPositiveButton("Przerwij",(d,w)->cancellation.set(true)).setNegativeButton("Kontynuuj",null).show();return;}
        if(web&&query.trim().isEmpty()){notice("Wpisz nazwę kanału lub temat przed Deep Search.");search.requestFocus();return;}
        busy=true;cancellation=new AtomicBoolean();ScanRequest request=new ScanRequest(web,false,query,countries,cancellation);if(refreshButton!=null)refreshButton.setText("Trwa…");notice(web?"AI szuka publicznych źródeł dla: "+query:"Wyszukiwanie kanałów… Dotychczasowa lista pozostaje dostępna.");work.execute(()->scanInWorker(request));
    }
    private void scanInWorker(ScanRequest request){
        if(request.initial)busy=true;boolean web=request.web;Set<String> selected=request.countries;AtomicBoolean cancel=request.cancelled;String requested=request.query;
        try{
            List<Station> found=web?catalog.searchWeb(requested,selected,this::notice,cancel):catalog.scanAll(selected,this::notice,cancel);
            String deepInfo="";if(web){int live=0;for(Station s:found)if(s.freshManifest())live++;deepInfo="Deep Search: "+found.size()+" pasujących adresów • "+live+" manifestów live. ";List<Station> merged=new ArrayList<>(found);merged.addAll(catalog.load());found=CatalogRepository.deduplicate(merged);catalog.save(found);}
            if(found.isEmpty()&&!library.isEmpty())found=new ArrayList<>(library);
            publish(found,cancel.get()?"Wyszukiwanie przerwano. Zachowano dostępne wyniki.":deepInfo+"Katalog: "+found.size()+" adresów • "+age(catalog.dataAt())+". Obraz sprawdza odtwarzacz.");
        }catch(Exception error){post(()->{channels=group(library);showMain();filter();notice(cancel.get()?"Wyszukiwanie przerwano. Zachowano poprzedni katalog.":"Nie udało się odświeżyć: "+message(error)+". Poprzednia lista pozostaje dostępna.");});}
        finally{busy=false;post(()->{if(refreshButton!=null)refreshButton.setText("Odśwież");});}
    }
    private void publish(List<Station> data,String info){
        List<Station> snapshot=new ArrayList<>(data);post(()->{library=snapshot;channels=group(snapshot);showMain();filter();notice(info);persistenceNotice();});
        if(!cancellation.get()&&!snapshot.isEmpty())refreshEpg(snapshot);
    }
    private List<Channel> group(List<Station> data){
        List<Station> ranked=new ArrayList<>(data);ranked.addAll(providerLibrary);ai.reorder(ranked);Map<String,Channel> result=new LinkedHashMap<>();Set<String> unique=new HashSet<>();
        for(Station station:ranked){if(!unique.add(station.sourceKey()))continue;Channel c=result.get(station.key());if(c==null){c=new Channel();describe(c,station);result.put(station.key(),c);}c.sources.add(station);c.searchEntries.add(LocalChannelSearch.index(station));}return new ArrayList<>(result.values());
    }
    private void describe(Channel c,Station s){c.category=topic(ai.category(s));c.searchable=normalize(s.name+" "+s.group+" "+c.category+" "+s.country);}
    private void refreshEpg(List<Station> data){epgCancellation.set(true);AtomicBoolean cancel=new AtomicBoolean();epgCancellation=cancel;Set<String> selected=new LinkedHashSet<>(countries);if(destroyed)return;epgRefreshRequestedAt=android.os.SystemClock.elapsedRealtime();epgWork.execute(()->{try{epg.refresh(selected,data,text->{if(!busy&&!cancel.get())notice(text);},cancel);if(cancel.get())return;post(()->{if(adapter!=null)adapter.notifyDataSetChanged();updateEpg();if(!busy)notice("EPG: "+age(epg.dataAt())+" • Profil: "+profiles.current());});}catch(Exception e){if(!cancel.get())notice("Ramówka chwilowo niedostępna. Kanały pozostają dostępne.");}});}
    private void filter(){
        if(!mainVisible||adapter==null)return;List<Channel> source=channels;
        if(tab==2||tab==3){List<Station> stored=tab==2?profiles.favorites():profiles.history();Map<String,Channel> byId=new HashMap<>();for(Channel c:channels)byId.put(c.head().key(),c);source=new ArrayList<>();for(Station s:stored){Channel c=byId.get(s.key());if(c==null){c=new Channel();c.sources.add(s);c.searchEntries.add(LocalChannelSearch.index(s));describe(c,s);}source.add(c);}}
        List<Channel> result=new ArrayList<>();LocalChannelSearch.Query searchQuery=LocalChannelSearch.prepare(query);for(Channel c:source){Station s=c.head();if(!providerScopeId.isEmpty()&&!providerScopeId.equals(s.providerId))continue;if(tab==0&&"radio".equalsIgnoreCase(s.kind)||tab==1&&!"radio".equalsIgnoreCase(s.kind))continue;
            if(tab<2&&!s.isProvider()&&!countries.isEmpty()&&!countries.contains(s.country.toUpperCase(Locale.ROOT)))continue;
            if(localMatch(searchQuery,c))result.add(c);}
        int allMatches=0;if(tab!=4)for(Channel c:channels)if((providerScopeId.isEmpty()||providerScopeId.equals(c.head().providerId))&&localMatch(searchQuery,c))allMatches++;
        int hidden=Math.max(0,allMatches-result.size());hiddenDownloadedMatches=hidden;visible=result;adapter.notifyDataSetChanged();String scope=new String[]{"TV","Radio","Ulubione","Historia","Wszystkie pobrane"}[tab];
        providerScopeButton.setVisibility(providerScopeId.isEmpty()?View.GONE:View.VISIBLE);channelTabs.setVisibility(providerScopeId.isEmpty()?View.VISIBLE:View.GONE);providerScopeButton.setText("× "+providerScopeName+" • pokaż całą bibliotekę");if(playerView!=null)playerView.post(this::fitPortraitPreview);
        count.setText(result.size()+" kanałów • "+scope+(hidden>0?" • +"+hidden+" poza filtrami":" • Profil: "+profiles.current()));
        emptyMessage.setText(hidden>0?"Pasujące kanały są pobrane, ale ukrywa je obecny kraj lub zakładka.":"Brak pobranych kanałów dla tej nazwy.\nNowych źródeł szukaj przyciskiem Deep Search.");showAllDownloaded.setText(hidden>0?"Pokaż wszystkie pobrane":"Wyczyść wyszukiwanie");showAllDownloaded.setVisibility(hidden>0||!query.trim().isEmpty()&&!channels.isEmpty()?View.VISIBLE:View.GONE);if(countriesButton!=null)countriesButton.setText(countryCaption());
    }
    private static boolean localMatch(LocalChannelSearch.Query query,Channel channel){for(LocalChannelSearch.Entry entry:channel.searchEntries)if(query.matches(entry))return true;return query.matchesText(channel.category);}
    private void playChannel(Channel channel,Station chosen){activeChannel=channel;attempted.clear();openSource(chosen==null?channel.head():chosen);}
    private void openSource(Station station){
        if(destroyed)return;playbackCancellation.set(true);playbackCancellation=new AtomicBoolean();if(player!=null){player.close();player=null;}final int generation=++playGeneration;activeSource=station;attempted.add(station.sourceKey());
        playingTitle.setText(station.name);playStatus.setText("Łączenie • źródło "+attempted.size()+" z "+activeChannel.sources.size());favoriteButton.setText(profiles.favorite(station)?"★":"☆");updateEpg();
        if(station.isProvider()){
            ProviderRepository repository=providers;AtomicBoolean cancel=playbackCancellation;
            playbackWork.execute(()->{try{Station resolved=repository.resolve(station,cancel);post(()->{if(generation==playGeneration&&repository==providers&&!cancel.get())startPlayer(station,resolved,generation);});}
                catch(Exception error){post(()->{if(generation==playGeneration&&!cancel.get())sourceFailed(station,"Nie można otworzyć własnego źródła. Sprawdź konto w Moje źródła.",generation,0);});}});
        }else startPlayer(station,station,generation);
    }
    private void startPlayer(Station reference,Station resolved,int generation){
        final PersonalAi learning=ai;try{player=new SwirPlayer(this,playerView,resolved,qualityMode,new SwirPlayer.Listener(){
            public void state(String text){if(generation==playGeneration&&!destroyed)playStatus.setText(text);}
            public void playbackActive(boolean active){if(generation!=playGeneration||destroyed)return;if(active&&foreground)getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);else getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);}
            public void confirmed(Station actual,long startup){if(generation!=playGeneration||destroyed)return;learning.playback(reference,true,startup);profiles.watched(reference);playStatus.setText("Odtwarzanie potwierdzone • "+String.format(Locale.ROOT,"%.1f",startup/1000.0)+" s • "+sourceLabel(reference));persistenceNotice();}
            public void watchedThirtySeconds(Station actual){if(generation==playGeneration&&!destroyed){learning.preference(reference,true);persistenceNotice();}}
            public void failed(Station actual,String reason,long elapsed){sourceFailed(reference,reason,generation,elapsed);}
        });}catch(Exception error){sourceFailed(reference,"Odtwarzacz nie obsługuje tego źródła lub DRM na tym urządzeniu.",generation,0);}
    }
    private void sourceFailed(Station reference,String reason,int generation,long elapsed){if(generation!=playGeneration||destroyed)return;ai.playback(reference,false,elapsed);post(()->{if(generation!=playGeneration)return;Station next=null;if(attempted.size()<10&&activeChannel!=null)for(Station s:activeChannel.sources)if(!attempted.contains(s.sourceKey())){next=s;break;}if(next!=null){notice("Źródło nie odpowiada. Próbuję kolejnego adresu tego kanału.");openSource(next);}else{playStatus.setText(reason+" Wybierz inne źródło lub odśwież listę.");getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);}});}
    private void stopPlayback(){++playGeneration;playbackCancellation.set(true);if(player!=null){player.close();player=null;}getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);if(playStatus!=null)playStatus.setText("Odtwarzanie zatrzymane");}
    private static String sourceLabel(Station station){return station.isProvider()?"Moje źródła • "+station.origin:host(station.url);}
    private void sourceDialog(){if(activeChannel==null){notice("Najpierw wybierz kanał z listy.");return;}String[] items=new String[activeChannel.sources.size()];for(int i=0;i<items.length;i++){Station s=activeChannel.sources.get(i);items[i]=(i+1)+". "+sourceLabel(s)+"\n"+s.availability()+"\n"+s.origin+(s.sourceKey().equals(activeSource.sourceKey())?" • wybrane":"");}new AlertDialog.Builder(this).setTitle("Źródła • "+activeChannel.head().name).setItems(items,(d,i)->playChannel(activeChannel,activeChannel.sources.get(i))).setNeutralButton("Nie interesuje mnie",(d,w)->{if(activeSource!=null){ai.preference(activeSource,false);channels=group(library);filter();notice("Zapisano preferencję tego profilu.");persistenceNotice();}}).setNegativeButton("Zamknij",null).show();}
    private void favorite(Station s){boolean value=!profiles.favorite(s);profiles.setFavorite(s,value);if(value)ai.preference(s,true);if(activeSource!=null&&activeSource.key().equals(s.key()))favoriteButton.setText(value?"★":"☆");filter();notice(value?"Dodano do ulubionych bieżącego profilu.":"Usunięto z ulubionych.");persistenceNotice();}
    private void channelActions(Channel c){new AlertDialog.Builder(this).setTitle(c.head().name).setItems(new String[]{"Odtwórz",profiles.favorite(c.head())?"Usuń z ulubionych":"Dodaj do ulubionych","Nie interesuje mnie"},(d,i)->{if(i==0)playChannel(c,null);else if(i==1)favorite(c.head());else{ai.preference(c.head(),false);channels=group(library);filter();notice("Zapisano preferencję dla tego profilu.");}}).show();}
    private String epgNow(Station station){return station.isProvider()?providers.now(station):epg.now(station);}
    private String epgNext(Station station){return station.isProvider()?providers.next(station):epg.next(station);}
    private void updateEpg(){if(playingEpg==null||activeSource==null)return;String now=epgNow(activeSource),next=epgNext(activeSource);playingEpg.setText((now==null||now.isEmpty()?"EPG: brak aktualnej ramówki":now)+(next==null||next.isEmpty()?"":"\n"+next));}
    private void toggleFullscreen(){fullScreen=!fullScreen;header.setVisibility(fullScreen?View.GONE:View.VISIBLE);providerTiles.setVisibility(fullScreen?View.GONE:View.VISIBLE);((View)toolbar.getParent()).setVisibility(fullScreen?View.GONE:View.VISIBLE);listPanel.setVisibility(fullScreen?View.GONE:View.VISIBLE);status.setVisibility(fullScreen?View.GONE:View.VISIBLE);
        if(!wide){playerPanel.setLayoutParams(fullScreen?new LinearLayout.LayoutParams(-1,0,1):fillWrap());playerView.setLayoutParams(new LinearLayout.LayoutParams(-1,fullScreen?0:dp(videoHeight()),fullScreen?1:0));}
        if(Build.VERSION.SDK_INT>=33){if(fullScreen&&fullscreenBackCallback==null)fullscreenBackCallback=Api33Back.register(this,this::toggleFullscreen);else if(!fullScreen)clearFullscreenBack();}playerView.requestFocus();}
    // API33+ gestures use the registered platform dispatcher; this is retained for
    // older Android and hardware-remote compatibility, not as the gesture handler.
    @android.annotation.SuppressLint("GestureBackNavigation")
    @Override public void onBackPressed(){if(fullScreen)toggleFullscreen();else super.onBackPressed();}
    private void clearFullscreenBack(){if(Build.VERSION.SDK_INT>=33&&fullscreenBackCallback!=null){Api33Back.unregister(this,fullscreenBackCallback);fullscreenBackCallback=null;}}
    @android.annotation.TargetApi(33)
    private static final class Api33Back {
        static Object register(Activity activity,Runnable action){android.window.OnBackInvokedCallback callback=action::run;activity.getOnBackInvokedDispatcher().registerOnBackInvokedCallback(android.window.OnBackInvokedDispatcher.PRIORITY_DEFAULT,callback);return callback;}
        static void unregister(Activity activity,Object callback){activity.getOnBackInvokedDispatcher().unregisterOnBackInvokedCallback((android.window.OnBackInvokedCallback)callback);}
    }
    @Override public void onConfigurationChanged(Configuration configuration){super.onConfigurationChanged(configuration);if(!mainVisible)return;if(fullScreen)toggleFullscreen();wide=configuration.screenWidthDp>=700||configuration.screenWidthDp>configuration.screenHeightDp;compactPlayerDetails();compactSourceControls();if(adapter!=null)adapter.notifyDataSetChanged();body.removeAllViews();body.setOrientation(wide?LinearLayout.HORIZONTAL:LinearLayout.VERTICAL);listPanel.setPadding(wide?dp(10):0,dp(8),0,0);playerView.setLayoutParams(new LinearLayout.LayoutParams(-1,wide?0:dp(videoHeight()),wide?1:0));if(wide){body.addView(listPanel,new LinearLayout.LayoutParams(0,-1,1));body.addView(playerPanel,new LinearLayout.LayoutParams(0,-1,1.3f));}else{body.addView(playerPanel,fillWrap());body.addView(listPanel,new LinearLayout.LayoutParams(-1,0,1));}shell.requestApplyInsets();}
    @Override protected void onResume(){super.onResume();foreground=true;main.removeCallbacks(epgClock);main.post(epgClock);}
    @Override protected void onPause(){foreground=false;main.removeCallbacks(epgClock);if(player!=null)player.pause();getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);super.onPause();}
    @Override protected void onStop(){stopPlayback();super.onStop();}
    @Override protected void onDestroy(){destroyed=true;if(scannerPage!=null)scannerPage.close();pendingScannerImport=pendingScannerExport=null;pendingScannerCsv="";scannerWork.shutdownNow();if(explorerPage!=null)explorerPage.close();explorerWork.shutdownNow();if(discoveryPage!=null)discoveryPage.dismiss();if(providerPage!=null)providerPage.closePage();clearFullscreenBack();cancellation.set(true);epgCancellation.set(true);playbackCancellation.set(true);main.removeCallbacksAndMessages(null);if(player!=null)player.close();work.shutdownNow();epgWork.shutdownNow();providerWork.shutdownNow();playbackWork.shutdownNow();super.onDestroy();}
    @Override public void onSaveInstanceState(Bundle out){out.putString("query",SourceLinkParser.importCandidate(query)?"":query);out.putInt("tab",tab);super.onSaveInstanceState(out);}
    private void countryDialog(){
        if(countryNames.isEmpty()){notice("Lista krajów nie jest jeszcze gotowa.");return;}boolean[] selected=new boolean[countryCodes.size()];for(int i=0;i<selected.length;i++)selected[i]=countries.contains(countryCodes.get(i));
        new AlertDialog.Builder(this).setTitle("Kraje wyszukiwania • maks. 12").setMultiChoiceItems(countryNames.toArray(new String[0]),selected,(d,i,checked)->selected[i]=checked).setPositiveButton("Zastosuj",(d,w)->{Set<String> values=new LinkedHashSet<>();for(int i=0;i<selected.length;i++)if(selected[i])values.add(countryCodes.get(i));if(values.isEmpty()||values.size()>12){notice("Wybierz od 1 do 12 krajów albo użyj opcji Cały świat.");return;}setCountries(values);}).setNeutralButton("Cały świat",(d,w)->setCountries(new LinkedHashSet<>())).setNegativeButton("Anuluj",null).show();
    }
    private void setCountries(Set<String> values){countries=values;settings.edit().putStringSet("countries",new HashSet<>(values)).apply();filter();notice("Wybrane kraje: "+countryCaption()+". Odśwież katalog lub szukaj w sieci.");}
    private void profileDialog(){List<String> names=profiles.profiles();String[] items=new String[names.size()+1];for(int i=0;i<names.size();i++)items[i]=names.get(i)+(names.get(i).equals(profiles.current())?" ✓":"");items[names.size()]="＋ Nowy profil";
        new AlertDialog.Builder(this).setTitle("Profil na tym urządzeniu").setItems(items,(d,i)->{if(i==names.size()){EditText field=new EditText(this);field.setSingleLine();field.setHint("Nazwa profilu");new AlertDialog.Builder(this).setTitle("Nowy profil").setView(field).setPositiveButton("Utwórz",(dialog,which)->{try{profiles.add(field.getText().toString());profileChanged();}catch(Exception e){notice(message(e));}}).setNegativeButton("Anuluj",null).show();}else{profiles.select(names.get(i));profileChanged();}}).setNegativeButton("Zamknij",null).show();}
    private void profileChanged(){if(scannerPage!=null)scannerPage.close();pendingScannerImport=pendingScannerExport=null;pendingScannerCsv="";if(explorerPage!=null)explorerPage.close();providerScopeId=providerScopeName="";stopPlayback();activeChannel=null;activeSource=null;ai=new PersonalAi(this,profiles.currentId());catalog.setDiscoveryRanking(profiles.currentId(),ai::reorder);providerLibrary=new ArrayList<>();providers=new ProviderRepository(this,profiles.currentId());reloadProviders(false);channels=group(library);filter();playingTitle.setText("Profil: "+profiles.current());playingEpg.setText("");notice("Osobne źródła, ulubione, historia i lokalne preferencje • "+profiles.current());persistenceNotice();}
    private ProviderManagerDialog.Listener providerListener(ProviderRepository repository){return new ProviderManagerDialog.Listener(){public boolean active(){return repository==providers&&!destroyed;}public void changed(){if(repository==providers&&!destroyed)reloadProviders(false);}public void showChannels(String id,String name){if(repository!=providers||destroyed)return;providerScopeId=id;providerScopeName=name;reloadProviders(true);}public void discoverPlaylists(){if(repository==providers&&!destroyed)playlistDiscoveryDialog();}public void exploreSources(){if(repository==providers&&!destroyed)explorerDialog();}public void scanPortals(){if(repository==providers&&!destroyed)scannerDialog();}};}
    private void explorerDialog(){explorerDialog(false);}
    private void explorerDialog(boolean portals){if(destroyed)return;if(explorerPage!=null)explorerPage.close();ProviderRepository owner=providers;if(explorerEngine==null)explorerEngine=new SourceExplorer(this);explorerPage=new SourceExplorerDialog(this,explorerEngine,new SourceExplorerStore(this,profiles.currentId()),explorerWork,new SourceExplorerDialog.Listener(){public boolean active(){return owner==providers&&!destroyed;}public void privateLink(String value){if(active())pasteProviderLink(value);}public void add(SourceCandidate candidate){if(active())providerPage=ProviderManagerDialog.prefillCandidate(MainActivity.this,owner,providerWork,providerListener(owner),candidate);}},countries,countryCodes,countryNames);if(portals)explorerPage.showPortals();else explorerPage.show();}
    private void scannerDialog(){
        if(destroyed)return;if(scannerPage!=null)scannerPage.close();ProviderRepository owner=providers;if(scannerEngine==null)scannerEngine=new PortalScanner(this);
        scannerPage=new PortalScannerDialog(this,owner,scannerWork,scannerEngine,new PortalScannerDialog.Listener(){
            public boolean active(){return owner==providers&&!destroyed;}
            public void chooseFile(PortalScannerDialog page){pendingScannerImport=page;try{Intent intent=new Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("*/*");startActivityForResult(intent,IMPORT_PORTALS);}catch(Exception error){try{startActivityForResult(new Intent(Intent.ACTION_GET_CONTENT).addCategory(Intent.CATEGORY_OPENABLE).setType("*/*"),IMPORT_PORTALS);}catch(Exception missing){pendingScannerImport=null;notice("Brak aplikacji do wyboru pliku TXT.");}}}
            public void exportReport(PortalScannerDialog page,String csv){pendingScannerExport=page;pendingScannerCsv=csv;try{startActivityForResult(new Intent(Intent.ACTION_CREATE_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("text/csv").putExtra(Intent.EXTRA_TITLE,"SwirStream-raport-portali.csv"),EXPORT_PORTALS);}catch(Exception error){pendingScannerExport=null;pendingScannerCsv="";notice("Nie można otworzyć zapisu raportu.");}}
            public void discover(){if(active())explorerDialog(true);}
            public void configure(String url){if(active())providerPage=ProviderManagerDialog.prefillPortal(MainActivity.this,owner,providerWork,providerListener(owner),url);}
            public void saved(ProviderRepository.SourceInfo source,boolean play){if(!active())return;providerScopeId=source.id;providerScopeName=source.name;reloadProviders(true);if(play)providerWork.execute(()->{List<Station> stored=owner.stations();List<Station> candidates=new ArrayList<>();for(Station station:stored)if(source.id.equals(station.providerId)&&!station.name.trim().startsWith("#")&&!station.name.trim().startsWith("---")&&!station.name.toLowerCase(Locale.ROOT).contains("xxx"))candidates.add(station);post(()->{if(!active())return;if(candidates.isEmpty()){notice("Źródło zapisano. Brak kanału do próby.");return;}Station sample=candidates.get(0);Channel channel=new Channel();describe(channel,sample);channel.sources.add(sample);channel.searchEntries.add(LocalChannelSearch.index(sample));playChannel(channel,sample);});});}
        });scannerPage.show();
    }
    private void providerDialog(){ProviderRepository repository=providers;providerPage=ProviderManagerDialog.showSection(this,repository,providerWork,-1,providerListener(repository));}
    private void pasteProviderLink(String initial){if(destroyed)return;ProviderRepository repository=providers;if(providerPage!=null)providerPage.closePage();providerPage=ProviderManagerDialog.pasteLink(this,repository,providerWork,providerListener(repository),initial);}
    private void providerSection(int section){ProviderRepository repository=providers;providerPage=ProviderManagerDialog.showSection(this,repository,providerWork,section,providerListener(repository));}
    private void reloadProviders(boolean reveal){
        ProviderRepository repository=providers;providerWork.execute(()->{try{List<Station> saved=repository.stations();post(()->{if(repository!=providers)return;providerLibrary=saved;if(!providerScopeId.isEmpty()){boolean exists=false;for(Station station:saved)if(providerScopeId.equals(station.providerId)){exists=true;break;}if(!exists)providerScopeId=providerScopeName="";}channels=group(library);if(!mainVisible&&!saved.isEmpty())showMain();if(reveal){tab=4;query="";if(search!=null)search.setText("");}filter();updateEpg();if(reveal)notice(providerScopeId.isEmpty()?"Moje źródła: "+saved.size()+" kanałów w tym profilu.":"Kanały źródła: "+providerScopeName+". Przycisk × przywraca całą bibliotekę.");});}
        catch(Exception error){notice("Nie można odczytać własnych źródeł. Zapisane pliki pozostały bez zmian.");}});
    }
    private void learningDialog(){String message="Profil: "+profiles.current()+"\n\nTasteNet rozpoznaje temat kanału. Lokalne AI porządkuje źródła i uczy się z ulubionych, Twoich ocen oraz 30 sekund rzeczywistego odtwarzania.\n\nPreferencje: "+ai.preferenceCount()+" • obserwacje źródeł: "+ai.sourceCount()+"\nUczenie: "+(ai.enabled()?"włączone":"wyłączone");
        new AlertDialog.Builder(this).setTitle("Lokalne AI").setMessage(message).setPositiveButton(ai.enabled()?"Wyłącz uczenie":"Włącz uczenie",(d,w)->{ai.setEnabled(!ai.enabled());channels=group(library);filter();persistenceNotice();}).setNeutralButton("Wyczyść naukę",(d,w)->new AlertDialog.Builder(this).setTitle("Wyczyścić naukę profilu?").setMessage("Usunie wyuczone preferencje, oceny źródeł i historię przeglądania źródeł przez AI. Ulubione oraz historia oglądania zostaną zachowane.").setPositiveButton("Wyczyść",(a,b)->{ai.reset();catalog.resetDiscoveryLearning(profiles.currentId());channels=group(library);filter();persistenceNotice();}).setNegativeButton("Anuluj",null).show()).setNegativeButton("Zamknij",null).show();}
    private void menu(View anchor){PopupMenu menu=new PopupMenu(this,anchor);String[] items={"Playlisty M3U","Profile","Lokalne AI","Źródło EPG","Stan źródeł","Moje źródła","Testery Streamtest","O aplikacji"};for(int i=0;i<items.length;i++)menu.getMenu().add(0,i,0,items[i]);menu.setOnMenuItemClickListener(item->{switch(item.getItemId()){case 0:playlistDialog();break;case 1:profileDialog();break;case 2:learningDialog();break;case 3:epgDialog();break;case 4:diagnostics();break;case 5:providerDialog();break;case 6:streamtestDialog();break;default:new AlertDialog.Builder(this).setTitle("SwirStream AI 0.3.7 • by Swir").setMessage("Odtwarzacz TV i radia na Androida. Swir Player używa Android Media3; AI działa lokalnie i porządkuje wyniki.\n\nPubliczne źródła mogą zmieniać adres, wymagać abonamentu lub być niedostępne w danym kraju. Aplikacja nie zapewnia wszystkich kanałów i nie omija zabezpieczeń.\n\nPilot: strzałki wybierają element, OK uruchamia, Wstecz opuszcza pełny ekran.").setNeutralButton("Licencje",(d,w)->licenses()).setPositiveButton("OK",null).show();}return true;});menu.show();}
    private void streamtestDialog(){new AlertDialog.Builder(this).setTitle("Testery Streamtest").setMessage("Zewnętrzne narzędzia do diagnozowania streamów. Otwieramy samą stronę, bez przekazywania adresu kanału i danych konta. Testy publikowane w serwisie mogą być widoczne dla innych. Własne źródła odtwarzaj i sprawdzaj bezpośrednio w aplikacji.").setItems(new String[]{"Streamtest.co","Streamtest.in — publiczne logi"},(d,i)->{try{startActivity(new Intent(Intent.ACTION_VIEW,Uri.parse(i==0?"https://streamtest.co/":"https://streamtest.in/logs")));}catch(ActivityNotFoundException error){notice("Brak przeglądarki. Adresy: streamtest.co oraz streamtest.in/logs.");}}).setNegativeButton("Zamknij",null).show();}
    private void diagnostics(){List<String> lines=catalog.diagnostics();String text=String.join("\n\n",lines)+"\n\nKatalog: "+age(catalog.dataAt())+"\nEPG: "+age(epg.dataAt())+"\nMoje źródła: "+providerLibrary.size()+" kanałów w profilu\n"+SwirPlayer.drmCapabilities()+"\n\nAdres z katalogu nie jest potwierdzeniem działania. Sukces źródła zapisujemy po obrazie lub odtwarzanym dźwięku.";new AlertDialog.Builder(this).setTitle("Stan źródeł").setMessage(text).setPositiveButton("OK",null).show();}
    private void epgDialog(){LinearLayout form=column();form.setPadding(dp(20),dp(8),dp(20),0);EditText country=new EditText(this);country.setSingleLine();country.setHint("Kod kraju, np. PL");country.setText(countries.isEmpty()?"PL":countries.iterator().next());form.addView(country);EditText url=new EditText(this);url.setSingleLine();url.setHint("https://…/epg.xml lub epg.xml.gz");form.addView(url);TextView hint=text("Puste pole adresu przywraca publiczne źródła EPG.",13,MUTED);form.addView(hint);
        new AlertDialog.Builder(this).setTitle("Własna ramówka XMLTV").setView(form).setPositiveButton("Zapisz",(d,w)->{try{if(SourcePrivacy.requiresVault(url.getText().toString()))throw new IOException("EPG z danymi dostępu dodaj w Moje źródła razem z playlistą lub kontem.");epg.setSource(country.getText().toString().trim(),url.getText().toString().trim());work.execute(()->refreshEpg(new ArrayList<>(library)));}catch(Exception e){notice(message(e));}}).setNegativeButton("Anuluj",null).show();}
    private void playlistDialog(){new AlertDialog.Builder(this).setTitle("Playlisty M3U").setItems(new String[]{"Importuj plik playlisty","Eksportuj widoczne kanały ("+visible.size()+")","Pobierz znalezioną playlistę","Dodaj listę z URL lub konto"},(d,i)->{if(i==3){providerDialog();return;}if(i==0)choosePlaylist();else if(i==1){List<Station> selected=new ArrayList<>();for(Channel c:visible)for(Station station:c.sources)if(!station.isProvider())selected.add(station);if(selected.isEmpty()){notice("Brak publicznych kanałów do eksportu. Własne konta i chronione adresy pozostają w Moje źródła.");return;}export(catalog.exportM3u(selected),"SwirStream-"+new java.text.SimpleDateFormat("yyyyMMdd",Locale.ROOT).format(new Date())+".m3u");}else foundPlaylists();}).setNegativeButton("Zamknij",null).show();}
    private void choosePlaylist(){
        try{startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("*/*"),IMPORT);}
        catch(ActivityNotFoundException unavailable){try{startActivityForResult(new Intent(Intent.ACTION_GET_CONTENT).addCategory(Intent.CATEGORY_OPENABLE).setType("*/*"),IMPORT);}catch(ActivityNotFoundException|SecurityException missing){notice("Na tym urządzeniu brakuje aplikacji do wybierania plików. Zainstaluj menedżer plików obsługujący wybór dokumentów i spróbuj ponownie.");}}
        catch(SecurityException denied){notice("System nie pozwala otworzyć wyboru plików. Sprawdź ustawienia urządzenia.");}
    }
    private void playlistDiscoveryDialog(){
        if(busy){notice("Wyszukiwanie już trwa. Zaczekaj na wyniki lub przerwij je przyciskiem Odśwież.");return;}
        LinearLayout box=column();box.setPadding(dp(18),dp(8),dp(18),0);box.setTag("playlist_discovery_form");EditText term=new EditText(this);term.setTag("playlist_discovery_query");term.setSaveEnabled(false);if(android.os.Build.VERSION.SDK_INT>=26)term.setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_NO_EXCLUDE_DESCENDANTS);term.setSingleLine(true);term.setHint("Kanał lub temat, np. TVP lub sport");term.setText(query);box.addView(term,fillWrap());Set<String> selected=new LinkedHashSet<>(countries);
        Button areas=button("Kraje: "+(selected.isEmpty()?"cały świat":String.join(", ",selected)),v->{boolean[] checked=new boolean[countryCodes.size()];for(int i=0;i<checked.length;i++)checked[i]=selected.contains(countryCodes.get(i));new AlertDialog.Builder(this).setTitle("Kraje playlist • maks. 12").setMultiChoiceItems(countryNames.toArray(new String[0]),checked,(d,i,on)->checked[i]=on).setPositiveButton("Zastosuj",(d,w)->{Set<String> value=new LinkedHashSet<>();for(int i=0;i<checked.length;i++)if(checked[i])value.add(countryCodes.get(i));if(value.size()>12||value.isEmpty()){notice("Wybierz od 1 do 12 krajów lub Cały świat.");return;}selected.clear();selected.addAll(value);((Button)v).setText("Kraje: "+String.join(", ",selected));}).setNeutralButton("Cały świat",(d,w)->{selected.clear();((Button)v).setText("Kraje: cały świat");}).setNegativeButton("Anuluj",null).show();});areas.setTag("playlist_discovery_countries");box.addView(areas,fillWrap());box.addView(text("AI porządkuje publiczne playlisty według dopasowań i preferencji profilu. Wyszukiwanie obejmuje katalogi oraz strony nadawców; nie wyszukuje kont, haseł, MAC ani licencji DRM.",13,MUTED),fillWrap());
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle("AI: znajdź playlisty").setView(box).setPositiveButton("Szukaj playlist",null).setNegativeButton("Anuluj",null).create();dialog.setOnShowListener(d->dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v->{String requested=term.getText().toString().trim();if(requested.isEmpty()){term.setError("Wpisz kanał lub temat");return;}if(SourceLinkParser.importCandidate(requested)){term.setText("");dialog.dismiss();pasteProviderLink(requested);return;}dialog.dismiss();discoverPlaylists(requested,new LinkedHashSet<>(selected));}));dialog.show();
    }
    private void discoverPlaylists(String requested,Set<String> selected){
        if(SourceLinkParser.importCandidate(requested)){pasteProviderLink(requested);return;}
        if(busy)return;busy=true;AtomicBoolean cancel=new AtomicBoolean();cancellation=cancel;ProviderRepository owner=providers;PersonalAi learning=ai;
        TextView progress=text("Przygotowanie wyszukiwania…",14,TEXT);progress.setPadding(dp(18),dp(12),dp(18),dp(12));AlertDialog dialog=new AlertDialog.Builder(this).setTitle("AI szuka publicznych playlist").setView(progress).setNegativeButton("Przerwij",(d,w)->cancel.set(true)).create();dialog.setCancelable(false);dialog.show();if(refreshButton!=null)refreshButton.setText("Trwa…");
        work.execute(()->{try{List<Station> results=catalog.searchWeb(requested,selected,value->post(()->{progress.setText(value);notice(value);}),cancel);PublicHttp.checkCancelled(cancel);List<PlaylistSuggestions.Entry> matches=new ArrayList<>();for(CatalogRepository.FoundPlaylist playlist:catalog.playlists()){PublicHttp.checkCancelled(cancel);PlaylistSuggestions.Entry entry=PlaylistSuggestions.match(playlist,catalog.importM3u(playlist.text,playlist.source),requested,selected,learning::score);if(entry!=null)matches.add(entry);}List<PlaylistSuggestions.Entry> ranked=PlaylistSuggestions.rank(matches);List<String> notes=catalog.diagnostics();
            if(!results.isEmpty()){List<Station> combined=new ArrayList<>(results);combined.addAll(catalog.load());List<Station> merged=CatalogRepository.deduplicate(combined);PublicHttp.checkCancelled(cancel);catalog.save(merged);publish(merged,"Znaleziono "+results.size()+" pasujących adresów; playlist: "+ranked.size()+".");}
            post(()->{if(!destroyed&&providers==owner&&!cancel.get())playlistResults(owner,requested,ranked,notes);});
        }catch(Exception error){post(()->{if(!destroyed&&providers==owner)notice(cancel.get()?"Przerwano. Poprzednie kanały i źródła pozostają zapisane.":"Nie udało się zakończyć wyszukiwania. Poprzednia biblioteka pozostaje dostępna.");});}finally{busy=false;post(()->{dialog.dismiss();if(refreshButton!=null)refreshButton.setText("Odśwież");});}});
    }
    private void playlistResults(ProviderRepository owner,String requested,List<PlaylistSuggestions.Entry> entries,List<String> notes){
        if(owner!=providers)return;LinearLayout box=column();box.setTag("playlist_discovery_results");box.setPadding(dp(18),dp(8),dp(18),dp(8));box.addView(text("Zapytanie: "+requested+" • "+entries.size()+" playlist\nKolejność uwzględnia dopasowania i preferencje tego profilu. Daty opisują pobrane kopie. Obraz potwierdza dopiero odtwarzacz.",13,MUTED),fillWrap());
        AlertDialog[] page=new AlertDialog[1];for(PlaylistSuggestions.Entry entry:entries){CatalogRepository.FoundPlaylist playlist=entry.playlist;String at=new java.text.SimpleDateFormat("dd.MM.yyyy HH:mm",Locale.getDefault()).format(new Date(playlist.fetchedAt));Button result=button(playlist.name+"\n"+host(playlist.source)+" • "+entry.matches+" dopasowań / "+entry.total+" adresów\nPobrano: "+at+(playlist.stale()?" • stara kopia":""),v->{page[0].dismiss();discoveryPage=new AlertDialog.Builder(this).setTitle(playlist.name).setMessage("Pochodzenie: "+playlist.source+"\nPobrano: "+at+"\nDopasowania: "+entry.matches+" / "+entry.total+(playlist.sanitized?"\nOczyszczona kopia • pominięte wpisy: "+playlist.removedEntries:"")+"\n\nDodanie pobierze playlistę ponownie, pominie wpisy z danymi kont i zapisze publiczne kanały w bieżącym profilu.").setPositiveButton("Dodaj do Playlist URL",(d,w)->{if(owner==providers)providerPage=ProviderManagerDialog.prefillM3u(this,owner,providerWork,providerListener(owner),ProviderRepository.label(playlist.name,"Publiczna playlista"),playlist.source,playlist.epgUrls().isEmpty()?"":playlist.epgUrls().get(0));}).setNegativeButton("Wróć",(d,w)->playlistResults(owner,requested,entries,notes)).show();});result.setSingleLine(false);result.setMinHeight(dp(82));result.setTag("playlist_result_"+box.getChildCount());box.addView(result,fillWrap());}
        if(entries.isEmpty())box.addView(text("Brak publicznej playlisty pasującej do nazwy i krajów. Spróbuj innej nazwy lub szerszego wyboru krajów. Bezpośredni stream może istnieć bez publicznej playlisty.",15,TEXT),fillWrap());
        box.addView(text("Przebieg i dostępność źródeł\n"+String.join("\n",notes),12,MUTED),fillWrap());ScrollView scroll=new ScrollView(this);scroll.addView(box);page[0]=new AlertDialog.Builder(this).setTitle("Znalezione publiczne playlisty").setView(scroll).setNegativeButton("Zamknij",null).create();discoveryPage=page[0];page[0].show();
    }
    private void foundPlaylists(){List<CatalogRepository.FoundPlaylist> found=catalog.playlists();if(found.isEmpty()){notice("Brak znalezionych playlist. Najpierw odśwież katalog.");return;}String[] names=new String[found.size()];for(int i=0;i<names.length;i++)names[i]=found.get(i).name+"\n"+host(found.get(i).source)+" • "+age(found.get(i).fetchedAt);
        new AlertDialog.Builder(this).setTitle("Znalezione playlisty").setItems(names,(d,i)->{CatalogRepository.FoundPlaylist p=found.get(i);new AlertDialog.Builder(this).setTitle(p.name).setMessage("Źródło: "+p.source+(p.sanitized?"\n\nOczyszczona kopia: pominięto "+p.removedEntries+" wpisów z danymi kont.":"\n\nPobrana playlista zachowuje zawartość publicznego źródła.")+" Eksport widocznych kanałów uwzględnia Twoje filtry.").setPositiveButton(p.sanitized?"Pobierz kopię":"Pobierz oryginał",(a,b)->export(p.text,p.name.replaceAll("[^\\p{L}\\p{N} ._-]","_")+".m3u")).setNeutralButton("Importuj",(a,b)->importText(p.text,p.source)).setNegativeButton("Anuluj",null).show();}).setNegativeButton("Zamknij",null).show();}
    private void export(String text,String name){try{try(OutputStream out=new FileOutputStream(new File(getCacheDir(),"pending-export.m3u"))){out.write(text.getBytes(StandardCharsets.UTF_8));}Intent intent=new Intent(Intent.ACTION_CREATE_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("audio/x-mpegurl").putExtra(Intent.EXTRA_TITLE,name);startActivityForResult(intent,EXPORT);}catch(Exception e){notice("Nie można przygotować eksportu: "+message(e));}}
    @Override protected void onActivityResult(int request,int result,Intent data){super.onActivityResult(request,result,data);if(request==IMPORT_PORTALS){PortalScannerDialog owner=pendingScannerImport;pendingScannerImport=null;if(result==RESULT_OK&&data!=null&&data.getData()!=null&&owner!=null&&owner.active())owner.accept(data.getData());return;}if(request==EXPORT_PORTALS){PortalScannerDialog owner=pendingScannerExport;String csv=pendingScannerCsv;pendingScannerExport=null;pendingScannerCsv="";if(result==RESULT_OK&&data!=null&&data.getData()!=null&&owner!=null&&owner.active()){Uri output=data.getData();scannerWork.execute(()->{try(OutputStream stream=getContentResolver().openOutputStream(output,"wt")){if(stream==null)throw new IOException();stream.write(csv.getBytes(StandardCharsets.UTF_8));notice("Zapisano raport bez danych dostępu.");}catch(Exception error){notice("Nie udało się zapisać raportu.");}});}return;}if(result!=RESULT_OK||data==null||data.getData()==null)return;Uri uri=data.getData();
        if(request==IMPORT)work.execute(()->{try(InputStream input=getContentResolver().openInputStream(uri)){importText(read(input,8*1024*1024),uri.toString());}catch(Exception e){notice("Nie można odczytać playlisty: "+message(e));}});
        else if(request==EXPORT)work.execute(()->{try(InputStream input=new FileInputStream(new File(getCacheDir(),"pending-export.m3u"));OutputStream out=getContentResolver().openOutputStream(uri,"wt")){if(out==null)throw new IOException("Brak dostępu do pliku");byte[] bytes=new byte[16384];int n;while((n=input.read(bytes))!=-1)out.write(bytes,0,n);notice("Playlista została zapisana w wybranym miejscu.");}catch(Exception e){notice("Nie udało się zapisać playlisty: "+message(e));}});
    }
    private void importText(String text,String source){work.execute(()->{try{if(SourcePrivacy.requiresVault(text)||SourcePrivacy.requiresVault(source))throw new IOException("Listę z danymi dostępu dodaj jako URL w Moje źródła, aby zapisać ją w zaszyfrowanym magazynie.");List<Station> added=catalog.importM3u(text,source);List<Station> merged=new ArrayList<>(added);merged.addAll(catalog.load());List<Station> result=CatalogRepository.deduplicate(merged);catalog.save(result);publish(result,"Zaimportowano "+added.size()+" adresów. Całą pobraną bibliotekę zobaczysz w zakładce Wszystkie.");}catch(Exception e){notice("Błąd importu: "+message(e));}});}
    private void loadCountries()throws Exception{try(InputStream input=getAssets().open("countries.json")){JSONArray data=new JSONArray(read(input,1024*1024));List<String[]> values=new ArrayList<>();for(int i=0;i<data.length();i++){JSONObject c=data.getJSONObject(i);String code=c.getString("code");if(code.equals("UK"))code="GB";values.add(new String[]{code,c.getString("name")+" ("+code+")"});}values.sort(Comparator.comparing(v->v[1]));for(String[] v:values){countryCodes.add(v[0]);countryNames.add(v[1]);}}}
    private String countryCaption(){return countries.isEmpty()?"Cały świat":"Kraje: "+(countries.size()<=3?String.join(", ",countries):countries.size()+" wybranych");}
    private void persistenceNotice(){String warning=profiles.notice();if(!ai.notice().isEmpty())warning+=(warning.isEmpty()?"":" • ")+ai.notice();if(!warning.isEmpty())notice(warning);}
    private void notice(String value){post(()->{lastNotice=value;if(mainVisible&&status!=null)status.setText(value);else if(gateStatus!=null)gateStatus.setText("Proszę czekać…\n"+value);});}
    private void post(Runnable action){if(!destroyed)main.post(()->{if(!destroyed)action.run();});}
    private static String message(Exception e){return e.getMessage()==null?e.getClass().getSimpleName():e.getMessage();}
    private static String host(String url){try{String h=Uri.parse(url).getHost();return h==null?"plik lokalny":h;}catch(Exception e){return "źródło";}}
    private static String normalize(String value){return Normalizer.normalize(value.toLowerCase(Locale.ROOT).replace('ł','l'),Normalizer.Form.NFD).replaceAll("\\p{M}+","");}
    private static String topic(String code){switch(code){case "news":return "wiadomości informacje news";case "music":return "muzyka music";case "sports":return "sport";case "movies":return "filmy seriale";case "kids":return "dzieci bajki";case "documentary":return "dokument nauka";case "entertainment":return "rozrywka";case "lifestyle":return "styl życia";case "religious":return "religia";default:return "ogólne";}}
    private static String age(long time){if(time<=0)return "brak danych";long min=Math.max(0,(System.currentTimeMillis()-time)/60000);return min<1?"przed chwilą":min<60?min+" min temu":min<1440?min/60+" godz. temu":min/1440+" dni temu";}
    private static String read(InputStream input,int maximum)throws IOException{if(input==null)throw new IOException("Brak danych");ByteArrayOutputStream bytes=new ByteArrayOutputStream();byte[] b=new byte[16384];int n;while((n=input.read(b))!=-1){if(bytes.size()+n>maximum)throw new IOException("Plik jest zbyt duży");bytes.write(b,0,n);}return bytes.toString("UTF-8");}
    private int dp(int value){return Math.round(value*getResources().getDisplayMetrics().density);}
    private int videoHeight(){Configuration c=getResources().getConfiguration();return Math.min(205,Math.min((c.screenWidthDp-32)*9/16,Math.max(80,c.screenHeightDp-550)));}
    private void fitPortraitPreview(){
        if(destroyed||wide||fullScreen||playerView==null||channelList==null||body.getHeight()<=0)return;
        // Use the measured content area: Android 15 includes system bars in the
        // configuration dimensions. Keep a full channel card below the controls.
        int playerChrome=playerPanel.getHeight()-playerView.getHeight();
        int listChrome=listPanel.getHeight()-channelList.getHeight();
        View first=channelList.getChildAt(0);int rowSpace=Math.max(dp(100),first==null?0:first.getHeight()+dp(6));
        int available=body.getHeight()-playerChrome-listChrome-rowSpace;
        // A minimum of 80dp clipped channel cards on short edge-to-edge screens.
        int height=Math.min(Math.min(dp(205),playerView.getWidth()*9/16),Math.max(dp(48),available));
        LinearLayout.LayoutParams params=(LinearLayout.LayoutParams)playerView.getLayoutParams();
        if(height>0&&params.height!=height){params.height=height;params.weight=0;playerView.setLayoutParams(params);}
    }
    private boolean shortLandscape(){return wide&&getResources().getConfiguration().screenHeightDp<480;}
    private void compactSourceControls(){if(providerTiles==null)return;boolean compact=shortLandscape();
        brandBox.setMinimumHeight(dp(compact?32:51));brandTitle.setText(compact?"SWIRSTREAM AI · by Swir":"SWIRSTREAM AI");brandTitle.setTextSize(compact?14:16);brandSignature.setVisibility(compact?View.GONE:View.VISIBLE);
        ViewGroup.LayoutParams icon=brandIcon.getLayoutParams();icon.width=icon.height=dp(compact?32:39);brandIcon.setLayoutParams(icon);ViewGroup.LayoutParams profile=profileButton.getLayoutParams();profile.height=dp(compact?32:44);profileButton.setLayoutParams(profile);profileButton.setMinHeight(dp(compact?32:42));profileButton.setMinimumHeight(dp(compact?32:42));headerMenu.setMinHeight(dp(compact?32:42));headerMenu.setMinimumHeight(dp(compact?32:42));ViewGroup.LayoutParams menu=headerMenu.getLayoutParams();menu.width=dp(compact?40:48);menu.height=dp(compact?32:48);headerMenu.setLayoutParams(menu);
        String[] names=compact?new String[]{"Playlisty URL","Xtream","MAC Player","DRM"}:new String[]{"Playlisty\nURL","Xtream\nkonta","MAC\nPlayer","DRM\nlicencje"};for(int i=0;i<providerTiles.getChildCount();i++){Button tile=(Button)providerTiles.getChildAt(i);tile.setText(names[i]);tile.setSingleLine(compact);tile.setMaxLines(compact?1:2);LinearLayout.LayoutParams layout=(LinearLayout.LayoutParams)tile.getLayoutParams();layout.height=dp(compact?40:50);tile.setLayoutParams(layout);}}
    private void compactPlayerDetails(){if(playingEpg!=null)playingEpg.setVisibility(getResources().getConfiguration().screenHeightDp<700?View.GONE:View.VISIBLE);}
    private LinearLayout column(){LinearLayout l=new LinearLayout(this);l.setOrientation(LinearLayout.VERTICAL);return l;}
    private LinearLayout row(){LinearLayout l=new LinearLayout(this);l.setOrientation(LinearLayout.HORIZONTAL);return l;}
    private LinearLayout.LayoutParams fillWrap(){return new LinearLayout.LayoutParams(-1,-2);}
    private LinearLayout.LayoutParams wrap(){return new LinearLayout.LayoutParams(-2,dp(44));}
    private TextView text(String value,int size,int color){TextView t=new TextView(this);t.setText(value);t.setTextSize(size);t.setTextColor(color);return t;}
    private Button button(String title,View.OnClickListener click){Button b=new Button(this);b.setText(title);b.setAllCaps(false);b.setTextSize(13);b.setSingleLine(true);b.setMinWidth(0);b.setMinimumWidth(0);b.setTextColor(new ColorStateList(new int[][]{new int[]{android.R.attr.state_focused},new int[]{}},new int[]{BG,TEXT}));b.setMinHeight(dp(42));b.setMinimumHeight(dp(42));b.setPadding(dp(12),0,dp(12),0);b.setBackground(focusBackground(PANEL));b.setFocusable(true);b.setOnClickListener(click);return b;}
    private GradientDrawable rounded(int color,int stroke){GradientDrawable d=new GradientDrawable();d.setColor(color);d.setCornerRadius(dp(9));if(stroke!=0)d.setStroke(dp(2),stroke);return d;}
    private StateListDrawable focusBackground(int color){StateListDrawable d=new StateListDrawable();d.addState(new int[]{android.R.attr.state_focused},rounded(CYAN,CYAN));d.addState(new int[]{android.R.attr.state_pressed},rounded(0xff27375b,PURPLE));d.addState(new int[]{},rounded(color,0));return d;}
    private StateListDrawable outlineFocus(){StateListDrawable d=new StateListDrawable();d.addState(new int[]{android.R.attr.state_focused},rounded(PANEL,CYAN));d.addState(new int[]{},rounded(PANEL,0));return d;}
    private void wireRowFocus(LinearLayout row){View previous=null;for(int i=0;i<row.getChildCount();i++){View current=row.getChildAt(i);if(!current.isFocusable())continue;if(current.getId()==View.NO_ID)current.setId(View.generateViewId());if(previous!=null){previous.setNextFocusRightId(current.getId());previous.setNextFocusForwardId(current.getId());current.setNextFocusLeftId(previous.getId());}previous=current;}}
    private void licenses(){try{String[] files=getAssets().list("licenses");List<String> names=new ArrayList<>();names.add("Informacje o bibliotekach");if(files!=null)names.addAll(Arrays.asList(files));new AlertDialog.Builder(this).setTitle("Licencje i informacje").setItems(names.toArray(new String[0]),(d,which)->{String asset=which==0?"THIRD_PARTY_ANDROID.md":"licenses/"+names.get(which);try(InputStream input=getAssets().open(asset)){new AlertDialog.Builder(this).setTitle(names.get(which)).setMessage(read(input,2*1024*1024)).setPositiveButton("OK",null).show();}catch(Exception e){notice("Nie można odczytać informacji: "+message(e));}}).setNegativeButton("Zamknij",null).show();}catch(Exception e){notice("Nie można odczytać licencji: "+message(e));}}
    private HorizontalScrollView scrollHorizontal(View child){HorizontalScrollView s=new HorizontalScrollView(this);s.setHorizontalScrollBarEnabled(false);s.setFillViewport(false);s.addView(child);return s;}
    private void insets(View root,int margin){root.setOnApplyWindowInsetsListener((v,in)->{int l,t,r,b;if(Build.VERSION.SDK_INT>=30){android.graphics.Insets bars=in.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());l=bars.left;t=bars.top;r=bars.right;b=bars.bottom;}else{l=in.getSystemWindowInsetLeft();t=in.getSystemWindowInsetTop();r=in.getSystemWindowInsetRight();b=in.getSystemWindowInsetBottom();}v.setPadding(l+dp(margin),t+dp(margin),r+dp(margin),b+dp(margin));return in;});root.requestApplyInsets();}
    private final class ChannelAdapter extends BaseAdapter {
        public int getCount(){return visible.size();}public Object getItem(int p){return visible.get(p);}public long getItemId(int p){long hash=0xcbf29ce484222325L;for(char c:visible.get(p).head().key().toCharArray()){hash^=c;hash*=0x100000001b3L;}return hash;}public boolean hasStableIds(){return true;}
        final class Holder {LinearLayout card;TextView name,detail,current,next;}
        public View getView(int position,View old,ViewGroup parent){
            Holder holder;if(old!=null&&old.getTag() instanceof Holder)holder=(Holder)old.getTag();else{
                holder=new Holder();holder.card=column();holder.card.setPadding(dp(12),dp(10),dp(12),dp(10));holder.name=text("",16,TEXT);holder.name.setTypeface(null,Typeface.BOLD);holder.detail=text("",11,PURPLE);holder.current=text("",12,MUTED);holder.next=text("",11,MUTED);
                for(TextView line:new TextView[]{holder.name,holder.detail,holder.current,holder.next}){line.setSingleLine(true);line.setEllipsize(TextUtils.TruncateAt.END);holder.card.addView(line);}holder.card.setTag(holder);
            }
            boolean compact=shortLandscape();holder.card.setPadding(dp(12),dp(compact?4:10),dp(12),dp(compact?4:10));
            Channel channel=visible.get(position);Station s=channel.head();holder.card.setBackground(rounded(position%2==0?PANEL:0xff0d1626,0));holder.name.setText((profiles.favorite(s)?"★  ":"")+s.name);holder.detail.setText((s.country.isEmpty()?"Kraj nieznany":s.country)+"  •  "+channel.sources.size()+" źródeł  •  "+channel.category);
            String now=epgNow(s),next=epgNext(s);holder.current.setText(now==null||now.isEmpty()?"EPG: brak aktualnej ramówki":now);holder.next.setText(next==null?"":next);holder.next.setVisibility(compact||next==null||next.isEmpty()?View.GONE:View.VISIBLE);holder.card.setContentDescription(s.name+", "+channel.sources.size()+" źródeł. "+(now==null?"":now));return holder.card;
        }
    }
}


