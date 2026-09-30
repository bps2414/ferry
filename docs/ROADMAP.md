# Roadmap

Decidido em 2026-09-30. Ordem: lógica → UI/marca → self-hosted (Docker) → inglês → webhook → payload.

Estado: Fases 1, 1.5 e 2 concluídas. **Próxima: Fase 3 (self-hosted no Docker).**

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

## Fase 3 — Self-hosted no Docker

- **Objetivo**: o Ferry roda no servidor de casa dentro de um container, e do PC é só abrir `http://<ip-do-servidor>:<porta>` no navegador.
- **Como**:
  - o app hoje é WPF (`net10.0-windows`) e não roda em container Linux. A lógica (`Engine`, `Ftp`, `Archives`, `Job`, `Settings`) vira uma biblioteca `net10.0` sem WPF;
  - um servidor ASP.NET Core serve a interface web (mesma "Régua" e Configurações da Fase 2) e manda o progresso ao vivo (SignalR ou SSE);
  - a porta sai de uma variável de ambiente, com padrão fixo.
- **O que muda por não ser Windows**:
  - DPAPI (senha do jogo) → chave gerada no volume `/data`;
  - toast do Windows → notificação no navegador (e o webhook da Fase 5);
  - `%LOCALAPPDATA%\Ferry` → volume `/data` (`settings.json`, `queue.json`, `log.txt`);
  - `7z.exe`/`7z.dll` → `7zz` do Linux dentro da imagem;
  - "arrastar e soltar" e o seletor de arquivo → os dois caminhos: pasta monitorada no volume `/games` e upload pelo navegador (arrastar para a página).
- **Rede**: a varredura da sub-rede para achar o PS5 precisa de `network_mode: host`, senão só enxerga a rede interna do Docker.
- **Entrega**: `Dockerfile` + `docker-compose.yml` de exemplo, imagem publicada no GHCR pelo CI por tag.
- **Windows e web juntos**: o app WPF continua existindo e usa a mesma biblioteca do núcleo; as duas versões saem em cada release.
- **Login**: a interface web pede usuário e senha (definidos na primeira abertura), com sessão por cookie.
- **E2E**: sobe o container, abre a interface com Playwright e repete os cenários de envio contra o servidor FTP falso.

## Fase 4 — Inglês

- Interface em português e inglês, escolha nas Configurações (padrão: idioma do navegador/sistema).
- Textos saem do código para arquivos de recurso (`pt-BR`, `en`), incluindo log visível, avisos e erros.
- Vem depois da Fase 3 para traduzir uma interface só (a web).
- **E2E**: roda o fluxo principal nos dois idiomas e confere que não sobra texto sem tradução.

## Fase 5 — Webhook configurável

- URL de webhook nas Configurações (Discord, ntfy, genérico em JSON), disparado em concluído, erro e "precisa de senha".
- O texto da mensagem segue o idioma escolhido na Fase 4.
- Botão "Testar" que manda uma mensagem de exemplo.
- **E2E**: servidor HTTP falso recebe o webhook e confere evento, jogo e idioma.

## Fase 6 — Payload (hello world)

- ELF feito com o [ps5-payload-sdk](https://github.com/ps5-payload-dev/sdk), compilado no WSL (Ubuntu, WSL2, já instalado).
- O app manda o ELF para a porta 9021, o PS5 mostra "Ferry conectado" e o payload termina. Nada fica residente.
- **Depois**: agente residente (notificação, espaço livre, lista de jogos), protocolo de envio próprio e integração com o loader (pesquisar antes).

## Pesquisa, sem data

- **Envio progressivo** (part1 enquanto a part2 baixa): revisitar só se download lento virar gargalo.
- **Ideias soltas**: biblioteca do PS5, aviso de duplicado, perfis de console, enviar pasta extraída, limite de velocidade, auto-update, histórico.
