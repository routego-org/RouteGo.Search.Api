using RouteGo.Search.Api.Features.TripListings.CancelTripListing;
using RouteGo.Search.Api.Features.TripListings.CreateTripListing;
using RouteGo.Search.Api.Features.TripListings.UpdateSeatStatuses;
using RouteGo.Search.Api.Features.TripListings.UpdateTripListingFares;

namespace RouteGo.Search.Api.Infrastructure.Messaging;

/// <summary>
/// Hands each event to the trip-listing slice that owns its event type, constructing that slice's
/// handler from the shared ports. One case per slice; an event type with no case is not Search's.
/// </summary>
public sealed class TripListingEventDispatcher
{
    private readonly IWriterConnectionFactory _writerFactory;
    private readonly IClock _clock;
    private readonly ILoggerFactory _loggerFactory;

    public TripListingEventDispatcher(IWriterConnectionFactory writerFactory, IClock clock, ILoggerFactory loggerFactory)
    {
        _writerFactory = writerFactory;
        _clock = clock;
        _loggerFactory = loggerFactory;
    }

    /// <summary>Handles the event, or returns null when no slice owns its event type.</summary>
    public async Task<EventHandlingOutcome?> DispatchAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        switch (receivedEvent.EventType)
        {
            case CreateTripListingHandler.TriggerEventType:
                var create = new CreateTripListingHandler(
                    _writerFactory, _loggerFactory.CreateLogger<CreateTripListingHandler>());
                return await create.HandleAsync(receivedEvent, cancellationToken);
            case UpdateTripListingFaresHandler.TriggerEventType:
                var fares = new UpdateTripListingFaresHandler(
                    _writerFactory, _loggerFactory.CreateLogger<UpdateTripListingFaresHandler>());
                return await fares.HandleAsync(receivedEvent, cancellationToken);
            case CancelTripListingHandler.TriggerEventType:
                var cancel = new CancelTripListingHandler(
                    _writerFactory, _clock, _loggerFactory.CreateLogger<CancelTripListingHandler>());
                return await cancel.HandleAsync(receivedEvent, cancellationToken);
            case UpdateSeatStatusesHandler.SeatHeld:
            case UpdateSeatStatusesHandler.SeatBooked:
            case UpdateSeatStatusesHandler.SeatReleased:
            case UpdateSeatStatusesHandler.SeatBlocked:
            case UpdateSeatStatusesHandler.SeatUnblocked:
            case UpdateSeatStatusesHandler.SeatChanged:
                var seats = new UpdateSeatStatusesHandler(
                    _writerFactory, _loggerFactory.CreateLogger<UpdateSeatStatusesHandler>());
                return await seats.HandleAsync(receivedEvent, cancellationToken);
            default:
                return null;
        }
    }

    /// <summary>The alert code for an event of this type that could not be applied.</summary>
    public static ProblemType NotAppliedProblemFor(string eventType)
    {
        if (eventType.StartsWith("Seat", StringComparison.Ordinal))
        {
            return ProblemTypes.SeatEventNotApplied;
        }

        return ProblemTypes.FleetEventNotApplied;
    }
}
