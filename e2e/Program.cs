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
using Ferry;

if (args.Length == 1 && args[0] == "--localization")
{
    LocalizationChecks.Run();
    return 0;
}
if (args.Length == 1 && args[0] == "--webhook")
{
    await WebhookChecks.RunAsync();
    return 0;
}

var root = AppContext.BaseDirectory;
while (!Directory.Exists(Path.Combine(root, "e2e")) || !Directory.Exists(Path.Combine(root, "app"))) root = Path.GetDirectoryName(root)!;
var tools = Path.Combine(root, "e2e", "tools");
// Windows: o 7z.exe embutido no app; Linux: o 7zz/7z do sistema (o mesmo que o servidor web usa)
if (OperatingSystem.IsWindows()) Archives.SevenZipPath = Path.Combine(root, "app", "tools", "7z.exe");
var sevenZip = Archives.SevenZip();
var rar = Path.Combine(tools, OperatingSystem.IsWindows() ? "Rar.exe" : "rar");
// Uso: dotnet run --project e2e            -> tudo (formatos + fases extras)
//      dotnet run --project e2e -- G2 G7   -> modo rápido: só esses casos, sem fases extras
var only = args.ToHashSet(StringComparer.OrdinalIgnoreCase);
var full = only.Count == 0;
var work = Path.Combine(Path.GetTempPath(), "ferry-e2e");
if (Directory.Exists(work)) Directory.Delete(work, true);
string Dir(string name) => Directory.CreateDirectory(Path.Combine(work, name)).FullName;
var input = Dir("input"); var dropped = Dir("dropped"); var ftpRoot = Dir("ftproot");
// Jogos falsos e arquivos compactados são determinísticos: ficam em cache entre rodadas.
// Mude GenVersion quando mexer no gerador.
const string GenVersion = "v4";
var cache = Path.Combine(Path.GetTempPath(), "ferry-e2e-cache-" + GenVersion);
var cached = File.Exists(Path.Combine(cache, "ok"));
if (!cached && Directory.Exists(cache)) Directory.Delete(cache, true);
var src = Directory.CreateDirectory(Path.Combine(cache, "src")).FullName;
var archives = Directory.CreateDirectory(Path.Combine(cache, "archives")).FullName;
const string RemoteDir = "/mnt/ext1/homebrew";
const long Vol = 5_000_000;
var sw = Stopwatch.StartNew();
// nada do E2E toca o settings.json / log.txt reais do usuário
Settings.FilePath = Path.Combine(work, "settings.json");
FileLog.FilePath = Path.Combine(work, "log.txt");
Secret.KeyFile = Path.Combine(work, "secret.key");

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
    void PutBytes(string rel, byte[] b) { var p = Path.Combine(g, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, b); }
    Put("EBOOT.BIN", 1_200_000);
    PutBytes("sce_sys/param.sfo", Sfo(("TITLE", $"Jogo Teste {i + 1}"), ("TITLE_ID", $"PPSA0{i + 1:0000}")));
    PutBytes("sce_sys/param.json", Encoding.UTF8.GetBytes($"{{\"titleId\":\"PPSA0{i + 1:0000}\"}}"));
    PutBytes("sce_sys/icon0.png", Png(1000 + i));
    Put("data/big.bin", 22_000_000);
    Put("data/sub pasta/ação çõ.dat", 777_777);
    Put("data/vazio.bin", 0);

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
            foreach (var (rel, size) in new[] { ("EBOOT.BIN", 900_000), ("data/sub pasta/ação çõ.dat", 12_345), ("sce_module/libnovo.prx", 50_000) })
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
// imagens .exfat (ShadowMount+): IMG1 solta, IMG2 dentro de um .part1.rar numa subpasta
var img1 = Path.Combine(src, "_exfat", "IMG1.exfat");
var img2 = Path.Combine(src, "IMG2", "Pasta Img", "IMG2.exfat");
if (!cached)
{
    foreach (var (p, seed, size) in new[] { (img1, 2001, 40_000_000), (img2, 2002, 12_000_000) })
    {
        Directory.CreateDirectory(Path.GetDirectoryName(p)!); var b = new byte[size]; new Random(seed).NextBytes(b); File.WriteAllBytes(p, b);
    }
    var img2Out = Directory.CreateDirectory(Path.Combine(archives, "IMG2.rar")).FullName;
    Run(rar, Path.Combine(src, "IMG2"), "a", "-m0", "-v5000000b", "-r", "-idq", Path.Combine(img2Out, "IMG2.rar"), "Pasta Img");
    var madeImg = Directory.GetFiles(img2Out).Select(Path.GetFileName).ToList();
    if (madeImg.Count < 2 || !madeImg.All(n => System.Text.RegularExpressions.Regex.IsMatch(n!, @"^IMG2\.part\d\.rar$")))
        throw new Exception($"Gerador de IMG2 produziu nomes inesperados: {string.Join(", ", madeImg)}");
}
if (!cached) File.WriteAllText(Path.Combine(cache, "ok"), "");
Console.WriteLine($"Arquivos de teste: {(cached ? "cache" : "gerados")} ({sw.Elapsed.TotalSeconds:0.0}s)");
if (!full) cases = cases.Where(c => only.Contains(Path.GetFileNameWithoutExtension(c.archive))).ToArray();

// ---------- servidor FTP ----------
// Principal imita o ftpsrv do PS5: sem APPE (parcial precisa ser reenviado inteiro).
var (ftp, port, ftpLog) = StartFtp(ftpRoot, appe: false);

