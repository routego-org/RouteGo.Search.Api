namespace RouteGo.Search.Api.Features.TripListings.UpdateTripListingFares;

/// <summary>
/// Replaces a trip listing's seat fares with the ones in Fleet's FareChanged, whatever each seat's
/// status. The time the change occurred orders changes: one that is not later than the last one
/// applied changes nothing. A change for a trip not known yet waits until the trip arrives.
/// </summary>
public sealed class UpdateTripListingFaresHandler
{
    /// <summary>The producer's event type that starts this slice.</summary>
    public const string TriggerEventType = "FareChanged";

    private const string WriteAction = "UpdateTripListingFares";
    private const long PaisePerRupee = 100;

    private static readonly JsonSerializerOptions _eventJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private const string LockListingSql = """
        SELECT details_received_at, fares_changed_at
        FROM trip_listings.trip_listings
        WHERE trip_id = @TripId
        FOR UPDATE;
        """;

    private const string CountSeatsSql = """
        SELECT count(*) FROM trip_listings.listing_seats WHERE trip_id = @TripId AND seat_no = ANY(@SeatNos);
        """;

    private const string UpdateFaresSql = """
        UPDATE trip_listings.listing_seats AS s
        SET fare = v.fare
        FROM unnest(@SeatNos, @Fares) AS v(seat_no, fare)
        WHERE s.trip_id = @TripId AND s.seat_no = v.seat_no;
        UPDATE trip_listings.trip_listings SET fares_changed_at = @OccurredAt WHERE trip_id = @TripId;
        """;

    private readonly IWriterConnectionFactory _writerFactory;
    private readonly ILogger<UpdateTripListingFaresHandler> _logger;

    public UpdateTripListingFaresHandler(IWriterConnectionFactory writerFactory, ILogger<UpdateTripListingFaresHandler> logger)
    {
        _writerFactory = writerFactory;
        _logger = logger;
    }

    /// <summary>Applies one FareChanged event and says what the queue should do with its message.</summary>
    public async Task<EventHandlingOutcome> HandleAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        FareChangedData? data = ReadData(receivedEvent.Data);
        string? problem = DescribeProblem(data);
        if (problem != null)
        {
            return SetAside(receivedEvent, problem);
        }

