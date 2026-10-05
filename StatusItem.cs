using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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

    public static string Apply(int style, string text) => NativeInterop.ApplyStatusStyle(style, text);
}
