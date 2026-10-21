using System.Globalization;

namespace HMS.Desktop.Services;

/// <summary>Builds query strings with escaped values and ISO dates; empty values are left out.</summary>
public static class Query
{
    public static string Build(string path, params (string Name, object? Value)[] values)
    {
        var parts = values
            .Select(v => (v.Name, Text: v.Value switch
            {
                null => null,
                DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                bool b => b ? "true" : "false",
                DateTime t => t.ToString("O", CultureInfo.InvariantCulture),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => v.Value.ToString(),
            }))
            .Where(v => !string.IsNullOrWhiteSpace(v.Text))
            .Select(v => $"{v.Name}={Uri.EscapeDataString(v.Text!)}")
            .ToList();
        return parts.Count == 0 ? path : $"{path}?{string.Join('&', parts)}";
    }
}
