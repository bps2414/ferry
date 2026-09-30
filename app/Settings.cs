using System.IO;
using System.Text.Json;

namespace PS5Sender;

public class Settings
{
    public static readonly string AppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS5Sender");
    public static string FilePath { get; set; } = Path.Combine(AppDir, "settings.json"); // o E2E aponta para outro lugar
    static readonly object SaveLock = new();

    public string Host { get; set; } = "192.168.0.10";
    public int Port { get; set; } = 2121;
    public string User { get; set; } = "anonymous";
    public string Password { get; set; } = "";
    public string InputFolder { get; set; } = "";
    public string RemoteDir { get; set; } = "/mnt/ext1/homebrew";
    /// Pasta de imagens .exfat: um scanpath do ShadowMount+ (imagens na raiz dele; ver docs/FTP-PS5.md)
    public string ImageDir { get; set; } = "/mnt/ext1/homebrew";
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

/// <summary>Log em %LOCALAPPDATA%\PS5Sender\log.txt; passando de ~5 MB vira log.1.txt (só uma geração).</summary>
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
