# Arquitetura

[English](en/ARCHITECTURE.md) · **Português (BR)** · [Español](es/ARQUITECTURA.md)

.NET 10. A lógica fica em `core/` (sem interface) e tem duas caras: o app Windows (WPF, `.exe` único) e o servidor web self-hosted (Docker ou binário Linux).

| Arquivo | Papel |
|---|---|
| `core/Engine.cs` | Fila: varre entradas, agrupa partes, decide quando começar, processa um jogo por vez, salva a fila |
| `core/Archives.cs` | 7-Zip: agrupar volumes, listar, testar senha, abrir o stream `7z x -so`, achar a pasta do jogo e o `dec` (ou a imagem `.exfat`) |
| `core/Ftp.cs` | FTP: estado remoto (`SIZE`), envio em streaming com conexões paralelas, verificação |
| `core/Job.cs` | Um item da fila (estado, progresso, velocidade, ETA) |
| `core/Settings.cs` | Configurações em `settings.json` na pasta de dados (`%LOCALAPPDATA%\Ferry`, ou `FERRY_DATA`) |
| `core/Webhooks.cs` | Avisos HTTP compartilhados: fila limitada em memória, snapshots imutáveis, localização e teste; integrado aos callbacks Done/askPassword sem alterar o FTP |
| `core/Secret.cs` | Senha do jogo guardada na fila: DPAPI no Windows, AES-GCM com `secret.key` (600) fora dele |
| `core/Discovery.cs` | Busca do PS5 na rede (/24 de cada interface, portas 2121 e 1337) |
| `app/MainWindow.xaml(.cs)` | Interface Windows; `App.xaml.cs` extrai o `7z.exe` embutido e passa o caminho ao core |
| `web/Program.cs` | Servidor web: API, cookie de login, configurações campo a campo, SSE, interface embutida |
| `web/Hub.cs` | Ponte Engine ↔ navegador: fotografia da fila, log, avisos, senha pedida ao navegador |
| `web/Auth.cs` | Um usuário, criado na primeira abertura (`auth.json`, PBKDF2-SHA512); limite de tentativas |
| `web/Uploads.cs` | Upload em blocos com retomada |
| `web/ui/` | Interface web (HTML/CSS/JS puro), embutida no binário |

## Self-hosted (web)

- **7-Zip**: o core procura `7zz`/`7z` ao lado do programa e no PATH. Na imagem Docker é o `7zip` + `7zip-rar` do Ubuntu; no `.tar.gz` Linux vai o `7zzs` oficial (estático) ao lado do `ferry`.
- **Progresso ao vivo**: `GET /api/events` (SSE). A cada 250 ms o servidor monta a fotografia da fila (`Hub.Snapshot`) e só manda se mudou; manda também as linhas novas do log e os avisos. Comandos (pausar, retomar, transferir agora…) vão por `POST`.
- **Senha do arquivo**: o `askPassword` da Engine vira um pedido pendente (`TaskCompletionSource`) que aparece na fotografia; o navegador abre o diálogo e responde em `POST /api/jobs/{id}/password`. Pausar, cancelar ou remover o jogo encerra o pedido.
- **Login**: cookie `HttpOnly`/`SameSite=Strict`; as chaves do cookie ficam em `/data/keys`, então o login sobrevive a reiniciar o container. Toda a API (fora `/api/auth/*`) exige login.
- **Upload**: `POST /api/uploads` (nome, tamanho, data) devolve um id estável e o byte onde continuar; `PUT /api/uploads/{id}?offset=N` acrescenta um bloco (offset diferente do servidor → `409` com o offset certo). O parcial fica em `<pasta>/.ferry-upload/` — a Engine só olha a raiz da pasta — e só ganha o nome final inteiro, então um `.exfat` pela metade nunca entra na fila.
- **Reiniciar**: o que foi enviado e conferido fica em `queue.json` (`Done`); ao reabrir, o jogo com as partes ainda na pasta volta como "Concluído" em vez de entrar na fila de novo. Adicionar os arquivos de novo (arrastar, seletor, upload) tira essa marca.

## Fluxo de um jogo

```
arquivos na pasta / seletor / arrastar
        │
        ▼
Agrupar volumes por nome-base ──► faltam partes? ── "Aguardando partes"
        │ nomes completos
        ▼
Tamanho estável por N s? ──► 7z l (confere volumes) ──► "Na fila"
        │
        ▼
7z l -slt  → lista de itens (caminho, tamanho, criptografado)
senha?     → 7z t no 1º item criptografado (barato) → diálogo até acertar
Plan()     → destino de cada item: pasta do jogo, dec por cima, cascas descartadas
        │
        ▼
SIZE de cada arquivo no PS5 → lista "need" (só o que falta)
        │
        ▼
7z x -so @lista  ──stdout──► leitor único
                                ├─ arquivo ≤ 4 MB → RAM → canal → N-1 conexões paralelas
                                └─ arquivo > 4 MB → stream direto na conexão principal
        │
        ▼
SIZE de novo → confere tudo → "Concluído" → (opcional) apaga as partes originais
```

