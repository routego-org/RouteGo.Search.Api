namespace RouteGo.Search.Api.Features.TripListings.UpdateSeatStatuses;

[Collection(DatabaseCollection.Name)]
public sealed class UpdateSeatStatusesTests : IAsyncLifetime
{
    private static readonly Guid _t201 = Guid.Parse("00000000-0000-0000-0000-000000000201");
    private static readonly Guid _t208 = Guid.Parse("00000000-0000-0000-0000-000000000208");
    private static readonly DateTimeOffset _at1000 = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture _database;
    private readonly ListingStore _store;
    private readonly FakeLogger<UpdateSeatStatusesHandler> _logger = new FakeLogger<UpdateSeatStatusesHandler>();

    public UpdateSeatStatusesTests(DatabaseFixture database)
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
    public async Task HandleAsync_SeatHeld_SeatsAreNoLongerFree()
    {
        // Arrange
        await ScheduleAsync(_t201);

        // Act
        EventHandlingOutcome outcome = await ApplyAsync("SeatHeld", "S1", TestEvents.SeatEvent(_t201, "U1", "U2"), _at1000);

        // Assert
        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Equal("Held", await _store.SeatStatusAsync(_t201, "U1"));
        Assert.Equal("Held", await _store.SeatStatusAsync(_t201, "U2"));
        Assert.Equal(34, (await _store.SearchMumbaiToPuneAsync(_t201))!.SeatsLeft);
    }

    [Fact]
    public async Task HandleAsync_SeatBookedAfterHeld_SeatsAreBooked()
    {
        // Arrange
        await ScheduleAsync(_t201);
        await ApplyAsync("SeatHeld", "S1", TestEvents.SeatEvent(_t201, "U1", "U2"), _at1000);

        // Act
        await ApplyAsync("SeatBooked", "S2", TestEvents.SeatEvent(_t201, "U1", "U2"), _at1000.AddMinutes(2));

        // Assert
        Assert.Equal("Booked", await _store.SeatStatusAsync(_t201, "U1"));
        Assert.Equal("Booked", await _store.SeatStatusAsync(_t201, "U2"));
        Assert.Equal(34, await _store.AvailableSeatCountAsync(_t201));
    }

    [Fact]
    public async Task HandleAsync_SeatReleasedToAvailable_SeatsAreFreeAgain()
    {
        // Arrange
        await ScheduleAsync(_t201);
        await ApplyAsync("SeatHeld", "S1", TestEvents.SeatEvent(_t201, "U1", "U2"), _at1000);

        // Act
        await ApplyAsync("SeatReleased", "S2", TestEvents.SeatReleased(_t201, "Available", "U1", "U2"), _at1000.AddMinutes(10));

        // Assert
        Assert.Equal("Available", await _store.SeatStatusAsync(_t201, "U1"));
        Assert.Equal(36, await _store.AvailableSeatCountAsync(_t201));
    }

    [Fact]
    public async Task HandleAsync_SeatReleasedToBlocked_SeatStaysBlocked()
    {
        // Arrange
        await ScheduleAsync(_t201);

        // Act
        await ApplyAsync("SeatReleased", "S1", TestEvents.SeatReleased(_t201, "Blocked", "U3"), _at1000);

        // Assert
        Assert.Equal("Blocked", await _store.SeatStatusAsync(_t201, "U3"));
        Assert.Equal(35, await _store.AvailableSeatCountAsync(_t201));
    }

    [Fact]
    public async Task HandleAsync_OperatorBlocksThenUnblocks_SeatsFollow()
    {
        // Arrange
        await ScheduleAsync(_t201);

        // Act
        await ApplyAsync("SeatBlocked", "S1", TestEvents.SeatEvent(_t201, "L1", "L2"), _at1000);
        long afterBlock = await _store.AvailableSeatCountAsync(_t201);
        await ApplyAsync("SeatUnblocked", "S2", TestEvents.SeatEvent(_t201, "L2"), _at1000.AddMinutes(5));

        // Assert
        Assert.Equal(34, afterBlock);
        Assert.Equal("Blocked", await _store.SeatStatusAsync(_t201, "L1"));
        Assert.Equal("Available", await _store.SeatStatusAsync(_t201, "L2"));
        Assert.Equal(35, await _store.AvailableSeatCountAsync(_t201));
    }

