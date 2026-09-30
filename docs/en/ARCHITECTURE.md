# Architecture

**English** · [Português (BR)](../ARQUITETURA.md) · [Español](../es/ARQUITECTURA.md)

.NET 10. The logic lives in `core/` (no UI) and has two faces: the Windows app (WPF, single `.exe`) and the self-hosted web server (Docker or Linux binary).

| File | Role |
|---|---|
| `core/Engine.cs` | Queue: scans inputs, groups parts, decides when to start, processes one game at a time, saves the queue |
| `core/Archives.cs` | 7-Zip: group volumes, list, test password, open the `7z x -so` stream, find the game folder and `dec` (or the `.exfat` image) |
| `core/Ftp.cs` | FTP: remote state (`SIZE`), streaming upload with parallel connections, verification |
| `core/Job.cs` | One queue item (state, progress, speed, ETA) |
| `core/Settings.cs` | Settings in `settings.json` in the data folder (`%LOCALAPPDATA%\Ferry`, or `FERRY_DATA`) |
| `core/Webhooks.cs` | Shared HTTP notifications: bounded in-memory queue, immutable snapshots, localization and test; wired into Done/askPassword callbacks without changing FTP |
| `core/Secret.cs` | Game password stored in the queue: DPAPI on Windows, AES-GCM with `secret.key` (600) elsewhere |
| `core/Discovery.cs` | Finds the PS5 on the network (/24 of each interface, ports 2121 and 1337) |
| `app/MainWindow.xaml(.cs)` | Windows interface; `App.xaml.cs` extracts the embedded `7z.exe` and passes its path to the core |
| `web/Program.cs` | Web server: API, login cookie, per-field settings, SSE, embedded interface |
| `web/Hub.cs` | Engine ↔ browser bridge: queue snapshot, log, notices, password asked in the browser |
| `web/Auth.cs` | One user, created on first open (`auth.json`, PBKDF2-SHA512); login attempt limit |
| `web/Uploads.cs` | Chunked upload with resume |
| `web/ui/` | Web interface (plain HTML/CSS/JS), embedded in the binary |

## Self-hosted (web)

