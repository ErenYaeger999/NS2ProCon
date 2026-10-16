using NS2Pro.Core;
using System.Text.Json;

namespace NS2Pro.App.Services;

public sealed class AppSettings
{
    private static readonly object FileGate = new();
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NS2Pro");
    public static string FilePath => Path.Combine(DataDirectory, "settings.json");
    public ButtonLayout Layout { get; set; } = ButtonLayout.Labels;
    public double Deadzone { get; set; } = .06;
    public string Theme { get; set; } = "Light";
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoStartConnection { get; set; }
    public string LastControllerAddress { get; set; } = "BC:89:A6:5C:7A:32";

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        lock (FileGate) {
            Directory.CreateDirectory(DataDirectory);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, FilePath, true);
        }
    }
}

public static class AppLog
{
    private static readonly object Gate = new();
    public static string FilePath => Path.Combine(AppSettings.DataDirectory, "session.log");
    public static event Action<string>? LineWritten;
    public static void Write(string level, string text)
    {
        var line = $"{DateTimeOffset.Now:HH:mm:ss}  {level.ToUpperInvariant(),-5}  {text}";
        lock (Gate) {
            try {
                Directory.CreateDirectory(AppSettings.DataDirectory);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 2_000_000)
                    File.Move(FilePath, FilePath + ".previous", true);
                File.AppendAllText(FilePath, line + Environment.NewLine);
            } catch (IOException) { }
        }
        LineWritten?.Invoke(line);
    }
}
