// E2E: sobe FTP local (pyftpdlib), gera jogos falsos em cada formato de split, roda o Engine do app
// sem UI e confere SHA-256 de cada arquivo recebido. Gera e2e_report.md na raiz do projeto.
using System.Diagnostics;
using System.IO;
using System.IO.Hashing;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using PS5Sender;

var root = AppContext.BaseDirectory;
while (!Directory.Exists(Path.Combine(root, "e2e")) || !Directory.Exists(Path.Combine(root, "app"))) root = Path.GetDirectoryName(root)!;
var tools = Path.Combine(root, "e2e", "tools");
var sevenZip = Path.Combine(root, "app", "tools", "7z.exe");
var rar = Path.Combine(tools, "Rar.exe");
// Uso: dotnet run --project e2e            -> tudo (formatos + fases extras)
//      dotnet run --project e2e -- G2 G7   -> modo rápido: só esses casos, sem fases extras
var only = args.ToHashSet(StringComparer.OrdinalIgnoreCase);
var full = only.Count == 0;
var work = Path.Combine(Path.GetTempPath(), "ps5sender-e2e");
if (Directory.Exists(work)) Directory.Delete(work, true);
string Dir(string name) => Directory.CreateDirectory(Path.Combine(work, name)).FullName;
var input = Dir("input"); var dropped = Dir("dropped"); var ftpRoot = Dir("ftproot");
// Jogos falsos e arquivos compactados são determinísticos: ficam em cache entre rodadas.
// Mude GenVersion quando mexer no gerador.
const string GenVersion = "v1";
var cache = Path.Combine(Path.GetTempPath(), "ps5sender-e2e-cache-" + GenVersion);
var cached = File.Exists(Path.Combine(cache, "ok"));
if (!cached && Directory.Exists(cache)) Directory.Delete(cache, true);
var src = Directory.CreateDirectory(Path.Combine(cache, "src")).FullName;
var archives = Directory.CreateDirectory(Path.Combine(cache, "archives")).FullName;
const string RemoteDir = "/mnt/ext1/homebrew";
const long Vol = 5_000_000;
var sw = Stopwatch.StartNew();

await EnsureRar();

// ---------- jogos falsos ----------
// names: todo volume gerado precisa casar com isto (garante que o gerador fez o formato certo)
(string format, string archive, bool dropIn, string? pw, string names)[] cases =
[
    ("zip.001 / .002",               "G1.zip", false, null, @"^G1\.zip\.\d{3}$"),
    (".z01 / .z02 + .zip",           "G2.zip", false, null, @"^G2\.(z\d{2}|zip)$"),
    (".part1.rar … .partN.rar (RAR5)", "G3.rar", false, null, @"^G3\.part\d\.rar$"),
    (".r00 / .r01 + .rar (RAR4)",    "G4.rar", false, null, @"^G4\.(r\d{2}|rar)$"),
    (".7z.001 … .N",                 "G5.7z",  false, null, @"^G5\.7z\.\d{3}$"),
    (".7z.001 com senha (diálogo) via arrastar-soltar", "G6.7z", true, "senha123", @"^G6\.7z\.\d{3}$"),
    (".rar único com PPSA…-app0 + dec (dec sobrepõe)", "G7.rar", false, null, @"^G7\.rar$"),
];

