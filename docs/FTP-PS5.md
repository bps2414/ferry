# Compatibilidade FTP com o PS5

[English](en/FTP-PS5.md) · **Português (BR)** · [Español](es/FTP-PS5.md)

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

## Imagens `.exfat` (ShadowMount+)

Conferido no código (cópia em [aloksaurabh/elf-arsenal](https://github.com/aloksaurabh/elf-arsenal), `ShadowMountPlus-main`, set/2026):

- **Onde procura**: nos *scanpaths*. Padrão (`include/sm_paths.h: SM_DEFAULT_SCAN_PATHS_INITIALIZER`): `/data/homebrew`, `/data/etaHEN/games`, `/mnt/ext0|ext1/homebrew`, `/mnt/ext0|ext1/etaHEN/games`, `/mnt/usb0..7/homebrew`, `/mnt/usb0..7/etaHEN/games`, `/mnt/usb0..7`, `/mnt/ext0`, `/mnt/ext1`. É configurável: uma ou mais linhas `scanpath=` em `/data/shadowmount/config.ini` substituem a lista inteira (`sm_config_mount.c`, parser da chave `scanpath`; README "Scan paths").
- **Profundidade**: `sm_scan_tree.c: sm_scan_tree_walk` visita imagens nos arquivos da pasta listada. Com `scan_depth=1` (padrão, `include/sm_limits.h: DEFAULT_SCAN_DEPTH`), só a raiz do scanpath é listada, então a imagem tem que estar **direto** em `<scanpath>/<nome>.exfat`. Com `scan_depth=2` ou `recursive_scan=1`, um nível de subpasta também vale.
- **Como reconhece**: só pela extensão, sem diferenciar maiúsculas: `sm_image.c: detect_image_fs_type` (`strrchr(name, '.')` + `strcasecmp`) aceita `.ffpkg` (UFS), `.exfat` e `.ffpfs` (PFS). Arquivos que começam com `.` são ignorados. **`X.exfat.ferry-part` tem extensão `.ferry-part` e é ignorado**, então o sufixo da Fase 1 serve.
- **Estabilidade**: `sm_image.c: maybe_mount_image_file` chama `sm_mount_device.c: is_source_stable_for_mount`, que usa `sm_stability.c: is_path_stable_now`. Essa função compara o maior entre `st_ctime` e `st_mtime` **do próprio arquivo** com `stability_wait_seconds` (padrão 10 s, `DEFAULT_STABILITY_WAIT_SECONDS`, até 3600). Não olha tamanho. Um envio pausado ou lento (mais de 10 s sem escrita) com o nome final seria montado pela metade. Por isso a publicação atômica é necessária.
- **Conteúdo esperado**: os arquivos do jogo na raiz da imagem (`/sce_sys/param.json` direto, sem pasta extra; README "Image layout requirement"). O app não abre a imagem e não confere isso.

O que o app faz:

- Aceita `.exfat` solto (stream direto do disco) ou dentro de um arquivo compactado sem pasta de jogo (stream do `7z x -so`). Destino: `ImageDir` nas Configurações (padrão `/mnt/ext1/homebrew`), com o nome do arquivo.
- A imagem sobe como `<nome>.exfat.ferry-part` e só é renomeada (`RNFR`/`RNTO`) depois do `SIZE` conferido. A retomada (APPE só em parcial começado pelo app) e o aviso "Jogo já instalado" (se `<nome>.exfat` já existe) seguem as regras das pastas de jogo.

**Em aberto:**

- Reenviar por cima de uma imagem **já montada**: o app apaga o nome final e renomeia o novo. `sm_image.c: cleanup_stale_image_mounts` só desmonta quando o caminho some e a montagem continua legível. Como o caminho volta a existir logo depois do `RNTO`, o SM+ pode continuar usando a imagem antiga (inode apagado) até reiniciar o PS5 ou o SM+. Não testado no console.
- Não achei como o SM+ reage ao `DELE` de uma imagem montada (se o ftpsrv consegue apagar o arquivo aberto pelo `lvd`/`md`). No FreeBSD o `unlink` de arquivo aberto funciona, mas não conferi no PS5.

Outros formatos de imagem (`.ffpkg`, `.ffpfsc`) e pacotes `.pkg`/fPKG: ver [PKG-PS5.md](PKG-PS5.md).

## Notificação no PS5

Não há comando FTP nem API de rede para notificações no ftpsrv ou no etaHEN. O caminho seria um payload ELF (compilado com o SDK do PS5, chamando `sceKernelSendNotificationRequest`) enviado ao elfldr na porta 9021. Não implementado.