var settings = new Settings { Host = "127.0.0.1", Port = port, User = "ps5", Password = "ps5pass", InputFolder = input, RemoteDir = RemoteDir, Connections = 4, DeleteOriginal = true,
    KnownPasswords = ["nao-e-esta", "senha123"] }; // G6 abre com a 2ª, sem diálogo
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
        settled = cases.Where(c => partCount[c.archive] > 1).All(c => engine.Jobs.Any(j => j.Name == Path.GetFileNameWithoutExtension(c.archive) && (j.Detail.Contains("altam") || j.Detail.Contains("faltando"))));
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
    // rar e zip dividido: o 7z diz qual volume falta e o card mostra "faltando <volume>"
    var held = Path.GetFileName(heldBack[c.archive])[(name.Length + 1)..];
    var namesMissing = name is "G2" or "G3" or "G4";
    waited[c.archive] = partCount[c.archive] == 1
        ? j == null ? "ok" : $"FALHA ({j.Stage})"
        : j?.Stage != Stage.AguardandoPartes ? $"FALHA ({j?.Stage.ToString() ?? "sem card"})"
        : namesMissing && j.Detail != "faltando " + held ? $"FALHA (detalhe \"{j.Detail}\", esperado \"faltando {held}\")"
        : namesMissing ? $"ok (\"{j.Detail}\")" : "ok";
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
var allOk = waited.Values.All(v => v.StartsWith("ok")) && testMsg.StartsWith("Conectado") && badLogin.StartsWith("rejeitou");
// o log do pyftpdlib no Windows usa "\" nos caminhos
List<string> Lines(List<string> log, string cmd, string pathPart) { lock (log) return log.Where(l => l.Contains(cmd + " ") && l.Replace('\\', '/').Contains(pathPart)).ToList(); }
// Publicação atômica: param.json/param.sfo só ganham o nome final (RNTO) depois do último STOR/APPE do jogo.
string PublishOrder(List<string> log, string game)
{
    List<string> all; lock (log) all = [.. log.Select(l => l.Replace('\\', '/'))];
    var lastPut = all.FindLastIndex(l => (l.Contains("STOR ") || l.Contains("APPE ")) && l.Contains("/" + game + "/"));
    var renames = new[] { "param.json", "param.sfo" }.Select(f => all.FindIndex(l => l.Contains("RNTO ") && l.Contains("/" + game + "/sce_sys/" + f))).ToList();
    return renames.All(r => r > lastPut && lastPut >= 0) ? "ok" : $"FALHA (último STOR/APPE na linha {lastPut}, RNTO param.json/sfo nas linhas {string.Join("/", renames)})";
}
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
    var pwOk = passwordAsked.Contains(name) ? $"diálogo aberto {passwordAsked.Count(n => n == name)}x (esperado 0)" : c.pw == null ? "n/a" : "senha conhecida, sem diálogo";
    var gi = int.Parse(name[1..]); // G2 → Jogo Teste 2 (no modo rápido a lista é filtrada, a posição não serve)
    var coverOk = j != null && j.Title == $"Jogo Teste {gi}" && j.TitleId == $"PPSA0{gi:0000}" && j.Icon is { } icon && icon.SequenceEqual(File.ReadAllBytes(Path.Combine(g, "sce_sys", "icon0.png")));
    var cover = coverOk ? $"{j!.TitleId} · {j.Title} · capa ok" : $"FALHA (\"{j?.Title}\" / \"{j?.TitleId}\" / capa {j?.Icon?.Length ?? 0} bytes)";
    var order = PublishOrder(ftpLog, Path.GetFileName(g));
    var ok = j?.Stage == Stage.Verificado && okHashes == files.Length && extra == 0 && origDeleted && waited[c.archive].StartsWith("ok") && !pwOk.StartsWith("diálogo") && coverOk && order == "ok";
    allOk &= ok;
    rows.Add($"| {c.format} | {partCount[c.archive]} (`{Path.GetFileName(heldBack[c.archive])}` chegou por último) | {waited[c.archive]} | {j?.StageText ?? "—"} | {okHashes}/{files.Length}{(extra != 0 ? $" (extras: {extra})" : "")} | {pwOk} | {cover} | {order} | {(origDeleted ? "sim" : "não")} | {(ok ? "✅ OK" : "❌ FALHA" + (j?.Detail is { Length: > 0 } d ? ": " + d : ""))} |");
}
// pyftpdlib registra "STOR/APPE <caminho> completed=1 bytes=N".
static long Bytes(IEnumerable<string> lines) => lines.Sum(l => long.Parse(System.Text.RegularExpressions.Regex.Match(l, @"bytes=(\d+)").Groups[1].Value));
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

// ---------- servidor COM APPE (ftpsrv novo, com SELF ligado por padrão) ----------
// big.bin pela metade, começado por ESTE app (registrado na fila) → continua com APPE.
// EBOOT.BIN menor e diferente (outra versão, não é nosso) → STOR inteiro. icon0.png maior → STOR, fica do tamanho certo.
var appeRoot = Dir("ftproot-appe");
var (ftp2, port2, ftp2Log) = StartFtp(appeRoot, appe: true);
var inAppe = Dir("input-appe");
File.Copy(Path.Combine(archives, "G7.rar", "G7.rar"), Path.Combine(inAppe, "G7.rar"));
var g7Name = Path.GetFileName(gameDirs["G7.rar"]);
var g7Remote = Path.Combine(appeRoot, "mnt", "ext1", "homebrew", g7Name);
byte[] Local7(string rel) => File.ReadAllBytes(Path.Combine(gameDirs["G7.rar"], rel));
void Remote7(string rel, byte[] b) { var p = Path.Combine(g7Remote, rel); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, b); }
var g7BigLocal = Local7("data/big.bin");
Remote7("data/big.bin", g7BigLocal[..(g7BigLocal.Length / 2)]);
var otherVersion = new byte[Local7("EBOOT.BIN").Length / 2]; new Random(7).NextBytes(otherVersion);
Remote7("EBOOT.BIN", otherVersion);
var bigger = new byte[Local7("sce_sys/icon0.png").Length * 2]; new Random(8).NextBytes(bigger);
Remote7("sce_sys/icon0.png", bigger);
var qAppe = Path.Combine(work, "queue-appe.json");
File.WriteAllText(qAppe, System.Text.Json.JsonSerializer.Serialize(new
{
    Dropped = Array.Empty<string>(), Removed = Array.Empty<string>(), Passwords = new Dictionary<string, string>(),
    Started = new Dictionary<string, Dictionary<string, long>> { [Path.Combine(inAppe, "G7.rar")] = new() { [$"{RemoteDir}/{g7Name}/data/big.bin"] = g7BigLocal.Length } },
}));
var sAppe = new Settings { Host = "127.0.0.1", Port = port2, User = "ps5", Password = "ps5pass", InputFolder = inAppe, RemoteDir = RemoteDir, Connections = 4 };
var eAppe = new Engine(sAppe, m => Console.WriteLine("[appe] " + m), _ => Task.FromResult<string?>(null)) { StableSeconds = 2, QueueFile = qAppe };
eAppe.Restore();
await RunUntil(eAppe, j => j.Name == "G7" && j.Stage is Stage.Verificado or Stage.Erro, TimeSpan.FromMinutes(2));
var appeBytes = Bytes(Lines(ftp2Log, "APPE", g7Name + "/data/big.bin"));
var ebootStor7 = Lines(ftp2Log, "STOR", g7Name + "/EBOOT.BIN");
var g7Files = Directory.GetFiles(gameDirs["G7.rar"], "*", SearchOption.AllDirectories).Length;
var appeSame = SameFiles(gameDirs["G7.rar"], g7Remote);
var leftovers = Directory.GetFiles(g7Remote, "*" + Engine.PartSuffix, SearchOption.AllDirectories).Length;
var order7 = PublishOrder(ftp2Log, g7Name);
var appeOk = appeBytes == g7BigLocal.Length - g7BigLocal.Length / 2 && Lines(ftp2Log, "APPE", g7Name + "/EBOOT.BIN").Count == 0
    && Bytes(ebootStor7) == Local7("EBOOT.BIN").Length && appeSame == g7Files && leftovers == 0 && order7 == "ok";
