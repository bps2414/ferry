using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Ferry;

public enum Stage { AguardandoPartes, NaFila, Extraindo, Enviando, Verificado, Pausado, Cancelado, Erro }

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

    Stage _stage;
    public Stage Stage
    {
        get => _stage;
        set
        {
            _stage = value;
            foreach (var p in new[] { nameof(Stage), nameof(StageText), nameof(CanPause), nameof(CanResume), nameof(CanCancel), nameof(CanRetry), nameof(CanSendNow), nameof(IsActive) }) Changed(p);
        }
    }

    public string StageText => Stage switch
    {
        Stage.AguardandoPartes => "Aguardando partes",
        Stage.NaFila => "Na fila",
        Stage.Extraindo => "Lendo arquivo",
        Stage.Enviando => "Enviando",
        Stage.Verificado => "Concluído",
        Stage.Pausado => "Pausado",
        Stage.Cancelado => "Cancelado",
        _ => "Erro",
    };

    public bool IsActive => Stage is Stage.Extraindo or Stage.Enviando;
    public bool CanPause => Stage is Stage.NaFila or Stage.Extraindo or Stage.Enviando;
    public bool CanResume => Stage == Stage.Pausado;
    public bool CanCancel => Stage is Stage.AguardandoPartes or Stage.NaFila or Stage.Extraindo or Stage.Enviando or Stage.Pausado;
    public bool CanRetry => Stage is Stage.Erro or Stage.Cancelado;
    public bool CanSendNow => Stage is Stage.NaFila or Stage.Pausado; // "Transferir agora": passa na frente do que está enviando

    double _progress; public double Progress { get => _progress; set { _progress = value; Changed(); } }
    string _detail = ""; public string Detail { get => _detail; set { _detail = value; Changed(); } }
    // números grandes do card: "18.21 de 29.20 GB", "87.3" + "MB/s", "2:09" + "min restantes" ("—" parado)
    string _amount = ""; public string Amount { get => _amount; set { _amount = value; Changed(); } }
    string _rateValue = "—"; public string RateValue { get => _rateValue; set { _rateValue = value; Changed(); } }
    string _rateUnit = "MB/s"; public string RateUnit { get => _rateUnit; set { _rateUnit = value; Changed(); } }
    string _etaValue = "—"; public string EtaValue { get => _etaValue; set { _etaValue = value; Changed(); } }
    string _etaUnit = "restante"; public string EtaUnit { get => _etaUnit; set { _etaUnit = value; Changed(); } }
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
            Progress = total > 0 ? 100.0 * done / total : 0;
            Amount = $"{Size(done)} de {Size(total)}";
            if (Rate <= 0) return;
            var r = Size((long)Rate).Split(' ');
            (RateValue, RateUnit) = (r[0], r[1] + "/s");
            (EtaValue, EtaUnit) = Time((total - done) / Rate);
        }
    }

    public void ResetRate() { lock (_gate) { _lastBytes = 0; Rate = 0; _lastTime = DateTime.UtcNow; _lastUi = default; } Idle(); Amount = ""; CurrentFile = ""; }

    public void Finish(string detail) { Rate = 0; Progress = 100; Idle(); CurrentFile = ""; Detail = detail; }

    void Idle() { (RateValue, RateUnit, EtaValue, EtaUnit) = ("—", "MB/s", "—", "restante"); }

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
        return t.TotalHours >= 1 ? ($"{(int)t.TotalHours}:{t.Minutes:00}", "h restantes") : t.TotalMinutes >= 1 ? ($"{t.Minutes}:{t.Seconds:00}", "min restantes") : ($"{t.Seconds}", "s restantes");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new(p));
}
