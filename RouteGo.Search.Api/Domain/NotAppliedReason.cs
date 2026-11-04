namespace RouteGo.Search.Api.Domain;

/// <summary>
/// Why an event could not be applied — the <c>reason</c> of the FLEET_EVENT_NOT_APPLIED and
/// SEAT_EVENT_NOT_APPLIED alerts, logged by its camelCase name.
/// </summary>
public enum NotAppliedReason
{
    /// <summary>The event's data is missing or unusable; it is never retried.</summary>
    InvalidEventData,

    /// <summary>The event's trip is still unknown after every automatic retry.</summary>
    TripUnknown,

    /// <summary>A temporary failure lasted through every automatic retry.</summary>
    RepeatedFailure,
}
