# SwirStream AI

**TV, radio and live streams for Windows and Android — by Swir.**

SwirStream AI combines an embedded player, public-source discovery, personal playlists and local preference learning. It has no chat assistant and does not require a cloud AI subscription. The current app interface is primarily Polish; the project overview, release notes and roadmap here are in English.

## Download

The first GitHub preview contains **Windows 0.9.7** and **Android 0.3.6**.

| Platform | Download |
| --- | --- |
| Windows 11, x64 | [Portable ZIP with EXE and dependencies](https://github.com/Swir/Swirstream/releases/download/v0.9.7/SwirStreamAI_0.9.7_Windows_x64.zip) |
| Android 6 or later | [Signed APK](https://github.com/Swir/Swirstream/releases/download/v0.9.7/SwirStreamAI_Android_0.3.6.apk) |

[Release notes and all assets](https://github.com/Swir/Swirstream/releases/tag/v0.9.7) · [Development roadmap](ROADMAP.md)

On Windows, extract the **entire ZIP** into a new folder before running `SwirStreamAI.exe`. VLC is not required. To move your existing library, close both versions, back up the old `data` folder, and copy it beside the new EXE under the same Windows account.

After updating either app, open **MAC Player**, select your source, and choose **Odśwież i pokaż kanały** (Refresh and show channels). This refresh loads the new playback-selection fields.

## What the application does

- Searches supported public catalogues and web sources, with country choices and multiple source candidates.
- Imports M3U playlists and user-configured playlist URLs, Xtream accounts and supported MAC portals.
- Plays supported streams inside the application, with favourites, history, local search and XMLTV EPG.
- Uses local TasteNet classification and per-profile preferences to rank content. The web explorer is a bounded discovery pipeline, not an AI model that searches the entire Internet.
- Keeps configured private sources separate from public discovery. Test accounts and access credentials are not supplied with the project.

Windows uses an embedded libmpv/FFmpeg player. Android uses Media3. DRM support depends on the platform, format, device and authorized licence configuration; it is not universal. See the platform documentation for the supported scope.

## This preview

Three portal fixes improve direct-MAC discovery, incremental reading of large catalogues, and selection of the original stream address when both relevant operator flags are explicitly disabled.

The final Windows build passed **380 targeted checks** and four additional test suites after ZIP extraction. Android passed **196 selected checks**, including emulator runs on Android 6 and 15. A Windows comparison used the unmodified IPTVnator 0.25.0.

All **76 authorized account configurations** were attempted on Windows: 60 loaded catalogues, including 36 with a confirmed audio/video playback sample. Another 24 loaded a catalogue without a confirmed sample, 10 responses were rejected or unsupported, and 6 requests timed out. These are sampled results, not verification of every channel. Android operator-account playback and physical F150/HY300 devices were not tested.

Catalogue limits remain **64 MB per decoded response, 30,000 unique retained channels and 8 MB of retained fields**. Earlier intermittent GUI/native-player hangs remain unresolved. The complete application feature matrix was not rerun for this preview. See [detailed release notes](docs/RELEASE_NOTES.md).

## Source layout and builds

- `windows/`: Windows application, models, assets and tests; version 0.9.7.
- `android/`: Android application, models, assets and tests; version 0.3.6, version code 10.
- `docs/SOURCE_SNAPSHOT.json`: checksums linking all 303 application-source files to the tested source archives.

Published binaries were built and tested before this repository snapshot was created. Their source files are preserved byte for byte. Repository documentation does not rebuild or replace release assets, and no automatic publishing workflow is installed.

### Windows

Use Windows x64, PowerShell, .NET 10 SDK (tested with 10.0.404) and Windows `tar`, with network access for pinned dependencies:

```powershell
pwsh -File .\windows\build.ps1 -Destination .\artifacts\windows-0.9.7
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