var appeLine = appeOk
    ? $"✅ big.bin nosso pela metade: só a metade que faltava (APPE, {appeBytes} bytes); EBOOT.BIN menor de outra versão: STOR inteiro; icon0.png maior: ficou do tamanho certo; SELF desligado (SIZE real); {appeSame}/{g7Files} hashes; param.json/sfo renomeados só depois do último envio"
    : $"❌ FALHA: APPE big.bin {appeBytes} bytes, EBOOT.BIN STOR {Bytes(ebootStor7)} bytes, {appeSame}/{g7Files} iguais, sobras {leftovers}, ordem {order7}";

// Jogo já publicado no PS5: avisa em vez de mandar por cima; "Tentar de novo" = reenviar mesmo assim.
var eInst = new Engine(sAppe, m => Console.WriteLine("[inst] " + m), _ => Task.FromResult<string?>(null)) { StableSeconds = 2, QueueFile = Path.Combine(work, "queue-inst.json") };
await RunUntil(eInst, j => j.Name == "G7" && j.Stage is Stage.Verificado or Stage.Erro, TimeSpan.FromMinutes(1));
Job? jInst; lock (eInst.Lock) jInst = eInst.Jobs.FirstOrDefault(j => j.Name == "G7");
var warnedInst = jInst is { Stage: Stage.Erro, Installed: true } && jInst.Detail.StartsWith("Jogo já instalado");
if (jInst != null) eInst.Retry(jInst);
var instOk = warnedInst && await RunUntil(eInst, j => j.Name == "G7" && j.Stage == Stage.Verificado, TimeSpan.FromMinutes(1)) && SameFiles(gameDirs["G7.rar"], g7Remote) == g7Files;
ftp2.Kill(true);

// Log persistente: comando e resposta de cada STOR/APPE e SIZE
var fileLog = File.Exists(FileLog.FilePath) ? File.ReadAllText(FileLog.FilePath) : "";
var logOk = fileLog.Contains($"APPE {RemoteDir}/{g7Name}/data/big.bin → 226") && fileLog.Contains($"STOR {RemoteDir}/{g7Name}/EBOOT.BIN → 226")
    && fileLog.Contains($"SIZE {RemoteDir}/{g7Name}/EBOOT.BIN → 213 {Local7("EBOOT.BIN").Length}");

// ---------- imagem .exfat (ShadowMount+): servidor COM APPE, destino ImageDir ≠ RemoteDir ----------
var exRoot = Dir("ftproot-exfat");
var (ftp3, port3, ftp3Log) = StartFtp(exRoot, appe: true, mbps: 10); // 10 MB/s: dá tempo de pausar o IMG1 (40 MB) no meio
var inEx = Dir("input-exfat"); var img1Drop = Path.Combine(Dir("dropped-exfat"), "IMG1.exfat");
File.Copy(img1, img1Drop);
foreach (var f in Directory.GetFiles(Path.Combine(archives, "IMG2.rar"))) File.Copy(f, Path.Combine(inEx, Path.GetFileName(f)));
var sEx = new Settings { Host = "127.0.0.1", Port = port3, User = "ps5", Password = "ps5pass", RemoteDir = RemoteDir, ImageDir = "/data/homebrew", Connections = 4, DeleteOriginal = false, InputFolder = inEx };
var imgDir = Path.Combine(exRoot, "data", "homebrew");
var part1 = Path.Combine(imgDir, "IMG1.exfat" + Engine.PartSuffix); var part2 = Path.Combine(imgDir, "IMG2.exfat" + Engine.PartSuffix);
var img1Bytes = File.ReadAllBytes(img1); var img2Len = new FileInfo(img2).Length;
List<string> After(int m) { lock (ftp3Log) return ftp3Log.Skip(m).ToList(); }
int Mark() { lock (ftp3Log) return ftp3Log.Count; }
Job? ExJob(Engine e, string n) { lock (e.Lock) return e.Jobs.FirstOrDefault(x => x.Name == n); }
bool ExHash(string n, string local) { var r = Path.Combine(imgDir, n); return File.Exists(r) && Sha(r) == Sha(local); }
string[] ImgFiles() => Directory.Exists(imgDir) ? Directory.GetFiles(imgDir).Select(file => Path.GetFileName(file)!).Order().ToArray() : [];
int ExLeft() => Directory.GetFiles(exRoot, "*" + Engine.PartSuffix, SearchOption.AllDirectories).Length;
// RNTO para o nome final só depois do último STOR/APPE da imagem
string ImgOrder(string name)
{
    var all = After(0).Select(l => l.Replace('\\', '/')).ToList();
    var lastPut = all.FindLastIndex(l => (l.Contains("STOR ") || l.Contains("APPE ")) && l.Contains(name + ".exfat"));
    var rnto = all.FindIndex(l => l.Contains("RNTO ") && l.Contains("/data/homebrew/" + name + ".exfat"));
    return lastPut >= 0 && rnto > lastPut ? "ok" : $"FALHA ({name}: último STOR/APPE na linha {lastPut}, RNTO na {rnto})";
}

