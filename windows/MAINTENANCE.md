# SwirStream AI — utrzymanie 0.9.8

## Portal TXT scanner 0.9.8

The scanner accepts at most 1 MiB before decoding and 200 unique entries. UTF-8
is strict; UTF-16 requires BOM. Parse diagnostics retain at most 50 safe line/code
records with separate totals. Query/userinfo/fragment URLs and malformed MACs are
rejected. No identifier is generated; only user-provided exact pairs are probed.
Preview and scan never persist accounts. A selected Add action uses the existing
protected transactional store and a fresh catalogue, reusing matching identities.

Sequential scans have a 30-second per-entry deadline, 10-minute overall budget,
cancellation and profile/lifetime guards. Only counts and status survive each
catalogue fetch. URL-only requests use public-page restrictions without MAC/token;
API paths and nonstandard ports are unsupported for this public check. Redirects
stay on the original public host/origin, permitting standard HTTP-to-HTTPS upgrade.
HTTP success or recognized portal HTML never imply catalogue/playback availability.
The separate sample action hands off to existing decoded A/V confirmation.

Offline `--portal-scanner-test` includes bundled shared Windows/Android fixtures
in assets/portal-txt-parity.json plus resource, cancellation, redaction and mock
production-client cases. `--gui-test --portal-scanner-ui-only` covers visible entry,
preview/results, source save/reuse, URL-only editor, public-search handoff, import
interleaving and profile switch. These synthetic results do not validate operator
availability. Current exact-build results belong to verification/RAPORT_WYDANIA.

## Portal protocol compatibility 0.9.7

Private portal connections now negotiate at most three known API endpoints under
the user-supplied installation, always on the same origin. An explicit PHP endpoint
is tried first. A structured nonempty genre response selects the direct MAC
protocol. HTTP 401/403 and recognized authentication-required responses enter the
existing full handshake/profile path. Explicit denial stops; full-profile ID and
status validation is unchanged. Malformed and not-found endpoints may fall through
within the same bounded candidate list. Source settings are never rewritten.

Playback negotiates again before resolving the channel. A public MAG250
User-Agent replaces the default HTTP User-Agent rather than concatenating it.
API requests use the configured MAC; no serial/device identities are generated.
Native playback receives the public User-Agent, never global MAC cookies or
session Bearer headers. Such session-bound media remain a compatibility limit.
Catalog availability, link resolution and decoded playback remain separate facts.

The offline portal suite covers direct and full protocol routing, denial handling,
endpoint prefixes, same-origin boundaries and actual constructed HTTP headers.
Current real-account/native results belong to the release report, with its exact
DLL hash; historical evidence below is not relabeled as a new test run.
The native engine, models and public search/feed scope are unchanged.

R2 reads portal get_all_channels incrementally, retaining only id/name/cmd/xmltv_id.
This endpoint alone allows up to 64,000,000 decoded bytes; other provider documents
keep their prior 24 MB ceiling. Retained payload is bounded to 8,000,000 UTF-8 bytes
and 30,000 unique channels, with an explicit truncated-catalog notice. The reader
continues through EOF after retention limits, so late denial, malformed JSON or a
raw-byte overflow cannot be accepted as a partial success. UTF-8 is validated even
inside ignored fields; token size and depth are bounded. Exact unique counts are
reported only for complete catalogs; truncated catalogs provide a lower bound.

R3 also retains nullable use_http_tmp_link and use_load_balancing metadata in the
protected catalogue. Only both explicitly false flags allow the original absolute
public HTTP(S) command, with an optional ffmpeg/ffrt prefix, after fresh session
validation and a bounded public-DNS preflight. Other commands, absent/unknown or
enabled flags use create_link. Older vault records deserialize both flags as null.
Strict parsing rejects coercion of fractions/exponents, whitespace and Unicode
lookalikes into false. No new media credentials or identity fields are generated.
The native media engine still resolves its own requests; the DNS preflight is not
an assertion of DNS pinning for every segment or redirect inside native playback.

## Historical release notes


## Portal diagnostics 0.9.6

This scoped maintenance change separates public results from the selected private
source. First connection failure opens the source's empty view; failed refresh
retains its cached channels and original success date. Safe failure stages and
codes are stored separately for catalog and playback resolution. No raw exception
message or account/endpoint is exposed by diagnostics. Stale/cancelled operations
must not overwrite the newer source/profile state.

