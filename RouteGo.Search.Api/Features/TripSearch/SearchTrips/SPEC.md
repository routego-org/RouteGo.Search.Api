# CAP-SRCH-001 — Search trips

- Capability: CAP-SRCH-001
- Canonical spec: `spec-RouteGo.Search.API/capabilities/trip-search/features/trip-search.md` (v0.1.1; the specs repo is not yet a submodule here)
- Area: TripSearch
- Slice: `Features/TripSearch/SearchTrips/`

## Contract
Implemented **verbatim** from the issued contract — if it looks wrong, report drift upstream
(`skills-spec-authoring`), never deviate locally.

- Operation: `searchTrips` — `contracts/trip-search/openapi.yaml` → `contracts/trip-search/trip-listing.yaml`
- Route / verb: `GET /search/trips`
- Request: query `fromCityId`, `toCityId`, `date`, `busTypes`, `isAc`, `minFare`, `maxFare`,
  `departAfter`, `departBefore`, `minRating`, `sort`, `page` (see `searchTrips`). Every value is
  bound as text so a malformed one is refused with `VALIDATION_FAILED` naming the field; the
  generated OpenAPI document therefore shows them as strings — the contract is the authority.
- Response: 200 `TripListingPage`
- Problem codes: `VALIDATION_FAILED` (400) — `ProblemTypes.ValidationFailed`

## Acceptance criteria
Each criterion maps to one test in `SearchTrips.Tests.cs` (seed data in `SearchTrips.Seed.Tests.cs`).

- [x] AC-1: `SearchTrips_MumbaiToPuneOnDate_ListsTripsWithTheirDetails`
- [x] AC-2: `SearchTrips_StopAlongTheWay_DoesNotMatch`
- [x] AC-3: `SearchTrips_CancelledTrip_NeverAppears`
- [x] AC-4: `SearchTrips_TripInsideSearchCutoff_LeavesTheAnswer`
- [x] AC-5: `SearchTrips_PastTravelDate_IsRefusedAndTodayIsAccepted`
- [x] AC-6: `SearchTrips_CheaperSeatsBooked_PriceIsLowestFreeFare`
- [x] AC-7: `SearchTrips_SoldOutTrip_IsMarkedAndListedLast`
- [x] AC-8: `SearchTrips_HeldBookedAndBlockedSeats_AreNotFree`
- [x] AC-9: `SearchTrips_CombinedFilters_ApplyTogether`
- [x] AC-10: `SearchTrips_SortByRating_HighestFirstWithTiesByDeparture`
- [x] AC-11: `SearchTrips_NoSortChosen_ListsByDepartureWithSoldOutAfter`
- [x] AC-12: `SearchTrips_FortyFiveTrips_PagesTwentyAtATime`
- [x] AC-13: `SearchTrips_MalformedSearch_IsRefusedNamingTheField`
- [x] AC-14: `SearchTrips_FleetAndSeatInventoryStopped_AnswersFromOwnListings` — the host has no Fleet or Seat Inventory client, so the answer cannot depend on them
- [x] AC-15: `SearchTrips_AfterSeatsHeld_ShowsFewerSeatsLeft` — no result cache yet, so an applied seat change shows at once
- [ ] AC-16: 95% of 100 searches within 1 second over 5,000 trips — a load test against a deployed environment, not covered by the slice tests

## Invariants touched
Business rules live in `Domain/`, never in this slice. List the ones this feature relies on.

- Searchable trip: not cancelled, details received, departing after now plus the search cutoff
  (`SEARCH_CUTOFF_MINUTES`). Expressed in SQL here and in `SearchCities`; it moves to a shared
  place if C# code ever needs it.
- Seats left = Available seats; lowest free fare = lowest Available fare, or the lowest of any seat
  when sold out. Derived in this slice's SQL at the time of each answer.
