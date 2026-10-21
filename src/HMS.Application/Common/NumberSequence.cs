using HMS.Application.Abstractions;
using HMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Common;

public interface INumberSequence
{
    /// <summary>Allocates the next branch number (e.g. RES-LOS-000123). Must be called inside a transaction.</summary>
    Task<string> NextAsync(Branch branch, string sequenceName, CancellationToken ct);

    /// <summary>Adds any missing counters for a branch to the context (saved by the caller).</summary>
    Task EnsureCountersAsync(Guid branchId, CancellationToken ct);
}

/// <summary>
/// Concurrency-safe numbering: the increment takes a row lock held until the caller's transaction
/// commits, so two transactions can never read the same value; a rollback also rolls the number back.
/// </summary>
internal sealed class NumberSequence(IApplicationDbContext db) : INumberSequence
{
    public static readonly string[] All =
        [
            SequenceNames.Reservation, SequenceNames.Folio, SequenceNames.Receipt, SequenceNames.Invoice, SequenceNames.Refund,
            SequenceNames.Journal, SequenceNames.Requisition, SequenceNames.PurchaseOrder, SequenceNames.GoodsReceipt,
            SequenceNames.SupplierInvoice, SequenceNames.SupplierPayment, SequenceNames.RestaurantOrder, SequenceNames.KitchenTicket,
            SequenceNames.WorkOrder, SequenceNames.StockDocument,
        ];

    public async Task<string> NextAsync(Branch branch, string sequenceName, CancellationToken ct)
    {
        db.RequireTransaction();
        var updated = await db.BranchSequences
            .Where(s => s.BranchId == branch.Id && s.Name == sequenceName)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextValue, x => x.NextValue + 1), ct);
        if (updated != 1)
            throw new InvalidOperationException($"Sequence {sequenceName} is missing for branch {branch.Code}.");

        var value = await db.BranchSequences
            .Where(s => s.BranchId == branch.Id && s.Name == sequenceName)
            .Select(s => s.NextValue)
            .SingleAsync(ct);
        return $"{sequenceName}-{branch.Code}-{value:D6}";
    }

    public async Task EnsureCountersAsync(Guid branchId, CancellationToken ct)
    {
        var existing = (await db.BranchSequences.Where(s => s.BranchId == branchId).Select(s => s.Name).ToListAsync(ct)).ToHashSet();
        foreach (var name in All.Where(n => !existing.Contains(n)))
            db.BranchSequences.Add(new BranchSequence { BranchId = branchId, Name = name, NextValue = 0 });
    }
}
