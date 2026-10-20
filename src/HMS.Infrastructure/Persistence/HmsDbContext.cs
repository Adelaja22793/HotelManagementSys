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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(HmsDbContext).Assembly);

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
