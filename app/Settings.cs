using System.IO;
using System.Text.Json;

namespace PS5Sender;

public class Settings
{
    public static readonly string AppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS5Sender");
    static readonly string FilePath = Path.Combine(AppDir, "settings.json");

    public string Host { get; set; } = "192.168.0.10";
    public int Port { get; set; } = 2121;
    public string User { get; set; } = "anonymous";
    public string Password { get; set; } = "";
    public string InputFolder { get; set; } = "";
    public string RemoteDir { get; set; } = "/mnt/ext1/homebrew";
    public int Connections { get; set; } = 4;
    public bool DeleteOriginal { get; set; }

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppDir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