// Passo I: envio novo (IMG1 arrastado, IMG2 pela pasta monitorada) + pausar/retomar no meio do IMG1
var eEx = new Engine(sEx, m => Console.WriteLine("[exfat] " + m), _ => Task.FromResult<string?>(null)) { StableSeconds = 2, QueueFile = Path.Combine(work, "queue-exfat.json") };
eEx.AddFiles([img1Drop]);
using var stopEx = new CancellationTokenSource();
var runEx = Task.Run(() => eEx.RunAsync(stopEx.Token));
var exPaused = false;
for (var exEnd = DateTime.UtcNow.AddMinutes(2); DateTime.UtcNow < exEnd; await Task.Delay(20))
{
    Job? ex1;
    lock (eEx.Lock)
    {
        if (eEx.Jobs.Count == 2 && eEx.Jobs.All(x => x.Stage is Stage.Verificado or Stage.Erro)) break;
        ex1 = eEx.Jobs.FirstOrDefault(x => x.Name == "IMG1");
    }
    // Progress do card só atualiza 4x/s (e o loopback é rápido): o parcial no servidor, ainda incompleto, é quem diz que está no meio
    if (!exPaused && ex1 is { Stage: Stage.Enviando, Progress: < 70 } && File.Exists(part1) && new FileInfo(part1).Length is > 0 and var pl && pl < img1Bytes.Length)
    {
        exPaused = true; eEx.Pause(ex1); await Task.Delay(1500); eEx.Resume(ex1);
    }
}
stopEx.Cancel(); await runEx;
var exA1 = ExJob(eEx, "IMG1"); var exA2 = ExJob(eEx, "IMG2");
var exAppe1 = Lines(ftp3Log, "APPE", "IMG1.exfat" + Engine.PartSuffix).Count;
var exMnt = Path.Combine(exRoot, "mnt");
var exNoMnt = !Directory.Exists(exMnt) || Directory.GetFiles(exMnt, "*", SearchOption.AllDirectories).Length == 0;
var exOrder = ImgOrder("IMG1") == "ok" && ImgOrder("IMG2") == "ok" ? "ok" : ImgOrder("IMG1") + " " + ImgOrder("IMG2");
var ex1Ok = exPaused && exA1 is { Stage: Stage.Verificado, Title: "", Icon: null } && exA2 is { Stage: Stage.Verificado, Title: "", Icon: null }
    && ExHash("IMG1.exfat", img1) && ExHash("IMG2.exfat", img2) && ExLeft() == 0 && ImgFiles().SequenceEqual(new[] { "IMG1.exfat", "IMG2.exfat" })
    && exAppe1 >= 1 && exNoMnt && exOrder == "ok";
var ex1Line = ex1Ok
    ? $"✅ IMG1 arrastado e IMG2 (dentro do .part1.rar) enviados para ImageDir com o nome do arquivo, sem capa; hash confere; pausou/retomou e continuou com APPE ({exAppe1}x); RNTO para o nome final só depois do último envio; sem sobra .ferry-part; nada em {RemoteDir}"
    : $"❌ FALHA: pausou={exPaused}, IMG1 {exA1?.Stage} \"{exA1?.Detail}\" (título \"{exA1?.Title}\"), IMG2 {exA2?.Stage} \"{exA2?.Detail}\" (título \"{exA2?.Title}\"), arquivos [{string.Join(", ", ImgFiles())}], sobras {ExLeft()}, APPE IMG1 {exAppe1}x, mnt vazio={exNoMnt}, ordem {exOrder}";

// Passo II: reabrir com parcial nosso (registrado na fila → APPE) e parcial de outra versão (STOR inteiro)
File.Delete(Path.Combine(imgDir, "IMG1.exfat")); File.Delete(Path.Combine(imgDir, "IMG2.exfat"));
File.WriteAllBytes(part1, img1Bytes[..(img1Bytes.Length / 2)]);
var exOther = new byte[6_000_000]; new Random(9).NextBytes(exOther); File.WriteAllBytes(part2, exOther);
var qEx2 = Path.Combine(work, "queue-exfat2.json");
File.WriteAllText(qEx2, System.Text.Json.JsonSerializer.Serialize(new
{
    Dropped = new[] { img1Drop }, Removed = Array.Empty<string>(), Passwords = new Dictionary<string, string>(),
    Started = new Dictionary<string, Dictionary<string, long>> { [img1Drop] = new() { [$"/data/homebrew/IMG1.exfat{Engine.PartSuffix}"] = img1Bytes.Length } },
}));
var mark2 = Mark();
var eEx2 = new Engine(sEx, m => Console.WriteLine("[exfat2] " + m), _ => Task.FromResult<string?>(null)) { StableSeconds = 2, QueueFile = qEx2 };
eEx2.Restore();
await RunUntil(eEx2, _ => eEx2.Jobs.Count(x => x.Stage is Stage.Verificado or Stage.Erro) == 2, TimeSpan.FromMinutes(2));
var log2 = After(mark2);
var exB1 = ExJob(eEx2, "IMG1"); var exB2 = ExJob(eEx2, "IMG2");
var exAppeBytes = Bytes(Lines(log2, "APPE", "IMG1.exfat" + Engine.PartSuffix));
var exAppe2 = Lines(log2, "APPE", "IMG2.exfat").Count;
var exStor2 = Bytes(Lines(log2, "STOR", "IMG2.exfat" + Engine.PartSuffix));
var ex2Ok = exB1?.Stage == Stage.Verificado && exB2?.Stage == Stage.Verificado && exAppeBytes == img1Bytes.Length - img1Bytes.Length / 2 && exAppe2 == 0 && exStor2 == img2Len
    && ExHash("IMG1.exfat", img1) && ExHash("IMG2.exfat", img2) && ExLeft() == 0;
