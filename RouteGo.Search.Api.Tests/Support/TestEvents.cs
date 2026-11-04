using System.Text.Json;

namespace RouteGo.Search.Api.Tests.Support;

/// <summary>
/// Events as Fleet's and Seat Inventory's events documents spell them, for the trip-listing worker
/// tests. Data is built as a JSON object so a criterion can remove or change one field.
/// </summary>
public static class TestEvents
{
    public static readonly Guid Mumbai = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid Pune = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public static readonly Guid Lonavala = Guid.Parse("00000000-0000-0000-0000-000000000003");
    public static readonly Guid BusB11 = Guid.Parse("00000000-0000-0000-0000-000000000b11");

    /// <summary>When the criteria's events happened unless one says otherwise.</summary>
    public static readonly DateTimeOffset OccurredAt = new DateTimeOffset(2026, 10, 20, 4, 30, 0, TimeSpan.Zero);

    /// <summary>The envelope around <paramref name="data"/>, as the queue listener hands it to a slice.</summary>
    public static ReceivedEvent Envelope(
        string eventType, string eventId, JsonObject data, DateTimeOffset occurredAt, bool isLastAttempt = false)
    {
        JsonElement element = JsonSerializer.SerializeToElement(data);
        return new ReceivedEvent
        {
            EventId = eventId,
            EventType = eventType,
            OccurredAt = occurredAt,
            CorrelationId = "corr-" + eventId,
            Data = element,
            IsLastAttempt = isLastAttempt,
        };
    }

    /// <summary>
    /// T-201: BusCo Travels (4.2), "BusCo Volvo 9600", Sleeper, AC, Wifi and charging point; Mumbai via
    /// Lonavala to Pune on 2026-10-25, 16:30Z to 19:30Z; U1–U20 "Upper berth" at 900 and L1–L16
    /// "Lower berth" at 1200. Fields Search ignores are included, as Fleet sends them.
    /// </summary>
    public static JsonObject TripScheduled(Guid tripId)
    {
        var departure = new DateTime(2026, 10, 25, 16, 30, 0, DateTimeKind.Utc);
        return new JsonObject
        {
            ["tripId"] = tripId,
            ["scheduleId"] = Guid.NewGuid(),
            ["operatorId"] = "op-1",
            ["operatorName"] = "BusCo Travels",
            ["operatorEmail"] = "owner@busco.in",
            ["rating"] = 4.2m,
            ["busId"] = BusB11,
            ["busName"] = "BusCo Volvo 9600",
            ["busType"] = "Sleeper",
            ["isAc"] = true,
            ["amenities"] = new JsonArray("Wifi", "ChargingPoint"),
            ["source"] = City(Mumbai, "Mumbai"),
            ["destination"] = City(Pune, "Pune"),
            ["stops"] = new JsonArray(
                Stop(1, Mumbai, "Mumbai", departure),
                Stop(2, Lonavala, "Lonavala", departure.AddMinutes(90)),
                Stop(3, Pune, "Pune", departure.AddHours(3))),
            ["serviceDate"] = "2026-10-25",
            ["departureUtc"] = departure,
            ["arrivalUtc"] = departure.AddHours(3),
            ["seats"] = Seats(),
            ["totalSeats"] = 36,
        };
    }

    /// <summary>A FareChanged setting each named seat to <paramref name="fare"/>.</summary>
    public static JsonObject FareChanged(Guid tripId, string seatClass, int fare, params string[] seatNos)
    {
        var seats = new JsonArray();
        foreach (string seatNo in seatNos)
        {
            seats.Add(new JsonObject { ["seatNo"] = seatNo, ["class"] = seatClass, ["fare"] = fare });
        }

        return new JsonObject
        {
            ["tripId"] = tripId,
            ["classFares"] = new JsonArray(new JsonObject { ["class"] = seatClass, ["fare"] = fare }),
            ["seats"] = seats,
        };
    }

    /// <summary>A TripCancelled with its reason.</summary>
    public static JsonObject TripCancelled(Guid tripId, string reason)
    {
        return new JsonObject { ["tripId"] = tripId, ["reason"] = reason };
    }

    /// <summary>A SeatHeld, SeatBooked, SeatBlocked or SeatUnblocked naming <paramref name="seatNos"/>.</summary>
    public static JsonObject SeatEvent(Guid tripId, params string[] seatNos)
    {
        return new JsonObject
        {
            ["tripId"] = tripId,
            ["bookingId"] = Guid.NewGuid(),
            ["seatNos"] = Labels(seatNos),
            ["operatorId"] = "op-1",
        };
    }

    /// <summary>A SeatReleased naming <paramref name="seatNos"/>, released to <paramref name="releasedTo"/>.</summary>
    public static JsonObject SeatReleased(Guid tripId, string releasedTo, params string[] seatNos)
    {
        JsonObject data = SeatEvent(tripId, seatNos);
        data["reason"] = "HoldExpired";
        data["releasedTo"] = releasedTo;
        return data;
    }

    /// <summary>A SeatChanged moving one seat to another.</summary>
    public static JsonObject SeatChanged(Guid tripId, string fromSeatNo, string toSeatNo)
    {
        return new JsonObject
        {
            ["tripId"] = tripId,
            ["bookingId"] = Guid.NewGuid(),
            ["changes"] = new JsonArray(new JsonObject { ["fromSeatNo"] = fromSeatNo, ["toSeatNo"] = toSeatNo }),
        };
    }

    /// <summary>The labels <paramref name="prefix"/>1 to <paramref name="prefix"/><paramref name="count"/>.</summary>
    public static string[] SeatLabels(string prefix, int count)
    {
        string[] labels = new string[count];
        for (int number = 1; number <= count; number++)
        {
            labels[number - 1] = prefix + number;
        }

        return labels;
    }

    private static JsonObject City(Guid cityId, string name)
    {
        return new JsonObject { ["cityId"] = cityId, ["name"] = name };
    }

    private static JsonObject Stop(int sequence, Guid cityId, string cityName, DateTime arrivalUtc)
    {
        return new JsonObject
        {
            ["sequence"] = sequence,
            ["cityId"] = cityId,
            ["cityName"] = cityName,
            ["arrivalUtc"] = arrivalUtc,
        };
    }

    private static JsonArray Seats()
    {
        var seats = new JsonArray();
        foreach (string seatNo in SeatLabels("U", 20))
        {
            seats.Add(Seat(seatNo, "Upper", "Upper berth", 900));
        }

        foreach (string seatNo in SeatLabels("L", 16))
        {
            seats.Add(Seat(seatNo, "Lower", "Lower berth", 1200));
        }

        return seats;
    }

    private static JsonObject Seat(string seatNo, string deck, string seatClass, int fare)
    {
        return new JsonObject
        {
            ["seatNo"] = seatNo,
            ["deck"] = deck,
            ["row"] = 1,
            ["col"] = 1,
            ["class"] = seatClass,
            ["fare"] = fare,
        };
    }

    private static JsonArray Labels(string[] seatNos)
    {
        var labels = new JsonArray();
        foreach (string seatNo in seatNos)
        {
            labels.Add(seatNo);
        }

        return labels;
    }
}
