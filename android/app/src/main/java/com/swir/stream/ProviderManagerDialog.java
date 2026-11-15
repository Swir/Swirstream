package com.swir.stream;

import android.app.Activity;
import android.app.AlertDialog;
import android.graphics.Color;
import android.os.Handler;
import android.os.Looper;
import android.text.InputType;
import android.view.View;
import android.view.WindowManager;
import android.widget.EditText;
import android.widget.Button;
import android.view.Gravity;
import android.graphics.drawable.GradientDrawable;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.ArrayAdapter;
import android.widget.TextView;
import android.widget.Toast;
import java.io.IOException;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.atomic.AtomicBoolean;

/** Source manager has no global preference fields, credential logs or saved form state. */
@androidx.media3.common.util.UnstableApi
public final class ProviderManagerDialog {
    interface Listener { default boolean active(){return true;} void changed(); void showChannels(String id,String name); void discoverPlaylists(); default void exploreSources(){} }
    private static final String[] TYPES={"m3u","xtream","portal","drm","direct"};
    private static final String[] TITLES={"Playlisty URL","Xtream","MAC Player","DRM"};
    private static final String[] EXPLANATIONS={
        "Dodaj publiczną lub własną playlistę M3U z adresu URL. AI może znaleźć publiczne playlisty według kanału i wybranych krajów.",
        "Połącz swoje konto Xtream: adres serwera, login i hasło od dostawcy. Tutaj zapisujesz konta i pobierasz ich kanały na żywo.",
        "Połącz portal za pomocą swojego MAC. Program rozpoznaje katalog bez pełnej sesji oraz Stalker / Ministra wymagający aktywnego profilu. Sam katalog nie potwierdza odtwarzania. Portale wymagające dodatkowej rejestracji lub aplikacji operatora mogą nie być zgodne.",
        "Dodaj transmisję z własnym serwerem licencji Widevine lub ClearKey. Zapis konfiguracji nie potwierdza odtwarzania; sprawdza je odtwarzacz na tym urządzeniu."
    };
    private final Activity activity;private final ProviderRepository repository;private final ExecutorService executor;private final Listener listener;private final Handler main=new Handler(Looper.getMainLooper());
    private final int section;private AlertDialog page;
    private ProviderManagerDialog(Activity activity,ProviderRepository repository,ExecutorService executor,Listener listener,int section){this.activity=activity;this.repository=repository;this.executor=executor;this.listener=listener;this.section=section;}
    public static void show(Activity activity,ProviderRepository repository,ExecutorService executor,Runnable onChanged){showSection(activity,repository,executor,-1,new Listener(){public void changed(){onChanged.run();}public void showChannels(String id,String name){onChanged.run();}public void discoverPlaylists(){}});}
    static ProviderManagerDialog showSection(Activity activity,ProviderRepository repository,ExecutorService executor,int section,Listener listener){ProviderManagerDialog page=new ProviderManagerDialog(activity,repository,executor,listener,section);page.load();return page;}
    static ProviderManagerDialog pasteLink(Activity activity,ProviderRepository repository,ExecutorService executor,Listener listener,String initial){ProviderManagerDialog page=new ProviderManagerDialog(activity,repository,executor,listener,-1);page.paste(initial);return page;}
    static ProviderManagerDialog prefillM3u(Activity activity,ProviderRepository repository,ExecutorService executor,Listener listener,String name,String url,String epgUrl){ProviderManagerDialog page=new ProviderManagerDialog(activity,repository,executor,listener,0);org.json.JSONObject config=new org.json.JSONObject();try{config.put("publicDiscovery",true);}catch(org.json.JSONException impossible){throw new IllegalStateException(impossible);}page.form(0,name,url,epgUrl,"",config);return page;}
    static ProviderManagerDialog prefillCandidate(Activity activity,ProviderRepository repository,ExecutorService executor,Listener listener,SourceCandidate candidate){if(candidate.kind==SourceExplorerRules.Kind.PublicPlaylist)return prefillM3u(activity,repository,executor,listener,candidate.title,candidate.url,"");int kind=candidate.kind==SourceExplorerRules.Kind.PortalCandidate?2:candidate.protectedMedia?3:4;ProviderManagerDialog page=new ProviderManagerDialog(activity,repository,executor,listener,kind==4?0:kind);page.form(kind,candidate.title,candidate.url,"","",new org.json.JSONObject());return page;}
    static List<ProviderRepository.SourceInfo> sourcesForSection(List<ProviderRepository.SourceInfo> all,int section){List<ProviderRepository.SourceInfo> result=new java.util.ArrayList<>();for(ProviderRepository.SourceInfo source:all)if(section<0||TYPES[section].equals(source.type)||section==0&&"direct".equals(source.type))result.add(source);return result;}
    private boolean alive(){return listener.active()&&!activity.isFinishing()&&!activity.isDestroyed();}
    private int dp(int value){return Math.round(value*activity.getResources().getDisplayMetrics().density);}
    private TextView label(String value,int size,int color){TextView text=new TextView(activity);text.setText(value);text.setTextSize(size);text.setTextColor(color);text.setPadding(0,dp(5),0,dp(5));return text;}
    private Button action(String value,String tag,Runnable run){Button button=new Button(activity);button.setText(value);button.setTag(tag);button.setAllCaps(false);button.setTextSize(14);button.setMinHeight(dp(48));button.setFocusable(true);button.setOnClickListener(v->run.run());return button;}
    private void load(){if(!alive())return;executor.execute(()->{List<ProviderRepository.SourceInfo> sources=sourcesForSection(repository.listSources(),section);main.post(()->{if(alive())list(sources);});});}
    void closePage(){if(page!=null){page.dismiss();page=null;}}
    private void list(List<ProviderRepository.SourceInfo> sources){
        closePage();LinearLayout box=new LinearLayout(activity);box.setOrientation(LinearLayout.VERTICAL);box.setPadding(dp(18),dp(8),dp(18),dp(10));box.setTag(section<0?"provider_section_all":"provider_section_"+TYPES[section]);
        box.addView(label(section<0?"Własne źródła bieżącego profilu. Wybierz kategorię lub dodaj nowe źródło.":EXPLANATIONS[section],14,0xffa1afc8));
        box.addView(action("Wklej link od dostawcy","provider_paste",()->{closePage();paste("");}));
        box.addView(action(section<0?"＋ Dodaj źródło":"＋ "+new String[]{"Dodaj playlistę URL","Dodaj konto Xtream","Dodaj portal MAC","Dodaj transmisję DRM"}[section],"provider_add",()->{closePage();if(section<0)choose();else form(section);}));
        if(section==0)box.addView(action("Dodaj bezpośredni HLS / DASH / MP4","provider_add_direct",()->{closePage();form(4);}));
        box.addView(action("Eksplorator źródeł WWW","provider_explorer",()->{closePage();listener.exploreSources();}));
        if(section==0)box.addView(action("AI: znajdź playlisty","provider_discover",()->{closePage();listener.discoverPlaylists();}));
        if(sources.isEmpty())box.addView(label("Nie dodano jeszcze żadnego źródła w tej sekcji. Dane logowania i prywatne listy pozostają zaszyfrowane na tym urządzeniu, osobno dla każdego profilu.",14,0xffeef5ff));
        for(ProviderRepository.SourceInfo source:sources){
            LinearLayout card=new LinearLayout(activity);card.setOrientation(LinearLayout.VERTICAL);card.setPadding(dp(10),dp(8),dp(10),dp(10));GradientDrawable background=new GradientDrawable();background.setColor(0xff17243b);background.setCornerRadius(dp(10));card.setBackground(background);
            LinearLayout.LayoutParams margin=new LinearLayout.LayoutParams(-1,-2);margin.topMargin=dp(10);box.addView(card,margin);
            card.addView(label(source.name,17,0xff40e8e8));String at=source.updatedAt>0?new java.text.SimpleDateFormat("dd.MM.yyyy HH:mm",java.util.Locale.getDefault()).format(new java.util.Date(source.updatedAt)):"brak udanego zapisu";
            card.addView(label(type(source.type)+" • "+source.count+" kanałów\n"+(("drm".equals(source.type)||"direct".equals(source.type))?"Konfiguracja: ":"Ostatnie pobranie: ")+at+"\n"+source.status,13,0xffa1afc8));
            Button show=action("Pokaż kanały ("+source.count+")","provider_show_"+source.id,()->{closePage();listener.showChannels(source.id,source.name);});show.setEnabled(source.count>0);card.addView(show);
            card.addView(action(("drm".equals(source.type)||"direct".equals(source.type))?"Sprawdź adresy konfiguracji":"Odśwież listę kanałów","provider_refresh_"+source.id,()->{closePage();run(cancel->repository.refresh(source.id,cancel),null);}));
            card.addView(action("Edytuj dane źródła","provider_edit_"+source.id,()->{closePage();edit(source);}));
            card.addView(action("Usuń źródło","provider_remove_"+source.id,()->{closePage();new AlertDialog.Builder(activity).setTitle("Usunąć źródło?").setMessage("Usunięte zostaną dane źródła „"+source.name+"” w bieżącym profilu. Ulubione z tego źródła przestaną działać.").setPositiveButton("Usuń",(d,w)->run(cancel->{repository.remove(source.id);return null;},null)).setNegativeButton("Anuluj",(d,w)->load()).show();}));
        }
        if(section==3)box.addView(label(SwirPlayer.drmCapabilities(),13,0xffa1afc8));
        ScrollView scroll=new ScrollView(activity);scroll.setFillViewport(true);scroll.addView(box);page=new AlertDialog.Builder(activity).setTitle((section<0?"Moje źródła":TITLES[section])+" • bieżący profil").setView(scroll).setNegativeButton("Zamknij",null).create();page.show();if(page.getWindow()!=null)page.getWindow().setBackgroundDrawableResource(android.R.color.transparent);
        // Keep system focus navigation and scrolling available to projector remotes.
        if(page.getWindow()!=null){page.getWindow().setBackgroundDrawable(new android.graphics.drawable.ColorDrawable(0xff101a2c));page.getWindow().setLayout(android.view.ViewGroup.LayoutParams.MATCH_PARENT,android.view.ViewGroup.LayoutParams.WRAP_CONTENT);}
    }
    private void choose(){new AlertDialog.Builder(activity).setTitle("Dodaj swoje źródło").setItems(new String[]{"Playlista M3U z adresu URL","Konto Xtream • kanały na żywo","Portal MAC • zarejestrowane urządzenie","Transmisja DRM • własna licencja","Bezpośredni HLS / DASH / MP4"},(dialog,which)->form(which)).setNegativeButton("Wróć",(d,w)->load()).show();}
    private void paste(String initial){
        closePage();LinearLayout box=new LinearLayout(activity);box.setOrientation(LinearLayout.VERTICAL);box.setPadding(dp(18),dp(8),dp(18),dp(8));box.setTag("provider_paste_form");
        box.addView(label("Wklej cały link od dostawcy. Rozpoznamy Xtream, playlistę URL lub portal MAC. Rozpoznanie niczego nie pobiera ani nie zapisuje.",14,0xffa1afc8));
        EditText link=field(box,"Link (ukryty — może zawierać login i hasło)",true,false);link.setTag("provider_pasted_link");link.setText(initial);
        box.addView(action("Wklej ze schowka","provider_clipboard",()->{android.content.ClipboardManager clipboard=(android.content.ClipboardManager)activity.getSystemService(android.content.Context.CLIPBOARD_SERVICE);if(clipboard!=null&&clipboard.hasPrimaryClip()&&clipboard.getPrimaryClip()!=null&&clipboard.getPrimaryClip().getItemCount()>0){CharSequence value=clipboard.getPrimaryClip().getItemAt(0).coerceToText(activity);if(value!=null)link.setText(value);}else link.setError("Schowek jest pusty");}));
        box.addView(action("Ten link to konto Xtream","provider_paste_xtream",()->recognize(link,true)));
        box.addView(action("Użyj całego linku jako własnej playlisty URL","provider_paste_m3u",()->{try{String value=SourceLinkParser.clean(link.getText().toString());java.net.URI uri=ProviderHttp.checkedUri(value);link.setText("");closePage();form(0,uri.getHost(),value,"","",new org.json.JSONObject());}catch(IOException error){link.setError(error.getMessage());}}));
        ScrollView scroll=new ScrollView(activity);scroll.addView(box);AlertDialog dialog=new AlertDialog.Builder(activity).setTitle("Wklej link • prywatny import").setView(scroll).setPositiveButton("Rozpoznaj link",null).setNegativeButton("Anuluj",null).create();page=dialog;if(dialog.getWindow()!=null)dialog.getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);dialog.setOnShowListener(d->dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(v->recognize(link,false)));dialog.show();
    }
    private void recognize(EditText link,boolean explicitXtream){
        try{SourceLinkParser.Result parsed=SourceLinkParser.parse(link.getText().toString(),explicitXtream);org.json.JSONObject config=new org.json.JSONObject();if(parsed.kind==SourceLinkParser.Kind.XTREAM)config.put("user",parsed.user).put("password",parsed.password);link.setText("");closePage();form(parsed.kind==SourceLinkParser.Kind.XTREAM?1:parsed.kind==SourceLinkParser.Kind.PORTAL?2:parsed.kind==SourceLinkParser.Kind.DIRECT?4:0,parsed.name,parsed.kind==SourceLinkParser.Kind.XTREAM?parsed.base:parsed.url,"","",config);}
        catch(IOException error){link.setError(error.getMessage());}catch(org.json.JSONException error){link.setError("Nie udało się przygotować formularza.");}
    }
    private EditText field(LinearLayout box,String label,boolean secret,boolean multiline){
        TextView text=new TextView(activity);text.setText(label);text.setPadding(0,14,0,2);box.addView(text);EditText input=new EditText(activity);input.setSaveEnabled(false);input.setSingleLine(!multiline);input.setTextSize(15);input.setInputType(InputType.TYPE_CLASS_TEXT|(secret?InputType.TYPE_TEXT_VARIATION_PASSWORD:multiline?InputType.TYPE_TEXT_FLAG_MULTI_LINE:InputType.TYPE_TEXT_VARIATION_URI)|InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);if(android.os.Build.VERSION.SDK_INT>=26)input.setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_NO_EXCLUDE_DESCENDANTS);box.addView(input);return input;
    }
    private void edit(ProviderRepository.SourceInfo source){executor.execute(()->{try{org.json.JSONObject value=repository.editConfiguration(source.id),config=value.getJSONObject("config");int kind=java.util.Arrays.asList(TYPES).indexOf(value.getString("type"));if(kind<0)throw new IOException();String name=value.getString("name"),url=config.optString(kind==1?"base":"url"),epg=config.optString("epgUrl");main.post(()->{if(alive())form(kind,name,url,epg,source.id,config);});}catch(Exception error){main.post(()->{if(alive()){Toast.makeText(activity,"Nie można odczytać danych tego źródła.",Toast.LENGTH_LONG).show();load();}});}});}
    private void form(int kind){form(kind,"","","","",new org.json.JSONObject());}
    private void form(int kind,String initialName,String initialUrl,String initialEpg,String sourceId,org.json.JSONObject config){
        LinearLayout box=new LinearLayout(activity);box.setOrientation(LinearLayout.VERTICAL);int pad=(int)(20*activity.getResources().getDisplayMetrics().density);box.setPadding(pad,8,pad,8);ScrollView scroll=new ScrollView(activity);scroll.addView(box);
        TextView hint=new TextView(activity);hint.setText((config.optBoolean("publicDiscovery")?"Publiczna lista z wyszukiwania AI: przy każdym odświeżeniu pomijamy wpisy z danymi kont. ":"")+"Użyj danych przekazanych przez swojego dostawcę. Adresy HTTP nie szyfrują połączenia. Dane źródła pozostają w tym profilu i nie trafiają do eksportu publicznych playlist.");hint.setTextSize(13);box.addView(hint);
        EditText name=field(box,"Nazwa źródła",false,false),url=field(box,kind==1?"Serwer lub pełny link Xtream (get.php / player_api.php)":kind==2?"Adres portalu (np. https://dostawca.pl/c/)":kind==3?"Adres manifestu DASH / HLS":kind==4?"Adres HLS / DASH / MP4 / radia":"Adres playlisty M3U / get.php",false,false);
        name.setTag("provider_name");url.setTag("provider_url");name.setText(initialName);url.setText(initialUrl);box.setTag("provider_form_"+TYPES[kind]);
        EditText user=kind==1?field(box,"Login (ukryty)",true,false):null,password=kind==1?field(box,"Hasło",true,false):null,mac=kind==2?field(box,"Twój MAC zarejestrowany u dostawcy",false,false):null,epg=kind==0||kind==4?field(box,"XMLTV / EPG (opcjonalnie)",false,false):null;
        if(user!=null){user.setTag("provider_login");user.setText(config.optString("user"));password.setTag("provider_password");password.setText(config.optString("password"));}if(mac!=null){mac.setTag("provider_mac");mac.setText(config.optString("mac"));}
        if(epg!=null){epg.setTag("provider_epg");epg.setText(initialEpg);}
        if(kind==1){box.addView(label("Możesz wkleić pełny link w pole serwera — login i hasło zostaną rozpoznane. Ponowne dodanie tego samego serwera i loginu odświeży istniejące konto, zachowując nazwę i ulubione. Nowe hasło zapisze się dopiero po udanym połączeniu.",13,0xffa1afc8));url.setOnFocusChangeListener((v,focused)->{if(!focused&&SourceLinkParser.importCandidate(url.getText().toString())&&url.getText().toString().contains("?"))normalizeXtream(url,user,password,name);});}
        Spinner scheme=new Spinner(activity);EditText license,headers;if(kind==3){TextView note=new TextView(activity);note.setText("DRM wymaga uprawnień do treści, działającego serwera licencji i wsparcia urządzenia. ClearKey: DASH; Widevine: zgodny DASH / HLS. Program nie pozyskuje kluczy ani nie omija zabezpieczeń.");note.setPadding(0,14,0,6);box.addView(note);scheme.setAdapter(new ArrayAdapter<>(activity,android.R.layout.simple_spinner_dropdown_item,new String[]{"Widevine","ClearKey"}));box.addView(scheme);license=field(box,"Adres serwera licencji",false,false);headers=field(box,"Nagłówki licencji (opcjonalnie, Nazwa: wartość)",true,true);license.setText(config.optString("license"));scheme.setSelection("clearkey".equals(config.optString("scheme"))?1:0);StringBuilder initialHeaders=new StringBuilder();org.json.JSONObject savedHeaders=config.optJSONObject("headers");if(savedHeaders!=null){java.util.Iterator<String> keys=savedHeaders.keys();while(keys.hasNext()){String key=keys.next();initialHeaders.append(key).append(": ").append(savedHeaders.optString(key)).append("\n");}}headers.setText(initialHeaders.toString());}else{license=null;headers=null;}
        if(kind==2){TextView note=new TextView(activity);note.setText("Rozpoznamy interfejs portalu i tryb MAC. Lista kanałów nie potwierdza aktywności konta ani odtwarzania. Nie zmieniamy Twojego MAC; dodatkową rejestrację urządzenia ustal z dostawcą.");box.addView(note);}
        AlertDialog dialog=new AlertDialog.Builder(activity).setTitle((sourceId.isEmpty()?"Nowe źródło • ":"Edytuj źródło • ")+new String[]{"M3U","Xtream","MAC","DRM","Bezpośrednia transmisja"}[kind]).setView(scroll).setPositiveButton(kind==3||kind==4?"Zapisz konfigurację":"Pobierz i zapisz",null).setNegativeButton("Anuluj",(d,w)->load()).create();
        dialog.setOnShowListener(ignored->{if(dialog.getWindow()!=null)dialog.getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(view->{
            if(kind==1&&!normalizeXtream(url,user,password,name))return;
            String n=name.getText().toString().trim(),u=url.getText().toString().trim(),usr=user==null?"":user.getText().toString(),pw=password==null?"":password.getText().toString(),m=mac==null?"":mac.getText().toString(),e=epg==null?"":epg.getText().toString().trim(),s=scheme.getSelectedItemPosition()==0?"widevine":"clearkey",l=license==null?"":license.getText().toString().trim();Map<String,String> h;
            try{h=parseHeaders(headers==null?"":headers.getText().toString());}catch(IOException error){Toast.makeText(activity,error.getMessage(),Toast.LENGTH_LONG).show();return;}
            run(cancel->{if(!sourceId.isEmpty())return repository.update(sourceId,n,TYPES[kind],u,usr,pw,m,e,s,l,h,cancel);if(kind==0)return config.optBoolean("publicDiscovery")?repository.addPublicM3u(n,u,e,cancel):repository.addM3u(n,u,e,cancel);if(kind==1)return repository.addXtream(n,u,usr,pw,cancel);if(kind==2)return repository.addPortal(n,u,m,cancel);if(kind==4)return repository.addDirect(n,u,e,cancel);return repository.addDrm(n,u,s,l,h,cancel);},dialog);
        });});page=dialog;if(dialog.getWindow()!=null)dialog.getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);dialog.show();
    }
    private boolean normalizeXtream(EditText url,EditText user,EditText password,EditText name){try{SourceLinkParser.Result parsed=SourceLinkParser.xtream(url.getText().toString(),user.getText().toString(),password.getText().toString());url.setText(parsed.base);user.setText(parsed.user);password.setText(parsed.password);if(name.getText().toString().trim().isEmpty())name.setText(parsed.name);url.setError(null);return true;}catch(IOException error){url.setError(error.getMessage());return false;}}
    interface Operation {ProviderRepository.SourceInfo run(AtomicBoolean cancelled)throws IOException;}
    private void run(Operation operation,AlertDialog form){
        if(!alive())return;AtomicBoolean cancel=new AtomicBoolean();AlertDialog progress=new AlertDialog.Builder(activity).setTitle("Proszę czekać").setMessage(section==3?"Sprawdzanie adresów konfiguracji DRM…\nOdtwarzanie sprawdzisz przyciskiem Pokaż kanały.":"Pobieranie i sprawdzanie źródła…\nDotychczasowa lista pozostaje dostępna.").setNegativeButton("Anuluj",(d,w)->cancel.set(true)).create();progress.setCancelable(false);progress.show();if(form!=null)form.getButton(AlertDialog.BUTTON_POSITIVE).setEnabled(false);
        executor.execute(()->{String message=null;boolean success=false;ProviderRepository.SourceInfo saved=null;try{saved=operation.run(cancel);success=true;}catch(IOException e){message=e.getMessage();}catch(Exception e){message="Nie udało się przygotować źródła. Poprzednia lista pozostaje zapisana.";}final boolean ok=success;final String error=message;final ProviderRepository.SourceInfo result=saved;
            main.post(()->{progress.dismiss();if(!alive())return;if(form!=null)form.getButton(AlertDialog.BUTTON_POSITIVE).setEnabled(true);if(ok){if(form!=null)form.dismiss();listener.changed();if(form!=null&&result!=null){closePage();listener.showChannels(result.id,result.name);Toast.makeText(activity,"Otworzono źródło: "+result.count+" kanałów. Wpisz nazwę, aby przeszukać jego listę.",Toast.LENGTH_LONG).show();}else load();}else if(!cancel.get()){new AlertDialog.Builder(activity).setTitle("Źródło nie zostało zmienione").setMessage(error==null?"Nie udało się pobrać danych źródła.":error).setPositiveButton("OK",(d,w)->{if(form==null)load();}).show();}else if(form==null)load();});
        });
    }
    static Map<String,String> parseHeaders(String text)throws IOException{if(text.trim().isEmpty())return Collections.emptyMap();Map<String,String> out=new LinkedHashMap<>();for(String line:text.split("\\r?\\n")){if(line.trim().isEmpty())continue;int colon=line.indexOf(':');if(colon<1)throw new IOException("Każdy nagłówek zapisz jako Nazwa: wartość.");out.put(line.substring(0,colon).trim(),line.substring(colon+1).trim());}return ProviderHttp.cleanHeaders(out);}
    private static String type(String value){return "m3u".equals(value)?"M3U":"xtream".equals(value)?"Xtream":"portal".equals(value)?"MAC":"drm".equals(value)?"DRM":"direct".equals(value)?"Bezpośrednia transmisja":"zapis chroniony";}
}
