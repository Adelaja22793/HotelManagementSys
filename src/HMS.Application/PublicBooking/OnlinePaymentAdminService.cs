using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Contracts.PublicBooking;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.PublicBooking;

/// <summary>Staff tools for the website: switch online booking on or off and deal with payments that need a person.</summary>
public interface IOnlinePaymentAdminService
{
    Task SetSettingsAsync(Guid branchId, PublicBookingSettingsRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<OnlinePaymentDto>> ListAsync(Guid branchId, string? status, CancellationToken ct = default);
    Task<OnlinePaymentDto> ResolveAsync(Guid branchId, Guid paymentId, ResolvePaymentRequest request, CancellationToken ct = default);
}

internal sealed class OnlinePaymentAdminService(IApplicationDbContext db, IBranchAccessService access, ICurrentUser user, IClock clock)
    : IOnlinePaymentAdminService
{
    public async Task SetSettingsAsync(Guid branchId, PublicBookingSettingsRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var branch = await db.Branches.SingleAsync(b => b.Id == scope.BranchId, ct);
        branch.SetPublicBooking(request.Enabled);
        db.Touch(branch);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<OnlinePaymentDto>> ListAsync(Guid branchId, string? status, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.BookingPaymentAttempts.AsNoTracking().Where(p => p.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(p => p.Status == EnumParser.Parse<OnlinePaymentStatus>(status, nameof(status)));
        var list = await q.OrderByDescending(p => p.CreatedAtUtc).Take(200).ToListAsync(ct);
        var holdIds = list.Select(p => p.HoldId).Distinct().ToList();
        var holds = await db.BookingHolds.AsNoTracking().Where(h => holdIds.Contains(h.Id)).ToDictionaryAsync(h => h.Id, ct);
        return list.Select(p => ToDto(p, holds[p.HoldId])).ToList();
    }

    public async Task<OnlinePaymentDto> ResolveAsync(Guid branchId, Guid paymentId, ResolvePaymentRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var payment = await db.BookingPaymentAttempts.GetOwnedAsync(branchId, paymentId, "Online payment", ct);
        BranchOperations.EnsureStamp(payment, request.ConcurrencyStamp, "payment");
        payment.Resolve(scope.UserId, request.Note);
        db.Touch(payment);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(Contracts.Common.ErrorCodes.ConcurrencyConflict, "The payment was changed by someone else. Reload and try again.");
        }
        var hold = await db.BookingHolds.AsNoTracking().SingleAsync(h => h.Id == payment.HoldId, ct);
        return ToDto(payment, hold);
    }

    private static OnlinePaymentDto ToDto(BookingPaymentAttempt p, BookingHold h) => new(p.Id, h.Reference, p.Provider, p.MerchantReference, p.Amount,
        p.Currency, p.Status.ToString(), p.ProviderStatus, p.ProviderTransactionId, p.ReportedAmount, p.ReportedCurrency, p.FailureReason, p.CreatedAtUtc,
        p.VerifiedAtUtc, h.GuestLastName is null ? null : $"{h.GuestFirstName} {h.GuestLastName}", h.GuestEmail, p.ResolutionNote, p.ConcurrencyStamp);
}
