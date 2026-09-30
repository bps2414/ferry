# Testes E2E

[English](en/TESTS.md) · **Português (BR)** · [Español](es/PRUEBAS.md)

Dois testes ponta a ponta:

- `e2e/` (C#), sem interface: usa a mesma `Engine` (Ferry.Core) contra um servidor FTP local. Roda no **Windows e no Linux** (o CI roda nos dois).
- `e2e/web/` (Node + Playwright): sobe a **imagem Docker** e dirige a interface web num Chromium de verdade (ver [E2E web](#e2e-web-docker) abaixo).

## Rodar

Requer Python 3 com `pyftpdlib` (`pip install pyftpdlib`). No Linux, também o 7-Zip com RAR no PATH (`apt install 7zip 7zip-rar`).

```bash
dotnet run --project e2e              # completo (~40 s)
dotnet run --project e2e -- G2 G7     # modo rápido: só esses casos, sem fases extras (~15 s)
```

Gera `e2e_report.md` na raiz e sai com código 0 (passou) ou 1 (falhou). Na 1ª execução baixa o RAR 6.24 oficial do rarlab.com para `e2e/tools/` (Windows: `Rar.exe` do instalador; Linux: `rar` do rarlinux), usado só para **criar** os arquivos de teste; o app só extrai, com o 7-Zip. No Windows o 7-Zip é o `app/tools/7z.exe` embutido no app; no Linux, o `7zz`/`7z` do sistema.

## O servidor de teste imita o ftpsrv

`e2e/ftpserver.py` (pyftpdlib) aceita **só os comandos do ftpsrv** e responde `502 Command not recognized` ao resto — foi assim que apareceram os problemas de `NLST`, `SIZE` e UTF-8 que um servidor completo escondia. Comandos recusados ficam em `%TEMP%\ferry-e2e\ftproot.recusados.txt`. Upload limitado a 40 MB/s por conexão (10 MB/s nos casos .exfat e "Transferir agora", que precisam pegar o envio no meio).

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
  - ao reabrir sem senhas conhecidas, abre com a senha da fila (cifrada: DPAPI no Windows, AES-GCM no Linux), sem diálogo.
- **Salvar atômico**: um `queue.json.tmp` pela metade não atrapalha reabrir.
- **Fechar e reabrir**: para o engine no meio do envio, cria outro com a mesma `queue.json`; a fila volta sozinha e nada completo é reenviado.
- **Pausar/retomar** no meio do stream; **remover da fila** e re-adicionar; **testar conexão** com senha certa e errada.

## Contratos de webhook

`dotnet run --project e2e -- --webhook` usa HTTP local falso, sem FTP ou credenciais reais: configuração antiga, validação/persistência, HTTP 401/429/500/302, timeout, conexão recusada, três formatos, pt-BR/en/Automático, fallback Windows, limites, segredos, fila cheia, snapshots e encerramento. A CI roda no Windows e Linux. O E2E web adicional verifica os eventos com jogos/FTP reais de teste, senha incorreta sem duplicação, teste que aguarda salvar, três receptores, idioma após reinício, página fechada e falha HTTP sem afetar hashes do FTP.

Receptores reais não são acessados pelos testes. Relatórios web distinguem DLL local de Docker; execução local não comprova CI remota. Detalhes de configuração e entrega em [WEBHOOK.md](WEBHOOK.md).

## E2E web (Docker)

### Contratos de localização

`dotnet run --project e2e -- --localization` verifica configuração antiga com idioma automático, persistência, variantes/fallback, mensagens aninhadas, diagnósticos brutos e códigos de erro de extração, progresso/ações e igualdade de chaves/parâmetros nos três catálogos (núcleo, API, UI). Não sobe FTP nem gera arquivos de jogo. A CI executa esse modo no Windows e Linux.

### Idiomas e execução local

O comando `node web.mjs` executa o fluxo completo duas vezes, em dados e processos isolados: `pt-BR` e `en`. Os relatórios individuais são `e2e_report_web_pt-BR.md` e `e2e_report_web_en.md`; o relatório agregado mantém `e2e_report_web.md`. As capturas ficam em `e2e/web/report/<idioma>/`. Para rodar só um idioma, configure `FERRY_TEST_LOCALE=pt-BR` ou `en`.

Além de preservar as verificações de hash, login, pausa, retomada e reinício, confere textos explícitos por idioma, Automático com ordem de preferência/fallback, preferência explícita sobre o navegador, rejeição de idioma inválido, persistência e troca durante upload/transferência. A troca com diálogo aberto aciona o mesmo evento do seletor via DOM para verificar a preservação do modal e da senha; não simula clicar atrás do modal.

Sem Docker, há um modo local que inicia e reinicia a DLL do servidor com dados temporários isolados. Ele exercita o produto e a persistência, mas não valida a imagem Docker:

```powershell
dotnet build web -c Release
cd e2e/web
npm ci
npx playwright install chromium
$env:FERRY_WEB_MODE = "local"
node web.mjs
```

`FERRY_WEB_DLL`, `FERRY_PYTHON` e `FERRY_7ZIP` permitem caminhos explícitos. No Windows, o gerador usa `app/tools/7z.exe` e `python`; no Linux, busca `7zz`/`7z` e `python3`. Python requer `pyftpdlib` em ambos. A DLL local usa `FERRY_DATA`, `FERRY_GAMES` e porta exclusivos da rodada; nunca usa a configuração real do usuário.

`e2e/web/web.mjs` sobe a imagem com `--network host` e `--user <seu uid>` (como no `docker-compose.yml`), o mesmo `ftpserver.py` (com APPE) e abre a página num Chromium (Playwright). Gera `e2e_report_web.md` na raiz e as capturas em `e2e/web/report/`.

```bash
docker build -t ferry:e2e .
cd e2e/web && npm ci && npx playwright install chromium && node web.mjs
```

Requer Docker, Python com `pyftpdlib` e 7-Zip (para montar os arquivos de teste). O que confere:

- **Login**: sem login a API responde 401; a primeira abertura pede para criar o usuário (senha curta é recusada); sair → 401; senha errada → mensagem; certa → entra.
- **Configurações**: cada campo salva sozinho; porta inválida mostra o erro e não é gravada; pasta monitorada já vem `/games`; "Testar conexão" e o cartão do PS5 ficam "online" (um teste antigo não sobrescreve o novo).
- **Upload + senha no navegador**: os volumes de um `.7z.001…` com senha e cabeçalhos cifrados são enviados pela página; o diálogo de senha abre no navegador (1ª errada → "Senha incorreta", 2ª certa); SHA-256 de cada arquivo no PS5 falso, capa e `PPSA…`, senha aprendida.
- **Pasta monitorada + pausar/retomar** pela página (progresso congela) e **reiniciar o container** no meio do envio de uma imagem `.exfat` de 400 MB: volta sozinho, continua com `APPE`, o login continua valendo, e o jogo já concluído volta como "Concluído" sem reenviar nada.
- **Upload que continua**: 1º bloco enviado pela API, bloco repetido → 409, o parcial não entra na fila, e a página continua do byte 16 MB; hash confere no servidor e no PS5.
- **Celular** (390 px): sem rolagem lateral; **nenhum erro de JavaScript** na página.
