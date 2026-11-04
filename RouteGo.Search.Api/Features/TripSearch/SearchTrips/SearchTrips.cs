namespace RouteGo.Search.Api.Features.TripSearch.SearchTrips;

/// <summary>
/// A trip search, as typed in the query string. Every value arrives as text so a malformed one is
/// refused by the validator with a named field, not by model binding.
/// </summary>
public sealed record SearchTripsRequest
{
    /// <summary>Fleet's identifier of the trip's first city (uuid). Required.</summary>
    public string? FromCityId { get; init; }

    /// <summary>Fleet's identifier of the trip's last city (uuid); not the same as fromCityId. Required.</summary>
    public string? ToCityId { get; init; }

    /// <summary>The travel date: the trip's service date in IST (yyyy-MM-dd), today or later. Required.</summary>
    public string? Date { get; init; }

    /// <summary>One or more of seater, sleeper, semiSleeper, comma-separated; a trip matches any of them.</summary>
    public string? BusTypes { get; init; }

    /// <summary>true for AC only, false for non-AC only; absent for either.</summary>
    public string? IsAc { get; init; }

    /// <summary>Lowest acceptable lowest free fare, whole INR; not above maxFare.</summary>
    public string? MinFare { get; init; }

    /// <summary>Highest acceptable lowest free fare, whole INR.</summary>
    public string? MaxFare { get; init; }

    /// <summary>Earliest departure time of day in IST (HH:mm), inclusive; not later than departBefore.</summary>
    public string? DepartAfter { get; init; }

    /// <summary>Latest departure time of day in IST (HH:mm), inclusive.</summary>
    public string? DepartBefore { get; init; }

    /// <summary>Lowest acceptable operator rating, 0.0 to 5.0.</summary>
    public string? MinRating { get; init; }

    /// <summary>departureAt (default), lowestFreeFare or operatorRating.</summary>
    public string? Sort { get; init; }

    /// <summary>The page to return, from 1 (default 1).</summary>
    public string? Page { get; init; }
}

/// <summary>The bus type, as Fleet defines it.</summary>
public enum BusType
{
    /// <summary>Seats only.</summary>
    Seater,

    /// <summary>Berths only.</summary>
    Sleeper,

    /// <summary>Reclining seats.</summary>
    SemiSleeper,
}

/// <summary>An amenity of the bus.</summary>
public enum Amenity
{
    /// <summary>Wi-Fi.</summary>
    Wifi,

    /// <summary>A charging point.</summary>
    ChargingPoint,

    /// <summary>A blanket.</summary>
    Blanket,

    /// <summary>A water bottle.</summary>
    WaterBottle,

    /// <summary>A reading light.</summary>
    ReadingLight,

    /// <summary>CCTV.</summary>
    Cctv,

    /// <summary>GPS tracking.</summary>
    GpsTracking,

    /// <summary>A restroom.</summary>
    Restroom,
}

/// <summary>A city at one end of a trip.</summary>
/// <param name="CityId">Fleet's city identifier.</param>
/// <param name="Name">The city's name.</param>
public sealed record TripCity(Guid CityId, string Name);

/// <summary>A stop along the way, shown for information only.</summary>
/// <param name="CityId">Fleet's city identifier.</param>
/// <param name="CityName">The city's name.</param>
/// <param name="ArrivalAt">When the trip reaches this stop (UTC).</param>
public sealed record ListingStop(Guid CityId, string CityName, DateTime ArrivalAt);

/// <summary>One bookable or sold-out trip in a search answer.</summary>
public sealed record TripListing
{
    /// <summary>The trip's identifier, as Fleet assigns it.</summary>
    public required Guid TripId { get; init; }

    /// <summary>The operator's company name.</summary>
    public required string OperatorName { get; init; }

    /// <summary>The operator's rating, 0.0 to 5.0.</summary>
    public required decimal OperatorRating { get; init; }

    /// <summary>Fleet's bus identifier, for a link to the public bus page.</summary>
    public required Guid BusId { get; init; }

    /// <summary>The bus's name.</summary>
    public required string BusName { get; init; }

    /// <summary>The bus type.</summary>
    public required BusType BusType { get; init; }

    /// <summary>Whether the bus is air-conditioned.</summary>
    public required bool IsAc { get; init; }

    /// <summary>The bus's amenities.</summary>
    public required IReadOnlyList<Amenity> Amenities { get; init; }

    /// <summary>The trip's first city.</summary>
    public required TripCity FromCity { get; init; }

    /// <summary>The trip's last city.</summary>
    public required TripCity ToCity { get; init; }

    /// <summary>The calendar date (IST) on which the trip departs.</summary>
    public required DateOnly ServiceDate { get; init; }

    /// <summary>When the trip leaves its first city (UTC).</summary>
    public required DateTime DepartureAt { get; init; }

    /// <summary>When the trip reaches its last city (UTC).</summary>
    public required DateTime ArrivalAt { get; init; }

    /// <summary>Minutes from departure to arrival.</summary>
    public required int DurationMinutes { get; init; }

    /// <summary>The stops along the way, in order; the first and last city are not included.</summary>
    public required IReadOnlyList<ListingStop> Stops { get; init; }

    /// <summary>The lowest fare among free seats, whole INR; for a sold-out trip, the lowest fare of any seat.</summary>
    public required int LowestFreeFare { get; init; }

    /// <summary>The number of free seats, as Search last knew it.</summary>
    public required int SeatsLeft { get; init; }

    /// <summary>True when no seat is free.</summary>
    public required bool IsSoldOut { get; init; }
}

