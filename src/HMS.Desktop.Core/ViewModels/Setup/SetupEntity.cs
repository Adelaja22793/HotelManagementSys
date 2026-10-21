using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using HMS.Desktop.Services;

namespace HMS.Desktop.ViewModels.Setup;

/// <summary>A list of records the form can pick from, such as room types or stock locations.</summary>
public sealed class LookupSource(string title, Type dtoType, Func<string?, string> path, Func<object, string>? label = null,
    Func<ApiClient, Task<IReadOnlyList<FieldOption>>>? loader = null, Func<object, bool>? filter = null)
{
    public string Title { get; } = title;
    public Type DtoType { get; } = dtoType;

    /// <summary>The branch-relative list route. The argument is the parent record's id for nested lists.</summary>
    public Func<string?, string> Path { get; } = path;

    public string LabelFor(object dto) => label?.Invoke(dto) ?? SetupEntity.DefaultLabel(dto);

    public async Task<IReadOnlyList<FieldOption>> LoadAsync(ApiClient api, string? parentId)
    {
        if (loader is not null) return await loader(api);
        var items = await api.GetListAsync(api.BranchPath(Path(parentId)), DtoType);
        return [.. items.Where(i => filter?.Invoke(i) != false)
            .Select(i => new FieldOption(SetupEntity.Read(i, "Id"), LabelFor(i), i)).OrderBy(o => o.Label, StringComparer.CurrentCultureIgnoreCase)];
    }
}

/// <summary>How one request field is shown. Anything not configured is worked out from the request's type.</summary>
public sealed class FieldSpec
{
    public string? Label { get; set; }
    public FieldKind? Kind { get; set; }
    public IReadOnlyList<string>? Choices { get; set; }
    public bool AllowOwnValue { get; set; }
    public LookupSource? Lookup { get; set; }
    public object? Default { get; set; }
    public bool Hidden { get; set; }
    public object? FixedValue { get; set; }
    public string? Hint { get; set; }

    /// <summary>For a date: starts this many days after the business date, for example 1 for an end date.</summary>
    public int DaysAhead { get; set; }

    /// <summary>Takes the id of the record the action is about (for example the purchase order an invoice is for).</summary>
    public bool TargetId { get; set; }

    /// <summary>Pick list made from the record the action is about, such as the lines of the chosen purchase order.</summary>
    public Func<object, IEnumerable<FieldOption>>? FromTarget { get; set; }
}

/// <summary>
/// Something a hotel sets up once and maintains: room types, tax rules, stock items and so on. The form fields are
/// built from the server's request records, so adding a field on the server shows up here without screen code.
/// </summary>
public sealed class SetupEntity
{
    public required string Group { get; init; }
    public required string Title { get; init; }
    public required string Singular { get; init; }
    public required string Permission { get; init; }
    public required Type DtoType { get; init; }
    public required Type CreateType { get; init; }
    public Type? UpdateType { get; init; }
    public required string ListPath { get; init; }
    public required string CreatePath { get; init; }
    public string? UpdatePath { get; init; }
    public string? Description { get; init; }

    /// <summary>For records that live under another one, like tables under an outlet.</summary>
    public LookupSource? Parent { get; init; }

    /// <summary>The list route returns one record (the branch itself) rather than a list.</summary>
    public bool SingleRecord { get; init; }

    /// <summary>Creating with PUT (an upsert such as account mappings) instead of POST.</summary>
    public bool CreateWithPut { get; init; }

    /// <summary>Saving always uses the create route, which replaces the current version (notification templates).</summary>
    public bool SaveCreatesVersion { get; init; }

    public IReadOnlyList<(string Header, string Property)>? Columns { get; init; }
    public Dictionary<string, FieldSpec> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool CanCreate => !SingleRecord;
    public bool CanEdit => UpdatePath is not null || SaveCreatesVersion;

    public SetupEntity Field(string name, Action<FieldSpec> configure)
    {
        if (!Fields.TryGetValue(name, out var spec)) Fields[name] = spec = new FieldSpec();
        configure(spec);
        return this;
    }

