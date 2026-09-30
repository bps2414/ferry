# Roadmap

**English** · [Português (BR)](../ROADMAP.md) · [Español](../es/ROADMAP.md)

Decided on 2026-09-30. Order: logic → UI/brand → self-hosted (Docker) → English → webhook → PKG/fPKG → progressive upload → payload.

Status: Phases 1, 1.5, 2 and 3 done (3 shipped in release `v1.3.0-beta.1`). **Next: Phase 4 (English).**

## Phase 1 — Logic (release `v1.1.0-beta.1`)

- **Auto-save**:
  - each field saves when it changes, and an invalid value isn't saved (red border + hint; validates port and folder, the host accepts a hostname);
  - atomic write (`.tmp` + `File.Replace`) of `settings.json` and `queue.json`;
  - no Save button. The current layout stays until Phase 2.
- **Known passwords**:
  - editable list in Settings, one per line, in plain text (they're public passwords from sites);
  - before the dialog, the app silently tries each one (`PasswordOkAsync`);
  - the password that works in the dialog is added to the end of the list automatically.
- **Game password saved**: stored in `queue.json` with DPAPI and deleted when the game leaves the queue. Doesn't ask again on reopen.
- **Cover and title**:
  - reads `sce_sys/param.sfo` and `icon0.png` from inside the archive;
  - the card shows the cover, the title and the `PPSAxxxxx`;
  - without a readable cover, it shows the file name in large type, no fake icon.
- **Waiting for parts**: shows which parts are missing ("missing part4, part5").
- **Find the PS5 on the network**: on open, if the saved IP doesn't answer, scans the subnet (2121/1337) and offers the IP found. There is also a "Search" button. Never changes the IP by itself.
- **Windows toast**: for finished, error and "needs password", only when the window is not focused.
- **CI**: actions updated to Node 24.
- **E2E**:
  - known password (G6 with no dialog) and learned password;
  - password remembered on reopen;
  - atomic save;
  - cover/title (fake games get `param.sfo` + `icon0.png`).
- **Releases**: delete the old stable `v1.0.0` release.
- **Added during real use (PPSA11386)**:
  - persistent log in `log.txt`;
  - APPE only on partials started by the app;
  - SIZE checked after each upload, with `SELF` off;
  - atomic publishing of `param.json`/`param.sfo`;
  - game already installed warning (see [FTP-PS5.md](FTP-PS5.md)).

## Phase 1.5 — ShadowMount+ `.exfat` image (release `v1.1.0-beta.2`)

- **Accepted as a game**:
  - loose `.exfat` (drag, picker, watched folder), streamed directly from disk;
  - archive (any accepted split) with an `.exfat` image inside instead of a game folder, streamed `7z -so` → FTP.
- **Destination**: "Imagens .exfat (ShadowMount+)" field in Settings (`ImageDir`, default `/mnt/ext1/homebrew`). Saves on its own and validates (path starting with `/`). The remote name is the `.exfat`'s name.
- **Atomic publishing**: uploaded as `<name>.exfat.ferry-part` and only renamed after `SIZE` is verified. SM+ recognizes images by extension only, and the suffix keeps it out (research in [FTP-PS5.md](FTP-PS5.md)).
- **Resume, already-installed warning, log**: same rules as Phase 1.
- **Card**: no cover (the app doesn't open the image), shows the name in large type.
- **E2E**: loose `.exfat` and inside `.part1.rar`, pause/resume, reopen with our partial (APPE) and another version's (STOR), already present + "Try again".

## Phase 2 — Brand and interface: **Ferry** (release `v1.2.0-beta.1`)

- **Decided in the mockups**: "Ruler" queue (rows separated by hairlines, %, MB/s and remaining in fixed columns, bar = crossing), Settings in two columns, "Crossing" logo (two piers and the arrow), Geist font, blue accent `#6F97FF`.
- **Added in use**: "Transfer now" on a queued/paused game; the current upload goes back to the queue and continues later.

- **Name**: Ferry, with the subtitle "send games to your PS5".
  - Repo `ps5-sender` → `ferry` (GitHub redirects the old links) and exe `Ferry.exe`.
  - Data moves to `%LOCALAPPDATA%\Ferry`, migrating from `PS5Sender`.
  - No Sony symbols (△○✕□, PS logo).
- **Direction**:
  - editorial/typographic "PlayStation noir": blue-black, almost monochrome, one accent;
  - large numbers (%, MB/s, ETA), game title prominent, rigid grid;
  - **forbidden**: glow, purple-blue gradient, frosted glass, AI-template look.
- **Tools**: `/frontend-design` + `/impeccable`, producing mockups before implementing.
- **Settings without scrolling**: the layout is decided in this session.

## Phase 3 — Self-hosted on Docker (release `v1.3.0-beta.1`) — done

- **Goal**: Ferry runs on the home server inside a container, and from the PC you just open `http://<server-ip>:<port>` in the browser.
- **How**:
  - the app today is WPF (`net10.0-windows`) and doesn't run in a Linux container. The logic (`Engine`, `Ftp`, `Archives`, `Job`, `Settings`) becomes a `net10.0` library without WPF;
  - an ASP.NET Core server serves the web interface (same "Ruler" and Settings from Phase 2) and sends live progress (SignalR or SSE);
  - the port comes from an environment variable, with a fixed default.
- **What changes by not being Windows**:
  - DPAPI (game password) → key generated in the `/data` volume;
  - Windows toast → notice inside the page, and system notification when the browser allows it (only on https or localhost; the Phase 5 webhook covers the rest);
  - `%LOCALAPPDATA%\Ferry` → `/data` volume (`settings.json`, `queue.json`, `log.txt`);
  - `7z.exe`/`7z.dll` → Linux 7-Zip: Ubuntu's `7zip` + `7zip-rar` in the image, official `7zzs` next to the Linux binary;
  - drag and drop and the file picker → both paths: watched folder in the `/games` volume and upload through the browser (drag onto the page).
- **Network**: scanning the subnet to find the PS5 needs `network_mode: host`, otherwise it only sees Docker's internal network.
- **Delivery**: `Dockerfile` + example `docker-compose.yml`, image published to GHCR by CI on tag.
- **Linux without Docker**: the same web server published as a self-contained `linux-x64` and `linux-arm64` binary in every release. No native Linux window app (the web interface covers it).
- **Windows and web together**: the WPF app keeps existing and uses the same core library; both versions ship in every release.
- **Login**: the web interface asks for user and password (set on first open), with a cookie session.
- **E2E**: starts the container, opens the interface with Playwright and repeats the upload scenarios against the fake FTP server.
- **Added while implementing**: progress over SSE (no SignalR); chunked upload with resume; a game already sent comes back as finished on reopen/restart instead of hitting the "already installed" warning (applies to the Windows app too); the newest connection test is the one that counts on the PS5 card.

## Phase 4 — English

- Interface in Portuguese and English, chosen in Settings (default: browser/system language).
- Texts move out of the code into resource files (`pt-BR`, `en`), including the visible log, notices and errors.
- Comes after Phase 3 so only one interface (the web one) is translated.
- **E2E**: runs the main flow in both languages and checks no untranslated text is left.
- Already done: README in English (default), Portuguese and Spanish, and the docs in the three languages. When the interface is in English, remove the "interface in Portuguese" note and the Portuguese button names from the English README.

## Phase 5 — Configurable webhook

- Webhook URL in Settings (Discord, ntfy, generic JSON), fired on finished, error and "needs password".
- The message text follows the language chosen in Phase 4.
- "Test" button that sends a sample message.
- **E2E**: a fake HTTP server receives the webhook and checks event, game and language.

## Phase 6 — PKG and fPKG

Full research (firmwares, kstuff, installers) in [PKG-PS5.md](PKG-PS5.md).

- **`.ffpkg`, `.ffpfs` and `.ffpfsc` images** (ShadowMount+) accepted like `.exfat`: same destination, `.ferry-part` and rename at the end. Works on any jailbroken firmware.
- **Loose `.pkg`** (PS4 or PS5 fPKG): Ferry serves the file over HTTP (with *range* and a token in the URL) and asks etaHEN DPI to install it (port 9090, `{ "url": … }`). The PS5 downloads it directly.
- **`.pkg` inside an archive**: send it over FTP to the PS5 and install from the local path, or extract it on the server and serve it. Decide after confirming whether DPI accepts a local path.
- **Card**: PS4 or PS5 from the `.pkg` header, with a warning when the type doesn't run on the firmware (e.g. PS5 fPKG on 12.xx and 13.xx doesn't work yet).
- **Settings**: package installer (etaHEN DPI, port), with a connection test and the reminder to set `DPI=1` in etaHEN's `config.ini`.
- **E2E**: a fake DPI receives the request and downloads the URL with *range* and resume; checks hash, token and the error when there is no DPI.
- **Before starting**: review the research (the scene moves fast) and close the "to confirm" items.

## Phase 7 — Progressive upload (multi-part RAR)

- **Goal**: while the download is still running into the watched folder (e.g. JDownloader on the server), start extracting and sending to the PS5 the parts that have already arrived, in order, instead of waiting for all of them.
- **Format limit**:
  - `.zip` and `.7z` keep their index at the end (last part), so they can't start before it arrives. For those, the behavior stays as today;
  - feasible for multi-part RAR (`.partN.rar` and `.rar` + `.rNN`): the files come in sequence, each with its own header.
- **How** (research first):
  - 7-Zip seems to open every volume at the start (to confirm);
  - alternatives: `unrar` extracting volume by volume and waiting for the next one, or reading the RAR sequentially ourselves;
  - a game file that spans two parts only finishes when the next part arrives. The upload either waits with the FTP connection open, or closes and continues with `APPE` (to decide).
- **Order**: only moves on with the next part in the sequence (part3 doesn't count if part2 is missing). A part still downloading (name ending in `.part`, size changing) doesn't count.
- **Safety**:
  - atomic publishing stays: `param.json`/`param.sfo` only at the end, after the last part and the verification, so ShadowMount+ doesn't install a half-sent game;
  - the RAR password is asked for before starting.
- **Card**: shows "Sending part 3 of ?" while the total isn't known, and "Waiting for part4" when it stops to wait.
- **Settings**: on/off switch (default: off until it's mature).
- **E2E**:
  - a multi-part RAR arrives in the folder one part at a time, slowly;
  - the upload starts before the last part;
  - checks the SHA-256 of each file on the fake PS5 and that `param.json`/`param.sfo` only show up at the end;
  - `.zip`/`.7z` still wait for all parts.

## Phase 8 — Payload (hello world)

- ELF built with the [ps5-payload-sdk](https://github.com/ps5-payload-dev/sdk), compiled in WSL (Ubuntu, WSL2, already installed).
- The app sends the ELF to port 9021, the PS5 shows "Ferry connected" and the payload exits. Nothing stays resident.
- **Later**: resident agent (notification, free space, game list), own upload protocol and integration with the loader (research first).

## Research, no date

- **Loose ideas**: PS5 library, duplicate warning, console profiles, send an extracted folder, speed limit, auto-update, history.
