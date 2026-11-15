Skaner portali TXT w 0.3.7
MAC Player → Skaner portali • import TXT → Importuj TXT. Najpierw zobaczysz liczbę pozycji, błędnych wierszy i duplikatów; połączenia zaczynają się dopiero po „Sprawdź listę”.
Obsługiwane: sam URL, URL|MAC, albo adres i własne MAC w kolejnych wierszach. UTF-8 lub UTF-16 z BOM; do 1 MiB i 200 unikalnych pozycji. Przekroczenie limitu odrzuca cały import. Opcjonalny komentarz po MAC zaczyna się od „ - ” lub „ #”. Adresy z loginem, hasłem lub innymi parametrami zapytania dodaj normalnym formularzem, nie TXT.
Skan jest kolejny, do 30 s na pozycję i 10 min na listę. Przerwanie zachowuje ukończone wyniki. Niesprawdzone pozycje nie są oznaczane jako działające. Ponowny skan zaczyna od pierwszego wiersza; długie listy z timeoutami podziel lub zmień kolejność.
Sam URL sprawdza tylko publiczną stronę bez logowania (porty 80/443); prywatny adres API lub inny port wymaga własnego MAC. Rozpoznany portal i pobrany katalog nie potwierdzają obrazu. „Dodaj wybrane źródło” ponownie pobiera tylko wybrany katalog i zapisuje go w chronionym magazynie. „Dodaj i otwórz próbkę w playerze” uruchamia pierwszy kanał po pominięciu prostych separatorów; wynik potwierdza dopiero odtwarzacz. Pozostałe konta nie są zapisywane.
Raport CSV zawiera tylko numery wierszy, statusy i liczby kanałów. Zamknięcie skanera, zmiana profilu lub odtworzenie aplikacji usuwa tymczasową listę. Plik TXT wybrany przez użytkownika pozostaje w jego lokalizacji.
„Szukaj publicznych portali” otwiera eksplorator z przygotowanym tematem i filtrem portali. Nie wyszukuje cudzych MAC/loginów. Adres opublikowany w WWW sam w sobie nie oznacza darmowego dostępu.
# MAC Player w 0.3.6
Program rozpoznaje dwa rodzaje interfejsu: katalog MAC dostępny bez pełnej sesji oraz portal wymagający handshake i aktywnego profilu. Rozpoznaje maksymalnie trzy adresy API na tym samym serwerze, zachowując wpisany adres źródła. Każde odtwarzanie ponawia rozpoznanie sesji. Gdy operator jawnie wyłącza link tymczasowy i równoważenie serwerów, używany jest oryginalny publiczny adres kanału; w pozostałych przypadkach pobierany jest nowy create_link. Starszy zapis bez tych flag wymaga odświeżenia listy, aby skorzystać z tej obsługi.

Sam katalog nie potwierdza aktywności konta ani odtwarzania. Jawna odmowa dostępu zatrzymuje połączenie; sprawdzanie pełnego profilu pozostaje ścisłe. Nie generujemy numeru seryjnego ani identyfikatorów urządzenia. Nagłówki MAC/token nie są przekazywane innym domenom. Duża lista portalu jest czytana strumieniowo: do 64 000 000 bajtów odpowiedzi, 8 000 000 bajtów wybranych pól i 30 000 unikalnych kanałów. Po ograniczeniu listy program pokazuje komunikat; pozostałe pozycje nie są zapisane. Cała odpowiedź jest sprawdzana do końca, a błąd zachowuje poprzedni katalog. Pozostałe dokumenty prywatnego źródła nadal mają limit 12 MiB.

Android nie był testowany na rzeczywistym koncie operatora ani fizycznym F150/HY300. Test porównawczy Windows nie jest dowodem działania APK. Zakres testów tej konkretnej APK jest zapisany w dołączonym raporcie; wcześniejsze wyniki poniżej są historyczne.

# Potwierdzanie odtwarzania w 0.3.5