One read-only test of the user's configured account made only handshake/profile
requests on the existing0.9.5 client. Both HTTP replies were200, but profile access
was not confirmed. No catalog/playback request followed. The sanitized observation
is included with its original timestamp and is not a successful0.9.6 playback test.

The final release report distinguishes targeted0.9.6 checks from historical0.9.5
evidence. The intermittent GUI timeout and0.9.1 native hang remain unresolved.
Native engine/model/feed versions are unchanged; the public feed and EPG audit is
the earlier same-day evidence with original timestamps, not a fresh crawl.
No repeated operator login, batch credential test or permission bypass is included.
Known protocol limits (SN/certificates/pairing, session media headers, nontrivial
portal commands) remain unverified for this operator. Windows playback was never
reached in the diagnosed account.

## Historical release notes

## Playback validation and CENC decoder guard 0.9.5

Final R2 public native CENC scenario passed (correct/wrong/recovery/plain/Dispose).
The complete earlier Candidate1 matrix passed720 checks on byte-identical EXE/DLL.
The R2 repetition passed22 suites/589 checks, then the GUI test timed out after240s
without a final GUI report. Bounded late/newer, full Catalog and LocalSearch checks
with fresh/cache/native playback+Stop did not reproduce it. Its cause is unknown;
the release explicitly retains allTestsPassed=false and the failed evidence.
The package is ready as a maintenance artifact with that documented limitation,
not a declaration that every test or the whole project is complete.

PlaybackProgress is shared by UI and StreamProbe: selected audio must progress,
TV also needs video progress, while genuine video-only and radio remain supported.
Encrypted playback needs 12 seconds of sustained observation, not a clear lead.
Failed startup is stopped before persistence; history/source success/watch learning
require confirmed playback. Pause/buffering/error cannot accrue positive time.

EncryptedPlaybackGuard applies only while an explicit CENC key is configured.
It counts native error/fatal events from exact ad/vd/ffmpeg audio/video modules;
raw log text is never copied or persisted. Short bursts, warnings and network logs
do not qualify. Selected stalled tracks need repeated errors across >=2s plus
>=3s without progress; dense repeated decode failures across >=3s also stop
concealed corrupt frames whose timestamps advance. Windows are bounded to5s.
Pause/cache/seek/source/track changes reset the policy. The failure stops playback,
clears the key and latches a safe message/State7 until explicit Stop or Play.
Black/static frames are never a failure criterion. This is not authentication
of decrypted plaintext or a guarantee for every codec/DRM/provider.

Pinned mpv/FFmpeg, model weights, feeds and dependencies are unchanged from0.9.4.
The .NET self-contained runtime and SDK are recorded in build-info.json; current
tools are10.0.404/10.0.15. Same-day public maintenance checks retain their original
timestamps and are identified as reused endpoint evidence, not new binary tests.
No operator authentication is performed in0.9.5. Previous account rejection and
0.9.4 wrong-key failure are preserved as historical evidence. Final package tests
must independently pass on the exact new EXE/DLL. The0.9.1 native hang cause is
still unknown; the test watchdog is not a general interactive-engine fix.

## Portal profile validation 0.9.4

PortalSession validates a positive signed-64-bit integer profile id and explicit
inactive status/blocked values before listing channels or creating playback links.
Flags accept absent/null/empty text, false, integer zero and text 0/false only.
Only ASCII space/tab/CR/LF may pad text; other control or Unicode whitespace fails.
Fractions, exponents, booleans for id, overflow and unknown flag values are rejected;
no lossy conversion to zero. Synthetic tests cover both refresh and resolve gates.
No protocol, pairing, native player, feed or model change is included. The current
SDK10.0.404 bundles .NET10.0.15 (previous release10.0.14); native build20261107
remains pinned. Build-info and bundled licenses identify the actual runtimes.
The versioned report records any operator test separately from fixture results.

## Source Explorer 0.9.3

