# Redesenho da interface — Fila, Configurações, celular e Windows

Data: **2026-10-14**. Estado: **plano; código não alterado**. Base analisada: `fab0939`. Escopo: interface web (`web/ui`) e app Windows (`app/MainWindow.xaml`, `app/App.xaml`). Evidência: capturas do E2E web (`e2e/web/report/pt-BR`), da rodada de energia (`e2e/windows/report/power-pt-BR.png`) e `docs/screenshots`.

## Decisão de direção

**Evoluir, não trocar a identidade.** A direção da Fase 2 continua válida e é o que diferencia o Ferry: "PlayStation noir" editorial, quase monocromático, um acento azul `#6F97FF`, Geist, números grandes, Fila "Régua" com a barra como travessia, logo Travessia. Continua proibido: glow, gradiente roxo-azul, vidro fosco, cara de template.

O que muda é a **densidade de informação e a hierarquia**: a Régua foi desenhada para um jogo enviando e hoje a fila mostra muitos estados (aguardando partes, pausado, erro, concluído, PKG). Cada estado recebe o mesmo layout de quatro colunas, e o resultado é ruído.

## Problemas encontrados

### Fila

1. **Colunas vazias em todo card.** Card aguardando, pausado, com erro ou concluído mostra `0,0% / — MB/s / — restante`. Em 5 cards concluídos/PKG (captura `11-pkg`) são 10 traços e 5 "100,0%" que não dizem nada além de "terminou".
2. **Hierarquia invertida.** Sem capa o título passa de 22 px para 30 px (`.job.nocover .title`): `Kestrel_Run_v1.02`, que só espera uma parte, fica maior que o jogo que está enviando.
3. **Sem ordem por urgência.** Erros e pedidos de senha aparecem onde estiverem na lista (o erro de senha incorreta ficou no fim em `fila.png`). Concluídos ocupam o mesmo espaço que o envio ativo.
4. **Estado só por texto colorido pequeno** (12,5–13 px). A barra repete a cor, mas em card parado a barra vazia com seta não comunica "pausado" vs "aguardando".
5. **Ações espalhadas.** "Transferir agora" é um botão grande no lugar das métricas; "Solicitar instalação" ocupa três colunas; os ícones ficam à direita. No celular os ícones caem numa linha própria, isolada.
6. **Cabeçalho pobre para fila longa.** Há velocidade total, mas não progresso total nem previsão de término da fila — justamente o que importa para quem arma "desligar ao terminar".

### Avisos

7. **Toasts duplicados**: "Senha necessária / W1 precisa de senha" aparece duas vezes (`05-enviando`). O toast de senha não tem ação; o usuário precisa achar o card.

### Casca e status

