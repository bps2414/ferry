using System.Globalization;
using System.Text.Json;

namespace Ferry;

public record Message(string Key, params object?[] Args)
{
    public string Render(string locale = "pt-BR") => Localization.Render(this, locale);
}

public sealed class LocalizedException(Message message, Exception? inner = null) : Exception(message.Render(), inner)
{
    public Message MessageData { get; } = message;
}

public static class Localization
{
    static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Catalogs = new()
    {
        ["pt-BR"] = Read("pt-BR"), ["en"] = Read("en")
    };

    public static string NormalizeLocale(string? locale) => locale?.StartsWith("pt", StringComparison.OrdinalIgnoreCase) == true ? "pt-BR" : "en";
    public static IReadOnlyDictionary<string, string> Catalog(string locale) => Catalogs[NormalizeLocale(locale)];
    public static void AddCatalog(string locale, IReadOnlyDictionary<string, string> entries)
    {
        var key = NormalizeLocale(locale);
        var merged = new Dictionary<string, string>(Catalogs[key]);
        foreach (var entry in entries) merged.Add(entry.Key, entry.Value);
        Catalogs[key] = merged;
    }
    static IReadOnlyDictionary<string, string> Read(string locale)
    {
        using var stream = typeof(Localization).Assembly.GetManifestResourceStream($"Ferry.locales.{locale}.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    public static string Render(Message message, string locale = "pt-BR")
    {
        if (message.Key == "core.size" && message.Args is [long bytes])
        {
            var numbers = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            if (NormalizeLocale(locale) == "pt-BR") numbers.NumberDecimalSeparator = ",";
            return bytes switch
            {
                >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.00", numbers) + " GB",
                >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0.0", numbers) + " MB",
                >= 1L << 10 => (bytes / 1024.0).ToString("0", numbers) + " KB",
                _ => bytes.ToString(numbers) + " B"
            };
        }
        var catalog = Catalog(locale);
        if (!catalog.TryGetValue(message.Key, out var template)) throw new KeyNotFoundException(message.Key);
        if (message.Args.Length > 0 && message.Args[0] is byte or short or int or long or float or double or decimal)
        {
            var suffix = Convert.ToDouble(message.Args[0], CultureInfo.InvariantCulture) == 1 ? ".one" : ".other";
            if (catalog.TryGetValue(message.Key + suffix, out var plural)) template = plural;
        }
        return string.Format(CultureInfo.InvariantCulture, template, message.Args.Select(a => a is Message nested ? nested.Render(locale) : a).ToArray());
    }
    public static Message ExceptionMessage(Exception exception)
    {
        var result = exception is LocalizedException localized ? localized.MessageData
            : new Message("core.raw", exception.Message.Contains("See InnerException") ? "" : exception.Message);
        if (exception.InnerException is { } inner)
        {
            var nested = ExceptionMessage(inner);
            if (result.Render() == "") return nested;
            if (nested.Render() != result.Render() && !result.Args.OfType<Message>().Any(a => a.Render() == nested.Render()))
                return new("core.join", result, nested);
        }
        return result;
    }
}
