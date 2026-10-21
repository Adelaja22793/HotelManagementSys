using HMS.Domain.Enums;

namespace HMS.Application.Accounting;

/// <summary>Posting purposes. Each company maps every key to one of its accounts.</summary>
public static class AccountKeys
{
    public const string Cash = "Cash";
    public const string Bank = "Bank";
    public const string PosSettlement = "PosSettlement";
    public const string GuestReceivable = "GuestReceivable";
    public const string Inventory = "Inventory";
    public const string AccountsPayable = "AccountsPayable";
    public const string GoodsReceivedNotInvoiced = "GoodsReceivedNotInvoiced";
    public const string GuestDeposits = "GuestDeposits";
    public const string TaxPayable = "TaxPayable";
    public const string RoomRevenue = "RoomRevenue";
    public const string RestaurantRevenue = "RestaurantRevenue";
    public const string BarRevenue = "BarRevenue";
    public const string OtherRevenue = "OtherRevenue";
    public const string FoodCost = "FoodCost";
    public const string HousekeepingSupplies = "HousekeepingSupplies";
    public const string Wages = "Wages";
    public const string Repairs = "Repairs";
    public const string BankFees = "BankFees";
    public const string InventoryAdjustments = "InventoryAdjustments";
    public const string PurchasePriceVariance = "PurchasePriceVariance";
    public const string CashOverShort = "CashOverShort";
    public const string OpeningBalances = "OpeningBalances";
}

/// <summary>
/// Starting chart from the blueprint's example. The hotel's accountants should review it and the
/// account mappings before go-live; both can be changed through the accounting API.
/// </summary>
public static class DefaultChart
{
    public sealed record DefaultAccount(string Code, string Name, AccountType Type, bool IsControl, string? Key);

    public static readonly IReadOnlyList<DefaultAccount> Accounts =
    [
        new("1000", "Cash on Hand", AccountType.Asset, false, AccountKeys.Cash),
        new("1010", "Bank Accounts", AccountType.Asset, false, AccountKeys.Bank),
        new("1020", "POS Settlement Receivable", AccountType.Asset, false, AccountKeys.PosSettlement),
        new("1100", "Guest Accounts Receivable", AccountType.Asset, true, AccountKeys.GuestReceivable),
        new("1200", "Inventory", AccountType.Asset, true, AccountKeys.Inventory),
        new("1300", "Property and Equipment", AccountType.Asset, false, null),
        new("2000", "Accounts Payable", AccountType.Liability, true, AccountKeys.AccountsPayable),
        new("2010", "Goods Received Not Invoiced", AccountType.Liability, true, AccountKeys.GoodsReceivedNotInvoiced),
        new("2100", "Guest Deposits / Unearned Revenue", AccountType.Liability, false, AccountKeys.GuestDeposits),
        new("2200", "Tax Payable", AccountType.Liability, false, AccountKeys.TaxPayable),
        new("3000", "Owner Equity", AccountType.Equity, false, AccountKeys.OpeningBalances),
        new("4000", "Room Revenue", AccountType.Revenue, false, AccountKeys.RoomRevenue),
        new("4100", "Restaurant Revenue", AccountType.Revenue, false, AccountKeys.RestaurantRevenue),
        new("4200", "Bar Revenue", AccountType.Revenue, false, AccountKeys.BarRevenue),
        new("4300", "Other Revenue", AccountType.Revenue, false, AccountKeys.OtherRevenue),
        new("5000", "Food and Beverage Cost", AccountType.Expense, false, AccountKeys.FoodCost),
        new("5100", "Housekeeping Supplies", AccountType.Expense, false, AccountKeys.HousekeepingSupplies),
        new("5200", "Salaries and Wages", AccountType.Expense, false, AccountKeys.Wages),
        new("5300", "Utilities", AccountType.Expense, false, null),
        new("5400", "Repairs and Maintenance", AccountType.Expense, false, AccountKeys.Repairs),
        new("5500", "Bank and Payment Fees", AccountType.Expense, false, AccountKeys.BankFees),
        new("5600", "Inventory Shrinkage and Wastage", AccountType.Expense, false, AccountKeys.InventoryAdjustments),
        new("5700", "Purchase Price Variance", AccountType.Expense, false, AccountKeys.PurchasePriceVariance),
        new("5800", "Cash Over and Short", AccountType.Expense, false, AccountKeys.CashOverShort),
    ];

    public static readonly IReadOnlySet<string> Keys = Accounts.Where(a => a.Key is not null).Select(a => a.Key!).ToHashSet();
}
