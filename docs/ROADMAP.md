# Roadmap

[English](en/ROADMAP.md) · **Português (BR)** · [Español](es/ROADMAP.md)

Decidido em 2026-09-30. Ordem: lógica → UI/marca → self-hosted (Docker) → inglês → webhook → PKG/fPKG → envio progressivo → payload.

Estado: Fases 1, 1.5, 2 e 3 concluídas (a 3 saiu na release `v1.3.0-beta.1`). **Fases 5, 5.1 e 6 implementadas. A fase 6 cobre PKG solto e um PKG por ZIP/RAR/7z; instalação real ainda exige validação no PS5. Próxima: Fase 7 (envio progressivo). Publicação depende da CI.**

## Fase 1 — Lógica (release `v1.1.0-beta.1`)

- **Salvar automático**:
  - cada campo salva ao mudar, e o valor inválido não é salvo (borda vermelha + dica; valida porta e pasta, o host aceita hostname);
  - gravação atômica (`.tmp` + `File.Replace`) de `settings.json` e `queue.json`;
  - sem botão Salvar. O layout atual fica até a Fase 2.
- **Senhas conhecidas**:
  - lista editável nas Configurações, uma por linha, em texto (são senhas públicas de sites);
  - antes do diálogo, o app testa cada uma em silêncio (`PasswordOkAsync`);
  - a senha que funcionar no diálogo entra no fim da lista sozinha.
- **Senha do jogo salva**: guardada na `queue.json` com DPAPI e apagada quando o jogo sai da fila. Não pede de novo ao reabrir.
- **Capa e título**:
  - lê `sce_sys/param.sfo` e `icon0.png` de dentro do arquivo;
  - o card mostra a capa, o título e o `PPSAxxxxx`;
  - sem capa legível, mostra o nome do arquivo em tipografia grande, sem ícone falso.
- **Aguardando partes**: mostra quais partes faltam ("faltando part4, part5").
- **Achar o PS5 na rede**: ao abrir, se o IP salvo não responde, varre a sub-rede (2121/1337) e oferece o IP encontrado. Também há um botão "Procurar". Nunca troca o IP sozinho.
- **Toast do Windows**: para concluído, erro e "precisa de senha", só quando a janela está sem foco.
- **CI**: actions atualizadas para Node 24.
- **E2E**:
  - senha conhecida (G6 sem diálogo) e senha aprendida;
  - senha lembrada ao reabrir;
  - salvar atômico;
  - capa/título (jogos falsos ganham `param.sfo` + `icon0.png`).
- **Releases**: apagar a release `v1.0.0` estável antiga.
- **Acrescentado em uso real (PPSA11386)**:
  - log persistente em `log.txt`;
  - APPE só em parcial começado pelo app;
  - SIZE conferido após cada envio, com `SELF` desligado;
  - publicação atômica de `param.json`/`param.sfo`;
  - aviso de jogo já instalado (ver `FTP-PS5.md`).

## Fase 1.5 — Imagem `.exfat` do ShadowMount+ (release `v1.1.0-beta.2`)

- **Aceita como jogo**:
  - `.exfat` solto (arrastar, seletor, pasta monitorada), com stream direto do disco;
  - arquivo compactado (qualquer split aceito) com uma imagem `.exfat` dentro em vez de pasta de jogo, com stream `7z -so` → FTP.
- **Destino**: campo "Imagens .exfat (ShadowMount+)" nas Configurações (`ImageDir`, padrão `/mnt/ext1/homebrew`). Salva sozinho e valida (caminho começando com `/`). O nome remoto é o nome do `.exfat`.
- **Publicação atômica**: sobe como `<nome>.exfat.ferry-part` e só é renomeada depois do `SIZE` conferido. O SM+ reconhece imagem só pela extensão, e o sufixo fica de fora (pesquisa em `FTP-PS5.md`).
- **Retomada, aviso de já instalado, log**: mesmas regras da Fase 1.
- **Card**: sem capa (o app não abre a imagem), mostra o nome grande.
- **E2E**: `.exfat` solto e dentro de `.part1.rar`, pausar/retomar, reabrir com parcial nosso (APPE) e de outra versão (STOR), já existente + "Tentar de novo".

## Fase 2 — Marca e interface: **Ferry** (release `v1.2.0-beta.1`)

- **Decidido nos mockups**: Fila "Régua" (linhas por fio, %, MB/s e restante em colunas fixas, barra = travessia), Configurações em duas colunas, logo "Travessia" (dois cais e a seta), fonte Geist, acento azul `#6F97FF`.
- **Acrescentado em uso**: "Transferir agora" num jogo da fila/pausado; o envio atual volta para a fila e continua depois.

- **Nome**: Ferry, com o subtítulo "envio de jogos para PS5".
  - Repo `ps5-sender` → `ferry` (o GitHub redireciona os links antigos) e exe `Ferry.exe`.
  - Dados passam a `%LOCALAPPDATA%\Ferry`, migrando os de `PS5Sender`.
  - Sem símbolos da Sony (△○✕□, logo PS).
