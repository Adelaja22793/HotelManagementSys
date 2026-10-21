using HMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HMS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Database guards on money and quantity columns. Added on SQL Server only: SQLite stores decimals as
/// text, so numeric comparisons in a check constraint would not behave as intended there.
/// </summary>
internal static class DecimalCheckConstraints
{
    public static void Apply(ModelBuilder builder)
    {
        builder.Entity<StockBalance>().ToTable(t => t.HasCheckConstraint("CK_StockBalances_NonNegative", "[Quantity] >= 0"));
        builder.Entity<PurchaseOrderLine>().ToTable(t => t.HasCheckConstraint("CK_PurchaseOrderLines_Received",
            "[ReceivedQuantity] >= 0 AND [ReceivedQuantity] <= [Quantity]"));
        builder.Entity<SupplierInvoice>().ToTable(t => t.HasCheckConstraint("CK_SupplierInvoices_Paid", "[AmountPaid] >= 0 AND [AmountPaid] <= [Total]"));
        builder.Entity<JournalEntryLine>().ToTable(t => t.HasCheckConstraint("CK_JournalEntryLines_OneSide",
            "([Debit] > 0 AND [Credit] = 0) OR ([Credit] > 0 AND [Debit] = 0)"));
    }
}

// ---------- inventory ----------

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.ToTable("Suppliers");
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.ContactName).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(50);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Address).HasMaxLength(500);
        b.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class StockLocationConfiguration : IEntityTypeConfiguration<StockLocation>
{
    public void Configure(EntityTypeBuilder<StockLocation> b)
    {
        b.ToTable("StockLocations");
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> b)
    {
        b.ToTable("InventoryItems");
        b.Property(x => x.Sku).HasMaxLength(40).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Unit).HasMaxLength(20).IsRequired();
        b.Property(x => x.Category).HasMaxLength(100);
        b.Property(x => x.ExpenseAccountKey).HasMaxLength(50).IsRequired();
        b.Property(x => x.ReorderLevel).HasPrecision(18, 3);
        b.HasIndex(x => new { x.BranchId, x.Sku }).IsUnique();
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> b)
    {
        b.ToTable("StockBalances");
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.AverageCost).HasPrecision(18, 4);
        b.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        b.Ignore(x => x.Value);
        b.HasIndex(x => new { x.ItemId, x.LocationId }).IsUnique();
        b.HasIndex(x => x.BranchId);
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId);
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId);
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.ToTable("StockMovements");
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.UnitCost).HasPrecision(18, 4);
        b.Property(x => x.SourceType).HasMaxLength(50).IsRequired();
        b.Property(x => x.SourceId).HasMaxLength(100).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(300);
        b.Property(x => x.CreatedByUserId).HasMaxLength(450).IsRequired();
        // One movement per source, item, location and type: retries can never move stock twice.
        b.HasIndex(x => new { x.BranchId, x.SourceType, x.SourceId, x.ItemId, x.LocationId, x.Type }).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.ItemId, x.BusinessDate });
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId);
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId);
    }
}

// ---------- purchasing ----------

internal sealed class PurchaseRequisitionConfiguration : IEntityTypeConfiguration<PurchaseRequisition>
{
    public void Configure(EntityTypeBuilder<PurchaseRequisition> b)
    {
        b.ToTable("PurchaseRequisitions");
        b.Property(x => x.RequisitionNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.RejectionReason).HasMaxLength(500);
        b.HasIndex(x => new { x.BranchId, x.RequisitionNumber }).IsUnique();
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.RequisitionId);
    }
}

internal sealed class PurchaseRequisitionLineConfiguration : IEntityTypeConfiguration<PurchaseRequisitionLine>
{
    public void Configure(EntityTypeBuilder<PurchaseRequisitionLine> b)
    {
        b.ToTable("PurchaseRequisitionLines");
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.EstimatedUnitCost).HasPrecision(18, 4);
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId);
    }
}

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        b.ToTable("PurchaseOrders");
        b.Property(x => x.OrderNumber).HasMaxLength(30).IsRequired();
        b.Ignore(x => x.Total);
        b.HasIndex(x => new { x.BranchId, x.OrderNumber }).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.Status });
        b.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId);
        b.HasOne<StockLocation>().WithMany().HasForeignKey(x => x.DeliveryLocationId);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PurchaseOrderId);
    }
}

internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> b)
    {
        b.ToTable("PurchaseOrderLines");
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.ReceivedQuantity).HasPrecision(18, 3);
        b.Property(x => x.InvoicedQuantity).HasPrecision(18, 3);
        b.Property(x => x.UnitCost).HasPrecision(18, 4);
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId);
    }
}

internal sealed class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> b)
    {
        b.ToTable("GoodsReceipts");
        b.Property(x => x.ReceiptNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.DeliveryNote).HasMaxLength(200);
        b.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        b.Ignore(x => x.Total);
        b.HasIndex(x => new { x.BranchId, x.ReceiptNumber }).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => x.PurchaseOrderId);
        b.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.GoodsReceiptId);
    }
}

internal sealed class GoodsReceiptLineConfiguration : IEntityTypeConfiguration<GoodsReceiptLine>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptLine> b)
    {
        b.ToTable("GoodsReceiptLines");
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.UnitCost).HasPrecision(18, 4);
        b.HasOne<PurchaseOrderLine>().WithMany().HasForeignKey(x => x.PurchaseOrderLineId);
    }
}

internal sealed class SupplierInvoiceConfiguration : IEntityTypeConfiguration<SupplierInvoice>
{
    public void Configure(EntityTypeBuilder<SupplierInvoice> b)
    {
        b.ToTable("SupplierInvoices");
        b.Property(x => x.InvoiceNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.SupplierReference).HasMaxLength(100).IsRequired();
        b.Property(x => x.DiscrepancyNotes).HasMaxLength(4000);
        b.Ignore(x => x.Outstanding);
        b.HasIndex(x => new { x.BranchId, x.InvoiceNumber }).IsUnique();
        // The same supplier bill cannot be entered twice.
        b.HasIndex(x => new { x.BranchId, x.SupplierId, x.SupplierReference }).IsUnique();
        b.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId);
        b.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.SupplierInvoiceId);
    }
}

internal sealed class SupplierInvoiceLineConfiguration : IEntityTypeConfiguration<SupplierInvoiceLine>
{
    public void Configure(EntityTypeBuilder<SupplierInvoiceLine> b)
    {
        b.ToTable("SupplierInvoiceLines");
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.UnitPrice).HasPrecision(18, 4);
        b.HasOne<PurchaseOrderLine>().WithMany().HasForeignKey(x => x.PurchaseOrderLineId);
    }
}

internal sealed class SupplierPaymentConfiguration : IEntityTypeConfiguration<SupplierPayment>
{
    public void Configure(EntityTypeBuilder<SupplierPayment> b)
    {
        b.ToTable("SupplierPayments");
        b.Property(x => x.PaymentNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(100).IsRequired();
        b.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.BranchId, x.PaymentNumber }).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.IdempotencyKey }).IsUnique();
        b.HasOne<SupplierInvoice>().WithMany().HasForeignKey(x => x.SupplierInvoiceId);
    }
}

// ---------- restaurant ----------

internal sealed class RestaurantOutletConfiguration : IEntityTypeConfiguration<RestaurantOutlet>
{
    public void Configure(EntityTypeBuilder<RestaurantOutlet> b)
    {
        b.ToTable("RestaurantOutlets");
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        b.HasOne<StockLocation>().WithMany().HasForeignKey(x => x.StockLocationId);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class RestaurantTableConfiguration : IEntityTypeConfiguration<RestaurantTable>
{
    public void Configure(EntityTypeBuilder<RestaurantTable> b)
    {
        b.ToTable("RestaurantTables");
        b.Property(x => x.TableNumber).HasMaxLength(20).IsRequired();
        b.HasIndex(x => new { x.OutletId, x.TableNumber }).IsUnique();
        b.HasOne<RestaurantOutlet>().WithMany().HasForeignKey(x => x.OutletId);
    }
}

internal sealed class MenuCategoryConfiguration : IEntityTypeConfiguration<MenuCategory>
{
    public void Configure(EntityTypeBuilder<MenuCategory> b)
    {
        b.ToTable("MenuCategories");
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasOne<RestaurantOutlet>().WithMany().HasForeignKey(x => x.OutletId);
    }
}

internal sealed class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> b)
    {
        b.ToTable("MenuItems");
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Station).HasMaxLength(50).IsRequired();
        b.HasIndex(x => new { x.OutletId, x.Code }).IsUnique();
        b.HasOne<RestaurantOutlet>().WithMany().HasForeignKey(x => x.OutletId);
        b.HasOne<MenuCategory>().WithMany().HasForeignKey(x => x.CategoryId);
        b.HasMany(x => x.Ingredients).WithOne().HasForeignKey(x => x.MenuItemId);
    }
}

