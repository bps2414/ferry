# Relatório E2E — Ferry

- Data: 2026-09-30 11:11:15 · Windows
- Resultado geral: **PASSOU**  (78s)
- Servidor: pyftpdlib (imitando o ftpsrv: só os comandos dele; upload limitado a 40 MB/s por conexão) em 127.0.0.1:51475, destino `/mnt/ext1/homebrew`, 4 conexões, apagar original = sim
- Ferramentas: 7-Zip 24.07 (embutido no app), RAR 6.24.0 (só para gerar os testes)
- Jogo falso: 6 arquivos (~24 MB, incompressíveis) dentro de 2 pastas casca; volumes de 5 MB

| Formato | Volumes | Esperou volume faltante | Estado final | SHA-256 iguais | Senha | Capa e título | param.json/sfo renomeados depois do último envio | Originais apagados | Resultado |
|---|---|---|---|---|---|---|---|---|---|
| zip.001 / .002 | 5 (`G1.zip.005` chegou por último) | ok | Concluído | 7/7 | n/a | PPSA00001 · Jogo Teste 1 · capa ok | ok | sim | ✅ OK |
| .z01 / .z02 + .zip | 5 (`G2.z04` chegou por último) | ok ("faltando z04") | Concluído | 7/7 | n/a | PPSA00002 · Jogo Teste 2 · capa ok | ok | sim | ✅ OK |
| .part1.rar … .partN.rar (RAR5) | 5 (`G3.part5.rar` chegou por último) | ok ("faltando part5.rar") | Concluído | 7/7 | n/a | PPSA00003 · Jogo Teste 3 · capa ok | ok | sim | ✅ OK |
| .r00 / .r01 + .rar (RAR4) | 5 (`G4.r03` chegou por último) | ok ("faltando r03") | Concluído | 7/7 | n/a | PPSA00004 · Jogo Teste 4 · capa ok | ok | sim | ✅ OK |
| .7z.001 … .N | 5 (`G5.7z.005` chegou por último) | ok | Concluído | 7/7 | n/a | PPSA00005 · Jogo Teste 5 · capa ok | ok | sim | ✅ OK |
| .7z.001 com senha (diálogo) via arrastar-soltar | 5 (`G6.7z.005` chegou por último) | ok | Concluído | 7/7 | senha conhecida, sem diálogo | PPSA00006 · Jogo Teste 6 · capa ok | ok | sim | ✅ OK |
| .rar único com PPSA…-app0 + dec (dec sobrepõe) | 1 (`G7.rar` chegou por último) | ok | Concluído | 8/8 | n/a | PPSA00007 · Jogo Teste 7 · capa ok | ok | sim | ✅ OK |

| Verificação extra | Resultado |
|---|---|
| Testar conexão (credenciais certas) | Conectado, mas /mnt/ext1/homebrew não existe (será criado no envio). |
| Testar conexão (senha errada) | rejeitou: Code: 530 Message: Authentication failed. |
| Já no PS5, servidor sem APPE (igual ftpsrv antigo) | ✅ EBOOT.BIN completo nem foi extraído/reenviado; big.bin pela metade (não começado por este app) foi reenviado inteiro (STOR); hash confere |
| Já no PS5, servidor com APPE e SELF (ftpsrv novo) | ✅ big.bin nosso pela metade: só a metade que faltava (APPE, 11000000 bytes); EBOOT.BIN menor de outra versão: STOR inteiro; icon0.png maior: ficou do tamanho certo; SELF desligado (SIZE real); 8/8 hashes; param.json/sfo renomeados só depois do último envio |
| Jogo já instalado no PS5 | ✅ avisou "Jogo já instalado…" sem enviar; "Tentar de novo" reenviou por cima e conferiu |
| Log persistente (log.txt) | ✅ comando e resposta de STOR/APPE/SIZE gravados |
| Fechar e reabrir o app no meio do envio (G6) | ✅ fechou em 0% com 3 arquivo(s) completos no PS5; ao reabrir a fila voltou sozinha, nenhum deles foi extraído/reenviado; hash confere |
| Senha aprendida e lembrada (G6) | ✅ diálogo 2x (1ª errada), senha entrou no fim das senhas conhecidas; ao reabrir sem senhas conhecidas abriu com a senha lembrada (cifrada na fila), sem diálogo |
| Salvar atômico | ✅ .tmp pela metade na fila não impediu reabrir; settings.json e queue.json sem sobra de .tmp e válidos |
| Pausar/retomar no meio do stream (G5) | ✅ pausou em 0% (progresso congelado por 1,5 s), retomou; hash confere |
| Remover da fila (G6) | ✅ sumiu da fila, não voltou sozinho, voltou ao adicionar de novo |
| Migração de dados PS5Sender → Ferry | ✅ settings.json, queue.json e log.txt copiados com o mesmo conteúdo; pasta antiga intacta; 2ª chamada não sobrescreveu o settings.json alterado |
| Imagem .exfat solta e dentro de .part1.rar (ShadowMount+) | ✅ IMG1 arrastado e IMG2 (dentro do .part1.rar) enviados para ImageDir com o nome do arquivo, sem capa; hash confere; pausou/retomou e continuou com APPE (1x); RNTO para o nome final só depois do último envio; sem sobra .ferry-part; nada em /mnt/ext1/homebrew |
| Imagem .exfat: reabrir com parcial nosso (APPE) e parcial de outra versão (STOR inteiro) | ✅ parcial nosso do IMG1 (registrado na fila): só a metade que faltava (APPE, 20000000 bytes); parcial de outra versão do IMG2: STOR inteiro (12000000 bytes), sem APPE; hashes conferem, sem sobra .ferry-part |
| Imagem .exfat já no PS5: aviso + Tentar de novo | ✅ IMG1 e IMG2 já no PS5: avisou "Jogo já instalado…" sem enviar nada; "Tentar de novo" reenviou por cima (STOR inteiro) e conferiu o hash |
| Transferir agora | ✅ com ImgA (80 MB) em 26% enviando, "Transferir agora" no IMG2: ImgA voltou para a fila (não pausou), IMG2 ficou Verificado primeiro, depois ImgA continuou só com o que faltava (STOR 23068672 + APPE 56931328 bytes = 80000000); IMG2 enviado uma vez; hashes conferem |
| Disco | extração em streaming (7z -so → FTP): nenhum arquivo extraído é gravado localmente |

Repetir: `dotnet run --project e2e` (na pasta do repo). Requer Python com `pyftpdlib`.
