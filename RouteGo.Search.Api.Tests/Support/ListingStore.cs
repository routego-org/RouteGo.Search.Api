using RouteGo.Search.Api.Features.TripSearch.SearchCities;
using RouteGo.Search.Api.Features.TripSearch.SearchTrips;

namespace RouteGo.Search.Api.Tests.Support;

/// <summary>
/// Reads what the trip-listing workers wrote: seats and listings straight from the database, and
/// search answers through the real search handlers, as a customer would see them.
/// </summary>
public sealed class ListingStore
{
    // "Now" for every search: Tuesday 20 Oct 2026, 10:00 IST, five days before the criteria's trips.
    private static readonly DateTimeOffset _now = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly TimeSpan _searchCutoff = TimeSpan.FromMinutes(30);
    private const int PageSize = 20;

    private readonly DatabaseFixture _database;

    public ListingStore(DatabaseFixture database)
    {
        _database = database;
    }

    public async Task<string?> SeatStatusAsync(Guid tripId, string seatNo)
    {
        return await _database.QueryScalarAsync<string>(
            "SELECT status FROM trip_listings.listing_seats WHERE trip_id = @TripId AND seat_no = @SeatNo",
            new { TripId = tripId, SeatNo = seatNo });
    }

    /// <summary>The seat's fare in whole rupees.</summary>
    public async Task<long> SeatFareAsync(Guid tripId, string seatNo)
    {
        return await _database.QueryScalarAsync<long>(
            "SELECT fare / 100 FROM trip_listings.listing_seats WHERE trip_id = @TripId AND seat_no = @SeatNo",
            new { TripId = tripId, SeatNo = seatNo });
    }

    public async Task<long> SeatCountAsync(Guid tripId)
    {
        return await _database.QueryScalarAsync<long>(
            "SELECT count(*) FROM trip_listings.listing_seats WHERE trip_id = @TripId", new { TripId = tripId });
    }

    public async Task<long> AvailableSeatCountAsync(Guid tripId)
    {
        return await _database.QueryScalarAsync<long>(
            "SELECT count(*) FROM trip_listings.listing_seats WHERE trip_id = @TripId AND status = 'Available'",
            new { TripId = tripId });
    }

    public async Task<bool> ListingExistsAsync(Guid tripId)
    {
        return await _database.QueryScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM trip_listings.trip_listings WHERE trip_id = @TripId)", new { TripId = tripId });
    }

    public async Task<bool> IsCancelledAsync(Guid tripId)
    {
        return await _database.QueryScalarAsync<bool>(
            "SELECT is_cancelled FROM trip_listings.trip_listings WHERE trip_id = @TripId", new { TripId = tripId });
    }

    public async Task<bool> HasDetailsAsync(Guid tripId)
    {
        return await _database.QueryScalarAsync<bool>(
            "SELECT details_received_at IS NOT NULL FROM trip_listings.trip_listings WHERE trip_id = @TripId",
            new { TripId = tripId });
    }

    /// <summary>The trip as a search from Mumbai to Pune on 2026-10-25 shows it, or null when it is not found.</summary>
    public async Task<TripListing?> SearchMumbaiToPuneAsync(Guid tripId)
    {
        var handler = new SearchTripsHandler(WorkerPorts.Reader(_database), new FixedClock(_now), _searchCutoff, PageSize);
        var request = new SearchTripsRequest
        {
            FromCityId = TestEvents.Mumbai.ToString(),
            ToCityId = TestEvents.Pune.ToString(),
            Date = "2026-10-25",
        };
        SearchTripsResponse response = await handler.HandleAsync(request, CancellationToken.None);
        foreach (TripListing trip in response.Items)
        {
            if (trip.TripId == tripId)
            {
                return trip;
            }
        }

        return null;
    }

    /// <summary>The names of the from-city suggestions for <paramref name="prefix"/>.</summary>
    public async Task<List<string>> FromCitySuggestionsAsync(string prefix)
    {
        var handler = new SearchCitiesHandler(WorkerPorts.Reader(_database), new FixedClock(_now), _searchCutoff);
        var request = new SearchCitiesRequest(prefix, SearchCitiesHandler.FromRole, null);
        SearchCitiesResponse response = await handler.HandleAsync(request, CancellationToken.None);
        var names = new List<string>();
        foreach (City city in response.Items)
        {
            names.Add(city.Name);
        }

        return names;
    }
}
