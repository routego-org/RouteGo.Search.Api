using System.Globalization;
using System.Text;

namespace RouteGo.Search.Api.Features.TripSearch.SearchTrips;

/// <summary>Finds searchable trips between two cities on a date, from Search's own trip listings.</summary>
public sealed class SearchTripsHandler
{
    private const int PaisePerRupee = 100;
    private const string IstTimeZone = "Asia/Kolkata";

    // A searchable trip on the asked route and date: not cancelled, its details received, departing after
    // the search cutoff. Seats left and the lowest free fare are derived per trip from its seats; a
    // sold-out trip shows the lowest fare of any seat. The optional filters are appended after it.
    private const string MatchingTripsSql = """
        WITH matching AS (
            SELECT t.trip_id, t.operator_name, t.operator_rating, t.bus_id, t.bus_name, t.bus_type, t.is_ac,
                   t.amenities, t.from_city_id, t.from_city_name, t.to_city_id, t.to_city_name, t.service_date,
                   t.departure_at, t.arrival_at, seats.seats_left, seats.lowest_free_fare,
                   seats.seats_left = 0 AS is_sold_out
            FROM trip_listings.trip_listings t
            CROSS JOIN LATERAL (
                SELECT count(*) FILTER (WHERE s.status = 'Available') AS seats_left,
                       COALESCE(min(s.fare) FILTER (WHERE s.status = 'Available'), min(s.fare)) AS lowest_free_fare
                FROM trip_listings.listing_seats s
                WHERE s.trip_id = t.trip_id
            ) seats
            WHERE t.from_city_id = @FromCityId
              AND t.to_city_id = @ToCityId
              AND t.service_date = @Date::date
              AND NOT t.is_cancelled
              AND t.details_received_at IS NOT NULL
              AND t.departure_at > @SearchableAfter
        """;

    private const string BusTypesFilter = " AND t.bus_type = ANY(@BusTypes)";
    private const string IsAcFilter = " AND t.is_ac = @IsAc";
    private const string MinFareFilter = " AND seats.lowest_free_fare >= @MinFarePaise";
    private const string MaxFareFilter = " AND seats.lowest_free_fare <= @MaxFarePaise";
    private const string DepartAfterFilter = $" AND (t.departure_at AT TIME ZONE '{IstTimeZone}')::time >= @DepartAfter::time";
    private const string DepartBeforeFilter = $" AND (t.departure_at AT TIME ZONE '{IstTimeZone}')::time <= @DepartBefore::time";
    private const string MinRatingFilter = " AND t.operator_rating >= @MinRating";

    private const string CountSql = ") SELECT count(*) FROM matching;";

    // Bookable trips before sold-out ones; within each group the chosen order, then departure, then trip.
    private const string PageSql = """
        ) SELECT trip_id, operator_name, operator_rating, bus_id, bus_name, bus_type, is_ac, amenities,
                 from_city_id, from_city_name, to_city_id, to_city_name, service_date, departure_at, arrival_at,
                 seats_left, lowest_free_fare, is_sold_out
        FROM matching
        ORDER BY is_sold_out, {0}, departure_at, trip_id
        LIMIT @PageSize OFFSET @Offset;
        """;

    private const string StopsSql = """
        SELECT trip_id, city_id, city_name, arrival_at
        FROM trip_listings.listing_stops
        WHERE trip_id = ANY(@TripIds)
        ORDER BY trip_id, sequence;
        """;

    private static readonly Dictionary<TripSort, string> _orderBySort = new Dictionary<TripSort, string>
    {
        { TripSort.DepartureAt, "departure_at" },
        { TripSort.LowestFreeFare, "lowest_free_fare" },
        { TripSort.OperatorRating, "operator_rating DESC" },
    };

    private readonly IReaderConnectionFactory _readerFactory;
    private readonly IClock _clock;
    private readonly TimeSpan _searchCutoff;
    private readonly int _pageSize;

    /// <summary>Creates the handler.</summary>
    public SearchTripsHandler(IReaderConnectionFactory readerFactory, IClock clock, TimeSpan searchCutoff, int pageSize)
    {
        _readerFactory = readerFactory;
        _clock = clock;
        _searchCutoff = searchCutoff;
        _pageSize = pageSize;
    }

