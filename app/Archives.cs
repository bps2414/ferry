using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PS5Sender;

public class ArchiveGroup
{
    public required string Key;
    public required string Name;
    public string? Plain;                          // X.zip / X.rar / X.7z
    public SortedDictionary<int, string> Vols = []; // volumes numerados
    public int FirstIndex = 1;                     // .r00 começa em 0
    public bool PlainIsMain;                       // .z01+.zip e .r00+.rar abrem pelo arquivo sem número

    public IEnumerable<string> Parts => Plain is null ? Vols.Values : Vols.Values.Append(Plain);
    public string? Main => PlainIsMain || Vols.Count == 0 ? Plain : Vols.GetValueOrDefault(FirstIndex);

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
    static readonly Regex PlainRe = new(@"^(.+)\.(zip|7z|rar)$", RegexOptions.IgnoreCase);

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
                matched = true; break;
            }
            if (matched) continue;
            var p = PlainRe.Match(fn);
            if (p.Success) Get(dir, p.Groups[1].Value, p.Groups[2].Value).Plain = f;
        }
        return groups;
    }

    static string? _exe;
    static string SevenZip()
    {
        if (_exe != null) return _exe;
        var dir = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "7z"); // ao lado do .exe
        Directory.CreateDirectory(dir);
        foreach (var name in new[] { "7z.exe", "7z.dll" })
        {
            using var src = typeof(Archives).Assembly.GetManifestResourceStream(name)!;
            var dst = Path.Combine(dir, name);
            if (File.Exists(dst) && new FileInfo(dst).Length == src.Length) continue;
            using var fs = File.Create(dst); src.CopyTo(fs);
        }
        return _exe = Path.Combine(dir, "7z.exe");
    }

    // Senha fictícia quando não há senha: evita o 7z travar pedindo senha no console.
    static string Pw(string? pw) => "-p" + (string.IsNullOrEmpty(pw) ? "x-sem-senha" : pw);

    static Process Start(params string[] args)
    {
        var psi = new ProcessStartInfo(SevenZip()) { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, UseShellExecute = false, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return Process.Start(psi)!;
    }

    /// <summary>Lista o conteúdo (7z l -slt). Entries só vêm preenchidas quando Ok.</summary>
    public static async Task<(ListResult result, List<Entry> entries)> ListAsync(string main, string? pw)
    {
        using var p = Start("l", "-slt", "-sccUTF-8", Pw(pw), main);
        var outTask = p.StandardOutput.ReadToEndAsync();
        var err = await p.StandardError.ReadToEndAsync();
        var output = await outTask;
        var all = output + err;
        await p.WaitForExitAsync();
        if (all.Contains("Wrong password") || all.Contains("Can not open encrypted archive")) return (ListResult.NeedPassword, []);
        if (p.ExitCode != 0 || Regex.IsMatch(all, "Unexpected end|Missing volume|Headers Error|Can not open|ERROR", RegexOptions.IgnoreCase)) return (ListResult.Incomplete, []);

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
        return (ListResult.Ok, entries);
    }

    /// <summary>Testa a senha só no primeiro arquivo criptografado (barato), para não mandar lixo ao FTP.</summary>
    public static async Task<bool> PasswordOkAsync(string main, string? pw, Entry first)
    {
        using var p = Start("t", "-sccUTF-8", Pw(pw), main, first.Path.Replace('/', '\\'));
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
}

public record Entry(string Path, long Size, bool IsDir, bool Encrypted);