SourceExplorer classifies public documents as PortalCandidate, PublicPlaylist,
DirectStream or ProviderPage. This is bounded document discovery, not a new trained
model. Limits: six search requests including fallback, twenty fetched pages, depth
two, one hundred candidates, ninety-second linked budget; search stage25s.
Origin modification dates are retained, including old static portal-client HTML;
a fresh GET is not a claim that channels are fresh or playable.
PublicWeb.FetchChecked applies discovery URL policy before EVERY redirect request
and retains DNS pinning, public IPs, ports80/443, no proxies/cookies, content limits.
Discovery never calls portal API/handshake or guesses endpoints. Portal proof uses
a published client route plus active technical script signature; comments, inert
code, articles and lookalike basenames are not proof. Scripts are never fetched.
Search titles/snippets, encoded credential prose, account URLs and credentialed
manifest bodies are excluded. Playlists retain per-entry public sanitization.
SourceExplorerStore is profile-local DPAPI with backup recovery. Failed/cancelled
scans and sorting never renew older dates. UI captures its profile and rejects late
results after switching. Portal handoff supplies exact URL and an empty MAC. Public
M3U retains DiscoveredPublic on every refresh. Direct sources use embedded playback;
protected HLS is not silently passed to clear playback. DASH ContentProtection means
configuration required, not supported/decrypted proof.
--source-explorer-test [--network] covers fake documents, transport hops, limits,
cancellation/recovery and optional public frontend/M3U/MPD reads. GUI tests exercise
actual dialogs, handoff, local filtering and profile switching. No real operator
account was used. Native player/network limitations and the historical CENC hang
remain; this release does not change the native engine.

Live validation found that the official Shaka ClearKey MPD is served as
application/octet-stream (200, 15441 bytes, LastModified2019-06-05). The first
candidate correctly failed its live assertion because transport rejected MIME.
PublicWeb now accepts this MIME only on .mpd/.m3u/.m3u8 paths, within the existing
12MB limit and only with XML/MPD or #EXTM3U content. Binary/nonmanifest responses
remain rejected and XML safety is unchanged. The failed candidate report and
actual response diagnosis are retained; final tests run against the rebuilt DLL.

An independent live Search run reached the official iptv-org GitHub README but
missed its public plain .m3u URL in a code block, while navigation links consumed
the extraction limit. Link extraction is now separate from portal fingerprinting:
published URLs in pre/code remain text, preferred media/playlist URLs are collected
before the bounded navigation list, and escaped routing/signatures remain unchanged.
Portal fingerprints still exclude code/comments/inert scripts. Fixtures cover150
navigation links before a playlist and the actual crawl order, within unchanged
page/time limits. Earlier all-green transport tests did not prove search usefulness;
the failed live search evidence is retained and the final Search is repeated.

## Smart import and reliability 0.9.2

`XtreamLink` decodes account input locally and once, with strict UTF-8, duplicate
credential rejection and preserved server routing prefix. `SmartImportForm`
provides masked preview, explicit own-M3U fallback and server+username dedupe.
`PrivateSourceStore.Connect` commits candidate config/catalog atomically only
after a successful fetch; failed edits retain playable old references. Refresh
uses its original snapshot and cannot replace a newer concurrent edit.
Private playback catches check request and profile ownership. Public search
intercepts pasted URLs before transport and masks/clears report queries even
when pasted while another scan is running. No new protocol/DRM or VOD scope.
Local variants/all/working/channels views now retain the active imported collection;
private source detail counts protected references rather than public candidates.
Whole PL/EN genre queries expand only via group tokens. Discovery does not let an
exact foreign channel named Filmy/Music suppress country-scoped category matches.
Named non-genre and numeric channel queries retain the exact-ID narrowing.

Run `--xtream-link-test` plus `--gui-test` for parser/transaction/late-refresh,
smart paste→form→fixture fetch→local search, delayed search privacy and obsolete
playback callback regressions. Provider network tests distinguish public live
sources from controlled Xtream/MAC fixtures. No commercial account was available.
The historical native ClearKey hang remains unexplained; test-only watchdog is
not a runtime fix. Preserve its evidence and test each final executable separately.

## Własne źródła 0.9.1

PrivateProviderClient obsługuje ręcznie konfigurowane M3U, Xtream live/XMLTV,
podstawowy portal MAC i ograniczone DASH ClearKey. PrivateSourceStore zapisuje
katalog, konfigurację i ramówkę per profil przy użyciu Windows CurrentUser DPAPI.
W zwykłej bibliotece są wyłącznie niejawne identyfikatory prywatnych pozycji.
Odświeżanie zachowuje poprzedni katalog i jego datę przy błędzie; zmiana konfiguracji
wymaga nowego udanego pobrania przed odtwarzaniem. Prywatne źródła nie wchodzą do crawlera.
Szczegółowy zakres, dowody i ograniczenia: PRIVATE_SOURCES.md. Testy kontrolowane:
--provider-test; okna i prywatne odtwarzanie lokalne w --gui-test. Testy sieciowe
nie korzystają z logów kont ani z danych dostępu osób trzecich.

