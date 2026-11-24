package com.swir.stream;

import android.app.Activity;
import android.app.AlertDialog;
import android.text.Editable;
import android.text.TextWatcher;
import android.view.WindowManager;
import android.widget.*;
import java.text.Normalizer;
import java.util.*;

final class CountryPicker {
    interface Selection { void set(Set<String> codes); }
    private static String fold(String value) {
        return Normalizer.normalize(value.toLowerCase(Locale.ROOT).replace('ł','l'),Normalizer.Form.NFKD)
                .replaceAll("\\p{M}+","");
    }
    private static class CountryNames {
        static final Map<String,String> nativeNames = load();
        static Map<String,String> load() {
            Map<String,Set<String>> names=new HashMap<>();
            for (Locale locale:Locale.getAvailableLocales()) {
                String code=locale.getCountry();
                if(code.length()!=2)continue;
                names.computeIfAbsent(code,key->new HashSet<>()).add(fold(locale.getDisplayCountry(locale)));
            }
            Map<String,String> searchable=new HashMap<>();
            for (Map.Entry<String,Set<String>> row:names.entrySet()) searchable.put(row.getKey(),String.join(" ",row.getValue()));
            return searchable;
        }
    }
    static AlertDialog show(Activity activity, UiLanguage ui, String title, List<String> codes, List<String> names,
                            Set<String> current, Selection apply) {
        Set<String> checked = new LinkedHashSet<>(current);
        List<Integer> visible = new ArrayList<>();
        List<String> index = new ArrayList<>();
        for (int i=0;i<codes.size();i++) {
            String code=codes.get(i);
            String polish=new Locale("",code).getDisplayCountry(new Locale("pl"));
            index.add(fold(code+(code.equals("GB")?" UK ":" ")+names.get(i)+" "+WorldMarkets.name(code)+" "+polish)+" "+CountryNames.nativeNames.getOrDefault(code,""));
        }
        LinearLayout box=new LinearLayout(activity);box.setOrientation(LinearLayout.VERTICAL);
        int pad=(int)(16*activity.getResources().getDisplayMetrics().density);
        box.setPadding(pad,pad/2,pad,0);
        EditText search=new EditText(activity);search.setSingleLine(true);search.setTag("country_filter");
        search.setHint(ui.t("Filtruj kraje…"));box.addView(search);
        ListView list=new ListView(activity);list.setTag("country_options");list.setChoiceMode(ListView.CHOICE_MODE_MULTIPLE);
        box.addView(list,new LinearLayout.LayoutParams(-1,0,1));
        TextView count=new TextView(activity);box.addView(count);
        ArrayAdapter<String> adapter=new ArrayAdapter<>(activity,android.R.layout.simple_list_item_multiple_choice);
        list.setAdapter(adapter);
        Runnable updateCount=()->count.setText((ui.english?"Selected: ":"Wybrano: ")+checked.size());
        Runnable filter=()->{
            String query=fold(search.getText().toString().trim());
            visible.clear();list.clearChoices();adapter.setNotifyOnChange(false);adapter.clear();
            for (int i=0;i<codes.size();i++) if(index.get(i).contains(query)){visible.add(i);adapter.add(names.get(i));}
            adapter.notifyDataSetChanged();
            for(int i=0;i<visible.size();i++)list.setItemChecked(i,checked.contains(codes.get(visible.get(i))));
            updateCount.run();
        };
        list.setOnItemClickListener((parent,view,position,id)->{
            String code=codes.get(visible.get(position));
            if(list.isItemChecked(position))checked.add(code);else checked.remove(code);
            updateCount.run();
        });
        search.addTextChangedListener(new TextWatcher(){
            public void beforeTextChanged(CharSequence s,int start,int count,int after){}
            public void onTextChanged(CharSequence s,int start,int before,int count){filter.run();}
            public void afterTextChanged(Editable ignored){}
        });
        filter.run();
        AlertDialog dialog=new AlertDialog.Builder(activity).setTitle(title).setView(box)
                .setPositiveButton(ui.t("Zastosuj"),(d,w)->apply.set(new LinkedHashSet<>(checked)))
                .setNeutralButton(ui.t("Cały świat"),(d,w)->apply.set(new LinkedHashSet<>()))
                .setNegativeButton(ui.t("Anuluj"),null).create();
        dialog.getWindow().setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_STATE_ALWAYS_HIDDEN | WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE);
        dialog.show();dialog.getWindow().setLayout(-1,(int)(activity.getResources().getDisplayMetrics().heightPixels*0.85f));
        return dialog;
    }
}
