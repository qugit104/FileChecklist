using System.Globalization;
using System.Text.Json;

namespace FileChecklist.Core;

public static class Text
{
    private static readonly Lazy<Dictionary<string, string>> English = new(() =>
    {
        using var stream = typeof(Text).Assembly.GetManifestResourceStream("FileChecklist.English.json")
            ?? throw new InvalidOperationException("English language resource is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    });

    public static bool IsChinese => CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
    public static string T(string source) => IsChinese ? source : English.Value.GetValueOrDefault(source, source);
    public static string F(FormattableString source) => string.Format(CultureInfo.CurrentUICulture, T(source.Format), source.GetArguments());
}
