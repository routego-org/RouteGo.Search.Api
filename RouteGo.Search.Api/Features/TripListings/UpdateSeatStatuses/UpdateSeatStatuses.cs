namespace RouteGo.Search.Api.Features.TripListings.UpdateSeatStatuses;

/// <summary>
/// Keeps each listed seat's status from Seat Inventory's six seat events. Every seat records the time
/// of the last seat event applied to it, and an event that is not later than that changes nothing for
/// that seat — so repeats, late and reordered events never corrupt seats left. A seat event for a
/// trip not known yet waits until the trip arrives.
/// </summary>
public sealed class UpdateSeatStatusesHandler
{
    /// <summary>A hold was created: its seats become Held.</summary>
    public const string SeatHeld = "SeatHeld";

    /// <summary>A hold was confirmed: its seats become Booked.</summary>
    public const string SeatBooked = "SeatBooked";

    /// <summary>A hold's seats were released: they become what they were released to.</summary>
    public const string SeatReleased = "SeatReleased";

    /// <summary>An operator blocked seats: they become Blocked.</summary>
    public const string SeatBlocked = "SeatBlocked";

    /// <summary>An operator unblocked seats: they become Available.</summary>
    public const string SeatUnblocked = "SeatUnblocked";

    /// <summary>A booking moved seats: each from-seat becomes Available and each to-seat Booked.</summary>
    public const string SeatChanged = "SeatChanged";

    private const string WriteAction = "UpdateSeatStatuses";
    private const string Available = "Available";
    private const string Held = "Held";
    private const string Booked = "Booked";
    private const string Blocked = "Blocked";

    private static readonly JsonSerializerOptions _eventJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private const string LockListingSql = """
        SELECT details_received_at FROM trip_listings.trip_listings WHERE trip_id = @TripId FOR UPDATE;
        """;

    private const string CountSeatsSql = """
        SELECT count(*) FROM trip_listings.listing_seats WHERE trip_id = @TripId AND seat_no = ANY(@SeatNos);
        """;

    // Each seat is judged on its own: only a change later than the last one applied to it counts.
    private const string UpdateStatusesSql = """
        UPDATE trip_listings.listing_seats AS s
        SET status = v.status, status_changed_at = @OccurredAt
        FROM unnest(@SeatNos, @Statuses) AS v(seat_no, status)
        WHERE s.trip_id = @TripId
          AND s.seat_no = v.seat_no
          AND (s.status_changed_at IS NULL OR s.status_changed_at < @OccurredAt);
        """;

    private readonly IWriterConnectionFactory _writerFactory;
    private readonly ILogger<UpdateSeatStatusesHandler> _logger;

    public UpdateSeatStatusesHandler(IWriterConnectionFactory writerFactory, ILogger<UpdateSeatStatusesHandler> logger)
    {
        _writerFactory = writerFactory;
        _logger = logger;
    }

    /// <summary>Applies one seat event and says what the queue should do with its message.</summary>
    public async Task<EventHandlingOutcome> HandleAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        SeatEventData? data = ReadData(receivedEvent.Data);
        SeatChanges changes = ToSeatChanges(receivedEvent.EventType, data);
        if (changes.Problem != null)
        {
            return SetAside(receivedEvent, changes.Problem);
        }