    /// <summary>Lists one page of matching trips for a validated request.</summary>
    public async Task<SearchTripsResponse> HandleAsync(SearchTripsRequest request, CancellationToken cancellationToken)
    {
        SearchTripsQuery query = SearchTripsValues.ToQuery(request);
        string filteredTripsSql = MatchingTripsSql + BuildFilters(query);
        string orderBy = _orderBySort[query.Sort];
        string sql = filteredTripsSql + CountSql + filteredTripsSql + string.Format(PageSql, orderBy);
        DynamicParameters parameters = BuildParameters(query);

        TripPage page = await _readerFactory.ExecuteReadAsync(
            connection => LoadPageAsync(connection, sql, parameters, cancellationToken),
            cancellationToken);
        return ToResponse(page, query.Page, _pageSize);
    }

    // Every fragment is a fixed string chosen by which filters are present; values travel as parameters.
    private static string BuildFilters(SearchTripsQuery query)
    {
        var filters = new StringBuilder();
        AppendWhen(filters, query.BusTypes.Count > 0, BusTypesFilter);
        AppendWhen(filters, query.IsAc.HasValue, IsAcFilter);
        AppendWhen(filters, query.MinFare.HasValue, MinFareFilter);
        AppendWhen(filters, query.MaxFare.HasValue, MaxFareFilter);
        AppendWhen(filters, query.DepartAfter.HasValue, DepartAfterFilter);
        AppendWhen(filters, query.DepartBefore.HasValue, DepartBeforeFilter);
        AppendWhen(filters, query.MinRating.HasValue, MinRatingFilter);
        return filters.ToString();
    }

    private static void AppendWhen(StringBuilder filters, bool isPresent, string filter)
    {
        if (isPresent)
        {
            filters.Append(filter);
        }
    }

    private DynamicParameters BuildParameters(SearchTripsQuery query)
    {
        var parameters = new DynamicParameters();
        parameters.Add("FromCityId", query.FromCityId);
        parameters.Add("ToCityId", query.ToCityId);
        parameters.Add("Date", query.Date.ToString(SearchTripsValues.DateFormat, CultureInfo.InvariantCulture));
        parameters.Add("SearchableAfter", _clock.UtcNow.Add(_searchCutoff));
        parameters.Add("BusTypes", ToDatabaseNames(query.BusTypes));
        parameters.Add("IsAc", query.IsAc);
        parameters.Add("MinFarePaise", query.MinFare * PaisePerRupee);
        parameters.Add("MaxFarePaise", query.MaxFare * PaisePerRupee);
        parameters.Add("DepartAfter", query.DepartAfter?.ToString(SearchTripsValues.TimeOfDayFormat, CultureInfo.InvariantCulture));
        parameters.Add("DepartBefore", query.DepartBefore?.ToString(SearchTripsValues.TimeOfDayFormat, CultureInfo.InvariantCulture));
        parameters.Add("MinRating", query.MinRating);
        parameters.Add("PageSize", _pageSize);
        parameters.Add("Offset", (query.Page - 1) * _pageSize);
        return parameters;
    }

    private static string[] ToDatabaseNames(IReadOnlyList<BusType> busTypes)
    {
        string[] names = new string[busTypes.Count];
        for (int index = 0; index < busTypes.Count; index++)
        {
            names[index] = busTypes[index].ToString();
        }

        return names;
    }

    // The count and the page share one round trip; the stops of the page's trips are one more query.
    private static async Task<TripPage> LoadPageAsync(
        DbConnection connection, string sql, DynamicParameters parameters, CancellationToken cancellationToken)
    {
        var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
        using SqlMapper.GridReader results = await connection.QueryMultipleAsync(command);
        long totalCount = await results.ReadSingleAsync<long>();
        IEnumerable<TripRow> tripRows = await results.ReadAsync<TripRow>();
        var trips = tripRows.ToList();

        Guid[] tripIds = trips.Select(trip => trip.TripId).ToArray();
        var stopsCommand = new CommandDefinition(StopsSql, new { TripIds = tripIds }, cancellationToken: cancellationToken);
        IEnumerable<StopRow> stops = await connection.QueryAsync<StopRow>(stopsCommand);
        return new TripPage((int)totalCount, trips, stops.ToList());
    }

