namespace RouteGo.Search.Api.Features.TripSearch.SearchTrips;

// The trip listings the criteria share: T-201, T-202 and T-203 from Mumbai to Pune on Saturday 25 Oct 2026.
public sealed partial class SearchTripsTests
{
    private static readonly TimeSpan _ist = TimeSpan.FromHours(5.5);

    // "Now" in every criterion unless it says otherwise: Tuesday 20 Oct 2026, 10:00 IST.
    private static readonly DateTimeOffset _now = new DateTimeOffset(2026, 10, 20, 10, 0, 0, _ist);

    private static readonly Guid _mumbai = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid _pune = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid _lonavala = Guid.Parse("00000000-0000-0000-0000-000000000003");

    private static readonly Guid _t201 = Guid.Parse("00000000-0000-0000-0000-000000000201");
    private static readonly Guid _t202 = Guid.Parse("00000000-0000-0000-0000-000000000202");
    private static readonly Guid _t203 = Guid.Parse("00000000-0000-0000-0000-000000000203");
    private static readonly Guid _t204 = Guid.Parse("00000000-0000-0000-0000-000000000204");
    private static readonly Guid _busB11 = Guid.Parse("00000000-0000-0000-0000-000000000b11");

    private const string InsertTripSql = """
        INSERT INTO trip_listings.trip_listings (
            trip_id, operator_name, operator_rating, bus_id, bus_name, bus_type, is_ac, amenities,
            from_city_id, from_city_name, to_city_id, to_city_name, service_date,
            departure_at, arrival_at, details_received_at, is_cancelled)
        VALUES (
            @TripId, @OperatorName, @OperatorRating, @BusId, @BusName, @BusType, @IsAc, @Amenities,
            @FromCityId, @FromCityName, @ToCityId, @ToCityName, (@DepartureAt AT TIME ZONE 'Asia/Kolkata')::date,
            @DepartureAt, @ArrivalAt, @DepartureAt - interval '7 days', false);
        """;

    // Seats are labelled <prefix>1, <prefix>2, ... and stored with the fare in paise.
    private const string InsertSeatsSql = """
        INSERT INTO trip_listings.listing_seats (trip_id, seat_no, seat_class, fare, status)
        SELECT @TripId, @LabelPrefix || n, @SeatClass, @FarePaise, @Status
        FROM generate_series(1, @Count) AS n;
        """;

    private const string InsertStopSql = """
        INSERT INTO trip_listings.listing_stops (trip_id, sequence, city_id, city_name, arrival_at)
        VALUES (@TripId, @Sequence, @CityId, @CityName, @ArrivalAt);
        """;

    private const string SetSeatsStatusSql = """
        UPDATE trip_listings.listing_seats
        SET status = @Status, status_changed_at = now()
        WHERE trip_id = @TripId AND seat_no = ANY(@SeatNos);
        """;

    private const string CancelTripSql = """
        UPDATE trip_listings.trip_listings SET is_cancelled = true, cancelled_at = now() WHERE trip_id = @TripId;
        """;

    private async Task SeedCriteriaTripsAsync()
    {
        await SeedT201Async();
        await SeedT202Async();
        await SeedT203Async();
    }

    // Mumbai → Lonavala → Pune, 22:00 IST, sleeper, AC; 20 upper berths at 900 and 16 lower berths at 1200, all free.
    private async Task SeedT201Async()
    {
        var departure = new DateTimeOffset(2026, 10, 25, 22, 0, 0, _ist);
        await SeedTripAsync(new SeedTrip
        {
            TripId = _t201,
            OperatorName = "BusCo Travels",
            OperatorRating = 4.2m,
            BusId = _busB11,
            BusName = "BusCo Volvo 9600",
            BusType = "Sleeper",
            IsAc = true,
            Amenities = new[] { "Wifi", "ChargingPoint" },
            DepartureAt = departure,
            ArrivalAt = departure.AddHours(3),
        });
        await SeedSeatsAsync(_t201, "U", "Upper berth", 900, 20, "Available");
        await SeedSeatsAsync(_t201, "L", "Lower berth", 1200, 16, "Available");
        var stop = new { TripId = _t201, Sequence = 2, CityId = _lonavala, CityName = "Lonavala", ArrivalAt = departure.AddMinutes(90).UtcDateTime };
        await _database.SeedAsync(InsertStopSql, stop);
    }

