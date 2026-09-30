using System.IO;
using System.Threading.Channels;
using FluentFTP;

namespace PS5Sender;

public static class Ftp
{
    const int SmallFile = 4 << 20; // até 4 MB: buffer em RAM e envio em paralelo; acima: stream direto

    static AsyncFtpClient Client(Settings s)
    {
        var c = new AsyncFtpClient(s.Host, s.User, s.Password, s.Port);
        c.Config.ConnectTimeout = 10000;
        c.Config.DataConnectionType = FtpDataConnectionType.PASV; // ftpsrv não tem EPSV
        c.Encoding = System.Text.Encoding.UTF8; // sem FEAT o FluentFTP cairia em ASCII e trocaria acentos por "?"
        c.Config.TransferChunkSize = 1 << 20;
        return c;
    }

    public static async Task<string> TestAsync(Settings s)
    {
        await using var c = Client(s);
        await c.Connect();
        return await c.DirectoryExists(s.RemoteDir)
            ? $"Conectado. Destino {s.RemoteDir} existe."
            : $"Conectado, mas {s.RemoteDir} não existe (será criado no envio).";
    }

    /// <summary>
    /// O que já está no PS5: tamanho remoto de cada item (-1 = não existe / fora do jogo) e se o servidor aceita APPE.
    /// Usa SIZE arquivo a arquivo: o LIST do ftpsrv ignora caminhos começando com "-" e não lista recursivo.
    /// </summary>
    public static async Task<(long[] have, bool append, string probe)> RemoteStateAsync(Settings s, List<Entry> entries, string?[] targets, string remoteDir, CancellationToken ct, bool noAppend = false)
    {
        await using var c = Client(s);
        await c.Connect(ct);
        // ftpsrv responde 502 (não implementado); servidor com APPE reclama só da falta de argumento (501).
        var reply = await c.Execute("APPE", ct);
        var append = !noAppend && reply.Code == "501";
        var probe = $"APPE sem argumento → {reply.Code} {reply.Message}".Trim();
        var have = new long[entries.Count];
        await c.Execute("TYPE I", ct); // SIZE em modo ASCII é recusado por vários servidores
        for (var i = 0; i < entries.Count; i++)
        {
            have[i] = -1;
            if (targets[i] is not { } r) continue;
            // SIZE direto: sem FEAT o FluentFTP não sabe que o servidor tem SIZE e o GetFileSize devolve -1
            var sz = await c.Execute("SIZE " + remoteDir + "/" + r, ct);
            if (sz.Code == "213" && long.TryParse(sz.Message.Trim(), out var size)) have[i] = size;
        }
        return (have, append, probe);
    }