Krótki materiał, który naturalnie dobiegł końca przed potwierdzeniem, pozostaje zakończony; nie zamienia się po czasie w błąd ani ujemny sygnał nauki.

Odtwarzacz sprawdza postęp faktycznie wybranych ścieżek obrazu i dźwięku.
Historia i dodatnie uczenie źródła pojawiają się dopiero po potwierdzeniu.
Wyciszenie, statyczny lub czarny obraz, radio i materiały bez ścieżki audio
pozostają obsługiwane; pauza i zatrzymana ścieżka nie zwiększają czasu nauki.
Dla DRM bramka potwierdzenia wymaga kluczy i co najmniej 12 sekund wspólnego
postępu. Obraz może już być widoczny podczas sprawdzania. To ostrożna polityka,
nie gwarancja wykrycia każdego błędnego klucza ani dowolnej długości clear lead.
Test publicznej próbki ClearKey obejmuje również poprawny JSON licencji ze
złym kluczem, brak pozytywnej historii/uczenia oraz powrót do poprawnej licencji.

# Poprawki MAC Player w 0.3.4
Adresy portalu zachowują zakodowany prefiks, także spacje i znaki % w ścieżce.
W trybie pełnej sesji odpowiedź dostawcy musi być poprawnym JSON-em i jednoznacznie potwierdzać aktywny profil.
Odrzucone lub nieznane statusy nie wczytują kanałów; nieudane odświeżenie zachowuje poprzedni zapis.
Końcowy adres odtwarzania nadal nie może wskazywać sieci lokalnej.
Obsługę MAC sprawdzono na kontrolowanych odpowiedziach protokołu; w tym wydaniu
nie wykonano logowania Androidem do rzeczywistego konta operatora.

# Eksplorator źródeł w 0.3.3

Na głównym pasku obok „Wklej link” wybierz „Eksplorator WWW”.
Wpisz kanał/nadawcę/temat i kraje albo przeanalizuj publiczny adres.
Wyniki mają cztery sekcje: playlisty, transmisje, portale i strony/informacje.
Każdy wynik pokazuje pochodzenie, datę sprawdzenia i datę serwera, jeśli ją podał.
„Dodaj portal” otwiera MAC Player z samym adresem; wpisujesz własny MAC.
Eksplorator nie wyszukuje kont i nie loguje się do znalezionych portali.
Playlista trafia do publicznego importu, a HLS/DASH/MP4 do Playlist URL.
Odtwarzanie odbywa się w Swir Player. Manifest DRM wymaga własnej konfiguracji.
Samo znalezienie adresu lub HTTP 200 nie potwierdza kanałów ani obrazu.
Wyniki zapisują się oddzielnie dla profili; błędy i anulowanie zachowują poprzedni zapis.
Limit: 4 zapytania z alternatywną wyszukiwarką, 12 stron, głębokość 2, 100 wyników.
Publiczna analiza WWW obejmuje HTTP/HTTPS na portach 80/443. Własny adres
dostawcy na innym publicznym porcie dodaj przez „Wklej link” lub właściwą sekcję.
Kraje są częścią zapytania, nie gwarantowanym krajem każdego znalezionego źródła.
Lokalne AI nadal porządkuje zaimportowane kanały. Eksplorator jest analizatorem WWW,
nie nowym modelem AI i nie skanuje całego internetu.

# Import linków w 0.3.2

Na głównym pasku wybierz „Wklej link”. Wklej cały get.php / player_api.php
z loginem i hasłem, rozpoznaj dane i wybierz „Pobierz i zapisz”.
Nie trzeba samodzielnie dzielić adresu na serwer, login i hasło.
Pełny link można też wkleić w pole serwera w sekcji Xtream.
Podgląd jest chroniony; rozpoznanie nie łączy się z serwerem i niczego nie zapisuje.
Nieznane parametry dostawcy nie są usuwane — można świadomie użyć całego
linku jako własnej playlisty URL. To oddzielna opcja od publicznych list AI.
Link wklejony do wyszukiwarki przechodzi do prywatnego importu, nie do wyszukiwarek WWW.
Po udanym imporcie od razu widzisz kanały tego konta i możesz przeszukać je lokalnie.
Ponowne dodanie tego samego serwera i loginu zachowuje konto oraz ulubione;
nowe hasło zapisuje się dopiero po udanym pobraniu. Sama zmiana nazwy działa offline.
Odświeżenie M3U zachowuje ulubione przy zmianie kolejności/tokenów tam, gdzie
kanał można jednoznacznie rozpoznać. Niejednoznaczny stary wpis jest niedostępny,
zamiast uruchamiać inny kanał.

