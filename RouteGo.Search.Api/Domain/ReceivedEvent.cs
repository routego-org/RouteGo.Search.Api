namespace RouteGo.Search.Api.Domain;

/// <summary>
/// One event taken off the search queue, as a trip-listing slice sees it: the RouteGo envelope
/// fields it needs, the event's own data, and whether this is the queue's last automatic attempt
/// before the message is parked.
/// </summary>
public sealed record ReceivedEvent
{
    /// <summary>The event ID from the producer's envelope; the same on every repeat delivery.</summary>
    public required string EventId { get; init; }

    /// <summary>The producer's event type, for example <c>TripScheduled</c>.</summary>
    public required string EventType { get; init; }

    /// <summary>When the change the event reports happened, in UTC.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The correlation ID that ties this event to the request that caused it.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>The event-specific data object.</summary>
    public required JsonElement Data { get; init; }

    /// <summary>Whether a failure now parks the event instead of retrying it.</summary>
    public required bool IsLastAttempt { get; init; }
}
