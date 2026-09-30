# Arquitetura

WPF (.NET 10), publicado como `.exe` único. Quatro arquivos de lógica, uma janela.

| Arquivo | Papel |
|---|---|
| `app/Engine.cs` | Fila: varre entradas, agrupa partes, decide quando começar, processa um jogo por vez, salva a fila |
| `app/Archives.cs` | 7-Zip: agrupar volumes, listar, testar senha, abrir o stream `7z x -so`, achar a pasta do jogo e o `dec` (ou a imagem `.exfat`) |
| `app/Ftp.cs` | FTP: estado remoto (`SIZE`), envio em streaming com conexões paralelas, verificação |
| `app/Job.cs` | Um item da fila (estado, progresso, velocidade, ETA) |
| `app/MainWindow.xaml(.cs)` | Interface |
| `app/Settings.cs` | Configurações em `%LOCALAPPDATA%\Ferry\settings.json` |

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
- Fila persistida em `%LOCALAPPDATA%\Ferry\queue.json` (arquivos adicionados e itens removidos).
