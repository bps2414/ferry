<img src="docs/logo.png" width="56" alt=""> 

# Ferry

A interface web oferece português (Brasil) e inglês. Em **Configurações → Idioma**, escolha **Automático**, **Português (Brasil)** ou **English**. Automático usa o primeiro idioma compatível do navegador e recorre ao inglês nos demais casos. A escolha explícita vale para a instalação, inclusive no login, e persiste ao reiniciar. A interface Windows WPF continua em português.

[English](README.md) · **Português (BR)** · [Español](README.es.md)

envio de jogos para PS5

![Fila do Ferry](docs/screenshots/fila.png)

[![CI](https://github.com/bps2414/ferry/actions/workflows/ci.yml/badge.svg)](https://github.com/bps2414/ferry/actions/workflows/ci.yml)

App que pega jogos compactados (`.zip`, `.rar`, `.7z`, inclusive divididos em partes), **extrai e envia ao mesmo tempo** para um PS5 com jailbreak via FTP — sem gravar os arquivos extraídos no seu disco.

- **Windows**: um `.exe` portátil (sem instalar nada)
- **Self-hosted** (Docker ou Linux): roda no servidor de casa e você usa pelo navegador, em `http://<ip-do-servidor>:8021`
- Interface escura, com fila, progresso, velocidade e tempo restante
- Pausa, retoma, fecha e reabre sem perder o que já foi enviado

> Feito para uso com homebrew/backups próprios em console desbloqueado. Use por sua conta e risco.

## Download

Baixe o `Ferry.exe` na página de [Releases](../../releases) e execute. Requer Windows 10/11 x64.

## Self-hosted (servidor de casa)

A mesma lógica do app Windows, com interface web: fila ao vivo, configurações, log, senha do arquivo pedida no navegador e envio de arquivos arrastando para a página (em blocos, continua de onde parou se a conexão cair).

**Docker** (amd64 e arm64): copie o [`docker-compose.yml`](docker-compose.yml), troque `/caminho/dos/jogos` pela sua pasta e rode:

```bash
docker compose up -d
```

Abra `http://<ip-do-servidor>:8021`. Na primeira abertura a página pede para criar o usuário e a senha. Imagem: `ghcr.io/bps2414/ferry`.

- `network_mode: host` é o recomendado: a busca do PS5 varre a rede de casa e o FTP com o PS5 funciona sem NAT.
- Volumes: `/data` (configurações, fila, log, login) e `/games` (pasta monitorada: o que cair nela entra na fila sozinho; os envios pelo navegador também vão para lá).
- Variáveis: `FERRY_PORT` (padrão `8021`), `FERRY_DATA` (`/data`), `FERRY_GAMES` (`/games`).
- Esqueceu a senha: apague `auth.json` na pasta de dados e abra a página de novo.

**Linux sem Docker** (x64 ou arm64, ex.: Raspberry Pi): baixe `Ferry-linux-x64.tar.gz` (ou `-arm64`) nas [Releases](../../releases), extraia e rode `./ferry`. O 7-Zip vai junto no pacote. Os dados ficam em `~/.local/share/Ferry` (ou em `FERRY_DATA`).

> Sem https, o navegador não deixa a página mostrar avisos do sistema; os avisos (concluído, erro, senha) aparecem dentro da página. Para acesso de fora de casa, use um proxy reverso com https ou VPN — não exponha a porta direto na internet.

## Como usar

1. Rode o payload de FTP no PS5 (**ftpsrv**, porta 2121, ou o FTP do **etaHEN**, porta 1337).
2. Abra o app → **Configurações**: IP do PS5, porta, destino (**M.2** `/mnt/ext1/homebrew` ou **SSD interno** `/data/homebrew`). Tudo salva sozinho. O pé da barra lateral mostra se o PS5 está online.
3. Na **Fila**, clique no centro da tela (abre o seletor do Windows) ou arraste os arquivos para a janela. Selecione **todas as partes** de uma vez.
4. Pronto: quando todas as partes estiverem presentes, o jogo é extraído e enviado para `<destino>/<pasta do jogo>`.

Opcional: uma **pasta monitorada** — tudo que cair nela entra na fila sozinho (útil para a pasta de downloads).

**Transferir agora**: num jogo na fila ou pausado, passa ele na frente. O envio em andamento volta para a fila e depois continua de onde parou.

## Formatos aceitos

| Formato | Exemplo |
|---|---|
| Arquivo único | `Jogo.zip`, `Jogo.rar`, `Jogo.7z` |
| Divisão bruta | `Jogo.zip.001`, `.002`… / `Jogo.7z.001`… |
| Zip dividido | `Jogo.z01`, `Jogo.z02`… + `Jogo.zip` |
| RAR novo | `Jogo.part1.rar` … `Jogo.partN.rar` |
| RAR antigo | `Jogo.rar` + `Jogo.r00`, `Jogo.r01`… |
| Com senha | abre um diálogo pedindo a senha (avisa se estiver errada) |
| Imagem do ShadowMount+ | `Jogo.exfat` solto ou dentro de qualquer formato acima. Vai inteira para a pasta de imagens (Configurações) |

O app espera **todas as partes** chegarem e o tamanho delas **parar de mudar** antes de começar (dá para deixar o download terminando).

## O que ele faz sozinho

- **Pastas “casca”**: desce pelas pastas até achar a que tem `EBOOT.BIN` ou `sce_sys/param.sfo` e envia só ela.
- **Pasta `dec`**: se o arquivo tiver a pasta do jogo (`PPSA…-app0`) **e** uma pasta `dec` ao lado, o conteúdo do `dec` sobrescreve o do jogo (igual a copiar o jogo e depois o `dec` por cima). Os arquivos substituídos nem são enviados.
- **Retomada**: antes de enviar, pergunta ao PS5 o que já está lá. Arquivo completo é pulado (nem é extraído); arquivo pela metade continua de onde parou se o servidor aceitar `APPE`, senão é reenviado inteiro.
- **Fechar e reabrir**: a fila é salva; ao reabrir, volta sozinha e continua de onde parou. Jogo já enviado volta como "Concluído" (não é mandado de novo); adicionar os arquivos de novo reenvia.
- **Dados**: no Windows, configurações, fila e log ficam em `%LOCALAPPDATA%\Ferry` (migrados automaticamente da pasta antiga `PS5Sender`, que não é apagada); no self-hosted, em `/data`.
- **Verificação**: no fim confere o tamanho de cada arquivo no PS5. Só depois disso (e se você ativar a opção) apaga as partes originais.

## Documentação

- [Arquitetura e fluxo](docs/ARQUITETURA.md) — como a extração em streaming funciona
- [Compatibilidade FTP com o PS5](docs/FTP-PS5.md) — o que o ftpsrv suporta e por que isso importa
- [PKG e fPKG no PS5](docs/PKG-PS5.md) — jailbreak e fPKG por firmware, instaladores de pacote (pesquisa para uma fase futura)
- [Testes E2E](docs/TESTES.md) — como rodar e o que é verificado
- [Roadmap](docs/ROADMAP.md)
- [Último relatório E2E](e2e_report.md) · [E2E web (Docker)](e2e_report_web.md)

## Compilar

Requer [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet publish app -c Release -o dist                  # Windows: dist/Ferry.exe (~63 MB, 7-Zip embutido)
docker build -t ferry .                                # imagem Docker
dotnet publish web -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true   # binário Linux (precisa do 7zz ao lado ou no PATH)
```

O CI (GitHub Actions) roda o E2E no Windows e no Linux, constrói a imagem Docker e roda o E2E web contra ela (Chromium), e guarda o `.exe` e os binários Linux como artefatos. A imagem vai para o GHCR em todo push na `main` (`:main`) e nas tags. Para lançar uma versão, crie uma tag `v*` — ex.: `git tag v1.3.0-beta.1 && git push --tags` (com `-` vira pré-release) — e o CI publica a imagem (`:1.3.0-beta.1` e `:latest`) e anexa o `.exe` e os `.tar.gz` Linux à release.

## Limitações conhecidas

- **Notificação no PS5**: não implementada. Nem o ftpsrv nem o etaHEN expõem notificação pela rede; exigiria um payload ELF próprio (SDK do PS5) enviado ao elfldr.
- Pausar/retomar ou reabrir faz o 7-Zip reler o arquivo desde o início (não reenvia o que já está no PS5, mas gasta CPU/disco).
- Arquivos grandes vão por uma conexão só (o 7-Zip entrega um arquivo por vez); as conexões paralelas aceleram os arquivos pequenos.
- Pacotes `.pkg`/fPKG e imagens `.ffpkg` ainda não são aceitos; estão planejados (ver [PKG e fPKG no PS5](docs/PKG-PS5.md)).

## Créditos

- [7-Zip](https://www.7-zip.org/) (LGPL + restrição unRAR) — extração
- [FluentFTP](https://github.com/robinrodricks/FluentFTP) (MIT) — cliente FTP
- [Geist](https://github.com/vercel/geist-font) (OFL, `app/fonts/OFL.txt`) — fonte da interface, embutida no exe e no servidor web
- [ps5-payload-ftpsrv](https://github.com/john-tornblom/ps5-payload-ftpsrv) e [etaHEN](https://github.com/etaHEN/etaHEN) — servidores FTP no PS5

Licença: [MIT](LICENSE).
