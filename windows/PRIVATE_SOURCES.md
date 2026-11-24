# Moje źródła — SwirStream AI Windows 0.9.8

Aktualne wydanie 0.12.0 obsługuje pojedynczo skonfigurowane źródła. Opisy skanera TXT poniżej dotyczą archiwalnej wersji 0.9.8. Nowe funkcje i instalator opisuje [wydanie 0.12.0](../docs/releases/v0.12.0.md).

## Skaner portali TXT w 0.9.8

Wejdź w **MAC Player → Skaner portali TXT**. Zaimportuj TXT, sprawdź podgląd,
następnie wybierz **Sprawdź listę**. Własne pary `URL|MAC` albo adres i kilka MAC
w następnych wierszach sprawdzane są przez ten sam klient, co zwykły MAC Player.
Duplikaty są usuwane. Daty dopisane po MAC są komentarzem, nie dowodem ważności.
Obsługiwane komentarze: pełne wiersze `#`/`;`, końcówka MAC po ` - ` albo `#`.
Niepoprawny wiersz przerywa przypisanie kolejnych MAC do wcześniejszego adresu.

Limit wynosi 1 MiB surowych danych i 200 unikalnych pozycji; przekroczenie limitu
odrzuca cały import. Kodowania: UTF-8, opcjonalnie z BOM, oraz UTF-16 z BOM.
Skan jest sekwencyjny, do 30 s na pozycję i 10 min łącznie. Przerwanie pozostawia
wcześniejsze wyniki; pozostałe pozycje są niesprawdzone. Zmiana profilu zamyka skaner.

URL bez MAC sprawdza wyłącznie publiczną stronę na porcie 80/443. Adres API lub
inny port wymagają własnego MAC w edytorze. Program nie próbuje logowania z takim
URL i nie zbiera danych kont z internetu. Rozpoznanie klienta Stalker/Ministra
nie oznacza bezpłatnego dostępu. Publiczny Eksplorator zachowuje źródło znaleziska.

Skan nie zapisuje kont. **Dodaj wybrany** ponownie pobiera katalog i używa
chronionego magazynu bieżącego profilu (maksymalnie 12 źródeł). Ponowne dodanie
tej samej pary aktualizuje istniejące źródło. **Odtwórz próbkę** robi to samo,
a potem przekazuje kanał do głównego playera, gdzie sprawdzany jest rzeczywisty
obraz i dźwięk. Katalog, limit katalogu i odtwarzanie mają odrębne statusy.
Raport CSV zawiera numer pozycji, wiersz TXT, status, liczbę kanałów i datę próby;
nie eksportuje adresów, MAC, tokenów ani odpowiedzi operatora.


## Rozpoznawanie portalu w 0.9.7

Po podaniu własnego adresu i MAC program sprawdza najwyżej trzy znane ścieżki API
na tym samym serwerze. Jawny adres pliku `.php` ma pierwszeństwo; typowe warianty
to `portal.php` i `server/load.php`. Zachowany jest podfolder instalacji.
Obsługiwany jest katalog MAC bez tokenu oraz pełna sesja handshake/profil.
Tryb bez tokenu wymaga poprawnej, niepustej odpowiedzi kategorii; sam HTTP 200
nie wystarcza. Jawna odmowa dostępu zatrzymuje połączenie. Pełny tryb nadal wymaga
aktywnego profilu. Nie tworzymy numerów SN ani dodatkowych identyfikatorów urządzenia.
Adres wpisany w ustawieniach pozostaje niezmieniony. Wybranie kanału ponownie
rozpoznaje połączenie. Stały adres z katalogu jest używany tylko wtedy, gdy operator
jawnie wyłączył obie flagi: link tymczasowy i równoważenie obciążenia. W pozostałych
przypadkach pobierany jest świeży link. Sam katalog nie potwierdza odtwarzania.
Po aktualizacji odśwież starszy zapis listy, aby pobrać także te metadane kanałów.

