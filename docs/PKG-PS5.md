# PKG e fPKG no PS5 com jailbreak

[English](en/PKG-PS5.md) · **Português (BR)** · [Español](es/PKG-PS5.md)

Pesquisa para a Fase 6 do [roadmap](ROADMAP.md). Situação em **30/09/2026**. A cena muda rápido; confira as fontes antes de implementar. O que não consegui confirmar está marcado como **a confirmar**.


## Uso no Ferry — Fase 6

Aceita `.pkg` solto ou **um único PKG por compactado** (ZIP/RAR/7z, volumes e senha). Arquivos auxiliares são descartados; vários PKGs ou mistura com dump/imagem geram erro explícito.

Em Configurações, mantenha o FTP do PS5, informe a pasta de pacotes (padrão `/data/ferry/pkg`) e a porta DPI (9090). Ative `DPI=1` em `/data/etaHEN/config.ini`. “Testar DPI” apenas verifica a porta; não instala um pacote.

O Ferry transmite por FTP sem extrair o pacote inteiro no PC, confere SIZE e publica com rename antes de pedir a instalação pelo caminho local. O console precisa de espaço para o PKG e o jogo instalado. Windows e web usam o mesmo fluxo.

“Pacote pronto” significa transferência preparada; “Instalação solicitada” significa pedido aceito pelo DPI. Acompanhe o término na fila de downloads do PS5. “Verifique no PS5” significa resultado desconhecido: confira antes de reenviar, pois o primeiro pedido pode ter sido aceito. Reiniciar o Ferry não repete pedidos enviados.

PKG de origem, compactado e pacote remoto são preservados, inclusive com “Apagar originais” ligado. Remover o card não cancela a instalação nem apaga o pacote remoto. Não há limpeza automática, confirmação remota de conclusão, HTTP direto ou ordenação automática de jogo/update/DLC nesta entrega.

`.exfat`, `.ffpkg`, `.ffpfs` e `.ffpfsc` continuam como imagens do ShadowMount+, na pasta de imagens; nunca passam pelo DPI. O cabeçalho identifica CNT/PS4 ou FIH/PS5, sem comprovar assinatura ou compatibilidade de firmware. O funcionamento por caminho local exige aceitação no seu PS5; fixtures locais não a substituem.

Validação focada: `dotnet run --project e2e -- --pkg`. Windows: `dotnet run --project e2e/windows -c Release`. E2E web roda pt-BR/en. Docker, CI remota e console precisam de execução própria.

## Pesquisa de referência

A pesquisa abaixo descreve o estado consultado em 30/09/2026, incluindo propostas anteriores. Para o comportamento implementado, vale o contrato acima; HTTP direto permanece futuro e compatibilidade por firmware não é uma garantia do Ferry.
## Formas de ter um jogo no PS5 com jailbreak

| Forma | O que é | Quem instala/carrega | Ferry antes da Fase 6 |
|---|---|---|---|
| **Pasta do jogo** (dump) | Pasta `PPSA…` com `eboot.bin` e `sce_sys/` | ShadowMount+ (instala sozinho ao achar `sce_sys/param.json`) ou itemzflow | ✅ extrai e envia por FTP |
| **Imagem** `.exfat` | O jogo dentro de uma imagem de disco | ShadowMount+ monta | ✅ envia inteira para a pasta de imagens |
| **Imagem** `.ffpkg` (UFS) | Idem; o formato **recomendado** pelo ShadowMount+ | ShadowMount+ monta | ❌ ainda não (mesmo caminho do `.exfat`) |
| **Imagem** `.ffpfs` / `.ffpfsc` (PFS, `c` = comprimida) | Idem, experimental; a comprimida lê a ~150–250 MB/s | ShadowMount+ monta | ❌ ainda não |
| **fPKG de PS4** | Pacote `.pkg` de PS4 com assinatura falsa | Instalador de pacotes (etaHEN DPI e outros) + **kstuff** para rodar | ❌ |
| **fPKG de PS5** | Pacote `.pkg` de PS5 com assinatura falsa, gerado de um dump | Instalador de pacotes + **kstuff-lite** com suporte a fPKG de PS5 | ❌ |

- **fPKG** = *fake package*: um `.pkg` montado fora da Sony. Precisa de um "enabler" no kernel (kstuff) para o sistema aceitar e rodar.
- **kstuff** faz o PS5 aceitar executáveis e pacotes com assinatura falsa (fSELF e fPKG). Ele é "um debugger vigiando o kernel em tempo real", e por isso pesa no desempenho. Existe o plugin `kstuff-toggle` para desligar quando não precisa.
- Um `.pkg` **não dá para instalar mandando por FTP para uma pasta**. Alguém no PS5 tem que chamar o instalador do sistema. Por isso o Ferry precisa conversar com um instalador, não só com o FTP.

## Jailbreak por firmware

