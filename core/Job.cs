using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Ferry;

public enum Stage { AguardandoPartes, NaFila, Extraindo, Enviando, Verificado, Pausado, Cancelado, Erro, PacotePronto, SolicitandoInstalacao, InstalacaoSolicitada, VerifiqueNoPs5 }

public class Job : INotifyPropertyChanged
{
    public required string Key { get; init; }       // nome-base do grupo (sem sufixo de volume)
    public required string Name { get; init; }
    public string MainFile { get; set; } = "";      // arquivo que o 7z abre
    public List<string> Parts { get; set; } = [];
    public string? ArchivePassword { get; set; }
    public CancellationTokenSource? Cts { get; set; }
    public bool Installed { get; set; } // destino já tinha o jogo publicado (param.json/sfo com nome final)
    public bool Force { get; set; }     // usuário mandou reenviar por cima do jogo instalado
    public PackagePreparation? Package { get; internal set; }

    Stage _stage;
    public Stage Stage
    {
        get => _stage;
        set
        {
            _stage = value;
            foreach (var p in new[] { nameof(Stage), nameof(Package), nameof(StageMessage), nameof(StageText), nameof(CanPause), nameof(CanResume), nameof(CanCancel), nameof(CanRetry), nameof(CanSendNow), nameof(CanRequestInstall), nameof(IsActive) }) Changed(p);
        }
    }

    public Message StageMessage => new("core.stage." + Stage);
    public string StageText => Enum.IsDefined(Stage) ? StageMessage.Render() : new Message("core.stage.Erro").Render();

    public bool IsActive => Stage is Stage.Extraindo or Stage.Enviando;
    public bool CanPause => Stage is Stage.NaFila or Stage.Extraindo or Stage.Enviando;
    public bool CanResume => Stage == Stage.Pausado;
    public bool CanCancel => Stage is Stage.AguardandoPartes or Stage.NaFila or Stage.Extraindo or Stage.Enviando or Stage.Pausado;
    public bool CanRetry => (Stage is Stage.Erro or Stage.Cancelado) && Package?.State is not ("prepared" or "submitting" or "submitted" or "unknown");
    public bool CanRequestInstall => Stage is Stage.PacotePronto or Stage.VerifiqueNoPs5;
    public bool CanSendNow => Stage is Stage.NaFila or Stage.Pausado; // "Transferir agora": passa na frente do que está enviando

    double _progress; public double Progress { get => _progress; set { _progress = value; Changed(); } }
    string _detail = "";
    public Message? DetailMessage { get; private set; }
    public string Detail { get => _detail; set { DetailMessage = null; _detail = value; Changed(); Changed(nameof(DetailMessage)); } }
    public void SetDetail(Message message) { _detail = message.Render(); DetailMessage = message; Changed(nameof(Detail)); Changed(nameof(DetailMessage)); }
    public long DoneBytes { get; private set; }
    public long TotalBytes { get; private set; }
    public double? SecondsRemaining { get; private set; }
    // números grandes do card: "18.21 de 29.20 GB", "87.3" + "MB/s", "2:09" + "min restantes" ("—" parado)
    string _amount = ""; public string Amount { get => _amount; set { _amount = value; Changed(); } }
    string _rateValue = "—"; public string RateValue { get => _rateValue; set { _rateValue = value; Changed(); } }
    string _rateUnit = "MB/s"; public string RateUnit { get => _rateUnit; set { _rateUnit = value; Changed(); } }
    string _etaValue = "—"; public string EtaValue { get => _etaValue; set { _etaValue = value; Changed(); } }
    string _etaUnit = new Message("core.remaining").Render(); public string EtaUnit { get => _etaUnit; set { _etaUnit = value; Changed(); } }
    string _file = ""; public string CurrentFile { get => _file; set { _file = value; Changed(); } }
    // do sce_sys/param.sfo e icon0.png de dentro do arquivo ("" / null = não deu para ler)
    string _title = ""; public string Title { get => _title; set { _title = value; Changed(); } }
    string _titleId = ""; public string TitleId { get => _titleId; set { _titleId = value; Changed(); } }
    byte[]? _icon; public byte[]? Icon { get => _icon; set { _icon = value; Changed(); } }
    public double Rate { get; private set; } // bytes/s, lido pelo resumo da janela

    // Chamado de várias threads e a cada poucos KB: atualiza a UI no máximo 4x/s (senão o dispatcher do WPF afoga).
    readonly object _gate = new();
    long _lastBytes; DateTime _lastTime = DateTime.UtcNow, _lastUi;
    public void Report(long done, long total)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastUi).TotalMilliseconds < 250 && done < total) return;
            _lastUi = now;
            var dt = (now - _lastTime).TotalSeconds;
            if (dt >= 1)
            {
                var inst = Math.Max(0, done - _lastBytes) / dt;
                Rate = Rate == 0 ? inst : Rate * 0.7 + inst * 0.3;
                _lastBytes = done; _lastTime = now;
            }
            DoneBytes = done; TotalBytes = total; SecondsRemaining = Rate > 0 && double.IsFinite(Rate) ? Math.Min(359999, Math.Max(0, total - done) / Rate) : null;
            Progress = total > 0 ? 100.0 * done / total : 0;
            Amount = new Message("core.amount", Size(done), Size(total)).Render();
            if (Rate <= 0) return;
            var r = Size((long)Rate).Split(' ');
            (RateValue, RateUnit) = (r[0], r[1] + "/s");
            (EtaValue, EtaUnit) = Time((total - done) / Rate);
        }
    }

    public void ResetRate() { lock (_gate) { _lastBytes = 0; Rate = 0; _lastTime = DateTime.UtcNow; _lastUi = default; } Idle(); DoneBytes = TotalBytes = 0; Amount = ""; CurrentFile = ""; }

    public void Finish(string detail) { Rate = 0; Progress = 100; Idle(); CurrentFile = ""; Detail = detail; }

    public void Finish(Message detail) { Finish(detail.Render()); SetDetail(detail); }

    void Idle() { SecondsRemaining = null; (RateValue, RateUnit, EtaValue, EtaUnit) = ("—", "MB/s", "—", new Message("core.remaining").Render()); }

    public static string Size(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):0.00} GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{b / 1024.0:0} KB",
        _ => $"{b} B",
    };

    static (string, string) Time(double s)
    {
        var t = TimeSpan.FromSeconds(Math.Min(s, 359999));
        return t.TotalHours >= 1 ? ($"{(int)t.TotalHours}:{t.Minutes:00}", new Message("core.hoursRemaining").Render()) : t.TotalMinutes >= 1 ? ($"{t.Minutes}:{t.Seconds:00}", new Message("core.minutesRemaining").Render()) : ($"{t.Seconds}", new Message("core.secondsRemaining").Render());
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new(p));
}
