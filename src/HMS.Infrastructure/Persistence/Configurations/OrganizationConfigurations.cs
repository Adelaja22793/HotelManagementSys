using HMS.Domain.Entities;
using HMS.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HMS.Infrastructure.Persistence.Configurations;

internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> b)
    {
        b.ToTable("Companies");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.DefaultCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.HasMany(x => x.Branches).WithOne(x => x.Company).HasForeignKey(x => x.CompanyId);
    }
}

internal sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> b)
    {
        b.ToTable("Branches");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.AddressLine).HasMaxLength(300);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.Phone).HasMaxLength(40);
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
    }
}

internal sealed class HotelUserBranchConfiguration : IEntityTypeConfiguration<HotelUserBranch>
{
    public void Configure(EntityTypeBuilder<HotelUserBranch> b)
    {
        b.ToTable("HotelUserBranches");
        b.HasIndex(x => new { x.UserId, x.BranchId }).IsUnique();
        b.HasIndex(x => x.BranchId);
        b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId);
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.Property(x => x.Action).HasMaxLength(50).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(100).IsRequired();
        b.Property(x => x.ChangedProperties).HasMaxLength(4000);
        b.Property(x => x.Details).HasMaxLength(4000);
        b.Property(x => x.TraceId).HasMaxLength(100);
        b.HasIndex(x => new { x.BranchId, x.OccurredAtUtc });
        b.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.CompanyId);
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens");
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.UserId);
        b.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