- **7-Zip**: the core looks for `7zz`/`7z` next to the program and on PATH. In the Docker image it's Ubuntu's `7zip` + `7zip-rar`; the Linux `.tar.gz` ships the official static `7zzs` next to `ferry`.
- **Live progress**: `GET /api/events` (SSE). Every 250 ms the server builds a queue snapshot (`Hub.Snapshot`) and sends it only if it changed; it also sends new log lines and notices. Commands (pause, resume, transfer now…) go through `POST`.
- **Archive password**: the Engine's `askPassword` becomes a pending request (`TaskCompletionSource`) that shows up in the snapshot; the browser opens the dialog and answers at `POST /api/jobs/{id}/password`. Pausing, cancelling or removing the game ends the request.
- **Login**: `HttpOnly`/`SameSite=Strict` cookie; the cookie keys live in `/data/keys`, so the login survives a container restart. The whole API (except `/api/auth/*`) requires login.
- **Upload**: `POST /api/uploads` (name, size, date) returns a stable id and the byte to continue from; `PUT /api/uploads/{id}?offset=N` appends a chunk (offset different from the server's → `409` with the right offset). The partial file stays in `<folder>/.ferry-upload/` — the Engine only looks at the folder root — and only gets its final name when complete, so a half-uploaded `.exfat` never enters the queue.
- **Restart**: what was sent and verified is kept in `queue.json` (`Done`); on reopen, a game whose parts are still in the folder comes back as finished instead of being queued again. Adding the files again (drag, picker, upload) clears that mark.

## Flow of a game

```
files in the folder / picker / drag and drop
        │
        ▼
Group volumes by base name ──► parts missing? ── "Waiting for parts"
        │ names complete
        ▼
Size stable for N s? ──► 7z l (checks volumes) ──► "Queued"
        │
        ▼
7z l -slt  → item list (path, size, encrypted)
password?  → 7z t on the 1st encrypted item (cheap) → dialog until correct
Plan()     → destination of each item: game folder, dec on top, shells dropped
        │
        ▼
SIZE of each file on the PS5 → "need" list (only what is missing)
        │
        ▼
7z x -so @list  ──stdout──► single reader
                                ├─ file ≤ 4 MB → RAM → channel → N-1 parallel connections
                                └─ file > 4 MB → streamed directly on the main connection
        │
        ▼
SIZE again → verify everything → "Finished" → (optional) delete the original parts
```

## Why streaming

Extracting to disk and then sending needs free space equal to the extracted game (tens of GB). With `7z x -so`, 7-Zip writes all files concatenated to stdout, **in listing order** (`7z l`). Since the sizes come from the listing, the app cuts the stream into files and sends each one straight to an FTP socket. Nothing extracted touches the disk, and the pipe gives natural backpressure: 7-Zip only moves on when FTP consumes.

With `@list` (a file with the paths, `-scsUTF-8 -spd`), 7-Zip extracts **only** the items still missing on the PS5 — that is what allows pausing, closing and reopening without re-sending anything complete.

## Volume grouping

Regex on the file name (`Archives.Group`):

| Pattern | Key | File 7-Zip opens | 1st index |
|---|---|---|---|
| `X.(zip|7z|rar).NNN` | `X.ext` | `.001` | 1 |
| `X.partN.rar` | `X.rar` | `part1` | 1 |
| `X.zNN` + `X.zip` | `X.zip` | `X.zip` | 1 |
| `X.rNN` + `X.rar` | `X.rar` | `X.rar` | 0 |

"Complete by name" = contiguous indexes (and the main file present). Even so, the last volume can be missing without a gap in the numbering (e.g. `.001`–`.004` of 5), so the app still runs `7z l` and only queues if there is no "Missing volume / Unexpected end". After `7z l`, it re-checks the signature (name+size+date) of the parts: if a volume arrived during `7z l`, the stability window starts over.

## Game folder and `dec`

`FindGameRoot`: the shallowest folder with `EBOOT.BIN` or `sce_sys/param.sfo`, ignoring folders named `dec` (which also have `EBOOT.BIN`). `Plan`: if there is a `dec/` next to the game folder (or `dec/<game folder>/`), each `dec` file becomes a destination and the game file with the same path is dropped.

## `.exfat` image (ShadowMount+)

- Loose `.exfat`: `Archives.Group` treats it as a single file (`X.exfat`). The `Engine` skips `7z l` and builds a one-item list. The upload reads a `FileStream` from disk instead of 7z's stdout. When resuming with `APPE`, the part already sent is skipped with `Seek`, without reading it.
- Inside an archive: if `Plan` finds no game folder, `Archives.ImagePlan` picks the `.exfat` items (by file name, ignoring inner folders) and drops the rest. From there it is the same `7z x -so` stream.
- Destination `Settings.ImageDir`. The image goes into `Engine.Held`, so it is uploaded as `.ferry-part` and renamed at the end, like `param.json`.

## Interface

- Progress comes from many threads every few KB; `Job.Report` limits it to 4 updates/s (otherwise the WPF dispatcher drowns and the screen freezes).
- `BindingOperations.EnableCollectionSynchronization` lets the queue be changed from background threads.
- Look (Phase 2, "Ferry"): tokens and styles in `App.xaml`; embedded Geist font (`app/fonts`, `pack://application:,,,/Ferry;component/fonts/#Geist`), tabular numbers across the window. The progress bar is the logo's "crossing" (piers at the ends, arrow at the progress tip); `OnProgress` animates the value to the new one in 350 ms, and the color changes by state with `ColorAnimation` in the `DataTrigger`s. The icon (`Ferry.ico`) uses the same 16×16 geometry as `LogoPosts`/`LogoArrow`.
- **Transfer now** (`Engine.SendNow`): moves the game to the top and the one that was sending right behind it; that one goes back to `NaFila` (queued) and has its `Cts` cancelled. `RunAsync` takes the first `NaFila` in the list; the interrupted one later sends only what is missing (same resume as pause).
- Queue persisted in `queue.json` in the data folder (added files, removed items, encrypted passwords, started uploads and finished games).

### Windows language and tray

`app/WpfText.cs` adds web UI catalogs and `app/locales/{pt-BR,en}.json` to core `Localization`. Bindings and job converters react to language changes without replacing controls or jobs: invalid edited fields, validation and typed passwords survive. Structured messages render in the current locale; raw diagnostics and existing persisted logs are not rewritten.

`Settings.Language` (`auto`, `pt-BR`, `en`) persists in the same settings as the web. Automatic captures the original Windows display language (Portuguese → pt-BR; others → en); the web uses the browser. Windows also saves `Settings.WebhookAutoLocale` when changing language or configuring the webhook; the web saves its browser locale there. Independent processes editing the same file do not synchronize live.

Minimize hides the existing window; the tray restores its last normal/maximized state. Password requests restore before opening the modal. Close or Exit cancels the app and disposes its icon.
