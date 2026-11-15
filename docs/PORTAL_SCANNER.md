# Portal TXT scanner

The portal scanner is available in Windows 0.9.8 and Android 0.3.7. Open **MAC Player**, then **Skaner portali TXT** on Windows or **Skaner portali • import TXT** on Android. Import your TXT file, review the preview, then choose **Sprawdź listę**. Windows 0.9.7 / Android 0.3.6 do not contain it.

The scanner checks a list you supply and lets you add selected sources to your active profile. Scanning does not automatically add every account to your library. Internet discovery remains separate: it proposes public portal pages and does not obtain account credentials.

## Input format

A TXT file may contain a portal address alone, an address and your assigned MAC on the same line, or an address followed by its MAC entries:

```text
https://portal.example.org/c/

https://second.example.org/c/|02:00:00:00:00:01

https://third.example.org/c/
02:00:00:00:00:02
02:00:00:00:00:03 - optional expiry note
```

These are placeholders, not working portals or accounts. Replace them with your own configuration. An expiry note is only text; it cannot establish that an account is active or authorized.

Import is limited to a 1 MiB file and 200 unique entries. UTF-8 and BOM-marked UTF-16 are supported. Import checks the file before scanning, reports rejected entries and duplicates, and does not display the rejected line's credentials. An invalid portal line clears the previous portal context, so a following MAC cannot accidentally be attached to that earlier portal.

## What the results mean

| Result | What was checked |
| --- | --- |
| Public page available | A public page was read without account authentication. |
| Portal client recognized | The page matches a supported portal client. This does not prove free access. |
| Catalogue available | The supplied portal/MAC pair returned a valid channel catalogue. Playback is still untested. |
| Refused, timed out or unsupported | The check did not establish access; a responding server is not reported as a working account. |
| Not checked / cancelled | No completed result is available for this entry. |

For an address without a MAC, the scanner cannot authenticate an account. Public page checks use the existing discovery rules; custom API paths or ports may require adding your own portal configuration instead.

Choose a successful entry to add it through the application's protected source storage. A playback action uses the embedded player and its existing video/audio progress checks. Preparing a URL or loading a catalogue is never reported as confirmed playback.

## Public discovery

The public-portal search starts the existing web explorer with a portal-focused query and result filter. Results retain their source and check time. A publicly reachable portal is not necessarily free, and an operator may require a separately supplied account.

## Limits and privacy

Checks run sequentially, with a 30-second budget for each entry and a 10-minute budget for the full scan. Cancellation preserves completed results. Only result metadata is retained while scanning; large catalogues are released between entries. Selected sources are refreshed again before saving. Closing the scanner or changing profiles prevents late callbacks from adding data to the wrong profile.

Starting another scan checks the list from its beginning; there is no resume-unchecked action yet. If a long list repeatedly reaches the time limit, split it into smaller files or put unchecked entries first. On Android, a cloud document provider can delay opening a TXT file before the application receives a readable stream; use a local file if that provider does not respond.

The scanner does not generate MAC addresses, bypass access refusals, fetch credentials from web pages or treat expiry comments as proof of access. Safe result exports exclude MAC addresses, access tokens and raw responses.

The existing catalogue limits still apply: 64 MB decoded response, 30,000 unique retained channels and 8 MB of retained fields. A successful sample does not establish that every channel will play.
