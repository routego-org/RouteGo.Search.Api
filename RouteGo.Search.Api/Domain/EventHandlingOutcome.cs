namespace RouteGo.Search.Api.Domain;

/// <summary>What the queue listener does with a message once a trip-listing slice has handled its event.</summary>
public enum EventHandlingOutcome
{
    /// <summary>Done with — applied, or a repeat that changes nothing: the message is deleted.</summary>
    Consumed,

    /// <summary>Left on the queue, so it is delivered again later — or parked after the last attempt.</summary>
    RetryLater,

    /// <summary>Unusable data, never retried: moved to the dead-letter queue at once for the platform engineer.</summary>
    SetAside,
}
