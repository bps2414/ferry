# E2E tests

## Localization contracts and browser languages

Run `dotnet run --project e2e -- --localization` for legacy automatic settings, persistence, locale variants/fallback, nested messages, raw diagnostics, extraction error codes, progress/actions and matching keys/arguments across core, backend and UI catalogs. This focused mode needs no FTP or generated game files; CI runs it on Windows and Linux.

`node e2e/web/web.mjs` runs the full browser flow serially in isolated `pt-BR` and `en` data/processes, preserving hashes, authentication, pause/resume and restart checks. Reports are `e2e_report_web_pt-BR.md` and `e2e_report_web_en.md`, aggregated into `e2e_report_web.md`; screenshots use `e2e/web/report/<locale>/`. Set `FERRY_TEST_LOCALE=pt-BR` or `en` for one run. Expected visible text is explicit for each language. Automatic language order/fallback, explicit precedence, invalid preferences, persistence, and switching during upload/transfer are covered. The open-dialog switch dispatches the settings event via DOM and verifies modal/input preservation; it does not simulate a pointer click behind the modal.

Without Docker, build `dotnet build web -c Release`, install `e2e/web` dependencies and Chromium, then set `FERRY_WEB_MODE=local` before `node web.mjs`. This spawns/restarts the local DLL using isolated temporary settings/games and an exclusive port; it does not validate the Docker image. Optional `FERRY_WEB_DLL`, `FERRY_PYTHON`, `FERRY_7ZIP` override tool paths. Windows uses bundled `app/tools/7z.exe` and `python`; Linux resolves `7zz`/`7z` and `python3`. Python requires `pyftpdlib`.

**English** · [Português (BR)](../TESTES.md) · [Español](../es/PRUEBAS.md)

Two end-to-end tests:

