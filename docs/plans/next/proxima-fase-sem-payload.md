# Próxima fase sem payload — diagnóstico de serviços e destinos

Data: **2026-09-30**. Estado: **proposta documentada, não implementada**. Base pesquisada: `ec0ebbb`. Evidências e ranking: [cenário PS5](../../PESQUISA-CENARIO-PS5-2026-09.md). [Contrato desta rodada após integração](../completed/produto-ux-proxima-fase.md), mantido pelo Maestro; este plano não o modifica. O contrato foi concluído pelo Maestro após integração e validação local; o diagnóstico abaixo continua somente proposto.

## Problema e resultado esperado

Hoje encontrar uma porta 2121/1337 não identifica o console; testar FTP cobre a pasta de dump, e “Testar DPI” conecta ao socket sem validar o instalador. Quem envia imagem/PKG pode interpretar esses resultados como destino pronto ou instalação compatível. DPI v1, DPI v2 e receptores de terceiros têm protocolos diferentes; mudar a porta não os torna intercambiáveis.

Entregar **um diagnóstico explícito dos serviços e destinos configurados**, com estados, horário e próximos passos proporcionais à evidência. O usuário poderá distinguir FTP autenticado, pasta consultável, instalador apenas alcançável por TCP e resultado que ainda exige conferência no PS5. A fase usa serviços que o usuário já carregou; não instala, distribui, compila, hospeda ou envia payload.

Escolha: valor 5 / esforço 2 / risco 2 na escala ordinal da pesquisa. Resolve uma lacuna comprovada no código e evita acoplar o Ferry aos receptores de ps5upload/pkg-sender. Esforço é relativo, não prazo. Critério de sucesso é precisão do diagnóstico e ausência de efeitos remotos; não throughput.

## Escopo único

1. Modelo compartilhado no core para resultado de diagnóstico, usado por WPF e web.
2. Ação explícita “Verificar serviços e destinos”, sem varredura automática adicional. Usa host/portas/pastas que o usuário já configurou. O resultado pode mostrar dump, imagens e PKG separadamente.
3. FTP: conexão/autenticação e consultas de existência de cada destino configurado, sem criar pasta ou arquivo. Porta não determina marca ou firmware. Ausência/negação/não suporte precisam ser separados quando o protocolo permitir; caso ambíguo fica desconhecido.
4. DPI v1: testar somente alcance TCP da porta configurada. Não enviar JSON de instalação, URL, caminho fictício ou PKG de teste para identificar o servidor. Sem endpoint inofensivo confirmado no v1, identidade/protocolo/instalação continuam **não verificados**.
5. Resultado por serviço/caminho e ajuda contextual: FTP autenticado não prova permissão de escrita; DPI alcançável não prova pedido aceito; PKG preparado não prova instalação. Explicar dependências com links datados para fontes, sem recomendar firmware/jailbreak universal.
6. Nova execução substitui a anterior apenas se pertence à configuração vigente. Alterar configuração relevante invalida o resultado; trocar idioma apenas muda a apresentação. Nenhum resultado é reaproveitado após reinício.

Não criar “PS5 compatível” como booleano agregado. Diagnóstico não muda a fila, inicia envio nem bloqueia o fluxo existente com uma promessa de compatibilidade. O fluxo de envio continua a verificar suas condições reais; este relatório é informação para decisão do usuário.

## Contratos propostos

### Entradas e captura

Capturar um snapshot imutável de `Host`, `Port`, `User`, credencial FTP, `RemoteDir`, `ImageDir`, `PkgDir` e `DpiPort` da configuração válida no início. Credencial só em memória para autenticação; não entra em resposta, log, cache, comparação pública ou exportação. Campos inválidos ainda editados na UI não substituem os persistidos.

Cada execução recebe `requestId` e revisão de configuração. A revisão deve mudar quando qualquer entrada relevante, inclusive senha, mudar; não derivar identificador público de hash de senha. Captura de revisão e snapshot deve ser consistente. Cancelar a execução anterior quando iniciar outra. Publicação só aceita id/revisão atuais; cancelamento/resultado antigo não pode sobrescrever a execução nova.

### Resultado e semântica

Estrutura conceitual, sem criar código/schema nesta rodada:

| Campo | Contrato |
|---|---|
| `requestId`, `configurationRevision` | Identificam execução/configuração; sem segredo ou hash de credencial |
| `checkedAtUtc` | Horário da observação; não persistir como evidência vigente entre sessões |
| `ftpConnection` | `authenticated`, `failed`, `unknown`, `cancelled`; código/mensagem próprios e diagnóstico bruto limitado |
| `destinations[]` | Tipo `dump`, `image`, `pkg`, caminho consultado; `exists`, `missing`, `denied`, `unknown`, `notChecked`, `cancelled` |
| `dpiReachability` | `reachable`, `unreachable`, `unknown`, `cancelled`; sempre `verificationLevel=tcpOnly` |
| `limitations[]` | Escrita, firmware, assinatura, montador e instalação não verificados; não converter limite em falha inexistente |

