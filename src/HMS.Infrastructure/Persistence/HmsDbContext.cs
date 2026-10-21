using HMS.Application.Abstractions;
using HMS.Domain.Common;
using HMS.Domain.Entities;
using HMS.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HMS.Infrastructure.Persistence;

public class HmsDbContext(DbContextOptions<HmsDbContext> options)
    : IdentityDbContext<ApplicationUser>(options), IApplicationDbContext
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<HotelUserBranch> UserBranches => Set<HotelUserBranch>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<BranchSequence> BranchSequences => Set<BranchSequence>();

    public DbSet<RoomType> RoomTypes => Set<RoomType>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomBlock> RoomBlocks => Set<RoomBlock>();
    public DbSet<RatePlan> RatePlans => Set<RatePlan>();
    public DbSet<RoomRate> RoomRates => Set<RoomRate>();
    public DbSet<TaxRule> TaxRules => Set<TaxRule>();
    public DbSet<ServiceItem> ServiceItems => Set<ServiceItem>();
    public DbSet<Guest> Guests => Set<Guest>();
    public DbSet<RoomNightInventory> RoomNightInventory => Set<RoomNightInventory>();
    public DbSet<AssignedRoomNight> AssignedRoomNights => Set<AssignedRoomNight>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ReservationNight> ReservationNights => Set<ReservationNight>();
    public DbSet<Stay> Stays => Set<Stay>();
    public DbSet<HousekeepingTask> HousekeepingTasks => Set<HousekeepingTask>();
    public DbSet<Folio> Folios => Set<Folio>();
    public DbSet<FolioCharge> FolioCharges => Set<FolioCharge>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<CashierShift> CashierShifts => Set<CashierShift>();
    public DbSet<CashierMovement> CashierMovements => Set<CashierMovement>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<StockLocation> StockLocations => Set<StockLocation>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<PurchaseRequisition> PurchaseRequisitions => Set<PurchaseRequisition>();
    public DbSet<PurchaseRequisitionLine> PurchaseRequisitionLines => Set<PurchaseRequisitionLine>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();
    public DbSet<SupplierInvoice> SupplierInvoices => Set<SupplierInvoice>();
    public DbSet<SupplierInvoiceLine> SupplierInvoiceLines => Set<SupplierInvoiceLine>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<RestaurantOutlet> RestaurantOutlets => Set<RestaurantOutlet>();
    public DbSet<RestaurantTable> RestaurantTables => Set<RestaurantTable>();
    public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<MenuItemIngredient> MenuItemIngredients => Set<MenuItemIngredient>();
    public DbSet<RestaurantOrder> RestaurantOrders => Set<RestaurantOrder>();
    public DbSet<RestaurantOrderLine> RestaurantOrderLines => Set<RestaurantOrderLine>();
    public DbSet<KitchenTicket> KitchenTickets => Set<KitchenTicket>();
    public DbSet<KitchenTicketLine> KitchenTicketLines => Set<KitchenTicketLine>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<MaintenanceRequest> MaintenanceRequests => Set<MaintenanceRequest>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderPartUsage> WorkOrderParts => Set<WorkOrderPartUsage>();
    public DbSet<PreventiveMaintenancePlan> PreventivePlans => Set<PreventiveMaintenancePlan>();
    public DbSet<PreventiveMaintenanceOccurrence> PreventiveOccurrences => Set<PreventiveMaintenanceOccurrence>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<WorkShift> WorkShifts => Set<WorkShift>();
    public DbSet<RosterEntry> RosterEntries => Set<RosterEntry>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<LeaveRecord> LeaveRecords => Set<LeaveRecord>();
    public DbSet<OvertimeRequest> OvertimeRequests => Set<OvertimeRequest>();
    public DbSet<ShiftHandover> ShiftHandovers => Set<ShiftHandover>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountMapping> AccountMappings => Set<AccountMapping>();
    public DbSet<AccountingPeriod> AccountingPeriods => Set<AccountingPeriod>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(HmsDbContext).Assembly);
        if (Database.IsSqlServer()) Configurations.DecimalCheckConstraints.Apply(builder);

        foreach (var entity in builder.Model.GetEntityTypes())
        {
            // Never cascade-delete business data; financial and audit records are reversed, not removed.
            foreach (var fk in entity.GetForeignKeys().Where(fk => !fk.IsOwnership))
            {
                if (!fk.DeclaringEntityType.ClrType.Namespace!.StartsWith("Microsoft.AspNetCore.Identity", StringComparison.Ordinal))
                    fk.DeleteBehavior = DeleteBehavior.Restrict;
            }

            if (typeof(IConcurrencyTracked).IsAssignableFrom(entity.ClrType))
                builder.Entity(entity.ClrType).Property(nameof(IConcurrencyTracked.ConcurrencyStamp)).IsConcurrencyToken();
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // All event timestamps are UTC. Re-apply the kind on read (SQLite and some drivers drop it).
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        configurationBuilder.Properties<string>().HaveMaxLength(450);
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}
