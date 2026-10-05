using System.IO;
using System.Threading.Channels;
using FluentFTP;

namespace Ferry;

public static class Ftp
{
    public const int SmallFile = 4 << 20; // até 4 MB: buffer em RAM e envio em paralelo; acima: stream direto

    static AsyncFtpClient Client(Settings s)
    {
        var c = new AsyncFtpClient(s.Host, s.User, s.Password, s.Port);
        c.Config.ConnectTimeout = 10000;
        // ftpsrv do PS5 às vezes demora >15 s (padrão) para responder o STOR com várias conexões gravando no disco
        c.Config.ReadTimeout = c.Config.DataConnectionReadTimeout = 60000;
        c.Config.DataConnectionType = FtpDataConnectionType.PASV; // ftpsrv não tem EPSV
        c.Encoding = System.Text.Encoding.UTF8; // sem FEAT o FluentFTP cairia em ASCII e trocaria acentos por "?"
        c.Config.TransferChunkSize = 1 << 20;
        return c;
    }

    /// <summary>
    /// Conecta e desliga o "SELF transfer mode" do ftpsrv novo (ps5-payload-dev): ligado por padrão, faz o SIZE
    /// de um SELF (eboot.bin, .sprx) devolver o tamanho do ELF de dentro, não o do arquivo. É um liga/desliga.
    /// </summary>
    static async Task<AsyncFtpClient> Open(Settings s, CancellationToken ct)
    {
        var c = Client(s);
        await c.Connect(ct);
        if ((await c.Execute("SELF", ct)).Message.Contains("enabled")) await c.Execute("SELF", ct); // estava desligado: volta
        await c.Execute("TYPE I", ct); // SIZE em modo ASCII é recusado por vários servidores
        return c;
    }

    public static async Task<string> TestAsync(Settings s) => (await TestMessageAsync(s)).Render();

    public static async Task<Message> TestMessageAsync(Settings s)
    {
        await using var c = Client(s);
        await c.Connect();
        return await c.DirectoryExists(s.RemoteDir)
            ? new("core.ftp.connected", s.RemoteDir)
            : new("core.ftp.connectedMissing", s.RemoteDir);
    }

    /// <summary>
    /// Tamanho remoto de cada caminho (-1 = não existe / null no array) e se o servidor aceita APPE.
    /// Usa SIZE arquivo a arquivo: o LIST do ftpsrv ignora caminhos começando com "-" e não lista recursivo.
    /// </summary>
    public static async Task<(long[] have, bool append, string probe)> RemoteStateAsync(Settings s, string?[] paths, CancellationToken ct, bool noAppend = false)
    {
        await using var c = await Open(s, ct);
        // ftpsrv antigo responde 502 (não implementado); servidor com APPE reclama só da falta de argumento (501).
        var reply = await c.Execute("APPE", ct);
        var append = !noAppend && reply.Code == "501";
        var probe = $"APPE sem argumento → {reply.Code} {reply.Message}".Trim();
        var have = new long[paths.Length];
        for (var i = 0; i < paths.Length; i++)
            have[i] = paths[i] is { } p ? await Size(c, p, ct) : -1;
        return (have, append, probe);
    }

    // SIZE direto: sem FEAT o FluentFTP não sabe que o servidor tem SIZE e o GetFileSize devolve -1
    static async Task<long> Size(AsyncFtpClient c, string path, CancellationToken ct)
    {
        var r = await c.Execute("SIZE " + path, ct);
        FileLog.Write($"SIZE {path} → {r.Code} {r.Message}");
        return r.Code == "213" && long.TryParse(r.Message.Trim(), out var n) ? n : -1;
    }