## Por que streaming

Extrair para o disco e depois enviar exige espaço livre igual ao jogo extraído (dezenas de GB). Com `7z x -so`, o 7-Zip escreve todos os arquivos concatenados no stdout, **na ordem da listagem** (`7z l`). Como os tamanhos vêm da listagem, o app corta o stream em arquivos e manda cada um direto para um socket FTP. Nada extraído toca o disco, e o pipe dá contrapressão natural: o 7-Zip só avança quando o FTP consome.

Com `@lista` (arquivo com os caminhos, `-scsUTF-8 -spd`), o 7-Zip extrai **só** os itens que ainda faltam no PS5 — é isso que permite pausar, fechar e reabrir sem reenviar nada completo.

## Agrupamento de volumes

Regex por nome de arquivo (`Archives.Group`):

| Padrão | Chave | Arquivo que o 7-Zip abre | 1º índice |
|---|---|---|---|
| `X.(zip|7z|rar).NNN` | `X.ext` | `.001` | 1 |
| `X.partN.rar` | `X.rar` | `part1` | 1 |
| `X.zNN` + `X.zip` | `X.zip` | `X.zip` | 1 |
| `X.rNN` + `X.rar` | `X.rar` | `X.rar` | 0 |

“Completo pelo nome” = índices contíguos (e o arquivo principal presente). Mesmo assim o volume final pode faltar sem buraco na numeração (ex.: `.001`–`.004` de 5), então o app ainda roda `7z l` e só enfileira se não houver “Missing volume / Unexpected end”. Depois do `7z l`, reconfere a assinatura (nome+tamanho+data) das partes: se um volume chegou durante o `7z l`, recomeça a janela de estabilidade.

## Pasta do jogo e `dec`

`FindGameRoot`: a pasta mais rasa com `EBOOT.BIN` ou `sce_sys/param.sfo`, ignorando pastas chamadas `dec` (que também têm `EBOOT.BIN`). `Plan`: se existir `dec/` ao lado da pasta do jogo (ou `dec/<pasta do jogo>/`), cada arquivo do `dec` vira destino e o arquivo do jogo com o mesmo caminho é descartado.

## Imagem `.exfat` (ShadowMount+)

- `.exfat` solto: `Archives.Group` o trata como arquivo único (`X.exfat`). A `Engine` pula o `7z l` e monta uma lista de um item só. O envio lê um `FileStream` do disco em vez do stdout do 7z. Na retomada com `APPE`, o trecho já enviado é pulado com `Seek`, sem ler.
- Dentro de um compactado: se `Plan` não acha pasta de jogo, `Archives.ImagePlan` escolhe os itens `.exfat` (pelo nome do arquivo, sem as pastas de dentro) e descarta o resto. Daí em diante é o mesmo stream `7z x -so`.
- Destino `Settings.ImageDir`. A imagem entra em `Engine.Held`, então sobe com `.ferry-part` e é renomeada no fim, como o `param.json`.

## Interface

- Progresso vem de muitas threads a cada poucos KB; `Job.Report` limita a 4 atualizações/s (senão o dispatcher do WPF afoga e a tela fica parada).
- `BindingOperations.EnableCollectionSynchronization` deixa a fila ser alterada de threads de fundo.
- Visual (Fase 2, "Ferry"): tokens e estilos em `App.xaml`; fonte Geist embutida (`app/fonts`, `pack://application:,,,/Ferry;component/fonts/#Geist`), números tabulares na janela toda. A barra de progresso é a "travessia" do logo (cais nas pontas, seta na ponta do progresso); `OnProgress` anima o valor até o novo em 350 ms, e a cor muda por estado com `ColorAnimation` nos `DataTrigger`. O ícone (`Ferry.ico`) usa a mesma geometria 16×16 de `LogoPosts`/`LogoArrow`.
- **Transferir agora** (`Engine.SendNow`): move o jogo para o topo e o que estava enviando para logo atrás, este volta a `NaFila` e tem o `Cts` cancelado. O `RunAsync` pega o primeiro `NaFila` da lista; o interrompido depois só envia o que falta (mesma retomada da pausa).
- Fila persistida em `queue.json` na pasta de dados (arquivos adicionados, itens removidos, senhas cifradas, envios começados e jogos concluídos).
