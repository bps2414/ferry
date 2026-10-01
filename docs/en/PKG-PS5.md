# PKG and fPKG on a jailbroken PS5

**English** · [Português (BR)](../PKG-PS5.md) · [Español](../es/PKG-PS5.md)

> **2026-10-01 revision:** the user reported that FTP PKG upload followed by local-path DPI installation does not solve their use case. Removal is planned; the code still implements the behavior below. USB or PKG Manager streaming is the proposed replacement. See the [research and replacement plan (Portuguese)](../plans/next/remover-pkg-ftp-e-adotar-pkg-manager.md). No console installation was verified in this research; the old instructions do not establish compatibility.

Research for Phase 6 of the [roadmap](ROADMAP.md). Status as of **2026-09-30**. The scene moves fast; check the sources before implementing. What I couldn't confirm is marked **to confirm**.


## Using Ferry — Phase 6

Supports loose `.pkg` or **one PKG per archive** (ZIP/RAR/7z, volumes and passwords). Auxiliary files are ignored; multiple PKGs or mixed dump/image payloads are rejected explicitly.

In Settings, choose upload only (default) or automatically request installation via DPI. The default destination is `/data/etaHEN/pkgs`, with PKGs directly in that folder and unique filenames; partial uploads are not listed as PKGs by the installer.

After upload, open etaHEN Toolbox → ★ Custom Background Package Installer and select the package. For another folder, set “Custom PKG Search Path” on the console to the folder shown on the card, then leave and reopen the installer. Packages uploaded by the previous version retain their original path: use the folder shown on the card without uploading again.

The previous Settings default `/data/ferry/pkg` migrates to `/data/etaHEN/pkgs`. Custom folders and paths already recorded in the queue are preserved. Automatic installation defaults to off, including on upgrade.

For automatic mode or the “Request installation” button, enable `DPI=1` in etaHEN and set the DPI port (9090). “Test DPI” only checks the port. Ferry streams over FTP without extracting the whole package on the PC, checks SIZE and publishes by rename before any installation request. Windows and web use the same flow.

“Package ready” means upload complete and available for manual installation. “Installation requested” means DPI accepted the request; follow completion on the PS5. “Check on PS5” means an unknown result: check before resubmitting, since the first request may have been accepted. Restarting Ferry never repeats sent requests.

The original PKG/archive and remote package are retained even with Delete originals enabled. Removing a card neither cancels installation nor deletes the package. Automatic cleanup, remote completion confirmation, direct HTTP and automatic game/update/DLC ordering are outside this delivery.

`.exfat`, `.ffpkg`, `.ffpfs` and `.ffpfsc` remain ShadowMount+ images, sent to the images folder without DPI. Headers identify CNT/PS4 or FIH/PS5 without proving fake signatures or firmware compatibility. The local path needs acceptance on your actual PS5; local fixtures cannot replace it.

Focused validation: `dotnet run --project e2e -- --pkg`. Windows: `dotnet run --project e2e/windows -c Release`. Web E2E runs pt-BR/en. Docker, remote CI and console require their own execution.

## Enabling DPI

DPI is included in etaHEN; nothing needs installing on the PC.

1. With etaHEN loaded, open **etaHEN Toolbox → Services → Direct Package Installer** (port **9090**) on PS5 and enable it.
2. In Ferry, enter the same PS5 IP, keep DPI port 9090 and click **Test DPI**. The test installs nothing.
3. Enable automatic DPI requests for new uploads, or leave it off and use the package button when wanted.

Alternatively edit `/data/etaHEN/config.ini` through FTP, set `DPI=1` and reload etaHEN. `DPI_v2=1` enables a separate service on 12800; changing Ferry's port to 12800 does not change its protocol.

For manual installation from internal storage, open **★ Custom Background Package Installer**, which reads `/data/etaHEN/pkgs` by default. Set “Custom PKG Search Path” for another folder and reopen the installer. On some firmware it depends on DPI v2. This differs from the standard USB Package Installer.

Game, update and DLC remain separate PKGs with unique filenames, even when archive entries share a name. The console installer applies them; Ferry does not check Title ID correspondence, update compatibility or installation order.

