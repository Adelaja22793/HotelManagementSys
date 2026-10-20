using HMS.Application.Abstractions;
using HMS.Application.Billing;
using HMS.Application.Common;
using HMS.Application.Guests;
using HMS.Application.Rates;
using HMS.Application.Rooms;
using HMS.Application.Security;
using HMS.Contracts.Common;
using HMS.Contracts.Guests;
using HMS.Contracts.Reservations;
using HMS.Domain.Common;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Reservations;

/// <summary>Blueprint's IBookingInventoryService: the single path for creating and changing bookings.</summary>
public interface IReservationService
{
    Task<IReadOnlyList<RoomTypeAvailabilityDto>> GetAvailabilityAsync(Guid branchId, AvailabilityQuery query, CancellationToken ct = default);
    Task<ReservationDto> CreateAsync(Guid branchId, CreateReservationRequest request, string? idempotencyKey, CancellationToken ct = default);
    Task<ReservationDto> GetAsync(Guid branchId, Guid reservationId, CancellationToken ct = default);
    Task<PagedResult<ReservationListItemDto>> SearchAsync(Guid branchId, ReservationSearchQuery query, CancellationToken ct = default);
    Task<ReservationDto> ModifyAsync(Guid branchId, Guid reservationId, ModifyReservationRequest request, CancellationToken ct = default);
    Task<ReservationDto> CancelAsync(Guid branchId, Guid reservationId, CancelReservationRequest request, CancellationToken ct = default);
    Task<ReservationDto> MarkNoShowAsync(Guid branchId, Guid reservationId, CancelReservationRequest request, CancellationToken ct = default);
    Task<ReservationDto> AssignRoomAsync(Guid branchId, Guid reservationId, AssignRoomRequest request, CancellationToken ct = default);
    Task<ReservationDto> UnassignRoomAsync(Guid branchId, Guid reservationId, Guid concurrencyStamp, CancellationToken ct = default);

    /// <summary>Reserves the physical room for every night from <paramref name="from"/> (caller owns the transaction).</summary>
    Task AssignRoomNightsAsync(BranchScope scope, Reservation reservation, Room room, DateOnly from, CancellationToken ct);
}