/// <summary>One page of trips: bookable trips first, sold-out trips after. No match is an empty page.</summary>
public sealed record SearchTripsResponse
{
    /// <summary>The trips on this page.</summary>
    public required IReadOnlyList<TripListing> Items { get; init; }

    /// <summary>How many trips match in all.</summary>
    public required int TotalCount { get; init; }

    /// <summary>This page's number, from 1.</summary>
    public required int Page { get; init; }

    /// <summary>Trips per page.</summary>
    public required int PageSize { get; init; }
}

/// <summary>Request-shape rules for a trip search, plus the one date rule that needs today's date.</summary>
public sealed class SearchTripsValidator : AbstractValidator<SearchTripsRequest>
{
    /// <summary>Creates the validator for searches made on <paramref name="todayInIst"/>.</summary>
    public SearchTripsValidator(DateOnly todayInIst)
    {
        RuleFor(x => x.FromCityId)
            .Must(SearchTripsValues.IsGuid).WithMessage("fromCityId is required and must be a city identifier (uuid).");
        RuleFor(x => x.ToCityId)
            .Cascade(CascadeMode.Stop)
            .Must(SearchTripsValues.IsGuid).WithMessage("toCityId is required and must be a city identifier (uuid).")
            .Must((request, toCityId) => !SearchTripsValues.IsSameCity(request.FromCityId, toCityId))
            .WithMessage("toCityId must not be the same as fromCityId.");
        RuleFor(x => x.Date)
            .Cascade(CascadeMode.Stop)
            .Must(SearchTripsValues.IsDate).WithMessage("date is required, as yyyy-MM-dd.")
            .Must(date => SearchTripsValues.IsOnOrAfter(date, todayInIst)).WithMessage("date must be today (IST) or later.");
        RuleFor(x => x.BusTypes)
            .Must(SearchTripsValues.IsAbsentOrBusTypes).WithMessage("busTypes must be one or more of seater, sleeper, semiSleeper, each once.");
        RuleFor(x => x.IsAc)
            .Must(SearchTripsValues.IsAbsentOrBoolean).WithMessage("isAc must be true or false.");
        AddFareRules();
        AddDepartureTimeRules();
        RuleFor(x => x.MinRating)
            .Must(SearchTripsValues.IsAbsentOrRating).WithMessage("minRating must be a number from 0.0 to 5.0.");
        RuleFor(x => x.Sort)
            .Must(SearchTripsValues.IsAbsentOrSort).WithMessage("sort must be departureAt, lowestFreeFare or operatorRating.");
        RuleFor(x => x.Page)
            .Must(SearchTripsValues.IsAbsentOrPage).WithMessage("page must be a whole number from 1.");
    }

    private void AddFareRules()
    {
        RuleFor(x => x.MinFare)
            .Cascade(CascadeMode.Stop)
            .Must(SearchTripsValues.IsAbsentOrFare).WithMessage("minFare must be a whole number of rupees from 0.")
            .Must((request, minFare) => !SearchTripsValues.IsFareAbove(minFare, request.MaxFare))
            .WithMessage("minFare must not be above maxFare.");
        RuleFor(x => x.MaxFare)
            .Must(SearchTripsValues.IsAbsentOrFare).WithMessage("maxFare must be a whole number of rupees from 0.");
    }

    private void AddDepartureTimeRules()
    {
        RuleFor(x => x.DepartAfter)
            .Cascade(CascadeMode.Stop)
            .Must(SearchTripsValues.IsAbsentOrTimeOfDay).WithMessage("departAfter must be a time of day as HH:mm.")
            .Must((request, departAfter) => !SearchTripsValues.IsTimeLater(departAfter, request.DepartBefore))
            .WithMessage("departAfter must not be later than departBefore.");
        RuleFor(x => x.DepartBefore)
            .Must(SearchTripsValues.IsAbsentOrTimeOfDay).WithMessage("departBefore must be a time of day as HH:mm.");
    }
}

/// <summary>Trip search between two cities on a date, for the search page.</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "TripSearch")]
[Route("search/trips")]
public sealed class SearchTripsController : ControllerBase
{
    private readonly SearchTripsHandler _handler;
    private readonly SearchTripsValidator _validator;

    /// <summary>Creates the controller.</summary>
    public SearchTripsController(IReaderConnectionFactory readerFactory, IClock clock, IConfiguration configuration)
    {
        int searchCutoffMinutes = configuration.GetValue<int>(Infrastructure.ConfigKeys.SearchCutoffMinutes);
        int pageSize = configuration.GetValue<int>(Infrastructure.ConfigKeys.PageSize);
        var searchCutoff = TimeSpan.FromMinutes(searchCutoffMinutes);
        _handler = new SearchTripsHandler(readerFactory, clock, searchCutoff, pageSize);
        _validator = new SearchTripsValidator(SearchTripsValues.TodayInIst(clock.UtcNow));
    }

    /// <summary>Find bookable trips between two cities on a date: filtered, sorted, sold-out trips last, one page at a time.</summary>
    /// <response code="200">The requested page of trips. No match is an empty page.</response>
    /// <response code="400">A request parameter is invalid (VALIDATION_FAILED).</response>
    // Public by product decision: customers search before they sign in, and a sent token is ignored.
    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(typeof(SearchTripsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchTrips([FromQuery] SearchTripsRequest request, CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ProblemResult.ValidationFailed(this, validation.ToDictionary());
        }

        SearchTripsResponse response = await _handler.HandleAsync(request, cancellationToken);
        return Ok(response);
    }
}
