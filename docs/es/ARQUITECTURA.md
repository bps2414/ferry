# Arquitectura

[English](../en/ARCHITECTURE.md) · [Português (BR)](../ARQUITETURA.md) · **Español**

.NET 10. La lógica está en `core/` (sin interfaz) y tiene dos caras: la app de Windows (WPF, un solo `.exe`) y el servidor web self-hosted (Docker o binario Linux).

| Archivo | Función |
|---|---|
| `core/Engine.cs` | Cola: revisa las entradas, agrupa partes, decide cuándo empezar, procesa un juego a la vez, guarda la cola |
| `core/Archives.cs` | 7-Zip: agrupar volúmenes, listar, probar contraseña, abrir el stream `7z x -so`, encontrar la carpeta del juego y el `dec` (o la imagen `.exfat`) |
| `core/Ftp.cs` | FTP: estado remoto (`SIZE`), envío en streaming con conexiones paralelas, verificación |
| `core/Job.cs` | Un elemento de la cola (estado, progreso, velocidad, ETA) |
| `core/Settings.cs` | Ajustes en `settings.json` en la carpeta de datos (`%LOCALAPPDATA%\Ferry`, o `FERRY_DATA`) |
| `core/Webhooks.cs` | Avisos HTTP compartidos: cola limitada en memoria, snapshots inmutables, idioma y prueba; callbacks Done/askPassword sin cambiar FTP |
| `core/Secret.cs` | Contraseña del juego guardada en la cola: DPAPI en Windows, AES-GCM con `secret.key` (600) fuera de él |
| `core/Discovery.cs` | Búsqueda de la PS5 en la red (/24 de cada interfaz, puertos 2121 y 1337) |
| `app/MainWindow.xaml(.cs)` | Interfaz de Windows; `App.xaml.cs` extrae el `7z.exe` incluido y pasa la ruta al core |
| `web/Program.cs` | Servidor web: API, cookie de login, ajustes campo por campo, SSE, interfaz incluida |
| `web/Hub.cs` | Puente Engine ↔ navegador: foto de la cola, log, avisos, contraseña pedida al navegador |
| `web/Auth.cs` | Un usuario, creado la primera vez (`auth.json`, PBKDF2-SHA512); límite de intentos |
| `web/Uploads.cs` | Subida por bloques con reanudación |
| `web/ui/` | Interfaz web (HTML/CSS/JS puro), incluida en el binario |

## Self-hosted (web)

- **7-Zip**: el core busca `7zz`/`7z` junto al programa y en el PATH. En la imagen Docker es el `7zip` + `7zip-rar` de Ubuntu; en el `.tar.gz` de Linux va el `7zzs` oficial (estático) junto a `ferry`.
- **Progreso en vivo**: `GET /api/events` (SSE). Cada 250 ms el servidor arma la foto de la cola (`Hub.Snapshot`) y solo la envía si cambió; también envía las líneas nuevas del log y los avisos. Los comandos (pausar, reanudar, transferir ahora…) van por `POST`.
- **Contraseña del archivo**: el `askPassword` de la Engine se convierte en un pedido pendiente (`TaskCompletionSource`) que aparece en la foto; el navegador abre el diálogo y responde en `POST /api/jobs/{id}/password`. Pausar, cancelar o quitar el juego cierra el pedido.
- **Login**: cookie `HttpOnly`/`SameSite=Strict`; las claves de la cookie están en `/data/keys`, así que el login sobrevive al reinicio del contenedor. Toda la API (salvo `/api/auth/*`) exige login.
- **Subida**: `POST /api/uploads` (nombre, tamaño, fecha) devuelve un id estable y el byte desde donde continuar; `PUT /api/uploads/{id}?offset=N` añade un bloque (offset distinto al del servidor → `409` con el offset correcto). El parcial queda en `<carpeta>/.ferry-upload/` — la Engine solo mira la raíz de la carpeta — y solo recibe el nombre final cuando está completo, así que un `.exfat` a medias nunca entra en la cola.
- **Reinicio**: lo enviado y verificado queda en `queue.json` (`Done`); al volver a abrir, el juego cuyas partes siguen en la carpeta vuelve como terminado en lugar de entrar de nuevo en la cola. Añadir los archivos otra vez (arrastrar, selector, subida) quita esa marca.

## Flujo de un juego

```
archivos en la carpeta / selector / arrastrar
        │
        ▼
Agrupar volúmenes por nombre base ──► ¿faltan partes? ── "Esperando partes"
        │ nombres completos
        ▼
¿Tamaño estable por N s? ──► 7z l (comprueba volúmenes) ──► "En cola"
        │
        ▼
7z l -slt   → lista de elementos (ruta, tamaño, cifrado)
¿contraseña? → 7z t en el 1er elemento cifrado (barato) → diálogo hasta acertar
Plan()      → destino de cada elemento: carpeta del juego, dec encima, envoltorios descartados
        │
        ▼
SIZE de cada archivo en la PS5 → lista "need" (solo lo que falta)
        │
        ▼
7z x -so @lista  ──stdout──► lector único
                                ├─ archivo ≤ 4 MB → RAM → canal → N-1 conexiones paralelas
                                └─ archivo > 4 MB → stream directo en la conexión principal
        │
        ▼
SIZE de nuevo → verifica todo → "Terminado" → (opcional) borra las partes originales
```

