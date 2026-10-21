using HMS.Contracts.Accounting;
using HMS.Contracts.Branches;
using HMS.Contracts.Common;
using HMS.Contracts.Inventory;
using HMS.Contracts.Maintenance;
using HMS.Contracts.Notifications;
using HMS.Contracts.Rates;
using HMS.Contracts.Restaurant;
using HMS.Contracts.Rooms;
using HMS.Contracts.Staff;
using HMS.Contracts.Users;
using P = HMS.Desktop.Services.PermissionNames;

namespace HMS.Desktop.ViewModels.Setup;

/// <summary>Every record a hotel sets up, grouped as on the Setup screen.</summary>
public static class SetupCatalog
{
    public static readonly LookupSource RoomTypes = new("Room types", typeof(RoomTypeDto), _ => "room-types");
    public static readonly LookupSource Rooms = new("Rooms", typeof(RoomDto), _ => "rooms");
    public static readonly LookupSource RatePlans = new("Rate plans", typeof(RatePlanDto), _ => "rate-plans");
    public static readonly LookupSource Departments = new("Departments", typeof(DepartmentDto), _ => "staff/departments");
    public static readonly LookupSource StockLocations = new("Stores", typeof(StockLocationDto), _ => "inventory/locations");
    public static readonly LookupSource Outlets = new("Outlets", typeof(OutletDto), _ => "restaurant/outlets");
    public static readonly LookupSource MenuCategories = new("Menu categories", typeof(MenuCategoryDto), p => $"restaurant/outlets/{p}/categories");
    public static readonly LookupSource Assets = new("Assets", typeof(AssetDto), _ => "maintenance/assets");
    public static readonly LookupSource Accounts = new("Accounts", typeof(AccountDto), _ => "accounting/accounts");
    public static readonly LookupSource Users = new("Users", typeof(UserDto), _ => "", loader: async api =>
    {
        var page = await api.GetAsync<PagedResult<UserDto>>("api/v1/users?page=1&pageSize=200");
        return [.. page.Items.Select(u => new FieldOption(u.Id, $"{u.FullName} · {u.Email}")).OrderBy(o => o.Label)];
    });

    public static readonly string[] ExpenseKeys = ["FoodCost", "HousekeepingSupplies", "Repairs", "Wages", "BankFees", "InventoryAdjustments"];
    public static readonly string[] Priorities = ["Low", "Normal", "High", "Urgent"];

    public static IReadOnlyList<SetupEntity> All { get; } = Build();

