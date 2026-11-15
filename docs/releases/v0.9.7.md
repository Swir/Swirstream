SwirStream AI — Windows 0.9.7 and Android 0.3.6 — by Swir

This release fixes three portal compatibility problems that could leave a channel list empty or prevent playback after the list had loaded. It includes a built Windows application with its runtime dependencies and a signed Android APK.

## What changed

- **Direct MAC portals:** recognize portals that provide their catalogue without a separate token-based handshake. Explicit access refusals remain failures, and token-based portals still require a valid session.
- **Large catalogues:** read channel responses incrementally and keep the previous catalogue if a refresh fails. The app visibly identifies a shortened list.
- **Playback address selection:** use the original channel address when the operator explicitly disables both temporary links and load balancing, after checking the session again. Missing or enabled flags retain the normal `create_link` flow. Refresh existing sources to load these new channel fields.

## Verification

- **Windows:** 380 targeted checks across 14 suites passed. The ZIP was extracted into a different folder, every extracted file was checked against its original, and four additional suites passed from that location.
- **Android:** 196 selected checks passed: 100 unit tests, plus 47 regression tests and one public-stream UI test on each of Android 6 and Android 15 emulators. APK signatures and upgrading without uninstalling were checked.
- **Real portal tests on Windows:** all 76 supplied, authorized account configurations were attempted. The results below are mutually exclusive:

| Result | Accounts |
| --- | ---: |
| Catalogue loaded and a sample played with progressing video and audio | 36 |
| Catalogue loaded, but sampled playback was not confirmed | 24 |
| Catalogue response rejected or unsupported | 10 |
| Request timed out; result remains incomplete | 6 |

In total, 60 accounts loaded a catalogue. Five catalogues reached the retained-list limit. Separate follow-up checks confirmed that three of the rejected responses exceeded 64 MB; the remaining rejection causes were not established. One supplied portal address had no associated MAC and could not be tested.

Comparison with the unmodified IPTVnator 0.25.0 covered three accounts. It exposed the direct-MAC and playback-address differences fixed here. For one previously failing channel, the final Windows build completed a one-minute audio/video observation and visibly played beyond one minute in its normal window. Another stream stopped after roughly 40 seconds in both players, while a third comparison produced a catalogue but no confirmed playback.

Playback results cover sampled channels at the time of testing, not every channel or future availability. Real operator accounts were not tested on Android. Physical F150 and HY300 devices were not tested.

## Installation and upgrade

**Windows:** download `SwirStreamAI_0.9.7_Windows_x64.zip`, extract the entire archive into a new folder, then run `SwirStreamAI.exe`. VLC is not required. To retain an existing library, close both versions, back up the old `data` folder, and copy it beside the new EXE using the same Windows account.

**Android:** install `SwirStreamAI_Android_0.3.6.apk`. It uses the existing signing identity and supports Android 6 or later; compatibility with every device is not established.

**After updating:** open **MAC Player**, select your source, and choose **Odśwież i pokaż kanały** (Refresh and show channels). Previously saved lists do not contain the new playback-selection fields.

## Remaining limitations

- Catalogue limits are 64 MB per decoded response, up to 30,000 unique channels, and 8 MB of retained channel fields. A shortened list is identified explicitly; responses beyond the response limit are rejected.
- Earlier intermittent GUI and native-player hangs remain unresolved. These targeted checks do not establish that every application feature is fixed.
- Operator-specific device pairing, serial numbers, certificates, media-session headers, and portal EPG may require further support. This release does not expand DRM support or provide universal portal compatibility.
- The application source archives are available separately. The bundled native dependencies still have an incomplete corresponding-source bundle; the application source ZIP is not a complete source distribution for all GPL dependencies.

The verification reports preserve earlier failed attempts and distinguish them from the final build's passing checks. Test account credentials and operator addresses are not included in the release assets.
