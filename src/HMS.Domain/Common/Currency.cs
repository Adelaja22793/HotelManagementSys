namespace HMS.Domain.Common;

public static class Currency
{
    public const string Default = "NGN";

    public static string Normalize(string? code)
    {
        var value = (code ?? Default).Trim().ToUpperInvariant();
        if (value.Length != 3 || !value.All(char.IsAsciiLetterUpper))
            throw new DomainException("CURRENCY_INVALID", "Currency must be a 3-letter ISO 4217 code.");
        return value;
    }
}
