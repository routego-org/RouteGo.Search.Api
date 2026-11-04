namespace RouteGo.Search.Api.Infrastructure.Messaging;

/// <summary>
/// The result of reading one queue message: the event when the RouteGo envelope is complete, or
/// what is wrong with the message when it is not.
/// </summary>
public sealed record EventEnvelopeReading(ReceivedEvent? Event, string? Problem);