Duże listy portalu są czytane strumieniowo: maksymalnie 64 MB odpowiedzi, bez
przechowywania zbędnych pól. Zapisywane jest najwyżej 30 000 unikalnych kanałów
oraz 8 MB wybranych pól katalogu. Osiągnięcie limitu zapisanych pozycji jest jawnie
opisane przy źródle. Reszta dokumentu nadal podlega sprawdzeniu; odmowa dostępu,
uszkodzony JSON lub przekroczenie 64 MB zachowuje wcześniejszą listę i jej datę.

## Status własnego źródła

Lista publiczna nie jest listą portalu. Brak pomyślnego pobrania własnego źródła
otwiera pusty widok z jego nazwą i powodem błędu. Przy nieudanym odświeżeniu
wcześniejszych kanałów pokazywany jest zapis z jego oryginalną datą.
Etapy logowania, profilu, listy i przygotowania kanału mają bezpieczne komunikaty.
Błąd przygotowania kanału jest oddzielony od statusu ostatniego pobrania listy.
W trybie wymagającym profilu jego odrzucenie blokuje pobranie listy i linku kanału.
Aktywację oraz przypisanie MAC należy potwierdzić u operatora.


„Moje źródła” pozwala dodać własną playlistę lub konto otrzymane od operatora.
Kanały pozostają przypisane do wybranego profilu. Publiczne wyszukiwanie i lokalne
AI nie wyszukują cudzych loginów, adresów MAC ani kluczy abonamentów.

## Dodawanie i odświeżanie

W 0.9.2 przycisk **Wklej link** rozpoznaje pełne adresy konta `get.php`,
`player_api.php`, `xmltv.php` i jednoznaczne `/live/login/hasło/numer.ts`.
Podgląd pokazuje wyłącznie typ/serwer i informację, czy aktualizujesz istniejące konto.
Pola są maskowane; przed zatwierdzeniem formularza nie wykonuje się połączenia.
Pełny link działa również w polu **Serwer Xtream**. Port i podfolder pozostają.
Znak `+` w query oznacza spację, `%2B` oznacza plus; dekodowanie UTF-8 następuje raz.
Duplikaty, brak danych i nieznane wymagane parametry nie są po cichu pomijane.
**Dokładny URL M3U** zachowuje własny link z hasłem i parametrami operatora bez
konwersji do konta. To inny tryb niż oczyszczone playlisty znalezione przez AI.
Adres samego konta z query bez endpointu rozpoznajemy tylko w wybranym trybie Xtream.

Powtórny serwer/login zachowuje ID i nazwę konta. Nowe dane zostają zapisane dopiero
z poprawnie pobranym katalogiem; błąd lub anulowanie edycji pozostawia poprzednie
dane dostępu, kanały i datę, więc stara lista nadal może być odtwarzana. Nowe źródło,
którego pobranie się nie udało, pozostaje do poprawienia; anulowane nowe nie jest zapisywane.
Linki wpisane w główne wyszukiwanie są przechwytywane lokalnie, bez publicznego
zapytania ani zapisu hasła w raporcie, również gdy wcześniejszy skan jeszcze trwa.

Na górze okna wybierz **Playlisty URL**, **Xtream**, **MAC Player** lub **DRM**.
Każdy moduł ma własną listę i formularz z właściwymi polami. **Zapisz i połącz**
pobiera kanały oraz EPG i automatycznie otwiera listę. Postęp i przerwanie są
dostępne w tym samym oknie. **Odśwież i pokaż kanały** ponawia pobranie, a
**Pokaż zapisane kanały** korzysta z ostatniego katalogu bez połączenia.
Pole wyszukiwania na głównym ekranie filtruje już wczytane kanały danego źródła.
Wszystkie wcześniejsze źródła pozostają w tym samym chronionym magazynie: pliki,
bezpośrednie adresy i przeniesione kanały znajdują się w **Playlisty URL**.
Opcjonalny zbiorczy widok **Wszystkie źródła** pokazuje wszystkie rodzaje.
Samo pobranie listy nie potwierdza działania obrazu i dźwięku.

