using HMS.Application.Abstractions;
using HMS.Application.Common;
using HMS.Application.Security;
using HMS.Contracts.Auth;
using HMS.Contracts.Branches;
using HMS.Contracts.Common;
using HMS.Contracts.Users;
using HMS.Domain.Entities;
using HMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HMS.Infrastructure.Identity;

internal sealed class IdentityService(
    UserManager<ApplicationUser> userManager,
    HmsDbContext db,
    TokenService tokens,
    ICurrentUser currentUser,
    IClock clock) : IIdentityService
{
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
            throw InvalidCredentials();

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null) throw InvalidCredentials();

        if (await userManager.IsLockedOutAsync(user))
            throw new UnauthenticatedException("ACCOUNT_LOCKED", "The account is temporarily locked. Try again later.");

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            throw InvalidCredentials();
        }

        if (!user.IsActive || !await db.Companies.AnyAsync(c => c.Id == user.CompanyId && c.IsActive, cancellationToken))
            throw InvalidCredentials();

        await userManager.ResetAccessFailedCountAsync(user);
        user.LastLoginAtUtc = clock.UtcNow;
        await userManager.UpdateAsync(user);

        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<LoginResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var hash = TokenService.Hash(request.RefreshToken ?? string.Empty);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (stored is null) throw InvalidRefresh();

        if (stored.RevokedAtUtc is not null)
        {
            // A rotated token was presented again: assume theft and revoke every active token for the user.
            await RevokeAllAsync(stored.UserId, now, cancellationToken);
            throw InvalidRefresh();
        }
        if (!stored.IsActive(now)) throw InvalidRefresh();

        var user = await userManager.FindByIdAsync(stored.UserId);
        if (user is null || !user.IsActive) throw InvalidRefresh();

        var response = await IssueTokensAsync(user, cancellationToken, rotate: stored);
        return response;
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken = default)
    {
        var hash = TokenService.Hash(request.RefreshToken ?? string.Empty);
        var stored = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash && t.UserId == currentUser.UserId, cancellationToken);
        if (stored is { RevokedAtUtc: null })
        {
            stored.RevokedAtUtc = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await RequireCurrentUserAsync();
        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword ?? string.Empty, request.NewPassword ?? string.Empty);
        ThrowIfFailed(result, nameof(request.NewPassword));

        user.MustChangePassword = false;
        await userManager.UpdateAsync(user);
        await RevokeAllAsync(user.Id, clock.UtcNow, cancellationToken);
        WriteAudit(user, "PasswordChanged", null);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CurrentUserDto> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var user = await RequireCurrentUserAsync();
        var roles = await userManager.GetRolesAsync(user);
        return ToCurrentUserDto(user, roles);
    }

    public async Task<PagedResult<UserDto>> ListUsersAsync(PageQuery query, CancellationToken cancellationToken = default)
    {
        var companyId = RequireCompany();
        var users = db.Users.AsNoTracking().Where(u => u.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            users = users.Where(u => u.FullName.Contains(s) || u.Email!.Contains(s));
        }

        var total = await users.CountAsync(cancellationToken);
        var page = await users.OrderBy(u => u.FullName).Skip(query.Skip).Take(query.PageSize).ToListAsync(cancellationToken);
        var items = new List<UserDto>(page.Count);
        foreach (var u in page) items.Add(await ToUserDtoAsync(u, cancellationToken));
        return new PagedResult<UserDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<UserDto> GetUserAsync(string userId, CancellationToken cancellationToken = default) =>
        await ToUserDtoAsync(await FindCompanyUserAsync(userId), cancellationToken);

    public async Task<UserDto> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        var companyId = RequireCompany();
        var validation = new Validation()
            .Require(nameof(request.Email), request.Email, 256)
            .Require(nameof(request.FullName), request.FullName)
            .Require(nameof(request.TemporaryPassword), request.TemporaryPassword, 128);
        ValidateRoles(validation, request.Roles);
        validation.ThrowIfInvalid();

        await ValidateBranchesAsync(companyId, request.BranchIds, request.DefaultBranchId, cancellationToken);

        if (await userManager.FindByEmailAsync(request.Email.Trim()) is not null)
            throw new ConflictException("USER_EMAIL_EXISTS", "A user with this email already exists.");

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            EmailConfirmed = true,
            FullName = request.FullName.Trim(),
            CompanyId = companyId,
            MustChangePassword = true,
            CreatedAtUtc = clock.UtcNow,
            LockoutEnabled = true,
        };
        ThrowIfFailed(await userManager.CreateAsync(user, request.TemporaryPassword), nameof(request.TemporaryPassword));
        ThrowIfFailed(await userManager.AddToRolesAsync(user, request.Roles.Distinct()), nameof(request.Roles));
        SyncBranches(user, companyId, request.BranchIds, request.DefaultBranchId, existing: []);
        WriteAudit(user, "UserCreated", string.Join(",", request.Roles));
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return await ToUserDtoAsync(user, cancellationToken);
    }

    public async Task<UserDto> UpdateUserAccessAsync(string userId, UpdateUserAccessRequest request, CancellationToken cancellationToken = default)
    {
        var companyId = RequireCompany();
        var validation = new Validation();
        ValidateRoles(validation, request.Roles);
        validation.When(userId == currentUser.UserId && !request.IsActive, nameof(request.IsActive), "You cannot deactivate your own account.");
        validation.When(userId == currentUser.UserId && !request.Roles.Contains(Roles.SystemAdmin) && currentUser.IsInRole(Roles.SystemAdmin),
            nameof(request.Roles), "You cannot remove your own SystemAdmin role.");
        validation.ThrowIfInvalid();
        await ValidateBranchesAsync(companyId, request.BranchIds, request.DefaultBranchId, cancellationToken);

        var user = await FindCompanyUserAsync(userId);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var currentRoles = await userManager.GetRolesAsync(user);
        var toRemove = currentRoles.Except(request.Roles).ToArray();
        var toAdd = request.Roles.Distinct().Except(currentRoles).ToArray();
        if (toRemove.Length > 0) ThrowIfFailed(await userManager.RemoveFromRolesAsync(user, toRemove), nameof(request.Roles));
        if (toAdd.Length > 0) ThrowIfFailed(await userManager.AddToRolesAsync(user, toAdd), nameof(request.Roles));

        var wasActive = user.IsActive;
        user.IsActive = request.IsActive;
        if (toRemove.Length > 0 || toAdd.Length > 0 || wasActive != user.IsActive)
        {
            // Invalidate outstanding tokens so changed access takes effect immediately.
            await userManager.UpdateSecurityStampAsync(user);
            await RevokeAllAsync(user.Id, clock.UtcNow, cancellationToken);
        }
        else
        {
            await userManager.UpdateAsync(user);
        }

        var existing = await db.UserBranches.Where(m => m.UserId == user.Id).ToListAsync(cancellationToken);
        SyncBranches(user, companyId, request.BranchIds, request.DefaultBranchId, existing);
        WriteAudit(user, "UserAccessChanged", $"roles={string.Join(",", request.Roles)};active={request.IsActive}");
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return await ToUserDtoAsync(user, cancellationToken);
    }

    private async Task<LoginResponse> IssueTokensAsync(ApplicationUser user, CancellationToken cancellationToken, RefreshToken? rotate = null)
    {
        var now = clock.UtcNow;
        var roles = await userManager.GetRolesAsync(user);
        var (access, accessExpires) = tokens.CreateAccessToken(user, roles, now);
        var (refresh, refreshHash, refreshExpires) = tokens.CreateRefreshToken(now);

        if (rotate is not null)
        {
            rotate.RevokedAtUtc = now;
            rotate.ReplacedByTokenHash = refreshHash;
        }
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = refreshExpires,
        });
        await db.SaveChangesAsync(cancellationToken);

        var branches = await GetBranchesForUserAsync(user, roles, cancellationToken);
        return new LoginResponse(access, accessExpires, refresh, refreshExpires, ToCurrentUserDto(user, roles), branches);
    }

    private async Task<IReadOnlyList<BranchSummaryDto>> GetBranchesForUserAsync(ApplicationUser user, IList<string> roles, CancellationToken ct)
    {
        var memberships = await db.UserBranches.Where(m => m.UserId == user.Id && m.IsActive).ToListAsync(ct);
        var query = db.Branches.Where(b => b.CompanyId == user.CompanyId && b.IsActive);
        if (!roles.Contains(Roles.SystemAdmin))
        {
            var ids = memberships.Select(m => m.BranchId).ToList();
            query = query.Where(b => ids.Contains(b.Id));
        }
        var branches = await query.OrderBy(b => b.Name).ToListAsync(ct);
        return branches
            .Select(b => new BranchSummaryDto(b.Id, b.Code, b.Name, b.TimeZoneId, memberships.Any(m => m.BranchId == b.Id && m.IsDefault)))
            .ToList();
    }

    private async Task RevokeAllAsync(string userId, DateTime now, CancellationToken ct)
    {
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var t in active) t.RevokedAtUtc = now;
        await db.SaveChangesAsync(ct);
    }

    private void SyncBranches(ApplicationUser user, Guid companyId, IReadOnlyList<Guid> branchIds, Guid? defaultBranchId, List<HotelUserBranch> existing)
    {
        var wanted = branchIds.Distinct().ToHashSet();
        foreach (var m in existing)
        {
            if (wanted.Contains(m.BranchId)) m.Restore(); else m.Revoke();
            m.IsDefault = m.BranchId == defaultBranchId && wanted.Contains(m.BranchId);
        }
        foreach (var id in wanted.Where(id => existing.All(m => m.BranchId != id)))
            db.UserBranches.Add(new HotelUserBranch(user.Id, companyId, id, id == defaultBranchId));
    }

    private async Task ValidateBranchesAsync(Guid companyId, IReadOnlyList<Guid> branchIds, Guid? defaultBranchId, CancellationToken ct)
    {
        var ids = branchIds.Distinct().ToList();
        var found = await db.Branches.CountAsync(b => ids.Contains(b.Id) && b.CompanyId == companyId, ct);
        var validation = new Validation()
            .When(found != ids.Count, "BranchIds", "One or more branches do not exist in your company.")
            .When(defaultBranchId is { } d && !ids.Contains(d), "DefaultBranchId", "Default branch must be one of the assigned branches.");
        validation.ThrowIfInvalid();
    }

    private static void ValidateRoles(Validation validation, IReadOnlyList<string>? roles)
    {
        if (roles is null || roles.Count == 0) { validation.Add("Roles", "At least one role is required."); return; }
        foreach (var r in roles.Where(r => !Roles.All.Contains(r)))
            validation.Add("Roles", $"Unknown role '{r}'.");
    }

    private void WriteAudit(ApplicationUser user, string action, string? details) =>
        db.AuditLogs.Add(new AuditLog
        {
            OccurredAtUtc = clock.UtcNow,
            CompanyId = user.CompanyId,
            UserId = currentUser.UserId ?? user.Id,
            Action = action,
            EntityType = nameof(ApplicationUser),
            EntityId = user.Id,
            Details = details,
            TraceId = currentUser.TraceId,
        });

    private async Task<ApplicationUser> FindCompanyUserAsync(string userId)
    {
        var companyId = RequireCompany();
        var user = await userManager.FindByIdAsync(userId);
        if (user is null || user.CompanyId != companyId) throw new NotFoundException("User", userId);
        return user;
    }

    private async Task<ApplicationUser> RequireCurrentUserAsync()
    {
        if (currentUser.UserId is null) throw new UnauthenticatedException();
        return await userManager.FindByIdAsync(currentUser.UserId) ?? throw new UnauthenticatedException();
    }

    private Guid RequireCompany() => currentUser.CompanyId ?? throw new UnauthenticatedException();

    private async Task<UserDto> ToUserDtoAsync(ApplicationUser user, CancellationToken ct)
    {
        var roles = await userManager.GetRolesAsync(user);
        var branches = await db.UserBranches.AsNoTracking()
            .Where(m => m.UserId == user.Id && m.IsActive)
            .Select(m => new UserBranchDto(m.BranchId, m.Branch!.Code, m.IsDefault))
            .ToListAsync(ct);
        return new UserDto(user.Id, user.Email ?? string.Empty, user.FullName, user.IsActive, user.MustChangePassword,
            roles.Order().ToList(), branches);
    }

    private static CurrentUserDto ToCurrentUserDto(ApplicationUser user, IList<string> roles) =>
        new(user.Id, user.Email ?? string.Empty, user.FullName, user.CompanyId, roles.Order().ToList(),
            RolePermissions.ForRoles(roles), user.MustChangePassword);

    private static void ThrowIfFailed(IdentityResult result, string field)
    {
        if (!result.Succeeded)
            throw new ValidationAppException(new Dictionary<string, string[]> { [field] = result.Errors.Select(e => e.Description).ToArray() });
    }

    private static UnauthenticatedException InvalidCredentials() =>
        new(ErrorCodes.InvalidCredentials, "Email or password is incorrect.");

    private static UnauthenticatedException InvalidRefresh() =>
        new(ErrorCodes.InvalidRefreshToken, "The session has expired. Please sign in again.");
}
