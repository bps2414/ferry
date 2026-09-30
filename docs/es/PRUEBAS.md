# Pruebas E2E

## Contratos de idiomas y navegador

`dotnet run --project e2e -- --localization` comprueba configuración antigua automática, persistencia, variantes/fallback, mensajes anidados, diagnósticos originales, códigos de error de extracción, progreso/acciones y claves/parámetros coincidentes entre catálogos del núcleo, API y UI. No requiere FTP ni juegos generados; CI lo ejecuta en Windows y Linux.

`node e2e/web/web.mjs` ejecuta el flujo completo en rondas aisladas de `pt-BR` y `en`, preservando hashes, autenticación, pausa, continuación y reinicio. Produce `e2e_report_web_pt-BR.md`, `e2e_report_web_en.md` y el agregado `e2e_report_web.md`; capturas en `e2e/web/report/<idioma>/`. Usa `FERRY_TEST_LOCALE=pt-BR` o `en` para una sola ronda. Comprueba textos explícitos, orden/fallback automático, elección explícita, rechazo de preferencias inválidas, persistencia y cambio durante subida/transferencia. Con diálogo abierto, dispara el evento del selector mediante DOM para comprobar la preservación del modal/contraseña; no simula un clic detrás del modal.

Sin Docker, ejecuta `dotnet build web -c Release`, instala dependencias y Chromium en `e2e/web`, configura `FERRY_WEB_MODE=local` y ejecuta `node web.mjs`. Inicia/reinicia la DLL local con datos, juegos y puerto temporales aislados; no valida Docker. `FERRY_WEB_DLL`, `FERRY_PYTHON` y `FERRY_7ZIP` permiten rutas explícitas. En Windows usa `app/tools/7z.exe` y `python`; en Linux busca `7zz`/`7z` y `python3`. Python requiere `pyftpdlib`.

[English](../en/TESTS.md) · [Português (BR)](../TESTES.md) · **Español**

Dos pruebas de extremo a extremo:

