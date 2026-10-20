namespace HMS.Domain.Common;

/// <summary>Hotel stay dates: arrival inclusive, departure exclusive (the nights slept).</summary>
public readonly record struct StayDates
{
    public const int MaxNights = 90;

    public StayDates(DateOnly arrival, DateOnly departure)
    {
        if (departure <= arrival)
            throw new DomainException("DATES_INVALID", "Departure must be after arrival.");
        if (departure.DayNumber - arrival.DayNumber > MaxNights)
            throw new DomainException("STAY_TOO_LONG", $"A single reservation can cover at most {MaxNights} nights.");
        Arrival = arrival;
        Departure = departure;
    }

    public DateOnly Arrival { get; }
    public DateOnly Departure { get; }
    public int Nights => Departure.DayNumber - Arrival.DayNumber;

    public IEnumerable<DateOnly> EachNight()
    {
        for (var d = Arrival; d < Departure; d = d.AddDays(1)) yield return d;
    }

    public bool Overlaps(DateOnly start, DateOnly endExclusive) => Arrival < endExclusive && start < Departure;
}
