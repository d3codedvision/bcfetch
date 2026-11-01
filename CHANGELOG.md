# Changelog

## Unreleased

* Command-line mode: `bcfetch <url>...` downloads without opening a window. Options `-o/--output`, `-d/--discography`, `-v/--verbose`; exit code 0 on success, 1 on errors, 130 when cancelled. Run without arguments to open the window as before.
* Desktop notification when all downloads finish (via `notify-send`; the `.deb` recommends `libnotify-bin`). Turn it off in Settings > General.
* Failed tracks are counted. When a run ends with failures, the window logs the count and the Start button becomes "Retry failed"; the command line prints the count and exits 1. Retrying re-runs the same URLs and skips files that already finished.
* Network errors other than HTTP 429 (timeouts, dropped connections, server errors) and tag-saving failures on a corrupt download now count as a failed try instead of aborting the whole run. Cover art downloads get the same handling.

## 0.1.1

* UI restyle: teal accent on cool grey, Outfit Gothic and JetBrains Mono, progress line under the header, settings tabs on the left, discography as a switch. The never-implemented "notification sounds" setting is removed.

## 0.1.0

* Linux port of [BandcampDownloader](https://github.com/Otiel/BandcampDownloader) using Avalonia.
* Core split out into a UI-independent library.
* Main window, settings window and About page (embedded changelog) ported.
* Packaging: .deb and tar.gz with checksums.
