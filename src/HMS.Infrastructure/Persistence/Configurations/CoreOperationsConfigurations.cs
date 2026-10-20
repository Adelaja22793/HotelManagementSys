using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HMS.Infrastructure.Persistence.Configurations;

internal sealed class BranchSequenceConfiguration : IEntityTypeConfiguration<BranchSequence>
{
    public void Configure(EntityTypeBuilder<BranchSequence> b)
    {
        b.ToTable("BranchSequences");
        b.HasKey(x => new { x.BranchId, x.Name });
        b.Property(x => x.Name).HasMaxLength(10);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class RoomTypeConfiguration : IEntityTypeConfiguration<RoomType>
{
    public void Configure(EntityTypeBuilder<RoomType> b)
    {
        b.ToTable("RoomTypes");
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Ignore(x => x.MaxOccupancy);
        b.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> b)
    {
        b.ToTable("Rooms");
        b.Property(x => x.RoomNumber).HasMaxLength(20).IsRequired();
        b.Property(x => x.Floor).HasMaxLength(20);
        b.Ignore(x => x.IsOccupied);
        b.HasIndex(x => new { x.BranchId, x.RoomNumber }).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.RoomTypeId });
        b.HasOne(x => x.RoomType).WithMany().HasForeignKey(x => x.RoomTypeId);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class RoomBlockConfiguration : IEntityTypeConfiguration<RoomBlock>
{
    public void Configure(EntityTypeBuilder<RoomBlock> b)
    {
        b.ToTable("RoomBlocks");
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.HasIndex(x => new { x.BranchId, x.RoomId, x.StartDate });
        b.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId);
    }
}

internal sealed class RatePlanConfiguration : IEntityTypeConfiguration<RatePlan>
{
    public void Configure(EntityTypeBuilder<RatePlan> b)
    {
        b.ToTable("RatePlans");
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.CancellationPolicy).HasMaxLength(1000).IsRequired();
        b.Property(x => x.DepositPercent).HasPrecision(5, 2);
        b.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
    }
}

internal sealed class RoomRateConfiguration : IEntityTypeConfiguration<RoomRate>
{
    public void Configure(EntityTypeBuilder<RoomRate> b)
    {
        b.ToTable("RoomRates");
        b.HasIndex(x => new { x.BranchId, x.RatePlanId, x.RoomTypeId, x.StartDate });
        b.HasOne<RatePlan>().WithMany().HasForeignKey(x => x.RatePlanId);
        b.HasOne<RoomType>().WithMany().HasForeignKey(x => x.RoomTypeId);
    }
}

internal sealed class TaxRuleConfiguration : IEntityTypeConfiguration<TaxRule>
{
    public void Configure(EntityTypeBuilder<TaxRule> b)
    {
        b.ToTable("TaxRules");
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.RatePercent).HasPrecision(7, 4);
        b.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
    }
}

internal sealed class ServiceItemConfiguration : IEntityTypeConfiguration<ServiceItem>
{
    public void Configure(EntityTypeBuilder<ServiceItem> b)
    {
        b.ToTable("ServiceItems");
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
    }
}

internal sealed class GuestConfiguration : IEntityTypeConfiguration<Guest>
{
    public void Configure(EntityTypeBuilder<Guest> b)
    {
        b.ToTable("Guests");
        b.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        b.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        b.Property(x => x.Email).HasMaxLength(256);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Nationality).HasMaxLength(60);
        b.Property(x => x.IdDocumentType).HasMaxLength(40);
        b.Property(x => x.IdDocumentNumber).HasMaxLength(60);
        b.Property(x => x.Address).HasMaxLength(400);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Ignore(x => x.FullName);
        b.HasIndex(x => new { x.BranchId, x.LastName, x.FirstName });
        b.HasIndex(x => new { x.BranchId, x.Email });
        b.HasIndex(x => new { x.BranchId, x.Phone });
    }
}

internal sealed class RoomNightInventoryConfiguration : IEntityTypeConfiguration<RoomNightInventory>
{
    public void Configure(EntityTypeBuilder<RoomNightInventory> b)
    {
        b.ToTable("RoomNightInventory", t =>
        {
            t.HasCheckConstraint("CK_RoomNightInventory_NonNegative",
                "[PhysicalCount] >= 0 AND [BlockedCount] >= 0 AND [ConfirmedCount] >= 0 AND [HeldCount] >= 0");
        });
        b.Ignore(x => x.Sellable);
        b.Ignore(x => x.Available);
        b.HasIndex(x => new { x.BranchId, x.RoomTypeId, x.Date }).IsUnique();
        b.HasOne<RoomType>().WithMany().HasForeignKey(x => x.RoomTypeId);
    }
}

