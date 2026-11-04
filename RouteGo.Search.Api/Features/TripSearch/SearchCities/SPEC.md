# CAP-SRCH-002 — Search cities

- Capability: CAP-SRCH-002
- Canonical spec: `spec-RouteGo.Search.API/capabilities/trip-search/features/city-autocomplete.md` (v0.1.1; the specs repo is not yet a submodule here)
- Area: TripSearch
- Slice: `Features/TripSearch/SearchCities/`

## Contract
Implemented **verbatim** from the issued contract — if it looks wrong, report drift upstream
(`skills-spec-authoring`), never deviate locally.

- Operation: `searchCities` — `contracts/trip-search/openapi.yaml` → `contracts/trip-search/city.yaml`
- Route / verb: `GET /search/cities`
- Request: query `prefix`, `cityRole`, `fromCityId` (see `searchCities`)
- Response: 200 `CityList`
- Problem codes: `VALIDATION_FAILED` (400) — `ProblemTypes.ValidationFailed`

## Acceptance criteria
Each criterion maps to one test in `SearchCities.Tests.cs`.

- [x] AC-1: `SearchCities_FromPrefixMu_ReturnsMumbaiWithItsIdentifier`
- [x] AC-2: `SearchCities_LowerCasePrefix_MatchesStartOfNameOnly`
- [x] AC-3: `SearchCities_CancelledDepartedOrLastCityOnly_IsNoFromCity`
- [x] AC-4: `SearchCities_ToCitiesWithFromCity_NarrowsToReachableCities`
- [x] AC-5: `SearchCities_ToCitiesWithoutFromCity_ReturnsEveryLastCity`
- [x] AC-6: `SearchCities_OnlyTripInsideSearchCutoff_ReturnsEmptyList`
- [x] AC-7: `SearchCities_TwelveMatchingCities_ReturnsFirstTenByName`
- [x] AC-8: `SearchCities_MalformedRequest_IsRefusedNamingTheField`
- [x] AC-9: `SearchCities_FromCitiesWithFromCity_IgnoresTheFromCity`

## Invariants touched
Business rules live in `Domain/`, never in this slice. List the ones this feature relies on.

- Searchable trip: not cancelled, details received, departing after now plus the search cutoff
  (`SEARCH_CUTOFF_MINUTES`). Expressed in this slice's SQL only; it moves to a shared place when
  a second slice needs it in C#.
