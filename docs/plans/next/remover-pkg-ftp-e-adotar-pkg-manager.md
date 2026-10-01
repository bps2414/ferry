# PKG: retirar o fluxo FTP + DPI por caminho e adotar PKG Manager

Data: **2026-10-01**. Estado: **descoberta registrada e proposta para implementação; código ainda não alterado**. Checkout pesquisado: `046c9bd848978a7675d8b8408ca0265269172e0a`.

## Descoberta e decisão de escopo

O usuário reportou que o método atual de enviar PKG para o etaHEN é inútil no seu cenário e pediu para registrar a descoberta, planejar a remoção e pesquisar USB/streaming com [ps5-pkg-manager](https://github.com/itsPLK/ps5-pkg-manager). Isso substitui a premissa de produto da Fase 6: copiar um PKG para a memória interna do console não entrega, por si só, uma instalação utilizável.

**Planejar a retirada do upload de PKG por FTP e da solicitação DPI v1 com caminho local.** Manter o envio FTP de dumps e imagens ShadowMount+. A instalação de PKG passa a ter como proposta USB ou um instalador externo que leia o pacote pela rede.

O relato é evidência do problema do usuário. Não houve reprodução em PS5 nesta pesquisa; não se declara que todos os firmwares/builds do etaHEN rejeitam caminhos internos. O [código do DPI v1](https://github.com/etaHEN/etaHEN/blob/dafa13b562ddb137a4b4a97b9aaa287c0c57cc9c/Source%20Code/util/source/DirectPKGInstaller.cpp) encaminha `url` ao instalador do sistema, e o [README do etaHEN](https://github.com/etaHEN/etaHEN) mantém serviços DPI v1/v2. Essas fontes não comprovam o funcionamento do caminho local no console do usuário.

## O fluxo confirmado do PKG Manager

Fonte de implementação: **v1.4.1**, commit `a35943f80e0664e4e7d9b4c410d8ada6a57da9e2`, consultado em 01/10/2026. Era a release mais recente na consulta; verificar novamente antes da implementação.

| Caminho | Como usar | Onde fica a origem |
|---|---|---|
| Direct Install | Carregar PKG Manager no PS5; abrir `http://IP-DO-PS5:8844` no navegador do PC; escolher Direct Install e selecionar/arrastar um `.pkg` | PC; o navegador fornece os bytes durante a instalação |
| SMB/Samba | Compartilhar uma pasta de PKGs no PC/NAS e cadastrá-la em Settings → Samba do PKG Manager; usar Rescan após adicionar arquivos | PC/NAS; o console lê a pasta pela rede |
| USB | Colocar PKG na raiz do USB ou em `/pkg/`; o PKG Manager também percorre subpastas dentro de `/pkg/` | USB conectado ao PS5 |

Esses fluxos e a carga via Payload Manager ou ELF/elfldr estão no [README oficial](https://github.com/itsPLK/ps5-pkg-manager/blob/a35943f80e0664e4e7d9b4c410d8ada6a57da9e2/README.md). O gerenciador roda **no PS5**; o PC abre a interface hospedada pelo console. Não é necessário instalar o gerenciador como aplicativo Windows.

Direct Install usa uma sessão em RAM e evita armazenar o PKG completo temporariamente no SSD do PS5. Ainda é necessário espaço para o conteúdo instalado. USB e SMB também alimentam o instalador sem copiar primeiro o pacote inteiro para o armazenamento interno. A [arquitetura](https://github.com/itsPLK/ps5-pkg-manager/blob/a35943f80e0664e4e7d9b4c410d8ada6a57da9e2/ARCHITECTURE.md) documenta esse modelo.

Para Direct Install, manter PC e aba abertos até o resultado terminal; não desligar após um simples contador de bytes enviados. O [cliente oficial](https://github.com/itsPLK/ps5-pkg-manager/blob/a35943f80e0664e4e7d9b4c410d8ada6a57da9e2/frontend/src/hooks/useDirectUpload.js) continua atendendo pedidos de segmentos durante a instalação. Isso é diferente de iniciar a instalação de um pacote disponível continuamente por SMB/USB.

A v1.3.0 corrigiu instalações consecutivas que falhavam em firmware 9.60 e posteriores. A v1.4.1 corrigiu o caminho USB/disco sem rede; nesse modo o andamento deve ser acompanhado nas notificações do PS5, sem progresso/cancelamento no gerenciador. Ver [releases](https://github.com/itsPLK/ps5-pkg-manager/releases). Nenhuma dessas notas prova compatibilidade universal com firmware, assinatura ou tipo de pacote.

Há uma [solicitação aberta para instalar da memória interna](https://github.com/itsPLK/ps5-pkg-manager/issues/35). Ela não é prova sobre o etaHEN, mas reforça que não devemos pressupor que o PKG Manager aproveitará automaticamente os PKGs já enviados pelo Ferry para `/data/etaHEN/pkgs`.

## Proposta recomendada

**Direção principal após a conversa: integrar o Ferry ao PKG Manager instalado/carregado pelo usuário no PS5.** O Ferry mantém seleção, fila, streaming e acompanhamento; o gerenciador fornece a instalação no console. USB e acesso direto à página do gerenciador ficam como alternativas. A instalação/carga desse componente é um pré-requisito explicado ao usuário, sem criar um instalador ou payload próprio do Ferry.

Fluxo de produto proposto:

1. Configurações mostram **PKG Manager**, IP do PS5 e porta web 8844. Se o serviço não responder como esperado, indicar que o usuário precisa instalar/carregar o PKG Manager no console, com link para o projeto e **Abrir interface do gerenciador**. O teste verifica respostas conhecidas do serviço, sem instalar pacote.
2. Usuário adiciona um `.pkg` solto ao Ferry. A UI informa que a ação será **enviar e instalar por streaming**, sem cópia integral temporária do PKG no PS5. O comando explícito inicia a operação; não reaproveitar a preferência antiga de instalação automática.
3. O Ferry cria a sessão, entrega o cabeçalho, inicia a instalação pelo contrato do gerenciador e continua fornecendo os segmentos solicitados. Mostra separadamente envio e andamento da instalação, em vez de concluir o card ao terminar de enviar bytes.
4. O PC/servidor permanece disponível enquanto o console precisar da origem. Conclusão, webhook e eventual desligamento dependem do resultado terminal confirmado. Se esse resultado não puder ser confirmado, mostrar **Verifique no PS5** e impedir nova instalação automática.

As Etapas 1 e 2 abaixo compõem a primeira entrega de integração pretendida. São separadas para implementação e validação; oferecer somente o link externo não equivale a entregar a integração. Ainda se trata de proposta, sem alteração de código autorizada nesta rodada.

### Etapa 1 — retirar o caminho antigo e preparar a integração

Preparação independente do cliente de streaming:

1. Desativar o processamento de novos PKGs por FTP e a emissão de pedidos DPI v1 por caminho local. Um `.pkg` solto ou detectado em compactado deve receber uma explicação estável, sem entrar em loop de tentativa automática.
2. Remover da UI destino PKG interno, porta/teste DPI, instalação automática e botão de solicitar instalação pelo método antigo. Nas duas interfaces, oferecer **Abrir PKG Manager** com IP/porta próprios do serviço (8844 por padrão) e instruções USB/SMB/Direct Install. Abrir a página não inicia nem comprova instalação.
3. Configurações antigas continuam legíveis, mas `AutoInstallPackages=true` não pode reativar o fluxo removido. Não reaproveitar `DpiPort` como porta PKG Manager: os protocolos são diferentes.
4. Preservar ledger/fila antigos como histórico, inclusive caminho remoto e resultados `submitting`/`unknown`. Desabilitar ações de envio/instalação desses registros; não convertê-los em sucesso e não repetir efeitos remotos ao reabrir.
5. Não apagar fontes, compactados, PKGs/parciais remotos ou credenciais como efeito da migração. Qualquer limpeza é outra tarefa explícita. Não garantir importação dos pacotes internos pelo novo gerenciador.
6. Atualizar README, documentação, textos pt-BR/en e testes do contrato alterado. Preservar a identificação `.pkg` para orientar o usuário; não tratar um pacote como dump ou imagem por remover seu transporte.

O Ferry continua útil para dumps e imagens; enquanto a integração não estiver pronta, o caminho externo fica explícito. Um PKG dentro de RAR/ZIP/7z precisa ser extraído antes de selecioná-lo no Direct Install ou disponibilizá-lo por USB/SMB.

### Etapa 2 — integrar streaming de `.pkg` solto ao Ferry

Proposta após testar o gerenciador real no console do usuário. O PKG Manager permanece um pré-requisito carregado pelo usuário; o Ferry não compila, distribui nem injeta payload nesta etapa.

Criar um cliente compartilhado no core usado pelo Windows e pelo servidor web. Fazer o transporte pelo backend .NET, inclusive na web; não depender de uma página HTTPS do Ferry conseguir abrir HTTP/WebSocket diretamente no PS5. No self-hosted, quem precisa alcançar o console e ler a origem é o servidor onde o Ferry roda. O upload atual do navegador para `/games` é uma transferência anterior e separada.

O protocolo observado tem:

- REST na porta **8844**, com inicialização/consulta/cancelamento de sessão em `/api/upload/*`. A sessão devolve `session_id`, `offset` e `ws_port`; não fixar a porta retornada no cliente. Fonte: [REST do Direct Install](https://github.com/itsPLK/ps5-pkg-manager/blob/a35943f80e0664e4e7d9b4c410d8ada6a57da9e2/frontend/src/api/directInstall.js) e [servidor](https://github.com/itsPLK/ps5-pkg-manager/blob/a35943f80e0664e4e7d9b4c410d8ada6a57da9e2/src/http_server.c).
- WebSocket normalmente na **18842**, `/ws/upload`; segmentos endereçados de 1 MiB, `ack`, `busy` e pedidos `seek`. Uma sessão tem um dono; outra sessão concorrente deve falhar de forma clara. Fonte: [contrato do upload](https://github.com/itsPLK/ps5-pkg-manager/blob/a35943f80e0664e4e7d9b4c410d8ada6a57da9e2/include/ws_upload.h).
- O instalador recebe HTTP Range interno no PS5, normalmente em **18841**. Essa porta não é o endpoint de upload do PC. As operações de iniciar instalação e consultar seu resultado precisam seguir o fluxo `live:<session_id>` do cliente oficial. Fontes: [arquitetura](https://github.com/itsPLK/ps5-pkg-manager/blob/a35943f80e0664e4e7d9b4c410d8ada6a57da9e2/ARCHITECTURE.md) e [cliente oficial](https://github.com/itsPLK/ps5-pkg-manager/blob/a35943f80e0664e4e7d9b4c410d8ada6a57da9e2/frontend/src/hooks/useDirectUpload.js).

Implementar a partir do contrato observado, com leitores de arquivo que suportem offsets, buffers limitados, confirmação do usuário para iniciar instalação e identificação da origem. Respeitar a janela de upload anunciada, backpressure e pedidos de segmentos, inclusive trechos já enviados e posteriormente descartados da RAM. Não copiar código GPL-3.0 do gerenciador para o Ferry MIT; eventual incorporação de código exige análise separada de licença.

Antes de efeito remoto, registrar intenção/sessão de forma durável. Distinguir origem disponível, streaming em andamento, instalação iniciada, instalação concluída, falha e resultado desconhecido. Não emitir webhook de instalação concluída nem liberar desligamento por `ack` ou fim do upload. Queda de processo/conexão exige reconciliação de sessão e estado; não prometer a retomada durável do FTP para uma sessão em RAM. Confirmar operação/capacidade do serviço por respostas conhecidas; porta aberta é evidência somente de conectividade.

### Etapa 3 — decidir o tratamento de compactados

O Direct Install não aceita RAR/ZIP/7z como substituto do PKG. O protocolo pede segmentos arbitrários do pacote; `7z x -so` entrega um fluxo sequencial. Uma memória circular limitada não garante responder a um `seek` antigo ou distante. Os volumes `PS5MPKG1` do gerenciador são fatias sem compressão com cabeçalho próprio, não os volumes RAR/7z aceitos pelo Ferry. Fontes: [motor de segmentos](https://github.com/itsPLK/ps5-pkg-manager/blob/a35943f80e0664e4e7d9b4c410d8ada6a57da9e2/frontend/src/utils/segmentSender.js), [stream em RAM](https://github.com/itsPLK/ps5-pkg-manager/blob/a35943f80e0664e4e7d9b4c410d8ada6a57da9e2/include/ws_stream.h) e [multipart](https://github.com/itsPLK/ps5-pkg-manager/blob/a35943f80e0664e4e7d9b4c410d8ada6a57da9e2/include/multipart.h).

| Opção futura | Custo e limite | Recomendação |
|---|---|---|
| Extrair PKG completo no PC/servidor e depois streamar | Precisa de espaço para o PKG; validar CRC/saída antes de instalar; muda a promessa de não extrair no disco | Mais simples para suporte geral, somente com essa mudança de produto explícita |
| Extrair diretamente para USB | Evita staging no SSD interno do PC; requer USB com espaço e filesystem adequado, depois instalação no console | Alternativa de exportação, sem integração de instalação nesta fase |
| Streaming direto de compactado com cache/reextração | Acesso aleatório, solid/encriptação, CRC tardio e pedidos simultâneos exigem prova técnica própria | Fora da primeira integração; não prometer suporte |

Começar a Etapa 2 por **PKG solto**, preservando a regra atual de não extrair dados completos silenciosamente no disco. Caso se queira compactados depois, escolher conscientemente uma das opções; não reutilizar FTP para a memória interna como fallback oculto.

## Mapa da remoção futura

| Área | Arquivos/responsabilidades |
|---|---|
| Transporte e ledger | `core/PkgInstaller.cs`, `core/Engine.Packages.cs`, integração em `core/Engine.cs`; retirar execução FTP/DPI mantendo leitura compatível do histórico |
| Classificação e estado | `core/Archives.cs`, `core/PackageHeader.cs`, `core/Job.cs`; manter distinção PKG/imagem e ações coerentes |
| Configuração | `core/Settings.cs`; ler campos legados sem efeito, novo endpoint independente apenas se necessário |
| API/web | `web/Program.cs`, `web/Hub.cs`, `web/ui/index.html`, `web/ui/app.js`; retirar rotas/controles antigos, preservar auth, uploads e fila |
| Windows/energia | `app/MainWindow.xaml`, `app/MainWindow.xaml.cs`, `app/TransferPowerSession.cs`, `core/Engine.WorkObservation.cs`; histórico incerto continua sem justificar desligamento |
| Textos e contratos | Catálogos core/app/web/UI pt-BR/en; `e2e/PkgChecks.cs`, `e2e/LocalizationChecks.cs`, `e2e/windows`, `e2e/web/web.mjs`, README e docs nos três idiomas |

O [plano de diagnóstico sem payload](proxima-fase-sem-payload.md) deve ser revisado antes de execução: retirar a premissa de pasta PKG interna e DPI como próximos serviços do produto. Diagnóstico FTP de dump/imagem continua útil. Usar um gerenciador externo não significa criar um payload próprio do Ferry.

## Validação exigida na implementação

Na Etapa 1, testar primeiro a ausência de efeitos do caminho removido: zero upload FTP de PKG e zero JSON DPI, incluindo monitoramento, retomada, settings antigas e ledger `prepared`/`submitting`/`submitted`/`unknown`. Originais e histórico preservados; dumps/imagens continuam com hashes, publicação e retomada. Atualizar os testes da Fase 6 para o novo contrato, preservando garantias de integridade e resultado incerto.

Na Etapa 2, receptor falso deve pedir segmentos fora de ordem, repetir trechos descartados, responder `busy`, recusar segunda sessão e interromper conexão. Comparar bytes/hashes; distinguir upload e instalação; testar origem alterada, reinício, cancelamento e falha de persistência antes/depois do envio. Não criar seletor fictício: documentar o comando real do harness quando existir.

Comandos já existentes, **a executar conforme a etapa implementada**:

```powershell
dotnet run --project e2e -- --pkg
dotnet run --project e2e -- --localization
dotnet run --project e2e -- G3 G7
dotnet run --project e2e/windows -c Release
dotnet build web -c Release
```

Executar o E2E web pt-BR/en pelo modo Docker ou DLL local descrito em [TESTES.md](../../TESTES.md), exercitando no navegador o caminho alterado. A rodada focada G3/G7 cobre dumps; complementar os casos existentes de imagens para validar o transporte preservado. Checks da CI aplicáveis continuam obrigatórios.

Aceite de instalação exige **PS5 real**: anotar firmware/build do etaHEN, versão do gerenciador, método de carga e pacote de teste conhecido; exercitar Direct Install, instalação consecutiva, update/DLC correspondentes e interrupção de rede. Uso real, fixtures, build, Docker e CI são evidências separadas. O firmware e a build do console do usuário não foram confirmados nesta pesquisa; uma instalação bem-sucedida não comprova todas as combinações.

## Entrega desta pesquisa

Somente documentação e memória: nenhum código, configuração de runtime, payload, teste de contrato, release ou pacote no console foi alterado. Foram lidos o código local, fontes oficiais, releases e issues ao vivo. A descoberta do usuário foi registrada com seu limite de evidência. Validação documental registrada após conferir diff e links locais; E2E/build e instalação no PS5 não fazem parte desta entrega de planejamento.

Validação em 01/10/2026: `git diff --check -- docs/PKG-PS5.md docs/en/PKG-PS5.md docs/es/PKG-PS5.md docs/ROADMAP.md docs/plans/next/proxima-fase-sem-payload.md` saiu com **0**, usando a configuração normal do repositório. Verificador Python (`python -`, leitura UTF-8 e destinos de links Markdown nos seis documentos desta entrega) passou: **6 documentos, 31 links locais existentes**, sem espaços finais no novo plano. Uma tentativa com `core.autocrlf=false` classificou CRLF como whitespace; o comando foi corrigido para usar a configuração do checkout, sem alterar arquivos ou afrouxar as verificações. Os avisos LF/CRLF restantes do Git são de conversão de fim de linha.
