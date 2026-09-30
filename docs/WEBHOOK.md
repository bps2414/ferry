# Webhook

[English](en/WEBHOOK.md) · **Português (BR)** · [Español](es/WEBHOOK.md)

Em **Configurações → Webhook**, escolha Discord, ntfy ou JSON genérico, informe a URL e ative os avisos. Disponível na web e no app Windows. Começa desligado; configurações antigas não ativam nenhum envio.

O botão **Testar webhook** usa os valores salvos, mesmo com os avisos automáticos desligados. Na web, aguarda as edições pendentes antes de enviar. “O receptor aceitou a mensagem” significa uma resposta HTTP de sucesso, não confirmação de exibição no celular.

## Eventos

| Evento | Quando |
|---|---|
| `completed` | O jogo foi enviado, publicado e conferido no PS5 |
| `error` | Uma falha coloca o jogo em Erro; inclui o aviso de jogo já instalado |
| `password_required` | Primeiro pedido manual de senha; senhas conhecidas e tentativas incorretas não repetem o aviso |
| `test` | Botão de teste; `job` é `null` |

Pausar, cancelar, retentativas internas do FTP e restaurar jogos concluídos não geram avisos. A notificação vem do servidor/app, não do navegador: continua com a página fechada enquanto o Ferry estiver rodando.

## Idioma

- Português ou English explícitos seguem o campo Idioma da Fase 4.
- Automático na web guarda o idioma efetivo do navegador quando você configura/testa o webhook ou altera o idioma. Abrir outro navegador não muda o idioma dos avisos. Esse valor persiste ao reiniciar; configurações antigas usam inglês até serem configuradas.
- Automático no Windows segue o idioma de exibição original do Windows: português → pt-BR; inglês e demais → en. O app WPF e seus avisos seguem a mesma escolha explícita `Settings.Language` da web. Trocar idioma ou configurar o webhook no Windows salva esse idioma automático em `Settings.WebhookAutoLocale`; pela web, usa o navegador.
- As chaves dos eventos e do JSON não são traduzidas.

## Receptores

- **Discord:** cole a URL do webhook do canal. Envia JSON com `content`, `allowed_mentions.parse: []` e `wait=true`; não cria bot e não permite menções por nomes de jogos. Parâmetros existentes, como `thread_id`, são preservados. [API oficial](https://docs.discord.com/developers/resources/webhook#execute-webhook).
- **ntfy:** cole a URL inteira do tópico, por exemplo `https://ntfy.sh/meu-topico`. Envia POST de texto UTF-8, com título Ferry. Também aceita um ntfy self-hosted em HTTP. O tópico precisa permitir publicação sem cabeçalhos de autenticação adicionais. [API oficial](https://docs.ntfy.sh/publish/).
- **JSON genérico:** qualquer receptor HTTP que aceite o contrato abaixo. `Content-Type: application/json`; sucesso é HTTP `2xx`.

```json
{
  "schemaVersion": 1,
  "event": "completed",
  "occurredAt": "2026-09-30T15:00:00+00:00",
  "language": "pt-BR",
  "job": {
    "name": "Jogo",
    "title": "Título do jogo",
    "titleId": "PPSA12345"
  },
  "title": "Envio concluído",
  "message": "Título do jogo concluído."
}
```

Título/ID ausentes ficam como strings vazias; a mensagem usa o nome do arquivo como fallback. Mensagens de erro trazem um resumo seguro; os diagnósticos completos continuam no log local. Senhas, caminhos locais e URL do webhook não são incluídos no payload. Nomes/títulos são limitados e as mensagens são truncadas para respeitar os receptores.

## Entrega e dados

Uma tentativa por evento, timeout de 10 segundos e fila limitada a 64 avisos em memória. Fila cheia descarta o aviso e registra a falha; encerrar o Ferry cancela os envios e perde pendências. Não há reenvio automático, fila persistente ou garantia de entrega. Falhas HTTP, inclusive `429`, redirecionamentos, timeout e conexão recusada são registradas sem mudar o jogo nem bloquear o FTP ou o diálogo de senha.

Cada aviso captura jogo, URL, serviço e idioma no momento do evento. Alterações nas configurações valem para os próximos eventos; avisos já enfileirados usam a configuração anterior.

Os campos `WebhookEnabled`, `WebhookKind`, `WebhookUrl` e `WebhookAutoLocale` ficam no `settings.json`, com a gravação atômica existente. A URL aparece mascarada no formulário, mas é armazenada em texto e pode conter um token: proteja o arquivo de dados e os backups. A API de configuração e o endpoint `POST /api/webhook/test` exigem login; a URL não aparece no SSE, nos catálogos públicos ou nos logs do webhook.

Um destino por instalação; sem templates personalizados, cabeçalhos extras, anexos ou comandos remotos.
