using HMS.Domain.Common;

namespace HMS.Domain.Entities;

/// <summary>Nightly price for a room type under a rate plan over a date range (season).</summary>
public class RoomRate : BranchEntity
{
    private RoomRate() { }

    public RoomRate(Guid companyId, Guid branchId, Guid ratePlanId, Guid roomTypeId, DateOnly startDate, DateOnly endDate, decimal nightlyRate)
    {
        if (endDate < startDate) throw new DomainException("DATES_INVALID", "Rate end date must be on or after the start date.");
        CompanyId = companyId;
        BranchId = branchId;
        RatePlanId = ratePlanId;
        RoomTypeId = roomTypeId;
        StartDate = startDate;
        EndDate = endDate;
        NightlyRate = Money.RequirePositive(nightlyRate, "RATE_INVALID");
    }

    public Guid RatePlanId { get; private set; }
    public Guid RoomTypeId { get; private set; }
    public DateOnly StartDate { get; private set; }
    /// <summary>Inclusive last night the rate applies to.</summary>
    public DateOnly EndDate { get; private set; }
    public decimal NightlyRate { get; private set; }

    public bool Covers(DateOnly night) => night >= StartDate && night <= EndDate;
}
