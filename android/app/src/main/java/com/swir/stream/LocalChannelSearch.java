package com.swir.stream;

import java.text.Normalizer;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

/** Literal, forgiving local typing. Does not expand a web query or change identity. */
final class LocalChannelSearch {
    private LocalChannelSearch() {}

    static Query prepare(String text) {return new Query(text);}

    /** Immutable text snapshot; build off the UI thread when the catalogue changes. */
    static final class Entry {
        private final List<Terms> names;
        private final boolean present;
        private Entry(List<Terms> names,boolean present){this.names=names;this.present=present;}
    }

    private static final class Terms {
        final List<String> words;final String joined;
        Terms(List<String> words){this.words=words;this.joined=join(words);}
    }

    static Entry index(Station station) {
        List<Terms> indexed=new ArrayList<>();if(station==null)return new Entry(indexed,false);
        List<String> feed=tokens(M3u.identifierName(station.feed));
        for(String name:M3u.searchNames(station)) {
            List<String> words=tokens(name);add(indexed,words);
            // A language accompanies the canonical name or its alias, never a
            // different source record. Avoid retaining redundant copies.
            if(!feed.isEmpty()&&!words.containsAll(feed)){List<String> combined=new ArrayList<>(words);combined.addAll(feed);add(indexed,combined);}
        }
        add(indexed,tokens(station.group));return new Entry(indexed,true);
    }

    private static void add(List<Terms> names,List<String> words) {
        if(words.isEmpty())return;for(Terms old:names)if(old.words.equals(words))return;names.add(new Terms(words));
    }

    private static String join(List<String> words) {
        StringBuilder out=new StringBuilder();for(String word:words){
            // Removing spaces between words must not turn two literal numbers
            // ("1 0") into a different channel number ("10").
            if(out.length()>0&&Character.isDigit(out.charAt(out.length()-1))&&Character.isDigit(word.charAt(0)))out.append('\u0001');
            out.append(word);
        }return out.toString();
    }

    static final class Query {
        private final List<String> words;
        private final String joined;
        private final boolean empty;
        private final Map<String,Boolean> textCache=new HashMap<>();
        private Query(String text) {
            empty=text==null||text.trim().isEmpty();words=tokens(text);joined=join(words);
        }

        boolean matches(Station station) {
            if(station==null)return false;if(empty)return true;return matches(index(station));
        }

        boolean matches(Entry entry) {
            if(entry==null||!entry.present)return false;if(empty)return true;if(words.isEmpty())return false;
            for(Terms name:entry.names)if(matchesTokens(name))return true;return false;
        }

        boolean matchesText(String text) {
            if(empty)return true;if(words.isEmpty()||text==null||text.isEmpty())return false;
            Boolean cached=textCache.get(text);if(cached!=null)return cached;
            boolean result=matchesTokens(new Terms(tokens(text)));if(textCache.size()<32)textCache.put(text,result);return result;
        }

        private boolean matchesTokens(Terms available) {
            // Case changes may erase the visible CamelCase boundaries. A cached
            // literal spelling also makes TVPINFO match TVP Info. A trailing
            // number must end here, so TVP1 never prefixes TVP10 or TVP100.
            if(available.joined.startsWith(joined)) {
                int end=joined.length();
                if(end==available.joined.length()||!Character.isDigit(joined.charAt(end-1))||!Character.isDigit(available.joined.charAt(end)))return true;
            }
            for(String word:words) {
                boolean found=false;boolean literal=word.charAt(0)=='+'||Character.isDigit(word.codePointAt(0));
                for(String candidate:available.words)if(literal?candidate.equals(word):candidate.startsWith(word)){found=true;break;}
                if(!found)return false;
            }
            return true;
        }
    }

    /** Split original case before folding so BBCNews and TVN24 keep their boundaries. */
    private static List<String> tokens(String value) {
        List<String> out=new ArrayList<>();if(value==null||value.isEmpty())return out;
        String normalized=Normalizer.normalize(value,Normalizer.Form.NFKD);StringBuilder clean=new StringBuilder(normalized.length());
        for(int i=0;i<normalized.length();) {
            int cp=normalized.codePointAt(i);i+=Character.charCount(cp);int type=Character.getType(cp);
            if(type==Character.NON_SPACING_MARK||type==Character.COMBINING_SPACING_MARK||type==Character.ENCLOSING_MARK)continue;
            clean.appendCodePoint(cp=='ł'?'l':cp=='Ł'?'L':cp);
        }
        String text=clean.toString();StringBuilder word=new StringBuilder();int previous=-1,kind=0;
        for(int i=0;i<text.length();) {
            int cp=text.codePointAt(i),nextOffset=i+Character.charCount(cp);int next=nextOffset<text.length()?text.codePointAt(nextOffset):-1;
            if(Character.isLetter(cp)) {
                boolean camel=kind==1&&Character.isUpperCase(cp)&&(Character.isLowerCase(previous)||Character.isUpperCase(previous)&&next>=0&&Character.isLowerCase(next));
                if(kind!=1||camel)flush(out,word);kind=1;word.appendCodePoint(Character.toLowerCase(cp));
            }else if(Character.isDigit(cp)) {
                if(kind!=2)flush(out,word);kind=2;word.append((char)('0'+Character.digit(cp,10)));
            }else if(cp=='+') {
                flush(out,word);int after=nextOffset;
                while(after<text.length()&&Character.isWhitespace(text.codePointAt(after)))after+=Character.charCount(text.codePointAt(after));
                if(after<text.length()&&Character.isDigit(text.codePointAt(after))){word.append('+');kind=2;i=after;previous=cp;continue;}
                out.add("+");kind=0;
            }else{flush(out,word);kind=0;}
            previous=cp;i=nextOffset;
        }
        flush(out,word);return out;
    }

    private static void flush(List<String> out,StringBuilder word) {
        if(word.length()>0){
            String text=word.toString();boolean unicode=false;for(int i=0;i<text.length();i++)if(text.charAt(i)>127){unicode=true;break;}
            // Full case folding covers expansions such as ß → SS and the two
            // lowercase sigma forms. ASCII has already been folded above.
            if(unicode)text=text.toUpperCase(Locale.ROOT).toLowerCase(Locale.ROOT).replace('\u03c2','\u03c3');
            out.add(text);word.setLength(0);
        }
    }
}