    [Fact]
    public async Task HandleAsync_SeatChanged_MovesTheBookingAndKeepsSeatsLeft()
    {
        // Arrange
        await ScheduleAsync(_t201);
        await ApplyAsync("SeatBooked", "S1", TestEvents.SeatEvent(_t201, "L1"), _at1000);
        long before = await _store.AvailableSeatCountAsync(_t201);

        // Act
        await ApplyAsync("SeatChanged", "S2", TestEvents.SeatChanged(_t201, "L1", "L5"), _at1000.AddMinutes(5));

        // Assert
        Assert.Equal("Available", await _store.SeatStatusAsync(_t201, "L1"));
        Assert.Equal("Booked", await _store.SeatStatusAsync(_t201, "L5"));
        Assert.Equal(before, await _store.AvailableSeatCountAsync(_t201));
    }

    [Fact]
    public async Task HandleAsync_AllCheaperSeatsBooked_LowestFreeFareFollowsThenSoldOut()
    {
        // Arrange
        await ScheduleAsync(_t201);

        // Act
        await ApplyAsync("SeatBooked", "S1", TestEvents.SeatEvent(_t201, TestEvents.SeatLabels("U", 20)), _at1000);
        Features.TripSearch.SearchTrips.TripListing? upperGone = await _store.SearchMumbaiToPuneAsync(_t201);
        await ApplyAsync("SeatBooked", "S2", TestEvents.SeatEvent(_t201, TestEvents.SeatLabels("L", 16)), _at1000.AddMinutes(1));
        Features.TripSearch.SearchTrips.TripListing? soldOut = await _store.SearchMumbaiToPuneAsync(_t201);

        // Assert
        Assert.Equal(1200, upperGone!.LowestFreeFare);
        Assert.Equal(16, upperGone.SeatsLeft);
        Assert.True(soldOut!.IsSoldOut);
        Assert.Equal(900, soldOut.LowestFreeFare);
    }

    [Fact]
    public async Task HandleAsync_SameEventTwice_ChangesNothingMore()
    {
        // Arrange
        await ScheduleAsync(_t201);
        await ApplyAsync("SeatHeld", "S1", TestEvents.SeatEvent(_t201, "U1"), _at1000);

        // Act
        EventHandlingOutcome outcome = await ApplyAsync("SeatHeld", "S1", TestEvents.SeatEvent(_t201, "U1"), _at1000);

        // Assert
        Assert.Equal(EventHandlingOutcome.Consumed, outcome);
        Assert.Equal("Held", await _store.SeatStatusAsync(_t201, "U1"));
        Assert.Equal(35, await _store.AvailableSeatCountAsync(_t201));
    }

    [Fact]
    public async Task HandleAsync_OlderEventArrivesLate_SeatKeepsLaterStatus()
    {
        // Arrange
        await ScheduleAsync(_t201);
        await ApplyAsync("SeatReleased", "S2", TestEvents.SeatReleased(_t201, "Available", "U1"), _at1000.AddMinutes(10));

        // Act
        await ApplyAsync("SeatHeld", "S1", TestEvents.SeatEvent(_t201, "U1"), _at1000);

        // Assert
        Assert.Equal("Available", await _store.SeatStatusAsync(_t201, "U1"));
    }

    [Fact]
    public async Task HandleAsync_DuplicatesAndReordering_CountsStayRight()
    {
        // Arrange
        await ScheduleAsync(_t201);
        JsonObject releaseU2 = TestEvents.SeatReleased(_t201, "Available", "U2");

        // Act: published Held U1 10:00, Booked U1 10:02, Held U2 10:03, Released U2 10:13 — received out of order, with repeats.
        await ApplyAsync("SeatReleased", "S4", releaseU2, _at1000.AddMinutes(13));
        await ApplyAsync("SeatHeld", "S1", TestEvents.SeatEvent(_t201, "U1"), _at1000);
        await ApplyAsync("SeatHeld", "S3", TestEvents.SeatEvent(_t201, "U2"), _at1000.AddMinutes(3));
        await ApplyAsync("SeatBooked", "S2", TestEvents.SeatEvent(_t201, "U1"), _at1000.AddMinutes(2));
        await ApplyAsync("SeatHeld", "S1", TestEvents.SeatEvent(_t201, "U1"), _at1000);
        await ApplyAsync("SeatReleased", "S4", releaseU2, _at1000.AddMinutes(13));

        // Assert
        Assert.Equal("Booked", await _store.SeatStatusAsync(_t201, "U1"));
        Assert.Equal("Available", await _store.SeatStatusAsync(_t201, "U2"));
        Assert.Equal(35, await _store.AvailableSeatCountAsync(_t201));
    }

