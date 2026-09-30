using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Ferry;

/// Live presentation bindings: changing language never replaces a control or a Job.
public sealed class WpfText : INotifyPropertyChanged
{
    public static WpfText Current { get; } = new();
    public string SystemLocale { get; } = CultureInfo.CurrentUICulture.Name;
    public string AutomaticLocale => Localization.NormalizeLocale(SystemLocale);
    public string Locale { get; private set; } = "en";
    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");
    public event PropertyChangedEventHandler? PropertyChanged;

    WpfText()
    {
        foreach (var locale in new[] { "pt-BR", "en" })
        foreach (var folder in new[] { "ui", "app" })
        {
            using var stream = typeof(WpfText).Assembly.GetManifestResourceStream($"Ferry.{folder}.locales.{locale}.json")
                ?? throw new InvalidOperationException("Missing embedded catalog: " + folder + "/" + locale);
            Localization.AddCatalog(locale, JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!);
        }
    }

    public static string ResolveLocale(string? preference, string systemLocale) =>
        preference is "pt-BR" or "en" ? preference : Localization.NormalizeLocale(systemLocale);

    public void Apply(string? preference)
    {
        Locale = ResolveLocale(preference, SystemLocale);
        Culture = CultureInfo.GetCultureInfo(Locale == "pt-BR" ? "pt-BR" : "en-US");
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = Culture;
        PropertyChanged?.Invoke(this, new(nameof(Locale)));
        PropertyChanged?.Invoke(this, new(nameof(Culture)));
    }

    public string Render(Message message) => message.Render(Locale, Culture);
    public string T(string key, params object?[] args) => Render(new Message(key, args));
    public string Size(long bytes) => T("core.size", bytes);

    public static void Bind(DependencyObject target, DependencyProperty property, Message message) =>
        BindingOperations.SetBinding(target, property, new Binding(nameof(Locale))
        {
            Source = Current, Converter = new MessageConverter(), ConverterParameter = message,
            Mode = BindingMode.OneWay
        });

    sealed class MessageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Current.Render((Message)parameter);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding(nameof(WpfText.Locale))
    {
        Source = WpfText.Current, Converter = new TextConverter(), ConverterParameter = key, Mode = BindingMode.OneWay
    }.ProvideValue(serviceProvider);

    sealed class TextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => WpfText.Current.T((string)parameter);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}

public sealed class JobTextExtension(string field) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new MultiBinding { Converter = new JobTextConverter(), ConverterParameter = field, Mode = BindingMode.OneWay };
        binding.Bindings.Add(new Binding());
        binding.Bindings.Add(new Binding(nameof(WpfText.Locale)) { Source = WpfText.Current });
        // Raw counters don't notify separately; these presentation properties notify on every report.
        foreach (var property in new[] { "Stage", "Detail", "Progress", "Amount", "RateValue", "EtaValue" })
            binding.Bindings.Add(new Binding(property));
        return binding.ProvideValue(serviceProvider);
    }
}

public sealed class JobTextConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values[0] is not Job job) return "";
        var text = WpfText.Current;
        var rate = text.Size((long)job.Rate).Split(' ');
        var seconds = job.SecondsRemaining;
        var time = TimeSpan.FromSeconds(Math.Clamp(seconds ?? 0, 0, 359999));
        return parameter switch
        {
            "Stage" => text.Render(Enum.IsDefined(job.Stage) ? job.StageMessage : new("core.stage.Erro")),
            "Detail" => job.DetailMessage is { } detail ? text.Render(detail) : job.Detail,
            "Amount" => job.Amount.Length == 0 ? "" : text.T("core.amount", text.Size(job.DoneBytes), text.Size(job.TotalBytes)),
            "Progress" => job.Progress.ToString("0.0", text.Culture),
            "RateValue" => job.Rate > 0 ? rate[0] : "—",
            "RateUnit" => job.Rate > 0 ? rate[1] + "/s" : "MB/s",
            "EtaValue" => seconds == null ? "—" : time.TotalHours >= 1 ? string.Format(text.Culture, "{0}:{1:00}", (int)time.TotalHours, time.Minutes)
                : time.TotalMinutes >= 1 ? string.Format(text.Culture, "{0}:{1:00}", time.Minutes, time.Seconds) : time.Seconds.ToString(text.Culture),
            "EtaUnit" => text.T(seconds == null ? "core.remaining" : time.TotalHours >= 1 ? "core.hoursRemaining" : time.TotalMinutes >= 1 ? "core.minutesRemaining" : "core.secondsRemaining"),
            _ => throw new ArgumentException("Unknown job presentation field: " + parameter)
        };
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