    private static List<SetupEntity> Build() =>
    [
        // ---- Rooms and rates ----
        new SetupEntity
        {
            Group = "Rooms and rates", Title = "Room types", Singular = "room type", Permission = P.RoomsManage,
            DtoType = typeof(RoomTypeDto), CreateType = typeof(CreateRoomTypeRequest), UpdateType = typeof(UpdateRoomTypeRequest),
            ListPath = "room-types", CreatePath = "room-types", UpdatePath = "room-types/{id}",
            Description = "The kinds of room you sell, such as Standard or Deluxe, with their base nightly rate.",
            Columns = [("Code", "Code"), ("Name", "Name"), ("Adults", "MaxAdults"), ("Children", "MaxChildren"), ("Base rate", "BaseRate"), ("Rooms", "RoomCount"), ("Active", "IsActive")],
        }.Default("MaxAdults", 2).Default("MaxChildren", 0).Kind("Description", FieldKind.LongText),

        new SetupEntity
        {
            Group = "Rooms and rates", Title = "Rooms", Singular = "room", Permission = P.RoomsManage,
            DtoType = typeof(RoomDto), CreateType = typeof(CreateRoomRequest), UpdateType = typeof(UpdateRoomRequest),
            ListPath = "rooms", CreatePath = "rooms", UpdatePath = "rooms/{id}",
            Description = "Each physical room, its number, floor and type.",
            Columns = [("Room", "RoomNumber"), ("Floor", "Floor"), ("Type", "RoomTypeId"), ("Status", "Status"), ("Active", "IsActive")],
        }.Lookup("RoomTypeId", RoomTypes, "Room type"),

        new SetupEntity
        {
            Group = "Rooms and rates", Title = "Rate plans", Singular = "rate plan", Permission = P.RatesManage,
            DtoType = typeof(RatePlanDto), CreateType = typeof(CreateRatePlanRequest), UpdateType = typeof(UpdateRatePlanRequest),
            ListPath = "rate-plans", CreatePath = "rate-plans", UpdatePath = "rate-plans/{id}",
            Description = "Ways of selling a room (for example Bed only, or Bed and breakfast), with deposit and cancellation terms.",
            Columns = [("Code", "Code"), ("Name", "Name"), ("Refundable", "IsRefundable"), ("Deposit %", "DepositPercent"), ("Cancellation policy", "CancellationPolicy"), ("Active", "IsActive")],
        }.Label("DepositPercent", "Deposit %").Kind("CancellationPolicy", FieldKind.LongText).Default("IsRefundable", true).Default("DepositPercent", 0m),

        new SetupEntity
        {
            Group = "Rooms and rates", Title = "Seasonal room rates", Singular = "room rate", Permission = P.RatesManage,
            DtoType = typeof(RoomRateDto), CreateType = typeof(CreateRoomRateRequest),
            ListPath = "room-rates", CreatePath = "room-rates",
            Description = "A nightly rate for a room type and rate plan over a date range. The last night is included.",
        }.Lookup("RatePlanId", RatePlans, "Rate plan").Lookup("RoomTypeId", RoomTypes, "Room type").Label("EndDate", "Last night"),

        new SetupEntity
        {
            Group = "Rooms and rates", Title = "Taxes", Singular = "tax", Permission = P.RatesManage,
            DtoType = typeof(TaxRuleDto), CreateType = typeof(CreateTaxRuleRequest), UpdateType = typeof(UpdateTaxRuleRequest),
            ListPath = "tax-rules", CreatePath = "tax-rules", UpdatePath = "tax-rules/{id}",
            Description = "VAT and other taxes, and whether they apply to rooms, services or both.",
        }.Label("RatePercent", "Rate %").Choices("Scope", "Room", "Service", "All").Label("IsInclusive", "Included in the price")
         .Label("EffectiveTo", "Effective until (optional)"),

        new SetupEntity
        {
            Group = "Rooms and rates", Title = "Services and extras", Singular = "service", Permission = P.RatesManage,
            DtoType = typeof(ServiceItemDto), CreateType = typeof(CreateServiceItemRequest), UpdateType = typeof(UpdateServiceItemRequest),
            ListPath = "service-items", CreatePath = "service-items", UpdatePath = "service-items/{id}",
            Description = "Things you charge to a guest's bill, such as laundry or airport pickup.",
        }.Default("IsTaxable", true),

        // ---- Staff ----
        new SetupEntity
        {
            Group = "Staff", Title = "Departments", Singular = "department", Permission = P.StaffManage,
            DtoType = typeof(DepartmentDto), CreateType = typeof(SaveDepartmentRequest), UpdateType = typeof(SaveDepartmentRequest),
            ListPath = "staff/departments", CreatePath = "staff/departments", UpdatePath = "staff/departments/{id}",
            Description = "Front office, housekeeping, kitchen and so on. Employees and handovers belong to a department.",
        },

        new SetupEntity
        {
            Group = "Staff", Title = "Work shifts", Singular = "shift", Permission = P.StaffManage,
            DtoType = typeof(WorkShiftDto), CreateType = typeof(SaveWorkShiftRequest), UpdateType = typeof(SaveWorkShiftRequest),
            ListPath = "staff/shifts", CreatePath = "staff/shifts", UpdatePath = "staff/shifts/{id}",
            Description = "Shift times used on the roster. An end time before the start means the shift ends the next day.",
        }.Kind("StartLocal", FieldKind.Time, "24-hour time, e.g. 07:00").Label("StartLocal", "Starts at")
         .Kind("EndLocal", FieldKind.Time, "24-hour time, e.g. 15:00").Label("EndLocal", "Ends at").Default("BreakMinutes", 30),

        new SetupEntity
        {
            Group = "Staff", Title = "Employees", Singular = "employee", Permission = P.StaffManage,
            DtoType = typeof(EmployeeDto), CreateType = typeof(SaveEmployeeRequest), UpdateType = typeof(SaveEmployeeRequest),
            ListPath = "staff/employees", CreatePath = "staff/employees", UpdatePath = "staff/employees/{id}",
            Description = "Staff records. Link an employee to a user account so they can clock in themselves.",
            Columns = [("Number", "EmployeeNumber"), ("First name", "FirstName"), ("Last name", "LastName"), ("Department", "DepartmentName"), ("Job title", "JobTitle"), ("Phone", "Phone"), ("Active", "IsActive")],
        }.Lookup("DepartmentId", Departments, "Department").Lookup("UserId", Users, "User account (optional)"),

        // ---- Inventory and purchasing ----
        new SetupEntity
        {
            Group = "Inventory and purchasing", Title = "Stock items", Singular = "stock item", Permission = P.InventoryManage,
            DtoType = typeof(InventoryItemDto), CreateType = typeof(SaveInventoryItemRequest), UpdateType = typeof(SaveInventoryItemRequest),
            ListPath = "inventory/items", CreatePath = "inventory/items", UpdatePath = "inventory/items/{id}",
            Description = "Everything you keep in stock. Add opening quantities on the Inventory screen with \"Receive stock\".",
            Columns = [("SKU", "Sku"), ("Name", "Name"), ("Unit", "Unit"), ("Category", "Category"), ("Reorder at", "ReorderLevel"), ("On hand", "QuantityOnHand"), ("Active", "IsActive")],
        }.Label("Sku", "SKU / item code").Suggest("Unit", "each", "kg", "g", "litre", "ml", "bottle", "pack", "box", "carton", "crate")
         .Choices("ExpenseAccountKey", ExpenseKeys).Label("ExpenseAccountKey", "Expense account").Default("ExpenseAccountKey", "FoodCost")
         .Default("ReorderLevel", 0m),

        new SetupEntity
        {
            Group = "Inventory and purchasing", Title = "Stores", Singular = "store", Permission = P.InventoryManage,
            DtoType = typeof(StockLocationDto), CreateType = typeof(SaveStockLocationRequest), UpdateType = typeof(SaveStockLocationRequest),
            ListPath = "inventory/locations", CreatePath = "inventory/locations", UpdatePath = "inventory/locations/{id}",
            Description = "Places stock is kept, such as the main store, kitchen and bar.",
        },

        new SetupEntity
        {
            Group = "Inventory and purchasing", Title = "Suppliers", Singular = "supplier", Permission = P.PurchasingManage,
            DtoType = typeof(SupplierDto), CreateType = typeof(SaveSupplierRequest), UpdateType = typeof(SaveSupplierRequest),
            ListPath = "inventory/suppliers", CreatePath = "inventory/suppliers", UpdatePath = "inventory/suppliers/{id}",
            Columns = [("Code", "Code"), ("Name", "Name"), ("Contact", "ContactName"), ("Phone", "Phone"), ("Email", "Email"), ("Terms (days)", "PaymentTermsDays"), ("Active", "IsActive")],
        }.Kind("Address", FieldKind.LongText).Label("PaymentTermsDays", "Payment terms (days)").Default("PaymentTermsDays", 30),

        // ---- Restaurant ----
        new SetupEntity
        {
            Group = "Restaurant", Title = "Outlets", Singular = "outlet", Permission = P.RestaurantManage,
            DtoType = typeof(OutletDto), CreateType = typeof(SaveOutletRequest), UpdateType = typeof(SaveOutletRequest),
            ListPath = "restaurant/outlets", CreatePath = "restaurant/outlets", UpdatePath = "restaurant/outlets/{id}",
            Description = "Restaurants, bars and room service. Each takes its stock from one store.",
        }.Choices("Type", "Restaurant", "Bar", "RoomService").Lookup("StockLocationId", StockLocations, "Takes stock from"),

        new SetupEntity
        {
            Group = "Restaurant", Title = "Tables", Singular = "table", Permission = P.RestaurantManage, Parent = Outlets,
            DtoType = typeof(TableDto), CreateType = typeof(SaveTableRequest), UpdateType = typeof(SaveTableRequest),
            ListPath = "restaurant/outlets/{parent}/tables", CreatePath = "restaurant/outlets/{parent}/tables", UpdatePath = "restaurant/outlets/{parent}/tables/{id}",
            Columns = [("Table", "TableNumber"), ("Seats", "Seats"), ("Active", "IsActive")],
        }.Default("Seats", 4),

        new SetupEntity
        {
            Group = "Restaurant", Title = "Menu categories", Singular = "menu category", Permission = P.RestaurantManage, Parent = Outlets,
            DtoType = typeof(MenuCategoryDto), CreateType = typeof(SaveMenuCategoryRequest),
            ListPath = "restaurant/outlets/{parent}/categories", CreatePath = "restaurant/outlets/{parent}/categories",
            Columns = [("Name", "Name"), ("Order", "SortOrder")],
        }.Label("SortOrder", "Position on the menu").Default("SortOrder", 1),

        new SetupEntity
        {
            Group = "Restaurant", Title = "Menu items", Singular = "menu item", Permission = P.RestaurantManage, Parent = Outlets,
            DtoType = typeof(MenuItemDto), CreateType = typeof(SaveMenuItemRequest), UpdateType = typeof(SaveMenuItemRequest),
            ListPath = "restaurant/outlets/{parent}/menu", CreatePath = "restaurant/outlets/{parent}/menu", UpdatePath = "restaurant/outlets/{parent}/menu/{id}",
            Columns = [("Code", "Code"), ("Name", "Name"), ("Category", "CategoryId"), ("Price", "Price"), ("Taxable", "IsTaxable"), ("Station", "Station"), ("Active", "IsActive")],
        }.Lookup("CategoryId", MenuCategories, "Category").Suggest("Station", "Kitchen", "Bar", "Grill", "Pastry").Default("Station", "Kitchen")
         .Default("IsTaxable", true),

        // ---- Maintenance ----
        new SetupEntity
        {
            Group = "Maintenance", Title = "Assets", Singular = "asset", Permission = P.MaintenanceManage,
            DtoType = typeof(AssetDto), CreateType = typeof(SaveAssetRequest), UpdateType = typeof(SaveAssetRequest),
            ListPath = "maintenance/assets", CreatePath = "maintenance/assets", UpdatePath = "maintenance/assets/{id}",
            Description = "Equipment you maintain, such as air conditioners, generators and lifts.",
            Columns = [("Code", "Code"), ("Name", "Name"), ("Category", "Category"), ("Room", "RoomId"), ("Location", "Location"), ("Status", "Status"), ("Maintenance cost", "MaintenanceCost")],
        }.Lookup("RoomId", Rooms, "Room (optional)").Choices("Status", "InService", "UnderMaintenance", "Retired")
         .Suggest("Category", "Air conditioning", "Electrical", "Plumbing", "Generator", "Kitchen equipment", "Furniture", "IT"),

        new SetupEntity
        {
            Group = "Maintenance", Title = "Preventive plans", Singular = "preventive plan", Permission = P.MaintenanceManage,
            DtoType = typeof(PreventivePlanDto), CreateType = typeof(SavePreventivePlanRequest), UpdateType = typeof(SavePreventivePlanRequest),
            ListPath = "maintenance/preventive-plans", CreatePath = "maintenance/preventive-plans", UpdatePath = "maintenance/preventive-plans/{id}",
            Description = "Routine servicing. Work orders are created automatically when a plan falls due.",
        }.Lookup("AssetId", Assets, "Asset (optional)").Lookup("RoomId", Rooms, "Room (optional)").Choices("Priority", Priorities)
         .Default("Priority", "Normal").Label("IntervalDays", "Every (days)").Default("IntervalDays", 90),

        // ---- Accounting ----
        new SetupEntity
        {
            Group = "Accounting", Title = "Chart of accounts", Singular = "account", Permission = P.AccountingManage,
            DtoType = typeof(AccountDto), CreateType = typeof(SaveAccountRequest), UpdateType = typeof(SaveAccountRequest),
            ListPath = "accounting/accounts", CreatePath = "accounting/accounts", UpdatePath = "accounting/accounts/{id}",
        }.Choices("Type", "Asset", "Liability", "Equity", "Revenue", "Expense").Label("IsControl", "Control account (posted by the system only)"),

        new SetupEntity
        {
            Group = "Accounting", Title = "Posting accounts", Singular = "posting account", Permission = P.AccountingManage,
            DtoType = typeof(AccountMappingDto), CreateType = typeof(SetAccountMappingRequest), UpdateType = typeof(SetAccountMappingRequest),
            ListPath = "accounting/mappings", CreatePath = "accounting/mappings", UpdatePath = "accounting/mappings", CreateWithPut = true,
            Description = "Which account the system posts to for each purpose, such as room revenue or cash.",
            Columns = [("Purpose", "Key"), ("Account", "AccountCode"), ("Name", "AccountName")],
        }.Choices("Key", "Cash", "Bank", "PosSettlement", "GuestReceivable", "Inventory", "AccountsPayable", "GoodsReceivedNotInvoiced",
            "GuestDeposits", "TaxPayable", "RoomRevenue", "RestaurantRevenue", "BarRevenue", "OtherRevenue", "FoodCost", "HousekeepingSupplies",
            "Wages", "Repairs", "BankFees", "InventoryAdjustments", "PurchasePriceVariance", "CashOverShort", "OpeningBalances")
         .Label("Key", "Purpose").Lookup("AccountId", Accounts, "Account"),

        // ---- Hotel ----
        new SetupEntity
        {
            Group = "Hotel", Title = "Branch details", Singular = "branch", Permission = P.BranchesManage, SingleRecord = true,
            DtoType = typeof(BranchDto), CreateType = typeof(UpdateBranchRequest), UpdateType = typeof(UpdateBranchRequest),
            ListPath = "", CreatePath = "", UpdatePath = "",
            Description = "Name, address, currency, and when the business day ends.",
            Columns = [("Code", "Code"), ("Name", "Name"), ("City", "City"), ("Phone", "Phone"), ("Currency", "Currency"), ("Day ends at", "BusinessDayCutoffLocal"), ("Business date", "CurrentBusinessDate")],
        }.Suggest("TimeZoneId", "Africa/Lagos", "Africa/Accra", "Africa/Nairobi", "Africa/Johannesburg", "Europe/London", "UTC").Label("TimeZoneId", "Time zone")
         .Suggest("Currency", "NGN", "USD", "GBP", "EUR", "GHS", "KES", "ZAR").Label("AddressLine", "Address")
         .Label("BusinessDayCutoffLocal", "Business day ends at", "24-hour time, e.g. 04:00").Kind("BusinessDayCutoffLocal", FieldKind.Time)
         .Label("HousekeepingRequiresInspection", "Rooms must be inspected after cleaning"),

        new SetupEntity
        {
            Group = "Hotel", Title = "Message templates", Singular = "message template", Permission = P.NotificationsManage, SaveCreatesVersion = true,
            DtoType = typeof(TemplateDto), CreateType = typeof(SaveTemplateRequest),
            ListPath = "notifications/templates", CreatePath = "notifications/templates",
            Description = "Emails and text messages sent to guests and staff. Use placeholders like {{GuestName}}. Saving keeps the old version.",
            Columns = [("Event", "EventType"), ("Channel", "Channel"), ("Language", "Language"), ("Version", "Version"), ("Subject", "Subject"), ("Active", "IsActive")],
        }.Choices("EventType", "ReservationConfirmed", "GuestCheckedOut", "PaymentConfirmed", "RefundStatusChanged", "LowStockDetected",
            "MaintenanceAssigned", "ShiftVarianceDetected", "DailyReportReady", "OnlinePaymentNeedsReview", "CancellationRequested")
         .Label("EventType", "Event").Choices("Channel", "Email", "Sms", "InApp").Suggest("Language", "en").Default("Language", "en")
         .Kind("Body", FieldKind.LongText).Label("BranchSpecific", "Only for this branch").Default("BranchSpecific", true),
    ];
}
