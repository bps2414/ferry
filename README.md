# PS5 Sender

[![CI](https://github.com/bps2414/ps5-sender/actions/workflows/ci.yml/badge.svg)](https://github.com/bps2414/ps5-sender/actions/workflows/ci.yml)

App para Windows que pega jogos compactados (`.zip`, `.rar`, `.7z`, inclusive divididos em partes), **extrai e envia ao mesmo tempo** para um PS5 com jailbreak via FTP — sem gravar os arquivos extraídos no seu disco.

- Um `.exe` portátil (sem instalar nada)
- Interface escura em português, com fila, progresso, velocidade e tempo restante
- Pausa, retoma, fecha e reabre sem perder o que já foi enviado

> Feito para uso com homebrew/backups próprios em console desbloqueado. Use por sua conta e risco.

## Download

Baixe o `PS5Sender.exe` na página de [Releases](../../releases) e execute. Requer Windows 10/11 x64.

## Como usar

1. Rode o payload de FTP no PS5 (**ftpsrv**, porta 2121, ou o FTP do **etaHEN**, porta 1337).
2. Abra o app → **Configurações**: IP do PS5, porta, destino (**M.2** `/mnt/ext1/homebrew` ou **SSD interno** `/data/homebrew`) → **Salvar**. O cartão no canto inferior esquerdo mostra se o PS5 está online.
3. Na **Fila**, clique no centro da tela (abre o seletor do Windows) ou arraste os arquivos para a janela. Selecione **todas as partes** de uma vez.
4. Pronto: quando todas as partes estiverem presentes, o jogo é extraído e enviado para `<destino>/<pasta do jogo>`.

Opcional: uma **pasta monitorada** — tudo que cair nela entra na fila sozinho (útil para a pasta de downloads).

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
- **Fechar e reabrir**: a fila é salva; ao reabrir, volta sozinha e continua de onde parou.
- **Verificação**: no fim confere o tamanho de cada arquivo no PS5. Só depois disso (e se você ativar a opção) apaga as partes originais.

## Documentação

- [Arquitetura e fluxo](docs/ARQUITETURA.md) — como a extração em streaming funciona
- [Compatibilidade FTP com o PS5](docs/FTP-PS5.md) — o que o ftpsrv suporta e por que isso importa
- [Testes E2E](docs/TESTES.md) — como rodar e o que é verificado
- [Último relatório E2E](e2e_report.md)

## Compilar

Requer [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet publish app -c Release -o dist
```

Gera `dist/PS5Sender.exe` (single-file, self-contained, ~63 MB, com o 7-Zip embutido).

O CI (GitHub Actions) compila, roda o E2E completo e guarda o `.exe` como artefato em todo push. Para lançar uma versão, crie uma tag `v*` — ex.: `git tag v1.1.0-beta.1 && git push --tags` (com `-` vira pré-release) — e o CI anexa o `.exe` à release.

## Limitações conhecidas

- **Notificação no PS5**: não implementada. Nem o ftpsrv nem o etaHEN expõem notificação pela rede; exigiria um payload ELF próprio (SDK do PS5) enviado ao elfldr.
- Pausar/retomar ou reabrir faz o 7-Zip reler o arquivo desde o início (não reenvia o que já está no PS5, mas gasta CPU/disco).
- Arquivos grandes vão por uma conexão só (o 7-Zip entrega um arquivo por vez); as conexões paralelas aceleram os arquivos pequenos.
- A senha do arquivo compactado não é salva: é pedida de novo depois de reabrir o app.

## Créditos

- [7-Zip](https://www.7-zip.org/) (LGPL + restrição unRAR) — extração
- [FluentFTP](https://github.com/robinrodricks/FluentFTP) (MIT) — cliente FTP
- [ps5-payload-ftpsrv](https://github.com/john-tornblom/ps5-payload-ftpsrv) e [etaHEN](https://github.com/etaHEN/etaHEN) — servidores FTP no PS5

Licença: [MIT](LICENSE).
