# SwirStream AI 0.9.8 — by Swir

Nowy **MAC Player → Skaner portali TXT** pozwala wczytać własną listę, obejrzeć
podgląd i uruchomić sprawdzanie. Wybierz **Importuj TXT → Sprawdź listę**, a potem
**Dodaj wybrany** lub **Odtwórz próbkę**. Podczas skanowania konta nie są zapisywane.
Dodanie wybranej pozycji ponownie pobiera katalog i zapisuje ją w chronionym
magazynie bieżącego profilu. Próbka otwiera się w głównym Swir Playerze.

TXT obsługuje `URL|MAC`, adres i własne MAC w kolejnych wierszach oraz sam URL.
Limit wynosi 1 MiB i 200 unikalnych wpisów; kodowanie UTF-8 albo UTF-16 z BOM.
Podgląd pokazuje liczbę błędnych wierszy i duplikatów. Skan wykonuje jedną próbę
naraz, do 30 sekund na pozycję i 10 minut łącznie. Można go przerwać bez utraty
już uzyskanych wyników. Raport CSV nie zawiera adresów ani danych konta.

Sam URL sprawdzany jest tylko jako publiczna strona, bez logowania. Rozpoznanie
portalu nie potwierdza bezpłatnego dostępu. **Szukaj publicznych portali** otwiera
Eksplorator z gotową frazą i filtrem portali; wynik zachowuje pochodzenie.
Wyszukiwanie nie pobiera MAC, loginów ani haseł z internetu. Katalog kanałów
i rzeczywiste odtwarzanie pozostają odrębnymi wynikami.

Wersja 0.9.7 dodała wariant portalu MAC: katalog udostępniany bez sesji
tokenowej przez `portal.php`. Program rozpoznaje typ po odpowiedzi serwera,
zachowując dotychczasową obsługę pełnej sesji handshake i aktywnego profilu.
Odmowa aktywnego profilu nadal zatrzymuje połączenie.

Po podaniu własnego adresu i MAC sprawdzane są najwyżej trzy znane ścieżki API
na tym samym serwerze. Jawny adres `.php` ma pierwszeństwo; podfolder i port
pozostają zachowane. Program nie zmienia zapisanego adresu i nie szuka innych kont.
Przed odtwarzaniem ponownie rozpoznaje połączenie. Jeśli operator jawnie oznaczył
adres jako stały (obie flagi linku tymczasowego i równoważenia obciążenia są
wyłączone), program używa bezpiecznego adresu z katalogu. Pozostałe kanały wymagają
świeżego linku. Starsze zapisane listy zachowują tę drugą ścieżkę do odświeżenia.
Poprawiono także nagłówek zgodności klienta: właściwy User-Agent zastępuje domyślny.

Duże katalogi portalu są odczytywane strumieniowo, z limitem 64 MB odpowiedzi.
Program zachowuje tylko potrzebne pola, do 30 000 unikalnych kanałów i 8 MB danych.
Ograniczona lista otrzymuje wyraźny komunikat; pozostała odpowiedź nadal jest
sprawdzana do końca. Uszkodzenie lub odmowa nie zastępuje poprzedniej dobrej listy.

Lista kanałów, uzyskanie linku oraz odtworzony obraz i dźwięk mają odrębne znaczenie.
Publiczny katalog jest oddzielony od źródeł użytkownika. Nieudane odświeżenie
zachowuje poprzednie kanały i ich datę; błąd odtwarzania nie udaje błędu całej listy.
Wyniki dla konkretnej kompilacji i hashe plików opisuje `RAPORT_WYDANIA.txt`
oraz folder `verification`. Testy offline nie są dowodem dostępności operatora.

Obsługiwane są playlisty URL/pliki M3U, własne konta Xtream live, portale MAC live
i ograniczone DASH ClearKey CENC/H264 z jednym kluczem. Nie ma ogólnej obsługi
Widevine/PlayReady, EPG portalu ani portali wymagających SN, certyfikatu czy parowania.
Odtwarzacz Windows nie wysyła MAC ani Bearer jako globalnych nagłówków mediów;
transmisje wymagające takiego uwierzytelniania mogą pozostać niezgodne.
Historyczne zawieszenia niektórych testów interfejsu/natywnego silnika opisane
w `MAINTENANCE.md` nie są uznawane za naprawione przez tę zmianę protokołu.

Silnik, lokalne modele AI i publiczne wyszukiwanie pozostają w dotychczasowym
zakresie. AI pomaga sortować publiczne źródła i dopasowywać wybór; nie zastępuje
autoryzacji operatora ani nie gwarantuje wszystkich kanałów.

Rozpakuj cały ZIP do nowego folderu i uruchom `SwirStreamAI.exe`. VLC i osobny
.NET nie są potrzebne. Paczka nie zawiera Twoich kont ani danych.
Przy zamkniętej starej i nowej aplikacji wykonaj kopię własnego folderu `data`,
a następnie skopiuj go obok nowego EXE. Prywatne źródła wymagają tego samego
konta Windows i kluczy DPAPI. Instrukcja: [ZACZNIJ_TUTAJ.txt](ZACZNIJ_TUTAJ.txt).
