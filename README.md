# SwirStream AI

**TV, radio and live streams for Windows and Android — by Swir.**

SwirStream AI combines an embedded player, public-source discovery, personal playlists and local preference learning. Discovery and ranking run without a cloud AI subscription. In the working version the first scan, country picker, channel search and main player controls support English and Polish. Some advanced settings still use Polish.

## Download

| Platform | Current release |
| --- | --- |
| Windows 11 x64 | [SwirStream AI 0.12.0 installer](https://github.com/Swir/Swirstream/releases/download/v0.12.0/SwirStreamAI_0.12.0_Setup_x64.exe) |
| Android 6 or later | [SwirStream AI 0.6.0 APK](https://github.com/Swir/Swirstream/releases/download/v0.12.0/SwirStreamAI_Android_0.6.0.apk) |

First-time Windows Setup downloads and verifies libmpv from its upstream publisher. Start both clients normally to check for future stable versions. You can also select **App updates / Aktualizacje programu**.

Deep Search includes worldwide IPTV-org and Free-TV catalogues, TDTChannels, Kodinerds and public broadcaster sites. Import IPTV bouquets exported by Dreambox or Enigma2 receivers under **Import M3U / Dreambox IPTV**. The public search respects the selected countries. Finding a stream and verifying playback are separate steps.

The player also supports favourites, history, XMLTV guides, per-profile local ranking and individually configured provider accounts. Windows uses libmpv. Android uses Media3.

[Release notes](docs/releases/v0.12.0.md) · [All releases](https://github.com/Swir/Swirstream/releases) · [Development roadmap](ROADMAP.md)

## Source layout and builds

The working tree builds Windows 0.12.0 and Android 0.6.0. New installations start with the device region when recognized and use worldwide search otherwise. Existing installations keep their selected countries. Choose **Language / Język** to select English or Polish for the next launch. Both versions use the same country and language index for public TV searches. The saved profile helps choose the broadcaster domain for each search. Playback is verified separately from catalogue discovery.

The shared vocabulary lives in `shared/discovery/world-search.tsv`. To refresh it, save the provider's country catalogue and run `python tools/update_search_markets.py countries.json` from the repository root. The tool validates country codes and writes the resource used by both builds. [Provider country catalogue](https://iptv-org.github.io/api/countries.json)

- `windows/` contains the Windows application, models, assets and tests.
- `android/` contains the Android application, assets and tests. The current version code is 13.
- `docs/SOURCE_SNAPSHOT.json` records the application-source checksums for the published v0.9.8 release.

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
