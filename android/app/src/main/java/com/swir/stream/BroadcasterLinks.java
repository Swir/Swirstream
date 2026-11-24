package com.swir.stream;

import java.net.URI;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

final class BroadcasterLinks {
    private static final int FLAGS = Pattern.CASE_INSENSITIVE | Pattern.UNICODE_CASE;
    private static final Pattern TAG = Pattern.compile("<(a|iframe)\\b([^<>]{0,8192})>", FLAGS);
    private static final Pattern ATTRIBUTE = Pattern.compile("\\s(href|src|title|aria-label)\\s*=\\s*([\"'])(.*?)\\2", FLAGS | Pattern.DOTALL);
    private static final Pattern SIGNAL = Pattern.compile("(?<![\\p{L}\\p{N}])(?:live(?:stream|streams|tv)?|player|embed|stream(?:ing)?|na[-_ ]?[żz]ywo|direc(?:t|to|tos)|dirett[ae]|ao[-_ ]vivo|en[-_ ]vivo|zive|živě|naživo|наживо|uživo|уживо|canlı|direkte|direktsänd|trực tiếp|siaran langsung|прямой эфир|مباشر|שידור חי|ζωντανά|tiešraidē|tiesiogiai|suora lähetys)(?![\\p{L}\\p{N}])|(?:直播(?!回放)|生放送|ライブ配信|실시간|생방송|ถ่ายทอดสด|लाइव|সরাসরি)", FLAGS);
    private static final Pattern EXCLUDED = Pattern.compile("(?:^|[/_-])(?:replay|catchup|vod|podcasts?|archiwum|nachrichtenarchiv)(?:[/_.-]|$)|\\.(?:js|css|png|jpe?g|webp|svg|gif|ico|mp4|pdf)$", FLAGS);
    private static final Pattern NESTED = Pattern.compile("<a\\b", FLAGS);
    private static final Pattern MARKUP = Pattern.compile("<[^>]*>");
    private static final Pattern CLOSE = Pattern.compile("</a", FLAGS);

    static int pathScore(URI address) {
        String path = address.getPath().replaceFirst("/+$", "");
        if (EXCLUDED.matcher(path).find()) return -1;
        String tail = path.substring(path.lastIndexOf('/') + 1);
        if (tail.length() <= 30 && SIGNAL.matcher(tail).find()) return 3;
        String lower = path.toLowerCase(Locale.ROOT);
        if (tail.length() <= 40 && (lower.contains("/live-tv/") || lower.contains("/live-radio/"))) return 3;
        return SIGNAL.matcher(path).find() || SIGNAL.matcher(address.getHost().split("\\.")[0]).find() ? 1 : 0;
    }

    static List<String> find(PublicHttp.Document doc, boolean embedded) {
        Map<String, Integer> scores = new LinkedHashMap<>();
        Matcher tag = TAG.matcher(doc.text);
        Matcher close = CLOSE.matcher(doc.text);
        while (tag.find()) {
            if (embedded != tag.group(1).equalsIgnoreCase("iframe")) continue;
            String target = "";
            StringBuilder label = new StringBuilder();
            Matcher attribute = ATTRIBUTE.matcher(tag.group(2));
            while (attribute.find()) {
                String name = attribute.group(1).toLowerCase(Locale.ROOT);
                String value = M3u.entities(attribute.group(3));
                if (name.equals(embedded ? "src" : "href")) target = value.trim();
                if (name.equals("title") || name.equals("aria-label")) label.append(' ').append(value);
            }
            if (target.isEmpty() || target.startsWith("#")) continue;
            String url = WebCrawler.resolve(doc.url, target).split("#", 2)[0];
            if (!PublicHttp.allowedDocument(url) || PublicHttp.browserOnly(url) || SourcePrivacy.publicCredentials(url)
                    || url.equals(doc.url.split("#", 2)[0])) continue;
            String host = WebCrawler.host(url);
            if (!embedded && !host.equals(WebCrawler.host(doc.url))) continue;
            if (embedded && host.matches(".*(?:doubleclick|googleads|adservice|adsystem).*")) continue;
            int score = pathScore(URI.create(url));
            if (score < 0) continue;
            if (!embedded && close.region(tag.end(), Math.min(doc.text.length(), tag.end() + 512)).find()) {
                String body = doc.text.substring(tag.end(), close.start());
                if (!NESTED.matcher(body).find()) label.append(' ').append(M3u.entities(MARKUP.matcher(body).replaceAll(" ")));
            }
            if (label.length() <= 240 && SIGNAL.matcher(label).find()) score += 2;
            if (score > 0) scores.put(url, Math.max(score, scores.getOrDefault(url, 0)));
        }
        List<String> sorted = new ArrayList<>(scores.keySet());
        sorted.sort((first, second) -> Integer.compare(scores.get(second), scores.get(first)));
        return new ArrayList<>(sorted.subList(0, Math.min(sorted.size(), embedded ? 2 : 3)));
    }
}
