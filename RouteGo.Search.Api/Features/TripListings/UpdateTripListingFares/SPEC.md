# CAP-LST-002 — Update trip listing fares

- Capability: CAP-LST-002
- Canonical spec: `spec-RouteGo.Search.API/capabilities/trip-listings/features/trip-listing-fare-update.md` (v0.1.1; the specs repo is not yet a submodule here)
- Area: TripListings
- Slice: `Features/TripListings/UpdateTripListingFares/`

## Contract
Implemented **verbatim** from the issued spec — if it looks wrong, report drift upstream, never
deviate locally.

- Kind: `worker` — no HTTP operation (so no controller). Trigger `topic: fare-changed`: Fleet's
  `FareChanged` on SNS `routego-fleet-events`, delivered through SQS `search-q` to
  `UpdateTripListingFaresHandler` by `TripListingEventDispatcher`.
- Payload: envelope `eventId`, `occurredAt`, `correlationId`; data `tripId`, `seats[seatNo, fare]`
  (`classFares` and each seat's `class` are not used — the per-seat fares say the same).
- Problem codes: `FLEET_EVENT_NOT_APPLIED` (worker code).

## Acceptance criteria
Each criterion maps to one test in `UpdateTripListingFares.Tests.cs`.

- [x] AC-1: `HandleAsync_UpperBerthsRepriced_LowestFreeFareFollows`
- [x] AC-2: `HandleAsync_BookedSeat_IsRepricedAndStaysBooked`
- [x] AC-3: `HandleAsync_SameEventTwice_FaresUnchanged`
- [x] AC-4: `HandleAsync_OlderChangeArrivesLate_LatestChangeWins`
- [x] AC-5: `HandleAsync_TripNotKnownYet_WaitsThenAppliesAfterTheTrip`
- [x] AC-6: `HandleAsync_TripStillUnknownOnLastAttempt_ParksAndAlerts`
- [x] AC-7: `HandleAsync_UnusableData_SetsAsideAndChangesNoFare`

## Invariants touched
- `trip_listings.fares_changed_at` orders fare changes: one not later than it changes nothing.
- All named seats change together, or none.
