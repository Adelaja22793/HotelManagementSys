using HMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HMS.Infrastructure.Persistence.Configurations;

internal sealed class BookingHoldConfiguration : IEntityTypeConfiguration<BookingHold>
{
    public void Configure(EntityTypeBuilder<BookingHold> b)
    {
        b.ToTable("BookingHolds", t =>
        {
            t.HasCheckConstraint("CK_BookingHolds_Dates", "[DepartureDate] > [ArrivalDate]");
            t.HasCheckConstraint("CK_BookingHolds_Guests", "[Adults] >= 1 AND [Children] >= 0");
        });
        b.Property(x => x.Reference).HasMaxLength(20).IsRequired();
        b.Property(x => x.AccessTokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.ClientFingerprint).HasMaxLength(64);
        b.Property(x => x.GuestFirstName).HasMaxLength(100);
        b.Property(x => x.GuestLastName).HasMaxLength(100);
        b.Property(x => x.GuestEmail).HasMaxLength(254);
        b.Property(x => x.GuestPhone).HasMaxLength(40);
        b.Property(x => x.GuestCountry).HasMaxLength(60);
        b.Property(x => x.SpecialRequests).HasMaxLength(500);
        b.Property(x => x.CancellationReason).HasMaxLength(500);
        b.Property(x => x.ReviewReason).HasMaxLength(300);
        b.Ignore(x => x.TotalAmount);
        b.Ignore(x => x.DepositAmount);
        b.Ignore(x => x.RoomCount);
        b.Ignore(x => x.HasGuestDetails);
        b.HasIndex(x => x.Reference).IsUnique();
        b.HasIndex(x => new { x.Status, x.ExpiresAtUtc });
        b.HasIndex(x => new { x.ClientFingerprint, x.Status });
        b.HasMany(x => x.Rooms).WithOne().HasForeignKey(x => x.HoldId);
        b.Navigation(x => x.Rooms).UsePropertyAccessMode(PropertyAccessMode.Property);
        b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
    }
}

internal sealed class BookingHoldRoomConfiguration : IEntityTypeConfiguration<BookingHoldRoom>
{
    public void Configure(EntityTypeBuilder<BookingHoldRoom> b)
    {
        b.ToTable("BookingHoldRooms", t => t.HasCheckConstraint("CK_BookingHoldRooms_Quantity", "[Quantity] BETWEEN 1 AND 4"));
        b.Property(x => x.QuoteJson).HasMaxLength(20000).IsRequired();
        b.HasOne(x => x.RoomType).WithMany().HasForeignKey(x => x.RoomTypeId);
        b.HasOne<RatePlan>().WithMany().HasForeignKey(x => x.RatePlanId);
        b.HasIndex(x => new { x.HoldId, x.RoomTypeId }).IsUnique();
    }
}

internal sealed class BookingPaymentAttemptConfiguration : IEntityTypeConfiguration<BookingPaymentAttempt>
{
    public void Configure(EntityTypeBuilder<BookingPaymentAttempt> b)
    {
        b.ToTable("BookingPaymentAttempts", t => t.HasCheckConstraint("CK_BookingPaymentAttempts_Amount", "[Amount] > 0"));
        b.Property(x => x.Provider).HasMaxLength(30).IsRequired();
        b.Property(x => x.MerchantReference).HasMaxLength(60).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        b.Property(x => x.ReportedCurrency).HasMaxLength(3);
        b.Property(x => x.AuthorizationUrl).HasMaxLength(1000);
        b.Property(x => x.ProviderTransactionId).HasMaxLength(100);
        b.Property(x => x.ProviderStatus).HasMaxLength(50);
        b.Property(x => x.FailureReason).HasMaxLength(300);
        b.Property(x => x.ResolutionNote).HasMaxLength(500);
        b.Ignore(x => x.IsFinal);
        b.HasIndex(x => x.MerchantReference).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.Status });
        b.HasOne<BookingHold>().WithMany().HasForeignKey(x => x.HoldId);
    }
}

internal sealed class BookingChannelConfiguration : IEntityTypeConfiguration<BookingChannel>
{
    public void Configure(EntityTypeBuilder<BookingChannel> b)
    {
        b.ToTable("BookingChannels");
        b.Property(x => x.Code).HasMaxLength(20).IsRequired();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        b.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId);
    }
}

internal sealed class ChannelReservationMappingConfiguration : IEntityTypeConfiguration<ChannelReservationMapping>
{
    public void Configure(EntityTypeBuilder<ChannelReservationMapping> b)
    {
        b.ToTable("ChannelReservationMappings");
        b.Property(x => x.ExternalReference).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.ReservationId).IsUnique();
        b.HasIndex(x => new { x.BranchId, x.ChannelId, x.ExternalReference });
        b.HasOne<BookingChannel>().WithMany().HasForeignKey(x => x.ChannelId);
        b.HasOne<Reservation>().WithMany().HasForeignKey(x => x.ReservationId);
    }
}