- `e2e/` (C#), sin interfaz: usa la misma `Engine` (Ferry.Core) contra un servidor FTP local. Corre en **Windows y Linux** (el CI corre en los dos).
- `e2e/web/` (Node + Playwright): levanta la **imagen Docker** y maneja la interfaz web en un Chromium de verdad (ver [E2E web](#e2e-web-docker) abajo).

## Ejecutar

Requiere Python 3 con `pyftpdlib` (`pip install pyftpdlib`). En Linux, también 7-Zip con RAR en el PATH (`apt install 7zip 7zip-rar`).

```bash
dotnet run --project e2e              # completa (~40 s)
dotnet run --project e2e -- G2 G7     # modo rápido: solo esos casos, sin fases extra (~15 s)
```

Genera `e2e_report.md` en la raíz y sale con código 0 (pasó) o 1 (falló). En la 1.ª ejecución descarga el RAR 6.24 oficial de rarlab.com en `e2e/tools/` (Windows: `Rar.exe` del instalador; Linux: `rar` de rarlinux), usado solo para **crear** los archivos de prueba; la app solo extrae, con 7-Zip. En Windows, 7-Zip es el `app/tools/7z.exe` incluido en la app; en Linux, el `7zz`/`7z` del sistema.

## El servidor de prueba imita a ftpsrv

`e2e/ftpserver.py` (pyftpdlib) acepta **solo los comandos de ftpsrv** y responde `502 Command not recognized` al resto — así aparecieron los problemas de `NLST`, `SIZE` y UTF-8 que un servidor completo ocultaba. Los comandos rechazados quedan en `%TEMP%\ferry-e2e\ftproot.recusados.txt`. Subida limitada a 40 MB/s por conexión (10 MB/s en los casos .exfat y "Transferir ahora", que necesitan pillar el envío a la mitad).

## Casos

Cada juego falso tiene 6 archivos (~24 MB incompresibles, un nombre con acento, un archivo vacío) dentro de 2 carpetas envoltorio, en volúmenes de 5 MB. Son deterministas y quedan en caché (`%TEMP%\ferry-e2e-cache-v3`; cada uno tiene `param.sfo` real, `param.json` e `icon0.png` de verdad; cambia `GenVersion` al modificar el generador).

| Caso | Formato |
|---|---|
| G1 | `.zip.001…` (7-Zip) |
| G2 | `.z01…` + `.zip` (generador propio de zip dividido PKWARE) |
| G3 | `.part1.rar…` (RAR5) |
| G4 | `.rar` + `.r00…` (RAR4, nombres antiguos) |
| G5 | `.7z.001…` |
| G6 | `.7z.001…` con contraseña y cabeceras cifradas, añadido arrastrando y soltando; abre con la 2.ª contraseña conocida, sin diálogo |
| G7 | `.rar` único con `PPSA…-app0` + `dec` (el `dec` va primero en el archivo) |

Para cada caso: todas las partes menos la última → comprueba que queda "Esperando partes"; la última llega escrita despacio (simula una descarga) → comprueba el SHA-256 de **cada** archivo recibido, que no haya archivos de más y que los originales se borren.

## Fases extra (ejecución completa)

En todos los casos, el E2E también comprueba portada/título (`param.sfo` + `icon0.png`) y "falta <volumen>" (rar y zip dividido). Comprueba además la publicación atómica: por el orden del log del servidor, el `RNTO` de `param.json`/`param.sfo` viene después del último `STOR`/`APPE` del juego.

- **Ya en la PS5, sin APPE**: un `EBOOT.BIN` completo no se extrae ni se reenvía; un `big.bin` a medias, no empezado por la app, se reenvía entero con `STOR`.
- **Ya en la PS5, con APPE y SELF** (imita el ftpsrv nuevo, cuyo `SIZE` del eboot.bin miente con `SELF` activado):
  - `big.bin` a medias registrado en la cola: solo la mitad que faltaba va con `APPE`;
  - `EBOOT.BIN` más pequeño y de otra versión: `STOR` entero;
  - `icon0.png` más grande en la PS5: queda del tamaño correcto.
- **Juego ya instalado**: avisa sin enviar; "Intentar de nuevo" reenvía encima.
- **Imagen `.exfat` (ShadowMount+)**: servidor con APPE, `ImageDir = /data/homebrew`. Los archivos (`IMG1.exfat` de 40 MB suelto e `IMG2.exfat` de 12 MB dentro de `.part1.rar`) tienen bytes aleatorios deterministas y quedan en caché.
  - envío nuevo: hash igual, sin portada, sin restos de `.ferry-part`, y por el orden del log del servidor el `RNTO` al nombre final viene después del último `STOR`/`APPE`. Pausar y reanudar a la mitad continúa con `APPE`;
  - volver a abrir: el parcial registrado en la cola continúa con `APPE` (solo la mitad que faltaba) y el parcial más pequeño de otra versión va entero con `STOR`;
  - ya existente: avisa "Juego ya instalado…" sin enviar, y "Intentar de nuevo" reenvía encima.
- **Transferir ahora**: con una imagen de 80 MB (generada al momento) enviándose, `SendNow` en IMG2 (`.part1.rar`) → la imagen vuelve a `NaFila` (no se pausa), IMG2 queda `Verificado` primero, y la imagen continúa solo con lo que faltaba (`STOR` + `APPE` = tamaño total).
- **Log persistente**: `log.txt` tiene el comando y la respuesta de STOR/APPE/SIZE.
- **Contraseña aprendida y recordada** (G6, en la fase cerrar/volver a abrir):
  - diálogo con la 1.ª contraseña incorrecta;
  - la correcta entra en las contraseñas conocidas;
  - al volver a abrir sin contraseñas conocidas, abre con la contraseña de la cola (cifrada: DPAPI en Windows, AES-GCM en Linux), sin diálogo.
- **Guardado atómico**: un `queue.json.tmp` a medias no impide volver a abrir.
- **Cerrar y volver a abrir**: detiene la engine a mitad del envío, crea otra con la misma `queue.json`; la cola vuelve sola y no se reenvía nada completo.
- **Pausar/reanudar** a mitad del stream; **quitar de la cola** y volver a añadir; **probar conexión** con contraseña correcta e incorrecta.

## Contratos de webhook

`dotnet run --project e2e -- --webhook` usa HTTP local falso, sin FTP ni credenciales reales: configuración antigua, validación/persistencia, HTTP 401/429/500/302, timeout, conexión rechazada, tres formatos, pt-BR/en/Automático, fallback Windows, límites, secretos, cola llena, snapshots y cierre. CI lo ejecuta en Windows y Linux. E2E web comprueba juegos/FTP de prueba, eventos, contraseña incorrecta sin duplicación, esperar cambios guardados, tres receptores, idioma tras reinicio, navegador cerrado y error HTTP sin afectar hashes FTP.

Los tests no contactan proveedores reales. Informes distinguen DLL local y Docker; ejecución local no demuestra CI remota. Configuración y entrega en [WEBHOOK.md](WEBHOOK.md).

## E2E web (Docker)

`e2e/web/web.mjs` levanta la imagen con `--network host` y `--user <tu uid>` (como en `docker-compose.yml`), el mismo `ftpserver.py` (con APPE) y abre la página en un Chromium (Playwright). Genera `e2e_report_web.md` en la raíz y las capturas en `e2e/web/report/`.

```bash
docker build -t ferry:e2e .
cd e2e/web && npm ci && npx playwright install chromium && node web.mjs
```

Requiere Docker, Python con `pyftpdlib` y 7-Zip (para crear los archivos de prueba). Qué comprueba:

- **Login**: sin login la API responde 401; la primera vez pide crear el usuario (una contraseña corta se rechaza); salir → 401; contraseña incorrecta → mensaje; correcta → entra.
- **Ajustes**: cada campo se guarda solo; un puerto inválido muestra el error y no se guarda; la carpeta vigilada ya viene como `/games`; "Probar conexión" y la tarjeta de la PS5 quedan "online" (una prueba antigua no sobrescribe la nueva).
- **Subida + contraseña en el navegador**: los volúmenes de un `.7z.001…` con contraseña y cabeceras cifradas se suben por la página; el diálogo de contraseña se abre en el navegador (1.ª incorrecta → "Contraseña incorrecta", 2.ª correcta); SHA-256 de cada archivo en la PS5 falsa, portada y `PPSA…`, contraseña aprendida.
- **Carpeta vigilada + pausar/reanudar** desde la página (el progreso se congela) y **reiniciar el contenedor** durante el envío de una imagen `.exfat` de 400 MB: vuelve solo, continúa con `APPE`, el login sigue valiendo, y el juego ya terminado vuelve como terminado sin reenviar nada.
- **Subida que continúa**: 1er bloque enviado por la API, bloque repetido → 409, el parcial no entra en la cola, y la página continúa desde el byte 16 MB; el hash coincide en el servidor y en la PS5.
- **Móvil** (390 px): sin scroll horizontal; **ningún error de JavaScript** en la página.
