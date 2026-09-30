# Webhook

[English](../en/WEBHOOK.md) · [Português (BR)](../WEBHOOK.md) · **Español**

En **Ajustes → Webhook**, elija Discord, ntfy o JSON genérico, indique la URL y active los avisos. Disponible en la web y Windows. Desactivado inicialmente; actualizar una configuración antigua no envía avisos.

**Probar webhook** usa los valores guardados, incluso con los avisos automáticos desactivados. La web espera las ediciones pendientes antes de probar. Una respuesta HTTP de éxito confirma que el receptor aceptó el mensaje, no que se mostró en el teléfono.

## Eventos

| Evento | Cuándo |
|---|---|
| `completed` | El juego se ha enviado, publicado y verificado en la PS5 |
| `installation_requested` | DPI aceptó una solicitud de instalación PKG; no confirma el final en la consola |
| `error` | Un fallo deja el juego en Error, incluido el aviso de juego ya instalado |
| `password_required` | Primera solicitud manual de contraseña; contraseñas conocidas e intentos incorrectos no repiten el aviso |
| `test` | Botón de prueba; `job` es `null` |

Pausar, cancelar, reintentos internos del FTP y restaurar juegos terminados no generan avisos. El servidor/app envía las notificaciones con el navegador cerrado mientras Ferry sigue funcionando.

## Idioma

- Português o English explícitos siguen el ajuste de idioma de la Fase 4.
- Automático en la web guarda el idioma efectivo del navegador al configurar/probar el webhook o cambiar el idioma. Abrir otro navegador no lo cambia. Persiste al reiniciar; configuraciones antiguas usan inglés hasta configurarlo.
- Automático en Windows sigue el idioma original de Windows: portugués → pt-BR; inglés y los demás → en. La app WPF y los avisos siguen la misma elección explícita `Settings.Language` de la web. Cambiar idioma o configurar el webhook desde Windows guarda su idioma automático en `Settings.WebhookAutoLocale`; la web usa el navegador.
- Los nombres de eventos y claves JSON no se traducen. No hay catálogo de interfaz en español.

## Receptores

- **Discord:** URL del webhook del canal; JSON con `content`, `allowed_mentions.parse: []` y `wait=true`. Los nombres de juegos no activan menciones. Conserva parámetros como `thread_id`; no necesita bot. [API oficial](https://docs.discord.com/developers/resources/webhook#execute-webhook).
- **ntfy:** URL completa del tema, por ejemplo `https://ntfy.sh/mi-tema`; POST de texto UTF-8 con título Ferry. También admite ntfy self-hosted en HTTP. El tema debe permitir publicación sin cabeceras de autenticación adicionales. [API oficial](https://docs.ntfy.sh/publish/).
- **JSON genérico:** receptor HTTP del contrato siguiente; `Content-Type: application/json`. Éxito: HTTP `2xx`.

```json
{
  "schemaVersion": 1,
  "event": "completed",
  "occurredAt": "2026-09-30T15:00:00+00:00",
  "language": "en",
  "job": { "name": "Game", "title": "Game title", "titleId": "PPSA12345" },
  "title": "Transfer complete",
  "message": "Game title complete."
}
```

Títulos/IDs ausentes son cadenas vacías; el mensaje usa el nombre del archivo como alternativa. Errores incluyen un resumen seguro; el diagnóstico completo queda en el log local. No exporta contraseñas, rutas locales ni URL del webhook. Nombres/títulos y mensajes tienen límites de longitud.

## Entrega y datos

Un intento por evento, timeout de 10 segundos y cola en memoria de 64 avisos. Una cola llena descarta el aviso y registra el fallo; cerrar Ferry cancela envíos y pierde pendientes. Sin reenvío automático, cola persistente ni garantía de entrega. Errores HTTP, incluso `429`, redirecciones, timeout y conexión rechazada no cambian el juego ni bloquean FTP o el diálogo de contraseña.

Cada aviso captura juego, destino, servicio e idioma al ocurrir el evento. Cambios afectan eventos futuros; avisos ya encolados usan la configuración anterior.

`WebhookEnabled`, `WebhookKind`, `WebhookUrl` y `WebhookAutoLocale` se guardan en `settings.json`, con escritura atómica. La URL aparece oculta en el formulario, pero se almacena como texto y puede contener un token: proteja el archivo y las copias. Ajustes y `POST /api/webhook/test` requieren login. La URL no aparece en SSE, catálogos públicos ni logs del webhook.

Un destino por instalación; sin plantillas personalizadas, cabeceras extra, adjuntos ni comandos remotos.
