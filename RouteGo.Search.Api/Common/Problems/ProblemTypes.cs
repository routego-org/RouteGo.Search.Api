namespace RouteGo.Search.Api.Common.Problems;

/// <summary>
/// Projection of the product's problem-type catalog (contracts/problem-types.md in the
/// product-specs repo). Codes are never minted here — a new code starts life in the catalog
/// and lands here as a constant carrying the same code and the same type URI, so this class
/// can be diffed against the catalog rather than trusted to match it.
/// <para>
/// Seeded with every baseline code this codebase itself emits. The baseline rows that are
/// missing are emitted by middleware this project does not own — 401/403 by the authentication
/// handler, 429 by the rate limiter — and 400 validation problems are identified by their
/// <c>errors</c> member rather than a code, by contract. Append one constant per product code
/// as capabilities emit them (references/host-components.md).
/// </para>
/// </summary>
public static class ProblemTypes
{
    private const string ProblemsBase = "urn:routego:search:problems";

    /// <summary>404 — the resource does not exist.</summary>
    public static readonly ProblemType NotFound = new("NOT_FOUND", $"{ProblemsBase}/not-found");

    /// <summary>409 — state changed since read, or the request duplicates existing state.</summary>
    public static readonly ProblemType Conflict = new("CONFLICT", $"{ProblemsBase}/conflict");

    /// <summary>422 — a business rule refused the request and no specific code exists yet.</summary>
    public static readonly ProblemType BusinessRuleViolated =
        new("BUSINESS_RULE_VIOLATED", $"{ProblemsBase}/business-rule-violated");

    /// <summary>502 — a required dependency is down or returned an unusable response.</summary>
    public static readonly ProblemType UpstreamUnavailable =
        new("UPSTREAM_UNAVAILABLE", $"{ProblemsBase}/upstream-unavailable");

    /// <summary>504 — a required dependency did not respond in time and no data could be served.</summary>
    public static readonly ProblemType UpstreamTimeout =
        new("UPSTREAM_TIMEOUT", $"{ProblemsBase}/upstream-timeout");

    /// <summary>500 — unhandled server fault. Emitted by UnhandledExceptionHandler, never by a slice.</summary>
    public static readonly ProblemType InternalError =
        new("INTERNAL_ERROR", $"{ProblemsBase}/internal-error");

    /// <summary>
    /// 400 — request parameters failed validation. This product's catalog gives the 400 a code
    /// like every other error, and joins its type URI to the base with ':' as the catalog does.
    /// </summary>
    public static readonly ProblemType ValidationFailed =
        new("VALIDATION_FAILED", $"{ProblemsBase}:validation-failed");

    /// <summary>
    /// Worker code — a Fleet event (TripScheduled, FareChanged, TripCancelled) could not be applied.
    /// It reaches operators as an error log line, never an HTTP client.
    /// </summary>
    public static readonly ProblemType FleetEventNotApplied =
        new("FLEET_EVENT_NOT_APPLIED", $"{ProblemsBase}:fleet-event-not-applied");

    /// <summary>
    /// Worker code — a seat event (SeatHeld, SeatBooked, SeatReleased, SeatBlocked, SeatUnblocked,
    /// SeatChanged) could not be applied. It reaches operators as an error log line.
    /// </summary>
    public static readonly ProblemType SeatEventNotApplied =
        new("SEAT_EVENT_NOT_APPLIED", $"{ProblemsBase}:seat-event-not-applied");
}
