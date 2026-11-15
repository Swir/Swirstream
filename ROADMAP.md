SwirStream AI — proposed next steps

The next development cycle should prioritize reliable playback and complete, searchable catalogues before adding more provider types or changing the visual design. These are planned tasks, not capabilities already delivered.

## 1. Playback reliability and release reproducibility

Investigate the remaining GUI/native-player hangs and add bounded recovery for expired sessions, temporary network loss, and live streams ending unexpectedly. Keep manual Stop final, avoid reconnect loops after an access refusal, and preserve the working library during failures. Complete the native dependencies' corresponding-source bundle and document a reproducible release process.

**Done when:** supported test streams complete 30-minute runs; interrupted connections recover within a documented retry budget; manual Stop prevents reconnects; repeated channel changes and shutdown do not hang; release binaries, application sources, native dependency sources, and build instructions can be traced to exact versions.

## 2. Complete large catalogues and dependable local search

Add provider-supported catalogue pagination with resumable progress instead of relying on a larger single-response limit. Keep a stable local search index for every imported channel, with consistent treatment of Polish characters, country prefixes, punctuation, and quality labels. Show whether a result is missing because of a filter, an incomplete import, or a provider limit.

**Done when:** paginated fixtures above 100,000 channels import with bounded memory; interrupted imports resume without duplicates or losing the previous catalogue; search returns channels from every imported page consistently on Windows and Android; unsupported pagination has an explicit, recoverable status.

## 3. Operator EPG and channel identity

Add supported portal EPG retrieval, retain existing XMLTV support, and improve matching through provider channel IDs, aliases, country, and language. Display current/next programmes with clear timezone handling, cache age, and missing-data states.

**Done when:** Windows and Android show the same channel/programme match for shared fixtures; timezone and daylight-saving transitions are tested; stale EPG is labelled; a failed refresh preserves the previous schedule without presenting it as current.

## 4. Android TV and physical-device validation

Test the signed APK on the user's Android 15 F150 and HY300 projector, record the projector's actual Android version, and improve remote-control focus, fullscreen behaviour, resume, audio-track selection, and decoder fallback.

**Done when:** both devices can install/update, browse and search a large local catalogue, start supported streams, switch channels, and exit cleanly; projector flows work using only its remote; prolonged playback and app background/resume are checked on the physical hardware.

## 5. Maintainable discovery and local personalization

Separate provider adapters from the catalogue and player so source changes can be diagnosed and tested independently. Add versioned source metadata, freshness checks, and explicit failure reasons. Improve per-profile local ranking using verified playback progress, recent reliability, language, country, and user feedback; keep assistant chat out of the interface.

**Done when:** a broken provider does not interrupt other searches; outdated source data is visible and refreshable; ranking improvements are measured on a fixed evaluation set; failed, paused, or abandoned playback cannot generate positive learning; profiles can reset or export their learning without exposing credentials.

## Delivery order

Start with stability and reproducible releases, then large-catalogue import/search. Develop EPG and Android TV validation alongside that work where they do not block the core fixes. Expand discovery and personalization after those foundations are measured and reliable. Use small preview releases with explicit test scope before calling a milestone stable.
