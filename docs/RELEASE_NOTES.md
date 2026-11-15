SwirStream AI — Windows 0.9.8 and Android 0.3.7 — by Swir

This preview adds a separate **portal TXT scanner** to **MAC Player** on both platforms. Import a list, review its entries, check access, then add a selected source or try a channel in the embedded player.

## What changed

- **TXT import:** accepts a portal URL alone, `URL|MAC`, or a portal URL followed by your assigned MAC entries. Supports UTF-8 and BOM-marked UTF-16, removes duplicates, and reports invalid lines without exposing credentials. Limits: 1 MiB and 200 unique entries.
- **Bounded checks:** scans sequentially with progress, cancellation, a 30-second budget per entry and a 10-minute budget per scan. Completed results remain available when scanning stops. Scanning itself does not save accounts.
- **Clear results:** distinguishes a reachable public page from a recognized portal client and a successfully loaded channel catalogue. A loaded catalogue is explicitly marked as playback untested.
- **Selected actions:** refresh and save the chosen source through protected profile storage, or open a sample in the existing embedded player. Existing matching sources are reused. Closing the scanner or switching profiles prevents delayed results from writing to another profile.
- **Public discovery shortcut:** opens the existing web explorer with a portal-focused query and filter. It looks for public pages; it does not collect account credentials or establish that a portal is free.
- **Private reports:** CSV exports contain entry numbers, TXT line numbers, result states and counts, without portal addresses, MACs or access tokens.
- **Android:** adds a fallback file picker for devices lacking the standard document picker, and stops portal processing after an authenticated category request explicitly refuses access.

## Use it

Open **MAC Player → Skaner portali TXT → Importuj TXT** on Windows, or **MAC Player → Skaner portali • import TXT** on Android. Review the preview, then choose **Sprawdź listę**. Select a successful catalogue to add it or open a sample in the player. See the [TXT format and limits](https://github.com/Swir/Swirstream/blob/v0.9.8/docs/PORTAL_SCANNER.md).

## Verification

Windows passed **514 distinct targeted checks**, including 43 scanner checks and 16 dedicated scanner UI checks. The full GUI suite was also repeated once; that repeat is preserved in the evidence and is not counted twice here. Additional tests run from the extracted ZIP verify relocation.

A normal-window Windows test imported one previously authorized account, loaded **27,165 channels**, and launched a sample in the embedded player. Changing video, advancing playback position, the player's confirmed-playback state, manual Stop and clean exit were observed. The public-URL-only check separately returned a public-page result without account authentication. This does not revalidate the 76 accounts tested for the previous release.

Android passed **244 selected checks**: 111 unit tests, 52 provider/player regression tests on each of Android 6 and Android 15, and 14/15 scanner tests respectively. One public-network sample was intentionally skipped on Android 6. The Android 15 scanner flow saved a synthetic portal source and played a real public DASH sample with confirmed audio/video progress. APK signing and replacement installation without uninstalling were checked. Operator accounts were not tested on Android.

The attached `TEST_RESULTS.json` and `SHA256SUMS.txt` identify the exact tested and packaged files. Test accounts and signing keys are excluded.

## Installation

**Windows:** extract the entire `SwirStreamAI_0.9.8_Windows_x64.zip` into a new folder and run `SwirStreamAI.exe`. VLC is not required. To transfer a library, close both versions, back up the old `data` folder and copy it beside the new EXE under the same Windows account.

**Android:** install `SwirStreamAI_Android_0.3.7.apk`. The APK uses the existing signing identity and targets Android 6 or later.

## Remaining limits

- A reachable portal may require an operator account; catalogue access does not guarantee playback of every channel. Operator-specific pairing, serial numbers, certificates, portal EPG and DRM remain outside this scanner change.
- Retained catalogues remain limited to 30,000 unique channels, a 64 MB decoded response and 8 MB of retained fields. Re-running a scan starts from the beginning; long lists may need splitting after the 10-minute limit.
- An Android cloud document provider may block while opening a file; use a local TXT file if it does not respond. The F150 phone and HY300 projector have not been physically tested.
- Earlier intermittent GUI/native-player hangs remain unresolved. This preview verifies the stated scope, not the complete feature matrix or long-duration playback.
- The Windows native dependencies' corresponding-source bundle remains incomplete; the application source ZIP is not a complete source distribution for all native dependencies.

Previous portal compatibility results remain documented in the [0.9.7 release](https://github.com/Swir/Swirstream/releases/tag/v0.9.7).