## Poprawki lokalnego wyszukiwania 0.8.1

FilterLocal dopasowuje zapisane tytuły i aliasy bez odrzucania legacy ID przez nowszy spis.
Filter pozostaje rygorystyczny przy odkrywaniu nowych źródeł. LocalQuery zachowuje nazwy
kanałów (np. Polskie Radio), a polecenia naturalne interpretuje spójnie przed i po skanie.
Widok biblioteki ma własne źródło, filtrowanie i stronicowanie; brak kraju w M3U nie usuwa
importu. Zmiana krajów i typu filtruje bazę lokalnie; nowy skan wymaga przycisku.
Scalanie nie przenosi aliasów między różnymi językami/feedami/krajami tego samego URL.
Regresje: --local-search-test i rozszerzone --gui-test. Zależności i modele bez zmian.

## Zmiana wyszukiwania w 0.8.0

Tryb Deep zachowuje limit 180 s / 120 stron, zwiększa głębokość do 3 i rozdziela
30 s budżetu wyszukiwarek od odczytu stron. Rozwija zapytania o nazwy, aliasy,
identyfikatory i język kraju. Bing RSS ma niezależną ścieżkę DuckDuckGo HTML
(klasa result__a, jawne przekierowanie uddg); challenge wyłącza dostawcę w przebiegu.
Konkretne strony live poprzedzają homepages. Indeks domen eliminuje przeglądanie
całych 31 tys. nadawców dla każdego wyniku. Publiczne playlisty mają ten sam fallback,
a głęboki przebieg czyta global index.m3u obok skonfigurowanych list.

Streams.json i zachowanie nagłówków istniały już wcześniej; nie są deklarowane
jako nowa funkcja. Audyt 08.11.2026 znalazł 26 URL PL / 76 DE spoza globalnego M3U,
lecz nie traktuje obecności w JSON jako dowodu działania. channels.json replaced_by
rozszerza aliasy bez zamiany ID istniejącego źródła. Warianty językowe i +1 zachowują
odrębność. Usunięto niedziałające startowe nazwy nl1/at1 Radio Browser; aktualnie
potwierdzony jest de1, więc nie deklarujemy niezależnej redundancji radia.

Nowe lokalne regresje: --deep-web-test oraz --deep-identity-test. Raport wydania
oddziela kandydatów, manifest live i rzeczywiste dekodowanie przez Swir Player.

## Architektura

WinForms / .NET 10 publikuje się jako samodzielne win-x64. Swir Player osadza
libmpv/FFmpeg w oknie aplikacji; własny interfejs nie oznacza autorskiego dekodera.
Nie ma zależności od instalacji VLC. Silnik to przypięty build shinchiro z 07.11.2026;
pochodzenie i SHA-256 są w player/PROVENANCE.json. Nie kompilowano go lokalnie.

StartupGate pokazuje osobne okno oczekiwania i wstrzymuje główne okno do końca
wyszukiwania. Poprawny zapis pozwala wybrać poprzednią bazę. Anulowanie kończy
żądania przed zamknięciem. EPG zaczyna pracę po otwarciu głównego okna.
DiscoveryForm zbiera osobną partię i publikuje ją po zakończeniu dostawców oraz
wstępnych prób. CatalogView filtruje lokalnie i stronicuje po 100. Nieudany test
aktualizuje znaczek bez usuwania kanału ani przestawiania wierszy.

PlaylistRepository zapisuje znalezione M3U, metadane kanałów, źródło, daty i hash.
HLS media/master playlist nie jest biblioteką kanałów. Eksport oryginału zachowuje
całą listę; filtrowany stosuje kraje i opcjonalny zbiór świeżo potwierdzonych URL.
Wczytanie nie oznacza udanego dekodowania. Jawne „Ustaw EPG playlisty” otwiera
ustawienia wybranego XMLTV dla kraju; import M3U sam nie nadpisuje źródła ramówki.

