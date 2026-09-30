# Relatório E2E web — Ferry self-hosted (en)

- Data: 2026-09-30 12:12:12 UTC
- Resultado geral: **PASSOU**  (69s)
- Servidor: DLL local .NET, dados isolados; Chromium (Playwright). Docker não exercitado nesta rodada.
- PS5 falso: pyftpdlib imitando o ftpsrv novo (com APPE), 40 MB/s por conexão; arquivos de teste gerados com 7-Zip 24.07 (x64) : Copyright (c) 1999-2024 Igor Pavlov : 2024-06-19

| Verificação | Resultado |
|---|---|
| Catálogo público pt-BR | ✅  |
| Catálogo público en | ✅  |
| Catálogos servidos têm mesmas chaves e parâmetros | ✅  |
| Automático pt-PT, en-US | ✅ pt-BR |
| Automático en-GB, pt-BR | ✅ en |
| Automático ja-JP, pt-BR | ✅ pt-BR |
| Automático ja-JP, fr-FR | ✅ en |
| API sem login responde 401 | ✅ settings 401, events 401 |
| Primeira abertura cria o usuário | ✅ tela "Create access", senha curta: "The password must have at least 8 characters.", auth.json criado |
| Idioma inválido não altera preferência | ✅  |
| Escolha explícita precede navegador | ✅  |
| Configurações salvam sozinhas e recusam o inválido | ✅ porta "abc" → "The port must be a number from 1 to 65535." (continuou 2121); host/porta/usuário/senha salvos; pasta monitorada = \\?\C:\Users\ADMINI~1\AppData\Local\Temp\ferry-web-e2e-en\games; GET atrasado 1,5 s não desfez o que foi digitado=true |
| Testar conexão | ✅ Connected, but /mnt/ext1/homebrew does not exist (it will be created during transfer). |
| Log já recebido muda de idioma sem perder sequência | ✅  |
| Idioma durante upload preserva campo editado | ✅  |
| Idioma preserva diálogo e senha digitada | ✅  |
| Upload pelo navegador → senha no navegador → envio ao PS5 | ✅ 8 volumes .7z enviados pela página; diálogo "Password-protected archive", 1ª senha errada → "Senha incorreta", 2ª certa; estado Verificado (/mnt/ext1/homebrew/PPSA09001-Jogo Web); 7/7 SHA-256 iguais; capa e PPSA09001; senha entrou nas senhas conhecidas=true; cartão durante o envio "(o envio acabou antes de pegar o meio)" |
| Idioma preserva card pausado e progresso | ✅  |
| Idioma durante transferência mantém andamento | ✅  |
| Idioma persiste após reiniciar e recarregar | ✅  |
| Pausar e retomar pela página | ✅ congelou em 21.8% por 1,5 s e retomou |
| Reiniciar o servidor local no meio do envio | ✅ reinício da DLL local em 56.4% (300313834 bytes no PS5); voltou sozinho, continuou com APPE (2x), hash confere, sem .ferry-part; login continuou valendo=true; Jogo Web continuou Verificado ("Sent and verified before reopening") sem reenviar nada |
| Upload continua de onde parou | ✅ 1º bloco (16 MB) pela API; bloco repetido → 409; parcial não entrou na fila=true; a página continuou do byte 16777216 (3 bloco(s)); arquivo no servidor e no PS5 com o mesmo hash |
| Adicionar de novo um jogo concluído | ✅ reenvio pela página começou do byte 0 (4 blocos, sem pular por ter o mesmo tamanho); o card voltou para a fila e parou em "Game already installed on the PS5 — the …" |
| Sair e entrar de novo | ✅ depois de sair a API responde 401; senha errada → "Incorrect username or password."; certa → entrou |
| Celular (390 px) | ✅ largura da página 390px, sem rolagem lateral |
| Sem erro de JavaScript na página | ✅ nenhum |

## Capturas

![01-criar-acesso](e2e/web/report/en/01-criar-acesso.png)
![02-configuracoes](e2e/web/report/en/02-configuracoes.png)
![03-fila-vazia](e2e/web/report/en/03-fila-vazia.png)
![04-senha](e2e/web/report/en/04-senha.png)
![06-concluido](e2e/web/report/en/06-concluido.png)
![07-pausado](e2e/web/report/en/07-pausado.png)
![08-entrar](e2e/web/report/en/08-entrar.png)
![09-celular](e2e/web/report/en/09-celular.png)

Repetir: `dotnet build web -c Release`; em `e2e/web`, `npm ci`, `npx playwright install chromium`; configure `FERRY_WEB_MODE=local` e execute `node web.mjs`. Requer .NET, Python com `pyftpdlib` e 7-Zip.
