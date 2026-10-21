using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HMS.Desktop.ViewModels.Setup;

public enum FieldKind { Text, LongText, Number, WholeNumber, YesNo, Date, Time, Choice, Lookup, Secret }

/// <summary>An entry in a pick list: the value sent to the server and the text the user sees.</summary>
public sealed record FieldOption(object? Value, string Label, object? Record = null)
{
    public override string ToString() => Label;
}

/// <summary>
/// One input on a setup form. The form is built from the request record the server expects, so every field the
/// server needs is on screen and the value is converted to the right type before it is sent.
/// </summary>
public sealed partial class FormField : ObservableObject
{
    public FormField(string name, string label, FieldKind kind, Type valueType, bool isOptional)
    {
        Name = name;
        Label = label;
        Kind = kind;
        ValueType = valueType;
        IsOptional = isOptional;
    }

    public string Name { get; }
    public string Label { get; }
    public FieldKind Kind { get; }
    public Type ValueType { get; }
    public bool IsOptional { get; }
    public string? Hint { get; init; }

    /// <summary>For text fields with suggestions: the user may pick one or type their own.</summary>
    public bool AllowsOwnValue { get; init; }

    public ObservableCollection<FieldOption> Options { get; } = [];

    [ObservableProperty] private string? _text;
    [ObservableProperty] private bool _flag;
    [ObservableProperty] private DateTime? _date;
    [ObservableProperty] private FieldOption? _selected;

    public string Caption => IsOptional || Kind == FieldKind.YesNo ? Label : Label + " *";

    public bool IsTextInput => Kind is FieldKind.Text or FieldKind.Number or FieldKind.WholeNumber or FieldKind.Time;
    public bool IsLongText => Kind == FieldKind.LongText;
    public bool IsSecret => Kind == FieldKind.Secret;
    public bool IsYesNo => Kind == FieldKind.YesNo;
    public bool ShowsCaption => Kind != FieldKind.YesNo;
    public bool IsDate => Kind == FieldKind.Date;
    public bool IsPicker => Kind is FieldKind.Choice or FieldKind.Lookup;

    /// <summary>Shows a value from an existing record (or a default) in the input.</summary>
    public void SetValue(object? value)
    {
        switch (Kind)
        {
            case FieldKind.YesNo:
                Flag = value is true;
                break;
            case FieldKind.Date:
                Date = value is DateOnly d ? d.ToDateTime(TimeOnly.MinValue) : null;
                break;
            case FieldKind.Time:
                Text = value is TimeOnly t ? t.ToString("HH:mm", CultureInfo.InvariantCulture) : value?.ToString();
                break;
            case FieldKind.Choice or FieldKind.Lookup:
                Selected = Options.FirstOrDefault(o => Equals(o.Value, value));
                if (Selected is null && AllowsOwnValue) Text = value?.ToString();
                break;
            case FieldKind.Number or FieldKind.WholeNumber:
                Text = value switch
                {
                    decimal m => m.ToString("0.##", CultureInfo.CurrentCulture),
                    null => null,
                    _ => Convert.ToString(value, CultureInfo.CurrentCulture),
                };
                break;
            default:
                Text = value?.ToString();
                break;
        }
    }

    /// <summary>Reads the input as the type the server expects. Returns false with a message when it is not valid.</summary>
    public bool TryGetValue(out object? value, out string? error)
    {
        value = null;
        error = null;
        var target = Nullable.GetUnderlyingType(ValueType) ?? ValueType;
        switch (Kind)
        {
            case FieldKind.YesNo:
                value = Flag;
                return true;
            case FieldKind.Date:
                if (Date is null) return Missing(out error);
                value = DateOnly.FromDateTime(Date.Value);
                return true;
            case FieldKind.Choice or FieldKind.Lookup:
                if (Selected is not null)
                {
                    value = Selected.Value;
                    return Selected.Value is not null || IsOptional || Missing(out error);
                }
                if (AllowsOwnValue && !string.IsNullOrWhiteSpace(Text))
                {
                    value = Text.Trim();
                    return true;
                }
                return Missing(out error);
        }

        var text = Text?.Trim();
        if (string.IsNullOrEmpty(text)) return Missing(out error);
        if (Kind == FieldKind.Time)
        {
            if (!TimeOnly.TryParseExact(text, ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                error = $"{Label} must be a time like 07:00 or 23:30.";
                return false;
            }
            value = target == typeof(TimeOnly) ? time : time.ToString("HH:mm", CultureInfo.InvariantCulture);
            return true;
        }
        if (target == typeof(string))
        {
            value = text;
            return true;
        }
        if (target == typeof(int))
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var i)) { value = i; return true; }
            error = $"{Label} must be a whole number.";
            return false;
        }
        if (target == typeof(decimal))
        {
            if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var m)
                || decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out m)) { value = m; return true; }
            error = $"{Label} must be a number.";
            return false;
        }
        error = $"{Label} has a value this screen cannot read.";
        return false;
    }

    private bool Missing(out string? error)
    {
        error = IsOptional ? null : $"Enter {Label.ToLowerInvariant()}.";
        return IsOptional;
    }
}
