package com.swir.stream;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

final class WorldMarkets {
    private static final class Index {
        static final Map<String, String[]> ROWS = load();
        private static Map<String, String[]> load() {
            Map<String, String[]> rows = new HashMap<>();
            InputStream input = WorldMarkets.class.getResourceAsStream("/world-search.tsv");
            if (input == null) throw new IllegalStateException("Brak indeksu języków wyszukiwania.");
            try (BufferedReader reader = new BufferedReader(new InputStreamReader(input, StandardCharsets.UTF_8))) {
                String line;
                while ((line = reader.readLine()) != null) {
                    String[] fields = line.split("\t");
                    rows.put(fields[0], fields);
                }
            } catch (IOException error) {
                throw new IllegalStateException(error);
            }
            return rows;
        }
    }
    static String canonical(String code) { return "UK".equals(code) ? "GB" : code; }
    static String tvCatalogCode(String code) {
        return "GB".equals(code) ? "uk" : code.toLowerCase(Locale.ROOT);
    }
    static String name(String code) {
        String[] fields = Index.ROWS.get(canonical(code));
        return fields == null ? code : fields[1];
    }
    static List<String> terms(String code, boolean radio) {
        String[] fields = Index.ROWS.get(canonical(code));
        if (fields == null) return Collections.singletonList(radio ? "live radio" : "live TV");
        List<String> terms = new ArrayList<>();
        for (int i = radio ? 3 : 2; i < fields.length; i += 2) terms.add(fields[i]);
        return terms;
    }
}
