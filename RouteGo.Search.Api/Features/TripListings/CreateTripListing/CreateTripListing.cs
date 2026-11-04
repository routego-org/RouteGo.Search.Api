namespace RouteGo.Search.Api.Features.TripListings.CreateTripListing;

/// <summary>
/// Creates Search's trip listing — details, stops along the way and every seat, Available — from
/// Fleet's TripScheduled. A listing's details are set once: a repeat of the event, or another
/// TripScheduled for a trip already known, changes nothing. A listing an early TripCancelled
/// created gets its details and stays cancelled.
/// </summary>
public sealed class CreateTripListingHandler
{
    /// <summary>The producer's event type that starts this slice.</summary>
    public const string TriggerEventType = "TripScheduled";

    private const string WriteAction = "CreateTripListing";
    private const long PaisePerRupee = 100;

    private static readonly JsonSerializerOptions _eventJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    // The row may already exist from an early TripCancelled; locking it serialises two deliveries
    // for the same trip, so only the first fills the details.
    private const string EnsureListingSql = """
        INSERT INTO trip_listings.trip_listings (trip_id, is_cancelled)
        VALUES (@TripId, false)
        ON CONFLICT (trip_id) DO NOTHING;
        """;

    private const string LockListingSql = """
        SELECT details_received_at FROM trip_listings.trip_listings WHERE trip_id = @TripId FOR UPDATE;
        """;

    private const string FillDetailsSql = """
        UPDATE trip_listings.trip_listings
        SET operator_name = @OperatorName, operator_rating = @OperatorRating, bus_id = @BusId,
            bus_name = @BusName, bus_type = @BusType, is_ac = @IsAc, amenities = @Amenities,
            from_city_id = @FromCityId, from_city_name = @FromCityName,
            to_city_id = @ToCityId, to_city_name = @ToCityName, service_date = @ServiceDate::date,
            departure_at = @DepartureAt, arrival_at = @ArrivalAt, details_received_at = now()
        WHERE trip_id = @TripId;
        """;

    private const string InsertStopsSql = """
        INSERT INTO trip_listings.listing_stops (trip_id, sequence, city_id, city_name, arrival_at)
        SELECT @TripId, s.sequence, s.city_id, s.city_name, s.arrival_at
        FROM unnest(@Sequences, @CityIds, @CityNames, @ArrivalAts) AS s(sequence, city_id, city_name, arrival_at);
        """;

    private const string InsertSeatsSql = """
        INSERT INTO trip_listings.listing_seats (trip_id, seat_no, seat_class, fare)
        SELECT @TripId, s.seat_no, s.seat_class, s.fare
        FROM unnest(@SeatNos, @SeatClasses, @Fares) AS s(seat_no, seat_class, fare);
        """;

    private readonly IWriterConnectionFactory _writerFactory;
    private readonly ILogger<CreateTripListingHandler> _logger;

    public CreateTripListingHandler(IWriterConnectionFactory writerFactory, ILogger<CreateTripListingHandler> logger)
    {
        _writerFactory = writerFactory;
        _logger = logger;
    }

    /// <summary>Applies one TripScheduled event and says what the queue should do with its message.</summary>
    public async Task<EventHandlingOutcome> HandleAsync(ReceivedEvent receivedEvent, CancellationToken cancellationToken)
    {
        TripScheduledData? data = ReadData(receivedEvent.Data);
        string? problem = DescribeProblem(data);
        if (problem != null)
        {
            EventAlerts.LogNotApplied(
                _logger, ProblemTypes.FleetEventNotApplied, receivedEvent, NotAppliedReason.InvalidEventData, problem);
            return EventHandlingOutcome.SetAside;
        }

        bool isCreated = await CreateOnceAsync(data!, cancellationToken);
        if (!isCreated)
        {
            _logger.LogInformation(
                "Trip listing {TripId} already has its details; TripScheduled event {EventId} (correlation {CorrelationId}) changes nothing",
                data!.TripId, receivedEvent.EventId, receivedEvent.CorrelationId);
            return EventHandlingOutcome.Consumed;
        }

        _logger.LogInformation(
            "Trip listing {TripId} created from TripScheduled event {EventId} (correlation {CorrelationId}) with {SeatCount} seats",
            data!.TripId, receivedEvent.EventId, receivedEvent.CorrelationId, data.Seats!.Count);
        return EventHandlingOutcome.Consumed;
    }

