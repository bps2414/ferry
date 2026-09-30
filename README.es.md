<img src="docs/logo.png" width="56" alt=""> 

# Ferry

[English](README.md) · [Português (BR)](README.pt-BR.md) · **Español**

envío de juegos a la PS5

![Cola de Ferry](docs/screenshots/fila.png)

[![CI](https://github.com/bps2414/ferry/actions/workflows/ci.yml/badge.svg)](https://github.com/bps2414/ferry/actions/workflows/ci.yml)

Ferry toma juegos comprimidos (`.zip`, `.rar`, `.7z`, incluso divididos en partes) y **los extrae y envía al mismo tiempo** a una PS5 con jailbreak por FTP — los archivos extraídos nunca se escriben en tu disco.

- **Windows**: un `.exe` portátil (sin instalar nada)
- **Self-hosted** (Docker o Linux): corre en tu servidor de casa y lo usas desde el navegador en `http://<ip-del-servidor>:8021`
- Interfaz oscura con cola, progreso, velocidad y tiempo restante
- Pausa, reanuda, cierra y vuelve a abrir sin perder lo que ya se envió

> Por ahora la interfaz está en portugués; el inglés es el siguiente punto del [roadmap](docs/ROADMAP.md). Los nombres de los botones aparecen abajo tal como están en la app, con su significado.

> Hecho para tus propios homebrew/backups en una consola desbloqueada. Úsalo bajo tu propio riesgo.

## Descarga

Descarga `Ferry.exe` desde la página de [Releases](../../releases) y ejecútalo. Requiere Windows 10/11 x64.

## Self-hosted (servidor de casa)

El mismo motor que la app de Windows, con interfaz web: cola en vivo, ajustes, log, contraseña del archivo pedida en el navegador y subida de archivos arrastrándolos a la página (en bloques — continúa donde se quedó si se cae la conexión).

**Docker** (amd64 y arm64): copia el [`docker-compose.yml`](docker-compose.yml), cambia `/caminho/dos/jogos` por tu carpeta de juegos y ejecuta:

```bash
docker compose up -d
```

Abre `http://<ip-del-servidor>:8021`. La primera vez, la página pide crear el usuario y la contraseña. Imagen: `ghcr.io/bps2414/ferry`.

- Se recomienda `network_mode: host`: la búsqueda de la PS5 recorre tu red de casa y el FTP con la PS5 funciona sin NAT.
- Volúmenes: `/data` (ajustes, cola, log, login) y `/games` (carpeta vigilada: lo que caiga en ella entra solo en la cola; las subidas desde el navegador también van ahí).
- Variables: `FERRY_PORT` (por defecto `8021`), `FERRY_DATA` (`/data`), `FERRY_GAMES` (`/games`).
- ¿Olvidaste la contraseña? Borra `auth.json` en la carpeta de datos y abre la página otra vez.

**Linux sin Docker** (x64 o arm64, p. ej. Raspberry Pi): descarga `Ferry-linux-x64.tar.gz` (o `-arm64`) desde [Releases](../../releases), extráelo y ejecuta `./ferry`. 7-Zip va incluido en el paquete. Los datos quedan en `~/.local/share/Ferry` (o en `FERRY_DATA`).

> Sin https el navegador no deja que la página muestre notificaciones del sistema; los avisos (terminado, error, contraseña) aparecen dentro de la página. Para acceder desde fuera de casa, usa un proxy inverso con https o una VPN — no expongas el puerto directamente a internet.

## Cómo usar

1. Ejecuta un payload de FTP en la PS5 (**ftpsrv**, puerto 2121, o el FTP de **etaHEN**, puerto 1337).
2. Abre la app → **Configurações** (Ajustes): IP de la PS5, puerto, destino (**M.2** `/mnt/ext1/homebrew` o **SSD interno** `/data/homebrew`). Todo se guarda solo. El pie de la barra lateral muestra si la PS5 está en línea.
3. En **Fila** (Cola), haz clic en el centro de la pantalla (abre el selector de archivos) o arrastra los archivos a la ventana. Selecciona **todas las partes** a la vez.
4. Listo: cuando estén todas las partes, el juego se extrae y se envía a `<destino>/<carpeta del juego>`.

Opcional: una **carpeta vigilada** — todo lo que caiga en ella entra solo en la cola (útil para la carpeta de descargas).

**Transferir agora** (Transferir ahora): en un juego en cola o pausado, lo pasa al frente. El envío en curso vuelve a la cola y después continúa donde se quedó.

## Formatos aceptados

| Formato | Ejemplo |
|---|---|
| Archivo único | `Juego.zip`, `Juego.rar`, `Juego.7z` |
| División simple | `Juego.zip.001`, `.002`… / `Juego.7z.001`… |
| Zip dividido | `Juego.z01`, `Juego.z02`… + `Juego.zip` |
| RAR nuevo | `Juego.part1.rar` … `Juego.partN.rar` |
| RAR antiguo | `Juego.rar` + `Juego.r00`, `Juego.r01`… |
| Con contraseña | se abre un diálogo pidiendo la contraseña (avisa si es incorrecta) |
| Imagen de ShadowMount+ | `Juego.exfat` suelto o dentro de cualquier formato anterior. Va entera a la carpeta de imágenes (Ajustes) |

La app espera a que lleguen **todas las partes** y a que su tamaño **deje de cambiar** antes de empezar (puedes dejar una descarga terminando).

## Lo que hace solo

- **Carpetas "envoltorio"**: baja por las carpetas hasta encontrar la que tiene `EBOOT.BIN` o `sce_sys/param.sfo` y envía solo esa.
- **Carpeta `dec`**: si el archivo tiene la carpeta del juego (`PPSA…-app0`) **y** una carpeta `dec` al lado, el contenido de `dec` sobrescribe el del juego (igual que copiar el juego y luego `dec` encima). Los archivos reemplazados ni se envían.
- **Reanudación**: antes de enviar, pregunta a la PS5 qué hay ya. Los archivos completos se saltan (ni se extraen); uno a medias continúa donde se quedó si el servidor acepta `APPE`, si no se reenvía entero.
- **Cerrar y volver a abrir**: la cola se guarda; al volver a abrir, regresa sola y continúa donde se quedó. Un juego ya enviado vuelve como terminado (no se envía de nuevo); añadir sus archivos otra vez lo reenvía.
- **Datos**: en Windows, ajustes, cola y log están en `%LOCALAPPDATA%\Ferry` (migrados automáticamente de la carpeta antigua `PS5Sender`, que no se borra); en self-hosted, en `/data`.
- **Verificación**: al final comprueba el tamaño de cada archivo en la PS5. Solo después (y si activas la opción) borra las partes originales.

## Documentación (en portugués)

- [Arquitectura y flujo](docs/ARQUITETURA.md) — cómo funciona la extracción en streaming
- [Compatibilidad FTP con la PS5](docs/FTP-PS5.md) — qué soporta ftpsrv y por qué importa
- [Pruebas E2E](docs/TESTES.md) — cómo ejecutarlas y qué se verifica
- [Último informe E2E](e2e_report.md) · [E2E web (Docker)](e2e_report_web.md)

## Compilar

Requiere el [SDK de .NET 10](https://dotnet.microsoft.com/download).

```bash
dotnet publish app -c Release -o dist                  # Windows: dist/Ferry.exe (~63 MB, 7-Zip incluido)
docker build -t ferry .                                # imagen Docker
dotnet publish web -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true   # binario Linux (necesita 7zz al lado o en el PATH)
```

El CI (GitHub Actions) ejecuta el E2E en Windows y Linux, construye la imagen Docker y ejecuta el E2E web contra ella (Chromium), y guarda el `.exe` y los binarios Linux como artefactos. La imagen se publica en GHCR en cada push a `main` (`:main`) y en las tags. Para lanzar una versión, crea una tag `v*` — p. ej. `git tag v1.3.0-beta.1 && git push --tags` (con `-` es pre-release) — y el CI publica la imagen (`:1.3.0-beta.1` y `:latest`) y adjunta el `.exe` y los `.tar.gz` de Linux a la release.

## Limitaciones conocidas

- **Notificación en la PS5**: no implementada. Ni ftpsrv ni etaHEN exponen notificaciones por red; haría falta un payload ELF propio (SDK de PS5) enviado a elfldr.
- Pausar/reanudar o volver a abrir hace que 7-Zip relea el archivo desde el principio (no reenvía lo que ya está en la PS5, pero gasta CPU/disco).
- Los archivos grandes van por una sola conexión (7-Zip entrega un archivo a la vez); las conexiones paralelas aceleran los archivos pequeños.

## Créditos

- [7-Zip](https://www.7-zip.org/) (LGPL + restricción unRAR) — extracción
- [FluentFTP](https://github.com/robinrodricks/FluentFTP) (MIT) — cliente FTP
- [Geist](https://github.com/vercel/geist-font) (OFL, `app/fonts/OFL.txt`) — fuente de la interfaz, incluida en el exe y en el servidor web
- [ps5-payload-ftpsrv](https://github.com/john-tornblom/ps5-payload-ftpsrv) y [etaHEN](https://github.com/etaHEN/etaHEN) — servidores FTP en la PS5

Licencia: [MIT](LICENSE).