Sources: [services menu](https://github.com/etaHEN/etaHEN/blob/main/Source%20Code/shellui/assets/etaHEN_toolbox.xml) and [package search](https://github.com/etaHEN/etaHEN/blob/main/Source%20Code/shellui/src/MonoUtils.cpp). [GronedWaffel r2](https://github.com/GronedWaffel/etahen-13.60/releases/tag/v2.5B-13.60-r2) retains these services but declares firmware 13.60 only, without certifying 13.42.

## Reference research

The research below records the sources checked on 2026-09-30, including earlier proposals. The contract above describes implemented behavior; direct HTTP remains future work and firmware compatibility is not a Ferry guarantee.
## Ways to have a game on a jailbroken PS5

| Form | What it is | Who installs/loads it | Ferry before Phase 6 |
|---|---|---|---|
| **Game folder** (dump) | `PPSA…` folder with `eboot.bin` and `sce_sys/` | ShadowMount+ (installs on its own when it finds `sce_sys/param.json`) or itemzflow | ✅ extracts and sends over FTP |
| **`.exfat` image** | The game inside a disk image | ShadowMount+ mounts it | ✅ sends it whole to the images folder |
| **`.ffpkg` image** (UFS) | Same; the format **recommended** by ShadowMount+ | ShadowMount+ mounts it | ❌ not yet (same path as `.exfat`) |
| **`.ffpfs` / `.ffpfsc` image** (PFS, `c` = compressed) | Same, experimental; the compressed one reads at ~150–250 MB/s | ShadowMount+ mounts it | ❌ not yet |
| **PS4 fPKG** | A PS4 `.pkg` with a fake signature | Package installer (etaHEN DPI and others) + **kstuff** to run it | ❌ |
| **PS5 fPKG** | A PS5 `.pkg` with a fake signature, built from a dump | Package installer + **kstuff-lite** with PS5 fPKG support | ❌ |

- **fPKG** = *fake package*: a `.pkg` built outside Sony. It needs a kernel "enabler" (kstuff) for the system to accept and run it.
- **kstuff** makes the PS5 accept executables and packages with fake signatures (fSELF and fPKG). It is "a debugger watching the kernel in real time", so it costs performance. There is a `kstuff-toggle` plugin to turn it off when not needed.
- A `.pkg` **can't be installed by sending it over FTP to a folder**. Something on the PS5 has to call the system installer. That's why Ferry needs to talk to an installer, not just FTP.

## Jailbreak by firmware

| Firmware | Public path | Notes |
|---|---|---|
| 3.00–4.51 | IPV6 (kernel) via WebKit or BD-JB2 | The first jailbreak |
| up to 5.50 | UMTX (WebKit, CVE-2024-43102) | — |
| 1.00–10.01 | Lapse (kernel) | Several entry points (BD-J, WebKit, YouTube) |
| 4.03–12.40 (one source says 13.40, **to confirm**) | Y2JB: entry through the YouTube app; kernel via Lapse (up to 10.01) or P2JB (up to 12.70) | — |
| **7.00–13.60** | **Relapse**: WebKit + kernel race condition (September 2026) | PS5 and PS5 Pro. Loads in seconds; usually sends kstuff, ShadowMount+ and etaHEN next |
| **14.00** (2026-09-16) | none public | Whoever updated lost the jailbreak |

## fPKG by firmware

| Firmware | PS4 fPKG | PS5 fPKG |
|---|---|---|
| 3.00–4.51 | ✅ kstuff (since 2023) | via kstuff-lite 1.11: from 2.50 on |
| 5.xx–7.61 | ✅ kstuff (April 2025) | ✅ kstuff-lite 1.11 |
| 8.xx–9.xx | ✅ kstuff-lite 1.11 (**to confirm**) | ✅ kstuff-lite 1.11 |
| 10.00–10.01 | ✅ kstuff 1.6.6 (EchoStretch) and kstuff-lite | ✅ kstuff-lite 1.11 |
| 11.00–11.60 | kstuff-lite 1.11 (**to confirm**) | ✅ up to 11.40; "fixed for 11.60" in the changelog |
| 12.00–12.70 | kstuff-lite 1.11 (**to confirm**) | ❌ the new method (PPR, by Drakmor) isn't enabled in this range |
| 13.00–13.60 | kstuff-lite 1.11 loads (fSELF); fPKG **to confirm** | ❌ |
| 14.00 | ❌ no jailbreak | ❌ |

- **kstuff-lite 1.11 beta** (EchoStretch, with Drakmor's PS5 fPKG work): runs from 1.00 to 13.60. PS5 fPKG goes up to 11.40, with a fix for 11.60.
- **On 12.xx and 13.xx**, today, the way to play PS5 games is **ShadowMount+ dumps and images**, which Ferry already sends. PS5 fPKG doesn't work there yet.
- **Tools to build PS5 fPKGs** from a dump: FPKG-GUI (Drakmor) and PS5-FPKG-Builder. **For PS1/PS2/PSP**: PS-Classics-fPKG-Builder.

## How installers receive a `.pkg` over the network

| Service | Port | How to ask | Where the `.pkg` comes from |
|---|---|---|---|
| **etaHEN DPI v1** | 9090 (TCP) | Send `{ "url" : "http://…" }`; answers `{ "res" : "0" }` and closes the connection | The PS5 downloads from the URL. According to the docs, a local path (`/data/pkg/game.pkg`) also works (**to confirm**) |
| **etaHEN DPI v2** | 12800 (HTTP) | Web interface on the PS5; tools send the URL to it | URL (HTTP) |
| **ezRemote DPI** (separate payload) | 9040 (TCP) | Send the URL as text (`echo URL \| nc PS5 9040`) | http/https only. Doesn't need etaHEN or kstuff to run |
| **pkg-sender** (own payload) | 12800 + UDP beacon 12801 | `POST /api/install` (JSON) or `GET /install?url=`; progress at `GET /api/status` | The PC serves the file over HTTP with *range* (port 9898). Images (`.exfat`, `.ffpkg`, `.ffpfsc`) are copied to `/data/homebrew` |
| **ps5upload** (own payload) | 9113–9114 | Own binary protocol (FTX2) | Upload from the PC, NAS/SMB or HTTP link |

- etaHEN's DPI ships **disabled**. To enable it: `DPI=1` (v1) and/or `DPI_v2=1` in etaHEN's `config.ini`.
- etaHEN ports: FTP 1337, elfldr 9021, klog 9081, DPI 9090, DPI v2 12800.
- DPI v1 **doesn't report progress**: only "accepted". Progress shows up in the PS5 download queue. Whether there is a progress API: **to confirm** in etaHEN's code.

## Other tools that do part of what Ferry does

- **ps5upload**: desktop (Windows, macOS, Linux, Android) and web via Docker. Sends games and packages, decompresses `.zip`/`.7z`/`.rar` on the PC and sends them already extracted, resumes, verifies with BLAKE3, and installs `.pkg` with its own installer on the PS5. Firmware 1.00–13.60. It's the closest project to Ferry. Ferry's difference: it needs no payload of its own (it uses the FTP that is already there) and it is simpler.
- **pkg-sender**, **PS5 PKG Virtual Shop** and **etahen-pkg-loader**: install `.pkg` through DPI or their own receiver.

## Proposal for Ferry (Phase 6)

1. **`.ffpkg`, `.ffpfs` and `.ffpfsc` images** handled like `.exfat`: same destination, `.ferry-part` and rename at the end. ShadowMount+ recognizes them by extension (`detect_image_fs_type`; check whether `.ffpfsc` is already in the code). It's the cheapest step and works on **any** jailbroken firmware.
2. **Loose `.pkg`** (PS4 or PS5): Ferry serves the file over HTTP, with *range* and a random token in the URL, no login. Then it asks etaHEN DPI v1 (port 9090) to install from that URL, and the PS5 downloads it directly. With `network_mode: host`, the server is already on the home network.
3. **`.pkg` inside an archive**: the PS5 downloads with *range*, and `7z x -so` gives no random access. Options:
   - (a) send the `.pkg` over FTP to the PS5 (streamed, as today) and ask for installation from the local path, if DPI accepts it. Uses the space of the `.pkg` plus the installed game on the PS5;
   - (b) extract the `.pkg` on the server and serve it. Uses disk on the server, which breaks the "nothing extracted on disk" rule.

   Decide after confirming DPI's local path.
4. **Card and firmware warning**: tell PS4 from PS5 by the `.pkg` header (PS4 starts with `\x7FCNT`; PS5's, **to confirm**). Warn when the type doesn't run on the given firmware (e.g. PS5 fPKG on 12.xx).
5. **Settings**: "Package installer" (etaHEN DPI, port 9090), with a connection test. Remind the user that DPI ships disabled in etaHEN.
6. **E2E**: a fake DPI (TCP 9090) receives the JSON and downloads the URL with *range* and resume. The test checks the hash, that a wrong token is refused, and the error when DPI doesn't answer.

**Open before implementing**: DPI progress API; local path in DPI v1; PS5 `.pkg` header; PS4 fPKG on 11.xx–13.xx with kstuff-lite; whether to also support the standalone receivers (pkg-sender, ezRemote DPI) besides etaHEN.

## Sources

- etaHEN: [README](https://github.com/etaHEN/etaHEN) (ports, DPI, `config.ini`) · [1.7b with remote install (Wololo)](https://wololo.net/2024/02/26/ps5-release-etahen-1-7b-adds-remote-package-install-support/)
- kstuff: [4.51 (Wololo, 2023)](https://wololo.net/2023/11/05/ps5-sleirsgoevys-kstuff-and-fpkg-ps4-support-added-to-firmware-4-51-etahen-updated-to-support-4-51-as-well/) · [up to 7.61 (Wololo, 2025)](https://wololo.net/2025/04/23/ps5-kstuff-gets-ported-to-all-supported-firmwares-up-to-7-61-included-kstuff-toggle-plugin/) · [kstuff-lite 1.11](https://github.com/EchoStretch/kstuff-lite/releases/tag/v1.11) · [1.00–13.60 summary (onejailbreak)](https://onejailbreak.com/blog/kstuff-lite-1-11-adds-ps5-firmware-1-00-13-60-support/)
- PS5 fPKG: [FPKG up to 11.40 (Se7enSins)](https://www.se7ensins.com/media/fake-packages-announced-for-ps5-up-to-11-40-with-fpkg-builder-more.2571/) · [PS5-FPKG-Builder](https://github.com/Phoenixx1202/PS5-FPKG-Builder) · [PS-Classics-fPKG-Builder](https://github.com/SvenGDK/PS-Classics-fPKG-Builder)
- Jailbreak: [Relapse 13.60 (VideoCardz)](https://videocardz.com/newz/ps5-13-60-jailbreak-released-almost-every-ps5-can-now-be-jailbroken) · [Relapse and 14.00 (Kotaku)](https://kotaku.com/new-ps5-jailbreak-exploit-works-on-systems-running-july-2026-firmware-2000738283) · [UMTX](https://github.com/PS5Dev/PS5-UMTX-Jailbreak) · [vulnerability list (psdevwiki)](https://www.psdevwiki.com/ps5/Vulnerabilities) · [guide (GBAtemp)](https://gbatemp.net/threads/ps5-exploit-guide.613891/)
- ShadowMount+: [README](https://github.com/drakmor/ShadowMountPlus/blob/main/README.md) (formats `.ffpkg`, `.exfat`, `.ffpfs`, `.ffpfsc`)
- Installers and tools: [ps5-ezremote-dpi](https://github.com/cy33hc/ps5-ezremote-dpi) · [pkg-sender](https://github.com/Loopayeh/pkg-sender) · [ps5upload](https://github.com/phantomptr/ps5upload) · [etahen-pkg-loader](https://github.com/trocla/etahen-pkg-loader) · [PS5 PKG Virtual Shop](https://github.com/MestreTM/ps5_pkg_virtual_shop)
