namespace HMS.Domain.Common;

public static class Money
{
    /// <summary>Single rounding policy for all monetary amounts: 2 decimals, half away from zero.</summary>
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static decimal RequirePositive(decimal amount, string code = "AMOUNT_INVALID")
    {
        if (amount <= 0) throw new DomainException(code, "Amount must be greater than zero.");
        if (decimal.Round(amount, 2) != amount) throw new DomainException(code, "Amount can have at most 2 decimal places.");
        return amount;
    }
}