## Por qué streaming

Extraer al disco y luego enviar exige espacio libre igual al juego extraído (decenas de GB). Con `7z x -so`, 7-Zip escribe todos los archivos concatenados en stdout, **en el orden del listado** (`7z l`). Como los tamaños vienen del listado, la app corta el stream en archivos y envía cada uno directo a un socket FTP. Nada extraído toca el disco, y el pipe da contrapresión natural: 7-Zip solo avanza cuando el FTP consume.

Con `@lista` (archivo con las rutas, `-scsUTF-8 -spd`), 7-Zip extrae **solo** los elementos que aún faltan en la PS5 — eso permite pausar, cerrar y volver a abrir sin reenviar nada completo.

## Agrupación de volúmenes

Regex por nombre de archivo (`Archives.Group`):

| Patrón | Clave | Archivo que abre 7-Zip | 1er índice |
|---|---|---|---|
| `X.(zip|7z|rar).NNN` | `X.ext` | `.001` | 1 |
| `X.partN.rar` | `X.rar` | `part1` | 1 |
| `X.zNN` + `X.zip` | `X.zip` | `X.zip` | 1 |
| `X.rNN` + `X.rar` | `X.rar` | `X.rar` | 0 |

"Completo por el nombre" = índices contiguos (y el archivo principal presente). Aun así puede faltar el último volumen sin hueco en la numeración (p. ej. `.001`–`.004` de 5), así que la app igual ejecuta `7z l` y solo encola si no hay "Missing volume / Unexpected end". Después del `7z l`, vuelve a comprobar la firma (nombre+tamaño+fecha) de las partes: si llegó un volumen durante el `7z l`, reinicia la ventana de estabilidad.

## Carpeta del juego y `dec`

`FindGameRoot`: la carpeta menos profunda con `EBOOT.BIN` o `sce_sys/param.sfo`, ignorando carpetas llamadas `dec` (que también tienen `EBOOT.BIN`). `Plan`: si existe `dec/` junto a la carpeta del juego (o `dec/<carpeta del juego>/`), cada archivo de `dec` se convierte en destino y el archivo del juego con la misma ruta se descarta.

## Imagen `.exfat` (ShadowMount+)

- `.exfat` suelto: `Archives.Group` lo trata como archivo único (`X.exfat`). La `Engine` salta el `7z l` y arma una lista de un solo elemento. El envío lee un `FileStream` del disco en vez del stdout de 7z. Al reanudar con `APPE`, lo ya enviado se salta con `Seek`, sin leerlo.
- Dentro de un comprimido: si `Plan` no encuentra carpeta del juego, `Archives.ImagePlan` elige los elementos `.exfat` (por el nombre del archivo, sin las carpetas internas) y descarta el resto. De ahí en adelante es el mismo stream `7z x -so`.
- Destino `Settings.ImageDir`. La imagen entra en `Engine.Held`, así que se sube como `.ferry-part` y se renombra al final, como el `param.json`.

## Interfaz

- El progreso llega de muchos hilos cada pocos KB; `Job.Report` lo limita a 4 actualizaciones/s (si no, el dispatcher de WPF se ahoga y la pantalla se congela).
- `BindingOperations.EnableCollectionSynchronization` permite modificar la cola desde hilos de fondo.
- Aspecto (Fase 2, "Ferry"): tokens y estilos en `App.xaml`; fuente Geist incluida (`app/fonts`, `pack://application:,,,/Ferry;component/fonts/#Geist`), números tabulares en toda la ventana. La barra de progreso es la "travesía" del logo (muelles en los extremos, flecha en la punta del progreso); `OnProgress` anima el valor hasta el nuevo en 350 ms, y el color cambia según el estado con `ColorAnimation` en los `DataTrigger`. El icono (`Ferry.ico`) usa la misma geometría 16×16 que `LogoPosts`/`LogoArrow`.
- **Transferir ahora** (`Engine.SendNow`): mueve el juego arriba y el que se estaba enviando justo detrás; este vuelve a `NaFila` (en cola) y se cancela su `Cts`. `RunAsync` toma el primer `NaFila` de la lista; el interrumpido después envía solo lo que falta (misma reanudación que la pausa).
- Cola guardada en `queue.json` en la carpeta de datos (archivos añadidos, elementos quitados, contraseñas cifradas, envíos empezados y juegos terminados).
