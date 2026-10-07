# Changelog

[English](CHANGELOG.md) · **Português (BR)**

Todas as mudanças relevantes do Ferry ficam aqui. O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto segue o [Versionamento Semântico](https://semver.org/lang/pt-BR/): `MAJOR.MINOR.PATCH`, com `-beta.N` enquanto o Ferry está em beta.

- **MAJOR**: mudança incompatível (formato de configurações, fila ou dados que versões antigas não leem).
- **MINOR**: recurso novo, compatível com o que já existe.
- **PATCH**: só correção de bug.
- A versão mora num lugar só, o [`Directory.Build.props`](Directory.Build.props). Cada versão é uma tag git `vX.Y.Z[-beta.N]`, e a seção dela aqui vira o texto da [Release no GitHub](../../releases). Como lançar uma: [Como lançar](#como-lançar).

## [Não lançado]

## [1.6.2-beta.1] - 2026-10-07

### Adicionado

- Pasta de jogo solta: arraste a pasta do jogo (sem arquivo compactado) e o Ferry envia direto do disco, retomando de onde parou.
- **Remover concluídos da fila automaticamente** (Configurações → Envio, desligado por padrão, também na interface web). O item concluído some alguns segundos depois. Fica pausado enquanto um desligamento do PC está armado, porque mexer na fila cancela o desligamento.
- Opções de energia da sessão no Windows: desligar o PC ao terminar as transferências e impedir a suspensão durante os envios, as duas só para a sessão atual.
- Pedido automático de instalação de PKG e porta do DPI nas Configurações (opcionais); preparação do PKG para o etaHEN.
- Logo novo (fita estilo PS5), animação na tela inicial e novo ícone do app.

### Alterado

- Windows: as opções de energia saíram do topo fixo de todas as telas e foram para **Configurações → Energia**. O topo só mostra uma faixa, com a contagem regressiva e o botão de cancelar, enquanto um desligamento está armado.

### Corrigido

- Quedas de conexão durante APPE não desativam mais a retomada nem reiniciam arquivos parciais do zero. Só uma resposta explícita de comando não suportado provoca reenvio inteiro; erros de permissão preservam o parcial.
- A retomada de compactados mostra o progresso da releitura e inclui imediatamente os bytes já salvos no PS5. Esses bytes não inflam mais a velocidade de envio.
- As conexões FTP encerram entre tentativas e só abrem quando há trabalho, evitando sessões ociosas durante a releitura do arquivo.
- Retomar um arquivo parcial de pasta solta não lê mais dezenas de GB do disco só para descartar antes de continuar. Antes parecia travado em 0 MB/s e sem nada no log; agora pula direto para a posição certa.

## [1.6.0-beta.1] - 2026-09-30

### Adicionado

- PKG e fPKG: envio de `.pkg` solto, ou de um `.pkg` dentro de ZIP/RAR/7z, por FTP para instalar manualmente no etaHEN.
- Pedido de instalação automática pelo etaHEN DPI (opcional). A fila registra o preparo e o envio do pedido; resultado incerto pede para conferir no PS5. A instalação real ainda precisa ser validada num console.

## [1.5.1-beta.1] - 2026-09-30

### Adicionado

- App Windows em Português (Brasil) e English. Automático segue o idioma de exibição do Windows; a escolha é compartilhada com a interface web pelo `settings.json`.
- Bandeja do sistema: minimizar esconde a janela enquanto envios, pasta monitorada e webhooks continuam. Duplo clique ou **Abrir o Ferry** restaura; pedido de senha restaura a janela antes do diálogo.

## [1.5.0-beta.3] - 2026-09-30

### Alterado

- Os testes de webhook controlam as respostas HTTP e usam o timeout de produção, então os contratos de webhook são validados com o comportamento real.

## [1.5.0-beta.2] - 2026-09-30

### Alterado

- A suíte E2E agora interrompe o container Docker antes da conclusão para validar que o envio retoma direito.

## [1.5.0-beta.1] - 2026-09-30

### Adicionado

- Webhooks configuráveis no app Windows e na web: Discord, ntfy ou JSON genérico, disparados em conclusão, erro e pedido de senha. Ativação opcional, URL mascarada e botão de teste. Configuração e contrato em [WEBHOOK.md](docs/WEBHOOK.md).

## [1.4.0-beta.1] - 2026-09-30

### Adicionado

- Interface web em Português (Brasil) e English, com Automático, Português ou English nas Configurações. Trocar o idioma mantém a fila, os envios e os diálogos de senha abertos.
- Todo texto visível foi para arquivos de recursos `pt-BR` e `en` compartilhados, incluindo log, avisos e erros.

## [1.3.0-beta.1] - 2026-09-30

### Adicionado

- **Ferry self-hosted**: o mesmo motor do app Windows, com interface web (fila ao vivo, configurações, log, senha do arquivo pedida no navegador, envio em pedaços arrastando arquivos para a página). Login com usuário e senha.
- Imagem Docker (amd64 e arm64) publicada no GHCR, e binários Linux autocontidos (x64 e arm64) em toda release.
- README e docs em inglês, português e espanhol.

### Alterado

- A lógica saiu do app WPF para a biblioteca portátil `Ferry.Core`, compartilhada pelo app Windows, pelo servidor web e pelos testes.
- A senha do jogo é guardada com DPAPI no Windows e AES-GCM no Linux; `FERRY_DATA` troca a pasta de dados.

## [1.2.0-beta.2] - 2026-09-30

### Corrigido

- Timeout do FTP: o ftpsrv do PS5 às vezes passa de 15 s para responder um `STOR` com 8 conexões gravando no disco externo. O Ferry agora espera 60 s e, em timeout, erro de socket ou de I/O, consulta o PS5 de novo e continua só com o que falta (até 3 vezes).

## [1.2.0-beta.1] - 2026-09-30

### Adicionado

- Marca Ferry e interface nova: fonte Geist, números tabulares, fila em régua com %, MB/s e tempo restante, barra de progresso "travessia" animada, cor por estado e Configurações em duas colunas.
- **Transferir agora**: passa um jogo na frente da fila; o envio atual volta para a fila e continua de onde parou.

### Alterado

- Renomeado de PS5 Sender para Ferry: `Ferry.exe`, namespace, CI, README e docs. Os dados ficam em `%LOCALAPPDATA%\Ferry`, migrados uma vez (copiados, não movidos) da antiga pasta `PS5Sender`.

### Corrigido

- Pausar durante a conferência remota não sobrescreve mais o estado do item.

## [1.1.0-beta.2] - 2026-09-30

### Adicionado

- Imagens `.exfat` do ShadowMount+, soltas ou dentro de `.zip`/`.rar`/`.7z`, enviadas para uma pasta de imagens configurável. Publicação atômica (`.ferry-part` renomeado só depois de tudo conferido).

## [1.1.0-beta.1] - 2026-09-30

### Adicionado

- Lista de senhas conhecidas nas Configurações, testadas em silêncio antes do diálogo de senha; a que funciona fica lembrada.
- A senha do jogo é salva criptografada na fila e esquecida quando o jogo sai dela.
- Capa e título lidos de dentro do arquivo (`param.sfo`, `icon0.png`), com o `PPSAxxxxx`.
- "Aguardando partes" mostra quais partes faltam.
- Achar o PS5 na rede quando o IP salvo para de responder (portas 2121/1337); só oferece, nunca troca sozinho.
- Toast do Windows para concluído, erro e senha necessária, só quando a janela está sem foco.
- As configurações salvam sozinhas a cada mudança, com gravação atômica; valor inválido nunca é salvo.
- `log.txt` persistente com cada comando `STOR`/`APPE` e `SIZE` e a resposta.

### Corrigido

- `APPE` só continua um parcial que o próprio Ferry começou; arquivo de outro tamanho já no console é reenviado inteiro.
- O tamanho é conferido depois de cada `STOR`/`APPE`, e a falha mostra o caminho remoto e "esperado X, no PS5 Y".
- O ftpsrv novo liga o `SELF` por padrão, e isso fazia o `SIZE` de arquivos SELF mentir; o Ferry agora desliga.
- O ShadowMount+ não instala mais o jogo no meio do envio: `param.json`/`param.sfo` sobem por último.

## [1.0.0-beta.1] - 2026-09-30

### Adicionado

- Primeira versão, como PS5 Sender: extrai arquivos de jogo e envia ao PS5 por FTP, em streaming, sem gravar nada no disco.
- Fila que agrupa volumes (`.001`, `.z01`, `.partN.rar`, `.r00`), espera as partes que faltam e o tamanho do arquivo estabilizar.
- Conexões paralelas para arquivos pequenos; retomada por `SIZE` (pula o que já está no PS5) e `APPE` quando suportado; fila persistente.
- Pasta `dec` sobrepõe o jogo; diálogo de senha; verificação de tamanho antes de apagar os originais.
- Funciona com o conjunto mínimo de comandos do ftpsrv (sem `NLST`/`FEAT`/`EPSV`).
- Interface WPF escura e suíte E2E contra um servidor que imita o ftpsrv.
- CI com build, E2E e release automática por tag no GitHub Actions.

## Como lançar

1. Enquanto trabalha, anote cada mudança em **Não lançado** nos dois changelogs (este e o em inglês).
2. Para lançar, escolha o próximo número pelas regras do topo e rode `tools/release.ps1 -Version 1.7.0-beta.1`. Ele transforma **Não lançado** nessa versão com a data de hoje, atualiza o `Directory.Build.props` e os links, faz o commit e cria a tag `v1.7.0-beta.1`.
3. `git push --follow-tags`. A CI compila, testa e publica a release com as notas deste arquivo e do [CHANGELOG.md](CHANGELOG.md) (inglês primeiro, depois português). A CI falha se a tag não bater com o `Directory.Build.props` ou se algum dos changelogs não tiver a seção da versão.

[Não lançado]: https://github.com/bps2414/ferry/compare/v1.6.2-beta.1...HEAD
[1.6.2-beta.1]: https://github.com/bps2414/ferry/compare/v1.6.0-beta.1...v1.6.2-beta.1
[1.6.0-beta.1]: https://github.com/bps2414/ferry/compare/v1.5.1-beta.1...v1.6.0-beta.1
[1.5.1-beta.1]: https://github.com/bps2414/ferry/compare/v1.5.0-beta.3...v1.5.1-beta.1
[1.5.0-beta.3]: https://github.com/bps2414/ferry/compare/v1.5.0-beta.2...v1.5.0-beta.3
[1.5.0-beta.2]: https://github.com/bps2414/ferry/compare/v1.5.0-beta.1...v1.5.0-beta.2
[1.5.0-beta.1]: https://github.com/bps2414/ferry/compare/v1.4.0-beta.1...v1.5.0-beta.1
[1.4.0-beta.1]: https://github.com/bps2414/ferry/compare/v1.3.0-beta.1...v1.4.0-beta.1
[1.3.0-beta.1]: https://github.com/bps2414/ferry/compare/v1.2.0-beta.2...v1.3.0-beta.1
[1.2.0-beta.2]: https://github.com/bps2414/ferry/compare/v1.2.0-beta.1...v1.2.0-beta.2
[1.2.0-beta.1]: https://github.com/bps2414/ferry/compare/v1.1.0-beta.2...v1.2.0-beta.1
[1.1.0-beta.2]: https://github.com/bps2414/ferry/compare/v1.1.0-beta.1...v1.1.0-beta.2
[1.1.0-beta.1]: https://github.com/bps2414/ferry/compare/v1.0.0-beta.1...v1.1.0-beta.1
[1.0.0-beta.1]: https://github.com/bps2414/ferry/releases/tag/v1.0.0-beta.1