internal sealed class MenuItemIngredientConfiguration : IEntityTypeConfiguration<MenuItemIngredient>
{
    public void Configure(EntityTypeBuilder<MenuItemIngredient> b)
    {
        b.ToTable("MenuItemIngredients");
        b.Property(x => x.Quantity).HasPrecision(18, 4);
        b.HasIndex(x => new { x.MenuItemId, x.ItemId }).IsUnique();
        b.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId);
    }
}

internal sealed class RestaurantOrderConfiguration : IEntityTypeConfiguration<RestaurantOrder>
{
    public void Configure(EntityTypeBuilder<RestaurantOrder> b)
    {
        b.ToTable("RestaurantOrders");
        b.Property(x => x.OrderNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.VoidReason).HasMaxLength(300);
        b.Ignore(x => x.ActiveLines);
        b.Ignore(x => x.Subtotal);
        b.HasIndex(x => new { x.BranchId, x.OrderNumber }).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.Status });
        // One open order per table.
        b.HasIndex(x => x.TableId).IsUnique().HasFilter("[TableId] IS NOT NULL AND [Status] = 0").HasDatabaseName("IX_RestaurantOrders_OpenTable");
        b.HasOne(x => x.Outlet).WithMany().HasForeignKey(x => x.OutletId);
        b.HasOne<RestaurantTable>().WithMany().HasForeignKey(x => x.TableId);
        b.HasOne<Stay>().WithMany().HasForeignKey(x => x.StayId);
        b.HasOne<Folio>().WithMany().HasForeignKey(x => x.FolioId);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.OrderId);
    }
}

internal sealed class RestaurantOrderLineConfiguration : IEntityTypeConfiguration<RestaurantOrderLine>
{
    public void Configure(EntityTypeBuilder<RestaurantOrderLine> b)
    {
        b.ToTable("RestaurantOrderLines");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Station).HasMaxLength(50).IsRequired();
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.Notes).HasMaxLength(300);
        b.Property(x => x.VoidReason).HasMaxLength(300);
        b.HasOne<MenuItem>().WithMany().HasForeignKey(x => x.MenuItemId);
    }
}

internal sealed class KitchenTicketConfiguration : IEntityTypeConfiguration<KitchenTicket>
{
    public void Configure(EntityTypeBuilder<KitchenTicket> b)
    {
        b.ToTable("KitchenTickets");
        b.Property(x => x.TicketNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Station).HasMaxLength(50).IsRequired();
        b.HasIndex(x => new { x.BranchId, x.TicketNumber }).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.Station, x.Status });
        b.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.TicketId);
    }
}

internal sealed class KitchenTicketLineConfiguration : IEntityTypeConfiguration<KitchenTicketLine>
{
    public void Configure(EntityTypeBuilder<KitchenTicketLine> b)
    {
        b.ToTable("KitchenTicketLines");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.Notes).HasMaxLength(300);
        b.HasOne<RestaurantOrderLine>().WithMany().HasForeignKey(x => x.OrderLineId);
    }
}

// ---------- maintenance ----------

