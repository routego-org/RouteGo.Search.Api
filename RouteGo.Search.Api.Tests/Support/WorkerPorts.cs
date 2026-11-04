using RouteGo.Search.Api.Infrastructure.Pgsql;

namespace RouteGo.Search.Api.Tests.Support;

/// <summary>The connection ports the trip-listing workers run on, over the test database.</summary>
public static class WorkerPorts
{
    /// <summary>The writer, stamped as the system actor as the deployed workers are.</summary>
    public static IWriterConnectionFactory Writer(DatabaseFixture database)
    {
        return new WriterConnectionFactory(database.ConnectionString, SystemActor.Default, ConnectionRetryOptions.ManagedInstances);
    }

    /// <summary>A writer whose database cannot be reached, for temporary-failure criteria.</summary>
    public static IWriterConnectionFactory UnreachableWriter()
    {
        const string unreachable = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2";
        return new WriterConnectionFactory(unreachable, SystemActor.Default, ConnectionRetryOptions.ManagedInstances);
    }

    /// <summary>The reader the search operations use.</summary>
    public static IReaderConnectionFactory Reader(DatabaseFixture database)
    {
        return new ReaderConnectionFactory(database.ConnectionString);
    }
}