internal sealed class AssignedRoomNightConfiguration : IEntityTypeConfiguration<AssignedRoomNight>
{
    public void Configure(EntityTypeBuilder<AssignedRoomNight> b)
    {
        b.ToTable("AssignedRoomNights");
        b.HasIndex(x => new { x.RoomId, x.Date }).IsUnique();
        b.HasIndex(x => x.ReservationId);
        b.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId);
        b.HasOne<Reservation>().WithMany().HasForeignKey(x => x.ReservationId);
    }
}

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> b)
    {
        b.ToTable("Reservations");
        b.Property(x => x.ReservationNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.IdempotencyKey).HasMaxLength(100);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.RatePlanName).HasMaxLength(200);
        b.Property(x => x.CancellationPolicy).HasMaxLength(1000);
        b.Property(x => x.CancellationReason).HasMaxLength(500);
        b.Ignore(x => x.Dates);
        b.Ignore(x => x.NightCount);
        b.HasIndex(x => new { x.BranchId, x.ReservationNumber }).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.IdempotencyKey }).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
        b.HasIndex(x => new { x.BranchId, x.ArrivalDate, x.Status });
        b.HasIndex(x => new { x.BranchId, x.DepartureDate, x.Status });
        b.HasIndex(x => x.GuestId);
        b.HasOne(x => x.Guest).WithMany().HasForeignKey(x => x.GuestId);
        b.HasOne(x => x.RoomType).WithMany().HasForeignKey(x => x.RoomTypeId);
        b.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId);
        b.HasOne<RatePlan>().WithMany().HasForeignKey(x => x.RatePlanId);
        b.HasMany(x => x.Nights).WithOne().HasForeignKey(x => x.ReservationId);
        b.Navigation(x => x.Nights).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ReservationNightConfiguration : IEntityTypeConfiguration<ReservationNight>
{
    public void Configure(EntityTypeBuilder<ReservationNight> b)
    {
        b.ToTable("ReservationNights");
        b.Ignore(x => x.Net);
        b.HasIndex(x => new { x.ReservationId, x.Date }).IsUnique();
    }
}

internal sealed class StayConfiguration : IEntityTypeConfiguration<Stay>
{
    public void Configure(EntityTypeBuilder<Stay> b)
    {
        b.ToTable("Stays");
        b.Property(x => x.BalanceOverrideReason).HasMaxLength(500);
        b.HasIndex(x => x.ReservationId).IsUnique();
        // At most one active stay per room.
        b.HasIndex(x => new { x.BranchId, x.RoomId })
            .IsUnique()
            .HasFilter($"[Status] = {(int)StayStatus.Active}")
            .HasDatabaseName("IX_Stays_ActiveRoom");
        b.HasIndex(x => new { x.BranchId, x.Status });
        b.HasOne(x => x.Reservation).WithMany().HasForeignKey(x => x.ReservationId);
        b.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId);
        b.HasOne<Guest>().WithMany().HasForeignKey(x => x.GuestId);
    }
}

internal sealed class HousekeepingTaskConfiguration : IEntityTypeConfiguration<HousekeepingTask>
{
    public void Configure(EntityTypeBuilder<HousekeepingTask> b)
    {
        b.ToTable("HousekeepingTasks");
        b.Property(x => x.SourceType).HasMaxLength(30).IsRequired();
        b.Property(x => x.SourceId).HasMaxLength(100).IsRequired();
        b.Property(x => x.RejectionReason).HasMaxLength(500);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Ignore(x => x.IsOpen);
        b.HasIndex(x => new { x.BranchId, x.SourceType, x.SourceId }).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.Status, x.BusinessDate });
        b.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId);
    }
}