    private static SearchTripsResponse ToResponse(TripPage page, int pageNumber, int pageSize)
    {
        Dictionary<Guid, List<ListingStop>> stopsByTrip = GroupStops(page.Stops);
        var items = new List<TripListing>();
        foreach (TripRow row in page.Trips)
        {
            List<ListingStop>? stops;
            if (!stopsByTrip.TryGetValue(row.TripId, out stops))
            {
                stops = new List<ListingStop>();
            }

            items.Add(ToTripListing(row, stops));
        }

        return new SearchTripsResponse
        {
            Items = items,
            TotalCount = page.TotalCount,
            Page = pageNumber,
            PageSize = pageSize,
        };
    }

    private static Dictionary<Guid, List<ListingStop>> GroupStops(List<StopRow> stops)
    {
        var stopsByTrip = new Dictionary<Guid, List<ListingStop>>();
        foreach (StopRow stop in stops)
        {
            if (!stopsByTrip.ContainsKey(stop.TripId))
            {
                stopsByTrip[stop.TripId] = new List<ListingStop>();
            }

            stopsByTrip[stop.TripId].Add(new ListingStop(stop.CityId, stop.CityName, stop.ArrivalAt));
        }

        return stopsByTrip;
    }

    private static TripListing ToTripListing(TripRow row, List<ListingStop> stops)
    {
        TimeSpan duration = row.ArrivalAt - row.DepartureAt;
        return new TripListing
        {
            TripId = row.TripId,
            OperatorName = row.OperatorName,
            OperatorRating = row.OperatorRating,
            BusId = row.BusId,
            BusName = row.BusName,
            BusType = Enum.Parse<BusType>(row.BusType),
            IsAc = row.IsAc,
            Amenities = ToAmenities(row.Amenities),
            FromCity = new TripCity(row.FromCityId, row.FromCityName),
            ToCity = new TripCity(row.ToCityId, row.ToCityName),
            ServiceDate = DateOnly.FromDateTime(row.ServiceDate),
            DepartureAt = row.DepartureAt,
            ArrivalAt = row.ArrivalAt,
            DurationMinutes = (int)duration.TotalMinutes,
            Stops = stops,
            LowestFreeFare = (int)(row.LowestFreeFare / PaisePerRupee),
            SeatsLeft = (int)row.SeatsLeft,
            IsSoldOut = row.IsSoldOut,
        };
    }

    private static List<Amenity> ToAmenities(string[] names)
    {
        var amenities = new List<Amenity>();
        foreach (string name in names)
        {
            amenities.Add(Enum.Parse<Amenity>(name));
        }

        return amenities;
    }

    private sealed record TripPage(int TotalCount, List<TripRow> Trips, List<StopRow> Stops);

    private sealed class TripRow
    {
        public Guid TripId { get; init; }

        public string OperatorName { get; init; } = string.Empty;

        public decimal OperatorRating { get; init; }

        public Guid BusId { get; init; }

        public string BusName { get; init; } = string.Empty;

        public string BusType { get; init; } = string.Empty;

        public bool IsAc { get; init; }

        public string[] Amenities { get; init; } = Array.Empty<string>();

        public Guid FromCityId { get; init; }

        public string FromCityName { get; init; } = string.Empty;

        public Guid ToCityId { get; init; }

        public string ToCityName { get; init; } = string.Empty;

        public DateTime ServiceDate { get; init; }

        public DateTime DepartureAt { get; init; }

        public DateTime ArrivalAt { get; init; }

        public long SeatsLeft { get; init; }

        public long LowestFreeFare { get; init; }

        public bool IsSoldOut { get; init; }
    }

    private sealed class StopRow
    {
        public Guid TripId { get; init; }

        public Guid CityId { get; init; }

        public string CityName { get; init; } = string.Empty;

        public DateTime ArrivalAt { get; init; }
    }
}
