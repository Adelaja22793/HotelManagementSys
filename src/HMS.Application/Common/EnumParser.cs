namespace HMS.Application.Common;

public static class EnumParser
{
    public static T Parse<T>(string? value, string field) where T : struct, Enum
    {
        if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse<T>(value.Trim(), ignoreCase: true, out var result)
            && Enum.IsDefined(result) && !int.TryParse(value, out _))
            return result;
        throw new ValidationAppException(field, $"Must be one of: {string.Join(", ", Enum.GetNames<T>())}.");
    }
}
