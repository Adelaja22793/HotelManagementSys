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

    DatabaseFacade Database { get; }

    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
