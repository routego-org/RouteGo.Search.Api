namespace RouteGo.Search.Api.Features.TripListings.CancelTripListing;

[Collection(DatabaseCollection.Name)]
public sealed class CancelTripListingTests : IAsyncLifetime
{
    private static readonly Guid _t202 = Guid.Parse("00000000-0000-0000-0000-000000000202");
    private static readonly Guid _t205 = Guid.Parse("00000000-0000-0000-0000-000000000205");
    private static readonly DateTimeOffset _now = new DateTimeOffset(2026, 10, 21, 6, 0, 0, TimeSpan.Zero);

    private const string CancelledAtSql = "SELECT cancelled_at FROM trip_listings.trip_listings WHERE trip_id = @TripId";

    private readonly DatabaseFixture _database;
    private readonly ListingStore _store;
    private readonly FakeLogger<CancelTripListingHandler> _logger = new FakeLogger<CancelTripListingHandler>();

    public CancelTripListingTests(DatabaseFixture database)
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
    public async Task HandleAsync_SearchableTrip_LeavesSearch()
    {
        // Arrange
        await ScheduleAsync(_t202);

        // Act
        EventHandlingOutcome outcome = await CreateHandler().HandleAsync(Cancelled("C1", _t202), CancellationToken.None);

        // Assert
        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.True(await _store.IsCancelledAsync(_t202));
        Assert.Null(await _store.SearchMumbaiToPuneAsync(_t202));
    }

    [Fact]
    public async Task HandleAsync_OnlyTripFromCity_CityIsNoLongerSuggested()
    {
        // Arrange
        await ScheduleAsync(_t202);

        // Act
        await CreateHandler().HandleAsync(Cancelled("C1", _t202), CancellationToken.None);

        // Assert
        Assert.Empty(await _store.FromCitySuggestionsAsync("Mu"));
    }

    [Fact]
    public async Task HandleAsync_SameEventTwice_StaysCancelledUnchanged()
    {
        // Arrange
        await ScheduleAsync(_t202);
        CancelTripListingHandler handler = CreateHandler();
        await handler.HandleAsync(Cancelled("C1", _t202), CancellationToken.None);
        DateTime? firstCancelledAt = await _database.QueryScalarAsync<DateTime?>(CancelledAtSql, new { TripId = _t202 });
        var later = new CancelTripListingHandler(WorkerPorts.Writer(_database), new FixedClock(_now.AddHours(1)), _logger);

        // Act
        EventHandlingOutcome outcome = await later.HandleAsync(Cancelled("C1", _t202), CancellationToken.None);

        // Assert
        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.True(await _store.IsCancelledAsync(_t202));
        Assert.Equal(firstCancelledAt, await _database.QueryScalarAsync<DateTime?>(CancelledAtSql, new { TripId = _t202 }));
    }

    [Fact]
    public async Task HandleAsync_TripNotKnownYet_IsRememberedAndNeverSearchable()
    {
        // Act
        await CreateHandler().HandleAsync(Cancelled("C5", _t205), CancellationToken.None);
        bool rememberedWithoutDetails = !await _store.HasDetailsAsync(_t205);
        await ScheduleAsync(_t205);

        // Assert
        Assert.True(rememberedWithoutDetails);
        Assert.True(await _store.IsCancelledAsync(_t205));
        Assert.Null(await _store.SearchMumbaiToPuneAsync(_t205));
    }

    [Fact]
    public async Task HandleAsync_LaterFleetAndSeatEvents_TripStaysCancelled()
    {
        // Arrange
        await ScheduleAsync(_t202);
        await CreateHandler().HandleAsync(Cancelled("C1", _t202), CancellationToken.None);

        // Act
        await new UpdateTripListingFares.UpdateTripListingFaresHandler(
                WorkerPorts.Writer(_database), new FakeLogger<UpdateTripListingFares.UpdateTripListingFaresHandler>())
            .HandleAsync(TestEvents.Envelope("FareChanged", "F1", TestEvents.FareChanged(_t202, "Upper berth", 1000, "U1"), _now), CancellationToken.None);
        await new UpdateSeatStatuses.UpdateSeatStatusesHandler(
                WorkerPorts.Writer(_database), new FakeLogger<UpdateSeatStatuses.UpdateSeatStatusesHandler>())
            .HandleAsync(TestEvents.Envelope("SeatUnblocked", "S1", TestEvents.SeatEvent(_t202, "U2"), _now), CancellationToken.None);

        // Assert
        Assert.True(await _store.IsCancelledAsync(_t202));
        Assert.Null(await _store.SearchMumbaiToPuneAsync(_t202));
    }

    [Fact]
    public async Task HandleAsync_NoTripId_SetsAsideAndAlerts()
    {
        // Arrange
        var data = new JsonObject { ["reason"] = "Bus breakdown" };

        // Act
        EventHandlingOutcome outcome = await CreateHandler().HandleAsync(
            TestEvents.Envelope(CancelTripListingHandler.TriggerEventType, "C9", data, _now), CancellationToken.None);

        // Assert
        Assert.Equal(EventHandlingOutcome.SetAside, outcome);
        Assert.True(_logger.HasAlert("FLEET_EVENT_NOT_APPLIED", "C9", "invalidEventData"));
    }

    private CancelTripListingHandler CreateHandler()
    {
        return new CancelTripListingHandler(WorkerPorts.Writer(_database), new FixedClock(_now), _logger);
    }

    private async Task ScheduleAsync(Guid tripId)
    {
        var create = new CreateTripListing.CreateTripListingHandler(
            WorkerPorts.Writer(_database), new FakeLogger<CreateTripListing.CreateTripListingHandler>());
        ReceivedEvent scheduled = TestEvents.Envelope(
            "TripScheduled", "E-" + tripId, TestEvents.TripScheduled(tripId), TestEvents.OccurredAt);
        await create.HandleAsync(scheduled, CancellationToken.None);
    }

    private static ReceivedEvent Cancelled(string eventId, Guid tripId)
    {
        return TestEvents.Envelope(
            CancelTripListingHandler.TriggerEventType, eventId, TestEvents.TripCancelled(tripId, "Bus breakdown"), _now);
    }
}
