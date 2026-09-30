# Compatibilidade FTP com o PS5

O [ps5-payload-ftpsrv](https://github.com/john-tornblom/ps5-payload-ftpsrv) implementa só um conjunto mínimo de comandos (lista do `main.c`):

```
CDUP CWD DELE LIST MKD NOOP PASV PORT PWD QUIT REST RETR RMD RNFR RNTO SIZE STOR SYST TYPE USER
(+ KILL, MTRW e outros específicos do PS5)
```

Versões mais novas (e o FTP do etaHEN) também aceitam `APPE`. Qualquer outro comando recebe `502 Command not recognized`.

## O que isso quebrava e como o app lida

| Comportamento do servidor | Efeito com um cliente FTP comum | O que o app faz |
|---|---|---|
| Sem `FEAT` | FluentFTP não sabe que existe `SIZE` e cai em ASCII | Manda `SIZE` direto (após `TYPE I`) e força UTF-8 |
| Sem `NLST`/`MLSD` | `UploadStream`/`FileExists` do FluentFTP falham com 502 | Envia com `OpenWrite`/`OpenAppend` (só `TYPE` + `PASV` + `STOR`/`APPE`) |
| Sem `EPSV` | tentativa extra a cada transferência | Usa `PASV` direto |
| `STOR` abre com `O_TRUNC` e ignora `REST` | “retomar” com `REST`+`STOR` apagaria o arquivo | Nunca usa `REST` para upload |
| `APPE` pode não existir | parcial não continua | Sonda: `APPE` sem argumento → `501` = existe; `502` = não. Sem `APPE`, reenvia o parcial inteiro. Se o `APPE` falhar na prática, repete sem ele automaticamente |
| `LIST` com argumento começando em `-` lista a pasta atual | listagem recursiva errada | Não usa listagem: estado remoto e verificação são por `SIZE` arquivo a arquivo |
| `USER` já responde `230` (sem senha) | — | Funciona com qualquer usuário/senha |

## ftpsrv novo (ps5-payload-dev/ftpsrv, o que tem `APPE`)

Conferido no código (`cmd.c`, `srv.c`, set/2026):

- **`SELF` ligado por padrão** (`env.self2elf = 1`): o `SIZE` de um SELF (eboot.bin, .sprx, .prx assinados) devolve o tamanho do ELF de dentro, não o do arquivo. Aí o tamanho nunca bate, o app reenvia à toa e a verificação falha. `SELF` é um liga/desliga que responde `226 SELF transfer mode enabled|disabled`. O app manda `SELF` em toda conexão e repete se a resposta for "enabled". No ftpsrv antigo, o comando responde `502` e nada muda.
- **`STOR` trunca nas duas versões**: a antiga abre com `O_TRUNC`; a nova abre sem `O_TRUNC`, mas faz `ftruncate` no fim do envio. Por isso não é preciso `DELE` antes do `STOR`.
- **`APPE`** faz `stat` e continua do tamanho atual. O app só usa `APPE` em parcial que ele mesmo começou (registrado na `queue.json` com o caminho remoto e o tamanho final). Qualquer outro arquivo remoto de tamanho diferente é reenviado inteiro com `STOR`, porque um arquivo menor pode ser outra versão, e continuar geraria um arquivo corrompido do tamanho certo.
- Depois de cada `STOR`/`APPE`, o app lê a resposta final e confere o `SIZE` na hora. Se não bater, falha o arquivo com "o PS5 não deixou sobrescrever…".

## Loaders que instalam sozinhos (ShadowMount+)

O ShadowMount+ (`sm_gameinfo.c: directory_has_param_json`) reconhece um jogo pela existência de **`sce_sys/param.json`**. A checagem de estabilidade (`wait_for_stability_fast`) só olha o mtime da pasta `sce_sys`. Resultado: com o `param.json` presente, ele instala o jogo no meio do envio. Depois de instalado, ele monta um overlay de backport (nullfs) por cima da pasta, e aí o `SIZE`/`RETR` de `eboot.bin`, `fakelib/*`, `sce_module/*.prx` e `sce_sys/about/right.sprx` passam a mostrar a versão do backport, mesmo depois de `DELE`+`STOR` responderem 226.

O que o app faz:

- **Publicação atômica**: `sce_sys/param.json` e `sce_sys/param.sfo` sobem como `*.ferry-part`. Só depois que todo o resto foi enviado e conferido eles são renomeados (`RNFR`/`RNTO`, apagando antes o nome final se existir). O `param.sfo` entra por garantia: não achei código do itemzflow para confirmar o que ele usa. A retomada reconhece o arquivo com sufixo.
- **Jogo já instalado**: se o `param.json` ou o `param.sfo` com o nome final já existe no destino, o card avisa e não envia. "Tentar de novo" reenvia mesmo assim. Nesse reenvio, uma divergência nos arquivos típicos de backport vira aviso no log, não erro.

## Notificação no PS5

Não há comando FTP nem API de rede para notificações no ftpsrv ou no etaHEN. O caminho seria um payload ELF (compilado com o SDK do PS5, chamando `sceKernelSendNotificationRequest`) enviado ao elfldr na porta 9021. Não implementado.
