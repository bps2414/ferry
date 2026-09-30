# Cenário PS5 e oportunidades para o Ferry — setembro de 2026

Pesquisa ao vivo em **2026-09-30**, corte inclusivo nessa data. Base local: `ec0ebbb`, worktree Farol. Esta entrega é pesquisa e planejamento; não altera o produto nem executa console, instalador, FTP ou energia reais.

## Resultado

Recomendo uma única próxima fase: **diagnóstico de serviços e destinos antes do envio**, usando o FTP existente e distinguindo teste TCP de comprovação de protocolo. O Ferry já cobre streaming de compactados completos, imagens e PKG preparado por FTP; seu teste DPI atual apenas conecta a uma porta. Ferramentas comparadas oferecem orientação e estados mais ricos, mas vários desses recursos dependem de receptores próprios. O plano recomendado está em [próxima fase sem payload](plans/next/proxima-fase-sem-payload.md).

RAR progressivo tem fundamento no formato e nos callbacks do UnRAR, mas **a viabilidade integrada ao Ferry ainda não foi demonstrada**. Deve sair da posição de próxima entrega e passar por uma prova técnica futura, sem substituir o fluxo atual.

## Método e confiança

- Fontes primárias: repositórios dos mantenedores, READMEs, código, releases/API GitHub, FileZilla oficial e RARLAB. Nenhuma notícia, agregador ou mirror é usado para confirmar compatibilidade.
- Para GitHub, consultei metadados e `/releases?per_page=100`; selecionei releases com `published_at` até 2026-09-30. Consultei também `/commits?until=2026-09-30T23:59:59Z&per_page=1` e li os READMEs nesses SHAs. Datas abaixo são UTC, não a data do crawl do buscador. O corte não prevê publicações posteriores à consulta, mesmo no mesmo dia.
- Release publicada, tag com nome beta e flag `prerelease` são coisas diferentes. “Mais recente” abaixo significa a release publicada mais recente encontrada dentro do corte. Não significa estável, certificada ou testada por nós.
- Código de branch pode diferir do binário da release; não trate recursos do README como contrato daquele binário sem conferir a tag. Ausência de API ou recurso nesta pesquisa significa “não confirmado”, não uma prova de inexistência.
- Não medi throughput, uso de CPU, instalação ou compatibilidade em hardware. Números e resultados dos autores continuam sendo alegações/testes deles.

## Existência, releases e manutenção

Todos os seis repositórios iniciais existem e retornaram releases pela API oficial. Nenhum estava marcado como arquivado na consulta. Atividade recente é sinal de manutenção, não garantia de suporte.

