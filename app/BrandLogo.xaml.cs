using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Ferry;

public partial class BrandLogo : UserControl
{
    Storyboard? _opening;
    Storyboard? _hover;
    bool _introduced;
    bool _openingPlaying;

    public BrandLogo() => InitializeComponent();

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemPreferenceChanged;
        SystemParameters.StaticPropertyChanged += OnSystemPreferenceChanged;
        if (_introduced || !SystemParameters.ClientAreaAnimation) return;
        _introduced = true;
        _openingPlaying = true;
        _opening = ((Storyboard)FindResource("FerryBrandEnter")).Clone();
        _opening.Completed += (_, _) => _openingPlaying = false;
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
        _hover?.Remove(this);
        _openingPlaying = false;
    }

    void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (_openingPlaying || !SystemParameters.ClientAreaAnimation) return;
        _hover ??= CreateHover();
        _hover.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
    }

    static Storyboard CreateHover()
    {
        var duration = TimeSpan.FromMilliseconds(350);
        var motion = new DoubleAnimation(-200, 240, duration) { FillBehavior = FillBehavior.Stop };
        Storyboard.SetTargetName(motion, "ShineOffset");
        Storyboard.SetTargetProperty(motion, new PropertyPath("X"));
        var opacity = new DoubleAnimationUsingKeyFrames { Duration = duration, FillBehavior = FillBehavior.Stop };
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280))));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(duration)));
        Storyboard.SetTargetName(opacity, "Shine");
        Storyboard.SetTargetProperty(opacity, new PropertyPath("Opacity"));
        var storyboard = new Storyboard();
        storyboard.Children.Add(motion);
        storyboard.Children.Add(opacity);
        return storyboard;
    }
}
