# Changelog

## Unreleased

* Command-line mode: `bcfetch <url>...` downloads without opening a window. Options `-o/--output`, `-d/--discography`, `-v/--verbose`; exit code 0 on success, 1 on errors, 130 when cancelled. Run without arguments to open the window as before.

## 0.1.1

* UI restyle: teal accent on cool grey, Outfit Gothic and JetBrains Mono, progress line under the header, settings tabs on the left, discography as a switch. The never-implemented "notification sounds" setting is removed.

## 0.1.0

* Linux port of [BandcampDownloader](https://github.com/Otiel/BandcampDownloader) using Avalonia.
* Core split out into a UI-independent library.
* Main window, settings window and About page (embedded changelog) ported.
* Packaging: .deb and tar.gz with checksums.
