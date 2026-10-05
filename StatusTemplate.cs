using System.Globalization;

namespace PandaChatbox;

internal static class StatusTemplate
{
    public static string Expand(string text, IReadOnlyDictionary<string, string> values) =>
        NativeInterop.ExpandTemplate(text, values);

    public static string FormatBytes(double gigabytes) =>
        gigabytes.ToString("0.##", CultureInfo.InvariantCulture) + "GB";
}
