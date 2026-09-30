using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ferry.Web;

/// <summary>
/// Ponte entre a Engine e o navegador: fotografia da fila para o SSE, log, avisos (concluído, erro, senha)
/// e a senha pedida ao navegador no lugar do diálogo do WPF.
/// </summary>
public class Hub(Settings settings)
{
    public Engine Engine { get; set; } = null!;
    public Settings Settings => settings;

    // ---------- log e avisos: listas com número de sequência; cada conexão SSE manda o que ainda não mandou ----------
    public record Line(long Seq, string Time, Message Message)
    {
        public string Text => $"{Time}  {Message.Render()}";
    }
    public record Notice(long Seq, string Kind, Message TitleMessage, Message TextMessage)
    {
        public string Title => TitleMessage.Render();
        public string Text => TextMessage.Render();
    }
    readonly List<Line> _log = [];
    readonly List<Notice> _notices = [];
    long _seq;

    public void Log(string msg) => Log(new Message("core.raw", msg));

    public void Log(Message msg)
    {
        FileLog.Write(msg.Render());
        lock (_log)
        {
            _log.Add(new(Interlocked.Increment(ref _seq), DateTime.Now.ToString("HH:mm:ss"), msg));
            if (_log.Count > 2000) _log.RemoveRange(0, _log.Count - 2000);
        }
    }

    public List<Line> LogSince(long seq) { lock (_log) return _log.Where(l => l.Seq > seq).ToList(); }
    public void ClearLog() { lock (_log) _log.Clear(); }

    void Notify(string kind, Message title, Message text)
    {
        lock (_notices)
        {
            _notices.Add(new(Interlocked.Increment(ref _seq), kind, title, text));
            if (_notices.Count > 100) _notices.RemoveAt(0);
        }
    }

    public List<Notice> NoticesSince(long seq) { lock (_notices) return _notices.Where(n => n.Seq > seq).ToList(); }
    public long LastSeq => Interlocked.Read(ref _seq);

    public void OnDone(Job job)
    {
        if (job.Stage == Stage.Verificado) Notify("done", new("web.doneTitle"), new("web.doneText", job.Title != "" ? job.Title : job.Name));
        else if (job.Stage == Stage.InstalacaoSolicitada) Notify("installation_requested", new("web.installationTitle"), new("web.installationText", job.Title != "" ? job.Title : job.Name));
        else if (job.Stage == Stage.VerifiqueNoPs5) Notify("unknown", new("web.installationUnknownTitle"), new("web.installationUnknownText", job.Title != "" ? job.Title : job.Name));
        else Notify("error", new("web.errorTitle"), new("web.errorText", job.Name, job.DetailMessage ?? new Message("core.raw", Short(job.Detail))));
    }

    static string Short(string s)
    {
        var line = s.Split('\n')[0].Trim();
        return line.Length > 120 ? line[..120] + "…" : line;
    }

    // ---------- senha: o ProcessAsync espera até o navegador responder (ou o jogo ser cancelado/removido) ----------
    record Ask(long Seq, Job Job, bool Wrong, TaskCompletionSource<string?> Answer); // Seq: cada pedido é um diálogo novo no navegador
    readonly ConcurrentDictionary<string, Ask> _asks = [];

    public Task<string?> AskPassword(Job job)
    {
        var ask = new Ask(Interlocked.Increment(ref _seq), job, job.ArchivePassword != null, new(TaskCreationOptions.RunContinuationsAsynchronously));
        _asks[Id(job)] = ask;
        Notify("password", new("web.passwordTitle"), new("web.passwordText", job.Name));
        return ask.Answer.Task;
    }

    /// null = cancelou (o jogo vai para Erro "Senha não informada", como no diálogo).
    public bool Answer(string id, string? password)
    {
        if (!_asks.TryRemove(id, out var ask)) return false;
        ask.Answer.TrySetResult(string.IsNullOrEmpty(password) ? null : password);
        return true;
    }

    // ---------- estado do PS5 (cartão da barra lateral) e PS5 achado na rede ----------
    public string Ps5Status { get; private set; } = "unknown"; // unknown | testing | online | offline
    public Message? TestMessageData { get; private set; }
    public string TestMessage => TestMessageData?.Render() ?? "";
    public (string Ip, int Port)? Found { get; set; }

    long _test; // só o teste mais recente mexe no cartão (um teste antigo, de outro IP, pode terminar depois)