var gameDirs = new Dictionary<string, string>();
for (var i = 0; i < cases.Length; i++)
{
    var c = cases[i];
    var game = $"PPSA0{i + 1:0000}-Jogo Teste {i + 1}";
    var shell = i == 6 ? Path.Combine(src, c.archive) : Path.Combine(src, c.archive, "Casca externa", "casca interna");
    var g = Path.Combine(shell, game);
    gameDirs[c.archive] = i == 6 ? Path.Combine(src, "_esperado_G7", game) : g; // G7: esperado = jogo + dec por cima
    if (cached) continue;
    var rnd = new Random(1000 + i); // determinístico: repetível
    void Put(string rel, int size) { var p = Path.Combine(g, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); var b = new byte[size]; rnd.NextBytes(b); File.WriteAllBytes(p, b); }
    Put("EBOOT.BIN", 1_200_000);
    Put(@"sce_sys\param.sfo", 4_096);
    Put(@"sce_sys\icon0.png", 150_000);
    Put(@"data\big.bin", 22_000_000);
    Put(@"data\sub pasta\ação çõ.dat", 777_777);
    Put(@"data\vazio.bin", 0);

    var outDir = Directory.CreateDirectory(Path.Combine(archives, c.archive)).FullName;
    var srcTop = Path.Combine(src, c.archive);
    var name = Path.GetFileNameWithoutExtension(c.archive);
    switch (i)
    {
        case 0: Run(sevenZip, srcTop, "a", "-tzip", "-mx0", "-v5000000b", Path.Combine(outDir, "G1.zip"), "Casca externa"); break;
        case 1: SplitZip.Write(srcTop, Path.Combine(outDir, "G2"), Vol); break;
        case 2: Run(rar, srcTop, "a", "-m0", "-v5000000b", "-r", "-idq", Path.Combine(outDir, "G3.rar"), "Casca externa"); break;
        // .r00 real = RAR4 com nomes antigos (RAR 7 não cria mais; por isso Rar.exe 6.24)
        case 3: Run(rar, srcTop, "a", "-ma4", "-m0", "-v5000000b", "-vn", "-r", "-idq", Path.Combine(outDir, "G4.rar"), "Casca externa"); break;
        case 4: Run(sevenZip, srcTop, "a", "-t7z", "-mx0", "-v5000000b", Path.Combine(outDir, "G5.7z"), "Casca externa"); break;
        case 5: Run(sevenZip, srcTop, "a", "-t7z", "-mx0", "-v5000000b", "-p" + c.pw, "-mhe=on", Path.Combine(outDir, "G6.7z"), "Casca externa"); break;
        case 6:
            // dec: troca EBOOT.BIN, sobrescreve um arquivo com tamanho diferente e adiciona um novo.
            var dec = Path.Combine(srcTop, "dec");
            foreach (var (rel, size) in new[] { ("EBOOT.BIN", 900_000), (@"data\sub pasta\ação çõ.dat", 12_345), (@"sce_module\libnovo.prx", 50_000) })
            {
                var p = Path.Combine(dec, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); var b = new byte[size]; rnd.NextBytes(b); File.WriteAllBytes(p, b);
            }
            var expected = gameDirs[c.archive];
            foreach (var f in Directory.GetFiles(g, "*", SearchOption.AllDirectories).Select(f => (f, g)).Concat(Directory.GetFiles(dec, "*", SearchOption.AllDirectories).Select(f => (f, g: dec))))
            {
                var dst = Path.Combine(expected, Path.GetRelativePath(f.g, f.f)); Directory.CreateDirectory(Path.GetDirectoryName(dst)!); File.Copy(f.f, dst, true);
            }
            // dec vem ANTES do jogo no arquivo: o app precisa descartar a versão do jogo mesmo chegando depois
            Run(rar, srcTop, "a", "-m0", "-r", "-idq", Path.Combine(outDir, "G7.rar"), "dec", game);
            break;
    }
    var made = Directory.GetFiles(outDir).Select(Path.GetFileName).ToList();
    if (made.Count < (i == 6 ? 1 : 2) || !made.All(n => System.Text.RegularExpressions.Regex.IsMatch(n!, c.names)))
        throw new Exception($"Gerador de {c.format} produziu nomes inesperados: {string.Join(", ", made)}");
}
if (!cached) File.WriteAllText(Path.Combine(cache, "ok"), "");
Console.WriteLine($"Arquivos de teste: {(cached ? "cache" : "gerados")} ({sw.Elapsed.TotalSeconds:0.0}s)");
if (!full) cases = cases.Where(c => only.Contains(Path.GetFileNameWithoutExtension(c.archive))).ToArray();

// ---------- servidor FTP ----------
// Principal imita o ftpsrv do PS5: sem APPE (parcial precisa ser reenviado inteiro).
var (ftp, port, ftpLog) = StartFtp(ftpRoot, appe: false);

var settings = new Settings { Host = "127.0.0.1", Port = port, User = "ps5", Password = "ps5pass", InputFolder = input, RemoteDir = RemoteDir, Connections = 4, DeleteOriginal = true };
var testMsg = await Ftp.TestAsync(settings);
var badLogin = "";
try { await Ftp.TestAsync(new Settings { Host = "127.0.0.1", Port = port, User = "ps5", Password = "errada", RemoteDir = RemoteDir }); badLogin = "aceitou senha errada (FALHA)"; }
catch (Exception e) { badLogin = "rejeitou: " + e.Message.Split('\n')[0]; }

