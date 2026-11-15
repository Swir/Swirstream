# SwirStream AI

**TV, radio and live streams for Windows and Android — by Swir.**

SwirStream AI combines an embedded player, public-source discovery, personal playlists and local preference learning. It has no chat assistant and does not require a cloud AI subscription. The current app interface is primarily Polish; the project overview, release notes and roadmap here are in English.

## Download

The portal scanner preview contains **Windows 0.9.8** and **Android 0.3.7**.

| Platform | Download |
| --- | --- |
| Windows 11, x64 | [Portable ZIP with EXE and dependencies](https://github.com/Swir/Swirstream/releases/download/v0.9.8/SwirStreamAI_0.9.8_Windows_x64.zip) |
| Android 6 or later | [Signed APK](https://github.com/Swir/Swirstream/releases/download/v0.9.8/SwirStreamAI_Android_0.3.7.apk) |

[Release notes and all assets](https://github.com/Swir/Swirstream/releases/tag/v0.9.8) · [Portal TXT scanner guide](docs/PORTAL_SCANNER.md) · [Development roadmap](ROADMAP.md)

On Windows, extract the **entire ZIP** into a new folder before running `SwirStreamAI.exe`. VLC is not required. To move your existing library, close both versions, back up the old `data` folder, and copy it beside the new EXE under the same Windows account.

To scan a TXT list, open **MAC Player → Skaner portali**, import the file and choose **Sprawdź listę**. A scan checks the entries without automatically adding accounts to your library; selected sources can then be saved or opened in the embedded player.

## What the application does

- Searches supported public catalogues and web sources, with country choices and multiple source candidates.
- Imports M3U playlists and user-configured playlist URLs, Xtream accounts and supported MAC portals.
- Imports TXT portal lists with preview, duplicate removal, bounded access checks and credential-free CSV reports.
- Plays supported streams inside the application, with favourites, history, local search and XMLTV EPG.
- Uses local TasteNet classification and per-profile preferences to rank content. The web explorer is a bounded discovery pipeline, not an AI model that searches the entire Internet.
- Keeps configured private sources separate from public discovery. Test accounts and access credentials are not supplied with the project.

Windows uses an embedded libmpv/FFmpeg player. Android uses Media3. DRM support depends on the platform, format, device and authorized licence configuration; it is not universal. See the platform documentation for the supported scope.

## This preview

The new scanner accepts portal URLs alone, `URL|MAC`, and URL blocks followed by assigned MAC entries. It supports files up to 1 MiB and 200 unique entries. Checks run sequentially with cancellation, 30 seconds per entry and 10 minutes per scan. Public-page availability, catalogue access and confirmed playback are separate states.

The final Windows build passed **514 distinct targeted checks**, including 43 scanner checks and 16 dedicated scanner UI checks, plus tests after ZIP extraction. The [verification report](docs/VERIFICATION.json) records both platforms' exact test scope and artifact hashes.

One authorized Windows account was checked through the new scanner: **27,165 channels** loaded, and a sample played in the normal embedded player window. The public-URL-only path was checked separately without account authentication. The previous 76-account comparison belongs to [release 0.9.7](docs/releases/v0.9.7.md); those accounts were not all retested for this update. Android operator-account playback and physical F150/HY300 devices remain unverified.

Catalogue limits remain **64 MB per decoded response, 30,000 unique retained channels and 8 MB of retained fields**. Earlier intermittent GUI/native-player hangs remain unresolved. The complete application feature matrix was not rerun for this preview. See [detailed release notes](docs/RELEASE_NOTES.md).

## Source layout and builds

- `windows/`: Windows application, models, assets and tests; version 0.9.8.
- `android/`: Android application, models, assets and tests; version 0.3.7, version code 11.
- `docs/SOURCE_SNAPSHOT.json`: checksums linking the current application-source files to the tested source archives.

Published binaries are built from frozen application-source inventories. The source archives are checked against those inventories and the corresponding repository files. Root documentation is separate from the application builds. No automatic publishing workflow is installed.

### Windows

Use Windows x64, PowerShell, .NET 10 SDK (tested with 10.0.404) and Windows `tar`, with network access for pinned dependencies:

```powershell
pwsh -File .\windows\build.ps1 -Destination .\artifacts\windows-0.9.8
```

The destination must be new or empty. The script builds the application and obtains the pinned native player; it does not compile the native dependencies. Run the produced `TESTUJ_WINDOWS.cmd` and the platform-specific checks before distributing a new build.

### Android

Use JDK 17 or newer compatible with AGP 9.0.1, Android SDK platform 36.1 and build-tools 36.0.0. Set `JAVA_HOME` and `ANDROID_HOME` or provide your own untracked `local.properties`. The included wrapper pins Gradle 9.3.1.

```powershell
pwsh -File .\android\build.ps1 -Mode Debug
```

`-Mode Release` produces an unsigned APK. Use your own signing key for your builds; the release signing key is not in this repository. A differently signed APK cannot update the published APK in place.

## Licensing and dependency provenance

The original application source is MIT licensed. Third-party components retain their own terms; see [Windows notices](windows/THIRD_PARTY.txt) and [Android notices](android/docs/THIRD_PARTY_ANDROID.md).

The selected Windows native player build includes GPL components. Its bundled corresponding-source coverage is **incomplete**, and the application source archive is not a complete source distribution for all native dependencies. Completing that dependency inventory and source bundle is an explicit release-maintenance task in the [roadmap](ROADMAP.md). The MIT application licence does not replace those dependency obligations.
