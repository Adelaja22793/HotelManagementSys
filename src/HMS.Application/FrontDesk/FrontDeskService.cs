using HMS.Application.Abstractions;
using HMS.Application.Billing;
using HMS.Application.Common;
using HMS.Application.Reservations;
using HMS.Application.Rooms;
using HMS.Application.Security;
using HMS.Contracts.FrontDesk;
using HMS.Contracts.Reservations;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.FrontDesk;

public interface IFrontDeskService
{
    Task<FrontDeskSummaryDto> GetSummaryAsync(Guid branchId, CancellationToken ct = default);
    Task<IReadOnlyList<ReservationListItemDto>> GetArrivalsAsync(Guid branchId, DateOnly? date, CancellationToken ct = default);
    Task<IReadOnlyList<StayDto>> GetInHouseAsync(Guid branchId, CancellationToken ct = default);
    Task<IReadOnlyList<StayDto>> GetDeparturesAsync(Guid branchId, DateOnly? date, CancellationToken ct = default);
    Task<StayDto> GetStayAsync(Guid branchId, Guid stayId, CancellationToken ct = default);
    Task<StayDto> CheckInAsync(Guid branchId, CheckInRequest request, CancellationToken ct = default);
    Task<CheckOutResultDto> CheckOutAsync(Guid branchId, CheckOutRequest request, CancellationToken ct = default);
}