    [Fact]
    public async Task HandleAsync_TripNotKnownYet_WaitsThenAppliesAfterTheTrip()
    {
        // Arrange
        ReceivedEvent held = TestEvents.Envelope("SeatHeld", "S8", TestEvents.SeatEvent(_t208, "U1"), _at1000);

        // Act
        EventHandlingOutcome first = await CreateHandler().HandleAsync(held, CancellationToken.None);
        await ScheduleAsync(_t208);
        EventHandlingOutcome retried = await CreateHandler().HandleAsync(held, CancellationToken.None);

        // Assert
        Assert.Equal(EventHandlingOutcome.RetryLater, first);
        Assert.Equal(EventHandlingOutcome.Consumed, retried);
        Assert.Equal("Held", await _store.SeatStatusAsync(_t208, "U1"));
    }

    [Fact]
    public async Task HandleAsync_TripStillUnknownOnLastAttempt_ParksAndAlerts()
    {
        // Arrange
        ReceivedEvent lastAttempt = TestEvents.Envelope(
            "SeatHeld", "S9", TestEvents.SeatEvent(_t208, "U1"), _at1000, isLastAttempt: true);

        // Act
        EventHandlingOutcome outcome = await CreateHandler().HandleAsync(lastAttempt, CancellationToken.None);

        // Assert
        Assert.Equal(EventHandlingOutcome.RetryLater, outcome);
        Assert.True(_logger.HasAlert("SEAT_EVENT_NOT_APPLIED", "S9", "tripUnknown"));
    }

    [Theory]
    [InlineData("SeatHeld", "noTripId")]
    [InlineData("SeatHeld", "noSeats")]
    [InlineData("SeatHeld", "unknownSeat")]
    [InlineData("SeatReleased", "noReleasedTo")]
    public async Task HandleAsync_UnusableData_SetsAsideAndChangesNoSeat(string eventType, string defect)
    {
        // Arrange
        await ScheduleAsync(_t201);
        JsonObject data = WithDefect(TestEvents.SeatReleased(_t201, "Available", "U1", "U2"), defect);

        // Act
        EventHandlingOutcome outcome = await ApplyAsync(eventType, "S7", data, _at1000);

        // Assert
        Assert.Equal(EventHandlingOutcome.SetAside, outcome);
        Assert.Equal(36, await _store.AvailableSeatCountAsync(_t201));
        Assert.True(_logger.HasAlert("SEAT_EVENT_NOT_APPLIED", "S7", "invalidEventData"));
    }

    [Fact]
    public async Task HandleAsync_EventsWaitingAfterRestart_AppliesEveryOne()
    {
        // Arrange: the queue kept four SeatHeld events while Search was stopped.
        await ScheduleAsync(_t201);

        // Act
        await ApplyAsync("SeatHeld", "S1", TestEvents.SeatEvent(_t201, "U1"), _at1000);
        await ApplyAsync("SeatHeld", "S2", TestEvents.SeatEvent(_t201, "U2"), _at1000.AddMinutes(1));
        await ApplyAsync("SeatHeld", "S3", TestEvents.SeatEvent(_t201, "U3"), _at1000.AddMinutes(2));
        await ApplyAsync("SeatHeld", "S4", TestEvents.SeatEvent(_t201, "U4"), _at1000.AddMinutes(3));

        // Assert
        Assert.Equal(32, (await _store.SearchMumbaiToPuneAsync(_t201))!.SeatsLeft);
    }

    private UpdateSeatStatusesHandler CreateHandler()
    {
        return new UpdateSeatStatusesHandler(WorkerPorts.Writer(_database), _logger);
    }

    private async Task<EventHandlingOutcome> ApplyAsync(string eventType, string eventId, JsonObject data, DateTimeOffset occurredAt)
    {
        ReceivedEvent seatEvent = TestEvents.Envelope(eventType, eventId, data, occurredAt);
        return await CreateHandler().HandleAsync(seatEvent, CancellationToken.None);
    }

    private async Task ScheduleAsync(Guid tripId)
    {
        var create = new CreateTripListing.CreateTripListingHandler(
            WorkerPorts.Writer(_database), new FakeLogger<CreateTripListing.CreateTripListingHandler>());
        ReceivedEvent scheduled = TestEvents.Envelope(
            "TripScheduled", "E-" + tripId, TestEvents.TripScheduled(tripId), TestEvents.OccurredAt);
        await create.HandleAsync(scheduled, CancellationToken.None);
    }

    private static JsonObject WithDefect(JsonObject data, string defect)
    {
        switch (defect)
        {
            case "noTripId":
                data.Remove("tripId");
                break;
            case "noSeats":
                data.Remove("seatNos");
                break;
            case "unknownSeat":
                data["seatNos"]![1] = "Z99";
                break;
            case "noReleasedTo":
                data.Remove("releasedTo");
                break;
            default:
                throw new ArgumentException($"Unknown defect '{defect}'.", nameof(defect));
        }

        return data;
    }
}
