using System.Collections;
using System.Reflection;

namespace HMS.Desktop.ViewModels.Setup;

/// <summary>Builds form inputs from a request record's constructor, and the record back from the inputs.</summary>
public static class FormBuilder
{
    private static readonly NullabilityInfoContext Nullability = new();

    public static ConstructorInfo Constructor(Type requestType) =>
        requestType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();

    internal static string Pascal(string name) => char.ToUpperInvariant(name[0]) + name[1..];

    /// <summary>The list parameter of a request (such as order lines) and its item type, if it has one.</summary>
    public static (ParameterInfo Parameter, Type ItemType)? ListParameter(Type requestType)
    {
        foreach (var p in Constructor(requestType).GetParameters())
            if (p.ParameterType.IsGenericType && p.ParameterType != typeof(string)
                && typeof(IEnumerable).IsAssignableFrom(p.ParameterType))
                return (p, p.ParameterType.GetGenericArguments()[0]);
        return null;
    }

    public static List<FormField> BuildFields(Type requestType, IReadOnlyDictionary<string, FieldSpec> specs, object? existing,
        IReadOnlyDictionary<string, IReadOnlyList<FieldOption>> lookups, DateOnly? today = null)
    {
        var fields = new List<FormField>();
        foreach (var p in Constructor(requestType).GetParameters())
        {
            var name = Pascal(p.Name!);
            specs.TryGetValue(name, out var spec);
            if (name == "ConcurrencyStamp" || spec?.Hidden == true || spec?.TargetId == true) continue;
            var type = p.ParameterType;
            if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type)) continue; // lists have their own editor

            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            var optional = Nullable.GetUnderlyingType(type) is not null
                || (type == typeof(string) && Nullability.Create(p).WriteState == NullabilityState.Nullable);
            var kind = spec?.Kind ?? (spec?.Lookup is not null || spec?.FromTarget is not null ? FieldKind.Lookup
                : spec?.Choices is not null ? FieldKind.Choice
                : underlying == typeof(bool) ? FieldKind.YesNo
                : underlying == typeof(int) ? FieldKind.WholeNumber
                : underlying == typeof(decimal) ? FieldKind.Number
                : underlying == typeof(DateOnly) ? FieldKind.Date
                : underlying == typeof(TimeOnly) ? FieldKind.Time
                : underlying == typeof(Guid) ? FieldKind.Lookup
                : FieldKind.Text);

            var field = new FormField(name, spec?.Label ?? SetupEntity.Humanize(name), kind, type, optional)
            {
                Hint = spec?.Hint,
                AllowsOwnValue = spec?.AllowOwnValue == true,
            };
            if (kind == FieldKind.Choice)
            {
                if (optional) field.Options.Add(new FieldOption(null, "(none)"));
                foreach (var c in spec!.Choices!) field.Options.Add(new FieldOption(c, SetupEntity.Humanize(c)));
            }
            else if (kind == FieldKind.Lookup && spec?.Lookup is { } lookup && lookups.TryGetValue(lookup.Title, out var options))
                SetOptions(field, options);

            var value = existing is not null && SetupEntity.HasProperty(existing, name) ? SetupEntity.Read(existing, name)
                : spec?.Default ?? (p.HasDefaultValue ? p.DefaultValue : underlying == typeof(DateOnly) ? (today ?? DateOnly.FromDateTime(DateTime.Today)).AddDays(spec?.DaysAhead ?? 0) : null);
            field.SetValue(value);
            fields.Add(field);
        }
        return fields;
    }

    public static void SetOptions(FormField field, IEnumerable<FieldOption> options)
    {
        var keep = field.Selected?.Value;
        field.Options.Clear();
        if (field.IsOptional) field.Options.Add(new FieldOption(null, "(none)"));
        foreach (var o in options) field.Options.Add(o);
        field.Selected = field.Options.FirstOrDefault(o => keep is not null && Equals(o.Value, keep));
    }

    /// <summary>
    /// Builds the request. The concurrency stamp comes from <paramref name="existing"/> (the record being changed or acted on);
    /// <paramref name="list"/> fills the request's list parameter.
    /// </summary>
    public static object? BuildRequest(Type requestType, IReadOnlyDictionary<string, FieldSpec> specs, object? existing,
        IReadOnlyList<FormField> form, IList? list, out string? error)
    {
        error = null;
        var ctor = Constructor(requestType);
        var byName = form.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var args = new List<object?>();
        foreach (var p in ctor.GetParameters())
        {
            var name = Pascal(p.Name!);
            specs.TryGetValue(name, out var spec);
            if (name == "ConcurrencyStamp")
                args.Add(existing is not null && SetupEntity.HasProperty(existing, name) ? SetupEntity.Read(existing, name) : null);
            else if (spec?.TargetId == true)
                args.Add(existing is null ? null : SetupEntity.Read(existing, "Id"));
            else if (spec?.Hidden == true)
                args.Add(spec.FixedValue ?? (p.HasDefaultValue ? p.DefaultValue : null));
            else if (byName.TryGetValue(name, out var field))
            {
                if (!field.TryGetValue(out var value, out error)) return null;
                args.Add(value);
            }
            else if (list is not null && p.ParameterType.IsInstanceOfType(list))
                args.Add(list);
            else
                args.Add(p.HasDefaultValue ? p.DefaultValue : null);
        }
        return ctor.Invoke([.. args]);
    }
}
