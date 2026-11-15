# Budowanie Androida 0.2.1

Gotowa APK zawiera bibliotekę odtwarzacza i modele; użytkownik nie instaluje Javy, VLC ani narzędzi programistycznych.
Kod wydania ma `versionName 0.2.1` i `versionCode 3`. Nie zmienia paczki Windows.

Do kompilacji źródeł potrzebne są JDK 17, Android SDK 36.1, build-tools 36.0.0 i internet do repozytoriów Google/Maven Central. Ustaw `JAVA_HOME` oraz `ANDROID_HOME`. Gradle Wrapper 9.3.1 ma przypiętą sumę SHA-256 dystrybucji. AGP: 9.0.1; dokładne wersje bibliotek są w `app/build.gradle` i dokumentacji zależności.

Uruchom `build.ps1` w PowerShell. Wersja deweloperska powstaje w `app/build/outputs/apk/debug/app-debug.apk`.

`build.ps1 -Mode Release` tworzy niepodpisaną APK wydania. Podpisz ją własnym trwałym kluczem przy użyciu `apksigner` z Android SDK. Klucza i hasła nie ma w archiwum źródeł. Samodzielnie podpisana paczka nie aktualizuje wydania podpisanego innym kluczem; nie odinstalowuj użytkownikowi starej aplikacji bez kopii jego danych.

Testy lokalne: `gradlew.bat :app:testDebugUnitTest`. Testy w emulatorze: `gradlew.bat :app:connectedDebugAndroidTest`. Testy produkcyjnych usług są wyłączone domyślnie; uruchamia się je jawnie z argumentem instrumentacji `network=true`. Są zależne od bieżącej dostępności nadawców. Testy są w osobnej APK; główna aplikacja nie zawiera odbiornika poleceń testowych.

Wariant wydania: `gradlew.bat :app:assembleRelease :app:assembleReleaseAndroidTest :app:lintRelease :app:testReleaseUnitTest -PtestBuildType=release`. Przed instrumentacją podpisz APK aplikacji i APK testów tym samym kluczem. Konfiguracja L8 testów zachowuje także reguły bibliotek aplikacji: bez tego AGP 9.0.1 może odchudzić testowy `j$` tak, że podczas instrumentacji przesłania on potrzebne metody właściwego APK. Ta poprawka dotyczy wyłącznie pakietu testów.

Regresje Deep Search obejmują niezależne awarie katalogów/wyszukiwarek, nazwy i warianty
językowe kanałów, filtry kraju, anulowanie bez nadpisania katalogu, manifesty HLS i odrzucanie HTML/VOD
oraz zachowanie niepotwierdzonych adresów. Test HTTP lub manifestu nie zastępuje próby
dekodowania obrazu przez odtwarzacz. Samo dodanie testów albo poprawna kompilacja nie
oznaczają ich zaliczenia na urządzeniu. Zapisz wyniki instrumentacji, testy rzeczywistych
źródeł, podpis, wersję i SHA-256 dokładnie tej APK, którą przekazujesz użytkownikowi.

Do pakowania służy `tools/package_release.py --apk ABSOLUTE_APK --report FINAL_JSON --out OUTPUTS`.
Skrypt nie kompiluje ani nie podpisuje aplikacji. Wymaga raportu z `releaseReady=true`
i zgodnej sumy APK, następnie tworzy paczkę i archiwum źródeł 0.2.1. Nie zastępuj raportu
testami starszej wersji i nie nadpisuj artefaktów 0.1.0 podczas przygotowywania aktualizacji.
Aktualizacja z zachowaniem danych wymaga tego samego klucza podpisującego co wcześniejsza
instalacja oraz wyższego `versionCode`.

Kod aplikacji jest na licencji MIT. `docs/dependency-source` zawiera zachowane źródła desugar wraz z pochodzeniem i haszami. Pozostałe licencje i pochodzenie modeli/ikony opisano w `THIRD_PARTY_ANDROID.md`, `MODEL_CARD_ANDROID.md` i `ICON_PROVENANCE.txt`.
