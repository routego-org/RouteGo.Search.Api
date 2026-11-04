namespace RouteGo.Search.Api.Tests.Support;

/// <summary>A clock that always reads the same moment.</summary>
public sealed class FixedClock : IClock
{
    private readonly DateTimeOffset _utcNow;

    public FixedClock(DateTimeOffset now)
    {
        _utcNow = now.ToUniversalTime();
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow
    {
        get
        {
            return _utcNow;
        }
    }
}
