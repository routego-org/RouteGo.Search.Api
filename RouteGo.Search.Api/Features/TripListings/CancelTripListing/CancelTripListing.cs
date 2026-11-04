namespace RouteGo.Search.Api.Features.TripListings.CancelTripListing;

/// <summary>
/// Takes a cancelled trip out of search for good, from Fleet's TripCancelled. A cancellation that
/// arrives before the trip itself is remembered as a cancelled listing with no details, so the
/// trip's TripScheduled can never make it searchable.
/// </summary>
public sealed class CancelTripListingHandler
{
    /// <summary>The producer's event type that starts this slice.</summary>
    public const string TriggerEventType = "TripCancelled";

    private const string WriteAction = "CancelTripListing";

    private static readonly JsonSerializerOptions _eventJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    // Cancelling an already-cancelled listing changes nothing, so a repeat keeps the first time.
    private const string CancelSql = """
        INSERT INTO trip_listings.trip_listings (trip_id, is_cancelled, cancelled_at)
        VALUES (@TripId, true, @CancelledAt)
        ON CONFLICT (trip_id) DO UPDATE
        SET is_cancelled = true, cancelled_at = EXCLUDED.cancelled_at
        WHERE NOT trip_listings.is_cancelled;
        """;

    private readonly IWriterConnectionFactory _writerFactory;
    private readonly IClock _clock;
    private readonly ILogger<CancelTripListingHandler> _logger;

    public CancelTripListingHandler(IWriterConnectionFactory writerFactory, IClock clock, ILogger<CancelTripListingHandler> logger)
    {
        _writerFactory = writerFactory;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Applies one TripCancelled event and says what the queue should do with its message.</summary>
    public async Task<EventHandlingOutcome> HandleAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        TripCancelledData? data = ReadData(receivedEvent.Data);
        if (data is null || data.TripId is null)
        {
            EventAlerts.LogNotApplied(
                _logger, ProblemTypes.FleetEventNotApplied, receivedEvent, NotAppliedReason.InvalidEventData,
                "tripId is missing");
            return EventHandlingOutcome.SetAside;
        }

        await using IWriterConnection writer =
            await _writerFactory.OpenWriteTransactionAsync(WriteAction, cancellationToken);
        var parameters = new { data.TripId, CancelledAt = _clock.UtcNow };
        await writer.Connection.ExecuteAsync(new CommandDefinition(
            CancelSql, parameters, writer.Transaction, cancellationToken: cancellationToken));
        await writer.CommitAsync(cancellationToken);

        // The reason is kept on this log line only: search never shows a cancelled trip.
        _logger.LogInformation(
            "Trip listing {TripId} cancelled by TripCancelled event {EventId} (correlation {CorrelationId}), reason {CancellationReason}",
            data.TripId, receivedEvent.EventId, receivedEvent.CorrelationId, data.Reason);
        return EventHandlingOutcome.Consumed;
    }

    private static TripCancelledData? ReadData(JsonElement data)
    {
        try
        {
            return data.Deserialize<TripCancelledData>(_eventJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class TripCancelledData
    {
        public Guid? TripId { get; init; }

        public string? Reason { get; init; }
    }
}