- `e2e/` (C#), no interface: uses the same `Engine` (Ferry.Core) against a local FTP server. Runs on **Windows and Linux** (CI runs both).
- `e2e/web/` (Node + Playwright): starts the **Docker image** and drives the web interface in a real Chromium (see [Web E2E](#web-e2e-docker) below).

## Running

Requires Python 3 with `pyftpdlib` (`pip install pyftpdlib`). On Linux, also 7-Zip with RAR on PATH (`apt install 7zip 7zip-rar`).

```bash
dotnet run --project e2e              # full run (~40 s)
dotnet run --project e2e -- G2 G7     # quick mode: only these cases, no extra phases (~15 s)
```

Generates `e2e_report.md` at the repo root and exits with code 0 (passed) or 1 (failed). On the 1st run it downloads the official RAR 6.24 from rarlab.com into `e2e/tools/` (Windows: `Rar.exe` from the installer; Linux: `rar` from rarlinux), used only to **create** the test archives; the app only extracts, with 7-Zip. On Windows, 7-Zip is the app's embedded `app/tools/7z.exe`; on Linux, the system `7zz`/`7z`.

## The test server imitates ftpsrv

`e2e/ftpserver.py` (pyftpdlib) accepts **only ftpsrv's commands** and answers `502 Command not recognized` to the rest — that's how the `NLST`, `SIZE` and UTF-8 problems a full server was hiding showed up. Refused commands are logged in `%TEMP%\ferry-e2e\ftproot.recusados.txt`. Upload limited to 40 MB/s per connection (10 MB/s in the .exfat and "Transfer now" cases, which need to catch the upload mid-way).

## Cases

Each fake game has 6 files (~24 MB incompressible, a file name with an accent, an empty file) inside 2 shell folders, in 5 MB volumes. They are deterministic and cached (`%TEMP%\ferry-e2e-cache-v3`; each has a real `param.sfo`, `param.json` and a real `icon0.png`; change `GenVersion` when changing the generator).

| Case | Format |
|---|---|
| G1 | `.zip.001…` (7-Zip) |
| G2 | `.z01…` + `.zip` (own PKWARE split-zip generator) |
| G3 | `.part1.rar…` (RAR5) |
| G4 | `.rar` + `.r00…` (RAR4, old names) |
| G5 | `.7z.001…` |
| G6 | `.7z.001…` with password and encrypted headers, added by drag and drop; opens with the 2nd known password, no dialog |
| G7 | single `.rar` with `PPSA…-app0` + `dec` (`dec` comes first in the archive) |

For each case: all parts but the last → checks it stays "Waiting for parts"; the last one arrives written slowly (simulates a download) → checks the SHA-256 of **every** received file, no extra files, originals deleted.

## Extra phases (full run)

In every case, the E2E also checks cover/title (`param.sfo` + `icon0.png`) and "missing <volume>" (rar and split zip). It also checks atomic publishing: by the order of the server log, the `RNTO` of `param.json`/`param.sfo` comes after the game's last `STOR`/`APPE`.

- **Already on the PS5, no APPE**: a complete `EBOOT.BIN` is neither extracted nor re-sent; a half-sent `big.bin`, not started by the app, is re-sent in full with `STOR`.
- **Already on the PS5, with APPE and SELF** (imitates the new ftpsrv, whose `SIZE` of eboot.bin lies with `SELF` on):
  - half-sent `big.bin` recorded in the queue: only the missing half goes with `APPE`;
  - a smaller `EBOOT.BIN` of another version: full `STOR`;
  - a larger `icon0.png` on the PS5: ends up the right size.
- **Game already installed**: warns without sending; "Try again" re-sends over it.
- **`.exfat` image (ShadowMount+)**: server with APPE, `ImageDir = /data/homebrew`. The files (a loose 40 MB `IMG1.exfat` and a 12 MB `IMG2.exfat` inside `.part1.rar`) have deterministic random bytes and are cached.
  - new upload: same hash, no cover, no leftover `.ferry-part`, and by the order of the server log the `RNTO` to the final name comes after the last `STOR`/`APPE`. Pausing and resuming mid-way continues with `APPE`;
  - reopen: the partial recorded in the queue continues with `APPE` (only the missing half) and a smaller partial of another version goes in full with `STOR`;
  - already present: warns "Game already installed…" without sending, and "Try again" re-sends over it.
- **Transfer now**: with an 80 MB image (generated on the fly) uploading, `SendNow` on IMG2 (`.part1.rar`) → the image goes back to `NaFila` (doesn't pause), IMG2 becomes `Verificado` first, and the image continues with only what was missing (`STOR` + `APPE` = full size).
- **Persistent log**: `log.txt` has the command and reply of STOR/APPE/SIZE.
- **Password learned and remembered** (G6, in the close/reopen phase):
  - dialog with a wrong 1st password;
  - the right one joins the known passwords;
  - on reopen without known passwords, it opens with the password from the queue (encrypted: DPAPI on Windows, AES-GCM on Linux), no dialog.
- **Atomic save**: a half-written `queue.json.tmp` doesn't get in the way of reopening.
- **Close and reopen**: stops the engine mid-upload, creates another with the same `queue.json`; the queue comes back on its own and nothing complete is re-sent.
- **Pause/resume** mid-stream; **remove from queue** and re-add; **test connection** with right and wrong password.

## Webhook contracts

`dotnet run --project e2e -- --webhook` uses a fake local HTTP server without FTP or real credentials: legacy configuration, validation/persistence, HTTP 401/429/500/302, timeout, connection refusal, three formats, pt-BR/en/Automatic, Windows fallback, limits, secrets, queue capacity, snapshots and shutdown. CI runs it on Windows and Linux. Web E2E also checks actual game/FTP fixtures, events, no duplicate password notification, waiting for saved edits, all providers, language after restart, closed browser and failed HTTP without affecting FTP hashes.

Tests never contact real notification providers. Web reports distinguish local DLL from Docker; local execution does not prove remote CI. See [WEBHOOK.md](WEBHOOK.md) for setup and delivery.

## Web E2E (Docker)

`e2e/web/web.mjs` starts the image with `--network host` and `--user <your uid>` (as in `docker-compose.yml`), the same `ftpserver.py` (with APPE), and opens the page in Chromium (Playwright). It generates `e2e_report_web.md` at the repo root and screenshots in `e2e/web/report/`.

```bash
docker build -t ferry:e2e .
cd e2e/web && npm ci && npx playwright install chromium && node web.mjs
```

Requires Docker, Python with `pyftpdlib` and 7-Zip (to build the test archives). What it checks:

- **Login**: without login the API answers 401; the first open asks to create the user (a short password is refused); log out → 401; wrong password → message; right → in.
- **Settings**: each field saves on its own; an invalid port shows the error and isn't saved; the watched folder already comes as `/games`; "Test connection" and the PS5 card go "online" (an old test doesn't overwrite the new one).
- **Upload + password in the browser**: the volumes of a `.7z.001…` with password and encrypted headers are uploaded through the page; the password dialog opens in the browser (1st wrong → "Incorrect password", 2nd right); SHA-256 of each file on the fake PS5, cover and `PPSA…`, password learned.
- **Watched folder + pause/resume** from the page (progress freezes) and **restarting the container** while uploading a 400 MB `.exfat` image: it comes back on its own, continues with `APPE`, the login is still valid, and the already-finished game comes back as finished without re-sending anything.
- **Resumable upload**: 1st chunk sent through the API, repeated chunk → 409, the partial doesn't enter the queue, and the page continues from byte 16 MB; the hash matches on the server and on the PS5.
- **Phone** (390 px): no horizontal scroll; **no JavaScript errors** on the page.
