namespace RouteGo.Search.Api.Infrastructure.Messaging;

public sealed class EventEnvelopeReaderTests
{
    private const string CompleteEnvelope = """
        {"eventId":"7d1f0a52-3c4b-4e8a-9f2d-1b6c5e8a9d01","eventType":"TripScheduled","version":1,
         "occurredAt":"2026-10-02T10:15:30Z","correlationId":"c-1","producer":"fleet-service",
         "data":{"tripId":"00000000-0000-0000-0000-000000000201"}}
        """;

    [Fact]
    public void Read_CompleteEnvelope_ReturnsEvent()
    {
        EventEnvelopeReading reading = EventEnvelopeReader.Read(CompleteEnvelope, isLastAttempt: true);

        Assert.NotNull(reading.Event);
        Assert.Equal("7d1f0a52-3c4b-4e8a-9f2d-1b6c5e8a9d01", reading.Event.EventId);
        Assert.Equal("TripScheduled", reading.Event.EventType);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 10, 15, 30, TimeSpan.Zero), reading.Event.OccurredAt);
        Assert.Equal("c-1", reading.Event.CorrelationId);
        Assert.True(reading.Event.IsLastAttempt);
    }

    [Fact]
    public void Read_BodyNotJson_ReportsProblem()
    {
        EventEnvelopeReading reading = EventEnvelopeReader.Read("not json", isLastAttempt: false);

        Assert.Null(reading.Event);
        Assert.Equal("the body is not JSON", reading.Problem);
    }

    [Fact]
    public void Read_NoEventId_ReportsMissingEventId()
    {
        string body = """{"eventType":"TripScheduled","occurredAt":"2026-10-02T10:15:30Z","correlationId":"c-1","data":{}}""";

        EventEnvelopeReading reading = EventEnvelopeReader.Read(body, isLastAttempt: false);

        Assert.Null(reading.Event);
        Assert.Equal("the envelope has no usable eventId", reading.Problem);
    }

    [Fact]
    public void Read_OccurredAtNotATime_ReportsMissingOccurredAt()
    {
        string body = """{"eventId":"e","eventType":"TripScheduled","occurredAt":"yesterday","correlationId":"c-1","data":{}}""";

        EventEnvelopeReading reading = EventEnvelopeReader.Read(body, isLastAttempt: false);

        Assert.Null(reading.Event);
        Assert.Equal("the envelope has no usable occurredAt", reading.Problem);
    }
}