- **Direção**:
  - "PlayStation noir" editorial/tipográfico: preto-azulado, quase monocromático, um acento;
  - números grandes (%, MB/s, ETA), título do jogo em destaque, grid rígido;
  - **proibido**: glow, gradiente roxo-azul, vidro fosco, cara de template de IA.
- **Ferramentas**: `/frontend-design` + `/impeccable`, gerando mockups antes de implementar.
- **Configurações sem rolagem**: o formato é decidido nesta sessão.

## Fase 3 — Self-hosted no Docker (release `v1.3.0-beta.1`) — feita

- **Objetivo**: o Ferry roda no servidor de casa dentro de um container, e do PC é só abrir `http://<ip-do-servidor>:<porta>` no navegador.
- **Como**:
  - o app hoje é WPF (`net10.0-windows`) e não roda em container Linux. A lógica (`Engine`, `Ftp`, `Archives`, `Job`, `Settings`) vira uma biblioteca `net10.0` sem WPF;
  - um servidor ASP.NET Core serve a interface web (mesma "Régua" e Configurações da Fase 2) e manda o progresso ao vivo (SignalR ou SSE);
  - a porta sai de uma variável de ambiente, com padrão fixo.
- **O que muda por não ser Windows**:
  - DPAPI (senha do jogo) → chave gerada no volume `/data`;
  - toast do Windows → aviso dentro da página, e notificação do sistema quando o navegador deixa (só em https ou localhost; o webhook da Fase 5 cobre o resto);
  - `%LOCALAPPDATA%\Ferry` → volume `/data` (`settings.json`, `queue.json`, `log.txt`);
  - `7z.exe`/`7z.dll` → 7-Zip do Linux: `7zip` + `7zip-rar` do Ubuntu na imagem, `7zzs` oficial ao lado do binário Linux;
  - "arrastar e soltar" e o seletor de arquivo → os dois caminhos: pasta monitorada no volume `/games` e upload pelo navegador (arrastar para a página).
- **Rede**: a varredura da sub-rede para achar o PS5 precisa de `network_mode: host`, senão só enxerga a rede interna do Docker.
- **Entrega**: `Dockerfile` + `docker-compose.yml` de exemplo, imagem publicada no GHCR pelo CI por tag.
- **Linux sem Docker**: o mesmo servidor web publicado como binário autocontido `linux-x64` e `linux-arm64` em cada release. Sem app de janela nativo para Linux (a interface web cobre).
- **Windows e web juntos**: o app WPF continua existindo e usa a mesma biblioteca do núcleo; as duas versões saem em cada release.
- **Login**: a interface web pede usuário e senha (definidos na primeira abertura), com sessão por cookie.
- **E2E**: sobe o container, abre a interface com Playwright e repete os cenários de envio contra o servidor FTP falso.
- **Acrescentado ao implementar**: progresso por SSE (sem SignalR); upload em blocos com retomada; jogo já enviado volta como "Concluído" ao reabrir/reiniciar em vez de cair no aviso de "já instalado" (vale também para o app Windows); o teste de conexão mais novo é o que vale no cartão do PS5.

## Fase 4 — Inglês

- Interface web em português e inglês: Automático, Português (Brasil) ou English nas Configurações. Automático usa o primeiro idioma compatível do navegador, com fallback para inglês; escolha explícita persiste em `settings.json` e vale também para o login. WPF permanecia em português na Fase 4; a Fase 5.1 acrescenta localização nativa.
- Textos saem do código para arquivos de recurso (`pt-BR`, `en`), incluindo log visível, avisos e erros.
- Vem depois da Fase 3 para traduzir uma interface só (a web).
- **E2E**: roda o fluxo principal nos dois idiomas e confere que não sobra texto sem tradução.
- Implementado: catálogos compartilhados de chave e parâmetros para núcleo, API e UI; troca imediata preservando fila, uploads e diálogo de senha; números formatados pelo navegador. Diagnósticos brutos de FTP/7-Zip/sistema mantêm o conteúdo original, e `log.txt` existente permanece intacto. README e docs atualizados nos três idiomas.
- Validação: contratos focados em `dotnet run --project e2e -- --localization`; E2E web executa duas rodadas isoladas (`pt-BR`, `en`), com hashes, autenticação, retomada e reinício. O modo local não comprova Docker nem execução remota de CI.

## Fase 5 — Webhook configurável

