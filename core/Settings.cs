using System.IO;
using System.Text.Json;

namespace Ferry;

public class Settings
{
    static readonly string LocalData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    /// FERRY_DATA troca a pasta de dados (no container: /data)
    public static readonly string AppDir = Environment.GetEnvironmentVariable("FERRY_DATA") is { Length: > 0 } d ? d : Path.Combine(LocalData, "Ferry");
    /// Pasta do nome antigo do app (PS5Sender); só é lida na migração.
    public static readonly string OldAppDir = Path.Combine(LocalData, "PS5Sender");
    public static string FilePath { get; set; } = Path.Combine(AppDir, "settings.json"); // o E2E aponta para outro lugar
    static readonly object SaveLock = new();

    public string Language { get; set; } = "auto";
    public bool WebhookEnabled { get; set; }
    public string WebhookKind { get; set; } = "generic";
    public string WebhookUrl { get; set; } = "";
    /// Idioma do navegador que configurou os avisos; não depende de uma página aberta.
    public string WebhookAutoLocale { get; set; } = "en";
    public string Host { get; set; } = "192.168.0.10";
    public int Port { get; set; } = 2121;
    public string User { get; set; } = "anonymous";
    public string Password { get; set; } = "";
    public string InputFolder { get; set; } = "";
    public string RemoteDir { get; set; } = "/mnt/ext1/homebrew";
    /// Pasta de imagens .exfat: um scanpath do ShadowMount+ (imagens na raiz dele; ver docs/FTP-PS5.md)
    public string ImageDir { get; set; } = "/mnt/ext1/homebrew";
    public string PkgDir { get; set; } = "/data/ferry/pkg";
    public int DpiPort { get; set; } = 9090;
    public int Connections { get; set; } = 4;
    public bool DeleteOriginal { get; set; }
    /// Senhas públicas (de sites) testadas antes de abrir o diálogo; a que funcionar no diálogo entra no fim.
    public List<string> KnownPasswords { get; set; } = [];

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    // UI e Engine (senha aprendida) salvam de threads diferentes
    public void Save()
    {
        lock (SaveLock) AtomicWrite(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// Migração única: se a pasta nova ainda não tem settings.json nem queue.json e a antiga existe, COPIA os arquivos
    /// (a antiga nunca é apagada). Chamar no início do app, antes de qualquer Load.
    public static void Migrate(string oldDir, string newDir)
    {
        try
        {
            if (!Directory.Exists(oldDir) || File.Exists(Path.Combine(newDir, "settings.json")) || File.Exists(Path.Combine(newDir, "queue.json"))) return;
            Directory.CreateDirectory(newDir);
            var copied = new List<string>();
            foreach (var name in new[] { "settings.json", "queue.json", "log.txt", "log.1.txt" })
            {
                var from = Path.Combine(oldDir, name);
                if (!File.Exists(from)) continue;
                File.Copy(from, Path.Combine(newDir, name));
                copied.Add(name);
            }
            if (copied.Count > 0) FileLog.Write(new Message("core.migrated", oldDir, string.Join(", ", copied)).Render());
        }
        catch { } // migrar nunca impede o app de abrir
    }

    /// Grava em .tmp e troca: queda de energia no meio não deixa o arquivo pela metade.
    public static void AtomicWrite(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        if (File.Exists(path)) File.Replace(tmp, path, null);
        else File.Move(tmp, path);
    }
}

/// <summary>Log em log.txt na pasta de dados; passando de ~5 MB vira log.1.txt (só uma geração).</summary>
public static class FileLog
{
    public static string FilePath { get; set; } = Path.Combine(Settings.AppDir, "log.txt");
    const long MaxSize = 5 << 20;

    public static void Write(string msg)
    {
        try
        {
            lock (typeof(FileLog))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (new FileInfo(FilePath) is { Exists: true, Length: > MaxSize }) File.Move(FilePath, Path.ChangeExtension(FilePath, ".1.txt"), true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}\n");
            }
        }
        catch { } // log nunca derruba o envio
    }
}