internal sealed class FrontDeskService(
    IApplicationDbContext db,
    IBranchAccessService access,
    ICurrentUser user,
    IClock clock,
    IReservationService reservations,
    IRoomInventoryService inventory,
    IFolioService folios) : IFrontDeskService
{
    public async Task<FrontDeskSummaryDto> GetSummaryAsync(Guid branchId, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var today = scope.BusinessDate;
        var res = db.Reservations.Where(r => r.BranchId == branchId);
        var rooms = await db.Rooms.Where(r => r.BranchId == branchId && r.IsActive).GroupBy(r => r.Status)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var blocked = await db.RoomBlocks.CountAsync(b => b.BranchId == branchId && b.IsActive && b.StartDate <= today && b.EndDate > today, ct);

        return new FrontDeskSummaryDto(today,
            await res.CountAsync(r => r.ArrivalDate == today && r.Status == ReservationStatus.Confirmed, ct),
            await res.CountAsync(r => r.ArrivalDate == today && r.Status == ReservationStatus.CheckedIn, ct),
            await res.CountAsync(r => r.DepartureDate == today && r.Status == ReservationStatus.CheckedIn, ct),
            await db.Stays.CountAsync(s => s.BranchId == branchId && s.Status == StayStatus.Closed && s.Reservation!.DepartureDate == today, ct),
            await db.Stays.CountAsync(s => s.BranchId == branchId && s.Status == StayStatus.Active, ct),
            rooms.Where(r => r.Key == RoomStatus.VacantClean).Sum(r => r.Count),
            rooms.Where(r => r.Key == RoomStatus.VacantDirty).Sum(r => r.Count),
            blocked);
    }

    public async Task<IReadOnlyList<ReservationListItemDto>> GetArrivalsAsync(Guid branchId, DateOnly? date, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var day = date ?? scope.BusinessDate;
        var page = await reservations.SearchAsync(branchId, new ReservationSearchQuery
        {
            Status = nameof(ReservationStatus.Confirmed), ArrivalFrom = day, ArrivalTo = day, PageSize = 200,
        }, ct);
        return page.Items;
    }

    public async Task<IReadOnlyList<StayDto>> GetInHouseAsync(Guid branchId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        return await StaysAsync(db.Stays.Where(s => s.BranchId == branchId && s.Status == StayStatus.Active), ct);
    }

    public async Task<IReadOnlyList<StayDto>> GetDeparturesAsync(Guid branchId, DateOnly? date, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var day = date ?? scope.BusinessDate;
        return await StaysAsync(db.Stays.Where(s => s.BranchId == branchId && s.Status == StayStatus.Active && s.Reservation!.DepartureDate <= day), ct);
    }

    public async Task<StayDto> GetStayAsync(Guid branchId, Guid stayId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var list = await StaysAsync(db.Stays.Where(s => s.BranchId == branchId && s.Id == stayId), ct);
        return list.SingleOrDefault() ?? throw new NotFoundException("Stay", stayId);
    }

    public async Task<StayDto> CheckInAsync(Guid branchId, CheckInRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var reservation = await db.Reservations.Include(r => r.RoomType).GetOwnedAsync(branchId, request.ReservationId, "Reservation", ct);
        BranchOperations.EnsureStamp(reservation, request.ConcurrencyStamp, "reservation");
        if (reservation.Status != ReservationStatus.Confirmed)
            throw new ConflictException("RESERVATION_STATE", $"Reservation {reservation.ReservationNumber} is {reservation.Status} and cannot be checked in.");

        var today = scope.BusinessDate;
        if (today < reservation.ArrivalDate)
            throw new ConflictException("EARLY_CHECK_IN", $"Arrival is {reservation.ArrivalDate:yyyy-MM-dd}. Change the reservation dates to check in early.");
        if (today >= reservation.DepartureDate)
            throw new ConflictException("STAY_ENDED", "The reservation's departure date has passed. Mark it no-show or change the dates.");

        var roomId = request.RoomId ?? reservation.RoomId
            ?? throw new ValidationAppException(nameof(request.RoomId), "Select a room for this guest.");
        var room = await db.Rooms.GetOwnedAsync(branchId, roomId, "Room", ct);
        if (await db.RoomBlocks.AnyAsync(b => b.RoomId == room.Id && b.IsActive && b.StartDate <= today && b.EndDate > today, ct))
            throw new ConflictException("ROOM_BLOCKED", $"Room {room.RoomNumber} is out of service.");
        var folio = await db.Folios.SingleAsync(f => f.ReservationId == reservation.Id, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (reservation.RoomId != room.Id || !await db.AssignedRoomNights.AnyAsync(a => a.ReservationId == reservation.Id && a.RoomId == room.Id, ct))
            await reservations.AssignRoomNightsAsync(scope, reservation, room, reservation.ArrivalDate, ct);

        room.MarkOccupied();
        reservation.MarkCheckedIn();
        var stay = new Stay(scope.CompanyId, branchId, reservation.Id, reservation.GuestId, room.Id, scope.UtcNow, scope.UserId);
        db.Stays.Add(stay);
        folio.AttachStay(stay.Id);
        // Policy: the accepted nightly rates are posted at check-in; unused nights are reversed at early check-out.
        await folios.PostRoomChargesAsync(scope, folio, reservation, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            throw new ConflictException("ROOM_UNAVAILABLE", $"Room {room.RoomNumber} is already occupied or assigned to another guest.");
        }
        await tx.CommitAsync(ct);
        return await GetStayAsync(branchId, stay.Id, ct);
    }

    public async Task<CheckOutResultDto> CheckOutAsync(Guid branchId, CheckOutRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var stay = await db.Stays.GetOwnedAsync(branchId, request.StayId, "Stay", ct);
        if (stay.Status != StayStatus.Active) throw new ConflictException("STAY_CLOSED", "The guest has already checked out.");
        var reservation = await db.Reservations.Include(r => r.Guest).SingleAsync(r => r.Id == stay.ReservationId, ct);
        var room = await db.Rooms.SingleAsync(r => r.Id == stay.RoomId, ct);
        var folio = await db.Folios.Include(f => f.Reservation).Include(f => f.Guest).SingleAsync(f => f.ReservationId == reservation.Id, ct);

        // Guests are charged at least one night; leaving early releases the unused nights.
        var minimumDeparture = reservation.ArrivalDate.AddDays(1);
        var actualDeparture = scope.BusinessDate < minimumDeparture ? minimumDeparture
            : scope.BusinessDate > reservation.DepartureDate ? reservation.DepartureDate : scope.BusinessDate;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (actualDeparture < reservation.DepartureDate)
        {
            await folios.ReverseRoomChargesFromAsync(scope, folio, actualDeparture, "early departure", ct);
            await inventory.ReleaseAsync(branchId, reservation.RoomTypeId, actualDeparture, reservation.DepartureDate, 1, ct);
            await db.AssignedRoomNights.Where(a => a.ReservationId == reservation.Id && a.Date >= actualDeparture).ExecuteDeleteAsync(ct);
        }

        var balance = (await folios.GetBalanceAsync(folio.Id, ct)).Balance;
        var withBalance = balance > 0;
        if (withBalance)
        {
            if (!request.AllowBalance)
                throw new ConflictException("BALANCE_DUE", $"The guest owes {folio.Currency} {balance:N2}. Take payment before check-out.");
            if (!RolePermissions.HasPermission(user.Roles, Permissions.CheckOutWithBalance))
                throw new ForbiddenException(message: "Checking out with an unpaid balance requires manager permission.");
            new Validation().Require(nameof(request.BalanceOverrideReason), request.BalanceOverrideReason, 500).ThrowIfInvalid();
        }

        stay.Close(scope.UtcNow, scope.UserId, withBalance, withBalance ? request.BalanceOverrideReason!.Trim() : null);
        reservation.MarkCheckedOut(actualDeparture);
        room.MarkVacantDirty();

        var task = await db.HousekeepingTasks.SingleOrDefaultAsync(t => t.BranchId == branchId && t.SourceType == "CheckOut" && t.SourceId == stay.Id.ToString(), ct);
        if (task is null)
        {
            task = new HousekeepingTask(scope.CompanyId, branchId, room.Id, HousekeepingTaskType.Checkout, "CheckOut", stay.Id.ToString(), scope.BusinessDate);
            db.HousekeepingTasks.Add(task);
        }

        // A zero balance closes the folio; any debit or credit stays open for collection or refund.
        if (balance == 0) folio.Close(scope.UtcNow);
        var invoice = await folios.CreateInvoiceAsync(scope, folio, ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new CheckOutResultDto(await GetStayAsync(branchId, stay.Id, ct), invoice.Id, invoice.InvoiceNumber, task.Id);
    }

    private async Task<IReadOnlyList<StayDto>> StaysAsync(IQueryable<Stay> query, CancellationToken ct)
    {
        var stays = await query.AsNoTracking()
            .Select(s => new
            {
                s.Id, s.ReservationId, s.Reservation!.ReservationNumber, s.GuestId,
                GuestFirst = s.Reservation.Guest!.FirstName, GuestLast = s.Reservation.Guest.LastName,
                s.RoomId, s.Room!.RoomNumber, s.Status, s.Reservation.ArrivalDate, s.Reservation.DepartureDate,
                s.CheckedInAtUtc, s.CheckedOutAtUtc,
            })
            .OrderBy(s => s.RoomNumber)
            .Take(500)
            .ToListAsync(ct);

        var reservationIds = stays.Select(s => s.ReservationId).ToList();
        var folioIds = await db.Folios.Where(f => f.ReservationId != null && reservationIds.Contains(f.ReservationId.Value))
            .Select(f => new { ReservationId = f.ReservationId!.Value, f.Id }).ToDictionaryAsync(f => f.ReservationId, f => f.Id, ct);

        var result = new List<StayDto>(stays.Count);
        foreach (var s in stays)
        {
            Guid? folioId = folioIds.TryGetValue(s.ReservationId, out var fid) ? fid : null;
            var balance = folioId is { } id ? (await folios.GetBalanceAsync(id, ct)).Balance : 0;
            result.Add(new StayDto(s.Id, s.ReservationId, s.ReservationNumber, s.GuestId, $"{s.GuestFirst} {s.GuestLast}", s.RoomId, s.RoomNumber,
                s.Status.ToString(), s.ArrivalDate, s.DepartureDate, s.CheckedInAtUtc, s.CheckedOutAtUtc, folioId, balance));
        }
        return result;
    }
}
