# Roadmap

[English](../en/ROADMAP.md) · [Português (BR)](../ROADMAP.md) · **Español**

Decidido el 2026-09-30. Orden: lógica → UI/marca → self-hosted (Docker) → inglés → webhook → PKG/fPKG → envío progresivo → payload.

Estado: Fases 1, 1.5, 2 y 3 terminadas (la 3 salió en la release `v1.3.0-beta.1`). **Fase 5 implementada y validada localmente (núcleo y web en pt-BR/en); publicación de prerelease depende de CI. Fase 5.1: idioma y bandeja Windows; siguiente: Fase 6 (PKG/fPKG).**

## Fase 1 — Lógica (release `v1.1.0-beta.1`)

- **Guardado automático**:
  - cada campo se guarda al cambiar, y un valor inválido no se guarda (borde rojo + pista; valida puerto y carpeta, el host acepta hostname);
  - escritura atómica (`.tmp` + `File.Replace`) de `settings.json` y `queue.json`;
  - sin botón Guardar. El diseño actual se queda hasta la Fase 2.
- **Contraseñas conocidas**:
  - lista editable en los Ajustes, una por línea, en texto (son contraseñas públicas de sitios);
  - antes del diálogo, la app prueba cada una en silencio (`PasswordOkAsync`);
  - la contraseña que funcione en el diálogo se añade sola al final de la lista.
- **Contraseña del juego guardada**: guardada en `queue.json` con DPAPI y borrada cuando el juego sale de la cola. No la vuelve a pedir al volver a abrir.
- **Portada y título**:
  - lee `sce_sys/param.sfo` e `icon0.png` de dentro del archivo;
  - la tarjeta muestra la portada, el título y el `PPSAxxxxx`;
  - sin portada legible, muestra el nombre del archivo en tipografía grande, sin icono falso.
- **Esperando partes**: muestra qué partes faltan ("faltan part4, part5").
- **Encontrar la PS5 en la red**: al abrir, si la IP guardada no responde, recorre la subred (2121/1337) y ofrece la IP encontrada. También hay un botón "Buscar". Nunca cambia la IP sola.
- **Toast de Windows**: para terminado, error y "necesita contraseña", solo cuando la ventana no tiene el foco.
- **CI**: actions actualizadas a Node 24.
- **E2E**:
  - contraseña conocida (G6 sin diálogo) y contraseña aprendida;
  - contraseña recordada al volver a abrir;
  - guardado atómico;
  - portada/título (los juegos falsos reciben `param.sfo` + `icon0.png`).
- **Releases**: borrar la release estable antigua `v1.0.0`.
- **Añadido en uso real (PPSA11386)**:
  - log persistente en `log.txt`;
  - APPE solo en parciales empezados por la app;
  - SIZE comprobado tras cada envío, con `SELF` desactivado;
  - publicación atómica de `param.json`/`param.sfo`;
  - aviso de juego ya instalado (ver [FTP-PS5.md](FTP-PS5.md)).

## Fase 1.5 — Imagen `.exfat` de ShadowMount+ (release `v1.1.0-beta.2`)

- **Aceptada como juego**:
  - `.exfat` suelto (arrastrar, selector, carpeta vigilada), con stream directo del disco;
  - archivo comprimido (cualquier división aceptada) con una imagen `.exfat` dentro en lugar de carpeta de juego, con stream `7z -so` → FTP.
- **Destino**: campo "Imagens .exfat (ShadowMount+)" en los Ajustes (`ImageDir`, por defecto `/mnt/ext1/homebrew`). Se guarda solo y valida (ruta que empieza con `/`). El nombre remoto es el del `.exfat`.
- **Publicación atómica**: se sube como `<nombre>.exfat.ferry-part` y solo se renombra después de verificar el `SIZE`. SM+ reconoce imágenes solo por la extensión, y el sufijo la deja fuera (investigación en [FTP-PS5.md](FTP-PS5.md)).
- **Reanudación, aviso de ya instalado, log**: mismas reglas que la Fase 1.
- **Tarjeta**: sin portada (la app no abre la imagen), muestra el nombre grande.
- **E2E**: `.exfat` suelto y dentro de `.part1.rar`, pausar/reanudar, volver a abrir con parcial nuestro (APPE) y de otra versión (STOR), ya existente + "Intentar de nuevo".