**AI: znajdź playlisty** w module Playlisty URL korzysta z głębokiego wyszukiwania
publicznych źródeł i bieżących krajów. Wyniki pokazują źródło, daty, liczbę
unikalnych dopasowań i odrębnie potwierdzone próby odtwarzania. Istniejące lokalne
preferencje zmieniają kolejność, nie status działania ani datę. Uczenie wyłączone
oznacza neutralny wpływ preferencji. **Dodaj do Playlist URL** wymaga zatwierdzenia
formularza; nie otwiera przeglądarki ani nie dodaje automatycznie kont. EPG zostaje
odczytane z pobranej playlisty. Ten sam URL nie tworzy kolejnej identycznej subskrypcji.

| Rodzaj | Obsługa tej wersji |
| --- | --- |
| Playlista URL M3U | Publiczny adres HTTP/HTTPS wskazany przez użytkownika, także `get.php` lub URL bez rozszerzenia. Typ rozpoznajemy po treści. Dostępne są względne adresy kanałów i XMLTV z nagłówka playlisty. |
| Plik M3U | Plik wybrany na komputerze. Jego dane są przechowywane jako prywatne źródło. |
| Transmisja URL | Jeden własny bezpośredni adres. Zapisanie nie uruchamia przeszukiwania internetu ani nie potwierdza odtwarzania. |
| Xtream | Adres serwera, login i hasło; kanały live, kategorie i XMLTV. Weryfikowane są aktywność i termin konta, jeśli operator je podaje. Filmy VOD, seriale i archiwum nie należą do tej integracji. |
| Portal MAC | Własny MAC zarejestrowany u operatora. Ograniczony zestaw ścieżek API, katalog bez tokenu lub pełna sesja handshake/profil, lista live. Przed odtwarzaniem świeża sesja oraz link tymczasowy albo jawnie oznaczony stały adres kanału. |
| DASH ClearKey | Jeden KID i jeden klucz CENC dla zgodnego DASH/H264. Wpisz własny klucz operatora albo bezpośredni URL jego serwera licencji ClearKey. |

Do jednego profilu można dodać maksymalnie 12 źródeł. Zmiana adresu lub danych
dostępu jest sprawdzana przez pobranie przed zastąpieniem dotychczasowej konfiguracji.
Błąd i anulowanie zachowują poprzednie dane, kanały oraz datę ostatniego pobrania.
Data dostawcy, data pobrania i ostatnia próba są odrębnymi informacjami.

EPG dla M3U/Xtream jest dopasowywane do identyfikatora kanału, a pomocniczo do nazwy.
Brak lub niezgodność ramówki nie oznacza błędu całej listy. Portale MAC w tej wersji
nie mają własnego adaptera EPG.

## Kontrola odtwarzania w 0.9.5

Przy ClearKey program obserwuje postęp wymaganych ścieżek przez co najmniej
12 sekund przed potwierdzeniem odtwarzania. Trwałe natywne błędy dekodera
powodują zatrzymanie, wyczyszczenie klucza i bezpieczny komunikat. Nie ustala
przyczyny na podstawie samego czarnego obrazu ani przesuwającego się czasu.
Nie jest to kryptograficzny dowód poprawnej treści ani uniwersalna obsługa DRM.
Dokładny wynik testu bieżącego EXE/DLL podaje RAPORT_WYDANIA.txt i verification.

Test negatywny w0.9.4 ujawnił postęp PTS i uszkodzone klatki przy złym kluczu,
bez postępu dźwięku. Ten nieudany wynik pozostaje zachowany jako historyczny;
starsze poniższe wyniki nie zastępują końcowej kontroli nowego wydania.
Kontrola0.9.5 wymaga konkretnego błędu runtime, zatrzymania i usunięcia klucza
przed ręcznym Stop, następnie ponownego dobrego AV i zwykłego filmu.
Sama niedostępność sieci ani dowolny błąd HTTP nie zaliczają negatywnego testu.

## Zgodność portali i DRM