var ex2Line = ex2Ok
    ? $"✅ parcial nosso do IMG1 (registrado na fila): só a metade que faltava (APPE, {exAppeBytes} bytes); parcial de outra versão do IMG2: STOR inteiro ({exStor2} bytes), sem APPE; hashes conferem, sem sobra .ferry-part"
    : $"❌ FALHA: IMG1 {exB1?.Stage} \"{exB1?.Detail}\", IMG2 {exB2?.Stage} \"{exB2?.Detail}\", APPE IMG1 {exAppeBytes} bytes (esperado {img1Bytes.Length - img1Bytes.Length / 2}), APPE IMG2 {exAppe2}x, STOR IMG2 {exStor2} bytes (esperado {img2Len}), sobras {ExLeft()}";

// Passo III: imagem final já existe no PS5 → avisa; "Tentar de novo" reenvia por cima (STOR inteiro)
var eEx3 = new Engine(sEx, m => Console.WriteLine("[exfat3] " + m), _ => Task.FromResult<string?>(null)) { StableSeconds = 2, QueueFile = Path.Combine(work, "queue-exfat3.json") };
eEx3.AddFiles([img1Drop]);
var mark3 = Mark();
await RunUntil(eEx3, _ => eEx3.Jobs.Count(x => x.Stage == Stage.Erro) == 2, TimeSpan.FromMinutes(1));
var exC1 = ExJob(eEx3, "IMG1"); var exC2 = ExJob(eEx3, "IMG2");
var exWarned = exC1 is { Stage: Stage.Erro, Installed: true } && exC1.Detail.StartsWith("Jogo já instalado") && exC2 is { Stage: Stage.Erro, Installed: true } && exC2.Detail.StartsWith("Jogo já instalado");
var exSent3 = After(mark3).Count(l => l.Contains("STOR ") || l.Contains("APPE "));
if (exC1 != null) eEx3.Retry(exC1);
if (exC2 != null) eEx3.Retry(exC2);
var exRetried = await RunUntil(eEx3, _ => eEx3.Jobs.Count(x => x.Stage == Stage.Verificado) == 2, TimeSpan.FromMinutes(1));
var log3 = After(mark3);
var exStor31 = Bytes(Lines(log3, "STOR", "IMG1.exfat" + Engine.PartSuffix)); var exStor32 = Bytes(Lines(log3, "STOR", "IMG2.exfat" + Engine.PartSuffix));
var ex3Ok = exWarned && exSent3 == 0 && exRetried && exStor31 == img1Bytes.Length && exStor32 == img2Len && ExHash("IMG1.exfat", img1) && ExHash("IMG2.exfat", img2) && ExLeft() == 0;
var ex3Line = ex3Ok
    ? "✅ IMG1 e IMG2 já no PS5: avisou \"Jogo já instalado…\" sem enviar nada; \"Tentar de novo\" reenviou por cima (STOR inteiro) e conferiu o hash"
    : $"❌ FALHA: avisou={exWarned} (IMG1 {exC1?.Stage} \"{exC1?.Detail}\", IMG2 {exC2?.Stage} \"{exC2?.Detail}\"), envios antes do Retry={exSent3}, reenviou={exRetried}, STOR IMG1 {exStor31} / IMG2 {exStor32} bytes, sobras {ExLeft()}";
ftp3.Kill(true);

// ---------- Transferir agora: passa na frente do que está enviando; o preemptado volta para a fila e continua com APPE ----------
var agRoot = Dir("ftproot-agora");
var (ftp4, port4, ftp4Log) = StartFtp(agRoot, appe: true, mbps: 10); // 10 MB/s: ImgA (80 MB) ainda enviando quando o IMG2 fica pronto
var inAg = Dir("input-agora"); var dropAg = Dir("dropped-agora");
var agA = Path.Combine(inAg, "ImgA.exfat"); // 80 MB (~8 s a 10 MB/s): dá tempo de B ficar pronto e de pegar A no meio
var agABytes = new byte[80_000_000]; new Random(3001).NextBytes(agABytes); File.WriteAllBytes(agA, agABytes);
foreach (var f in Directory.GetFiles(Path.Combine(archives, "IMG2.rar"))) File.Copy(f, Path.Combine(dropAg, Path.GetFileName(f)));
var sAg = new Settings { Host = "127.0.0.1", Port = port4, User = "ps5", Password = "ps5pass", RemoteDir = RemoteDir, ImageDir = "/data/homebrew", Connections = 4, DeleteOriginal = false, InputFolder = inAg };
var agDone = new List<string>();
var eAg = new Engine(sAg, m => Console.WriteLine("[agora] " + m), _ => Task.FromResult<string?>(null)) { StableSeconds = 2, QueueFile = Path.Combine(work, "queue-agora.json"), Done = j => { lock (agDone) agDone.Add($"{j.Name}:{j.Stage}"); } };
eAg.AddFiles(Directory.GetFiles(dropAg)); // B = IMG2 (12 MB); A (ImgA) vem da pasta monitorada e entra primeiro na fila
using var stopAg = new CancellationTokenSource();
var runAg = Task.Run(() => eAg.RunAsync(stopAg.Token));
var agImg = Path.Combine(agRoot, "data", "homebrew");
var agPartA = Path.Combine(agImg, "ImgA.exfat" + Engine.PartSuffix);
long agCut = 0; var agStageA = Stage.Erro;
for (var agEnd = DateTime.UtcNow.AddMinutes(2); DateTime.UtcNow < agEnd; await Task.Delay(20))
{
    var a = ExJob(eAg, "ImgA"); var b = ExJob(eAg, "IMG2");
    if (a is { Stage: Stage.Enviando } && b is { CanSendNow: true } && File.Exists(agPartA) && new FileInfo(agPartA).Length is > 0 and var pl && pl < agABytes.Length / 2)
    {
        agCut = pl; eAg.SendNow(b); agStageA = a.Stage; break;
    }
}
for (var agEnd = DateTime.UtcNow.AddMinutes(2); DateTime.UtcNow < agEnd; await Task.Delay(100)) lock (agDone) if (agDone.Count == 2) break;
stopAg.Cancel(); await runAg;
ftp4.Kill(true);
var agAppeA = Bytes(Lines(ftp4Log, "APPE", "ImgA.exfat" + Engine.PartSuffix)); var agStorA = Bytes(Lines(ftp4Log, "STOR", "ImgA.exfat" + Engine.PartSuffix));
var agStorB = Lines(ftp4Log, "STOR", "IMG2.exfat" + Engine.PartSuffix); var agAppeB = Lines(ftp4Log, "APPE", "IMG2.exfat" + Engine.PartSuffix).Count;
string agOrder; lock (agDone) agOrder = string.Join(" → ", agDone);
var agSame = File.Exists(Path.Combine(agImg, "ImgA.exfat")) && Sha(Path.Combine(agImg, "ImgA.exfat")) == Sha(agA) && File.Exists(Path.Combine(agImg, "IMG2.exfat")) && Sha(Path.Combine(agImg, "IMG2.exfat")) == Sha(img2);
var agOk = agCut > 0 && agStageA == Stage.NaFila && agOrder == "IMG2:Verificado → ImgA:Verificado" && agSame
    && agAppeA > 0 && agAppeA <= agABytes.Length - agCut && agStorA + agAppeA == agABytes.Length // A: nada reenviado, só o que faltava
    && agStorB.Count == 1 && Bytes(agStorB) == img2Len && agAppeB == 0 // B: enviado uma vez, inteiro
    && Directory.GetFiles(agRoot, "*" + Engine.PartSuffix, SearchOption.AllDirectories).Length == 0;