## Fase 2 — Marca e interfaz: **Ferry** (release `v1.2.0-beta.1`)

- **Decidido en los mockups**: cola "Regla" (filas separadas por hilos, %, MB/s y restante en columnas fijas, barra = travesía), Ajustes en dos columnas, logo "Travesía" (dos muelles y la flecha), fuente Geist, acento azul `#6F97FF`.
- **Añadido en uso**: "Transferir ahora" en un juego en cola/pausado; el envío actual vuelve a la cola y continúa después.

- **Nombre**: Ferry, con el subtítulo "envío de juegos a la PS5".
  - Repo `ps5-sender` → `ferry` (GitHub redirige los enlaces antiguos) y exe `Ferry.exe`.
  - Los datos pasan a `%LOCALAPPDATA%\Ferry`, migrando los de `PS5Sender`.
  - Sin símbolos de Sony (△○✕□, logo PS).
- **Dirección**:
  - "PlayStation noir" editorial/tipográfico: negro azulado, casi monocromo, un acento;
  - números grandes (%, MB/s, ETA), título del juego destacado, grid rígido;
  - **prohibido**: glow, degradado morado-azul, vidrio esmerilado, cara de plantilla de IA.
- **Herramientas**: `/frontend-design` + `/impeccable`, generando mockups antes de implementar.
- **Ajustes sin scroll**: el formato se decide en esta sesión.

## Fase 3 — Self-hosted en Docker (release `v1.3.0-beta.1`) — hecha

- **Objetivo**: Ferry corre en el servidor de casa dentro de un contenedor, y desde el PC solo hay que abrir `http://<ip-del-servidor>:<puerto>` en el navegador.
- **Cómo**:
  - la app hoy es WPF (`net10.0-windows`) y no corre en un contenedor Linux. La lógica (`Engine`, `Ftp`, `Archives`, `Job`, `Settings`) pasa a ser una biblioteca `net10.0` sin WPF;
  - un servidor ASP.NET Core sirve la interfaz web (misma "Regla" y Ajustes de la Fase 2) y envía el progreso en vivo (SignalR o SSE);
  - el puerto sale de una variable de entorno, con un valor por defecto fijo.
- **Qué cambia por no ser Windows**:
  - DPAPI (contraseña del juego) → clave generada en el volumen `/data`;
  - toast de Windows → aviso dentro de la página, y notificación del sistema cuando el navegador lo permite (solo en https o localhost; el webhook de la Fase 5 cubre el resto);
  - `%LOCALAPPDATA%\Ferry` → volumen `/data` (`settings.json`, `queue.json`, `log.txt`);
  - `7z.exe`/`7z.dll` → 7-Zip de Linux: `7zip` + `7zip-rar` de Ubuntu en la imagen, `7zzs` oficial junto al binario Linux;
  - arrastrar y soltar y el selector de archivos → los dos caminos: carpeta vigilada en el volumen `/games` y subida por el navegador (arrastrar a la página).
- **Red**: recorrer la subred para encontrar la PS5 necesita `network_mode: host`, si no solo ve la red interna de Docker.
- **Entrega**: `Dockerfile` + `docker-compose.yml` de ejemplo, imagen publicada en GHCR por el CI en cada tag.
- **Linux sin Docker**: el mismo servidor web publicado como binario autocontenido `linux-x64` y `linux-arm64` en cada release. Sin app de ventana nativa para Linux (la interfaz web lo cubre).
- **Windows y web juntos**: la app WPF sigue existiendo y usa la misma biblioteca del núcleo; las dos versiones salen en cada release.
- **Login**: la interfaz web pide usuario y contraseña (definidos la primera vez), con sesión por cookie.
- **E2E**: levanta el contenedor, abre la interfaz con Playwright y repite los escenarios de envío contra el servidor FTP falso.
- **Añadido al implementar**: progreso por SSE (sin SignalR); subida por bloques con reanudación; un juego ya enviado vuelve como terminado al volver a abrir/reiniciar en lugar de caer en el aviso de "ya instalado" (vale también para la app de Windows); la prueba de conexión más reciente es la que cuenta en la tarjeta de la PS5.

