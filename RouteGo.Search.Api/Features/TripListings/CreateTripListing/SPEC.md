# CAP-LST-001 — Create trip listing

- Capability: CAP-LST-001
- Canonical spec: `spec-RouteGo.Search.API/capabilities/trip-listings/features/trip-listing-creation.md` (v0.1.1; the specs repo is not yet a submodule here)
- Area: TripListings
- Slice: `Features/TripListings/CreateTripListing/`

## Contract
Implemented **verbatim** from the issued spec — if it looks wrong, report drift upstream, never
deviate locally.

- Kind: `worker` — no HTTP operation (so no controller). Trigger `topic: trip-scheduled`: Fleet
  publishes to SNS `routego-fleet-events`; SQS `search-q` is subscribed with raw delivery and an
  `eventType` filter; `Infrastructure/Messaging/SearchQueueListener` hands each `TripScheduled` to
  `CreateTripListingHandler` through `TripListingEventDispatcher`.
- Payload: envelope `eventId`, `eventType`, `occurredAt`, `correlationId`, `data`; data fields per
  Fleet's `architecture/fleet-events.md` (`tripId`, `operatorName`, `rating`, `busId`, `busName`,
  `busType`, `isAc`, `amenities`, `source`, `destination`, `stops`, `serviceDate`, `departureUtc`,
  `arrivalUtc`, `seats[seatNo, class, fare]`).
- Problem codes: `FLEET_EVENT_NOT_APPLIED` (worker code — an error log line naming the event ID,
  event type and reason).

## Acceptance criteria
Each criterion maps to one test in `CreateTripListing.Tests.cs`.

- [x] AC-1: `HandleAsync_NewTrip_CreatesSearchableListingWithAvailableSeats`
- [x] AC-2: `HandleAsync_SameEventTwice_KeepsSeatsAndTheirStatuses`
- [x] AC-3: `HandleAsync_DifferentEventForKnownTrip_ChangesNothing`
- [x] AC-4: `HandleAsync_CancellationArrivedFirst_StaysCancelledWithDetails`
- [x] AC-5: `HandleAsync_UnusableData_SetsAsideAndAlerts`
- [x] AC-6: `HandleAsync_DatabaseUnavailable_CreatesNothingAndReplayCreates` — the handler fails
  with nothing half-created and a replay creates the listing; the `repeatedFailure` alert on the
  last attempt is written by `SearchQueueListener`, which has no automated test (it needs SQS)
- [x] AC-7: `HandleAsync_EventsWaitingAfterRestart_CreatesEveryListing` — the waiting itself is
  the queue's (SQS keeps the messages)

## Invariants touched
- A listing's details, stops and seats are written once, together, in one transaction.
- Stops along the way are the event's stops other than the first and last.

## Decisions taken with the developer
- Built like Notification's workers: an in-process queue listener in this API, one slice per event.
- One queue `search-q` (+ `search-dlq`, 5 receives) for all Fleet and seat events.
- Unusable data is never retried: alert, then moved to `search-dlq` at once.
- Data the database would refuse (rating outside 0–5, unknown bus type or amenity, arrival not
  after departure, a stop outside the journey) is unusable data too, so it never loops on retries.
