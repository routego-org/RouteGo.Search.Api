namespace RouteGo.Search.Api.Features.TripSearch.SearchTrips;

[Collection(DatabaseCollection.Name)]
public sealed partial class SearchTripsTests : IAsyncLifetime
{
    private readonly DatabaseFixture _database;
    private readonly List<ApiFactory> _factories = new List<ApiFactory>();
    private readonly HttpClient _client;

    public SearchTripsTests(DatabaseFixture database)
    {
        _database = database;
        // The test host pins Development on the host only; LocalEntryPoints reads the process variable
        // to decide whether to seed the local settings (pool profile, search cutoff, page size), so the
        // test sets it the way a local run's launch profile does.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        _client = CreateClient(_now);
    }

    public async Task InitializeAsync()
    {
        await _database.ResetBusinessTablesAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (ApiFactory factory in _factories)
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task SearchTrips_MumbaiToPuneOnDate_ListsTripsWithTheirDetails()
    {
        // Arrange
        await SeedCriteriaTripsAsync();

        // Act
        SearchTripsResponse response = await SearchAsync(_client, "");

        // Assert
        Assert.Equal(new[] { _t202, _t201, _t203 }, TripIds(response));
        Assert.Equal(3, response.TotalCount);
        TripListing t201 = response.Items[1];
        Assert.Equal("BusCo Travels", t201.OperatorName);
        Assert.Equal(4.2m, t201.OperatorRating);
        Assert.Equal(_busB11, t201.BusId);
        Assert.Equal("BusCo Volvo 9600", t201.BusName);
        Assert.Equal(BusType.Sleeper, t201.BusType);
        Assert.True(t201.IsAc);
        Assert.Equal(new[] { Amenity.Wifi, Amenity.ChargingPoint }, t201.Amenities);
        Assert.Equal(new TripCity(_mumbai, "Mumbai"), t201.FromCity);
        Assert.Equal(new TripCity(_pune, "Pune"), t201.ToCity);
        Assert.Equal(new DateOnly(2026, 10, 25), t201.ServiceDate);
        Assert.Equal(new DateTime(2026, 10, 25, 16, 30, 0, DateTimeKind.Utc), t201.DepartureAt);
        Assert.Equal(new DateTime(2026, 10, 25, 19, 30, 0, DateTimeKind.Utc), t201.ArrivalAt);
        Assert.Equal(180, t201.DurationMinutes);
        Assert.Equal(900, t201.LowestFreeFare);
        Assert.Equal(36, t201.SeatsLeft);
        Assert.False(t201.IsSoldOut);
        ListingStop stop = Assert.Single(t201.Stops);
        Assert.Equal(new ListingStop(_lonavala, "Lonavala", new DateTime(2026, 10, 25, 18, 0, 0, DateTimeKind.Utc)), stop);
    }

    [Fact]
    public async Task SearchTrips_StopAlongTheWay_DoesNotMatch()
    {
        // Arrange
        await SeedCriteriaTripsAsync();

        // Act
        SearchTripsResponse toStop = await GetAsync(_client, $"?fromCityId={_mumbai}&toCityId={_lonavala}&date=2026-10-25");
        SearchTripsResponse fromStop = await GetAsync(_client, $"?fromCityId={_lonavala}&toCityId={_pune}&date=2026-10-25");

        // Assert
        Assert.Empty(toStop.Items);
        Assert.Equal(0, toStop.TotalCount);
        Assert.Empty(fromStop.Items);
        Assert.Equal(0, fromStop.TotalCount);
    }

    [Fact]
    public async Task SearchTrips_CancelledTrip_NeverAppears()
    {
        // Arrange
        await SeedCriteriaTripsAsync();
        await CancelTripAsync(_t202);

        // Act
        SearchTripsResponse response = await SearchAsync(_client, "");

        // Assert
        Assert.DoesNotContain(_t202, TripIds(response));
    }

    [Fact]
    public async Task SearchTrips_TripInsideSearchCutoff_LeavesTheAnswer()
    {
        // Arrange
        await SeedCriteriaTripsAsync();
        HttpClient at2125 = CreateClient(new DateTimeOffset(2026, 10, 25, 21, 25, 0, _ist));
        HttpClient at2135 = CreateClient(new DateTimeOffset(2026, 10, 25, 21, 35, 0, _ist));

        // Act
        SearchTripsResponse before = await SearchAsync(at2125, "");
        SearchTripsResponse after = await SearchAsync(at2135, "");

        // Assert
        Assert.Equal(new[] { _t201 }, TripIds(before));
        Assert.Empty(after.Items);
    }

    [Fact]
    public async Task SearchTrips_PastTravelDate_IsRefusedAndTodayIsAccepted()
    {
        // Act
        HttpResponseMessage yesterday = await _client.GetAsync($"/search/trips?fromCityId={_mumbai}&toCityId={_pune}&date=2026-10-19");
        HttpResponseMessage today = await _client.GetAsync($"/search/trips?fromCityId={_mumbai}&toCityId={_pune}&date=2026-10-20");

        // Assert
        await AssertRefusedNamingAsync(yesterday, "date");
        Assert.Equal(HttpStatusCode.OK, today.StatusCode);
    }

    [Fact]
    public async Task SearchTrips_CheaperSeatsBooked_PriceIsLowestFreeFare()
    {
        // Arrange
        await SeedCriteriaTripsAsync();
        await SetSeatsStatusAsync(_t201, UpperBerths(), "Booked");

        // Act
        SearchTripsResponse unfiltered = await SearchAsync(_client, "");
        SearchTripsResponse max1000 = await SearchAsync(_client, "&maxFare=1000");
        SearchTripsResponse max1200 = await SearchAsync(_client, "&maxFare=1200");

        // Assert
        TripListing t201 = Assert.Single(unfiltered.Items, trip => trip.TripId == _t201);
        Assert.Equal(1200, t201.LowestFreeFare);
        Assert.Equal(16, t201.SeatsLeft);
        Assert.DoesNotContain(_t201, TripIds(max1000));
        Assert.Contains(_t201, TripIds(max1200));
    }

    [Fact]
    public async Task SearchTrips_SoldOutTrip_IsMarkedAndListedLast()
    {
        // Arrange
        await SeedCriteriaTripsAsync();

        // Act
        SearchTripsResponse response = await SearchAsync(_client, "&sort=lowestFreeFare");

        // Assert
        Assert.Equal(new[] { _t202, _t201, _t203 }, TripIds(response));
        TripListing t203 = response.Items[2];
        Assert.True(t203.IsSoldOut);
        Assert.Equal(0, t203.SeatsLeft);
        Assert.Equal(450, t203.LowestFreeFare);
    }

    [Fact]
    public async Task SearchTrips_HeldBookedAndBlockedSeats_AreNotFree()
    {
        // Arrange
        await SeedCriteriaTripsAsync();

        // Act
        SearchTripsResponse response = await SearchAsync(_client, "");

        // Assert
        TripListing t202 = Assert.Single(response.Items, trip => trip.TripId == _t202);
        Assert.Equal(12, t202.SeatsLeft);
    }

    [Fact]
    public async Task SearchTrips_CombinedFilters_ApplyTogether()
    {
        // Arrange
        await SeedCriteriaTripsAsync();
        string evening = "&busTypes=sleeper,semiSleeper&departAfter=12:00&departBefore=23:59&minRating=4.0";

        // Act
        SearchTripsResponse acOnly = await SearchAsync(_client, evening + "&isAc=true");
        SearchTripsResponse nonAcOnly = await SearchAsync(_client, evening + "&isAc=false");
        SearchTripsResponse seaterOnly = await SearchAsync(_client, "&busTypes=seater");

        // Assert
        Assert.Equal(new[] { _t201, _t203 }, TripIds(acOnly));
        Assert.Empty(nonAcOnly.Items);
        Assert.Equal(new[] { _t202 }, TripIds(seaterOnly));
    }

    [Fact]
    public async Task SearchTrips_SortByRating_HighestFirstWithTiesByDeparture()
    {
        // Arrange
        await SeedCriteriaTripsAsync();
        await SeedBookableTripAsync(_t204, new DateTimeOffset(2026, 10, 25, 18, 0, 0, _ist), 4.2m);

        // Act
        SearchTripsResponse response = await SearchAsync(_client, "&sort=operatorRating");

        // Assert
        Assert.Equal(new[] { _t204, _t201, _t202, _t203 }, TripIds(response));
    }

    [Fact]
    public async Task SearchTrips_NoSortChosen_ListsByDepartureWithSoldOutAfter()
    {
        // Arrange
        await SeedCriteriaTripsAsync();
        await SeedBookableTripAsync(_t204, new DateTimeOffset(2026, 10, 25, 18, 0, 0, _ist), 4.2m);

        // Act
        SearchTripsResponse response = await SearchAsync(_client, "");

        // Assert
        Assert.Equal(new[] { _t202, _t204, _t201, _t203 }, TripIds(response));
    }

    [Fact]
    public async Task SearchTrips_FortyFiveTrips_PagesTwentyAtATime()
    {
        // Arrange
        List<Guid> tripIds = await SeedBookableTripsAsync(45);

        // Act
        SearchTripsResponse page3 = await SearchAsync(_client, "&page=3");
        SearchTripsResponse page4 = await SearchAsync(_client, "&page=4");

        // Assert
        Assert.Equal(tripIds.GetRange(40, 5), TripIds(page3));
        Assert.Equal(45, page3.TotalCount);
        Assert.Equal(3, page3.Page);
        Assert.Equal(20, page3.PageSize);
        Assert.Empty(page4.Items);
        Assert.Equal(45, page4.TotalCount);
    }

    [Theory]
    [InlineData("?toCityId=00000000-0000-0000-0000-000000000002&date=2026-10-25", "fromCityId")]
    [InlineData("?fromCityId=00000000-0000-0000-0000-000000000001&date=2026-10-25", "toCityId")]
    [InlineData("?fromCityId=00000000-0000-0000-0000-000000000001&toCityId=00000000-0000-0000-0000-000000000002", "date")]
    [InlineData("?fromCityId=00000000-0000-0000-0000-000000000001&toCityId=00000000-0000-0000-0000-000000000001&date=2026-10-25", "toCityId")]
    [InlineData(ValidSearch + "&minFare=900&maxFare=500", "minFare")]
    [InlineData(ValidSearch + "&departAfter=20:00&departBefore=08:00", "departAfter")]
    [InlineData(ValidSearch + "&busTypes=doubleDecker", "busTypes")]
    [InlineData(ValidSearch + "&minRating=5.5", "minRating")]
    [InlineData(ValidSearch + "&sort=cheapest", "sort")]
    [InlineData(ValidSearch + "&page=0", "page")]
    public async Task SearchTrips_MalformedSearch_IsRefusedNamingTheField(string query, string field)
    {
        // Act
        HttpResponseMessage response = await _client.GetAsync("/search/trips" + query);

        // Assert
        await AssertRefusedNamingAsync(response, field);
    }

    [Fact]
    public async Task SearchTrips_FleetAndSeatInventoryStopped_AnswersFromOwnListings()
    {
        // Arrange: Search holds no client for Fleet or Seat Inventory; it answers from its own listings only.
        await SeedCriteriaTripsAsync();

        // Act
        SearchTripsResponse response = await SearchAsync(_client, "");

        // Assert
        Assert.Equal(new[] { _t202, _t201, _t203 }, TripIds(response));
    }

    [Fact]
    public async Task SearchTrips_AfterSeatsHeld_ShowsFewerSeatsLeft()
    {
        // Arrange
        await SeedCriteriaTripsAsync();
        await SetSeatsStatusAsync(_t202, new[] { "A1", "A2" }, "Held");

        // Act
        SearchTripsResponse response = await SearchAsync(_client, "");

        // Assert
        TripListing t202 = Assert.Single(response.Items, trip => trip.TripId == _t202);
        Assert.Equal(10, t202.SeatsLeft);
    }

    private const string ValidSearch =
        "?fromCityId=00000000-0000-0000-0000-000000000001&toCityId=00000000-0000-0000-0000-000000000002&date=2026-10-25";

    private HttpClient CreateClient(DateTimeOffset now)
    {
        var clock = new FixedClock(now);
        ApiFactory factory = TestHost.For(
            _database,
            services => services.Add(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<IClock>(clock)));
        _factories.Add(factory);
        return factory.CreateClient();
    }

    // Mumbai → Pune on Saturday 25 Oct 2026, plus any extra query parameters.
    private static async Task<SearchTripsResponse> SearchAsync(HttpClient client, string extraQuery)
    {
        return await GetAsync(client, ValidSearch + extraQuery);
    }

    private static async Task<SearchTripsResponse> GetAsync(HttpClient client, string query)
    {
        HttpResponseMessage response = await client.GetAsync("/search/trips" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SearchTripsResponse? body = await response.Content.ReadFromJsonAsync<SearchTripsResponse>(WireJson.Options);
        return body!;
    }

    private static async Task AssertRefusedNamingAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Stream content = await response.Content.ReadAsStreamAsync();
        using System.Text.Json.JsonDocument body = await System.Text.Json.JsonDocument.ParseAsync(content);
        Assert.Equal("VALIDATION_FAILED", body.RootElement.GetProperty("code").GetString());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty(field, out _));
    }

    private static List<Guid> TripIds(SearchTripsResponse response)
    {
        return response.Items.Select(trip => trip.TripId).ToList();
    }

    private static string[] UpperBerths()
    {
        string[] labels = new string[20];
        for (int number = 1; number <= labels.Length; number++)
        {
            labels[number - 1] = "U" + number;
        }

        return labels;
    }
}