# Nowe sekcje w 0.3.1

Cztery stale widoczne przyciski otwierają oddzielne listy i formularze:
Playlisty URL, Xtream, MAC Player i DRM. Nie trzeba przewijać paska narzędzi.
Każde źródło ma „Pokaż kanały”, odświeżanie i edycję danych. Filtr konta
można wyłączyć przyciskiem × nad listą. Zmiana profilu usuwa poprzedni filtr.

W sekcji Playlisty URL funkcja „AI: znajdź playlisty” uruchamia rzeczywiste
ograniczone wyszukiwanie publicznych katalogów i stron nadawców według
zapytania i krajów. Wyniki pokazują źródło, pierwotną datę pobrania i liczbę
unikalnych pasujących kanałów. Lokalne preferencje porządkują wyniki;
brak historii nie usuwa list. Znaleziona lista trafia do formularza URL i EPG,
a po potwierdzeniu jest pobierana do chronionego magazynu profilu.
Nie wyszukujemy cudzych loginów, haseł, zarejestrowanych MAC ani licencji.

Edycja nie usuwa źródła; zapis danych i kanałów następuje po udanym pobraniu.
Powtórne dodanie tego samego adresu M3U odświeża dotychczasowe źródło.
Ulubione zachowują identyfikator źródła; zmiany identyfikatorów kanałów u
dostawcy mogą wymagać ponownego wybrania kanału.

# SwirStream AI na Androida — by Swir

Wersja Android 0.3.7 to osobna aplikacja dla telefonu i projektora: publiczne TV/radio,
odtwarzanie w programie, playlisty M3U, EPG, ulubione i lokalna nauka preferencji.
Nie ma panelu asystenta ani potrzeby kupowania abonamentu AI.
Własne źródła URL, Xtream, MAC oraz konfiguracja DRM są dostępne od 0.3.0. Wydanie 0.3.1 dodaje dla nich osobne widoczne sekcje, edycję i wyszukiwanie publicznych playlist. Zachowuje poprawione wyszukiwanie lokalne i zakładkę „Wszystkie”.
Tryb „Deep Search” nadal szuka dodatkowych źródeł w internecie. APK nie aktualizuje wersji Windows.

## Instalacja i urządzenia

1. Pobierz plik APK i otwórz go w menedżerze plików urządzenia. Jeśli Android tego
   wymaga, zezwól temu menedżerowi na instalację aplikacji z tego źródła.
2. Na projektorze można przenieść APK np. przez USB i otworzyć go w jego menedżerze
   plików. Nazwy ekranów instalacji zależą od oprogramowania projektora.
3. Uruchom „SwirStream AI”. Do katalogów, EPG i transmisji potrzebny jest internet.
   Sama aplikacja nie wymaga usług Google Play ani konta Google.

Jeśli masz SwirStream AI 0.1.0, 0.2.0, 0.2.1, 0.3.0, 0.3.1, 0.3.2, 0.3.3 lub 0.3.4 na Androidzie, otwórz nową APK jako aktualizację. Wydanie podpisane tym
samym kluczem i z wyższym numerem wersji może zachować profile, historię i ulubione.
Nie odinstalowuj poprzedniej aplikacji przed aktualizacją — usuwa to jej lokalne dane.