TasteNet klasyfikuje dziesięć tematyk; osobny model profilu uczy preferencji.
SearchLearning uczy kolejność odwiedzania stron z rzeczywistych wyników odtwarzania.
Sieć, parsery, kolejka prób i awaryjna zmiana źródła pozostają kodem aplikacji.
Interfejs nie ma asystenta. Stałe wagi klasyfikatora pozostają bez zmian;
modele preferencji i wyszukiwania aktualizują się lokalnie. Szczegóły: MODEL_CARD.md.

## Lokalne uczenie wyszukiwania

SearchLearning(profileFolder, LibraryData, IntentNet) zapisuje search-learning.json
oddzielnie dla profilu. Record przyjmuje rzeczywisty Health z sond i odtwarzacza;
nie ucz sukcesu z pobrania HTTP/M3U, EPG ani samej obecności w katalogu. Model ma
512 znormalizowanych cech haszowanych: domena, kanał, kraj, tematyka, rodzaj.
Regresja logistyczna aktualizuje wagi ograniczone do ±6. Wpływ starych wag maleje
wykładniczo ze stałą 30 dni; maks. 4000 odcisków źródeł ma retencję 30 dni.
Powtórzony wynik źródła nie mnoży nauki przez 30 min, lecz świeża zmiana wyniku
może skorygować ocenę. Pełny kontrakt i ograniczenia są w MODEL_CARD.md.

Snapshot zamraża preferencje i wyuczone oceny dla danego przebiegu. DiscoveryCoverage
zmienia wyłącznie kolejność; co czwarty slot eksploruje rzadziej odwiedzane miejsca,
a kraje rotują. Dokładne zapytanie i wybrane kraje mają pierwszeństwo. Sprawdź,
że wyłączenie LearnPreferences zatrzymuje zapis i osobiste priorytety szukania.
Reset aktywnego profilu zeruje TasteWeights/TasteSignals/TasteExamples,
SearchLearning i jego visited-broadcasters.json; nie czyści biblioteki oglądania.
Historia odwiedzin jest teraz przypisana do profilu. Przełączenie profilu tworzy
nową instancję modelu. Nie pakuj żadnego search-learning.json ani historii odwiedzin.
Test --search-learning-test sprawdza zachowanie na fixture, nie mierzy ogólnego
wzrostu skuteczności. Modele nie generują kodu ani nie zmieniają zabezpieczeń sieci.

## Kompilacja i pakowanie

Na Windows x64 potrzebne są SDK .NET 10, PowerShell i tar obsługujący archiwum 7z
dostawcy. Uruchom build.ps1 -Destination <nowy-folder>. Opcjonalny parametr
-DependencyCache <folder> pozwala wykorzystać zweryfikowane pobrania.
Skrypt odmawia pracy w niepustym folderze i nie aktualizuje działającej aplikacji.
Publikuje --self-contained true, wywołuje dołączony Acquire-SwirPlayer.ps1,
kopiuje player, dokumentację, trzy archiwa źródeł oraz licencje dokładnych wersji
runtime odczytanych z opublikowanego runtimeconfig.json. build-info.json opisuje
kompilację, nie zastępuje raportu testów. Skrypt niczego nie publikuje.

Pełny ZIP musi zachować cały układ folderów. Nie przygotowuj poprawki nadpisującej
stary EXE lub samą DLL: wydanie wymaga kompletnej paczki. Silnik jest ten sam co w 0.7.0. Nie pakuj prywatnego data, historii,
własnych playlist, cache prób ani sekretów. Migracja: kopia całego data, zamknięcie
obu wersji, przeniesienie danych do nowego folderu. Zachowaj poprzednie wydanie.

## Nierozwiązana kompletność źródeł silnika

Wybrany build natywny jest GPL-3.0-or-later z odpowiednimi warunkami składników,
nie LGPL-only. Są dokładne źródła mpv, FFmpeg i recepty budowania. Brakuje
zweryfikowanego pełnego zestawu źródeł/rewizji wszystkich statycznie połączonych
zależności i kompletnego spisu ich licencji. Recepta zawiera zależności z ruchomych
gałęzi, a artefakt logu dostawcy wymagał uwierzytelnienia GitHub.
sourceBundleComplete=false musi pozostać zgodne ze stanem faktycznym.
Nie opisuj trzech archiwów jako kompletnego corresponding source ani dowodu
odtwarzalnej kompilacji natywnej. Dołączenie zawiadomienia nie usuwa tej luki.
Przed uznaniem źródeł za kompletne pozyskaj brakujące rewizje, licencje i źródła
albo odtwórz silnik z w pełni przypiętego, zachowanego zestawu. THIRD_PARTY.txt
oraz player/PROVENANCE.json wskazują dokładny zakres dołączonych materiałów.

