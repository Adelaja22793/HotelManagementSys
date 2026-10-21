using HMS.Contracts.Auth;
using HMS.Contracts.Billing;
using HMS.Contracts.Branches;
using HMS.Contracts.Cashier;
using HMS.Contracts.Housekeeping;
using HMS.Contracts.Inventory;
using HMS.Contracts.Maintenance;
using HMS.Contracts.PublicBooking;
using HMS.Contracts.Purchasing;
using HMS.Contracts.Rooms;
using HMS.Contracts.Staff;
using P = HMS.Desktop.Services.PermissionNames;
using S = HMS.Desktop.ViewModels.Setup.SetupCatalog;

namespace HMS.Desktop.ViewModels.Setup;

/// <summary>The task buttons each screen offers, beyond what the screen itself shows.</summary>
public static class ActionCatalog
{
    private static string Money(decimal m) => m.ToString("N2", System.Globalization.CultureInfo.CurrentCulture);

    public static readonly LookupSource Items = new("Stock items", typeof(InventoryItemDto), _ => "inventory/items",
        i => $"{((InventoryItemDto)i).Sku} · {((InventoryItemDto)i).Name} ({((InventoryItemDto)i).Unit})", filter: i => ((InventoryItemDto)i).IsActive);
    public static readonly LookupSource Stores = new("Stores", typeof(StockLocationDto), _ => "inventory/locations", filter: l => ((StockLocationDto)l).IsActive);
    public static readonly LookupSource Suppliers = new("Suppliers", typeof(SupplierDto), _ => "inventory/suppliers", filter: s => ((SupplierDto)s).IsActive);
    public static readonly LookupSource ApprovedRequisitions = new("Approved requisitions", typeof(RequisitionDto), _ => "purchasing/requisitions",
        r => $"{((RequisitionDto)r).RequisitionNumber} ({((RequisitionDto)r).Lines.Count} lines)", filter: r => ((RequisitionDto)r).Status == "Approved");
    public static readonly LookupSource OpenOrders = new("Open purchase orders", typeof(PurchaseOrderDto), _ => "purchasing/orders", OrderLabel,
        filter: o => ((PurchaseOrderDto)o).Status is "Draft" or "Approved" or "PartiallyReceived");
    public static readonly LookupSource ReceivedOrders = new("Purchase orders with goods received", typeof(PurchaseOrderDto), _ => "purchasing/orders", OrderLabel,
        filter: o => ((PurchaseOrderDto)o).Lines.Any(l => l.ReceivedQuantity > l.InvoicedQuantity));
    public static readonly LookupSource InvoicesToApprove = new("Invoices with a difference", typeof(SupplierInvoiceDto), _ => "purchasing/invoices", InvoiceLabel,
        filter: i => ((SupplierInvoiceDto)i).Status == "Discrepancy");
    public static readonly LookupSource InvoicesToPay = new("Unpaid invoices", typeof(SupplierInvoiceDto), _ => "purchasing/invoices", InvoiceLabel,
        filter: i => ((SupplierInvoiceDto)i).Status is "Matched" or "Approved" or "PartiallyPaid");
    public static readonly LookupSource RequestedRefunds = new("Refunds waiting for approval", typeof(RefundDto), _ => "refunds?status=Requested", RefundLabel);
    public static readonly LookupSource ApprovedRefunds = new("Approved refunds", typeof(RefundDto), _ => "refunds?status=Approved", RefundLabel);
    public static readonly LookupSource ShiftsWithVariance = new("Shifts waiting for variance approval", typeof(CashierShiftDto), _ => "cashier/shifts?status=Closing",
        s => $"{((CashierShiftDto)s).BusinessDate:dd MMM} · cash variance {Money(((CashierShiftDto)s).CashVariance ?? 0)}");
    public static readonly LookupSource ActiveBlocks = new("Room blocks", typeof(RoomBlockDto), _ => "room-blocks",
        b => $"{((RoomBlockDto)b).RoomNumber} · {((RoomBlockDto)b).StartDate:dd MMM} to {((RoomBlockDto)b).EndDate:dd MMM} · {((RoomBlockDto)b).Reason}",
        filter: b => ((RoomBlockDto)b).IsActive);
    public static readonly LookupSource Employees = new("Employees", typeof(EmployeeDto), _ => "staff/employees", filter: e => ((EmployeeDto)e).IsActive);
    public static readonly LookupSource PendingLeave = new("Leave requests", typeof(LeaveDto), _ => "staff/leave",
        l => $"{((LeaveDto)l).Type} · {((LeaveDto)l).StartDate:dd MMM} to {((LeaveDto)l).EndDate:dd MMM}", filter: l => ((LeaveDto)l).Status == "Pending");
    public static readonly LookupSource PendingOvertime = new("Overtime requests", typeof(OvertimeDto), _ => "staff/overtime",
        o => $"{((OvertimeDto)o).WorkDate:dd MMM} · {((OvertimeDto)o).Hours} h · {((OvertimeDto)o).Reason}", filter: o => ((OvertimeDto)o).Status == "Pending");
    public static readonly LookupSource PaymentsToReview = new("Online payments needing review", typeof(OnlinePaymentDto), _ => "online-payments?status=NeedsReview",
        p => $"{((OnlinePaymentDto)p).HoldReference} · {((OnlinePaymentDto)p).GuestName} · {Money(((OnlinePaymentDto)p).Amount)}");

