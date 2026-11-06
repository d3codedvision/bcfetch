# Changelog

## 0.4.2

Usability fixes for the main window.

* "Saving to" is a real button now: reachable with Tab, announced by screen readers, opens Settings on the Downloads tab.
* Download is disabled until there is a link to download (Retry failed stays available after a run with failures), so a click can no longer fail silently in a tab you are not looking at.
* The first-steps hint is in the Tracks tab, which is the first tab and the one shown at start. Step 2 now says to check the destination and change it in Settings.
* During a run Cancel takes the place of Download, and a hint says that links dropped on the window are queued.
* Clear turns into Undo for 5 seconds, so pasted links are not lost by one click. Undo brings back the links only; the track list and log stay cleared.

## 0.4.1

Fixes and polish for the 0.4.0 main window.

* The track list follows the download: it scrolls to the newest track in progress, and only moves when that track changes, so scrolling by hand still works.
* Clicking "Saving to" opens Settings on the Downloads tab.
* Settings, Downloads: the "Save albums to" box now has the full width (label above, Browse beside it) instead of overflowing the window.
* The Tracks tab is always shown, not only after a run starts.
* The links box (and Download) is disabled during a run. Links dropped on the window are still queued.
* New "Clear" button next to the "Saving to" line: empties the links box, the track list and the log. Disabled during a run.

## 0.4.0

Where files go is now a setting, and the track list and the log have a tab each.

* Settings, Downloads: "Save albums to" (with Browse) lives here now. A new option, "Ask for a folder before each download", opens the folder picker when you press Download. The folder you pick applies to that download only (queued batches included); the saved folder stays the default. Cancelling the picker cancels the download.
* Main window: the folder field is gone. A line next to the discography switch shows where files will go; click it to open Settings.
* Main window: Tracks and Log are tabs, so each gets the full height. Tracks is shown while a run is active (and after it, with the result strip); the Log tab title counts errors, for example "Log (2 errors)".
* New texts are in English and German; other languages show English for them until translated.

## 0.3.9

Main window: clearer idle, running and finished states.

* Idle: the empty log area now shows three short steps (paste links, choose a folder, press Download), the link pattern and the discography hint, and the URL box placeholder is one line.
* Running: the URL box is emptied when a run starts ("Paste more links to add them to the queue") so pressing "Add to queue" no longer queues the same links again; the links come back when the run ends. "Add to queue" is a quiet button, and Cancel appears only while a run is active.
* Running: one readout shows tracks done, bytes, speed (MB/s above 1 MB/s) and the time left once the total size is known, for example "2 of 6 tracks · 11.3 / 24.0 MB · 812 kB/s · 16 s left".
* Finished: failed tracks move to the top of the list and a strip above it shows "4 done · 2 skipped · 1 failed".
* Success is green in the log as well as in the track list; the accent colour is for actions and progress. The app icon is shown next to the name.
* The readout in the footer gives way to the buttons when the window is narrow (a long "Retry failed" label was cut off in German).

## 0.3.8

Keyboard and screen-reader pass, and a correction to 0.3.7.

* Tab follows the screen: About, Settings, URL box, folder field, Browse, discography switch, track list, log, then Cancel/Download. Before, Download came right after Settings.
* The track list and the log can be scrolled with the keyboard (arrow keys, Page Up/Down, Home, End) and show an accent border while they have focus.
* A run's end is announced to screen readers ("Downloads finished", or how many tracks failed). Not tested with a real screen reader.
* The window's minimum height is 520 (was 480) and the log keeps at least four lines at that size. The Settings window can be resized (minimum 720 x 440); its content scrolls.
* The URL box help text is now really readable. 0.3.7 changed its colour, but the TextBox template still faded the text to 50%, so it stayed at about 2:1; it now measures 5.4:1 in the light theme and 6.5:1 in the dark theme.

## 0.3.7

Interface audit fixes.

* The window works at its minimum size: the URL box, track list and log share the available height, so the Download/Cancel buttons and progress readouts are no longer pushed out of view. The minimum height is now 480.
* Settings labels wrap instead of being cut off ("Max gleichzeitige Album Do" is now complete), and an invalid value gets a red border plus a sentence under the field, instead of a faint tint with an unexplained disabled Save button.
* The track list is virtualized: 500 tracks build in about 0.1 s instead of 1.8 s and update 30 times faster.
* The log in the window keeps the latest 500 lines, so long runs stay responsive (3,000 lines took 11 s, 3 s now and no longer growing); the full history is in the log file.
* Contrast: the URL box help text is readable (2:1 became 4.9:1), the light-theme accent and warning colours pass 4.5:1, and input borders are visible (3:1 or more).
* Accessibility: the URL box, folder button, progress bars, track list and log have accessible names, and the "Save albums to" label is tied to its field (its access key now works).
* Status and log colours come from the theme tokens (no more duplicated hex values); the Settings info line follows the theme.
* The URL box wraps its text, which removes a stray scrollbar line across it.

## 0.3.6

* Optional download log: Settings > Downloads > "Write a log file in each album folder" (off by default) adds `bcfetch-download.log` to every album folder. Each run appends which tracks were downloaded, skipped or failed (with the reason), a one-line result, and a line when a finished album is skipped. It works from the window and the command line.
* A track whose download delivers no file (for example an HTTP 500 from the server) now counts as failed. Before, with tag writing off, it was reported as downloaded and only a half-written `.download` file remained.

## 0.3.5

* Drag and drop now works: links, text and text files dropped on the window from a browser or file manager are added to the URL box (or the queue during a run). In 0.3.0-0.3.4 it did nothing. Two causes: the handlers were registered for the wrong event route, and Avalonia 11's X11 backend cannot receive drops from other programs at all.
* Updated to Avalonia 12.1.4 and SkiaSharp 3.119.4 (needed for drops from other programs). Cover art resizing uses the new SkiaSharp sampling API, with new tests for resize, no-upscale and JPEG conversion.
* The main window shows the version next to the app name.

## 0.3.4

* Software centres now match the installed app to its AppStream metadata: the desktop entry is renamed to `io.github.d3codedvision.bcfetch.desktop` (same as the AppStream id; `StartupWMClass=bcfetch` keeps the window grouped under its icon) and the metadata names the package. The generated metainfo no longer repeats the release notes inside a comment.

## 0.3.3

* Packages carry AppStream metadata, so software centres show the license (MIT), a full description, release notes taken from this changelog, the project and bug-tracker links and an age rating instead of "Unknown license" and "No details for this release". The `.deb` also gets a homepage, section, a longer description and a copyright file; the desktop entry gets keywords and a generic name. The AppImage embeds the same metadata.

## 0.3.2

* Greek (Ελληνικά) added to the languages: a full translation of the window, settings, dialogs, tooltips and messages. Like the other new translations it has not been reviewed by a native speaker, so corrections are welcome.
* The tab names in Settings (General, Downloads, Tags, ...) now follow the selected language; they were English in every language.
* Translations for the strings added since 0.2.0: the queue and retry buttons, log and notification messages, tray menu, track statuses and the settings for tray icon, finish notification and skipping finished albums. Covered: ar, ca, de, eo, es, fi, fr, hr, hu, id, it, ja, ko, nb-NO, nl, pl, pt, pt-BR, ro, ru, sv, tr, uk, vi, zh (arz stays English; it has only a few strings). These are my own translations and have not been reviewed by native speakers, so corrections are welcome. The command line and the Core log messages stay in English.

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