    // Data that is not even the right JSON shape (text where a number belongs) is as unusable as
    // data with a field missing.
    private static TripScheduledData? ReadData(JsonElement data)
    {
        try
        {
            return data.Deserialize<TripScheduledData>(_eventJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? DescribeProblem(TripScheduledData? data)
    {
        if (data is null)
        {
            return "data does not have the TripScheduled shape";
        }

        return data.DescribeProblem();
    }

    // The listing, its stops and all its seats are written together, or none.
    private async Task<bool> CreateOnceAsync(TripScheduledData data, CancellationToken cancellationToken)
    {
        await using IWriterConnection writer =
            await _writerFactory.OpenWriteTransactionAsync(WriteAction, cancellationToken);
        await ExecuteAsync(writer, EnsureListingSql, new { data.TripId }, cancellationToken);
        DateTime? detailsReceivedAt = await writer.Connection.QuerySingleAsync<DateTime?>(new CommandDefinition(
            LockListingSql, new { data.TripId }, writer.Transaction, cancellationToken: cancellationToken));
        if (detailsReceivedAt != null)
        {
            await writer.CommitAsync(cancellationToken);
            return false;
        }

        await ExecuteAsync(writer, FillDetailsSql, DetailsParameters(data), cancellationToken);
        await ExecuteAsync(writer, InsertStopsSql, StopsParameters(data), cancellationToken);
        await ExecuteAsync(writer, InsertSeatsSql, SeatsParameters(data), cancellationToken);
        await writer.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task ExecuteAsync(
        IWriterConnection writer, string sql, object parameters, CancellationToken cancellationToken)
    {
        await writer.Connection.ExecuteAsync(new CommandDefinition(
            sql, parameters, writer.Transaction, cancellationToken: cancellationToken));
    }

    private static object DetailsParameters(TripScheduledData data)
    {
        return new
        {
            data.TripId,
            data.OperatorName,
            OperatorRating = data.Rating,
            data.BusId,
            data.BusName,
            data.BusType,
            data.IsAc,
            Amenities = data.Amenities!.ToArray(),
            FromCityId = data.Source!.CityId,
            FromCityName = data.Source.Name,
            ToCityId = data.Destination!.CityId,
            ToCityName = data.Destination.Name,
            ServiceDate = data.ServiceDate!.Value.ToDateTime(TimeOnly.MinValue),
            DepartureAt = data.DepartureUtc!.Value.UtcDateTime,
            ArrivalAt = data.ArrivalUtc!.Value.UtcDateTime,
        };
    }

    private static object StopsParameters(TripScheduledData data)
    {
        List<StopData> stops = data.StopsAlongTheWay();
        int[] sequences = new int[stops.Count];
        var cityIds = new Guid[stops.Count];
        string[] cityNames = new string[stops.Count];
        var arrivalAts = new DateTime[stops.Count];
        for (int index = 0; index < stops.Count; index++)
        {
            sequences[index] = stops[index].Sequence!.Value;
            cityIds[index] = stops[index].CityId!.Value;
            cityNames[index] = stops[index].CityName!;
            arrivalAts[index] = stops[index].ArrivalUtc!.Value.UtcDateTime;
        }

        return new { data.TripId, Sequences = sequences, CityIds = cityIds, CityNames = cityNames, ArrivalAts = arrivalAts };
    }

    // Fares arrive in whole rupees and are stored in paise.
    private static object SeatsParameters(TripScheduledData data)
    {
        List<SeatData?> seats = data.Seats!;
        string[] seatNos = new string[seats.Count];
        string[] seatClasses = new string[seats.Count];
        long[] fares = new long[seats.Count];
        for (int index = 0; index < seats.Count; index++)
        {
            seatNos[index] = seats[index]!.SeatNo!;
            seatClasses[index] = seats[index]!.Class!;
            fares[index] = seats[index]!.Fare!.Value * PaisePerRupee;
        }

        return new { data.TripId, SeatNos = seatNos, SeatClasses = seatClasses, Fares = fares };
    }
}
