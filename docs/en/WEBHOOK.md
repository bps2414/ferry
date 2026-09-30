# Webhook

**English** · [Português (BR)](../WEBHOOK.md) · [Español](../es/WEBHOOK.md)

In **Settings → Webhook**, choose Discord, ntfy or generic JSON, enter the URL and enable notifications. Available in the web and Windows apps. Disabled by default; upgrading an old configuration does not send anything.

**Test webhook** uses saved values, even with automatic notifications disabled. The web UI waits for pending edits before testing. “The receiver accepted the message” means a successful HTTP response, not confirmation that a phone displayed it.

## Events

| Event | Trigger |
|---|---|
| `completed` | The game has been transferred, published and verified on the PS5 |
| `installation_requested` | DPI accepted a PKG installation request; does not confirm completion on the console |
| `error` | A failure puts the game in Error, including an already-installed warning |
| `password_required` | First manual password request; known passwords and incorrect attempts do not repeat it |
| `test` | Test button; `job` is `null` |

Pause, cancellation, internal FTP retries and restored completed games do not notify. Notifications originate from the server/app and continue with the browser closed while Ferry is running.

## Language

- Explicit Português or English follows the Phase 4 language setting.
- Automatic on the web remembers the browser's effective language when configuring/testing the webhook or changing the language. Opening another browser does not change notification language. This survives restart; old configurations default to English until configured.
- Automatic on Windows follows the original Windows display language: Portuguese → pt-BR; English and all others → en. The WPF app and notifications follow the same explicit `Settings.Language` preference as the web. Changing language or configuring the webhook on Windows saves its automatic locale in `Settings.WebhookAutoLocale`; the web uses the browser.
- Event names and JSON keys are never translated.

## Receivers

- **Discord:** paste the channel webhook URL. Sends JSON `content`, `allowed_mentions.parse: []` and `wait=true`; game names cannot trigger mentions. Existing query parameters, such as `thread_id`, are preserved. No bot needed. [Official API](https://docs.discord.com/developers/resources/webhook#execute-webhook).
- **ntfy:** paste the entire topic URL, such as `https://ntfy.sh/my-topic`. Sends UTF-8 text via POST with the title Ferry; self-hosted HTTP is supported. The topic must permit publishing without additional authentication headers. [Official API](https://docs.ntfy.sh/publish/).
- **Generic JSON:** an HTTP receiver accepting this contract, with `Content-Type: application/json`; success means HTTP `2xx`.

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

Missing titles/IDs are empty strings; messages fall back to the archive name. Errors carry a safe summary; full diagnostics stay in the local log. Passwords, local paths and webhook URLs are not exported. Names/titles and messages are bounded to receiver limits.

## Delivery and data

One attempt per event, a 10-second timeout and an in-memory queue of 64 notifications. A full queue drops the notification and logs it; shutdown cancels requests and loses pending notifications. No automatic retries, persistent delivery queue or delivery guarantee. HTTP errors, including `429`, redirects, timeout and connection refusal never change game state or block FTP or the password dialog.

Each notification snapshots the game, destination, provider and language at event time. Changes affect future events; queued notifications use their previous configuration.

`WebhookEnabled`, `WebhookKind`, `WebhookUrl` and `WebhookAutoLocale` live in `settings.json` using existing atomic writes. The form masks the URL, but it is stored as plain text and may contain a token: protect the data file and backups. Settings and `POST /api/webhook/test` require login. Webhook URLs are absent from SSE, public catalogs and webhook logs.

One destination per installation; no custom templates, extra headers, attachments or remote commands.
