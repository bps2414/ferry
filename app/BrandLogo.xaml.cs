using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Ferry;

public partial class BrandLogo : UserControl
{
    Storyboard? _opening;
    bool _introduced;

    public BrandLogo() => InitializeComponent();

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemPreferenceChanged;
        SystemParameters.StaticPropertyChanged += OnSystemPreferenceChanged;
        if (_introduced || !SystemParameters.ClientAreaAnimation) return;
        _introduced = true;
        _opening = ((Storyboard)FindResource("FerryBrandEnter")).Clone();
        _opening.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
    }

    void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemPreferenceChanged;
        StopMotion();
    }

    void OnSystemPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation) && !SystemParameters.ClientAreaAnimation)
            Dispatcher.Invoke(StopMotion);
    }

    void StopMotion()
    {
        _opening?.Remove(this);
    }
}
