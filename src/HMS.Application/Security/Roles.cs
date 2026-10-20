namespace HMS.Application.Security;

public static class Roles
{
    public const string SystemAdmin = "SystemAdmin";
    public const string HotelManager = "HotelManager";
    public const string Receptionist = "Receptionist";
    public const string Housekeeping = "Housekeeping";
    public const string Cashier = "Cashier";
    public const string Accountant = "Accountant";
    public const string Auditor = "Auditor";
    public const string InventoryClerk = "InventoryClerk";
    public const string RestaurantStaff = "RestaurantStaff";
    public const string MaintenanceTechnician = "MaintenanceTechnician";
    public const string HumanResources = "HumanResources";

    public static readonly IReadOnlyList<string> All =
    [
        SystemAdmin, HotelManager, Receptionist, Housekeeping, Cashier, Accountant, Auditor,
        InventoryClerk, RestaurantStaff, MaintenanceTechnician, HumanResources,
    ];
}
