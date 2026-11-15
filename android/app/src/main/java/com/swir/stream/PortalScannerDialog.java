package com.swir.stream;

import android.app.*;
import android.net.Uri;
import android.os.*;
import android.view.*;
import android.widget.*;
import java.io.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** All import credentials remain transient; only the explicit selected Add/Play writes the protected repository. */
final class PortalScannerDialog {
    interface Listener {boolean active();void chooseFile(PortalScannerDialog owner);void exportReport(PortalScannerDialog owner,String csv);void discover();void configure(String url);void saved(ProviderRepository.SourceInfo source,boolean play);}
    private final Activity activity;private final ProviderRepository repository;private final ExecutorService worker;private final PortalScanner engine;private final Listener listener;private final Handler main=new Handler(Looper.getMainLooper());
    private final Object lifetimeGate=new Object();private AtomicBoolean cancellation=new AtomicBoolean();private volatile boolean closed;private boolean busy;private int generation,selected;private AlertDialog page;
    private volatile InputStream importing;
    private void cancelOperation(){cancellation.set(true);InputStream stream=importing;if(stream!=null)try{stream.close();}catch(IOException ignored){}}
    private List<PortalTxt.Entry> entries=new ArrayList<>();private List<PortalScanner.Result> results=new ArrayList<>();private String message="Wczytaj TXT: sam URL, URL|MAC albo URL i MAC w kolejnych wierszach. Maks. 1 MiB / 200 pozycji.";
    PortalScannerDialog(Activity activity,ProviderRepository repository,ExecutorService worker,PortalScanner engine,Listener listener){this.activity=activity;this.repository=repository;this.worker=worker;this.engine=engine;this.listener=listener;}
    boolean active(){return !closed&&listener.active()&&!activity.isFinishing()&&!activity.isDestroyed();}
    void show(){render();}
    void close(){synchronized(lifetimeGate){closed=true;cancelOperation();generation++;}entries=new ArrayList<>();results=new ArrayList<>();if(page!=null){page.dismiss();page=null;}}
    private int dp(int value){return Math.round(value*activity.getResources().getDisplayMetrics().density);}
    private TextView text(String value){TextView view=new TextView(activity);view.setText(value);view.setTextColor(0xffdce7f7);view.setTextSize(14);view.setPadding(0,dp(7),0,dp(7));return view;}
    private Button button(String title,String tag,Runnable action){Button view=new Button(activity);view.setText(title);view.setTag(tag);view.setAllCaps(false);view.setMinHeight(dp(48));view.setOnClickListener(v->action.run());return view;}
    private void render(){if(!active())return;if(page!=null)page.dismiss();LinearLayout box=new LinearLayout(activity);box.setOrientation(LinearLayout.VERTICAL);box.setTag("portal_scanner");box.setPadding(dp(16),dp(8),dp(16),dp(12));
        box.addView(text("Skan sprawdza publiczną stronę lub katalog dla własnego MAC. Lista kanałów nie potwierdza odtwarzania. Wyniki są tymczasowe; dodajesz tylko wybrane źródło."));
        Button load=button("Importuj TXT","scanner_import",()->{if(!busy)listener.chooseFile(this);});load.setEnabled(!busy);box.addView(load);
        Button web=button("Szukaj publicznych portali","scanner_web",()->{if(!busy){close();listener.discover();}});web.setEnabled(!busy);box.addView(web);
        TextView status=text(message);status.setTag("scanner_status");box.addView(status);
        Spinner list=new Spinner(activity);list.setTag("scanner_entries");List<String> labels=new ArrayList<>();for(int i=0;i<entries.size();i++)labels.add(entries.get(i).label(i));if(labels.isEmpty())labels.add("Brak wczytanych pozycji");list.setAdapter(new ArrayAdapter<>(activity,android.R.layout.simple_spinner_dropdown_item,labels));list.setSelection(Math.min(selected,labels.size()-1));box.addView(list);
        TextView details=text(selectedLabel());details.setTag("scanner_result");box.addView(details);list.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onNothingSelected(AdapterView<?> parent){}public void onItemSelected(AdapterView<?> parent,View v,int position,long id){selected=position;details.setText(selectedLabel());}});
        Button scan=button("Sprawdź listę","scanner_start",this::start);scan.setEnabled(!busy&&!entries.isEmpty());box.addView(scan);
        if(busy)box.addView(button("Przerwij skan / pobieranie","scanner_cancel",()->{cancelOperation();message="Przerywam… ukończone wyniki pozostaną widoczne.";status.setText(message);}));
        Button add=button("Dodaj wybrane źródło","scanner_add",()->save(false));add.setEnabled(!busy&&!entries.isEmpty());box.addView(add);
        Button play=button("Dodaj i otwórz próbkę w playerze","scanner_play",()->save(true));play.setEnabled(!busy&&!entries.isEmpty());box.addView(play);
        Button export=button("Eksportuj raport bez danych dostępu","scanner_export",()->{if(!busy)listener.exportReport(this,PortalScanner.csv(entries,results));});export.setEnabled(!busy&&!entries.isEmpty());box.addView(export);
        box.addView(text("Limit jednej pozycji: 30 s. Cała lista: do 10 min. Po anulowaniu pozostałe pozycje mają status „Nie sprawdzono”. Publiczne adresy nie oznaczają darmowych kanałów."));
        ScrollView scroll=new ScrollView(activity);scroll.addView(box);page=new AlertDialog.Builder(activity).setTitle("Skaner portali • TXT").setView(scroll).setNegativeButton("Zamknij",(d,w)->close()).create();page.setOnCancelListener(d->close());page.show();if(page.getWindow()!=null){page.getWindow().setBackgroundDrawable(new android.graphics.drawable.ColorDrawable(0xff101a2c));page.getWindow().setLayout(-1,-1);}
    }
    private String selectedLabel(){return entries.isEmpty()?"Wynik pojawi się po sprawdzeniu listy.":entries.get(Math.min(selected,entries.size()-1)).label(selected)+"\n"+results.get(Math.min(selected,results.size()-1)).label();}
    void accept(Uri uri){if(!active()||busy)return;beginImport(()->activity.getContentResolver().openInputStream(uri));}
    interface Input {InputStream open()throws IOException;}
    void beginImport(Input input){if(!active()||busy)return;busy=true;int serial=++generation;AtomicBoolean operation=cancellation=new AtomicBoolean();message="Odczytuję TXT…";render();worker.execute(()->{try(InputStream stream=input.open()){importing=stream;PublicHttp.checkCancelled(operation);PortalTxt.Import parsed=PortalTxt.read(stream);main.post(()->{if(!active()||serial!=generation)return;busy=false;if(operation.get()){message="Anulowano import. Poprzednia lista zachowana.";render();return;}entries=new ArrayList<>(parsed.entries);results=new ArrayList<>();for(int i=0;i<entries.size();i++)results.add(PortalScanner.Result.status(PortalScanner.Status.NOT_CHECKED));selected=0;message="Wczytano "+entries.size()+" pozycji • błędne wiersze: "+parsed.invalidCount+" • duplikaty: "+parsed.duplicates+". Wybierz Sprawdź listę.";render();});}catch(Exception error){main.post(()->{if(!active()||serial!=generation)return;busy=false;message=operation.get()?"Anulowano import. Poprzednia lista zachowana.":"Nie wczytano TXT. Sprawdź limit 1 MiB / 200 pozycji i poprawne UTF-8 lub UTF-16 z BOM. Poprzednia lista zachowana.";render();});}finally{importing=null;}});}
    private void start(){
        if(!active()||busy||entries.isEmpty())return;busy=true;int serial=++generation;AtomicBoolean operation=cancellation=new AtomicBoolean();List<PortalTxt.Entry> input=new ArrayList<>(entries);results=new ArrayList<>();for(int i=0;i<input.size();i++)results.add(PortalScanner.Result.status(PortalScanner.Status.NOT_CHECKED));message="Sprawdzam listę…";render();
        worker.execute(()->{try{List<PortalScanner.Result> completed=engine.scan(input,operation,(index,result)->main.post(()->{if(!active()||serial!=generation)return;results.set(index,result);message="Pozycja "+(index+1)+" / "+input.size();if(page!=null){View root=page.getWindow().getDecorView();((TextView)root.findViewWithTag("scanner_status")).setText(message);((TextView)root.findViewWithTag("scanner_result")).setText(selectedLabel());}}));main.post(()->{if(!active()||serial!=generation)return;busy=false;results=new ArrayList<>(completed);int checked=0;for(PortalScanner.Result row:results)if(row.status!=PortalScanner.Status.NOT_CHECKED&&row.status!=PortalScanner.Status.CANCELLED)checked++;message=(operation.get()?"Przerwano":checked<entries.size()?"Osiągnięto limit czasu; pozostałe pozycje niesprawdzone":"Zakończono")+" • sprawdzono "+checked+" / "+entries.size()+". Odtwarzanie nadal wymaga próby w playerze.";render();});}
        catch(Exception error){main.post(()->{if(!active()||serial!=generation)return;busy=false;message="Przerwano sprawdzanie. Ukończone wyniki zachowano.";render();});}});
    }
    private void save(boolean play){if(!active()||busy||entries.isEmpty())return;PortalTxt.Entry entry=entries.get(selected);if(!entry.account()){close();listener.configure(entry.url);return;}if(results.get(selected).status!=PortalScanner.Status.CATALOG_AVAILABLE){message="Najpierw sprawdź tę pozycję i pobierz poprawny katalog.";render();return;}busy=true;int serial=++generation;AtomicBoolean operation=cancellation=new AtomicBoolean();String name="Portal TXT "+(selected+1);message="Ponownie pobieram wybrane źródło przed zapisaniem…";render();worker.execute(()->{try{ProviderRepository.SourceInfo source=repository.addScannedPortal(name,entry.url,entry.mac,operation,lifetimeGate,this::active);main.post(()->{if(!active()||serial!=generation)return;busy=false;boolean attemptPlayback=play&&!operation.get();close();listener.saved(source,attemptPlayback);});}catch(Exception error){main.post(()->{if(!active()||serial!=generation)return;busy=false;message=operation.get()?"Anulowano dodawanie.":"Nie zapisano źródła. Poprzednia biblioteka pozostała bez zmian.";render();});}});}
}
