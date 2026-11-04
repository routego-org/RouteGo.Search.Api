using System.Globalization;

namespace RouteGo.Search.Api.Infrastructure.Messaging;

/// <summary>
/// Reads the RouteGo event envelope from a raw SQS message body:
/// <c>{ eventId, eventType, version, occurredAt, correlationId, producer, data }</c>.
/// A message without the fields a send log entry needs (event ID, event type, time it occurred,
/// correlation ID and a data object) is unusable — no entry can record it — so it is reported,
/// not guessed at.
/// </summary>
public static class EventEnvelopeReader
{
    /// <summary>Reads <paramref name="body"/> into an event, or names why it cannot be read.</summary>
    public static EventEnvelopeReading Read(string body, bool isLastAttempt)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(body);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new EventEnvelopeReading(null, "the body is not JSON");
        }

        string? missing = DescribeMissingField(root);
        if (missing != null)
        {
            return new EventEnvelopeReading(null, $"the envelope has no usable {missing}");
        }

        var receivedEvent = new ReceivedEvent
        {
            EventId = root.GetProperty("eventId").GetString()!,
            EventType = root.GetProperty("eventType").GetString()!,
            OccurredAt = ReadTime(root.GetProperty("occurredAt"))!.Value,
            CorrelationId = root.GetProperty("correlationId").GetString()!,
            Data = root.GetProperty("data"),
            IsLastAttempt = isLastAttempt,
        };
        return new EventEnvelopeReading(receivedEvent, null);
    }

    private static string? DescribeMissingField(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return "object";
        }

        string[] textFields = new[] { "eventId", "eventType", "correlationId" };
        foreach (string field in textFields)
        {
            if (!HasText(root, field))
            {
                return field;
            }
        }

        if (!root.TryGetProperty("occurredAt", out JsonElement occurredAt) || ReadTime(occurredAt) is null)
        {
            return "occurredAt";
        }

        if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object)
        {
            return "data";
        }

        return null;
    }

    private static bool HasText(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(value.GetString());
    }

    private static DateTimeOffset? ReadTime(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        bool isTime = DateTimeOffset.TryParse(
            value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset time);
        if (!isTime)
        {
            return null;
        }

        return time.ToUniversalTime();
    }
}
