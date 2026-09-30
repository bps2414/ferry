# Roadmap

Decidido em 2026-09-30. Ordem: lógica → UI/marca → payload.

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

## Fase 2 — Marca e interface: **Ferry**

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

## Fase 3 — Payload (hello world)

- ELF feito com o [ps5-payload-sdk](https://github.com/ps5-payload-dev/sdk), compilado no WSL (Ubuntu, WSL2, já instalado).
- O app manda o ELF para a porta 9021, o PS5 mostra "Ferry conectado" e o payload termina. Nada fica residente.
- **Depois**: agente residente (notificação, espaço livre, lista de jogos), protocolo de envio próprio e integração com o loader (pesquisar antes).

## Pesquisa, sem data

- **Envio progressivo** (part1 enquanto a part2 baixa): revisitar só se download lento virar gargalo.
- **Ideias soltas**: biblioteca do PS5, aviso de duplicado, perfis de console, enviar pasta extraída, limite de velocidade, auto-update, histórico.
