# PKG y fPKG en la PS5 con jailbreak

[English](../en/PKG-PS5.md) · [Português (BR)](../PKG-PS5.md) · **Español**

Investigación para la Fase 6 del [roadmap](ROADMAP.md). Situación a **30/09/2026**. La escena cambia rápido; revisa las fuentes antes de implementar. Lo que no pude confirmar está marcado como **por confirmar**.


## Uso en Ferry — Fase 6

Acepta `.pkg` suelto o **un PKG por comprimido** (ZIP/RAR/7z, volúmenes y contraseña). Ignora archivos auxiliares; rechaza varios PKGs o mezcla con dump/imagen.

En Configuración, elige solo enviar (por defecto) o solicitar instalación automáticamente por DPI. El destino predeterminado es `/data/etaHEN/pkgs`, con PKGs directamente en esa carpeta y nombres exclusivos; el instalador no muestra envíos parciales como PKG.

Después del envío, abre etaHEN Toolbox → ★ Custom Background Package Installer y selecciona el paquete. Para otra carpeta, ajusta “Custom PKG Search Path” en la consola a la carpeta que muestra la tarjeta, sal y vuelve a abrir el instalador. Los paquetes de la versión anterior conservan su ruta: usa la carpeta mostrada sin volver a enviar.

El antiguo valor predeterminado `/data/ferry/pkg` migra a `/data/etaHEN/pkgs`. Las carpetas personalizadas y rutas registradas en la cola se conservan. La instalación automática queda desactivada por defecto, también al actualizar.

Para el modo automático o el botón de solicitar instalación, activa `DPI=1` en etaHEN y configura el puerto DPI (9090). La prueba solo comprueba el puerto. Ferry transmite por FTP sin extraer todo el paquete en el PC, comprueba SIZE y publica mediante rename antes de solicitar instalación. Windows y web usan el mismo flujo.

“Paquete listo” significa envío terminado, disponible para instalar manualmente. “Instalación solicitada” significa petición aceptada por DPI; comprueba el final en la PS5. “Comprueba en PS5” significa resultado desconocido: comprueba antes de reenviar. Reiniciar Ferry no repite solicitudes enviadas.

Conserva el PKG/comprimido original y el paquete remoto incluso con borrar originales activado. Quitar el card no cancela instalación ni borra el paquete. Sin limpieza automática, confirmación remota de finalización, HTTP directo ni orden automático de juego/update/DLC.

`.exfat`, `.ffpkg`, `.ffpfs` y `.ffpfsc` son imágenes de ShadowMount+, sin DPI. Las cabeceras identifican CNT/PS4 o FIH/PS5 sin garantizar firma falsa ni compatibilidad. La ruta local requiere aceptación en una PS5 real; las pruebas locales no la sustituyen.

Prueba enfocada: `dotnet run --project e2e -- --pkg`. Windows: `dotnet run --project e2e/windows -c Release`. E2E web ejecuta pt-BR/en. Docker, CI remota y consola necesitan ejecución propia.

## Activar DPI

DPI viene incluido en etaHEN; no se instala un programa en el PC.

1. Con etaHEN cargado, abre **etaHEN Toolbox → Services → Direct Package Installer** (puerto **9090**) y actívalo.
2. En Ferry, configura la IP de esa PS5, deja el puerto DPI 9090 y pulsa **Probar DPI**. La prueba no instala nada.
3. Activa solicitudes automáticas para nuevos envíos, o deja la opción desactivada y usa el botón del paquete cuando quieras.

Alternativa: edita `/data/etaHEN/config.ini` por FTP, define `DPI=1` y recarga etaHEN. `DPI_v2=1` activa otro servicio en 12800; cambiar el puerto de Ferry a 12800 no cambia su protocolo.

Para instalar desde almacenamiento interno, abre **★ Custom Background Package Installer**. Lee `/data/etaHEN/pkgs` por defecto; otra carpeta requiere “Custom PKG Search Path” y volver a abrir el instalador. En algunos firmwares depende de DPI v2. No es el Package Installer estándar para USB.

Juego, update y DLC se conservan como PKGs separados con nombres exclusivos, incluso cuando comparten nombre dentro del archivo. El instalador de la consola los aplica; Ferry no comprueba Title ID, compatibilidad de update ni orden de instalación.