        SeatUpdate update = await ApplyAsync(data!.TripId!.Value, changes, receivedEvent.OccurredAt, cancellationToken);
        switch (update.Result)
        {
            case SeatUpdateResult.Applied:
                _logger.LogInformation(
                    "{EventType} event {EventId} (correlation {CorrelationId}) changed {SeatCount} seats of trip listing {TripId}",
                    receivedEvent.EventType, receivedEvent.EventId, receivedEvent.CorrelationId, update.ChangedSeats, data.TripId);
                return EventHandlingOutcome.Consumed;
            case SeatUpdateResult.KnownOnlyAsCancelled:
                _logger.LogInformation(
                    "{EventType} event {EventId} (correlation {CorrelationId}) changes nothing: trip {TripId} is known only as cancelled",
                    receivedEvent.EventType, receivedEvent.EventId, receivedEvent.CorrelationId, data.TripId);
                return EventHandlingOutcome.Consumed;
            case SeatUpdateResult.UnknownSeat:
                return SetAside(receivedEvent, "a seat label is not a seat of the trip listing");
            case SeatUpdateResult.TripUnknown:
                return WaitForTrip(receivedEvent, data.TripId.Value);
            default:
                throw new InvalidOperationException($"Unhandled seat update result '{update.Result}'.");
        }
    }

    private EventHandlingOutcome SetAside(ReceivedEvent receivedEvent, string problem)
    {
        EventAlerts.LogNotApplied(
            _logger, ProblemTypes.SeatEventNotApplied, receivedEvent, NotAppliedReason.InvalidEventData, problem);
        return EventHandlingOutcome.SetAside;
    }

    // The trip's TripScheduled may simply not have arrived yet; on the last attempt the queue parks
    // the event and the platform engineer is told.
    private EventHandlingOutcome WaitForTrip(ReceivedEvent receivedEvent, Guid tripId)
    {
        if (receivedEvent.IsLastAttempt)
        {
            EventAlerts.LogNotApplied(
                _logger, ProblemTypes.SeatEventNotApplied, receivedEvent, NotAppliedReason.TripUnknown,
                $"trip {tripId} is still unknown");
            return EventHandlingOutcome.RetryLater;
        }

        _logger.LogWarning(
            "{EventType} event {EventId} (correlation {CorrelationId}) waits for trip {TripId}, not known yet",
            receivedEvent.EventType, receivedEvent.EventId, receivedEvent.CorrelationId, tripId);
        return EventHandlingOutcome.RetryLater;
    }

    private static SeatEventData? ReadData(JsonElement data)
    {
        try
        {
            return data.Deserialize<SeatEventData>(_eventJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // What each named seat becomes. A seat named twice in one event is unusable data: the event
    // would not say which status it ends in.
    private static SeatChanges ToSeatChanges(string eventType, SeatEventData? data)
    {
        if (data is null || data.TripId is null)
        {
            return SeatChanges.Unusable("tripId is missing");
        }

        if (eventType == SeatChanged)
        {
            return FromMoves(data.Changes);
        }

        string? status = StatusFor(eventType, data.ReleasedTo);
        if (status is null)
        {
            return SeatChanges.Unusable("releasedTo is missing or not Available or Blocked");
        }

        if (data.SeatNos is null || data.SeatNos.Count == 0)
        {
            return SeatChanges.Unusable("seatNos is missing or empty");
        }

        var changes = new SeatChanges();
        foreach (string? seatNo in data.SeatNos)
        {
            changes.Add(seatNo, status);
        }

        return changes;
    }

    private static SeatChanges FromMoves(List<SeatMoveData?>? moves)
    {
        if (moves is null || moves.Count == 0)
        {
            return SeatChanges.Unusable("changes is missing or empty");
        }

        var changes = new SeatChanges();
        foreach (SeatMoveData? move in moves)
        {
            changes.Add(move?.FromSeatNo, Available);
            changes.Add(move?.ToSeatNo, Booked);
        }

        return changes;
    }

    private static string? StatusFor(string eventType, string? releasedTo)
    {
        switch (eventType)
        {
            case SeatHeld:
                return Held;
            case SeatBooked:
                return Booked;
            case SeatBlocked:
                return Blocked;
            case SeatUnblocked:
                return Available;
            case SeatReleased:
                if (releasedTo == Available || releasedTo == Blocked)
                {
                    return releasedTo;
                }

                return null;
            default:
                throw new InvalidOperationException($"'{eventType}' is not a seat event.");
        }
    }

    private async Task<SeatUpdate> ApplyAsync(
        Guid tripId, SeatChanges changes, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        await using IWriterConnection writer =
            await _writerFactory.OpenWriteTransactionAsync(WriteAction, cancellationToken);
        ListingStateRow? listing = await writer.Connection.QuerySingleOrDefaultAsync<ListingStateRow>(new CommandDefinition(
            LockListingSql, new { TripId = tripId }, writer.Transaction, cancellationToken: cancellationToken));
        if (listing is null)
        {
            return new SeatUpdate(SeatUpdateResult.TripUnknown, 0);
        }

        if (listing.DetailsReceivedAt is null)
        {
            return new SeatUpdate(SeatUpdateResult.KnownOnlyAsCancelled, 0);
        }

        var parameters = new
        {
            TripId = tripId,
            SeatNos = changes.SeatNos.ToArray(),
            Statuses = changes.Statuses.ToArray(),
            OccurredAt = occurredAt.UtcDateTime,
        };
        long matchingSeats = await writer.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            CountSeatsSql, parameters, writer.Transaction, cancellationToken: cancellationToken));
        if (matchingSeats != changes.SeatNos.Count)
        {
            return new SeatUpdate(SeatUpdateResult.UnknownSeat, 0);
        }

        int changedSeats = await writer.Connection.ExecuteAsync(new CommandDefinition(
            UpdateStatusesSql, parameters, writer.Transaction, cancellationToken: cancellationToken));
        await writer.CommitAsync(cancellationToken);
        return new SeatUpdate(SeatUpdateResult.Applied, changedSeats);
    }

    private enum SeatUpdateResult
    {
        Applied,
        TripUnknown,
        KnownOnlyAsCancelled,
        UnknownSeat,
    }

    private sealed record SeatUpdate(SeatUpdateResult Result, int ChangedSeats);

    // The seats an event names and the status each one becomes, or why the event is unusable.
    private sealed class SeatChanges
    {
        public List<string> SeatNos { get; } = new List<string>();

        public List<string> Statuses { get; } = new List<string>();

        public string? Problem { get; private set; }

        public static SeatChanges Unusable(string problem)
        {
            var changes = new SeatChanges();
            changes.Problem = problem;
            return changes;
        }

        public void Add(string? seatNo, string status)
        {
            if (string.IsNullOrWhiteSpace(seatNo))
            {
                Problem ??= "a seat label is missing";
                return;
            }

            if (SeatNos.Contains(seatNo))
            {
                Problem ??= "a seat is named twice";
                return;
            }

            SeatNos.Add(seatNo);
            Statuses.Add(status);
        }
    }

    private sealed class SeatEventData
    {
        public Guid? TripId { get; init; }

        public List<string?>? SeatNos { get; init; }

        public string? ReleasedTo { get; init; }

        public List<SeatMoveData?>? Changes { get; init; }
    }

    private sealed class SeatMoveData
    {
        public string? FromSeatNo { get; init; }

        public string? ToSeatNo { get; init; }
    }

    private sealed class ListingStateRow
    {
        public DateTime? DetailsReceivedAt { get; init; }
    }
}