| Firmware | Caminho público | Observação |
|---|---|---|
| 3.00–4.51 | IPV6 (kernel) via WebKit ou BD-JB2 | O primeiro jailbreak |
| até 5.50 | UMTX (WebKit, CVE-2024-43102) | — |
| 1.00–10.01 | Lapse (kernel) | Várias entradas (BD-J, WebKit, YouTube) |
| 4.03–12.40 (uma fonte diz 13.40, **a confirmar**) | Y2JB: entrada pelo app do YouTube; kernel com Lapse (até 10.01) ou P2JB (até 12.70) | — |
| **7.00–13.60** | **Relapse**: WebKit + condição de corrida no kernel (setembro de 2026) | PS5 e PS5 Pro. Carrega em segundos; costuma mandar kstuff, ShadowMount+ e etaHEN em seguida |
| **14.00** (16/09/2026) | nenhum público | Quem atualizou perdeu o jailbreak |

## fPKG por firmware

| Firmware | fPKG de PS4 | fPKG de PS5 |
|---|---|---|
| 3.00–4.51 | ✅ kstuff (desde 2023) | via kstuff-lite 1.11: 2.50 em diante |
| 5.xx–7.61 | ✅ kstuff (abril de 2025) | ✅ kstuff-lite 1.11 |
| 8.xx–9.xx | ✅ kstuff-lite 1.11 (**a confirmar**) | ✅ kstuff-lite 1.11 |
| 10.00–10.01 | ✅ kstuff 1.6.6 (EchoStretch) e kstuff-lite | ✅ kstuff-lite 1.11 |
| 11.00–11.60 | kstuff-lite 1.11 (**a confirmar**) | ✅ até 11.40; "corrigido para 11.60" no changelog |
| 12.00–12.70 | kstuff-lite 1.11 (**a confirmar**) | ❌ o método novo (PPR, do Drakmor) não está ligado nessa faixa |
| 13.00–13.60 | kstuff-lite 1.11 carrega (fSELF); fPKG **a confirmar** | ❌ |
| 14.00 | ❌ sem jailbreak | ❌ |

- **kstuff-lite 1.11 beta** (EchoStretch, com o trabalho de fPKG de PS5 do Drakmor): roda de 1.00 a 13.60. O fPKG de PS5 vai até 11.40, com correção para 11.60.
- **Em 12.xx e 13.xx**, hoje, o caminho para jogos de PS5 são os **dumps e imagens do ShadowMount+**, que o Ferry já envia. O fPKG de PS5 ainda não funciona lá.
- **Ferramentas para gerar fPKG de PS5** a partir de um dump: FPKG-GUI (Drakmor) e PS5-FPKG-Builder. **Para PS1/PS2/PSP**: PS-Classics-fPKG-Builder.

## Como os instaladores recebem um `.pkg` pela rede

| Serviço | Porta | Como pedir | De onde vem o `.pkg` |
|---|---|---|---|
| **etaHEN DPI v1** | 9090 (TCP) | Manda `{ "url" : "http://…" }`; responde `{ "res" : "0" }` e fecha a conexão | O PS5 baixa da URL. Segundo a documentação, também aceita caminho local (`/data/pkg/jogo.pkg`, **a confirmar**) |
| **etaHEN DPI v2** | 12800 (HTTP) | Interface web no PS5; ferramentas mandam a URL para ela | URL (HTTP) |
| **ezRemote DPI** (payload à parte) | 9040 (TCP) | Manda a URL em texto (`echo URL \| nc PS5 9040`) | Só http/https. Não precisa de etaHEN nem kstuff para rodar |
| **pkg-sender** (payload próprio) | 12800 + beacon UDP 12801 | `POST /api/install` (JSON) ou `GET /install?url=`; progresso em `GET /api/status` | O PC serve o arquivo por HTTP com *range* (porta 9898). Imagens (`.exfat`, `.ffpkg`, `.ffpfsc`) são copiadas para `/data/homebrew` |
| **ps5upload** (payload próprio) | 9113–9114 | Protocolo binário próprio (FTX2) | Upload do PC, NAS/SMB ou link HTTP |

- O DPI do etaHEN vem **desligado**. Para ligar: `DPI=1` (v1) e/ou `DPI_v2=1` no `config.ini` do etaHEN.
- Portas do etaHEN: FTP 1337, elfldr 9021, klog 9081, DPI 9090, DPI v2 12800.
- O DPI v1 **não devolve progresso**: só "aceitei". O andamento aparece na fila de downloads do PS5. Se existe uma API de progresso: **a confirmar** no código do etaHEN.

## Outras ferramentas que fazem parte do que o Ferry faz

- **ps5upload**: desktop (Windows, macOS, Linux, Android) e web por Docker. Envia jogos e pacotes, descompacta `.zip`/`.7z`/`.rar` no PC e manda já extraído, retoma, confere com BLAKE3 e instala `.pkg` com instalador próprio no PS5. Firmware 1.00–13.60. É o projeto mais próximo do Ferry. A diferença do Ferry: não precisa de payload próprio (usa o FTP que já existe) e é mais simples.
- **pkg-sender**, **PS5 PKG Virtual Shop** e **etahen-pkg-loader**: instalam `.pkg` pelo DPI ou por receptor próprio.

## Proposta para o Ferry (Fase 6)

