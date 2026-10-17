using NS2Pro.Core;
using System.Text.Json;

namespace NS2Pro.App.Services;

public enum GameOutputMode { Xbox, DualSenseExperimental }

public sealed class AppSettings
{
    private static readonly object FileGate = new();
    public static bool IsPreview => Environment.GetCommandLineArgs().Contains("--preview-onboarding", StringComparer.Ordinal);
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NS2Pro", IsPreview ? "Preview" : "");
    public static string FilePath => Path.Combine(DataDirectory, "settings.json");
    public ButtonLayout Layout { get; set; } = ButtonLayout.Labels;
    public double Deadzone { get; set; } = .06;
    public string Theme { get; set; } = "Light";
    public bool CloseToTray { get; set; }
    public bool AutoStartConnection { get; set; }
    public bool RumbleEnabled { get; set; } = true;
    public double RumbleStrength { get; set; } = .65;
    public GameOutputMode OutputMode { get; set; }
    public int ViiperPort { get; set; } = 3242;
    public bool ManageHapticBackend { get; set; } = true;
    public bool PreferNativeHaptics { get; set; }
    public string LastControllerAddress { get; set; } = "";
    public bool HasCompletedFirstConnection { get; set; }
    public bool HasSeenConnectionGuide { get; set; }
    public int ConnectionNoticeDismissCount { get; set; }
    internal bool ShouldShowConnectionGuide => !HasSeenConnectionGuide && !HasCompletedFirstConnection;
    internal bool ShouldShowConnectionNotice => ConnectionNoticeDismissCount < 3;

    internal void DismissConnectionNotice() => ConnectionNoticeDismissCount = Math.Clamp(ConnectionNoticeDismissCount, 0, 3) + (ShouldShowConnectionNotice ? 1 : 0);

    public void Normalize()
    {
        HasSeenConnectionGuide |= HasCompletedFirstConnection;
        ConnectionNoticeDismissCount = Math.Clamp(ConnectionNoticeDismissCount, 0, 3);
        if (!Enum.IsDefined(Layout)) Layout = ButtonLayout.Labels;
        Deadzone = double.IsFinite(Deadzone) ? Math.Clamp(Deadzone, 0, .3) : .06;
        RumbleStrength = double.IsFinite(RumbleStrength) ? Math.Clamp(RumbleStrength, 0, 1) : .65;
        if (!Enum.IsDefined(OutputMode)) OutputMode = GameOutputMode.Xbox;
        ViiperPort = Math.Clamp(ViiperPort, 1024, 65535);
        LastControllerAddress = (LastControllerAddress ?? "").Trim().ToUpperInvariant();
        if (LastControllerAddress.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(LastControllerAddress, "^([0-9A-F]{2}:){5}[0-9A-F]{2}$"))
            LastControllerAddress = "";
    }

    public static AppSettings Load()
    {
        try {
            return FromJson(File.ReadAllText(FilePath));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    internal static AppSettings FromJson(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        using var document = JsonDocument.Parse(json);
        // Existing enhanced-mode users configured an external backend in 0.7.0.
        if (document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.TryGetProperty(nameof(ManageHapticBackend), out _) && settings.OutputMode == GameOutputMode.DualSenseExperimental)
            settings.ManageHapticBackend = false;
        // An existing settings file predates the seen flag: its user has already opened the app.
        if (document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.TryGetProperty(nameof(HasSeenConnectionGuide), out _))
            settings.HasSeenConnectionGuide = true;
        settings.Normalize();
        return settings;
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
            } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        LineWritten?.Invoke(line);
    }
}