8. **Status do PS5 longe do trabalho.** Fica no rodapé da barra lateral; o cartão "Achei um PS5 em…" disputa espaço com ele. No celular vira uma linha espremida acima das abas.
9. **Caminho com prefixo `\\?\`** aparece cru no vazio e nas Configurações (`03-fila-vazia`, `02-configuracoes`) quando o servidor roda em Windows.

### Configurações

10. **Uma página longa com rolagem** em duas colunas, contrariando "Configurações sem rolagem" da Fase 2. Conexão, destinos, PKG/DPI, webhook, envio, senhas, idioma e conta estão no mesmo plano; Idioma e Conta ficam colados no fim.
11. **Resultados de teste inline** em texto verde/vermelho longo ao lado do botão, quebrando o grid.
12. **Seção PKG vai mudar**: o [plano do PKG Manager](remover-pkg-ftp-e-adotar-pkg-manager.md) remove destino PKG interno, DPI e instalação automática. Redesenhar essa seção agora seria trabalho jogado fora.

### Windows

13. **Energia empurra a fila para baixo.** As duas caixas de energia e o texto explicativo (≈170 px) ficam acima do título "Fila" (`power-pt-BR.png`). O texto de ressalva PKG aparece sempre, mesmo sem PKG na fila.

### Celular

14. Cada card ocupa ≈300 px de altura (título, estado, detalhe truncado, métricas, linha de ações, barra). Cinco jogos = três telas de rolagem. "Limpar concluídos" e "Adicionar arquivos" empilhados no topo.

### Manutenção

15. **Tokens duplicados à mão** entre `web/ui/app.css` e `app/App.xaml` ("mesmos tokens do App.xaml" é só um comentário). Qualquer redesenho dobra o risco de divergência.

## Proposta

### 1. Fila por grupos

Ordem fixa, cabeçalho de grupo discreto (rótulo + contagem), grupos vazios somem:

| Grupo | Conteúdo | Formato do card |
|---|---|---|
| **Precisa de você** | senha pendente, erro, partes faltando há muito tempo, "Verifique no PS5" | completo, com ação principal visível (Digitar senha / Tentar de novo / Ver partes) |
| **Enviando** | ativo | Régua completa: capa, título, %, MB/s, restante, barra |
| **Na fila** | aguardando, pausado, aguardando partes | compacto (1 linha + barra fina): título, estado, `x GB de y GB`, ações |
| **Concluídos** | concluído, instalação solicitada | colapsável, linha única sem barra; "Limpar" no cabeçalho do grupo |

A ordem dentro de "Na fila" continua a do motor (não reordenar o que o usuário escolheu com "Transferir agora").

### 2. Métricas só quando significam algo

- Ativo: as três colunas como hoje.
- Pausado/na fila com progresso: só `%` (sem MB/s/restante).
- Aguardando partes: no lugar das colunas, "faltando part3.rar" em destaque.
- Concluído: sem colunas; um "✓ enviado às 14:32" curto.
- Título com tamanho fixo (22 px web/WPF) com ou sem capa; o ativo pode ganhar 26 px.

### 3. Estado como etiqueta

Rótulo de estado com ponto colorido + texto (verde/âmbar/vermelho/azul), mesmo tamanho em todos os cards. A barra do card parado usa traço tracejado ou cor `--idle`, sem a seta de travessia; a seta fica reservada para o que está atravessando.

### 4. Ações em um lugar

Uma ação principal por estado em botão de texto (Transferir agora, Digitar senha, Tentar de novo, Abrir PKG Manager quando aplicável) + ícones secundários (pausar/retomar, cancelar, remover). No celular, ícones na mesma linha do estado, alvo mínimo 44 px.

### 5. Cabeçalho da Fila

`Fila` + resumo + **barra agregada** (bytes enviados / total dos itens não concluídos) + velocidade total + **término previsto** (só quando houver ETA de todos os itens ativos/na fila; senão omitir — não inventar número). Botão "Adicionar arquivos" permanece como primário.

### 6. Avisos

Deduplicar toasts por `(evento, job)`; toast de senha com botão **Digitar senha** que abre o diálogo; erro com **Ver** que rola até o card. Manter `aria-live`.

### 7. Casca

- Status do PS5 como **chip no topo do conteúdo** (ponto + host + destino; clique = testar conexão), igual em desktop e celular. Barra lateral fica só com marca e navegação.
- "Achei um PS5" vira faixa no topo das Configurações e chip temporário no topo da Fila.
- Exibir caminhos sem o prefixo `\\?\` (só apresentação; o valor salvo não muda).

### 8. Configurações por seções

Navegação secundária à esquerda do conteúdo (no celular, lista que abre cada seção). Cada seção cabe numa tela sem rolagem em 1440×900:

1. **PS5** — IP/nome, Procurar na rede, porta, usuário, senha, Testar conexão.
2. **Destinos** — pasta dos jogos (M.2/SSD/outra), imagens ShadowMount+. PKG entra aqui depois do plano do PKG Manager (porta 8844, Abrir gerenciador).
3. **Envio** — conexões paralelas, apagar originais, pasta monitorada.
4. **Senhas conhecidas**.
5. **Avisos** — webhook (web e Windows), notificações do navegador.
6. **Geral** — idioma; conta/sair (web).

Resultado de teste vira **linha de status** abaixo do grupo (ícone + texto curto + horário), já preparando o diagnóstico da [Fase 6.1](proxima-fase-sem-payload.md). "Salvas automaticamente" continua no cabeçalho, com estado de erro quando o salvamento falhar.

### 9. Windows: energia compacta

Trocar as duas caixas por um controle **"Ao terminar"** no cabeçalho da Fila (botão com menu: Não fazer nada / Desligar este PC; checkbox Impedir suspensão). Estado armado vira chip ("Desligar ao terminar · cancelar"). A contagem de 60 s continua como **faixa** acima da Fila, como hoje (`PowerBanner`). A ressalva sobre PKG aparece só quando houver PKG na fila. **Contratos de energia não mudam** (sessão atual, bloqueios, 60 s, `shutdown /s /t 0`) — é só apresentação.

### 10. Celular

Cards compactos dos grupos 1 e 3 (≈72 px), concluídos colapsados, chip de status no topo, abas fixas, botão "Adicionar" fixo no rodapé da tela. Meta: 5 jogos em uma tela de 390×844.

### 11. Fonte única de tokens

`tools/tokens.json` (cores, raios, tamanhos de texto) gerando o bloco `:root` de `app.css` e os `SolidColorBrush`/`sys:Double` de `App.xaml`, no mesmo estilo de `tools/generate-brand.py`. A CI falha se o gerado divergir do versionado.

## Fora do escopo

- Tema claro, nova marca/logo, nova fonte, animações além das existentes.
- Mudar comportamento de fila, retomada, energia, webhook ou PKG. Mudanças de PKG seguem o plano próprio.
- Reordenar a fila por arrastar, perfis de vários consoles, telemetria.

## Fases de implementação

| # | Entrega | Depende de | Validação |
|---|---|---|---|
| R0 | **Mockups estáticos** (HTML com dados fixos) da Fila em todos os estados, Configurações por seção, celular 390 px e energia Windows; pt-BR e en; aprovação do usuário | — | revisão visual |
| R1 | Tokens únicos + componentes base (etiqueta de estado, card compacto, cabeçalho de grupo, chip de status) | R0 | CI de tokens; E2E web atual sem regressão |
| R2 | Fila web: grupos, métricas por estado, ações, cabeçalho agregado, toasts | R1 | E2E web pt-BR/en + novas capturas por estado |
| R3 | Configurações web por seções; prefixo `\\?\` | R1; **após Etapa 1 do PKG Manager** para a seção Destinos | E2E web (login, webhook, PKG) |
| R4 | Paridade WPF: Fila, Configurações e energia compacta | R2, R3 | `e2e/windows` (`--power` e padrão), capturas pt-BR/en |
| R5 | Celular e acessibilidade: alvos 44 px, foco, leitores de tela (`aria-live`, `AutomationProperties`), contraste ≥ 4,5:1 para texto (o `--idle` `#4A5363` tem ≈2,5:1 sobre `--bg` e só pode ser usado em elementos não textuais) | R2, R3 | captura 390×844; verificação de contraste; teclado |

