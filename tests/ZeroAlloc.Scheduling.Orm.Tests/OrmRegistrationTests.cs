using System.Data.Async;
using System.Data.Async.Adapters;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Scheduling.Orm.Tests;

/// <summary>
/// Which <see cref="IAsyncDbConnection"/> each <c>WithOrm</c> overload hands to the
/// <see cref="OrmJobStore"/> it registers.
/// </summary>
public sealed class OrmRegistrationTests
{
    private const string JobsKey = "jobs-db";
    private static readonly byte[] s_payload = [1, 2, 3];

    [Fact]
    public async Task Keyed_Overload_Uses_The_Keyed_Connection_And_Not_The_Unkeyed_One()
    {
        await using var jobsDb = new SqliteFixture();
        await jobsDb.MigrateAsync();
        var unkeyedResolved = false;
        var keyedResolved = 0;

        var services = new ServiceCollection();
        services.AddScoped<IAsyncDbConnection>(_ =>
        {
            unkeyedResolved = true;
            throw new InvalidOperationException("The job store must not use the unkeyed connection.");
        });
        services.AddKeyedScoped<IAsyncDbConnection>(JobsKey, (_, _) =>
        {
            keyedResolved++;
            return jobsDb.ConnectAsync().GetAwaiter().GetResult();
        });
        services.AddScheduling().WithOrm(OrmSchedulingDialect.Sqlite, JobsKey);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();

        await store.EnqueueAsync("Job.A", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);
        var claimed = await store.FetchPendingAsync(10, default);

        store.Should().BeOfType<OrmJobStore>();
        claimed.Should().ContainSingle().Which.TypeName.Should().Be("Job.A");
        keyedResolved.Should().Be(1);
        unkeyedResolved.Should().BeFalse();
    }

    [Fact]
    public async Task Keyed_Overload_Does_Not_Fall_Back_To_The_Unkeyed_Connection()
    {
        var services = new ServiceCollection();
        services.AddScoped<IAsyncDbConnection>(_ => UnopenedConnection());
        services.AddScheduling().WithOrm(OrmSchedulingDialect.Postgres, JobsKey);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var resolve = () => scope.ServiceProvider.GetRequiredService<IJobStore>();

        resolve.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Unkeyed_Overloads_Use_The_Unkeyed_Connection_And_Not_A_Keyed_One()
    {
        await using var jobsDb = new SqliteFixture();
        await jobsDb.MigrateAsync();
        var keyedResolved = false;

        var services = new ServiceCollection();
        services.AddScoped(_ => jobsDb.ConnectAsync().GetAwaiter().GetResult());
        services.AddKeyedScoped<IAsyncDbConnection>(JobsKey, (_, _) =>
        {
            keyedResolved = true;
            throw new InvalidOperationException("The unkeyed overload must not use a keyed connection.");
        });
        services.AddScheduling().WithOrm();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();

        await store.EnqueueAsync("Job.B", s_payload, DateTimeOffset.UtcNow.AddSeconds(-1), 3, null, default);
        var claimed = await store.FetchPendingAsync(10, default);

        claimed.Should().ContainSingle().Which.TypeName.Should().Be("Job.B");
        keyedResolved.Should().BeFalse();
    }

    [Fact]
    public async Task Keyed_Overload_Keeps_A_Job_Store_Registered_Earlier()
    {
        var own = new OrmJobStore(UnopenedConnection());
        var services = new ServiceCollection();
        services.AddScoped<IJobStore>(_ => own);
        services.AddScheduling().WithOrm(OrmSchedulingDialect.Postgres, JobsKey);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IJobStore>().Should().BeSameAs(own);
        services.Count(d => d.ServiceType == typeof(IJobStore)).Should().Be(1);
    }

    [Fact]
    public void Keyed_Overload_Rejects_A_Null_Key()
    {
        var builder = new ServiceCollection().AddScheduling();

        var act = () => builder.WithOrm(OrmSchedulingDialect.Postgres, connectionKey: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionKey");
    }

    [Fact]
    public void Keyed_Overload_Rejects_A_Null_Builder()
    {
        var act = () => OrmSchedulingServiceCollectionExtensions.WithOrm(
            null!, OrmSchedulingDialect.Postgres, JobsKey);

        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    /// <summary>A connection that is never opened; the test only needs an instance.</summary>
    private static IAsyncDbConnection UnopenedConnection()
        => new SqliteConnection("Data Source=:memory:").AsAsync();
}
