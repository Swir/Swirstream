package com.swir.stream;

import android.content.Context;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.text.Normalizer;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/** Inference-only port of the exact Windows SwirIntentNet-1 topic model. */
public final class TasteNet {
    public static final String[] TOPICS = {"news","music","sports","movies","kids","documentary","entertainment","lifestyle","religious","general"};
    private static final Pattern WORD = Pattern.compile("[a-z0-9]+");
    private static volatile TasteNet bundled;
    private final String[] labels;
    private final double[] w1,b1,w2,b2;
    private final Set<String> vocabulary;
    public static final class Prediction {
        public final String label;
        public final double confidence,margin;
        public final boolean accepted;
        Prediction(String label,double confidence,double margin,boolean accepted) {
            this.label=label;this.confidence=confidence;this.margin=margin;this.accepted=accepted;
        }
    }
    public static TasteNet load(Context context) throws IOException {
        TasteNet current=bundled;
        if(current!=null)return current;
        synchronized(TasteNet.class) {
            if(bundled==null)try(InputStream stream=context.getAssets().open("model/tastenet.json")){bundled=read(stream);}
            return bundled;
        }
    }
    static TasteNet read(InputStream input) throws IOException {
        ByteArrayOutputStream bytes=new ByteArrayOutputStream();byte[] block=new byte[8192];int n;
        while((n=input.read(block))!=-1){if(bytes.size()+n>4_000_000)throw new IOException("Model przekracza limit.");bytes.write(block,0,n);}
        try{return new TasteNet(new JSONObject(new String(bytes.toByteArray(),StandardCharsets.UTF_8)));}
        catch(JSONException|IllegalArgumentException e){throw new IOException("Nieprawidłowy model TasteNet.",e);}
    }
    private TasteNet(JSONObject json) throws JSONException {
        if(!"SwirIntentNet-1".equals(json.getString("Format"))||json.getInt("Inputs")!=1024||json.getInt("Hidden")!=64)throw new JSONException("Model shape");
        JSONArray names=json.getJSONArray("Labels");if(names.length()!=10)throw new JSONException("Labels");
        labels=new String[10];Set<String> unique=new HashSet<>();
        for(int i=0;i<labels.length;i++){labels[i]=names.getString(i);if(!isTopic(labels[i])||!unique.add(labels[i]))throw new JSONException("Unknown label");}
        w1=array(json,"W1",1024*64);b1=array(json,"B1",64);w2=array(json,"W2",64*10);b2=array(json,"B2",10);
        JSONArray words=json.getJSONArray("Vocabulary");if(words.length()>20000)throw new JSONException("Vocabulary");
        vocabulary=new HashSet<>();for(int i=0;i<words.length();i++)vocabulary.add(words.getString(i));
    }
    private static double[] array(JSONObject json,String name,int length) throws JSONException {
        JSONArray input=json.getJSONArray(name);if(input.length()!=length)throw new JSONException(name+" shape");
        double[] output=new double[length];for(int i=0;i<length;i++){double v=input.getDouble(i);if(Double.isNaN(v)||Double.isInfinite(v))throw new JSONException(name+" finite");output[i]=v;}return output;
    }
    static boolean isTopic(String name){for(String topic:TOPICS)if(topic.equals(name))return true;return false;}
    public static String normalize(String text) {
        // .NET invariant casing keeps U+0130 unchanged before FormD; Java's
        // locale-neutral String casing expands it to lowercase i + combining dot.
        String original=text==null?"":text;StringBuilder lowered=new StringBuilder();
        for(int i=0;i<original.length();){int cp=original.codePointAt(i);lowered.appendCodePoint(cp==0x0130?cp:Character.toLowerCase(cp));i+=Character.charCount(cp);}
        String decomposed=Normalizer.normalize(lowered.toString().replace('ł','l'),Normalizer.Form.NFD);
        StringBuilder output=new StringBuilder();for(int i=0;i<decomposed.length();i++){char c=decomposed.charAt(i);if(Character.getType(c)!=Character.NON_SPACING_MARK)output.append(c);}return output.toString();
    }
    static List<String> words(String text){List<String> result=new ArrayList<>();Matcher matcher=WORD.matcher(normalize(text));while(matcher.find())result.add(matcher.group());return result;}
    static int bucket(String value,int dimensions){int h=0x811c9dc5;for(int i=0;i<value.length();i++)h=(h^value.charAt(i))*16777619;return (int)((h&0xffffffffL)%dimensions);}
    double[] features(String text) {
        double[] result=new double[1024];List<String> words=words(text);
        for(String word:words){result[bucket("w:"+word,1024)]+=2;String padded="^"+word+"$";for(int i=0;i+3<=padded.length();i++)result[bucket("c:"+padded.substring(i,i+3),1024)]+=.45;}
        for(int i=0;i+1<words.size();i++)result[bucket("b:"+words.get(i)+" "+words.get(i+1),1024)]+=1;
        normalizeVector(result);return result;
    }
    static void normalizeVector(double[] vector){double norm=0;for(double v:vector)norm+=v*v;norm=Math.sqrt(norm);if(norm>0)for(int i=0;i<vector.length;i++)vector[i]/=norm;}
    public Prediction predict(String text) {
        double[] x=features(text),hidden=new double[64],p=new double[10];
        for(int j=0;j<64;j++){double z=b1[j];for(int i=0;i<1024;i++)if(x[i]!=0)z+=w1[j*1024+i]*x[i];hidden[j]=Math.tanh(z);}
        double max=Double.NEGATIVE_INFINITY;
        for(int k=0;k<10;k++){p[k]=b2[k];for(int j=0;j<64;j++)p[k]+=w2[k*64+j]*hidden[j];max=Math.max(max,p[k]);}
        double sum=0;for(int k=0;k<10;k++){p[k]=Math.exp(p[k]-max);sum+=p[k];}for(int k=0;k<10;k++)p[k]/=sum;
        int first=0;for(int k=1;k<10;k++)if(p[k]>p[first])first=k;
        int second=first==0?1:0;for(int k=0;k<10;k++)if(k!=first&&p[k]>p[second])second=k;
        boolean known=false;for(String word:words(text))if(vocabulary.contains(word)){known=true;break;}
        double margin=p[first]-p[second];return new Prediction(labels[first],p[first],margin,known&&p[first]>=.62&&margin>=.25);
    }
}
