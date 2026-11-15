# Modele SwirStream AI — Android 0.3.6

W 0.3.6 dodano rozpoznawanie interfejsu MAC według odpowiedzi protokołu. To reguły zgodności, nie nowy model ani uczenie kont. Wagi i bramka potwierdzania odtwarzania pozostają bez zmian. Sam katalog nie tworzy pozytywnego sygnału uczenia.

W 0.3.5 zmieniono kryterium potwierdzenia odtwarzania przekazywane do lokalnego uczenia. Wagi startowe modeli pozostają bez zmian; postęp dotyczy faktycznie wybranych ścieżek, a DRM dodatkowo wymaga 12 sekund wspólnego postępu po załadowaniu kluczy.

W 0.3.4 poprawiono walidację protokołu MAC Player. Modele i uczenie preferencji pozostają bez zmian.

W 0.3.3 Eksplorator źródeł analizuje publiczne strony według reguł i zachowuje pochodzenie wyników; nie jest nowo trenowanym modelem. Lokalne modele i uczenie preferencji kanałów pozostają bez zmian.

W 0.3.2 rozpoznawanie linków dostawcy jest lokalnym parserem, nie treningiem AI.
Adresy i dane kont nie trafiają do publicznych zapytań ani historii discovery.

Interfejs nie zawiera asystenta, modelu językowego ani rozpoznawania mowy.
Wnioskowanie i uczenie działają w aplikacji bez klucza API i zdalnego treningu.
Wersja 0.3.1 rozszerza uczenie na kanały z „Moich źródeł”. Używa bezpiecznej
referencji kanału i identyfikatora dostawcy w danym profilu. Nie zapisuje w modelu
prawdziwego URL, hasła ani nagłówków konta. Odrzuca referencję z innego profilu,
nieprawidłową referencję oraz tymczasowy adres przygotowany dla odtwarzacza.
Zachowuje poprawki wyszukiwania z 0.2.1; wagi stałego klasyfikatora TasteNet są bez zmian.

## Stały klasyfikator TasteNet

TasteNet.java jest portem wnioskowania Windows IntentNet.cs dla tych samych wag
SwirIntentNet-1. Model assets/model/tastenet.json ma SHA-256:

C391F83F06CF8D75BE36C17EE3A41B79F8ECA5563A7BE4525C57D04A5CA6DF65

Architektura: 1024 haszowane cechy słów, bigramów i trójek znaków; 64 jednostki tanh;
10 wyjść softmax. Tematy: news, music, sports, movies, kids, documentary,
entertainment, lifestyle, religious, general. Jawna kategoria dostawcy ma
pierwszeństwo. Gdy wynik nie spełnia progów, zwracane jest unknown.

Akceptacja wymaga wyniku ≥0,62, przewagi nad drugim wyjściem ≥0,25 i słowa ze
słownika. Wyniki softmax nie są skalibrowanym prawdopodobieństwem poprawności.
Trening Windows używał 316 autorskich krótkich fraz w pięciu językach, 100 epok,
ziarna 4019; dołączono dane treningowe i 40 osobnych fraz ewaluacyjnych.
To mały zestaw syntetyczny, nie reprezentatywny benchmark światowej telewizji.
Wagi klasyfikatora nie zmieniają się podczas używania Androida.

Testy Android zawierają 48 punktów odniesienia obliczonych przez niezmieniony kod
Windows: 40 fraz ewaluacyjnych i osiem przypadków brzegowych. Porównują hash modelu,
normalizację, wszystkie cechy, etykietę, confidence, margin i decyzję akceptacji.
Uwzględniono polskie znaki i różnicę invariant casing dla tureckiego İ.
Obecność testów nie stanowi deklaracji ich zaliczenia; wyniki podaje raport wydania.

## Model preferencji profilu

Osobna regresja logistyczna ma 512 znormalizowanych cech haszowanych: temat, kraj,
rodzaj transmisji, tożsamość kanału i tokeny nazwy. Wagi zaczynają od zera.
Dodatni sygnał pochodzi z ulubionych lub 30 sekund rzeczywistego postępu odtwarzania;
ujemny z jawnej oceny „Nie interesuje mnie”. To ocena preferencji, nie ukrycie kanału.

Aktualizacja: krok 0,8, regularizacja 0,9995, ograniczenie wag do ±8. Powtórzenie
tego samego wyniku preferencji dla kanału jest tłumione przez 24 godziny; przeciwna
ocena może skorygować wynik od razu. Android API łączy dodatnie sygnały oglądania
i ulubionych dla jednego kanału, aby powtarzanie tych czynności nie mnożyło nauki.

## Model skuteczności źródeł