internal sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> b)
    {
        b.ToTable("Assets");
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Category).HasMaxLength(100);
        b.Property(x => x.Location).HasMaxLength(200);
        b.Property(x => x.SerialNumber).HasMaxLength(100);
        b.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        b.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class MaintenanceRequestConfiguration : IEntityTypeConfiguration<MaintenanceRequest>
{
    public void Configure(EntityTypeBuilder<MaintenanceRequest> b)
    {
        b.ToTable("MaintenanceRequests");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.RejectionReason).HasMaxLength(500);
        b.HasIndex(x => new { x.BranchId, x.Status });
        b.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId);
        b.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId);
    }
}

internal sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> b)
    {
        b.ToTable("WorkOrders");
        b.Property(x => x.WorkOrderNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.CompletionNotes).HasMaxLength(2000);
        b.Property(x => x.LabourHours).HasPrecision(10, 2);
        b.Ignore(x => x.IsOpen);
        b.HasIndex(x => new { x.BranchId, x.WorkOrderNumber }).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.Status });
        b.HasIndex(x => x.AssetId);
        b.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId);
        b.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId);
        b.HasOne<MaintenanceRequest>().WithMany().HasForeignKey(x => x.RequestId);
        b.HasOne<RoomBlock>().WithMany().HasForeignKey(x => x.RoomBlockId);
        b.HasOne<PreventiveMaintenancePlan>().WithMany().HasForeignKey(x => x.PreventivePlanId);
    }
}

internal sealed class WorkOrderPartUsageConfiguration : IEntityTypeConfiguration<WorkOrderPartUsage>
{
    public void Configure(EntityTypeBuilder<WorkOrderPartUsage> b)
    {
        b.ToTable("WorkOrderParts");
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Property(x => x.UnitCost).HasPrecision(18, 4);
        b.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.WorkOrderId, x.IdempotencyKey }).IsUnique();
        b.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId);
        b.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId);
        b.HasOne<StockLocation>().WithMany().HasForeignKey(x => x.LocationId);
    }
}

internal sealed class PreventivePlanConfiguration : IEntityTypeConfiguration<PreventiveMaintenancePlan>
{
    public void Configure(EntityTypeBuilder<PreventiveMaintenancePlan> b)
    {
        b.ToTable("PreventiveMaintenancePlans");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.BranchId, x.IsActive, x.NextDueDate });
        b.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId);
        b.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId);
    }
}

internal sealed class PreventiveOccurrenceConfiguration : IEntityTypeConfiguration<PreventiveMaintenanceOccurrence>
{
    public void Configure(EntityTypeBuilder<PreventiveMaintenanceOccurrence> b)
    {
        b.ToTable("PreventiveMaintenanceOccurrences");
        b.HasIndex(x => new { x.BranchId, x.PlanId, x.DueDate }).IsUnique();
        b.HasOne<PreventiveMaintenancePlan>().WithMany().HasForeignKey(x => x.PlanId);
        b.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId);
    }
}

// ---------- staff ----------

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> b)
    {
        b.ToTable("Departments");
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> b)
    {
        b.ToTable("Employees");
        b.Property(x => x.EmployeeNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        b.Property(x => x.JobTitle).HasMaxLength(100);
        b.Property(x => x.Phone).HasMaxLength(50);
        b.Property(x => x.Email).HasMaxLength(256);
        b.Ignore(x => x.FullName);
        b.HasIndex(x => new { x.BranchId, x.EmployeeNumber }).IsUnique();
        b.HasIndex(x => x.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL");
        b.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId);
    }
}

internal sealed class WorkShiftConfiguration : IEntityTypeConfiguration<WorkShift>
{
    public void Configure(EntityTypeBuilder<WorkShift> b)
    {
        b.ToTable("WorkShifts");
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Ignore(x => x.IsOvernight);
        b.Ignore(x => x.PaidHours);
        b.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
    }
}

internal sealed class RosterEntryConfiguration : IEntityTypeConfiguration<RosterEntry>
{
    public void Configure(EntityTypeBuilder<RosterEntry> b)
    {
        b.ToTable("RosterEntries");
        b.Property(x => x.Notes).HasMaxLength(300);
        b.HasIndex(x => new { x.EmployeeId, x.WorkDate, x.ShiftId }).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.WorkDate });
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId);
        b.HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId);
    }
}