    public async Task<bool> TestConnection()
    {
        var mine = Interlocked.Increment(ref _test);
        Ps5Status = "testing";
        Message msg; bool ok;
        try { msg = await Ftp.TestMessageAsync(settings); ok = true; }
        catch (Exception e) { msg = new("web.testFailed", Localization.ExceptionMessage(e)); ok = false; }
        Log(ok ? new Message("web.testLog", msg) : new Message("web.testFailedLog", msg.Args[0]));
        if (mine == Interlocked.Read(ref _test)) (TestMessageData, Ps5Status) = (msg, ok ? "online" : "offline");
        return ok;
    }

    // Só oferece o que achou; nunca troca o IP sozinho.
    public async Task<Message> Discover()
    {
        var r = await Task.Run(Discovery.FindPs5);
        if (r is { } f && !(f.Ip == settings.Host && f.Port == settings.Port))
        {
            Found = f;
            Log(new Message("web.foundLog", f.Ip, f.Port));
            return new("web.found", f.Ip, f.Port);
        }
        Log(new Message(r is null ? "web.notFoundLog" : "web.sameFoundLog"));
        return new(r is null ? "web.notFound" : "web.sameFound");
    }

    // ---------- fila ----------
    public static string Id(Job j) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(j.Key)))[..12].ToLowerInvariant();

    public Job? Find(string id) { lock (Engine.Lock) return Engine.Jobs.FirstOrDefault(j => Id(j) == id); }

    public bool Act(string id, string action)
    {
        if (Find(id) is not { } job) return false;
        switch (action)
        {
            case "pause": if (!job.CanPause) return false; Engine.Pause(job); break;
            case "resume": if (!job.CanResume) return false; Engine.Resume(job); break;
            case "cancel": if (!job.CanCancel) return false; Engine.Cancel(job); break;
            case "retry": if (!job.CanRetry) return false; Engine.Retry(job); break;
            case "sendnow": if (!job.CanSendNow) return false; Engine.SendNow(job); break;
            case "remove": Engine.Remove(job); break;
            default: return false;
        }
        // saiu da fila ou parou esperando a senha: a espera termina (o ProcessAsync já foi cancelado)
        if (action is "pause" or "cancel" or "remove") Answer(id, null);
        return true;
    }

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// Fotografia do que a tela mostra. O SSE manda de novo só quando muda.
    public string Snapshot()
    {
        List<Job> jobs;
        lock (Engine.Lock) jobs = [.. Engine.Jobs];
        int Count(params Stage[] s) => jobs.Count(j => s.Contains(j.Stage));
        var ask = _asks.Values.FirstOrDefault();
        return JsonSerializer.Serialize(new
        {
            language = settings.Language,
            jobs = jobs.Select(j => new
            {
                id = Id(j), j.Name, j.Title, j.TitleId, packageFormat = j.Package?.Format, stage = j.Stage.ToString(), j.StageText, j.StageMessage, j.Detail, j.DetailMessage, j.CurrentFile,
                progress = Math.Round(j.Progress, 1), j.Amount, j.RateValue, j.RateUnit, j.EtaValue, j.EtaUnit,
                j.DoneBytes, j.TotalBytes, j.Rate, j.SecondsRemaining,
                icon = j.Icon is { } ic ? ic.Length : 0, j.IsActive, j.CanPause, j.CanResume, j.CanCancel, j.CanRetry, j.CanSendNow, j.CanRequestInstall,
            }),
            summary = new
            {
                sending = Count(Stage.Extraindo, Stage.Enviando), queued = Count(Stage.NaFila, Stage.AguardandoPartes, Stage.Pausado),
                done = Count(Stage.Verificado), requested = Count(Stage.InstalacaoSolicitada), ready = Count(Stage.PacotePronto), submitting = Count(Stage.SolicitandoInstalacao), unknown = Count(Stage.VerifiqueNoPs5),
                errors = Count(Stage.Erro), rate = (long)jobs.Where(j => j.IsActive).Sum(j => j.Rate),
            },
            ps5 = new { settings.Host, settings.Port, settings.RemoteDir, status = Ps5Status, message = TestMessage, messageData = TestMessageData, found = Found is { } f ? $"{f.Ip}:{f.Port}" : null },
            password = ask == null ? null : new { id = Id(ask.Job), ask.Job.Name, ask.Wrong, ask.Seq },
        }, Json);
    }
}
