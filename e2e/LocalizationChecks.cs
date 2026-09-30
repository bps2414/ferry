using System.Text.Json;
using System.Text.RegularExpressions;
using Ferry;

static class LocalizationChecks
{
    public static void Run()
    {
        var checks = 0;
        void Check(bool valid, string name)
        {
            if (!valid) throw new InvalidOperationException("Localization: " + name);
            checks++;
            Console.WriteLine("OK " + name);
        }
        var root = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(root, "web")) || !Directory.Exists(Path.Combine(root, "core"))) root = Path.GetDirectoryName(root)!;
        var originalSettings = Settings.FilePath;
        var temp = Path.Combine(Path.GetTempPath(), "ferry-localization-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            Settings.FilePath = Path.Combine(temp, "settings.json");
            File.WriteAllText(Settings.FilePath, "{\"Host\":\"127.0.0.1\",\"Port\":2121}");
            var legacy = Settings.Load();
            Check(legacy.Language == "auto" && legacy.Host == "127.0.0.1", "legacy settings default to auto and preserve fields");
            foreach (var language in new[] { "auto", "pt-BR", "en" })
            {
                legacy.Language = language;
                legacy.Save();
                Check(Settings.Load().Language == language, "settings persist " + language);
            }
            Check(Localization.NormalizeLocale("pt-PT") == "pt-BR" && Localization.NormalizeLocale("en-GB") == "en" && Localization.NormalizeLocale("ja-JP") == "en", "supported variants and fallback");
            Check(new Message("core.stage.Enviando").Render("en") == "Sending", "English stage keeps stable enum identifier");
            Check(new Message("core.stage.Enviando").Render("pt-BR") == "Enviando", "Portuguese stage preserves WPF text");
            const string raw = "/games/ação <teste> & arquivo.7z: 550 Access denied";
            var diagnostic = Localization.ExceptionMessage(new Exception(raw));
            Check(diagnostic.Render("en") == raw && diagnostic.Render("pt-BR") == raw, "raw paths and provider diagnostic bytes are preserved");
            var nested = new Message("core.jobLog", "ação <teste>", new Message("core.stage.Enviando"));
            Check(nested.Render("en") == "[ação <teste>] Sending" && nested.Render("pt-BR") == "[ação <teste>] Enviando", "nested messages translate without changing data");
            var extraction = new LocalizedException(new Message("core.archive.outputShort"));
            Check(extraction.MessageData.Key == "core.archive.outputShort" && extraction.MessageData.Render("en") == "7-Zip output ended earlier than expected", "extraction failure carries a stable code independently of its translated text");
            var job = new Job { Key = "test", Name = "test", Stage = Stage.Enviando };
            job.SetDetail(nested);
            job.Report(512, 1024);
            Check(job.DetailMessage == nested && job.Detail == nested.Render() && job.DoneBytes == 512 && job.TotalBytes == 1024 && job.Progress == 50 && job.CanPause, "localized details preserve progress and actions");
            job.Finish(new Message("core.uploadVerified"));
            Check(job.Progress == 100 && job.DetailMessage?.Key == "core.uploadVerified", "completion retains structured detail");

            var catalogs = new Dictionary<string, Dictionary<string, string>>();
            foreach (var locale in new[] { "pt-BR", "en" })
            {
                var merged = new Dictionary<string, string>(Localization.Catalog(locale));
                foreach (var folder in new[] { "web/locales", "web/ui/locales" })
                {
                    var file = Path.Combine(root, folder, locale + ".json");
                    if (!File.Exists(file)) throw new FileNotFoundException("Required catalog", file);
                    foreach (var entry in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!)
                        Check(merged.TryAdd(entry.Key, entry.Value), "unique catalog key " + locale + " " + entry.Key);
                }
                catalogs[locale] = merged;
            }
            var portuguese = catalogs["pt-BR"];
            var english = catalogs["en"];
            Check(portuguese.Keys.Order().SequenceEqual(english.Keys.Order()), "all core, backend and UI catalog keys match");
            foreach (var (key, value) in portuguese)
            {
                static string[] Parameters(string template) => Regex.Matches(template, @"\{(\d+)(?:[^}]*)\}").Select(m => m.Groups[1].Value).Distinct().Order().ToArray();
                Check(Parameters(value).SequenceEqual(Parameters(english[key])), "matching parameters " + key);
                Check(!string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(english[key]), "nonempty translation " + key);
            }
            foreach (var stage in Enum.GetValues<Stage>())
                Check(portuguese.ContainsKey("core.stage." + stage), "stage translation " + stage);
            foreach (var folder in new[] { "core", "web" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, folder), "*", SearchOption.AllDirectories)
                .Where(file => new[] { ".cs", ".js", ".html" }.Contains(Path.GetExtension(file)) && !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")))
            foreach (Match reference in Regex.Matches(File.ReadAllText(file), "[\"']((?:core|web|ui)\\.[A-Za-z0-9]+(?:\\.[A-Za-z0-9]+)*)[\"']"))
                Check(portuguese.ContainsKey(reference.Groups[1].Value), "catalog covers " + reference.Groups[1].Value);
            Console.WriteLine($"Localization: {checks} checks passed.");
        }
        finally
        {
            Settings.FilePath = originalSettings;
            Directory.Delete(temp, true);
        }
    }
}