    private static string OrderLabel(object o) => $"{((PurchaseOrderDto)o).OrderNumber} · {((PurchaseOrderDto)o).SupplierName} · {Money(((PurchaseOrderDto)o).Total)}";
    private static string InvoiceLabel(object i) => $"{((SupplierInvoiceDto)i).InvoiceNumber} · {((SupplierInvoiceDto)i).SupplierName} · outstanding {Money(((SupplierInvoiceDto)i).Outstanding)}";
    private static string RefundLabel(object r) => $"{((RefundDto)r).RefundNumber} · {Money(((RefundDto)r).Amount)} · {((RefundDto)r).Reason}";

    private static IEnumerable<FieldOption> OrderLines(object order) =>
        ((PurchaseOrderDto)order).Lines.Select(l => new FieldOption(l.Id, $"{l.Sku} · {l.ItemName} · received {l.ReceivedQuantity:0.##}, invoiced {l.InvoicedQuantity:0.##} at {Money(l.UnitCost)}", l));

    // ---- Inventory and purchasing ----

    public static IReadOnlyList<ActionDefinition> Inventory { get; } =
    [
        new ActionDefinition
        {
            Title = "Receive stock", Permission = P.InventoryManage, RequestType = typeof(StockReceiptRequest), Path = "inventory/receipts",
            Description = "Add stock without a purchase order: opening quantities, donations or stock found in a count.",
            SuccessMessage = "Stock received.",
        }.Field("ItemId", f => { f.Lookup = Items; f.Label = "Item"; }).Field("LocationId", f => { f.Lookup = Stores; f.Label = "Into store"; })
         .Field("UnitCost", f => f.Label = "Cost per unit").Field("Reason", f => { f.Choices = ["Opening balance", "Donation", "Found in count", "Returned from outlet"]; f.AllowOwnValue = true; f.Default = "Opening balance"; }),

        new ActionDefinition
        {
            Title = "Move stock", Permission = P.InventoryManage, RequestType = typeof(StockTransferRequest), Path = "inventory/transfers",
            Description = "Move stock between stores, for example from the main store to the kitchen.", SuccessMessage = "Stock moved.",
            Validate = r => ((StockTransferRequest)r).FromLocationId == ((StockTransferRequest)r).ToLocationId ? "Choose two different stores." : null,
        }.Field("ItemId", f => { f.Lookup = Items; f.Label = "Item"; }).Field("FromLocationId", f => { f.Lookup = Stores; f.Label = "From store"; })
         .Field("ToLocationId", f => { f.Lookup = Stores; f.Label = "To store"; }),

        new ActionDefinition
        {
            Title = "New requisition", Permission = P.InventoryView, RequestType = typeof(CreateRequisitionRequest), Path = "purchasing/requisitions",
            Description = "Ask for items to be bought. A manager approves it before a purchase order is raised.", SuccessMessage = "Requisition sent for approval.",
        }.Line("ItemId", f => { f.Lookup = Items; f.Label = "Item"; }).Line("EstimatedUnitCost", f => f.Label = "Estimated cost per unit (needed to raise an order from it)"),

        new ActionDefinition
        {
            Title = "New purchase order", Permission = P.PurchasingManage, RequestType = typeof(CreatePurchaseOrderRequest), Path = "purchasing/orders",
            Description = "Order from a supplier. Add the lines, or pick an approved requisition to copy its lines.", LinesOptional = true,
            SuccessMessage = "Purchase order created. It needs approval before goods can be received.",
            Validate = r => ((CreatePurchaseOrderRequest)r) is { RequisitionId: null, Lines.Count: 0 } ? "Add at least one line, or choose an approved requisition." : null,
        }.Field("SupplierId", f => { f.Lookup = Suppliers; f.Label = "Supplier"; }).Field("DeliveryLocationId", f => { f.Lookup = Stores; f.Label = "Deliver to store"; })
         .Field("RequisitionId", f => { f.Lookup = ApprovedRequisitions; f.Label = "From requisition (optional)"; })
         .Line("ItemId", f => { f.Lookup = Items; f.Label = "Item"; }).Line("UnitCost", f => f.Label = "Cost per unit"),

        new ActionDefinition
        {
            Title = "Cancel purchase order", Permission = P.PurchasingManage, RequestType = typeof(PurchaseOrderActionRequest), Path = "purchasing/orders/{id}/cancel",
            Target = OpenOrders, Description = "Cancel an order that has not been received.", SuccessMessage = "Purchase order cancelled.",
        },

        new ActionDefinition
        {
            Title = "Close purchase order", Permission = P.PurchasingManage, RequestType = typeof(PurchaseOrderActionRequest), Path = "purchasing/orders/{id}/close",
            Target = OpenOrders, Description = "Close an order when nothing more will be delivered.", SuccessMessage = "Purchase order closed.",
        },

        new ActionDefinition
        {
            Title = "Record supplier invoice", Permission = P.PurchasingManage, RequestType = typeof(CreateSupplierInvoiceRequest), Path = "purchasing/invoices",
            Target = ReceivedOrders, Description = "Enter the supplier's invoice. It is matched against the goods received.",
            EmptyTargetMessage = "No purchase order has goods waiting to be invoiced.", SuccessMessage = "Invoice recorded.",
        }.Field("PurchaseOrderId", f => f.TargetId = true).Field("SupplierReference", f => f.Label = "Supplier's invoice number")
         .Line("PurchaseOrderLineId", f => { f.FromTarget = OrderLines; f.Label = "Order line"; }).Line("UnitPrice", f => f.Label = "Price per unit"),

        new ActionDefinition
        {
            Title = "Approve supplier invoice", Permission = P.PurchasingApprove, RequestType = typeof(PurchaseOrderActionRequest),
            Path = "purchasing/invoices/{id}/approve", Target = InvoicesToApprove,
            Description = "Accept an invoice whose quantities or prices differ from what was received.", SuccessMessage = "Invoice approved for payment.",
        },

        new ActionDefinition
        {
            Title = "Pay supplier", Permission = P.PurchasingManage, RequestType = typeof(PaySupplierRequest), Path = "purchasing/invoices/{id}/payments",
            Target = InvoicesToPay, PrefillFromTarget = true, SuccessMessage = "Payment recorded.",
        }.Field("Amount", f => f.Label = "Amount paid").Field("Tender", f => { f.Choices = ["BankTransfer", "Cash"]; f.Default = "BankTransfer"; })
         .Field("Reference", f => f.Label = "Payment reference"),
    ];