APK wymaga Androida 6.0 / API 23 lub nowszego. F150 z podanym przez użytkownika
Androidem 15 spełnia wymaganie wersji systemu. Dla HY300 sprawdź rzeczywistą wersję
w ustawieniach urządzenia: sama informacja „najnowsza aktualizacja” jej nie określa.
Obsługa kodeków, płynność i zgodność pilota zależą od sprzętu oraz oprogramowania.
Nie deklarujemy tutaj testów na fizycznym F150 ani HY300; wynik emulatora nie jest
takim testem. Wyniki konkretnej paczki APK należy sprawdzić w dołączonym raporcie wydania.

Na Androidzie 6 i 7 aplikacja dołącza oficjalny certyfikat główny ISRG Root X1,
aby obsługiwać współczesne serwery HTTPS korzystające z tego zaufanego wystawcy.
Weryfikacja certyfikatów i zgodności nazwy serwera pozostaje aktywna. Aktualizacja
tego certyfikatu w aplikacji wymaga nowego APK; przycisk „Odśwież” go nie aktualizuje.

## Pierwszy start i szukanie

Na początku pojawia się ekran „Proszę czekać… Trwa pierwsze wyszukiwanie kanałów”.
Główna lista pojawia się po zebraniu wyników. Jeśli istnieje odpowiednia, aktualna
kopia katalogu, aplikacja może pokazać ją od razu wraz z informacją o wieku.
„Przerwij wyszukiwanie” przerywa pracę; anulowanie lub błąd nie usuwa zapisanej bazy.
Pobranie katalogu nie potwierdza odbioru: obraz lub dźwięk sprawdzamy przy odtwarzaniu.

Wybierz kraje — maksymalnie 12 — albo dostępne wyszukiwanie globalne. Po zmianie
wyboru użyj „Odśwież” lub „Deep Search”. Pole nazwy filtruje gotową listę;
„Deep Search” szuka dodatkowych źródeł dla wpisanej nazwy lub tematu. Zakładki oddzielają TV, radio,
ulubione i historię; „Wszystkie” obejmuje całą pobraną bibliotekę niezależnie od kraju i typu.
Przy jednym kanale może być kilka adresów do wyboru.

## Głębokie wyszukiwanie — Deep Search

Wpisz nazwę kanału, wybierz kraje i naciśnij „Deep Search”. Program pokazuje etapy pracy:

1. Przeszukuje globalną playlistę iptv-org, niezależną playlistę Free-TV oraz indeks
   transmisji `streams.json`. Indeks może zawierać adresy pominięte w playlistach.
   Rozpoznaje również nazwy w identyfikatorach i wariantach, np. `DWEnglish.de`
   lub `DW.de@English`, oraz poprzednie nazwy powiązane w katalogu z następcą kanału.
2. Przygotowuje do sześciu wariantów zapytania, uwzględniając nazwę, aliasy i język
   kraju. Korzysta z Bing RSS oraz pomocniczo DuckDuckGo HTML. Wyniki rozpoznanych
   nadawców prowadzą do analizy stron i odnośników do odtwarzaczy. Znalezione playlisty
   są sprawdzane przed dalszym przeglądaniem stron. Strony transmisji mają pierwszeństwo
   przed ogólnymi stronami nadawców. Blokada wyszukiwarki jest raportowana; aplikacja
   kontynuuje dostępne źródła i nie próbuje obchodzić zabezpieczeń wyszukiwarki.
3. Sprawdza do 12 dodatkowych manifestów HLS/DASH. Wynik znajdziesz w „Źródła”:
   „Manifest live dostępny” oznacza odczytaną listę transmisji, a nie potwierdzony obraz.
   Dopiero odtwarzacz sprawdza rzeczywisty odbiór. Dodatni status manifestu wygasa po
   30 minutach. Błąd kontroli nie usuwa adresu; można nadal spróbować go odtworzyć.

Wybrane kraje nadal ograniczają wyniki. Warianty językowe i regionalne mogą pozostać
osobnymi pozycjami. Nowe wyniki są dodawane do katalogu po zakończeniu pracy;
anulowanie nie zastępuje poprzedniej listy częściowo zebranymi danymi.

