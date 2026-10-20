namespace HMS.Application.Common;

/// <summary>Small helper that collects field errors and throws one validation exception.</summary>
public sealed class Validation
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.OrdinalIgnoreCase);

    public Validation Require(string field, string? value, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value)) Add(field, "Required.");
        else if (value.Trim().Length > maxLength) Add(field, $"Must be at most {maxLength} characters.");
        return this;
    }

    public Validation When(bool condition, string field, string error)
    {
        if (condition) Add(field, error);
        return this;
    }

    public Validation Add(string field, string error)
    {
        if (!_errors.TryGetValue(field, out var list)) _errors[field] = list = [];
        list.Add(error);
        return this;
    }

    public bool IsValid => _errors.Count == 0;

    public void ThrowIfInvalid()
    {
        if (!IsValid)
            throw new ValidationAppException(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }
}