## Fase 4 — Inglés

- Interfaz web en portugués e inglés: Automatic, Português (Brasil) o English en los Ajustes. Automático usa el primer idioma compatible del navegador y recurre al inglés; la elección explícita persiste en `settings.json`, incluido el acceso. WPF seguía en portugués en la Fase 4; la Fase 5.1 añade localización nativa.
- Los textos salen del código a archivos de recursos (`pt-BR`, `en`), incluidos el log visible, los avisos y los errores.
- Va después de la Fase 3 para traducir una sola interfaz (la web).
- **E2E**: ejecuta el flujo principal en los dos idiomas y comprueba que no quede texto sin traducir.
- Implementado: catálogos compartidos con claves y parámetros para núcleo, API y UI; el cambio inmediato preserva cola, subidas y diálogo de contraseña; el navegador formatea los números. Los diagnósticos originales de FTP/7-Zip/sistema conservan su contenido, y el `log.txt` existente permanece intacto. README y documentación actualizados en los tres idiomas.
- Validación: contratos enfocados con `dotnet run --project e2e -- --localization`; E2E web ejecuta rondas aisladas en `pt-BR` y `en`, con hashes, autenticación, continuación y reinicio. El modo local no demuestra Docker ni ejecución remota de CI.

## Fase 5 — Webhook configurable

- URL de webhook en los Ajustes (Discord, ntfy, JSON genérico), disparado en terminado, error y "necesita contraseña".
- El texto del mensaje sigue el idioma elegido en la Fase 4.
- Botón "Probar" que envía un mensaje de ejemplo.
- **E2E**: un servidor HTTP falso recibe el webhook y comprueba evento, juego e idioma.
- Implementado en web y Windows: activación opcional, servicio, URL oculta y prueba autenticada. Idioma automático persistido en servidor; en Windows sigue el idioma del sistema desde la Fase 5.1. HTTP en segundo plano, timeout de 10 s, cola limitada y un intento por evento; fallos no cambian el juego ni bloquean FTP/contraseña.
- Contrato y límites en [WEBHOOK.md](WEBHOOK.md). Validación: `dotnet run --project e2e -- --webhook`; E2E web cubre tres servicios, eventos, idioma, webhook de contraseña sin respuesta, reinicio y navegador cerrado. Proveedores reales y Docker local no ejercitados.

## Fase 5.1 — Windows: idioma y bandeja

Implementada y validada localmente: 124 comprobaciones WPF pasaron en ambos idiomas, con transferencia FTP y diálogo de contraseña. Siguiente: Fase 6.

- WPF en Português (Brasil) y English. Automático sigue el idioma original de Windows: portugués → pt-BR; los demás → en. `Settings.Language` se comparte con la web; Automático en la web sigue el navegador.
- Bindings actualizan la misma ventana sin sustituir campos, cola, progreso ni diálogo de contraseña. Catálogos compartidos y propios con paridad de claves/parámetros. Números, tamaños y duración siguen el idioma; diagnósticos FTP/7-Zip/sistema, rutas y nombres permanecen originales.
- Minimizar oculta en la bandeja y mantiene envíos, carpeta vigilada y webhooks. Doble clic u Open Ferry restaura normal/maximizado; pedir contraseña restaura antes del diálogo. X y Exit cierran. Docker/Linux siguen como servidor web.
- Validación: `dotnet run --project e2e/windows -c Release`, con WPF real, FTP local falso, hash tras cambiar idioma durante transferencia y contraseña escrita conservada en modal. No cubre PS5/proveedores reales ni clics físicos de bandeja.

## Fase 6 — PKG y fPKG

Investigación completa (firmwares, kstuff, instaladores) en [PKG-PS5.md](PKG-PS5.md).

