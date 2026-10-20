using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Contracts.Cashier;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Cashier;

public interface ICashierService
{
    Task<CashierShiftDto> OpenAsync(Guid branchId, OpenShiftRequest request, CancellationToken ct = default);
    Task<CashierShiftDto?> GetCurrentAsync(Guid branchId, CancellationToken ct = default);
    Task<CashierShiftDto> GetAsync(Guid branchId, Guid shiftId, CancellationToken ct = default);
    Task<IReadOnlyList<CashierShiftDto>> ListAsync(Guid branchId, string? status, CancellationToken ct = default);
    Task<CashierShiftDto> CloseAsync(Guid branchId, Guid shiftId, CloseShiftRequest request, CancellationToken ct = default);
    Task<CashierShiftDto> ApproveVarianceAsync(Guid branchId, Guid shiftId, ApproveVarianceRequest request, CancellationToken ct = default);

    /// <summary>The caller's open shift, locked against a concurrent close by touching its concurrency stamp.</summary>
    Task<CashierShift> RequireOpenShiftAsync(BranchScope scope, CancellationToken ct);
    Task<CashierShift?> FindOpenShiftAsync(BranchScope scope, CancellationToken ct);
}

internal sealed class CashierService(IApplicationDbContext db, IBranchAccessService access, ICurrentUser user, IClock clock) : ICashierService
{
    public async Task<CashierShiftDto> OpenAsync(Guid branchId, OpenShiftRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        if (await db.CashierShifts.AnyAsync(s => s.BranchId == branchId && s.CashierUserId == scope.UserId && s.Status != CashierShiftStatus.Closed, ct))
            throw new ConflictException("SHIFT_ALREADY_OPEN", "You already have an open cashier shift in this branch.");

        var shift = new CashierShift(scope.CompanyId, branchId, scope.UserId, request.OpeningFloat, scope.UtcNow, scope.BusinessDate);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.CashierShifts.Add(shift);
        if (request.OpeningFloat > 0)
            db.CashierMovements.Add(new CashierMovement(scope.CompanyId, branchId, shift.Id, CashierMovementType.OpeningFloat,
                TenderType.Cash, request.OpeningFloat, scope.UtcNow));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The filtered unique index caught a concurrent second open.
            throw new ConflictException("SHIFT_ALREADY_OPEN", "You already have an open cashier shift in this branch.");
        }
        await tx.CommitAsync(ct);
        return await ToDtoAsync(shift, ct);
    }

    public async Task<CashierShiftDto?> GetCurrentAsync(Guid branchId, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var shift = await db.CashierShifts.AsNoTracking()
            .SingleOrDefaultAsync(s => s.BranchId == branchId && s.CashierUserId == scope.UserId && s.Status != CashierShiftStatus.Closed, ct);
        return shift is null ? null : await ToDtoAsync(shift, ct);
    }

    public async Task<CashierShiftDto> GetAsync(Guid branchId, Guid shiftId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var shift = await db.CashierShifts.AsNoTracking().GetOwnedAsync(branchId, shiftId, "Cashier shift", ct);
        EnsureOwnerOrSupervisor(shift);
        return await ToDtoAsync(shift, ct);
    }

    public async Task<IReadOnlyList<CashierShiftDto>> ListAsync(Guid branchId, string? status, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.CashierShifts.AsNoTracking().Where(s => s.BranchId == branchId);
        if (!IsSupervisor()) q = q.Where(s => s.CashierUserId == user.UserId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = EnumParser.Parse<CashierShiftStatus>(status, nameof(status));
            q = q.Where(s => s.Status == parsed);
        }
        var shifts = await q.OrderByDescending(s => s.OpenedAtUtc).Take(100).ToListAsync(ct);
        var result = new List<CashierShiftDto>();
        foreach (var s in shifts) result.Add(await ToDtoAsync(s, ct));
        return result;
    }

    public async Task<CashierShiftDto> CloseAsync(Guid branchId, Guid shiftId, CloseShiftRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var shift = await db.CashierShifts.GetOwnedAsync(branchId, shiftId, "Cashier shift", ct);
        if (shift.CashierUserId != scope.UserId)
            throw new ForbiddenException(message: "Only the cashier who opened the shift can close it.");
        BranchOperations.EnsureStamp(shift, request.ConcurrencyStamp, "cashier shift");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (cash, pos, transfer) = await TotalsAsync(shift.Id, ct);
        shift.SubmitCount(scope.UtcNow, cash, pos, transfer, request.CountedCash, request.VerifiedPos, request.VerifiedTransfer, request.VarianceExplanation);
        // The stamp check on save makes a concurrent payment (which touches the shift) and this close mutually exclusive.
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ToDtoAsync(shift, ct);
    }

    public async Task<CashierShiftDto> ApproveVarianceAsync(Guid branchId, Guid shiftId, ApproveVarianceRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var shift = await db.CashierShifts.GetOwnedAsync(branchId, shiftId, "Cashier shift", ct);
        BranchOperations.EnsureStamp(shift, request.ConcurrencyStamp, "cashier shift");
        shift.ApproveVariance(scope.UserId, scope.UtcNow);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(shift, ct);
    }

    public async Task<CashierShift> RequireOpenShiftAsync(BranchScope scope, CancellationToken ct) =>
        await FindOpenShiftAsync(scope, ct)
        ?? throw new ConflictException("SHIFT_REQUIRED", "Open a cashier shift before taking cash or card payments.");

    public async Task<CashierShift?> FindOpenShiftAsync(BranchScope scope, CancellationToken ct)
    {
        var shift = await db.CashierShifts.SingleOrDefaultAsync(
            s => s.BranchId == scope.BranchId && s.CashierUserId == scope.UserId && s.Status != CashierShiftStatus.Closed, ct);
        if (shift is null) return null;
        shift.EnsureOpen();
        db.Touch(shift);
        return shift;
    }

    private async Task<(decimal Cash, decimal Pos, decimal Transfer)> TotalsAsync(Guid shiftId, CancellationToken ct)
    {
        var moves = await db.CashierMovements.Where(m => m.ShiftId == shiftId).Select(m => new { m.Tender, m.Amount }).ToListAsync(ct);
        return (moves.Where(m => m.Tender == TenderType.Cash).Sum(m => m.Amount),
                moves.Where(m => m.Tender == TenderType.PosCard).Sum(m => m.Amount),
                moves.Where(m => m.Tender == TenderType.BankTransfer).Sum(m => m.Amount));
    }

    private bool IsSupervisor() => Security.RolePermissions.HasPermission(user.Roles, Security.Permissions.CashierApproveVariance);

    private void EnsureOwnerOrSupervisor(CashierShift shift)
    {
        if (shift.CashierUserId != user.UserId && !IsSupervisor())
            throw new NotFoundException("Cashier shift", shift.Id);
    }

    private async Task<CashierShiftDto> ToDtoAsync(CashierShift s, CancellationToken ct)
    {
        var (cash, pos, transfer) = await TotalsAsync(s.Id, ct);
        return new CashierShiftDto(s.Id, s.CashierUserId, s.Status.ToString(), s.BusinessDate, s.OpenedAtUtc, s.ClosedAtUtc, s.OpeningFloat,
            cash, pos, transfer, s.ExpectedCash, s.ExpectedPos, s.ExpectedTransfer, s.CountedCash, s.VerifiedPos, s.VerifiedTransfer,
            s.CashVariance, s.PosVariance, s.TransferVariance, s.VarianceExplanation, s.ApprovedByUserId, s.ConcurrencyStamp);
    }
}
