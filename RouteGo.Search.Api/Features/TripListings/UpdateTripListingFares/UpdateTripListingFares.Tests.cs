namespace RouteGo.Search.Api.Features.TripListings.UpdateTripListingFares;

[Collection(DatabaseCollection.Name)]
public sealed class UpdateTripListingFaresTests : IAsyncLifetime
{
    private static readonly Guid _t201 = Guid.Parse("00000000-0000-0000-0000-000000000201");
    private static readonly Guid _t207 = Guid.Parse("00000000-0000-0000-0000-000000000207");
    private static readonly DateTimeOffset _at0800 = new DateTimeOffset(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    private const string BookSeatSql = """
        UPDATE trip_listings.listing_seats SET status = 'Booked' WHERE trip_id = @TripId AND seat_no = @SeatNo;
        """;

    private readonly DatabaseFixture _database;
    private readonly ListingStore _store;
    private readonly FakeLogger<UpdateTripListingFaresHandler> _logger = new FakeLogger<UpdateTripListingFaresHandler>();

    public UpdateTripListingFaresTests(DatabaseFixture database)
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
    public async Task HandleAsync_UpperBerthsRepriced_LowestFreeFareFollows()
    {
        // Arrange
        await ScheduleAsync(_t201);
        JsonObject data = TestEvents.FareChanged(_t201, "Upper berth", 1000, TestEvents.SeatLabels("U", 20));

        // Act
        EventHandlingOutcome outcome = await CreateHandler().HandleAsync(Changed("F1", data, _at0800), CancellationToken.None);

        // Assert
        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Equal(1000, await _store.SeatFareAsync(_t201, "U1"));
        Assert.Equal(1000, await _store.SeatFareAsync(_t201, "U20"));
        Assert.Equal(1200, await _store.SeatFareAsync(_t201, "L1"));
        Features.TripSearch.SearchTrips.TripListing? found = await _store.SearchMumbaiToPuneAsync(_t201);
        Assert.Equal(1000, found!.LowestFreeFare);
    }

    [Fact]
    public async Task HandleAsync_BookedSeat_IsRepricedAndStaysBooked()
    {
        // Arrange
        await ScheduleAsync(_t201);
        await _database.SeedAsync(BookSeatSql, new { TripId = _t201, SeatNo = "U1" });

        // Act
        await CreateHandler().HandleAsync(
            Changed("F1", TestEvents.FareChanged(_t201, "Upper berth", 1000, "U1"), _at0800), CancellationToken.None);

        // Assert
        Assert.Equal(1000, await _store.SeatFareAsync(_t201, "U1"));
        Assert.Equal("Booked", await _store.SeatStatusAsync(_t201, "U1"));
    }

    [Fact]
    public async Task HandleAsync_SameEventTwice_FaresUnchanged()
    {
        // Arrange
        await ScheduleAsync(_t201);
        ReceivedEvent changed = Changed("F1", TestEvents.FareChanged(_t201, "Upper berth", 1000, "U1"), _at0800);
        await CreateHandler().HandleAsync(changed, CancellationToken.None);

        // Act
        EventHandlingOutcome outcome = await CreateHandler().HandleAsync(changed, CancellationToken.None);

        // Assert
        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Equal(1000, await _store.SeatFareAsync(_t201, "U1"));
    }

    [Fact]
    public async Task HandleAsync_OlderChangeArrivesLate_LatestChangeWins()
    {
        // Arrange
        await ScheduleAsync(_t201);
        await CreateHandler().HandleAsync(
            Changed("F1", TestEvents.FareChanged(_t201, "Upper berth", 1200, "U1"), _at0800.AddHours(2)), CancellationToken.None);

        // Act
        EventHandlingOutcome outcome = await CreateHandler().HandleAsync(
            Changed("F0", TestEvents.FareChanged(_t201, "Upper berth", 1100, "U1"), _at0800.AddHours(1)), CancellationToken.None);

        // Assert
        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Equal(1200, await _store.SeatFareAsync(_t201, "U1"));
    }

    [Fact]
    public async Task HandleAsync_TripNotKnownYet_WaitsThenAppliesAfterTheTrip()
    {
        // Arrange
        ReceivedEvent changed = Changed("F7", TestEvents.FareChanged(_t207, "Upper berth", 1000, "U1"), _at0800);

        // Act
        EventHandlingOutcome first = await CreateHandler().HandleAsync(changed, CancellationToken.None);
        await ScheduleAsync(_t207);
        EventHandlingOutcome retried = await CreateHandler().HandleAsync(changed, CancellationToken.None);

        // Assert
        Assert.Equal(EventHandlingOutcome.RetryLater, first);
        Assert.Equal(EventHandlingOutcome.Consumed, retried);
        Assert.Equal(1000, await _store.SeatFareAsync(_t207, "U1"));
    }

    [Fact]
    public async Task HandleAsync_TripStillUnknownOnLastAttempt_ParksAndAlerts()
    {
        // Arrange
        ReceivedEvent lastAttempt = TestEvents.Envelope(
            UpdateTripListingFaresHandler.TriggerEventType, "F9",
            TestEvents.FareChanged(_t207, "Upper berth", 1000, "U1"), _at0800, isLastAttempt: true);

        // Act
        EventHandlingOutcome outcome = await CreateHandler().HandleAsync(lastAttempt, CancellationToken.None);

        // Assert
        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        Assert.True(_logger.HasAlert("FLEET_EVENT_NOT_APPLIED", "F9", "tripUnknown"));
    }

    [Theory]
    [InlineData("noTripId")]
    [InlineData("noSeats")]
    [InlineData("seatWithoutFare")]
    [InlineData("unknownSeat")]
    public async Task HandleAsync_UnusableData_SetsAsideAndChangesNoFare(string defect)
    {
        // Arrange
        await ScheduleAsync(_t201);
        JsonObject data = WithDefect(TestEvents.FareChanged(_t201, "Upper berth", 1000, "U1", "U2"), defect);

        // Act
        EventHandlingOutcome outcome = await CreateHandler().HandleAsync(Changed("F8", data, _at0800), CancellationToken.None);

        // Assert
        Assert.Equal(EventHandlingOutcome.SetAside, outcome);
        Assert.Equal(900, await _store.SeatFareAsync(_t201, "U1"));
        Assert.True(_logger.HasAlert("FLEET_EVENT_NOT_APPLIED", "F8", "invalidEventData"));
    }

    private UpdateTripListingFaresHandler CreateHandler()
    {
        return new UpdateTripListingFaresHandler(WorkerPorts.Writer(_database), _logger);
    }

    private async Task ScheduleAsync(Guid tripId)
    {
        var create = new CreateTripListing.CreateTripListingHandler(
            WorkerPorts.Writer(_database), new FakeLogger<CreateTripListing.CreateTripListingHandler>());
        ReceivedEvent scheduled = TestEvents.Envelope(
            "TripScheduled", "E-" + tripId, TestEvents.TripScheduled(tripId), TestEvents.OccurredAt);
        await create.HandleAsync(scheduled, CancellationToken.None);
    }

    private static ReceivedEvent Changed(string eventId, JsonObject data, DateTimeOffset occurredAt)
    {
        return TestEvents.Envelope(UpdateTripListingFaresHandler.TriggerEventType, eventId, data, occurredAt);
    }

    private static JsonObject WithDefect(JsonObject data, string defect)
    {
        switch (defect)
        {
            case "noTripId":
                data.Remove("tripId");
                break;
            case "noSeats":
                data.Remove("seats");
                break;
            case "seatWithoutFare":
                data["seats"]![1]!.AsObject().Remove("fare");
                break;
            case "unknownSeat":
                data["seats"]![1]!["seatNo"] = "Z99";
                break;
            default:
                throw new ArgumentException($"Unknown defect '{defect}'.", nameof(defect));
        }

        return data;
    }
}
