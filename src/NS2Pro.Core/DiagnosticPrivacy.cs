using System.Text.RegularExpressions;

namespace NS2Pro.Core;

public static partial class DiagnosticPrivacy
{
    public static string Redact(string text) => Address().Replace(Profile().Replace(text, "%USERPROFILE%"), "**:**:**:**:**:**");
    [GeneratedRegex(@"(?i)\b[A-Z]:[\\/]Users[\\/][^\\/\r\n]+")]
    private static partial Regex Profile();
    [GeneratedRegex(@"(?i)(?<![0-9A-F])(?:[0-9A-F]{2}:){5}[0-9A-F]{2}(?![0-9A-F])")]
    private static partial Regex Address();
}