internal sealed class FolioConfiguration : IEntityTypeConfiguration<Folio>
{
    public void Configure(EntityTypeBuilder<Folio> b)
    {
        b.ToTable("Folios");
        b.Property(x => x.FolioNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.HasIndex(x => new { x.BranchId, x.FolioNumber }).IsUnique();
        b.HasIndex(x => x.ReservationId).IsUnique();
        b.HasOne(x => x.Reservation).WithMany().HasForeignKey(x => x.ReservationId);
        b.HasOne(x => x.Guest).WithMany().HasForeignKey(x => x.GuestId);
        b.HasOne<Stay>().WithMany().HasForeignKey(x => x.StayId);
    }
}

internal sealed class FolioChargeConfiguration : IEntityTypeConfiguration<FolioCharge>
{
    public void Configure(EntityTypeBuilder<FolioCharge> b)
    {
        b.ToTable("FolioCharges");
        b.Property(x => x.Description).HasMaxLength(300).IsRequired();
        b.Property(x => x.IdempotencyKey).HasMaxLength(120);
        b.Property(x => x.Quantity).HasPrecision(18, 3);
        b.Ignore(x => x.IsReversed);
        b.HasIndex(x => new { x.FolioId, x.IdempotencyKey }).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
        b.HasIndex(x => new { x.BranchId, x.BusinessDate });
        b.HasOne<Folio>().WithMany().HasForeignKey(x => x.FolioId);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("Payments");
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        b.Property(x => x.ExternalReference).HasMaxLength(120);
        b.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.FailureReason).HasMaxLength(500);
        b.HasIndex(x => new { x.BranchId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => x.FolioId);
        b.HasIndex(x => x.CashierShiftId);
        b.HasOne<Folio>().WithMany().HasForeignKey(x => x.FolioId);
        b.HasOne<CashierShift>().WithMany().HasForeignKey(x => x.CashierShiftId);
    }
}

internal sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> b)
    {
        b.ToTable("Receipts");
        b.Property(x => x.ReceiptNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.SnapshotJson).HasMaxLength(8000).IsRequired();
        b.HasIndex(x => new { x.BranchId, x.ReceiptNumber }).IsUnique();
        b.HasIndex(x => x.PaymentId).IsUnique();
        b.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId);
    }
}

internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> b)
    {
        b.ToTable("Refunds");
        b.Property(x => x.RefundNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.Property(x => x.RejectionReason).HasMaxLength(500);
        b.Property(x => x.ExternalReference).HasMaxLength(120);
        b.HasIndex(x => new { x.BranchId, x.RefundNumber }).IsUnique();
        b.HasIndex(x => x.FolioId);
        b.HasOne<Folio>().WithMany().HasForeignKey(x => x.FolioId);
        b.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId);
        b.HasOne<CashierShift>().WithMany().HasForeignKey(x => x.CashierShiftId);
    }
}

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ToTable("Invoices");
        b.Property(x => x.InvoiceNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.SnapshotJson).IsRequired().HasMaxLength(int.MaxValue);
        b.HasIndex(x => new { x.BranchId, x.InvoiceNumber }).IsUnique();
        b.HasIndex(x => new { x.FolioId, x.Version }).IsUnique();
        b.HasOne<Folio>().WithMany().HasForeignKey(x => x.FolioId);
    }
}

internal sealed class CashierShiftConfiguration : IEntityTypeConfiguration<CashierShift>
{
    public void Configure(EntityTypeBuilder<CashierShift> b)
    {
        b.ToTable("CashierShifts");
        b.Property(x => x.VarianceExplanation).HasMaxLength(1000);
        b.Ignore(x => x.HasVariance);
        // One open (or closing) shift per cashier per branch.
        b.HasIndex(x => new { x.BranchId, x.CashierUserId })
            .IsUnique()
            .HasFilter($"[Status] <> {(int)CashierShiftStatus.Closed}")
            .HasDatabaseName("IX_CashierShifts_OpenPerCashier");
        b.HasIndex(x => new { x.BranchId, x.Status });
        b.HasIndex(x => new { x.BranchId, x.CashierUserId, x.OpenedAtUtc });
    }
}

internal sealed class CashierMovementConfiguration : IEntityTypeConfiguration<CashierMovement>
{
    public void Configure(EntityTypeBuilder<CashierMovement> b)
    {
        b.ToTable("CashierMovements");
        b.HasIndex(x => x.ShiftId);
        b.HasIndex(x => x.PaymentId).IsUnique().HasFilter("[PaymentId] IS NOT NULL");
        b.HasIndex(x => x.RefundId).IsUnique().HasFilter("[RefundId] IS NOT NULL");
        b.HasOne<CashierShift>().WithMany().HasForeignKey(x => x.ShiftId);
    }
}
