namespace RouteGo.Search.Api.Features.TripSearch.SearchCities;

/// <summary>Which cities to suggest for the "From" or "To" box of the search page.</summary>
/// <param name="Prefix">The start of the city name, 1 to 50 characters after trimming.</param>
/// <param name="CityRole">Which box is asking: <c>from</c> or <c>to</c>.</param>
/// <param name="FromCityId">The chosen from city; narrows to-city suggestions to cities reachable from it. Ignored for <c>from</c>.</param>
public sealed record SearchCitiesRequest(string? Prefix, string? CityRole, string? FromCityId);

/// <summary>A suggested city.</summary>
/// <param name="CityId">Fleet's city identifier.</param>
/// <param name="Name">The city's name.</param>
public sealed record City(Guid CityId, string Name);

/// <summary>The suggested cities: at most ten, sorted by name. No match is an empty list.</summary>
/// <param name="Items">The suggestions.</param>
public sealed record SearchCitiesResponse(IReadOnlyList<City> Items);

/// <summary>Request-shape rules for a city suggestion request.</summary>
public sealed class SearchCitiesValidator : AbstractValidator<SearchCitiesRequest>
{
    private const int MaxPrefixLength = 50;

    /// <summary>Creates the validator.</summary>
    public SearchCitiesValidator()
    {
        RuleFor(x => x.Prefix)
            .Cascade(CascadeMode.Stop)
            .Must(HasText).WithMessage("prefix is required and must not be blank.")
            .Must(FitsMaxLength).WithMessage($"prefix must be at most {MaxPrefixLength} characters.");
        RuleFor(x => x.CityRole)
            .Must(IsKnownCityRole).WithMessage("cityRole must be 'from' or 'to'.");
        RuleFor(x => x.FromCityId)
            .Must(IsAbsentOrGuid).WithMessage("fromCityId must be a city identifier (uuid).");
    }

    private static bool HasText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool FitsMaxLength(string? value)
    {
        return value!.Trim().Length <= MaxPrefixLength;
    }

    private static bool IsKnownCityRole(string? value)
    {
        return value == SearchCitiesHandler.FromRole || value == SearchCitiesHandler.ToRole;
    }

    private static bool IsAbsentOrGuid(string? value)
    {
        if (value is null)
        {
            return true;
        }

        return Guid.TryParse(value, out _);
    }
}

/// <summary>Suggests cities with searchable trips, by name prefix, from Search's own trip listings.</summary>
public sealed class SearchCitiesHandler
{
    /// <summary>The cityRole value for the "From" box.</summary>
    public const string FromRole = "from";

    /// <summary>The cityRole value for the "To" box.</summary>
    public const string ToRole = "to";

    private const int MaxSuggestions = 10;

    // A searchable trip: not cancelled, its details received, and departing after the search cutoff.
    // The trailing city_id keeps the order stable when two cities share a name.
    private const string FromCitiesSql = """
        SELECT DISTINCT from_city_id AS city_id, from_city_name AS name
        FROM trip_listings.trip_listings
        WHERE NOT is_cancelled
          AND details_received_at IS NOT NULL
          AND departure_at > @SearchableAfter
          AND lower(btrim(from_city_name)) LIKE @NamePattern ESCAPE '\'
        ORDER BY name, city_id
        LIMIT @Limit;
        """;

    private const string ToCitiesSql = """
        SELECT DISTINCT to_city_id AS city_id, to_city_name AS name
        FROM trip_listings.trip_listings
        WHERE NOT is_cancelled
          AND details_received_at IS NOT NULL
          AND departure_at > @SearchableAfter
          AND lower(btrim(to_city_name)) LIKE @NamePattern ESCAPE '\'
        ORDER BY name, city_id
        LIMIT @Limit;
        """;

    private const string ToCitiesFromCitySql = """
        SELECT DISTINCT to_city_id AS city_id, to_city_name AS name
        FROM trip_listings.trip_listings
        WHERE NOT is_cancelled
          AND details_received_at IS NOT NULL
          AND departure_at > @SearchableAfter
          AND from_city_id = @FromCityId
          AND lower(btrim(to_city_name)) LIKE @NamePattern ESCAPE '\'
        ORDER BY name, city_id
        LIMIT @Limit;
        """;

