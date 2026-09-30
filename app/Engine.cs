using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace PS5Sender;

/// <summary>Varre a pasta de entrada + arquivos adicionados, agrupa partes e processa um jogo por vez.</summary>
public class Engine(Settings settings, Action<string> log, Func<Job, Task<string?>> askPassword)
{
    public ObservableCollection<Job> Jobs { get; } = [];
    public object Lock { get; } = new();
    public int StableSeconds { get; set; } = 5;
    /// Arquivos adicionados/removidos sobrevivem a fechar o app.
    public string QueueFile { get; set; } = Path.Combine(Settings.AppDir, "queue.json");

    readonly HashSet<string> _dropped = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase); // grupos tirados da fila pelo usuário
    readonly Dictionary<string, (string sig, DateTime since)> _stable = [];

    record Saved(List<string> Dropped, List<string> Removed);

    public void Restore()
    {
        try
        {
            var s = JsonSerializer.Deserialize<Saved>(File.ReadAllText(QueueFile))!;
            lock (_dropped) { _dropped.UnionWith(s.Dropped.Where(File.Exists)); _removed.UnionWith(s.Removed); }
            if (_dropped.Count > 0) log($"Fila restaurada: {_dropped.Count} arquivo(s) adicionados antes");
        }
        catch { }
    }

    void SaveQueue()
    {
        try
        {
            Saved s;
            lock (_dropped) s = new([.. _dropped.Where(File.Exists)], [.. _removed]);
            Directory.CreateDirectory(Path.GetDirectoryName(QueueFile)!);
            File.WriteAllText(QueueFile, JsonSerializer.Serialize(s));
        }
        catch (Exception e) { log("Não salvou a fila: " + e.Message); }
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        var list = paths.Where(File.Exists).ToList();
        lock (_dropped) { foreach (var p in list) _dropped.Add(p); foreach (var k in Archives.Group(list).Keys) _removed.Remove(k); }
        SaveQueue();
    }

    /// <summary>Tira da fila (cancelando se estiver rodando). Volta se os arquivos forem adicionados de novo.</summary>
    public void Remove(Job job)
    {
        job.Cts?.Cancel();
        lock (_dropped) _removed.Add(job.Key);
        lock (Lock) Jobs.Remove(job);
        SaveQueue();
        log($"[{job.Name}] removido da fila");
    }

    public async Task RunAsync(CancellationToken stop)
    {
        var scan = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try { await ScanAsync(); } catch (Exception e) { log("Erro na varredura: " + e.Message); }
                await Task.Delay(1000, stop).ContinueWith(_ => { });
            }
        });
        while (!stop.IsCancellationRequested)
        {
            Job? next;
            lock (Lock) next = Jobs.FirstOrDefault(j => j.Stage == Stage.NaFila);
            if (next == null) { await Task.Delay(500, stop).ContinueWith(_ => { }); continue; }
            await ProcessAsync(next);
        }
        await scan;
    }

    Dictionary<string, ArchiveGroup> CurrentGroups()
    {
        var groups = Directory.Exists(settings.InputFolder) ? Archives.Group(Directory.GetFiles(settings.InputFolder)) : [];
        string[] dropped;
        lock (_dropped) dropped = [.. _dropped];
        foreach (var dir in dropped.Select(Path.GetDirectoryName).Distinct().Where(Directory.Exists))
            foreach (var (k, g) in Archives.Group(Directory.GetFiles(dir!)))
                if (g.Parts.Any(dropped.Contains)) groups[k] = g;
        return groups;
    }

    async Task ScanAsync()
    {
        var groups = CurrentGroups();
        lock (_dropped) foreach (var k in _removed) groups.Remove(k);
        lock (Lock)
        {
            // cards "aguardando" cujos arquivos sumiram
            foreach (var j in Jobs.Where(j => j.Stage == Stage.AguardandoPartes && !groups.ContainsKey(j.Key)).ToList()) Jobs.Remove(j);
            foreach (var g in groups.Values.Where(g => !Jobs.Any(j => j.Key == g.Key)))
                Jobs.Add(new Job { Key = g.Key, Name = g.Name, Stage = Stage.AguardandoPartes });
        }

        foreach (var g in groups.Values)
        {
            Job? job;
            lock (Lock) job = Jobs.FirstOrDefault(j => j.Key == g.Key);
            if (job?.Stage != Stage.AguardandoPartes) continue; // removido no meio da varredura
            job.Parts = [.. g.Parts];
            job.MainFile = g.Main ?? "";
            if (!g.CompleteByName) { job.Detail = $"{job.Parts.Count} parte(s) encontrada(s), faltam outras"; _stable.Remove(g.Key); continue; }

            var sig = Sig(job.Parts);
            if (!_stable.TryGetValue(g.Key, out var st) || st.sig != sig) { _stable[g.Key] = (sig, DateTime.UtcNow); job.Detail = "Aguardando o tamanho dos arquivos estabilizar"; continue; }
            if ((DateTime.UtcNow - st.since).TotalSeconds < StableSeconds) continue;

            if ((await Archives.ListAsync(job.MainFile, job.ArchivePassword)).result == ListResult.Incomplete)
            {
                job.Detail = "Faltam volumes ou arquivo incompleto";
                _stable[g.Key] = (sig, DateTime.UtcNow); // reavalia depois de outra janela
                continue;
            }
            // Um volume pode ter chegado durante o 7z l: o 7z viu, mas a lista de partes não. Recomeça a janela.
            if (CurrentGroups().GetValueOrDefault(g.Key) is not { } fresh || Sig([.. fresh.Parts]) != sig) { _stable.Remove(g.Key); continue; }
            job.Detail = $"{job.Parts.Count} parte(s) · {Job.Size(job.Parts.Sum(p => new FileInfo(p).Length))}";
            job.Stage = Stage.NaFila;
            log($"[{job.Name}] todas as partes presentes ({job.Parts.Count}), na fila");
        }
    }

    static string Sig(List<string> parts) => string.Join("|", parts.Select(p => { var fi = new FileInfo(p); return $"{p}:{(fi.Exists ? fi.Length : -1)}:{(fi.Exists ? fi.LastWriteTimeUtc.Ticks : 0)}"; }));

    // Extrai e envia ao mesmo tempo: stdout do 7z vai direto para o FTP, nada é gravado em disco.
    // Antes, pergunta ao PS5 o que já existe e pede ao 7z só o que falta: pausar, fechar o app ou
    // "tentar de novo" não reextrai nem reenvia arquivos que já estão completos no console.
    async Task ProcessAsync(Job job)
    {
        job.Cts = new CancellationTokenSource();
        var ct = job.Cts.Token;
        string? listFile = null;
        try
        {
            job.Stage = Stage.Extraindo; job.ResetRate();
            job.Detail = "Lendo o conteúdo do arquivo…";
            var (res, entries) = await Archives.ListAsync(job.MainFile, job.ArchivePassword);
            while (res == ListResult.NeedPassword || entries.FirstOrDefault(e => e.Encrypted && !e.IsDir) is { } enc && !await Archives.PasswordOkAsync(job.MainFile, job.ArchivePassword, enc))
            {
                log($"[{job.Name}] senha necessária");
                job.ArchivePassword = await askPassword(job) ?? throw new Exception("Senha não informada");
                (res, entries) = await Archives.ListAsync(job.MainFile, job.ArchivePassword);
            }
            if (res != ListResult.Ok) throw new Exception("Arquivo incompleto ou corrompido");
            ct.ThrowIfCancellationRequested();

            var (gameName, targets) = Archives.Plan(entries, job.Name) ?? throw new Exception("Nenhuma pasta com EBOOT.BIN ou sce_sys/param.sfo");
            var remote = settings.RemoteDir.TrimEnd('/') + "/" + gameName;

            var decCount = entries.Count(e => !e.IsDir && e.Path.Split('/').Contains("dec", StringComparer.OrdinalIgnoreCase));
            // 2ª volta só se o APPE do servidor falhar: aí o arquivo parcial é reenviado inteiro com STOR.
            for (var noAppend = false; ; noAppend = true)
            {
                job.Detail = "Conferindo o que já está no PS5…";
                var (have, append, probe) = await Ftp.RemoteStateAsync(settings, entries, targets, remote, ct, noAppend);
                var need = Enumerable.Range(0, entries.Count).Where(i => targets[i] != null && have[i] != entries[i].Size).ToList();
                var already = targets.Count(t => t != null) - need.Count;

                job.Stage = Stage.Enviando; job.ResetRate();
                job.Detail = remote;
                log($"[{job.Name}] enviando {need.Count} arquivo(s) para {remote} ({settings.Connections} conexões)"
                    + (already > 0 ? $", {already} já estavam completos no PS5" : "")
                    + (decCount > 0 ? $", {decCount} do dec por cima" : "")
                    + (append ? " · parcial continua com APPE" : " · parcial é reenviado inteiro") + $" [{probe}]");
                if (need.Count == 0) break;

                listFile ??= Path.Combine(Path.GetTempPath(), $"ps5sender-{Guid.NewGuid():N}.txt");
                File.WriteAllLines(listFile, need.Select(i => entries[i].Path.Replace('/', '\\')));
                using var p = Archives.OpenStream(job.MainFile, job.ArchivePassword, listFile);
                using var reg = ct.Register(() => { try { p.Kill(true); } catch { } });
                var err = p.StandardError.ReadToEndAsync();
                Exception? fail = null;
                try { await Ftp.StreamAsync(settings, p.StandardOutput.BaseStream, entries, targets, have, append, need, remote, job.Report, f => job.CurrentFile = f, ct); }
                catch (Exception e) { fail = e; try { p.Kill(true); } catch { } }
                await p.WaitForExitAsync();
                ct.ThrowIfCancellationRequested();
                var sevenErr = p.ExitCode != 0 ? (await err).Trim() : "";
                // Erro do lado do 7z (senha, CRC, volume ruim) aparece como stream curto + exit != 0.
                if ((fail is null || fail.Message.StartsWith("Saída do 7-Zip")) && sevenErr != "") throw new Exception("7-Zip falhou: " + sevenErr);
                if (fail != null && append && !noAppend && fail.Message.Contains("(APPE)"))
                {
                    log($"[{job.Name}] servidor recusou APPE ({Ftp.Flatten(fail)}); reenviando parciais inteiros");
                    continue;
                }
                if (fail != null) throw fail;
                break;
            }

            job.Detail = "Verificando no PS5…";
            await Ftp.VerifyAsync(settings, entries, targets, remote, ct);
            job.Stage = Stage.Verificado;
            job.Finish(remote);
            log($"[{job.Name}] upload verificado");
            if (settings.DeleteOriginal)
            {
                foreach (var f in job.Parts) File.Delete(f);
                SaveQueue();
                log($"[{job.Name}] originais apagados ({job.Parts.Count})");
            }
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            var msg = Ftp.Flatten(e);
            job.Stage = Stage.Erro; job.Detail = msg; job.CurrentFile = ""; log($"[{job.Name}] ERRO: {msg}");
        }
        catch { } // pausado/cancelado: Stage já foi definido por Pause/Cancel
        finally { if (listFile != null) try { File.Delete(listFile); } catch { } }
    }

    public void Pause(Job job) { job.Stage = Stage.Pausado; job.Cts?.Cancel(); log($"[{job.Name}] pausado"); }
    public void Resume(Job job) { job.Stage = Stage.NaFila; log($"[{job.Name}] retomado"); }

    public void Cancel(Job job)
    {
        job.Stage = Stage.Cancelado;
        job.Cts?.Cancel();
        job.ResetRate();
        log($"[{job.Name}] cancelado");
    }

    public void ClearFinished()
    {
        lock (Lock) foreach (var j in Jobs.Where(j => j.Stage == Stage.Verificado).ToList()) { Jobs.Remove(j); lock (_dropped) _removed.Add(j.Key); }
        SaveQueue();
    }

    // Volta para a checagem de partes; o envio pula o que já está no PS5.
    public void Retry(Job job) { job.Detail = ""; job.Progress = 0; job.Stage = Stage.AguardandoPartes; log($"[{job.Name}] tentando de novo"); }
}
