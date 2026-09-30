# Produto, UX e próxima fase do Ferry

Data: 2026-09-30. Base: `ec0ebbb`, branch `main`.

## Pedido e execução

Dois agentes GPT-6.1 Sol high, via Codex recrutado pelo Maestri, em duas worktrees separadas. Uma entrega implementação de UX/produto; outra entrega pesquisa atual e plano da próxima fase, sem payload. O Maestro integra, revisa, valida, remove as worktrees e faz commit local. Push e release ficam para o usuário.

## Contrato UX — Brisa

Implementar um pacote pequeno e coerente para transferências longas no Windows: opção explícita de desligar este PC ao finalizar a fila e, se viável sem ampliar muito o escopo, impedir suspensão durante transferência. Priorizar clareza do fluxo, estados, controles acessíveis e cancelamento; preservar o visual e os contratos atuais. Não redesenhar o app todo.

- Desligamento desativado por padrão, armado apenas na sessão atual e com fila não vazia contendo trabalho ainda por finalizar. Não rearmar ao abrir o app; não desligar por fila vazia ou histórico restaurado já concluído.
- Contagem regressiva visível de pelo menos 60 segundos, cancelável. Mostrar que a ação atinge este PC. Fechar o app cancela a ação. Sem `/f`, sem matar processos, sem desligar PS5 ou servidor Docker.
- Durante a contagem, manter este PC acordado até cancelar ou executar. Revalidar também fontes concluídas alteradas após armamento e antes da contagem; exclusão automática de originais após sucesso continua permitida. Espera pelo processo nativo de desligamento deve ter prazo finito.
- Só disparar após conclusão bem-sucedida de todo o trabalho observado. Pausa, erro, cancelamento, partes faltantes, senha pendente, instalação em solicitação ou resultado incerto bloqueiam. Remover trabalho observado desarma; adicionar/retomar trabalho invalida a contagem regressiva. Conferir novamente no instante da execução.
- PKG preparado ou pedido aceito pode significar transferência encerrada, nunca instalação comprovada. O texto deve explicar esse limite; resultado incerto bloqueia.
- Não alterar FTP, protocolo DPI, retomada, publicação atômica, segredos ou apagar originais. A web não deve oferecer desligamento do host.
- Novas strings próprias em `app/locales/{pt-BR,en}.json` (ou catálogo correspondente se necessário). Troca de idioma preserva estado e controles.
- Testes de falha antes da implementação. Serviço de energia injetável, com fake nos testes: jamais executar desligamento/suspensão reais durante desenvolvimento ou validação.
- Posse: `app/`, `core/` apenas se estritamente necessário para observar conclusão/atividade, `e2e/windows/`, e documento novo `docs/UX-PRODUTO.md`. Não editar README, roadmap, pesquisa ou contrato do Maestro. Não tocar manualmente `dist*/`, `bin/`, `obj/`.

## Contrato pesquisa — Farol

Pesquisa na internet, com fontes primárias consultadas e data explícita, sobre jailbreak PS5, etaHEN, ftpsrv, ShadowMount+, PKG e ferramentas comparáveis. Comparar capacidades documentadas ao código real do Ferry, sem transformar alegações dos autores em desempenho verificado.

- Separar jailbreak, firmware, loader, HEN, formato e serviço. Distinguir upstream oficial de forks/ports não oficiais; não generalizar compatibilidade.
- Matriz de pelo menos quatro ferramentas relevantes, incluindo ps5upload e um cliente FTP geral. Recursos úteis, diferenças, dependências, limitações, manutenção e evidências com links diretos.
- Priorizar problemas reais do usuário e propor uma próxima fase sem payload próprio ou envio de payload. Reavaliar envio progressivo RAR com limites técnicos e evidência; não implementar essa fase agora.
- Entregar ranking de oportunidades (valor/esforço/risco), uma fase recomendada com escopo, não objetivos, contratos, aceite, casos de falha e plano de validação. Separar pesquisa de console real, que não será exercitado.
- Posse exclusiva: `docs/PESQUISA-CENARIO-PS5-2026-09.md`, `docs/plans/next/proxima-fase-sem-payload.md`, `docs/ROADMAP.md`. Nenhuma alteração de código ou documentação de UX. Não editar contratos do Maestro nem README.

## Integração e aceite

O Maestro revisa ambos os diffs e pesquisa; pode pedir revisão cruzada sem mudar posse. Integra arquivos explicitamente, preserva a pasta `.maestri` preexistente, faz um ciclo final na raiz e registra resultados reais:

1. `dotnet run --project e2e -- --localization`
2. `dotnet run --project e2e -- --webhook`
3. `dotnet run --project e2e`
4. `dotnet run --project e2e -- --pkg`
5. `dotnet run --project e2e/windows -c Release`
6. `dotnet run --project e2e/windows -c Release -- --power`
7. `docker build -t ferry:e2e .` e E2E web; se Docker indisponível, E2E web no modo local documentado e registrar o limite.

Todos os serviços de teste locais e falsos; nenhum PS5, FTP, webhook ou desligamento real. Revisar relatório e `git diff --check`, atualizar documentação de uso e testes se necessário, mover este plano para completed, remover apenas as duas worktrees criadas nesta execução e fazer commit local do escopo revisado. Sem push/tag/deploy.

## Resultados

Concluído em 2026-09-30. Brisa/Farol executaram em Codex GPT-6.1 Sol high via Maestri, com posse exclusiva e revisão cruzada. Os 14 arquivos entregues foram integrados e comparados byte a byte antes dos ajustes documentais do Maestro.

Revisão: fonte terminal alterada desde Arm cancela; DeleteOriginal automático preservado; PC acordado durante os 60s; WaitForExit nativo limitado a 5s. Localização 1771, webhook 48, PKG 247, WPF 170 e energia fake 188 checks passaram. E2E completo do core passou; web em Chromium DLL local passou com 57 checks por idioma. Todos exit 0. Docker não pôde executar: CLI ausente, WinError 2. Nenhum PS5, webhook/FTP externo ou energia nativa foi exercitado.

A próxima fase 6.1 continua proposta, não implementada. RAR progressivo é prova futura; payload adiado. Relatório completo, evidências e comandos em [RELATORIO-PRODUTO-2026-09.md](../../RELATORIO-PRODUTO-2026-09.md). Entregas preservadas antes de remover as duas worktrees; commit somente local, sem push/tag/deploy.
