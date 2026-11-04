namespace RouteGo.Search.Api.Features.TripListings.CreateTripListing;

/// <summary>
/// The TripScheduled data Search needs, as Fleet's events document spells it, and the checks that
/// decide whether it is usable. Every member is nullable so a missing one is reported, not thrown.
/// </summary>
internal sealed class TripScheduledData
{
    private const decimal MaxRating = 5.0m;

    private static readonly HashSet<string> _busTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "Seater", "Sleeper", "SemiSleeper",
    };

    private static readonly HashSet<string> _amenities = new HashSet<string>(StringComparer.Ordinal)
    {
        "Wifi", "ChargingPoint", "Blanket", "WaterBottle", "ReadingLight", "Cctv", "GpsTracking", "Restroom",
    };

    public Guid? TripId { get; init; }

    public string? OperatorName { get; init; }

    public decimal? Rating { get; init; }

    public Guid? BusId { get; init; }

    public string? BusName { get; init; }

    public string? BusType { get; init; }

    public bool? IsAc { get; init; }

    public List<string?>? Amenities { get; init; }

    public CityData? Source { get; init; }

    public CityData? Destination { get; init; }

    public List<StopData?>? Stops { get; init; }

    public DateOnly? ServiceDate { get; init; }

    public DateTimeOffset? DepartureUtc { get; init; }

    public DateTimeOffset? ArrivalUtc { get; init; }

    public List<SeatData?>? Seats { get; init; }

    /// <summary>What is wrong with this data, naming the field, or null when it is usable.</summary>
    public string? DescribeProblem()
    {
        string? problem = DescribeTripProblem();
        if (problem != null)
        {
            return problem;
        }

        problem = DescribeBusProblem();
        if (problem != null)
        {
            return problem;
        }

        problem = DescribeStopsProblem();
        if (problem != null)
        {
            return problem;
        }

        return DescribeSeatsProblem();
    }

    private string? DescribeTripProblem()
    {
        if (TripId is null)
        {
            return "tripId is missing";
        }

        if (string.IsNullOrWhiteSpace(OperatorName) || Rating is null || Rating < 0 || Rating > MaxRating)
        {
            return "operatorName or rating (0.0 to 5.0) is missing or out of range";
        }

        if (!IsComplete(Source) || !IsComplete(Destination))
        {
            return "source or destination city is missing";
        }

        if (Source!.CityId == Destination!.CityId)
        {
            return "source and destination are the same city";
        }

        if (ServiceDate is null || DepartureUtc is null || ArrivalUtc is null)
        {
            return "serviceDate, departureUtc or arrivalUtc is missing";
        }

        if (ArrivalUtc <= DepartureUtc)
        {
            return "arrivalUtc is not after departureUtc";
        }

        return null;
    }

    private string? DescribeBusProblem()
    {
        if (BusId is null || string.IsNullOrWhiteSpace(BusName) || IsAc is null)
        {
            return "busId, busName or isAc is missing";
        }

        if (BusType is null || !_busTypes.Contains(BusType))
        {
            return "busType is missing or not Seater, Sleeper or SemiSleeper";
        }

        if (Amenities is null)
        {
            return "amenities is missing";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? amenity in Amenities)
        {
            if (amenity is null || !_amenities.Contains(amenity) || !seen.Add(amenity))
            {
                return "amenities holds an unknown or repeated value";
            }
        }

        return null;
    }

    private string? DescribeStopsProblem()
    {
        if (Stops is null || Stops.Count < 2)
        {
            return "stops is missing or has fewer than the first and last city";
        }

        var sequences = new HashSet<int>();
        foreach (StopData? stop in Stops)
        {
            if (stop is null || stop.Sequence is null || stop.CityId is null
                || string.IsNullOrWhiteSpace(stop.CityName) || stop.ArrivalUtc is null)
            {
                return "a stop has no sequence, cityId, cityName or arrivalUtc";
            }

            if (!sequences.Add(stop.Sequence.Value))
            {
                return "two stops have the same sequence";
            }
        }

        foreach (StopData stop in StopsAlongTheWay())
        {
            if (stop.ArrivalUtc < DepartureUtc || stop.ArrivalUtc > ArrivalUtc)
            {
                return "a stop along the way arrives outside the trip's departure and arrival";
            }
        }

        return null;
    }

    private string? DescribeSeatsProblem()
    {
        if (Seats is null || Seats.Count == 0)
        {
            return "seats is missing or empty";
        }

        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (SeatData? seat in Seats)
        {
            if (seat is null || string.IsNullOrWhiteSpace(seat.SeatNo)
                || string.IsNullOrWhiteSpace(seat.Class) || seat.Fare is null || seat.Fare < 0)
            {
                return "a seat has no seatNo, class or fare";
            }

            if (!labels.Add(seat.SeatNo))
            {
                return "two seats have the same seatNo";
            }
        }

        return null;
    }

    /// <summary>The stops other than the first and the last, in order. Only valid once checked.</summary>
    public List<StopData> StopsAlongTheWay()
    {
        var ordered = new List<StopData>();
        foreach (StopData? stop in Stops!)
        {
            ordered.Add(stop!);
        }

        ordered.Sort((left, right) => left.Sequence!.Value.CompareTo(right.Sequence!.Value));
        return ordered.GetRange(1, ordered.Count - 2);
    }

    private static bool IsComplete(CityData? city)
    {
        return city != null && city.CityId != null && !string.IsNullOrWhiteSpace(city.Name);
    }
}

/// <summary>A first or last city as the event gives it.</summary>
internal sealed class CityData
{
    public Guid? CityId { get; init; }

    public string? Name { get; init; }
}

/// <summary>One stop as the event gives it, first and last city included.</summary>
internal sealed class StopData
{
    public int? Sequence { get; init; }

    public Guid? CityId { get; init; }

    public string? CityName { get; init; }

    public DateTimeOffset? ArrivalUtc { get; init; }
}

/// <summary>One seat as the event gives it; deck, row and column are not Search's.</summary>
internal sealed class SeatData
{
    public string? SeatNo { get; init; }

    public string? Class { get; init; }

    public int? Fare { get; init; }
}