Etap WWW obejmuje do 60 stron, najwyżej trzy poziomy odnośników i orientacyjny budżet
90 sekund, z czego do 25 sekund przypada na uruchamianie zapytań wyszukiwarek.
Kontrola manifestów ma osobny budżet 30 sekund. Odczyt katalogów odbywa się wcześniej.
To budżety rozpoczynania kolejnych działań, nie gwarancja łącznego czasu: trwające
połączenie lub rozwiązywanie adresu serwera mogą wydłużyć oczekiwanie i anulowanie.

Deep Search może użyć kopii źródła z ostatnich sześciu godzin. Gdy dostawca nie działa,
używa poprawnego zapisu do siedmiu dni, zachowując jego pierwotną datę i oznaczając
tryb awaryjny w „Stan źródeł”. Zwykłe „Odśwież” ponownie pobiera katalogi; nie wykonuje
całego Deep Search ani nie instaluje nowej wersji APK.

## Odtwarzanie

Dotknij kanału, aby odtworzyć go wewnątrz aplikacji. „Źródła” otwiera jego warianty.
Po błędzie aplikacja próbuje kolejnych znanych adresów tego kanału. Tryby jakości
to „Stabilność”, „Najwyższa jakość” i „Oszczędzaj transfer”; wybierają dostępne
warianty nadawcy, nie poprawiają sztucznie obrazu. Gwiazdka dodaje kanał do ulubionych.
Przejście aplikacji do tła zatrzymuje odtwarzanie; to wydanie nie jest odtwarzaczem
radia działającym stale w tle.

Na projektorze: strzałki pilota przesuwają zaznaczenie, OK wybiera, Wstecz opuszcza
pełny ekran. Na telefonie działa obsługa dotykiem. Jeśli pilot lub menedżer plików
projektora nie udostępnia wszystkich działań, można użyć podłączonej myszy/klawiatury.

## Wyszukiwanie pobranej biblioteki — poprawka zachowana z 0.2.1

Pole nazwy oraz przycisk Szukaj na klawiaturze filtrują zapisane kanały lokalnie.
Nie uruchamiają Deep Search. Działa wpisywanie części słów, np. „pol spo”,
oraz zapisy z odstępami, np. „TVP 1” i „TVN 24”. Numery 1 i 10 pozostają różne.

Zakładki TV i Radio uwzględniają wybrane kraje. „Wszystkie” przeszukuje całą
pobraną bibliotekę, także inne kraje i playlisty bez oznaczenia kraju, bez zmiany
ustawień szukania w internecie. Gdy kraj lub zakładka ukrywa trafienie, program
pokazuje komunikat i przycisk „Pokaż wszystkie pobrane”.

Odświeżenie jednego kraju zachowuje pozostałe niewygasłe rekordy biblioteki.
Nie przedłuża daty ich ważności. Wynik zadania działającego w tle stosuje aktualnie
wpisany filtr; późniejsze wpisywanie nie zmienia zapytania już zleconego Deep Search.

## Playlisty i EPG

Menu ☰ → „Playlisty M3U” pozwala zaimportować plik, wyeksportować widoczne kanały
albo pobrać znalezioną playlistę. Android pokazuje systemowy wybór pliku/folderu.
„Pobierz oryginał” jest dostępne dla niezmienionej publicznej listy. Jeśli pominięto
wpisy z danymi kont, otrzymujesz oznaczoną „oczyszczoną kopię”; szczegóły pokazują
liczbę pominiętych wpisów. Metadane zachowują osobne sumy kontrolne oryginalnego
pobrania i oczyszczonej kopii. Eksport widocznych kanałów
uwzględnia bieżący widok i filtry; może zawierać adresy jeszcze nieodtworzone.
Import dodaje adresy do katalogu, lecz sam nie nadaje im statusu działających.