    // ---- Rooms ----

    public static IReadOnlyList<ActionDefinition> Rooms { get; } =
    [
        new ActionDefinition
        {
            Title = "Block a room", Permission = P.RoomsManage, RequestType = typeof(CreateRoomBlockRequest), Path = "room-blocks",
            Description = "Take a room out of sale, for example for repairs. It is sellable again on the end date.", SuccessMessage = "Room blocked.",
        }.Field("RoomId", f => { f.Lookup = S.Rooms; f.Label = "Room"; }).Field("EndDate", f => { f.Label = "Available again on"; f.DaysAhead = 1; })
         .Field("Reason", f => { f.Choices = ["Maintenance", "Renovation", "Deep cleaning", "Owner use"]; f.AllowOwnValue = true; }),

        new ActionDefinition
        {
            Title = "Release a room block", Permission = P.RoomsManage, RequestType = typeof(NoBody), Path = "room-blocks/{id}/release",
            Target = ActiveBlocks, SuccessMessage = "The room can be sold again.",
        },

        new ActionDefinition
        {
            Title = "Limit rooms for sale", Permission = P.RatesManage, RequestType = typeof(SetSellLimitRequest), Path = "inventory/sell-limit", UsePut = true,
            Description = "Sell at most this many rooms of a type on these dates. Leave the limit empty to remove it.", SuccessMessage = "Sell limit saved.",
        }.Field("RoomTypeId", f => { f.Lookup = S.RoomTypes; f.Label = "Room type"; }).Field("EndDate", f => { f.Label = "Until (not including)"; f.DaysAhead = 1; })
         .Field("SellLimit", f => f.Label = "Rooms for sale (empty = no limit)"),
    ];

