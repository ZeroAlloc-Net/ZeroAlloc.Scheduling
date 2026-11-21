using System.Data.Async;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.Scheduling.Orm.Tests;

/// <summary>
/// A database for <see cref="RecurringJobContract"/>, dropped on dispose. It keeps every connection
/// it hands to a store, since <see cref="OrmJobStore"/> does not dispose its connection, and closes
/// them on dispose, also when the test failed.
/// </summary>
public abstract class ContractDatabase : IAsyncDisposable
{
    private readonly List<IAsyncDbConnection> _storeConnections = [];

    /// <summary>The dialect the stores use.</summary>
    protected abstract OrmSchedulingDialect Dialect { get; }

    /// <summary>A new open connection, which the caller disposes.</summary>
    public abstract Task<IAsyncDbConnection> ConnectAsync();

    /// <summary>A store on a new connection, closed when this database is disposed.</summary>
    public async Task<OrmJobStore> StoreAsync()
    {
        var connection = await ConnectAsync().ConfigureAwait(false);
        lock (_storeConnections)
        {
            _storeConnections.Add(connection);
        }

        return new OrmJobStore(connection, Dialect);
    }

    /// <summary>Counts the job rows that match the SQL condition.</summary>
    public abstract Task<long> CountAsync(string condition);

    /// <summary>Applies the source's outstanding migrations and returns them.</summary>
    public abstract Task<IReadOnlyList<Migration>> MigrateAsync(IMigrationSource source);

    /// <summary>Drops the database, after the store connections are closed.</summary>
    protected abstract ValueTask DropAsync();

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _storeConnections)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        await DropAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
