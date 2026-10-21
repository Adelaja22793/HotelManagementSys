using HMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HMS.Infrastructure.Persistence.Configurations;

// ---------- business day and analytics ----------

internal sealed class BusinessDayCloseConfiguration : IEntityTypeConfiguration<BusinessDayClose>
{
    public void Configure(EntityTypeBuilder<BusinessDayClose> b)
    {
        b.ToTable("BusinessDayCloses");
        b.Property(x => x.StartedByUserId).IsRequired();
        b.HasIndex(x => new { x.BranchId, x.BusinessDate }).IsUnique();
        b.HasMany(x => x.Exceptions).WithOne().HasForeignKey(x => x.CloseId);
        b.Navigation(x => x.Exceptions).UsePropertyAccessMode(PropertyAccessMode.Property);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class BusinessDayExceptionConfiguration : IEntityTypeConfiguration<BusinessDayException>
{
    public void Configure(EntityTypeBuilder<BusinessDayException> b)
    {
        b.ToTable("BusinessDayExceptions");
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Reference).HasMaxLength(100).IsRequired();
        b.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        b.Property(x => x.AcceptanceReason).HasMaxLength(500);
        b.HasIndex(x => new { x.CloseId, x.Code, x.Reference }).IsUnique();
    }
}

internal sealed class BranchDailyMetricConfiguration : IEntityTypeConfiguration<BranchDailyMetric>
{
    public void Configure(EntityTypeBuilder<BranchDailyMetric> b)
    {
        b.ToTable("BranchDailyHotelMetrics");
        b.HasIndex(x => new { x.BranchId, x.Date }).IsUnique();
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class BranchDailyDepartmentMetricConfiguration : IEntityTypeConfiguration<BranchDailyDepartmentMetric>
{
    public void Configure(EntityTypeBuilder<BranchDailyDepartmentMetric> b)
    {
        b.ToTable("BranchDailyDepartmentMetrics");
        b.Property(x => x.Department).HasMaxLength(50).IsRequired();
        b.HasIndex(x => new { x.BranchId, x.Date, x.Department }).IsUnique();
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class AnalyticsRefreshRunConfiguration : IEntityTypeConfiguration<AnalyticsRefreshRun>
{
    public void Configure(EntityTypeBuilder<AnalyticsRefreshRun> b)
    {
        b.ToTable("AnalyticsRefreshRuns");
        b.HasIndex(x => new { x.BranchId, x.StartedAtUtc });
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

// ---------- notifications ----------

internal sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> b)
    {
        b.ToTable("NotificationTemplates");
        b.Property(x => x.EventType).HasMaxLength(50).IsRequired();
        b.Property(x => x.Language).HasMaxLength(10).IsRequired();
        b.Property(x => x.Subject).HasMaxLength(200);
        b.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.BranchId, x.EventType, x.Channel, x.Language, x.Version }).IsUnique();
    }
}

internal sealed class NotificationOutboxConfiguration : IEntityTypeConfiguration<NotificationOutbox>
{
    public void Configure(EntityTypeBuilder<NotificationOutbox> b)
    {
        b.ToTable("NotificationOutbox");
        b.Property(x => x.EventType).HasMaxLength(50).IsRequired();
        b.Property(x => x.SourceType).HasMaxLength(50).IsRequired();
        b.Property(x => x.SourceId).HasMaxLength(100).IsRequired();
        b.Property(x => x.Recipient).HasMaxLength(320).IsRequired();
        b.Property(x => x.Subject).HasMaxLength(200);
        b.Property(x => x.Body).HasMaxLength(20000).IsRequired();
        b.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.LastError).HasMaxLength(500);
        // A worker that lost its lease must not overwrite the new owner's result.
        b.Property(x => x.LeaseId).IsConcurrencyToken();
        b.HasIndex(x => new { x.BranchId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
        b.HasIndex(x => new { x.BranchId, x.Channel, x.Recipient, x.CreatedAtUtc });
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> b)
    {
        b.ToTable("NotificationDeliveries");
        b.Property(x => x.Provider).HasMaxLength(50).IsRequired();
        b.Property(x => x.ProviderMessageId).HasMaxLength(200);
        b.Property(x => x.Error).HasMaxLength(500);
        b.HasIndex(x => new { x.OutboxId, x.Attempt });
        b.HasOne<NotificationOutbox>().WithMany().HasForeignKey(x => x.OutboxId);
    }
}

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> b)
    {
        b.ToTable("NotificationPreferences");
        b.Property(x => x.EventType).HasMaxLength(50).IsRequired();
        b.HasIndex(x => new { x.BranchId, x.UserId, x.GuestId, x.EventType, x.Channel }).IsUnique();
    }
}

internal sealed class NotificationReadConfiguration : IEntityTypeConfiguration<NotificationRead>
{
    public void Configure(EntityTypeBuilder<NotificationRead> b)
    {
        b.ToTable("NotificationReads");
        b.HasKey(x => new { x.OutboxId, x.UserId });
        b.HasOne<NotificationOutbox>().WithMany().HasForeignKey(x => x.OutboxId);
    }
}

// ---------- documents ----------

internal sealed class DocumentRecordConfiguration : IEntityTypeConfiguration<DocumentRecord>
{
    public void Configure(EntityTypeBuilder<DocumentRecord> b)
    {
        b.ToTable("DocumentRecords");
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(50);
        b.HasIndex(x => new { x.BranchId, x.EntityType, x.EntityId });
        b.HasMany(x => x.Versions).WithOne().HasForeignKey(x => x.DocumentId);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> b)
    {
        b.ToTable("DocumentVersions");
        b.Property(x => x.OriginalFileName).HasMaxLength(200).IsRequired();
        b.Property(x => x.StorageKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.ScanDetail).HasMaxLength(300);
        b.Property(x => x.ApprovalNote).HasMaxLength(500);
        b.HasIndex(x => new { x.DocumentId, x.VersionNumber }).IsUnique();
        b.HasIndex(x => x.StorageKey).IsUnique();
        b.HasIndex(x => x.ScanStatus);
    }
}

internal sealed class DocumentSignatureConfiguration : IEntityTypeConfiguration<DocumentSignature>
{
    public void Configure(EntityTypeBuilder<DocumentSignature> b)
    {
        b.ToTable("DocumentSignatures");
        b.Property(x => x.SignerName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Purpose).HasMaxLength(300).IsRequired();
        b.Property(x => x.ContentSha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.SignatureHash).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.DocumentId);
        b.HasOne<DocumentVersion>().WithMany().HasForeignKey(x => x.VersionId);
    }
}

// ---------- security, lost and found, complaints ----------

internal sealed class SecurityIncidentConfiguration : IEntityTypeConfiguration<SecurityIncident>
{
    public void Configure(EntityTypeBuilder<SecurityIncident> b)
    {
        b.ToTable("SecurityIncidents");
        b.Property(x => x.IncidentNumber).HasMaxLength(40).IsRequired();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Description).HasMaxLength(4000).IsRequired();
        b.Property(x => x.Location).HasMaxLength(200);
        b.Property(x => x.Resolution).HasMaxLength(2000);
        b.HasIndex(x => x.IncidentNumber).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.Status });
        b.HasMany(x => x.Actions).WithOne().HasForeignKey(x => x.IncidentId);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class IncidentActionConfiguration : IEntityTypeConfiguration<IncidentAction>
{
    public void Configure(EntityTypeBuilder<IncidentAction> b)
    {
        b.ToTable("IncidentActions");
        b.Property(x => x.Action).HasMaxLength(50).IsRequired();
        b.Property(x => x.Note).HasMaxLength(2000);
    }
}