1. **Imagens `.ffpkg`, `.ffpfs` e `.ffpfsc`** do mesmo jeito que o `.exfat`: mesmo destino, `.ferry-part` e renomear no fim. O ShadowMount+ reconhece pela extensão (`detect_image_fs_type`; conferir se `.ffpfsc` já está no código). É o passo mais barato e vale em **qualquer** firmware com jailbreak.
2. **`.pkg` solto** (PS4 ou PS5): o Ferry serve o arquivo por HTTP, com *range* e um token aleatório na URL, sem login. Depois pede a instalação ao etaHEN DPI v1 (porta 9090) com essa URL, e o PS5 baixa direto. Com `network_mode: host`, o servidor já está na rede de casa.
3. **`.pkg` dentro de um compactado**: o PS5 baixa com *range*, e o `7z x -so` não dá acesso aleatório. Opções:
   - (a) mandar o `.pkg` por FTP para o PS5 (em streaming, como hoje) e pedir a instalação pelo caminho local, se o DPI aceitar. Gasta o espaço do `.pkg` mais o do jogo instalado no PS5;
   - (b) extrair o `.pkg` no servidor e servir. Gasta disco no servidor, o que quebra a regra "nada extraído no disco".

   Decidir depois de confirmar o caminho local do DPI.
4. **Card e aviso por firmware**: identificar PS4 ou PS5 pelo cabeçalho do `.pkg` (PS4 começa com `\x7FCNT`; o do PS5, **a confirmar**). Avisar quando o tipo não roda no firmware informado (ex.: fPKG de PS5 em 12.xx).
5. **Configurações**: "Instalador de pacotes" (etaHEN DPI, porta 9090), com um teste de conexão. Lembrar que o DPI vem desligado no etaHEN.
6. **E2E**: um DPI falso (TCP 9090) recebe o JSON e baixa a URL com *range* e retomada. O teste confere o hash, o token recusado sem ser o certo e o erro quando o DPI não responde.

**Em aberto antes de implementar**: API de progresso do DPI; caminho local no DPI v1; cabeçalho do `.pkg` de PS5; fPKG de PS4 em 11.xx–13.xx com kstuff-lite; se vale apoiar os receptores próprios (pkg-sender, ezRemote DPI) além do etaHEN.

## Fontes

- etaHEN: [README](https://github.com/etaHEN/etaHEN) (portas, DPI, `config.ini`) · [1.7b com instalação remota (Wololo)](https://wololo.net/2024/02/26/ps5-release-etahen-1-7b-adds-remote-package-install-support/)
- kstuff: [4.51 (Wololo, 2023)](https://wololo.net/2023/11/05/ps5-sleirsgoevys-kstuff-and-fpkg-ps4-support-added-to-firmware-4-51-etahen-updated-to-support-4-51-as-well/) · [até 7.61 (Wololo, 2025)](https://wololo.net/2025/04/23/ps5-kstuff-gets-ported-to-all-supported-firmwares-up-to-7-61-included-kstuff-toggle-plugin/) · [kstuff-lite 1.11](https://github.com/EchoStretch/kstuff-lite/releases/tag/v1.11) · [resumo 1.00–13.60 (onejailbreak)](https://onejailbreak.com/blog/kstuff-lite-1-11-adds-ps5-firmware-1-00-13-60-support/)
- fPKG de PS5: [FPKG até 11.40 (Se7enSins)](https://www.se7ensins.com/media/fake-packages-announced-for-ps5-up-to-11-40-with-fpkg-builder-more.2571/) · [PS5-FPKG-Builder](https://github.com/Phoenixx1202/PS5-FPKG-Builder) · [PS-Classics-fPKG-Builder](https://github.com/SvenGDK/PS-Classics-fPKG-Builder)
- Jailbreak: [Relapse 13.60 (VideoCardz)](https://videocardz.com/newz/ps5-13-60-jailbreak-released-almost-every-ps5-can-now-be-jailbroken) · [Relapse e 14.00 (Kotaku)](https://kotaku.com/new-ps5-jailbreak-exploit-works-on-systems-running-july-2026-firmware-2000738283) · [UMTX](https://github.com/PS5Dev/PS5-UMTX-Jailbreak) · [lista de vulnerabilidades (psdevwiki)](https://www.psdevwiki.com/ps5/Vulnerabilities) · [guia (GBAtemp)](https://gbatemp.net/threads/ps5-exploit-guide.613891/)
- ShadowMount+: [README](https://github.com/drakmor/ShadowMountPlus/blob/main/README.md) (formatos `.ffpkg`, `.exfat`, `.ffpfs`, `.ffpfsc`)
- Instaladores e ferramentas: [ps5-ezremote-dpi](https://github.com/cy33hc/ps5-ezremote-dpi) · [pkg-sender](https://github.com/Loopayeh/pkg-sender) · [ps5upload](https://github.com/phantomptr/ps5upload) · [etahen-pkg-loader](https://github.com/trocla/etahen-pkg-loader) · [PS5 PKG Virtual Shop](https://github.com/MestreTM/ps5_pkg_virtual_shop)