    public SetupEntity Choices(string name, params string[] values) => Field(name, f => f.Choices = values);
    public SetupEntity Suggest(string name, params string[] values) => Field(name, f => { f.Choices = values; f.AllowOwnValue = true; });
    public SetupEntity Lookup(string name, LookupSource source, string? label = null) => Field(name, f => { f.Lookup = source; f.Label ??= label; });
    public SetupEntity Default(string name, object value) => Field(name, f => f.Default = value);
    public SetupEntity Hide(string name, object? value = null) => Field(name, f => { f.Hidden = true; f.FixedValue = value; });
    public SetupEntity Label(string name, string label, string? hint = null) => Field(name, f => { f.Label = label; f.Hint = hint; });
    public SetupEntity Kind(string name, FieldKind kind, string? hint = null) => Field(name, f => { f.Kind = kind; f.Hint ??= hint; });

    public override string ToString() => Title;

    // ---- Forms -------------------------------------------------------------------------------------------------

    internal static ConstructorInfo Constructor(Type requestType) => FormBuilder.Constructor(requestType);

    /// <summary>The inputs for a new record, or for editing <paramref name="existing"/>.</summary>
    public IReadOnlyList<FormField> BuildForm(object? existing, IReadOnlyDictionary<string, IReadOnlyList<FieldOption>> lookups) =>
        FormBuilder.BuildFields(existing is not null && UpdateType is not null ? UpdateType : CreateType, Fields, existing, lookups);

    /// <summary>Turns the form back into the request record. Returns null with a message when something is missing.</summary>
    public object? BuildRequest(object? existing, IReadOnlyList<FormField> form, out string? error) =>
        FormBuilder.BuildRequest(existing is not null && UpdateType is not null ? UpdateType : CreateType, Fields, existing, form, null, out error);

    // ---- Lists -------------------------------------------------------------------------------------------------

    public IReadOnlyList<(string Header, string Property)> ColumnsFor() => Columns ?? [.. DtoType.GetProperties()
        .Where(p => p.Name is not ("Id" or "ConcurrencyStamp")
            && (p.PropertyType == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(p.PropertyType))
            && (p.PropertyType != typeof(Guid) && p.PropertyType != typeof(Guid?) || Fields.ContainsKey(p.Name)))
        .Select(p => (Humanize(p.Name.EndsWith("Id", StringComparison.Ordinal) ? p.Name[..^2] : p.Name), p.Name))];

    public string Cell(object dto, string property, IReadOnlyDictionary<string, IReadOnlyList<FieldOption>> lookups)
    {
        var value = Read(dto, property);
        if (Fields.TryGetValue(property, out var spec) && spec.Lookup is { } lookup && lookups.TryGetValue(lookup.Title, out var options))
            return options.FirstOrDefault(o => Equals(o.Value, value))?.Label ?? "";
        return value switch
        {
            null => "",
            bool b => b ? "Yes" : "No",
            decimal m => m.ToString("N2", CultureInfo.CurrentCulture),
            DateOnly d => d.ToString("d", CultureInfo.CurrentCulture),
            DateTime t => t.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
            TimeOnly t => t.ToString("HH:mm", CultureInfo.InvariantCulture),
            string s when spec?.Choices is not null => Humanize(s),
            _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? "",
        };
    }

    public string Route(string template, string? parentId, object? dto) =>
        template.Replace("{parent}", parentId).Replace("{id}", dto is null ? null : Convert.ToString(Read(dto, "Id"), CultureInfo.InvariantCulture));

    // ---- Helpers -----------------------------------------------------------------------------------------------

    internal static bool HasProperty(object obj, string name) => obj.GetType().GetProperty(name) is not null;

    internal static object? Read(object obj, string name) => obj.GetType().GetProperty(name)?.GetValue(obj);

    internal static string DefaultLabel(object dto)
    {
        string? S(string n) => HasProperty(dto, n) ? Convert.ToString(Read(dto, n), CultureInfo.CurrentCulture) : null;
        var name = S("Name") ?? S("Title") ?? S("FullName") ?? S("ItemName");
        var code = S("RoomNumber") ?? S("TableNumber") ?? S("Sku") ?? S("Code") ?? S("EmployeeNumber");
        if (S("FirstName") is { } first) name = $"{first} {S("LastName")}";
        return code is null ? name ?? dto.ToString()! : name is null ? code : $"{code} · {name}";
    }


    /// <summary>"MaxAdults" → "Max adults", "VacantClean" → "Vacant clean".</summary>
    public static string Humanize(string name)
    {
        var words = Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..].ToLowerInvariant();
    }
}