internal sealed class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> b)
    {
        b.ToTable("AttendanceRecords");
        b.Property(x => x.Notes).HasMaxLength(300);
        b.Ignore(x => x.IsOpen);
        b.Ignore(x => x.Hours);
        // Only one open clock-in per employee.
        b.HasIndex(x => x.EmployeeId).IsUnique().HasFilter("[ClockOutUtc] IS NULL").HasDatabaseName("IX_AttendanceRecords_OpenSession");
        b.HasIndex(x => new { x.BranchId, x.WorkDate });
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId);
    }
}

internal sealed class LeaveRecordConfiguration : IEntityTypeConfiguration<LeaveRecord>
{
    public void Configure(EntityTypeBuilder<LeaveRecord> b)
    {
        b.ToTable("LeaveRecords");
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasIndex(x => new { x.EmployeeId, x.StartDate });
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId);
    }
}

internal sealed class OvertimeRequestConfiguration : IEntityTypeConfiguration<OvertimeRequest>
{
    public void Configure(EntityTypeBuilder<OvertimeRequest> b)
    {
        b.ToTable("OvertimeRequests");
        b.Property(x => x.Reason).HasMaxLength(300).IsRequired();
        b.Property(x => x.Hours).HasPrecision(6, 2);
        b.HasIndex(x => new { x.EmployeeId, x.WorkDate });
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId);
    }
}

internal sealed class ShiftHandoverConfiguration : IEntityTypeConfiguration<ShiftHandover>
{
    public void Configure(EntityTypeBuilder<ShiftHandover> b)
    {
        b.ToTable("ShiftHandovers");
        b.Property(x => x.Notes).HasMaxLength(4000).IsRequired();
        b.Property(x => x.OpenIssues).HasMaxLength(4000);
        b.HasIndex(x => new { x.BranchId, x.DepartmentId, x.BusinessDate });
        b.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId);
    }
}

// ---------- accounting ----------

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> b)
    {
        b.ToTable("Accounts");
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Ignore(x => x.IsDebitNormal);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId);
    }
}

internal sealed class AccountMappingConfiguration : IEntityTypeConfiguration<AccountMapping>
{
    public void Configure(EntityTypeBuilder<AccountMapping> b)
    {
        b.ToTable("AccountMappings");
        b.Property(x => x.Key).HasMaxLength(50).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.Key }).IsUnique();
        b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId);
    }
}

internal sealed class AccountingPeriodConfiguration : IEntityTypeConfiguration<AccountingPeriod>
{
    public void Configure(EntityTypeBuilder<AccountingPeriod> b)
    {
        b.ToTable("AccountingPeriods");
        b.HasIndex(x => new { x.CompanyId, x.Year, x.Month }).IsUnique();
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId);
    }
}

internal sealed class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> b)
    {
        b.ToTable("JournalEntries");
        b.Property(x => x.EntryNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Description).HasMaxLength(300).IsRequired();
        b.Property(x => x.SourceType).HasMaxLength(50);
        b.Property(x => x.SourceId).HasMaxLength(100);
        b.Property(x => x.PostedByUserId).HasMaxLength(450).IsRequired();
        b.HasIndex(x => new { x.BranchId, x.EntryNumber }).IsUnique();
        // Source idempotency: an operational event can be posted at most once.
        b.HasIndex(x => new { x.BranchId, x.SourceType, x.SourceId }).IsUnique().HasFilter("[SourceType] IS NOT NULL");
        b.HasIndex(x => new { x.CompanyId, x.EntryDate });
        b.HasOne<AccountingPeriod>().WithMany().HasForeignKey(x => x.PeriodId);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.JournalEntryId);
    }
}

internal sealed class JournalEntryLineConfiguration : IEntityTypeConfiguration<JournalEntryLine>
{
    public void Configure(EntityTypeBuilder<JournalEntryLine> b)
    {
        b.ToTable("JournalEntryLines");
        b.Property(x => x.Memo).HasMaxLength(300);
        b.HasIndex(x => x.AccountId);
        b.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId);
    }
}
