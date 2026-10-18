using System.Text.Json;

namespace NS2Pro.App.Services;

internal static class RebootRequirement
{
    private static long Boot => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - Environment.TickCount64;
    public static void Mark(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(new { Boot }));
    }
    public static bool IsPending(string file)
    {
        if (!File.Exists(file)) return false;
        try {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            if (Math.Abs(json.RootElement.GetProperty("Boot").GetInt64() - Boot) < 30_000) return true;
            File.Delete(file); return false;
        } catch (Exception) { return true; }
    }
}
