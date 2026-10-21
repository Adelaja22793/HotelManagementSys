using HMS.Domain.Common;

namespace HMS.Domain.Entities;

/// <summary>A hotel property operated by the company. Owns its own time zone and business day.</summary>
public class Branch : AuditableEntity, IConcurrencyTracked
{
    public const string DefaultTimeZoneId = "Africa/Lagos";

    private Branch() { }

    public Branch(Guid companyId, string name, string code, string timeZoneId = DefaultTimeZoneId, string? currency = null)
    {
        if (companyId == Guid.Empty) throw new DomainException("COMPANY_REQUIRED", "Branch must belong to a company.");
        CompanyId = companyId;
        Code = Company.NormalizeCode(code);
        Update(name, timeZoneId, currency ?? Common.Currency.Default, null, null, null);
    }

    public Guid CompanyId { get; private set; }
    public Company? Company { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string TimeZoneId { get; private set; } = DefaultTimeZoneId;
    public string Currency { get; private set; } = Common.Currency.Default;
    public string? AddressLine { get; private set; }
    public string? City { get; private set; }
    public string? Phone { get; private set; }

    /// <summary>
    /// Local time of day at which the hotel business day rolls over. Night-audit style hotels
    /// often keep the previous business date until early morning.
    /// </summary>
    public TimeOnly BusinessDayCutoffLocal { get; private set; } = new(0, 0);

    /// <summary>When true a supervisor must inspect a cleaned room before it becomes VacantClean.</summary>
    public bool HousekeepingRequiresInspection { get; private set; } = true;

    public bool IsActive { get; private set; } = true;

    /// <summary>The last business date closed by the night audit. The branch never operates on or before it again.</summary>
    public DateOnly? LastClosedBusinessDate { get; private set; }

    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public void Update(string name, string timeZoneId, string currency, string? addressLine, string? city, string? phone)
    {
        Name = Guard.Required(name, "BRANCH_NAME_REQUIRED", "Branch name is required");
        TimeZoneId = ValidateTimeZone(timeZoneId);
        Currency = Common.Currency.Normalize(currency);
        AddressLine = string.IsNullOrWhiteSpace(addressLine) ? null : addressLine.Trim();
        City = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    }

    public void SetBusinessDayCutoff(TimeOnly cutoffLocal) => BusinessDayCutoffLocal = cutoffLocal;
    public void SetHousekeepingInspection(bool required) => HousekeepingRequiresInspection = required;
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    public TimeZoneInfo GetTimeZone() => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);

    /// <summary>Converts a UTC instant to the branch's local wall-clock time.</summary>
    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), GetTimeZone());

    /// <summary>
    /// The hotel operating date for a UTC instant, honouring the branch cutoff. Once a day is closed the branch
    /// moves to the next date straight away, even if the clock has not reached it yet (night audit before midnight).
    /// </summary>
    public DateOnly GetBusinessDate(DateTime utc)
    {
        var clockDate = GetClockDate(utc);
        return LastClosedBusinessDate is { } closed && clockDate <= closed ? closed.AddDays(1) : clockDate;
    }

    /// <summary>The operating date the wall clock alone gives, ignoring any night audit already done.</summary>
    public DateOnly GetClockDate(DateTime utc)
    {
        var local = ToLocal(utc);
        var date = DateOnly.FromDateTime(local);
        return TimeOnly.FromDateTime(local) < BusinessDayCutoffLocal ? date.AddDays(-1) : date;
    }

    /// <summary>The next day the night audit must close: the day after the last close, or today for a branch never closed.</summary>
    public DateOnly GetNextDateToClose(DateTime utcNow) => LastClosedBusinessDate?.AddDays(1) ?? GetBusinessDate(utcNow);

    /// <summary>Records the night audit. Days close one at a time, in order, and never ahead of the clock.</summary>
    public void CloseBusinessDay(DateOnly businessDate, DateTime utcNow)
    {
        var expected = GetNextDateToClose(utcNow);
        if (businessDate != expected)
            throw new DomainConflictException("BUSINESS_DATE_MISMATCH", $"The next day to close is {expected:yyyy-MM-dd}.");
        if (businessDate > GetClockDate(utcNow))
            throw new DomainConflictException("BUSINESS_DAY_NOT_STARTED", $"{businessDate:yyyy-MM-dd} has not started yet.");
        LastClosedBusinessDate = businessDate;
    }

    private static string ValidateTimeZone(string timeZoneId)
    {
        var id = Guard.Required(timeZoneId, "TIMEZONE_REQUIRED", "Time zone is required", 64);
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new DomainException("TIMEZONE_INVALID", $"Unknown time zone '{id}'.");
        }
        return id;
    }
}