    // ---- Housekeeping and maintenance ----

    public static IReadOnlyList<ActionDefinition> Housekeeping { get; } =
    [
        new ActionDefinition
        {
            Title = "New cleaning task", Permission = P.HousekeepingWork, RequestType = typeof(CreateHousekeepingTaskRequest), Path = "housekeeping/tasks",
            SuccessMessage = "Cleaning task created.",
        }.Field("RoomId", f => { f.Lookup = S.Rooms; f.Label = "Room"; }).Field("Type", f => { f.Choices = ["Checkout", "Stayover", "Touchup"]; f.Default = "Touchup"; }),
    ];

    public static IReadOnlyList<ActionDefinition> Maintenance { get; } =
    [
        new ActionDefinition
        {
            Title = "New work order", Permission = P.MaintenanceManage, RequestType = typeof(CreateWorkOrderRequest), Path = "maintenance/work-orders",
            Description = "Plan a repair directly, without a reported request.", SuccessMessage = "Work order created.",
        }.Field("Priority", f => { f.Choices = S.Priorities; f.Default = "Normal"; }).Field("RoomId", f => { f.Lookup = S.Rooms; f.Label = "Room (optional)"; })
         .Field("AssetId", f => { f.Lookup = S.Assets; f.Label = "Asset (optional)"; }).Field("AssignToUserId", f => { f.Lookup = S.Users; f.Label = "Assign to (optional)"; })
         .Field("DueDate", f => f.Label = "Due date (optional)").Field("TakeRoomOutOfService", f => f.Label = "Take the room out of sale")
         .Field("ExpectedReadyDate", f => f.Label = "Room ready again on (optional)").Field("Description", f => f.Kind = FieldKind.LongText),
    ];

    // ---- Money ----

    public static IReadOnlyList<ActionDefinition> Folios { get; } =
    [
        new ActionDefinition
        {
            Title = "Approve or reject a refund", Permission = P.RefundsApprove, RequestType = typeof(RefundDecisionRequest), Path = "refunds/{id}/decision",
            Target = RequestedRefunds, SuccessMessage = "Refund decision saved.",
        }.Field("Approved", f => { f.Label = "Approve this refund"; f.Default = true; }).Field("RejectionReason", f => f.Label = "Reason if rejected"),

        new ActionDefinition
        {
            Title = "Pay out a refund", Permission = P.RefundsApprove, RequestType = typeof(ExecuteRefundRequest), Path = "refunds/{id}/execute",
            Target = ApprovedRefunds, Description = "Record that an approved refund has been paid to the guest.", SuccessMessage = "Refund paid out.",
        }.Field("ExternalReference", f => f.Label = "Transfer or POS reference (optional)"),
    ];

