using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace Ferry;

/// <summary>Varre a pasta de entrada + arquivos adicionados, agrupa partes e processa um jogo por vez.</summary>
public partial class Engine(Settings settings, Action<string> log, Func<Job, Task<string?>> askPassword)
{
    public ObservableCollection<Job> Jobs { get; } = [];
    public object Lock { get; } = new();
    public int StableSeconds { get; set; } = 5;
    /// Arquivos adicionados/removidos sobrevivem a fechar o app.
    public string QueueFile { get; set; } = Path.Combine(Settings.AppDir, "queue.json");
    /// Chamado (de thread de fundo) quando um jogo termina: Verificado ou Erro.
    public Action<Job>? Done { get; set; }
    public Action<Message>? MessageLog { get; set; }
    void Emit(Message message) { if (MessageLog != null) MessageLog(message); else log(message.Render()); }
    void JobLog(Job job, Message message) => Emit(new("core.jobLog", job.Name, message));

    readonly HashSet<string> _dropped = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase); // grupos tirados da fila pelo usuário
    readonly Dictionary<string, (string sig, DateTime since)> _stable = [];
    // por jogo (Key): senha do arquivo cifrada (Secret: DPAPI no Windows); e arquivo remoto → tamanho esperado dos envios
    // que ESTE app começou (só esses podem continuar com APPE: outro arquivo menor no PS5 pode ser outra versão)
    Dictionary<string, string> _passwords = new(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, Dictionary<string, long>> _started = new(StringComparer.OrdinalIgnoreCase);
    // jogos já enviados e conferidos: ao reabrir (ou reiniciar o container) voltam como "Concluído", não entram na fila de novo
    HashSet<string> _done = new(StringComparer.OrdinalIgnoreCase);

    record Saved(List<string> Dropped, List<string> Removed, Dictionary<string, string>? Passwords, Dictionary<string, Dictionary<string, long>>? Started, List<string>? Done = null, Dictionary<string, PackagePreparation>? Packages = null);

    /// Sufixo dos arquivos que fazem o loader reconhecer o jogo, até o jogo inteiro estar no PS5 e conferido.
    public const string PartSuffix = ".ferry-part";
    // ShadowMount+ detecta pelo sce_sys/param.json; param.sfo também, por garantia; e monta imagem .exfat pela extensão (ver docs/FTP-PS5.md)
    static bool Held(string rel) => rel.Equals("sce_sys/param.json", StringComparison.OrdinalIgnoreCase) || rel.Equals("sce_sys/param.sfo", StringComparison.OrdinalIgnoreCase) || Archives.IsImage(rel);
    // arquivos que o overlay de backport do loader (ShadowMount+) troca por cima de um jogo instalado
    static bool Backport(string rel) => System.Text.RegularExpressions.Regex.IsMatch(rel, @"^(eboot\.bin|fakelib/.*|sce_module/.*\.prx|sce_sys/about/right\.sprx)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public void Restore()
    {
        try
        {
            var s = JsonSerializer.Deserialize<Saved>(File.ReadAllText(QueueFile))!;
            lock (_dropped)
            {
                _dropped.UnionWith(s.Dropped.Where(File.Exists)); _removed.UnionWith(s.Removed);
                _passwords = new(s.Passwords ?? [], StringComparer.OrdinalIgnoreCase);
                _started = new(s.Started ?? [], StringComparer.OrdinalIgnoreCase);
                _done = new(s.Done ?? [], StringComparer.OrdinalIgnoreCase);
                _packages = new(s.Packages ?? [], StringComparer.OrdinalIgnoreCase);
                foreach (var package in _packages.Values.Where(p => p.State == "submitting")) package.State = "unknown";
            }
            RestorePackages();
            if (_dropped.Count > 0) Emit(new("core.queueRestored", _dropped.Count));
        }
        catch { }
    }

    void SaveQueue()
    {
        try
        {
            SaveQueueRequired();
        }
        catch (Exception e) { Emit(new("core.queueSaveFailed", Localization.ExceptionMessage(e))); }
    }

    void SaveQueueRequired()
    {
        lock (_dropped) Settings.AtomicWrite(QueueFile, JsonSerializer.Serialize(new Saved([.. _dropped.Where(File.Exists)], [.. _removed], _passwords, _started, [.. _done], _packages)));
    }

    // jogo saiu da fila: esquece senha e envios começados
    void Forget(string key) { lock (_dropped) { _removed.Add(key); _passwords.Remove(key); _started.Remove(key); _done.Remove(key); } }

    public void AddFiles(IEnumerable<string> paths)
    {
        var list = paths.Where(File.Exists).ToList();
        var keys = Archives.Group(list).Keys.ToList();
        lock (_dropped) { foreach (var p in list) _dropped.Add(p); foreach (var k in keys) { _removed.Remove(k); _done.Remove(k); } }
        // adicionar de novo um jogo concluído = enviar de novo: o card volta para a checagem de partes
        lock (Lock)
        {
            foreach (var j in Jobs.Where(j => j.Package != null && keys.Contains(j.Key, StringComparer.OrdinalIgnoreCase)))
                _checkedPackages.Remove(j.Package!.Identity);
            foreach (var j in Jobs.Where(j => j.Stage == Stage.Verificado && keys.Contains(j.Key, StringComparer.OrdinalIgnoreCase)))
            {
                j.Detail = ""; j.Progress = 0; j.Stage = Stage.AguardandoPartes;
            }
        }
        SaveQueue();
    }

    /// <summary>Tira da fila (cancelando se estiver rodando). Volta se os arquivos forem adicionados de novo.</summary>
    public void Remove(Job job)
    {
        job.Cts?.Cancel();
        Forget(job.Key);
        lock (Lock) Jobs.Remove(job);
        SaveQueue();
        JobLog(job, new("core.removed"));
    }

    public async Task RunAsync(CancellationToken stop)
    {
        var scan = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try { await ScanAsync(); } catch (Exception e) { Emit(new("core.scanError", Localization.ExceptionMessage(e))); }
                await Task.Delay(1000, stop).ContinueWith(_ => { });
            }
        });
        while (!stop.IsCancellationRequested)
        {
            Job? next;
            if (_installRequests.TryDequeue(out var requested)) { await SubmitPackageAsync(requested); continue; }
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
        RestorePackages();
        await ValidateRestoredPackagesAsync();
        var groups = CurrentGroups();
        lock (_dropped) foreach (var k in _removed) groups.Remove(k);
        lock (Lock)
        {
            // cards "aguardando" cujos arquivos sumiram
            foreach (var j in Jobs.Where(j => j.Stage == Stage.AguardandoPartes && !groups.ContainsKey(j.Key)).ToList()) Jobs.Remove(j);
            foreach (var g in groups.Values.Where(g => !Jobs.Any(j => j.Key == g.Key)))
            {
                string? pw; bool done;
                lock (_dropped) { pw = Secret.Unprotect(_passwords.GetValueOrDefault(g.Key)); done = _done.Contains(g.Key); } // lembrados de antes de fechar o app
                var job = new Job { Key = g.Key, Name = g.Name, Stage = done ? Stage.Verificado : Stage.AguardandoPartes, ArchivePassword = pw, Parts = [.. g.Parts] };
                if (done) job.Finish(new Message("core.restoredComplete"));
                Jobs.Add(job);
            }
        }
        Message Missing(ArchiveGroup g, IEnumerable<string> names) => new("core.missing", string.Join(", ", names.Select(n => n.StartsWith(g.Name + ".") ? n[(g.Name.Length + 1)..] : n)));

        foreach (var g in groups.Values)
        {
            Job? job;
            lock (Lock) job = Jobs.FirstOrDefault(j => j.Key == g.Key);
            if (job?.Stage != Stage.AguardandoPartes) continue; // removido no meio da varredura
            job.Parts = [.. g.Parts];
            job.MainFile = g.Main ?? "";
            if (!g.CompleteByName)
            {
                job.SetDetail(g.Missing() is { Count: > 0 } miss ? Missing(g, miss) : new("core.partsFound", job.Parts.Count));
                _stable.Remove(g.Key); continue;
            }

            var sig = Sig(job.Parts);
            if (!_stable.TryGetValue(g.Key, out var st) || st.sig != sig) { _stable[g.Key] = (sig, DateTime.UtcNow); job.SetDetail(new("core.stabilizing")); continue; }
            if ((DateTime.UtcNow - st.since).TotalSeconds < StableSeconds) continue;

            if (!Archives.IsImage(job.MainFile) && !Archives.IsPackage(job.MainFile) && await Archives.ListAsync(job.MainFile, job.ArchivePassword) is (ListResult.Incomplete, _, var lost))
            {
                job.SetDetail(lost.Length > 0 ? Missing(g, lost) : new("core.incompleteParts")); // .001: o 7z não diz qual
                _stable[g.Key] = (sig, DateTime.UtcNow); // reavalia depois de outra janela
                continue;
            }
            // Um volume pode ter chegado durante o 7z l: o 7z viu, mas a lista de partes não. Recomeça a janela.
            if (CurrentGroups().GetValueOrDefault(g.Key) is not { } fresh || Sig([.. fresh.Parts]) != sig) { _stable.Remove(g.Key); continue; }
            job.SetDetail(new("core.partsSize", job.Parts.Count, new Message("core.size", job.Parts.Sum(p => new FileInfo(p).Length))));
            job.Stage = Stage.NaFila;
            JobLog(job, new("core.queued", job.Parts.Count));
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
            job.SetDetail(new("core.reading"));
            // .exfat solto: um item só, lido direto do disco
            var loose = Archives.IsImage(job.MainFile) || Archives.IsPackage(job.MainFile);
            var (res, entries, _) = loose ? (ListResult.Ok, [new(Path.GetFileName(job.MainFile), new FileInfo(job.MainFile).Length, false, false)], [])
                : await Archives.ListAsync(job.MainFile, job.ArchivePassword);
            // senha lembrada → senhas conhecidas (em silêncio) → diálogo
            var known = new Queue<string>(settings.KnownPasswords.ToList());
            var asked = false;
            while (res == ListResult.NeedPassword || entries.FirstOrDefault(e => e.Encrypted && !e.IsDir) is { } enc && !await Archives.PasswordOkAsync(job.MainFile, job.ArchivePassword, enc))
            {
                if (known.TryDequeue(out var k)) job.ArchivePassword = k;
                else
                {
                    JobLog(job, new("core.passwordNeeded"));
                    if (!asked) job.ArchivePassword = null; // o diálogo diz "senha incorreta" só depois de uma tentativa dele
                    job.ArchivePassword = await askPassword(job) ?? throw new LocalizedException(new("core.passwordMissing"));
                    asked = true;
                }
                (res, entries, _) = await Archives.ListAsync(job.MainFile, job.ArchivePassword);
            }
            if (res != ListResult.Ok) throw new LocalizedException(new("core.corruptArchive"));
            if (job.ArchivePassword is { } okPw)
            {
                if (asked && !settings.KnownPasswords.Contains(okPw))
                {
                    settings.KnownPasswords.Add(okPw);
                    try { settings.Save(); } catch { }
                    JobLog(job, new("core.passwordLearned"));
                }
                lock (_dropped) _passwords[job.Key] = Secret.Protect(okPw);
                SaveQueue();
            }
            ct.ThrowIfCancellationRequested();

            if (Archives.PackagePlan(entries) is { } package)
            {
                await ProcessPackageAsync(job, package, loose, ct);
                return;
            }

            // pasta de jogo → RemoteDir/<jogo>; senão imagem(ns) .exfat → ImageDir/<nome>.exfat
            string remote; string?[] targets;
            if (Archives.Plan(entries, job.Name) is (var gameName, var t)) (remote, targets) = (settings.RemoteDir.TrimEnd('/') + "/" + gameName, t);
            else (remote, targets) = (settings.ImageDir.TrimEnd('/'), Archives.ImagePlan(entries) ?? throw new LocalizedException(new("core.noGame")));
            await LoadCover(job, entries, targets);

            // Publicação atômica: param.json/param.sfo sobem com sufixo e só ganham o nome final depois que todo o
            // resto foi enviado e conferido. Antes disso nenhum loader (ShadowMount+) vê um jogo pela metade.
            var final = targets.Select(t => t == null ? null : remote + "/" + t).ToArray();
            var paths = final.Select((p, i) => p != null && Held(targets[i]!) ? p + PartSuffix : p).ToArray();
            var heldFinal = final.Select((p, i) => p != paths[i] ? p : null).ToArray();
            bool Lenient(int i) => job.Force && Backport(targets[i]!);

            job.SetDetail(new("core.checking"));
            if (!job.Force && (await Ftp.RemoteStateAsync(settings, heldFinal, ct)).have.Any(n => n >= 0))
            {
                job.Installed = true;
                throw new LocalizedException(new("core.installed"));
            }

            var decCount = entries.Count(e => !e.IsDir && e.Path.Split('/').Contains("dec", StringComparer.OrdinalIgnoreCase));
            // 2ª volta só se o APPE do servidor falhar: aí o arquivo parcial é reenviado inteiro com STOR.
            // Erro de rede (timeout, conexão caída) refaz a volta sozinho: reconsulta o PS5 e manda só o que falta.
            var noAppend = false;
            for (var tries = 0; ; tries++)
            {
                job.SetDetail(new("core.checking"));
                var (have, append, probe) = await Ftp.RemoteStateAsync(settings, paths, ct, noAppend);
                // Parcial só continua (APPE) se foi este app que começou esse envio, com esse tamanho final.
                // Arquivo menor que não é nosso pode ser outra versão: continuar daria um arquivo corrompido do tamanho certo.
                var foreign = 0;
                lock (_dropped)
                    for (var i = 0; i < paths.Length; i++)
                        if (paths[i] is { } rp && have[i] > 0 && have[i] < entries[i].Size
                            && _started.GetValueOrDefault(job.Key)?.GetValueOrDefault(rp) != entries[i].Size) { have[i] = -1; foreign++; }
                var need = Enumerable.Range(0, entries.Count).Where(i => paths[i] != null && have[i] != entries[i].Size).ToList();
                var already = paths.Count(t => t != null) - need.Count;

                ct.ThrowIfCancellationRequested(); // cancelado (pausa/transferir agora) durante a conferência: não sobrescreve o Stage
                job.Stage = Stage.Enviando; job.ResetRate();
                job.Detail = remote;
                Message Combine(Message left, Message right) => new("core.concat", left, right);
                var sending = new Message("core.sending", need.Count, remote, settings.Connections);
                if (already > 0) sending = Combine(sending, new("core.alreadyComplete", already));
                if (foreign > 0) sending = Combine(sending, new("core.foreignPartial", foreign));
                if (decCount > 0) sending = Combine(sending, new("core.decOverlay", decCount));
                sending = Combine(sending, new(append ? "core.appendPartial" : "core.resendPartial"));
                JobLog(job, Combine(sending, new("core.probe", probe[(probe.IndexOf('→') + 1)..].Trim())));
                if (need.Count == 0) break;

                listFile ??= Path.Combine(Path.GetTempPath(), $"ferry-{Guid.NewGuid():N}.txt");
                File.WriteAllLines(listFile, need.Select(i => Archives.Native(entries[i].Path)));
                using var p = loose ? null : Archives.OpenStream(job.MainFile, job.ArchivePassword, listFile);
                using var src = p?.StandardOutput.BaseStream ?? File.OpenRead(job.MainFile);
                using var reg = ct.Register(() => { try { p?.Kill(true); } catch { } });
                var err = p?.StandardError.ReadToEndAsync();
                Exception? fail = null;
                void Started(int i)
                {
                    lock (_dropped) { if (!_started.TryGetValue(job.Key, out var d)) _started[job.Key] = d = []; d[paths[i]!] = entries[i].Size; }
                    SaveQueue();
                }
                try
                {
                    await Ftp.StreamAsync(settings, src, entries, paths, have, append, need, job.Report,
                        f => job.CurrentFile = f[(remote.Length + 1)..], Started, Lenient, m => log($"[{job.Name}] {m}"), ct, m => JobLog(job, m));
                }
                catch (Exception e) { fail = e; try { p?.Kill(true); } catch { } }
                if (p != null) await p.WaitForExitAsync();
                ct.ThrowIfCancellationRequested();
                var sevenErr = p is { ExitCode: not 0 } ? (await err!).Trim() : "";
                // Erro do lado do 7z (senha, CRC, volume ruim) aparece como stream curto + exit != 0.
                if ((fail is null || fail is LocalizedException { MessageData.Key: "core.archive.outputShort" or "core.archive.outputLong" }) && sevenErr != "") throw new LocalizedException(new("core.archive.failed", sevenErr));
                if (fail != null && append && !noAppend && fail is LocalizedException { MessageData.Key: "core.ftp.sendFailed" } ftpFailure && Equals(ftpFailure.MessageData.Args[1], "APPE"))
                {
                    JobLog(job, new("core.appeRejected", Localization.ExceptionMessage(fail)));
                    noAppend = true;
                    continue;
                }
                if (fail != null && tries < 3 && Transient(fail))
                {
                    JobLog(job, new("core.networkRetry", Localization.ExceptionMessage(fail), tries + 1));
                    await Task.Delay(3000, ct);
                    continue;
                }
                if (fail != null) throw fail;
                break;
            }

            job.SetDetail(new("core.verifying"));
            var warned = await Verify(job, entries, paths, Lenient, ct);
            // tudo conferido: agora o jogo "aparece" para o loader
            for (var i = 0; i < paths.Length; i++)
                if (heldFinal[i] is { } to) await Ftp.RenameAsync(settings, paths[i]!, to, ct);
            await Verify(job, entries, heldFinal, Lenient, ct);
            lock (_dropped) { _started.Remove(job.Key); _done.Add(job.Key); }
            job.Force = job.Installed = false;
            job.Stage = Stage.Verificado;
            job.Finish(new Message("core.concat", remote, warned > 0 ? new Message("core.completeBackport", warned) : new Message("core.raw", "")));
            SaveQueue();
            JobLog(job, new("core.uploadVerified"));
            Done?.Invoke(job);
            if (settings.DeleteOriginal)
            {
                foreach (var f in job.Parts) File.Delete(f);
                SaveQueue();
                JobLog(job, new("core.originalsDeleted", job.Parts.Count));
            }
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            var message = Localization.ExceptionMessage(e);
            job.Stage = job.Package?.State == "prepared" ? Stage.PacotePronto : Stage.Erro; job.SetDetail(message); job.CurrentFile = ""; JobLog(job, new("core.error", message));
            Done?.Invoke(job);
        }
        catch { if (job.Package?.State == "prepared") SetPackageStage(job); } // Preserve a published PKG even if cancellation races publication.
        finally { if (listFile != null) try { File.Delete(listFile); } catch { } }
    }

    static bool Transient(Exception e)
    {
        for (Exception? x = e; x != null; x = x.InnerException)
            if (x is TimeoutException or System.Net.Sockets.SocketException || x is IOException and not EndOfStreamException) return true;
        return false;
    }

    /// <summary>Confere o tamanho no PS5 de cada caminho; devolve quantas divergências viraram só aviso (backport).</summary>
    async Task<int> Verify(Job job, List<Entry> entries, string?[] paths, Func<int, bool> lenient, CancellationToken ct)
    {
        var (have, _, _) = await Ftp.RemoteStateAsync(settings, paths, ct);
        var bad = Enumerable.Range(0, paths.Length).Where(i => paths[i] != null && have[i] != entries[i].Size).ToList();
        Message Line(int i) => new("core.verifyLine", paths[i], entries[i].Size, have[i] < 0 ? new Message("core.notExists") : have[i]);
        foreach (var i in bad.Where(lenient)) JobLog(job, new("core.verifyBackport", Line(i)));
        var real = bad.Where(i => !lenient(i)).ToList();
        if (real.Count > 0)
        {
            var lines = real.Take(5).Select(Line).Aggregate((a, b) => new Message("core.semicolon", a, b));
            throw new LocalizedException(new("core.verifyFailed", real.Count, lines));
        }
        return bad.Count;
    }

    /// Capa e título de dentro do arquivo (param.sfo + icon0.png, pequenos). Sem eles o card mostra o nome do arquivo.
    async Task LoadCover(Job job, List<Entry> entries, string?[] targets)
    {
        if (job.Title != "" || job.Icon != null) return;
        var idx = Enumerable.Range(0, entries.Count)
            .Where(i => targets[i] is "sce_sys/param.sfo" or "sce_sys/icon0.png" && entries[i].Size is > 0 and <= Ftp.SmallFile).ToList();
        if (idx.Count == 0 || await Archives.ReadSmallAsync(job.MainFile, job.ArchivePassword, [.. idx.Select(i => entries[i])]) is not { } data) return;
        for (var k = 0; k < idx.Count; k++)
            if (targets[idx[k]]!.EndsWith(".sfo")) (job.Title, job.TitleId) = Archives.ParseSfo(data[k]);
            else job.Icon = data[k];
    }

    public void Pause(Job job) { if (!job.CanPause) return; job.Stage = Stage.Pausado; job.Cts?.Cancel(); JobLog(job, new("core.paused")); }
    public void Resume(Job job) { if (!job.CanResume) return; job.Stage = Stage.NaFila; JobLog(job, new("core.resumed")); }

    // "Transferir agora": passa na frente da fila. O que estava enviando volta para a fila (NaFila, não Pausado):
    // o Cts cancelado cai no catch vazio do ProcessAsync e ele retoma depois, só com o que falta.
    public void SendNow(Job job)
    {
        if (!job.CanSendNow) return;
        Job? active;
        lock (Lock)
        {
            active = Jobs.FirstOrDefault(j => j.CanPause && j.IsActive && j != job);
            Jobs.Move(Jobs.IndexOf(job), 0);
            if (active != null) { Jobs.Move(Jobs.IndexOf(active), 1); active.Stage = Stage.NaFila; active.Cts?.Cancel(); }
            job.Stage = Stage.NaFila;
        }
        JobLog(job, new("core.concat", new Message("core.sendNow"), active != null ? new Message("core.backToQueue", active.Name) : new Message("core.raw", "")));
    }

    public void Cancel(Job job)
    {
        if (!job.CanCancel) return;
        job.Stage = Stage.Cancelado;
        job.Cts?.Cancel();
        job.ResetRate();
        JobLog(job, new("core.cancelled"));
    }

    public void ClearFinished()
    {
        lock (Lock) foreach (var j in Jobs.Where(j => j.Stage is Stage.Verificado or Stage.InstalacaoSolicitada).ToList()) { Jobs.Remove(j); Forget(j.Key); }
        SaveQueue();
    }

    // Volta para a checagem de partes; o envio pula o que já está no PS5. Jogo já instalado: "reenviar mesmo assim".
    public void Retry(Job job)
    {
        if (!job.CanRetry) return;
        job.Force |= job.Installed;
        job.Detail = ""; job.Progress = 0; job.Stage = Stage.AguardandoPartes;
        JobLog(job, new("core.concat", new Message("core.retry"), job.Force ? new Message("core.overwriteInstalled") : new Message("core.raw", "")));
    }
}