    /// <summary>
    /// Lê a saída do "7z x -so" com SÓ os itens em need (na ordem do arquivo) e manda cada um direto para o FTP.
    /// Nada é gravado em disco. Arquivo parcial: continua com APPE se o servidor aceitar, senão reenvia inteiro.
    /// </summary>
    public static async Task StreamAsync(Settings s, Stream src, List<Entry> entries, string?[] targets, long[] have, bool append,
        List<int> need, string remoteDir, Action<long, long> progress, Action<string> currentFile, CancellationToken ct)
    {
        var total = entries.Where((e, i) => targets[i] != null).Sum(e => e.Size);
        long done = entries.Where((e, i) => targets[i] != null && have[i] == e.Size).Sum(e => e.Size);
        void Add(long n) => progress(Interlocked.Add(ref done, n), total);
        Add(0);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tk = cts.Token;
        await using var main = Client(s);
        await main.Connect(tk);
        foreach (var d in need.Select(i => remoteDir + "/" + targets[i]).Select(p => p[..p.LastIndexOf('/')]).Append(remoteDir).Distinct())
            await main.CreateDirectory(d, true, tk);

        var workers = Math.Max(1, s.Connections - 1); // + a conexão "main" = s.Connections no total
        var ch = Channel.CreateBounded<(string path, byte[] data, bool append)>(workers * 2);
        var pool = Enumerable.Range(0, workers).Select(async _ =>
        {
            try
            {
                await using var c = Client(s);
                await c.Connect(tk);
                await foreach (var (path, data, app) in ch.Reader.ReadAllAsync(tk))
                    await Put(c, new Slice(new MemoryStream(data), data.Length, Add), path, app, tk);
            }
            catch { cts.Cancel(); throw; }
        }).ToList();

        try
        {
            var buf = new byte[1 << 20];
            async Task Skip(long n)
            {
                while (n > 0) { var k = await src.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, n)), tk); if (k == 0) throw new EndOfStreamException(); n -= k; }
            }

            foreach (var i in need)
            {
                var (e, rel) = (entries[i], targets[i]!);
                var path = remoteDir + "/" + rel;
                currentFile(rel);
                var off = append && have[i] > 0 && have[i] < e.Size ? have[i] : 0;
                if (off > 0) { await Skip(off); Add(off); }
                var len = e.Size - off;
                if (len <= SmallFile)
                {
                    var data = new byte[len];
                    await src.ReadExactlyAsync(data, tk);
                    await ch.Writer.WriteAsync((path, data, off > 0), tk);
                }
                else await Put(main, new Slice(src, len, Add), path, off > 0, tk);
            }
            if (await src.ReadAsync(buf, tk) > 0) throw new Exception("Saída do 7-Zip maior que a listagem");
            ch.Writer.Complete();
            await Task.WhenAll(pool);
        }
        catch when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            await Task.WhenAll(pool); // mostra o erro real do worker que falhou
            throw;
        }
        catch (EndOfStreamException) { throw new Exception("Saída do 7-Zip terminou antes do esperado"); }
    }

    static async Task Put(AsyncFtpClient c, Stream data, string path, bool append, CancellationToken ct)
    {
        // Stream de baixo nível: só TYPE + PASV + STOR/APPE. O UploadStream checa existência com NLST,
        // que o ftpsrv do PS5 não implementa (502 Command not recognized).
        try
        {
            await using var s = append ? await c.OpenAppend(path, FtpDataType.Binary, false, ct) : await c.OpenWrite(path, FtpDataType.Binary, false, ct);
            await data.CopyToAsync(s, 1 << 20, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            // stream do 7z acabou antes: o erro real é do 7z (a Engine mostra o stderr dele)
            if (e.GetBaseException() is EndOfStreamException) throw new Exception("Saída do 7-Zip terminou antes do esperado", e);
            throw new Exception($"Falha ao enviar {path} ({(append ? "APPE" : "STOR")}): {Flatten(e)}", e);
        }
    }

    /// <summary>Mensagens da exceção e de todas as internas ("See InnerException" não ajuda ninguém).</summary>
    public static string Flatten(Exception e)
    {
        var msgs = new List<string>();
        for (Exception? x = e; x != null; x = x.InnerException)
            if (!x.Message.Contains("See InnerException") && !msgs.Contains(x.Message)) msgs.Add(x.Message);
        return string.Join(" → ", msgs);
    }

    /// <summary>Confere o tamanho remoto de cada arquivo do jogo.</summary>
    public static async Task VerifyAsync(Settings s, List<Entry> entries, string?[] targets, string remoteDir, CancellationToken ct)
    {
        var (have, _, _) = await RemoteStateAsync(s, entries, targets, remoteDir, ct);
        var bad = entries.Where((e, i) => targets[i] != null && have[i] != e.Size).Select(e => e.Path).ToList();
        if (bad.Count > 0) throw new Exception($"Tamanho remoto divergente em {bad.Count} arquivo(s): " + string.Join(", ", bad.Take(5)));
    }

    /// <summary>Janela de leitura de exatamente len bytes sobre outro stream, contando o que foi lido.</summary>
    class Slice(Stream src, long len, Action<long> onRead) : Stream
    {
        long _pos;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => len;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_pos >= len) return 0;
            var n = await src.ReadAsync(buffer[..(int)Math.Min(buffer.Length, len - _pos)], ct);
            if (n == 0) throw new EndOfStreamException();
            _pos += n; onRead(n);
            return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
