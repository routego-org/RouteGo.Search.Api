namespace RouteGo.Search.Api.Features.TripListings.CreateTripListing;

[Collection(DatabaseCollection.Name)]
public sealed class CreateTripListingTests : IAsyncLifetime
{
    private static readonly Guid _t201 = Guid.Parse("00000000-0000-0000-0000-000000000201");
    private static readonly Guid _t205 = Guid.Parse("00000000-0000-0000-0000-000000000205");
    private static readonly Guid _t206 = Guid.Parse("00000000-0000-0000-0000-000000000206");

    private const string HoldSeatsSql = """
        UPDATE trip_listings.listing_seats SET status = 'Held' WHERE trip_id = @TripId AND seat_no = ANY(@SeatNos);
        """;

    private const string CancelledListingSql = """
        INSERT INTO trip_listings.trip_listings (trip_id, is_cancelled, cancelled_at) VALUES (@TripId, true, now());
        """;

    private readonly DatabaseFixture _database;
    private readonly ListingStore _store;
    private readonly FakeLogger<CreateTripListingHandler> _logger = new FakeLogger<CreateTripListingHandler>();

    public CreateTripListingTests(DatabaseFixture database)
    {
        _database = database;
        _store = new ListingStore(database);
    }

    public async Task InitializeAsync()
    {
        await _database.ResetBusinessTablesAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task HandleAsync_NewTrip_CreatesSearchableListingWithAvailableSeats()
    {
        // Arrange
        CreateTripListingHandler handler = CreateHandler();

        // Act
        EventHandlingOutcome outcome = await handler.HandleAsync(Scheduled("E1", _t201), CancellationToken.None);

        // Assert
        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.False(await _store.IsCancelledAsync(_t201));
        Assert.Equal(36, await _store.SeatCountAsync(_t201));
        Assert.Equal(36, await _store.AvailableSeatCountAsync(_t201));
        Assert.Equal(900, await _store.SeatFareAsync(_t201, "U1"));
        Assert.Equal(1200, await _store.SeatFareAsync(_t201, "L16"));
        Features.TripSearch.SearchTrips.TripListing? found = await _store.SearchMumbaiToPuneAsync(_t201);
        Assert.NotNull(found);
        Assert.Equal("BusCo Travels", found.OperatorName);
        Assert.Equal(4.2m, found.OperatorRating);
        Assert.Equal(TestEvents.BusB11, found.BusId);
        Assert.Equal(new DateTime(2026, 10, 25, 16, 30, 0, DateTimeKind.Utc), found.DepartureAt);
        Assert.Equal(new DateTime(2026, 10, 25, 19, 30, 0, DateTimeKind.Utc), found.ArrivalAt);
        Assert.Equal("Lonavala", Assert.Single(found.Stops).CityName);
        Assert.Equal(36, found.SeatsLeft);
        Assert.Equal(900, found.LowestFreeFare);
    }

    [Fact]
    public async Task HandleAsync_SameEventTwice_KeepsSeatsAndTheirStatuses()
    {
        // Arrange
        CreateTripListingHandler handler = CreateHandler();
        ReceivedEvent scheduled = Scheduled("E1", _t201);
        await handler.HandleAsync(scheduled, CancellationToken.None);
        await _database.SeedAsync(HoldSeatsSql, new { TripId = _t201, SeatNos = new[] { "U1", "U2" } });

        // Act
        EventHandlingOutcome outcome = await handler.HandleAsync(scheduled, CancellationToken.None);

        // Assert
        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Equal(36, await _store.SeatCountAsync(_t201));
        Assert.Equal("Held", await _store.SeatStatusAsync(_t201, "U1"));
        Assert.Equal("Held", await _store.SeatStatusAsync(_t201, "U2"));
    }

    [Fact]
    public async Task HandleAsync_DifferentEventForKnownTrip_ChangesNothing()
    {
        // Arrange
        CreateTripListingHandler handler = CreateHandler();
        await handler.HandleAsync(Scheduled("E1", _t201), CancellationToken.None);
        JsonObject repriced = TestEvents.TripScheduled(_t201);
        repriced["seats"]![0]!["fare"] = 5000;

        // Act
        EventHandlingOutcome outcome = await handler.HandleAsync(
            TestEvents.Envelope(CreateTripListingHandler.TriggerEventType, "E2", repriced, TestEvents.OccurredAt),
            CancellationToken.None);

        // Assert
        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Equal(900, await _store.SeatFareAsync(_t201, "U1"));
        Assert.Equal(36, await _store.SeatCountAsync(_t201));
    }

    [Fact]
    public async Task HandleAsync_CancellationArrivedFirst_StaysCancelledWithDetails()
    {
        // Arrange
        await _database.SeedAsync(CancelledListingSql, new { TripId = _t205 });
        CreateTripListingHandler handler = CreateHandler();

        // Act
        await handler.HandleAsync(Scheduled("E5", _t205), CancellationToken.None);

        // Assert
        Assert.True(await _store.HasDetailsAsync(_t205));
        Assert.True(await _store.IsCancelledAsync(_t205));
        Assert.Equal(36, await _store.SeatCountAsync(_t205));
        Assert.Null(await _store.SearchMumbaiToPuneAsync(_t205));
    }

    [Theory]
    [InlineData("noTripId")]
    [InlineData("noSource")]
    [InlineData("sameCities")]
    [InlineData("noServiceDate")]
    [InlineData("noDeparture")]
    [InlineData("noSeats")]
    [InlineData("repeatedSeat")]
    [InlineData("seatWithoutFare")]
    public async Task HandleAsync_UnusableData_SetsAsideAndAlerts(string defect)
    {
        // Arrange
        CreateTripListingHandler handler = CreateHandler();
        JsonObject data = WithDefect(TestEvents.TripScheduled(_t201), defect);

        // Act
        EventHandlingOutcome outcome = await handler.HandleAsync(
            TestEvents.Envelope(CreateTripListingHandler.TriggerEventType, "E9", data, TestEvents.OccurredAt),
            CancellationToken.None);

        // Assert
        Assert.Equal(EventHandlingOutcome.SetAside, outcome);
        Assert.False(await _store.ListingExistsAsync(_t201));
        Assert.True(_logger.HasAlert("FLEET_EVENT_NOT_APPLIED", "E9", "invalidEventData"));
    }

    [Fact]
    public async Task HandleAsync_DatabaseUnavailable_CreatesNothingAndReplayCreates()
    {
        // Arrange: the listener keeps the message on the queue for a DbException and alerts on the last attempt.
        var unreachable = new CreateTripListingHandler(WorkerPorts.UnreachableWriter(), _logger);
        ReceivedEvent scheduled = Scheduled("E1", _t201);

        // Act
        await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(
            () => unreachable.HandleAsync(scheduled, CancellationToken.None));
        EventHandlingOutcome replay = await CreateHandler().HandleAsync(scheduled, CancellationToken.None);

        // Assert
        Assert.Equal(EventHandlingOutcome.Consumed, replay);
        Assert.Equal(36, await _store.SeatCountAsync(_t201));
    }

    [Fact]
    public async Task HandleAsync_EventsWaitingAfterRestart_CreatesEveryListing()
    {
        // Arrange: the queue kept both announcements while Search was stopped.
        CreateTripListingHandler handler = CreateHandler();

        // Act
        await handler.HandleAsync(Scheduled("E1", _t201), CancellationToken.None);
        await handler.HandleAsync(Scheduled("E6", _t206), CancellationToken.None);

        // Assert
        Assert.NotNull(await _store.SearchMumbaiToPuneAsync(_t201));
        Assert.NotNull(await _store.SearchMumbaiToPuneAsync(_t206));
    }

    private CreateTripListingHandler CreateHandler()
    {
        return new CreateTripListingHandler(WorkerPorts.Writer(_database), _logger);
    }

    private static ReceivedEvent Scheduled(string eventId, Guid tripId)
    {
        return TestEvents.Envelope(
            CreateTripListingHandler.TriggerEventType, eventId, TestEvents.TripScheduled(tripId), TestEvents.OccurredAt);
    }

    private static JsonObject WithDefect(JsonObject data, string defect)
    {
        switch (defect)
        {
            case "noTripId":
                data.Remove("tripId");
                break;
            case "noSource":
                data.Remove("source");
                break;
            case "sameCities":
                data["destination"]!["cityId"] = TestEvents.Mumbai;
                break;
            case "noServiceDate":
                data.Remove("serviceDate");
                break;
            case "noDeparture":
                data.Remove("departureUtc");
                break;
            case "noSeats":
                data["seats"] = new JsonArray();
                break;
            case "repeatedSeat":
                data["seats"]![1]!["seatNo"] = "U1";
                break;
            case "seatWithoutFare":
                data["seats"]![0]!.AsObject().Remove("fare");
                break;
            default:
                throw new ArgumentException($"Unknown defect '{defect}'.", nameof(defect));
        }

        return data;
    }
}