Fuentes: [menú de servicios](https://github.com/etaHEN/etaHEN/blob/main/Source%20Code/shellui/assets/etaHEN_toolbox.xml) y [búsqueda de paquetes](https://github.com/etaHEN/etaHEN/blob/main/Source%20Code/shellui/src/MonoUtils.cpp). [GronedWaffel r2](https://github.com/GronedWaffel/etahen-13.60/releases/tag/v2.5B-13.60-r2) mantiene estos servicios, pero declara soporte solo para firmware 13.60, sin certificar 13.42.

## Investigación de referencia

La investigación siguiente registra fuentes consultadas el 30/09/2026, incluidas propuestas anteriores. El contrato anterior describe el comportamiento implementado; HTTP directo queda pendiente y la compatibilidad de firmware no es una garantía de Ferry.
## Formas de tener un juego en una PS5 con jailbreak

| Forma | Qué es | Quién lo instala/carga | Ferry antes de la Fase 6 |
|---|---|---|---|
| **Carpeta del juego** (dump) | Carpeta `PPSA…` con `eboot.bin` y `sce_sys/` | ShadowMount+ (instala solo al encontrar `sce_sys/param.json`) o itemzflow | ✅ extrae y envía por FTP |
| **Imagen `.exfat`** | El juego dentro de una imagen de disco | ShadowMount+ la monta | ✅ la envía entera a la carpeta de imágenes |
| **Imagen `.ffpkg`** (UFS) | Igual; el formato **recomendado** por ShadowMount+ | ShadowMount+ la monta | ❌ todavía no (mismo camino que el `.exfat`) |
| **Imagen `.ffpfs` / `.ffpfsc`** (PFS, `c` = comprimida) | Igual, experimental; la comprimida lee a ~150–250 MB/s | ShadowMount+ la monta | ❌ todavía no |
| **fPKG de PS4** | Un `.pkg` de PS4 con firma falsa | Instalador de paquetes (etaHEN DPI y otros) + **kstuff** para ejecutarlo | ❌ |
| **fPKG de PS5** | Un `.pkg` de PS5 con firma falsa, generado a partir de un dump | Instalador de paquetes + **kstuff-lite** con soporte de fPKG de PS5 | ❌ |

- **fPKG** = *fake package*: un `.pkg` hecho fuera de Sony. Necesita un "enabler" en el kernel (kstuff) para que el sistema lo acepte y lo ejecute.
- **kstuff** hace que la PS5 acepte ejecutables y paquetes con firma falsa (fSELF y fPKG). Es "un debugger vigilando el kernel en tiempo real", así que cuesta rendimiento. Existe el plugin `kstuff-toggle` para desactivarlo cuando no hace falta.
- Un `.pkg` **no se puede instalar enviándolo por FTP a una carpeta**. Algo en la PS5 tiene que llamar al instalador del sistema. Por eso Ferry necesita hablar con un instalador, no solo con el FTP.

## Jailbreak por firmware

| Firmware | Camino público | Notas |
|---|---|---|
| 3.00–4.51 | IPV6 (kernel) vía WebKit o BD-JB2 | El primer jailbreak |
| hasta 5.50 | UMTX (WebKit, CVE-2024-43102) | — |
| 1.00–10.01 | Lapse (kernel) | Varias entradas (BD-J, WebKit, YouTube) |
| 4.03–12.40 (una fuente dice 13.40, **por confirmar**) | Y2JB: entrada por la app de YouTube; kernel con Lapse (hasta 10.01) o P2JB (hasta 12.70) | — |
| **7.00–13.60** | **Relapse**: WebKit + condición de carrera en el kernel (septiembre de 2026) | PS5 y PS5 Pro. Carga en segundos; suele enviar kstuff, ShadowMount+ y etaHEN a continuación |
| **14.00** (16/09/2026) | ninguno público | Quien actualizó perdió el jailbreak |

## fPKG por firmware

| Firmware | fPKG de PS4 | fPKG de PS5 |
|---|---|---|
| 3.00–4.51 | ✅ kstuff (desde 2023) | vía kstuff-lite 1.11: desde 2.50 |
| 5.xx–7.61 | ✅ kstuff (abril de 2025) | ✅ kstuff-lite 1.11 |
| 8.xx–9.xx | ✅ kstuff-lite 1.11 (**por confirmar**) | ✅ kstuff-lite 1.11 |
| 10.00–10.01 | ✅ kstuff 1.6.6 (EchoStretch) y kstuff-lite | ✅ kstuff-lite 1.11 |
| 11.00–11.60 | kstuff-lite 1.11 (**por confirmar**) | ✅ hasta 11.40; "corregido para 11.60" en el changelog |
| 12.00–12.70 | kstuff-lite 1.11 (**por confirmar**) | ❌ el método nuevo (PPR, de Drakmor) no está activado en ese rango |
| 13.00–13.60 | kstuff-lite 1.11 carga (fSELF); fPKG **por confirmar** | ❌ |
| 14.00 | ❌ sin jailbreak | ❌ |

- **kstuff-lite 1.11 beta** (EchoStretch, con el trabajo de fPKG de PS5 de Drakmor): corre de 1.00 a 13.60. El fPKG de PS5 llega hasta 11.40, con corrección para 11.60.
- **En 12.xx y 13.xx**, hoy, el camino para juegos de PS5 son los **dumps e imágenes de ShadowMount+**, que Ferry ya envía. El fPKG de PS5 todavía no funciona ahí.
- **Herramientas para generar fPKG de PS5** a partir de un dump: FPKG-GUI (Drakmor) y PS5-FPKG-Builder. **Para PS1/PS2/PSP**: PS-Classics-fPKG-Builder.

## Cómo reciben los instaladores un `.pkg` por la red

| Servicio | Puerto | Cómo pedirlo | De dónde viene el `.pkg` |
|---|---|---|---|
| **etaHEN DPI v1** | 9090 (TCP) | Enviar `{ "url" : "http://…" }`; responde `{ "res" : "0" }` y cierra la conexión | La PS5 lo descarga de la URL. Según la documentación, también acepta ruta local (`/data/pkg/juego.pkg`, **por confirmar**) |
| **etaHEN DPI v2** | 12800 (HTTP) | Interfaz web en la PS5; las herramientas le envían la URL | URL (HTTP) |
| **ezRemote DPI** (payload aparte) | 9040 (TCP) | Enviar la URL como texto (`echo URL \| nc PS5 9040`) | Solo http/https. No necesita etaHEN ni kstuff para ejecutarse |
| **pkg-sender** (payload propio) | 12800 + beacon UDP 12801 | `POST /api/install` (JSON) o `GET /install?url=`; progreso en `GET /api/status` | El PC sirve el archivo por HTTP con *range* (puerto 9898). Las imágenes (`.exfat`, `.ffpkg`, `.ffpfsc`) se copian a `/data/homebrew` |
| **ps5upload** (payload propio) | 9113–9114 | Protocolo binario propio (FTX2) | Subida desde el PC, NAS/SMB o enlace HTTP |

- El DPI de etaHEN viene **desactivado**. Para activarlo: `DPI=1` (v1) y/o `DPI_v2=1` en el `config.ini` de etaHEN.
- Puertos de etaHEN: FTP 1337, elfldr 9021, klog 9081, DPI 9090, DPI v2 12800.
- El DPI v1 **no informa del progreso**: solo "aceptado". El avance aparece en la cola de descargas de la PS5. Si existe una API de progreso: **por confirmar** en el código de etaHEN.

## Otras herramientas que hacen parte de lo que hace Ferry

- **ps5upload**: escritorio (Windows, macOS, Linux, Android) y web por Docker. Envía juegos y paquetes, descomprime `.zip`/`.7z`/`.rar` en el PC y los envía ya extraídos, reanuda, verifica con BLAKE3 e instala `.pkg` con un instalador propio en la PS5. Firmware 1.00–13.60. Es el proyecto más cercano a Ferry. La diferencia de Ferry: no necesita payload propio (usa el FTP que ya existe) y es más simple.
- **pkg-sender**, **PS5 PKG Virtual Shop** y **etahen-pkg-loader**: instalan `.pkg` por DPI o con un receptor propio.

## Propuesta para Ferry (Fase 6)

1. **Imágenes `.ffpkg`, `.ffpfs` y `.ffpfsc`** tratadas como el `.exfat`: mismo destino, `.ferry-part` y renombrar al final. ShadowMount+ las reconoce por la extensión (`detect_image_fs_type`; comprobar si `.ffpfsc` ya está en el código). Es el paso más barato y vale en **cualquier** firmware con jailbreak.
2. **`.pkg` suelto** (PS4 o PS5): Ferry sirve el archivo por HTTP, con *range* y un token aleatorio en la URL, sin login. Después pide la instalación a etaHEN DPI v1 (puerto 9090) con esa URL, y la PS5 lo descarga directamente. Con `network_mode: host`, el servidor ya está en la red de casa.
3. **`.pkg` dentro de un comprimido**: la PS5 descarga con *range*, y `7z x -so` no da acceso aleatorio. Opciones:
   - (a) enviar el `.pkg` por FTP a la PS5 (en streaming, como hoy) y pedir la instalación por la ruta local, si DPI la acepta. Ocupa el espacio del `.pkg` más el del juego instalado en la PS5;
   - (b) extraer el `.pkg` en el servidor y servirlo. Ocupa disco en el servidor, lo que rompe la regla "nada extraído en el disco".

   Decidir después de confirmar la ruta local del DPI.
4. **Tarjeta y aviso por firmware**: distinguir PS4 de PS5 por la cabecera del `.pkg` (el de PS4 empieza con `\x7FCNT`; el de PS5, **por confirmar**). Avisar cuando el tipo no corre en el firmware indicado (p. ej. fPKG de PS5 en 12.xx).
5. **Ajustes**: "Instalador de paquetes" (etaHEN DPI, puerto 9090), con prueba de conexión. Recordar que el DPI viene desactivado en etaHEN.
6. **E2E**: un DPI falso (TCP 9090) recibe el JSON y descarga la URL con *range* y reanudación. La prueba comprueba el hash, que un token incorrecto se rechaza y el error cuando el DPI no responde.

**Pendiente antes de implementar**: API de progreso del DPI; ruta local en DPI v1; cabecera del `.pkg` de PS5; fPKG de PS4 en 11.xx–13.xx con kstuff-lite; si conviene soportar también los receptores propios (pkg-sender, ezRemote DPI) además de etaHEN.

## Fuentes

- etaHEN: [README](https://github.com/etaHEN/etaHEN) (puertos, DPI, `config.ini`) · [1.7b con instalación remota (Wololo)](https://wololo.net/2024/02/26/ps5-release-etahen-1-7b-adds-remote-package-install-support/)
- kstuff: [4.51 (Wololo, 2023)](https://wololo.net/2023/11/05/ps5-sleirsgoevys-kstuff-and-fpkg-ps4-support-added-to-firmware-4-51-etahen-updated-to-support-4-51-as-well/) · [hasta 7.61 (Wololo, 2025)](https://wololo.net/2025/04/23/ps5-kstuff-gets-ported-to-all-supported-firmwares-up-to-7-61-included-kstuff-toggle-plugin/) · [kstuff-lite 1.11](https://github.com/EchoStretch/kstuff-lite/releases/tag/v1.11) · [resumen 1.00–13.60 (onejailbreak)](https://onejailbreak.com/blog/kstuff-lite-1-11-adds-ps5-firmware-1-00-13-60-support/)
- fPKG de PS5: [FPKG hasta 11.40 (Se7enSins)](https://www.se7ensins.com/media/fake-packages-announced-for-ps5-up-to-11-40-with-fpkg-builder-more.2571/) · [PS5-FPKG-Builder](https://github.com/Phoenixx1202/PS5-FPKG-Builder) · [PS-Classics-fPKG-Builder](https://github.com/SvenGDK/PS-Classics-fPKG-Builder)
- Jailbreak: [Relapse 13.60 (VideoCardz)](https://videocardz.com/newz/ps5-13-60-jailbreak-released-almost-every-ps5-can-now-be-jailbroken) · [Relapse y 14.00 (Kotaku)](https://kotaku.com/new-ps5-jailbreak-exploit-works-on-systems-running-july-2026-firmware-2000738283) · [UMTX](https://github.com/PS5Dev/PS5-UMTX-Jailbreak) · [lista de vulnerabilidades (psdevwiki)](https://www.psdevwiki.com/ps5/Vulnerabilities) · [guía (GBAtemp)](https://gbatemp.net/threads/ps5-exploit-guide.613891/)
- ShadowMount+: [README](https://github.com/drakmor/ShadowMountPlus/blob/main/README.md) (formatos `.ffpkg`, `.exfat`, `.ffpfs`, `.ffpfsc`)
- Instaladores y herramientas: [ps5-ezremote-dpi](https://github.com/cy33hc/ps5-ezremote-dpi) · [pkg-sender](https://github.com/Loopayeh/pkg-sender) · [ps5upload](https://github.com/phantomptr/ps5upload) · [etahen-pkg-loader](https://github.com/trocla/etahen-pkg-loader) · [PS5 PKG Virtual Shop](https://github.com/MestreTM/ps5_pkg_virtual_shop)
