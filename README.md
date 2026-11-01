# bcfetch

Linux-native Bandcamp album downloader with an [Avalonia](https://avaloniaui.net) GUI.

A port of [Otiel/BandcampDownloader](https://github.com/Otiel/BandcampDownloader) (WPF, Windows-only) to .NET + Avalonia. It downloads the 128 kbps MP3 files Bandcamp streams freely on its site. Nothing more.

> **Status:** release 0.1.1. Main window, settings and About page are ported. Linux x64 only; not yet tested on Wayland-only or non-Debian systems.

## Features

* Download from album, track and artist pages.
* ID3 tags (album, artist, title, track number, year, lyrics) and cover art.
* Playlist files: m3u, pls, wpl, zpl.
* Configurable download path template, e.g. `~/Music/{artist}/{album}`.
* Settings window: tags, cover art, playlist, proxy, retries, dark/light theme, language.
* Update notice and an About page with the changelog.

Not ported from the Windows version: taskbar progress, the "finished" sound, and live language switching (restart after changing the language).

## Command line

```
bcfetch [options] <url>...
  -o, --output <path>   download path template, e.g. "~/Music/{artist}/{album}"
  -d, --discography     download the whole artist discography
  -v, --verbose         show verbose log lines
```

Without arguments bcfetch opens the window. Other settings are read from `~/.config/bcfetch/bcfetch.ini`; `-o` and `-d` apply to that run only.

## Layout

* `src/BcFetch.Core` – UI-independent logic (parsing, download, tagging, playlists, settings).
* `src/BcFetch.App` – Avalonia GUI (main window, settings, About).
* `packaging/` – `build.sh` for the `.deb` and tarball, `.desktop` file.
* `tests/BcFetch.Core.Tests` – NUnit tests.

## Install

Download from the [releases page](https://github.com/d3codedvision/bcfetch/releases) and check `SHA256SUMS`:

```sh
sudo apt install ./bcfetch_<version>_amd64.deb      # Debian / Ubuntu / Zorin
# or
chmod +x bcfetch-<version>-x86_64.AppImage && ./bcfetch-<version>-x86_64.AppImage   # any x64 distro, no install
# or
tar -xzf bcfetch-linux-x64.tar.gz && ./bcfetch      # any x64 distro
```

Maintainers: `packaging/build.sh` builds the tarball, `.deb`, AppImage and checksums into `dist/`. The AppImage step downloads a pinned `appimagetool` (checksum-verified) into `~/.cache/bcfetch-build/`. Pushing a `v*` tag runs the same script in CI and publishes the release.

Settings and the log live in `~/.config/bcfetch/`.

## Build

Requires the .NET 10 SDK.

```sh
dotnet build
dotnet test
```

## Credits and license

Based on BandcampDownloader by Otiel, MIT licensed. See [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Known issues

* If the window never opens and one CPU core stays at 100%, stale fontconfig caches are the cause: old `*-le64.cache-9` files in `~/.cache/fontconfig` next to newer ones send SkiaSharp's font manager into an endless loop. Delete them (`rm ~/.cache/fontconfig/*cache-9`); fontconfig recreates what it needs.