## Zależności i aktualizacje

Okresowo i po komunikatach bezpieczeństwa sprawdzaj wydania .NET, mpv, FFmpeg
i dostawcy Windows. To zadanie opiekuna, nie skonfigurowana automatyzacja.
Aktualizacja wymaga zmiany przypiętych URL/hash/rewizji w Acquire-SwirPlayer.ps1,
licencji i pochodzenia, testów rzeczywistej DLL oraz odpowiadających źródeł.
Nie zamieniaj tylko DLL na plik o nieznanej zgodności. Ciche sondy korzystają
z filtra fingerprint w przypiętym buildzie mpv; nowa wersja może go nie zawierać.
HlsPlaybackSource ogranicza master do wariantu zgodnego z wybranym trybem jakości,
zachowując pasujące grupy mediów. MainForm przekazuje quality.Text do SwirPlayer.Play;
testuj osobno stabilność, najwyższą jakość i oszczędzanie transferu oraz brak
zastąpienia obrazu samym audio. To wybór istniejącej reprezentacji, nie transkodowanie.
Sprawdzaj zamykanie procesów, pauzę, dźwięk, wyjście obrazu i kody wyjścia także po
zwolnieniu zasobów. Raport zapisany przed awarią procesu nie zalicza testu.

Nie ma publicznego repozytorium, aktywnego zdalnego CI, podpisu Authenticode ani
serwera aktualizacji. Odświeżenie danych, trening modelu i aktualizacja EXE/silnika
to oddzielne czynności. Nie uruchamiaj programów wskazanych w listach kanałów.
Zmiany usług wymagają utrzymania adapterów, nie obietnicy działania przez lata.

## Dostawcy, schematy i świeżość

data/sources.json ma Schema=1. TV używa szablonów {path} dla index.m3u oraz
countries/xx.m3u; radio schematu Radio Browser. ChannelDirectoryUrls oraz
StreamDirectoryUrls wskazują iptv-org API. Mirrory jednego projektu nie są
niezależnym potwierdzeniem. WebSearchTemplate wymaga RSS i {query}; Bing RSS jest
publicznym, niestabilnym interfejsem bez SLA. SharedPlaylistUrls obejmuje Free-TV/IPTV.
Nowy format wymaga adaptera i testów, nie samego podmienienia adresu.

Pobieraj rzeczywiste endpointy użyte przez aplikację; sprawdzaj schemat, zawartość,
Last-Modified, daty pobrania, wiek i błędy, nie samo HTTP 200 ani zgodność hasha.
Katalogi: cache 6 h, awaryjnie do 7 dni. Brak Last-Modified oznacza nieznaną datę
aktualizacji. catalogDataAt zachowuje pochodzenie wieku całej zachowanej bazy;
completedAt raportu nie odmładza danych po awarii lub anulowaniu.
Zapis startowy musi pasować do krajów i rodzaju. Uszkodzone rekordy nie mogą
przerwać startu. M3U może zawierać wygasające adresy mimo poprawnego hasha.
Nie zwiększaj limitów wieku ani nie wyłączaj walidacji, żeby ukryć awarię.

Odczyt WWW: zwykły do 24 stron / 60 s, głęboki do 120 / 180 s; publiczne listy
do 5 / 40 s lub 15 / 90 s. Historia pokrycia kieruje kolejne przebiegi do dawniej
sprawdzanych miejsc. Kraje, kategorie i ukrycie pozostają filtrami.
Jawny iframe i manifest mogą dać kandydatów; aplikacja nie uruchamia JavaScript
ani nie używa kont przeglądarki. PublicWeb ogranicza rozmiary/czas, adresy sieciowe,
DNS i przekierowania. HTML, EPG i sam manifest nie stanowią próby odtwarzania.

Analiza ma cel 8 różnych kanałów i limit 32 nowych prób. „Szukaj dalej” zwiększa
cel o 8. Potwierdzenie „Działające” wymaga udanej próby z ostatnich 10 minut;
starszy wynik może wpływać na ranking do 6 h. To nie ciągły monitoring.
Awaryjna zmiana źródła próbuje do 3 adresów zgodnego kanału/wariantu.
Nie ma gwarancji całego internetu, wszystkich płatnych kanałów ani długiej stabilności.

