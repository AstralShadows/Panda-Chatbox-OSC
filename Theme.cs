using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PandaChatbox;

/// <summary>
/// Swaps the color brushes in Application.Resources. Every style uses DynamicResource,
/// so the whole UI recolors instantly.
/// </summary>
public static class ThemeManager
{
    public static readonly string[] Names = { "Midnight", "Violet", "Rose", "Mint" };
    static readonly string[] SurfaceKeys =
    {
        "Header", "Panel", "Card", "CardHi", "Field", "Edge", "CardBorder", "TabSel", "Off", "PrevBg",
    };

    static readonly string[] Keys =
    {
        "Bg", "Header", "Panel", "Card", "CardHi", "Field", "Edge", "CardBorder",
        "TabSel", "Accent", "Pink", "Lilac", "Muted", "Off", "PrevBg",
    };

    // same order as Keys
    static readonly string[][] Palettes =
    {
        // Midnight (the original look)
        new[] { "0B1626", "0F1F36", "0F1F36", "172B47", "223C63", "0D1A2D", "2D4A75", "1F3A5F",
                "2B2358", "9B5CFF", "FF5FD2", "C2A3FF", "8FA6C8", "3B5073", "0A1322" },
        // Violet
        new[] { "2A2046", "1E1636", "241B3E", "3A2F6B", "4B3F8E", "261C45", "5B4BA8", "4B3F8E",
                "4B3F8E", "8B5CF6", "F472D0", "CDB9FF", "A99BD6", "5A4C8F", "1B1330" },
        // Rose
        new[] { "2B1420", "1F0E17", "25111B", "43202F", "5C2D42", "1A0C13", "7A3A55", "5C2D42",
                "5C2D42", "FF4D8D", "FF8FB8", "FFB3D1", "D69AB0", "6B3A4F", "160A10" },
        // Mint
        new[] { "0E1F1C", "0A1715", "0C1B18", "17332E", "20473F", "0A1614", "2F6B5F", "20473F",
                "20473F", "14B8A6", "5EEAD4", "99F6E4", "8DB8AE", "3A5F57", "08120F" },
    };

    public static void Apply(int index)
    {
        if (Application.Current == null) return;
        index = Math.Clamp(index, 0, Palettes.Length - 1);
        var res = Application.Current.Resources;
        var pal = Palettes[index];
        for (int i = 0; i < Keys.Length; i++)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#" + pal[i]));
            brush.Freeze();
            res[Keys[i]] = brush;
        }
    }

    public static void Apply(AppSettings settings)
    {
        Apply(settings.ThemeIndex);
        if (Application.Current == null) return;

        var resources = Application.Current.Resources;
        switch (settings.BackgroundMode)
        {
            case 1:
                resources["Bg"] = new SolidColorBrush(ToColor(
                    settings.BackgroundHue, settings.BackgroundSaturation, settings.BackgroundBrightness));
                break;
            case 2:
                if (string.IsNullOrWhiteSpace(settings.BackgroundImagePath))
                    break;
                if (!File.Exists(settings.BackgroundImagePath))
                    throw new FileNotFoundException("The selected background picture could not be found.", settings.BackgroundImagePath);

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(settings.BackgroundImagePath, UriKind.Absolute);
                image.EndInit();
                image.Freeze();

                var imageBrush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
                imageBrush.Freeze();
                resources["Bg"] = imageBrush;
                break;
        }

        foreach (string key in SurfaceKeys)
        {
            if (resources[key] is SolidColorBrush brush)
            {
                var translucentBrush = brush.Clone();
                translucentBrush.Opacity = 1 - settings.SurfaceTransparency;
                translucentBrush.Freeze();
                resources[key] = translucentBrush;
            }
        }

        var textColor = (Color)ColorConverter.ConvertFromString(settings.TextColor);
        var textBrush = new SolidColorBrush(textColor);
        textBrush.Freeze();
        resources["Text"] = textBrush;
    }

    public static Color PreviewColor(AppSettings settings) =>
        ToColor(settings.BackgroundHue, settings.BackgroundSaturation, settings.BackgroundBrightness);

    public static bool IsValidTextColor(string? value)
    {
        if (value == null || value.Length != 7 || value[0] != '#') return false;
        for (int i = 1; i < value.Length; i++)
            if (!Uri.IsHexDigit(value[i])) return false;
        return true;
    }

    static Color ToColor(double hue, double saturation, double value)
    {
        double chroma = value * saturation;
        double x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = value - chroma;
        (double r, double g, double b) = hue switch
        {
            < 60 => (chroma, x, 0d),
            < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x),
            < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
