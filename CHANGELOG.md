# Changelog

## 0.3.1

* Long names: the Windows limits (folder path cut at 247 characters, file names squeezed to 11 characters past 260) are gone. Each folder and file name is now shortened only when it exceeds the Linux limit of 255 bytes, cut at a character boundary and with the extension kept. Previously two albums with long names could end up in the same folder.
* Live language switch: saving a new language in Settings applies it at once. The main window is rebuilt in the new language and keeps its URLs, size and position; Settings, About and dialogs follow immediately. During a run it waits until the run ends, and it also waits while "Retry failed" is pending, because that state lives in the window.
* Log colours follow a theme switch: lines already in the log are recoloured instead of keeping the previous theme's palette.
## 0.3.0

* Per-track progress list in the window: each track shows waiting, a live percentage, done, skipped or failed, with its own progress line. The window is a little taller to make room.
* Download queue: while a run is active the Start button becomes "Add to queue" and the URL box stays editable. Queued URLs run as a new batch after the current one; Stop clears the queue. "Retry failed" now re-runs every batch that had failures, even after the box was cleared.
* Command line: `-f/--file <path>` reads URLs from a file (or `-` for stdin), one per line; blank lines and `#` comments are skipped.
* Albums that an earlier run finished are skipped before any size lookups or downloads. A finished album gets a hidden `.bcfetch-done` file in its folder; deleting a track or that file downloads the album again. Turn it off in Settings > Downloads, or pass `--force` on the command line. The album summary now reports success when every track is on disk, including tracks that were already there.

## 0.2.1

* The AppImage runtime used by the build is now pinned and checksum-verified. The 0.2.0 AppImage was packed with an unpinned runtime; use 0.2.1.

## 0.2.0

* Command-line mode: `bcfetch <url>...` downloads without opening a window. Options `-o/--output`, `-d/--discography`, `-v/--verbose`; exit code 0 on success, 1 on errors, 130 when cancelled. Run without arguments to open the window as before.
* Desktop notification when all downloads finish (via `notify-send`; the `.deb` recommends `libnotify-bin`). Turn it off in Settings > General.
* Failed tracks are counted. When a run ends with failures, the window logs the count and the Start button becomes "Retry failed"; the command line prints the count and exits 1. Retrying re-runs the same URLs and skips files that already finished.
* Network errors other than HTTP 429 (timeouts, dropped connections, server errors) and tag-saving failures on a corrupt download now count as a failed try instead of aborting the whole run. Cover art downloads get the same handling.
* AppImage release asset (`bcfetch-<version>-x86_64.AppImage`), listed in `SHA256SUMS`.

## 0.1.1

* UI restyle: teal accent on cool grey, Outfit Gothic and JetBrains Mono, progress line under the header, settings tabs on the left, discography as a switch. The never-implemented "notification sounds" setting is removed.

## 0.1.0

* Linux port of [BandcampDownloader](https://github.com/Otiel/BandcampDownloader) using Avalonia.
* Core split out into a UI-independent library.
* Main window, settings window and About page (embedded changelog) ported.
* Packaging: .deb and tar.gz with checksums.
