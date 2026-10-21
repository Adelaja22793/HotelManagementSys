using System.Text.Json;
using HMS.Application.Abstractions;
using HMS.Domain.Common;
using HMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HMS.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps created/updated metadata, rotates concurrency stamps and writes audit rows in the same
/// SaveChanges (and therefore the same transaction) as the business change.
/// Only property names are recorded, never values, so sensitive data does not leak into audit.
/// </summary>
public sealed class AuditingInterceptor(ICurrentUser currentUser, IClock clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;
        var now = clock.UtcNow;
        var userId = currentUser.UserId;
        var audits = new List<AuditLog>();

        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            // Ledger lines and stock history are append-only; corrections are new reversing records.
            if (entry.State != EntityState.Added && entry.Entity is StockMovement or JournalEntryLine or AuditLog or IncidentAction
                    or LostFoundCustodyEvent or EmergencyAction or DocumentSignature or NotificationDelivery)
                throw new InvalidOperationException($"{entry.Metadata.ClrType.Name} records are immutable.");
            if (entry.State == EntityState.Deleted && entry.Entity is JournalEntry)
                throw new InvalidOperationException("Journal entries are immutable.");
            if (entry.State == EntityState.Modified && entry.Entity is JournalEntry
                && entry.Properties.Any(p => p.IsModified && p.Metadata.Name != nameof(JournalEntry.ReversedByEntryId)))
                throw new InvalidOperationException("Journal entries are immutable; only the reversal link may be set.");

            if (entry.Entity is IAuditableEntity auditable)
            {
                if (entry.State == EntityState.Added)
                {
                    auditable.CreatedAtUtc = now;
                    auditable.CreatedByUserId ??= userId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    auditable.UpdatedAtUtc = now;
                    auditable.UpdatedByUserId = userId;
                }
            }

            if (entry.Entity is IConcurrencyTracked tracked && entry.State == EntityState.Modified)
                tracked.ConcurrencyStamp = Guid.NewGuid();

            if (entry.Entity is IAuditableEntity && entry.Entity is Entity entity)
                audits.Add(CreateAudit(entry, entity, now, userId));
        }

        if (audits.Count > 0) context.Set<AuditLog>().AddRange(audits);
    }

    private AuditLog CreateAudit(EntityEntry entry, Entity entity, DateTime now, string? userId)
    {
        var (companyId, branchId) = entry.Entity switch
        {
            Branch b => (b.CompanyId, (Guid?)b.Id),
            Company c => (c.Id, (Guid?)null),
            Account a => (a.CompanyId, (Guid?)null),
            AccountMapping m => (m.CompanyId, (Guid?)null),
            AccountingPeriod p => (p.CompanyId, (Guid?)null),
            IBranchOwned owned => (owned.CompanyId, (Guid?)owned.BranchId),
            HotelUserBranch m => (m.CompanyId, (Guid?)m.BranchId),
            _ => (Guid.Empty, (Guid?)null),
        };

        string? changed = null;
        if (entry.State == EntityState.Modified)
        {
            var names = entry.Properties
                .Where(p => p.IsModified && p.Metadata.Name is not (nameof(IAuditableEntity.UpdatedAtUtc) or nameof(IAuditableEntity.UpdatedByUserId) or nameof(IConcurrencyTracked.ConcurrencyStamp)))
                .Select(p => p.Metadata.Name)
                .ToArray();
            changed = JsonSerializer.Serialize(names);
        }

        return new AuditLog
        {
            OccurredAtUtc = now,
            CompanyId = companyId == Guid.Empty ? currentUser.CompanyId : companyId,
            BranchId = branchId,
            UserId = userId,
            Action = entry.State switch
            {
                EntityState.Added => "Created",
                EntityState.Deleted => "Deleted",
                _ => "Updated",
            },
            EntityType = entry.Metadata.ClrType.Name,
            EntityId = entity.Id.ToString(),
            ChangedProperties = changed,
            TraceId = currentUser.TraceId,
        };
    }
}
