namespace RouteGo.Search.Api.Infrastructure;

/// <summary>
/// The name of every environment-supplied configuration value, spelled exactly once. The names are
/// deliberately FLAT: each maps 1:1 to an environment variable of the same spelling, so no ':' or
/// '__' separator can be mistyped between the deployment template, the pipeline, and the reader
/// here. LocalEntryPoints and every consumer go through these constants, so a rename is a compile
/// error rather than a value that silently arrives null (references/configuration.md).
/// </summary>
public static class ConfigKeys
{
    /// <summary>The AWS SDK's own region variable, and the ONLY region source the app reads.
    /// Lambda sets it; LocalEntryPoints seeds it on a laptop. There is deliberately no app-owned
    /// AWS:Region key that could disagree with the runtime's own value.</summary>
    public const string AwsRegion = "AWS_REGION";

    // ══════════════════════════════════════════════════════════════════════════
    // FILLED BY the sibling slice skill and by feature work: one const per value
    // that varies by environment — a base URL, a client id, a public origin.
    //
    //   public const string BackendBaseUrl = "BackendBaseUrl";
    //
    // Adding one is four more edits, none of them a new file: a local value in
    // LocalEntryPoints, a parameter + env var in the deployment template, a
    // pipeline variable, and a RequireKeys entry if the app cannot start without
    // it. NEVER add a configuration key to an appsettings*.json.
    //
    // Otel:Exporter:* and AWS:Profile do NOT belong here — see
    // references/configuration.md for why those two keep their nested names.
    // ══════════════════════════════════════════════════════════════════════════
    /// <summary>Which Lambda execution model this process runs on — Lambda (one invocation per process,
    /// frozen between them) or ManagedInstances (many concurrent invocations in one always-on process).
    /// It picks the database pool profile (Infrastructure/Pgsql/ConnectionProfile). Set by the deployment
    /// template, never sniffed from the runtime. Required.</summary>
    public const string DbConnectionProfile = "DB_CONNECTION_PROFILE";

    /// <summary>The most database connections one process may hold: 2-4 on Lambda (a handler's opens are
    /// sequential), at least the per-environment concurrency on ManagedInstances. Set by the deployment
    /// template beside DbConnectionProfile. Required.</summary>
    public const string DbMaxPoolSize = "DB_MAX_POOL_SIZE";

    /// <summary>Minutes before departure at which a trip stops being searchable and stops counting for
    /// city suggestions (30 by product default). Set by the deployment template, so the product can
    /// change it without a release. Required.</summary>
    public const string SearchCutoffMinutes = "SEARCH_CUTOFF_MINUTES";

    /// <summary>Trips per page of a trip search (20 by product default). Set by the deployment
    /// template, so the product can change it without a release. Required.</summary>
    public const string PageSize = "PAGE_SIZE";

    /// <summary>The SQS queue the trip-listing workers read: subscribed to Fleet's and Seat Inventory's
    /// event topics. Set by the deployment template. Required.</summary>
    public const string SearchQueueUrl = "SEARCH_QUEUE_URL";

    /// <summary>The search queue's dead-letter queue, where an event with unusable data is set aside at
    /// once for the platform engineer. Set by the deployment template. Required.</summary>
    public const string SearchDeadLetterQueueUrl = "SEARCH_DEAD_LETTER_QUEUE_URL";

    /// <summary>An AWS-compatible endpoint to use instead of AWS — set only on a laptop, where LocalStack
    /// stands in for SQS. The SDK reads this variable itself.</summary>
    public const string AwsEndpointUrl = "AWS_ENDPOINT_URL";
}
