using HMS.Application.Abstractions;
using HMS.Application.Accounting;
using HMS.Application.Common;
using HMS.Application.Inventory;
using HMS.Application.Rooms;
using HMS.Contracts.Maintenance;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Maintenance;

public interface IMaintenanceService
{
    Task<IReadOnlyList<AssetDto>> ListAssetsAsync(Guid branchId, CancellationToken ct = default);
    Task<AssetDto> SaveAssetAsync(Guid branchId, Guid? assetId, SaveAssetRequest request, CancellationToken ct = default);
    Task<AssetHistoryDto> AssetHistoryAsync(Guid branchId, Guid assetId, CancellationToken ct = default);
    Task<IReadOnlyList<MaintenanceRequestDto>> ListRequestsAsync(Guid branchId, bool openOnly, CancellationToken ct = default);
    Task<MaintenanceRequestDto> ReportAsync(Guid branchId, CreateMaintenanceRequestRequest request, CancellationToken ct = default);
    Task<MaintenanceRequestDto> TriageAsync(Guid branchId, Guid requestId, TriageRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<WorkOrderDto>> ListWorkOrdersAsync(Guid branchId, bool openOnly, string? assignedTo, CancellationToken ct = default);
    Task<WorkOrderDto> GetWorkOrderAsync(Guid branchId, Guid workOrderId, CancellationToken ct = default);
    Task<WorkOrderDto> CreateWorkOrderAsync(Guid branchId, CreateWorkOrderRequest request, CancellationToken ct = default);
    Task<WorkOrderDto> AssignAsync(Guid branchId, Guid workOrderId, AssignWorkOrderRequest request, CancellationToken ct = default);
    Task<WorkOrderDto> StartAsync(Guid branchId, Guid workOrderId, WorkOrderActionRequest request, CancellationToken ct = default);
    Task<WorkOrderDto> UsePartAsync(Guid branchId, Guid workOrderId, UsePartRequest request, string? idempotencyKey, CancellationToken ct = default);
    Task<WorkOrderDto> FinishAsync(Guid branchId, Guid workOrderId, FinishWorkRequest request, CancellationToken ct = default);
    Task<WorkOrderDto> InspectAsync(Guid branchId, Guid workOrderId, InspectWorkRequest request, CancellationToken ct = default);
    Task<WorkOrderDto> CancelAsync(Guid branchId, Guid workOrderId, WorkOrderActionRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<PreventivePlanDto>> ListPlansAsync(Guid branchId, CancellationToken ct = default);
    Task<PreventivePlanDto> SavePlanAsync(Guid branchId, Guid? planId, SavePreventivePlanRequest request, CancellationToken ct = default);
    Task<GeneratePreventiveResultDto> GeneratePreventiveAsync(Guid branchId, GeneratePreventiveRequest request, CancellationToken ct = default);
}

internal sealed class MaintenanceService(
    IApplicationDbContext db,
    IBranchAccessService access,
    ICurrentUser user,
    IClock clock,
    INumberSequence numbers,
    IStockService stock,
    ILedgerService ledger,
    IRoomService rooms,
    Notifications.INotificationQueue notifications) : IMaintenanceService
{
    public async Task<IReadOnlyList<AssetDto>> ListAssetsAsync(Guid branchId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var assets = await db.Assets.AsNoTracking().Where(a => a.BranchId == branchId).OrderBy(a => a.Code).ToListAsync(ct);
        var costs = await CostsByAssetAsync(branchId, ct);
        return assets.Select(a => ToDto(a, costs.GetValueOrDefault(a.Id))).ToList();
    }

    public async Task<AssetDto> SaveAssetAsync(Guid branchId, Guid? assetId, SaveAssetRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.Code), request.Code, 30).Require(nameof(request.Name), request.Name, 200)
            .When(request.PurchaseCost < 0, nameof(request.PurchaseCost), "Cost cannot be negative.").ThrowIfInvalid();
        var status = EnumParser.Parse<AssetStatus>(request.Status, nameof(request.Status));
        if (request.RoomId is { } roomId) await db.Rooms.GetOwnedAsync(branchId, roomId, "Room", ct);
        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Assets.AnyAsync(a => a.BranchId == branchId && a.Code == code && a.Id != (assetId ?? Guid.Empty), ct))
            throw new ConflictException("ASSET_CODE_EXISTS", $"Asset code {code} is already used.");
        var asset = assetId is { } id ? await db.Assets.GetOwnedAsync(branchId, id, "Asset", ct) : null;
        if (asset is not null && request.ConcurrencyStamp is { } stamp) BranchOperations.EnsureStamp(asset, stamp, "asset");
        if (asset is null) db.Assets.Add(asset = new Asset { CompanyId = scope.CompanyId, BranchId = branchId });
        asset.Code = code;
        asset.Name = request.Name.Trim();
        asset.Category = Clean(request.Category);
        asset.RoomId = request.RoomId;
        asset.Location = Clean(request.Location);
        asset.SerialNumber = Clean(request.SerialNumber);
        asset.PurchaseDate = request.PurchaseDate;
        asset.PurchaseCost = request.PurchaseCost;
        asset.Status = status;
        await db.SaveChangesAsync(ct);
        return ToDto(asset, (await CostsByAssetAsync(branchId, ct)).GetValueOrDefault(asset.Id));
    }

    public async Task<AssetHistoryDto> AssetHistoryAsync(Guid branchId, Guid assetId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var asset = await db.Assets.AsNoTracking().GetOwnedAsync(branchId, assetId, "Asset", ct);
        var orders = await WorkOrdersQuery().Where(w => w.BranchId == branchId && w.AssetId == assetId).OrderByDescending(w => w.CreatedAtUtc).ToListAsync(ct);
        var dtos = await ToDtosAsync(orders, ct);
        var total = orders.Where(o => o.Status == WorkOrderStatus.Completed).Sum(o => o.LabourCost + o.PartsCost);
        var downtime = orders.Where(o => o.StartedAtUtc != null && o.CompletedAtUtc != null)
            .Sum(o => (decimal)(o.CompletedAtUtc!.Value - o.StartedAtUtc!.Value).TotalHours);
        return new AssetHistoryDto(ToDto(asset, total), dtos, total, Math.Round(downtime, 2));
    }

    public async Task<IReadOnlyList<MaintenanceRequestDto>> ListRequestsAsync(Guid branchId, bool openOnly, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.MaintenanceRequests.AsNoTracking().Where(r => r.BranchId == branchId);
        if (openOnly) q = q.Where(r => r.Status == MaintenanceRequestStatus.Open);
        return (await q.OrderByDescending(r => r.Priority).ThenBy(r => r.ReportedAtUtc).Take(300).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<MaintenanceRequestDto> ReportAsync(Guid branchId, CreateMaintenanceRequestRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var priority = EnumParser.Parse<MaintenancePriority>(request.Priority, nameof(request.Priority));
        await ValidateTargetsAsync(branchId, request.RoomId, request.AssetId, ct);
        var report = new MaintenanceRequest(scope.CompanyId, branchId, request.Title, Clean(request.Description), priority, request.RoomId, request.AssetId,
            scope.UserId, scope.UtcNow);
        db.MaintenanceRequests.Add(report);
        await db.SaveChangesAsync(ct);
        return ToDto(report);
    }

    public async Task<MaintenanceRequestDto> TriageAsync(Guid branchId, Guid requestId, TriageRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var report = await db.MaintenanceRequests.GetOwnedAsync(branchId, requestId, "Maintenance request", ct);
        BranchOperations.EnsureStamp(report, request.ConcurrencyStamp, "request");
        if (!request.Accept)
        {
            report.Reject(request.Reason ?? string.Empty);
            await db.SaveChangesAsync(ct);
            return ToDto(report);
        }

        var priority = request.Priority is null ? report.Priority : EnumParser.Parse<MaintenancePriority>(request.Priority, nameof(request.Priority));
        await CreateWorkOrderCoreAsync(scope, report.Title, report.Description, priority, report.RoomId, report.AssetId, report.Id, null, null,
            request.AssignToUserId, request.TakeRoomOutOfService, request.ExpectedReadyDate, wo => report.Convert(wo.Id), ct);
        return ToDto(await db.MaintenanceRequests.AsNoTracking().SingleAsync(r => r.Id == report.Id, ct));
    }

    public async Task<IReadOnlyList<WorkOrderDto>> ListWorkOrdersAsync(Guid branchId, bool openOnly, string? assignedTo, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = WorkOrdersQuery().Where(w => w.BranchId == branchId);
        if (openOnly) q = q.Where(w => w.Status != WorkOrderStatus.Completed && w.Status != WorkOrderStatus.Cancelled);
        if (!string.IsNullOrWhiteSpace(assignedTo)) q = q.Where(w => w.AssignedToUserId == assignedTo);
        return await ToDtosAsync(await q.OrderByDescending(w => w.Priority).ThenBy(w => w.DueDate).Take(300).ToListAsync(ct), ct);
    }

    public async Task<WorkOrderDto> GetWorkOrderAsync(Guid branchId, Guid workOrderId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var wo = await WorkOrdersQuery().SingleOrDefaultAsync(w => w.Id == workOrderId && w.BranchId == branchId, ct) ?? throw new NotFoundException("Work order", workOrderId);
        return (await ToDtosAsync([wo], ct))[0];
    }

    public async Task<WorkOrderDto> CreateWorkOrderAsync(Guid branchId, CreateWorkOrderRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var priority = EnumParser.Parse<MaintenancePriority>(request.Priority, nameof(request.Priority));
        await ValidateTargetsAsync(branchId, request.RoomId, request.AssetId, ct);
        var wo = await CreateWorkOrderCoreAsync(scope, request.Title, Clean(request.Description), priority, request.RoomId, request.AssetId, null, null,
            request.DueDate, request.AssignToUserId, request.TakeRoomOutOfService, request.ExpectedReadyDate, null, ct);
        return await GetWorkOrderAsync(branchId, wo.Id, ct);
    }

    public Task<WorkOrderDto> AssignAsync(Guid branchId, Guid workOrderId, AssignWorkOrderRequest request, CancellationToken ct = default) =>
        MutateAsync(branchId, workOrderId, request.ConcurrencyStamp, async (wo, scope) =>
        {
            await EnsureMemberAsync(scope, request.UserId, ct);
            wo.Assign(request.UserId);
            await NotifyAssignedAsync(scope, wo, ct);
        }, ct);

    public Task<WorkOrderDto> StartAsync(Guid branchId, Guid workOrderId, WorkOrderActionRequest request, CancellationToken ct = default) =>
        MutateAsync(branchId, workOrderId, request.ConcurrencyStamp, async (wo, scope) =>
        {
            wo.Start(scope.UserId, scope.UtcNow);
            if (wo.AssetId is { } assetId)
            {
                var asset = await db.Assets.SingleAsync(a => a.Id == assetId, ct);
                if (asset.Status == AssetStatus.InService) asset.Status = AssetStatus.UnderMaintenance;
            }
        }, ct);

    public async Task<WorkOrderDto> UsePartAsync(Guid branchId, Guid workOrderId, UsePartRequest request, string? idempotencyKey, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var key = InventoryService.RequireKey(idempotencyKey);
        if (request.Quantity <= 0) throw new ValidationAppException(nameof(request.Quantity), "Quantity must be positive.");
        if (await db.WorkOrderParts.AnyAsync(p => p.WorkOrderId == workOrderId && p.IdempotencyKey == key, ct))
            return await GetWorkOrderAsync(branchId, workOrderId, ct); // retried request

        var wo = await db.WorkOrders.GetOwnedAsync(branchId, workOrderId, "Work order", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var usage = new WorkOrderPartUsage
        {
            CompanyId = scope.CompanyId, BranchId = branchId, WorkOrderId = wo.Id, ItemId = request.ItemId, LocationId = request.LocationId,
            Quantity = request.Quantity, IdempotencyKey = key,
        };
        // Stock out, cost to the work order and the repairs expense, all in one transaction.
        var movement = await stock.RemoveAsync(scope, request.ItemId, request.LocationId, request.Quantity, StockMovementType.MaintenanceUse,
            "WorkOrderPart", usage.Id.ToString(), wo.WorkOrderNumber, ct);
        usage.UnitCost = movement.UnitCost;
        usage.TotalCost = -movement.TotalCost;
        wo.AddPartsCost(usage.TotalCost);
        db.WorkOrderParts.Add(usage);
        await ledger.PostAsync(scope, "WorkOrderPart", usage.Id.ToString(), $"Parts used on {wo.WorkOrderNumber}",
            [LedgerLine.Dr(AccountKeys.Repairs, usage.TotalCost), LedgerLine.Cr(AccountKeys.Inventory, usage.TotalCost)], ct);
        db.Touch(wo);
        await SaveOrConflictAsync(ct);
        await tx.CommitAsync(ct);
        return await GetWorkOrderAsync(branchId, workOrderId, ct);
    }

    public Task<WorkOrderDto> FinishAsync(Guid branchId, Guid workOrderId, FinishWorkRequest request, CancellationToken ct = default) =>
        MutateAsync(branchId, workOrderId, request.ConcurrencyStamp, (wo, _) =>
        {
            wo.FinishWork(request.LabourHours, request.LabourCost, Clean(request.Notes));
            return Task.CompletedTask;
        }, ct);

    public Task<WorkOrderDto> InspectAsync(Guid branchId, Guid workOrderId, InspectWorkRequest request, CancellationToken ct = default) =>
        MutateAsync(branchId, workOrderId, request.ConcurrencyStamp, async (wo, scope) =>
        {
            if (wo.AssignedToUserId == scope.UserId)
                throw new ForbiddenException(message: "Someone other than the technician must inspect and sign off the work.");
            wo.Inspect(request.Passed, scope.UserId, scope.UtcNow);
            if (wo.Status == WorkOrderStatus.Completed) await EndWorkAsync(scope, wo, ct);
        }, ct, transactional: true);

    public Task<WorkOrderDto> CancelAsync(Guid branchId, Guid workOrderId, WorkOrderActionRequest request, CancellationToken ct = default) =>
        MutateAsync(branchId, workOrderId, request.ConcurrencyStamp, async (wo, scope) =>
        {
            wo.Cancel();
            await EndWorkAsync(scope, wo, ct);
        }, ct, transactional: true);

    public async Task<IReadOnlyList<PreventivePlanDto>> ListPlansAsync(Guid branchId, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        return (await db.PreventivePlans.AsNoTracking().Where(p => p.BranchId == branchId).OrderBy(p => p.NextDueDate).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<PreventivePlanDto> SavePlanAsync(Guid branchId, Guid? planId, SavePreventivePlanRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        new Validation().Require(nameof(request.Title), request.Title, 200)
            .When(request.IntervalDays is < 1 or > 3660, nameof(request.IntervalDays), "Interval must be 1–3660 days.").ThrowIfInvalid();
        var priority = EnumParser.Parse<MaintenancePriority>(request.Priority, nameof(request.Priority));
        await ValidateTargetsAsync(branchId, request.RoomId, request.AssetId, ct);
        var plan = planId is { } id ? await db.PreventivePlans.GetOwnedAsync(branchId, id, "Preventive plan", ct) : null;
        if (plan is not null && request.ConcurrencyStamp is { } stamp) BranchOperations.EnsureStamp(plan, stamp, "plan");
        if (plan is null) db.PreventivePlans.Add(plan = new PreventiveMaintenancePlan { CompanyId = scope.CompanyId, BranchId = branchId });
        plan.Title = request.Title.Trim();
        plan.AssetId = request.AssetId;
        plan.RoomId = request.RoomId;
        plan.IntervalDays = request.IntervalDays;
        plan.NextDueDate = request.NextDueDate;
        plan.Priority = priority;
        plan.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return ToDto(plan);
    }

    public async Task<GeneratePreventiveResultDto> GeneratePreventiveAsync(Guid branchId, GeneratePreventiveRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        if (request.ThroughDate > scope.BusinessDate.AddDays(366))
            throw new ValidationAppException(nameof(request.ThroughDate), "Generate at most one year ahead.");
        var plans = await db.PreventivePlans.Where(p => p.BranchId == branchId && p.IsActive && p.NextDueDate <= request.ThroughDate).ToListAsync(ct);
        var created = new List<Guid>();
        foreach (var plan in plans)
        {
            while (plan.NextDueDate <= request.ThroughDate)
            {
                var due = plan.NextDueDate;
                // Unique (plan, due date): a second run, or two people running it at once, cannot duplicate work.
                if (!await db.PreventiveOccurrences.AnyAsync(o => o.PlanId == plan.Id && o.DueDate == due, ct))
                {
                    await using var tx = await db.Database.BeginTransactionAsync(ct);
                    var number = await numbers.NextAsync(scope.Branch, SequenceNames.WorkOrder, ct);
                    var wo = new WorkOrder(scope.CompanyId, branchId, number, plan.Title, $"Preventive maintenance due {due:yyyy-MM-dd}", plan.Priority,
                        plan.RoomId, plan.AssetId, null, plan.Id, due);
                    db.WorkOrders.Add(wo);
                    db.PreventiveOccurrences.Add(new PreventiveMaintenanceOccurrence { CompanyId = scope.CompanyId, BranchId = branchId, PlanId = plan.Id, DueDate = due, WorkOrderId = wo.Id });
                    plan.NextDueDate = due.AddDays(plan.IntervalDays);
                    try
                    {
                        await db.SaveChangesAsync(ct);
                        await tx.CommitAsync(ct);
                        created.Add(wo.Id);
                    }
                    catch (DbUpdateException)
                    {
                        await tx.RollbackAsync(ct);
                        db.ChangeTracker.Clear();
                        return new GeneratePreventiveResultDto(created.Count, created); // a concurrent run is generating; it will finish the rest
                    }
                }
                else
                {
                    plan.NextDueDate = due.AddDays(plan.IntervalDays);
                    await db.SaveChangesAsync(ct);
                }
            }
        }
        return new GeneratePreventiveResultDto(created.Count, created);
    }

    // ---------- helpers ----------

    private async Task<WorkOrder> CreateWorkOrderCoreAsync(BranchScope scope, string title, string? description, MaintenancePriority priority,
        Guid? roomId, Guid? assetId, Guid? requestId, Guid? planId, DateOnly? dueDate, string? assignTo, bool blockRoom, DateOnly? readyDate,
        Action<WorkOrder>? onCreated, CancellationToken ct)
    {
        Room? room = null;
        if (blockRoom)
        {
            if (roomId is null) throw new ValidationAppException("RoomId", "Choose the room to take out of service.");
            if (readyDate is null || readyDate <= scope.BusinessDate)
                throw new ValidationAppException("ExpectedReadyDate", "Give the date the room is expected back in service (after today).");
            room = await db.Rooms.GetOwnedAsync(scope.BranchId, roomId.Value, "Room", ct);
            await rooms.EnsureBlockRowsAsync(scope, room, scope.BusinessDate, readyDate.Value, ct);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var number = await numbers.NextAsync(scope.Branch, SequenceNames.WorkOrder, ct);
        var wo = new WorkOrder(scope.CompanyId, scope.BranchId, number, title, description, priority, roomId, assetId, requestId, planId, dueDate);
        if (!string.IsNullOrWhiteSpace(assignTo))
        {
            await EnsureMemberAsync(scope, assignTo, ct);
            wo.Assign(assignTo);
            await NotifyAssignedAsync(scope, wo, ct);
        }
        db.WorkOrders.Add(wo);
        onCreated?.Invoke(wo);
        if (room is not null)
        {
            var block = await rooms.AddBlockAsync(scope, room, scope.BusinessDate, readyDate!.Value, $"Out of service: {number} {title}", ct);
            wo.LinkBlock(block.Id);
        }
        await SaveOrConflictAsync(ct);
        await tx.CommitAsync(ct);
        return wo;
    }

    /// <summary>Returns the room to sale and the asset to service when work ends.</summary>
    private async Task EndWorkAsync(BranchScope scope, WorkOrder wo, CancellationToken ct)
    {
        if (wo.RoomBlockId is { } blockId)
        {
            var block = await db.RoomBlocks.Include(b => b.Room).SingleAsync(b => b.Id == blockId, ct);
            if (block.IsActive) await rooms.ReleaseBlockAsync(scope, block, ct);
        }
        if (wo.AssetId is { } assetId)
        {
            var asset = await db.Assets.SingleAsync(a => a.Id == assetId, ct);
            var otherOpen = await db.WorkOrders.AnyAsync(w => w.AssetId == assetId && w.Id != wo.Id
                && w.Status == WorkOrderStatus.InProgress, ct);
            if (asset.Status == AssetStatus.UnderMaintenance && !otherOpen) asset.Status = AssetStatus.InService;
        }
    }

    private async Task<WorkOrderDto> MutateAsync(Guid branchId, Guid workOrderId, Guid stamp, Func<WorkOrder, BranchScope, Task> action,
        CancellationToken ct, bool transactional = false)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var wo = await db.WorkOrders.GetOwnedAsync(branchId, workOrderId, "Work order", ct);
        BranchOperations.EnsureStamp(wo, stamp, "work order");
        await using var tx = transactional ? await db.Database.BeginTransactionAsync(ct) : null;
        await action(wo, scope);
        await SaveOrConflictAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return await GetWorkOrderAsync(branchId, workOrderId, ct);
    }

    private async Task EnsureMemberAsync(BranchScope scope, string userId, CancellationToken ct)
    {
        if (!await db.UserBranches.AnyAsync(m => m.UserId == userId && m.BranchId == scope.BranchId && m.IsActive, ct))
            throw new ValidationAppException("UserId", "The technician must be a staff member of this branch.");
    }

    private async Task NotifyAssignedAsync(BranchScope scope, WorkOrder wo, CancellationToken ct)
    {
        var roomNumber = wo.RoomId is { } roomId ? await db.Rooms.Where(r => r.Id == roomId).Select(r => r.RoomNumber).SingleOrDefaultAsync(ct) : null;
        await notifications.EnqueueInAppAsync(scope, NotificationEvents.MaintenanceAssigned, "WorkOrder", wo.Id.ToString(), "user:" + wo.AssignedToUserId,
            new Dictionary<string, string>
            {
                ["WorkOrderNumber"] = wo.WorkOrderNumber, ["Title"] = wo.Title, ["Priority"] = wo.Priority.ToString(),
                ["RoomText"] = roomNumber is null ? "" : $" in room {roomNumber}",
            }, ct, discriminator: wo.AssignedToUserId!);
    }

    private async Task SaveOrConflictAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(Contracts.Common.ErrorCodes.ConcurrencyConflict, "The work order or stock changed at the same time. Reload and try again.");
        }
    }

    private async Task ValidateTargetsAsync(Guid branchId, Guid? roomId, Guid? assetId, CancellationToken ct)
    {
        if (roomId is { } r) await db.Rooms.AsNoTracking().GetOwnedAsync(branchId, r, "Room", ct);
        if (assetId is { } a) await db.Assets.AsNoTracking().GetOwnedAsync(branchId, a, "Asset", ct);
    }

    private async Task<Dictionary<Guid, decimal>> CostsByAssetAsync(Guid branchId, CancellationToken ct) =>
        (await db.WorkOrders.AsNoTracking().Where(w => w.BranchId == branchId && w.AssetId != null && w.Status == WorkOrderStatus.Completed)
            .Select(w => new { AssetId = w.AssetId!.Value, w.LabourCost, w.PartsCost }).ToListAsync(ct))
        .GroupBy(w => w.AssetId).ToDictionary(g => g.Key, g => g.Sum(w => w.LabourCost + w.PartsCost));

    private IQueryable<WorkOrder> WorkOrdersQuery() => db.WorkOrders.AsNoTracking();

    private async Task<List<WorkOrderDto>> ToDtosAsync(List<WorkOrder> orders, CancellationToken ct)
    {
        var ids = orders.Select(o => o.Id).ToList();
        var parts = await db.WorkOrderParts.AsNoTracking().Where(p => ids.Contains(p.WorkOrderId)).ToListAsync(ct);
        var itemIds = parts.Select(p => p.ItemId).Distinct().ToList();
        var skus = await db.InventoryItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Sku, ct);
        var roomIds = orders.Where(o => o.RoomId != null).Select(o => o.RoomId!.Value).Distinct().ToList();
        var roomNumbers = await db.Rooms.AsNoTracking().Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.RoomNumber, ct);
        return orders.Select(w => new WorkOrderDto(w.Id, w.WorkOrderNumber, w.Title, w.Description, w.Priority.ToString(), w.RoomId,
            w.RoomId is { } rid ? roomNumbers.GetValueOrDefault(rid) : null, w.AssetId, w.RequestId, w.PreventivePlanId, w.DueDate, w.Status.ToString(),
            w.AssignedToUserId, w.RoomBlockId, w.StartedAtUtc, w.CompletedAtUtc, w.LabourHours, w.LabourCost, w.PartsCost, w.CompletionNotes,
            parts.Where(p => p.WorkOrderId == w.Id).Select(p => new PartUsageDto(p.Id, p.ItemId, skus.GetValueOrDefault(p.ItemId) ?? string.Empty,
                p.LocationId, p.Quantity, p.UnitCost, p.TotalCost)).ToList(), w.ConcurrencyStamp)).ToList();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AssetDto ToDto(Asset a, decimal cost) => new(a.Id, a.Code, a.Name, a.Category, a.RoomId, a.Location, a.SerialNumber, a.PurchaseDate,
        a.PurchaseCost, a.Status.ToString(), cost, a.ConcurrencyStamp);

    private static MaintenanceRequestDto ToDto(MaintenanceRequest r) => new(r.Id, r.Title, r.Description, r.Priority.ToString(), r.RoomId, r.AssetId,
        r.Status.ToString(), r.ReportedByUserId, r.ReportedAtUtc, r.WorkOrderId, r.RejectionReason, r.ConcurrencyStamp);

    private static PreventivePlanDto ToDto(PreventiveMaintenancePlan p) => new(p.Id, p.Title, p.AssetId, p.RoomId, p.IntervalDays, p.NextDueDate,
        p.Priority.ToString(), p.IsActive, p.ConcurrencyStamp);
}