| Projeto | Release encontrada / publicação UTC | Último commit até o corte | Natureza e limite |
|---|---|---|---|
| [etaHEN upstream](https://github.com/etaHEN/etaHEN) | [2.5B](https://github.com/etaHEN/etaHEN/releases/tag/2.5B), 2025-12-25 | 2026-05-04, `dafa13b562dd` | Projeto de origem do etaHEN; não transferir a ele compatibilidade anunciada por ports independentes. |
| [ftpsrv](https://github.com/ps5-payload-dev/ftpsrv) | [v0.21.1](https://github.com/ps5-payload-dev/ftpsrv/releases/tag/v0.21.1), 2026-08-20 | 2026-09-30, `23737c4738d1` | Servidor FTP de ps5-payload-dev; SDK/loader e jailbreak são dependências separadas. |
| [ShadowMountPlus](https://github.com/drakmor/ShadowMountPlus) | [1.7beta2](https://github.com/drakmor/ShadowMountPlus/releases/tag/1.7beta2), 2026-09-21 | 2026-09-23, `cd0e11ceb778` | API GitHub: fork de `RDX-Sci01/ShadowMount1.3GBT`. Release tem nome beta, mas `prerelease=false`; não chamá-la de estável por isso. |
| [ps5upload](https://github.com/phantomptr/ps5upload) | [v5.40.0](https://github.com/phantomptr/ps5upload/releases/tag/v5.40.0), 2026-09-30 | 2026-09-30, `3de53d2fd92c` | Cliente + engine + receptor próprios; releases frequentes, acoplamento de versões e superfície maior. |
| [pkg-sender](https://github.com/Loopayeh/pkg-sender) | [v1.2.8](https://github.com/Loopayeh/pkg-sender/releases/tag/v1.2.8), 2026-09-25 | 2026-09-27, `04b5b2183bad` | Projeto independente; API de seu receptor PS5 é declarada instável. |
| [PSS / PlayStation Studio](https://github.com/dirazi83/PSS) | [v1.0.8](https://github.com/dirazi83/PSS/releases/tag/v1.0.8), 2026-08-23 | 2026-08-23, `f59100157956` | Aplicativo independente; distinguir instalador PS4 dos recursos PS5. |
| [FileZilla Client oficial](https://filezilla-project.org/) | Número/data da release atual **não confirmados nesta consulta** | Não determinado | Existência e recursos confirmados; páginas de versões/download não forneceram histórico utilizável e SVN exigiu verificação humana. Não substituir por um mirror. |

Metadados reproduzíveis: [API etaHEN](https://api.github.com/repos/etaHEN/etaHEN/releases?per_page=100), [API ftpsrv](https://api.github.com/repos/ps5-payload-dev/ftpsrv/releases?per_page=100), [API ShadowMountPlus](https://api.github.com/repos/drakmor/ShadowMountPlus/releases?per_page=100), [API ps5upload](https://api.github.com/repos/phantomptr/ps5upload/releases?per_page=100), [API pkg-sender](https://api.github.com/repos/Loopayeh/pkg-sender/releases?per_page=100), [API PSS](https://api.github.com/repos/dirazi83/PSS/releases?per_page=100). Relação de fork: [metadados ShadowMountPlus](https://api.github.com/repos/drakmor/ShadowMountPlus).

## Firmware, exploit, loader, HEN e serviço

| Camada | O que confirma | O que não confirma |
|---|---|---|
| Firmware | Versão do software do console; offsets e caminhos podem variar | Ter cadeia pública funcional, HEN carregado ou instalação compatível |
| Exploit / jailbreak | Entrada e elevação de privilégios para aquela cadeia e versão | Funcionamento de todo payload, montagem, fPKG ou serviços de rede |
| ELF loader | Carregar código no console já explorado | FTP ou instalador habilitado; porta 9021 não é uma porta de envio de jogo |
| HEN / kstuff | Habilitações e patches específicos | Suporte irrestrito a todo firmware, pacote, DLC ou formato |
| Serviços | FTP, DPI v1, DPI v2, montador ou receptor, cada um com protocolo próprio | Identidade determinada apenas por porta ou instalação concluída |
| Formato | Dump/pasta, imagem ou pacote | Extensão como prova de assinatura, compatibilidade ou qualidade do conteúdo |

### Cadeias e compatibilidade: registrar o limite da fonte

- [Relapse, README fixado](https://github.com/ntfargo/Relapse-Exploit/blob/dd8e4e0914c5e9d066ee1f9b056be76cb992c8e0/README.md), commit de 2026-09-30: declara **7.00–13.60**, etapa de navegador e etapa de kernel, loader em 9021 e possibilidade de travamento/panic. [API de releases](https://api.github.com/repos/ntfargo/Relapse-Exploit/releases) retornou lista vazia; existe código, não uma release numerada confirmada. Isso não certifica etaHEN ou fPKG nessa faixa.
- [PS5 UMTX, README fixado](https://github.com/PS5Dev/PS5-UMTX-Jailbreak/blob/2cf6778ebe89ff35255e1c228826d0d2155e9d2a/README.md), 2024-10-08: diferencia vulnerabilidade até 7.61 da entrada WebKit corrigida em 6.00; lista versões até 5.50 e mantém ressalva de loader em 5.xx. Não transformar a faixa da vulnerabilidade em uma cadeia uniforme utilizável.
- [PSFree/Lapse, README fixado](https://github.com/Al-Azif/psfree-lapse/blob/08ecf038c94aa99b56e46c9f32e2e486f83656b6/README.md), 2025-09-05: escopo da vulnerabilidade PS5 PSFree 1.00–5.50 e Lapse 1.00–10.01, mas a tabela de suporte **deste repositório** marca PS5 N/A. Não recomendá-lo como implementação PS5 pronta pela primeira tabela.
- [elfldr](https://github.com/ps5-payload-dev/elfldr/blob/02cfe91eb3f9697787460ad77d73ff951f801f50/README.md), 2026-09-13: loader de payload após jailbreak, normalmente em 9021; não faz o papel de FTP/DPI. O Ferry não precisa passar a enviar ELF para usar serviços já carregados pelo usuário.
- [kstuff-lite v1.11](https://github.com/EchoStretch/kstuff-lite/releases/tag/v1.11), 2026-09-20: anuncia firmware 1.00–13.60, fPKG até 11.40 e correção para 11.60. Isso não prova suporte a fPKG PS5 em 12.xx/13.xx nem separa todos os casos PS4/PS5. Origem creditada: trabalho de sleirsgoevy e colaboradores; “lite” não é sinônimo de etaHEN upstream.
- O [README fixado do ps5upload](https://github.com/phantomptr/ps5upload/blob/3de53d2fd92c8f29fafe37f13c2c189713c0188a/README.md) separa binário/SDK **1.00–13.60**, cadeia prática aproximada **1.00–12.70** e hardware do autor **5.10/9.60**. Há divergência com a [release v5.40.0](https://github.com/phantomptr/ps5upload/releases/tag/v5.40.0), publicada em 2026-09-30 às 05:45:05 UTC: o autor relata testes em **dois consoles 13.60, PS5 de lançamento e PS5 Pro**, incluindo upload/download, pastas/ZIP, gerenciamento, montagem, saves e telas. Registrar esse relato mais recente sem apagar o limite do README nem estendê-lo a qualquer firmware/recurso. A release limita o suporte fake-package a **11.60** e informa que **wake de repouso falha em 13.60**, no contexto descrito de conta ativada offline/Remote Play. São resultados/limites declarados pelo autor, não testes nossos; “full speed” não é benchmark do Ferry.

Não construí uma tabela universal “jailbreak sim/não”: versões omitidas e firmwares posteriores ficam sem conclusão. Nenhuma atualização de firmware é recomendada a partir desta pesquisa. As tabelas antigas de [PKG-PS5.md](PKG-PS5.md) são contexto histórico, não fonte primária nova; seu ajuste é do dono desse arquivo, fora da posse Farol.

### Upstream, forks e ports

“Upstream” aqui significa projeto de origem/mantenedor, **não Sony oficial**. O [etaHEN upstream, README fixado](https://github.com/etaHEN/etaHEN/blob/dafa13b562ddb137a4b4a97b9aaa287c0c57cc9c/README.md) documenta FTP opcional 1337, loader 9021, DPI TCP 9090 e DPI v2 HTTP 12800. As opções DPI são independentes e inicialmente desativadas. Não há base nessa lista de portas para garantir compatibilidade com 13.60.

O [port GronedWaffel, README fixado](https://github.com/GronedWaffel/etahen-13.60/blob/655d03c10a09487d12097c8d73fc83f7907c4178/README.md) se declara experimental e não oficial. A [release r2](https://github.com/GronedWaffel/etahen-13.60/releases/tag/v2.5B-13.60-r2), publicada em 2026-09-30, limita-se a **13.60** e usa uma build ShadowMount ajustada. Relatos do tester sobre Toolbox/energia não certificam todos os recursos, outros firmwares ou repouso com jogos montados. `fork=false` na API desse port não muda sua origem derivada; metadado GitHub não substitui autoria/licença.

O [ShadowMountPlus 1.7beta1](https://github.com/drakmor/ShadowMountPlus/releases/tag/1.7beta1) anuncia suporte até 13.60 com Kstuff-lite >=1.07, UFS recomendado e PFS experimental; avisa riscos de montagem/energia. API HTTP v1 padrão em loopback `127.0.0.1:10101`, sem autenticação. Não abrir essa API à LAN automaticamente nem copiar comandos de montagem/delete para o Ferry. `.ffpkg` é imagem UFS, não um `.pkg` instalável via DPI.

## Comparação ao Ferry conferido no código

Referências locais na base `ec0ebbb`, não funcionalidades apenas prometidas pelo roadmap:

| Área | Comportamento observado | Código |
|---|---|---|
| Fontes e formatos | Compactados completos ZIP/RAR/7z, dumps e sobreposição `dec`; imagens soltas/compactadas `.exfat`, `.ffpkg`, `.ffpfs`, `.ffpfsc`; PKG solto ou um PKG por compactado, mistura rejeitada | [Archives.cs](../core/Archives.cs), `Group`, `Plan`, `ImagePlan`, `PackagePlan` |
| Streaming | `7z l` completo seguido de `7z x -so`; tamanhos/ordem conhecidos antes de cortar stdout. Espera estabilidade e volumes completos | [Archives.cs](../core/Archives.cs), `ListAsync`/`OpenStream`; [Engine.cs](../core/Engine.cs), `ScanAsync`/`ProcessAsync` |
| FTP | PASV, UTF-8, SELF desativado por conexão, TYPE I, SIZE e resposta final; pequenos arquivos em buffers limitados, grandes em stream | [Ftp.cs](../core/Ftp.cs), `Open`/`StreamAsync`/`Put` |
| Retomada e publicação | APPE condicionado a parcial registrado pelo Ferry; arquivos completos evitados. Metadados/imagens aguardam `.ferry-part` + rename; não é transação de todo o jogo | [Engine.cs](../core/Engine.cs), `Held`; [Ftp.cs](../core/Ftp.cs), `RenameAsync` |
| PKG | Identifica CNT/FIH sem verificar assinatura/firmware. Snapshot/identidade, caminho exclusivo, ledger persistido. `prepared`, `submitted` e `unknown` distintos; não repete automaticamente pedido incerto | [PackageHeader.cs](../core/PackageHeader.cs); [Engine.Packages.cs](../core/Engine.Packages.cs) |
| Diagnóstico | Busca /24 em portas 2121/1337, encontra socket, não identifica PS5. Teste FTP verifica login/pasta de dump; teste DPI verifica somente conexão TCP | [Discovery.cs](../core/Discovery.cs); [Ftp.cs](../core/Ftp.cs), `TestMessageAsync`; [PkgInstaller.cs](../core/PkgInstaller.cs), `TestAsync` |
| Interfaces | WPF Windows e web self-hosted, fila persistida, localização pt-BR/en e webhook; upload web com parcial isolado | [ARQUITETURA.md](ARQUITETURA.md); [web/Uploads.cs](../web/Uploads.cs); [Settings.cs](../core/Settings.cs) |

**SIZE confere comprimento, não hash remoto.** SHA-256 nos E2E compara os bytes do servidor falso; não vira garantia de integridade criptográfica no console. `Job.Installed` sinaliza destino já publicado, não comprovação de instalação/execução. `Done` também pode notificar PKG preparado, solicitado ou incerto: consumidores como automação de energia precisam observar o estado, não somente o callback.

| Ferramenta | Recursos úteis documentados | Dependências e limites | Diferença prática / oportunidade para Ferry |
|---|---|---|---|
| [ps5upload, README fixado](https://github.com/phantomptr/ps5upload/blob/3de53d2fd92c8f29fafe37f13c2c189713c0188a/README.md) e [release v5.40.0](https://github.com/phantomptr/ps5upload/releases/tag/v5.40.0) | FTX2, retomada, BLAKE3 por shard, packing e gerenciamento; release relata testes em dois PS5 13.60 (lançamento/Pro) | Receptor 9113/9114 e instalador próprios; README ainda cita 5.10/9.60 e faixa prática até 12.70. Release: fake-package até 11.60; wake 13.60 falha. Relato do autor, sem nossos testes | Integridade e estados são referências úteis; copiar protocolo exigiria payload, fora do escopo. Não inferir vantagem medida sobre FTP/Ferry |
| [pkg-sender, README fixado](https://github.com/Loopayeh/pkg-sender/blob/04b5b2183bad9d248053a378d56094537272c44b/README.md) | Catálogo, família base/update/DLC por Title ID, descoberta com confirmação, fila e retomada de imagens | PS5 exige `pkg-receiver.elf`; HTTP 12800, beacons UDP e servidor PC 9898. API declarada instável | Orientação de conexão e confirmação de IP cabem sem receptor próprio. Famílias requerem metadata PKG adicional. Não confundir o receptor com etaHEN DPI v2 pela porta igual |
| [PSS, README fixado](https://github.com/dirazi83/PSS/blob/f5910015795644a3f769a84eaa3c654cab6a0a31/README.md) | Gerenciador FTP, biblioteca/metadata, criação de imagens e guia de configuração | PySide6; exploração/DNS/payload e conversão ampliam instalação e manutenção. Remote PKG Installer descrito na seção PS4 | Diagnóstico e ajuda contextual úteis; não prova instalação PS5 via mesmo serviço PS4. Não reproduzir a suíte completa |
| [FileZilla Client, recursos oficiais](https://filezilla-project.org/client_features.php) | FTP/FTPS/SFTP, fila, retomada >4 GB, limites de velocidade, comparação de diretórios e logs | Funciona conforme comandos do servidor; não prepara dumps, aplica `dec` ou segura publicação para montador. Release atual não confirmada | Bom cliente geral para manutenção manual; limite de velocidade é candidato simples. Ferry já automatiza compactado → destino e publicação |
| [ftpsrv, README fixado](https://github.com/ps5-payload-dev/ftpsrv/blob/23737c4738d15b9b9b036665bbf008d8fd6d82d2/README.md) | Servidor 2121, FileZilla entre clientes testados, SELF por conexão | Jailbreak + ELF loader; não é um cliente ou instalador. Comandos variam com a build | É infraestrutura que o Ferry consome. Preservar adaptação a comandos limitados; não trocar dependência por um receptor Ferry |

Essas cinco entradas incluem quatro clientes/ferramentas comparáveis e um servidor de infraestrutura; ShadowMountPlus é montador, não concorrente de upload. Os recursos de cada linha são documentados pelos respectivos autores, não exercitados nesta rodada. Dados de velocidade no pkg-sender, inclusive loopback/PS4, e números de montagem/transferência de outros autores não são benchmark nosso nem comparação PS5 na mesma rede.

## Ranking de oportunidades

Escala ordinal 1–5: valor maior é melhor; esforço e risco maiores são piores. É julgamento de produto baseado no código e nas fontes, sem telemetria ou teste com usuários. Ordem pondera fricção resolvida e preservação dos contratos.

| Ordem | Oportunidade | Valor | Esforço | Risco | Decisão e evidência |
|---|---|---:|---:|---:|---|
| 1 | Diagnóstico de serviços/destinos, com evidência e limites visíveis | 5 | 2 | 2 | Próxima fase. `Discovery` detecta TCP; teste FTP cobre só RemoteDir; DPI testa só socket. Fontes usam protocolos diferentes em portas semelhantes |
| 2 | Limite de velocidade durante envio | 3 | 2 | 2 | Futuro. FileZilla oferece; Ferry usa pool/stream e precisaria limitar vazão agregada sem quebrar pausa/retomada. Demanda ainda não medida |
| 3 | Manifesto de envio de pasta já extraída | 4 | 3 | 3 | Futuro. Há valor para homebrew/dumps, mas `Group` hoje não trata diretório; preservar `dec`, identidade da origem e publicação exige contrato próprio |
| 4 | PKG solto por HTTP Range para DPI existente | 4 | 4 | 4 | Futuro condicionado a serviço real validado. Evitaria cópia intermediária no console, mas acrescenta servidor Windows, tokens, firewall, lifetime e incerteza; compactado não é seekable |
| 5 | Metadata/famílias base-update-DLC de PKG | 3 | 4 | 3 | Futuro. Referência pkg-sender/PSS; Ferry hoje identifica container, não extrai Title ID e semântica PKG suficientes para ordem automática |
| 6 | RAR progressivo | 4 | 5 | 5 | Prova técnica primeiro. Altera descoberta do manifesto, decoder, espera, retomada e validação antes de publicar |
| 7 | Biblioteca/API ShadowMount+ | 3 | 4 | 5 | Adiar. API local e não autenticada; versões/forks/firmware e operações destrutivas tornam integração mais arriscada |

Valor/esforço estimados não autorizam implementação. Payload próprio, distribuição/envio de ELF e exploração/DNS não participam da fase recomendada.

## Reavaliação da fase 7: RAR progressivo

### Evidência primária

1. [RAR5, especificação oficial](https://www.rarlab.com/technote.htm): cabeçalhos/dados de arquivos são sequenciais; flags indicam volumes, continuação de dados e arquivo sólido. O fim informa se outro volume é necessário. É base para leitura incremental, não promessa de manifesto completo no primeiro volume. Um arquivo pode atravessar volumes, e sólido conserva dependências de descompressão anteriores.
2. [7-Zip upstream, `Rar5Handler.cpp`](https://github.com/ip7z/7zip/blob/0766b733fe3e06dd2a7f9a3cfbf2108ac73abd17/CPP/7zip/Archive/Rar/Rar5Handler.cpp#L2251), commit de 2026-09-04: o caminho de abertura enumera o próximo volume via `GetStream`; quando ele falta, registra `_missingVolName` e interrompe a abertura. Isso confirma a dificuldade do uso atual `l` → `x -so`; não demonstra que toda biblioteca/extrator RAR seja incapaz de esperar volume futuro.
3. [Fonte UnRAR oficial 7.30 beta 1](https://www.rarlab.com/rar/unrarsrc-7.3.1.tar.gz), `version.hpp` datado 2026-09-09; SHA-256 do pacote consultado `634900842a3737d9cc15bbcc71d4c74cc713437e0bca296a573424fe5f2660ab`. Em `dll.hpp`: `UCM_CHANGEVOLUME(W)`, `UCM_PROCESSDATA`, `RAR_VOL_ASK`. Em `volume.cpp`: callback permite aguardar volume ainda inexistente; comentário de `-vp` alerta que arquivo presente pode ainda estar recebendo chunks. Existe mecanismo de espera/extração incremental, não um adaptador Ferry pronto. Origem confirmada na [página RARLAB](https://www.rarlab.com/rar_add.htm), separada de addons de terceiros.
4. O mesmo pacote tem `license.txt`: distribuição/uso para extração têm condições específicas. Uma futura decisão de incorporar UnRAR precisa verificar licença, empacotamento e ABI em Windows/Linux; não assumir licença MIT ou equivalência a um wrapper de terceiros.

### Bloqueios específicos do Ferry

- `ScanAsync` exige estabilidade das partes e `ListAsync` sem erro; `Ftp.StreamAsync` usa lista/tamanhos completos. Injetar o próximo volume no disco não fornece esse manifesto antecipado.
- A pasta raiz, imagens/PKG e a sobreposição `dec` podem aparecer tarde. Um arquivo enviado cedo pode depois ser descartado/substituído pelo planejamento completo. Colisões de destino, caminhos inseguros e compactados mistos também precisam ser rejeitados antes da publicação.
- CRC/hash pode falhar depois de bytes já enviados. Igualdade de SIZE e fim do stdout não substituem o resultado do decoder. Nenhum rename de metadado/imagem/PKG, pedido DPI, conclusão ou remoção de original pode ocorrer antes da validação de todo o conjunto.
- Sólido e arquivo atravessando partes precisam manter estado do decoder. Retomar APPE remoto não retoma automaticamente o decoder no mesmo byte; reprocessar o prefixo pode ser necessário. Escrever antes de CRC deixa parcial provisório, não sucesso.
- Volume com nome final e tamanho estável por alguns segundos pode voltar a crescer; estabilidade é heurística. Preferir sinal de conclusão do produtor/rename final; substituir volume já usado deve invalidar a sessão.
- Esperar download mantendo FTP aberto introduz timeout. Se houver prova técnica, explorar fechamento do socket e retomada apenas do parcial de identidade conhecida, com cancelamento imediato e buffers limitados; não prometer espera infinita segura.
- “ZIP/7z sempre têm índice no fim” é generalização excessiva: layout, headers e acesso necessário variam. Para o caminho de listagem completa do Ferry, ambos continuam esperando o conjunto; não se propõe leitor progressivo para eles.

### Decisão e gate futuro

**Possível em princípio para um subconjunto de RAR, não aprovado para entrega de produto.** Preferir prova isolada com UnRAR/callbacks à escrita de parser/decoder próprio. Ela deverá usar arquivos sintéticos e FTP falso, sem alterar o fluxo padrão e sem payload. Não foi executada nesta rodada.

Gate: RAR4/RAR5, sólido/não sólido, criptografia de headers/dados, arquivo atravessando volume, `dec` tardio, colisão e PKG/imagem misturados; chegada sequencial com volume ausente/mutável; CRC errado no último volume; pausa/cancelamento/reinício; hash final e nenhum metadado final antes do CRC/último volume; limite de RAM, ausência de staging do jogo inteiro, retomar com APPE suportado e fallback STOR. Se não resolver manifesto tardio e estado de decoder, manter espera completa ou rejeitar o subconjunto explicitamente. Nenhum ganho de velocidade está comprovado.

## Validação e pendências desta pesquisa

Foram lidos fontes primárias ao vivo, metadados de releases, código de extratores em memória e código local; nenhum binário externo foi executado. As páginas FileZilla foram lidas via HTTP; os recursos estavam disponíveis no conteúdo que o próprio site apresenta por JavaScript. Não havia navegador conectado; versões/download e SVN não permitiram confirmar a release atual.

Validação documental: `git diff --check`, links locais, existência dos símbolos e verificação HTTP dos links externos. Resultados finais são registrados em [plano](plans/next/proxima-fase-sem-payload.md#validação-desta-entrega-documental). Não rodei E2E/build por ser entrega restrita a Markdown. Console real, desempenho comparativo e prova incremental RAR permanecem sem validação. Documentos existentes fora da posse, incluindo traduções do roadmap e pesquisa PKG anterior, não foram editados.
