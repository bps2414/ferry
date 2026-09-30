# Transferências longas no Windows

O Ferry oferece duas opções explícitas no topo da janela: **desligar este PC ao terminar as transferências** e **impedir suspensão durante envios**. Ambas começam desativadas, valem somente na sessão atual e não são gravadas na configuração. Não há controle de energia na web, no PS5 ou no servidor Docker.

## Desligar este PC

1. Adicione trabalho que ainda precisa terminar e marque a opção. Uma fila vazia ou apenas histórico concluído não permite armar.
2. O Ferry aguarda sucesso de todos os cards presentes. Pausa, cancelamento, erro, partes faltantes, senha pendente, pedido de instalação em andamento e resultado incerto bloqueiam a contagem.
3. Após conclusão, a janela mostra **60 segundos** para cancelar. Se estava na bandeja, ela reaparece. O botão de cancelamento e a opção ficam disponíveis em qualquer página. Fechar o Ferry também cancela.
4. Adicionar, remover ou substituir cards desarma. `AddFiles` é observado antes de o card aparecer. As fontes da pasta monitorada e dos arquivos adicionados são inspecionadas desde o armamento, incluindo nomes, tamanhos e datas das partes; trabalho ainda não descoberto ou fontes indisponíveis bloqueiam. Fontes novas ou alteradas, inclusive histórico alterado no mesmo caminho antes da contagem, cancelam o desligamento. A exclusão automática de originais pelo envio bem-sucedido é registrada separadamente, com a revisão exata excluída, e não desarma. Retomar trabalho ou iniciar outro pedido de instalação invalida o prazo; depois do sucesso são necessários outros 60 segundos completos.
5. No fim do prazo, o Ferry confere novamente cards, senha, revisão de entrada e fontes antes de solicitar o desligamento local, uma única vez. Falha da ação é mostrada e não recebe retry automático.

**PKG enviado** e **instalação solicitada** podem encerrar a transferência, mas não comprovam instalação no PS5. **Verifique no PS5** continua bloqueando o desligamento. Os originais, protocolo FTP/DPI, retomada e publicação seguem suas políticas existentes.

Os 60 segundos são controlados pelo Ferry, usando relógio monotônico; o Windows recebe `shutdown.exe /s /t 0`, sem `/f` e sem host remoto. Um `/t` positivo implica `/f` no Windows, por isso não se usa o temporizador do sistema. A espera pela confirmação do comando tem prazo de 5 segundos; timeout é resultado não confirmado, sem repetição automática. Aplicativos abertos podem impedir o desligamento ou pedir salvamento. Depois de entregue o pedido ao Windows, o botão do Ferry já não aborta a ação. [Referência Microsoft](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/shutdown).

## Impedir suspensão durante envios

Quando marcada, a opção solicita `ES_CONTINUOUS | ES_SYSTEM_REQUIRED` enquanto existe extração/envio ativo e nenhuma senha pendente. A contagem de desligamento armado mantém essa solicitação mesmo com a opção de envio desmarcada, para que o PC não durma nos 60 segundos. Pausar/parar o envio libera sua solicitação; cancelar/finalizar a contagem ou fechar libera a solicitação da contagem, na mesma thread da interface. Não muda o plano de energia, não mantém a tela acesa e não impede suspensão solicitada manualmente. [Referência Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate).

## Validação e limites

```powershell
dotnet run --project e2e/windows -c Release -- --power
dotnet run --project e2e/windows -c Release
```

O primeiro comando exercita os contratos de energia com relógio controlado, fila/fontes locais e a janela WPF real. Salva `power-pt-BR.png` e `power-en.png` durante a contagem, no `FERRY_DATA` temporário anunciado pelo harness. O segundo confere regressões WPF de idioma, senha, bandeja e envio FTP/PKG com servidores locais falsos. **Os dois injetam energia fake**; nenhum teste instancia o adaptador nativo, desliga ou suspende o computador.

Os cenários de falha foram escritos antes da implementação. Cobrem vazio/histórico, todos os estados bloqueantes, vários cards, cancelamento/fechamento, troca de idioma, mutações rápidas entre ticks, falha de energia, liberação de suspensão e fontes novas ainda sem card no prazo final. Há também chegada entre a fotografia do tick e a reconferência final, fonte terminal alterada antes da contagem e envio FTP local com exclusão automática real do original temporário e energia fake. As capturas são renderizações WPF no tamanho padrão, sem provar cliques físicos, leitores de tela, balões ou execução nativa de energia. Hardware PS5 e energia real ficam fora desta validação.

Em 2026-09-30, na árvore integrada: `--power` passou com 188 checks e WPF sem seletor com 170 checks, ambos exit 0 após todos os ajustes. As capturas de energia fake em [pt-BR](../e2e/windows/report/power-pt-BR.png) e [en](../e2e/windows/report/power-en.png) foram preservadas da execução integrada. A revisão visual dos dois idiomas no tamanho padrão confirmou textos/cancelamento sem corte e espaço para a fila. Comandos, resultados e limites no [relatório da rodada](RELATORIO-PRODUTO-2026-09.md).
