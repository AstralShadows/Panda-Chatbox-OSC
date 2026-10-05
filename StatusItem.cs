using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;

namespace PandaChatbox;

/// <summary>
/// One status message row: text, in-rotation switch, favorite heart and a text style.
/// IsEditing / IsCurrent are UI-only and never saved.
/// </summary>
public class StatusItem : INotifyPropertyChanged
{
    string _text = "", _collection = "Default";
    bool _enabled = true, _favorite, _editing, _current;
    int _styleIndex;

    public event PropertyChangedEventHandler? PropertyChanged;
    void On([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public string Text
    {
        get => _text;
        set { value ??= ""; if (_text == value) return; _text = value; On(); }
    }

    public string Collection
    {
        get => _collection;
        set
        {
            value = string.IsNullOrWhiteSpace(value) ? "Default" : value.Trim();
            if (_collection == value) return;
            _collection = value;
            On();
        }
    }

    /// <summary>Power button: is this message part of the rotation?</summary>
    public bool Enabled
    {
        get => _enabled;
        set { if (_enabled == value) return; _enabled = value; On(); }
    }

    /// <summary>Heart button: used by the "Favorites only" rotation mode.</summary>
    public bool Favorite
    {
        get => _favorite;
        set { if (_favorite == value) return; _favorite = value; On(); }
    }

    /// <summary>Index into StatusStyles.Names (Default, UPPERCASE, ...).</summary>
    public int StyleIndex
    {
        get => _styleIndex;
        set
        {
            value = Math.Clamp(value, 0, StatusStyles.Names.Length - 1);
            if (_styleIndex == value) return;
            _styleIndex = value;
            On();
        }
    }

    [JsonIgnore]
    public bool IsEditing
    {
        get => _editing;
        set { if (_editing == value) return; _editing = value; On(); }
    }

    /// <summary>True for the message that is on the chatbox right now (shown green with a play icon).</summary>
    [JsonIgnore]
    public bool IsCurrent
    {
        get => _current;
        set { if (_current == value) return; _current = value; On(); }
    }
}

/// <summary>Per-message text transforms (the "Default" dropdown on each row).</summary>
public static class StatusStyles
{
    public static readonly string[] Names = { "Default", "UPPERCASE", "lowercase", "Wide", "Small caps", "Sparkles" };

    // a-z as small capitals (all in the basic multilingual plane)
    const string Small =
        "\u1D00\u0299\u1D04\u1D05\u1D07\uA730\u0262\u029C\u026A\u1D0A\u1D0B\u029F\u1D0D\u0274\u1D0F\u1D18\u01EB\u0280\uA731\u1D1B\u1D1C\u1D20\u1D21x\u028F\u1D22";

    public static string Apply(int style, string t) => style switch
    {
        1 => t.ToUpperInvariant(),
        2 => t.ToLowerInvariant(),
        3 => Wide(t),
        4 => SmallCaps(t),
        5 => "\u2728 " + t + " \u2728",
        _ => t,
    };

    static string Wide(string t)
    {
        var sb = new StringBuilder(t.Length);
        foreach (char c in t)
            sb.Append(c == ' ' ? '\u3000' : c >= '!' && c <= '~' ? (char)(c - '!' + 0xFF01) : c);
        return sb.ToString();
    }

    static string SmallCaps(string t)
    {
        var sb = new StringBuilder(t.Length);
        foreach (char c in t)
        {
            char l = char.ToLowerInvariant(c);
            sb.Append(l >= 'a' && l <= 'z' ? Small[l - 'a'] : c);
        }
        return sb.ToString();
    }
}
