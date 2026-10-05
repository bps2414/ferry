using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Ferry;

public class ArchiveGroup
{
    public required string Key;
    public required string Name;
    public string? Plain;                          // X.zip / X.rar / X.7z
    public SortedDictionary<int, string> Vols = []; // volumes numerados
    public int FirstIndex = 1;                     // .r00 começa em 0
    public bool PlainIsMain;                       // .z01+.zip e .r00+.rar abrem pelo arquivo sem número
    public Func<int, string>? VolName;             // nome do volume N (do padrão do 1º volume visto)

    public IEnumerable<string> Parts => Plain is null ? Vols.Values : Vols.Values.Append(Plain);
    public string? Main => PlainIsMain || Vols.Count == 0 ? Plain : Vols.GetValueOrDefault(FirstIndex);

    /// Nomes que faltam pela numeração (buracos e o arquivo principal). O último volume só o 7z percebe.
    public List<string> Missing()
    {
        var miss = Vols.Count == 0 ? [] : Enumerable.Range(FirstIndex, Vols.Keys.Max() - FirstIndex + 1).Where(i => !Vols.ContainsKey(i)).Select(VolName!).ToList();
        if (PlainIsMain && Plain is null) miss.Add(Path.GetFileName(Key));
        return miss;
    }

    public bool CompleteByName
    {
        get
        {
            if (Main is null) return false;
            var expected = Enumerable.Range(FirstIndex, Vols.Count);
            return Vols.Keys.SequenceEqual(expected);
        }
    }
}

public enum ListResult { Ok, Incomplete, NeedPassword }

