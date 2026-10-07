# Changelog

**English** · [Português (BR)](CHANGELOG.pt-BR.md)

All notable changes to Ferry are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project follows [Semantic Versioning](https://semver.org/): `MAJOR.MINOR.PATCH`, with `-beta.N` while Ferry is in beta.

- **MAJOR**: incompatible change (settings, queue or data format that old versions cannot read).
- **MINOR**: new feature, backward compatible.
- **PATCH**: bug fix only.
- The version lives in one place, [`Directory.Build.props`](Directory.Build.props). Each version is a git tag `vX.Y.Z[-beta.N]`, and its section here becomes the [GitHub Release](../../releases) notes. How to cut one: [Releasing](#releasing).

## [Unreleased]

## [1.6.2-beta.1] - 2026-10-07

### Added

- Loose game folder: drag a game folder (no archive) and Ferry sends it straight from disk, resuming where it stopped.
- **Automatically remove completed items from the queue** (Settings → Transfer, off by default, also in the web interface). A finished item disappears a few seconds later. It stays paused while a PC shutdown is armed, because changing the queue cancels the shutdown.
- Session power options on Windows: shut the PC down when transfers finish and keep it from sleeping during uploads, both only for the current session.
- Optional automatic PKG installation request and DPI port in Settings; PKG preparation for etaHEN.
- New logo (PS5-style ribbon), animated hero and new app icon.

### Changed

- Windows: the power options moved from the fixed top of every screen to **Settings → Power**. The top only shows a banner, with the countdown and a cancel button, while a shutdown is armed.

### Fixed

- Connection drops during APPE no longer disable resume or restart partial files from zero. Only an explicit unsupported-command response triggers a full resend; permission errors preserve the partial file.
- Archive resume shows rereading progress and immediately includes bytes already saved on the PS5. Existing bytes no longer inflate the transfer speed.
- FTP workers close between retries and connect only when there is work, avoiding idle sessions during archive rereading.
- Resuming a partial file from a loose folder no longer reads and discards tens of GB from disk before continuing. It used to look frozen at 0 MB/s with nothing in the log; now it seeks straight to the right offset.

## [1.6.0-beta.1] - 2026-09-30

### Added

- PKG and fPKG: send a loose `.pkg`, or one `.pkg` inside a ZIP/RAR/7z, over FTP for manual installation in etaHEN.
- Optional automatic installation request through etaHEN DPI. The queue records preparation and submission; an uncertain result asks you to check the PS5. Real installs still need validation on a console.

## [1.5.1-beta.1] - 2026-09-30

### Added

- Windows app in Português (Brasil) and English. Automatic follows the Windows display language; the choice is shared with the web interface through `settings.json`.
- System tray: minimizing hides the window while transfers, folder watching and webhooks keep running. Double-click or **Open Ferry** restores it; password requests restore the window before the dialog.

## [1.5.0-beta.3] - 2026-09-30

### Changed

- Webhook tests control HTTP responses and use the production timeout, so webhook contracts are validated against real behavior.

## [1.5.0-beta.2] - 2026-09-30

### Changed

- The end-to-end suite now interrupts the Docker container before completion to validate that a transfer resumes correctly.

## [1.5.0-beta.1] - 2026-09-30

### Added

- Configurable webhooks in the Windows and web apps: Discord, ntfy or generic JSON, fired on completion, error and password request. Optional activation, masked URL and a test button. Setup and contract in [WEBHOOK.md](docs/en/WEBHOOK.md).

## [1.4.0-beta.1] - 2026-09-30

### Added

- Web interface in Português (Brasil) and English, with Automatic, Português or English in Settings. Switching keeps the queue, uploads and open password dialogs.
- All visible text moved into shared `pt-BR` and `en` resource files, including the log, notices and errors.

## [1.3.0-beta.1] - 2026-09-30

### Added

- **Self-hosted Ferry**: the same engine as the Windows app, with a web interface (live queue, settings, log, archive password asked in the browser, chunked upload by dragging files onto the page). Login with user and password.
- Docker image (amd64 and arm64) published to GHCR, plus self-contained Linux binaries (x64 and arm64) in every release.
- README and docs in English, Portuguese and Spanish.

### Changed

- The logic moved out of the WPF app into a portable `Ferry.Core` library, shared by the Windows app, the web server and the tests.
- Game passwords are stored with DPAPI on Windows and AES-GCM on Linux; `FERRY_DATA` changes the data folder.

## [1.2.0-beta.2] - 2026-09-30

### Fixed

- FTP timeout: the PS5's ftpsrv sometimes takes longer than 15 s to answer a `STOR` with 8 connections writing to an external disk. Ferry now waits 60 s and, on a timeout, socket or I/O error, asks the PS5 again and continues with only what is missing (up to 3 times).

## [1.2.0-beta.1] - 2026-09-30

### Added

- Ferry brand and a new interface: Geist font, tabular numbers, a queue ruler with %, MB/s and time remaining, an animated "crossing" progress bar, status colors and a two-column Settings page.
- **Send now**: put a game ahead of the queue; the current upload goes back to the queue and continues from where it stopped.

### Changed

- Renamed from PS5 Sender to Ferry: `Ferry.exe`, namespace, CI, README and docs. Data lives in `%LOCALAPPDATA%\Ferry`, migrated once (copied, not moved) from the old `PS5Sender` folder.

### Fixed

- A pause during the remote check no longer overwrites the item's state.

## [1.1.0-beta.2] - 2026-09-30

### Added

- ShadowMount+ `.exfat` images, loose or inside a `.zip`/`.rar`/`.7z`, sent to a configurable image folder. Published atomically (`.ferry-part` renamed only after everything is verified).

## [1.1.0-beta.1] - 2026-09-30

### Added

- Known passwords list in Settings, tried silently before the password dialog; the one that works is remembered.
- The game password is saved encrypted in the queue and forgotten when the game leaves it.
- Cover and title read from inside the archive (`param.sfo`, `icon0.png`), with the `PPSAxxxxx` id.
- "Waiting for parts" shows which parts are missing.
- Find the PS5 on the network when the saved IP stops answering (ports 2121/1337); it only offers, never switches on its own.
- Windows toast for finished, error and password needed, only when the window is not focused.
- Settings save automatically on every change, with atomic writes; invalid values are never saved.
- Persistent `log.txt` with every `STOR`/`APPE` and `SIZE` command and reply.

### Fixed

- `APPE` only continues a partial file that Ferry itself started; a different-size file already on the console is re-sent whole.
- Size is checked after every `STOR`/`APPE`, and failures show the remote path and "expected X, on PS5 Y".
- Newer ftpsrv enables `SELF` by default, which made `SIZE` of SELF files lie; Ferry now turns it off.
- ShadowMount+ no longer installs a game halfway through an upload: `param.json`/`param.sfo` go up last.

## [1.0.0-beta.1] - 2026-09-30

### Added

- First release, as PS5 Sender: extracts game archives and sends them to the PS5 over FTP, streaming, with nothing written to disk.
- Queue that groups multi-part volumes (`.001`, `.z01`, `.partN.rar`, `.r00`), waits for missing parts and for the file size to settle.
- Parallel connections for small files; resume by `SIZE` (skips what is already on the PS5) and `APPE` when supported; persistent queue.
- A `dec` folder overlays the game; password dialog; size verification before deleting the originals.
- Works with the minimal command set of ftpsrv (no `NLST`/`FEAT`/`EPSV`).
- Dark WPF interface and an end-to-end suite against a server that imitates ftpsrv.
- CI with build, E2E and automatic release by tag on GitHub Actions.

## Releasing

1. While you work, add each change under **Unreleased** in both changelogs (this file and the Portuguese one).
2. To release, pick the next number by the rules at the top and run `tools/release.ps1 -Version 1.7.0-beta.1`. It turns **Unreleased** into that version with today's date, updates `Directory.Build.props` and the links, commits and creates the tag `v1.7.0-beta.1`.
3. `git push --follow-tags`. CI builds, tests and publishes the release with the notes from this file and from [CHANGELOG.pt-BR.md](CHANGELOG.pt-BR.md) (English first, then Portuguese). CI fails if the tag does not match `Directory.Build.props`, or if either changelog has no section for it.

[Unreleased]: https://github.com/bps2414/ferry/compare/v1.6.2-beta.1...HEAD
[1.6.2-beta.1]: https://github.com/bps2414/ferry/compare/v1.6.0-beta.1...v1.6.2-beta.1
[1.6.0-beta.1]: https://github.com/bps2414/ferry/compare/v1.5.1-beta.1...v1.6.0-beta.1
[1.5.1-beta.1]: https://github.com/bps2414/ferry/compare/v1.5.0-beta.3...v1.5.1-beta.1
[1.5.0-beta.3]: https://github.com/bps2414/ferry/compare/v1.5.0-beta.2...v1.5.0-beta.3
[1.5.0-beta.2]: https://github.com/bps2414/ferry/compare/v1.5.0-beta.1...v1.5.0-beta.2
[1.5.0-beta.1]: https://github.com/bps2414/ferry/compare/v1.4.0-beta.1...v1.5.0-beta.1
[1.4.0-beta.1]: https://github.com/bps2414/ferry/compare/v1.3.0-beta.1...v1.4.0-beta.1
[1.3.0-beta.1]: https://github.com/bps2414/ferry/compare/v1.2.0-beta.2...v1.3.0-beta.1
[1.2.0-beta.2]: https://github.com/bps2414/ferry/compare/v1.2.0-beta.1...v1.2.0-beta.2
[1.2.0-beta.1]: https://github.com/bps2414/ferry/compare/v1.1.0-beta.2...v1.2.0-beta.1
[1.1.0-beta.2]: https://github.com/bps2414/ferry/compare/v1.1.0-beta.1...v1.1.0-beta.2
[1.1.0-beta.1]: https://github.com/bps2414/ferry/compare/v1.0.0-beta.1...v1.1.0-beta.1
[1.0.0-beta.1]: https://github.com/bps2414/ferry/releases/tag/v1.0.0-beta.1
