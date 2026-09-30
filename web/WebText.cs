using System.Text.Json;

namespace Ferry.Web;

/// Resources travel with the standalone server as well as the Docker image.
public static class WebText
{
    public static void Initialize()
    {
        foreach (var locale in new[] { "pt-BR", "en" })
            Localization.AddCatalog(locale, Read("locales/" + locale + ".json"));
    }

    public static IReadOnlyDictionary<string, string> Catalog(string locale)
    {
        var result = new Dictionary<string, string>(Localization.Catalog(locale));
        foreach (var (key, value) in Read("ui/locales/" + locale + ".json")) result.Add(key, value);
        return result;
    }

    static Dictionary<string, string> Read(string name)
    {
        using var stream = typeof(WebText).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing embedded catalog: " + name);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    public static object Error(Message message) => new { error = message.Render(), errorMessage = message };
}