public static class Archives
{
    // n = nome-base, e = extensão do grupo, i = índice do volume
    static readonly (Regex re, bool plainIsMain, int first)[] Patterns =
    [
        (new(@"^(?<n>.+)\.(?<e>zip|7z|rar)\.(?<i>\d{3})$", RegexOptions.IgnoreCase), false, 1),
        (new(@"^(?<n>.+)\.part(?<i>\d+)\.(?<e>rar)$", RegexOptions.IgnoreCase), false, 1),
        (new(@"^(?<n>.+)\.(?<e>z)(?<i>\d{2})$", RegexOptions.IgnoreCase), true, 1),
        (new(@"^(?<n>.+)\.(?<e>r)(?<i>\d{2})$", RegexOptions.IgnoreCase), true, 0),
    ];
    // .exfat solto = imagem do ShadowMount+, enviada como está (sem 7-Zip)
    static readonly Regex PlainRe = new(@"^(.+)\.(zip|7z|rar|exfat|ffpkg|ffpfs|ffpfsc|pkg)$", RegexOptions.IgnoreCase);
    public static bool IsImage(string path) => new[] { ".exfat", ".ffpkg", ".ffpfs", ".ffpfsc" }.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    public static string DirStamp(string dir)
    {
        long n = 0, len = 0, t = 0;
        foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)) { n++; len += f.Length; t = Math.Max(t, f.LastWriteTimeUtc.Ticks); }
        return $"{n}:{len}:{t}";
    }
    public static long SourceSize(string path) => Directory.Exists(path)
        ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : new FileInfo(path).Length;
    /// <summary>Arquivos da pasta (recursivo, ordem estável), caminhos relativos com "/".</summary>
    public static List<Entry> FolderEntries(string dir) => [.. new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories)
        .Select(f => new Entry(System.IO.Path.GetRelativePath(dir, f.FullName).Replace('\\', '/'), f.Length, false, false))
        .OrderBy(e => e.Path, StringComparer.Ordinal)];

    /// <summary>Concatena arquivos do disco num stream só (mesmo contrato do stdout do 7z x -so).</summary>
    public sealed class ConcatStream(IEnumerable<string> files) : Stream
    {
        readonly IEnumerator<string> _files = files.GetEnumerator();
        FileStream? _cur;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            while (true)
            {
                if (_cur == null) { if (!_files.MoveNext()) return 0; _cur = new(_files.Current, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, true); }
                var n = await _cur.ReadAsync(buffer, ct);
                if (n > 0) return n;
                await _cur.DisposeAsync(); _cur = null;
            }
        }
        /// <summary>Pula n bytes do arquivo atual por seek (retomada via APPE não relê o que já está no PS5).</summary>
        public void SkipForward(long n)
        {
            while (_cur == null || _cur.Position >= _cur.Length)
            {
                _cur?.Dispose(); _cur = null;
                if (!_files.MoveNext()) throw new EndOfStreamException();
                _cur = new(_files.Current, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 81920, true);
            }
            _cur.Seek(n, SeekOrigin.Current);
        }
        public override int Read(byte[] b, int o, int c) => ReadAsync(b.AsMemory(o, c)).AsTask().GetAwaiter().GetResult();
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        protected override void Dispose(bool d) { if (d) _cur?.Dispose(); base.Dispose(d); }
    }

    public static bool IsPackage(string path) => path.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase);

    public static Entry? PackagePlan(List<Entry> entries)
    {
        var packages = entries.Where(e => !e.IsDir && IsPackage(e.Path)).ToList();
        if (packages.Count == 0) return null;
        if (packages.Count > 1) throw new LocalizedException(new("core.pkg.multiple", string.Join(", ", packages.Select(e => e.Path))));
        if (FindGameRoot(entries) != null || entries.Any(e => !e.IsDir && IsImage(e.Path)))
            throw new LocalizedException(new("core.pkg.mixed"));
        var path = packages[0].Path.Replace('\\', '/');
        if (path.StartsWith('/') || path.Split('/').Any(p => p is "." or ".." || p.Contains(':') || p.Any(char.IsControl)))
            throw new LocalizedException(new("core.pkg.path"));
        return packages[0];
    }

    public static Dictionary<string, ArchiveGroup> Group(IEnumerable<string> files)
    {
        var groups = new Dictionary<string, ArchiveGroup>(StringComparer.OrdinalIgnoreCase);
        ArchiveGroup Get(string dir, string name, string ext)
        {
            var key = Path.Combine(dir, name + "." + ext.ToLowerInvariant());
            if (!groups.TryGetValue(key, out var g)) groups[key] = g = new() { Key = key, Name = name };
            return g;
        }

        foreach (var f in files)
        {
            var dir = Path.GetDirectoryName(f)!; var fn = Path.GetFileName(f);
            var matched = false;
            foreach (var (re, plainIsMain, first) in Patterns)
            {
                var m = re.Match(fn);
                if (!m.Success) continue;
                // .z01 pertence a X.zip, .r00 a X.rar
                var ext = m.Groups["e"].Value.ToLowerInvariant() switch { "z" => "zip", "r" => "rar", var e => e };
                var idx = int.Parse(m.Groups["i"].Value);
                var g = Get(dir, m.Groups["n"].Value, ext);
                g.Vols[idx] = f; g.FirstIndex = first; g.PlainIsMain |= plainIsMain;
                var gi = m.Groups["i"];
                g.VolName ??= n => fn[..gi.Index] + n.ToString("D" + gi.Length) + fn[(gi.Index + gi.Length)..];
                matched = true; break;
            }
            if (matched) continue;
            var p = PlainRe.Match(fn);
            if (p.Success) Get(dir, p.Groups[1].Value, p.Groups[2].Value).Plain = f;
        }
        return groups;
    }

    /// Caminho do 7-Zip. O app Windows define (7z.exe embutido); senão procura ao lado do programa e no PATH.
    public static string? SevenZipPath { get; set; }
    public static string SevenZip() => SevenZipPath ??= Find7z();

    static string Find7z()
    {
        string[] names = OperatingSystem.IsWindows() ? ["7z.exe"] : ["7zz", "7z"];
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Prepend(Path.GetDirectoryName(Environment.ProcessPath)!);
        foreach (var d in dirs.Where(d => d != ""))
            foreach (var n in names)
                if (File.Exists(Path.Combine(d, n))) return Path.Combine(d, n);
        throw new LocalizedException(new("core.archive.notFound"));
    }

    /// Caminho de dentro do arquivo ("a/b") como o 7-Zip do sistema espera na lista de arquivos e no "7z t".
    public static string Native(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    // Senha fictícia quando não há senha: evita o 7z travar pedindo senha no console.
    static string Pw(string? pw) => "-p" + (string.IsNullOrEmpty(pw) ? "x-sem-senha" : pw);

    static Process Start(params string[] args)
    {
        var psi = new ProcessStartInfo(SevenZip()) { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, UseShellExecute = false, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return Process.Start(psi)!;
    }

    /// <summary>Lista o conteúdo (7z l -slt). Entries só vêm preenchidas quando Ok; missing = volumes que o 7z diz faltar.</summary>
    public static async Task<(ListResult result, List<Entry> entries, string[] missing)> ListAsync(string main, string? pw)
    {
        using var p = Start("l", "-slt", "-sccUTF-8", Pw(pw), main);
        var outTask = p.StandardOutput.ReadToEndAsync();
        var err = await p.StandardError.ReadToEndAsync();
        var output = await outTask;
        var all = output + err;
        await p.WaitForExitAsync();
        if (all.Contains("Wrong password") || all.Contains("Can not open encrypted archive")) return (ListResult.NeedPassword, [], []);
        if (p.ExitCode != 0 || Regex.IsMatch(all, "Unexpected end|Missing volume|Headers Error|Can not open|ERROR", RegexOptions.IgnoreCase))
            return (ListResult.Incomplete, [], [.. Regex.Matches(all, @"Missing volume : (.+?)\r?$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).Distinct()]);

        // Depois de "----------" vem um bloco "Chave = valor" por item, separados por linha em branco.
        var entries = new List<Entry>();
        var body = output[(output.IndexOf("\n----------", StringComparison.Ordinal) + 11)..];
        foreach (var block in Regex.Split(body, @"\r?\n\r?\n"))
        {
            var kv = block.Split('\n').Select(l => l.TrimEnd('\r').Split(" = ", 2)).Where(a => a.Length == 2).ToDictionary(a => a[0], a => a[1]);
            if (!kv.TryGetValue("Path", out var path)) continue;
            var isDir = kv.GetValueOrDefault("Folder") == "+" || kv.GetValueOrDefault("Attributes", "").StartsWith('D');
            entries.Add(new(path.Replace('\\', '/'), isDir ? 0 : long.Parse(kv.GetValueOrDefault("Size") is { Length: > 0 } s ? s : "0"), isDir, kv.GetValueOrDefault("Encrypted") == "+"));
        }
        return (ListResult.Ok, entries, []);
    }

    /// <summary>Extrai itens pequenos para a memória (mesmo "7z x -so" do envio). null = não deu.</summary>
    public static async Task<byte[][]?> ReadSmallAsync(string main, string? pw, List<Entry> items)
    {
        var list = Path.Combine(Path.GetTempPath(), $"ferry-{Guid.NewGuid():N}.txt");
        try
        {
            if (Directory.Exists(main)) return [.. items.Select(e => File.ReadAllBytes(System.IO.Path.Combine(main, Native(e.Path))))];
            File.WriteAllLines(list, items.Select(e => Native(e.Path)));
            using var p = OpenStream(main, pw, list);
            _ = p.StandardError.ReadToEndAsync();
            var s = p.StandardOutput.BaseStream;
            // o 7z entrega na ordem do arquivo, que é a ordem de items (vêm da listagem)
            var res = new byte[items.Count][];
            for (var i = 0; i < items.Count; i++) { res[i] = new byte[items[i].Size]; await s.ReadExactlyAsync(res[i]); }
            await p.WaitForExitAsync();
            return p.ExitCode == 0 ? res : null;
        }
        catch { return null; }
        finally { try { File.Delete(list); } catch { } }
    }

    /// <summary>TITLE e TITLE_ID de um param.sfo (formato PSF). ("", "") se não der para ler.</summary>
    public static (string title, string titleId) ParseSfo(byte[] b)
    {
        try
        {
            if (b.Length < 20 || Encoding.ASCII.GetString(b, 0, 4) != "\0PSF") return ("", "");
            int keys = BitConverter.ToInt32(b, 8), data = BitConverter.ToInt32(b, 12), n = BitConverter.ToInt32(b, 16);
            string Z(int o) => Encoding.UTF8.GetString(b, o, Array.IndexOf(b, (byte)0, o) - o);
            var kv = Enumerable.Range(0, n).Select(k => 20 + 16 * k).ToDictionary(
                e => Z(keys + BitConverter.ToUInt16(b, e)),
                e => Encoding.UTF8.GetString(b, data + BitConverter.ToInt32(b, e + 12), BitConverter.ToInt32(b, e + 4)).TrimEnd('\0'));
            return (kv.GetValueOrDefault("TITLE", ""), kv.GetValueOrDefault("TITLE_ID", ""));
        }
        catch { return ("", ""); }
    }

    /// <summary>Testa a senha só no primeiro arquivo criptografado (barato), para não mandar lixo ao FTP.</summary>
    public static async Task<bool> PasswordOkAsync(string main, string? pw, Entry first)
    {
        using var p = Start("t", "-sccUTF-8", Pw(pw), main, Native(first.Path));
        var outTask = p.StandardOutput.ReadToEndAsync();
        await p.StandardError.ReadToEndAsync();
        await outTask;
        await p.WaitForExitAsync();
        return p.ExitCode == 0;
    }

    /// <summary>7z x -so: todos os arquivos concatenados no stdout, na mesma ordem do 7z l.</summary>
    /// Com listFile, extrai só os itens listados (um caminho por linha): pula o que já está no PS5.
    public static Process OpenStream(string main, string? pw, string? listFile = null) =>
        listFile == null ? Start("x", "-so", "-sccUTF-8", Pw(pw), main)
                         : Start("x", "-so", "-sccUTF-8", "-scsUTF-8", "-spd", Pw(pw), main, "@" + listFile);

    /// <summary>Pasta "casca" mais rasa que contém EBOOT.BIN ou sce_sys/param.sfo ("" = raiz do arquivo).</summary>
    public static string? FindGameRoot(IEnumerable<Entry> entries)
    {
        var files = entries.Where(e => !e.IsDir).Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return files.Select(f => Regex.Match(f, @"^(?:(.*)/)?(EBOOT\.BIN|sce_sys/param\.sfo)$", RegexOptions.IgnoreCase))
            .Where(m => m.Success).Select(m => m.Groups[1].Value)
            .OrderBy(d => d.Split('/').Contains("dec", StringComparer.OrdinalIgnoreCase)) // "dec" tem EBOOT.BIN mas é sobreposição, não o jogo
            .ThenBy(d => d.Length).Cast<string?>().FirstOrDefault();
    }

    /// <summary>
    /// Destino relativo de cada item (null = descartar). Uma pasta "dec" ao lado da pasta do jogo sobrepõe
    /// os arquivos do jogo (igual a copiar o jogo e depois o dec por cima): o arquivo do jogo que o dec
    /// substitui é descartado e só a versão do dec é enviada.
    /// </summary>
    public static (string gameName, string?[] targets)? Plan(List<Entry> entries, string fallbackName)
    {
        if (FindGameRoot(entries) is not { } root) return null;
        var gameName = root == "" ? fallbackName : root[(root.LastIndexOf('/') + 1)..];
        var parent = root.Contains('/') ? root[..(root.LastIndexOf('/') + 1)] : "";
        var decRoot = entries.Where(e => !e.IsDir).Select(e => e.Path)
            .FirstOrDefault(p => p.StartsWith(parent + "dec/", StringComparison.OrdinalIgnoreCase)) is { } any ? any[..(parent.Length + 4)] : null;
        // dec/<pasta do jogo>/... também vale
        if (decRoot != null && entries.Where(e => !e.IsDir && e.Path.StartsWith(decRoot, StringComparison.OrdinalIgnoreCase))
                .All(e => e.Path[decRoot.Length..].StartsWith(gameName + "/", StringComparison.OrdinalIgnoreCase)))
            decRoot += gameName + "/";

        string? Under(string path, string prefix) => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path[prefix.Length..] : null;
        var targets = new string?[entries.Count];
        var fromDec = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].IsDir) continue;
            if (decRoot != null && Under(entries[i].Path, decRoot) is { } d) { targets[i] = d; fromDec.Add(d); }
            else targets[i] = root == "" ? entries[i].Path : Under(entries[i].Path, root + "/");
        }
        for (var i = 0; i < entries.Count; i++)
            if (targets[i] != null && fromDec.Contains(targets[i]!) && !(decRoot != null && entries[i].Path.StartsWith(decRoot, StringComparison.OrdinalIgnoreCase)))
                targets[i] = null; // o dec vence
        return (gameName, targets);
    }

    /// <summary>Arquivo sem pasta de jogo mas com imagem(ns) .exfat: cada imagem vai com o próprio nome, o resto é descartado.</summary>
    public static string?[]? ImagePlan(List<Entry> entries)
    {
        var targets = entries.Select(e => !e.IsDir && IsImage(e.Path) ? e.Path[(e.Path.LastIndexOf('/') + 1)..] : null).ToArray();
        if (targets.Where(t => t != null).GroupBy(t => t, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new LocalizedException(new("core.image.duplicate"));
        return targets.Any(t => t != null) ? targets : null;
    }
}

public record Entry(string Path, long Size, bool IsDir, bool Encrypted);