Exemplo esperado: “FTP autenticado; pasta de imagens encontrada; escrita não verificada. Porta DPI acessível; protocolo e instalação não verificados.” Timeout/502/550 ambíguo não deve virar “pasta vazia”, “espaço zero” ou ausência confirmada.

Código FTP 550 sozinho não prova pasta ausente: pode ser permissão. A implementação deverá preservar o motivo retornado, usar apenas consulta sem efeito documentada e suportada, e classificar `unknown` se não puder distinguir. Não recorrer a STOR/MKD/APPE/RNTO para provar capacidade de escrita. Sem SIZE/hash de um arquivo conhecido, não anunciar validação de bytes.

### Limites de execução

- Operação cancelável; prazo máximo **10 s por conexão** e **30 s por diagnóstico completo**, incluindo consultas. No máximo uma conexão de diagnóstico FTP e uma TCP DPI simultâneas; nenhuma conexão por arquivo, recursão ou varredura de sub-rede adicional.
- O deadline engloba DNS, connect, login, consultas e leitura. Dispor sockets ao cancelar; limpar estado “verificando”. Cancelamento precisa concluir o resultado/tarefa sem deixar atualização pendente.
- Sem retry automático do diagnóstico e sem conexão em loop. O usuário pode executar de novo. Falha em um destino não impede mostrar evidências independentes já obtidas dentro do deadline.
- Durante transferência ativa ou solicitação de instalação, botão indisponível; se trabalho começar durante o teste, cancelar o diagnóstico. Não disputar FTP nem atrasar instalação. A verificação usa leitura real do estado do Engine, não inferência por porcentagem.
- Não invocar `Ftp.Open` se isso alternar SELF como efeito do diagnóstico; separar login/consulta de preparação de transferência. Não enviar comandos de montagem, energia, payload ou instalação. Preservar FTP de produção, protocolo DPI e política de APPE.

### Integração web, WPF e localização

- Na web, futura rota autenticada `POST /api/diagnostics/services` inicia uma execução; corpo não aceita host/URL/caminho arbitrário, usa apenas a configuração validada. Resposta contém somente resultado sanitizado. Usuário não autenticado recebe 401 e nenhuma conexão é aberta.
- Revisão/execução compartilhadas no servidor: abas concorrentes não sobrescrevem um resultado vigente com o antigo. WPF usa o mesmo serviço/modelo do core.
- Resultado é efêmero, fica fora de `settings.json`, `queue.json` e ledger PKG. Não alterar schema persistido nessa fase.
- Strings novas em pt-BR/en nos catálogos correspondentes; números/horários no idioma da UI. Usar mensagens estruturadas e razões claras, sem reescrever diagnóstico bruto. Limitar diagnóstico remoto exibido a 1 KiB, como texto, sem HTML/XAML executável e sem conteúdo de credenciais.
- Ação com nome acessível, foco/teclado, indicador de execução e cancelamento. Troca de idioma preserva snapshot/id/estado sem novo acesso de rede. Nenhum recurso de desligamento na web; não conectar este diagnóstico a gatilhos de energia.

## Não objetivos

- Payload próprio, envio de qualquer payload, atualização automática de etaHEN/kstuff/ShadowMount, hospedagem de exploit/DNS ou recomendação de atualização de firmware.
- Identificar versão real de firmware/HEN por banner/porta; consultar biblioteca ShadowMount+, abrir sua API à LAN, montar/desmontar ou apagar conteúdo remoto.
- Afirmar permissão de escrita, espaço livre, assinatura/compatibilidade PKG, execução de jogo ou instalação final com base em TCP/CWD/SIZE.
- HTTP Range de PKG, conversão/assinatura, agrupamento automático base/update/DLC, pasta extraída, limite de velocidade ou alterações na retomada/publicação.
- RAR progressivo, trocar extrator ou remover a espera pelas partes. Ele é uma prova técnica futura separada, não um segundo pacote escondido nesta fase.
- Redesenho do app, perfis de múltiplos consoles, telemetria, benchmark ou disparo de webhook de diagnóstico.

## Casos de falha primeiro

Escrever cenários de integração/E2E **antes do código**, com FTP/TCP locais falsos e configurações isoladas. Aproveitar os harnesses existentes; não testar apenas um booleano que reproduz a implementação.

| Caso | Observação/aceite |
|---|---|
| TCP aberto que não é FTP; banner inválido | Nunca autenticar/rotular como PS5 pronto; erro/unknown explicado, sem escrita |
| FTP autenticado; dump existe; imagem ausente; PKG negado | Resultado separado por caminho; nenhum verde geral, nenhuma criação |
| FTP 550 ambíguo / 502 em consulta; socket cai após login | Unknown preserva o limite; não “missing” por inferência |
| Login errado | Não consultar destinos; senha ausente de resposta/log; erro de autenticação claro |
| Porta DPI aberta servindo outro protocolo | Somente `tcpOnly`; nenhum byte JSON/URL de instalação enviado |
| DPI recusado, DNS lento, FTP trava na consulta | Prazos cumpridos, cancelamento libera sockets, evidências independentes preservadas |
| Nova execução conclui antes da antiga; edição de host/senha/pasta durante teste | Antiga descartada; revisão invalida resultado, sem exibir destino errado |
| Mudança de idioma e abas concorrentes | Sem novos probes pela troca de idioma; somente execução vigente vence |
| Transferência/senha/instalação ativa | Diagnóstico não abre conexão extra; corrida de início cancela o probe |
| API sem login ou com corpo contendo host arbitrário | 401/validação; zero conexões fora do snapshot configurado |
| Abrir configuração antiga/reiniciar | Mesmos defaults, diagnóstico não restaurado; fila/ledger intactos |
| Resultado `unknown` em PKG antes/depois do diagnóstico | Nenhum RequestInstall, retry, mudança de estado, Done ou gatilho de energia |

