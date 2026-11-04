namespace RouteGo.Search.Api.Common;

/// <summary>
/// The one shape of the "event not applied" alert line, so the platform alarm can match every one
/// of them the same way: the cataloged code first, then the event ID, event type, reason and a
/// detail that names a field, never personal data.
/// </summary>
public static class EventAlerts
{
    /// <summary>Logs that <paramref name="receivedEvent"/> was not applied, for the platform alarm channel.</summary>
    public static void LogNotApplied(
        ILogger logger, ProblemType problemType, ReceivedEvent receivedEvent, NotAppliedReason reason, string detail)
    {
        string reasonName = JsonNamingPolicy.CamelCase.ConvertName(reason.ToString());
        logger.LogError(
            "{ProblemCode}: {EventType} event {EventId} (correlation {CorrelationId}) not applied, reason {Reason}: {Detail}",
            problemType.Code, receivedEvent.EventType, receivedEvent.EventId, receivedEvent.CorrelationId, reasonName, detail);
    }
}
