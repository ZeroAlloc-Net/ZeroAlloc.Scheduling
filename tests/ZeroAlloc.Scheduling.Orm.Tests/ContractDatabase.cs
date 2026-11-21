using System.Data.Async;
using ZeroAlloc.ORM.Migrations;

namespace ZeroAlloc.Scheduling.Orm.Tests;

/// <summary>A database for <see cref="RecurringJobContract"/>, dropped on dispose.</summary>
public abstract class ContractDatabase : IAsyncDisposable
{
    /// <summary>A new open connection.</summary>
    public abstract Task<IAsyncDbConnection> ConnectAsync();

    /// <summary>A store on a new connection.</summary>
    public abstract Task<OrmJobStore> StoreAsync();

    /// <summary>Counts the job rows that match the SQL condition.</summary>
    public abstract Task<long> CountAsync(string condition);

    /// <summary>Applies the source's outstanding migrations and returns them.</summary>
    public abstract Task<IReadOnlyList<Migration>> MigrateAsync(IMigrationSource source);

    public abstract ValueTask DisposeAsync();
}