var agLine = agOk
    ? $"✅ com ImgA (80 MB) em {agCut * 100 / agABytes.Length}% enviando, \"Transferir agora\" no IMG2: ImgA voltou para a fila (não pausou), IMG2 ficou Verificado primeiro, depois ImgA continuou só com o que faltava (STOR {agStorA} + APPE {agAppeA} bytes = {agABytes.Length}); IMG2 enviado uma vez; hashes conferem"
    : $"❌ FALHA: cortou={agCut} bytes, ImgA logo depois {agStageA}, ordem [{agOrder}], STOR ImgA {agStorA} + APPE {agAppeA} (total {agABytes.Length}), STOR IMG2 {agStorB.Count}x/{Bytes(agStorB)} bytes (esperado {img2Len}), APPE IMG2 {agAppeB}x, hashes={agSame}";

// ---------- fechar e reabrir o app no meio do envio (G6: senha aprendida no diálogo e lembrada ao reabrir) ----------
var inRe = Dir("reabrir");
foreach (var f in Directory.GetFiles(Path.Combine(archives, "G6.7z"))) File.Copy(f, Path.Combine(inRe, Path.GetFileName(f)));
var sRe = new Settings { Host = "127.0.0.1", Port = port, User = "ps5", Password = "ps5pass", InputFolder = "", RemoteDir = "/reabrir", Connections = 4, DeleteOriginal = true };
var qRe = Path.Combine(work, "queue-reabrir.json");
var askedA = 0; var askedB = 0;
// 1ª digitada errada: o app tem que perceber e pedir de novo
var eA = new Engine(sRe, m => Console.WriteLine("[A] " + m), _ => Task.FromResult<string?>(++askedA == 1 ? "senha-errada" : "senha123")) { StableSeconds = 2, QueueFile = qRe };
eA.AddFiles(Directory.GetFiles(inRe)); // como o seletor de arquivos
using var stopA = new CancellationTokenSource();
var runA = Task.Run(() => eA.RunAsync(stopA.Token));
Job? jA = null;
var tA = DateTime.UtcNow.AddMinutes(1);
var g5SrcDir = gameDirs["G6.7z"];
var g5ReRemote = Path.Combine(ftpRoot, "reabrir", Path.GetFileName(g5SrcDir));
bool AnyComplete() => Directory.Exists(g5ReRemote) && Directory.GetFiles(g5SrcDir, "*", SearchOption.AllDirectories)
    .Any(f => new FileInfo(f).Length > 0 && new FileInfo(Path.Combine(g5ReRemote, Path.GetRelativePath(g5SrcDir, f))) is { Exists: true } r && r.Length == new FileInfo(f).Length);
// fecha assim que pelo menos 1 arquivo estiver inteiro no servidor e o envio ainda estiver rodando
while (DateTime.UtcNow < tA) { lock (eA.Lock) jA = eA.Jobs.FirstOrDefault(j => j.Stage == Stage.Enviando); if (jA != null && AnyComplete()) break; jA = null; await Task.Delay(20); }
var closedAt = jA?.Progress ?? 0;
if (jA != null) eA.Pause(jA); // fechar o app = matar o envio em andamento
stopA.Cancel(); await runA;
await Task.Delay(1500); // servidor termina de gravar o que já estava em trânsito
var g5Src = gameDirs["G6.7z"];
var reRemote = Path.Combine(ftpRoot, "reabrir", Path.GetFileName(g5Src));
var completeBefore = Directory.GetFiles(g5Src, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(g5Src, f).Replace('\\', '/'))
    .Where(r => new FileInfo(Path.Combine(reRemote, r)) is { Exists: true } fi && fi.Length == new FileInfo(Path.Combine(g5Src, r)).Length && fi.Length > 0).ToList();
var learned = sRe.KnownPasswords.LastOrDefault() == "senha123"
    && System.Text.Json.JsonSerializer.Deserialize<Settings>(File.ReadAllText(Settings.FilePath))!.KnownPasswords.Contains("senha123");
var queueHidesPw = !File.ReadAllText(qRe).Contains("senha123"); // cifrada (DPAPI / AES-GCM), não texto puro
// Salvar atômico: um .tmp pela metade (queda no meio da gravação) não estraga a fila
File.WriteAllText(qRe + ".tmp", "{\"Dropped\":[\"meio escr");
// app reaberto: nada foi adicionado de novo e sem senhas conhecidas: só a senha lembrada da fila pode abrir
var sRe2 = new Settings { Host = "127.0.0.1", Port = port, User = "ps5", Password = "ps5pass", InputFolder = "", RemoteDir = "/reabrir", Connections = 4, DeleteOriginal = true };
var eB = new Engine(sRe2, m => Console.WriteLine("[B] " + m), _ => { askedB++; return Task.FromResult<string?>(null); }) { StableSeconds = 2, QueueFile = qRe };
eB.Restore();
var restored = await RunUntil(eB, j => j.Name == "G6" && j.Stage is Stage.Verificado or Stage.Erro, TimeSpan.FromMinutes(2));
var resent = completeBefore.Count(r => Lines(ftpLog, "STOR", "/reabrir/" + Path.GetFileName(g5Src) + "/" + r).Count > 1);
var reSame = SameFiles(g5Src, reRemote);
var reOk = jA != null && restored && completeBefore.Count > 0 && resent == 0 && reSame == Directory.GetFiles(g5Src, "*", SearchOption.AllDirectories).Length;
var reLine = reOk
    ? $"✅ fechou em {closedAt:0}% com {completeBefore.Count} arquivo(s) completos no PS5; ao reabrir a fila voltou sozinha, nenhum deles foi extraído/reenviado; hash confere"
    : $"❌ fechou={jA != null} ({closedAt:0}%), completos antes={completeBefore.Count}, fila restaurada={restored}, reenviados={resent}, hash {reSame}";