// "Já no PS5": metade de big.bin (parcial) e o EBOOT.BIN inteiro do G1 já estão no servidor.
var g1RemoteDir = Path.Combine(ftpRoot, "mnt", "ext1", "homebrew", Path.GetFileName(gameDirs["G1.zip"]));
var g1Remote = Path.Combine(g1RemoteDir, "data", "big.bin");
Directory.CreateDirectory(Path.GetDirectoryName(g1Remote)!);
var bigLocal = File.ReadAllBytes(Path.Combine(gameDirs["G1.zip"], "data", "big.bin"));
File.WriteAllBytes(g1Remote, bigLocal[..(bigLocal.Length / 2)]);
File.Copy(Path.Combine(gameDirs["G1.zip"], "EBOOT.BIN"), Path.Combine(g1RemoteDir, "EBOOT.BIN"));

// ---------- roda o Engine ----------
var logLines = new List<string>();
var passwordAsked = new List<string>();
var engine = new Engine(settings, m => { lock (logLines) logLines.Add(m); Console.WriteLine(m); }, j =>
{
    passwordAsked.Add(j.Name);
    // 1ª tentativa errada: o app tem que perceber e pedir de novo
    return Task.FromResult<string?>(passwordAsked.Count(n => n == j.Name) == 1 ? "senha-errada" : cases.First(c => Path.GetFileNameWithoutExtension(c.archive) == j.Name).pw);
}) { StableSeconds = 2, QueueFile = Path.Combine(work, "queue-main.json") };
using var stop = new CancellationTokenSource();
var run = Task.Run(() => engine.RunAsync(stop.Token));

// Fase 1: todas as partes menos o último volume numerado.
var heldBack = new Dictionary<string, string>();
var partCount = new Dictionary<string, int>();
foreach (var c in cases)
{
    var files = Directory.GetFiles(Path.Combine(archives, c.archive)).Order(StringComparer.OrdinalIgnoreCase).ToList();
    partCount[c.archive] = files.Count;
    // arquivo único: ele mesmo chega (devagar) na fase 2
    var last = files.Where(f => !f.EndsWith(".zip") && !f.EndsWith(".rar") || f.Contains(".part")).LastOrDefault() ?? files.Last();
    heldBack[c.archive] = last;
    var dest = c.dropIn ? dropped : input;
    foreach (var f in files.Where(f => f != last)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)));
    if (c.dropIn) engine.AddFiles(Directory.GetFiles(dropped));
}
// espera cada caso em partes ser avaliado como incompleto (em vez de um tempo fixo)
for (var t0 = DateTime.UtcNow; DateTime.UtcNow - t0 < TimeSpan.FromSeconds(15); await Task.Delay(100))
{
    bool settled;
    lock (engine.Lock)
        settled = cases.Where(c => partCount[c.archive] > 1).All(c => engine.Jobs.Any(j => j.Name == Path.GetFileNameWithoutExtension(c.archive) && j.Detail.Contains("altam")));
    if (settled) break;
}
await Task.Delay(1000); // margem: nenhum deve sair de "aguardando" sozinho

// Remover da fila: G6 some e não volta sozinho; volta ao adicionar os arquivos de novo.
var removeResult = "❌ sem card do G6 para remover";
Job? g6card; lock (engine.Lock) g6card = engine.Jobs.FirstOrDefault(j => j.Name == "G6");
if (g6card != null)
{
    engine.Remove(g6card);
    await Task.Delay(3000);
    bool back; lock (engine.Lock) back = engine.Jobs.Any(j => j.Name == "G6");
    engine.AddFiles(Directory.GetFiles(dropped));
    await Task.Delay(2000);
    bool readded; lock (engine.Lock) readded = engine.Jobs.Any(j => j.Name == "G6" && j != g6card);
    removeResult = !back && readded ? "✅ sumiu da fila, não voltou sozinho, voltou ao adicionar de novo" : $"❌ voltou sozinho={back}, re-adicionado={readded}";
}
var waited = new Dictionary<string, string>();
foreach (var c in cases)
{
    var name = Path.GetFileNameWithoutExtension(c.archive);
    Job? j; lock (engine.Lock) j = engine.Jobs.FirstOrDefault(x => x.Name == name);
    waited[c.archive] = partCount[c.archive] == 1
        ? j == null ? "ok" : $"FALHA ({j.Stage})"
        : j?.Stage == Stage.AguardandoPartes ? "ok" : $"FALHA ({j?.Stage.ToString() ?? "sem card"})";
}

