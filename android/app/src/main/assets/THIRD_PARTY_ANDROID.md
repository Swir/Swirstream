# Licencje i pochodzenie — SwirStream AI Android 0.3.0

## Kod aplikacji, modele i grafika

Autorski kod aplikacji oraz przygotowane dane treningowe/wagi TasteNet są objęte
MIT; pełny tekst: LICENSE_APP_MIT.txt. Licencja aplikacji nie zastępuje licencji
jej zależności. Port Java używa tych samych stałych wag co Windows, bez bibliotek
do zdalnego AI. Szczegóły i hash modelu: MODEL_CARD_ANDROID.md.

Ikona wykorzystuje oryginalny znak SwirStream przygotowany dla Windows 0.7.0.
Pochodzenie obrazu, prompt i hash wzorca zachowano w ICON_PROVENANCE.txt; Android
używa obrazu PNG skalowanego przez system, nie pliku Windows ICO. Pochodzenie ikony nie jest
testem wyglądu ani poprawności całego interfejsu Android.

Polskie nazwy krajów pochodzą z Unicode CLDR. Lista jest metadanymi, a nie bazą
działających transmisji. Licencja Unicode 3.0: licenses/UNICODE-3.0.txt.
Źródła danych: [Unicode CLDR](https://github.com/unicode-org/cldr-json) i
[kraje iptv-org](https://iptv-org.github.io/api/countries.json); kopia pochodzi
z materiałów projektu Windows pobranych 06.11.2026.

## Biblioteki w aplikacji

| Składnik | Wersja używana w wydaniu | Licencja / pochodzenie |
| --- | --- | --- |
| AndroidX Media3: exoplayer, hls, dash, ui oraz common/container/database/datasource/datasource-okhttp/decoder/extractor | 1.11.1 | Apache 2.0; Android Open Source Project |
| Kotlin standard library | 2.2.10 | Apache 2.0; JetBrains i autorzy |
| Kotlin stdlib-jdk7 i stdlib-jdk8 (moduły zgodności) | 1.9.10 | Apache 2.0; JetBrains i autorzy |
| OkHttp | 4.12.0 | Apache 2.0; Square i autorzy |
| Okio / Okio JVM | 3.6.0 | Apache 2.0; Square i autorzy |
| Guava Android | 33.3.1-android | Apache 2.0; Google i autorzy |
| failureaccess | 1.0.2 | Apache 2.0; licencja odziedziczona z Guava parent POM |
| listenablefuture | 9999.0-empty-to-avoid-conflict-with-guava | Pusty artefakt rozwiązujący konflikt zależności; Apache 2.0 |
| JetBrains annotations | 13.0 | Apache 2.0 |
| Pozostałe AndroidX: annotation, arch.core, collection, concurrent, core, customview, exifinterface, lifecycle, recyclerview, versionedparcelable | Dokładne wersje w dependency-inventory.json | Apache 2.0 według POM poszczególnych artefaktów |
| desugar_jdk_libs | 2.1.5 | GPL wersja 2 z wyjątkiem Classpath; także zawiadomienia właściwych plików |

Pełny wykaz współrzędnych Maven, oficjalnych URL POM, ich hashy i deklarowanych
licencji zapisano w dependency-inventory.json. Dokładne odczytane POM-y wraz
z nadrzędnymi POM Guava są w dependency-poms. Wersje faktycznej kompilacji mają
pierwszeństwo; przy zmianie zależności trzeba ponownie wygenerować ten wykaz.

Weryfikacja transportu własnych źródeł z 08.11.2026 obejmuje 38 współrzędnych
grafu zależności runtime oraz osobny desugaring. Węzły metadanych Maven nie oznaczają
osobnych bibliotek binarnych w APK. Nowe POM-y i oficjalne publikacje Maven mają
osobne SHA-256 oraz daty pobrania w wykazie. Raport `release-runtime-classpath.txt`
pochodzi z rozwiązywania zależności Gradle, sam w sobie nie potwierdza kompilacji APK.
Dat i pochodzenia wcześniejszych materiałów nie zmieniono.

Teksty Apache 2.0 i zawiadomienia są w licenses/APACHE-2.0.txt,
GUAVA-LICENSE.txt, KOTLIN-LICENSE.txt i KOTLIN-NOTICE.txt. Zachowany NOTICE Kotlin
pochodzi z repozytorium wydania i dotyczy dystrybucji Kotlin; do APK trafia biblioteka
standardowa, nie kompilator. Dokładne URL/hash pobranych tekstów: license-provenance.json.
Źródła: [Media3](https://github.com/androidx/media),
[Kotlin 2.2.10](https://github.com/JetBrains/kotlin/tree/v2.2.10),
[Guava 33.3.1](https://github.com/google/guava/tree/v33.3.1).

Własne źródła korzystają z transportu OkHttp, z obsługą danych przez Okio.
Pełne teksty licencji są w `licenses/OKHTTP-LICENSE.txt` i `licenses/OKIO-LICENSE.txt`.
Osobno zachowano tekst i upstream NOTICE Kotlin 1.9.10 dla modułów zgodności:
`licenses/KOTLIN-1.9.10-LICENSE.txt` oraz `licenses/KOTLIN-1.9.10-NOTICE.txt`.
Także ten NOTICE opisuje dystrybucję Kotlin, nie oznacza dołączenia kompilatora do APK.
Źródła wersjonowane: [OkHttp 4.12.0](https://github.com/square/okhttp/tree/parent-4.12.0),
[Okio 3.6.0](https://github.com/square/okio/tree/3.6.0),
[Kotlin 1.9.10](https://github.com/JetBrains/kotlin/tree/v1.9.10).

## Desugaring i zgodność ze starszym Androidem

com.android.tools:desugar_jdk_libs:2.1.5 jest elementem zgodności bibliotek Javy.
Jego POM deklaruje GPL-2.0 z wyjątkiem Classpath; nie jest to zależność wyłącznie
Apache 2.0. Tekst GPL i wyjątek zachowano w licenses/DESUGAR-GPL-2.0-WITH-CLASSPATH.txt,
wraz z ADDITIONAL_LICENSE_INFO i ASSEMBLY_EXCEPTION. Wyjątek dotyczy łączenia
z niezależnymi modułami na ich warunkach; należy zachować pozostałe warunki i
zawiadomienia składnika. Źródłem tekstu jest
[repozytorium Google](https://github.com/google/desugar_jdk_libs/blob/73170c345e6a762fc6a1f0301bb15218850023ef/LICENSE).

Do paczki źródłowej należy dołączyć folder dependency-source: zawiera migawkę
repozytorium desugar_jdk_libs z commita 73170c345e6a762fc6a1f0301bb15218850023ef,
który wprowadza VERSION_JDK11=2.1.5, oraz jej hash. Zawiera ona źródła, pliki
licencji i materiały budowania dostawcy. Nie wykonywano niezależnej kompilacji tej
biblioteki i nie deklarujemy zgodności bitowej migawki z opublikowanym JAR.
Oficjalny Maven sources.jar dla 2.1.5 zwracał 404; zamiast niego zachowano tę
wersjonowaną migawkę repozytorium. Skrypt Gradle aplikacji opisuje użyte zależności
i standardowe przekształcenie bibliotek dla Androida. Nie modyfikowano ich źródeł.

Duże archiwum źródeł jest materiałem dołączanym do dystrybucji źródłowej; nie trzeba
włączać go do zasobów APK. Teksty licencji i zawiadomienia powinny towarzyszyć APK.
Kod systemowego Androida, dekodery urządzenia i jego firmware nie są pakowane
przez ten projekt. Windowsowe .NET, VLC, libmpv i FFmpeg DLL nie należą do APK.

## Dane pobierane podczas pracy

Katalogi i strony są odczytywane na żywo; aplikacja nie udostępnia własnego serwera TV.
Główne źródła: [iptv-org](https://github.com/iptv-org/iptv),
[iptv-org API](https://github.com/iptv-org/api),
[Radio Browser](https://docs.radio-browser.info/),
[Free-TV/IPTV](https://github.com/Free-TV/IPTV) oraz publiczne strony nadawców.
Deep Search odczytuje również światowy `index.m3u` i `streams.json` z iptv-org API;
metadane `channels.json` dostarczają nazw, krajów, stron nadawców oraz powiązań
`replaced_by`. Wszystkie te pliki iptv-org są materiałami jednego projektu,
a mirrory nie oznaczają niezależnych danych. Free-TV jest odrębną listą.

Wyszukiwanie korzysta z [Bing RSS](https://www.bing.com/search?format=rss)
oraz pomocniczo [DuckDuckGo HTML](https://html.duckduckgo.com/html/).
Zapytania są wysyłane do tych usług; ich wyniki nie są modelem AI pakowanym do APK.
Publiczne wyszukiwanie nie używa danych z kont dodanych do „Moje źródła”,
nie wykonuje CAPTCHA i zgłasza odmowę dostępu lub niezgodny format. Dostępność dzisiejszej odpowiedzi
nie jest gwarancją przyszłej obsługi tego interfejsu przez dostawcę.

Kopie katalogów mogą być używane przez sześć godzin, a przy awarii do siedmiu dni
z zachowaniem pierwotnych dat. Kontrola manifestu nie odmładza danych katalogu.
„Odśwież” aktualizuje dane runtime; nowe adaptery usług i zmiany kodu wymagają APK.
EPG: [IPTV-EPG](https://iptv-epg.org/guides) oraz [FreeEPG](https://free-epg.de/),
lub źródło XMLTV wskazane przez użytkownika.

Obecność adresu, licencja katalogu i udana odpowiedź serwera nie potwierdzają
działania kanału, uprawnień nadawania ani dostępu do płatnego pakietu. Nośniki
i ramówki pozostają u swoich dostawców. Aplikacja nie obchodzi DRM ani logowania.
Przy eksporcie znalezionej playlisty zachowywany jest adres jej źródła.

„Moje źródła” łączy się z adresem playlisty, kontem Xtream lub portalem MAC
wskazanym przez użytkownika. Dane uwierzytelniające należą do użytkownika i jego
operatora; nie są katalogiem publicznych kont. Użytkownik dostarcza też adres
licencji dla własnej transmisji DRM. Media3 korzysta z systemowego Android MediaDrm;
obsługa Widevine i ClearKey zależy od urządzenia, formatu i warunków licencji.
Kod i klucze komercyjnego systemu DRM nie są dołączane przez ten projekt.
Opis obsługi: [Media3 DRM](https://developer.android.com/media/media3/exoplayer/drm).
Strony [Streamtest.co](https://streamtest.co/) i [Streamtest.in](https://streamtest.in/logs)
są zewnętrznymi narzędziami; aplikacja otwiera ich stronę bez adresu kanału i danych konta.

## Narzędzia i testy

Gradle/Android Gradle Plugin, kompilator Java, SDK Android i biblioteki testowe
nie są aplikacjami dołączonymi do uruchamianego APK. JUnit, AndroidX test runner/rules
oraz testowa biblioteka org.json służą kompilacji lub oddzielnemu test APK.
Nie należy mieszać ich wykazu z runtime APK. Weryfikację zależności, podpisu,
testów i końcowej paczki dokumentuje raport wydania, nie sam ten plik.