internal sealed class ReservationService(
    IApplicationDbContext db,
    IBranchAccessService access,
    ICurrentUser user,
    IClock clock,
    IPricingService pricing,
    IRoomInventoryService inventory,
    IGuestService guests,
    IFolioService folios,
    INumberSequence numbers) : IReservationService
{
    public async Task<IReadOnlyList<RoomTypeAvailabilityDto>> GetAvailabilityAsync(Guid branchId, AvailabilityQuery query, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var dates = new StayDates(query.ArrivalDate, query.DepartureDate);
        var types = await db.RoomTypes.AsNoTracking()
            .Where(t => t.BranchId == branchId && t.IsActive && t.MaxAdults >= query.Adults && t.MaxChildren >= query.Children)
            .OrderBy(t => t.BaseRate).ToListAsync(ct);
        var days = await inventory.GetAsync(scope.Branch, dates.Arrival, dates.Departure, ct);

        var result = new List<RoomTypeAvailabilityDto>();
        foreach (var type in types)
        {
            var available = days.Where(d => d.RoomTypeId == type.Id).Select(d => d.Available).DefaultIfEmpty(0).Min();
            Contracts.Rates.QuoteDto? quote = null;
            if (available > 0)
            {
                try { quote = pricing.ToDto(await pricing.PriceAsync(scope.Branch, type.Id, null, dates, query.Adults, query.Children, 0, ct)); }
                catch (ConflictException) { /* no rate plan configured yet */ }
            }
            result.Add(new RoomTypeAvailabilityDto(type.Id, type.Code, type.Name, type.MaxAdults, type.MaxChildren, Math.Max(0, available), quote));
        }
        return result;
    }

    public async Task<ReservationDto> CreateAsync(Guid branchId, CreateReservationRequest request, string? idempotencyKey, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        if (idempotencyKey is { Length: > 100 }) throw new ValidationAppException("Idempotency-Key", "Must be at most 100 characters.");

        if (idempotencyKey is not null && await FindByKeyAsync(branchId, idempotencyKey, ct) is { } replay)
            return await GetAsync(branchId, replay.Id, ct);

        var source = EnumParser.Parse<ReservationSource>(request.Source, nameof(request.Source));
        var dates = new StayDates(request.ArrivalDate, request.DepartureDate);
        if (dates.Arrival < scope.BusinessDate)
            throw new ValidationAppException(nameof(request.ArrivalDate), "Arrival cannot be before the current business date.");
        if (request.AllowOverbooking && !RolePermissions.HasPermission(user.Roles, Permissions.OverbookingOverride))
            throw new ForbiddenException(message: "Overbooking requires manager permission.");
        if (request.GuestId is null && request.NewGuest is null)
            throw new ValidationAppException(nameof(request.GuestId), "Select an existing guest or enter new guest details.");

        Guest? guest = null;
        if (request.GuestId is { } guestId)
            guest = await db.Guests.GetOwnedAsync(branchId, guestId, "Guest", ct);

        var priced = await pricing.PriceAsync(scope.Branch, request.RoomTypeId, request.RatePlanId, dates, request.Adults, request.Children,
            request.DiscountPercent, ct);
        await inventory.EnsureRowsAsync(scope.Branch, priced.RoomType.Id, dates.Arrival, dates.Departure, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var overbooked = await inventory.AllocateAsync(scope.Branch, priced.RoomType.Id, dates.Arrival, dates.Departure, 1, request.AllowOverbooking, ct);

        guest ??= await guests.FindOrAddAsync(scope, request.NewGuest!, ct);
        var number = await numbers.NextAsync(scope.Branch, SequenceNames.Reservation, ct);
        var reservation = new Reservation(scope.CompanyId, branchId, number, guest.Id, priced.RoomType.Id, priced.RatePlan.Id, dates,
            request.Adults, request.Children, source, idempotencyKey) { Notes = Clean(request.Notes) };
        reservation.ApplyPrice(priced.Quote, priced.RatePlan);
        if (overbooked) reservation.MarkOverbooked();
        db.Reservations.Add(reservation);
        await folios.CreateForReservationAsync(scope, reservation, ct);

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException && idempotencyKey is not null)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var winner = await FindByKeyAsync(branchId, idempotencyKey, ct);
            if (winner is null) throw;
            return await GetAsync(branchId, winner.Id, ct);
        }
        return await GetAsync(branchId, reservation.Id, ct);
    }

    public async Task<ReservationDto> GetAsync(Guid branchId, Guid reservationId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var r = await db.Reservations.AsNoTracking()
            .Include(x => x.Guest).Include(x => x.RoomType).Include(x => x.Room).Include(x => x.Nights)
            .GetOwnedAsync(branchId, reservationId, "Reservation", ct);
        var folioId = await db.Folios.Where(f => f.ReservationId == r.Id).Select(f => (Guid?)f.Id).SingleOrDefaultAsync(ct);
        return ToDto(r, folioId);
    }

    public async Task<PagedResult<ReservationListItemDto>> SearchAsync(Guid branchId, ReservationSearchQuery query, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var page = new PageQuery { Page = query.Page, PageSize = query.PageSize };
        var q = db.Reservations.AsNoTracking().Where(r => r.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = EnumParser.Parse<ReservationStatus>(query.Status, nameof(query.Status));
            q = q.Where(r => r.Status == status);
        }
        if (query.ArrivalFrom is { } from) q = q.Where(r => r.ArrivalDate >= from);
        if (query.ArrivalTo is { } to) q = q.Where(r => r.ArrivalDate <= to);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLower();
            q = q.Where(r => r.ReservationNumber.ToLower().Contains(s) || r.Guest!.LastName.ToLower().Contains(s)
                             || r.Guest.FirstName.ToLower().Contains(s) || (r.Guest.Phone != null && r.Guest.Phone.Contains(s))
                             || (r.Guest.Email != null && r.Guest.Email.Contains(s)));
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(r => r.ArrivalDate).ThenBy(r => r.ReservationNumber).Skip(page.Skip).Take(page.PageSize)
            .Select(r => new
            {
                r.Id, r.ReservationNumber, r.Status, r.Guest!.FirstName, r.Guest.LastName, TypeCode = r.RoomType!.Code,
                RoomNumber = r.Room != null ? r.Room.RoomNumber : null, r.ArrivalDate, r.DepartureDate, r.TotalAmount,
            })
            .ToListAsync(ct);
        return new PagedResult<ReservationListItemDto>(items.Select(r => new ReservationListItemDto(r.Id, r.ReservationNumber, r.Status.ToString(),
            $"{r.FirstName} {r.LastName}", r.TypeCode, r.RoomNumber, r.ArrivalDate, r.DepartureDate, r.TotalAmount)).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<ReservationDto> ModifyAsync(Guid branchId, Guid reservationId, ModifyReservationRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var reservation = await db.Reservations.Include(r => r.Nights).GetOwnedAsync(branchId, reservationId, "Reservation", ct);
        BranchOperations.EnsureStamp(reservation, request.ConcurrencyStamp, "reservation");
        if (reservation.Status != ReservationStatus.Confirmed)
            throw new ConflictException("RESERVATION_STATE", "Only confirmed reservations can be changed.");
        if (request.AllowOverbooking && !RolePermissions.HasPermission(user.Roles, Permissions.OverbookingOverride))
            throw new ForbiddenException(message: "Overbooking requires manager permission.");

        var dates = new StayDates(request.ArrivalDate, request.DepartureDate);
        if (dates.Arrival < scope.BusinessDate)
            throw new ValidationAppException(nameof(request.ArrivalDate), "Arrival cannot be before the current business date.");
        var folioId = await db.Folios.Where(f => f.ReservationId == reservation.Id).Select(f => f.Id).SingleAsync(ct);
        if (request.DiscountPercent > 0 && await db.Payments.AnyAsync(p => p.FolioId == folioId && p.Status == PaymentStatus.Confirmed, ct))
            throw new ConflictException("DISCOUNT_AFTER_PAYMENT", "Discounts cannot be added after a payment was received. Use a folio adjustment.");

        var priced = await pricing.PriceAsync(scope.Branch, request.RoomTypeId, reservation.RatePlanId, dates, request.Adults, request.Children,
            request.DiscountPercent, ct);
        await inventory.EnsureRowsAsync(scope.Branch, priced.RoomType.Id, dates.Arrival, dates.Departure, ct);
        var plan = await db.RatePlans.AsNoTracking().SingleAsync(p => p.Id == reservation.RatePlanId, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await inventory.ReleaseAsync(branchId, reservation.RoomTypeId, reservation.ArrivalDate, reservation.DepartureDate, 1, ct);
        var overbooked = await inventory.AllocateAsync(scope.Branch, priced.RoomType.Id, dates.Arrival, dates.Departure, 1, request.AllowOverbooking, ct);
        await db.AssignedRoomNights.Where(a => a.ReservationId == reservation.Id).ExecuteDeleteAsync(ct);

        db.ReservationNights.RemoveRange(reservation.Nights);
        reservation.ChangeStay(priced.RoomType.Id, dates, request.Adults, request.Children);
        reservation.ApplyPrice(priced.Quote, plan);
        if (overbooked) reservation.MarkOverbooked();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(branchId, reservation.Id, ct);
    }

    public async Task<ReservationDto> CancelAsync(Guid branchId, Guid reservationId, CancelReservationRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var reservation = await db.Reservations.GetOwnedAsync(branchId, reservationId, "Reservation", ct);
        BranchOperations.EnsureStamp(reservation, request.ConcurrencyStamp, "reservation");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        reservation.Cancel(request.Reason, scope.UtcNow);
        await ReleaseAllAsync(reservation, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(branchId, reservation.Id, ct);
    }

    public async Task<ReservationDto> MarkNoShowAsync(Guid branchId, Guid reservationId, CancelReservationRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var reservation = await db.Reservations.GetOwnedAsync(branchId, reservationId, "Reservation", ct);
        BranchOperations.EnsureStamp(reservation, request.ConcurrencyStamp, "reservation");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        reservation.MarkNoShow(scope.BusinessDate);
        reservation.Notes = Clean($"{reservation.Notes}\nNo-show: {request.Reason}".Trim());
        await ReleaseAllAsync(reservation, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(branchId, reservation.Id, ct);
    }

    public async Task<ReservationDto> AssignRoomAsync(Guid branchId, Guid reservationId, AssignRoomRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var reservation = await db.Reservations.GetOwnedAsync(branchId, reservationId, "Reservation", ct);
        BranchOperations.EnsureStamp(reservation, request.ConcurrencyStamp, "reservation");
        if (reservation.Status != ReservationStatus.Confirmed)
            throw new ConflictException("RESERVATION_STATE", "Rooms can be assigned only before check-in. Room moves come in a later release.");
        var room = await db.Rooms.GetOwnedAsync(branchId, request.RoomId, "Room", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AssignRoomNightsAsync(scope, reservation, room, reservation.ArrivalDate, ct);
        await SaveAssignmentAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(branchId, reservation.Id, ct);
    }

    public async Task<ReservationDto> UnassignRoomAsync(Guid branchId, Guid reservationId, Guid concurrencyStamp, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var reservation = await db.Reservations.GetOwnedAsync(branchId, reservationId, "Reservation", ct);
        BranchOperations.EnsureStamp(reservation, concurrencyStamp, "reservation");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        reservation.UnassignRoom();
        await db.AssignedRoomNights.Where(a => a.ReservationId == reservation.Id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(branchId, reservation.Id, ct);
    }

    public async Task AssignRoomNightsAsync(BranchScope scope, Reservation reservation, Room room, DateOnly from, CancellationToken ct)
    {
        db.RequireTransaction();
        if (!room.IsActive) throw new ConflictException("ROOM_INACTIVE", $"Room {room.RoomNumber} is not in service.");
        if (room.RoomTypeId != reservation.RoomTypeId)
            throw new ConflictException("ROOM_TYPE_MISMATCH", $"Room {room.RoomNumber} is not a {reservation.RoomType?.Code ?? "matching"} room. Change the reservation's room type first.");

        var start = from > reservation.ArrivalDate ? from : reservation.ArrivalDate;
        if (await db.RoomBlocks.AnyAsync(b => b.RoomId == room.Id && b.IsActive && b.StartDate < reservation.DepartureDate && b.EndDate > start, ct))
            throw new ConflictException("ROOM_BLOCKED", $"Room {room.RoomNumber} is out of service during this stay.");

        await db.AssignedRoomNights.Where(a => a.ReservationId == reservation.Id).ExecuteDeleteAsync(ct);
        for (var night = start; night < reservation.DepartureDate; night = night.AddDays(1))
            db.AssignedRoomNights.Add(new AssignedRoomNight { BranchId = scope.BranchId, RoomId = room.Id, Date = night, ReservationId = reservation.Id });
        reservation.AssignRoom(room.Id);
    }

    private async Task SaveAssignmentAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            throw new ConflictException("ROOM_ALREADY_ASSIGNED", "That room is already assigned to another guest for at least one of these nights.");
        }
    }

    private async Task ReleaseAllAsync(Reservation reservation, CancellationToken ct)
    {
        await inventory.ReleaseAsync(reservation.BranchId, reservation.RoomTypeId, reservation.ArrivalDate, reservation.DepartureDate, 1, ct);
        await db.AssignedRoomNights.Where(a => a.ReservationId == reservation.Id).ExecuteDeleteAsync(ct);
    }

    private Task<Reservation?> FindByKeyAsync(Guid branchId, string key, CancellationToken ct) =>
        db.Reservations.AsNoTracking().SingleOrDefaultAsync(r => r.BranchId == branchId && r.IdempotencyKey == key, ct);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(2000, value.Trim().Length)];

    internal static ReservationDto ToDto(Reservation r, Guid? folioId) => new(
        r.Id, r.ReservationNumber, r.Status.ToString(),
        new GuestSummaryDto(r.GuestId, r.Guest?.FullName ?? string.Empty, r.Guest?.Email, r.Guest?.Phone),
        r.RoomTypeId, r.RoomType?.Code ?? string.Empty, r.RatePlanId, r.RatePlanName, r.RoomId, r.Room?.RoomNumber,
        r.ArrivalDate, r.DepartureDate, r.NightCount, r.Adults, r.Children, r.Source.ToString(), r.IsOverbooked,
        r.Currency, r.RoomSubtotal, r.DiscountAmount, r.TaxAmount, r.TotalAmount, r.DepositRequired, r.IsRefundable, r.CancellationPolicy,
        folioId, r.Notes, r.Nights.OrderBy(n => n.Date).Select(n => new ReservationNightDto(n.Date, n.Rate, n.Discount)).ToList(),
        r.ConcurrencyStamp);
}
