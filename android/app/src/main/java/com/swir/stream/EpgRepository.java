package com.swir.stream;

import android.content.Context;
import android.util.AtomicFile;
import android.util.Xml;
import org.json.*;
import org.xmlpull.v1.XmlPullParser;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.text.Normalizer;
import java.text.SimpleDateFormat;
import java.util.*;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.regex.*;
import java.util.zip.GZIPInputStream;

/** XMLTV schedules. HTTP availability never constitutes proof of a playable channel. */
public final class EpgRepository {
    static final long HOUR = 3_600_000L;
    private final File directory;
    private final android.content.SharedPreferences settings;
    private final Map<String, Feed> feeds = new ConcurrentHashMap<>();
    private final Map<String, String> notices = new ConcurrentHashMap<>();
    private static final String[] TEMPLATES = {"https://iptv-epg.org/files/epg-%s.xml.gz", "https://free-epg.de/api/epg/%s.xml.gz"};

    public EpgRepository(Context context) {
        directory = new File(context.getFilesDir(), "epg"); directory.mkdirs();
        settings = context.getSharedPreferences("epg-sources", Context.MODE_PRIVATE);
    }
    public void setSource(String country, String url) {
        country = country.toUpperCase(Locale.ROOT);
        if (!country.matches("[A-Z]{2}")) throw new IllegalArgumentException("Wybierz kraj EPG.");
        url = url.trim();
        if (!url.isEmpty()) {
            try { checkedUrl(url, false); } catch (Exception e) { throw new IllegalArgumentException("Podaj publiczny adres HTTP lub HTTPS XMLTV."); }
        }
        if (url.isEmpty()) settings.edit().remove(country).apply(); else settings.edit().putString(country, url).apply();
        feeds.remove(country);
    }
    public String source(String country) { return settings.getString(country, ""); }
    public String notice() { return String.join("\n", notices.values()); }
    public long dataAt() { long at = 0; for (Feed f : feeds.values()) { long v = f.modified > 0 ? Math.min(f.downloaded, f.modified) : f.downloaded; at = at == 0 ? v : Math.min(at, v); } return at; }
    public void refresh(Set<String> countries, List<Station> stations, CatalogRepository.Progress progress, AtomicBoolean cancelled) {
        int count = 0; long deadline = android.os.SystemClock.elapsedRealtime() + 90_000;
        Set<String> selected = new LinkedHashSet<>(countries);
        if (selected.isEmpty()) for (Station s : stations) if (!"radio".equalsIgnoreCase(s.kind) && s.country.matches("[A-Za-z]{2}")) selected.add(s.country.toUpperCase(Locale.ROOT));
        Set<String> active = new LinkedHashSet<>(); for (String value : selected) { if (active.size() == 4) break; active.add(value.toUpperCase(Locale.ROOT)); }
        feeds.keySet().retainAll(active); notices.keySet().retainAll(active);
        if (selected.size() > 4) notices.put("limit", "EPG obejmuje pierwsze 4 wybrane kraje, aby ograniczyć pamięć na telefonach i projektorach.");
        for (String raw : selected) {
            if (cancelled.get() || ++count > 4 || android.os.SystemClock.elapsedRealtime() >= deadline) break;
            String country = raw.toUpperCase(Locale.ROOT);
            if (!country.matches("[A-Z]{2}")) continue;
            progress.update("EPG • " + country + " • pobieranie ramówki");
            String override = settings.getString(country, "");
            List<String> urls = override.isEmpty() ? Arrays.asList(String.format(Locale.ROOT, TEMPLATES[0], country.toLowerCase(Locale.ROOT)), String.format(Locale.ROOT, TEMPLATES[1], country.toLowerCase(Locale.ROOT))) : Collections.singletonList(override);
            Feed fallback = null, chosen = null;
            List<String> errors = new ArrayList<>();
            for (String url : urls) {
                if (cancelled.get() || android.os.SystemClock.elapsedRealtime() >= deadline) return;
                Feed cached = read(country, url);
                long now = System.currentTimeMillis();
                if (valid(cached, now, 6)) { chosen = cached; break; }
                if (valid(cached, now, 48)) fallback = cached;
                try {
                    Fetch result = fetch(url, cancelled);
                    chosen = parse(result.bytes, country, url, now, result.modified, cancelled);
                    save(chosen); break;
                } catch (Exception e) { errors.add(e.getMessage() == null ? e.getClass().getSimpleName() : e.getMessage()); }
            }
            if (chosen == null) chosen = fallback;
            if (chosen != null) {
                feeds.put(country, chosen);
                notices.put(country, country + ": " + chosen.channels.size() + " ramówek • " + (errors.isEmpty() ? "aktualne audycje" : "zapis awaryjny / źródło zapasowe • " + String.join("; ", errors)));
            } else { feeds.remove(country); notices.put(country, country + ": brak aktualnego EPG • " + String.join("; ", errors)); }
        }
    }
    public String now(Station station) { Programme p = slot(station, System.currentTimeMillis(), false); return p == null ? "Teraz: brak danych EPG" : "Teraz " + time(p.start) + "–" + time(p.stop) + "  " + p.title; }
    public String next(Station station) { Programme p = slot(station, System.currentTimeMillis(), true); return p == null ? "Następnie: brak danych EPG" : "Następnie " + time(p.start) + "  " + p.title; }
    private String time(long at) { return new SimpleDateFormat("HH:mm", Locale.getDefault()).format(new Date(at)); }
    Programme slot(Station s, long now, boolean wantNext) {
        Feed feed = feeds.get(s.country.toUpperCase(Locale.ROOT));
        if ("radio".equalsIgnoreCase(s.kind) || !valid(feed, now, 48)) return null;
        Channel match = match(feed, s);
        if (match == null) return null;
        Programme current = null; int overlaps = 0;
        for (Programme p : match.programmes) if (p.start <= now && now < p.stop) { current = p; overlaps++; }
        if (overlaps != 1) current = null;
        if (!wantNext) return current;
        long after = current == null ? now : current.stop;
        for (Programme p : match.programmes) if (p.start >= after && p.start > now) return p;
        return null;
    }
    static Channel match(Feed f, Station s) {
        // Timeshift/language variants must never silently inherit another programme schedule.
        if (Pattern.compile("\\+\\s*\\d").matcher(s.name).find() || (!s.feed.isEmpty() && !s.feed.matches("(?i)^(sd|hd|fhd|uhd|4k|\\d{3,4}p)$"))) return null;
        String stationId = !s.tvgId.isEmpty() ? s.tvgId : s.id;
        if (stationId.contains("@")) return null;
        if (!stationId.isEmpty() && f.channels.containsKey(stationId)) return f.channels.get(stationId);
        String key = nameKey(s.name, s.country);
        if (key.isEmpty()) return null;
        Channel result = null;
        for (Channel c : f.channels.values()) {
            boolean equal = false;
            for (String name : c.names) if (nameKey(name, s.country).equals(key)) { equal = true; break; }
            if (equal) { if (result != null) return null; result = c; }
        }
        return result;
    }
    static String nameKey(String text, String country) {
        text = text.replaceFirst("(?i)^" + Pattern.quote(country) + "\\s*[-|:]\\s*", "");
        text = Normalizer.normalize(text.toLowerCase(Locale.ROOT).replace('ł', 'l'), Normalizer.Form.NFD).replaceAll("\\p{M}", "");
        return text.replaceAll("\\b(hd|sd|fhd|uhd|4k)\\b|\\(\\d{3,4}p\\)", "").replaceAll("[^\\p{L}\\p{N}+]", "");
    }
    static long parseTime(String value) {
        if (value == null) return 0;
        Matcher m = Pattern.compile("^(\\d{14})\\s*([+-]\\d{4}|Z)?$").matcher(value);
        if (!m.matches()) return 0;
        String zone = m.group(2); if (zone == null || zone.equals("Z")) zone = "+0000";
        try { SimpleDateFormat format = new SimpleDateFormat("yyyyMMddHHmmss Z", Locale.ROOT); format.setLenient(false); return format.parse(m.group(1) + " " + zone).getTime(); } catch (Exception ignored) { return 0; }
    }
    static final class Programme {
        final long start, stop; final String title;
        Programme(long start, long stop, String title) { this.start = start; this.stop = stop; this.title = title; }
    }
    static final class Channel {
        final String id; final List<String> names = new ArrayList<>(); final List<Programme> programmes = new ArrayList<>();
        Channel(String id) { this.id = id; }
    }
    static final class Feed {
        String country, source; long downloaded, modified;
        final Map<String, Channel> channels = new LinkedHashMap<>();
    }
    static boolean valid(Feed f, long now, int hours) {
        if (f == null || f.downloaded > now + 300_000 || f.downloaded < now - hours * HOUR || (f.modified != 0 && (f.modified < now - 48 * HOUR || f.modified > now + 600_000))) return false;
        for (Channel c : f.channels.values()) for (Programme p : c.programmes) if (p.stop > now && p.start < now + 24 * HOUR) return true;
        return false;
    }
    static Feed parse(byte[] bytes, String country, String source, long downloaded, long modified, AtomicBoolean cancelled) throws Exception {
        if (modified > downloaded + 600_000 || (modified > 0 && modified < downloaded - 48 * HOUR)) throw new IOException("Nieaktualna data źródła EPG");
        InputStream raw = new ByteArrayInputStream(bytes);
        if (bytes.length > 2 && (bytes[0] & 255) == 31 && (bytes[1] & 255) == 139) raw = new GZIPInputStream(raw);
        Feed feed = new Feed(); feed.country = country; feed.source = source; feed.downloaded = downloaded; feed.modified = modified;
        try (InputStream input = new LimitedInputStream(raw, 80_000_000)) {
            XmlPullParser parser = Xml.newPullParser(); parser.setFeature(XmlPullParser.FEATURE_PROCESS_NAMESPACES, false);
            parser.setInput(input, null);
            Channel channel = null; String programmeChannel = "", title = ""; long start = 0, stop = 0; boolean inProgramme = false, root = false; int total = 0, retained = 0;
            for (int event = parser.getEventType(); event != XmlPullParser.END_DOCUMENT; event = parser.nextToken()) {
                if (cancelled.get()) throw new InterruptedIOException("Anulowano EPG");
                if (event == XmlPullParser.DOCDECL) throw new IOException("Niedozwolona deklaracja XML");
                if (event == XmlPullParser.ENTITY_REF && !safeEntity(parser.getName())) throw new IOException("Niedozwolona encja XML");
                if (event == XmlPullParser.START_TAG) {
                    String name = parser.getName();
                    if (!root) { if (!"tv".equals(name)) throw new IOException("To nie jest XMLTV"); root = true; }
                    if (name.equals("channel")) { String id = parser.getAttributeValue(null,"id"); channel = id != null && id.length() < 300 ? feed.channels.computeIfAbsent(id, Channel::new) : null; if (feed.channels.size() > 5000) throw new IOException("Za dużo kanałów EPG"); }
                    else if (name.equals("display-name") && channel != null) { String value = parser.nextText(); if (!value.isEmpty() && value.length() < 300 && channel.names.size() < 20) channel.names.add(value); }
                    else if (name.equals("programme")) {
                        if (++total > 600_000) throw new IOException("Za duża ramówka");
                        inProgramme = true; programmeChannel = parser.getAttributeValue(null, "channel"); start = parseTime(parser.getAttributeValue(null, "start")); stop = parseTime(parser.getAttributeValue(null, "stop")); title = "";
                    } else if (name.equals("title") && inProgramme) { String value = parser.nextText().trim(); if (title.isEmpty()) title = value.substring(0, Math.min(500, value.length())); }
                } else if (event == XmlPullParser.END_TAG) {
                    if (parser.getName().equals("channel")) channel = null;
                    if (parser.getName().equals("programme")) {
                        if (programmeChannel != null && programmeChannel.length() < 300 && !title.isEmpty() && start > 0 && stop > start && stop - start <= 24 * HOUR && stop > downloaded && start < downloaded + 24 * HOUR) {
                            Channel c = feed.channels.computeIfAbsent(programmeChannel, Channel::new);
                            if (feed.channels.size() > 5000) throw new IOException("Za dużo kanałów EPG");
                            c.programmes.add(new Programme(start, stop, title)); retained++;
                            c.programmes.sort(Comparator.comparingLong(p -> p.start));
                            if (c.programmes.size() > 48) { c.programmes.remove(c.programmes.size() - 1); retained--; }
                            if (retained > 25000) throw new IOException("Ramówka przekracza limit pamięci urządzenia");
                        }
                        inProgramme = false;
                    }
                }
            }
        }
        for (Channel c : feed.channels.values()) {
            c.programmes.sort(Comparator.comparingLong(p -> p.start));
            Set<String> seen = new HashSet<>(); c.programmes.removeIf(p -> !seen.add(p.start + ":" + p.stop + ":" + p.title));
        }
        if (!valid(feed, downloaded, 48)) throw new IOException("Brak aktualnych audycji w ramówce");
        return feed;
    }
    static boolean safeEntity(String name) { return name != null && (Arrays.asList("amp","lt","gt","apos","quot").contains(name) || name.matches("#(?:[0-9]{1,7}|x[0-9a-fA-F]{1,6})")); }
    private File path(String country, String source) throws Exception {
        byte[] hash = MessageDigest.getInstance("SHA-256").digest((country + source).getBytes(StandardCharsets.UTF_8));
        StringBuilder name = new StringBuilder(); for (byte b : hash) name.append(String.format(Locale.ROOT,"%02x", b));
        return new File(directory, name + ".json");
    }
    private void save(Feed f) throws Exception {
        JSONObject root = new JSONObject().put("schema",1).put("country", f.country).put("source", f.source).put("downloaded", f.downloaded).put("modified",f.modified);
        JSONArray channels = new JSONArray();
        for (Channel c : f.channels.values()) {
            JSONArray programmes = new JSONArray(); for (Programme p : c.programmes) programmes.put(new JSONArray().put(p.start).put(p.stop).put(p.title));
            channels.put(new JSONObject().put("id",c.id).put("names",new JSONArray(c.names)).put("programmes",programmes));
        }
        root.put("channels",channels);
        AtomicFile atomic = new AtomicFile(path(f.country,f.source)); FileOutputStream out = null;
        try { out = atomic.startWrite(); out.write(root.toString().getBytes(StandardCharsets.UTF_8)); atomic.finishWrite(out); } catch (Exception e) { if (out != null) atomic.failWrite(out); throw e; }
    }
    private Feed read(String country, String source) {
        try {
            File file = path(country,source); if (file.length() > 25_000_000) return null;
            JSONObject json; try (InputStream in = new AtomicFile(file).openRead()) { json = new JSONObject(new String(readBytes(in,25_000_000,new AtomicBoolean()), StandardCharsets.UTF_8)); }
            if (json.getInt("schema") != 1 || !country.equals(json.getString("country")) || !source.equals(json.getString("source"))) return null;
            Feed f = new Feed(); f.country=country; f.source=source; f.downloaded=json.getLong("downloaded"); f.modified=json.getLong("modified");
            JSONArray array = json.getJSONArray("channels"); if (array.length()>5000) return null;
            int retained = 0;
            for(int i=0;i<array.length();i++) {
                JSONObject item=array.getJSONObject(i); Channel c=new Channel(item.getString("id"));
                JSONArray names=item.getJSONArray("names"); for(int j=0;j<Math.min(20,names.length());j++) c.names.add(names.getString(j));
                JSONArray programmes=item.getJSONArray("programmes"); if (programmes.length()>48) return null;
                retained += programmes.length(); if (retained > 25000) return null;
                for(int j=0;j<programmes.length();j++) { JSONArray p=programmes.getJSONArray(j); long start=p.getLong(0),stop=p.getLong(1); if(stop<=start || stop-start>24*HOUR)return null; c.programmes.add(new Programme(start,stop,p.getString(2))); }
                c.programmes.sort(Comparator.comparingLong(p -> p.start)); f.channels.put(c.id,c);
            }
            return f;
        } catch(Exception ignored) { return null; }
    }
    static final class Fetch { final byte[] bytes; final long modified; Fetch(byte[] b,long m){bytes=b;modified=m;} }
    static URL checkedUrl(String value, boolean dns) throws Exception {
        URL u=new URL(value); String host=u.getHost().toLowerCase(Locale.ROOT);
        if (!(u.getProtocol().equals("https")||u.getProtocol().equals("http")) || u.getUserInfo()!=null || host.isEmpty() || host.equals("localhost") || host.endsWith(".local") || host.endsWith(".localhost") || (u.getPort()!=-1&&u.getPort()!=80&&u.getPort()!=443)) throw new IOException("Niepubliczny adres EPG");
        if(dns) for(InetAddress a:InetAddress.getAllByName(host)) { byte[] b=a.getAddress(); if(a.isAnyLocalAddress()||a.isLoopbackAddress()||a.isLinkLocalAddress()||a.isSiteLocalAddress()||a.isMulticastAddress() || (b.length==16 && (b[0]&0xfe)==0xfc) || (b.length==4 && (b[0]&255)==100 && (b[1]&255)>=64 && (b[1]&255)<=127))throw new IOException("Niepubliczny serwer EPG"); }
        return u;
    }
    static Fetch fetch(String value, AtomicBoolean cancelled) throws Exception {
        long deadline = android.os.SystemClock.elapsedRealtime() + 20_000;
        for(int redirect=0;redirect<5;redirect++) {
            if(cancelled.get())throw new InterruptedIOException("Anulowano");
            if(android.os.SystemClock.elapsedRealtime() >= deadline)throw new IOException("Limit czasu pobierania EPG");
            URL url=checkedUrl(value,true); HttpURLConnection c=(HttpURLConnection)url.openConnection();
            c.setConnectTimeout(10000);c.setReadTimeout(15000);c.setInstanceFollowRedirects(false);c.setRequestProperty("User-Agent","SwirStreamAI-Android/0.1.0"); c.setRequestProperty("Accept-Encoding","identity");
            try {
                int status=c.getResponseCode();
                if(status>=300&&status<400){String location=c.getHeaderField("Location");if(location==null)throw new IOException("Przekierowanie bez adresu");value=new URL(url,location).toString();continue;}
                if(status!=200)throw new IOException("EPG HTTP "+status);
                if(c.getContentLength()>20_000_000)throw new IOException("EPG przekracza limit pobierania");
                try(InputStream in=c.getInputStream()){return new Fetch(readBytes(in,20_000_000,cancelled,deadline),c.getLastModified());}
            } finally {c.disconnect();}
        }throw new IOException("Zbyt wiele przekierowań EPG");
    }
    static byte[] readBytes(InputStream in,int limit,AtomicBoolean cancelled)throws IOException {
        return readBytes(in,limit,cancelled,Long.MAX_VALUE);
    }
    static byte[] readBytes(InputStream in,int limit,AtomicBoolean cancelled,long deadline)throws IOException {
        ByteArrayOutputStream out=new ByteArrayOutputStream();byte[] buffer=new byte[16384];int n;
        while((n=in.read(buffer))!=-1){if(cancelled.get())throw new InterruptedIOException("Anulowano");if(android.os.SystemClock.elapsedRealtime() >= deadline)throw new IOException("Limit czasu pobierania EPG");if(out.size()+n>limit)throw new IOException("Przekroczony limit danych");out.write(buffer,0,n);}return out.toByteArray();
    }
    static final class LimitedInputStream extends FilterInputStream {
        long remaining;LimitedInputStream(InputStream in,long limit){super(in);remaining=limit;}
        private void used(int n)throws IOException {if(n>0 && (remaining-=n)<0)throw new IOException("Zbyt duży rozpakowany XMLTV");}
        @Override public int read()throws IOException{int n=in.read();if(n>=0)used(1);return n;}
        @Override public int read(byte[] b,int off,int len)throws IOException{int n=in.read(b,off,len);used(n);return n;}
    }
}
