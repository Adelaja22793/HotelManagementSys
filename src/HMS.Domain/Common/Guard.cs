namespace HMS.Domain.Common;

internal static class Guard
{
    public static string Required(string? value, string code, string message, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException(code, message);
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength) throw new DomainException(code, $"{message} (max {maxLength} characters).");
        return trimmed;
    }
}