var pwOkRe = askedA == 2 && learned && askedB == 0 && queueHidesPw;
var atomicOk = restored && !File.Exists(qRe + ".tmp") && !File.Exists(Settings.FilePath + ".tmp")
    && System.Text.Json.JsonDocument.Parse(File.ReadAllText(qRe)) != null;
allOk &= resumeOk && appeOk && instOk && logOk && reOk && pwOkRe && atomicOk && !pauseResult.StartsWith("❌") && !removeResult.StartsWith("❌");
extraRows.Add($"| Já no PS5, servidor sem APPE (igual ftpsrv antigo) | {(resumeOk ? "✅ EBOOT.BIN completo nem foi extraído/reenviado; big.bin pela metade (não começado por este app) foi reenviado inteiro (STOR); hash confere" : "❌ FALHA: " + resumeLine)} |");
extraRows.Add($"| Já no PS5, servidor com APPE e SELF (ftpsrv novo) | {appeLine} |");
extraRows.Add($"| Jogo já instalado no PS5 | {(instOk ? "✅ avisou \"Jogo já instalado…\" sem enviar; \"Tentar de novo\" reenviou por cima e conferiu" : $"❌ FALHA: avisou={warnedInst} ({jInst?.Stage} {jInst?.Detail})")} |");
extraRows.Add($"| Log persistente (log.txt) | {(logOk ? "✅ comando e resposta de STOR/APPE/SIZE gravados" : "❌ FALHA: faltam linhas de STOR/APPE/SIZE em " + FileLog.FilePath)} |");
extraRows.Add($"| Fechar e reabrir o app no meio do envio (G6) | {reLine} |");
extraRows.Add($"| Senha aprendida e lembrada (G6) | {(pwOkRe ? "✅ diálogo 2x (1ª errada), senha entrou no fim das senhas conhecidas; ao reabrir sem senhas conhecidas abriu com a senha lembrada (cifrada na fila), sem diálogo" : $"❌ FALHA: diálogo antes {askedA}x (esperado 2), aprendida={learned}, diálogo ao reabrir {askedB}x (esperado 0), fila sem texto puro={queueHidesPw}")} |");
extraRows.Add($"| Salvar atômico | {(atomicOk ? "✅ .tmp pela metade na fila não impediu reabrir; settings.json e queue.json sem sobra de .tmp e válidos" : "❌ FALHA")} |");
extraRows.Add($"| Pausar/retomar no meio do stream (G5) | {(pauseResult.StartsWith("❌") ? pauseResult : "✅ " + pauseResult + "; hash confere")} |");
extraRows.Add($"| Remover da fila (G6) | {removeResult} |");
// Migração de dados PS5Sender → Ferry: copia, não apaga a pasta antiga, não sobrescreve na 2ª chamada
var migOld = Dir("migracao-antiga"); var migNew = Path.Combine(work, "migracao-nova");
var migFiles = new Dictionary<string, string> { ["settings.json"] = "{\"Host\":\"1.2.3.4\"}", ["queue.json"] = "{\"Dropped\":[]}", ["log.txt"] = "linha do log antigo\n" };
foreach (var (n, c) in migFiles) File.WriteAllText(Path.Combine(migOld, n), c);
Settings.Migrate(migOld, migNew);
var migCopied = migFiles.All(f => File.Exists(Path.Combine(migNew, f.Key)) && File.ReadAllText(Path.Combine(migNew, f.Key)) == f.Value);
var migKept = migFiles.All(f => File.ReadAllText(Path.Combine(migOld, f.Key)) == f.Value);
File.WriteAllText(Path.Combine(migNew, "settings.json"), "{\"Host\":\"alterado\"}");
Settings.Migrate(migOld, migNew);
var migNoOverwrite = File.ReadAllText(Path.Combine(migNew, "settings.json")) == "{\"Host\":\"alterado\"}";
var migOk = migCopied && migKept && migNoOverwrite;
allOk &= migOk;
extraRows.Add($"| Migração de dados PS5Sender → Ferry | {(migOk ? "✅ settings.json, queue.json e log.txt copiados com o mesmo conteúdo; pasta antiga intacta; 2ª chamada não sobrescreveu o settings.json alterado" : $"❌ FALHA: copiou={migCopied}, antiga intacta={migKept}, sem sobrescrever={migNoOverwrite}")} |");
var exfatOk = ex1Ok && ex2Ok && ex3Ok;
allOk &= exfatOk && agOk;
extraRows.Add($"| Imagem .exfat solta e dentro de .part1.rar (ShadowMount+) | {ex1Line} |");
extraRows.Add($"| Imagem .exfat: reabrir com parcial nosso (APPE) e parcial de outra versão (STOR inteiro) | {ex2Line} |");
extraRows.Add($"| Imagem .exfat já no PS5: aviso + Tentar de novo | {ex3Line} |");
extraRows.Add($"| Transferir agora | {agLine} |");
}
ftp.Kill(true);

