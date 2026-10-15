using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;

namespace Ferry;

/// Fila em grupos, igual à web: Precisa de você → Enviando → Na fila → Concluídos.
/// A ordem dentro de cada grupo é a do motor (a que o usuário escolheu com "Transferir agora").
public sealed class QueueGroups : INotifyPropertyChanged
{
    public static QueueGroups Current { get; } = new();
    public static readonly string[] Order = ["attention", "active", "queue", "done"];
    public event PropertyChangedEventHandler? PropertyChanged;

    bool _doneExpanded;
    /// Concluídos começam recolhidos; a escolha vale enquanto o app está aberto (sobrevive à reorganização dos grupos).
    public bool DoneExpanded
    {
        get => _doneExpanded;
        set { if (_doneExpanded == value) return; _doneExpanded = value; PropertyChanged?.Invoke(this, new(nameof(DoneExpanded))); }
    }

    public static string Of(Job job) => job.Stage switch
    {
        Stage.Erro or Stage.PacotePronto or Stage.VerifiqueNoPs5 => "attention",
        _ when job.IsActive => "active",
        Stage.Extraindo or Stage.Enviando or Stage.SolicitandoInstalacao => "active",
        Stage.Verificado or Stage.InstalacaoSolicitada or Stage.Instalado => "done",
        _ => "queue",
    };

    /// Posição de cada jogo na fila do motor quando o grupo foi calculado; jogo novo vai para o fim até o próximo cálculo.
    public sealed class Sort(IReadOnlyDictionary<Job, int> positions) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not Job a || y is not Job b) return 0;
            var byGroup = Array.IndexOf(Order, Of(a)).CompareTo(Array.IndexOf(Order, Of(b)));
            return byGroup != 0 ? byGroup : Position(a).CompareTo(Position(b));
        }
        int Position(Job job) => positions.TryGetValue(job, out var index) ? index : int.MaxValue;
    }
}

public sealed class JobGroupConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is Job job ? QueueGroups.Of(job) : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// Título do grupo no idioma atual (muda junto com o idioma, sem recriar a lista).
public sealed class GroupTitleConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values[0] is string key ? WpfText.Current.T("ui.group." + key) : "";
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
