using System.Globalization;

namespace RouteGo.Search.Api.Features.TripSearch.SearchTrips;

/// <summary>The order trips are listed in, within the bookable group and within the sold-out group.</summary>
internal enum TripSort
{
    DepartureAt,
    LowestFreeFare,
    OperatorRating,
}

/// <summary>A validated trip search, as typed values. Absent optional filters are null.</summary>
internal sealed record SearchTripsQuery
{
    public required Guid FromCityId { get; init; }

    public required Guid ToCityId { get; init; }

    public required DateOnly Date { get; init; }

    public required IReadOnlyList<BusType> BusTypes { get; init; }

    public required bool? IsAc { get; init; }

    public required int? MinFare { get; init; }

    public required int? MaxFare { get; init; }

    public required TimeOnly? DepartAfter { get; init; }

    public required TimeOnly? DepartBefore { get; init; }

    public required decimal? MinRating { get; init; }

    public required TripSort Sort { get; init; }

    public required int Page { get; init; }
}

/// <summary>
/// How each query-string value of a trip search is read. The validator and the handler both go
/// through here, so what the validator accepts is exactly what the handler can read.
/// </summary>
internal static class SearchTripsValues
{
    /// <summary>India Standard Time has no daylight saving, so a fixed offset is exact.</summary>
    public static readonly TimeSpan IstOffset = TimeSpan.FromHours(5.5);

    public const string DateFormat = "yyyy-MM-dd";
    public const string TimeOfDayFormat = "HH:mm";
    private const decimal MaxRating = 5.0m;
    private const int FirstPage = 1;

    private static readonly Dictionary<string, BusType> _busTypesByWireName = new Dictionary<string, BusType>(StringComparer.Ordinal)
    {
        { "seater", BusType.Seater },
        { "sleeper", BusType.Sleeper },
        { "semiSleeper", BusType.SemiSleeper },
    };

    private static readonly Dictionary<string, TripSort> _sortsByWireName = new Dictionary<string, TripSort>(StringComparer.Ordinal)
    {
        { "departureAt", TripSort.DepartureAt },
        { "lowestFreeFare", TripSort.LowestFreeFare },
        { "operatorRating", TripSort.OperatorRating },
    };

    public static DateOnly TodayInIst(DateTimeOffset utcNow)
    {
        DateTimeOffset nowInIst = utcNow.ToOffset(IstOffset);
        return DateOnly.FromDateTime(nowInIst.DateTime);
    }

    public static bool IsGuid(string? value)
    {
        return Guid.TryParse(value, out _);
    }

    public static bool IsSameCity(string? fromCityId, string? toCityId)
    {
        if (!Guid.TryParse(fromCityId, out Guid from) || !Guid.TryParse(toCityId, out Guid to))
        {
            return false;
        }

        return from == to;
    }

    public static bool IsDate(string? value)
    {
        return TryParseDate(value, out _);
    }

    public static bool IsOnOrAfter(string? value, DateOnly today)
    {
        TryParseDate(value, out DateOnly date);
        return date >= today;
    }

    public static bool IsAbsentOrBusTypes(string? value)
    {
        if (value is null)
        {
            return true;
        }

        return TryParseBusTypes(value, out _);
    }

    public static bool IsAbsentOrBoolean(string? value)
    {
        if (value is null)
        {
            return true;
        }

        return bool.TryParse(value, out _);
    }

    public static bool IsAbsentOrFare(string? value)
    {
        if (value is null)
        {
            return true;
        }

        return TryParseWholeNumber(value, out _);
    }

    public static bool IsFareAbove(string? minFare, string? maxFare)
    {
        if (!TryParseWholeNumber(minFare, out int min) || !TryParseWholeNumber(maxFare, out int max))
        {
            return false;
        }

        return min > max;
    }

    public static bool IsAbsentOrTimeOfDay(string? value)
    {
        if (value is null)
        {
            return true;
        }

        return TryParseTimeOfDay(value, out _);
    }

    public static bool IsTimeLater(string? departAfter, string? departBefore)
    {
        if (!TryParseTimeOfDay(departAfter, out TimeOnly after) || !TryParseTimeOfDay(departBefore, out TimeOnly before))
        {
            return false;
        }

        return after > before;
    }

    public static bool IsAbsentOrRating(string? value)
    {
        if (value is null)
        {
            return true;
        }

        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal rating))
        {
            return false;
        }

        return rating <= MaxRating;
    }

    public static bool IsAbsentOrSort(string? value)
    {
        if (value is null)
        {
            return true;
        }

        return _sortsByWireName.ContainsKey(value);
    }

    public static bool IsAbsentOrPage(string? value)
    {
        if (value is null)
        {
            return true;
        }

        if (!TryParseWholeNumber(value, out int page))
        {
            return false;
        }

        return page >= FirstPage;
    }

    /// <summary>Reads a request the validator has accepted.</summary>
    public static SearchTripsQuery ToQuery(SearchTripsRequest request)
    {
        TryParseDate(request.Date, out DateOnly date);
        TryParseBusTypes(request.BusTypes, out List<BusType> busTypes);
        return new SearchTripsQuery
        {
            FromCityId = Guid.Parse(request.FromCityId!),
            ToCityId = Guid.Parse(request.ToCityId!),
            Date = date,
            BusTypes = busTypes,
            IsAc = ParseOptionalBoolean(request.IsAc),
            MinFare = ParseOptionalWholeNumber(request.MinFare),
            MaxFare = ParseOptionalWholeNumber(request.MaxFare),
            DepartAfter = ParseOptionalTimeOfDay(request.DepartAfter),
            DepartBefore = ParseOptionalTimeOfDay(request.DepartBefore),
            MinRating = ParseOptionalRating(request.MinRating),
            Sort = ParseSort(request.Sort),
            Page = ParseOptionalWholeNumber(request.Page) ?? FirstPage,
        };
    }

    private static bool TryParseDate(string? value, out DateOnly date)
    {
        return DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static bool TryParseTimeOfDay(string? value, out TimeOnly time)
    {
        return TimeOnly.TryParseExact(value, TimeOfDayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    private static bool TryParseWholeNumber(string? value, out int number)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    // The contract sends the list comma-separated; each type may appear once.
    private static bool TryParseBusTypes(string? value, out List<BusType> busTypes)
    {
        busTypes = new List<BusType>();
        if (value is null)
        {
            return true;
        }

        foreach (string name in value.Split(','))
        {
            if (!_busTypesByWireName.TryGetValue(name, out BusType busType) || busTypes.Contains(busType))
            {
                return false;
            }

            busTypes.Add(busType);
        }

        return true;
    }

    private static bool? ParseOptionalBoolean(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return bool.Parse(value);
    }

    private static int? ParseOptionalWholeNumber(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private static TimeOnly? ParseOptionalTimeOfDay(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return TimeOnly.ParseExact(value, TimeOfDayFormat, CultureInfo.InvariantCulture);
    }

    private static decimal? ParseOptionalRating(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return decimal.Parse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    private static TripSort ParseSort(string? value)
    {
        if (value is null)
        {
            return TripSort.DepartureAt;
        }

        return _sortsByWireName[value];
    }
}