MAC nie zastępuje abonamentu ani aktywacji urządzenia. Portale wymagające numeru
seryjnego, podpisu urządzenia, certyfikatu, dodatkowego parowania lub osobnej
aplikacji operatora mogą nie działać. Adaptery mają kontrolowane testy protokołu;
zakres próby konkretnego konta operatora podaje osobno raport wydania.

Windows korzysta z natywnego mpv/FFmpeg. Obsługa ClearKey jest ograniczona do
pojedynczego klucza DASH CENC i zgodnego materiału H264. Manifest z innymi lub
wieloma KID jest odrzucany. Własny klucz ma 32 znaki szesnastkowe, KID również
32 znaki bez separatorów. Przy URL licencji aplikacja wysyła żądanie ClearKey
z KID i oczekuje odpowiedniego klucza JWK. Zmiana kluczy w czasie transmisji,
niestandardowe protokoły licencji i wszystkie warianty ClearKey nie są objęte
deklarowaną zgodnością. Widevine i PlayReady nie są obsługiwane w Windows.

Źródło oficjalnego materiału kontrolnego:
[przykłady Google/Shaka](https://github.com/shaka-project/shaka-player/blob/main/demo/common/assets.js).
Używany publiczny test AXINOM to `protected_dash_1080p_h264_singlekey/manifest.mpd`.
Pierwszy test wykazał dekodowanie H264 1920×1080 oraz dźwięku i prawidłowe
wyczyszczenie klucza przy zatrzymaniu oraz przejściu do zwykłego filmu.
Krótki początek materiału może być nieszyfrowany, dlatego sam taki wynik nie jest
pełnym dowodem obsługi części szyfrowanej.

Test `--provider-player-test` obejmuje pobranie przez magazyn źródeł,
rozwiązanie konfiguracji i ten sam natywny odtwarzacz co aplikacja. Wymaga co
najmniej 12 sekund pozycji filmu i zdekodowanego wideo, bieżącego postępu dźwięku,
negatywnego testu błędnego klucza oraz powtórnego testu dobrego klucza. Raport
zawiera hashe EXE, kodu aplikacji i biblioteki odtwarzacza. Ten test używa jawnego
klucza demonstracyjnego, nie rzeczywistego serwera licencji komercyjnej.

Pełny test aplikacji z 08.11.2026 zakończył się powodzeniem
(`native-bomfix.json`, mpv 0.42.0 / FFmpeg N-128545):

| Kontrola | Zaobserwowany wynik |
| --- | --- |
| Właściwy klucz, magazyn → odtwarzacz | Wideo 1920×1080; zdekodowany PTS 12,231 s; 58 obserwacji postępu wideo i 58 dźwięku. |
| Błędny klucz, obserwacja 25 s | Pozycja zatrzymała się na 9,939 s; brak postępu audio; kryterium odtwarzania ponad 12 s nie zostało spełnione. |
| Ponownie właściwy klucz | Ponownie zdekodowany PTS 12,231 s i 58 obserwacji postępu wideo oraz dźwięku. |
| Zatrzymanie i zwykły film | Klucz wyczyszczony; zwykły film odtworzony z postępem obrazu i dźwięku. |

Obserwacje postępu nie są liczbą wszystkich klatek. Test dekoduje do wyjścia
testowego, bez wyświetlania okna; nie zastępuje kontroli wyglądu aplikacji.
Raport końcowego wydania musi dotyczyć dokładnie dołączonego EXE i DLL; kontrolę
powtarza się po przygotowaniu paczki.

Wielokodekowy przykład Shaka Angel One nie rozpoczął odtwarzania w limicie
20 sekund w pierwszym teście Windows, mimo poprawnej odpowiedzi jego publicznego
serwera licencji. Zachowano raport niepowodzenia. Obsługa jednego przykładu nie
jest obietnicą zgodności każdego źródła ani każdego urządzenia.

## Dane i połączenia

Adresy, dane konta, lista prywatnych kanałów i prywatne EPG są zapisywane przez
Windows DPAPI w pliku `private-sources.dpapi` w folderze profilu. Odczyt wymaga
właściwego konta Windows i jego kluczy DPAPI. Samo skopiowanie folderu `data`
na inny komputer lub do nowego konta o tej samej nazwie nie zapewnia odczytu.
Kopia `.bak` również jest szyfrowana. Jeśli zapis jest uszkodzony, aplikacja
próbuje odczytać kopię i nie zastępuje jej uszkodzonym plikiem.

Ulubione i historia w ogólnej bibliotece zachowują odwołania do prywatnych źródeł;
adres i chronione metadane nie są zapisywane tam jawnie. Eksport publicznego M3U
pomija prywatne źródła. Publiczny crawler nie otrzymuje danych tych kont.

Pobieranie list, API operatora i licencji sprawdza publiczne adresy sieciowe,
kontroluje przekierowania i nie przekazuje danych dostępu na inny serwer.
Obsługiwane są publiczne porty operatorów, nie wyłącznie 80 i 443. Adresy LAN
nie należą do obsługi tego klienta sieciowego. HTTP jest dozwolone dla zgodności
z operatorem, lecz nie szyfruje połączenia; HTTPS zachowuje walidację certyfikatu.

Natywny mpv sam pobiera dalsze manifesty i segmenty. Filtry klienta list/API nie
obejmują wszystkich tych zapytań; Windows nie zapewnia pełnej izolacji sieciowej
odtwarzacza. Dlatego ta wersja nie przekazuje do niego globalnych nagłówków
Authorization ani Cookie z playlist. Odrzuca też rozpoznane playlistowe ustawienia
takich nagłówków oraz DRM przeznaczone dla Kodi. Referer zawierający parametry
lub rozpoznaną ścieżkę konta jest odrzucany; zwykły publiczny Referer może być użyty.
Źródło wymagające innych prywatnych nagłówków może pozostać nieobsługiwane.

## Streamtest

[Streamtest.co](https://streamtest.co/) i [Streamtest.in — logi](https://streamtest.in/logs)
są zewnętrznymi narzędziami do ręcznej diagnostyki. Otwarcie strony nie przekazuje
adresu kanału ani danych konta. Nie są automatycznym dostawcą prywatnych playlist
i nie pobieramy danych dostępu z publikowanych logów. Wyniki testów w tych
serwisach mogą być publiczne; własne konto sprawdzaj bezpośrednio w aplikacji.

## Sprawdzenie wydania 0.9.1 — 9 listopada 2026
Pierwszy test AXINOM nie wrócił przez 330 s i został przerwany; nie uznano go za
potwierdzenie odtwarzania. Dodano wyłącznie checkpointy i 170-sekundowy watchdog
osobnego procesu testowego. Nie zmieniono silnika i nie ustalono przyczyny tamtego
zawieszenia. Raport nieudanej próby pozostaje w verification/earlier-attempts.
Ponowienie na dokładnym EXE/DLL wydania przeszło: dobry klucz dał 12,314 s
zdekodowanego video PTS i 42 postępy AV; zły klucz przez 25 s nie dał postępu audio
ani 12 s pozycji; po przywróceniu poprawnego klucza osiągnięto 12,189 s video PTS
oraz 35 postępów AV. Reset klucza, zwykły klip i zamknięcie silnika przeszły.
To nadal ograniczony test publicznego pojedynczego klucza CENC/H264, nie certyfikat
zgodności wszystkich DRM i nie test kont komercyjnych. Diagnostyczny watchdog
nie zabezpiecza interaktywnego odtwarzacza przed każdym możliwym błędem natywnym.

Publiczne subskrypcje znalezione przez AI pozostają oznaczone po ponownym otwarciu
profilu i są oczyszczane z rozpoznanych danych konta podczas każdego odświeżenia.
Ręcznie dodana własna lista zachowuje swoją odrębną obsługę. W publicznym eksporcie
zmieniona lista jest nazwana oczyszczoną kopią, z liczbą usuniętych wpisów i hashami;
publiczne podpisane adresy CDN nie są automatycznie uznawane za konto.