## EPG

Domyślne XMLTV/gzip: https://iptv-epg.org/files/epg-{country}.xml.gz oraz
https://free-epg.de/api/epg/{country}.xml.gz; ustawienia: data/epg-settings.json.
Dane społeczności mogą mieć wspólne pochodzenie. Sprawdzaj rzeczywiste audycje,
strefy, start/stop, Last-Modified, przyszłe/stare daty i niejednoznaczne nazwy.
Cache 6 h; awaryjnie do 48 h tylko z nadal aktualnymi audycjami. Odczyt ma limity
rozmiaru/liczby audycji, wyłączony resolver XML, maks. 12 krajów i 2 pobrania naraz.
UI odświeża wiersze co 30 s, sprawdza potrzebę pobrania co 15 min, a parsowanie
działa poza wątkiem okna. EPG nie przestawia kanałów ani nie dowodzi ich dostępności.
Po wyjściu z bramki startu trzeba uruchomić timer EPG, nie tylko jego flagę.

## Weryfikacja przed wydaniem

1. Zbuduj do nowego folderu. Potwierdź runtimeconfig.json, pochodzenie/hash DLL,
   brak folderu VLC, model/ikony/fixture i licencje rzeczywistych wersji runtime.
2. Uruchom TESTUJ_WINDOWS.cmd: self, discovery, web, playlist, personal, search-learning, deep,
   EPG, player, startup-resource i gate. Zachowaj raporty i kod wyjścia procesów.
   Fixture lokalne nie potwierdzają dzisiejszego działania dostawców.
3. Uruchom --gui-test --data-dir <osobny-folder> --report <plik> na pulpicie
   Windows. Sprawdź osadzone wideo/audio, sterowanie, źródła zapasowe, profile,
   listy/eksport, EPG, brak asystenta i brak otwierania przeglądarki po wyborze.
   Obejrzyj okno startowe i listy przy rozmiarze domyślnym oraz minimalnym.
4. Uruchom sieciowe --discovery-test --network, --web-test --network,
   --playlist-test --network, --personal-test --network, --deep-test --network
   i --epg-test --network z osobnymi raportami. Zapisz awarie/ograniczenia usług.
   Test WWW ma sprawdzać wynik wyszukiwania/wyodrębnienie/dekodowanie, nie sam katalog.
5. --gate-test obejmuje ukryte główne okno, tokeny anulowania, poprzednią bazę,
   wiek katalogu, kraj/rodzaj i malformed/null rekordy.
   --startup-test --data-dir <osobny-folder> --report <plik> mierzy responsywność
   okna w trybie diagnostycznym, nie czas pełnego internetowego wyszukiwania.
6. Zapakuj cały folder, wygeneruj manifest SHA-256, rozpakuj do innej lokalizacji,
   porównaj wszystkie pliki i uruchom rozpakowany EXE/testy. Sprawdź czyste zamknięcie.
   Zachowaj konkretną wersję źródeł i raporty odpowiadające tej samej DLL oraz EXE.

To procedura, nie deklaracja zaliczenia testów. Wyniki bieżącego wydania opisuj
na podstawie zachowanych raportów. Nie wpisuj liczby zaliczonych prób ani
kompletności źródeł bez dowodów.


## Modules and public playlist handoff in 0.9.1
ProviderModules implements four visible modules and immutable per-kind editors.
SaveConnectSource captures the profile store before async work; only a new successful
catalogue from that same store can replace the visible list. Cancel is checked before
persisting refresh completion. Default DiscoveredPublic=false is omitted from JSON,
keeping 0.9.0 account configuration hashes byte-compatible.
PublicPlaylistPrivacy removes definite account-bearing entries individually while
retaining public signed CDN links. FoundPlaylist keeps response SHA256, clean-body
SHA256, removed count and sanitized flag; exports label cleaned copies honestly.
Public-discovered subscriptions sanitize every refresh, while manual operator sources
keep their previous contract. AI handoff never silently converts an existing manual source.
Regression hooks: --public-playlist-test [--network], --public-provider-test,
and expanded --gui-test covering modules, migration, profile switch, failure/cancel,
local imported-channel search and neutral/duplicate-safe ranking.
