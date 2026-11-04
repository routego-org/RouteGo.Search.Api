# CAP-LST-003 — Cancel trip listing

- Capability: CAP-LST-003
- Canonical spec: `spec-RouteGo.Search.API/capabilities/trip-listings/features/trip-listing-cancellation.md` (v0.1.1; the specs repo is not yet a submodule here)
- Area: TripListings
- Slice: `Features/TripListings/CancelTripListing/`

## Contract
Implemented **verbatim** from the issued spec — if it looks wrong, report drift upstream, never
deviate locally.

- Kind: `worker` — no HTTP operation (so no controller). Trigger `topic: trip-cancelled`: Fleet's
  `TripCancelled` on SNS `routego-fleet-events`, delivered through SQS `search-q` to
  `CancelTripListingHandler` by `TripListingEventDispatcher`.
- Payload: envelope `eventId`, `correlationId`; data `tripId`, `reason` (log line only).
- Problem codes: `FLEET_EVENT_NOT_APPLIED` (worker code).

## Acceptance criteria
Each criterion maps to one test in `CancelTripListing.Tests.cs`.

- [x] AC-1: `HandleAsync_SearchableTrip_LeavesSearch`
- [x] AC-2: `HandleAsync_OnlyTripFromCity_CityIsNoLongerSuggested`
- [x] AC-3: `HandleAsync_SameEventTwice_StaysCancelledUnchanged`
- [x] AC-4: `HandleAsync_TripNotKnownYet_IsRememberedAndNeverSearchable`
- [x] AC-5: `HandleAsync_LaterFleetAndSeatEvents_TripStaysCancelled`
- [x] AC-6: `HandleAsync_NoTripId_SetsAsideAndAlerts`

## Invariants touched
- A cancelled listing stays cancelled: no statement in any slice sets `is_cancelled` back to false.
- `cancelled_at` is when Search learned of the cancellation (the clock), kept from the first delivery.
