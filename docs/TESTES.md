# Testes E2E

Um único teste ponta a ponta (`e2e/`), sem interface: usa a mesma `Engine` do app contra um servidor FTP local.

## Rodar

Requer Python 3 com `pyftpdlib` (`pip install pyftpdlib`).

```bash
dotnet run --project e2e              # completo (~40 s)
dotnet run --project e2e -- G2 G7     # modo rápido: só esses casos, sem fases extras (~15 s)
```

Gera `e2e_report.md` na raiz e sai com código 0 (passou) ou 1 (falhou). Na 1ª execução baixa o `Rar.exe` 6.24 oficial do rarlab.com para `e2e/tools/` (usado só para **criar** os arquivos de teste; o app só extrai, com o 7-Zip).

## O servidor de teste imita o ftpsrv

`e2e/ftpserver.py` (pyftpdlib) aceita **só os comandos do ftpsrv** e responde `502 Command not recognized` ao resto — foi assim que apareceram os problemas de `NLST`, `SIZE` e UTF-8 que um servidor completo escondia. Comandos recusados ficam em `%TEMP%\ferry-e2e\ftproot.recusados.txt`. Upload limitado a 40 MB/s por conexão.

## Casos

Cada jogo falso tem 6 arquivos (~24 MB incompressíveis, nome com acento, arquivo vazio) dentro de 2 pastas casca, em volumes de 5 MB. São determinísticos e ficam em cache (`%TEMP%\ferry-e2e-cache-v3`; cada um tem `param.sfo` real, `param.json` e `icon0.png` de verdade; mude `GenVersion` ao alterar o gerador).

| Caso | Formato |
|---|---|
| G1 | `.zip.001…` (7-Zip) |
| G2 | `.z01…` + `.zip` (gerador próprio de zip dividido PKWARE) |
| G3 | `.part1.rar…` (RAR5) |
| G4 | `.rar` + `.r00…` (RAR4, nomes antigos) |
| G5 | `.7z.001…` |
| G6 | `.7z.001…` com senha e cabeçalhos criptografados, adicionado por arrastar-soltar; abre com a 2ª senha conhecida, sem diálogo |
| G7 | `.rar` único com `PPSA…-app0` + `dec` (o `dec` vem antes no arquivo) |

Para cada caso: todas as partes menos a última → confere que fica “Aguardando partes”; a última chega escrita devagar (simula download) → confere SHA-256 de **cada** arquivo recebido, ausência de arquivos extras, originais apagados.

## Fases extras (rodada completa)

Em todos os casos, o E2E também confere capa/título (`param.sfo` + `icon0.png`) e "faltando <volume>" (rar e zip dividido). Confere ainda a publicação atômica: pela ordem do log do servidor, o `RNTO` de `param.json`/`param.sfo` vem depois do último `STOR`/`APPE` do jogo.

- **Já no PS5, sem APPE**: `EBOOT.BIN` completo não é extraído nem reenviado; `big.bin` pela metade, não começado pelo app, é reenviado inteiro com `STOR`.
- **Já no PS5, com APPE e SELF** (imita o ftpsrv novo, cujo `SIZE` de eboot.bin mente com o `SELF` ligado):
  - `big.bin` pela metade registrado na fila: só a metade que faltava vai com `APPE`;
  - `EBOOT.BIN` menor e de outra versão: `STOR` inteiro;
  - `icon0.png` maior no PS5: fica do tamanho certo.
- **Jogo já instalado**: avisa sem enviar; "Tentar de novo" reenvia por cima.
- **Imagem `.exfat` (ShadowMount+)**: servidor com APPE, `ImageDir = /data/homebrew`. Os arquivos (`IMG1.exfat` de 40 MB solto e `IMG2.exfat` de 12 MB dentro de `.part1.rar`) têm bytes aleatórios determinísticos e ficam em cache.
  - envio novo: hash igual, sem capa, sem sobra de `.ferry-part`, e pela ordem do log do servidor o `RNTO` para o nome final vem depois do último `STOR`/`APPE`. Pausar e retomar no meio continua com `APPE`;
  - reabrir: o parcial registrado na fila continua com `APPE` (só a metade que faltava) e o parcial menor de outra versão vai inteiro com `STOR`;
  - já existente: avisa "Jogo já instalado…" sem enviar, e "Tentar de novo" reenvia por cima.
- **Transferir agora**: com uma imagem de 80 MB (gerada na hora) enviando, `SendNow` no IMG2 (`.part1.rar`) → a imagem volta para `NaFila` (não pausa), IMG2 fica `Verificado` primeiro, e a imagem continua só com o que faltava (`STOR` + `APPE` = tamanho total).
- **Log persistente**: `log.txt` tem comando e resposta de STOR/APPE/SIZE.
- **Senha aprendida e lembrada** (G6, na fase fechar/reabrir):
  - diálogo com a 1ª senha errada;
  - a certa entra nas senhas conhecidas;
  - ao reabrir sem senhas conhecidas, abre com a senha da fila (DPAPI), sem diálogo.
- **Salvar atômico**: um `queue.json.tmp` pela metade não atrapalha reabrir.
- **Fechar e reabrir**: para o engine no meio do envio, cria outro com a mesma `queue.json`; a fila volta sozinha e nada completo é reenviado.
- **Pausar/retomar** no meio do stream; **remover da fila** e re-adicionar; **testar conexão** com senha certa e errada.
