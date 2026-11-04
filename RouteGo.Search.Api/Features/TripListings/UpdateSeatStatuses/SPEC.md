# CAP-LST-004 — Update seat statuses

- Capability: CAP-LST-004
- Canonical spec: `spec-RouteGo.Search.API/capabilities/trip-listings/features/seat-availability.md` (v0.1.1; the specs repo is not yet a submodule here)
- Area: TripListings
- Slice: `Features/TripListings/UpdateSeatStatuses/`

## Contract
Implemented **verbatim** from the issued spec — if it looks wrong, report drift upstream, never
deviate locally.

- Kind: `worker` — no HTTP operation (so no controller). Trigger `topic: seat-events`: Seat
  Inventory's six seat events on its "inventory events stream", assumed to be SNS
  `routego-inventory-events` (Seat Inventory has not built its publisher yet — confirm the name),
  delivered through SQS `search-q` to `UpdateSeatStatusesHandler` by `TripListingEventDispatcher`.
- Payload: envelope `eventId`, `eventType`, `occurredAt`, `correlationId`; data per Seat
  Inventory's `architecture/seat-events.md`: `tripId`, `seatNos`, `releasedTo` (SeatReleased),
  `changes[fromSeatNo, toSeatNo]` (SeatChanged). Booking IDs, hold expiry, release reason and
  operator ID are not used.
- Problem codes: `SEAT_EVENT_NOT_APPLIED` (worker code).

## Acceptance criteria
Each criterion maps to one test in `UpdateSeatStatuses.Tests.cs`.

- [x] AC-1: `HandleAsync_SeatHeld_SeatsAreNoLongerFree`
- [x] AC-2: `HandleAsync_SeatBookedAfterHeld_SeatsAreBooked`
- [x] AC-3: `HandleAsync_SeatReleasedToAvailable_SeatsAreFreeAgain`
- [x] AC-4: `HandleAsync_SeatReleasedToBlocked_SeatStaysBlocked`
- [x] AC-5: `HandleAsync_OperatorBlocksThenUnblocks_SeatsFollow`
- [x] AC-6: `HandleAsync_SeatChanged_MovesTheBookingAndKeepsSeatsLeft`
- [x] AC-7: `HandleAsync_AllCheaperSeatsBooked_LowestFreeFareFollowsThenSoldOut`
- [x] AC-8: `HandleAsync_SameEventTwice_ChangesNothingMore`
- [x] AC-9: `HandleAsync_OlderEventArrivesLate_SeatKeepsLaterStatus`
- [x] AC-10: `HandleAsync_DuplicatesAndReordering_CountsStayRight`
- [x] AC-11: `HandleAsync_TripNotKnownYet_WaitsThenAppliesAfterTheTrip`
- [x] AC-12: `HandleAsync_TripStillUnknownOnLastAttempt_ParksAndAlerts`
- [x] AC-13: `HandleAsync_UnusableData_SetsAsideAndChangesNoSeat`
- [x] AC-14: `HandleAsync_EventsWaitingAfterRestart_AppliesEveryOne`

## Invariants touched
- Latest change per seat wins: `listing_seats.status_changed_at`, judged seat by seat.
- Seats left, lowest free fare and sold out are derived from the seats by the search queries,
  never counted up or down here.
- A seat named twice in one event is unusable data (the event would not say its final status).