    /// <summary>
    /// Lê a saída do "7z x -so" com SÓ os itens em need (na ordem do arquivo) e manda cada um direto para o FTP.
    /// Nada é gravado em disco. Parcial com have[i] &gt; 0: continua com APPE (se append), senão reenvia inteiro.
    /// Depois de cada arquivo confere o SIZE; lenient(i) = divergência vira aviso no log em vez de erro.
    /// started(i) é chamado antes de mandar um arquivo grande (é o que pode ficar parcial e ser continuado).
    /// </summary>
    public static async Task StreamAsync(Settings s, Stream src, List<Entry> entries, string?[] paths, long[] have, bool append,
        List<int> need, Action<long, long> progress, Action<string> currentFile, Action<int> started, Func<int, bool> lenient, Action<string> log, CancellationToken ct, Action<Message>? messageLog = null)
    {
        void Emit(Message message) { if (messageLog != null) messageLog(message); else log(message.Render()); }
        var total = entries.Where((e, i) => paths[i] != null).Sum(e => e.Size);
        long done = entries.Where((e, i) => paths[i] != null && have[i] == e.Size).Sum(e => e.Size);
        void Add(long n) => progress(Interlocked.Add(ref done, n), total);
        Add(0);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tk = cts.Token;
        await using var main = await Open(s, tk);
        foreach (var d in need.Select(i => paths[i]!).Select(p => p[..p.LastIndexOf('/')]).Distinct())
            await main.CreateDirectory(d, true, tk);

        var workers = Math.Max(1, s.Connections - 1); // + a conexão "main" = s.Connections no total
        var ch = Channel.CreateBounded<(int i, byte[] data, bool append)>(workers * 2);
        var pool = Enumerable.Range(0, workers).Select(async _ =>
        {
            try
            {
                await using var c = await Open(s, tk);
                await foreach (var (i, data, app) in ch.Reader.ReadAllAsync(tk))
                    await Put(c, new Slice(new MemoryStream(data), data.Length, Add), paths[i]!, app, entries[i].Size, lenient(i), Emit, tk);
            }
            catch { cts.Cancel(); throw; }
        }).ToList();

        try
        {
            var buf = new byte[1 << 20];
            async Task Skip(long n)
            {
                if (src.CanSeek) { src.Seek(n, SeekOrigin.Current); return; } // .exfat solto: pula no disco
                if (src is Archives.ConcatStream cs) { cs.SkipForward(n); return; } // pasta solta: idem, sem reler dezenas de GB
                while (n > 0) { var k = await src.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, n)), tk); if (k == 0) throw new EndOfStreamException(); n -= k; }
            }

            foreach (var i in need)
            {
                var (e, path) = (entries[i], paths[i]!);
                currentFile(path);
                var off = append && have[i] > 0 && have[i] < e.Size ? have[i] : 0;
                if (off > 0) { await Skip(off); Add(off); }
                var len = e.Size - off;
                if (len <= SmallFile)
                {
                    var data = new byte[len];
                    await src.ReadExactlyAsync(data, tk);
                    await ch.Writer.WriteAsync((i, data, off > 0), tk);
                }
                else { started(i); await Put(main, new Slice(src, len, Add), path, off > 0, e.Size, lenient(i), Emit, tk); }
            }
            if (await src.ReadAsync(buf, tk) > 0) throw new LocalizedException(new("core.archive.outputLong"));
            ch.Writer.Complete();
            await Task.WhenAll(pool);
        }
        catch when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            await Task.WhenAll(pool); // mostra o erro real do worker que falhou
            throw;
        }
        catch (EndOfStreamException) { throw new LocalizedException(new("core.archive.outputShort")); }
    }

    static async Task Put(AsyncFtpClient c, Stream data, string path, bool append, long expected, bool lenient, Action<Message> log, CancellationToken ct)
    {
        var cmd = append ? "APPE" : "STOR";
        // Stream de baixo nível: só TYPE + PASV + STOR/APPE. O UploadStream checa existência com NLST,
        // que o ftpsrv do PS5 não implementa (502 Command not recognized).
        try
        {
            await using (var s = append ? await c.OpenAppend(path, FtpDataType.Binary, false, ct) : await c.OpenWrite(path, FtpDataType.Binary, false, ct))
                await data.CopyToAsync(s, 1 << 20, ct);
            var reply = await c.GetReply(ct); // resposta final (226/4xx/5xx): o Dispose do stream não lê
            FileLog.Write($"{cmd} {path} → {reply.Code} {reply.Message}");
            if (!reply.Success) throw new Exception($"{reply.Code} {reply.Message}");
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            FileLog.Write($"{cmd} {path} → falhou: {Flatten(e)}");
            // stream do 7z acabou antes: o erro real é do 7z (a Engine mostra o stderr dele)
            if (e.GetBaseException() is EndOfStreamException) throw new LocalizedException(new("core.archive.outputShort"), e);
            throw new LocalizedException(new("core.ftp.sendFailed", path, cmd, Localization.ExceptionMessage(e)), e);
        }
        // O servidor pode responder 226 e o arquivo não mudar (arquivo aberto pelo jogo/loader, overlay de backport).
        var got = await Size(c, path, ct);
        if (got == expected) return;
        var msg = new Message("core.ftp.size", path, expected, got);
        if (lenient) { log(new("core.ftp.backportWarning", msg)); return; }
        throw new LocalizedException(new("core.ftp.overwriteFailed", path, expected, got));
    }

    /// <summary>RNFR/RNTO. Apaga o destino antes (nem todo servidor renomeia por cima).</summary>
    public static async Task RenameAsync(Settings s, string from, string to, CancellationToken ct)
    {
        await using var c = await Open(s, ct);
        if (await Size(c, to, ct) >= 0) FileLog.Write($"DELE {to} → {(await c.Execute("DELE " + to, ct)).Code}");
        var r1 = await c.Execute("RNFR " + from, ct);
        var r2 = r1.Success ? await c.Execute("RNTO " + to, ct) : r1;
        FileLog.Write($"RNFR {from} → {r1.Code}; RNTO {to} → {r2.Code} {r2.Message}");
        if (!r2.Success) throw new LocalizedException(new("core.ftp.renameFailed", from, to, r2.Code, r2.Message));
    }

    /// <summary>Mensagens da exceção e de todas as internas ("See InnerException" não ajuda ninguém).</summary>
    public static string Flatten(Exception e)
    {
        var msgs = new List<string>();
        for (Exception? x = e; x != null; x = x.InnerException)
            if (!x.Message.Contains("See InnerException") && !msgs.Contains(x.Message)) msgs.Add(x.Message);
        return string.Join(" → ", msgs);
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
