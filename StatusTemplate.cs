using System.Globalization;
using System.Text.RegularExpressions;

namespace PandaChatbox;

internal static class StatusTemplate
{
    static readonly Regex Token = new(@"\{(?<name>[a-z]+)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Expand(string text, IReadOnlyDictionary<string, string> values) =>
        Token.Replace(text, match =>
            values.TryGetValue(match.Groups["name"].Value.ToLowerInvariant(), out string? value)
                ? value
                : match.Value);

    public static string FormatBytes(double gigabytes) =>
        gigabytes.ToString("0.##", CultureInfo.InvariantCulture) + "GB";
}