## Aceite

1. Mostrar claramente resultado por serviço/destino, horário, cancelamento e limites, nos dois idiomas; strings sem prometer instalação.
2. Captura consistente, revisão e execução garantem que resposta velha nunca substitui resultado novo e nenhum segredo é divulgado.
3. Log do servidor falso demonstra **zero** STOR, APPE, MKD, DELE, RNFR/RNTO, comando SELF, JSON DPI ou envio a loader. Consultas não inventam capacidades não suportadas.
4. Timeouts/cancelamento demonstrados sem bloquear UI/fila; não há diagnóstico durante atividade de transferência/instalação.
5. Preservar hashes/retomada/publicação nos testes de envio existentes; ledger de pedido incerto permanece incerto e sem retry automático.
6. Web autenticada e teclado/foco WPF exercitados; mudança de idioma não reinicia rede nem perde estado.
7. Testes locais e relato de aceite distinguem probes simulados de hardware. Console real poderá ser validado depois por pessoa autorizada; não é executado nesta rodada.

## Sequência futura e validação

Pacote único, depois de autorizado: casos falsos de falha → modelo/serviço no core → ação WPF/web + catálogos → validação focada → regressões pertinentes. Não começar por novos testes unitários depois de implementar.

Comandos já existentes no repositório, **a executar na futura implementação**:

```powershell
dotnet run --project e2e -- --localization
dotnet run --project e2e -- --pkg
dotnet run --project e2e -- G3 G7
dotnet run --project e2e/windows -c Release
dotnet build web -c Release
```

Acrescentar casos de diagnóstico ao harness focado e documentar seu seletor real quando existir; nenhum `--diagnostics` foi criado por este plano. WPF usa energia fake e serviços locais; preservar esse limite da rodada Brisa. Para web, seguir [TESTES.md](../../TESTES.md): E2E Chromium nos dois idiomas; Docker se disponível, modo DLL local com limitação explícita caso contrário. A implementação deverá executar também os checks exigidos pela CI/contrato em vigor; não reexecutar suíte global nesta entrega de Markdown.

Não publicar fase concluída com testes falhando. Confirmar erros atribuídos à base mediante reprodução e informar comando/resultado. Não declarar PS5 compatível ou release pronta com fixtures locais.

## Gate separado para RAR progressivo

Não é a próxima fase. A [pesquisa](../../PESQUISA-CENARIO-PS5-2026-09.md#reavaliação-da-fase-7-rar-progressivo) identifica callbacks UnRAR e impedimentos do manifesto completo/`dec` tardio. Uma futura prova terá que preservar CRC, publicação tardia, identidade, cancelamento, RAM limitada e pausa/retomada nos subconjuntos suportados; ZIP/7z seguem completos. Resultado da prova poderá ser rejeição ou suporte restrito, sem obrigação de entregar recurso. Nunca parser/decoder RAR próprio nesta fase.

## Validação desta entrega documental

Executados em 2026-09-30: `git diff --check` (exit 0); script Python de leitura UTF-8, links locais e HTTP externo. Primeira passada: 29 links locais existentes e 39 URLs externas com HTTP 200. Checagem após ajustes do Maestro: 29 links locais e quatro âncoras válidos; 36/39 URLs com HTTP 200, três APIs GitHub retornaram **403 rate limit exceeded** (etaHEN/PSS/Relapse releases, previamente lidas com sucesso). Essa segunda execução saiu com erro por exigir todos os HTTP 200; não foi classificada como falha preexistente nem repetida para ocultar o limite. Fontes já obtidas sustentam a pesquisa, mas a disponibilidade HTTP final desses três endpoints ficou limitada.

Na entrega isolada do Farol, o link ao contrato completed era o único destino local previsto ainda ausente. O Maestro o resolveu na integração; Farol não criou nem editou o contrato. A contagem HTTP confirma acesso, não valida cada alegação por si só; a leitura das fontes é a evidência de conteúdo.

Não há implementação nem novos testes nesta entrega; E2E/build não foram executados por escopo exclusivo de Markdown. As fontes e o código foram lidos ao vivo; console, benchmark e prova incremental não foram executados. A revisão do diff Brisa foi somente leitura do snapshot disponível, incluindo adaptador de energia, sessão, observação do Engine e casos fake; riscos foram registrados na seção Farol do Quadro-2 e comunicados ao Timoneiro. Não se editou nem testou a worktree Brisa; a revisão não é aceite de seu código ainda em execução.
