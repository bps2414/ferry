# Compatibilidad FTP con la PS5

[English](../en/FTP-PS5.md) · [Português (BR)](../FTP-PS5.md) · **Español**

[ps5-payload-ftpsrv](https://github.com/john-tornblom/ps5-payload-ftpsrv) implementa solo un conjunto mínimo de comandos (lista de `main.c`):

```
CDUP CWD DELE LIST MKD NOOP PASV PORT PWD QUIT REST RETR RMD RNFR RNTO SIZE STOR SYST TYPE USER
(+ KILL, MTRW y otros específicos de la PS5)
```

Las versiones más nuevas (y el FTP de etaHEN) también aceptan `APPE`. Cualquier otro comando recibe `502 Command not recognized`.

## Qué rompía esto y cómo lo maneja la app

| Comportamiento del servidor | Efecto en un cliente FTP común | Qué hace la app |
|---|---|---|
| Sin `FEAT` | FluentFTP no sabe que existe `SIZE` y cae en ASCII | Envía `SIZE` directo (tras `TYPE I`) y fuerza UTF-8 |
| Sin `NLST`/`MLSD` | `UploadStream`/`FileExists` de FluentFTP fallan con 502 | Envía con `OpenWrite`/`OpenAppend` (solo `TYPE` + `PASV` + `STOR`/`APPE`) |
| Sin `EPSV` | un intento extra en cada transferencia | Usa `PASV` directo |
| `STOR` abre con `O_TRUNC` e ignora `REST` | "reanudar" con `REST`+`STOR` borraría el archivo | Nunca usa `REST` para subir |
| `APPE` puede no existir | el parcial no continúa | Sondea: `APPE` sin argumento → `501` = existe; `502` = no. Sin `APPE`, reenvía el parcial entero. Si `APPE` falla en la práctica, repite sin él automáticamente |
| `LIST` con argumento que empieza con `-` lista la carpeta actual | listado recursivo erróneo | No usa listados: el estado remoto y la verificación son por `SIZE` archivo por archivo |
| `USER` ya responde `230` (sin contraseña) | — | Funciona con cualquier usuario/contraseña |

## ftpsrv nuevo (ps5-payload-dev/ftpsrv, el que tiene `APPE`)

Comprobado en el código (`cmd.c`, `srv.c`, sep/2026):

- **`SELF` activado por defecto** (`env.self2elf = 1`): el `SIZE` de un SELF (eboot.bin, .sprx, .prx firmados) devuelve el tamaño del ELF interno, no el del archivo. El tamaño nunca coincide, la app reenvía sin motivo y la verificación falla. `SELF` es un interruptor que responde `226 SELF transfer mode enabled|disabled`. La app envía `SELF` en cada conexión y lo repite si la respuesta es "enabled". En el ftpsrv antiguo, el comando responde `502` y nada cambia.
- **`STOR` trunca en las dos versiones**: la antigua abre con `O_TRUNC`; la nueva abre sin `O_TRUNC` pero hace `ftruncate` al final del envío. Por eso no hace falta `DELE` antes del `STOR`.
- **`APPE`** hace `stat` y continúa desde el tamaño actual. La app solo usa `APPE` en un parcial que ella misma empezó (registrado en `queue.json` con la ruta remota y el tamaño final). Cualquier otro archivo remoto de tamaño distinto se reenvía entero con `STOR`, porque un archivo más pequeño puede ser otra versión, y continuarlo generaría un archivo corrupto del tamaño correcto.
- Después de cada `STOR`/`APPE`, la app lee la respuesta final y comprueba el `SIZE` al momento. Si no coincide, el archivo falla con "la PS5 no dejó sobrescribir…".

## Loaders que instalan solos (ShadowMount+)

ShadowMount+ (`sm_gameinfo.c: directory_has_param_json`) reconoce un juego por la existencia de **`sce_sys/param.json`**. La comprobación de estabilidad (`wait_for_stability_fast`) solo mira el mtime de la carpeta `sce_sys`. Resultado: con el `param.json` presente, instala el juego a mitad del envío. Una vez instalado, monta un overlay de backport (nullfs) encima de la carpeta, y a partir de ahí el `SIZE`/`RETR` de `eboot.bin`, `fakelib/*`, `sce_module/*.prx` y `sce_sys/about/right.sprx` muestran la versión del backport, incluso después de que `DELE`+`STOR` respondan 226.

Qué hace la app:

- **Publicación atómica**: `sce_sys/param.json` y `sce_sys/param.sfo` se suben como `*.ferry-part`. Solo cuando todo lo demás se ha enviado y verificado se renombran (`RNFR`/`RNTO`, borrando antes el nombre final si existe). El `param.sfo` entra por si acaso: no encontré código de itemzflow que confirme qué usa. La reanudación reconoce el archivo con sufijo.
- **Juego ya instalado**: si el `param.json` o el `param.sfo` con el nombre final ya existe en el destino, la tarjeta avisa y no envía. "Intentar de nuevo" reenvía igualmente. En ese reenvío, una diferencia en los archivos típicos de backport se convierte en aviso en el log, no en error.

## Imágenes `.exfat` (ShadowMount+)

Comprobado en el código (copia en [aloksaurabh/elf-arsenal](https://github.com/aloksaurabh/elf-arsenal), `ShadowMountPlus-main`, sep/2026):

- **Dónde busca**: en los *scanpaths*. Por defecto (`include/sm_paths.h: SM_DEFAULT_SCAN_PATHS_INITIALIZER`): `/data/homebrew`, `/data/etaHEN/games`, `/mnt/ext0|ext1/homebrew`, `/mnt/ext0|ext1/etaHEN/games`, `/mnt/usb0..7/homebrew`, `/mnt/usb0..7/etaHEN/games`, `/mnt/usb0..7`, `/mnt/ext0`, `/mnt/ext1`. Es configurable: una o más líneas `scanpath=` en `/data/shadowmount/config.ini` reemplazan la lista entera (`sm_config_mount.c`, parser de la clave `scanpath`; README "Scan paths").
- **Profundidad**: `sm_scan_tree.c: sm_scan_tree_walk` visita imágenes entre los archivos de la carpeta listada. Con `scan_depth=1` (por defecto, `include/sm_limits.h: DEFAULT_SCAN_DEPTH`), solo se lista la raíz del scanpath, así que la imagen tiene que estar **directamente** en `<scanpath>/<nombre>.exfat`. Con `scan_depth=2` o `recursive_scan=1`, también vale un nivel de subcarpeta.
- **Cómo la reconoce**: solo por la extensión, sin distinguir mayúsculas: `sm_image.c: detect_image_fs_type` (`strrchr(name, '.')` + `strcasecmp`) acepta `.ffpkg` (UFS), `.exfat` y `.ffpfs` (PFS). Los archivos que empiezan con `.` se ignoran. **`X.exfat.ferry-part` tiene extensión `.ferry-part` y se ignora**, así que el sufijo de la Fase 1 sirve.
- **Estabilidad**: `sm_image.c: maybe_mount_image_file` llama a `sm_mount_device.c: is_source_stable_for_mount`, que usa `sm_stability.c: is_path_stable_now`. Esa función compara el mayor entre `st_ctime` y `st_mtime` **del propio archivo** con `stability_wait_seconds` (por defecto 10 s, `DEFAULT_STABILITY_WAIT_SECONDS`, hasta 3600). No mira el tamaño. Un envío pausado o lento (más de 10 s sin escribir) con el nombre final se montaría a medias. Por eso hace falta la publicación atómica.
- **Contenido esperado**: los archivos del juego en la raíz de la imagen (`/sce_sys/param.json` directo, sin carpeta extra; README "Image layout requirement"). La app no abre la imagen y no lo comprueba.

Qué hace la app:

- Acepta `.exfat` suelto (stream directo del disco) o dentro de un comprimido sin carpeta de juego (stream de `7z x -so`). Destino: `ImageDir` en los Ajustes (por defecto `/mnt/ext1/homebrew`), con el nombre del archivo.
- La imagen se sube como `<nombre>.exfat.ferry-part` y solo se renombra (`RNFR`/`RNTO`) después de verificar el `SIZE`. La reanudación (APPE solo en parcial empezado por la app) y el aviso "Juego ya instalado" (si `<nombre>.exfat` ya existe) siguen las reglas de las carpetas de juego.

**Pendiente:**

- Reenviar encima de una imagen **ya montada**: la app borra el nombre final y renombra la nueva. `sm_image.c: cleanup_stale_image_mounts` solo desmonta cuando la ruta desaparece y el montaje sigue legible. Como la ruta vuelve a existir justo después del `RNTO`, SM+ puede seguir usando la imagen antigua (inodo borrado) hasta reiniciar la PS5 o SM+. No probado en la consola.
- No encontré cómo reacciona SM+ al `DELE` de una imagen montada (si ftpsrv puede borrar el archivo abierto por `lvd`/`md`). En FreeBSD el `unlink` de un archivo abierto funciona, pero no lo comprobé en la PS5.

Otros formatos de imagen (`.ffpkg`, `.ffpfsc`) y paquetes `.pkg`/fPKG: ver [PKG-PS5.md](PKG-PS5.md).

## Notificación en la PS5

No hay comando FTP ni API de red para notificaciones en ftpsrv ni en etaHEN. El camino sería un payload ELF (compilado con el SDK de PS5, llamando a `sceKernelSendNotificationRequest`) enviado a elfldr en el puerto 9021. No implementado.
