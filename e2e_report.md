# Relatório E2E — PS5 Sender

- Data: 2026-09-30 00:12:50
- Resultado geral: **PASSOU**  (37s)
- Servidor: pyftpdlib (imitando o ftpsrv: só os comandos dele; upload limitado a 40 MB/s por conexão) em 127.0.0.1:61709, destino `/mnt/ext1/homebrew`, 4 conexões, apagar original = sim
- Ferramentas: 7-Zip 24.07 (embutido no app), Rar.exe 6.24.0 (só para gerar os testes)
- Jogo falso: 6 arquivos (~24 MB, incompressíveis) dentro de 2 pastas casca; volumes de 5 MB

| Formato | Volumes | Esperou volume faltante | Estado final | SHA-256 iguais | Senha | Originais apagados | Resultado |
|---|---|---|---|---|---|---|---|
| zip.001 / .002 | 5 (`G1.zip.005` chegou por último) | ok | Concluído | 6/6 | n/a | sim | ✅ OK |
| .z01 / .z02 + .zip | 5 (`G2.z04` chegou por último) | ok | Concluído | 6/6 | n/a | sim | ✅ OK |
| .part1.rar … .partN.rar (RAR5) | 5 (`G3.part5.rar` chegou por último) | ok | Concluído | 6/6 | n/a | sim | ✅ OK |
| .r00 / .r01 + .rar (RAR4) | 5 (`G4.r03` chegou por último) | ok | Concluído | 6/6 | n/a | sim | ✅ OK |
| .7z.001 … .N | 5 (`G5.7z.005` chegou por último) | ok | Concluído | 6/6 | n/a | sim | ✅ OK |
| .7z.001 com senha (diálogo) via arrastar-soltar | 5 (`G6.7z.005` chegou por último) | ok | Concluído | 6/6 | pedida 2x (1ª errada) | sim | ✅ OK |
| .rar único com PPSA…-app0 + dec (dec sobrepõe) | 1 (`G7.rar` chegou por último) | ok | Concluído | 7/7 | n/a | sim | ✅ OK |

| Verificação extra | Resultado |
|---|---|
| Testar conexão (credenciais certas) | Conectado, mas /mnt/ext1/homebrew não existe (será criado no envio). |
| Testar conexão (senha errada) | rejeitou: Code: 530 Message: Authentication failed. |
| Já no PS5, servidor sem APPE (igual ftpsrv) | ✅ EBOOT.BIN completo nem foi extraído/reenviado; big.bin pela metade foi reenviado inteiro (STOR); hash confere |
| Já no PS5, servidor com APPE | ✅ big.bin pela metade: enviou só a metade que faltava (APPE); jogo+dec com hash conferido |
| Fechar e reabrir o app no meio do envio | ✅ fechou em 0% com 2 arquivo(s) completos no PS5; ao reabrir a fila voltou sozinha, nenhum deles foi extraído/reenviado; hash confere |
| Pausar/retomar no meio do stream (G5) | ✅ pausou em 0% (progresso congelado por 1,5 s), retomou; hash confere |
| Remover da fila (G6) | ✅ sumiu da fila, não voltou sozinho, voltou ao adicionar de novo |
| Disco | extração em streaming (7z -so → FTP): nenhum arquivo extraído é gravado localmente |

Repetir: `dotnet run --project e2e` (na pasta PS5Sender). Requer Python com `pyftpdlib`.