Druga lokalnie uczona regresja ma 512 cech domeny nadawcy/pochodzenia źródła,
kanału, kraju, tematu i rodzaju. Przyjmuje wyłącznie wywołania z faktycznych
wyników odtwarzacza: obraz albo postępujące audio, lub rzeczywisty błąd.
Pobranie strony/M3U, HTTP 200, EPG lub gotowość bufora nie są sukcesem.
Testowe, lokalne/prywatne i znane przeglądarkowe adresy nie są danymi uczącymi.

Aktualizacja: krok 0,55, regularizacja 0,9995, wagi ±6. Wpływ starych danych maleje
wykładniczo ze stałą czasu 30 dni. Ten sam wynik źródła nie uczy ponownie przez
30 minut; zmiana sukces/porażka może skorygować model wcześniej. Stan przechowuje
maks. 4000 odcisków dla każdego rodzaju sygnału, z retencją 30 dni.

Suma bonusów preferencji i źródeł mieści się w ±24 punktach. Wkład źródeł rośnie
stopniowo w pierwszych sześciu przykładach. Porządkowanie zachowuje wszystkie
przekazane pozycje; dopiero jawne filtry widoku/krajów ograniczają wyniki.
Wyszukiwanie stron używa oceny przy ustalaniu kolejności, z częścią budżetu na
mniej odwiedzane miejsca. Konkretna nazwa kanału i kraj mają pierwszeństwo.
To mechanizm adaptacji, bez gwarancji poprawy każdego wyszukiwania i bez zmierzonego
tutaj procentu skuteczności. Wyuczony score nie oznacza świeżego potwierdzenia odbioru.

## Deep Search i granice roli AI

Deep Search łączy przeszukanie globalnych playlist i strukturalnego indeksu transmisji,
warianty zapytań do wyszukiwarek oraz analizę publicznych stron nadawców. Rozwijanie
zapytania jest deterministyczne: używa nazwy, aliasów, identyfikatorów i wariantów
programu, metadanych poprzednika/następcy oraz określeń transmisji w języku kraju.
Nie jest generowaniem zapytań przez LLM. Adresy mediów pochodzą z odczytanych źródeł;
model nie wymyśla nowych adresów i nie wykonuje logowania do płatnych usług.

Uczenie profilu wpływa na kolejność kandydatów i odwiedzania stron, z zachowaniem
części prób dla mniej poznanych miejsc. Osobna kontrola dostępności nadaje pierwszeństwo
źródłom ze świeżo odczytanym manifestem live; ten sygnał wygasa po 30 minutach.
Jest to reguła porządkowania, nie dodatkowy przykład treningowy ani dowód dekodowania
obrazu. Brak odpowiedzi lub nieobsługiwany format kontroli nie usuwa kandydata.

Zapytania są wysyłane do Bing RSS i pomocniczo DuckDuckGo HTML. Katalogi pochodzą
z iptv-org i Free-TV; dwa adresy serwerów tego samego katalogu nie stanowią dwóch
niezależnych dostawców danych. Usługi mogą odmówić dostępu lub zmienić format.
Wyniki i ograniczenia faktycznych testów danego APK opisuje raport wydania;
nie deklarujemy skuteczności przeszukania całej sieci ani wszystkich kanałów.

## Przechowywanie, kontrola i odporność

Stan personal-ai.json jest prywatny i oddzielny dla każdego identyfikatora profilu.
Zawiera wagi, znaczniki czasu, liczniki i hasze sygnałów, bez jawnych URL/nazw kanałów.
Haszowanie nie jest obietnicą anonimizacji. Biblioteka i historia odwiedzin
przechowują potrzebne nazwy/adresy lokalnie. Profile nie są zabezpieczone hasłem.

Wyłączenie uczenia zatrzymuje aktualizacje i wkład wyuczonych ocen, zachowując
dane do ponownego włączenia. Reset zeruje oba uczone modele i historię odwiedzania
stron podczas wyszukiwania aktywnego profilu;
nie zmienia stałych wag TasteNet, innych profili, ulubionych ani historii oglądania.
Usunięcie danych aplikacji usuwa lokalny zapis. Nie ma zdalnego treningu ani telemetrycznego
wysyłania modeli. Zapytania sieciowe i odbiór transmisji nadal kontaktują się z dostawcami.

Pliki zapisuje AtomicFile. Model jest walidowany pod względem schematu, wymiarów,
liczb skończonych, granic wag, czasu i wielkości. Uszkodzony zapis jest zachowywany
z rozszerzeniem .damaged, a oceny zaczynają od stanu neutralnego. Awaria zapisu jest
sygnalizowana; działanie w pamięci nie gwarantuje zachowania nauki po zamknięciu.
Testy obejmują m.in. izolację profili, deduplikację, on/off/reset, odtwarzanie stanu,
odporność na uszkodzenie oraz odzyskanie starej kopii AtomicFile po przerwanym zapisie.