R2 e R3 podem andar em paralelo. R4 só depois de a web estabilizar, para não redesenhar duas vezes.

## Cuidados com testes existentes

O E2E web localiza cards por `.job` + `.title` e lê `.pct .v` (`e2e/web/web.mjs`), além de IDs como `#authTitle` e `[data-set=…]`. Manter essas classes/IDs, ou atualizar o harness no mesmo commit. O WPF usa `x:Name` nos templates (`StageLbl`, `PctCell`, `SendNowBtn`…) — mesmo cuidado. As chaves `data-i18n` existentes ficam; textos novos entram em `web/ui/locales/*.json` e `app/locales/*.json` nos dois idiomas.

## Critérios de pronto

- Fila com 1 envio, 2 na fila, 1 erro e 5 concluídos cabe em 1440×900 sem rolagem e o erro aparece primeiro.
- Nenhum card não ativo mostra `—` em métricas.
- 5 jogos visíveis em 390×844.
- Cada seção de Configurações cabe em 1440×900.
- Energia armada ocupa no máximo uma linha fora da contagem.
- Tokens web/WPF gerados de uma fonte, verificados na CI.
- E2E web e Windows passando nos dois idiomas, com capturas novas em `e2e/*/report`.

## Questões abertas para o usuário

1. Concluídos colapsados por padrão, ou sempre visíveis com opção de recolher?
2. Pode haver um botão fixo de "Adicionar" no celular, ou prefere manter só no cabeçalho?
3. Fazer R0 (mockups) antes de qualquer código, como na Fase 2? (Recomendado.)