// Fase 2: chega o último volume, escrito devagar (como um download em andamento): 5 pedaços a cada 500 ms.
await Task.WhenAll(cases.Select(async c =>
{
    var data = File.ReadAllBytes(heldBack[c.archive]);
    await using var fs = new FileStream(Path.Combine(c.dropIn ? dropped : input, Path.GetFileName(heldBack[c.archive])), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    var chunk = data.Length / 5 + 1;
    for (var o = 0; o < data.Length; o += chunk)
    {
        await fs.WriteAsync(data.AsMemory(o, Math.Min(chunk, data.Length - o)));
        await fs.FlushAsync();
        await Task.Delay(500);
    }
}));

// Durante o envio: pausa o G5 no meio do stream, confere que o app parou de enviar, e retoma.
// "Parou" = progresso do app congelado. O tamanho no servidor não serve: no loopback o TCP guarda MBs em buffer
// que o servidor (limitado) continua gravando depois que o cliente já parou.
var pauseResult = "❌ não conseguiu pausar no meio";
var deadline = DateTime.UtcNow.AddMinutes(4);
var paused = false;
while (DateTime.UtcNow < deadline)
{
    bool done; Job? g5;
    lock (engine.Lock) { done = engine.Jobs.Count == cases.Length && engine.Jobs.All(j => j.Stage is Stage.Verificado or Stage.Erro); g5 = engine.Jobs.FirstOrDefault(j => j.Name == "G5"); }
    if (done) break;
    if (!paused && g5 is { Stage: Stage.Enviando, Progress: < 100 })
    {
        paused = true;
        engine.Pause(g5);
        await Task.Delay(500);
        var p1 = g5.Progress;
        await Task.Delay(1500);
        var p2 = g5.Progress;
        pauseResult = g5.Stage == Stage.Pausado && p1 == p2 && p1 < 100
            ? $"pausou em {p1:0}% (progresso congelado por 1,5 s), retomou"
            : $"❌ não parou (estado {g5.Stage}, progresso {p1:0.0}% → {p2:0.0}%)";
        engine.Resume(g5);
    }
    await Task.Delay(50);
}
stop.Cancel();
await run;

// ---------- verificação ----------
var rows = new List<string>();
var allOk = waited.Values.All(v => v == "ok") && testMsg.StartsWith("Conectado") && badLogin.StartsWith("rejeitou");
foreach (var c in cases)
{
    var name = Path.GetFileNameWithoutExtension(c.archive);
    Job? j; lock (engine.Lock) j = engine.Jobs.FirstOrDefault(x => x.Name == name);
    var g = gameDirs[c.archive];
    var remoteGame = Path.Combine(ftpRoot, "mnt", "ext1", "homebrew", Path.GetFileName(g));
    var files = Directory.GetFiles(g, "*", SearchOption.AllDirectories);
    var okHashes = files.Count(f =>
    {
        var r = Path.Combine(remoteGame, Path.GetRelativePath(g, f));
        return File.Exists(r) && Sha(f) == Sha(r);
    });
    var extra = Directory.Exists(remoteGame) ? Directory.GetFiles(remoteGame, "*", SearchOption.AllDirectories).Length - files.Length : -1;
    var origDeleted = !Directory.GetFiles(c.dropIn ? dropped : input).Any(f => Path.GetFileName(f).StartsWith(name + "."));
    var pwOk = c.pw == null ? passwordAsked.Contains(name) ? "pediu sem precisar" : "n/a" : passwordAsked.Count(n => n == name) == 2 ? "pedida 2x (1ª errada)" : $"pedida {passwordAsked.Count(n => n == name)}x (esperado 2)";
    var ok = j?.Stage == Stage.Verificado && okHashes == files.Length && extra == 0 && origDeleted && waited[c.archive] == "ok" && pwOk is "n/a" or "pedida 2x (1ª errada)";
    allOk &= ok;
    rows.Add($"| {c.format} | {partCount[c.archive]} (`{Path.GetFileName(heldBack[c.archive])}` chegou por último) | {waited[c.archive]} | {j?.StageText ?? "—"} | {okHashes}/{files.Length}{(extra != 0 ? $" (extras: {extra})" : "")} | {pwOk} | {(origDeleted ? "sim" : "não")} | {(ok ? "✅ OK" : "❌ FALHA" + (j?.Detail is { Length: > 0 } d ? ": " + d : ""))} |");
}
// pyftpdlib registra "STOR/APPE <caminho> completed=1 bytes=N".
static long Bytes(IEnumerable<string> lines) => lines.Sum(l => long.Parse(System.Text.RegularExpressions.Regex.Match(l, @"bytes=(\d+)").Groups[1].Value));
// o log do pyftpdlib no Windows usa "\" nos caminhos
List<string> Lines(List<string> log, string cmd, string pathPart) { lock (log) return log.Where(l => l.Contains(cmd + " ") && l.Replace('\\', '/').Contains(pathPart)).ToList(); }
var g1Name = Path.GetFileName(gameDirs["G1.zip"]);

// ---------- fases extras (só na rodada completa) ----------
var extraRows = new List<string>();
if (full)
{
// Sem APPE (ftpsrv): big.bin parcial é reenviado inteiro com STOR; EBOOT.BIN completo nem é extraído/enviado.
var bigStor = Lines(ftpLog, "STOR", g1Name + "/data/big.bin");
var ebootStor = Lines(ftpLog, "STOR", g1Name + "/EBOOT.BIN").Count;
var resumeOk = Lines(ftpLog, "APPE", g1Name).Count == 0 && bigStor.Count == 1 && Bytes(bigStor) == bigLocal.Length && ebootStor == 0
    && Sha(g1Remote) == Sha(Path.Combine(gameDirs["G1.zip"], "data", "big.bin"));
var resumeLine = $"big.bin STOR {bigStor.Count}x ({Bytes(bigStor)} bytes), EBOOT.BIN STOR {ebootStor}x";

// ---------- servidor COM APPE: parcial continua de onde parou ----------
var appeRoot = Dir("ftproot-appe");
var (ftp2, port2, ftp2Log) = StartFtp(appeRoot, appe: true);
var inAppe = Dir("input-appe");
File.Copy(Path.Combine(archives, "G7.rar", "G7.rar"), Path.Combine(inAppe, "G7.rar"));
var g7Name = Path.GetFileName(gameDirs["G7.rar"]);
var g7Big = Path.Combine(appeRoot, "mnt", "ext1", "homebrew", g7Name, "data", "big.bin");
Directory.CreateDirectory(Path.GetDirectoryName(g7Big)!);
var g7BigLocal = File.ReadAllBytes(Path.Combine(gameDirs["G7.rar"], "data", "big.bin"));
File.WriteAllBytes(g7Big, g7BigLocal[..(g7BigLocal.Length / 2)]);
var sAppe = new Settings { Host = "127.0.0.1", Port = port2, User = "ps5", Password = "ps5pass", InputFolder = inAppe, RemoteDir = RemoteDir, Connections = 4 };
var eAppe = new Engine(sAppe, m => Console.WriteLine("[appe] " + m), _ => Task.FromResult<string?>(null)) { StableSeconds = 2, QueueFile = Path.Combine(work, "queue-appe.json") };
await RunUntil(eAppe, j => j.Name == "G7" && j.Stage is Stage.Verificado or Stage.Erro, TimeSpan.FromMinutes(2));
var appeBytes = Bytes(Lines(ftp2Log, "APPE", g7Name + "/data/big.bin"));
var appeSame = SameFiles(gameDirs["G7.rar"], Path.Combine(appeRoot, "mnt", "ext1", "homebrew", g7Name));
var appeOk = appeBytes == g7BigLocal.Length - g7BigLocal.Length / 2 && appeSame == Directory.GetFiles(gameDirs["G7.rar"], "*", SearchOption.AllDirectories).Length;
ftp2.Kill(true);

// ---------- fechar e reabrir o app no meio do envio ----------
var inRe = Dir("reabrir");
foreach (var f in Directory.GetFiles(Path.Combine(archives, "G5.7z"))) File.Copy(f, Path.Combine(inRe, Path.GetFileName(f)));
var sRe = new Settings { Host = "127.0.0.1", Port = port, User = "ps5", Password = "ps5pass", InputFolder = "", RemoteDir = "/reabrir", Connections = 4, DeleteOriginal = true };
var qRe = Path.Combine(work, "queue-reabrir.json");
var eA = new Engine(sRe, m => Console.WriteLine("[A] " + m), _ => Task.FromResult<string?>(null)) { StableSeconds = 2, QueueFile = qRe };
eA.AddFiles(Directory.GetFiles(inRe)); // como o seletor de arquivos
using var stopA = new CancellationTokenSource();
var runA = Task.Run(() => eA.RunAsync(stopA.Token));
Job? jA = null;
var tA = DateTime.UtcNow.AddMinutes(1);
var g5SrcDir = gameDirs["G5.7z"];
var g5ReRemote = Path.Combine(ftpRoot, "reabrir", Path.GetFileName(g5SrcDir));
bool AnyComplete() => Directory.Exists(g5ReRemote) && Directory.GetFiles(g5SrcDir, "*", SearchOption.AllDirectories)
    .Any(f => new FileInfo(f).Length > 0 && new FileInfo(Path.Combine(g5ReRemote, Path.GetRelativePath(g5SrcDir, f))) is { Exists: true } r && r.Length == new FileInfo(f).Length);
// fecha assim que pelo menos 1 arquivo estiver inteiro no servidor e o envio ainda estiver rodando
while (DateTime.UtcNow < tA) { lock (eA.Lock) jA = eA.Jobs.FirstOrDefault(j => j.Stage == Stage.Enviando); if (jA != null && AnyComplete()) break; jA = null; await Task.Delay(20); }
var closedAt = jA?.Progress ?? 0;
if (jA != null) eA.Pause(jA); // fechar o app = matar o envio em andamento
stopA.Cancel(); await runA;
await Task.Delay(1500); // servidor termina de gravar o que já estava em trânsito
var g5Src = gameDirs["G5.7z"];
var reRemote = Path.Combine(ftpRoot, "reabrir", Path.GetFileName(g5Src));
var completeBefore = Directory.GetFiles(g5Src, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(g5Src, f).Replace('\\', '/'))
    .Where(r => new FileInfo(Path.Combine(reRemote, r)) is { Exists: true } fi && fi.Length == new FileInfo(Path.Combine(g5Src, r)).Length && fi.Length > 0).ToList();
var eB = new Engine(sRe, m => Console.WriteLine("[B] " + m), _ => Task.FromResult<string?>(null)) { StableSeconds = 2, QueueFile = qRe };
eB.Restore(); // app reaberto: nada foi adicionado de novo
var restored = await RunUntil(eB, j => j.Name == "G5" && j.Stage is Stage.Verificado or Stage.Erro, TimeSpan.FromMinutes(2));
var resent = completeBefore.Count(r => Lines(ftpLog, "STOR", "/reabrir/" + Path.GetFileName(g5Src) + "/" + r).Count > 1);
var reSame = SameFiles(g5Src, reRemote);
var reOk = jA != null && restored && completeBefore.Count > 0 && resent == 0 && reSame == Directory.GetFiles(g5Src, "*", SearchOption.AllDirectories).Length;
var reLine = reOk
    ? $"✅ fechou em {closedAt:0}% com {completeBefore.Count} arquivo(s) completos no PS5; ao reabrir a fila voltou sozinha, nenhum deles foi extraído/reenviado; hash confere"
    : $"❌ fechou={jA != null} ({closedAt:0}%), completos antes={completeBefore.Count}, fila restaurada={restored}, reenviados={resent}, hash {reSame}";
allOk &= resumeOk && appeOk && reOk && !pauseResult.StartsWith("❌") && !removeResult.StartsWith("❌");
extraRows.Add($"| Já no PS5, servidor sem APPE (igual ftpsrv) | {(resumeOk ? "✅ EBOOT.BIN completo nem foi extraído/reenviado; big.bin pela metade foi reenviado inteiro (STOR); hash confere" : "❌ FALHA: " + resumeLine)} |");
extraRows.Add($"| Já no PS5, servidor com APPE | {(appeOk ? "✅ big.bin pela metade: enviou só a metade que faltava (APPE); jogo+dec com hash conferido" : $"❌ FALHA: APPE {appeBytes} bytes, {appeSame} arquivos iguais")} |");
extraRows.Add($"| Fechar e reabrir o app no meio do envio | {reLine} |");
extraRows.Add($"| Pausar/retomar no meio do stream (G5) | {(pauseResult.StartsWith("❌") ? pauseResult : "✅ " + pauseResult + "; hash confere")} |");
extraRows.Add($"| Remover da fila (G6) | {removeResult} |");
}
ftp.Kill(true);

var sb = new StringBuilder();
sb.AppendLine("# Relatório E2E — PS5 Sender");
sb.AppendLine();
sb.AppendLine($"- Data: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
sb.AppendLine($"- Resultado geral: **{(allOk ? "PASSOU" : "FALHOU")}**  ({sw.Elapsed.TotalSeconds:0}s)");
sb.AppendLine($"- Servidor: pyftpdlib (imitando o ftpsrv: só os comandos dele; upload limitado a 40 MB/s por conexão) em 127.0.0.1:{port}, destino `{RemoteDir}`, {settings.Connections} conexões, apagar original = sim");
sb.AppendLine($"- Ferramentas: 7-Zip {FileVersionInfo.GetVersionInfo(sevenZip).ProductVersion} (embutido no app), Rar.exe {FileVersionInfo.GetVersionInfo(rar).ProductVersion} (só para gerar os testes)");
sb.AppendLine($"- Jogo falso: 6 arquivos (~24 MB, incompressíveis) dentro de 2 pastas casca; volumes de 5 MB");
sb.AppendLine();
sb.AppendLine("| Formato | Volumes | Esperou volume faltante | Estado final | SHA-256 iguais | Senha | Originais apagados | Resultado |");
sb.AppendLine("|---|---|---|---|---|---|---|---|");
rows.ForEach(r => sb.AppendLine(r));
sb.AppendLine();
sb.AppendLine("| Verificação extra | Resultado |");
sb.AppendLine("|---|---|");
sb.AppendLine($"| Testar conexão (credenciais certas) | {testMsg} |");
sb.AppendLine($"| Testar conexão (senha errada) | {badLogin} |");
extraRows.ForEach(r => sb.AppendLine(r));
if (!full) sb.AppendLine($"| Fases extras | — modo rápido ({string.Join(", ", only)}) |");
sb.AppendLine($"| Disco | extração em streaming (7z -so → FTP): nenhum arquivo extraído é gravado localmente |");
sb.AppendLine();
sb.AppendLine("Repetir: `dotnet run --project e2e` (na pasta PS5Sender). Requer Python com `pyftpdlib`.");
File.WriteAllText(Path.Combine(root, "e2e_report.md"), sb.ToString());
File.WriteAllLines(Path.Combine(root, "e2e", "last-run.log"), logLines);
lock (ftpLog) File.WriteAllLines(Path.Combine(root, "e2e", "last-ftp.log"), ftpLog);
Console.WriteLine(sb.ToString());
return allOk ? 0 : 1;

// ---------- helpers ----------
static string Sha(string f) { using var s = File.OpenRead(f); return Convert.ToHexString(SHA256.HashData(s)); }

static int SameFiles(string expected, string remote) => Directory.GetFiles(expected, "*", SearchOption.AllDirectories)
    .Count(f => Path.Combine(remote, Path.GetRelativePath(expected, f)) is var r && File.Exists(r) && Sha(f) == Sha(r));

// Roda o engine até alguma tarefa cumprir done (ou estourar o tempo).
static async Task<bool> RunUntil(Engine e, Func<Job, bool> done, TimeSpan timeout)
{
    using var stop = new CancellationTokenSource();
    var run = Task.Run(() => e.RunAsync(stop.Token));
    var end = DateTime.UtcNow + timeout;
    var ok = false;
    while (!ok && DateTime.UtcNow < end) { lock (e.Lock) ok = e.Jobs.Any(done); if (!ok) await Task.Delay(200); }
    stop.Cancel(); await run;
    return ok;
}

(Process, int, List<string>) StartFtp(string ftpRootDir, bool appe)
{
    var p = FreePort();
    var proc = Process.Start(new ProcessStartInfo("python", $"\"{Path.Combine(root, "e2e", "ftpserver.py")}\" {p} \"{ftpRootDir}\" {(appe ? "appe" : "noappe")}")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true })!;
    var log = new List<string>();
    proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (log) log.Add(e.Data); }; proc.BeginErrorReadLine();
    proc.OutputDataReceived += (_, e) => { if (e.Data != null) lock (log) log.Add(e.Data); }; proc.BeginOutputReadLine();
    for (var t = 0; ; t++)
    {
        try { using var tc = new TcpClient(); tc.Connect("127.0.0.1", p); break; }
        catch when (t < 50) { Thread.Sleep(200); }
    }
    return (proc, p, log);
}

