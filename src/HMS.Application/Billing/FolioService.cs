using System.Text.Json;
using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Application.Rates;
using HMS.Application.Security;
using HMS.Contracts.Billing;
using HMS.Domain.Common;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Billing;

public sealed record FolioBalance(decimal TotalCharges, decimal TotalTax, decimal ConfirmedPayments, decimal PendingPayments,
    decimal ExecutedRefunds)
{
    /// <summary>Outstanding = posted charges − confirmed payments + executed refunds. Pending payments never count.</summary>
    public decimal Balance => TotalCharges - ConfirmedPayments + ExecutedRefunds;
}

/// <summary>Shared folio operations used by front desk, cashier and (later) restaurant and room service.</summary>
public interface IFolioService
{
    Task<FolioDto> GetAsync(Guid branchId, Guid folioId, CancellationToken ct = default);
    Task<FolioDto> GetByReservationAsync(Guid branchId, Guid reservationId, CancellationToken ct = default);
    Task<FolioDto> PostChargeAsync(Guid branchId, Guid folioId, PostChargeRequest request, string? idempotencyKey, CancellationToken ct = default);
    Task<FolioDto> ReverseChargeAsync(Guid branchId, Guid folioId, Guid chargeId, ReverseChargeRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<InvoiceDto>> ListInvoicesAsync(Guid branchId, Guid folioId, CancellationToken ct = default);
    Task<InvoiceDto> IssueInvoiceAsync(Guid branchId, Guid folioId, CancellationToken ct = default);

    // Internal building blocks (callers own the transaction).
    Task<Folio> CreateForReservationAsync(BranchScope scope, Reservation reservation, CancellationToken ct);
    Task<FolioCharge> PostAsync(BranchScope scope, Folio folio, ChargeType type, string description, decimal quantity, decimal unitPrice,
        bool taxable, string? idempotencyKey, Guid? serviceItemId, DateOnly? businessDate, CancellationToken ct);
    Task PostRoomChargesAsync(BranchScope scope, Folio folio, Reservation reservation, CancellationToken ct);
    Task ReverseRoomChargesFromAsync(BranchScope scope, Folio folio, DateOnly fromNight, string reason, CancellationToken ct);
    Task<FolioBalance> GetBalanceAsync(Guid folioId, CancellationToken ct);
    Task<Invoice> CreateInvoiceAsync(BranchScope scope, Folio folio, CancellationToken ct);
}

internal sealed class FolioService(
    IApplicationDbContext db,
    IBranchAccessService access,
    ICurrentUser user,
    IClock clock,
    INumberSequence numbers,
    IPricingService pricing) : IFolioService
{
    public async Task<FolioDto> GetAsync(Guid branchId, Guid folioId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var folio = await db.Folios.AsNoTracking().Include(f => f.Reservation).Include(f => f.Guest).GetOwnedAsync(branchId, folioId, "Folio", ct);
        return await ToDtoAsync(folio, ct);
    }

    public async Task<FolioDto> GetByReservationAsync(Guid branchId, Guid reservationId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var folio = await db.Folios.AsNoTracking().Include(f => f.Reservation).Include(f => f.Guest)
            .SingleOrDefaultAsync(f => f.BranchId == branchId && f.ReservationId == reservationId, ct)
            ?? throw new NotFoundException("Folio for reservation", reservationId);
        return await ToDtoAsync(folio, ct);
    }

    public async Task<FolioDto> PostChargeAsync(Guid branchId, Guid folioId, PostChargeRequest request, string? idempotencyKey, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ValidationAppException("Idempotency-Key", "An Idempotency-Key header is required when posting charges.");
        if (request.Quantity <= 0 || request.Quantity > 1000)
            throw new ValidationAppException(nameof(request.Quantity), "Quantity must be between 0 and 1000.");

        var folio = await db.Folios.Include(f => f.Reservation).Include(f => f.Guest).GetOwnedAsync(branchId, folioId, "Folio", ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (request.ServiceItemId is { } serviceId)
        {
            var item = await db.ServiceItems.AsNoTracking().GetOwnedAsync(branchId, serviceId, "Service item", ct);
            if (!item.IsActive) throw new ConflictException("SERVICE_INACTIVE", $"{item.Name} is not available.");
            await PostAsync(scope, folio, ChargeType.Service, item.Name, request.Quantity, item.UnitPrice, item.IsTaxable,
                "API:" + idempotencyKey, item.Id, null, ct);
        }
        else
        {
            if (!RolePermissions.HasPermission(user.Roles, Permissions.FolioAdjust))
                throw new ForbiddenException(message: "Custom charges and adjustments require folio adjustment permission.");
            new Validation().Require(nameof(request.Description), request.Description, 300)
                .When(request.UnitPrice is null || request.UnitPrice == 0, nameof(request.UnitPrice), "Unit price is required.")
                .ThrowIfInvalid();
            var type = request.UnitPrice < 0 ? ChargeType.Adjustment : ChargeType.Service;
            await PostAsync(scope, folio, type, request.Description!, request.Quantity, request.UnitPrice!.Value, false,
                "API:" + idempotencyKey, null, null, ct);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ToDtoAsync(folio, ct);
    }

    public async Task<FolioDto> ReverseChargeAsync(Guid branchId, Guid folioId, Guid chargeId, ReverseChargeRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.Reason), request.Reason, 200).ThrowIfInvalid();
        var folio = await db.Folios.Include(f => f.Reservation).Include(f => f.Guest).GetOwnedAsync(branchId, folioId, "Folio", ct);
        folio.EnsureOpen();
        var charge = await db.FolioCharges.SingleOrDefaultAsync(c => c.Id == chargeId && c.FolioId == folio.Id, ct)
            ?? throw new NotFoundException("Charge", chargeId);
        if (charge.ParentChargeId is not null)
            throw new ConflictException("REVERSE_PARENT", "Reverse the original charge; its tax is reversed with it.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ReverseWithTaxAsync(scope, charge, request.Reason, ct);
        db.Touch(folio);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ToDtoAsync(folio, ct);
    }

    public async Task<IReadOnlyList<InvoiceDto>> ListInvoicesAsync(Guid branchId, Guid folioId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.BranchId == branchId && i.FolioId == folioId).OrderBy(i => i.Version).ToListAsync(ct);
        return invoices.Select(ToDto).ToList();
    }

    public async Task<InvoiceDto> IssueInvoiceAsync(Guid branchId, Guid folioId, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var folio = await db.Folios.Include(f => f.Reservation).Include(f => f.Guest).GetOwnedAsync(branchId, folioId, "Folio", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var invoice = await CreateInvoiceAsync(scope, folio, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(invoice);
    }

    public async Task<Folio> CreateForReservationAsync(BranchScope scope, Reservation reservation, CancellationToken ct)
    {
        var number = await numbers.NextAsync(scope.Branch, SequenceNames.Folio, ct);
        var folio = new Folio(scope.CompanyId, scope.BranchId, number, reservation.Id, reservation.GuestId, reservation.Currency);
        db.Folios.Add(folio);
        return folio;
    }

    public async Task<FolioCharge> PostAsync(BranchScope scope, Folio folio, ChargeType type, string description, decimal quantity,
        decimal unitPrice, bool taxable, string? idempotencyKey, Guid? serviceItemId, DateOnly? businessDate, CancellationToken ct)
    {
        folio.EnsureOpen();
        if (idempotencyKey is not null)
        {
            var existing = db.FolioCharges.Local.FirstOrDefault(c => c.FolioId == folio.Id && c.IdempotencyKey == idempotencyKey)
                ?? await db.FolioCharges.SingleOrDefaultAsync(c => c.FolioId == folio.Id && c.IdempotencyKey == idempotencyKey, ct);
            if (existing is not null) return existing; // retried request: no duplicate charge
        }

        var date = businessDate ?? scope.BusinessDate;
        var charge = new FolioCharge(scope.CompanyId, scope.BranchId, folio.Id, type, description, quantity, unitPrice, date, idempotencyKey, serviceItemId);
        db.FolioCharges.Add(charge);

        if (taxable && charge.Amount > 0)
        {
            foreach (var tax in (await pricing.TaxesForAsync(scope.BranchId, TaxScope.Service, date, ct)).Where(t => !t.IsInclusive))
            {
                var amount = Money.Round(charge.Amount * tax.RatePercent / 100m);
                if (amount == 0) continue;
                var taxCharge = new FolioCharge(scope.CompanyId, scope.BranchId, folio.Id, ChargeType.Tax, $"{tax.Name} on {description}",
                    1, amount, date, idempotencyKey is null ? null : $"{idempotencyKey}:TAX:{tax.Code}");
                taxCharge.LinkToParent(charge.Id);
                db.FolioCharges.Add(taxCharge);
            }
        }
        db.Touch(folio);
        return charge;
    }

    public async Task PostRoomChargesAsync(BranchScope scope, Folio folio, Reservation reservation, CancellationToken ct)
    {
        var nights = await db.ReservationNights.AsNoTracking().Where(n => n.ReservationId == reservation.Id).OrderBy(n => n.Date).ToListAsync(ct);
        foreach (var night in nights)
        {
            var description = night.Discount > 0
                ? $"Room {night.Date:yyyy-MM-dd} ({reservation.RatePlanName}, discount {night.Discount:N2})"
                : $"Room {night.Date:yyyy-MM-dd} ({reservation.RatePlanName})";
            var charge = await PostAsync(scope, folio, ChargeType.Room, description, 1, night.Net, false,
                $"ROOM:{reservation.Id}:{night.Date:yyyyMMdd}", null, night.Date, ct);

            if (night.TaxAmount > 0)
            {
                var tax = await PostAsync(scope, folio, ChargeType.Tax, $"Room taxes {night.Date:yyyy-MM-dd}", 1, night.TaxAmount, false,
                    $"ROOMTAX:{reservation.Id}:{night.Date:yyyyMMdd}", null, night.Date, ct);
                tax.LinkToParent(charge.Id);
            }
        }
    }

    public async Task ReverseRoomChargesFromAsync(BranchScope scope, Folio folio, DateOnly fromNight, string reason, CancellationToken ct)
    {
        var charges = await db.FolioCharges
            .Where(c => c.FolioId == folio.Id && c.Type == ChargeType.Room && c.BusinessDate >= fromNight
                        && c.ReversesChargeId == null && c.ReversedByChargeId == null)
            .ToListAsync(ct);
        foreach (var charge in charges) await ReverseWithTaxAsync(scope, charge, reason, ct);
    }

    public async Task<FolioBalance> GetBalanceAsync(Guid folioId, CancellationToken ct)
    {
        var charges = await db.FolioCharges.Where(c => c.FolioId == folioId).Select(c => new { c.Type, c.Amount }).ToListAsync(ct);
        var payments = await db.Payments.Where(p => p.FolioId == folioId && p.Status != PaymentStatus.Failed)
            .Select(p => new { p.Status, p.Amount }).ToListAsync(ct);
        var refunds = await db.Refunds.Where(r => r.FolioId == folioId && r.Status == RefundStatus.Executed).Select(r => r.Amount).SumClientAsync(ct);

        // Include anything added in this unit of work but not yet saved.
        var localCharges = db.FolioCharges.Local.Where(c => c.FolioId == folioId && db.ChangeTracker.Entries<FolioCharge>()
            .Any(e => e.Entity == c && e.State == EntityState.Added)).Select(c => new { c.Type, c.Amount });
        var all = charges.Concat(localCharges).ToList();

        return new FolioBalance(
            all.Sum(c => c.Amount),
            all.Where(c => c.Type == ChargeType.Tax).Sum(c => c.Amount),
            payments.Where(p => p.Status == PaymentStatus.Confirmed).Sum(p => p.Amount),
            payments.Where(p => p.Status == PaymentStatus.Pending).Sum(p => p.Amount),
            refunds);
    }

    public async Task<Invoice> CreateInvoiceAsync(BranchScope scope, Folio folio, CancellationToken ct)
    {
        var balance = await GetBalanceAsync(folio.Id, ct);
        var version = await db.Invoices.CountAsync(i => i.FolioId == folio.Id, ct) + 1;
        var number = await numbers.NextAsync(scope.Branch, SequenceNames.Invoice, ct);
        var charges = await db.FolioCharges.AsNoTracking().Where(c => c.FolioId == folio.Id).OrderBy(c => c.BusinessDate).ThenBy(c => c.CreatedAtUtc).ToListAsync(ct);

        var snapshot = JsonSerializer.Serialize(new
        {
            invoiceNumber = number,
            version,
            branch = new { scope.Branch.Code, scope.Branch.Name, scope.Branch.AddressLine, scope.Branch.City, scope.Branch.Phone },
            folio = folio.FolioNumber,
            reservation = folio.Reservation?.ReservationNumber,
            guest = folio.Guest?.FullName,
            currency = folio.Currency,
            issuedAtUtc = scope.UtcNow,
            lines = charges.Select(c => new { date = c.BusinessDate, c.Description, type = c.Type.ToString(), c.Quantity, c.UnitPrice, c.Amount }),
            totals = new { charges = balance.TotalCharges, tax = balance.TotalTax, paid = balance.ConfirmedPayments, refunded = balance.ExecutedRefunds, balance = balance.Balance },
        });

        var invoice = new Invoice(scope.CompanyId, scope.BranchId, folio.Id, number, version, scope.UtcNow,
            balance.TotalCharges, balance.TotalTax, balance.ConfirmedPayments - balance.ExecutedRefunds, balance.Balance, snapshot);
        db.Invoices.Add(invoice);
        return invoice;
    }

    private async Task ReverseWithTaxAsync(BranchScope scope, FolioCharge charge, string reason, CancellationToken ct)
    {
        db.FolioCharges.Add(charge.CreateReversal(scope.BusinessDate, reason));
        var taxes = await db.FolioCharges.Where(c => c.ParentChargeId == charge.Id && c.ReversedByChargeId == null).ToListAsync(ct);
        foreach (var tax in taxes)
        {
            var reversal = tax.CreateReversal(scope.BusinessDate, reason);
            reversal.LinkToParent(charge.Id);
            db.FolioCharges.Add(reversal);
        }
    }

    internal async Task<FolioDto> ToDtoAsync(Folio folio, CancellationToken ct)
    {
        var balance = await GetBalanceAsync(folio.Id, ct);
        var charges = await db.FolioCharges.AsNoTracking().Where(c => c.FolioId == folio.Id).ToListAsync(ct);
        var payments = await db.Payments.AsNoTracking().Where(p => p.FolioId == folio.Id).ToListAsync(ct);
        var paymentIds = payments.Select(p => p.Id).ToList();
        var receipts = await db.Receipts.AsNoTracking().Where(r => paymentIds.Contains(r.PaymentId)).ToListAsync(ct);
        var refunds = await db.Refunds.AsNoTracking().Where(r => r.FolioId == folio.Id).ToListAsync(ct);

        return new FolioDto(folio.Id, folio.FolioNumber, folio.ReservationId, folio.Reservation?.ReservationNumber ?? string.Empty,
            folio.StayId, folio.Guest?.FullName ?? string.Empty, folio.Currency, folio.Status.ToString(),
            balance.TotalCharges, balance.TotalTax, balance.ConfirmedPayments, balance.PendingPayments, balance.ExecutedRefunds, balance.Balance,
            charges.OrderBy(c => c.BusinessDate).ThenBy(c => c.CreatedAtUtc)
                .Select(c => new FolioChargeDto(c.Id, c.Type.ToString(), c.Description, c.Quantity, c.UnitPrice, c.Amount, c.BusinessDate,
                    c.CreatedAtUtc, c.ReversesChargeId, c.IsReversed, c.ParentChargeId)).ToList(),
            payments.OrderBy(p => p.ReceivedAtUtc).Select(p =>
            {
                var receipt = receipts.FirstOrDefault(r => r.PaymentId == p.Id);
                return new PaymentDto(p.Id, p.Tender.ToString(), p.Amount, p.Currency, p.Status.ToString(), p.ExternalReference,
                    p.ReceivedAtUtc, p.ConfirmedAtUtc, receipt?.ReceiptNumber, receipt?.Id);
            }).ToList(),
            refunds.OrderBy(r => r.RequestedAtUtc).Select(PaymentService.ToDto).ToList());
    }

    internal static InvoiceDto ToDto(Invoice i) =>
        new(i.Id, i.InvoiceNumber, i.Version, i.FolioId, i.IssuedAtUtc, i.TotalCharges, i.TotalTax, i.TotalPaid, i.Balance, i.SnapshotJson);
}