- **Imágenes `.ffpkg`, `.ffpfs` y `.ffpfsc`** (ShadowMount+) aceptadas como el `.exfat`: mismo destino, `.ferry-part` y renombrar al final. Vale en cualquier firmware con jailbreak.
- **`.pkg` suelto** (fPKG de PS4 o de PS5): Ferry sirve el archivo por HTTP (con *range* y un token en la URL) y pide la instalación a etaHEN DPI (puerto 9090, `{ "url": … }`). La PS5 lo descarga directamente.
- **`.pkg` dentro de un comprimido**: enviarlo por FTP a la PS5 e instalar desde la ruta local, o extraerlo en el servidor y servirlo. Decidir después de confirmar si DPI acepta ruta local.
- **Tarjeta**: PS4 o PS5 por la cabecera del `.pkg`, con aviso cuando el tipo no corre en el firmware (p. ej. fPKG de PS5 en 12.xx y 13.xx aún no funciona).
- **Ajustes**: instalador de paquetes (etaHEN DPI, puerto), con prueba de conexión y el recordatorio de activar `DPI=1` en el `config.ini` de etaHEN.
- **E2E**: un DPI falso recibe el pedido y descarga la URL con *range* y reanudación; comprueba hash, token y el error sin DPI.
- **Antes de empezar**: revisar la investigación (la escena cambia rápido) y cerrar los puntos "por confirmar".

## Fase 7 — Envío progresivo (RAR por partes)

- **Objetivo**: con la descarga todavía en curso en la carpeta vigilada (p. ej. JDownloader en el servidor), empezar a extraer y enviar a la PS5 las partes que ya llegaron, en orden, en lugar de esperar a todas.
- **Límite del formato**:
  - `.zip` y `.7z` guardan el índice al final (última parte), así que no se puede empezar antes de ella. En esos, el comportamiento sigue como hoy;
  - viable para RAR por partes (`.partN.rar` y `.rar` + `.rNN`): los archivos vienen en secuencia, cada uno con su propia cabecera.
- **Cómo** (investigar antes):
  - 7-Zip parece abrir todos los volúmenes al principio (por confirmar);
  - alternativas: `unrar` extrayendo volumen a volumen y esperando el siguiente, o leer el RAR en secuencia por cuenta propia;
  - un archivo del juego que cruza dos partes solo termina cuando llega la siguiente. El envío espera con el FTP abierto, o cierra y continúa con `APPE` (por decidir).
- **Orden**: solo avanza con la siguiente parte de la secuencia (part3 no vale si falta la part2). Una parte que aún se descarga (nombre terminado en `.part`, tamaño cambiando) no cuenta.
- **Seguridad**:
  - la publicación atómica sigue: `param.json`/`param.sfo` solo al final, después de la última parte y de la verificación, para que ShadowMount+ no instale el juego a medias;
  - la contraseña del RAR se pide antes de empezar.
- **Tarjeta**: muestra "Enviando parte 3 de ?" mientras no se conoce el total, y "Esperando part4" cuando se detiene a esperar.
- **Ajustes**: activar/desactivar (por defecto: desactivado hasta que esté maduro).
- **E2E**:
  - un RAR por partes llega a la carpeta una parte a la vez, despacio;
  - el envío empieza antes de la última parte;
  - comprueba el SHA-256 de cada archivo en la PS5 falsa y que `param.json`/`param.sfo` solo aparecen al final;
  - `.zip`/`.7z` siguen esperando todas las partes.

## Fase 8 — Payload (hello world)

- ELF hecho con el [ps5-payload-sdk](https://github.com/ps5-payload-dev/sdk), compilado en WSL (Ubuntu, WSL2, ya instalado).
- La app envía el ELF al puerto 9021, la PS5 muestra "Ferry conectado" y el payload termina. Nada queda residente.
- **Después**: agente residente (notificación, espacio libre, lista de juegos), protocolo de envío propio e integración con el loader (investigar antes).

## Investigación, sin fecha

- **Ideas sueltas**: biblioteca de la PS5, aviso de duplicado, perfiles de consola, enviar carpeta ya extraída, límite de velocidad, auto-update, historial.