    private readonly IReaderConnectionFactory _readerFactory;
    private readonly IClock _clock;
    private readonly TimeSpan _searchCutoff;

    /// <summary>Creates the handler.</summary>
    public SearchCitiesHandler(IReaderConnectionFactory readerFactory, IClock clock, TimeSpan searchCutoff)
    {
        _readerFactory = readerFactory;
        _clock = clock;
        _searchCutoff = searchCutoff;
    }

    /// <summary>Lists the suggested cities for a validated request.</summary>
    public async Task<SearchCitiesResponse> HandleAsync(SearchCitiesRequest request, CancellationToken cancellationToken)
    {
        string sql = ChooseSql(request);
        var parameters = new
        {
            SearchableAfter = _clock.UtcNow.Add(_searchCutoff),
            NamePattern = ToStartsWithPattern(request.Prefix!),
            FromCityId = ParseOptionalGuid(request.FromCityId),
            Limit = MaxSuggestions,
        };

        IEnumerable<CityRow> rows = await _readerFactory.ExecuteReadAsync(
            connection => connection.QueryAsync<CityRow>(
                new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)),
            cancellationToken);
        return ToResponse(rows);
    }

    // A from city narrows only to-city suggestions; on a from-city request it is ignored.
    private static string ChooseSql(SearchCitiesRequest request)
    {
        if (request.CityRole == FromRole)
        {
            return FromCitiesSql;
        }

        if (request.FromCityId is null)
        {
            return ToCitiesSql;
        }

        return ToCitiesFromCitySql;
    }

    // Matching ignores case and surrounding spaces; LIKE wildcards typed by the customer are literal.
    private static string ToStartsWithPattern(string prefix)
    {
        string normalized = prefix.Trim().ToLowerInvariant();
        string escaped = normalized.Replace("\\", "\\\\", StringComparison.Ordinal);
        escaped = escaped.Replace("%", "\\%", StringComparison.Ordinal);
        escaped = escaped.Replace("_", "\\_", StringComparison.Ordinal);
        return escaped + "%";
    }

    private static Guid? ParseOptionalGuid(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return Guid.Parse(value);
    }

    private static SearchCitiesResponse ToResponse(IEnumerable<CityRow> rows)
    {
        var items = new List<City>();
        foreach (CityRow row in rows)
        {
            items.Add(new City(row.CityId, row.Name));
        }

        return new SearchCitiesResponse(items);
    }

    private sealed class CityRow
    {
        public Guid CityId { get; init; }

        public string Name { get; init; } = string.Empty;
    }
}

/// <summary>City suggestions for the search page's "From" and "To" boxes.</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "TripSearch")]
[Route("search/cities")]
public sealed class SearchCitiesController : ControllerBase
{
    private readonly SearchCitiesHandler _handler;
    private readonly SearchCitiesValidator _validator = new();

    /// <summary>Creates the controller.</summary>
    public SearchCitiesController(IReaderConnectionFactory readerFactory, IClock clock, IConfiguration configuration)
    {
        int searchCutoffMinutes = configuration.GetValue<int>(Infrastructure.ConfigKeys.SearchCutoffMinutes);
        var searchCutoff = TimeSpan.FromMinutes(searchCutoffMinutes);
        _handler = new SearchCitiesHandler(readerFactory, clock, searchCutoff);
    }

    /// <summary>Suggest from-cities or to-cities with upcoming trips, by name prefix.</summary>
    /// <response code="200">The suggested cities. No match is an empty list.</response>
    /// <response code="400">A request parameter is invalid (VALIDATION_FAILED).</response>
    // Public by product decision: customers pick cities before they sign in, and a sent token is ignored.
    [AllowAnonymous]
    [HttpGet]
    [ProducesResponseType(typeof(SearchCitiesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchCities([FromQuery] SearchCitiesRequest request, CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ProblemResult.ValidationFailed(this, validation.ToDictionary());
        }

        SearchCitiesResponse response = await _handler.HandleAsync(request, cancellationToken);
        return Ok(response);
    }
}
