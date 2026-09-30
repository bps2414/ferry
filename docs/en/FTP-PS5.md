# FTP compatibility with the PS5

**English** · [Português (BR)](../FTP-PS5.md) · [Español](../es/FTP-PS5.md)

[ps5-payload-ftpsrv](https://github.com/john-tornblom/ps5-payload-ftpsrv) implements only a minimal set of commands (list from `main.c`):

```
CDUP CWD DELE LIST MKD NOOP PASV PORT PWD QUIT REST RETR RMD RNFR RNTO SIZE STOR SYST TYPE USER
(+ KILL, MTRW and other PS5-specific ones)
```

Newer versions (and etaHEN's FTP) also accept `APPE`. Any other command gets `502 Command not recognized`.

## What this broke and how the app handles it

| Server behavior | Effect on a regular FTP client | What the app does |
|---|---|---|
| No `FEAT` | FluentFTP doesn't know `SIZE` exists and falls back to ASCII | Sends `SIZE` directly (after `TYPE I`) and forces UTF-8 |
| No `NLST`/`MLSD` | FluentFTP's `UploadStream`/`FileExists` fail with 502 | Uploads with `OpenWrite`/`OpenAppend` (only `TYPE` + `PASV` + `STOR`/`APPE`) |
| No `EPSV` | an extra attempt on every transfer | Uses `PASV` directly |
| `STOR` opens with `O_TRUNC` and ignores `REST` | "resuming" with `REST`+`STOR` would wipe the file | Never uses `REST` for uploads |
| `APPE` may not exist | partial files don't continue | Probes: `APPE` with no argument → `501` = exists; `502` = no. Without `APPE`, re-sends the partial file in full. If `APPE` fails in practice, retries without it automatically |
| `LIST` with an argument starting with `-` lists the current folder | wrong recursive listing | Doesn't use listings: remote state and verification use `SIZE` file by file |
| `USER` already answers `230` (no password) | — | Works with any user/password |

## New ftpsrv (ps5-payload-dev/ftpsrv, the one with `APPE`)

Checked in the code (`cmd.c`, `srv.c`, Sep/2026):

- **`SELF` on by default** (`env.self2elf = 1`): `SIZE` of a SELF (signed eboot.bin, .sprx, .prx) returns the size of the ELF inside, not the file's. The size never matches, the app re-sends for nothing and verification fails. `SELF` is a toggle that answers `226 SELF transfer mode enabled|disabled`. The app sends `SELF` on every connection and repeats it if the answer is "enabled". On the old ftpsrv the command answers `502` and nothing changes.
- **`STOR` truncates in both versions**: the old one opens with `O_TRUNC`; the new one opens without `O_TRUNC` but calls `ftruncate` at the end of the upload. So there is no need for `DELE` before `STOR`.
- **`APPE`** calls `stat` and continues from the current size. The app only uses `APPE` on a partial file it started itself (recorded in `queue.json` with the remote path and final size). Any other remote file of a different size is re-sent in full with `STOR`, because a smaller file may be another version, and continuing it would produce a corrupted file of the right size.
- After each `STOR`/`APPE`, the app reads the final reply and checks `SIZE` right away. If it doesn't match, the file fails with "the PS5 didn't allow overwriting…".

## Loaders that install on their own (ShadowMount+)

ShadowMount+ (`sm_gameinfo.c: directory_has_param_json`) recognizes a game by the existence of **`sce_sys/param.json`**. The stability check (`wait_for_stability_fast`) only looks at the mtime of the `sce_sys` folder. Result: once `param.json` is present, it installs the game mid-upload. After installing, it mounts a backport overlay (nullfs) on top of the folder, and from then on `SIZE`/`RETR` of `eboot.bin`, `fakelib/*`, `sce_module/*.prx` and `sce_sys/about/right.sprx` show the backport version, even after `DELE`+`STOR` answer 226.

What the app does:

- **Atomic publishing**: `sce_sys/param.json` and `sce_sys/param.sfo` are uploaded as `*.ferry-part`. Only after everything else has been sent and verified are they renamed (`RNFR`/`RNTO`, deleting the final name first if it exists). `param.sfo` is included just in case: I found no itemzflow code confirming what it uses. Resume recognizes the file with the suffix.
- **Game already installed**: if `param.json` or `param.sfo` with the final name already exists at the destination, the card warns and doesn't send. "Try again" re-sends anyway. On that re-send, a mismatch in typical backport files becomes a log warning, not an error.

## `.exfat` images (ShadowMount+)

Checked in the code (copy in [aloksaurabh/elf-arsenal](https://github.com/aloksaurabh/elf-arsenal), `ShadowMountPlus-main`, Sep/2026):

- **Where it looks**: in the *scanpaths*. Default (`include/sm_paths.h: SM_DEFAULT_SCAN_PATHS_INITIALIZER`): `/data/homebrew`, `/data/etaHEN/games`, `/mnt/ext0|ext1/homebrew`, `/mnt/ext0|ext1/etaHEN/games`, `/mnt/usb0..7/homebrew`, `/mnt/usb0..7/etaHEN/games`, `/mnt/usb0..7`, `/mnt/ext0`, `/mnt/ext1`. It is configurable: one or more `scanpath=` lines in `/data/shadowmount/config.ini` replace the whole list (`sm_config_mount.c`, parser of the `scanpath` key; README "Scan paths").
- **Depth**: `sm_scan_tree.c: sm_scan_tree_walk` visits images among the files of the listed folder. With `scan_depth=1` (default, `include/sm_limits.h: DEFAULT_SCAN_DEPTH`), only the scanpath root is listed, so the image must be **directly** at `<scanpath>/<name>.exfat`. With `scan_depth=2` or `recursive_scan=1`, one level of subfolder also works.
- **How it recognizes**: by extension only, case-insensitive: `sm_image.c: detect_image_fs_type` (`strrchr(name, '.')` + `strcasecmp`) accepts `.ffpkg` (UFS), `.exfat` and `.ffpfs` (PFS). Files starting with `.` are ignored. **`X.exfat.ferry-part` has the extension `.ferry-part` and is ignored**, so the Phase 1 suffix works.
- **Stability**: `sm_image.c: maybe_mount_image_file` calls `sm_mount_device.c: is_source_stable_for_mount`, which uses `sm_stability.c: is_path_stable_now`. That function compares the larger of `st_ctime` and `st_mtime` **of the file itself** with `stability_wait_seconds` (default 10 s, `DEFAULT_STABILITY_WAIT_SECONDS`, up to 3600). It doesn't look at size. A paused or slow upload (more than 10 s without writes) with the final name would be mounted half-done. That's why atomic publishing is needed.
- **Expected content**: the game files at the image root (`/sce_sys/param.json` directly, no extra folder; README "Image layout requirement"). The app doesn't open the image and doesn't check this.

What the app does:

- Accepts a loose `.exfat` (streamed directly from disk) or one inside an archive with no game folder (streamed from `7z x -so`). Destination: `ImageDir` in Settings (default `/mnt/ext1/homebrew`), with the file's name.
- The image is uploaded as `<name>.exfat.ferry-part` and only renamed (`RNFR`/`RNTO`) after `SIZE` is verified. Resume (APPE only on a partial started by the app) and the "Game already installed" warning (if `<name>.exfat` already exists) follow the game-folder rules.

**Open questions:**

- Re-sending over an image that is **already mounted**: the app deletes the final name and renames the new one. `sm_image.c: cleanup_stale_image_mounts` only unmounts when the path disappears and the mount is still readable. Since the path exists again right after `RNTO`, SM+ may keep using the old image (deleted inode) until the PS5 or SM+ restarts. Not tested on the console.
- I didn't find how SM+ reacts to a `DELE` of a mounted image (whether ftpsrv can delete the file opened by `lvd`/`md`). On FreeBSD `unlink` of an open file works, but I didn't check on the PS5.

Other image formats (`.ffpkg`, `.ffpfsc`) and `.pkg`/fPKG packages: see [PKG-PS5.md](PKG-PS5.md).

## Notification on the PS5

There is no FTP command or network API for notifications in ftpsrv or etaHEN. The way would be an ELF payload (built with the PS5 SDK, calling `sceKernelSendNotificationRequest`) sent to elfldr on port 9021. Not implemented.