        FareChangeResult result = await ApplyAsync(data!, receivedEvent.OccurredAt, cancellationToken);
        switch (result)
        {
            case FareChangeResult.Applied:
                _logger.LogInformation(
                    "Fares of trip listing {TripId} replaced by FareChanged event {EventId} (correlation {CorrelationId})",
                    data!.TripId, receivedEvent.EventId, receivedEvent.CorrelationId);
                return EventHandlingOutcome.Consumed;
            case FareChangeResult.NotLater:
            case FareChangeResult.KnownOnlyAsCancelled:
                _logger.LogInformation(
                    "FareChanged event {EventId} (correlation {CorrelationId}) changes nothing for trip listing {TripId}: {Result}",
                    receivedEvent.EventId, receivedEvent.CorrelationId, data!.TripId, result);
                return EventHandlingOutcome.Consumed;
            case FareChangeResult.UnknownSeat:
                return SetAside(receivedEvent, "a seatNo is not a seat of the trip listing");
            case FareChangeResult.TripUnknown:
                return WaitForTrip(receivedEvent, data!.TripId!.Value);
            default:
                throw new InvalidOperationException($"Unhandled fare change result '{result}'.");
        }
    }

    private EventHandlingOutcome SetAside(ReceivedEvent receivedEvent, string problem)
    {
        EventAlerts.LogNotApplied(
            _logger, ProblemTypes.FleetEventNotApplied, receivedEvent, NotAppliedReason.InvalidEventData, problem);
        return EventHandlingOutcome.SetAside;
    }

    // The trip's TripScheduled may simply not have arrived yet; on the last attempt the queue parks
    // the event and the platform engineer is told.
    private EventHandlingOutcome WaitForTrip(ReceivedEvent receivedEvent, Guid tripId)
    {
        if (receivedEvent.IsLastAttempt)
        {
            EventAlerts.LogNotApplied(
                _logger, ProblemTypes.FleetEventNotApplied, receivedEvent, NotAppliedReason.TripUnknown,
                $"trip {tripId} is still unknown");
            return EventHandlingOutcome.RetryLater;
        }

        _logger.LogWarning(
            "FareChanged event {EventId} (correlation {CorrelationId}) waits for trip {TripId}, not known yet",
            receivedEvent.EventId, receivedEvent.CorrelationId, tripId);
        return EventHandlingOutcome.RetryLater;
    }

    private static FareChangedData? ReadData(JsonElement data)
    {
        try
        {
            return data.Deserialize<FareChangedData>(_eventJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? DescribeProblem(FareChangedData? data)
    {
        if (data is null || data.TripId is null)
        {
            return "tripId is missing";
        }

        if (data.Seats is null || data.Seats.Count == 0)
        {
            return "seats is missing or empty";
        }

        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (SeatFareData? seat in data.Seats)
        {
            if (seat is null || string.IsNullOrWhiteSpace(seat.SeatNo) || seat.Fare is null || seat.Fare < 0)
            {
                return "a seat has no seatNo or fare";
            }

            if (!labels.Add(seat.SeatNo))
            {
                return "two seats have the same seatNo";
            }
        }

        return null;
    }

    // All of the trip's fares change together, or none.
    private async Task<FareChangeResult> ApplyAsync(
        FareChangedData data, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        await using IWriterConnection writer =
            await _writerFactory.OpenWriteTransactionAsync(WriteAction, cancellationToken);
        ListingStateRow? listing = await writer.Connection.QuerySingleOrDefaultAsync<ListingStateRow>(new CommandDefinition(
            LockListingSql, new { data.TripId }, writer.Transaction, cancellationToken: cancellationToken));
        FareChangeResult? refusal = Decide(listing, occurredAt);
        if (refusal != null)
        {
            return refusal.Value;
        }

        object parameters = FareParameters(data, occurredAt);
        long matchingSeats = await writer.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            CountSeatsSql, parameters, writer.Transaction, cancellationToken: cancellationToken));
        if (matchingSeats != data.Seats!.Count)
        {
            return FareChangeResult.UnknownSeat;
        }

        await writer.Connection.ExecuteAsync(new CommandDefinition(
            UpdateFaresSql, parameters, writer.Transaction, cancellationToken: cancellationToken));
        await writer.CommitAsync(cancellationToken);
        return FareChangeResult.Applied;
    }

    private static FareChangeResult? Decide(ListingStateRow? listing, DateTimeOffset occurredAt)
    {
        if (listing is null)
        {
            return FareChangeResult.TripUnknown;
        }

        // Known only from an early cancellation: the trip will never be searchable.
        if (listing.DetailsReceivedAt is null)
        {
            return FareChangeResult.KnownOnlyAsCancelled;
        }

        if (listing.FaresChangedAt != null && occurredAt.UtcDateTime <= listing.FaresChangedAt.Value)
        {
            return FareChangeResult.NotLater;
        }

        return null;
    }

    // Fares arrive in whole rupees and are stored in paise.
    private static object FareParameters(FareChangedData data, DateTimeOffset occurredAt)
    {
        List<SeatFareData?> seats = data.Seats!;
        string[] seatNos = new string[seats.Count];
        long[] fares = new long[seats.Count];
        for (int index = 0; index < seats.Count; index++)
        {
            seatNos[index] = seats[index]!.SeatNo!;
            fares[index] = seats[index]!.Fare!.Value * PaisePerRupee;
        }

        return new { data.TripId, SeatNos = seatNos, Fares = fares, OccurredAt = occurredAt.UtcDateTime };
    }

    private enum FareChangeResult
    {
        Applied,
        TripUnknown,
        KnownOnlyAsCancelled,
        NotLater,
        UnknownSeat,
    }

    private sealed class FareChangedData
    {
        public Guid? TripId { get; init; }

        public List<SeatFareData?>? Seats { get; init; }
    }

    private sealed class SeatFareData
    {
        public string? SeatNo { get; init; }

        public int? Fare { get; init; }
    }

    private sealed class ListingStateRow
    {
        public DateTime? DetailsReceivedAt { get; init; }

        public DateTime? FaresChangedAt { get; init; }
    }
}
