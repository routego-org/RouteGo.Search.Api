namespace RouteGo.Search.Api.Features.TripSearch.SearchCities;

[Collection(DatabaseCollection.Name)]
public sealed class SearchCitiesTests : IAsyncLifetime
{
    // "Now" in every criterion: Tuesday 20 Oct 2026, 10:00 IST.
    private static readonly DateTimeOffset _now = new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.FromHours(5.5));

    private static readonly Guid _mumbai = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid _pune = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid _nashik = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid _bengaluru = Guid.Parse("00000000-0000-0000-0000-000000000004");
    private static readonly Guid _nagpur = Guid.Parse("00000000-0000-0000-0000-000000000005");
    private static readonly Guid _shirdi = Guid.Parse("00000000-0000-0000-0000-000000000006");

    private const string InsertTripSql = """
        INSERT INTO trip_listings.trip_listings (
            trip_id, operator_name, operator_rating, bus_id, bus_name, bus_type, is_ac, amenities,
            from_city_id, from_city_name, to_city_id, to_city_name, service_date,
            departure_at, arrival_at, details_received_at, is_cancelled)
        VALUES (
            @TripId, 'Test Operator', 4.0, @TripId, 'Test Bus', 'Seater', false, '{}',
            @FromCityId, @FromCityName, @ToCityId, @ToCityName, (@DepartureAt AT TIME ZONE 'Asia/Kolkata')::date,
            @DepartureAt, @DepartureAt + interval '3 hours', @DepartureAt - interval '7 days', @IsCancelled);
        """;

    private readonly DatabaseFixture _database;
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public SearchCitiesTests(DatabaseFixture database)
    {
        _database = database;
        // The test host pins Development on the host only; LocalEntryPoints reads the process variable
        // to decide whether to seed the local settings (pool profile, search cutoff, page size), so the
        // test sets it the way a local run's launch profile does.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        var clock = new FixedClock(_now);
        _factory = TestHost.For(
            database,
            services => services.Add(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<IClock>(clock)));
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _database.ResetBusinessTablesAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task SearchCities_FromPrefixMu_ReturnsMumbaiWithItsIdentifier()
    {
        // Arrange
        await SeedCriteriaTripsAsync();

        // Act
        SearchCitiesResponse response = await GetCitiesAsync("?prefix=Mu&cityRole=from");

        // Assert
        Assert.Equal(new[] { new City(_mumbai, "Mumbai") }, response.Items);
    }

    [Fact]
    public async Task SearchCities_LowerCasePrefix_MatchesStartOfNameOnly()
    {
        // Arrange
        await SeedCriteriaTripsAsync();

        // Act
        SearchCitiesResponse startsWith = await GetCitiesAsync("?prefix=pu&cityRole=from");
        SearchCitiesResponse inside = await GetCitiesAsync("?prefix=une&cityRole=from");

        // Assert
        Assert.Equal(new[] { new City(_pune, "Pune") }, startsWith.Items);
        Assert.Empty(inside.Items);
    }

    [Fact]
    public async Task SearchCities_CancelledDepartedOrLastCityOnly_IsNoFromCity()
    {
        // Arrange
        await SeedCriteriaTripsAsync();

        // Act
        SearchCitiesResponse fromCities = await GetCitiesAsync("?prefix=N&cityRole=from");
        SearchCitiesResponse toCities = await GetCitiesAsync("?prefix=N&cityRole=to");

        // Assert
        Assert.Empty(fromCities.Items);
        Assert.Equal(new[] { new City(_nashik, "Nashik") }, toCities.Items);
    }

    [Fact]
    public async Task SearchCities_ToCitiesWithFromCity_NarrowsToReachableCities()
    {
        // Arrange
        await SeedCriteriaTripsAsync();

        // Act
        SearchCitiesResponse reachable = await GetCitiesAsync($"?prefix=P&cityRole=to&fromCityId={_mumbai}");
        SearchCitiesResponse unreachable = await GetCitiesAsync($"?prefix=B&cityRole=to&fromCityId={_mumbai}");

        // Assert
        Assert.Equal(new[] { new City(_pune, "Pune") }, reachable.Items);
        Assert.Empty(unreachable.Items);
    }

    [Fact]
    public async Task SearchCities_ToCitiesWithoutFromCity_ReturnsEveryLastCity()
    {
        // Arrange
        await SeedCriteriaTripsAsync();

        // Act
        SearchCitiesResponse response = await GetCitiesAsync("?prefix=B&cityRole=to");

        // Assert
        Assert.Equal(new[] { new City(_bengaluru, "Bengaluru") }, response.Items);
    }

    [Fact]
    public async Task SearchCities_OnlyTripInsideSearchCutoff_ReturnsEmptyList()
    {
        // Arrange: departs today at 10:20 IST, twenty minutes from now, inside the 30-minute cutoff.
        await SeedTripAsync(_pune, "Pune", _bengaluru, "Bengaluru", _now.AddMinutes(20), false);

        // Act
        SearchCitiesResponse response = await GetCitiesAsync("?prefix=Pu&cityRole=from");

        // Assert
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task SearchCities_TwelveMatchingCities_ReturnsFirstTenByName()
    {
        // Arrange
        await SeedTripsFromCitiesStartingWithSAsync();

        // Act
        SearchCitiesResponse response = await GetCitiesAsync("?prefix=S&cityRole=from");

        // Assert
        string[] expected = { "Sa01", "Sa02", "Sa03", "Sa04", "Sa05", "Sa06", "Sa07", "Sa08", "Sa09", "Sa10" };
        Assert.Equal(expected, response.Items.Select(city => city.Name));
    }

    [Theory]
    [InlineData("?cityRole=from", "prefix")]
    [InlineData("?prefix=%20%20&cityRole=from", "prefix")]
    [InlineData("?prefix=ThisPrefixIsMuchLongerThanFiftyCharactersAndSoItIsRefused&cityRole=from", "prefix")]
    [InlineData("?prefix=Mu", "cityRole")]
    public async Task SearchCities_MalformedRequest_IsRefusedNamingTheField(string query, string field)
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync("/search/cities" + query);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using System.Text.Json.JsonDocument body = await ReadJsonAsync(response);
        Assert.Equal("VALIDATION_FAILED", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("urn:routego:search:problems:validation-failed", body.RootElement.GetProperty("type").GetString());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task SearchCities_FromCitiesWithFromCity_IgnoresTheFromCity()
    {
        // Arrange
        await SeedCriteriaTripsAsync();

        // Act
        SearchCitiesResponse response = await GetCitiesAsync($"?prefix=P&cityRole=from&fromCityId={_mumbai}");

        // Assert
        Assert.Equal(new[] { new City(_pune, "Pune") }, response.Items);
    }

    private async Task<SearchCitiesResponse> GetCitiesAsync(string query)
    {
        HttpResponseMessage response = await _client.GetAsync("/search/cities" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SearchCitiesResponse? body = await response.Content.ReadFromJsonAsync<SearchCitiesResponse>();
        return body!;
    }

    private static async Task<System.Text.Json.JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        Stream content = await response.Content.ReadAsStreamAsync();
        return await System.Text.Json.JsonDocument.ParseAsync(content);
    }

    // The shared background of the criteria: three searchable trips, one cancelled, one departed.
    private async Task SeedCriteriaTripsAsync()
    {
        var saturday = new DateTimeOffset(2026, 10, 25, 7, 0, 0, TimeSpan.FromHours(5.5));
        await SeedTripAsync(_mumbai, "Mumbai", _pune, "Pune", saturday, false);
        await SeedTripAsync(_mumbai, "Mumbai", _nashik, "Nashik", saturday, false);
        await SeedTripAsync(_pune, "Pune", _bengaluru, "Bengaluru", saturday, false);
        await SeedTripAsync(_nagpur, "Nagpur", _pune, "Pune", saturday, true);
        await SeedTripAsync(_nashik, "Nashik", _shirdi, "Shirdi", _now.AddDays(-1), false);
    }

    private async Task SeedTripsFromCitiesStartingWithSAsync()
    {
        var saturday = new DateTimeOffset(2026, 10, 25, 7, 0, 0, TimeSpan.FromHours(5.5));
        for (int number = 12; number >= 1; number--)
        {
            string name = $"Sa{number:00}";
            await SeedTripAsync(Guid.NewGuid(), name, _pune, "Pune", saturday, false);
        }
    }

    private async Task SeedTripAsync(
        Guid fromCityId, string fromCityName, Guid toCityId, string toCityName, DateTimeOffset departureAt, bool isCancelled)
    {
        var trip = new
        {
            TripId = Guid.NewGuid(),
            FromCityId = fromCityId,
            FromCityName = fromCityName,
            ToCityId = toCityId,
            ToCityName = toCityName,
            DepartureAt = departureAt.UtcDateTime,
            IsCancelled = isCancelled,
        };
        await _database.SeedAsync(InsertTripSql, trip);
    }

    private sealed class FixedClock : IClock
    {
        private readonly DateTimeOffset _utcNow;

        public FixedClock(DateTimeOffset now)
        {
            _utcNow = now.ToUniversalTime();
        }

        public DateTimeOffset UtcNow
        {
            get
            {
                return _utcNow;
            }
        }
    }
}