Lista pokazuje „Teraz” i „Następnie”, jeśli dostępna jest aktualna ramówka. Na niskim
ekranie w poziomie kompaktowy wiersz pokazuje bieżącą audycję, aby cały kanał mieścił
się pod wyszukiwarką.
Menu „Źródło EPG” pozwala podać URL XMLTV/XMLTV.GZ dla kraju; puste pole przywraca
domyślne źródła. EPG dopasowuje identyfikator lub jednoznaczną nazwę w tym samym
kraju. Warianty regionalne, językowe i +1 mogą pozostać bez dopasowania.

Ze względu na pamięć urządzeń mobilnych EPG obsługuje jednocześnie pierwsze cztery
kraje bieżącego wyboru, z programem teraz i do 24 godzin naprzód. Przy szukaniu
globalnym kraje są wyznaczane z kanałów. Dla dalszych krajów lista TV nadal działa,
ale ramówka może być niezaładowana. Wybierz mniej krajów, aby objąć je EPG.
Godziny korzystają ze strefy urządzenia. Brak EPG nie oznacza braku transmisji;
obecność ramówki nie daje abonamentu ani prawa dostępu do kanału.

## Profile i lokalne AI

„Profil” pozwala przełączyć lub utworzyć profil na tym urządzeniu. Każdy ma osobne
ulubione, historię oglądania, własne źródła i wyuczone oceny. Publiczny katalog,
wybór krajów, jakość oraz publiczne ustawienia EPG są wspólne. EPG dodane w „Moje
źródła” pozostaje w profilu razem z kontem. Profile nie mają osobnych haseł wejścia.

TasteNet rozpoznaje tematykę kanałów. Modele profilu uczą się z ulubionych, ocen
i 30 sekund rzeczywistego odtwarzania oraz skutecznych/nieudanych prób źródeł.
Wpływa to na kolejność propozycji i odwiedzania stron przy następnym szukaniu.
Nadal są sprawdzane mniej poznane miejsca; wybrane kraje i konkretna nazwa
kanału zachowują pierwszeństwo. Model nie tworzy sobie nowego kodu ani adresów.
Rozwijanie zapytań w Deep Search opiera się na regułach i metadanych; nie korzysta
z modelu językowego. AI pomaga ustalać kolejność. Sam odczyt strony, playlisty lub
manifestu nie jest sygnałem udanego odtwarzania i nie uczy modelu skuteczności źródeł.

Menu ☰ → „Lokalne AI”:

- „Wyłącz uczenie” zatrzymuje nowe uczenie i używanie wyuczonych ocen, zachowując zapis.
- „Włącz uczenie” przywraca działanie.
- „Wyczyść naukę” zeruje doświadczenia aktywnego profilu; ulubione i historia zostają.

Modele i biblioteka są zapisane w prywatnych plikach aplikacji. Nie są wysyłane do
usługi AI. Zapytania Deep Search trafiają do Bing i DuckDuckGo, a kanały i EPG łączą się
z serwerami dostawców. Odinstalowanie aplikacji lub wyczyszczenie jej danych usuwa
lokalne profile. Nie ma w tym wydaniu synchronizacji kont ani pełnego eksportu profili.

## Ograniczenia i aktualizacje

Program korzysta z rzeczywistych publicznych katalogów i ograniczonego czasowo
wyszukiwania stron. Nie przeszukuje całego internetu, nie gwarantuje wszystkich
kanałów HBO/TVN i nie dostarcza płatnych pakietów. Logowanie, DRM, geoblokady,
wygasające adresy lub awarie mogą uniemożliwić odbiór. Popularne kodeki działają
przez dekodery Androida; np. HEVC/AC-3 mogą nie być obsługiwane na danym sprzęcie.

„Stan źródeł” pokazuje daty i błędy. „Odśwież” aktualizuje dane, nie aplikację APK.
Nowe wersje programu instaluje się jako nowy APK podpisany tym samym kluczem,
z wyższą wersją. Zgodna aktualizacja zwykle zachowuje dane; nie odinstalowuj starej
aplikacji tylko po to, aby ją zaktualizować. Nie ma automatycznego aktualizatora APK
ani gwarancji niezmiennego działania usług przez lata.