static int FreePort() { var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); var p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }

static void Run(string exe, string cwd, params string[] args)
{
    var psi = new ProcessStartInfo(exe) { WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in args) psi.ArgumentList.Add(a);
    using var p = Process.Start(psi)!;
    var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEnd();
    p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception($"{Path.GetFileName(exe)} falhou ({p.ExitCode}): {e}{o.Result}");
}

async Task EnsureRar()
{
    if (File.Exists(rar)) return;
    // Rar.exe oficial, extraído do instalador do rarlab.com. Só para gerar os arquivos de teste.
    // 6.24 = última versão que ainda cria RAR4 com nomes antigos (-ma4 -vn).
    var setup = Path.Combine(work, "winrar-x64-624.exe");
    using (var http = new HttpClient()) await File.WriteAllBytesAsync(setup, await http.GetByteArrayAsync("https://www.rarlab.com/rar/winrar-x64-624.exe"));
    Run(sevenZip, work, "e", setup, "Rar.exe", "-o" + tools, "-y");
}

// Zip dividido estilo PKWARE/Info-ZIP (.z01, .z02 … + .zip), método "store". O 7-Zip só cria .zip.001.
static class SplitZip
{
    public static void Write(string srcRoot, string outBase, long vol)
    {
        var segs = new List<string>();
        FileStream? fs = null;
        long pos = 0;
        void Next() { fs?.Dispose(); var p = $"{outBase}.seg{segs.Count}"; segs.Add(p); fs = File.Create(p); pos = 0; }
        void Ensure(int n) { if (vol - pos < n) Next(); }
        void W(ReadOnlySpan<byte> b)
        {
            while (b.Length > 0)
            {
                if (pos == vol) Next();
                var k = (int)Math.Min(b.Length, vol - pos);
                fs!.Write(b[..k]); pos += k; b = b[k..];
            }
        }
        static byte[] U16(int v) => BitConverter.GetBytes((ushort)v);
        static byte[] U32(long v) => BitConverter.GetBytes((uint)v);

        Next();
        W(U32(0x08074b50)); // assinatura de arquivo dividido
        var cd = new MemoryStream();
        var files = Directory.GetFiles(srcRoot, "*", SearchOption.AllDirectories);
        foreach (var f in files)
        {
            var name = Encoding.UTF8.GetBytes(Path.GetRelativePath(srcRoot, f).Replace('\\', '/'));
            var data = File.ReadAllBytes(f);
            var crc = Crc32.HashToUInt32(data);
            Ensure(30 + name.Length);
            var disk = segs.Count - 1; var off = pos;
            W([.. U32(0x04034b50), .. U16(20), .. U16(0x0800), .. U16(0), .. U16(0), .. U16(0x21), .. U32(crc), .. U32(data.Length), .. U32(data.Length), .. U16(name.Length), .. U16(0), .. name]);
            W(data);
            cd.Write([.. U32(0x02014b50), .. U16(20), .. U16(20), .. U16(0x0800), .. U16(0), .. U16(0), .. U16(0x21), .. U32(crc), .. U32(data.Length), .. U32(data.Length), .. U16(name.Length), .. U16(0), .. U16(0), .. U16(disk), .. U16(0), .. U32(0), .. U32(off), .. name]);
        }
        Ensure((int)cd.Length + 22);
        var cdDisk = segs.Count - 1; var cdOff = pos;
        W(cd.ToArray());
        W([.. U32(0x06054b50), .. U16(cdDisk), .. U16(cdDisk), .. U16(files.Length), .. U16(files.Length), .. U32(cd.Length), .. U32(cdOff), .. U16(0)]);
        fs!.Dispose();
        for (var i = 0; i < segs.Count; i++) File.Move(segs[i], i == segs.Count - 1 ? outBase + ".zip" : $"{outBase}.z{i + 1:00}");
    }
}
