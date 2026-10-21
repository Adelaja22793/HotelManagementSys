using HMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace HMS.Application.Abstractions;

/// <summary>Unit of work over the HMS database. Implemented by the infrastructure DbContext.</summary>
public interface IApplicationDbContext
{
    DbSet<Company> Companies { get; }
    DbSet<Branch> Branches { get; }
    DbSet<HotelUserBranch> UserBranches { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<BranchSequence> BranchSequences { get; }

    DbSet<RoomType> RoomTypes { get; }
    DbSet<Room> Rooms { get; }
    DbSet<RoomBlock> RoomBlocks { get; }
    DbSet<RatePlan> RatePlans { get; }
    DbSet<RoomRate> RoomRates { get; }
    DbSet<TaxRule> TaxRules { get; }
    DbSet<ServiceItem> ServiceItems { get; }
    DbSet<Guest> Guests { get; }
    DbSet<RoomNightInventory> RoomNightInventory { get; }
    DbSet<AssignedRoomNight> AssignedRoomNights { get; }
    DbSet<Reservation> Reservations { get; }
    DbSet<ReservationNight> ReservationNights { get; }
    DbSet<Stay> Stays { get; }
    DbSet<HousekeepingTask> HousekeepingTasks { get; }
    DbSet<Folio> Folios { get; }
    DbSet<FolioCharge> FolioCharges { get; }
    DbSet<Payment> Payments { get; }
    DbSet<Receipt> Receipts { get; }
    DbSet<Refund> Refunds { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<CashierShift> CashierShifts { get; }
    DbSet<CashierMovement> CashierMovements { get; }

    DbSet<Supplier> Suppliers { get; }
    DbSet<StockLocation> StockLocations { get; }
    DbSet<InventoryItem> InventoryItems { get; }
    DbSet<StockBalance> StockBalances { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<PurchaseRequisition> PurchaseRequisitions { get; }
    DbSet<PurchaseRequisitionLine> PurchaseRequisitionLines { get; }
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderLine> PurchaseOrderLines { get; }
    DbSet<GoodsReceipt> GoodsReceipts { get; }
    DbSet<GoodsReceiptLine> GoodsReceiptLines { get; }
    DbSet<SupplierInvoice> SupplierInvoices { get; }
    DbSet<SupplierInvoiceLine> SupplierInvoiceLines { get; }
    DbSet<SupplierPayment> SupplierPayments { get; }
    DbSet<RestaurantOutlet> RestaurantOutlets { get; }
    DbSet<RestaurantTable> RestaurantTables { get; }
    DbSet<MenuCategory> MenuCategories { get; }
    DbSet<MenuItem> MenuItems { get; }
    DbSet<MenuItemIngredient> MenuItemIngredients { get; }
    DbSet<RestaurantOrder> RestaurantOrders { get; }
    DbSet<RestaurantOrderLine> RestaurantOrderLines { get; }
    DbSet<KitchenTicket> KitchenTickets { get; }
    DbSet<KitchenTicketLine> KitchenTicketLines { get; }
    DbSet<Asset> Assets { get; }
    DbSet<MaintenanceRequest> MaintenanceRequests { get; }
    DbSet<WorkOrder> WorkOrders { get; }
    DbSet<WorkOrderPartUsage> WorkOrderParts { get; }
    DbSet<PreventiveMaintenancePlan> PreventivePlans { get; }
    DbSet<PreventiveMaintenanceOccurrence> PreventiveOccurrences { get; }
    DbSet<Department> Departments { get; }
    DbSet<Employee> Employees { get; }
    DbSet<WorkShift> WorkShifts { get; }
    DbSet<RosterEntry> RosterEntries { get; }
    DbSet<AttendanceRecord> AttendanceRecords { get; }
    DbSet<LeaveRecord> LeaveRecords { get; }
    DbSet<OvertimeRequest> OvertimeRequests { get; }
    DbSet<ShiftHandover> ShiftHandovers { get; }
    DbSet<Account> Accounts { get; }
    DbSet<AccountMapping> AccountMappings { get; }
    DbSet<AccountingPeriod> AccountingPeriods { get; }
    DbSet<JournalEntry> JournalEntries { get; }
    DbSet<JournalEntryLine> JournalEntryLines { get; }
    DbSet<BusinessDayClose> BusinessDayCloses { get; }
    DbSet<BusinessDayException> BusinessDayExceptions { get; }
    DbSet<BranchDailyMetric> BranchDailyMetrics { get; }
    DbSet<BranchDailyDepartmentMetric> BranchDailyDepartmentMetrics { get; }
    DbSet<AnalyticsRefreshRun> AnalyticsRefreshRuns { get; }
    DbSet<NotificationTemplate> NotificationTemplates { get; }
    DbSet<NotificationOutbox> NotificationOutbox { get; }
    DbSet<NotificationDelivery> NotificationDeliveries { get; }
    DbSet<NotificationPreference> NotificationPreferences { get; }
    DbSet<NotificationRead> NotificationReads { get; }
    DbSet<DocumentRecord> Documents { get; }
    DbSet<DocumentVersion> DocumentVersions { get; }
    DbSet<DocumentSignature> DocumentSignatures { get; }
    DbSet<SecurityIncident> SecurityIncidents { get; }
    DbSet<IncidentAction> IncidentActions { get; }
    DbSet<LostFoundItem> LostFoundItems { get; }
    DbSet<LostFoundCustodyEvent> LostFoundCustodyEvents { get; }
    DbSet<VisitorLog> VisitorLogs { get; }
    DbSet<GuestComplaint> GuestComplaints { get; }
    DbSet<EmergencyEvent> EmergencyEvents { get; }
    DbSet<EmergencyAction> EmergencyActions { get; }

    DatabaseFacade Database { get; }

    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