Pochodzenie modeli: MODEL_CARD_ANDROID.md. Licencje składników i źródła:
THIRD_PARTY_ANDROID.md oraz folder licenses. Klucze podpisujące nie są częścią źródeł.


## Moje źródła — od 0.3.0; osobne sekcje i edycja od 0.3.1

Cztery przyciski na głównym ekranie (lub zbiorcze „Moje źródła” → „Dodaj źródło”):
• M3U z URL: także adres get.php i serwer z niestandardowym portem; opcjonalny XMLTV.
• Xtream: adres serwera, login i hasło do własnego konta. Kanały na żywo, kategorie i EPG.
• Portal MAC: adres portalu oraz MAC zarejestrowany u dostawcy. Podstawowy protokół
  Stalker/Ministra. Portale wymagające serialu lub aplikacji operatora mogą nie działać.
• DRM: własny adres transmisji, Widevine albo ClearKey i serwer licencji operatora;
  opcjonalne nagłówki licencji. ClearKey wymaga zgodnego DASH; nie jest obejściem DRM.

Lista źródła zapisuje się na urządzeniu. Wybierz źródło → Odśwież, aby pobrać nową listę.
Błąd serwera nie usuwa poprzednich kanałów i nie odmładza daty danych. Własne kanały
pozostają widoczne także bez oznaczenia kraju. Pole wyszukiwania filtruje je lokalnie.
Dane dostępu i katalog źródła są szyfrowane kluczem Android Keystore, osobno dla profili.
Historia i ulubione przechowują referencję, a nie hasło lub rzeczywisty adres transmisji.
Eksport publicznych playlist pomija własne konta i chronione adresy. Szyfrowany zapis
jest związany z urządzeniem; nie jest przenośną kopią konta. Po reinstalacji dodaj konto ponownie.

Historyczne testy 0.3.5 oraz wcześniejszego kandydata 0.3.6 obejmowały publiczny materiał Google/Shaka
ClearKey, sprawdzony przez pobranie licencji i rzeczywiste dekodowanie ruchomego
obrazu poza początkowym niezaszyfrowanym fragmentem, na emulatorach API 23 i API 35.
Test błędnej licencji sprawdzał, że nie powstaje fałszywe potwierdzenie odtwarzania.
Nie jest to ponowny test DRM końcowej APK 0.3.6; jej dokładny zakres podaje WERYFIKACJA.json.
Widevine jest konfigurowalne, ale nie potwierdzono odtwarzania Widevine ani płatnego
kanału konkretnego operatora. Xtream i MAC mają testy odpowiedzi protokołu, bez konta
abonamentowego dostawcy.

„Stan źródeł” pokazuje dostępność DRM na urządzeniu. To nie gwarantuje licencji operatora,
HD ani zgodności kodeków. Fizyczny F150 i HY300 wymagają sprawdzenia na sprzęcie.
Menu → Testery Streamtest otwiera streamtest.co lub streamtest.in/logs. Aplikacja nie
przekazuje tym serwisom URL kanału, tokenu ani hasła; zewnętrzne publiczne testy są widoczne
dla innych. Nie pobieramy danych logowania ani adresów MAC z cudzych logów.

## Historyczny zakres weryfikacji 0.3.5 — inny plik APK

Końcowa APK przeszła 82 testy lokalne i po 13 testów odtwarzania, DRM, cyklu życia oraz interfejsu na emulatorach API 23 i API 35. Pełna macierz 382 testów dotyczy poprzedniego kandydata 927f24a6. Jedyna późniejsza zmiana runtime wyłącza naturalny STATE_ENDED z timeoutu; końcowy raport sprawdza dokładnie ten diff i osobno identyfikuje hash końcowej APK. Nie przypisuje poprzedniej pełnej macierzy nowemu plikowi. Fizyczne F150 i HY300 nie były testowane.