    // Mumbai → Pune, 07:00 IST, seater, non-AC; 40 seats at 500: 12 Available, 3 Held, 20 Booked, 5 Blocked.
    private async Task SeedT202Async()
    {
        var departure = new DateTimeOffset(2026, 10, 25, 7, 0, 0, _ist);
        await SeedTripAsync(new SeedTrip
        {
            TripId = _t202,
            OperatorName = "Sharma Travels",
            OperatorRating = 3.8m,
            BusId = Guid.NewGuid(),
            BusName = "Sharma Express",
            BusType = "Seater",
            IsAc = false,
            Amenities = Array.Empty<string>(),
            DepartureAt = departure,
            ArrivalAt = departure.AddHours(3),
        });
        await SeedSeatsAsync(_t202, "A", "Seater", 500, 12, "Available");
        await SeedSeatsAsync(_t202, "H", "Seater", 500, 3, "Held");
        await SeedSeatsAsync(_t202, "B", "Seater", 500, 20, "Booked");
        await SeedSeatsAsync(_t202, "X", "Seater", 500, 5, "Blocked");
    }

    // Mumbai → Pune, 14:00 IST, semi-sleeper, AC; 30 seats at 450, none free.
    private async Task SeedT203Async()
    {
        var departure = new DateTimeOffset(2026, 10, 25, 14, 0, 0, _ist);
        await SeedTripAsync(new SeedTrip
        {
            TripId = _t203,
            OperatorName = "Konkan Lines",
            OperatorRating = 4.6m,
            BusId = Guid.NewGuid(),
            BusName = "Konkan Star",
            BusType = "SemiSleeper",
            IsAc = true,
            Amenities = Array.Empty<string>(),
            DepartureAt = departure,
            ArrivalAt = departure.AddHours(3),
        });
        await SeedSeatsAsync(_t203, "S", "Seater", 450, 30, "Booked");
    }

    // A plain bookable Mumbai → Pune trip with ten free seats at 600.
    private async Task SeedBookableTripAsync(Guid tripId, DateTimeOffset departure, decimal operatorRating)
    {
        await SeedTripAsync(new SeedTrip
        {
            TripId = tripId,
            OperatorName = "Extra Travels",
            OperatorRating = operatorRating,
            BusId = Guid.NewGuid(),
            BusName = "Extra Bus",
            BusType = "Seater",
            IsAc = false,
            Amenities = Array.Empty<string>(),
            DepartureAt = departure,
            ArrivalAt = departure.AddHours(3),
        });
        await SeedSeatsAsync(tripId, "A", "Seater", 600, 10, "Available");
    }

    // Trips one minute apart from 06:00 IST, so their departure order is their creation order.
    private async Task<List<Guid>> SeedBookableTripsAsync(int count)
    {
        var tripIds = new List<Guid>();
        var first = new DateTimeOffset(2026, 10, 25, 6, 0, 0, _ist);
        for (int index = 0; index < count; index++)
        {
            var tripId = Guid.NewGuid();
            await SeedBookableTripAsync(tripId, first.AddMinutes(index), 4.0m);
            tripIds.Add(tripId);
        }

        return tripIds;
    }

    private async Task SeedTripAsync(SeedTrip trip)
    {
        var row = new
        {
            trip.TripId,
            trip.OperatorName,
            trip.OperatorRating,
            trip.BusId,
            trip.BusName,
            trip.BusType,
            trip.IsAc,
            trip.Amenities,
            FromCityId = _mumbai,
            FromCityName = "Mumbai",
            ToCityId = _pune,
            ToCityName = "Pune",
            DepartureAt = trip.DepartureAt.UtcDateTime,
            ArrivalAt = trip.ArrivalAt.UtcDateTime,
        };
        await _database.SeedAsync(InsertTripSql, row);
    }

    private async Task SeedSeatsAsync(Guid tripId, string labelPrefix, string seatClass, int fareRupees, int count, string status)
    {
        var seats = new { TripId = tripId, LabelPrefix = labelPrefix, SeatClass = seatClass, FarePaise = fareRupees * 100L, Count = count, Status = status };
        await _database.SeedAsync(InsertSeatsSql, seats);
    }

    private async Task SetSeatsStatusAsync(Guid tripId, string[] seatNos, string status)
    {
        await _database.SeedAsync(SetSeatsStatusSql, new { TripId = tripId, SeatNos = seatNos, Status = status });
    }

    private async Task CancelTripAsync(Guid tripId)
    {
        await _database.SeedAsync(CancelTripSql, new { TripId = tripId });
    }

    private sealed record SeedTrip
    {
        public required Guid TripId { get; init; }

        public required string OperatorName { get; init; }

        public required decimal OperatorRating { get; init; }

        public required Guid BusId { get; init; }

        public required string BusName { get; init; }

        public required string BusType { get; init; }

        public required bool IsAc { get; init; }

        public required string[] Amenities { get; init; }

        public required DateTimeOffset DepartureAt { get; init; }

        public required DateTimeOffset ArrivalAt { get; init; }
    }

    private sealed class FixedClock : IClock
    {
        private readonly DateTimeOffset _utcNow;

        public FixedClock(DateTimeOffset now)
        {
            _utcNow = now.ToUniversalTime();
        }

        public DateTimeOffset UtcNow
        {
            get
            {
                return _utcNow;
            }
        }
    }
}
