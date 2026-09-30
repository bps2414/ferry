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

`e2e/ftpserver.py` (pyftpdlib) aceita **só os comandos do ftpsrv** e responde `502 Command not recognized` ao resto — foi assim que apareceram os problemas de `NLST`, `SIZE` e UTF-8 que um servidor completo escondia. Comandos recusados ficam em `%TEMP%\ps5sender-e2e\ftproot.recusados.txt`. Upload limitado a 40 MB/s por conexão.

## Casos

Cada jogo falso tem 6 arquivos (~24 MB incompressíveis, nome com acento, arquivo vazio) dentro de 2 pastas casca, em volumes de 5 MB. São determinísticos e ficam em cache (`%TEMP%\ps5sender-e2e-cache-v1`; mude `GenVersion` ao alterar o gerador).

| Caso | Formato |
|---|---|
| G1 | `.zip.001…` (7-Zip) |
| G2 | `.z01…` + `.zip` (gerador próprio de zip dividido PKWARE) |
| G3 | `.part1.rar…` (RAR5) |
| G4 | `.rar` + `.r00…` (RAR4, nomes antigos) |
| G5 | `.7z.001…` |
| G6 | `.7z.001…` com senha e cabeçalhos criptografados, adicionado por arrastar-soltar; 1ª senha digitada errada |
| G7 | `.rar` único com `PPSA…-app0` + `dec` (o `dec` vem antes no arquivo) |

Para cada caso: todas as partes menos a última → confere que fica “Aguardando partes”; a última chega escrita devagar (simula download) → confere SHA-256 de **cada** arquivo recebido, ausência de arquivos extras, originais apagados.

## Fases extras (rodada completa)

- **Já no PS5, sem APPE**: `EBOOT.BIN` completo não é extraído nem reenviado; `big.bin` pela metade é reenviado inteiro com `STOR`.
- **Já no PS5, com APPE**: segundo servidor com `APPE`; só a metade que faltava é enviada.
- **Fechar e reabrir**: para o engine no meio do envio, cria outro com a mesma `queue.json`; a fila volta sozinha e nada completo é reenviado.
- **Pausar/retomar** no meio do stream; **remover da fila** e re-adicionar; **testar conexão** com senha certa e errada.