- URL de webhook nas Configurações (Discord, ntfy, genérico em JSON), disparado em concluído, erro e "precisa de senha".
- O texto da mensagem segue o idioma escolhido na Fase 4.
- Botão "Testar" que manda uma mensagem de exemplo.
- **E2E**: servidor HTTP falso recebe o webhook e confere evento, jogo e idioma.
- Implementado na web e no Windows: ativação opcional, serviço, URL mascarada e teste autenticado. Idioma automático persiste no servidor; no Windows, Automático segue o idioma do sistema desde a Fase 5.1. HTTP roda em segundo plano, com timeout de 10 s, fila limitada e uma tentativa por evento; falhas não mudam o jogo nem bloqueiam FTP/senha.
- Contratos e limites em [WEBHOOK.md](WEBHOOK.md). Validação focada: `dotnet run --project e2e -- --webhook`; E2E web verifica os três serviços, eventos, idioma, senha sem resposta HTTP, reinício e navegador fechado. Provedores reais e Docker local não foram exercitados.

## Fase 5.1 — Windows: idioma e bandeja

Implementada e validada localmente: 124 verificações WPF passaram nos dois idiomas, incluindo transferência FTP e diálogo de senha. Próxima etapa: Fase 6.

- WPF em Português (Brasil) e English. Automático usa o idioma de exibição original do Windows: português → pt-BR; demais → en. `Settings.Language` é a mesma preferência da web; Automático na web continua seguindo o navegador.
- Bindings atualizam a mesma janela, preservando campos, fila, progresso e diálogo de senha. Textos próprios usam catálogos compartilhados e do app, com paridade de chaves/parâmetros. Números, tamanhos e duração seguem o idioma; diagnósticos FTP/7-Zip/sistema, caminhos e nomes permanecem brutos.
- Minimizar esconde na bandeja e mantém envios, monitoramento e webhooks. Duplo clique ou Abrir Ferry restaura normal/maximizado; pedido de senha restaura antes do diálogo. X e Sair encerram. Docker/Linux continuam como servidor web.
- Validação: `dotnet run --project e2e/windows -c Release`, com WPF real, FTP local falso, hash após troca durante transferência e senha digitada preservada no modal. O harness não cobre PS5/provedores reais nem cliques físicos na bandeja.

## Fase 6 — PKG e fPKG

Entrega inicial implementada: PKG solto e um PKG por compactado por FTP para instalação manual no etaHEN; DPI automático opcional nas Configurações. Ledger persiste preparo/pedido; resposta incerta requer conferência no PS5. Imagens adicionais usam o fluxo ShadowMount+. HTTP direto permanece complemento futuro. Testes locais não comprovam instalação no console.

[PKG](PKG-PS5.md)

## Fase 7 — Envio progressivo (RAR em partes)

- **Objetivo**: com o download ainda em andamento na pasta monitorada (ex.: JDownloader no servidor), começar a extrair e enviar ao PS5 as partes que já chegaram, na ordem, em vez de esperar todas.
- **Limite do formato**:
  - `.zip` e `.7z` guardam o índice no fim (última parte), então não dá para começar antes dela. Nesses, o comportamento continua o de hoje;
  - viável para RAR em partes (`.partN.rar` e `.rar` + `.rNN`): os arquivos vêm em sequência, cada um com cabeçalho próprio.
- **Como** (pesquisar antes):
  - o 7-Zip parece abrir todos os volumes no início (confirmar);
  - alternativas: `unrar` extraindo volume a volume e esperando o próximo, ou ler o RAR em sequência por conta própria;
  - um arquivo do jogo que atravessa duas partes só termina quando a próxima chega. O envio fica esperando com o FTP aberto, ou fecha e continua com `APPE` (decidir).
- **Ordem**: só avança com a próxima parte da sequência (part3 não vale se falta a part2). Parte ainda baixando (nome terminando em `.part`, tamanho mudando) não conta.
- **Segurança**:
  - a publicação atômica continua: `param.json`/`param.sfo` só no fim, depois da última parte e da verificação, para o ShadowMount+ não instalar o jogo pela metade;
  - a senha do RAR é pedida antes de começar.
- **Card**: mostra "Enviando parte 3 de ?" enquanto o total não é conhecido, e "Aguardando part4" quando parar esperando.
- **Configurações**: liga/desliga (padrão: desligado até estar maduro).
- **E2E**:
  - um RAR em partes chega na pasta uma parte de cada vez, devagar;
  - o envio começa antes da última parte;
  - confere o SHA-256 de cada arquivo no PS5 falso e que `param.json`/`param.sfo` só aparecem no fim;
  - `.zip`/`.7z` continuam esperando todas as partes.

## Fase 8 — Payload (hello world)

- ELF feito com o [ps5-payload-sdk](https://github.com/ps5-payload-dev/sdk), compilado no WSL (Ubuntu, WSL2, já instalado).
- O app manda o ELF para a porta 9021, o PS5 mostra "Ferry conectado" e o payload termina. Nada fica residente.
- **Depois**: agente residente (notificação, espaço livre, lista de jogos), protocolo de envio próprio e integração com o loader (pesquisar antes).

## Pesquisa, sem data

- **Ideias soltas**: biblioteca do PS5, aviso de duplicado, perfis de console, enviar pasta extraída, limite de velocidade, auto-update, histórico.
