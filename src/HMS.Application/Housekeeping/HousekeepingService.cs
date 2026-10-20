using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Application.Security;
using HMS.Contracts.Housekeeping;
using HMS.Domain.Entities;
using HMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HMS.Application.Housekeeping;

public interface IHousekeepingService
{
    Task<IReadOnlyList<HousekeepingTaskDto>> ListAsync(Guid branchId, string? status, bool openOnly, CancellationToken ct = default);
    Task<HousekeepingTaskDto> CreateAsync(Guid branchId, CreateHousekeepingTaskRequest request, CancellationToken ct = default);
    Task<HousekeepingTaskDto> AssignAsync(Guid branchId, Guid taskId, AssignHousekeepingTaskRequest request, CancellationToken ct = default);
    Task<HousekeepingTaskDto> StartAsync(Guid branchId, Guid taskId, HousekeepingActionRequest request, CancellationToken ct = default);
    Task<HousekeepingTaskDto> CompleteAsync(Guid branchId, Guid taskId, HousekeepingActionRequest request, CancellationToken ct = default);
    Task<HousekeepingTaskDto> InspectAsync(Guid branchId, Guid taskId, InspectionRequest request, CancellationToken ct = default);
    Task<HousekeepingTaskDto> CancelAsync(Guid branchId, Guid taskId, HousekeepingActionRequest request, CancellationToken ct = default);
}

internal sealed class HousekeepingService(IApplicationDbContext db, IBranchAccessService access, ICurrentUser user, IClock clock) : IHousekeepingService
{
    public async Task<IReadOnlyList<HousekeepingTaskDto>> ListAsync(Guid branchId, string? status, bool openOnly, CancellationToken ct = default)
    {
        await access.EnsureAccessAsync(branchId, cancellationToken: ct);
        var q = db.HousekeepingTasks.AsNoTracking().Include(t => t.Room).Where(t => t.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = EnumParser.Parse<HousekeepingTaskStatus>(status, nameof(status));
            q = q.Where(t => t.Status == parsed);
        }
        else if (openOnly)
        {
            q = q.Where(t => t.Status != HousekeepingTaskStatus.Completed && t.Status != HousekeepingTaskStatus.Cancelled);
        }
        var tasks = await q.OrderBy(t => t.Status).ThenBy(t => t.Room!.RoomNumber).Take(500).ToListAsync(ct);
        return tasks.Select(ToDto).ToList();
    }

    public async Task<HousekeepingTaskDto> CreateAsync(Guid branchId, CreateHousekeepingTaskRequest request, CancellationToken ct = default)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var type = EnumParser.Parse<HousekeepingTaskType>(request.Type, nameof(request.Type));
        var room = await db.Rooms.GetOwnedAsync(branchId, request.RoomId, "Room", ct);
        if (await db.HousekeepingTasks.AnyAsync(t => t.RoomId == room.Id && t.Type == type
                && t.Status != HousekeepingTaskStatus.Completed && t.Status != HousekeepingTaskStatus.Cancelled, ct))
            throw new ConflictException("TASK_EXISTS", $"Room {room.RoomNumber} already has an open {type} task.");

        var task = new HousekeepingTask(scope.CompanyId, branchId, room.Id, type, "Manual", Guid.NewGuid().ToString(), scope.BusinessDate)
        {
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
        };
        room.MarkDirty();
        db.HousekeepingTasks.Add(task);
        await db.SaveChangesAsync(ct);
        return await GetAsync(task.Id, ct);
    }

    public Task<HousekeepingTaskDto> AssignAsync(Guid branchId, Guid taskId, AssignHousekeepingTaskRequest request, CancellationToken ct = default) =>
        MutateAsync(branchId, taskId, request.ConcurrencyStamp, (task, _, _) =>
        {
            if (string.IsNullOrWhiteSpace(request.UserId)) throw new ValidationAppException(nameof(request.UserId), "Select a housekeeper.");
            task.Assign(request.UserId);
            return Task.CompletedTask;
        }, ct);

    public Task<HousekeepingTaskDto> StartAsync(Guid branchId, Guid taskId, HousekeepingActionRequest request, CancellationToken ct = default) =>
        MutateAsync(branchId, taskId, request.ConcurrencyStamp, (task, scope, _) =>
        {
            task.Start(scope.UserId, scope.UtcNow);
            return Task.CompletedTask;
        }, ct);

    public Task<HousekeepingTaskDto> CompleteAsync(Guid branchId, Guid taskId, HousekeepingActionRequest request, CancellationToken ct = default) =>
        MutateAsync(branchId, taskId, request.ConcurrencyStamp, (task, scope, room) =>
        {
            if (!string.IsNullOrWhiteSpace(request.Notes)) task.Notes = request.Notes.Trim();
            if (task.FinishCleaning(scope.UtcNow, scope.Branch.HousekeepingRequiresInspection)) room.MarkClean();
            return Task.CompletedTask;
        }, ct);

    public Task<HousekeepingTaskDto> InspectAsync(Guid branchId, Guid taskId, InspectionRequest request, CancellationToken ct = default) =>
        MutateAsync(branchId, taskId, request.ConcurrencyStamp, (task, scope, room) =>
        {
            if (!RolePermissions.HasPermission(user.Roles, Permissions.HousekeepingInspect))
                throw new ForbiddenException(message: "Only an authorised inspector can pass or fail a room.");
            if (request.Passed)
            {
                task.PassInspection(scope.UserId, scope.UtcNow);
                room.MarkClean(); // only an authorised inspection makes the room VacantClean
            }
            else
            {
                task.FailInspection(scope.UserId, request.Reason ?? string.Empty, scope.UtcNow);
            }
            return Task.CompletedTask;
        }, ct);

    public Task<HousekeepingTaskDto> CancelAsync(Guid branchId, Guid taskId, HousekeepingActionRequest request, CancellationToken ct = default) =>
        MutateAsync(branchId, taskId, request.ConcurrencyStamp, (task, _, _) =>
        {
            if (task.Type == HousekeepingTaskType.Checkout)
                throw new ConflictException("TASK_REQUIRED", "Check-out cleaning cannot be cancelled; the room must be cleaned before it is sold again.");
            task.Cancel();
            return Task.CompletedTask;
        }, ct);

    private async Task<HousekeepingTaskDto> MutateAsync(Guid branchId, Guid taskId, Guid stamp,
        Func<HousekeepingTask, BranchScope, Room, Task> action, CancellationToken ct)
    {
        var scope = await access.ScopeAsync(user, clock, branchId, ct);
        var task = await db.HousekeepingTasks.GetOwnedAsync(branchId, taskId, "Housekeeping task", ct);
        BranchOperations.EnsureStamp(task, stamp, "task");
        var room = await db.Rooms.SingleAsync(r => r.Id == task.RoomId, ct);
        await action(task, scope, room);
        await db.SaveChangesAsync(ct);
        return await GetAsync(task.Id, ct);
    }

    private async Task<HousekeepingTaskDto> GetAsync(Guid id, CancellationToken ct) =>
        ToDto(await db.HousekeepingTasks.AsNoTracking().Include(t => t.Room).SingleAsync(t => t.Id == id, ct));

    private static HousekeepingTaskDto ToDto(HousekeepingTask t) => new(t.Id, t.RoomId, t.Room?.RoomNumber ?? string.Empty,
        t.Room?.Status.ToString() ?? string.Empty, t.Type.ToString(), t.Status.ToString(), t.BusinessDate, t.AssignedToUserId,
        t.StartedAtUtc, t.CompletedAtUtc, t.RejectionReason, t.Notes, t.ConcurrencyStamp);
}
