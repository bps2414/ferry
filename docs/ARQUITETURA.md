# Arquitetura

WPF (.NET 10), publicado como `.exe` único. Quatro arquivos de lógica, uma janela.

| Arquivo | Papel |
|---|---|
| `app/Engine.cs` | Fila: varre entradas, agrupa partes, decide quando começar, processa um jogo por vez, salva a fila |
| `app/Archives.cs` | 7-Zip: agrupar volumes, listar, testar senha, abrir o stream `7z x -so`, achar a pasta do jogo e o `dec` |
| `app/Ftp.cs` | FTP: estado remoto (`SIZE`), envio em streaming com conexões paralelas, verificação |
| `app/Job.cs` | Um item da fila (estado, progresso, velocidade, ETA) |
| `app/MainWindow.xaml(.cs)` | Interface |
| `app/Settings.cs` | Configurações em `%LOCALAPPDATA%\PS5Sender\settings.json` |

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

## Interface

- Progresso vem de muitas threads a cada poucos KB; `Job.Report` limita a 4 atualizações/s (senão o dispatcher do WPF afoga e a tela fica parada).
- `BindingOperations.EnableCollectionSynchronization` deixa a fila ser alterada de threads de fundo.
- Fila persistida em `%LOCALAPPDATA%\PS5Sender\queue.json` (arquivos adicionados e itens removidos).
