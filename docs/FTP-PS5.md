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

## Notificação no PS5

Não há comando FTP nem API de rede para notificações no ftpsrv ou no etaHEN. O caminho seria um payload ELF (compilado com o SDK do PS5, chamando `sceKernelSendNotificationRequest`) enviado ao elfldr na porta 9021. Não implementado.