var sb = new StringBuilder();
sb.AppendLine("# Relatório E2E — Ferry");
sb.AppendLine();
sb.AppendLine($"- Data: {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {(OperatingSystem.IsWindows() ? "Windows" : "Linux")}");
sb.AppendLine($"- Resultado geral: **{(allOk ? "PASSOU" : "FALHOU")}**  ({sw.Elapsed.TotalSeconds:0}s)");
sb.AppendLine($"- Servidor: pyftpdlib (imitando o ftpsrv: só os comandos dele; upload limitado a 40 MB/s por conexão) em 127.0.0.1:{port}, destino `{RemoteDir}`, {settings.Connections} conexões, apagar original = sim");
sb.AppendLine($"- Ferramentas: 7-Zip {ToolVersion(sevenZip)} ({(OperatingSystem.IsWindows() ? "embutido no app" : "do sistema")}), RAR {ToolVersion(rar)} (só para gerar os testes)");
sb.AppendLine($"- Jogo falso: 6 arquivos (~24 MB, incompressíveis) dentro de 2 pastas casca; volumes de 5 MB");
sb.AppendLine();
sb.AppendLine("| Formato | Volumes | Esperou volume faltante | Estado final | SHA-256 iguais | Senha | Capa e título | param.json/sfo renomeados depois do último envio | Originais apagados | Resultado |");
sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
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
sb.AppendLine("Repetir: `dotnet run --project e2e` (na pasta do repo). Requer Python com `pyftpdlib`.");
File.WriteAllText(Path.Combine(root, "e2e_report.md"), sb.ToString());
File.WriteAllLines(Path.Combine(root, "e2e", "last-run.log"), logLines);
lock (ftpLog) File.WriteAllLines(Path.Combine(root, "e2e", "last-ftp.log"), ftpLog);
Console.WriteLine(sb.ToString());
return allOk ? 0 : 1;

// ---------- helpers ----------
static string Sha(string f) { using var s = File.OpenRead(f); return Convert.ToHexString(SHA256.HashData(s)); }

// param.sfo (PSF) com strings UTF-8
static byte[] Sfo(params (string key, string value)[] kv)
{
    using MemoryStream idx = new(), keys = new(), data = new();
    foreach (var (k, v) in kv)
    {
        var vb = Encoding.UTF8.GetBytes(v + "\0"); var max = (vb.Length + 3) / 4 * 4;
        idx.Write([.. BitConverter.GetBytes((ushort)keys.Length), .. BitConverter.GetBytes((ushort)0x0204), .. BitConverter.GetBytes(vb.Length), .. BitConverter.GetBytes(max), .. BitConverter.GetBytes((int)data.Length)]);
        keys.Write(Encoding.ASCII.GetBytes(k + "\0")); data.Write(vb); data.Write(new byte[max - vb.Length]);
    }
    while (keys.Length % 4 != 0) keys.WriteByte(0);
    var keyStart = 20 + (int)idx.Length;
    return [.. "\0PSF"u8, .. BitConverter.GetBytes(0x101), .. BitConverter.GetBytes(keyStart), .. BitConverter.GetBytes(keyStart + (int)keys.Length), .. BitConverter.GetBytes(kv.Length),
            .. idx.ToArray(), .. keys.ToArray(), .. data.ToArray()];
}

// icon0.png de verdade (ruído 64x64 RGBA), determinístico
static byte[] Png(int seed)
{
    const int W = 64;
    var rnd = new Random(seed);
    var raw = new byte[W * (1 + W * 4)]; // cada linha: filtro 0 + pixels
    for (var y = 0; y < W; y++) rnd.NextBytes(raw.AsSpan(y * (1 + W * 4) + 1, W * 4));
    using var z = new MemoryStream();
    using (var zs = new System.IO.Compression.ZLibStream(z, System.IO.Compression.CompressionLevel.Optimal, true)) zs.Write(raw);
    using var png = new MemoryStream();
    png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10]);
    void Chunk(string type, byte[] data)
    {
        byte[] Be(uint v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
        var td = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        png.Write(Be((uint)data.Length)); png.Write(td); png.Write(Be(Crc32.HashToUInt32(td)));
    }
    Chunk("IHDR", [0, 0, 0, W, 0, 0, 0, W, 8, 6, 0, 0, 0]);
    Chunk("IDAT", z.ToArray());
    Chunk("IEND", []);
    return png.ToArray();
}

// "7-Zip 23.01", "RAR 6.24": 1ª linha que o programa imprime sem argumentos
static string ToolVersion(string exe)
{
    if (OperatingSystem.IsWindows()) return FileVersionInfo.GetVersionInfo(exe).ProductVersion ?? "?";
    using var p = Process.Start(new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
    var line = p.StandardOutput.ReadToEnd().Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l != "") ?? "?";
    p.WaitForExit();
    return System.Text.RegularExpressions.Regex.Match(line, @"\d+\.\d+").Value is { Length: > 0 } v ? v : line;
}

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

// mbps: limite de upload por conexão. Os casos que precisam pegar o envio no meio usam menos, para não depender da máquina.
(Process, int, List<string>) StartFtp(string ftpRootDir, bool appe, int mbps = 40)
{
    var p = FreePort();
    var proc = Process.Start(new ProcessStartInfo(OperatingSystem.IsWindows() ? "python" : "python3", $"\"{Path.Combine(root, "e2e", "ftpserver.py")}\" {p} \"{ftpRootDir}\" {(appe ? "appe" : "noappe")} {mbps}")
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
    if (!OperatingSystem.IsWindows()) psi.Environment["LC_ALL"] = "C.UTF-8"; // sem locale o rar grava "ação" quebrado no arquivo
    using var p = Process.Start(psi)!;
    var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEnd();
    p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception($"{Path.GetFileName(exe)} falhou ({p.ExitCode}): {e}{o.Result}");
}

async Task EnsureRar()
{
    if (File.Exists(rar)) return;
    // RAR oficial do rarlab.com (Windows: Rar.exe do instalador; Linux: rar do rarlinux). Só para gerar os arquivos de teste.
    // 6.24 = última versão que ainda cria RAR4 com nomes antigos (-ma4 -vn).
    var win = OperatingSystem.IsWindows();
    var setup = Path.Combine(work, win ? "winrar-x64-624.exe" : "rarlinux-x64-624.tar.gz");
    using (var http = new HttpClient()) await File.WriteAllBytesAsync(setup, await http.GetByteArrayAsync("https://www.rarlab.com/rar/" + Path.GetFileName(setup)));
    if (win) { Run(sevenZip, work, "e", setup, "Rar.exe", "-o" + tools, "-y"); return; }
    Directory.CreateDirectory(tools);
    Run("tar", work, "-xzf", setup, "-C", tools, "--strip-components=1", "rar/rar");
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
