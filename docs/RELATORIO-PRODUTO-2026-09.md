# Entrega de produto e próxima fase — 2026-09-30

UX implementada e integrada na `main`; pesquisa e plano da próxima fase entregues. **Fase 6.1 é uma proposta de diagnóstico, ainda sem implementação.** Nenhum payload foi criado ou enviado.

## Produto entregue

- **Desligar este PC ao terminar as transferências**: opção desativada por padrão, válida só na sessão atual. Exige trabalho pendente ao armar; fila vazia e histórico concluído não disparam. Após sucesso de todos os itens, oferece 60 segundos visíveis para cancelar e restaura a janela da bandeja.
- Pausa, erro, cancelamento, partes faltantes, senha pendente, instalação em andamento e resultado incerto bloqueiam a contagem. Mudança da fila ou das fontes cancela; retomada reinicia os 60 segundos. Há uma nova conferência antes da ação. PKG preparado ou pedido aceito significa fim da transferência, sem comprovar instalação no PS5.
- **Impedir suspensão durante envios**: opção explícita da sessão, liberada quando o trabalho ativo para ou o Ferry fecha. A contagem de desligamento também mantém o PC acordado. Não altera plano de energia nem mantém a tela acesa.
- Serviço de energia injetável. O adaptador Windows usa `shutdown.exe /s /t 0`, sem `/f`, com espera máxima de 5 segundos e sem repetição automática. Testes usam exclusivamente energia fake.
- Painel acessível em todas as páginas WPF, textos pt-BR/en, idioma sem perder estado e cancelamento sempre disponível enquanto armado. README, documentos de testes e roadmap atualizados em pt-BR/en/es. A CI Windows passa a executar o seletor `--power`.

Uso e limites em [UX-PRODUTO.md](UX-PRODUTO.md). Capturas da execução integrada com fila sintética e energia fake: [pt-BR](../e2e/windows/report/power-pt-BR.png) e [en](../e2e/windows/report/power-en.png).

## Pesquisa e decisão de produto

[Pesquisa primária, cenário e comparação](PESQUISA-CENARIO-PS5-2026-09.md), corte 2026-09-30: quatro clientes (ps5upload, pkg-sender, PSS e FileZilla) e ftpsrv; comparação ao código real do Ferry, releases e SHAs consultados. Firmware, exploit, loader, HEN, serviço e formato foram separados. Alegações de autores não viraram resultados de hardware ou desempenho nossos.

**Próxima fase recomendada: diagnóstico de serviços e destinos.** Verificar FTP autenticado e cada pasta configurada; mostrar alcance TCP do DPI com o limite dessa evidência, horário, cancelamento, prazo e revisão de configuração. Sem escrita remota, instalação, envio de payload ou inferência de firmware. [Plano com contratos, falhas, aceite e validação](plans/next/proxima-fase-sem-payload.md).

RAR progressivo ficou como prova técnica futura com UnRAR, sem parser próprio e sem viabilidade integrada comprovada. Payload foi adiado. Limite de velocidade, pasta com manifesto e HTTP Range de PKG estão no ranking de oportunidades, sem ampliar a fase proposta.

A pesquisa registrou a divergência entre README e release ps5upload v5.40.0, atribuindo os testes relatados ao autor. A release atual do FileZilla não foi confirmada. HTTP inicial: 39/39 acessíveis; checagem final: 36/39, com três APIs GitHub retornando 403 por rate limit. Essa checagem final não foi declarada integralmente aprovada; fontes previamente lidas e links diretos estão documentados.

## Validação na árvore integrada

Comandos executados na raiz, salvo os indicados em `e2e/web`. Todos os testes usam dados isolados e servidores locais falsos. A validação ocorreu depois das duas entregas e da revisão; mudanças posteriores foram somente documentação e evidências.

| Comando | Resultado final |
|---|---|
| `dotnet run --project e2e -- --localization` | Exit 0; 1771 checks |
| `dotnet run --project e2e -- --webhook` | Exit 0; 48 checks |
| `dotnet run --project e2e` | Exit 0; compactados completos, hashes, retomada, imagens, senha, publicação e exclusão de originais aprovados |
| `dotnet run --project e2e -- --pkg` | Exit 0; 247 checks FTP/DPI falsos |
| `dotnet run --project e2e/windows -c Release` | Exit 0; 170 checks WPF após todos os ajustes |
| `dotnet run --project e2e/windows -c Release -- --power` | Exit 0; 188 checks de energia fake |
| `docker build -t ferry:e2e .` | Indisponível: CLI Docker ausente; WinError 2 |
| `dotnet build web -c Release` | Exit 0; zero avisos e erros |
| `npm ci` em `e2e/web` | Exit 0; dependências instaladas, zero vulnerabilidades reportadas |
| `$env:FERRY_WEB_MODE='local'; node web.mjs` em `e2e/web` | Exit 0; Chromium com DLL local, 57 checks pt-BR e 57 en |

Conferência documental final: UTF-8, JSON, quebras de linha e whitespace aprovados; 122 links locais e nove âncoras existentes; `git diff --check` exit 0. Os seis links GitHub relativos a releases e a formatação inicial dos READMEs foram confirmados sem alteração na base `ec0ebbb`, sem tratá-los como caminhos locais quebrados.

Evidências detalhadas: [core](../e2e_report.md), [web pt-BR](validation/2026-09-30/web-pt-BR.md) e [web en](validation/2026-09-30/web-en.md). Relatórios web desta rodada foram preservados nesses documentos; capturas web temporárias foram arquivadas localmente e os artefatos anteriores restaurados. Os logs completos, resultados JSON e capturas brutas estão em `%TEMP%\ferry-produto-20260930` nesta máquina.

A revisão cruzada encontrou duas falhas que foram corrigidas: fonte terminal alterada desde o armamento e espera nativa sem prazo. O teste de fonte foi reproduzido em vermelho antes da correção. Também se verificaram hold dos 60 segundos e envio FTP local com `DeleteOriginal`, preservando a exclusão automática sem permitir que uma nova fonte seja confundida com sucesso antigo.

**Limites:** Docker, Linux, CI remota, PS5 e energia nativa não foram exercitados. Capturas WPF confirmam o painel no tamanho padrão nos dois idiomas; não comprovam cliques físicos, leitor de tela ou execução do comando de energia. Os testes locais não comprovam instalação no console nem vantagem de desempenho sobre concorrentes.

## Integração e entrega local

Brisa e Farol rodaram em **Codex GPT-6.1 Sol high via Maestri**, em duas worktrees com arquivos disjuntos; OpenCode não foi invocado. Base comum `ec0ebbb`. Os 14 arquivos entregues foram copiados explicitamente e comparados byte a byte; ajustes finais de documentação ficaram sob o Maestro. O [contrato da rodada](plans/completed/produto-ux-proxima-fase.md) foi movido para `completed`; o Quadro-2 preserva entregas e revisão cruzada.

As duas worktrees desta rodada (`brisa` e `farol`) foram removidas após a conferência dos hashes de origem, dos arquivos integrados e do índice Git; seus agentes foram encerrados no Maestri. Resta somente a worktree principal. O commit local contém os 30 arquivos explícitos desta entrega; a pasta `.maestri/` preexistente permanece fora dele. Sem push, tag, release ou deploy; a publicação fica com o usuário.