    public static IReadOnlyList<ActionDefinition> Cashier { get; } =
    [
        new ActionDefinition
        {
            Title = "Approve shift variance", Permission = P.CashierApproveVariance, RequestType = typeof(ApproveVarianceRequest),
            Path = "cashier/shifts/{id}/approve-variance", Target = ShiftsWithVariance,
            Description = "Accept the difference between the expected and counted money so the shift can close.", SuccessMessage = "Variance approved and the shift is closed.",
        },
    ];

    // ---- Staff ----

    public static IReadOnlyList<ActionDefinition> Staff { get; } =
    [
        new ActionDefinition
        {
            Title = "Request overtime", Permission = P.StaffManage, RequestType = typeof(CreateOvertimeRequest), Path = "staff/overtime",
            SuccessMessage = "Overtime request saved.",
        }.Field("EmployeeId", f => { f.Lookup = Employees; f.Label = "Employee"; }),

        new ActionDefinition
        {
            Title = "Approve or reject overtime", Permission = P.StaffManage, RequestType = typeof(StaffDecisionRequest), Path = "staff/overtime/{id}/decision",
            Target = PendingOvertime, SuccessMessage = "Overtime decision saved.",
        }.Field("Approved", f => { f.Label = "Approve"; f.Default = true; }),

        new ActionDefinition
        {
            Title = "Approve or reject leave", Permission = P.StaffManage, RequestType = typeof(StaffDecisionRequest), Path = "staff/leave/{id}/decision",
            Target = PendingLeave, SuccessMessage = "Leave decision saved.",
        }.Field("Approved", f => { f.Label = "Approve"; f.Default = true; }),
    ];

    // ---- Hotel and account ----

    public static IReadOnlyList<ActionDefinition> Setup { get; } =
    [
        new ActionDefinition
        {
            Title = "Online booking on or off", Permission = P.BranchesManage, RequestType = typeof(PublicBookingSettingsRequest), Path = "public-booking", UsePut = true,
            Description = "Show or hide this branch on the guest booking website.", SuccessMessage = "Online booking setting saved.",
        }.Field("Enabled", f => f.Label = "Guests can book this branch online"),

        new ActionDefinition
        {
            Title = "Resolve an online payment", Permission = P.RefundsApprove, RequestType = typeof(ResolvePaymentRequest), Path = "online-payments/{id}/resolve",
            Target = PaymentsToReview, Description = "Close a payment that needed review, once the guest has been refunded or rebooked.",
            EmptyTargetMessage = "No online payment needs review.", SuccessMessage = "Payment marked as resolved.",
        }.Field("Note", f => { f.Kind = FieldKind.LongText; f.Label = "What was done"; }),

        new ActionDefinition
        {
            Title = "Add a branch", Permission = P.SystemManage, RequestType = typeof(CreateBranchRequest), Path = "api/v1/branches",
            Description = "Open another hotel location. Give users access to it on the Users screen.", SuccessMessage = "Branch created. Sign out and in again to switch to it.",
        }.Field("TimeZoneId", f => { f.Choices = ["Africa/Lagos", "Africa/Accra", "Africa/Nairobi", "Africa/Johannesburg", "Europe/London", "UTC"]; f.AllowOwnValue = true; f.Label = "Time zone"; f.Default = "Africa/Lagos"; })
         .Field("Currency", f => { f.Choices = ["NGN", "USD", "GBP", "EUR", "GHS", "KES", "ZAR"]; f.AllowOwnValue = true; f.Default = "NGN"; })
         .Field("AddressLine", f => f.Label = "Address"),
    ];

    public static IReadOnlyList<ActionDefinition> Account { get; } =
    [
        new ActionDefinition
        {
            Title = "Change my password", Permission = P.BranchesView, RequestType = typeof(ChangePasswordRequest), Path = "api/v1/auth/change-password",
            SuccessMessage = "Your password is changed.",
            Validate = r => ((ChangePasswordRequest)r).NewPassword.Length < 12 ? "The new password must be at least 12 characters." : null,
        }.Field("CurrentPassword", f => f.Kind = FieldKind.Secret).Field("NewPassword", f => f.Kind = FieldKind.Secret),
    ];
}

/// <summary>For actions the server takes without a body.</summary>
public sealed record NoBody;
