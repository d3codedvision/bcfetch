# bcfetch

Linux-native Bandcamp album downloader with an [Avalonia](https://avaloniaui.net) GUI.

A port of [Otiel/BandcampDownloader](https://github.com/Otiel/BandcampDownloader) (WPF, Windows-only) to .NET + Avalonia. It downloads the 128 kbps MP3 files Bandcamp streams freely on its site. Nothing more.

> **Status:** work in progress. Core split first, GUI port next.

## Features (target)

* Download from album, track and artist pages.
* ID3 tags (album, artist, title, track number, year, lyrics) and cover art.
* Playlist files: m3u, pls, wpl, zpl.
* Configurable download path template, e.g. `~/Music/{artist}/{album}`.

## Layout (planned)

* `src/BcFetch.Core` – UI-independent logic (parsing, download, tagging, playlists, settings).
* `src/BcFetch.App` – Avalonia GUI.
* `tests/BcFetch.Core.Tests` – NUnit tests.

## Build

Requires the .NET 10 SDK.

```sh
dotnet build
dotnet test
```

## Credits and license

Based on BandcampDownloader by Otiel, MIT licensed. See [LICENSE](LICENSE).

## Known issues

* If the window never opens and one CPU core stays at 100%, stale fontconfig caches are the cause: old `*-le64.cache-9` files in `~/.cache/fontconfig` next to newer ones send SkiaSharp's font manager into an endless loop. Delete them (`rm ~/.cache/fontconfig/*cache-9`); fontconfig recreates what it needs.