internal sealed class LostFoundItemConfiguration : IEntityTypeConfiguration<LostFoundItem>
{
    public void Configure(EntityTypeBuilder<LostFoundItem> b)
    {
        b.ToTable("LostFoundItems");
        b.Property(x => x.ItemNumber).HasMaxLength(40).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500).IsRequired();
        b.Property(x => x.Category).HasMaxLength(100);
        b.Property(x => x.FoundLocation).HasMaxLength(200).IsRequired();
        b.Property(x => x.StorageLocation).HasMaxLength(200).IsRequired();
        b.HasIndex(x => x.ItemNumber).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.Status });
        b.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.ItemId);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class LostFoundCustodyEventConfiguration : IEntityTypeConfiguration<LostFoundCustodyEvent>
{
    public void Configure(EntityTypeBuilder<LostFoundCustodyEvent> b)
    {
        b.ToTable("LostFoundCustodyEvents");
        b.Property(x => x.Note).HasMaxLength(500);
        b.Property(x => x.Counterparty).HasMaxLength(200);
    }
}

internal sealed class VisitorLogConfiguration : IEntityTypeConfiguration<VisitorLog>
{
    public void Configure(EntityTypeBuilder<VisitorLog> b)
    {
        b.ToTable("VisitorLogs");
        b.Property(x => x.VisitorName).HasMaxLength(200).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(50);
        b.Property(x => x.IdTypeChecked).HasMaxLength(50);
        b.Property(x => x.Purpose).HasMaxLength(300).IsRequired();
        b.HasIndex(x => new { x.BranchId, x.ArrivedAtUtc });
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class GuestComplaintConfiguration : IEntityTypeConfiguration<GuestComplaint>
{
    public void Configure(EntityTypeBuilder<GuestComplaint> b)
    {
        b.ToTable("GuestComplaints");
        b.Property(x => x.ComplaintNumber).HasMaxLength(40).IsRequired();
        b.Property(x => x.Category).HasMaxLength(100).IsRequired();
        b.Property(x => x.Description).HasMaxLength(4000).IsRequired();
        b.Property(x => x.Resolution).HasMaxLength(2000);
        b.HasIndex(x => x.ComplaintNumber).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.BusinessDate });
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class EmergencyEventConfiguration : IEntityTypeConfiguration<EmergencyEvent>
{
    public void Configure(EntityTypeBuilder<EmergencyEvent> b)
    {
        b.ToTable("EmergencyEvents");
        b.Property(x => x.Description).HasMaxLength(2000).IsRequired();
        b.Property(x => x.Summary).HasMaxLength(2000);
        b.HasMany(x => x.Actions).WithOne().HasForeignKey(x => x.EmergencyId);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class EmergencyActionConfiguration : IEntityTypeConfiguration<EmergencyAction>
{
    public void Configure(EntityTypeBuilder<EmergencyAction> b)
    {
        b.ToTable("EmergencyActions");
        b.Property(x => x.Action).HasMaxLength(1000).IsRequired();
    }
}
