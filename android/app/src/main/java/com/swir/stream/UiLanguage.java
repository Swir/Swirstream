package com.swir.stream;

import java.io.BufferedReader;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.Collections;
import java.util.Locale;
import java.util.Set;
import org.json.JSONObject;

final class UiLanguage {
    final boolean english;
    UiLanguage(String preference, Locale device) {
        english = "en".equals(preference) || !"pl".equals(preference) && !"pl".equals(device.getLanguage());
    }
    private static class English {
        static final JSONObject words = read();
        static JSONObject read() {
            try (InputStream in = UiLanguage.class.getResourceAsStream("/interface.en.json")) {
                if (in == null) throw new IllegalStateException("Missing English interface resource.");
                BufferedReader reader = new BufferedReader(new InputStreamReader(in, StandardCharsets.UTF_8));
                StringBuilder json = new StringBuilder();
                for (String line; (line=reader.readLine())!=null;) json.append(line);
                return new JSONObject(json.toString());
            } catch (Exception error) { throw new IllegalStateException(error); }
        }
    }
    String t(String label) { return english ? English.words.optString(label,label) : label; }
    static Set<String> initialCountries(Locale locale) {
        String code=WorldMarkets.canonical(locale.getCountry());
        return code.length()==2 && !WorldMarkets.name(code).equals(code) ? Collections.singleton(code) : Collections.emptySet();
    }
}
